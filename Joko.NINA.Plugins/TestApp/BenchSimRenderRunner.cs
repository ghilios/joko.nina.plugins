#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp {

    /// <summary>
    /// Headless benchmark for the synthetic camera's render pipeline, on a real ASTAP star field.
    ///
    /// <para><b>Why per-phase instrumentation instead of differencing two renders.</b>
    /// <c>NoiseGenerator.DevelopRange</c> branches at <c>PoissonToGaussianThreshold = 40</c>: below it a
    /// Poisson draw costs ~λ uniform samples, at or above it a fixed four. Development cost therefore swings
    /// by ~10× with λ, and <b>stars change λ</b>. A "dense frame minus starless frame" difference silently
    /// books that development delta as PSF cost — exactly the quantity this harness exists to isolate. So the
    /// compositor reports its own phases through the internal <see cref="RenderPhaseTimings"/> sink, and this
    /// runner reads them.</para>
    ///
    /// <para>Development is 50–90 % of a 61 MP render depending on the field, so the total wall clock is an
    /// insensitive instrument for anything the PSF does. Read <c>kernelGen</c> and <c>kernels</c>, not just
    /// <c>total</c>.</para>
    ///
    /// Usage:
    /// <code>
    /// TestApp bench-simrender [--catalog "C:\Program Files\astap"] [--field dense-wide,dense,sparse|all]
    ///                         [--defocus-steps 0,150,350] [--aberr A0,A1,A2] [--arms off,on-zero,on,on-strong]
    ///                         [--corner-astig 15] [--corner-astig-strong 40] [--limit-mag 17] [--exposure 5]
    ///                         [--iters 3] [--warmup 1] [--census] [--kernel-ladder] [--with-detection]
    ///                         [--csv out.csv]
    /// </code>
    /// </summary>
    public static class BenchSimRenderRunner {

        /// <summary>Default ASTAP database folder — a Windows path, since TestApp runs as a Windows binary.</summary>
        private const string DefaultCatalogPath = @"C:\Program Files\astap";

        private const double DefaultExposureSeconds = 5.0;
        private const int DefaultGain = 100;
        private const int DefaultOptimalFocuserPosition = 10000;
        private const double DefaultFocuserStepSizeMicrons = 3.336356;
        private const double DefaultSeeingArcsec = 2.5;
        private const double DefaultSkyBrightness = 20.5;
        private const double DefaultOpticalThroughput = 0.85;
        private const double DefaultSensorTemperatureCelsius = -10.0;
        private const int DefaultNoiseSeed = 42;

        /// <summary>The shipped corner-astigmatism residual (µm) -- what a user who enables the feature gets.</summary>
        private const double DefaultCornerAstigmatismMicrons = 15.0;

        /// <summary>An aggressive residual, for headroom.</summary>
        private const double StrongCornerAstigmatismMicrons = 40.0;

        /// <summary>A named pointing plus the optics to observe it with. Sensor is fixed to the QHY600/IMX455.</summary>
        private sealed record BenchField(
            string Name, double RaDegrees, double DecDegrees, double FocalLengthMm, double FocalRatio, string Note);

        /// <summary>A named aberration triple. Deliberately fixed values so the result table stays readable.</summary>
        private sealed record AberrationConfig(
            string Name, double TiltAmountMicrons, double TiltAngleDegrees, double BackfocusErrorMicrons, string Note);

        private static readonly IReadOnlyDictionary<string, BenchField> Fields = new Dictionary<string, BenchField>(StringComparer.OrdinalIgnoreCase) {
            // Gamma Cygni (Sadr), galactic l ~ 78 deg / b ~ +2 deg -- the Cygnus star cloud, and the same pointing
            // and optics as the checked-in D19_cygnus_deep_shed bank dataset, so its density is already
            // characterized. Diagonal FOV ~2.5 deg, comfortably inside the .290 single-cell cap.
            ["dense"] = new("dense", 305.5583, 40.2567, 1000.0, 7.1, "gamma Cygni star cloud"),
            // Same sky through a widefield astrograph (an FSQ-106-class 530 mm f/5, a classic QHY600 pairing).
            // At 1000 mm the frame covers only 2.8 sq deg, and the installed G18 database bottoms out at mag 18,
            // so the 1000 mm field tops out around 14k on-frame stars however faint the cut goes. The 3.6x wider
            // field is how a genuinely dense 61 MP frame is reached with this catalog.
            ["dense-wide"] = new("dense-wide", 305.5583, 40.2567, 530.0, 5.0, "gamma Cygni, widefield"),
            // The North Galactic Pole -- the emptiest sky reachable, at identical optics so ONLY the star count
            // differs. Phases that do not move between dense and sparse are field-independent; those that move
            // linearly are per-star.
            ["sparse"] = new("sparse", 192.8595, 27.1283, 1000.0, 7.1, "North Galactic Pole"),
            // Same sky, finer plate scale: sigma_min roughly doubles, so kernel radius doubles and kernel cost
            // and cache bytes both go as R^2. This is where a memory budget bites first.
            ["dense-oversampled"] = new("dense-oversampled", 305.5583, 40.2567, 2000.0, 8.0, "gamma Cygni, oversampled"),
        };

        private static readonly IReadOnlyDictionary<string, AberrationConfig> AberrationConfigs = new Dictionary<string, AberrationConfig>(StringComparer.OrdinalIgnoreCase) {
            ["A0"] = new("A0", 0.0, 0.0, 0.0, "clean"),
            ["A1"] = new("A1", 0.0, 0.0, 60.0, "backfocus only"),
            ["A2"] = new("A2", 40.0, 30.0, 60.0, "tilt + backfocus"),
        };

        public static Task Run(string[] args) {
            var catalogPath = DiagnosticUtil.GetArg(args, "--catalog") ?? DefaultCatalogPath;
            var fieldsArg = DiagnosticUtil.GetArg(args, "--field") ?? "dense-wide,dense,sparse";
            var defocusArg = DiagnosticUtil.GetArg(args, "--defocus-steps") ?? "0,150,350";
            var aberrArg = DiagnosticUtil.GetArg(args, "--aberr") ?? "A0,A1,A2";
            var armsArg = DiagnosticUtil.GetArg(args, "--arms") ?? "off,on-zero,on,on-strong";
            var nominalResidual = ParseDouble(DiagnosticUtil.GetArg(args, "--corner-astig"), DefaultCornerAstigmatismMicrons);
            var strongResidual = ParseDouble(DiagnosticUtil.GetArg(args, "--corner-astig-strong"), StrongCornerAstigmatismMicrons);
            var iters = ParseInt(DiagnosticUtil.GetArg(args, "--iters"), 3);
            var warmup = ParseInt(DiagnosticUtil.GetArg(args, "--warmup"), 1);
            // Mag 17 is the cut that makes `dense-wide` genuinely dense (~34k on-frame stars on a 61 MP frame).
            // The installed G18 database bottoms out near mag 18, which is why the cut is this faint rather than
            // the mag 15.5-16 the AF bank datasets use.
            var limitMag = ParseDouble(DiagnosticUtil.GetArg(args, "--limit-mag"), 17.0);
            var exposureSeconds = ParseDouble(DiagnosticUtil.GetArg(args, "--exposure"), DefaultExposureSeconds);
            // Direct overrides on the named aberration configs, for reproducing a specific user setup.
            var tiltOverride = DiagnosticUtil.GetArg(args, "--tilt");
            var backfocusOverride = DiagnosticUtil.GetArg(args, "--backfocus");
            var censusOnly = DiagnosticUtil.HasFlag(args, "--census");
            var ladderOnly = DiagnosticUtil.HasFlag(args, "--kernel-ladder");
            var csvPath = DiagnosticUtil.GetArg(args, "--csv");
            var withDetection = DiagnosticUtil.HasFlag(args, "--with-detection");

            PrintBanner(catalogPath);

            if (ladderOnly) {
                RunKernelLadder(iters);
                return Task.CompletedTask;
            }

            var selectedFields = ResolveFields(fieldsArg);
            if (selectedFields == null) {
                Environment.ExitCode = 2;
                return Task.CompletedTask;
            }
            var selectedAberrations = ResolveAberrations(aberrArg);
            if (selectedAberrations == null) {
                Environment.ExitCode = 2;
                return Task.CompletedTask;
            }
            if (tiltOverride != null || backfocusOverride != null) {
                selectedAberrations = selectedAberrations.Select(a => a with {
                    TiltAmountMicrons = tiltOverride != null ? ParseDouble(tiltOverride, a.TiltAmountMicrons) : a.TiltAmountMicrons,
                    BackfocusErrorMicrons = backfocusOverride != null ? ParseDouble(backfocusOverride, a.BackfocusErrorMicrons) : a.BackfocusErrorMicrons,
                }).ToList();
            }
            var defocusOffsets = defocusArg.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToList();
            var arms = armsArg.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

            using var contention = withDetection ? ConcurrentDetectionLoad.Start() : null;
            if (withDetection) {
                Console.WriteLine("contention: a real StarDetector.Detect loop is running alongside every timed render");
            }

            var rows = new List<string>();
            rows.Add("field,focalMm,focalRatio,limitMag,defocusSteps,aberr,arm,iters,"
                   + "starsQueried,stampJobs,distinctKernels,kernelCacheBytes,maxKernelRadiusPx,orientationBins,defocusQuantumUm,"
                   + "totalMsMedian,totalMsMin,totalMsMax,queryMs,jobBuildMs,kernelGenMs,stampMs,developMs,"
                   + "kernelGenMsPerKernel,skyPlusDarkElectronsPerPx,config,processorCount");

            foreach (var field in selectedFields) {
                PrintRigBanner(field, limitMag, exposureSeconds);

                foreach (var offset in defocusOffsets) {
                    foreach (var aberration in selectedAberrations) {
                        foreach (var arm in arms) {
                            RenderRequest request;
                            try {
                                request = BuildRequest(field, aberration, arm, offset, limitMag, exposureSeconds, catalogPath,
                                                       nominalResidual, strongResidual);
                            } catch (NotSupportedException ex) {
                                Console.Error.WriteLine($"arm '{arm}': {ex.Message}");
                                Environment.ExitCode = 2;
                                return Task.CompletedTask;
                            }
                            var compositor = new StarFieldCompositor(path => new AstapCatalogReader(path));
                            var cell = MeasureCell(compositor, request, iters, warmup, censusOnly);
                            PrintRow(field, offset, aberration, arm, cell, censusOnly);
                            if (!censusOnly) {
                                rows.Add(CsvRow(field, aberration, arm, offset, limitMag, iters, cell));
                            }
                        }
                    }
                }
            }

            if (!censusOnly && csvPath != null) {
                File.WriteAllLines(csvPath, rows, Encoding.UTF8);
                Console.WriteLine();
                Console.WriteLine($"CSV written to {csvPath}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// A background star-detection load, emulating what a render actually competes with in NINA: the
        /// camera prefetches the next frame at <c>StartExposure</c> while the previous autofocus point is still
        /// being detected, and both loops are routed through the same shared CPU governor. A ratio measured on
        /// an otherwise idle box is the optimistic case.
        /// </summary>
        private sealed class ConcurrentDetectionLoad : IDisposable {
            private readonly CancellationTokenSource cts = new CancellationTokenSource();
            private readonly Task loop;
            private readonly Mat field;

            private ConcurrentDetectionLoad(Mat field) {
                this.field = field;
                loop = Task.Run(async () => {
                    var detector = new StarDetector(new AlglibAPI());
                    while (!cts.IsCancellationRequested) {
                        try {
                            await detector.Detect(field, new StarDetectorParams(), null, cts.Token);
                        } catch (OperationCanceledException) {
                            return;
                        }
                    }
                });
            }

            public static ConcurrentDetectionLoad Start() {
                const int size = 3008;
                var field = StarStamper.CreateFlat(size, size, 0.05f);
                var random = new Random(1234);
                for (var i = 0; i < 1500; ++i) {
                    StarStamper.AddGaussianStar(field, random.Next(20, size - 20), random.Next(20, size - 20), 2.2, 0.6);
                }
                StarStamper.AddGaussianNoise(field, 0.01, 4242);
                return new ConcurrentDetectionLoad(field);
            }

            public void Dispose() {
                cts.Cancel();
                try { loop.Wait(TimeSpan.FromSeconds(30)); } catch (AggregateException) { }
                cts.Dispose();
                field.Dispose();
            }
        }

        /// <summary>One cell's measurement: the census plus the per-phase medians over the timed iterations.</summary>
        private sealed class CellResult {
            public RenderPhaseTimings Census;
            public double TotalMsMedian, TotalMsMin, TotalMsMax;
            public double QueryMs, JobBuildMs, KernelGenMs, StampMs, DevelopMs;
            public double SkyPlusDarkElectronsPerPx;
        }

        private static CellResult MeasureCell(
                StarFieldCompositor compositor, RenderRequest request, int iters, int warmup, bool censusOnly) {

            var result = new CellResult {
                SkyPlusDarkElectronsPerPx = SkyPlusDarkElectronsPerPixel(request)
            };

            // Census pass, untimed: the authoritative star and kernel counts for this cell.
            var census = new RenderPhaseTimings();
            var censusFrame = compositor.Render(request, null, census, CancellationToken.None);
            GC.KeepAlive(censusFrame);
            result.Census = census;
            if (censusOnly) {
                return result;
            }

            // Warmup, untimed. Without it the first timed iteration carries several hundred ms of cold disk I/O
            // paging the .290 catalog cells in -- a cost a live AF sweep pays once and never again.
            for (var w = 0; w < warmup; ++w) {
                var warm = compositor.Render(request, null, null, CancellationToken.None);
                GC.KeepAlive(warm);
            }

            var totals = new List<double>(iters);
            var samples = new List<RenderPhaseTimings>(iters);
            for (var i = 0; i < iters; ++i) {
                // A 61 MP render allocates a 244 MB accumulator and a 122 MB output per iteration; collecting
                // between iterations keeps one iteration's garbage from landing in the next one's sample.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var timings = new RenderPhaseTimings();
                var sw = Stopwatch.StartNew();
                var frame = compositor.Render(request, null, timings, CancellationToken.None);
                sw.Stop();
                GC.KeepAlive(frame);   // the 122 MB output is otherwise dead on return and could be elided
                totals.Add(sw.Elapsed.TotalMilliseconds);
                samples.Add(timings);
            }

            totals.Sort();
            result.TotalMsMedian = Median(totals);
            result.TotalMsMin = totals[0];
            result.TotalMsMax = totals[totals.Count - 1];
            result.QueryMs = MedianOf(samples, t => t.CatalogQueryMs);
            result.JobBuildMs = MedianOf(samples, t => t.StampJobBuildMs);
            result.KernelGenMs = MedianOf(samples, t => t.KernelGenerateMs);
            result.StampMs = MedianOf(samples, t => t.StampMs);
            result.DevelopMs = MedianOf(samples, t => t.DevelopMs);
            return result;
        }

        /// <summary>
        /// Times <see cref="PsfKernelGenerator.Generate"/> alone across a radius ladder and fits the log-log
        /// scaling exponent. Runs in seconds and needs no catalog, so it is the cheapest early warning that a
        /// kernel-generation change scales badly: a separable convolution holds an exponent near 2, while a
        /// direct 2-D convolution over the oversampled grid shows up immediately near 4.
        /// </summary>
        private static void RunKernelLadder(int iters) {
            var model = new DefocusModel(
                apertureMillimeters: 1000.0 / 7.1,
                focalLengthMillimeters: 1000.0,
                centralObstructionFraction: 0.0,
                pixelSizeMicrons: SensorRegistry.Get(SonySensorModel.IMX455).PixelSizeMicrons,
                seeingArcsec: DefaultSeeingArcsec,
                wavelengthNm: 540.0,
                focuserStepSizeMicrons: DefaultFocuserStepSizeMicrons,
                optimalFocuserPosition: DefaultOptimalFocuserPosition);

            Console.WriteLine();
            Console.WriteLine($"== kernel ladder (sigma_min={model.SigmaMinPixels:F3} px, N={model.FocalRatio:F2}, p={model.PixelSizeMicrons:F2} um) ==");
            Console.WriteLine($"{"targetR",8} {"actualR",8} {"circMs",9} {"ellipMs",9} {"ratio",7} {"bytes",12} {"cExp",6} {"eExp",6}");

            // Warm both generators before the first timed rung. Without this the smallest radius absorbs the
            // JIT of the whole generation path -- it measured ~6 ms against ~3.7 ms for a kernel four times its
            // area -- which inverts the low end of the ladder and drags the fitted exponent well below the truth.
            PsfKernelGenerator.Generate(model, 400.0);
            PsfKernelGenerator.GenerateElliptical(model.SigmaMinPixels, model.CentralObstructionFraction, 10.0, 20.0, 0.6);

            var xs = new List<double>();
            var ys = new List<double>();
            var es = new List<double>();
            foreach (var targetRadius in new[] { 8, 15, 30, 60, 120, 240 }) {
                // radius = ceil(r_out + 5*sigma)  =>  r_out = R - 5*sigma; and r_out = |delta| / (2*N*p).
                var rOut = targetRadius - 5.0 * model.SigmaMinPixels;
                if (rOut <= 0.0) continue;
                var defocusMicrons = rOut * 2.0 * model.FocalRatio * model.PixelSizeMicrons;

                var circularMs = TimeMedian(() => PsfKernelGenerator.Generate(model, defocusMicrons), iters);
                var kernel = PsfKernelGenerator.Generate(model, defocusMicrons);

                // A 2:1 ellipse at an off-axis angle, sized so its bounding half-extent lands on the same rung.
                var ellipticalMs = TimeMedian(
                    () => PsfKernelGenerator.GenerateElliptical(
                        model.SigmaMinPixels, model.CentralObstructionFraction, 0.5 * rOut, rOut, 0.6),
                    iters);

                xs.Add(Math.Log(kernel.Radius));
                ys.Add(Math.Log(Math.Max(circularMs, 1e-6)));
                es.Add(Math.Log(Math.Max(ellipticalMs, 1e-6)));

                // Local exponents against the previous rung. Reported per-rung rather than as one fit over the
                // whole ladder because the small radii are dominated by fixed cost -- the radial LUT has a 512-
                // entry floor -- so a single least-squares slope across the range reads well below the true
                // asymptotic scaling and would mask a genuinely bad convolution at large R.
                var circularExponent = xs.Count >= 2 ? (ys[^1] - ys[^2]) / (xs[^1] - xs[^2]) : double.NaN;
                var ellipticalExponent = xs.Count >= 2 ? (es[^1] - es[^2]) / (xs[^1] - xs[^2]) : double.NaN;
                Console.WriteLine($"{targetRadius,8} {kernel.Radius,8} {circularMs,9:F2} {ellipticalMs,9:F2} "
                                + $"{ellipticalMs / circularMs,7:F2} {kernel.ApproximateByteSize,12:N0} "
                                + $"{Exponent(circularExponent),6} {Exponent(ellipticalExponent),6}");
            }

            if (xs.Count >= 3) {
                // Asymptotic slope over the largest three rungs -- the regime that actually matters, since
                // kernel cost and cache bytes both grow as R^2 there.
                Console.WriteLine();
                Console.WriteLine($"asymptotic exponent over the top 3 rungs: circular={TopSlope(xs, ys):F2}, elliptical={TopSlope(xs, es):F2}"
                                + "   (a separable path holds ~2; a direct 2-D convolution shows ~4)");
            }
        }

        private static RenderRequest BuildRequest(
                BenchField field, AberrationConfig aberration, string arm, int defocusOffsetSteps,
                double limitMag, double exposureSeconds, string catalogPath,
                double nominalResidual, double strongResidual) {

            // Constructed directly rather than through SynthRenderRequestFactory: that factory maps a *bank
            // dataset spec* onto a request and hard-codes AberrationsEnabled = false, and the whole point of this
            // harness is to vary the aberration block. Nothing derived is duplicated here -- no defocus
            // quantizer, no kernel sizing -- so there is no formula that can drift from the renderer's own.
            var sensor = SensorRegistry.Get(SonySensorModel.IMX455);
            var request = new RenderRequest {
                FocuserConnected = true,
                FocuserPosition = DefaultOptimalFocuserPosition + defocusOffsetSteps,
                TelescopeConnected = true,
                RaDegreesJ2000 = field.RaDegrees,
                DecDegreesJ2000 = field.DecDegrees,
                ApertureMillimeters = field.FocalLengthMm / field.FocalRatio,
                FocalLengthMillimeters = field.FocalLengthMm,
                CentralObstructionEnabled = false,
                CentralObstructionFraction = 0.0,
                OpticalThroughput = DefaultOpticalThroughput,
                SensorModel = SonySensorModel.IMX455,
                Gain = DefaultGain,
                BiasPedestalAdu = sensor.BitDepth >= 16 ? 500 : 125,
                SensorTemperatureCelsius = DefaultSensorTemperatureCelsius,
                Filter = SimulatorFilter.L,
                SkyBrightnessMagPerArcsec2 = DefaultSkyBrightness,
                SeeingArcsec = DefaultSeeingArcsec,
                OptimalFocuserPosition = DefaultOptimalFocuserPosition,
                FocuserStepSizeMicrons = DefaultFocuserStepSizeMicrons,
                AstapCatalogPath = catalogPath,
                LimitingMagnitude = limitMag,
                RotationDegrees = 0.0,
                NoiseSeed = DefaultNoiseSeed,
                AberrationsEnabled = aberration.TiltAmountMicrons > 0.0 || aberration.BackfocusErrorMicrons != 0.0,
                TiltAngleDegrees = aberration.TiltAngleDegrees,
                TiltAmountMicrons = aberration.TiltAmountMicrons,
                BackfocusErrorMicrons = aberration.BackfocusErrorMicrons,
                OpticalAxisOffsetXMicrons = 0.0,
                OpticalAxisOffsetYMicrons = 0.0,
                ExposureSeconds = exposureSeconds
            };
            return ApplyArm(request, arm, nominalResidual, strongResidual);
        }

        /// <summary>
        /// Applies the feature arm to a request.
        ///
        /// <para><c>on-zero</c> models a <b>perfectly corrected optic</b> (residual 0), which is not the same
        /// thing as the feature being off: mis-spacing still splits the focal surfaces by exactly half the
        /// curvature it induces, so this arm is elliptical wherever the backfocus error is nonzero. On the clean
        /// <c>A0</c> config it IS a verification arm — with no residual and no spacing error the coefficient is
        /// literally 0.0, every star's two quantized levels coincide, the compositor takes the circular
        /// generator, and it must measure the same as <c>off</c>; a divergence there means the level-collapse
        /// rule broke. On <c>A1</c>/<c>A2</c> it is instead the floor of what a well-corrected rig costs.</para>
        /// </summary>
        private static RenderRequest ApplyArm(RenderRequest request, string arm, double nominalResidual, double strongResidual) {
            switch (arm.ToLowerInvariant()) {
                case "off":
                    return request with { AstigmatismEnabled = false, CornerAstigmatismMicrons = 0.0 };

                case "on-zero":
                    return request with { AstigmatismEnabled = true, CornerAstigmatismMicrons = 0.0 };

                case "on":
                    return request with { AstigmatismEnabled = true, CornerAstigmatismMicrons = nominalResidual };

                case "on-strong":
                    return request with { AstigmatismEnabled = true, CornerAstigmatismMicrons = strongResidual };

                default:
                    throw new NotSupportedException($"unknown arm '{arm}' (expected off, on-zero, on, on-strong)");
            }
        }

        /// <summary>
        /// Sky + dark electrons per pixel for this request. Reported because it decides which side of
        /// <c>NoiseGenerator.PoissonToGaussianThreshold</c> the frame develops on — the single biggest
        /// confound in any simulator timing number.
        /// </summary>
        private static double SkyPlusDarkElectronsPerPixel(RenderRequest request) {
            var sensor = SensorRegistry.Get(request.SensorModel);
            var filter = FilterRegistry.Get(request.Filter);
            var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
            return radiometry.SkyElectronsPerPixel() + radiometry.DarkElectronsPerPixel();
        }

        private static void PrintBanner(string catalogPath) {
            var config =
#if DEBUG
                "Debug";
#else
                "Release";
#endif
            Console.WriteLine($"config={config}  ProcessorCount={Environment.ProcessorCount}  "
                            + $"Vector<float>.Count={Vector<float>.Count}  "
                            + $"GC={(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}  catalog={catalogPath}");
            if (config != "Release") {
                Console.Error.WriteLine("WARNING: Debug build -- these numbers are not gate-eligible. Rebuild with -c Release.");
            }
        }

        private static void PrintRigBanner(BenchField field, double limitMag, double exposureSeconds) {
            var sensor = SensorRegistry.Get(SonySensorModel.IMX455);
            var model = new DefocusModel(
                apertureMillimeters: field.FocalLengthMm / field.FocalRatio,
                focalLengthMillimeters: field.FocalLengthMm,
                centralObstructionFraction: 0.0,
                pixelSizeMicrons: sensor.PixelSizeMicrons,
                seeingArcsec: DefaultSeeingArcsec,
                wavelengthNm: FilterRegistry.Get(SimulatorFilter.L).CentralWavelengthNm,
                focuserStepSizeMicrons: DefaultFocuserStepSizeMicrons,
                optimalFocuserPosition: DefaultOptimalFocuserPosition);
            var fovDeg = StarFieldCompositor.DiagonalFovDegrees(sensor.Width, sensor.Height, sensor.PixelSizeMicrons, field.FocalLengthMm);

            Console.WriteLine();
            Console.WriteLine($"== {field.Name}: {field.Note}, RA={field.RaDegrees:F4} Dec={field.DecDegrees:F4}, "
                            + $"{field.FocalLengthMm:F0} mm f/{field.FocalRatio:F1}, mag<={limitMag:F1}, {exposureSeconds:F1} s ==");
            Console.WriteLine($"   {sensor.Width}x{sensor.Height} ({sensor.Width * (long)sensor.Height / 1e6:F1} MP), "
                            + $"{model.ArcsecPerPixel:F3}\"/px, diagonal FOV {fovDeg:F2} deg");
            Console.WriteLine($"   sigma_min={model.SigmaMinPixels:F3} px, HFR_min={model.HfrMinPixels:F3} px, "
                            + $"kappa={model.KappaPixelsPerStep:F5} px/step, maxAbsDefocus={StarFieldCompositor.MaxAbsDefocusMicrons(model):F0} um");
            Console.WriteLine();
            Console.WriteLine($"{"defocus",8} {"aberr",6} {"arm",9} {"stars",8} {"jobs",8} {"kernels",8} {"cacheMB",8} {"maxR",6} "
                            + $"{"bins",5} {"query",8} {"jobBuild",9} {"kernelGen",10} {"stamp",8} {"develop",9} {"total",9}");
        }

        private static void PrintRow(BenchField field, int defocusOffset, AberrationConfig aberration, string arm, CellResult cell, bool censusOnly) {
            var c = cell.Census;
            if (censusOnly) {
                Console.WriteLine($"{defocusOffset,8} {aberration.Name,6} {arm,9} {c.StarsQueried,8:N0} {c.StampJobs,8:N0} "
                                + $"{c.DistinctKernels,8:N0} {c.KernelCacheBytes / 1048576.0,8:F1} {c.MaxKernelRadius,6}");
                return;
            }
            Console.WriteLine($"{defocusOffset,8} {aberration.Name,6} {arm,9} {c.StarsQueried,8:N0} {c.StampJobs,8:N0} "
                            + $"{c.DistinctKernels,8:N0} {c.KernelCacheBytes / 1048576.0,8:F1} {c.MaxKernelRadius,6} "
                            + $"{c.OrientationBins,5} {cell.QueryMs,8:F1} {cell.JobBuildMs,9:F1} {cell.KernelGenMs,10:F1} {cell.StampMs,8:F1} "
                            + $"{cell.DevelopMs,9:F1} {cell.TotalMsMedian,9:F1}");
        }

        private static string CsvRow(BenchField field, AberrationConfig aberration, string arm, int defocusOffset, double limitMag, int iters, CellResult cell) {
            var c = cell.Census;
            var config =
#if DEBUG
                "Debug";
#else
                "Release";
#endif
            var perKernel = c.DistinctKernels > 0 ? cell.KernelGenMs / c.DistinctKernels : 0.0;
            return string.Join(",", new[] {
                field.Name,
                field.FocalLengthMm.ToString("F0", CultureInfo.InvariantCulture),
                field.FocalRatio.ToString("F2", CultureInfo.InvariantCulture),
                limitMag.ToString("F1", CultureInfo.InvariantCulture),
                defocusOffset.ToString(CultureInfo.InvariantCulture),
                aberration.Name,
                arm,
                iters.ToString(CultureInfo.InvariantCulture),
                c.StarsQueried.ToString(CultureInfo.InvariantCulture),
                c.StampJobs.ToString(CultureInfo.InvariantCulture),
                c.DistinctKernels.ToString(CultureInfo.InvariantCulture),
                c.KernelCacheBytes.ToString(CultureInfo.InvariantCulture),
                c.MaxKernelRadius.ToString(CultureInfo.InvariantCulture),
                c.OrientationBins.ToString(CultureInfo.InvariantCulture),
                c.DefocusQuantumMicrons.ToString("F2", CultureInfo.InvariantCulture),
                cell.TotalMsMedian.ToString("F1", CultureInfo.InvariantCulture),
                cell.TotalMsMin.ToString("F1", CultureInfo.InvariantCulture),
                cell.TotalMsMax.ToString("F1", CultureInfo.InvariantCulture),
                cell.QueryMs.ToString("F1", CultureInfo.InvariantCulture),
                cell.JobBuildMs.ToString("F1", CultureInfo.InvariantCulture),
                cell.KernelGenMs.ToString("F1", CultureInfo.InvariantCulture),
                cell.StampMs.ToString("F1", CultureInfo.InvariantCulture),
                cell.DevelopMs.ToString("F1", CultureInfo.InvariantCulture),
                perKernel.ToString("F2", CultureInfo.InvariantCulture),
                cell.SkyPlusDarkElectronsPerPx.ToString("F2", CultureInfo.InvariantCulture),
                config,
                Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            });
        }

        private static List<BenchField> ResolveFields(string arg) {
            var names = arg.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? Fields.Keys.ToArray()
                : arg.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            var resolved = new List<BenchField>();
            foreach (var name in names) {
                if (!Fields.TryGetValue(name, out var field)) {
                    Console.Error.WriteLine($"Unknown --field '{name}' (known: {string.Join(", ", Fields.Keys)})");
                    return null;
                }
                resolved.Add(field);
            }
            return resolved;
        }

        private static List<AberrationConfig> ResolveAberrations(string arg) {
            var resolved = new List<AberrationConfig>();
            foreach (var name in arg.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())) {
                if (!AberrationConfigs.TryGetValue(name, out var config)) {
                    Console.Error.WriteLine($"Unknown --aberr '{name}' (known: {string.Join(", ", AberrationConfigs.Keys)})");
                    return null;
                }
                resolved.Add(config);
            }
            return resolved;
        }

        private static string Exponent(double value) => double.IsNaN(value) ? "-" : value.ToString("F2", CultureInfo.InvariantCulture);

        /// <summary>Least-squares slope of the last three log-log points.</summary>
        private static double TopSlope(List<double> xs, List<double> ys) {
            var from = Math.Max(0, xs.Count - 3);
            double mx = xs.Skip(from).Average(), my = ys.Skip(from).Average(), num = 0.0, den = 0.0;
            for (var i = from; i < xs.Count; ++i) {
                num += (xs[i] - mx) * (ys[i] - my);
                den += (xs[i] - mx) * (xs[i] - mx);
            }
            return den > 0 ? num / den : double.NaN;
        }

        private static double TimeMedian(Action action, int iters) {
            var samples = new List<double>(iters);
            for (var i = 0; i < iters; ++i) {
                var sw = Stopwatch.StartNew();
                action();
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            return Median(samples);
        }

        private static double MedianOf(List<RenderPhaseTimings> samples, Func<RenderPhaseTimings, double> selector) {
            var values = samples.Select(selector).ToList();
            values.Sort();
            return Median(values);
        }

        private static double Median(List<double> sorted) {
            if (sorted.Count == 0) return 0.0;
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }

        private static int ParseInt(string value, int fallback) =>
            value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

        private static double ParseDouble(string value, double fallback) =>
            value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }
}
