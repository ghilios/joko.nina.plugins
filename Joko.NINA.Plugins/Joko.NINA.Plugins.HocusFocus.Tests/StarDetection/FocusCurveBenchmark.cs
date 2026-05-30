using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Empirical comparison of the four hyperbolic focus-curve models (Symmetric, Uneven Blend (legacy),
    /// Tilted Hyperbola, Smooth Blend) on synthetic curves with a known best-focus position, and optionally on
    /// real saved auto-focus runs. Produces per-model accuracy and leave-one-out stability metrics.
    ///
    /// The synthetic benchmark also asserts the recommendation that drove this work: on asymmetric curves the
    /// two new models recover the true best focus more accurately than the symmetric hyperbola. The full
    /// benchmark table is written to CSV (env var FOCUS_BENCH_OUT, else the temp dir) and printed to the test
    /// log. Set env var FOCUS_BENCH_RUNS_DIR to a folder of saved AutoFocusReport JSON files to also benchmark
    /// real runs.
    /// </summary>
    [TestFixture]
    public class FocusCurveBenchmark {
        private IAlglibAPI alglibAPI;

        private static readonly HyperbolicFitModel[] Models = {
            HyperbolicFitModel.Symmetric,
            HyperbolicFitModel.UnevenBlendLegacy,
            HyperbolicFitModel.TiltedHyperbola,
            HyperbolicFitModel.SmoothBlend
        };

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        private sealed class Curve {
            public string Id;
            public List<ScatterErrorPoint> Points;
            public double TrueMinX;  // NaN when unknown (real runs)
            public bool Asymmetric;
        }

        private sealed class Row {
            public string CurveId;
            public string Model;
            public int N;
            public double BestFocus;
            public double TrueMinX;
            public double AbsErr;
            public double RSquared;
            public double RmsResid;
            public double MinStdErr;
            public double LooStd;
        }

        private List<Curve> BuildSyntheticCurves() {
            var curves = new List<Curve>();
            const double x0 = 5000, y0 = 0.5, a = 2.0, xStart = 4600, xStep = 25;
            const int count = 33;

            // Symmetric baselines.
            foreach (var b in new[] { 60.0, 90.0 }) {
                curves.Add(new Curve {
                    Id = $"sym_b{b:0}",
                    Points = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(x0, y0, a, b, xStart, xStep, count),
                    TrueMinX = x0,
                    Asymmetric = false
                });
            }

            // Tilted (asymmetric) at several skews.
            foreach (var s in new[] { 0.006, 0.012, 0.018 }) {
                const double b = 80.0;
                curves.Add(new Curve {
                    Id = $"tilt_s{s:0.###}",
                    Points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(x0, y0, a, b, s, xStart, xStep, count),
                    TrueMinX = SyntheticFocusCurveSamples.TiltedHyperbolaMinimumX(x0, a, b, s),
                    Asymmetric = true
                });
            }

            // Smooth-blend (asymmetric) with differing left/right shapes.
            foreach (var (b, c) in new[] { (55.0, 110.0), (70.0, 130.0) }) {
                var w = 0.25 * xStep;
                var pts = SyntheticFocusCurveSamples.SmoothBlendPoints(x0, y0, a, b, c, w, xStart, xStep, count);
                curves.Add(new Curve {
                    Id = $"blend_b{b:0}_c{c:0}",
                    Points = pts,
                    TrueMinX = BruteForceMinX(pts.Select(p => p.X).Min(), pts.Select(p => p.X).Max(), x => SyntheticFocusCurveSamples.SmoothBlend(x, x0, y0, a, b, c, w)),
                    Asymmetric = true
                });
            }

            // Noisy + single gross outlier variants (deterministic seed) to exercise robustness.
            var rng = new Random(2024);
            var baseTilt = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(x0, y0, a, 80.0, 0.014, xStart, xStep, count);
            var noisy = baseTilt.Select(p => new ScatterErrorPoint(p.X, p.Y + (rng.NextDouble() - 0.5) * 0.15, 0, 1.0)).ToList();
            noisy[5] = new ScatterErrorPoint(noisy[5].X, noisy[5].Y + 5.0, 0, 1.0); // gross outlier
            curves.Add(new Curve {
                Id = "tilt_noisy_outlier",
                Points = noisy,
                TrueMinX = SyntheticFocusCurveSamples.TiltedHyperbolaMinimumX(x0, a, 80.0, 0.014),
                Asymmetric = true
            });

            return curves;
        }

        private static double BruteForceMinX(double lo, double hi, Func<double, double> f) {
            double bestX = lo, bestY = double.MaxValue;
            for (int i = 0; i <= 20000; ++i) {
                var x = lo + (hi - lo) * i / 20000.0;
                var y = f(x);
                if (y < bestY) { bestY = y; bestX = x; }
            }
            return bestX;
        }

        private Row Evaluate(Curve curve, HyperbolicFitModel model) {
            var fit = AlglibHyperbolicFitting.Create(alglibAPI, model, curve.Points, stepSize: 25, useWeights: true);
            if (!fit.Solve()) {
                return new Row { CurveId = curve.Id, Model = model.ToString(), N = curve.Points.Count, BestFocus = double.NaN, TrueMinX = curve.TrueMinX, AbsErr = double.NaN, RSquared = double.NaN, RmsResid = double.NaN, MinStdErr = double.NaN, LooStd = double.NaN };
            }
            var rms = Rms(fit.Fitting, curve.Points);
            var loo = LeaveOneOutStd(curve, model);
            return new Row {
                CurveId = curve.Id,
                Model = model.ToString(),
                N = curve.Points.Count,
                BestFocus = fit.Minimum.X,
                TrueMinX = curve.TrueMinX,
                AbsErr = double.IsNaN(curve.TrueMinX) ? double.NaN : Math.Abs(fit.Minimum.X - curve.TrueMinX),
                RSquared = fit.RSquared,
                RmsResid = rms,
                MinStdErr = fit.MinimumStdError,
                LooStd = loo
            };
        }

        private double LeaveOneOutStd(Curve curve, HyperbolicFitModel model) {
            var predictions = new List<double>();
            for (int skip = 0; skip < curve.Points.Count; ++skip) {
                var subset = new List<ScatterErrorPoint>(curve.Points.Count - 1);
                for (int i = 0; i < curve.Points.Count; ++i) {
                    if (i != skip) {
                        subset.Add(curve.Points[i]);
                    }
                }
                var fit = AlglibHyperbolicFitting.Create(alglibAPI, model, subset, stepSize: 25, useWeights: true);
                if (fit.Solve() && !double.IsNaN(fit.Minimum.X) && !double.IsInfinity(fit.Minimum.X)) {
                    predictions.Add(fit.Minimum.X);
                }
            }
            if (predictions.Count < 2) {
                return double.NaN;
            }
            var mean = predictions.Average();
            var variance = predictions.Sum(p => (p - mean) * (p - mean)) / (predictions.Count - 1);
            return Math.Sqrt(variance);
        }

        private static double Rms(Func<double, double> fitting, List<ScatterErrorPoint> pts) {
            var sum = 0.0;
            foreach (var p in pts) {
                var r = fitting(p.X) - p.Y;
                sum += r * r;
            }
            return Math.Sqrt(sum / pts.Count);
        }

        private void WriteCsvAndSummary(List<Row> rows, string label) {
            var sb = new StringBuilder();
            sb.AppendLine("curve_id,n,model,bestFocus,trueMin,absErr,rSquared,rmsResid,minStdErr,looStd");
            foreach (var r in rows) {
                sb.AppendLine(string.Join(",", r.CurveId, r.N, r.Model,
                    F(r.BestFocus), F(r.TrueMinX), F(r.AbsErr), F(r.RSquared), F(r.RmsResid), F(r.MinStdErr), F(r.LooStd)));
            }
            var outDir = Environment.GetEnvironmentVariable("FOCUS_BENCH_OUT");
            if (string.IsNullOrEmpty(outDir)) {
                outDir = Path.Combine(Path.GetTempPath(), "focus-bench");
            }
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, $"focus_benchmark_{label}.csv");
            File.WriteAllText(path, sb.ToString());
            TestContext.Progress.WriteLine($"[{label}] benchmark CSV written to {path}");

            // Per-model aggregate summary.
            TestContext.Progress.WriteLine($"[{label}] per-model summary (mean over curves):");
            TestContext.Progress.WriteLine($"  {"model",-18} {"meanAbsErr",12} {"meanRmsResid",13} {"meanLooStd",12}");
            foreach (var g in rows.GroupBy(r => r.Model)) {
                var meanAbs = MeanOrNan(g.Select(r => r.AbsErr));
                var meanRms = MeanOrNan(g.Select(r => r.RmsResid));
                var meanLoo = MeanOrNan(g.Select(r => r.LooStd));
                TestContext.Progress.WriteLine($"  {g.Key,-18} {meanAbs,12:0.###} {meanRms,13:0.####} {meanLoo,12:0.###}");
            }
        }

        private static double MeanOrNan(IEnumerable<double> values) {
            var v = values.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).ToList();
            return v.Count == 0 ? double.NaN : v.Average();
        }

        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.######");

        [Test]
        public void Synthetic_NewAsymmetricModels_BeatSymmetric_OnAsymmetricCurves() {
            var curves = BuildSyntheticCurves();
            var rows = new List<Row>();
            foreach (var curve in curves) {
                foreach (var model in Models) {
                    rows.Add(Evaluate(curve, model));
                }
            }

            WriteCsvAndSummary(rows, "synthetic");

            // On asymmetric curves, the tilted and smooth-blend models should recover the true best focus more
            // accurately (smaller mean absolute error) than the symmetric hyperbola.
            var asym = rows.Where(r => !double.IsNaN(r.AbsErr) &&
                                       BuildSyntheticCurves().First(c => c.Id == r.CurveId).Asymmetric).ToList();
            double MeanAbs(HyperbolicFitModel m) => MeanOrNan(asym.Where(r => r.Model == m.ToString()).Select(r => r.AbsErr));

            var symErr = MeanAbs(HyperbolicFitModel.Symmetric);
            var tiltErr = MeanAbs(HyperbolicFitModel.TiltedHyperbola);
            var blendErr = MeanAbs(HyperbolicFitModel.SmoothBlend);

            Assert.Multiple(() => {
                Assert.That(tiltErr, Is.LessThan(symErr), $"tilted ({tiltErr:0.##}) should beat symmetric ({symErr:0.##}) on asymmetric curves");
                Assert.That(blendErr, Is.LessThan(symErr), $"smooth blend ({blendErr:0.##}) should beat symmetric ({symErr:0.##}) on asymmetric curves");
            });
        }

        [Test, Explicit("Set FOCUS_BENCH_RUNS_DIR to a folder of saved AutoFocusReport JSON files")]
        public void SavedRuns_Benchmark() {
            var dir = Environment.GetEnvironmentVariable("FOCUS_BENCH_RUNS_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) {
                Assert.Ignore("FOCUS_BENCH_RUNS_DIR not set or missing");
            }

            var curves = new List<Curve>();
            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)) {
                var pts = LoadMeasurePoints(file);
                if (pts != null && pts.Count >= 5) {
                    curves.Add(new Curve { Id = Path.GetFileNameWithoutExtension(file), Points = pts, TrueMinX = double.NaN, Asymmetric = true });
                }
            }
            Assert.That(curves, Is.Not.Empty, "no usable saved runs found");

            var rows = new List<Row>();
            foreach (var curve in curves) {
                foreach (var model in Models) {
                    rows.Add(Evaluate(curve, model));
                }
            }
            WriteCsvAndSummary(rows, "saved-runs");
        }

        private static List<ScatterErrorPoint> LoadMeasurePoints(string file) {
            try {
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                var measurePoints = json["MeasurePoints"] as Newtonsoft.Json.Linq.JArray;
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
            } catch {
                return null;
            }
        }
    }
}
