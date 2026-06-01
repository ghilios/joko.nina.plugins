using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus {

    [TestFixture]
    public class IsHyperbolicFitAcceptableTests {

        // --- R² criterion: reproduces the legacy R²-only behavior ---

        [Test]
        public void RSquared_BelowThreshold_IsRejected() {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.RSquared, rSquared: 0.70, reducedChiSquared: 0.5,
                rSquaredThreshold: 0.80, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.False);
        }

        [Test]
        public void RSquared_AtOrAboveThreshold_IsAccepted() {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.RSquared, rSquared: 0.90, reducedChiSquared: 999.0,
                rSquaredThreshold: 0.80, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.True);
        }

        [Test]
        public void RSquared_ThresholdDisabled_AlwaysAccepts() {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.RSquared, rSquared: 0.0, reducedChiSquared: double.NaN,
                rSquaredThreshold: 0.0, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.True);
        }

        // --- Reduced χ² criterion ---

        [Test]
        public void ReducedChiSquared_AboveThreshold_IsRejected() {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.ReducedChiSquared, rSquared: 0.99, reducedChiSquared: 10.0,
                rSquaredThreshold: 0.80, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.False);
        }

        [Test]
        public void ReducedChiSquared_BelowThreshold_IsAccepted() {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.ReducedChiSquared, rSquared: 0.01, reducedChiSquared: 3.0,
                rSquaredThreshold: 0.80, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.True);
        }

        [Test]
        public void ReducedChiSquared_IgnoresRSquared() {
            // Low R² must not reject under the χ² criterion when the χ² is good.
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.ReducedChiSquared, rSquared: 0.0, reducedChiSquared: 1.2,
                rSquaredThreshold: 0.95, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.True);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ReducedChiSquared_NonFinite_NeverRejects(double reducedChiSquared) {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.ReducedChiSquared, rSquared: 0.0, reducedChiSquared: reducedChiSquared,
                rSquaredThreshold: 0.80, reducedChiSquaredThreshold: 5.0);
            Assert.That(acceptable, Is.True);
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void ReducedChiSquared_ThresholdDisabled_AlwaysAccepts(double threshold) {
            var acceptable = AutoFocusEngine.IsHyperbolicFitAcceptable(
                FitRejectionCriterion.ReducedChiSquared, rSquared: 0.0, reducedChiSquared: 1000.0,
                rSquaredThreshold: 0.80, reducedChiSquaredThreshold: threshold);
            Assert.That(acceptable, Is.True);
        }
    }
}
