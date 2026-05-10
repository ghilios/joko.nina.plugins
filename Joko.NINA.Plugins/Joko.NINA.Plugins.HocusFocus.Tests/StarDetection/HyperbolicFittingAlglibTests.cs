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
    }
}
