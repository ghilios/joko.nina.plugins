using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Full-pipeline pin for the F4 fix (two named σ estimates) and its recalibration. The pin test below is
    /// written BEFORE the σ switch and must keep passing AFTER it. <see cref="BuildField"/> and
    /// <see cref="MismatchPathParams"/> are internal for reuse by the upcoming F4 tests in this fixture.
    ///
    /// Measured corridor on this field (mismatch path, default knobs). The default hotpixel filter
    /// (threshold 0.001 ≪ noise σ 0.02) median-filters essentially every pixel, so the sharp measurement
    /// image has σ̂_sharp ≈ 0.0086 (not the raw 0.02) while the blurred structure copy has σ̂_blurred ≈ 0.0050
    /// — a ratio of 1.73, not the pure white-noise 3.96 pinned in SigmaConsistencyTests. The resulting bars on
    /// the gate's brightness measure (NormalizedBrightness = peak − 0.25·meanFlux, sharp image):
    ///   old gate (Sensitivity 10 × σ̂_blurred)              ≈ 0.050
    ///   planned new gate (Sensitivity 2.0 × σ̂_sharp)       ≈ 0.017  (= 3.46 × σ̂_blurred)
    ///   structure binarization (median + 4.0 × σ̂_blurred)  ≈ median + 0.020
    /// Because the new gate bar sits BELOW the binarization bar, a star faint enough to be rejected by the
    /// new gate can never binarize into a candidate: stars "rejected by the sensitivity gate in both regimes"
    /// cannot exist on this path. Empirically, faint stars tuned to reach the gate measure
    /// NormalizedBrightness ≈ 6.4–9.0 × σ̂_blurred (0.032–0.045) — LowSensitivity under the old gate but
    /// ACCEPTED under the post-F4-equivalent gate (Sensitivity=3.46 ⇒ detected=17), which would break this
    /// pin. The faint stars are therefore pinned below binarization instead (see FaintStars), and the pin is
    /// designed so no star's brightness measure falls between the two gate bars: bright stars sit far above
    /// both, faint stars never become candidates.
    /// </summary>
    [TestFixture]
    public class SigmaConsistencyDetectorTests {
        private const int Size = 256;
        private const double NoiseSigma = 0.02;
        private const double Background = 0.05;
        private const double StarSigmaPx = 2.5;
        private const int Seed = 424242;

        // 12 bright stars (peaks 0.40-0.80 = 20-40× raw noise σ). Their brightness measure is ≥ 8× the old
        // gate bar (0.050) and ≥ 23× the planned new bar (0.017), so they pass the gate decisively in both
        // regimes.
        private static readonly (double x, double y, double peak)[] BrightStars = {
            (30, 30, 0.80), (90, 30, 0.70), (150, 30, 0.60), (210, 30, 0.50),
            (30, 90, 0.80), (90, 90, 0.70), (150, 90, 0.60), (210, 90, 0.50),
            (30, 150, 0.45), (90, 150, 0.45), (150, 150, 0.40), (210, 150, 0.40),
        };

        // 8 faint stars (peak 0.020 = 1.0× raw noise σ ≈ 2.3× σ̂_sharp). They stay below the structure-map
        // binarization bar and never become candidates (binarization onset on this field is between peak
        // 0.0225 and 0.025, a ≥12% amplitude margin), so StructureCandidates stays at 12 and LowSensitivity
        // at 0. Binarization legitimately uses the blurred-map σ and is untouched by F4, so this cull is
        // identical in both regimes — verified by simulating the post-F4 knobs on current code
        // (Sensitivity=3.46 ≡ 2.0×σ̂_sharp, StarClippingMultiplier=0.692 ≡ 0.4×σ̂_sharp): detected=12,
        // candidates=12 either way.
        // REVIEW FOLLOW-UP: the original intent was to pin the sensitivity gate itself (LowSensitivity == 8),
        // but that is empirically impossible in both regimes at default knobs (see class doc) — this pin's
        // faint-star coverage is the binarization cull, not the gate.
        private const double FaintPeak = 0.020;

        private static readonly (double x, double y)[] FaintStars = {
            (30, 210), (60, 210), (90, 210), (120, 210),
            (150, 210), (180, 210), (210, 210), (240, 210),
        };

        internal static Mat BuildField() {
            var mat = SyntheticStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y, peak) in BrightStars) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, peak);
            foreach (var (x, y) in FaintStars) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, FaintPeak);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        internal static StarDetectorParams MismatchPathParams() => new StarDetectorParams {
            // The F4 mismatch path: a noise-reduction radius is set but measurement noise reduction is off,
            // so the structure copy is blurred while the sharp srcImage is what gets measured. NoiseClippingMultiplier
            // is pinned to the pre-audit default (4.0) so this F4 mismatch-path test stays calibrated; the
            // golden-set recall audit lowered the production default to 2.0 separately.
            StarMeasurementNoiseReductionEnabled = false,
            NoiseReductionRadius = 3,
            NoiseClippingMultiplier = 4.0,
        };

        [Test]
        public async Task Detect_DefaultKnobsOnMismatchPath_DetectsExactlyTheBrightStars() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            var result = await detector.Detect(image, MismatchPathParams(), null, CancellationToken.None);
            var metrics = result.Metrics;
            TestContext.WriteLine(
                $"detected={result.DetectedStars.Count} candidates={metrics.StructureCandidates} " +
                $"lowSensitivity={metrics.LowSensitivity} tooSmall={metrics.TooSmall}");
            Assert.Multiple(() => {
                Assert.That(result.DetectedStars.Count, Is.EqualTo(12),
                    "default knobs should accept all 12 bright stars and reject all 8 faint ones — this " +
                    "count must be identical before and after the σ switch + recalibration");
                Assert.That(metrics.StructureCandidates, Is.EqualTo(12),
                    "the 8 faint stars must be culled by structure-map binarization (F4-invariant) and " +
                    "never reach the per-candidate filters");
                Assert.That(metrics.LowSensitivity, Is.EqualTo(0),
                    "no star may occupy the sensitivity-gate band: a star gate-rejected today would be " +
                    "accepted post-F4, because the new bar (2.0×σ̂_sharp ≈ 0.017) sits below the binarization " +
                    "bar (4.0×σ̂_blurred ≈ 0.020) on this path");
            });
        }

        [Test]
        public async Task Detect_MismatchPath_ExposesHonestMeasurementSigmaAndSmallerStructureSigma() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            // HotpixelFiltering off: the default hotpixel median compresses the sharp image's sigma to ~0.43x raw
            // (threshold 0.001 << sigma_n), which would break the comparison to the injected noise level. This
            // test probes sigma THREADING (which image each estimate is computed on), not default-knob behavior.
            var p = MismatchPathParams();
            p.HotpixelFiltering = false;
            var result = await detector.Detect(image, p, null, CancellationToken.None);
            TestContext.WriteLine($"σ_measure={result.MeasurementNoiseSigma:F6} σ_structure={result.StructureNoiseSigma:F6}");
            Assert.Multiple(() => {
                Assert.That(result.MeasurementNoiseSigma, Is.EqualTo(NoiseSigma).Within(0.4 * NoiseSigma),
                    "measurement σ must be estimated on the sharp srcImage (the image MeasureStar samples)");
                Assert.That(result.StructureNoiseSigma, Is.LessThan(0.5 * result.MeasurementNoiseSigma),
                    "structure σ comes from the blurred copy and must be much smaller on this path");
            });
        }

        [Test]
        public async Task Detect_ConsistentPaths_ReuseStructureSigmaAsMeasurementSigma() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());

            // Path 1: measurement noise reduction ON → srcImage is the blurred image → identical estimates.
            var nrOn = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = true, NoiseReductionRadius = 3 };
            using var image1 = image.Clone();
            var r1 = await detector.Detect(image1, nrOn, null, CancellationToken.None);
            // Path 2: no noise reduction at all → no blur anywhere → identical estimates.
            var noNr = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = false, NoiseReductionRadius = 0 };
            using var image2 = image.Clone();
            var r2 = await detector.Detect(image2, noNr, null, CancellationToken.None);

            Assert.Multiple(() => {
                Assert.That(r1.MeasurementNoiseSigma, Is.EqualTo(r1.StructureNoiseSigma),
                    "with measurement NR on, the images are identical and must yield identical σ — today guaranteed by reusing the same estimate; a deterministic recompute refactor may relax this to Within(1e-12)");
                Assert.That(r2.MeasurementNoiseSigma, Is.EqualTo(r2.StructureNoiseSigma),
                    "with radius 0 the images are identical and must yield identical σ — today guaranteed by reusing the same estimate; a deterministic recompute refactor may relax this to Within(1e-12)");
            });
        }

        [Test]
        public async Task Detect_SensitivityGate_UsesMeasurementSigma() {
            // Stars at 5σ_n: under the OLD code the gate at Sensitivity=10 was applied against the blurred copy's
            // σ (~0.005), an effective bar of ~0.05 NB that 5σ stars clear; with honest σ (0.02, hotpixel off),
            // Sensitivity=10 means a true 10σ bar (~0.2 NB) and they must be rejected. FAILS before the fix.
            var mat = SyntheticStarField.CreateFlat(Size, Size, (float)Background);
            var positions = new (double x, double y)[] { (40, 40), (120, 40), (200, 40), (40, 120), (120, 120) };
            foreach (var (x, y) in positions) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, 5.0 * NoiseSigma);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            using var image = mat;

            var p = MismatchPathParams();
            p.HotpixelFiltering = false; // same rationale as above: probe honest σ against the raw noise level
            p.Sensitivity = 10.0; // explicit: probe the honest meaning of "10σ"
            var detector = new StarDetector(new AlglibAPI());
            var result = await detector.Detect(image, p, null, CancellationToken.None);
            TestContext.WriteLine($"detected={result.DetectedStars.Count} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.Multiple(() => {
                Assert.That(result.DetectedStars.Count, Is.EqualTo(0),
                    "5σ stars must fail an honest 10σ sensitivity gate");
                Assert.That(result.Metrics.LowSensitivity, Is.GreaterThanOrEqualTo(positions.Length),
                    "the faint stars must be rejected specifically by the sensitivity gate");
            });
        }
    }
}
