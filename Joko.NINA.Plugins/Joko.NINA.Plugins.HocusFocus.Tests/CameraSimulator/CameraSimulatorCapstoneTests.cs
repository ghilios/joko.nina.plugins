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
        /// (b) Aberration inject ⇄ recover through the full pipeline. Inject a known tilt + backfocus, render a
        /// stepped-focuser run, detect each star per frame, fit each star's best-focus focuser position from its
        /// HFR²-vs-position parabola, then linear-least-squares fit the tilted paraboloid best-focus surface
        /// z = Gx·x + Gy·y + K·(x²+y²) + Z0 to the per-star (x, y, bestFocus) points. Recover the inspector's own
        /// tilt azimuth / tilt effect / curvature effect and assert they match what was injected.
        /// </summary>
        [Test]
        public async Task AberrationSurfaceRecoveredThroughFullPipeline_TiltAndBackfocus() {
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
                tiltAngleDegrees: injectedPhiDegrees, tiltAmountMicrons: injectedTiltMicrons, backfocusErrorMicrons: injectedBackfocusMicrons);
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
                    tiltAngleDegrees: injectedPhiDegrees, tiltAmountMicrons: injectedTiltMicrons, backfocusErrorMicrons: injectedBackfocusMicrons);
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
