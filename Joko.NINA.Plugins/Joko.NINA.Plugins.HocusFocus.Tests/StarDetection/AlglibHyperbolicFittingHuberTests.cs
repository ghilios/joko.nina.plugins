using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class AlglibHyperbolicFittingHuberTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        private static List<ScatterErrorPoint> CleanCurveWithOutlier() {
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.5, a: 2.0, b: 80.0,
                xStart: 4700, xStep: 25, count: 25);
            // Inject a single gross outlier well away from the vertex so an unweighted fit is visibly pulled.
            var bad = pts[3];
            pts[3] = new ScatterErrorPoint(bad.X, bad.Y + 6.0, 0, 1.0);
            return pts;
        }

        [Test]
        public void HuberIrls_GrossOutlier_RecoversMinimumBetterThanPlainFit() {
            const double trueX0 = 5000;

            var robust = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithOutlier(), useWeights: false);
            robust.HuberIrlsEnabled = true;
            Assert.That(robust.Solve(), Is.True);

            var plain = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithOutlier(), useWeights: false);
            plain.HuberIrlsEnabled = false;
            Assert.That(plain.Solve(), Is.True);

            var robustErr = Math.Abs(robust.Minimum.X - trueX0);
            var plainErr = Math.Abs(plain.Minimum.X - trueX0);

            Assert.Multiple(() => {
                // The robust fit should be no worse, and meaningfully better, than the outlier-pulled plain fit.
                Assert.That(robustErr, Is.LessThanOrEqualTo(plainErr));
                Assert.That(robustErr, Is.LessThan(5.0), "Huber IRLS should keep the best-focus estimate near truth despite the outlier");
            });
        }
    }
}
