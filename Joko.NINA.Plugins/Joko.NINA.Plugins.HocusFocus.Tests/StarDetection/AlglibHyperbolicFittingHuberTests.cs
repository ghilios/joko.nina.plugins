using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
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

        private static List<ScatterErrorPoint> CleanCurveWithSameSideOutliers() {
            var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 5000, y0: 0.5, a: 2.0, b: 80.0,
                xStart: 4700, xStep: 25, count: 25);
            // Several outliers on the SAME side shift the residual median away from zero, which is
            // exactly where an uncentered |r| comparison and a median-centered MAD disagree.
            foreach (var i in new[] { 3, 9, 20 }) {
                var bad = pts[i];
                pts[i] = new ScatterErrorPoint(bad.X, bad.Y + 4.0, 0, 1.0);
            }
            return pts;
        }

        [Test]
        public void HuberIrls_SameSideOutliers_RecoversMinimumNearTruth() {
            var robust = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithSameSideOutliers(), useWeights: false);
            robust.HuberIrlsEnabled = true;
            Assert.That(robust.Solve(), Is.True);

            var plain = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithSameSideOutliers(), useWeights: false);
            plain.HuberIrlsEnabled = false;
            Assert.That(plain.Solve(), Is.True);

            var robustErr = Math.Abs(robust.Minimum.X - 5000);
            var plainErr = Math.Abs(plain.Minimum.X - 5000);
            Assert.Multiple(() => {
                // The scenario must be non-trivial: same-side outliers pull the non-robust fit off truth...
                Assert.That(plainErr, Is.GreaterThan(robustErr),
                    "same-side outliers should pull the non-robust fit further from truth than the robust fit");
                // ...and Huber IRLS should keep best-focus near truth despite them.
                Assert.That(robustErr, Is.LessThan(10.0),
                    "Huber IRLS should keep best-focus near truth despite same-side outliers");
            });
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

        [Test]
        public void SolveHuberIrls_MidLoopSolveFailure_ReturnsLastGoodSolution() {
            // The IRLS loop always attempts a second reweighted solve after a successful first one
            // (the convergence check cannot pass on iteration 0: prevSumAbsResiduals starts at +inf).
            // Failing solve #2 must leave the result exactly equal to the plain non-IRLS solution —
            // not the failed solve's parameters.
            // WHY the first IRLS solve equals the plain solve: at iter=0 the IRLS path initializes
            // effectiveWeights as a fresh clone of Weights (all 1.0 when useWeights:false), which is
            // the same uniform weight vector the plain non-IRLS path uses — so solve #1 is numerically
            // identical to the plain solve, and restoring it on solve-#2 failure reproduces it exactly.
            var failing = new FailNthSolveAlglibAPI(failOnCall: 2);
            var robust = HyperbolicFittingAlglib.Create(failing, CleanCurveWithOutlier(), useWeights: false);
            robust.HuberIrlsEnabled = true;
            Assert.That(robust.Solve(), Is.True);

            var plain = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithOutlier(), useWeights: false);
            plain.HuberIrlsEnabled = false;
            Assert.That(plain.Solve(), Is.True);

            Assert.Multiple(() => {
                Assert.That(robust.Minimum.X, Is.Not.NaN);
                Assert.That(robust.Minimum.Y, Is.Not.NaN);
                Assert.That(robust.Minimum.X, Is.EqualTo(plain.Minimum.X).Within(1e-6));
                Assert.That(robust.Minimum.Y, Is.EqualTo(plain.Minimum.Y).Within(1e-6));
            });
        }
    }
}
