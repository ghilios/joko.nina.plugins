using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// Tier-B realism capstones for the far-from-focus AutoFocus robustness battery
    /// (<c>AutoFocusEngineSweepBatteryTests</c>). The deterministic Tier-A battery scripts its detector; these tests
    /// anchor Tier A's degradation assumptions to real physics: full frames rendered by the real
    /// <see cref="StarFieldCompositor"/> and read by the real <see cref="StarDetector"/>, with degradation induced
    /// only via PHYSICAL levers (star magnitude, sky brightness, exposure, defocus distance).
    /// <list type="bullet">
    ///   <item><b>R1</b> — a field that detects cleanly at focus goes (near-)starless far from focus: the premise of
    ///   the F1/F2/B-series cutoff scenarios.</item>
    ///   <item><b>R2</b> — on a low-SNR frame far from focus, whatever the detector reports is a small population of
    ///   LOW-HFR blobs, never real high-HFR donuts: the premise of the A3/AB1 false-minimum scenarios (spurious far
    ///   detections mimic good focus because noise blobs are compact).</item>
    /// </list>
    /// Renders are seeded (<c>NoiseSeed</c>), so the detection outcomes asserted here are deterministic.
    /// </summary>
    [TestFixture]
    [Category("SlowIntegration")]
    public class CameraSimulatorAfDegradationCapstoneTests {

        // Star field shared by both capstones: 8 faint stars spread across the frame, clear of the borders.
        // Bright enough to be unambiguous at focus with the default scene sky/exposure, faint enough that their
        // far-defocus donuts (flux spread over hundreds of pixels) sink below the noise. (Tuned empirically: at
        // mag ~13 the +120-step donuts were STILL cleanly detected — the real detector is remarkably donut-tolerant —
        // hence the fainter field and deeper defocus here.)
        private static readonly (double px, double py, double mag)[] Placements = {
            (500, 500, 14.6), (1500, 520, 14.8), (2500, 500, 15.0),
            (520, 1500, 14.7), (2500, 1500, 15.2),
            (500, 2500, 15.4), (1500, 2500, 14.9), (2500, 2500, 15.1),
        };

        private const int FarOffsetSteps = 240; // 1200 µm of defocus — far beyond any sane AF window

        /// <summary>
        /// R1 — faint field far from focus ⇒ the real detector goes (near-)starless. Confirms the regime Tier A's
        /// cutoff knob scripts as "(0, 0, 0) beyond N steps": detection does not degrade gracefully far out, it
        /// collapses, which is exactly what F1/F2 and the B-series reversal scenarios assume.
        /// </summary>
        [Test]
        public async Task R1_FaintField_DetectsAtFocus_GoesStarlessFarFromFocus() {
            var projection = SyntheticCameraTestScene.Projection();
            var stars = Placements.Select(s => SyntheticCameraTestScene.StarAtPixel(projection, s.px, s.py, s.mag)).ToList();
            var reader = new FakeCatalogReader(stars);

            // Near-focus frame: every star should be an easy detection.
            var nearRequest = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition);
            var nearDetected = await Detect(reader, nearRequest);
            var nearMatched = CountMatched(nearDetected);

            // Far frame: the same field, 120 steps out. The defocus model predicts a huge donut; the per-pixel
            // signal collapses by the donut's area ratio and the detector should find essentially nothing.
            var farRequest = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + FarOffsetSteps);
            var expectedDonutHfr = ExpectedHfr(farRequest, FarOffsetSteps);
            var farDetected = await Detect(reader, farRequest);
            var farMatched = CountMatched(farDetected);

            TestContext.WriteLine($"near: detected={nearDetected.Count} matched={nearMatched}/{Placements.Length}");
            TestContext.WriteLine($"far (+{FarOffsetSteps} steps, expected donut HFR {expectedDonutHfr:F1}px): detected={farDetected.Count} matched={farMatched}");

            Assert.Multiple(() => {
                Assert.That(nearMatched, Is.GreaterThanOrEqualTo(6),
                    "the field must be genuinely detectable at focus — otherwise 'goes starless far out' proves nothing");
                Assert.That(farMatched, Is.LessThanOrEqualTo(1),
                    "far from focus the real stars vanish (their donuts sink below the noise)");
                Assert.That(farDetected.Count, Is.LessThanOrEqualTo(3),
                    "the far frame is (near-)starless overall — the F1/F2 cutoff regime is physically real");
            });
        }

        /// <summary>
        /// R2 — low-SNR frame far from focus ⇒ any detections the real detector does report are FEW and LOW-HFR
        /// (compact noise blobs), never the real ~13 px donuts. This is precisely the A3/AB1 premise: far-from-focus
        /// spurious detections carry deceptively LOW HFR, so they can fake a focus minimum — and nothing detected far
        /// out ever reports the true (high) defocus HFR.
        /// </summary>
        [Test]
        public async Task R2_LowSnrFarFromFocus_AnyDetectionsAreFewAndLowHfr() {
            var projection = SyntheticCameraTestScene.Projection();
            var stars = Placements.Select(s => SyntheticCameraTestScene.StarAtPixel(projection, s.px, s.py, s.mag)).ToList();
            var reader = new FakeCatalogReader(stars);

            // Harsher physics than R1: bright sky + short exposure — the classic low-SNR AF frame.
            const double harshSkyMagPerArcsec2 = 17.5;
            const double harshExposureSeconds = 0.6;

            var farRequest = SyntheticCameraTestScene.Request(
                SyntheticCameraTestScene.OptimalFocuserPosition + FarOffsetSteps,
                exposureSeconds: harshExposureSeconds) with {
                SkyBrightnessMagPerArcsec2 = harshSkyMagPerArcsec2
            };
            var expectedDonutHfr = ExpectedHfr(farRequest, FarOffsetSteps);

            var farDetected = await Detect(reader, farRequest);
            var farMatched = CountMatched(farDetected);
            var hfrs = farDetected.Select(d => d.HFR).OrderByDescending(h => h).ToList();

            TestContext.WriteLine($"far low-SNR (+{FarOffsetSteps} steps, sky {harshSkyMagPerArcsec2}, {harshExposureSeconds}s): " +
                $"detected={farDetected.Count} matched={farMatched} expectedDonutHFR={expectedDonutHfr:F1}px " +
                $"HFRs=[{string.Join(", ", hfrs.Select(h => h.ToString("F2")))}]");

            Assert.Multiple(() => {
                // A small spurious population is tolerated (that is the point); a large one would mean the detector
                // hallucinates whole star fields, which Tier A does not model.
                Assert.That(farDetected.Count, Is.LessThanOrEqualTo(12),
                    "far-out low-SNR frames yield at most a small population of detections");
                Assert.That(farMatched, Is.LessThanOrEqualTo(1), "none of the real stars' donuts survive the noise");
                // The A3/AB1 premise: anything reported far out is a compact (low-HFR) blob, nothing resembling the
                // true defocus donut. A detection at ~the donut HFR would be a REAL measurement, not a spurious one.
                Assert.That(farDetected.Where(d => d.HFR >= 0.5 * expectedDonutHfr), Is.Empty,
                    "no far-out detection reports anywhere near the true defocus HFR — spurious detections are low-HFR");
            });
        }

        // ------------------------------------------------------------------------------------------------------------

        private static async Task<List<Star>> Detect(FakeCatalogReader reader, RenderRequest request) {
            var sensor = SyntheticCameraTestScene.SensorDef;
            var pixels = new StarFieldCompositor(reader).Render(request, CancellationToken.None);
            using var mat = CvImageUtility.ToOpenCVMat(pixels, sensor.BitDepth, sensor.Width, sensor.Height);
            var result = await new StarDetector(new AlglibAPI()).Detect(mat, new StarDetectorParams(), null, CancellationToken.None);
            return result.DetectedStars ?? new List<Star>();
        }

        /// <summary>Injected stars recovered within 6 px of their projected pixel (the capstone matching convention).</summary>
        private static int CountMatched(List<Star> detected) {
            var matched = 0;
            foreach (var (px, py, _) in Placements) {
                var hit = detected.Any(d => Math.Sqrt((d.Center.X - px) * (d.Center.X - px) + (d.Center.Y - py) * (d.Center.Y - py)) < 6.0);
                if (hit) {
                    ++matched;
                }
            }
            return matched;
        }

        private static double ExpectedHfr(RenderRequest request, int focuserOffsetSteps) {
            var model = DefocusModel.FromRequest(request, SyntheticCameraTestScene.SensorDef, SyntheticCameraTestScene.FilterDef);
            return model.HfrAtDefocusMicrons(focuserOffsetSteps * SyntheticCameraTestScene.FocuserStepSizeMicrons);
        }
    }
}
