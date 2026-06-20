#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
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
    /// Headless A-vs-B donut-size agreement report. For one image, runs HocusFocus star detection exactly
    /// like ContaminationDiagnosticRunner, then for every accepted DONUT star computes two independent size
    /// estimates and reports how well they agree:
    ///   (A) curve-of-growth encircled-flux radius R50 (the production-candidate metric), and
    ///   (B) the annulus⊛Gaussian forward-fit ring radius Rring (a brightness-independent geometric ground
    ///       truth).
    /// Acceptance per design §9.5: R50-vs-Rring slope ∈ ~[0.9,1.1], both brightness slopes &lt; 0.3 px/dex.
    /// TestApp-only; makes no production-plugin changes and is read-only on the profile.
    /// </summary>
    internal static class AgreementRunner {

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Agreement run failed");
                Environment.ExitCode = 1;
            }
        }

        private sealed class Row {
            public double CenterX, CenterY, PeakBrightness, R50, Rring, Eps, FitR2;
        }

        private static async Task RunImpl(string[] args) {
            var imagePath = DiagnosticUtil.GetArg(args, "--image");
            if (string.IsNullOrWhiteSpace(imagePath)) {
                Console.Error.WriteLine("Usage: TestApp agreement --image <path> [--profile-id <guid>] [--out <dir>] [--defocus-distortion] [--defocus-centering]");
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
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "agreement", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            Logger.SetLogLevel(LogLevelEnum.TRACE);

            // The real ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a
            // (non-running) WPF Application must exist or it NREs.
            if (Application.Current == null) {
                new Application();
            }

            Console.WriteLine($"Image: {imagePath}");
            Console.WriteLine($"Output: {outDir}");

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

            var baseParams = HocusFocusStarDetection.BuildStarDetectorParams(options);

            // Opt-in defocus-aware switches (mirrors ContaminationDiagnosticRunner) so donut candidates that the
            // distortion/centering gates would otherwise reject can be admitted and measured.
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

            // Load the original image once (CV_32F, normalized [0,1]); detection mutates its input, so it gets a
            // clone and the original is kept for the size measurements.
            using var srcFloat = await DiagnosticUtil.LoadFloatMat(imagePath, profileService);
            Console.WriteLine($"Image dimensions: {srcFloat.Width} x {srcFloat.Height}");

            var result = await DetectClone(srcFloat, baseParams);
            var stars = result.DetectedStars ?? new List<Star>();
            // Per-image measurement noise σ — the same value the contamination test uses as its per-star
            // NoiseSigma fallback (it equals the measurement sigma in the production path). Drives the
            // ring-center 3σ threshold and the curve-of-growth noise-convergence cap.
            double noiseSigma = result.MeasurementNoiseSigma;
            if (!(noiseSigma > 0)) noiseSigma = 1e-6;
            Console.WriteLine($"Detected {stars.Count} stars, MeasurementNoiseSigma={noiseSigma.ToString("G6", CultureInfo.InvariantCulture)}");

            double sizeRefPx = baseParams.DefocusDistortionSizeReference;
            var rows = new List<Row>();
            int donutCount = 0;
            foreach (var star in stars) {
                double bboxMax = Math.Max(star.StarBoundingBox.Width, star.StarBoundingBox.Height);
                if (bboxMax < sizeRefPx) continue;
                donutCount++;

                double maxRadius = Math.Min(90.0, Math.Max(30.0, bboxMax * 1.5));
                var plane = star.BackgroundPlane;

                var c = DonutRadiusOracle.RingCenter(srcFloat, star.Center.X, star.Center.Y, plane, star.Background, maxRadius, noiseSigma);
                var annular = DonutRadiusOracle.ScanAnnularBins(srcFloat, c.cx, c.cy, plane, star.Background, maxRadius, 0.0);
                double r50 = DonutRadiusOracle.EncircledRadius(annular, noiseSigma, maxRadius, 0.5);
                var fit = DonutRadiusOracle.FitAnnulus(annular, maxRadius);

                if (double.IsNaN(r50) || double.IsNaN(fit.Rring)) continue;
                rows.Add(new Row {
                    CenterX = c.cx, CenterY = c.cy, PeakBrightness = star.PeakBrightness,
                    R50 = r50, Rring = fit.Rring, Eps = fit.Eps, FitR2 = fit.RSquared,
                });
            }
            Console.WriteLine($"Donut stars (bboxMax >= {sizeRefPx.ToString("G6", CultureInfo.InvariantCulture)} px): {donutCount}, valid rows: {rows.Count}");

            WriteCsv(Path.Combine(outDir, "agreement.csv"), rows);
            WriteSummary(Path.Combine(outDir, "agreement_summary.txt"), imagePath, srcFloat, baseParams, noiseSigma, stars.Count, donutCount, rows);
            Console.WriteLine($"Wrote agreement.csv, agreement_summary.txt to {outDir}");
        }

        private static async Task<HocusFocusStarDetectorResult> DetectClone(Mat srcFloat, StarDetectorParams p) {
            using var clone = srcFloat.Clone();
            var detector = new StarDetector(new AlglibAPI());
            return await detector.Detect(clone, p, null, CancellationToken.None);
        }

        private static void WriteCsv(string path, List<Row> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("CenterX,CenterY,PeakBrightness,R50,Rring,Eps,FitR2");
            foreach (var r in rows) {
                if (double.IsNaN(r.R50) || double.IsNaN(r.Rring)) continue;
                sb.AppendLine(string.Join(",", F(r.CenterX), F(r.CenterY), F(r.PeakBrightness), F(r.R50), F(r.Rring), F(r.Eps), F(r.FitR2)));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, string imagePath, Mat srcFloat, StarDetectorParams p,
                double noiseSigma, int totalStars, int donutCount, List<Row> allRows) {
            // Exclude oracle fit failures (FitR2 < 0.5) from the agreement statistics.
            const double minFitR2 = 0.5;
            var used = allRows.Where(r => r.FitR2 >= minFitR2).ToList();
            int excluded = allRows.Count - used.Count;

            var sb = new StringBuilder();
            sb.AppendLine("=== A-vs-B donut-size agreement (design §9.5) ===");
            sb.AppendLine($"Image: {imagePath}");
            sb.AppendLine($"Dimensions: {srcFloat.Width} x {srcFloat.Height}");
            sb.AppendLine($"MeasurementNoiseSigma: {noiseSigma.ToString("G9", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"DefocusDistortionSizeReference (donut threshold): {p.DefocusDistortionSizeReference.ToString(CultureInfo.InvariantCulture)} px");
            sb.AppendLine($"DefocusAwareDistortion={p.DefocusAwareDistortion}, DefocusAwareCentering={p.DefocusAwareCentering}");
            sb.AppendLine($"Total detected stars: {totalStars}");
            sb.AppendLine($"Donut stars (bboxMax >= size ref): {donutCount}");
            sb.AppendLine($"Valid rows (R50 & Rring not NaN): {allRows.Count}");
            sb.AppendLine($"Rows used (FitR2 >= {minFitR2.ToString(CultureInfo.InvariantCulture)}): {used.Count}");
            sb.AppendLine($"Rows excluded (oracle fit failure): {excluded}");
            sb.AppendLine();

            if (used.Count < 2) {
                sb.AppendLine("Not enough rows (need >= 2) to compute agreement statistics.");
                File.WriteAllText(path, sb.ToString());
                Console.WriteLine(sb.ToString());
                return;
            }

            var r50 = used.Select(r => r.R50).ToArray();
            var rring = used.Select(r => r.Rring).ToArray();
            var logB = used.Select(r => Math.Log10(Math.Max(r.PeakBrightness, 1e-30))).ToArray();

            // (1) R50 (y) vs Rring (x)
            double pearson = Pearson(rring, r50);
            var (slopeAB, interAB) = Ols(rring, r50);

            // (2) absolute agreement
            var absDiff = used.Select(r => Math.Abs(r.R50 - r.Rring)).OrderBy(v => v).ToList();
            double medAbsDiff = Median(absDiff);
            double medRring = Median(rring.OrderBy(v => v).ToList());
            double medAbsDiffPct = medRring > 0 ? 100.0 * medAbsDiff / medRring : double.NaN;

            // (3) brightness slopes (should be ~0)
            var (slopeR50B, _) = Ols(logB, r50);
            var (slopeRringB, _) = Ols(logB, rring);

            sb.AppendLine("R50 (y) vs Rring (x):");
            sb.AppendLine($"  Pearson r       = {F(pearson)}");
            sb.AppendLine($"  OLS slope       = {F(slopeAB)}");
            sb.AppendLine($"  OLS intercept   = {F(interAB)} px");
            sb.AppendLine();
            sb.AppendLine($"median |R50 - Rring| = {F(medAbsDiff)} px ({F(medAbsDiffPct)}% of median Rring={F(medRring)} px)");
            sb.AppendLine();
            sb.AppendLine("Brightness slopes (px per decade of PeakBrightness; should be ~0):");
            sb.AppendLine($"  R50   vs log10(PeakBrightness) slope = {F(slopeR50B)}");
            sb.AppendLine($"  Rring vs log10(PeakBrightness) slope = {F(slopeRringB)}");
            sb.AppendLine();

            bool slopeOk = slopeAB >= 0.9 && slopeAB <= 1.1;
            bool brightOk = Math.Abs(slopeR50B) < 0.3 && Math.Abs(slopeRringB) < 0.3;
            sb.AppendLine("§9.5 gate (slope in [0.9,1.1] AND |both brightness slopes| < 0.3 px/dex):");
            sb.AppendLine($"  slope-in-range  : {(slopeOk ? "PASS" : "FAIL")} (slope={F(slopeAB)})");
            sb.AppendLine($"  brightness-flat : {(brightOk ? "PASS" : "FAIL")} (R50={F(slopeR50B)}, Rring={F(slopeRringB)})");
            sb.AppendLine($"  OVERALL         : {(slopeOk && brightOk ? "PASS" : "FAIL")}");

            File.WriteAllText(path, sb.ToString());
            Console.WriteLine(sb.ToString());
        }

        // ---- stats helpers ----

        private static double Pearson(double[] x, double[] y) {
            int n = x.Length;
            double mx = x.Average(), my = y.Average();
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; ++i) {
                double a = x[i] - mx, b = y[i] - my;
                sxy += a * b; sxx += a * a; syy += b * b;
            }
            return (sxx > 0 && syy > 0) ? sxy / Math.Sqrt(sxx * syy) : double.NaN;
        }

        // OLS of y on x: returns (slope, intercept).
        private static (double slope, double intercept) Ols(double[] x, double[] y) {
            int n = x.Length;
            double mx = x.Average(), my = y.Average();
            double sxy = 0, sxx = 0;
            for (int i = 0; i < n; ++i) {
                double a = x[i] - mx;
                sxy += a * (y[i] - my); sxx += a * a;
            }
            double slope = sxx > 0 ? sxy / sxx : double.NaN;
            double intercept = my - slope * mx;
            return (slope, intercept);
        }

        private static double Median(List<double> sorted) {
            if (sorted.Count == 0) return double.NaN;
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        private static string F(double v) {
            if (double.IsNaN(v)) return "NaN";
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }
    }
}
