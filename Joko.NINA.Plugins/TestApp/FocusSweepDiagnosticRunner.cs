#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;
using Size = OpenCvSharp.Size;

namespace TestApp {

    /// <summary>
    /// Headless diagnostic: replays a saved AutoFocus run through HocusFocus star detection and reports star
    /// count, HFR (median + robust spread), and per-reason rejection counts as a function of focuser position.
    /// Turns the analysis's "detection degrades at defocus" mechanisms (F1/F2) into measured numbers. Mirrors
    /// ContaminationDiagnosticRunner; TestApp is not run in CI, so this consumes real data on disk only.
    /// </summary>
    internal static class FocusSweepDiagnosticRunner {

        // Mirrors AutoFocusEngine.IMAGE_FILE_REGEX (private) so the diagnostic stays decoupled from the engine.
        private static readonly Regex ImageFileRegex = new Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private sealed class Frame {
            public string Path;
            public int FocuserPosition;
        }

        private sealed class PositionAccum {
            public int FocuserPosition;
            public int Frames;
            public readonly List<double> Hfrs = new List<double>();
            public readonly List<double> MeasurementSigmas = new List<double>();
            public readonly List<double> StructureSigmas = new List<double>();
            public long StructureCandidates, TotalDetected, TooSmall, OnBorder, TooDistorted, Degenerate,
                Saturated, LowSensitivity, NotCentered, TooFlat, TooLowHFR, HFRAnalysisFailed, PSFFitFailed,
                ContaminationSuspected, OutsideROI;
        }

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Focus-sweep diagnostic run failed");
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunImpl(string[] args) {
            var afRun = DiagnosticUtil.GetArg(args, "--af-run");
            var synthesizeDir = DiagnosticUtil.GetArg(args, "--synthesize");
            bool defaultParams = args.Any(a => a.Equals("--default-params", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(synthesizeDir)) {
                int count = 9;
                var countArg = DiagnosticUtil.GetArg(args, "--synthesize-count");
                if (!string.IsNullOrWhiteSpace(countArg)) count = int.Parse(countArg, CultureInfo.InvariantCulture);
                SynthesizeAfRun(synthesizeDir, count);
                Console.WriteLine($"Synthesized {count} frames into {synthesizeDir}");
                afRun = synthesizeDir; // proceed to sweep the freshly-synthesized folder
            }

            if (string.IsNullOrWhiteSpace(afRun)) {
                Console.Error.WriteLine("Usage: TestApp focus-sweep --af-run <dir> [--profile-id <guid>] [--out <dir>] [--default-params] [--noise-level <None|Low|Typical|High>] [--brightness-sensitivity <v>] [--brightness-sensitivity-sweep <a,b,step>]");
                Console.Error.WriteLine("       TestApp focus-sweep --synthesize <dir> [--synthesize-count <n>] --default-params [--out <dir>]");
                Environment.ExitCode = 2;
                return;
            }
            if (!Directory.Exists(afRun)) {
                throw new DirectoryNotFoundException($"AF-run folder not found: {afRun}");
            }

            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "focus-sweep");
            }
            Directory.CreateDirectory(outDir);
            Logger.SetLogLevel(LogLevelEnum.TRACE);

            var frames = ParseAfRun(afRun);
            if (frames.Count == 0) {
                throw new InvalidOperationException($"No AF frames matched the filename pattern in {afRun}");
            }
            Console.WriteLine($"AF run: {afRun} ({frames.Count} frames, {frames.Select(f => f.FocuserPosition).Distinct().Count()} positions)");

            // Build detector params: from the real profile (default) or library defaults (--default-params).
            IProfileService profileService = null;
            StarDetectorParams baseParams;
            if (defaultParams) {
                baseParams = new StarDetectorParams();
                Console.WriteLine("Using default StarDetectorParams (--default-params; no profile loaded)");
            } else {
                if (Application.Current == null) {
                    new Application(); // ProfileService.ActiveProfile setter touches Application.Current.Resources
                }
                var concreteProfileService = new ProfileService();
                concreteProfileService.TryLoad(DiagnosticUtil.GetArg(args, "--profile-id") ?? string.Empty);
                profileService = concreteProfileService;
                if (profileService.ActiveProfile == null) {
                    throw new InvalidOperationException("No active NINA profile. Pass --profile-id, run NINA once, or use --default-params.");
                }
                Console.WriteLine($"Profile: {profileService.ActiveProfile.Name} ({profileService.ActiveProfile.Id})");
                var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
                var accessor = new PluginOptionsAccessor(profileService, guid.Value);
                var options = new StarDetectionOptions(profileService, accessor);
                // F11 step 6: optionally apply a NoiseLevel preset's exact params (for the None/High recalibration
                // measurement) by switching the in-memory options to simple mode at the requested preset with the
                // Typical FocusRange/PixelScale (so no WideRange/LongFL deltas apply). BuildStarDetectorParams then
                // produces that preset's canonical knobs. TestApp never saves the profile, so this does not persist.
                var noiseLevelArg = DiagnosticUtil.GetArg(args, "--noise-level");
                if (!string.IsNullOrWhiteSpace(noiseLevelArg)) {
                    if (!Enum.TryParse<NoiseLevelEnum>(noiseLevelArg, ignoreCase: true, out var noiseLevel)) {
                        throw new ArgumentException($"--noise-level: '{noiseLevelArg}' is not a valid NoiseLevel (None, Low, Typical, High)");
                    }
                    options.UseAdvanced = false;
                    options.Simple_FocusRange = FocusRangeEnum.Typical;
                    options.Simple_PixelScale = PixelScaleEnum.Typical;
                    options.Simple_NoiseLevel = noiseLevel;
                    Console.WriteLine($"Applied NoiseLevel preset: {noiseLevel} (simple mode; BrightnessSensitivity={options.BrightnessSensitivity})");
                }
                baseParams = HocusFocusStarDetection.BuildStarDetectorParams(options);
            }
            // Mirror real AF: PSF modeling is disabled during AutoFocus, so HFR is the measured quantity.
            baseParams.ModelPSF = false;

            // F11 step 6: a single-value override and a sweep for BrightnessSensitivity, applied on top of the
            // built base params (profile-derived or --default-params). The sweep is how the recalibrated knob
            // value is chosen empirically — see docs/f11-meanflux-sensitivity-recalibration-design.md §3.
            var brightnessOverride = DiagnosticUtil.GetArg(args, "--brightness-sensitivity");
            if (!string.IsNullOrWhiteSpace(brightnessOverride)) {
                if (!double.TryParse(brightnessOverride, NumberStyles.Float, CultureInfo.InvariantCulture, out var overrideValue)) {
                    throw new ArgumentException($"--brightness-sensitivity: '{brightnessOverride}' is not a valid number");
                }
                baseParams.Sensitivity = overrideValue;
                Console.WriteLine($"Override: BrightnessSensitivity = {baseParams.Sensitivity}");
            }
            var brightnessSweepArg = DiagnosticUtil.GetArg(args, "--brightness-sensitivity-sweep");
            if (!string.IsNullOrWhiteSpace(brightnessSweepArg)) {
                await RunBrightnessSweep(frames, baseParams, profileService, brightnessSweepArg, outDir);
                return;
            }

            var byPosition = new SortedDictionary<int, PositionAccum>();
            var detector = new StarDetector(new AlglibAPI());
            foreach (var frame in frames) {
                using var img = await DiagnosticUtil.LoadFloatMat(frame.Path, profileService);
                var result = await detector.Detect(img, baseParams, null, CancellationToken.None);
                if (!byPosition.TryGetValue(frame.FocuserPosition, out var accum)) {
                    accum = new PositionAccum { FocuserPosition = frame.FocuserPosition };
                    byPosition[frame.FocuserPosition] = accum;
                }
                Accumulate(accum, result);
                Console.WriteLine($"  pos {frame.FocuserPosition}: {result.DetectedStars.Count} stars");
            }

            var rows = byPosition.Values.ToList();
            WriteCsv(Path.Combine(outDir, "focus_sweep.csv"), rows);
            WriteSummary(Path.Combine(outDir, "focus_sweep_summary.txt"), afRun, baseParams, rows);
            WriteVCurvePng(Path.Combine(outDir, "focus_sweep_hfr.png"), rows);
            Console.WriteLine($"Wrote focus_sweep.csv, focus_sweep_summary.txt, focus_sweep_hfr.png to {outDir}");
        }

        private static async Task RunBrightnessSweep(
            List<Frame> frames, StarDetectorParams baseParams, IProfileService profileService,
            string sweepArg, string outDir) {
            var parts = sweepArg.Split(',');
            if (parts.Length != 3) {
                throw new ArgumentException("--brightness-sensitivity-sweep expects <a,b,step>");
            }
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double step)) {
                throw new ArgumentException("--brightness-sensitivity-sweep: all three values must be valid numbers (e.g. 0.8,2.4,0.1)");
            }
            if (step <= 0) {
                throw new ArgumentException("--brightness-sensitivity-sweep step must be > 0");
            }
            if (a > b) {
                throw new ArgumentException($"--brightness-sensitivity-sweep: start ({a}) must be <= end ({b}); the sweep would be empty");
            }

            var detector = new StarDetector(new AlglibAPI());
            var rows = new List<string> { "sensitivity,FocuserPosition,StarCount,LowSensitivity,MedianHFR" };
            var totals = new List<string> { "sensitivity,TotalStars,TotalLowSensitivity" };
            // Detect only reads its params, so reusing the same (mutable) instance per value is safe.
            for (double s = a; s <= b + 1e-9; s += step) {
                baseParams.Sensitivity = s;
                var byPos = new SortedDictionary<int, (int stars, long lowSens, List<double> hfrs)>();
                long totalStars = 0, totalLow = 0;
                foreach (var frame in frames) {
                    using var img = await DiagnosticUtil.LoadFloatMat(frame.Path, profileService);
                    var result = await detector.Detect(img, baseParams, null, CancellationToken.None);
                    if (!byPos.TryGetValue(frame.FocuserPosition, out var acc)) {
                        acc = (0, 0, new List<double>());
                    }
                    acc.stars += result.DetectedStars.Count;
                    acc.lowSens += result.Metrics.LowSensitivity;
                    foreach (var st in result.DetectedStars) acc.hfrs.Add(st.HFR);
                    byPos[frame.FocuserPosition] = acc;
                    totalStars += result.DetectedStars.Count;
                    totalLow += result.Metrics.LowSensitivity;
                }
                var tag = s.ToString("0.###", CultureInfo.InvariantCulture);
                foreach (var kv in byPos) {
                    var (median, _) = MedianMad(kv.Value.hfrs);
                    rows.Add(string.Join(",", tag, kv.Key, kv.Value.stars, kv.Value.lowSens,
                        median.ToString("0.####", CultureInfo.InvariantCulture)));
                }
                totals.Add(string.Join(",", tag, totalStars, totalLow));
                Console.WriteLine($"BrightnessSensitivity={tag}: {totalStars} stars total, {totalLow} low-sensitivity rejections");
            }
            File.WriteAllLines(Path.Combine(outDir, "brightness_sweep.csv"), rows);
            File.WriteAllLines(Path.Combine(outDir, "brightness_sweep_totals.csv"), totals);
            Console.WriteLine($"Wrote brightness_sweep.csv, brightness_sweep_totals.csv to {outDir}");
        }

        private static List<Frame> ParseAfRun(string dir) {
            var root = new DirectoryInfo(dir);
            var search = root;
            // Accept either an attempt folder directly or a parent with a single attempt* subfolder.
            if (!root.GetFiles().Any(f => ImageFileRegex.IsMatch(Path.GetFileNameWithoutExtension(f.Name)))) {
                var attempt = root.EnumerateDirectories("attempt*").FirstOrDefault();
                if (attempt != null) search = attempt;
            }
            var frames = new List<Frame>();
            foreach (var file in search.GetFiles()) {
                var m = ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups["FOCUSER"].Value, out var pos)) continue;
                frames.Add(new Frame { Path = file.FullName, FocuserPosition = pos });
            }
            return frames;
        }

        private static void Accumulate(PositionAccum a, HocusFocusStarDetectorResult result) {
            a.Frames++;
            var m = result.Metrics;
            a.StructureCandidates += m.StructureCandidates;
            a.TotalDetected += m.TotalDetected;
            a.TooSmall += m.TooSmall;
            a.OnBorder += m.OnBorder;
            a.TooDistorted += m.TooDistorted;
            a.Degenerate += m.Degenerate;
            a.Saturated += m.Saturated;
            a.LowSensitivity += m.LowSensitivity;
            a.NotCentered += m.NotCentered;
            a.TooFlat += m.TooFlat;
            a.TooLowHFR += m.TooLowHFR;
            a.HFRAnalysisFailed += m.HFRAnalysisFailed;
            a.PSFFitFailed += m.PSFFitFailed;
            a.ContaminationSuspected += m.ContaminationSuspected;
            a.OutsideROI += m.OutsideROI;
            a.MeasurementSigmas.Add(result.MeasurementNoiseSigma);
            a.StructureSigmas.Add(result.StructureNoiseSigma);
            foreach (var s in result.DetectedStars) a.Hfrs.Add(s.HFR);
        }

        private static (double median, double mad) MedianMad(List<double> values) {
            if (values.Count == 0) return (double.NaN, double.NaN);
            return values.MedianMAD();
        }

        private static void WriteCsv(string path, List<PositionAccum> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("FocuserPosition,Frames,StarCount,MedianHFR,MAD_HFR,StructureCandidates,TotalDetected," +
                "TooSmall,OnBorder,TooDistorted,Degenerate,Saturated,LowSensitivity,NotCentered,TooFlat," +
                "TooLowHFR,HFRAnalysisFailed,PSFFitFailed,ContaminationSuspected,OutsideROI," +
                "MedianMeasurementSigma,MedianStructureSigma");
            foreach (var r in rows) {
                var (median, mad) = MedianMad(r.Hfrs);
                var (mSig, _) = MedianMad(r.MeasurementSigmas);
                var (sSig, _) = MedianMad(r.StructureSigmas);
                sb.AppendLine(string.Join(",",
                    r.FocuserPosition, r.Frames, r.Hfrs.Count, F(median), F(mad),
                    r.StructureCandidates, r.TotalDetected, r.TooSmall, r.OnBorder, r.TooDistorted, r.Degenerate,
                    r.Saturated, r.LowSensitivity, r.NotCentered, r.TooFlat, r.TooLowHFR, r.HFRAnalysisFailed,
                    r.PSFFitFailed, r.ContaminationSuspected, r.OutsideROI, F(mSig), F(sSig)));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, string afRun, StarDetectorParams p, List<PositionAccum> rows) {
            var sb = new StringBuilder();
            sb.AppendLine($"AF run: {afRun}");
            sb.AppendLine($"Params: {p}");
            sb.AppendLine($"Positions: {rows.Count}");
            sb.AppendLine($"Total frames: {rows.Sum(r => r.Frames)}");
            var withStars = rows.Where(r => r.Hfrs.Count > 0).ToList();
            if (withStars.Count > 0) {
                var best = withStars.OrderBy(r => MedianMad(r.Hfrs).median).First();
                sb.AppendLine($"Min median-HFR position (detector's-eye focus estimate): {best.FocuserPosition} " +
                    $"(median HFR {F(MedianMad(best.Hfrs).median)}, {best.Hfrs.Count} stars)");
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteVCurvePng(string path, List<PositionAccum> rows) {
            try {
                var data = rows.Where(r => r.Hfrs.Count > 0)
                    .Select(r => (x: (double)r.FocuserPosition, y: MedianMad(r.Hfrs)))
                    .OrderBy(t => t.x).ToList();
                if (data.Count == 0) {
                    Logger.Warning("V-curve PNG skipped: no positions had detected stars");
                    return;
                }
                var xs = data.Select(d => d.x).ToArray();
                var ys = data.Select(d => d.y.median).ToArray();
                var err = data.Select(d => double.IsNaN(d.y.mad) ? 0.0 : d.y.mad).ToArray();
                var plt = new ScottPlot.Plot(900, 600);
                plt.AddScatter(xs, ys, markerSize: 6);
                plt.AddErrorBars(xs, ys, null, err);
                plt.XLabel("Focuser Position");
                plt.YLabel("Median HFR (px)");
                plt.Title("Focus Sweep — detector HFR vs position");
                plt.SaveFig(path);
            } catch (Exception ex) {
                Logger.Warning($"V-curve PNG render failed ({ex.Message}); CSV/summary still written");
            }
        }

        private static string F(double v) => double.IsNaN(v) ? "NaN" : v.ToString("G9", CultureInfo.InvariantCulture);

        // Writes `count` synthetic 16-bit TIFF frames named with the saved-AF convention, with a V-shaped HFR
        // (sharp in the middle, defocused at the ends). Lets the diagnostic be exercised end-to-end with no
        // real data. Self-contained (cannot reference the Tests project's generators).
        private static void SynthesizeAfRun(string dir, int count) {
            Directory.CreateDirectory(dir);
            const int frameSize = 128;
            const int basePos = 10000, stepPos = 100;
            int center = count / 2;
            for (int i = 0; i < count; ++i) {
                int pos = basePos + i * stepPos;
                // V-shaped width, kept gentle (≤ ~4px σ) and bright so stars stay detectable at the ends and
                // the median HFR forms a clean V instead of the defocused frames dropping out entirely.
                double sigma = 2.0 + 0.5 * Math.Abs(i - center);
                using var f = SynthFrame(frameSize, frameSize, sigma, peak: 0.85, background: 0.02);
                using var u16 = new Mat();
                f.ConvertTo(u16, MatType.CV_16U, ushort.MaxValue);
                var name = $"{i:00}_Frame00_BitDepth16_Bayered0_Focuser{pos}.tif";
                Cv2.ImWrite(Path.Combine(dir, name), u16);
            }
        }

        // A 3x3 grid of Gaussian stars on a flat background.
        private static Mat SynthFrame(int w, int h, double sigma, double peak, double background) {
            var mat = new Mat(new Size(w, h), MatType.CV_32F, new Scalar(background));
            for (int gy = 1; gy <= 3; ++gy) {
                for (int gx = 1; gx <= 3; ++gx) {
                    AddGaussian(mat, w * gx / 4.0, h * gy / 4.0, sigma, peak);
                }
            }
            return mat;
        }

        private static void AddGaussian(Mat mat, double cx, double cy, double sigma, double peak) {
            int w = mat.Width, h = mat.Height;
            double inv = 1.0 / (2.0 * sigma * sigma);
            int rad = (int)Math.Ceiling(5.0 * sigma);
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = Math.Max(0, (int)(cy - rad)); y < Math.Min(h, (int)(cy + rad)); ++y) {
                    for (int x = Math.Max(0, (int)(cx - rad)); x < Math.Min(w, (int)(cx + rad)); ++x) {
                        double dx = x - cx, dy = y - cy;
                        data[y * w + x] += (float)(peak * Math.Exp(-(dx * dx + dy * dy) * inv));
                    }
                }
            }
        }
    }
}
