using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Tests the nested F-test that decides whether a 5-parameter asymmetric hyperbola significantly improves
    /// on the 4-parameter Symmetric baseline. The statistic is scale-invariant in σ (numerator and denominator
    /// both scale by 1/σ²), so it is valid even though weighted reduced χ² runs ≪ 1 for star-rich fields.
    /// </summary>
    [TestFixture]
    public class AsymmetryFTestTests {

        // Reproduces the faithful "uneven" run numbers (n=10): Symmetric reduced χ²=0.0259607 (dof 6) vs
        // Tilted reduced χ²=0.00707306 (dof 5) ⇒ F≈17 ⇒ significant at 0.95 AND 0.99.
        [Test]
        public void Justified_WhenAsymmetricFitsSignificantlyBetter() {
            double chiSym = 0.0259607 * 6;   // χ² = reducedχ² × dof, p_sym = 4 ⇒ dof = 10-4 = 6
            double chiTilt = 0.00707306 * 5; // p_asym = 5 ⇒ dof = 10-5 = 5
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(chiSym, chiTilt, n: 10, confidence: 0.95), Is.True);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(chiSym, chiTilt, n: 10, confidence: 0.99), Is.True);
        }

        [Test]
        public void NotJustified_WhenImprovementIsTiny() {
            // Asymmetric barely improves χ² (1%): F is small ⇒ not significant.
            double chiSym = 0.020 * 6;
            double chiAsym = 0.0198 * 5;
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(chiSym, chiAsym, n: 10, confidence: 0.95), Is.False);
        }

        [Test]
        public void NotJustified_WhenAsymmetricFitsWorseOrEqual() {
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.10, 0.12, n: 10, confidence: 0.95), Is.False);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.10, 0.10, n: 10, confidence: 0.95), Is.False);
        }

        [Test]
        public void NotJustified_WhenTooFewPointsToTest() {
            // n - p_asym < 1 ⇒ no denominator degrees of freedom ⇒ cannot test ⇒ false.
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.50, 0.10, n: 5, confidence: 0.95), Is.False);
        }

        [Test]
        public void NotJustified_OnNonFiniteInputs() {
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(double.NaN, 0.1, n: 10, confidence: 0.95), Is.False);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.5, double.NaN, n: 10, confidence: 0.95), Is.False);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.5, 0.0, n: 10, confidence: 0.95), Is.False);
        }
    }
}
