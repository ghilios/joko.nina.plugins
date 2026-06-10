using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Pins MeasureStar's HFR against analytic ground truth on controlled synthetic shapes, and measures the
    /// magnitude of two confirmed biases: F3 (the soft-threshold τ is subtracted from each pixel, pulling HFR
    /// down on stars with a radial gradient) and F4 (an understated σ lets large-radius noise inflate HFR).
    /// All inputs are deterministic (seeded noise); bias tests assert direction + a bounded magnitude and log
    /// the measured numbers via TestContext so the magnitudes are visible without being brittle.
    /// </summary>
    [TestFixture]
    public class MeasureStarBiasTests {
        private const int Size = 81;       // R = min(bbox)/2 = 40.5, large vs the shapes below (low truncation)
        private const double Cx = 40.0;
        private const double Cy = 40.0;

        private static Star NewStar(double background) => new Star {
            Center = new Point2d(Cx, Cy),
            StarBoundingBox = new Rect(0, 0, Size, Size),
            Background = background
        };

        [Test]
        public void MeasureStar_CleanDisk_MatchesTwoThirdsRadius() {
            const double a = 12.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticDefocusedStarImage.CreateDisk(Size, Size, Cx, Cy, a, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var star = NewStar(bg);
            Assert.That(detector.MeasureStar(image, star, p, 0.0), Is.True);
            double truth = HfrGroundTruth.Disk(a); // 8.0
            TestContext.WriteLine($"clean disk: measured={star.HFR:F4} truth={truth:F4}");
            Assert.That(star.HFR, Is.EqualTo(truth).Within(0.3));
        }

        [Test]
        public void MeasureStar_CleanAnnulus_MatchesClosedForm() {
            const double inner = 6.0, outer = 14.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticDefocusedStarImage.CreateAnnulus(Size, Size, Cx, Cy, inner, outer, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var star = NewStar(bg);
            Assert.That(detector.MeasureStar(image, star, p, 0.0), Is.True);
            double truth = HfrGroundTruth.Annulus(inner, outer); // ~10.53
            TestContext.WriteLine($"clean annulus: measured={star.HFR:F4} truth={truth:F4}");
            Assert.That(star.HFR, Is.EqualTo(truth).Within(0.3));
        }

        [Test]
        public void MeasureStar_CleanGaussian_MatchesSigmaRootPiOverTwo() {
            const double sigma = 4.0, peak = 1.0, bg = 0.0; // R = 40.5 = 10σ → truncation negligible
            using var image = SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var star = NewStar(bg);
            Assert.That(detector.MeasureStar(image, star, p, 0.0), Is.True);
            double truth = HfrGroundTruth.Gaussian(sigma); // ~5.013
            TestContext.WriteLine($"clean gaussian: measured={star.HFR:F4} truth={truth:F4}");
            Assert.That(star.HFR, Is.EqualTo(truth).Within(0.3));
        }
    }
}
