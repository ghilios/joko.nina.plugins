using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Robustness regression tests built from REAL saved auto-focus curves (mike1959 UC108/CS200 rigs) that drove
    /// the tilted/smooth-blend models into degenerate fits: the closed-form minimum ran away to 1e9–1e14 steps,
    /// and a single divergent leave-one-out refit blew the LOO stability up to 1e7–1e16. The fit must instead
    /// return a finite minimum near the sampled range and a bounded LOO for every model.
    /// </summary>
    [TestFixture]
    public class AsymmetricFitRobustnessTests {
        private IAlglibAPI alglibAPI;

        private static readonly HyperbolicFitModel[] AllModels = {
            HyperbolicFitModel.Symmetric,
            HyperbolicFitModel.UnevenBlend,
            HyperbolicFitModel.TiltedHyperbola,
            HyperbolicFitModel.SmoothBlend
        };

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // UC108, n=21: a monotonic ramp (7.84 -> 9.6, no real vertex in range). Tilted ran the minimum to -7.5e14.
        private static readonly double[][] MonotonicRamp_NoVertexInRange = {
            new[] { 0.0, 7.839050, 0.638013 },
            new[] { 1.0, 7.961625, 0.671539 },
            new[] { 11.0, 8.303164, 0.640615 },
            new[] { 21.0, 8.599752, 0.578047 },
            new[] { 31.0, 8.802762, 0.557605 },
            new[] { 41.0, 8.997434, 0.486232 },
            new[] { 51.0, 9.034380, 0.505478 },
            new[] { 61.0, 9.239328, 0.616678 },
            new[] { 71.0, 9.397387, 0.556208 },
            new[] { 81.0, 9.454321, 0.552180 },
            new[] { 91.0, 9.491712, 0.563458 },
            new[] { 101.0, 9.532192, 0.550557 },
            new[] { 111.0, 9.544934, 0.593257 },
            new[] { 121.0, 9.556212, 0.608118 },
            new[] { 131.0, 9.594925, 0.612019 },
            new[] { 141.0, 9.625054, 0.548355 },
            new[] { 151.0, 9.522130, 0.547106 },
            new[] { 161.0, 9.570066, 0.570344 },
            new[] { 171.0, 9.628579, 0.570173 },
            new[] { 181.0, 9.627635, 0.587291 },
            new[] { 191.0, 9.519182, 0.573350 },
        };

        // UC108, n=28: noisy vee with a long flat bottom. Full fit was sane but one LOO refit diverged -> LOO 1.3e7.
        private static readonly double[][] NoisyVeeWithFlatBottom = {
            new[] { 527.0, 5.151655, 0.477706 },
            new[] { 567.0, 3.951342, 0.432381 },
            new[] { 607.0, 3.553880, 0.422637 },
            new[] { 647.0, 3.597756, 0.432242 },
            new[] { 687.0, 3.457501, 0.422707 },
            new[] { 727.0, 3.494074, 0.461190 },
            new[] { 767.0, 3.370830, 0.412188 },
            new[] { 807.0, 3.422125, 0.453938 },
            new[] { 847.0, 3.404670, 0.422942 },
            new[] { 887.0, 3.469618, 0.404840 },
            new[] { 927.0, 3.464067, 0.395075 },
            new[] { 967.0, 3.527581, 0.317375 },
            new[] { 1007.0, 3.440380, 0.313216 },
            new[] { 1047.0, 3.456382, 0.396028 },
            new[] { 1087.0, 3.422506, 0.409591 },
            new[] { 1127.0, 3.481277, 0.377310 },
            new[] { 1167.0, 3.535559, 0.358833 },
            new[] { 1207.0, 3.496477, 0.334895 },
            new[] { 1247.0, 3.534520, 0.306257 },
            new[] { 1287.0, 3.406142, 0.394025 },
            new[] { 1327.0, 3.691549, 0.347828 },
            new[] { 1367.0, 3.205172, 0.302236 },
            new[] { 1407.0, 2.918304, 0.359171 },
            new[] { 1447.0, 2.631631, 0.398774 },
            new[] { 1487.0, 3.209720, 0.447621 },
            new[] { 1527.0, 3.458746, 0.421403 },
            new[] { 1567.0, 4.124533, 0.371556 },
            new[] { 1607.0, 4.833289, 0.583977 },
        };

        // CS200, n=9: a clean tight vee. Even here a single LOO refit diverged -> LOO 1.1e11.
        private static readonly double[][] CleanTightVee = {
            new[] { 10691.0, 6.600253, 1.054358 },
            new[] { 10711.0, 4.930959, 0.515568 },
            new[] { 10731.0, 3.847189, 0.764816 },
            new[] { 10751.0, 2.842909, 0.780947 },
            new[] { 10771.0, 2.800419, 0.890474 },
            new[] { 10791.0, 3.613742, 0.883015 },
            new[] { 10811.0, 5.232144, 1.133187 },
            new[] { 10831.0, 7.516464, 1.715516 },
            new[] { 10851.0, 9.277716, 1.540238 },
        };

        private static IEnumerable<TestCaseData> Cases() {
            var fixtures = new (string name, double[][] data)[] {
                ("MonotonicRamp", MonotonicRamp_NoVertexInRange),
                ("NoisyVee", NoisyVeeWithFlatBottom),
                ("CleanTightVee", CleanTightVee),
            };
            foreach (var (name, data) in fixtures) {
                foreach (var model in AllModels) {
                    yield return new TestCaseData(model, data).SetName($"{model}_{name}");
                }
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void Solve_MinimumIsFiniteAndNearRange(HyperbolicFitModel model, double[][] data) {
            var points = ToPoints(data);
            var (minX, maxX) = (points.Min(p => p.X), points.Max(p => p.X));
            var span = maxX - minX;
            var stepSize = InferStep(points);

            var fit = AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights: true);
            Assert.That(fit.Solve(), Is.True, $"{model} failed to solve");

            var x = fit.Minimum.X;
            Assert.Multiple(() => {
                Assert.That(double.IsNaN(x), Is.False, $"{model} minimum is NaN");
                Assert.That(double.IsInfinity(x), Is.False, $"{model} minimum is Infinity");
                // A focus-curve vertex may legitimately sit just outside a one-sided sweep, but never astronomically
                // far. The tilted model's capped b bounds the minimum offset to ~4.1 spans, so five sampled spans on
                // either side is the guaranteed window; the bug produced 1e9-1e14.
                Assert.That(x, Is.GreaterThanOrEqualTo(minX - 5.0 * span).And.LessThanOrEqualTo(maxX + 5.0 * span),
                    $"{model} minimum {x} is wildly outside the sampled range [{minX}, {maxX}]");
            });
        }

        [TestCaseSource(nameof(Cases))]
        public void MinimumStdError_DegenerateFit_IsNaNOrBoundedBySpan(HyperbolicFitModel model, double[][] data) {
            var points = ToPoints(data);
            var (minX, maxX) = (points.Min(p => p.X), points.Max(p => p.X));
            var span = maxX - minX;
            var stepSize = InferStep(points);

            var fit = AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights: true);
            Assert.That(fit.Solve(), Is.True, $"{model} failed to solve");

            // The parametric σ(focus) can blow up from an ill-conditioned covariance on a near-degenerate/low-R²
            // fit. A standard error larger than the entire sampled sweep does not localize focus and must be
            // reported as not-determined (NaN), never as a misleadingly precise 1e6+ steps.
            var se = fit.MinimumStdError;
            if (!double.IsNaN(se)) {
                Assert.That(double.IsInfinity(se), Is.False, $"{model} σ(focus) is Infinity");
                Assert.That(se, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(se, Is.LessThanOrEqualTo(span), $"{model} σ(focus) {se} exceeds the sampled span {span}");
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void LeaveOneOut_IsFiniteAndBounded(HyperbolicFitModel model, double[][] data) {
            var points = ToPoints(data);
            var (minX, maxX) = (points.Min(p => p.X), points.Max(p => p.X));
            var span = maxX - minX;
            var stepSize = InferStep(points);

            var loo = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(alglibAPI, model, points, stepSize, useWeights: true);

            // LOO may be NaN (too few usable refits) but must never be a giant finite blowup. The best-focus
            // position cannot plausibly move by more than the sampled span when one point is dropped.
            if (!double.IsNaN(loo)) {
                Assert.That(double.IsInfinity(loo), Is.False, $"{model} LOO is Infinity");
                Assert.That(loo, Is.GreaterThanOrEqualTo(0.0));
                Assert.That(loo, Is.LessThanOrEqualTo(span), $"{model} LOO {loo} exceeds the sampled span {span} (degenerate refit)");
            }
        }

        private static List<ScatterErrorPoint> ToPoints(double[][] data) =>
            data.Select(r => new ScatterErrorPoint(r[0], r[1], 0, r[2])).ToList();

        private static int InferStep(List<ScatterErrorPoint> points) {
            var xs = points.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            var diffs = new List<double>();
            for (int i = 1; i < xs.Count; ++i) {
                diffs.Add(xs[i] - xs[i - 1]);
            }
            diffs.Sort();
            return diffs.Count == 0 ? 25 : Math.Max(1, (int)Math.Round(diffs[diffs.Count / 2]));
        }
    }
}
