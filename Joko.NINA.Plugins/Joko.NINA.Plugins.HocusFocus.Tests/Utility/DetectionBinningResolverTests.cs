using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class DetectionBinningResolverTests {

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
        public void DescribeRecommendation_LeadsWithTheMeasurementAndStaysShort() {
            // It shares a row with the control, so it says the measurement and the verdict, nothing else. The
            // reasoning is in the tooltip.
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, 3.6), Is.EqualTo("Measured in-focus HFR 3.6 px - 1x1 is right"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, 6.1), Is.EqualTo("Measured in-focus HFR 6.1 px - 2x2 recommended"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(2, 6.1), Is.EqualTo("Measured in-focus HFR 6.1 px - 2x2 is right"));
            });
        }

        [Test]
        public void DescribeRecommendation_WithNoMeasurement_SaysHowToGetOne() {
            // The regression this whole approach exists for: a user with 3.6 px stars was told to bin 2x2, because
            // the recommendation was estimated from pixel scale under an assumed 3" seeing. It now refuses to guess.
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, double.NaN), Is.EqualTo("Run an auto-focus to get a recommendation"));
                Assert.That(DetectionBinningResolver.DescribeRecommendation(1, 0.0), Is.EqualTo("Run an auto-focus to get a recommendation"));
            });
        }

        [Test]
        public void RecommendFromHfr_OnTheReportedCase_LeavesBinningOff() {
            // 3.6 px measured is inside the detector's calibrated 2-4 px band, so 1x1 is correct.
            Assert.That(DetectionBinningResolver.RecommendFromHfr(3.6), Is.EqualTo(1));
        }

        [Test]
        public void DescribeRecommendationDetail_CarriesTheReasoningAndTheDate() {
            var when = new DateTime(2026, 7, 27, 3, 4, 5, DateTimeKind.Utc);
            var above = DetectionBinningResolver.DescribeRecommendationDetail(1, 6.1, when);
            Assert.Multiple(() => {
                Assert.That(above, Does.Contain("2 to 4 px"));
                Assert.That(above, Does.Contain("2x2"));
                Assert.That(above, Does.Contain("3.1 px"), "it must say where binning lands the star size");
                Assert.That(above, Does.Contain("Measured"));
            });

            var inRange = DetectionBinningResolver.DescribeRecommendationDetail(1, 3.6, when);
            Assert.That(inRange, Does.Contain("binning is not needed"));

            var none = DetectionBinningResolver.DescribeRecommendationDetail(1, double.NaN, null);
            Assert.Multiple(() => {
                Assert.That(none, Does.Contain("Nothing measured yet"));
                Assert.That(none, Does.Contain("seeing varies"), "it should explain why it will not estimate");
                Assert.That(none, Does.Not.Contain("NaN"));
            });
        }

        [Test]
        public void ShouldShowRecommendation_OnlyWhenItAsksForSomething() {
            Assert.Multiple(() => {
                // Nothing measured: it asks for an auto-focus, so it is worth a line.
                Assert.That(DetectionBinningResolver.ShouldShowRecommendation(1, double.NaN), Is.True);
                // Measured and disagrees: it asks for a factor change.
                Assert.That(DetectionBinningResolver.ShouldShowRecommendation(1, 6.1), Is.True);
                // Measured and agrees: nothing to ask for. A line that only confirms the current setting is
                // noise, and it trains the eye to skip the line that matters.
                Assert.That(DetectionBinningResolver.ShouldShowRecommendation(2, 6.1), Is.False);
                Assert.That(DetectionBinningResolver.ShouldShowRecommendation(1, 3.6), Is.False);
            });
        }

        [Test]
        public void DiffersFromRecommendation_IsFalseWithoutAMeasurement() {
            // No measurement means no advice, so nothing to disagree with — the UI must not highlight a mismatch
            // it cannot justify.
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.DiffersFromRecommendation(4, double.NaN), Is.False);
                Assert.That(DetectionBinningResolver.DiffersFromRecommendation(1, 6.1), Is.True);
                Assert.That(DetectionBinningResolver.DiffersFromRecommendation(2, 6.1), Is.False);
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
