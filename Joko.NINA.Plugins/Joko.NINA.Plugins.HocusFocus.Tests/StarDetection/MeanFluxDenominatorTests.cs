using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Pins the F11 fix: NormalizedBrightness's meanFlux must divide by the clip-survivor count
    /// (numUnclippedPixels), not the full structure-footprint count (starPoints.Count). The buggy denominator
    /// understates meanFlux and inflates NB; the fix lowers NB, tightening the sensitivity gate.
    ///
    /// The field's stars have a large clipped footprint fraction so the NB shift is wide and the gate flip is
    /// robust: each star is a sharp bright core (so peak is high and the surviving pixels' median stays well
    /// below PeakResponse·peak — passes the TooFlat gate) plus a broad halo that registers in the wavelet
    /// structure map (inflating starPoints.Count) but is mostly clipped at the elevated StarClippingMultiplier,
    /// so the survivor fraction is small. With these stars the per-star sensitivity (NB/σ) is ~69.5–70.9
    /// pre-fix and ~65.8–67.9 post-fix; a gate at 68.5 sits cleanly between, accepting all four pre-fix and
    /// rejecting all four (via the sensitivity gate specifically) post-fix.
    /// </summary>
    [TestFixture]
    public class MeanFluxDenominatorTests {
        private const int Size = 256;
        private const double Background = 0.05;
        private const double NoiseSigma = 0.02;
        private const int Seed = 515151;

        // Sharp bright core (σ=1.5, peak=0.80) keeps the peak high and the surviving-pixel median far below
        // PeakResponse·peak (passes TooFlat). Broad halo (σ=10, peak=0.18) registers in the structure map so it
        // inflates the footprint, but at StarClippingMultiplier=8 most halo pixels fall below the clip margin and
        // are excluded from totalFlux — a small survivor fraction, which is the regime where the survivor-count
        // denominator (post-fix) yields a much larger meanFlux than the footprint-count denominator (pre-fix).
        private const double CoreSigma = 1.5;
        private const double CorePeak = 0.80;
        private const double HaloSigma = 10.0;
        private const double HaloPeak = 0.18;
        private const double StarClippingMultiplier = 8.0;

        // Sensitivity gate placed between the inflated (pre-fix) per-star NB/σ (~69.5–70.9, all accepted) and the
        // honest (post-fix) NB/σ (~65.8–67.9, all rejected). 68.5 gives ~0.6σ margin on each side.
        private const double GateSensitivity = 68.5;

        private static readonly (double x, double y)[] Centers = {
            (64, 64), (192, 64), (64, 192), (192, 192),
        };

        private static OpenCvSharp.Mat BuildField() {
            var mat = SyntheticHaloStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y) in Centers) {
                SyntheticHaloStarField.AddCoreHaloStar(mat, x, y,
                    coreSigma: CoreSigma, corePeak: CorePeak, haloSigma: HaloSigma, haloPeak: HaloPeak);
            }
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        // The mismatch path (NR radius set, measurement NR off) so σ_measure is the honest sharp σ.
        private static StarDetectorParams Params(double sensitivity) => new StarDetectorParams {
            StarMeasurementNoiseReductionEnabled = false,
            NoiseReductionRadius = 3,
            Sensitivity = sensitivity,
            StarClippingMultiplier = StarClippingMultiplier,
        };

        [Test]
        public void Detect_HighSensitivityGate_RejectsCoreHaloStarsOnHonestMeanFlux() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            var result = detector.Detect(image, Params(GateSensitivity), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.Multiple(() => {
                Assert.That(result.DetectedStars.Count, Is.EqualTo(0),
                    "with the honest survivor-mean meanFlux, NB drops below the gate for these clipped-halo stars");
                Assert.That(result.Metrics.LowSensitivity, Is.GreaterThanOrEqualTo(Centers.Length),
                    "the stars must be rejected specifically by the sensitivity gate");
            });
        }
    }
}
