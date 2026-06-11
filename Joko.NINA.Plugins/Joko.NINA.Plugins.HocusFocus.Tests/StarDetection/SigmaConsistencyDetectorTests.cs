using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Full-pipeline tests for the F4 fix (two named σ estimates) and its recalibration. The pin test below
    /// is written BEFORE the σ switch and must keep passing AFTER it: old code at old defaults and new code at
    /// recalibrated defaults must detect the same star set (effective thresholds preserved by design).
    /// </summary>
    [TestFixture]
    public class SigmaConsistencyDetectorTests {
        private const int Size = 256;
        private const double NoiseSigma = 0.02;
        private const double Background = 0.05;
        private const double StarSigmaPx = 2.5;
        private const int Seed = 424242;

        // 12 bright stars (peaks 20-40× noise σ: decisively above the sensitivity gate in both the old
        // effective regime (~2σ_sharp) and the recalibrated honest regime (2.0σ_sharp)).
        private static readonly (double x, double y, double peak)[] BrightStars = {
            (30, 30, 0.80), (90, 30, 0.70), (150, 30, 0.60), (210, 30, 0.50),
            (30, 90, 0.80), (90, 90, 0.70), (150, 90, 0.60), (210, 90, 0.50),
            (30, 150, 0.45), (90, 150, 0.45), (150, 150, 0.40), (210, 150, 0.40),
        };

        // 8 ultra-faint stars (peak 1.5× noise σ: above the structure-map binarize threshold — which sits at
        // ~0.8σ_n because it legitimately uses the smoothed-image σ — but decisively below the sensitivity
        // gate in both regimes, so they pin the gate, not structure detection).
        private static readonly (double x, double y)[] FaintStars = {
            (30, 210), (60, 210), (90, 210), (120, 210),
            (150, 210), (180, 210), (210, 210), (240, 210),
        };

        internal static OpenCvSharp.Mat BuildField() {
            var mat = SyntheticStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y, peak) in BrightStars) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, peak);
            foreach (var (x, y) in FaintStars) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, 1.5 * NoiseSigma);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        internal static StarDetectorParams MismatchPathParams() => new StarDetectorParams {
            // The F4 mismatch path: a noise-reduction radius is set but measurement noise reduction is off,
            // so the structure copy is blurred while the sharp srcImage is what gets measured. All other
            // values stay at class defaults so this test tracks the default knob recalibration.
            StarMeasurementNoiseReductionEnabled = false,
            NoiseReductionRadius = 3,
        };

        [Test]
        public void Detect_DefaultKnobsOnMismatchPath_DetectsExactlyTheBrightStars() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            var result = detector.Detect(image, MismatchPathParams(), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.That(result.DetectedStars.Count, Is.EqualTo(12),
                "default knobs should accept all 12 bright stars and reject all 8 ultra-faint ones — " +
                "this count must be identical before and after the σ switch + recalibration");
        }
    }
}
