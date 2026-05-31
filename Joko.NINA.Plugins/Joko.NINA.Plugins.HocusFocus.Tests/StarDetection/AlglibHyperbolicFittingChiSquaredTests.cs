using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class AlglibHyperbolicFittingChiSquaredTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // Box-Muller standard normal with a deterministic Random.
        private static double NextGaussian(Random rng) {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        private static List<ScatterErrorPoint> NoisySymmetricPoints(double sigma, int seed, double errorY) {
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4600, xStep: 20, count: 41, errorY: errorY);
            var rng = new Random(seed);
            var noisy = new List<ScatterErrorPoint>(clean.Count);
            foreach (var p in clean) {
                noisy.Add(new ScatterErrorPoint(p.X, p.Y + sigma * NextGaussian(rng), 0, errorY));
            }
            return noisy;
        }

        [Test]
        public void ReducedChiSquared_WeightedFitWithMatchingSigma_IsApproximatelyOne() {
            const double sigma = 0.1;
            var noisy = NoisySymmetricPoints(sigma, seed: 7, errorY: sigma);

            // Pure weighted least squares (no Huber reweighting) so E[reduced χ²] = 1 exactly.
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, noisy, useWeights: true);
            fit.HuberIrlsEnabled = false;
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.ReducedChiSquared), Is.False);
                Assert.That(fit.ReducedChiSquared, Is.EqualTo(1.0).Within(0.6));
            });
        }

        [Test]
        public void ReducedChiSquared_UnweightedFit_EqualsResidualSumOfSquaresOverDof() {
            var noisy = NoisySymmetricPoints(sigma: 0.15, seed: 11, errorY: 1.0);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, noisy, useWeights: false);
            fit.HuberIrlsEnabled = false;
            Assert.That(fit.Solve(), Is.True);

            // All points have Y >> 0.1, so none are filtered: n = count, p = 4 (symmetric hyperbola).
            var n = noisy.Count;
            const int p = 4;
            var rss = noisy.Sum(pt => {
                var r = fit.Fitting(pt.X) - pt.Y;
                return r * r;
            });
            var expectedDof = Math.Max(1, n - p);

            Assert.Multiple(() => {
                Assert.That(fit.DegreesOfFreedom, Is.EqualTo(expectedDof));
                Assert.That(fit.ChiSquared, Is.EqualTo(rss).Within(1e-6));
                Assert.That(fit.ReducedChiSquared, Is.EqualTo(rss / expectedDof).Within(1e-9));
            });
        }

        [Test]
        public void ChiSquaredMetrics_FailedSolve_RemainNaN() {
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, new List<ScatterErrorPoint>(), useWeights: false);
            Assert.That(fit.Solve(), Is.False);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.ChiSquared), Is.True);
                Assert.That(double.IsNaN(fit.ReducedChiSquared), Is.True);
            });
        }

        [Test]
        public void LeaveOneOut_NoisyData_IsFiniteAndPositive() {
            var noisy = NoisySymmetricPoints(sigma: 0.12, seed: 23, errorY: 1.0);

            var loo = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                alglibAPI, HyperbolicFitModel.Symmetric, noisy, stepSize: 20, useWeights: false);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(loo), Is.False);
                Assert.That(double.IsInfinity(loo), Is.False);
                Assert.That(loo, Is.GreaterThan(0.0));
            });
        }

        [Test]
        public void LeaveOneOut_NearNoiselessData_IsSmall() {
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4600, xStep: 20, count: 41);

            var loo = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                alglibAPI, HyperbolicFitModel.Symmetric, clean, stepSize: 20, useWeights: false);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(loo), Is.False);
                // Dropping any single point barely moves the best-focus estimate for a clean curve.
                Assert.That(loo, Is.LessThan(1.0));
            });
        }

        [Test]
        public void LeaveOneOut_TooFewPoints_ReturnsNaN() {
            var fourPoints = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4950, xStep: 25, count: 4);

            var loo = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                alglibAPI, HyperbolicFitModel.Symmetric, fourPoints, stepSize: 25, useWeights: false);

            Assert.That(double.IsNaN(loo), Is.True);
        }
    }
}
