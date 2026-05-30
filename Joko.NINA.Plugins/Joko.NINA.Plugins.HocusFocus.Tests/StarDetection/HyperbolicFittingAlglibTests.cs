using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class HyperbolicFittingAlglibTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        [Test]
        public void Solve_NoiselessSymmetricHyperbola_RecoversX0() {
            const double trueX0 = 5000;
            const double trueY0 = 0.0;
            const double trueA = 2.0;
            const double trueB = 80.0;
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: trueX0, y0: trueY0, a: trueA, b: trueB,
                xStart: 4800, xStep: 25, count: 17);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            var ok = fit.Solve();

            Assert.Multiple(() => {
                Assert.That(ok, Is.True);
                Assert.That(fit.Minimum.X, Is.EqualTo(trueX0).Within(1.0));
                // Minimum.Y in HyperbolicFittingAlglib is (a + y0). At noiseless data, Y at x0 is also a + y0.
                Assert.That(fit.Minimum.Y, Is.EqualTo(trueA + trueY0).Within(0.05));
                Assert.That(fit.RSquared, Is.GreaterThan(0.99));
            });
        }

        [Test]
        public void Solve_EmptyInputs_ReturnsFalse() {
            var fit = HyperbolicFittingAlglib.Create(
                alglibAPI,
                new System.Collections.Generic.List<OxyPlot.Series.ScatterErrorPoint>(),
                useWeights: false);
            Assert.That(fit.Solve(), Is.False);
        }

        [Test]
        public void Solve_FilteredZeroOutputs_StillFitsHyperbola() {
            // Add some zero-Y points that should be filtered out by the Y >= 0.1 gate.
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 1.5, b: 50.0,
                xStart: 4900, xStep: 20, count: 11);
            pts.Add(new OxyPlot.Series.ScatterErrorPoint(5000, 0.0, 0, 1.0));
            pts.Add(new OxyPlot.Series.ScatterErrorPoint(6000, 0.05, 0, 1.0));

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            var ok = fit.Solve();

            Assert.Multiple(() => {
                Assert.That(ok, Is.True);
                Assert.That(fit.RSquared, Is.GreaterThan(0.99));
            });
        }

        [Test]
        public void Solve_WithWeights_StillRecoversMinimum() {
            const double trueX0 = 4000;
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: trueX0, y0: 0.0, a: 2.5, b: 60.0,
                xStart: 3850, xStep: 30, count: 11);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: true);
            var ok = fit.Solve();

            Assert.Multiple(() => {
                Assert.That(ok, Is.True);
                Assert.That(fit.Minimum.X, Is.EqualTo(trueX0).Within(2.0));
            });
        }

        [Test]
        public void Solve_AsymmetricallySampledHyperbola_StillFits() {
            const double trueX0 = 5000;
            // Heavily right-skewed sampling
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: trueX0, y0: 0.0, a: 1.5, b: 50.0,
                xStart: 4995, xStep: 25, count: 11);

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            var ok = fit.Solve();

            Assert.Multiple(() => {
                Assert.That(ok, Is.True);
                Assert.That(fit.RSquared, Is.GreaterThan(0.95));
            });
        }

        [Test]
        public void Expression_NonEmpty_WhenFitSucceeds() {
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4800, xStep: 25, count: 17);
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);
            Assert.That(fit.Expression, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Solve_NarrowDataSet_StillProducesAFit() {
            // Only 5 points clustered tightly around the minimum
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4990, xStep: 5, count: 5);
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);
            // With dense local sampling, x0 may not be precisely recovered, but fit must produce a sane Minimum.X
            Assert.That(fit.Minimum.X, Is.GreaterThan(0));
        }

        [Test]
        public void MinimumStdError_NoiselessFit_IsFiniteAndSmall() {
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4800, xStep: 25, count: 17);
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.MinimumStdError), Is.False);
                Assert.That(fit.MinimumStdError, Is.GreaterThanOrEqualTo(0.0));
                // Perfect data ⇒ the best-focus position is essentially exactly determined.
                Assert.That(fit.MinimumStdError, Is.LessThan(1.0));
            });
        }

        [Test]
        public void MinimumStdError_NoisyFit_IsPositiveAndFinite() {
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.0, a: 2.0, b: 80.0,
                xStart: 4700, xStep: 25, count: 25);
            var rng = new System.Random(13);
            var noisy = new System.Collections.Generic.List<OxyPlot.Series.ScatterErrorPoint>();
            foreach (var p in clean) {
                var n = (rng.NextDouble() - 0.5) * 0.4; // ~±0.2 HFR noise
                noisy.Add(new OxyPlot.Series.ScatterErrorPoint(p.X, p.Y + n, 0, 1.0));
            }

            var fit = HyperbolicFittingAlglib.Create(alglibAPI, noisy, useWeights: false);
            Assert.That(fit.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(double.IsNaN(fit.MinimumStdError), Is.False);
                Assert.That(double.IsInfinity(fit.MinimumStdError), Is.False);
                Assert.That(fit.MinimumStdError, Is.GreaterThan(0.0));
            });
        }

        [Test]
        public void Minimum_AtMinimumXValue_EqualsAPlusY0() {
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 1.5, a: 2.0, b: 80.0,
                xStart: 4800, xStep: 25, count: 17);
            var fit = HyperbolicFittingAlglib.Create(alglibAPI, pts, useWeights: false);
            Assert.That(fit.Solve(), Is.True);
            Assert.That(fit.Minimum.Y, Is.EqualTo(2.0 + 1.5).Within(0.05));
        }
    }
}
