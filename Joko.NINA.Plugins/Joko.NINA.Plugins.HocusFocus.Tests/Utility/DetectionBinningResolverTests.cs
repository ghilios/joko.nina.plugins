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
        public void Resolve_ExplicitSettings_PassThrough() {
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Bin1, PixelScale(5600)), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Bin2, PixelScale(910)), Is.EqualTo(2));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Bin3, double.NaN), Is.EqualTo(3));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Bin4, PixelScale(910)), Is.EqualTo(4));
            });
        }

        // The worked-example table: a 3.76 µm sensor across the focal lengths the documentation cites.
        [TestCase(910.0, 1)]
        [TestCase(2000.0, 1)]
        [TestCase(2800.0, 2)]
        [TestCase(3910.0, 3)]
        [TestCase(5600.0, 4)]
        [TestCase(12000.0, 4)]   // clamped at the maximum
        public void Resolve_Auto_MatchesTheDocumentedTable(double focalLengthMm, int expected) {
            Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, PixelScale(focalLengthMm)), Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_Auto_LandsInFocusHfrInsideTheCalibratedBand() {
            // The point of the rule: whatever the pixel scale, the binned in-focus HFR should end up near the
            // detector's 2-4 px band.
            for (var focalLength = 500.0; focalLength <= 6000.0; focalLength += 100.0) {
                var pixelScale = PixelScale(focalLength);
                var factor = DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, pixelScale);
                var binnedHfr = DetectionBinningResolver.EstimateInFocusHfrPixels(pixelScale) / factor;
                Assert.That(binnedHfr, Is.LessThanOrEqualTo(4.5),
                    $"at {focalLength}mm the binned in-focus HFR should not exceed the calibrated band (factor {factor})");
            }
        }

        [Test]
        public void Resolve_Auto_UnusablePixelScale_FallsBackToUnbinned() {
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, double.NaN), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, 0.0), Is.EqualTo(1));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, double.PositiveInfinity), Is.EqualTo(1));
            });
        }

        [Test]
        public void Resolve_Auto_BacksOffWhenTheCameraIsAlreadyBinning() {
            // The caller passes a pixel scale that ALREADY includes camera binning, so hardware 2x2 halves the
            // software factor rather than stacking on top of it.
            var native = PixelScale(5600);
            Assert.Multiple(() => {
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, native), Is.EqualTo(4));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, native * 2), Is.EqualTo(2));
                Assert.That(DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, native * 4), Is.EqualTo(1));
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
        public void ToSetting_RoundTripsAResolvedFactor() {
            for (var factor = 1; factor <= DetectionBinningResolver.MaxBinningFactor; ++factor) {
                var setting = DetectionBinningResolver.ToSetting(factor);
                Assert.That(DetectionBinningResolver.Resolve(setting, double.NaN), Is.EqualTo(factor));
            }
            Assert.That(DetectionBinningResolver.ToSetting(99), Is.EqualTo(DetectionBinningEnum.Bin4));
            Assert.That(DetectionBinningResolver.ToSetting(0), Is.EqualTo(DetectionBinningEnum.Bin1));
        }

        [Test]
        public void DescribeResolution_ReportsTheFactorAndItsReasoning() {
            var text = DetectionBinningResolver.DescribeResolution(DetectionBinningEnum.Auto, PixelScale(2800));
            Assert.Multiple(() => {
                Assert.That(text, Does.StartWith("Resolved: 2x"));
                Assert.That(text, Does.Contain("/px"));
                Assert.That(text, Does.Contain("HFR"));
            });

            var explicitText = DetectionBinningResolver.DescribeResolution(DetectionBinningEnum.Bin3, PixelScale(2800));
            Assert.That(explicitText, Does.StartWith("Using: 3x"));
        }

        [Test]
        public void DescribeResolution_UnknownPixelScale_SaysSoInsteadOfPrintingNaN() {
            var text = DetectionBinningResolver.DescribeResolution(DetectionBinningEnum.Auto, double.NaN);
            Assert.Multiple(() => {
                Assert.That(text, Does.Contain("pixel scale unknown"));
                Assert.That(text, Does.Not.Contain("NaN"));
            });
        }
    }
}
