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
using KdTree;
using KdTree.Math;
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
            var imagePath = DiagnosticUtil.GetArg(args, "--image");
            if (string.IsNullOrWhiteSpace(imagePath)) {
                Console.Error.WriteLine("Usage: TestApp contamination --image <path> [--profile-id <guid>] [--out <dir>] [--sensitivity <double>] [--sensitivity-sweep <a,b,step>]");
                Environment.ExitCode = 2;
                return;
            }
            if (!File.Exists(imagePath)) {
                throw new FileNotFoundException($"Image not found: {imagePath}", imagePath);
            }

            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            double? sensitivityOverride = null;
            var sensitivityArg = DiagnosticUtil.GetArg(args, "--sensitivity");
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
            // Keep contaminated stars in the result so they can be analyzed here (production would reject them
            // when RejectContaminatedStars is on). The per-star ContaminationSuspected flag still records the
            // decision either way.
            baseParams.RejectContaminatedStars = false;
            // Force PSF modeling on so per-star shape metrics (eccentricity/FWHM) are always available. PSF
            // fitting happens after the contamination decision, so this only adds shape data.
            if (!baseParams.ModelPSF) {
                Console.WriteLine("Forcing ModelPSF on for shape diagnostics (does not affect contamination decisions)");
                Logger.Info("Diagnostic: forced ModelPSF=true to capture eccentricity/FWHM");
                baseParams.ModelPSF = true;
            }
            if (sensitivityOverride.HasValue) {
                Console.WriteLine($"Overriding ContaminationSensitivity {baseParams.ContaminationSensitivity} -> {sensitivityOverride.Value}");
                baseParams.ContaminationSensitivity = sensitivityOverride.Value;
            }

            // Opt-in defocus-aware distortion test switch (for the near-focus precision check). Flips the flag ON
            // and optionally overrides the two tuning knobs; prints accepted + TooDistorted before/after so the
            // gate's effect on a single frame is visible without labels.
            if (DiagnosticUtil.HasFlag(args, "--defocus-distortion")) {
                baseParams.DefocusAwareDistortion = true;
                var sizeRefArg = DiagnosticUtil.GetArg(args, "--defocus-size-ref");
                if (!string.IsNullOrWhiteSpace(sizeRefArg) && double.TryParse(sizeRefArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizeRef)) {
                    baseParams.DefocusDistortionSizeReference = sizeRef;
                }
                var minFactorArg = DiagnosticUtil.GetArg(args, "--defocus-min-factor");
                if (!string.IsNullOrWhiteSpace(minFactorArg) && double.TryParse(minFactorArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var minFactor)) {
                    baseParams.DefocusDistortionMinFactor = minFactor;
                }
                Console.WriteLine($"DefocusAwareDistortion=ON (SizeReference={baseParams.DefocusDistortionSizeReference.ToString(CultureInfo.InvariantCulture)} px, MinFactor={baseParams.DefocusDistortionMinFactor.ToString(CultureInfo.InvariantCulture)})");
            } else {
                Console.WriteLine("DefocusAwareDistortion=OFF");
            }

            // Companion defocus-aware CENTERING test switch (relaxes the NotCentered gate for large/defocused
            // candidates). Shares the size reference with the distortion gate; optional --defocus-center-factor
            // overrides the max centering-tolerance multiplier.
            if (DiagnosticUtil.HasFlag(args, "--defocus-centering")) {
                baseParams.DefocusAwareCentering = true;
                var sizeRefArg = DiagnosticUtil.GetArg(args, "--defocus-size-ref");
                if (!string.IsNullOrWhiteSpace(sizeRefArg) && double.TryParse(sizeRefArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizeRef)) {
                    baseParams.DefocusDistortionSizeReference = sizeRef;
                }
                var centerFactorArg = DiagnosticUtil.GetArg(args, "--defocus-center-factor");
                if (!string.IsNullOrWhiteSpace(centerFactorArg) && double.TryParse(centerFactorArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var centerFactor)) {
                    baseParams.DefocusCenteringToleranceFactor = centerFactor;
                }
                Console.WriteLine($"DefocusAwareCentering=ON (SizeReference={baseParams.DefocusDistortionSizeReference.ToString(CultureInfo.InvariantCulture)} px, ToleranceFactor={baseParams.DefocusCenteringToleranceFactor.ToString(CultureInfo.InvariantCulture)})");
            } else {
                Console.WriteLine("DefocusAwareCentering=OFF");
            }

            // Load the original image once (CV_32F, normalized [0,1]). Detection mutates its input in place,
            // so each run gets a clone and the original is kept for the annotated background.
            using var srcFloat = await DiagnosticUtil.LoadFloatMat(imagePath, profileService);
            Console.WriteLine($"Image dimensions: {srcFloat.Width} x {srcFloat.Height}");
            Logger.Info($"Loaded image {srcFloat.Width}x{srcFloat.Height} from {imagePath}");

            var sweepArg = DiagnosticUtil.GetArg(args, "--sensitivity-sweep");
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
            Console.WriteLine($"PRECISION-CHECK accepted={total}, TooDistorted={result.Metrics.TooDistorted}, NotCentered={result.Metrics.NotCentered}, TooSmall={result.Metrics.TooSmall}");

            var shapes = BuildShapeLookup(result, diagnostics);
            WriteCsv(Path.Combine(outDir, "contamination_stars.csv"), diagnostics, shapes);
            WriteSummary(Path.Combine(outDir, "contamination_summary.txt"), imagePath, srcFloat, baseParams, total, suspected, diagnostics, shapes);
            WriteAnnotated(Path.Combine(outDir, "contamination_annotated.png"), srcFloat, diagnostics, shapes);
            WriteGradientRobustSweep(Path.Combine(outDir, "gr_sweep.csv"), diagnostics, shapes);
            Console.WriteLine($"Wrote contamination_stars.csv, contamination_summary.txt, contamination_annotated.png, gr_sweep.csv to {outDir}");
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
                var sweepShapes = BuildShapeLookup(result, diagnostics);
                WriteCsv(Path.Combine(outDir, $"contamination_stars_{tag}.csv"), diagnostics, sweepShapes);
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

        private static void LogResolvedOptions(StarDetectionOptions o) {
            // Proves the real profile was read (e.g. ContaminationSensitivity differs from the 4.0 default)
            Console.WriteLine($"ContaminationSensitivity (resolved): {o.ContaminationSensitivity.ToString(CultureInfo.InvariantCulture)}");
            Logger.Info($"Resolved star-detection options: ContaminationSensitivity={o.ContaminationSensitivity}, " +
                $"NoiseClippingMultiplier={o.NoiseClippingMultiplier}, StarClippingMultiplier={o.StarClippingMultiplier}, " +
                $"BrightnessSensitivity={o.BrightnessSensitivity}, StarPeakResponse={o.StarPeakResponse}, " +
                $"NoiseReductionRadius={o.NoiseReductionRadius}, StructureLayers={o.StructureLayers}, " +
                $"MinHFR={o.MinHFR}, MinStarBoundingBoxSize={o.MinStarBoundingBoxSize}, ModelPSF={o.ModelPSF}, PSFFitType={o.PSFFitType}");
        }

        // A flagged star is considered to actually have a "close" neighbor when its nearest detected
        // neighbor sits within this many HFR of its center. Beyond this, it is treated as isolated.
        private const double CloseNeighborHfrFactor = 3.0;

        /// <summary>
        /// Per-star shape + proximity data joined to each contamination record. Lets the diagnostic test the
        /// "flagged stars are eccentric/blurry, not next to a neighbor" hypothesis. Eccentricity/FWHM come
        /// from the PSF fit (NaN when no fit); NearestNeighborDist is to the closest OTHER detected star.
        /// </summary>
        private sealed class ShapeInfo {
            public double Eccentricity = double.NaN;
            public double FWHMx = double.NaN;
            public double FWHMy = double.NaN;
            public double FWHMPixels = double.NaN;
            public double ThetaDeg = double.NaN;
            public bool PsfFitOk = false;
            public double NearestNeighborDist = double.NaN;
            public double NearestNeighborOverHfr = double.NaN;
            // Brightness + footprint, for the donut HFR-vs-brightness analysis.
            public double PeakBrightness = double.NaN;
            public double MeanBrightness = double.NaN;
            public double BBoxW = double.NaN;
            public double BBoxH = double.NaN;
            public bool HasCloseNeighbor =>
                !double.IsNaN(NearestNeighborOverHfr) && NearestNeighborOverHfr < CloseNeighborHfrFactor;
        }

        /// <summary>
        /// Joins each contamination record (re-sorted from a ConcurrentBag, so NOT index-aligned with
        /// DetectedStars) to its star's PSF shape metrics by matching on center coordinates, and computes
        /// each star's nearest-detected-neighbor distance via a 2-D Kd-tree.
        /// </summary>
        private static Dictionary<ContaminationDiagnosticRecord, ShapeInfo> BuildShapeLookup(
                HocusFocusStarDetectorResult result, List<ContaminationDiagnosticRecord> diagnostics) {
            var lookup = new Dictionary<ContaminationDiagnosticRecord, ShapeInfo>();
            var stars = result.DetectedStars ?? new List<Star>();

            // Center -> Star map for the record->star join. record.CenterX/Y is assigned directly from
            // star.Center (no ROI in the diagnostic path), so a rounded-coordinate key matches exactly.
            var byCenter = new Dictionary<(long, long), Star>();
            foreach (var s in stars) {
                byCenter[CenterKey(s.Center.X, s.Center.Y)] = s;
            }

            // Kd-tree of all detected centers for nearest-neighbor distance.
            var tree = new KdTree<double, Star>(2, new DoubleMath(), AddDuplicateBehavior.Skip);
            foreach (var s in stars) {
                tree.Add(new[] { s.Center.X, s.Center.Y }, s);
            }
            var nnByCenter = new Dictionary<(long, long), double>();
            foreach (var s in stars) {
                var key = CenterKey(s.Center.X, s.Center.Y);
                if (nnByCenter.ContainsKey(key)) continue;
                var neighbors = tree.GetNearestNeighbours(new[] { s.Center.X, s.Center.Y }, 2);
                double nn = double.NaN;
                if (neighbors.Length >= 2) {
                    var p = neighbors[1].Point; // [0] is the star itself
                    nn = Math.Sqrt((s.Center.X - p[0]) * (s.Center.X - p[0]) + (s.Center.Y - p[1]) * (s.Center.Y - p[1]));
                }
                nnByCenter[key] = nn;
            }

            foreach (var r in diagnostics) {
                var info = new ShapeInfo();
                var key = CenterKey(r.CenterX, r.CenterY);
                if (byCenter.TryGetValue(key, out var star)) {
                    info.PeakBrightness = star.PeakBrightness;
                    info.MeanBrightness = star.MeanBrightness;
                    info.BBoxW = star.StarBoundingBox.Width;
                    info.BBoxH = star.StarBoundingBox.Height;
                    if (star.PSF != null) {
                        info.PsfFitOk = true;
                        info.Eccentricity = star.PSF.Eccentricity;
                        info.FWHMx = star.PSF.FWHMx;
                        info.FWHMy = star.PSF.FWHMy;
                        info.FWHMPixels = star.PSF.FWHMPixels;
                        info.ThetaDeg = star.PSF.ThetaRadians * 180.0 / Math.PI;
                    }
                    if (nnByCenter.TryGetValue(key, out var nn)) {
                        info.NearestNeighborDist = nn;
                        if (!double.IsNaN(nn) && r.Hfr > 0) {
                            info.NearestNeighborOverHfr = nn / r.Hfr;
                        }
                    }
                }
                lookup[r] = info;
            }
            return lookup;
        }

        // Round center coords to 0.01 px for an exact-match key between records and stars.
        private static (long, long) CenterKey(double x, double y) =>
            ((long)Math.Round(x * 100.0), (long)Math.Round(y * 100.0));

        private static void WriteCsv(string path, List<ContaminationDiagnosticRecord> diagnostics,
                Dictionary<ContaminationDiagnosticRecord, ShapeInfo> shapes) {
            var sb = new StringBuilder();
            var header = new List<string> {
                "CenterX", "CenterY", "Hfr", "Background", "NoiseSigma", "Sensitivity", "MinSectorPixels",
                "ContaminationSuspected",
                "GradientSlope", "LocalSigmaResidual", "MaxSectorResidualOverSE", "ResidualTrippingSector",
            };
            for (int i = 0; i < NumSectors; ++i) header.Add($"resid{i}");
            for (int i = 0; i < NumSectors; ++i) header.Add($"residCount{i}");
            header.Add("Eccentricity");
            header.Add("FWHMx");
            header.Add("FWHMy");
            header.Add("FWHMPixels");
            header.Add("ThetaDeg");
            header.Add("NearestNeighborDist");
            header.Add("NearestNeighborOverHfr");
            header.Add("HasCloseNeighbor");
            header.Add("PsfFitOk");
            header.Add("PeakBrightness");
            header.Add("MeanBrightness");
            header.Add("BBoxW");
            header.Add("BBoxH");
            sb.AppendLine(string.Join(",", header));

            foreach (var r in diagnostics) {
                var info = shapes != null && shapes.TryGetValue(r, out var s) ? s : new ShapeInfo();
                var row = new List<string> {
                    F(r.CenterX), F(r.CenterY), F(r.Hfr), F(r.Background), F(r.NoiseSigma), F(r.Sensitivity), r.MinSectorPixels.ToString(CultureInfo.InvariantCulture),
                    r.ContaminationSuspected ? "1" : "0",
                    F(r.GradientSlope), F(r.LocalSigmaResidual), F(r.MaxSectorResidualOverSE), r.ResidualTrippingSector.ToString(CultureInfo.InvariantCulture),
                };
                for (int i = 0; i < NumSectors; ++i) row.Add(r.SectorResidualMedian != null ? F(r.SectorResidualMedian[i]) : "NaN");
                for (int i = 0; i < NumSectors; ++i) row.Add(r.SectorResidualCount != null ? r.SectorResidualCount[i].ToString(CultureInfo.InvariantCulture) : "0");
                row.Add(F(info.Eccentricity));
                row.Add(F(info.FWHMx));
                row.Add(F(info.FWHMy));
                row.Add(F(info.FWHMPixels));
                row.Add(F(info.ThetaDeg));
                row.Add(F(info.NearestNeighborDist));
                row.Add(F(info.NearestNeighborOverHfr));
                row.Add(info.HasCloseNeighbor ? "1" : "0");
                row.Add(info.PsfFitOk ? "1" : "0");
                row.Add(F(info.PeakBrightness));
                row.Add(F(info.MeanBrightness));
                row.Add(F(info.BBoxW));
                row.Add(F(info.BBoxH));
                sb.AppendLine(string.Join(",", row));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, string imagePath, Mat srcFloat, StarDetectorParams p, int total, int suspected, List<ContaminationDiagnosticRecord> diagnostics,
                Dictionary<ContaminationDiagnosticRecord, ShapeInfo> shapes) {
            var sb = new StringBuilder();
            sb.AppendLine($"Image: {imagePath}");
            sb.AppendLine($"Dimensions: {srcFloat.Width} x {srcFloat.Height}");
            sb.AppendLine($"Params: {p}");
            sb.AppendLine($"ContaminationSensitivity: {p.ContaminationSensitivity.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Total detected: {total}");
            sb.AppendLine($"Contamination suspected: {suspected} ({Percent(suspected, total)})");

            WriteHypothesisComparison(sb, diagnostics, shapes);

            var stats = diagnostics.Select(r => r.MaxSectorResidualOverSE).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (stats.Count > 0) {
                sb.AppendLine();
                sb.AppendLine("MaxSectorResidualOverSE distribution (gradient-robust test statistic; flag when > sensitivity):");
                sb.AppendLine($"  count = {stats.Count}");
                sb.AppendLine($"  min    = {F(stats.First())}");
                sb.AppendLine($"  median = {F(Percentile(stats, 0.50))}");
                sb.AppendLine($"  p90    = {F(Percentile(stats, 0.90))}");
                sb.AppendLine($"  max    = {F(stats.Last())}");
            }
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// The decisive test of the user's hypothesis: compares the flagged (ContaminationSuspected) group
        /// against the clean group on eccentricity, HFR, and nearest-neighbor proximity. If flagged stars are
        /// more eccentric/blurry yet NO closer to neighbors than clean stars (and most are isolated), the
        /// observation "flagged stars are eccentric, not next to a neighbor" is confirmed.
        /// </summary>
        private static void WriteHypothesisComparison(StringBuilder sb, List<ContaminationDiagnosticRecord> diagnostics,
                Dictionary<ContaminationDiagnosticRecord, ShapeInfo> shapes) {
            if (shapes == null) return;
            var flagged = diagnostics.Where(r => r.ContaminationSuspected).ToList();
            var clean = diagnostics.Where(r => !r.ContaminationSuspected).ToList();

            sb.AppendLine();
            sb.AppendLine("=== Hypothesis: flagged stars are eccentric/blurry, NOT next to a neighbor ===");
            sb.AppendLine($"  flagged = {flagged.Count}, clean = {clean.Count}");

            int noPsf = flagged.Count(r => !(shapes.TryGetValue(r, out var s) && s.PsfFitOk));
            sb.AppendLine($"  flagged stars with no PSF fit (eccentricity unavailable): {noPsf}");

            sb.AppendLine();
            sb.AppendLine("  metric                  flagged(median / p90)      clean(median / p90)");
            AppendMetricLine(sb, "Eccentricity", flagged, clean, shapes, s => s.Eccentricity);
            AppendMetricLine(sb, "HFR", flagged, clean, shapes, _ => double.NaN, r => r.Hfr);
            AppendMetricLine(sb, "FWHMPixels", flagged, clean, shapes, s => s.FWHMPixels);
            AppendMetricLine(sb, "NearestNeighborDist", flagged, clean, shapes, s => s.NearestNeighborDist);
            AppendMetricLine(sb, "NearestNeighborOverHfr", flagged, clean, shapes, s => s.NearestNeighborOverHfr);

            int flaggedClose = flagged.Count(r => shapes.TryGetValue(r, out var s) && s.HasCloseNeighbor);
            int flaggedIsolated = flagged.Count - flaggedClose;
            int cleanClose = clean.Count(r => shapes.TryGetValue(r, out var s) && s.HasCloseNeighbor);
            sb.AppendLine();
            sb.AppendLine($"  \"close neighbor\" = nearest detected star within {CloseNeighborHfrFactor:0.#} x HFR");
            sb.AppendLine($"    flagged with close neighbor : {flaggedClose} ({Percent(flaggedClose, flagged.Count)})");
            sb.AppendLine($"    flagged ISOLATED            : {flaggedIsolated} ({Percent(flaggedIsolated, flagged.Count)})");
            sb.AppendLine($"    clean with close neighbor   : {cleanClose} ({Percent(cleanClose, clean.Count)})  [baseline rate]");
        }

        private static void AppendMetricLine(StringBuilder sb, string name,
                List<ContaminationDiagnosticRecord> flagged, List<ContaminationDiagnosticRecord> clean,
                Dictionary<ContaminationDiagnosticRecord, ShapeInfo> shapes,
                Func<ShapeInfo, double> shapeSelector, Func<ContaminationDiagnosticRecord, double> recordSelector = null) {
            double Sel(ContaminationDiagnosticRecord r) =>
                recordSelector != null ? recordSelector(r) : (shapes.TryGetValue(r, out var s) ? shapeSelector(s) : double.NaN);
            var fv = flagged.Select(Sel).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            var cv = clean.Select(Sel).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            string F2(List<double> v) => v.Count == 0 ? "n/a" : $"{F(Percentile(v, 0.50))} / {F(Percentile(v, 0.90))}";
            sb.AppendLine($"  {name,-22}  {F2(fv),-26} {F2(cv)}");
        }

        /// <summary>
        /// Sweeps the gradient-robust decision threshold using statistics already computed in a single detection
        /// run (no re-detection): a star is flagged at sensitivity S iff its MaxSectorResidualOverSE &gt; S. For
        /// each S also reports the median local gradient slope and eccentricity of the flagged set, so a
        /// threshold that admits high-gradient or high-eccentricity stars is visible. Helps tune the sensitivity.
        /// </summary>
        private static void WriteGradientRobustSweep(string path, List<ContaminationDiagnosticRecord> diagnostics,
                Dictionary<ContaminationDiagnosticRecord, ShapeInfo> shapes) {
            int total = diagnostics.Count;
            var sb = new StringBuilder();
            sb.AppendLine("sensitivity,flagged,flagged_pct,flagged_median_gradientslope,flagged_median_ecc");
            for (double s = 3.0; s <= 8.0 + 1e-9; s += 0.5) {
                var gr = diagnostics.Where(r => !double.IsNaN(r.MaxSectorResidualOverSE) && r.MaxSectorResidualOverSE > s).ToList();
                var grSlopes = gr.Select(r => r.GradientSlope).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
                var grEcc = gr.Select(r => shapes != null && shapes.TryGetValue(r, out var sh) ? sh.Eccentricity : double.NaN).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
                sb.AppendLine(string.Join(",",
                    s.ToString("0.0", CultureInfo.InvariantCulture),
                    gr.Count, PercentValue(gr.Count, total).ToString("0.##", CultureInfo.InvariantCulture),
                    grSlopes.Count > 0 ? F(Percentile(grSlopes, 0.50)) : "NaN",
                    grEcc.Count > 0 ? F(Percentile(grEcc, 0.50)) : "NaN"));
            }
            File.WriteAllText(path, sb.ToString());
        }

        // MTF-stretched BGR background for the annotated image.
        private static Mat BuildStretchedBgr(Mat srcFloat) {
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
            var bgr = new Mat();
            Cv2.CvtColor(stretched8, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }

        private static void WriteAnnotated(string path, Mat srcFloat, List<ContaminationDiagnosticRecord> diagnostics,
                Dictionary<ContaminationDiagnosticRecord, ShapeInfo> shapes) {
            // MTF-stretch a 16-bit copy for a viewable background, then draw star markers colored by status.
            using var bgr = BuildStretchedBgr(srcFloat);

            var green = new Scalar(0, 255, 0);     // accepted, clean
            var magenta = new Scalar(255, 0, 255); // flagged AND has a close detected neighbor (BGR)
            var cyan = new Scalar(255, 255, 0);    // flagged but ISOLATED (no close neighbor) (BGR)
            foreach (var r in diagnostics) {
                Scalar color;
                if (!r.ContaminationSuspected) {
                    color = green;
                } else {
                    bool close = shapes != null && shapes.TryGetValue(r, out var s) && s.HasCloseNeighbor;
                    color = close ? magenta : cyan;
                }
                Cv2.Circle(bgr, new OpenCvSharp.Point((int)Math.Round(r.CenterX), (int)Math.Round(r.CenterY)), 8, color, 1, LineTypes.AntiAlias);
            }
            Cv2.ImWrite(path, bgr);
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

        private static string F(double v) {
            if (double.IsNaN(v)) return "NaN";
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static double PercentValue(int n, int total) => total > 0 ? 100.0 * n / total : 0.0;

        private static string Percent(int n, int total) => $"{PercentValue(n, total).ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}
