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
    public class SmoothBlendHyperbolicFittingAlglibTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // The model derives its logistic width as 0.25 * sample spacing, so generate data with the matching w.
        private static double WidthFor(double xStep) => 0.25 * xStep;

        // Brute-force the minimum of the generating smooth-blend curve for an independent truth.
        private static double TrueMinX(double x0, double y0, double a, double b, double c, double w, double lo, double hi) {
            var bestX = lo;
            var bestY = double.MaxValue;
            for (int i = 0; i <= 20000; ++i) {
                var x = lo + (hi - lo) * i / 20000.0;
                var y = SyntheticFocusCurveSamples.SmoothBlend(x, x0, y0, a, b, c, w);
                if (y < bestY) {
                    bestY = y;
                    bestX = x;
                }
            }
            return bestX;
        }

        [Test]
        public void Solve_KnownBlend_RecoversMinimum() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 60.0, c = 110.0, xStep = 25;
            var w = WidthFor(xStep);
            var pts = SyntheticFocusCurveSamples.SmoothBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, w: w, xStart: 4600, xStep: xStep, count: 33);
            var trueMin = TrueMinX(x0, y0, a, b, c, w, 4600, 4600 + 32 * xStep);

            var fit = SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(fit.Minimum.X, Is.EqualTo(trueMin).Within(5.0));
                Assert.That(fit.RSquared, Is.GreaterThan(0.999));
            });
        }

        [Test]
        public void Solve_EqualBandC_ReducesToSymmetricAtX0() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 80.0, xStep = 25;
            // b == c => the blend is exactly the symmetric hyperbola, minimum at x0 regardless of t.
            var pts = SyntheticFocusCurveSamples.SmoothBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: b, w: WidthFor(xStep), xStart: 4700, xStep: xStep, count: 25);

            var fit = SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(fit.Minimum.X, Is.EqualTo(x0).Within(2.0));
                Assert.That(fit.RSquared, Is.GreaterThan(0.999));
            });
        }

        [Test]
        public void Fitting_IsSmoothAcrossVertex() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 60.0, c = 110.0, xStep = 25;
            var pts = SyntheticFocusCurveSamples.SmoothBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, w: WidthFor(xStep), xStart: 4600, xStep: xStep, count: 33);
            var fit = SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            // Second difference (curvature proxy) must stay small and bounded right across the vertex,
            // i.e. no kink. A clamped blend would show a curvature spike here.
            const double h = 1.0;
            var maxSecondDiff = 0.0;
            for (double x = fit.Minimum.X - 30; x <= fit.Minimum.X + 30; x += h) {
                var second = fit.Fitting(x - h) - 2 * fit.Fitting(x) + fit.Fitting(x + h);
                maxSecondDiff = Math.Max(maxSecondDiff, Math.Abs(second));
            }
            Assert.That(maxSecondDiff, Is.LessThan(0.02), "curve should be smooth (no kink) across the vertex");
        }

        [Test]
        public void Solve_SmoothAsymmetricData_FitsBetterThanLegacyUneven() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 55.0, c = 120.0, xStep = 25;
            var pts = SyntheticFocusCurveSamples.SmoothBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, w: WidthFor(xStep), xStart: 4600, xStep: xStep, count: 33);

            var smooth = SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            var legacy = HyperbolicUnevenFittingAlglib.Create(alglibAPI, pts, (int)xStep, useWeights: false);
            Assert.That(smooth.Solve(), Is.True);
            Assert.That(legacy.Solve(), Is.True);

            Assert.That(smooth.RSquared, Is.GreaterThanOrEqualTo(legacy.RSquared));
        }

        [Test]
        public void Solve_WithOptGuard_AnalyticJacobianMatches() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 60.0, c = 110.0, xStep = 25;
            var pts = SyntheticFocusCurveSamples.SmoothBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, w: WidthFor(xStep), xStart: 4600, xStep: xStep, count: 33);

            var fit = SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: true);
            fit.OptGuardEnabled = true;
            fit.HuberIrlsEnabled = false; // verify the analytic Jacobian on a single clean solve
            Assert.That(fit.Solve(), Is.True);
        }

        [Test]
        public void MinimumStdError_NoisyFit_IsPositiveAndFinite() {
            const double x0 = 5000, y0 = 0.5, a = 2.0, b = 60.0, c = 110.0, xStep = 25;
            var clean = SyntheticFocusCurveSamples.SmoothBlendPoints(
                x0: x0, y0: y0, a: a, b: b, c: c, w: WidthFor(xStep), xStart: 4600, xStep: xStep, count: 33);
            var rng = new Random(31);
            var noisy = clean.Select(p => new ScatterErrorPoint(p.X, p.Y + (rng.NextDouble() - 0.5) * 0.2, 0, 1.0)).ToList();

            var fit = SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, noisy, useWeights: false);
            Assert.That(fit.Solve(), Is.True);
            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.MinimumStdError), Is.False);
                Assert.That(double.IsInfinity(fit.MinimumStdError), Is.False);
                Assert.That(fit.MinimumStdError, Is.GreaterThan(0.0));
            });
        }
    }
}
