#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json.Linq;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Headless fit-quality harness over a directory (or zip) of saved auto-focus report JSONs. It re-fits
    /// every curve's <c>MeasurePoints</c> with every selected hyperbolic model and reports the fit-quality
    /// metrics (R², reduced χ², σ(focus)=MinimumStdError, leave-one-out stability, RMS residual) so the models
    /// can be compared across many real runs at once. Needs no NINA profile and no images — it operates purely
    /// on the JSON, so it runs anywhere (including on a zip a user sends).
    /// </summary>
    internal static class FitQualityRunner {

        private static readonly HyperbolicFitModel[] AllModels = {
            HyperbolicFitModel.Symmetric,
            HyperbolicFitModel.UnevenBlendLegacy,
            HyperbolicFitModel.TiltedHyperbola,
            HyperbolicFitModel.SmoothBlend,
        };

        // Step size to assume when the curve's positions don't admit a sensible spacing estimate.
        private const int FallbackStepSize = 25;

        public static void Run(string[] args) {
            try {
                RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Fit-quality run failed");
                Environment.ExitCode = 1;
            }
        }

        /// <summary>One re-fit of one curve with one model (plus the curve metadata, repeated per row).</summary>
        private sealed class Row {
            public string FileRel;
            public string Timestamp;
            public string Filter;
            public string Region;
            public string SavedMethod;
            public string SavedFitting;
            public string SavedModel;
            public int N;
            public int StepSize;
            public HyperbolicFitModel Model;
            public bool SolveOk;
            public double BestFocus = double.NaN;
            public double RSquared = double.NaN;
            public double ReducedChiSquared = double.NaN;
            public double MinStdErr = double.NaN;
            public double LooStd = double.NaN;
            public double RmsResid = double.NaN;
            // The report's own recorded values (for the model it actually used), repeated per row.
            public double StoredRSquared = double.NaN;
            public double StoredMinStdErr = double.NaN;
            public double StoredReducedChiSquared = double.NaN;
            public double StoredLooStd = double.NaN;
        }

        private static void RunImpl(string[] args) {
            var zip = GetArg(args, "--zip");
            var dir = GetArg(args, "--dir");
            var outDir = GetArg(args, "--out");
            var modelsArg = GetArg(args, "--models");
            var minPointsArg = GetArg(args, "--min-points");
            var useWeights = !HasFlag(args, "--no-weights");

            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-fitquality", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            int minPoints = 5;
            if (!string.IsNullOrWhiteSpace(minPointsArg)) {
                minPoints = int.Parse(minPointsArg, CultureInfo.InvariantCulture);
            }
            if (minPoints < 5) {
                Console.WriteLine($"--min-points {minPoints} raised to 5 (leave-one-out stability needs at least 5 points)");
                minPoints = 5;
            }

            var models = ParseModels(modelsArg);

            // Verbose logging to the NINA log file for offline inspection.
            Logger.SetLogLevel(LogLevelEnum.TRACE);

            // Mirror the contamination runner: a (non-running) WPF Application may be required by NINA utility
            // code paths. Harmless here even though we never touch a profile.
            if (Application.Current == null) {
                new Application();
            }

            // Resolve the directory to scan. A zip takes precedence and is extracted to a temp dir.
            string scanDir;
            string tempExtractDir = null;
            if (!string.IsNullOrWhiteSpace(zip)) {
                if (!File.Exists(zip)) {
                    throw new FileNotFoundException($"Zip not found: {zip}", zip);
                }
                tempExtractDir = Path.Combine(Path.GetTempPath(), "hf-fitquality-zip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(tempExtractDir);
                ZipFile.ExtractToDirectory(zip, tempExtractDir);
                scanDir = tempExtractDir;
                Console.WriteLine($"Zip: {zip} -> extracted to {tempExtractDir}");
            } else {
                scanDir = string.IsNullOrWhiteSpace(dir) ? Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus") : dir;
            }

            Console.WriteLine($"Scanning: {scanDir} (recursive)");
            Console.WriteLine($"Output:   {outDir}");
            Console.WriteLine($"Models:   {string.Join(", ", models.Select(m => m.ToString()))}");
            Console.WriteLine($"Weights:  {(useWeights ? "weighted (1/Error)" : "unweighted")}    min-points: {minPoints}");

            if (!Directory.Exists(scanDir)) {
                throw new DirectoryNotFoundException($"Scan directory not found: {scanDir}");
            }

            var files = Directory.EnumerateFiles(scanDir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            Console.WriteLine($"Found {files.Count} *.json files");

            var alglibAPI = new AlglibAPI();
            var rows = new List<Row>();
            int parsed = 0;
            int skipped = 0;
            foreach (var file in files) {
                var rel = Path.GetRelativePath(scanDir, file);
                var curve = LoadCurve(file, rel);
                if (curve == null || curve.Points.Count < minPoints) {
                    int n = curve?.Points.Count ?? 0;
                    Console.WriteLine($"  SKIP {rel} ({(curve == null ? "no MeasurePoints / parse error" : $"{n} valid points < {minPoints}")})");
                    Logger.Info($"Fit-quality skip {rel}: {(curve == null ? "no usable MeasurePoints" : $"{n} points")}");
                    skipped++;
                    continue;
                }
                parsed++;

                int stepSize = InferStepSize(curve.Points);
                Console.WriteLine($"  {rel}: n={curve.Points.Count}, step={stepSize}, savedModel={curve.SavedModel}");
                foreach (var model in models) {
                    rows.Add(Evaluate(alglibAPI, curve, model, stepSize, useWeights));
                }
            }

            WriteRunsCsv(Path.Combine(outDir, "fit_quality_runs.csv"), rows);
            WriteSummary(Path.Combine(outDir, "fit_quality_summary.txt"), files.Count, parsed, skipped, models, useWeights, minPoints, rows);

            PrintDigest(files.Count, parsed, skipped, models, rows);
            Console.WriteLine($"Wrote fit_quality_runs.csv, fit_quality_summary.txt to {outDir}");

            if (tempExtractDir != null) {
                try {
                    Directory.Delete(tempExtractDir, true);
                } catch (Exception ex) {
                    Logger.Warning($"Could not delete temp extract dir {tempExtractDir}: {ex.Message}");
                }
            }
        }

        /// <summary>A parsed curve: its focus points plus the report metadata we carry through to the CSV.</summary>
        private sealed class Curve {
            public string FileRel;
            public List<ScatterErrorPoint> Points;
            public string Timestamp = "";
            public string Filter = "";
            public string Region = "—";
            public string SavedMethod = "";
            public string SavedFitting = "";
            public string SavedModel = "—";
            public double StoredRSquared = double.NaN;
            public double StoredMinStdErr = double.NaN;
            public double StoredReducedChiSquared = double.NaN;
            public double StoredLooStd = double.NaN;
        }

        private static Curve LoadCurve(string file, string rel) {
            try {
                var json = JObject.Parse(File.ReadAllText(file));
                var points = LoadMeasurePoints(json);
                if (points == null) {
                    return null;
                }
                var curve = new Curve { FileRel = rel, Points = points };
                curve.Timestamp = (string)json["Timestamp"] ?? "";
                curve.Filter = (string)json["Filter"] ?? "";
                curve.SavedMethod = (string)json["Method"] ?? "";
                curve.SavedFitting = (string)json["Fitting"] ?? "";

                var regionIdx = json["Region"]?["Index"];
                if (regionIdx != null && regionIdx.Type != JTokenType.Null) {
                    curve.Region = regionIdx.ToString();
                }

                curve.StoredRSquared = ReadDouble(json["RSquares"]?["Hyperbolic"]);
                curve.StoredMinStdErr = ReadDouble(json["HyperbolicMinimumStdError"]);
                curve.StoredReducedChiSquared = ReadDouble(json["HyperbolicReducedChiSquared"]);
                curve.StoredLooStd = ReadDouble(json["HyperbolicLeaveOneOutStdError"]);
                curve.SavedModel = ReadModelName(json["HocusFocusAutoFocusOptions"]?["HyperbolicFitModel"]);
                return curve;
            } catch (Exception ex) {
                Logger.Warning($"Failed to parse {rel}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Builds the weighted focus points from <c>MeasurePoints</c> — the exact pattern used by the offline
        /// FocusCurveBenchmark: keep points with finite Position and positive Value, weight = 1/Error
        /// (ScatterErrorPoint carries ErrorY, from which the fit derives the 1/σ weight).
        /// </summary>
        private static List<ScatterErrorPoint> LoadMeasurePoints(JObject json) {
            var measurePoints = json["MeasurePoints"] as JArray;
            if (measurePoints == null) {
                return null;
            }
            var pts = new List<ScatterErrorPoint>();
            foreach (var mp in measurePoints) {
                var x = (double?)mp["Position"] ?? double.NaN;
                var y = (double?)mp["Value"] ?? double.NaN;
                var e = (double?)mp["Error"] ?? 1.0;
                if (!double.IsNaN(x) && !double.IsNaN(y) && y > 0) {
                    pts.Add(new ScatterErrorPoint(x, y, 0, e <= 0 ? 1.0 : e));
                }
            }
            return pts;
        }

        private static Row Evaluate(IAlglibAPI alglibAPI, Curve curve, HyperbolicFitModel model, int stepSize, bool useWeights) {
            var row = new Row {
                FileRel = curve.FileRel,
                Timestamp = curve.Timestamp,
                Filter = curve.Filter,
                Region = curve.Region,
                SavedMethod = curve.SavedMethod,
                SavedFitting = curve.SavedFitting,
                SavedModel = curve.SavedModel,
                N = curve.Points.Count,
                StepSize = stepSize,
                Model = model,
                StoredRSquared = curve.StoredRSquared,
                StoredMinStdErr = curve.StoredMinStdErr,
                StoredReducedChiSquared = curve.StoredReducedChiSquared,
                StoredLooStd = curve.StoredLooStd,
            };

            try {
                var fit = AlglibHyperbolicFitting.Create(alglibAPI, model, curve.Points, stepSize, useWeights);
                if (!fit.Solve()) {
                    row.SolveOk = false;
                    return row;
                }
                row.SolveOk = true;
                row.BestFocus = fit.Minimum.X;
                row.RSquared = fit.RSquared;
                row.ReducedChiSquared = fit.ReducedChiSquared;
                row.MinStdErr = fit.MinimumStdError;
                row.RmsResid = Rms(fit.Fitting, curve.Points);
                row.LooStd = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(alglibAPI, model, curve.Points, stepSize, useWeights);
            } catch (Exception ex) {
                Logger.Warning($"Fit failed for {curve.FileRel} model {model}: {ex.Message}");
                row.SolveOk = false;
            }
            return row;
        }

        private static double Rms(Func<double, double> fitting, List<ScatterErrorPoint> pts) {
            var sum = 0.0;
            foreach (var p in pts) {
                var r = fitting(p.X) - p.Y;
                sum += r * r;
            }
            return Math.Sqrt(sum / pts.Count);
        }

        /// <summary>Median spacing of the sorted unique positions; falls back to 25 when it can't be inferred.</summary>
        private static int InferStepSize(List<ScatterErrorPoint> points) {
            var positions = points.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            if (positions.Count < 2) {
                return FallbackStepSize;
            }
            var diffs = new List<double>(positions.Count - 1);
            for (int i = 1; i < positions.Count; ++i) {
                var d = positions[i] - positions[i - 1];
                if (d > 0) {
                    diffs.Add(d);
                }
            }
            if (diffs.Count == 0) {
                return FallbackStepSize;
            }
            diffs.Sort();
            var median = diffs.Count % 2 == 1 ? diffs[diffs.Count / 2] : 0.5 * (diffs[diffs.Count / 2 - 1] + diffs[diffs.Count / 2]);
            var rounded = (int)Math.Round(median);
            return rounded > 0 ? rounded : FallbackStepSize;
        }

        private static void WriteRunsCsv(string path, List<Row> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("file,timestamp,filter,region,savedMethod,savedFitting,savedModel,n,stepSize,model,solveOk,bestFocus," +
                "rSquared,reducedChiSquared,minStdErr,looStd,rmsResid,storedRSquared,storedMinStdErr,storedReducedChiSquared,storedLooStd");
            foreach (var r in rows) {
                sb.AppendLine(string.Join(",",
                    Csv(r.FileRel), Csv(r.Timestamp), Csv(r.Filter), Csv(r.Region),
                    Csv(r.SavedMethod), Csv(r.SavedFitting), Csv(r.SavedModel),
                    r.N.ToString(CultureInfo.InvariantCulture), r.StepSize.ToString(CultureInfo.InvariantCulture),
                    r.Model.ToString(), r.SolveOk ? "1" : "0", F(r.BestFocus),
                    F(r.RSquared), F(r.ReducedChiSquared), F(r.MinStdErr), F(r.LooStd), F(r.RmsResid),
                    F(r.StoredRSquared), F(r.StoredMinStdErr), F(r.StoredReducedChiSquared), F(r.StoredLooStd)));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSummary(string path, int filesFound, int parsed, int skipped, HyperbolicFitModel[] models,
                bool useWeights, int minPoints, List<Row> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("=== Fit-quality summary ===");
            sb.AppendLine($"Files found:   {filesFound}");
            sb.AppendLine($"Curves parsed: {parsed}");
            sb.AppendLine($"Skipped:       {skipped}");
            sb.AppendLine($"Weights:       {(useWeights ? "weighted (1/Error)" : "unweighted")}");
            sb.AppendLine($"Min points:    {minPoints}");
            sb.AppendLine($"Models:        {string.Join(", ", models.Select(m => m.ToString()))}");
            sb.AppendLine();

            sb.AppendLine("Per-model across all curves:");
            sb.AppendLine($"  {"model",-18} {"solveOk",8} {"R2(med/mean)",-20} {"redChi2(med/mean)",-22} {"sigFocus(med/mean,finite)",-30} {"loo(med/mean)",-20} {"rms(med/mean)",-20}");
            foreach (var model in models) {
                var mr = rows.Where(r => r.Model == model).ToList();
                int n = mr.Count;
                int ok = mr.Count(r => r.SolveOk);
                var okRows = mr.Where(r => r.SolveOk).ToList();
                var sig = okRows.Select(r => r.MinStdErr).Where(IsFinite).ToList();
                sb.AppendLine($"  {model,-18} {($"{ok}/{n}"),8} " +
                    $"{MedMean(okRows.Select(r => r.RSquared)),-20} " +
                    $"{MedMean(okRows.Select(r => r.ReducedChiSquared)),-22} " +
                    $"{($"{MedMean(sig)} [{sig.Count}]"),-30} " +
                    $"{MedMean(okRows.Select(r => r.LooStd)),-20} " +
                    $"{MedMean(okRows.Select(r => r.RmsResid)),-20}");
            }

            // Per-curve "wins": the model with the lowest LOO std and lowest RMS residual.
            var byCurve = rows.GroupBy(r => r.FileRel + "|" + r.Region).ToList();
            var looWins = new Dictionary<HyperbolicFitModel, int>();
            var rmsWins = new Dictionary<HyperbolicFitModel, int>();
            foreach (var m in models) { looWins[m] = 0; rmsWins[m] = 0; }
            var disagreements = new List<double>();
            foreach (var g in byCurve) {
                var solved = g.Where(r => r.SolveOk).ToList();
                var looCand = solved.Where(r => IsFinite(r.LooStd)).ToList();
                if (looCand.Count > 0) {
                    looWins[looCand.OrderBy(r => r.LooStd).First().Model]++;
                }
                var rmsCand = solved.Where(r => IsFinite(r.RmsResid)).ToList();
                if (rmsCand.Count > 0) {
                    rmsWins[rmsCand.OrderBy(r => r.RmsResid).First().Model]++;
                }
                var focuses = solved.Select(r => r.BestFocus).Where(IsFinite).ToList();
                if (focuses.Count >= 2) {
                    disagreements.Add(focuses.Max() - focuses.Min());
                }
            }

            sb.AppendLine();
            sb.AppendLine("Per-curve wins (lowest is best):");
            sb.AppendLine($"  {"model",-18} {"lowestLoo",10} {"lowestRms",10}");
            foreach (var m in models) {
                sb.AppendLine($"  {m,-18} {looWins[m],10} {rmsWins[m],10}");
            }

            if (disagreements.Count > 0) {
                disagreements.Sort();
                sb.AppendLine();
                sb.AppendLine("Model disagreement per curve (max - min bestFocus across solved models, focuser steps):");
                sb.AppendLine($"  curves   = {disagreements.Count}");
                sb.AppendLine($"  median   = {F(Percentile(disagreements, 0.50))}");
                sb.AppendLine($"  p90      = {F(Percentile(disagreements, 0.90))}");
                sb.AppendLine($"  max      = {F(disagreements.Last())}");
            }

            File.WriteAllText(path, sb.ToString());
        }

        private static void PrintDigest(int filesFound, int parsed, int skipped, HyperbolicFitModel[] models, List<Row> rows) {
            Console.WriteLine();
            Console.WriteLine($"=== Digest: {filesFound} files, {parsed} curves parsed, {skipped} skipped ===");
            Console.WriteLine($"  {"model",-18} {"solveOk",8} {"medLoo",10} {"medRms",10} {"medR2",8}");
            foreach (var model in models) {
                var mr = rows.Where(r => r.Model == model && r.SolveOk).ToList();
                int n = rows.Count(r => r.Model == model);
                Console.WriteLine($"  {model,-18} {($"{mr.Count}/{n}"),8} " +
                    $"{Med(mr.Select(r => r.LooStd)),10} {Med(mr.Select(r => r.RmsResid)),10} {Med(mr.Select(r => r.RSquared)),8}");
            }
        }

        private static HyperbolicFitModel[] ParseModels(string modelsArg) {
            if (string.IsNullOrWhiteSpace(modelsArg)) {
                return AllModels;
            }
            var result = new List<HyperbolicFitModel>();
            foreach (var token in modelsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (Enum.TryParse<HyperbolicFitModel>(token, ignoreCase: true, out var m)) {
                    if (!result.Contains(m)) {
                        result.Add(m);
                    }
                } else {
                    Console.WriteLine($"Unknown model '{token}' ignored. Valid: {string.Join(", ", AllModels.Select(x => x.ToString()))}");
                }
            }
            return result.Count > 0 ? result.ToArray() : AllModels;
        }

        private static string ReadModelName(JToken tok) {
            if (tok == null || tok.Type == JTokenType.Null) {
                return "—";
            }
            if (tok.Type == JTokenType.Integer) {
                var iv = (int)tok;
                return Enum.IsDefined(typeof(HyperbolicFitModel), iv) ? ((HyperbolicFitModel)iv).ToString() : iv.ToString(CultureInfo.InvariantCulture);
            }
            return tok.ToString();
        }

        private static double ReadDouble(JToken tok) {
            if (tok == null || tok.Type == JTokenType.Null) {
                return double.NaN;
            }
            try {
                return (double)tok;
            } catch {
                return double.NaN;
            }
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static double Percentile(List<double> sorted, double q) {
            if (sorted.Count == 0) return double.NaN;
            if (sorted.Count == 1) return sorted[0];
            double idx = q * (sorted.Count - 1);
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            double frac = idx - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }

        private static string MedMean(IEnumerable<double> values) {
            var v = values.Where(IsFinite).OrderBy(x => x).ToList();
            if (v.Count == 0) return "n/a";
            return $"{F(Percentile(v, 0.50))}/{F(v.Average())}";
        }

        private static string Med(IEnumerable<double> values) {
            var v = values.Where(IsFinite).OrderBy(x => x).ToList();
            return v.Count == 0 ? "n/a" : F(Percentile(v, 0.50));
        }

        private static bool HasFlag(string[] args, string name) =>
            args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

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
            if (double.IsInfinity(v)) return v > 0 ? "Inf" : "-Inf";
            return v.ToString("G9", CultureInfo.InvariantCulture);
        }

        private static string Csv(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
