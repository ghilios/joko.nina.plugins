using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    /// <summary>
    /// Capstones: prove the whole synthetic pipeline (ASTAP-shaped catalog → projection → local defocus →
    /// PSF → stamp → noise → <c>ushort[]</c>) recovers the known truth it was built from, using the real
    /// <see cref="StarDetector"/> as an independent reader. Kept fast with the smallest sensor (IMX533) and a
    /// handful of stars.
    /// </summary>
    [TestFixture]
    public class CameraSimulatorCapstoneTests {

        /// <summary>
        /// (a) Detector recovery — render one near-focus frame from a fixed fake catalog, then confirm the real
        /// detector finds every injected star at its projected pixel with the defocus-model HFR.
        /// </summary>
        [Test]
        public async Task DetectorRecoversInjectedStars_PositionAndHfr() {
            var sensor = SyntheticCameraTestScene.SensorDef;
            int width = sensor.Width, height = sensor.Height;
            var projection = SyntheticCameraTestScene.Projection();

            // A handful of stars spread across the frame (kept clear of the borders so none are rejected on-edge).
            var placements = new (double px, double py, double mag)[] {
                (500, 500, 10.0), (1500, 520, 10.5), (2500, 500, 11.0),
                (520, 1500, 10.3), (2500, 1500, 10.8),
                (500, 2500, 11.2), (1500, 2500, 10.6), (2500, 2500, 10.4),
            };
            var stars = placements.Select(s => SyntheticCameraTestScene.StarAtPixel(projection, s.px, s.py, s.mag)).ToList();

            // Focuser 30 steps past optimal ⇒ a uniform Δ = 150 µm (aberrations off) so every star is a comfortably
            // sampled ~2.5 px blob — clearly above the detector's MinHFR floor.
            const int focuserOffset = 30;
            var request = SyntheticCameraTestScene.Request(SyntheticCameraTestScene.OptimalFocuserPosition + focuserOffset);
            var expectedHfr = ExpectedHfr(request, focuserOffset);

            var pixels = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, CancellationToken.None);
            using var mat = CvImageUtility.ToOpenCVMat(pixels, sensor.BitDepth, width, height);
            var result = await new StarDetector(new AlglibAPI()).Detect(mat, new StarDetectorParams(), null, CancellationToken.None);
            var detected = result.DetectedStars ?? new List<Star>();

            TestContext.WriteLine($"injected={stars.Count} detected={detected.Count} expectedHFR={expectedHfr:F3}");

            // Every injected star must be recovered at its projected position, with HFR near the model prediction.
            // Each star was placed by deprojecting (px,py); the TAN round-trip is exact, so the projected pixel is
            // (px,py) — assert the detector lands there.
            var matched = 0;
            foreach (var (px, py, mag) in placements) {
                double sx = px, sy = py;
                var near = detected
                    .Select(d => (d, dist: Math.Sqrt((d.Center.X - sx) * (d.Center.X - sx) + (d.Center.Y - sy) * (d.Center.Y - sy))))
                    .OrderBy(t => t.dist)
                    .FirstOrDefault();
                Assert.That(near.d, Is.Not.Null, $"no detection near injected star at ({sx:F0},{sy:F0})");
                Assert.That(near.dist, Is.LessThan(6.0), $"nearest detection {near.dist:F2}px from injected ({sx:F0},{sy:F0})");
                TestContext.WriteLine($"  ({sx:F0},{sy:F0}) mag {mag}: matched d={near.dist:F2}px HFR={near.d.HFR:F3}");
                // Detector HFR carries known biases; allow a generous band around the analytic prediction.
                Assert.That(near.d.HFR, Is.EqualTo(expectedHfr).Within(0.30 * expectedHfr));
                ++matched;
            }

            Assert.That(matched, Is.EqualTo(placements.Length), "every injected star recovered");
            // No meaningful spurious detections on a low-sky, bright-star frame.
            Assert.That(detected.Count, Is.LessThanOrEqualTo(placements.Length + 2));
        }

        private static double ExpectedHfr(RenderRequest request, int focuserOffsetSteps) {
            var model = DefocusModel.FromRequest(request, SyntheticCameraTestScene.SensorDef, SyntheticCameraTestScene.FilterDef);
            return model.HfrAtDefocusMicrons(focuserOffsetSteps * SyntheticCameraTestScene.FocuserStepSizeMicrons);
        }

        /// <summary>
        /// (c) The eccentricity capstone — the acceptance test for the astigmatism model. Inject a known tilt +
        /// backfocus error, render one frame, and confirm the <b>real detector</b> measures stars elongated
        /// <b>tangentially</b> on one edge, <b>radially</b> on the opposite one, and round in the middle.
        ///
        /// <para>Predicted for this scene (IMX533, N = 7.0, σ_min = 1.32 px, 2Np = 52.64 µm/px, corner
        /// r′² = 63.96e6 µm², tilt 120 µm at 0°, backfocus 40 µm, corner residual 15 µm, focuser at best focus).
        /// The split is a₂·r′² with a₂ = K/2 + a_c/r_c² = 5.47e-7, so A is the same at both edges and it is Δ
        /// that changes sign:</para>
        /// <list type="table">
        /// <item><description>left edge x≈300: Δ = +83.2 µm, A = +11.2 µm ⇒ (a_rad, a_tan) = (1.37, 1.79) px ⇒ <b>tangential</b></description></item>
        /// <item><description>right edge x≈2700: Δ = −108.1 µm, A = +11.1 µm ⇒ (2.26, 1.84) px ⇒ <b>radial</b></description></item>
        /// <item><description>centre: r′ = 0 ⇒ Δ = A = 0 ⇒ round</description></item>
        /// </list>
        ///
        /// <para><b>Kept near focus deliberately.</b> A strongly elliptical <i>donut</i> fits a Moffat poorly and
        /// can fail <c>PSFGoodnessOfFitThreshold</c> outright, taking <c>PSF.Eccentricity</c> with it — so
        /// "improving" this test by defocusing harder would break it.</para>
        ///
        /// <para><b>Measured</b> (for the record, since the thresholds below are set from these): orientation
        /// scores −0.98 left / +0.97 right; eccentricity 0.26 at both edges against 0.11 at the centre. The edge
        /// figures come in below what the closed form predicts because <c>PSF.Eccentricity</c> is an FWHM ratio
        /// from a Moffat fit while the prediction is a second moment — they agree in ordering, not in magnitude,
        /// exactly as the design spec says. The centre's 0.11 is the fit's own noise floor, which is why "round"
        /// is asserted as clearly-less-than-the-edges rather than as zero.</para>
        /// </summary>
        [Test]
        public async Task AstigmatismRendersRadialAndTangentialEdgesThroughFullPipeline() {
            var measured = await MeasureEdgeElongation(backfocusErrorMicrons: 40.0, tiltAmountMicrons: 120.0,
                cornerAstigmatismMicrons: 15.0);

            Assert.Multiple(() => {
                // Radial-vs-tangential score: +1 = the major axis points at the optical axis, −1 = across it.
                Assert.That(measured.LeftScore, Is.LessThan(-0.5), "left edge is tangentially elongated");
                Assert.That(measured.RightScore, Is.GreaterThan(0.5), "right edge is radially elongated");
                Assert.That(measured.LeftEccentricity, Is.GreaterThan(0.20), "and measurably elongated at all");
                Assert.That(measured.RightEccentricity, Is.GreaterThan(0.20));
                Assert.That(measured.CentreEccentricity, Is.LessThan(0.15), "the on-axis stars stay round");
            });
        }

        /// <summary>
        /// (c′) <b>Pure tilt, perfect spacing</b> — the case a user hits first, and the one this model exists to
        /// get right. A tilted sensor cannot change the beam's aberrations; it only chooses where along each beam
        /// it samples. So the elongation here is not made by the tilt at all: it is the corrector's own residual
        /// split a_c, which is present everywhere and always, being <i>revealed</i> because tilt drags one edge to
        /// Δ &gt; 0 and the other to Δ &lt; 0 against that fixed split. Opposite edges therefore elongate
        /// perpendicular to each other with no backfocus error anywhere in the scene.
        ///
        /// <para>An earlier model made the split proportional to the local spacing error, which tilt drives. That
        /// makes A flip sign in lockstep with Δ, so Δ·A &lt; 0 everywhere and every edge elongates radially by the
        /// same small amount — no perpendicular pair at any tilt magnitude. This test is what distinguishes the
        /// two, so it is not redundant with (c) above.</para>
        ///
        /// <para>Predicted: a₂ = a_c/r_c² = 6.25e-7 with backfocus 0, so left edge Δ = +96.1 µm, A = +12.8 µm ⇒
        /// (1.58, 2.07) px <b>tangential</b>; right edge Δ = −95.4 µm, A = +12.6 µm ⇒ (2.05, 1.57) px
        /// <b>radial</b>. a_c = 40 µm rather than the shipped 15 µm so the ellipticity clears the Moffat fit's own
        /// noise floor — at 15 µm the prediction is a real but marginal e ≈ 0.25.</para>
        /// </summary>
        [Test]
        public async Task PureTilt_WithPerfectSpacing_StillFlipsRadialToTangentialAcrossTheField() {
            var measured = await MeasureEdgeElongation(backfocusErrorMicrons: 0.0, tiltAmountMicrons: 120.0,
                cornerAstigmatismMicrons: 40.0);

            Assert.Multiple(() => {
                Assert.That(measured.LeftScore, Is.LessThan(-0.5), "left edge is tangentially elongated");
                Assert.That(measured.RightScore, Is.GreaterThan(0.5), "right edge is radially elongated");
                Assert.That(measured.LeftEccentricity, Is.GreaterThan(0.20));
                Assert.That(measured.RightEccentricity, Is.GreaterThan(0.20));
                Assert.That(measured.CentreEccentricity, Is.LessThan(0.15), "the on-axis stars stay round");
            });
        }

        /// <summary>
        /// (c″) <b>Tilt that carries the corrector</b> — the second astigmatism mechanism, and the one that
        /// makes a badly tilted rig look badly tilted. The residual term (c′) is fixed while tilt drives Δ
        /// without bound, so its axis ratio decays toward 1: push the tilt far enough and the corners render
        /// <i>round</i>, and any corner can still be brought to a perfect point focus. A real tilt is rarely
        /// the detector alone — a sagging focuser or a non-square thread tilts the corrector too, which
        /// displaces the astigmatic node and adds a split proportional to the tilt itself.
        ///
        /// <para>Two things distinguish it from (c′), and both are asserted: the elongation is <b>radial on
        /// both edges</b> rather than perpendicular (Δ and the split flip together, so Δ·A &lt; 0 everywhere),
        /// and it is <b>much stronger at the same tilt</b>. Predicted for 150 µm of tilt with c_t = 0.25 and a
        /// perfect corrector: left edge Δ = +120.1, A = −30.0 ⇒ (2.85, 1.71) px, right edge Δ = −119.3,
        /// A = +29.8 ⇒ (2.83, 1.70) px — axis ratio 1.67 = (1+c_t)/(1−c_t) at both, and at any tilt.</para>
        /// </summary>
        [Test]
        public async Task TiltThatCarriesTheCorrector_ElongatesRadiallyOnBothEdges_AndFarMoreThanTheResidualAlone() {
            var carried = await MeasureEdgeElongation(backfocusErrorMicrons: 0.0, tiltAmountMicrons: 150.0,
                cornerAstigmatismMicrons: 0.0, tiltAstigmatismFraction: 0.25);
            var residualOnly = await MeasureEdgeElongation(backfocusErrorMicrons: 0.0, tiltAmountMicrons: 150.0,
                cornerAstigmatismMicrons: 15.0, tiltAstigmatismFraction: 0.0);

            Assert.Multiple(() => {
                Assert.That(carried.LeftScore, Is.GreaterThan(0.5), "radial on the left edge");
                Assert.That(carried.RightScore, Is.GreaterThan(0.5), "and radial on the right edge too");
                Assert.That(carried.LeftEccentricity, Is.GreaterThan(0.35));
                Assert.That(carried.RightEccentricity, Is.GreaterThan(0.35));
                Assert.That(carried.CentreEccentricity, Is.LessThan(0.15), "the node is still on axis, so the centre is round");

                // The contrast is the point: same tilt, same frame, and the residual alone has already begun
                // to wash out at a tilt this size.
                Assert.That(carried.LeftEccentricity, Is.GreaterThan(residualOnly.LeftEccentricity + 0.10));
                Assert.That(residualOnly.LeftScore, Is.LessThan(0.0),
                    "the residual mechanism is perpendicular across the field, not radial on both sides");
            });
        }

        /// <summary>
        /// (d) The spacing-direction diagnostic, end to end — and its <b>limit</b>. Pure backfocus with no tilt,
        /// so Δ = −K·r′² is set by the curvature alone and reversing the spacer reverses it. The split does
        /// <i>not</i> reverse with it: A = (C/2 + a_c)·r′²/r_c² carries the corrector's residual, which the spacer
        /// cannot touch. So the familiar "swap a spacer and the corners rotate 90°" holds only while the residual
        /// still outvotes the induced term, i.e. |C| &lt; 2·a_c; past that both directions read radial.
        ///
        /// <para>That window is the honest form of a widely repeated rule. Reports of the flip and flat denials of
        /// it (Roland Christen's among them) are both consistent with this: which one a given observer sees
        /// depends on how their spacing error compares with their corrector's residual astigmatism.</para>
        ///
        /// <para>a_c = 120 µm here — a deliberately poor corrector — so the window reaches |C| &lt; 240 µm and the
        /// stars inside it are still large enough for a reliable Moffat fit. Inside (±100 µm): a₂ = 2.66e-6 and
        /// 1.09e-6 respectively, giving left-edge (1.64, 0.43) px radial versus (0.18, 1.04) px tangential.
        /// Outside (±300 µm) both signs give a_rad &gt; a_tan.</para>
        /// </summary>
        [Test]
        public async Task ReversingTheBackfocusError_FlipsRadialToTangential_OnlyInsideTheResidualWindow() {
            const double residual = 120.0;
            var (positive, negative) = (
                await MeasureEdgeElongation(backfocusErrorMicrons: 100.0, tiltAmountMicrons: 0.0, cornerAstigmatismMicrons: residual),
                await MeasureEdgeElongation(backfocusErrorMicrons: -100.0, tiltAmountMicrons: 0.0, cornerAstigmatismMicrons: residual));
            var (farPositive, farNegative) = (
                await MeasureEdgeElongation(backfocusErrorMicrons: 300.0, tiltAmountMicrons: 0.0, cornerAstigmatismMicrons: residual),
                await MeasureEdgeElongation(backfocusErrorMicrons: -300.0, tiltAmountMicrons: 0.0, cornerAstigmatismMicrons: residual));

            Assert.Multiple(() => {
                Assert.That(positive.LeftScore, Is.GreaterThan(0.5), "inside the window, too-long spacing: left edge radial");
                Assert.That(positive.RightScore, Is.GreaterThan(0.5), "and the right edge too");
                Assert.That(negative.LeftScore, Is.LessThan(-0.5), "reversed: left edge tangential");
                Assert.That(negative.RightScore, Is.LessThan(-0.5), "and the right edge too");

                // Same ellipse, rotated -- so the measured elongation should be comparable, not merely present.
                Assert.That(positive.LeftEccentricity, Is.GreaterThan(0.20));
                Assert.That(negative.LeftEccentricity, Is.GreaterThan(0.20));
                Assert.That(positive.CentreEccentricity, Is.LessThan(0.15), "the on-axis stars stay round either way");
                Assert.That(negative.CentreEccentricity, Is.LessThan(0.15));

                // ...and past the window the flip is simply gone. Asserted, not merely noted, because a model that
                // flipped at every magnitude would pass every assertion above and still be wrong.
                Assert.That(farPositive.LeftScore, Is.GreaterThan(0.5), "outside the window both spacing directions read radial");
                Assert.That(farNegative.LeftScore, Is.GreaterThan(0.5));
            });
        }

        private readonly record struct EdgeElongation(
            double LeftScore, double RightScore, double LeftEccentricity, double RightEccentricity, double CentreEccentricity);

        private static async Task<EdgeElongation> MeasureEdgeElongation(
                double backfocusErrorMicrons, double tiltAmountMicrons, double cornerAstigmatismMicrons,
                double tiltAstigmatismFraction = 0.0) {
            var sensor = SyntheticCameraTestScene.SensorDef;
            int width = sensor.Width, height = sensor.Height;
            var projection = SyntheticCameraTestScene.Projection();
            double centreX = width / 2.0, centreY = height / 2.0;

            var regions = new (string name, double x, double y)[] { ("left", 300, 1504), ("right", 2700, 1504), ("centre", 1504, 1504) };
            var offsets = new (double dx, double dy)[] { (0, -220), (0, -110), (0, 0), (0, 110), (0, 220) };
            var placements = regions
                .SelectMany(r => offsets.Select(o => (r.name, x: r.x + o.dx, y: r.y + o.dy)))
                .ToArray();
            var stars = placements.Select(p => SyntheticCameraTestScene.StarAtPixel(projection, p.x, p.y, 10.2)).ToList();

            // Focuser AT best focus, so the centre is exactly on the surface and the pattern is symmetric.
            var request = SyntheticCameraTestScene.Request(
                SyntheticCameraTestScene.OptimalFocuserPosition,
                aberrationsEnabled: true, tiltAngleDegrees: 0.0, tiltAmountMicrons: tiltAmountMicrons,
                backfocusErrorMicrons: backfocusErrorMicrons,
                astigmatismEnabled: true, cornerAstigmatismMicrons: cornerAstigmatismMicrons,
                tiltAstigmatismFraction: tiltAstigmatismFraction);

            var pixels = new StarFieldCompositor(new FakeCatalogReader(stars)).Render(request, CancellationToken.None);
            using var mat = CvImageUtility.ToOpenCVMat(pixels, sensor.BitDepth, width, height);
            var result = await new StarDetector(new AlglibAPI()).Detect(mat, new StarDetectorParams(), null, CancellationToken.None);
            var detected = (result.DetectedStars ?? new List<Star>()).Where(s => s.PSF != null).ToList();

            var scores = new Dictionary<string, List<double>>();
            var eccentricities = new Dictionary<string, List<double>>();
            foreach (var (name, px, py) in placements) {
                var near = detected
                    .Select(d => (d, dist: Math.Sqrt((d.Center.X - px) * (d.Center.X - px) + (d.Center.Y - py) * (d.Center.Y - py))))
                    .OrderBy(t => t.dist)
                    .FirstOrDefault();
                if (near.d == null || near.dist > 6.0) {
                    continue;
                }

                // PSFModel reports theta for its fitted x axis, so the MAJOR axis is theta only when FWHMx is the
                // larger of the two -- otherwise it is a quarter turn away.
                var psf = near.d.PSF;
                var majorTheta = psf.FWHMx >= psf.FWHMy ? psf.ThetaRadians : psf.ThetaRadians + Math.PI / 2.0;

                // The radial direction in the same convention. InspectorVM draws an angle theta as
                // (cos theta, -sin theta) "since y is inverted to render top-down", so an image-space direction
                // (dx, dy) corresponds to atan2(-dy, dx) -- hence the negated y here. Derived, not fitted.
                var radialTheta = Math.Atan2(-(py - centreY), px - centreX);

                // cos(2 dtheta) folds the mod-pi ambiguity away: +1 radial, -1 tangential, 0 at 45 degrees.
                var score = Math.Cos(2.0 * (majorTheta - radialTheta));
                if (!scores.TryGetValue(name, out var list)) {
                    scores[name] = list = new List<double>();
                    eccentricities[name] = new List<double>();
                }
                list.Add(score);
                eccentricities[name].Add(psf.Eccentricity);
            }

            double Median(string region, Dictionary<string, List<double>> source) {
                Assert.That(source.TryGetValue(region, out var values) && values.Count >= 3, Is.True,
                    $"need at least 3 PSF-modelled stars in the {region} region, got {(source.TryGetValue(region, out var v) ? v.Count : 0)}");
                var sorted = values.OrderBy(x => x).ToList();
                return sorted[sorted.Count / 2];
            }

            var measured = new EdgeElongation(
                Median("left", scores), Median("right", scores),
                Median("left", eccentricities), Median("right", eccentricities), Median("centre", eccentricities));
            TestContext.WriteLine(
                $"backfocus {backfocusErrorMicrons:F0} µm, tilt {tiltAmountMicrons:F0} µm, residual {cornerAstigmatismMicrons:F0} µm, c_t {tiltAstigmatismFraction:F2}: leftScore={measured.LeftScore:F3} rightScore={measured.RightScore:F3} "
                + $"e(left)={measured.LeftEccentricity:F3} e(right)={measured.RightEccentricity:F3} e(centre)={measured.CentreEccentricity:F3}");
            return measured;
        }

        /// <summary>
        /// (b) Aberration inject ⇄ recover through the full pipeline. Inject a known tilt + backfocus, render a
        /// stepped-focuser run, detect each star per frame, fit each star's best-focus focuser position from its
        /// HFR²-vs-position parabola, then linear-least-squares fit the tilted paraboloid best-focus surface
        /// z = Gx·x + Gy·y + K·(x²+y²) + Z0 to the per-star (x, y, bestFocus) points. Recover the inspector's own
        /// tilt azimuth / tilt effect / curvature effect and assert they match what was injected.
        ///
        /// <para>Run <b>with and without astigmatism</b>, at the same tolerances. That the astigmatic case
        /// passes them unchanged is the empirical half of the safety argument: at a fixed field point the split
        /// A does not depend on the focuser, so Δ → −Δ maps the semi-axis pair (|Δ−A|, |Δ+A|) to
        /// (|Δ+A|, |Δ−A|) — the PSF at −Δ is the PSF at +Δ rotated by exactly 90°. Every rotation-invariant
        /// statistic, HFR included, is therefore untouched, so each star's HFR² parabola keeps its vertex and
        /// the surface fit recovers the same numbers. If this ever fails with astigmatism on, the model or the
        /// rasterizer is wrong — widening the tolerance would only hide it.</para>
        /// </summary>
        [Test]
        public async Task AberrationSurfaceRecoveredThroughFullPipeline_TiltAndBackfocus([Values(false, true)] bool astigmatism) {
            var sensor = SyntheticCameraTestScene.SensorDef;
            int width = sensor.Width, height = sensor.Height;
            var projection = SyntheticCameraTestScene.Projection();
            var pixel = sensor.PixelSizeMicrons;
            var halfWMicrons = width * pixel / 2.0;
            var halfHMicrons = height * pixel / 2.0;

            const double injectedPhiDegrees = 30.0;
            const double injectedTiltMicrons = 80.0;
            const double injectedBackfocusMicrons = 40.0;
            var k = SyntheticCameraTestScene.FocuserStepSizeMicrons;
            var x0 = SyntheticCameraTestScene.OptimalFocuserPosition;

            // The injected surface, expressed exactly as the inspector fits it, is the ground truth.
            var truthRequest = SyntheticCameraTestScene.Request(
                x0, aberrationsEnabled: true,
                tiltAngleDegrees: injectedPhiDegrees, tiltAmountMicrons: injectedTiltMicrons, backfocusErrorMicrons: injectedBackfocusMicrons,
                astigmatismEnabled: astigmatism, cornerAstigmatismMicrons: astigmatism ? 15.0 : 0.0);
            var surface = AberrationSurface.FromRequest(truthRequest, sensor);

            // Stars spread across the field, weighted to corners/edges for tilt+curvature leverage.
            var placements = new (double px, double py)[] {
                (250, 250), (2750, 250), (250, 2750), (2750, 2750),
                (1504, 250), (250, 1504), (2750, 1504), (1504, 2750),
                (1504, 1504), (850, 850), (2150, 850), (850, 2150), (2150, 2150),
            };
            var stars = placements.Select(p => SyntheticCameraTestScene.StarAtPixel(projection, p.px, p.py, 10.5)).ToList();
            var reader = new FakeCatalogReader(stars);

            // Stepped focuser run bracketing every star's shifted best-focus (minima span roughly x0−8 … x0+24 steps).
            // 9 positions across a wide V give every star points on both sides of its minimum with strong lever arm.
            var offsets = new[] { -40, -28, -16, -4, 8, 20, 32, 44, 56 };

            // Per star: collect (focuserOffsetSteps, HFR) at every frame it is detected near its projected pixel.
            var perStar = placements.Select(_ => new List<(double offset, double hfr)>()).ToArray();
            foreach (var offset in offsets) {
                var request = SyntheticCameraTestScene.Request(
                    x0 + offset, aberrationsEnabled: true,
                    tiltAngleDegrees: injectedPhiDegrees, tiltAmountMicrons: injectedTiltMicrons, backfocusErrorMicrons: injectedBackfocusMicrons,
                    astigmatismEnabled: astigmatism, cornerAstigmatismMicrons: astigmatism ? 15.0 : 0.0);
                var pixels = new StarFieldCompositor(reader).Render(request, CancellationToken.None);
                using var mat = CvImageUtility.ToOpenCVMat(pixels, sensor.BitDepth, width, height);
                var result = await new StarDetector(new AlglibAPI()).Detect(mat, new StarDetectorParams(), null, CancellationToken.None);
                var detected = result.DetectedStars ?? new List<Star>();

                for (var i = 0; i < placements.Length; ++i) {
                    var (px, py) = placements[i];
                    var near = detected
                        .Select(d => (d, dist: Math.Sqrt((d.Center.X - px) * (d.Center.X - px) + (d.Center.Y - py) * (d.Center.Y - py))))
                        .OrderBy(t => t.dist)
                        .FirstOrDefault();
                    if (near.d != null && near.dist < 6.0) {
                        perStar[i].Add((offset, near.d.HFR));
                    }
                }
            }

            // Per star: fit HFR² = A·off² + B·off + C and take the vertex −B/(2A) as the best-focus focuser offset.
            var xs = new List<double[]>();
            var zs = new List<double>();
            for (var i = 0; i < placements.Length; ++i) {
                var pts = perStar[i];
                if (pts.Count < 5) {
                    TestContext.WriteLine($"star {i} at ({placements[i].px},{placements[i].py}) detected in only {pts.Count} frames — skipped");
                    continue;
                }
                var offs = pts.Select(p => p.offset).ToArray();
                var hfr2 = pts.Select(p => p.hfr * p.hfr).ToArray();
                var c = MathNet.Numerics.Fit.Polynomial(offs, hfr2, 2); // [C, B, A]
                var a = c[2];
                var b = c[1];
                if (a <= 0) {
                    TestContext.WriteLine($"star {i}: non-convex HFR² parabola (A={a:E2}) — skipped");
                    continue;
                }
                var vertexOffsetSteps = -b / (2.0 * a);
                var bestFocusMicrons = (x0 + vertexOffsetSteps) * k;

                var xMicrons = (placements[i].px - width / 2.0) * pixel;
                var yMicrons = (placements[i].py - height / 2.0) * pixel;
                xs.Add(new[] { xMicrons, yMicrons, xMicrons * xMicrons + yMicrons * yMicrons });
                zs.Add(bestFocusMicrons);
            }

            Assert.That(xs.Count, Is.GreaterThanOrEqualTo(8), "need enough well-sampled stars for a robust surface fit");

            // Linear least-squares fit z = Z0 + Gx·x + Gy·y + K·(x²+y²) (intercept ⇒ p[0]=Z0).
            var p = MathNet.Numerics.Fit.MultiDim(xs.ToArray(), zs.ToArray(), intercept: true);
            double z0 = p[0], gx = p[1], gy = p[2], kCurv = p[3];

            var recoveredPhiDeg = Math.Atan2(gy, gx) * 180.0 / Math.PI;
            var recoveredTilt = Math.Abs(gx) * halfWMicrons + Math.Abs(gy) * halfHMicrons;
            var recoveredCurvature = kCurv * (halfWMicrons * halfWMicrons + halfHMicrons * halfHMicrons);

            TestContext.WriteLine($"stars used={xs.Count}");
            TestContext.WriteLine($"injected: Phi={injectedPhiDegrees:F2}° Tilt={injectedTiltMicrons:F2}µm Curvature={injectedBackfocusMicrons:F2}µm");
            TestContext.WriteLine($"          Gx={surface.Gx:E4} Gy={surface.Gy:E4} K={surface.K:E4} Z0={surface.Z0:F1}");
            TestContext.WriteLine($"recovered:Phi={recoveredPhiDeg:F2}° Tilt={recoveredTilt:F2}µm Curvature={recoveredCurvature:F2}µm");
            TestContext.WriteLine($"          Gx={gx:E4} Gy={gy:E4} K={kCurv:E4} Z0={z0:F1}");

            Assert.Multiple(() => {
                // The render→detect→fit chain adds noise; a few degrees / a handful of µm is the realistic band.
                Assert.That(recoveredPhiDeg, Is.EqualTo(injectedPhiDegrees).Within(6.0), "recovered tilt azimuth φ");
                Assert.That(recoveredTilt, Is.EqualTo(injectedTiltMicrons).Within(0.20 * injectedTiltMicrons), "recovered tilt effect");
                Assert.That(recoveredCurvature, Is.EqualTo(injectedBackfocusMicrons).Within(0.25 * injectedBackfocusMicrons), "recovered curvature effect");
                // Cross-check the raw gradients/curvature against the surface the simulator actually injected.
                Assert.That(gx, Is.EqualTo(surface.Gx).Within(0.20 * Math.Abs(surface.Gx) + 1e-4));
                Assert.That(gy, Is.EqualTo(surface.Gy).Within(0.25 * Math.Abs(surface.Gy) + 1e-4));
            });
        }
    }
}
