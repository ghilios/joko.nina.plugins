using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class DetectionBinningResolverTests {

        /// <summary>Pixel scale for a 3.76 µm sensor at the given focal length — the worked examples in
        /// docs/detection-binning-design.md and the settings page.</summary>
        private static double PixelScale(double focalLengthMm) => MathUtility.ArcsecPerPixel(3.76, focalLengthMm);

        [Test]
        public void ToFactor_MapsTheSettingToItsInteger() {
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.ToFactor(DetectionBinningEnum.Bin1), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.ToFactor(DetectionBinningEnum.Bin2), Is.EqualTo(2));
                Assert.That(DetectionBinningResolver.ToFactor(DetectionBinningEnum.Bin3), Is.EqualTo(3));
                Assert.That(DetectionBinningResolver.ToFactor(DetectionBinningEnum.Bin4), Is.EqualTo(4));
            });
        }

        [Test]
        public void ToFactor_ClampsAnOutOfRangeValue() {
            // A persisted value from an older build (including the removed Auto = 0) must never bin a user's
            // frames by an unsupported factor, and must never resolve to "off by accident" above the maximum.
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.ToFactor((DetectionBinningEnum)0), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.ToFactor((DetectionBinningEnum)(-3)), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.ToFactor((DetectionBinningEnum)9), Is.EqualTo(4));
            });
        }

        [Test]
        public void NoAutoMemberExists() {
            // The setting is explicit by design: a factor that resolved itself would change detection behavior on
            // upgrade and invalidate settings the user had already tuned.
            Assert.That(System.Enum.GetNames(typeof(DetectionBinningEnum)), Does.Not.Contain("Auto"));
        }

        // The worked-example table: a 3.76 µm sensor across the focal lengths the documentation cites.
        [TestCase(910.0, 1)]
        [TestCase(2000.0, 1)]
        [TestCase(2800.0, 2)]
        [TestCase(3910.0, 3)]
        [TestCase(5600.0, 4)]
        [TestCase(12000.0, 4)]   // clamped at the maximum
        public void RecommendFromPixelScale_MatchesTheDocumentedTable(double focalLengthMm, int expected) {
            Assert.That(DetectionBinningResolver.RecommendFromPixelScale(PixelScale(focalLengthMm)), Is.EqualTo(expected));
        }

        [Test]
        public void RecommendFromPixelScale_LandsInFocusHfrInsideTheCalibratedBand() {
            // The point of the rule: whatever the pixel scale, the binned in-focus HFR should end up near the
            // detector's 2-4 px band.
            for (var focalLength = 500.0; focalLength <= 6000.0; focalLength += 100.0) {
                var pixelScale = PixelScale(focalLength);
                var factor = DetectionBinningResolver.RecommendFromPixelScale(pixelScale);
                var binnedHfr = DetectionBinningResolver.EstimateInFocusHfrPixels(pixelScale) / factor;
                Assert.That(binnedHfr, Is.LessThanOrEqualTo(4.5),
                    $"at {focalLength}mm the binned in-focus HFR should not exceed the calibrated band (factor {factor})");
            }
        }

        [Test]
        public void RecommendFromPixelScale_UnusablePixelScale_RecommendsUnbinned() {
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.RecommendFromPixelScale(double.NaN), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.RecommendFromPixelScale(0.0), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.RecommendFromPixelScale(double.PositiveInfinity), Is.EqualTo(1));
            });
        }

        [Test]
        public void RecommendFromPixelScale_BacksOffWhenTheCameraIsAlreadyBinning() {
            // The caller passes a pixel scale that ALREADY includes camera binning, so hardware 2x2 halves the
            // recommended software factor rather than stacking on top of it.
            var native = PixelScale(5600);
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.RecommendFromPixelScale(native), Is.EqualTo(4));
                Assert.That(DetectionBinningResolver.RecommendFromPixelScale(native * 2), Is.EqualTo(2));
                Assert.That(DetectionBinningResolver.RecommendFromPixelScale(native * 4), Is.EqualTo(1));
            });
        }

        [TestCase(1.5, 1)]
        [TestCase(4.4, 1)]
        [TestCase(4.6, 2)]
        [TestCase(7.5, 3)]
        [TestCase(11.0, 4)]
        [TestCase(40.0, 4)]
        public void RecommendFromHfr_RoundsToTheNearestFactorThatHitsTheTarget(double hfr, int expected) {
            Assert.That(DetectionBinningResolver.RecommendFromHfr(hfr), Is.EqualTo(expected));
        }

        [Test]
        public void RecommendFromHfr_DegenerateInput_FallsBackToUnbinned() {
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.RecommendFromHfr(double.NaN), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.RecommendFromHfr(0.0), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.RecommendFromHfr(-3.0), Is.EqualTo(1));
            });
        }

        [Test]
        public void ToSetting_RoundTripsAFactor() {
            for (var factor = 1; factor <= DetectionBinningResolver.MaxBinningFactor; ++factor) {
                var setting = DetectionBinningResolver.ToSetting(factor);
                Assert.That(DetectionBinningResolver.ToFactor(setting), Is.EqualTo(factor));
            }
            Assert.That(DetectionBinningResolver.ToSetting(99), Is.EqualTo(DetectionBinningEnum.Bin4));
            Assert.That(DetectionBinningResolver.ToSetting(0), Is.EqualTo(DetectionBinningEnum.Bin1));
        }

        [Test]
        public void DescribeRecommendation_IsShortEnoughToSitBesideTheDropdown() {
            // It shares a row with the control, so it says the factor and nothing else. The reasoning is in the
            // tooltip; anything longer would cost the vertical space this layout exists to save.
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, PixelScale(2800)), Is.EqualTo("Recommended: 2x2"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(2, PixelScale(2800)), Is.EqualTo("Recommended: 2x2 (current)"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, PixelScale(910)), Is.EqualTo("Recommended: 1x1 (current)"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(2, PixelScale(910)), Is.EqualTo("Recommended: 1x1"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, double.NaN), Is.EqualTo("No recommendation"));
            });
        }

        [Test]
        public void DescribeRecommendationDetail_CarriesTheReasoningAndNoRawNaN() {
            var above = DetectionBinningResolver.DescribeRecommendationDetail(1, PixelScale(2800));
            Assert.Multiple(() => {
                Assert.That(above, Does.Contain("0.28\"/px"));
                Assert.That(above, Does.Contain("5.4 px"));
                Assert.That(above, Does.Contain("2.7 px"), "it must say where binning lands the star size");
                Assert.That(above, Does.Contain("2 to 4 px"));
            });

            var atOne = DetectionBinningResolver.DescribeRecommendationDetail(1, PixelScale(910));
            Assert.That(atOne, Does.Contain("binning is not needed"));

            var unknown = DetectionBinningResolver.DescribeRecommendationDetail(1, double.NaN);
            Assert.Multiple(() => {
                Assert.That(unknown, Does.Contain("pixel size and focal length"));
                Assert.That(unknown, Does.Not.Contain("NaN"));
            });
        }

        [Test]
        public void DiffersFromRecommendation_IsFalseWithoutAPixelScale() {
            // No pixel scale means no advice, so nothing to disagree with — the UI must not highlight a mismatch
            // it cannot justify.
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.DiffersFromRecommendation(4, double.NaN), Is.False);
                Assert.That(DetectionBinningResolver.DiffersFromRecommendation(1, PixelScale(2800)), Is.True);
                Assert.That(DetectionBinningResolver.DiffersFromRecommendation(2, PixelScale(2800)), Is.False);
            });
        }

        [Test]
        public void ApplyFactor_KeepsPixelScaleConsistentWithTheNewFactor() {
            // The wizard re-stamps already-built params to run at a factor the user has not committed to. PixelScale
            // carries the factor, so it has to move with it or the arcsec-valued outputs go wrong.
            var p = new StarDetectorParams { DetectionBinning = 2, PixelScale = 0.56 };   // 0.28"/px native

            DetectionBinningResolver.ApplyFactor(p, 3);

            Assert.Multiple(() => {
                Assert.That(p.DetectionBinning, Is.EqualTo(3));
                Assert.That(p.PixelScale, Is.EqualTo(0.84).Within(1e-12));
            });

            DetectionBinningResolver.ApplyFactor(p, 1);
            Assert.Multiple(() => {
                Assert.That(p.DetectionBinning, Is.EqualTo(1));
                Assert.That(p.PixelScale, Is.EqualTo(0.28).Within(1e-12));
            });
        }
    }
}
