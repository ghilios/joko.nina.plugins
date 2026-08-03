#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// The render pipeline. Turns an immutable <see cref="RenderRequest"/> into a row-major
    /// <c>ushort[width*height]</c> 16-bit frame:
    /// <list type="number">
    /// <item>resolve the sensor/filter and build the radiometry, defocus, aberration-surface, and TAN-projection models;</item>
    /// <item>resolve the catalog reader for the request's catalog path and query it for the sensor's diagonal field of view;</item>
    /// <item>project each star, look up its <b>local</b> defocus Δ from the aberration surface, quantize Δ, and
    /// build (once, cached) the analytic PSF kernel for that level;</item>
    /// <item>stamp <c>kernel·flux</c> into a shared electron accumulator using a disjoint horizontal row-stripe
    /// partition (race-free — see below), add sky + dark, and develop to ADU with Poisson + read noise.</item>
    /// </list>
    ///
    /// <para><b>Catalog robustness.</b> A missing/absent ASTAP database, a pointing outside the catalog's
    /// coverage, a per-cell decode error, or simply zero stars never fails the exposure: the compositor logs a
    /// descriptive warning and renders a <b>starless</b> frame (sky + dark + noise only). The focuser/mount
    /// disconnected checks are the camera's hard errors, not the compositor's.</para>
    ///
    /// <para><b>Catalog path.</b> The reader is resolved <b>per exposure</b> from the request's
    /// <see cref="RenderRequest.AstapCatalogPath"/> snapshot rather than latched at construction, so editing the
    /// option takes effect on the very next exposure (no camera reconnect) and the missing-database warning always
    /// names the path that was actually read.</para>
    ///
    /// <para><b>Concurrency.</b> <see cref="StarStamper.Stamp"/>'s accumulator write is a non-atomic <c>+=</c>
    /// and defocused donuts overlap, so stamping cannot be parallelized over stars into one shared buffer. This
    /// compositor instead partitions the frame into horizontal <b>row-stripes</b>: each stripe owns a disjoint
    /// block of output rows and, running in parallel, stamps every star whose kernel footprint intersects those
    /// rows into <b>only its own rows</b> of the one shared accumulator (a star spanning two stripes is stamped
    /// by both, each clipping to its rows). Because the stripes write disjoint rows, the shared-array
    /// <c>+=</c> never races. Development then runs in parallel too, via <see cref="FrameDeveloper"/>: one
    /// seeded <see cref="NoiseGenerator"/> per stripe over a disjoint index range, so the noise stays
    /// deterministic for a given seed.</para>
    ///
    /// <para>Note the two partitions are <b>not</b> the same kind of thing and must not be harmonized. This
    /// stamping partition is a pure execution detail — stripes only decide <i>who writes which rows</i>, never
    /// what value lands there — so <see cref="StripeCount"/> is free to track
    /// <see cref="Environment.ProcessorCount"/>. <see cref="FrameDeveloper.StripeCount"/> is a fixed constant
    /// because each of its stripes draws from its own RNG stream, which makes that partition part of the frame's
    /// identity.</para>
    /// </summary>
    public class StarFieldCompositor : IStarFieldCompositor {

        /// <summary>
        /// Defocus quantization resolution, expressed as the change in the geometric donut <b>outer radius</b>
        /// (px) between adjacent quantized levels. One PSF kernel is built (and cached) per distinct level, so a
        /// finer value builds more kernels but tracks the HFR-vs-focuser curve more smoothly. 0.25 px keeps the
        /// per-star HFR error well under the detector's own bias while collapsing the smooth aberration surface
        /// onto a small kernel set (adjacent field points share a level).
        /// </summary>
        private const double DonutRadiusQuantumPixels = 0.25;

        /// <summary>Floor on the quantization step (µm) so a fast optic (small N) cannot produce a degenerate sub-µm quantum.</summary>
        private const double MinDefocusQuantumMicrons = 0.5;

        /// <summary>
        /// Kernel-support cap used when clamping the quantized defocus. Kept a little below
        /// <see cref="PsfKernelGenerator.MaxKernelRadius"/> so <c>ceil(r_out + 5σ)</c> stays within the hard cap
        /// even after the σ tail is added. A physically-absurd defocus (far past any real focuser travel) is
        /// clamped to the largest kernel rather than throwing.
        /// </summary>
        private const int MaxSafeKernelRadius = 500;

        /// <summary>Fractional margin added to the diagonal FOV so stars just outside the exact frame edge are still queried.</summary>
        private const double FovMarginFactor = 1.05;

        /// <summary>Target rows per stripe used to size the row-stripe partition (bounds boundary re-stamping).</summary>
        private const int TargetRowsPerStripe = 128;

        private readonly Func<string, IAstapCatalogReader> catalogReaderFactory;

        /// <param name="catalogReaderFactory">Resolves the ASTAP catalog reader for a catalog directory. Invoked
        /// once per <see cref="Render"/> with that exposure's <see cref="RenderRequest.AstapCatalogPath"/>, so the
        /// reader always reads the path the user currently has configured. The camera passes
        /// <c>path => new AstapCatalogReader(path)</c>; tests inject a fake.</param>
        public StarFieldCompositor(Func<string, IAstapCatalogReader> catalogReaderFactory) {
            this.catalogReaderFactory = catalogReaderFactory ?? throw new ArgumentNullException(nameof(catalogReaderFactory));
        }

        /// <summary>
        /// Convenience overload that binds one reader for every catalog path — for callers (and tests) that
        /// already hold the reader they want and do not care about the request's path.
        /// </summary>
        /// <param name="catalogReader">The ASTAP catalog reader to use regardless of the request's catalog path.</param>
        public StarFieldCompositor(IAstapCatalogReader catalogReader)
            : this(FixedReaderFactory(catalogReader)) {
        }

        /// <summary>Wraps a fixed reader as a path-ignoring factory, validating it eagerly (not at render time).</summary>
        private static Func<string, IAstapCatalogReader> FixedReaderFactory(IAstapCatalogReader catalogReader) {
            if (catalogReader == null) throw new ArgumentNullException(nameof(catalogReader));
            return _ => catalogReader;
        }

        private readonly struct StampJob {
            public StampJob(double cx, double cy, PsfKernel kernel, double flux) {
                Cx = cx;
                Cy = cy;
                Kernel = kernel;
                Flux = flux;
            }

            public double Cx { get; }
            public double Cy { get; }
            public PsfKernel Kernel { get; }
            public double Flux { get; }
        }

        /// <inheritdoc/>
        public ushort[] Render(RenderRequest request, CancellationToken token) => Render(request, null, token);

        /// <summary>
        /// Renders exactly what <see cref="Render(RenderRequest, CancellationToken)"/> does, and — when
        /// <paramref name="truthSink"/> is non-null — appends one <see cref="StarTruth"/> per accepted star to
        /// it as a side effect. This is the synthetic-bank generator's seam onto ground truth the compositor
        /// already computes internally (position, flux, quantized defocus, kernel HFR) and would otherwise
        /// discard; it exists as a second overload rather than a new interface member so
        /// <see cref="IStarFieldCompositor"/> — the camera's MEF-composed seam — is untouched.
        /// <b>Passing a null sink must render byte-identically to the single-argument overload</b>: this method
        /// skips every truth computation entirely when <paramref name="truthSink"/> is null, so the two calls
        /// run the identical stamp/development pipeline with no extra allocation on the hot path.
        /// </summary>
        public ushort[] Render(RenderRequest request, ICollection<StarTruth> truthSink, CancellationToken token) {
            if (request == null) throw new ArgumentNullException(nameof(request));
            token.ThrowIfCancellationRequested();

            var sensor = SensorRegistry.Get(request.SensorModel);
            var filter = FilterRegistry.Get(request.Filter);
            int width = sensor.Width;
            int height = sensor.Height;

            // The radiometry/defocus/aberration models and the TAN projection all require a positive focal length
            // (plate scale). A focal length of 0 is a misconfiguration — the camera resolves it from the profile —
            // but rather than crash the exposure we render a dark frame (bias + dark + read noise, no sky/stars).
            // No truth either way: a dark frame has no stars to have truth about.
            double sky = 0.0, dark = 0.0;
            List<StampJob> jobs;
            if (request.FocalLengthMillimeters > 0.0) {
                var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
                var defocusModel = DefocusModel.FromRequest(request, sensor, filter);
                var aberration = AberrationSurface.FromRequest(request, sensor);
                sky = radiometry.SkyElectronsPerPixel();
                dark = radiometry.DarkElectronsPerPixel();
                jobs = BuildStampJobs(request, sensor, radiometry, defocusModel, aberration, width, height, truthSink, token);
            } else {
                Logger.Warning(
                    $"Synthetic camera: focal length is {request.FocalLengthMillimeters} mm (must be > 0). " +
                    "Rendering a dark frame (bias + dark + noise only, no sky or stars).");
                dark = sensor.DarkElectronsPerPixelPerSecondAtTemperature(request.SensorTemperatureCelsius) * Math.Max(0.0, request.ExposureSeconds);
                jobs = new List<StampJob>();
            }

            var accumulator = new float[width * height];

            // Stamp every job into the shared accumulator via the disjoint row-stripe partition, then fold in the
            // uniform sky + dark background. Both are per-stripe writes to disjoint rows, so the shared-array
            // accumulation never races.
            var background = (float)(sky + dark);
            StampAndAddBackground(accumulator, width, height, jobs, background, token);

            token.ThrowIfCancellationRequested();

            // Development is ~90% of a 61 MP render, so it runs in parallel — deterministically, via a fixed
            // stripe partition with one seeded generator per stripe. See FrameDeveloper.StripeCount.
            return FrameDeveloper.DevelopToAdu(
                accumulator, sensor, request.Gain, request.BiasPedestalAdu, request.NoiseSeed, token);
        }

        /// <summary>
        /// Projects the catalog stars and turns each on-frame (or wing-spilling) star into a <see cref="StampJob"/>:
        /// its sub-pixel centre, the cached PSF kernel for its quantized local defocus, and its total flux (e⁻).
        /// When <paramref name="truthSink"/> is non-null, appends one <see cref="StarTruth"/> per accepted job to
        /// it — same loop, same acceptance decision, so a wing-spill star that gets a <see cref="StampJob"/> also
        /// gets truth (that is exactly the case a recall audit cares about: its donut lands on the sensor even
        /// though its centre does not). When <paramref name="truthSink"/> is null this method does no extra work
        /// or allocation beyond what it always did.
        /// </summary>
        private List<StampJob> BuildStampJobs(
                RenderRequest request, SensorDefinition sensor, RadiometryCalculator radiometry,
                DefocusModel defocusModel, AberrationSurface aberration, int width, int height,
                ICollection<StarTruth> truthSink, CancellationToken token) {

            var projection = new TanProjection(
                request.RaDegreesJ2000, request.DecDegreesJ2000,
                request.FocalLengthMillimeters, sensor.PixelSizeMicrons, request.RotationDegrees, width, height);

            var fovDeg = DiagonalFovDegrees(width, height, sensor.PixelSizeMicrons, request.FocalLengthMillimeters) * FovMarginFactor;

            var quantumMicrons = DefocusQuantumMicrons(defocusModel);
            var maxAbsMicrons = MaxAbsDefocusMicrons(defocusModel);

            // PSF margin: the worst-case kernel radius over the field (evaluated at the corners for this focuser
            // position). Stars whose centres fall up to this far off-frame still spill their donut onto the sensor.
            var psfMargin = WorstCaseKernelRadius(request, sensor, defocusModel, aberration, quantumMicrons, maxAbsMicrons);

            var stars = QueryCatalogStars(request, fovDeg);

            var jobs = new List<StampJob>(stars.Count);
            var kernelCache = new Dictionary<long, PsfKernel>();
            var processed = 0;
            foreach (var star in stars) {
                if ((++processed & 0x3FFF) == 0) {
                    token.ThrowIfCancellationRequested();
                }

                var coordinates = star.Coordinates;
                if (coordinates == null) {
                    continue;
                }
                if (coordinates.Epoch != Epoch.J2000) {
                    coordinates = coordinates.Transform(Epoch.J2000);
                }

                if (!projection.TryProject(coordinates.RADegrees, coordinates.Dec, out var x, out var y, psfMargin)) {
                    continue;
                }

                var px = (int)Math.Round(x);
                var py = (int)Math.Round(y);
                var delta = aberration.LocalDefocusMicrons(px, py, request.FocuserPosition);
                var level = QuantizeLevel(delta, quantumMicrons, maxAbsMicrons);
                if (!kernelCache.TryGetValue(level, out var kernel)) {
                    kernel = PsfKernelGenerator.Generate(defocusModel, level * quantumMicrons);
                    kernelCache[level] = kernel;
                }

                var flux = radiometry.StarElectrons(star.Magnitude);
                if (flux <= 0.0) {
                    continue;
                }
                jobs.Add(new StampJob(x, y, kernel, flux));

                if (truthSink != null) {
                    // Same phase selection Stamp itself will make for this exact (x, y) and kernel — see
                    // StarStamper.SelectPhase's doc for why this can never disagree with the actual stamp.
                    StarStamper.SelectPhase(x, y, kernel.PhasesPerAxis, out var phaseX, out var phaseY);
                    truthSink.Add(new StarTruth {
                        CxPixels = x,
                        CyPixels = y,
                        RaDegrees = coordinates.RADegrees,
                        DecDegrees = coordinates.Dec,
                        MagnitudeV = star.Magnitude,
                        FluxElectrons = flux,
                        LocalDefocusMicrons = delta,
                        QuantizedDefocusMicrons = level * quantumMicrons,
                        AnalyticHfrPixels = kernel.AnalyticHfrPixels,
                        MeasuredHfrPixels = kernel.MeasuredHfrPixels,
                        OuterRadiusPixels = kernel.OuterRadiusPixels,
                        InnerRadiusPixels = kernel.InnerRadiusPixels,
                        KernelSupportRadiusPixels = kernel.Radius,
                        PhaseX = phaseX,
                        PhaseY = phaseY,
                        KernelPeakFraction = kernel.PhasePeak(phaseX, phaseY)
                    });
                }
            }
            return jobs;
        }

        /// <summary>
        /// Resolves the reader for this exposure's catalog path and queries it for the pointing, returning an
        /// empty list (never throwing) on any failure. A missing/absent database is logged distinctly from a
        /// pointing that simply has no catalog stars, so the user can tell "install an ASTAP database" apart from
        /// "this patch of sky is empty".
        /// </summary>
        private List<CatalogStar> QueryCatalogStars(RenderRequest request, double fovDeg) {
            try {
                var stars = new List<CatalogStar>();
                // Resolved here, from the request's path snapshot, so an options edit lands on the next exposure —
                // and inside the try, so a factory that rejects the path is the same starless-frame warning as a
                // path that simply has no database, rather than a failed exposure.
                var catalogReader = catalogReaderFactory(request.AstapCatalogPath);
                // Query() validates the folder/database eagerly; per-cell decode errors surface during enumeration,
                // so the foreach is inside the try too.
                foreach (var star in catalogReader.Query(request.RaDegreesJ2000, request.DecDegreesJ2000, fovDeg, request.LimitingMagnitude)) {
                    stars.Add(star);
                }
                if (stars.Count == 0) {
                    Logger.Warning(
                        $"Synthetic camera: the pointing RA={request.RaDegreesJ2000:F4}°, Dec={request.DecDegreesJ2000:F4}° has no catalog stars " +
                        $"down to magnitude {request.LimitingMagnitude:F1} within a {fovDeg:F2}° field. Rendering a starless frame.");
                }
                return stars;
            } catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException) {
                Logger.Warning(
                    $"Synthetic camera: no ASTAP database found at '{request.AstapCatalogPath}' ({ex.Message}). " +
                    "Rendering a starless frame (sky + dark + noise only). Install an ASTAP star database and point the catalog path at it to render stars.");
                return new List<CatalogStar>();
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Warning(
                    $"Synthetic camera: the catalog query for RA={request.RaDegreesJ2000:F4}°, Dec={request.DecDegreesJ2000:F4}° " +
                    $"failed ({ex.GetType().Name}: {ex.Message}). Rendering a starless frame.");
                return new List<CatalogStar>();
            }
        }

        /// <summary>
        /// Stamps all jobs into <paramref name="accumulator"/> and adds the uniform background, both via a
        /// disjoint horizontal row-stripe partition so the shared-array writes never race.
        /// </summary>
        private static void StampAndAddBackground(
                float[] accumulator, int width, int height, IReadOnlyList<StampJob> jobs, float background, CancellationToken token) {

            var stripeCount = StripeCount(height);
            // Routed through the shared CPU governor for the same reason FrameDeveloper is: a render is kicked
            // off at StartExposure and runs alongside the star detection of the previous autofocus point, so an
            // ungoverned loop here would stack ProcessorCount threads on top of detection's. Passing stripeCount
            // as the knob keeps the degree exactly what it was (it is already <= ProcessorCount), so this changes
            // only which scheduler runs the loop. Value-neutral: each stripe writes just its own rows, so every
            // pixel still sees its jobs applied in j-ascending order with the background last, whatever the
            // scheduler or thread count does.
            var options = ParallelExecution.CreateOptions(stripeCount, token);
            Parallel.For(0, stripeCount, options, stripe => {
                var rowStart = (int)((long)stripe * height / stripeCount);
                var rowEnd = (int)((long)(stripe + 1) * height / stripeCount);
                if (rowStart >= rowEnd) {
                    return;
                }
                options.CancellationToken.ThrowIfCancellationRequested();

                for (var j = 0; j < jobs.Count; ++j) {
                    var job = jobs[j];
                    // Vertical footprint of this star's kernel; skip jobs that do not touch this stripe.
                    var y0 = (int)Math.Floor(job.Cy);
                    var top = y0 - job.Kernel.Radius - 1;
                    var bottom = y0 + job.Kernel.Radius + 1;
                    if (bottom < rowStart || top >= rowEnd) {
                        continue;
                    }
                    // The row clip is what makes the parallel stamp race-free: this stripe writes only its own
                    // [rowStart, rowEnd) rows of the shared accumulator, disjoint from every other stripe.
                    StarStamper.Stamp(accumulator, width, height, job.Cx, job.Cy, job.Kernel, job.Flux, rowStart, rowEnd);
                }

                if (background != 0f) {
                    var from = rowStart * width;
                    var to = rowEnd * width;
                    for (var i = from; i < to; ++i) {
                        accumulator[i] += background;
                    }
                }
            });
        }

        /// <summary>Angular diagonal field of view (degrees) for a sensor at a focal length.</summary>
        internal static double DiagonalFovDegrees(int width, int height, double pixelSizeMicrons, double focalLengthMm) {
            var diagonalPixels = Math.Sqrt((double)width * width + (double)height * height);
            var diagonalMm = 0.5 * diagonalPixels * (pixelSizeMicrons / 1000.0);
            return 2.0 * Math.Atan(diagonalMm / focalLengthMm) * 180.0 / Math.PI;
        }

        /// <summary>Defocus quantization step (µm) sized for <see cref="DonutRadiusQuantumPixels"/> of donut-radius resolution.</summary>
        private static double DefocusQuantumMicrons(DefocusModel model) {
            // r_out(px) = |Δ| / (2·N·p); a q-px change in r_out ⇒ a (q·2·N·p) µm change in Δ.
            var quantum = DonutRadiusQuantumPixels * 2.0 * model.FocalRatio * model.PixelSizeMicrons;
            return Math.Max(MinDefocusQuantumMicrons, quantum);
        }

        /// <summary>
        /// Largest |Δ| (µm) whose kernel support stays within <see cref="MaxSafeKernelRadius"/>. Quantized defocus
        /// is clamped to this so a runaway field point can never allocate an unbounded kernel.
        ///
        /// This is the <b>single source of truth for the kernel-cap guard</b>: both this renderer's own
        /// <see cref="QuantizeLevel"/> clamp and any external caller that needs to know "how far past focus can
        /// this model still render" — e.g. a synthetic-bank generator picking a defocus sweep range without
        /// risking a <see cref="PsfKernelGenerator.MaxKernelRadius"/> throw — must go through this one method,
        /// so the render-time clamp and an offline check of it can never drift apart. Lifted from <c>private</c>
        /// for exactly that reason.
        /// </summary>
        internal static double MaxAbsDefocusMicrons(DefocusModel model) {
            // radius ≈ ceil(r_out + 5σ) ≤ MaxSafeKernelRadius ⇒ r_out ≤ MaxSafeKernelRadius − 5σ − 1 (px).
            var maxOuterRadiusPixels = MaxSafeKernelRadius - 5.0 * model.SigmaMinPixels - 1.0;
            if (maxOuterRadiusPixels <= 0.0) {
                return 0.0;
            }
            return maxOuterRadiusPixels * 2.0 * model.FocalRatio * model.PixelSizeMicrons;
        }

        /// <summary>Quantizes a defocus Δ (µm) to an integer level after clamping to the safe range.</summary>
        private static long QuantizeLevel(double deltaMicrons, double quantumMicrons, double maxAbsMicrons) {
            var clamped = Math.Clamp(deltaMicrons, -maxAbsMicrons, maxAbsMicrons);
            return (long)Math.Round(clamped / quantumMicrons);
        }

        /// <summary>
        /// The worst-case kernel support radius (px) over the field for this focuser position, evaluated at the
        /// sensor corners (where the tilted/curved best-focus surface is farthest from the current focus). Used as
        /// the projection PSF margin so wing-spilling corner stars are not dropped.
        /// </summary>
        private static int WorstCaseKernelRadius(
                RenderRequest request, SensorDefinition sensor, DefocusModel model, AberrationSurface aberration,
                double quantumMicrons, double maxAbsMicrons) {

            int w = sensor.Width, h = sensor.Height;
            var corners = new (int px, int py)[] {
                (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w / 2, h / 2)
            };
            var worstAbsDelta = 0.0;
            foreach (var (px, py) in corners) {
                var delta = Math.Abs(aberration.LocalDefocusMicrons(px, py, request.FocuserPosition));
                if (delta > worstAbsDelta) {
                    worstAbsDelta = delta;
                }
            }
            var level = QuantizeLevel(worstAbsDelta, quantumMicrons, maxAbsMicrons);
            var quantizedDelta = level * quantumMicrons;
            var radius = (int)Math.Ceiling(model.OuterAnnulusRadiusPixels(quantizedDelta) + 5.0 * model.SigmaMinPixels);
            return Math.Clamp(radius, 1, MaxSafeKernelRadius);
        }

        /// <summary>Number of row-stripes for the parallel stamp, bounded by the CPU count and the frame height.</summary>
        private static int StripeCount(int height) {
            var byHeight = Math.Max(1, height / TargetRowsPerStripe);
            return Math.Clamp(Environment.ProcessorCount, 1, byHeight);
        }
    }
}
