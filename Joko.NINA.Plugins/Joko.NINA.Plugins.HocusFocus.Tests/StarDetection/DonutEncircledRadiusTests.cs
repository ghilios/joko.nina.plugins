using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class DonutEncircledRadiusTests {
        // A uniform disk's true 50%-enclosed radius is R/√2 (≈0.707 R). MeasureStar's flux-weighted mean
        // would give 2R/3 (≈0.667 R); R_e must land on the cumulative value.
        [Test]
        public void Re50_UniformDisk_MatchesHalfFluxRadius() {
            using var img = SyntheticDefocusedStarImage.CreateDisk(120, 120, 60, 60, radius: 20, peak: 1.0, background: 0.01);
            var plane = LocalBackgroundPlane.Flat(60, 60, 0.01);
            var re = DonutEncircledRadius.Measure(img, 60, 60, plane, 0.01, noiseSigma: 1e-4, maxRadius: 40, frac: 0.5).Radius;
            Assert.That(re, Is.EqualTo(20.0 / System.Math.Sqrt(2.0)).Within(1.0)); // ≈14.14, ±1px (1px binning)
        }

        // The headline property: R_e of the SAME annulus is independent of brightness (peak).
        [Test]
        public void Re50_Annulus_IsBrightnessIndependent() {
            double Re(double peak) {
                using var img = SyntheticDefocusedStarImage.CreateAnnulus(160, 160, 80, 80,
                    innerRadius: 8, outerRadius: 22, peak: peak, background: 0.02, edgeBlurSigma: 3.0);
                var plane = LocalBackgroundPlane.Flat(80, 80, 0.02);
                var c = DonutEncircledRadius.RingCenter(img, 80, 80, plane, 0.02, 50, 1e-4);
                return DonutEncircledRadius.Measure(img, c.cx, c.cy, plane, 0.02, 1e-4, 50, 0.5).Radius;
            }
            var faint = Re(0.05);
            var bright = Re(5.0);   // 100× brighter
            Assert.That(System.Math.Abs(bright - faint), Is.LessThan(0.6)); // px (validated regime ~0.35 px/dex)
        }

        // No flux above background in the integration window ⇒ total ≤ 0 ⇒ Radius is NaN (graceful, not 0 / not an exception).
        // The image is pure background; the subtracted background plane sits slightly above it, so every
        // background-subtracted pixel is non-positive ⇒ total < 0 ⇒ the !(total > 0) NaN path is exercised.
        [Test]
        public void Measure_AllBackgroundImage_ReturnsNaN() {
            using var img = SyntheticDefocusedStarImage.CreateDisk(80, 80, 40, 40, radius: 0.0, peak: 0.0, background: 0.05);
            var plane = LocalBackgroundPlane.Flat(40, 40, 0.06);
            var re = DonutEncircledRadius.Measure(img, 40, 40, plane, 0.06, noiseSigma: 1e-4, maxRadius: 30, frac: 0.5).Radius;
            Assert.That(double.IsNaN(re), Is.True);
        }
    }
}
