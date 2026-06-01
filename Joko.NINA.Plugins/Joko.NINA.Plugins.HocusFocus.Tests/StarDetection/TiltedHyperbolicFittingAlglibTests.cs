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
    public class TiltedHyperbolicFittingAlglibTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        [Test]
        public void Solve_KnownSkew_RecoversShiftedMinimum() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 80.0, s = 0.012; // |s| < a/b = 0.025
            var pts = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: x0, y0: y0, a: a, b: b, s: s, xStart: 4600, xStep: 25, count: 33);
            var trueMin = SyntheticFocusCurveSamples.TiltedHyperbolaMinimumX(x0, a, b, s);

            var fit = TiltedHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                // The minimum is genuinely shifted off x0 by the skew, and the fit should recover it.
                Assert.That(Math.Abs(trueMin - x0), Is.GreaterThan(10.0), "sanity: skew should move the minimum off x0");
                Assert.That(fit.Minimum.X, Is.EqualTo(trueMin).Within(3.0));
                Assert.That(fit.RSquared, Is.GreaterThan(0.999));
            });
        }

        [Test]
        public void Solve_ZeroSkew_ReducesToSymmetricAtX0() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 80.0;
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: x0, y0: y0, a: a, b: b, xStart: 4700, xStep: 25, count: 25);

            var fit = TiltedHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(fit.Minimum.X, Is.EqualTo(x0).Within(1.0));
                Assert.That(fit.RSquared, Is.GreaterThan(0.999));
            });
        }

        [Test]
        public void Solve_AsymmetricData_FitsBetterThanSymmetric() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 80.0, s = 0.015;
            var pts = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: x0, y0: y0, a: a, b: b, s: s, xStart: 4600, xStep: 25, count: 33);

            var tilted = TiltedHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            var symmetric = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(tilted.Solve(), Is.True);
            Assert.That(symmetric.Solve(), Is.True);

            var tiltedRms = Rms(tilted.Fitting, pts);
            var symmetricRms = Rms(symmetric.Fitting, pts);

            Assert.Multiple(() => {
                Assert.That(tiltedRms, Is.LessThan(symmetricRms), "tilted model should fit asymmetric data better");
                Assert.That(tilted.RSquared, Is.GreaterThan(symmetric.RSquared));
            });
        }

        [Test]
        public void Solve_SteepSkew_RespectsSlopeBoundAndStaysFinite() {
            // Strongly skewed data: the optimizer must not drive |s| past a/b into NaN/Inf territory.
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 60.0, s = 0.028; // |s| < a/b = 0.0333
            var pts = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: x0, y0: y0, a: a, b: b, s: s, xStart: 4500, xStep: 20, count: 51);

            var fit = TiltedHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.Minimum.X), Is.False);
                Assert.That(double.IsInfinity(fit.Minimum.X), Is.False);
                Assert.That(fit.RSquared, Is.GreaterThan(0.99));
            });
        }

        [Test]
        public void Solve_WithOptGuard_AnalyticJacobianMatches() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 80.0, s = 0.01;
            var pts = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: x0, y0: y0, a: a, b: b, s: s, xStart: 4600, xStep: 25, count: 33);

            var fit = TiltedHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: true);
            fit.OptGuardEnabled = true;
            // Verify the analytic Jacobian directly with a single clean solve; the IRLS reweighting iterations
            // are a separate, gradient-preserving concern (and on noiseless data they down-weight to tiny,
            // ill-conditioned weights that make OptGuard's relative check spurious).
            fit.HuberIrlsEnabled = false;
            Assert.That(fit.Solve(), Is.True);
        }

        [Test]
        public void MinimumStdError_NoisyFit_IsPositiveAndFinite() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 80.0, s = 0.012;
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: x0, y0: y0, a: a, b: b, s: s, xStart: 4600, xStep: 25, count: 33);
            var rng = new Random(29);
            var noisy = clean.Select(p => new ScatterErrorPoint(p.X, p.Y + (rng.NextDouble() - 0.5) * 0.2, 0, 1.0)).ToList();

            var fit = TiltedHyperbolicFittingAlglib.Create(alglibAPI, noisy, useWeights: false);
            Assert.That(fit.Solve(), Is.True);
            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.MinimumStdError), Is.False);
                Assert.That(double.IsInfinity(fit.MinimumStdError), Is.False);
                Assert.That(fit.MinimumStdError, Is.GreaterThan(0.0));
            });
        }

        private static double Rms(Func<double, double> fitting, List<ScatterErrorPoint> pts) {
            var sum = 0.0;
            foreach (var p in pts) {
                var r = fitting(p.X) - p.Y;
                sum += r * r;
            }
            return Math.Sqrt(sum / pts.Count);
        }
    }
}
