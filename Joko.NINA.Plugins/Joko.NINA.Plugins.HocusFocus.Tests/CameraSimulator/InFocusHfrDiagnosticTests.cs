using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// DIAGNOSTIC harness for "the optimization wizard reports a lower in-focus HFR than the optics can produce".
    ///
    /// <para>The simulator carries a closed-form truth model (<see cref="DefocusModel"/>:
    /// <c>HFR(d) = sqrt(HFR_min² + (κ·d)²)</c>, with <c>HFR_min = 1.2533·σ_min</c>), so every stage of the chain
    /// can be measured against a known answer rather than an assumption. The chain is:</para>
    /// <list type="number">
    ///   <item>the optics — what HFR the rendered frames should have (DefocusModel);</item>
    ///   <item>per-frame detection — what the star detector measures on each rendered frame;</item>
    ///   <item>the curve fit — what the optimizer's hyperbola reports as the minimum, which is the number the
    ///   wizard's binning recommendation reads.</item>
    /// </list>
    /// <para>Each test prints its numbers so a regression shows WHERE the chain diverges, not just that it did.</para>
    /// </summary>
    [TestFixture]
    [Category("SlowIntegration")]
    public class InFocusHfrDiagnosticTests {

        // The rig from the manual repro steps: IMX533 (3008², 3.76 µm) on a C11 at f/10, simulator seeing default.
        private const double FocalLengthMm = 2800.0;
        private const double ApertureMm = 280.0;
        private const double ExposureSeconds = 4.0;

        // A realistic field rather than a handful of bright stars: 12x12 grid, magnitudes spanning bright to near
        // the detection floor, so the per-frame aggregate is dominated by ordinary stars the way a real frame is.
        private static IReadOnlyList<(double px, double py, double mag)> Field(double brightestMag, double faintestMag) {
            var placements = new List<(double, double, double)>();
            const int n = 12;
            const double margin = 260.0;
            var span = 3008.0 - 2 * margin;
            for (var iy = 0; iy < n; iy++) {
                for (var ix = 0; ix < n; ix++) {
                    // Deterministic jitter so stars do not land on a perfect lattice (which would alias with the
                    // detector's structure map), and a deterministic magnitude ramp across the field.
                    var jx = ((ix * 7 + iy * 13) % 11 - 5) * 6.0;
                    var jy = ((ix * 5 + iy * 17) % 11 - 5) * 6.0;
                    var t = (iy * n + ix) / (double)(n * n - 1);
                    placements.Add((margin + ix * span / (n - 1) + jx, margin + iy * span / (n - 1) + jy,
                        brightestMag + t * (faintestMag - brightestMag)));
                }
            }
            return placements;
        }

        private static RenderRequest Request(int focuserPosition) => SyntheticCameraTestScene.Request(
            focuserPosition,
            exposureSeconds: ExposureSeconds,
            focalLengthMillimeters: FocalLengthMm,
            apertureMillimeters: ApertureMm,
            limitingMagnitude: 16.0);

        private static DefocusModel Truth() => DefocusModel.FromRequest(
            Request(SyntheticCameraTestScene.OptimalFocuserPosition),
            SyntheticCameraTestScene.SensorDef,
            SyntheticCameraTestScene.FilterDef);

        /// <summary>Renders one frame and returns the accepted stars, detected exactly the way auto-focus does
        /// (default knobs, ModelPSF off, pixel scale from the rig).</summary>
        private static async Task<IReadOnlyList<Star>> DetectFrameAsync(int focuserPosition, IReadOnlyList<(double px, double py, double mag)> field) {
            var projection = SyntheticCameraTestScene.Projection(FocalLengthMm);
            var stars = field.Select(s => SyntheticCameraTestScene.StarAtPixel(projection, s.px, s.py, s.mag)).ToList();
            var request = Request(focuserPosition);
            var pixels = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, CancellationToken.None);

            var sensor = SyntheticCameraTestScene.SensorDef;
            using var mat = CvImageUtility.ToOpenCVMat(pixels, sensor.BitDepth, sensor.Width, sensor.Height);
            var p = new StarDetectorParams {
                ModelPSF = false,
                PixelScale = SyntheticCameraTestScene.PixelScaleArcsecPerPixel(FocalLengthMm)
            };
            var result = await new StarDetector(new AlglibAPI()).Detect(mat, p, null, CancellationToken.None);
            return result.DetectedStars ?? new List<Star>();
        }

        /// <summary>The per-frame aggregate the auto-focus curve is built from: the median HFR over the stars that
        /// survive the same saturation exclusion <c>HocusFocusStarDetection</c> applies.</summary>
        private static double FrameHfr(IReadOnlyList<Star> stars, out int usedCount) {
            var forHfr = HocusFocusStarDetection.StarsForHfrAggregation(stars, excludeSaturated: true, saturationThreshold: 0.99);
            usedCount = forHfr.Count;
            if (forHfr.Count == 0) {
                return double.NaN;
            }
            return forHfr.Select(s => s.HFR).OrderBy(h => h).ElementAt(forHfr.Count / 2);
        }

        [Test]
        public async Task Stage1_InFocusFrame_MedianHfrMatchesTheOpticsModel() {
            var truth = Truth();
            var field = Field(brightestMag: 9.0, faintestMag: 14.5);
            var stars = await DetectFrameAsync(SyntheticCameraTestScene.OptimalFocuserPosition, field);
            var measured = FrameHfr(stars, out var used);

            var hfrs = stars.Select(s => s.HFR).OrderBy(h => h).ToList();
            TestContext.WriteLine($"rig: {FocalLengthMm}mm f/{FocalLengthMm / ApertureMm:F1}, {truth.ArcsecPerPixel:F4}\"/px, seeing {SyntheticCameraTestScene.SeeingArcsec}\"");
            TestContext.WriteLine($"optics truth  HFR_min = {truth.HfrMinPixels:F3} px  (sigma_seeing {truth.SigmaSeeingPixels:F3} px)");
            TestContext.WriteLine($"detected {stars.Count} stars, {used} used for HFR; median = {measured:F3} px");
            if (hfrs.Count > 0) {
                TestContext.WriteLine($"  HFR percentiles: p10={Pct(hfrs, 0.10):F2} p25={Pct(hfrs, 0.25):F2} p50={Pct(hfrs, 0.50):F2} p75={Pct(hfrs, 0.75):F2} p90={Pct(hfrs, 0.90):F2}");
            }

            Assert.That(measured, Is.EqualTo(truth.HfrMinPixels).Within(0.15 * truth.HfrMinPixels),
                "the detector's in-focus HFR must track the optics model; a large shortfall here means the MEASUREMENT is biased, not the fit");
        }

        /// <summary>
        /// The whole chain at several sweep WIDTHS. A narrow sweep barely defocuses (the curve is almost flat);
        /// a real auto-focus sweep reaches several times the in-focus HFR at its ends. If the reported minimum
        /// depends on how far out the sweep goes, the far points are the problem, not the fit or the detector.
        /// </summary>
        [TestCase(10)]
        [TestCase(40)]
        [TestCase(80)]
        [TestCase(160)]
        [TestCase(320)]
        public async Task Stage2_SweepWidth_ChangesTheReportedMinimum(int stepSize) {
            var truth = Truth();
            var field = Field(brightestMag: 9.0, faintestMag: 14.5);
            var positions = SweepPositions(stepSize);

            TestContext.WriteLine($"step size {stepSize} ({OffsetSteps} per side)   optics HFR_min = {truth.HfrMinPixels:F3} px, kappa = {truth.KappaPixelsPerStep:F4} px/step");
            TestContext.WriteLine("pos        truth   measured  ratio   stars");
            var points = new List<ScatterErrorPoint>();
            foreach (var pos in positions) {
                var stars = await DetectFrameAsync(pos, field);
                var measured = FrameHfr(stars, out var used);
                var expected = truth.HfrAtFocuserPosition(pos);
                TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-9} {1,6:F2}  {2,7:F2}   {3,5:F2}   {4,4}", pos, expected, measured, measured / expected, used));
                if (double.IsFinite(measured) && measured > 0) {
                    points.Add(new ScatterErrorPoint(pos, measured, 0, 0.01));
                }
            }

            var model = AlglibHyperbolicFitting.SelectBestModel(
                new AlglibAPI(), points, stepSize, useWeights: false, maxOutlierRejections: 0, rejectionConfidence: 0.0,
                out var bestFit, out _);

            var vertex = bestFit?.Minimum.Y ?? double.NaN;
            TestContext.WriteLine($"=> model {model}, vertex {vertex:F3} px, R2 {bestFit?.RSquared:F4}; vertex/truth = {vertex / truth.HfrMinPixels:F3}");
            Assert.Pass("diagnostic output only");
        }

        /// <summary>
        /// Compares three ways of answering "how big is an in-focus star on this rig" across sweep widths:
        /// the fitted hyperbola's vertex (what the wizard reads today), the smallest measured point, and the
        /// measurement at the frame nearest the fitted best-focus POSITION. Also prints the fit's R², to check
        /// whether it separates the sweeps whose far frames have gone bad from the ones that are still sound.
        /// </summary>
        [TestCase(10)]
        [TestCase(40)]
        [TestCase(80)]
        [TestCase(160)]
        [TestCase(320)]
        public async Task Stage4_CandidateEstimators_AcrossSweepWidths(int stepSize) {
            var truth = Truth();
            var field = Field(brightestMag: 9.0, faintestMag: 14.5);

            var measuredByPosition = new Dictionary<int, double>();
            var points = new List<ScatterErrorPoint>();
            foreach (var pos in SweepPositions(stepSize)) {
                var stars = await DetectFrameAsync(pos, field);
                var measured = FrameHfr(stars, out _);
                if (double.IsFinite(measured) && measured > 0) {
                    measuredByPosition[pos] = measured;
                    points.Add(new ScatterErrorPoint(pos, measured, 0, 0.01));
                }
            }

            AlglibHyperbolicFitting.SelectBestModel(
                new AlglibAPI(), points, stepSize, useWeights: false, maxOutlierRejections: 0, rejectionConfidence: 0.0,
                out var bestFit, out _);

            var vertexY = bestFit?.Minimum.Y ?? double.NaN;
            var rSquared = bestFit?.RSquared ?? double.NaN;
            var minMeasured = measuredByPosition.Values.Min();
            var nearestToFitX = double.NaN;
            if (bestFit != null && double.IsFinite(bestFit.Minimum.X)) {
                var nearest = measuredByPosition.OrderBy(kv => Math.Abs(kv.Key - bestFit.Minimum.X)).First();
                nearestToFitX = nearest.Value;
            }

            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "step {0,-4}  R2 {1,8:F4}  truth {2:F2}  | vertex {3,7:F2} (x{4:F2})  minPoint {5,6:F2} (x{6:F2})  nearestToFitX {7,6:F2} (x{8:F2})",
                stepSize, rSquared, truth.HfrMinPixels,
                vertexY, vertexY / truth.HfrMinPixels,
                minMeasured, minMeasured / truth.HfrMinPixels,
                nearestToFitX, nearestToFitX / truth.HfrMinPixels));
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "         recommended factor: from vertex {0}, from minPoint {1}, from nearestToFitX {2}, from truth {3}",
                DetectionBinningResolver.RecommendFromHfr(vertexY),
                DetectionBinningResolver.RecommendFromHfr(minMeasured),
                DetectionBinningResolver.RecommendFromHfr(nearestToFitX),
                DetectionBinningResolver.RecommendFromHfr(truth.HfrMinPixels)));
            Assert.Pass("diagnostic output only");
        }

        // The sweep the wizard captures: offsetSteps per side at stepSize, centered on focus.
        private const int StepSize = 80;
        private const int OffsetSteps = 4;

        private static IReadOnlyList<int> SweepPositions(int stepSize = StepSize) {
            var positions = new List<int>();
            for (var i = -OffsetSteps; i <= OffsetSteps; i++) {
                positions.Add(SyntheticCameraTestScene.OptimalFocuserPosition + i * stepSize);
            }
            return positions;
        }

        private static double Pct(IReadOnlyList<double> sorted, double q) =>
            sorted[Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Round(q * (sorted.Count - 1))))];
    }
}
