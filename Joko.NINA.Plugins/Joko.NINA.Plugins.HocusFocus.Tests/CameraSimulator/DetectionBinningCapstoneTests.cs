using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// End-to-end capstones for software detection binning, on physically-rendered frames rather than synthetic
    /// rasters: the real <see cref="StarFieldCompositor"/> renders a star field at a given focal length, the real
    /// <see cref="StarDetector"/> reads it, and the real <see cref="DetectionBinningResolver"/> picks the factor.
    ///
    /// <para>Each case asserts the promise of the feature: Auto picks the factor the rig needs, the injected stars
    /// come back at their real positions in CAPTURED pixels, and the reported HFR is the same as an unbinned run's.
    /// Camera binning is applied with the simulator's own <see cref="HocusFocusSimulatorCamera.BinFrame"/>, so the
    /// stacked cases exercise the same charge-summing model NINA would see from the simulated camera.</para>
    /// </summary>
    [TestFixture]
    [Category("SlowIntegration")]
    public class DetectionBinningCapstoneTests {

        public sealed record Rig(string Name, double FocalLengthMm, double ApertureMm, int CameraBinning, int ExpectedAutoFactor);

        // Real-ish optical trains spanning the range the Auto rule has to cover, all on the scene's IMX533
        // (3008², 3.76 µm). Aperture tracks focal length so the frames stay realistically exposed.
        private static readonly Rig Refractor = new("130mm f/7 refractor", 910.0, 130.0, 1, 1);
        private static readonly Rig C11 = new("C11 @ f/10", 2800.0, 280.0, 1, 2);
        private static readonly Rig LongSct = new("SCT @ 4500mm", 4500.0, 320.0, 1, 3);
        private static readonly Rig LongSctBinned2 = new("SCT @ 5600mm, camera 2x2", 5600.0, 356.0, 2, 2);
        private static readonly Rig C11Binned2 = new("C11 @ f/10, camera 2x2", 2800.0, 280.0, 2, 1);

        // Bright enough to be unambiguous on every rig here, spread clear of the borders.
        private static readonly (double px, double py, double mag)[] Placements = {
            (600, 620, 10.5), (1500, 640, 10.9), (2400, 600, 11.3),
            (620, 1500, 11.1), (2400, 1500, 10.7),
            (600, 2400, 11.5), (1500, 2400, 10.3), (2400, 2400, 11.0),
        };

        private const double ExposureSeconds = 4.0;

        // Magnitude offset that puts the faint-field case near this rig's detection floor, where the SNR gain is
        // what decides the outcome. Empirically calibrated: at +6.5 mag the unbinned run finds 5 of 8 and the
        // binned run 7 of 8, so both are in a working regime rather than at total collapse.
        private const double FaintOffset = 6.5;

        /// <summary>Detector params for a capstone run. Only ModelPSF is forced off (as auto-focus does); every
        /// other knob is left at its documented default so these frames go through the real production gates.</summary>
        private static StarDetectorParams Params(int detectionBinning, double pixelScaleArcsecPerPixel) => new StarDetectorParams {
            DetectionBinning = detectionBinning,
            ModelPSF = false,
            // Matches ApplyDetectionImageContext: the detector reasons in binned pixels, so PixelScale carries the
            // factor and every arcsec-valued output stays physical.
            PixelScale = pixelScaleArcsecPerPixel * detectionBinning
        };

        private static ushort[] Render(Rig rig) {
            var projection = SyntheticCameraTestScene.Projection(rig.FocalLengthMm);
            var stars = Placements.Select(s => SyntheticCameraTestScene.StarAtPixel(projection, s.px, s.py, s.mag)).ToList();
            var reader = new FakeCatalogReader(stars);
            var request = SyntheticCameraTestScene.Request(
                SyntheticCameraTestScene.OptimalFocuserPosition,
                exposureSeconds: ExposureSeconds,
                focalLengthMillimeters: rig.FocalLengthMm,
                apertureMillimeters: rig.ApertureMm,
                limitingMagnitude: 13.0);
            var pixels = new StarFieldCompositor(reader).Render(request, CancellationToken.None);

            var sensor = SyntheticCameraTestScene.SensorDef;
            return rig.CameraBinning > 1
                ? HocusFocusSimulatorCamera.BinFrame(pixels, sensor.Width, sensor.Height, rig.CameraBinning, sensor.BitDepth)
                : pixels;
        }

        private static async Task<HocusFocusStarDetectorResult> DetectAsync(ushort[] pixels, Rig rig, StarDetectorParams p) {
            var sensor = SyntheticCameraTestScene.SensorDef;
            using var mat = CvImageUtility.ToOpenCVMat(
                pixels, sensor.BitDepth, sensor.Width / rig.CameraBinning, sensor.Height / rig.CameraBinning);
            return await new StarDetector(new AlglibAPI()).Detect(mat, p, null, CancellationToken.None);
        }

        /// <summary>Injected stars recovered within <paramref name="toleranceCapturedPx"/> of where they were
        /// placed, expressed in CAPTURED pixels (so a camera-binned rig's expectations shrink with the frame).</summary>
        private static int CountMatched(IReadOnlyList<Star> detected, int cameraBinning, double toleranceCapturedPx) {
            var matched = 0;
            foreach (var (px, py, _) in Placements) {
                var expectedX = px / cameraBinning;
                var expectedY = py / cameraBinning;
                if (detected.Any(d => Math.Sqrt((d.Center.X - expectedX) * (d.Center.X - expectedX) + (d.Center.Y - expectedY) * (d.Center.Y - expectedY)) <= toleranceCapturedPx)) {
                    ++matched;
                }
            }
            return matched;
        }

        private static double MedianHfr(IReadOnlyList<Star> stars) =>
            stars.Select(s => s.HFR).OrderBy(h => h).ElementAt(stars.Count / 2);

        private static IEnumerable<TestCaseData> Rigs() {
            yield return new TestCaseData(Refractor).SetName("A_Refractor_910mm_AutoStaysUnbinned");
            yield return new TestCaseData(C11).SetName("B_C11_2800mm_AutoPicks2x");
            yield return new TestCaseData(LongSct).SetName("C_Sct_4500mm_AutoPicks3x");
            yield return new TestCaseData(LongSctBinned2).SetName("D_Sct_5600mm_Camera2x_AutoAddsAnother2x");
            yield return new TestCaseData(C11Binned2).SetName("E_C11_Camera2x_AutoBacksOff");
        }

        [TestCaseSource(nameof(Rigs))]
        public async Task Auto_PicksTheDocumentedFactorAndPreservesEveryReportedValue(Rig rig) {
            var pixelScale = SyntheticCameraTestScene.PixelScaleArcsecPerPixel(rig.FocalLengthMm, rig.CameraBinning);
            var factor = DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, pixelScale);
            Assert.That(factor, Is.EqualTo(rig.ExpectedAutoFactor),
                $"{rig.Name}: {pixelScale:F3}\"/px, est. in-focus HFR {DetectionBinningResolver.EstimateInFocusHfrPixels(pixelScale):F1} px");

            var pixels = Render(rig);
            var unbinned = await DetectAsync(pixels, rig, Params(1, pixelScale));
            var binned = await DetectAsync(pixels, rig, Params(factor, pixelScale));

            var unbinnedHfr = MedianHfr(unbinned.DetectedStars);
            var binnedHfr = binned.DetectedStars.Count > 0 ? MedianHfr(binned.DetectedStars) : double.NaN;
            var unbinnedMatched = CountMatched(unbinned.DetectedStars, rig.CameraBinning, 6.0);
            var binnedMatched = CountMatched(binned.DetectedStars, rig.CameraBinning, 6.0);

            TestContext.WriteLine($"{rig.Name}: {pixelScale:F3}\"/px, detection binning {factor}x");
            TestContext.WriteLine($"  1x     : {unbinned.DetectedStars.Count} stars, {unbinnedMatched}/{Placements.Length} matched, median HFR {unbinnedHfr:F2} px");
            TestContext.WriteLine($"  {factor}x     : {binned.DetectedStars.Count} stars, {binnedMatched}/{Placements.Length} matched, median HFR {binnedHfr:F2} px");
            TestContext.WriteLine($"  binned HFR in analyzed pixels: {binnedHfr / factor:F2}");

            Assert.Multiple(() => {
                Assert.That(unbinnedMatched, Is.GreaterThanOrEqualTo(Placements.Length - 1),
                    "the field must be genuinely detectable unbinned, otherwise the comparison proves nothing");
                Assert.That(binnedMatched, Is.GreaterThanOrEqualTo(unbinnedMatched),
                    "binning must not lose stars");
                // HFR is reported in CAPTURED pixels at every factor, which is what keeps auto-focus curves
                // comparable. It is not IDENTICAL across factors, and the tolerance says so: binning raises the
                // per-pixel SNR, so more of each star's outer flux clears the measurement threshold and the
                // flux-weighted mean radius creeps up with the factor (~5% at 2x, ~16% at 3x on these frames).
                // The shift is a scale factor across the whole curve, so it does not move best focus - but HFR
                // values are not comparable BETWEEN factors, which is why changing the factor asks for a re-tune.
                Assert.That(binnedHfr, Is.EqualTo(unbinnedHfr).Within(0.20 * unbinnedHfr),
                    "the reported HFR must stay the star's real size in captured pixels, within the documented drift");
                Assert.That(binned.DetectionBinning, Is.EqualTo(factor));
            });
        }

        [Test]
        public async Task Auto_BringsInFocusHfrIntoTheDetectorsCalibratedBand() {
            // The reason the feature exists: at long focal length the unbinned in-focus HFR sits far above the
            // 2-4 px the pixel-unit gates are tuned for, and binning brings the ANALYZED size back into range.
            var rig = LongSct;
            var pixelScale = SyntheticCameraTestScene.PixelScaleArcsecPerPixel(rig.FocalLengthMm, rig.CameraBinning);
            var factor = DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, pixelScale);

            var pixels = Render(rig);
            var unbinned = await DetectAsync(pixels, rig, Params(1, pixelScale));
            var binned = await DetectAsync(pixels, rig, Params(factor, pixelScale));

            var capturedHfr = MedianHfr(unbinned.DetectedStars);
            var analyzedHfr = MedianHfr(binned.DetectedStars) / factor;
            TestContext.WriteLine($"{rig.Name}: captured HFR {capturedHfr:F2} px -> analyzed at {factor}x as {analyzedHfr:F2} px");

            Assert.Multiple(() => {
                Assert.That(capturedHfr, Is.GreaterThan(4.0), "this rig is genuinely oversampled");
                Assert.That(analyzedHfr, Is.InRange(1.5, 4.5), "and binning puts the analyzed star size back in range");
            });
        }

        [Test]
        public async Task Auto_RecoversFaintStarsThatTheUnbinnedRunMisses() {
            // The SNR half of the benefit: mean/summed binning lifts faint stars above the structure-map threshold.
            var rig = LongSct;
            var pixelScale = SyntheticCameraTestScene.PixelScaleArcsecPerPixel(rig.FocalLengthMm, rig.CameraBinning);
            var factor = DetectionBinningResolver.Resolve(DetectionBinningEnum.Auto, pixelScale);

            var projection = SyntheticCameraTestScene.Projection(rig.FocalLengthMm);
            // Deliberately near the detection floor for this rig (see FaintOffset).
            var faint = Placements.Select(s => SyntheticCameraTestScene.StarAtPixel(projection, s.px, s.py, s.mag + FaintOffset)).ToList();
            var request = SyntheticCameraTestScene.Request(
                SyntheticCameraTestScene.OptimalFocuserPosition,
                exposureSeconds: ExposureSeconds,
                focalLengthMillimeters: rig.FocalLengthMm,
                apertureMillimeters: rig.ApertureMm,
                limitingMagnitude: 16.0);
            var pixels = new StarFieldCompositor(new FakeCatalogReader(faint)).Render(request, CancellationToken.None);

            var unbinned = await DetectAsync(pixels, rig, Params(1, pixelScale));
            var binned = await DetectAsync(pixels, rig, Params(factor, pixelScale));

            var unbinnedMatched = CountMatched(unbinned.DetectedStars, rig.CameraBinning, 8.0);
            var binnedMatched = CountMatched(binned.DetectedStars, rig.CameraBinning, 8.0);
            TestContext.WriteLine($"faint field at {rig.Name}: 1x matched {unbinnedMatched}/{Placements.Length}, {factor}x matched {binnedMatched}/{Placements.Length}");

            Assert.That(binnedMatched, Is.GreaterThan(unbinnedMatched),
                "binning must recover stars the unbinned run misses - that is the SNR half of the benefit");
        }
    }
}
