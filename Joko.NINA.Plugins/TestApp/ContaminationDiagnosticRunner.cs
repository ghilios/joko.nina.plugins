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
using NINA.Image.FileFormat.FITS;
using NINA.Image.FileFormat.XISF;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Headless diagnostic harness for the sector-annulus contamination test. Loads an image file, runs
    /// HocusFocus star detection using the user's real NINA profile settings, and writes per-star
    /// contamination diagnostics (CSV + annotated PNG + summary text) so the test can be re-tuned offline.
    /// </summary>
    internal static class ContaminationDiagnosticRunner {
        private const int NumSectors = 8;

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Contamination diagnostic run failed");
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunImpl(string[] args) {
            var imagePath = GetArg(args, "--image");
            if (string.IsNullOrWhiteSpace(imagePath)) {
                Console.Error.WriteLine("Usage: TestApp contamination --image <path> [--profile-id <guid>] [--out <dir>] [--sensitivity <double>] [--sensitivity-sweep <a,b,step>]");
                Environment.ExitCode = 2;
                return;
            }
            if (!File.Exists(imagePath)) {
                throw new FileNotFoundException($"Image not found: {imagePath}", imagePath);
            }

            var profileId = GetArg(args, "--profile-id");
            var outDir = GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            double? sensitivityOverride = null;
            var sensitivityArg = GetArg(args, "--sensitivity");
            if (!string.IsNullOrWhiteSpace(sensitivityArg)) {
                sensitivityOverride = double.Parse(sensitivityArg, CultureInfo.InvariantCulture);
            }

            // Verbose logging to the NINA log file for offline inspection
            Logger.SetLogLevel(LogLevelEnum.TRACE);

            // The real ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a
            // (non-running) WPF Application must exist or it NREs.
            if (Application.Current == null) {
                new Application();
            }

            Console.WriteLine($"Image: {imagePath}");
            Console.WriteLine($"Output: {outDir}");

            // Load the user's real profile + star detection settings
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile;
            if (activeProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA at least once to create a profile.");
            }
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");
            Logger.Info($"Loaded profile {activeProfile.Name} ({activeProfile.Id})");

            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new InvalidOperationException("Could not resolve the HocusFocus plugin assembly GUID");
            }
            var accessor = new PluginOptionsAccessor(profileService, guid.Value);
            var options = new StarDetectionOptions(profileService, accessor);
            LogResolvedOptions(options);

            // Build params from the SAME mapping NINA uses (single source of truth), enable diagnostics
            var baseParams = HocusFocusStarDetection.BuildStarDetectorParams(options);
            baseParams.CollectContaminationDiagnostics = true;
            if (sensitivityOverride.HasValue) {
                Console.WriteLine($"Overriding ContaminationSensitivity {baseParams.ContaminationSensitivity} -> {sensitivityOverride.Value}");
                baseParams.ContaminationSensitivity = sensitivityOverride.Value;
            }

            // Load the original image once (CV_32F, normalized [0,1]). Detection mutates its input in place,
            // so each run gets a clone and the original is kept for the annotated background.
            using var srcFloat = await LoadFloatMat(imagePath, profileService);
            Console.WriteLine($"Image dimensions: {srcFloat.Width} x {srcFloat.Height}");
            Logger.Info($"Loaded image {srcFloat.Width}x{srcFloat.Height} from {imagePath}");

            var sweepArg = GetArg(args, "--sensitivity-sweep");
            if (!string.IsNullOrWhiteSpace(sweepArg)) {
                await RunSweep(srcFloat, baseParams, sweepArg, outDir);
                return;
            }

            // Single run
            var result = await DetectClone(srcFloat, baseParams);
            var diagnostics = result.ContaminationDiagnostics ?? new List<ContaminationDiagnosticRecord>();
            int total = result.DetectedStars.Count;
            int suspected = result.Metrics.ContaminationSuspected;
            Console.WriteLine($"Detected {total}, {suspected} contamination-suspected ({Percent(suspected, total)})");

            WriteCsv(Path.Combine(outDir, "contamination_stars.csv"), diagnostics);
            WriteSummary(Path.Combine(outDir, "contamination_summary.txt"), imagePath, srcFloat, baseParams, total, suspected, diagnostics);
            WriteAnnotated(Path.Combine(outDir, "contamination_annotated.png"), srcFloat, diagnostics);
            Console.WriteLine($"Wrote contamination_stars.csv, contamination_summary.txt, contamination_annotated.png to {outDir}");
        }

        private static async Task RunSweep(Mat srcFloat, StarDetectorParams baseParams, string sweepArg, string outDir) {
            var parts = sweepArg.Split(',');
            if (parts.Length != 3) {
                throw new ArgumentException("--sensitivity-sweep expects <a,b,step>");
            }
            double a = double.Parse(parts[0], CultureInfo.InvariantCulture);
            double b = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double step = double.Parse(parts[2], CultureInfo.InvariantCulture);
            if (step <= 0) {
                throw new ArgumentException("--sensitivity-sweep step must be > 0");
            }

            var sweepRows = new List<string> { "sensitivity,suspected,total,percent" };
            // Detect only reads its params, so reusing the same (mutable) instance per value is safe.
            for (double s = a; s <= b + 1e-9; s += step) {
                baseParams.ContaminationSensitivity = s;
                var result = await DetectClone(srcFloat, baseParams);
                var diagnostics = result.ContaminationDiagnostics ?? new List<ContaminationDiagnosticRecord>();
                int total = result.DetectedStars.Count;
                int suspected = result.Metrics.ContaminationSuspected;
                var tag = s.ToString("0.###", CultureInfo.InvariantCulture);
                Console.WriteLine($"sensitivity={tag}: detected {total}, {suspected} suspected ({Percent(suspected, total)})");
                Logger.Info($"Sweep sensitivity={tag}: detected {total}, suspected {suspected}");
                WriteCsv(Path.Combine(outDir, $"contamination_stars_{tag}.csv"), diagnostics);
                sweepRows.Add(string.Join(",", tag, suspected, total, PercentValue(suspected, total).ToString("0.##", CultureInfo.InvariantCulture)));
            }
            File.WriteAllLines(Path.Combine(outDir, "sweep.csv"), sweepRows);
            Console.WriteLine($"Wrote sweep.csv (+ per-value CSVs) to {outDir}");
        }

        private static async Task<HocusFocusStarDetectorResult> DetectClone(Mat srcFloat, StarDetectorParams p) {
            using var clone = srcFloat.Clone();
            var detector = new StarDetector(new AlglibAPI());
            return await detector.Detect(clone, p, null, CancellationToken.None);
        }

        private static async Task<Mat> LoadFloatMat(string path, IProfileService profileService) {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff") {
                using var src = new Mat(path, ImreadModes.Unchanged);
                var dst = new Mat();
                Program.ConvertToFloat(src, dst);
                return dst;
            }
            if (ext == ".xisf" || ext == ".fits" || ext == ".fit") {
                var factory = new ImageDataFactory(profileService, new StubBehaviorSelector<IStarDetection>(new StubStarDetection()), new StubBehaviorSelector<IStarAnnotator>());
                var uri = new Uri(Path.GetFullPath(path));
                IImageData imageData = ext == ".xisf"
                    ? await XISF.Load(uri, false, factory, CancellationToken.None)
                    : await FITS.Load(uri, false, factory, CancellationToken.None);
                return CvImageUtility.ToOpenCVMat(imageData);
            }
            throw new NotSupportedException($"Unsupported image extension '{ext}'. Supported: .tif/.tiff, .xisf, .fits/.fit");
        }

        private static void LogResolvedOptions(StarDetectionOptions o) {
            // Proves the real profile was read (e.g. ContaminationSensitivity differs from the 4.0 default)
            Console.WriteLine($"ContaminationSensitivity (resolved): {o.ContaminationSensitivity.ToString(CultureInfo.InvariantCulture)}");
            Logger.Info($"Resolved star-detection options: ContaminationSensitivity={o.ContaminationSensitivity}, " +
                $"NoiseClippingMultiplier={o.NoiseClippingMultiplier}, StarClippingMultiplier={o.StarClippingMultiplier}, " +
                $"BrightnessSensitivity={o.BrightnessSensitivity}, StarPeakResponse={o.StarPeakResponse}, " +
                $"NoiseReductionRadius={o.NoiseReductionRadius}, StructureLayers={o.StructureLayers}, " +
                $"MinHFR={o.MinHFR}, MinStarBoundingBoxSize={o.MinStarBoundingBoxSize}, ModelPSF={o.ModelPSF}, PSFFitType={o.PSFFitType}");
        }

        private static void WriteCsv(string path, List<ContaminationDiagnosticRecord> diagnostics) {
            var sb = new StringBuilder();
            var header = new List<string> { "CenterX", "CenterY", "Hfr", "Background", "NoiseSigma", "Sensitivity", "MinSectorPixels" };
            for (int i = 0; i < NumSectors; ++i) header.Add($"m{i}");
            for (int i = 0; i < NumSectors; ++i) header.Add($"c{i}");
            for (int i = 0; i < NumSectors / 2; ++i) header.Add($"diff{i}");
            for (int i = 0; i < NumSectors / 2; ++i) header.Add($"thr{i}");
            for (int i = 0; i < NumSectors / 2; ++i) header.Add($"ratio{i}");
            for (int i = 0; i < NumSectors / 2; ++i) header.Add($"skip{i}");
            header.Add("TrippingPairIndex");
            header.Add("ContaminationSuspected");
            header.Add("MaxRatio");
            sb.AppendLine(string.Join(",", header));

            foreach (var r in diagnostics) {
                var row = new List<string> {
                    F(r.CenterX), F(r.CenterY), F(r.Hfr), F(r.Background), F(r.NoiseSigma), F(r.Sensitivity), r.MinSectorPixels.ToString(CultureInfo.InvariantCulture)
                };
                for (int i = 0; i < NumSectors; ++i) row.Add(F(r.SectorMedians[i]));
                for (int i = 0; i < NumSectors; ++i) row.Add(r.SectorCounts[i].ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < NumSectors / 2; ++i) row.Add(F(r.PairDiff[i]));
                for (int i = 0; i < NumSectors / 2; ++i) row.Add(F(r.PairThreshold[i]));
                for (int i = 0; i < NumSectors / 2; ++i) row.Add(F(r.PairRatio[i]));
                for (int i = 0; i < NumSectors / 2; ++i) row.Add(r.PairSkipped[i] ? "1" : "0");
                row.Add(r.TrippingPairIndex.ToString(CultureInfo.InvariantCulture));
                row.Add(r.ContaminationSuspected ? "1" : "0");
                row.Add(F(MaxRatio(r)));
                sb.AppendLine(string.Join(",", row));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, string imagePath, Mat srcFloat, StarDetectorParams p, int total, int suspected, List<ContaminationDiagnosticRecord> diagnostics) {
            var sb = new StringBuilder();
            sb.AppendLine($"Image: {imagePath}");
            sb.AppendLine($"Dimensions: {srcFloat.Width} x {srcFloat.Height}");
            sb.AppendLine($"Params: {p}");
            sb.AppendLine($"ContaminationSensitivity: {p.ContaminationSensitivity.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Total detected: {total}");
            sb.AppendLine($"Contamination suspected: {suspected} ({Percent(suspected, total)})");

            var maxRatios = diagnostics.Select(MaxRatio).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (maxRatios.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("MaxRatio distribution (over accepted stars with at least one evaluated sector pair):");
                sb.AppendLine($"  count = {maxRatios.Count}");
                sb.AppendLine($"  min    = {F(maxRatios.First())}");
                sb.AppendLine($"  median = {F(Percentile(maxRatios, 0.50))}");
                sb.AppendLine($"  p90    = {F(Percentile(maxRatios, 0.90))}");
                sb.AppendLine($"  max    = {F(maxRatios.Last())}");
                sb.AppendLine();
                sb.AppendLine("  buckets:");
                sb.AppendLine($"    <1.0       : {maxRatios.Count(x => x < 1.0)}");
                sb.AppendLine($"    1.0 - 1.25 : {maxRatios.Count(x => x >= 1.0 && x < 1.25)}");
                sb.AppendLine($"    1.25 - 1.5 : {maxRatios.Count(x => x >= 1.25 && x < 1.5)}");
                sb.AppendLine($"    1.5 - 2.0  : {maxRatios.Count(x => x >= 1.5 && x < 2.0)}");
                sb.AppendLine($"    >= 2.0     : {maxRatios.Count(x => x >= 2.0)}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteAnnotated(string path, Mat srcFloat, List<ContaminationDiagnosticRecord> diagnostics) {
            // MTF-stretch a 16-bit copy for a viewable background, then draw star markers colored by status.
            using var src16 = new Mat();
            srcFloat.ConvertTo(src16, MatType.CV_16U, ushort.MaxValue);
            using var stretched16 = new Mat();
            try {
                var stats = CvImageUtility.CalculateStatistics_Histogram(src16);
                using var lut = CvImageUtility.CreateMTFLookup(stats);
                CvImageUtility.ApplyLUT(src16, lut, stretched16);
            } catch (Exception ex) {
                Logger.Warning($"MTF stretch failed ({ex.Message}); falling back to linear normalization for the annotated image");
                Cv2.Normalize(src16, stretched16, 0, ushort.MaxValue, NormTypes.MinMax);
            }
            using var stretched8 = new Mat();
            stretched16.ConvertTo(stretched8, MatType.CV_8U, 1.0 / 256.0);
            using var bgr = new Mat();
            Cv2.CvtColor(stretched8, bgr, ColorConversionCodes.GRAY2BGR);

            var green = new Scalar(0, 255, 0);     // accepted, clean
            var magenta = new Scalar(255, 0, 255); // contamination suspected (BGR)
            foreach (var r in diagnostics) {
                var color = r.ContaminationSuspected ? magenta : green;
                Cv2.Circle(bgr, new OpenCvSharp.Point((int)Math.Round(r.CenterX), (int)Math.Round(r.CenterY)), 8, color, 1, LineTypes.AntiAlias);
            }
            Cv2.ImWrite(path, bgr);
        }

        private static double MaxRatio(ContaminationDiagnosticRecord r) {
            double max = double.NaN;
            for (int i = 0; i < r.PairRatio.Length; ++i) {
                var v = r.PairRatio[i];
                if (double.IsNaN(v)) continue;
                if (double.IsNaN(max) || v > max) max = v;
            }
            return max;
        }

        private static double Percentile(List<double> sorted, double q) {
            if (sorted.Count == 0) return double.NaN;
            if (sorted.Count == 1) return sorted[0];
            double idx = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            double frac = idx - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }

        private static string GetArg(string[] args, string name) {
            for (int i = 0; i < args.Length - 1; ++i) {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static string F(double v) {
            if (double.IsNaN(v)) return "NaN";
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static double PercentValue(int n, int total) => total > 0 ? 100.0 * n / total : 0.0;

        private static string Percent(int n, int total) => $"{PercentValue(n, total).ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}
