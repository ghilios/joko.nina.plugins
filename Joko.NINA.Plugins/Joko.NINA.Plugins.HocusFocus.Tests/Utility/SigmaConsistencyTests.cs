using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    /// <summary>
    /// Pins the F4 mechanism: a Gaussian blur of the default noise-reduction kernel cuts white-noise σ by a
    /// large factor (≈ the inverse L2 norm of the 2D kernel, ~4-5× for radius 3 / kernel 7), so σ estimated on
    /// the blurred structure-map copy badly understates the σ of the sharp image that star measurement samples.
    /// The recalibration constants in StarDetectionOptions (10→2.0, 2.0→0.4, i.e. ×0.2) assume this factor.
    /// </summary>
    [TestFixture]
    public class SigmaConsistencyTests {

        [Test]
        public void KappaSigma_GaussianBlurredWhiteNoise_SigmaShrinksRoughlyFiveFold() {
            const int size = 512;
            const double noiseSigma = 0.02;
            const int radius = 3; // default NoiseReductionRadius → kernel = 2*3+1 = 7
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
                Assert.That(ratio, Is.InRange(3.0, 7.0),
                    "blur should understate σ by roughly the factor the recalibration constants assume (~5×)");
            });
        }
    }
}
