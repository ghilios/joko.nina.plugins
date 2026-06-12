using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    /// <summary>
    /// Pins the F4 mechanism: a Gaussian blur of the default noise-reduction kernel cuts white-noise σ by the
    /// inverse L2 norm of the 2D kernel — theoretically ≈3.96 for radius 3 / kernel 7 (measured ≈3.95 below) —
    /// so σ estimated on the blurred structure-map copy badly understates the σ of the sharp image that star
    /// measurement samples. The planned ×0.2 recalibration of the gate knobs (Sensitivity 10→2.0,
    /// StarClippingMultiplier 2.0→0.4) treats this factor as 5, so at defaults it is a slight effective
    /// loosening: old effective gate ≈ 10/3.96 ≈ 2.53×σ_sharp vs new 2.0×σ_sharp, i.e. ~21% more permissive —
    /// the real-data before/after AF-run sweep is the arbiter of whether that is acceptable. This factor holds
    /// for pure white noise; in the full detector pipeline the default hotpixel filter median-filters
    /// high-noise images first, which compresses the effective sharp/blurred ratio well below 3.96 (measured
    /// 1.73 on the synthetic field in SigmaConsistencyDetectorTests).
    /// </summary>
    [TestFixture]
    public class SigmaConsistencyTests {

        [Test]
        public void KappaSigma_GaussianBlurredWhiteNoise_SigmaShrinksRoughlyFourFold() {
            const int size = 512;
            const double noiseSigma = 0.02;
            const int radius = 3; // default NoiseReductionRadius → kernel = 2*3+1 = 7
            // Background 0.2 = 10× noise σ above zero: KappaSigmaNoiseEstimate masks pixels to
            // (float.Epsilon, threshold] from iteration 2, so the background must sit ≫ σ above zero to keep
            // the negative noise tail in-frame and the σ estimate unbiased.
            using var sharp = SyntheticGaussianStarImage.CreateFlat(size, size, 0.2f);
            SyntheticDefocusedStarImage.AddGaussianNoise(sharp, noiseSigma, seed: 777);
            using var blurred = sharp.Clone();
            CvImageUtility.ConvolveGaussian(blurred, blurred, radius * 2 + 1);

            var sharpEstimate = CvImageUtility.KappaSigmaNoiseEstimate(sharp, clippingMultipler: 4.0);
            var blurredEstimate = CvImageUtility.KappaSigmaNoiseEstimate(blurred, clippingMultipler: 4.0);
            double ratio = sharpEstimate.Sigma / blurredEstimate.Sigma;
            TestContext.WriteLine($"σ_sharp={sharpEstimate.Sigma:F6} σ_blurred={blurredEstimate.Sigma:F6} ratio={ratio:F2}");

            Assert.Multiple(() => {
                Assert.That(sharpEstimate.Sigma, Is.EqualTo(noiseSigma).Within(0.3 * noiseSigma),
                    "kappa-sigma should recover the true white-noise σ on the sharp image");
                Assert.That(ratio, Is.InRange(3.0, 5.0),
                    "blur should understate σ by ≈1/‖kernel‖₂ ≈ 3.96 for kernel 7; the planned ×0.2 " +
                    "recalibration rounds this to 5, making the new 2.0×σ_sharp gate ~21% looser at defaults " +
                    "than the old effective 2.53×σ_sharp");
            });
        }
    }
}
