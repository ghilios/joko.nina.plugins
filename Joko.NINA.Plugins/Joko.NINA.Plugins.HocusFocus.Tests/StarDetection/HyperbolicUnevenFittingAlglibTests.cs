using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
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
    }
}
