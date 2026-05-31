using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OxyPlot.Series;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class HyperbolicUnevenFittingAlglibTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        [Test]
        public void Create_ZeroStepSize_Throws() {
            var pts = new List<ScatterErrorPoint> {
                new(1000, 1.0, 0, 1.0),
                new(2000, 0.5, 0, 1.0),
                new(3000, 1.0, 0, 1.0),
            };
            Assert.Throws<ArgumentException>(() =>
                HyperbolicUnevenFittingAlglib.Create(alglibAPI, pts, stepSize: 0, useWeights: false));
        }

        [Test]
        public void Solve_NoiselessSymmetricHyperbola_RecoversApproxMinimum() {
            const double trueX0 = 5000;
            const double trueA = 2.0;
            const double trueB = 80.0;
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: trueX0, y0: 0.0, a: trueA, b: trueB,
                xStart: 4800, xStep: 25, count: 17);

            var fit = HyperbolicUnevenFittingAlglib.Create(alglibAPI, pts, stepSize: 25, useWeights: false);
            var ok = fit.Solve();

            Assert.Multiple(() => {
                Assert.That(ok, Is.True);
                Assert.That(fit.Minimum.X, Is.EqualTo(trueX0).Within(2.0));
                Assert.That(fit.RSquared, Is.GreaterThan(0.99));
            });
        }

        [Test]
        public void Solve_EmptyInputs_ReturnsFalse() {
            var fit = HyperbolicUnevenFittingAlglib.Create(
                alglibAPI, new List<ScatterErrorPoint>(), stepSize: 25, useWeights: false);
            Assert.That(fit.Solve(), Is.False);
        }

        // Reflection access to the protected hooks — the model's constructor is private so we cannot subclass.
        private static readonly MethodInfo ModelValueMethod =
            typeof(HyperbolicUnevenFittingAlglib).GetMethod("ModelValue", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo ModelGradientMethod =
            typeof(HyperbolicUnevenFittingAlglib).GetMethod("ModelGradient", BindingFlags.Instance | BindingFlags.NonPublic);

        private static double ModelValue(HyperbolicUnevenFittingAlglib fit, double[] p, double x) =>
            (double)ModelValueMethod.Invoke(fit, new object[] { p, x });

        private static double[] ModelGradient(HyperbolicUnevenFittingAlglib fit, double[] p, double x) {
            var grad = new double[p.Length];
            ModelGradientMethod.Invoke(fit, new object[] { p, x, grad });
            return grad;
        }

        // The analytic ModelGradient (used to build the σ(focus) covariance) must match central finite
        // differences of ModelValue at points away from the two ramp kinks (x == x0 and x == x0 − stepSize).
        [Test]
        public void ModelGradient_MatchesFiniteDifferences_AwayFromKinks() {
            const int stepSize = 25;
            // A handful of points just to construct the instance (so StepSize is set); the fit itself is unused.
            var seed = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0, xStart: 4900, xStep: stepSize, count: 9);
            var fit = HyperbolicUnevenFittingAlglib.Create(alglibAPI, seed, stepSize, useWeights: false);

            var p = new double[] { 5000.0, 0.5, 2.0, 55.0, 120.0 }; // x0, y0, a, b, c (asymmetric: b != c)
            // x0 = 5000, stepSize = 25 ⇒ ramp interior is (4975, 5000). Include one interior x (4990) and
            // several outside; avoid the exact kinks at 5000 and 4975.
            var xs = new[] { 4850.0, 4900.0, 4990.0, 5100.0, 5200.0 };

            foreach (var x in xs) {
                var analytic = ModelGradient(fit, p, x);
                for (int j = 0; j < p.Length; ++j) {
                    var h = 1e-5 * Math.Max(1.0, Math.Abs(p[j]));
                    var pPlus = (double[])p.Clone();
                    var pMinus = (double[])p.Clone();
                    pPlus[j] += h;
                    pMinus[j] -= h;
                    var fd = (ModelValue(fit, pPlus, x) - ModelValue(fit, pMinus, x)) / (2.0 * h);
                    Assert.That(analytic[j], Is.EqualTo(fd).Within(1e-4 * (1.0 + Math.Abs(fd))),
                        $"∂/∂p[{j}] mismatch at x={x}");
                }
            }
        }

        // σ(focus) is now produced for the Uneven Blend model: on a noisy asymmetric curve it must be finite,
        // positive, and within the sampled span (passing the base ill-conditioning guard).
        [Test]
        public void MinimumStdError_NoisyAsymmetricFit_IsPositiveFiniteAndBounded() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 55.0, c = 120.0, xStep = 25;
            const double xStart = 4600;
            const int count = 33;
            var clean = SyntheticFocusCurveSamples.UnevenBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, stepSize: xStep, xStart: xStart, xStep: xStep, count: count);
            var rng = new Random(17);
            var noisy = clean.Select(pt => new ScatterErrorPoint(pt.X, pt.Y + (rng.NextDouble() - 0.5) * 0.2, 0, 1.0)).ToList();

            var fit = HyperbolicUnevenFittingAlglib.Create(alglibAPI, noisy, (int)xStep, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            var span = (count - 1) * xStep;
            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.MinimumStdError), Is.False);
                Assert.That(double.IsInfinity(fit.MinimumStdError), Is.False);
                Assert.That(fit.MinimumStdError, Is.GreaterThan(0.0));
                Assert.That(fit.MinimumStdError, Is.LessThan(span));
            });
        }

        // The reported best focus (Minimum.X = x0) must coincide with the actual trough of the fitted curve,
        // confirming the σ(focus) = se(x0) reduction the covariance relies on.
        [Test]
        public void Minimum_CoincidesWithFittedCurveTrough() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 55.0, c = 120.0, xStep = 25;
            const double xStart = 4600;
            const int count = 33;
            var pts = SyntheticFocusCurveSamples.UnevenBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, stepSize: xStep, xStart: xStart, xStep: xStep, count: count);

            var fit = HyperbolicUnevenFittingAlglib.Create(alglibAPI, pts, (int)xStep, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            var lo = xStart;
            var hi = xStart + (count - 1) * xStep;
            var bruteMinX = lo;
            var bruteMinY = double.MaxValue;
            for (int i = 0; i <= 40000; ++i) {
                var x = lo + (hi - lo) * i / 40000.0;
                var y = fit.Fitting(x);
                if (y < bruteMinY) {
                    bruteMinY = y;
                    bruteMinX = x;
                }
            }
            Assert.That(fit.Minimum.X, Is.EqualTo(bruteMinX).Within(2.0 * xStep));
        }

        // The behavioral change for Hybrid: the Uneven Blend model now contributes a finite σ(focus), so it competes
        // in Tier 1 (parametric σ(focus)) rather than only the leave-one-out fallback.
        [Test]
        public void SelectBestModel_LegacyContributesFiniteSigmaToTier1() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 50.0, c = 130.0, xStep = 25;
            var pts = SyntheticFocusCurveSamples.UnevenBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, stepSize: xStep, xStart: 4600, xStep: xStep, count: 33, errorY: 0.05);

            var legacy = AlglibHyperbolicFitting.Create(
                alglibAPI, HyperbolicFitModel.UnevenBlendLegacy, pts, (int)xStep, useWeights: true);
            Assert.That(legacy.Solve(), Is.True);
            Assert.That(double.IsNaN(legacy.MinimumStdError), Is.False,
                "Uneven Blend model must now report a finite σ(focus)");

            // And the selector resolves via the Tier-1 (finite σ(focus)) path with a finite-σ winner.
            var chosen = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, pts, (int)xStep, useWeights: true, out var bestFit);
            Assert.Multiple(() => {
                Assert.That(chosen, Is.Not.EqualTo(HyperbolicFitModel.Hybrid));
                Assert.That(bestFit, Is.Not.Null);
                Assert.That(double.IsNaN(bestFit.MinimumStdError), Is.False);
            });
        }
    }
}
