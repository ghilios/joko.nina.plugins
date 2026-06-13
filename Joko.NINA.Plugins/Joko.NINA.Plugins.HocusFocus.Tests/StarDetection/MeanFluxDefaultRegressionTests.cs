using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Non-regression guard for the F11 meanFlux fix at the SHIPPED default. The fix (divide meanFlux by the
    /// clip-survivor count) only depresses NormalizedBrightness materially when the structure footprint is much
    /// larger than the clip-survivor set — a regime reached at elevated StarClippingMultiplier / StructureLayers
    /// (see MeanFluxDenominatorTests, which forces that regime to pin the fix's direction). At the shipped class
    /// default (Sensitivity = 2.0, StarClippingMultiplier = 2.0, StructureLayers = 4) the sensitivity gate is
    /// non-binding for ordinary stars, so the fix is behavior-preserving — the real-data sweeps confirmed zero
    /// accepted-count change at every shipped preset (None/Low/Typical/High). This test pins that property: a
    /// field of ordinary core+halo stars is detected in full at the default Sensitivity after the fix.
    /// </summary>
    [TestFixture]
    public class MeanFluxDefaultRegressionTests {
        private const int Size = 256;
        private const double Background = 0.05;
        private const double NoiseSigma = 0.02;
        private const int Seed = 626262;

        private static readonly (double x, double y)[] Centers = {
            (64, 64), (192, 64), (64, 192), (192, 192),
        };

        private static OpenCvSharp.Mat BuildField() {
            var mat = SyntheticHaloStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y) in Centers) {
                SyntheticHaloStarField.AddCoreHaloStar(mat, x, y, coreSigma: 1.5, corePeak: 0.55, haloSigma: 8.0, haloPeak: 0.03);
            }
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        [Test]
        public void Detect_ShippedDefaultSensitivity_DetectsAllOrdinaryStars() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            // Class-default Sensitivity (2.0) and StarClippingMultiplier (2.0); only the NR mismatch path is set so
            // sigma_measure is the honest sharp sigma. These ordinary bright-core stars sit well above the default
            // gate, so the survivor-mean meanFlux fix must not drop any of them.
            var p = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = false, NoiseReductionRadius = 3 };
            var result = detector.Detect(image, p, null, CancellationToken.None).GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} sensitivity={p.Sensitivity} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.That(result.DetectedStars.Count, Is.EqualTo(Centers.Length),
                "the shipped default gate must accept these ordinary stars after the F11 fix (yield preserved)");
        }
    }
}
