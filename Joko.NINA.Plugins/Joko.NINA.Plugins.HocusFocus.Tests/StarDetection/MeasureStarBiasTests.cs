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

        [Test]
        public void MeasureStar_UniformDisk_SoftThresholdBarelyChangesHfr() {
            // A uniform disk has no radial gradient, so subtracting a constant τ scales every surviving pixel
            // equally and the flux-weighted radius is (almost) unchanged — only the partially-covered rim
            // shifts. This isolates WHY the F3 bias requires a gradient (contrast with the Gaussian below).
            const double a = 12.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticDefocusedStarImage.CreateDisk(Size, Size, Cx, Cy, a, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var pNoClip = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            var pClip = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0, HfrTauPolicy = TauClipPolicy.SubtractTau };
            var s0 = NewStar(bg);
            var sT = NewStar(bg);
            detector.MeasureStar(image, s0, pNoClip, 0.0);
            detector.MeasureStar(image, sT, pClip, 0.3); // τ = 1.0 * 0.3
            TestContext.WriteLine($"uniform disk: hfr(τ=0)={s0.HFR:F4} hfr(τ=0.3)={sT.HFR:F4} Δ={sT.HFR - s0.HFR:F4}");
            Assert.That(sT.HFR, Is.EqualTo(s0.HFR).Within(0.15));
        }

        [Test]
        public void MeasureStar_GaussianSoftThreshold_BiasesHfrDownward() {
            const double sigma = 5.0, peak = 1.0, bg = 0.0;
            using var image = SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, bg);
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0, HfrTauPolicy = TauClipPolicy.SubtractTau }; // τ == noiseSigma
            double[] taus = { 0.0, 0.02, 0.05, 0.10, 0.20 };
            var hfrs = new double[taus.Length];
            for (int i = 0; i < taus.Length; ++i) {
                var star = NewStar(bg);
                detector.MeasureStar(image, star, p, taus[i]);
                hfrs[i] = star.HFR;
                TestContext.WriteLine($"τ={taus[i]:F3} → HFR={hfrs[i]:F4} (bias {hfrs[i] - hfrs[0]:F4})");
            }
            for (int i = 1; i < hfrs.Length; ++i)
                Assert.That(hfrs[i], Is.LessThan(hfrs[i - 1]), $"HFR should decrease monotonically as τ grows (i={i})");
            Assert.That(hfrs[0] - hfrs[^1], Is.GreaterThan(0.1), "soft-threshold should bias HFR downward measurably at τ=0.2");
        }

        [Test]
        public void MeasureStar_GaussianSoftThreshold_RelativeBiasGrowsForFainterStars() {
            const double sigma = 5.0, bg = 0.0, tau = 0.05;
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0, HfrTauPolicy = TauClipPolicy.SubtractTau };
            double[] peaks = { 1.0, 0.5, 0.25, 0.125 };
            double prevRel = -1.0;
            foreach (var peak in peaks) {
                using var image = SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, bg);
                var s0 = NewStar(bg);
                var sT = NewStar(bg);
                detector.MeasureStar(image, s0, p, 0.0);
                detector.MeasureStar(image, sT, p, tau);
                double rel = (s0.HFR - sT.HFR) / s0.HFR;
                TestContext.WriteLine($"peak={peak:F3} τ/peak={tau / peak:F3} relBias={rel:P2}");
                if (prevRel >= 0)
                    Assert.That(rel, Is.GreaterThan(prevRel), "relative HFR bias should grow as the star gets fainter");
                prevRel = rel;
            }
        }

        [Test]
        public void MeasureStar_UnderstatedSigma_InflatesHfrUnderNoise() {
            // A small star in a large aperture surrounded by noise. With correctly-scaled τ the noise is
            // clipped; with an understated σ (production measures σ on a ~4-5× smoothed copy but samples the
            // sharp image) positive noise at large radius leaks into the flux sum and inflates HFR (F4).
            const double sigma = 3.0, peak = 1.0, bg = 0.0;
            const double sigmaSharp = 0.02; // true white-noise σ of the measurement image
            const int seed = 12345;
            const int bigSize = 121;        // R = 60.5 = 20σ → a large pure-noise annulus inside the aperture
            const double bigC = 60.0;
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 2.0, HfrTauPolicy = TauClipPolicy.SubtractTau };

            double MeasureWith(double noiseSigmaArg) {
                using var image = SyntheticGaussianStarImage.Create(bigSize, bigSize, bigC, bigC, sigma, sigma, peak, bg);
                SyntheticDefocusedStarImage.AddGaussianNoise(image, sigmaSharp, seed);
                var star = new Star {
                    Center = new Point2d(bigC, bigC),
                    StarBoundingBox = new Rect(0, 0, bigSize, bigSize),
                    Background = bg
                };
                detector.MeasureStar(image, star, p, noiseSigmaArg);
                return star.HFR;
            }

            double hfrTrue = MeasureWith(sigmaSharp);              // τ = 2·σ_sharp     → noise clipped
            double hfrUnderstated = MeasureWith(sigmaSharp / 5.0); // τ = 2·σ_sharp/5 → noise leaks in
            TestContext.WriteLine($"HFR(trueσ)={hfrTrue:F4}  HFR(understatedσ)={hfrUnderstated:F4}  inflation={hfrUnderstated - hfrTrue:F4}");
            Assert.That(hfrUnderstated, Is.GreaterThan(hfrTrue + 0.5), "understated σ should inflate HFR by leaking large-radius noise");
        }

        [Test]
        public void MeasureStar_Annulus_HfrExceedsFilledDisk_SameOuterRadius() {
            const double outer = 14.0, inner = 8.0, peak = 1.0, bg = 0.0;
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 0.0 };
            using var disk = SyntheticDefocusedStarImage.CreateDisk(Size, Size, Cx, Cy, outer, peak, bg);
            using var annulus = SyntheticDefocusedStarImage.CreateAnnulus(Size, Size, Cx, Cy, inner, outer, peak, bg);
            var sd = NewStar(bg);
            var sa = NewStar(bg);
            detector.MeasureStar(disk, sd, p, 0.0);
            detector.MeasureStar(annulus, sa, p, 0.0);
            TestContext.WriteLine($"disk HFR={sd.HFR:F4}  annulus HFR={sa.HFR:F4}");
            Assert.That(sa.HFR, Is.GreaterThan(sd.HFR), "annulus pushes flux outward → larger HFR than a filled disk of the same outer radius");
        }
    }
}
