using System;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class AberrationSurfaceTests {

    // IMX455 geometry.
    private const int W = 9576;
    private const int H = 6388;
    private const double P = 3.76;
    private const double K_MicronsPerStep = 0.49;
    private const int X0Steps = 10000;

    private static double HalfW => W * P / 2.0;
    private static double HalfH => H * P / 2.0;

    private static AberrationSurface Build(
        bool enabled = true,
        double tiltAngleDeg = 0.0,
        double tiltAmountUm = 0.0,
        double backfocusUm = 0.0,
        double offsetXUm = 0.0,
        double offsetYUm = 0.0) {
        return new AberrationSurface(enabled, tiltAngleDeg, tiltAmountUm, backfocusUm, offsetXUm, offsetYUm,
            W, H, P, K_MicronsPerStep, X0Steps);
    }

    [TestCase(0.0, 20.0, 15.0)]
    [TestCase(30.0, 50.0, -30.0)]
    [TestCase(45.0, 35.0, 0.0)]
    [TestCase(120.0, 40.0, 25.0)]
    [TestCase(-60.0, 10.0, -5.0)]
    public void InjectRecover_IsIdentity(double phiDeg, double tiltUm, double backfocusUm) {
        var surface = Build(tiltAngleDeg: phiDeg, tiltAmountUm: tiltUm, backfocusUm: backfocusUm);
        Assert.Multiple(() => {
            Assert.That(surface.Phi, Is.EqualTo(phiDeg * Math.PI / 180.0).Within(1e-9), "azimuth φ ↔ Phi");
            Assert.That(surface.PredictedTiltEffectMicrons, Is.EqualTo(tiltUm).Within(1e-9), "TiltAmount ↔ TiltEffect");
            Assert.That(surface.PredictedCurvatureEffectMicrons, Is.EqualTo(backfocusUm).Within(1e-9), "Backfocus ↔ CurvatureEffect");
        });
    }

    [TestCase(0.0, 20.0, 15.0)]
    [TestCase(30.0, 50.0, -30.0)]
    [TestCase(120.0, 40.0, 25.0)]
    [TestCase(-60.0, 10.0, -5.0)]
    public void PredictedEffects_MatchInspectorParaboloidModel(double phiDeg, double tiltUm, double backfocusUm) {
        var surface = Build(tiltAngleDeg: phiDeg, tiltAmountUm: tiltUm, backfocusUm: backfocusUm);

        // Reconstruct the exact model the inspector fits, then recompute its reported effects the same way
        // SensorModelAberrationResult.Update does (CurvatureAt(halfW,halfH); (maxCorner−minCorner)/2).
        var model = new SensorParaboloidModel(surface.X0, surface.Y0, surface.Z0, surface.Gx, surface.Gy, surface.K);

        var curvatureEffect = model.CurvatureAt(HalfW, HalfH);
        var tiltCorners = new[] {
            model.TiltAt(-HalfW, -HalfH),
            model.TiltAt(HalfW, -HalfH),
            model.TiltAt(-HalfW, HalfH),
            model.TiltAt(HalfW, HalfH),
        };
        var tiltEffect = (tiltCorners.Max() - tiltCorners.Min()) / 2.0;

        Assert.Multiple(() => {
            Assert.That(model.Phi, Is.EqualTo(surface.Phi).Within(1e-12), "inspector Phi matches surface Phi");
            Assert.That(model.Theta, Is.EqualTo(surface.Theta).Within(1e-12), "inspector Theta matches surface Theta");
            Assert.That(tiltEffect, Is.EqualTo(surface.PredictedTiltEffectMicrons).Within(1e-9));
            Assert.That(tiltEffect, Is.EqualTo(tiltUm).Within(1e-9));
            Assert.That(curvatureEffect, Is.EqualTo(surface.PredictedCurvatureEffectMicrons).Within(1e-9));
            Assert.That(curvatureEffect, Is.EqualTo(backfocusUm).Within(1e-9));
        });
    }

    [Test]
    public void Disabled_LocalDefocusIsUniformAndEqualsFlatFormula() {
        var surface = Build(enabled: false, tiltAngleDeg: 30.0, tiltAmountUm: 50.0, backfocusUm: 20.0);
        const int currentSteps = 10800;
        var expected = K_MicronsPerStep * (currentSteps - X0Steps); // 800 · 0.49 = 392 µm

        Assert.Multiple(() => {
            Assert.That(surface.Gx, Is.EqualTo(0.0));
            Assert.That(surface.Gy, Is.EqualTo(0.0));
            Assert.That(surface.K, Is.EqualTo(0.0));
            // Sample the four corners + center + an off-axis point; all must be identical (flat surface).
            foreach (var (px, py) in new[] { (0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1), (W / 2, H / 2), (1234, 4321) }) {
                Assert.That(surface.LocalDefocusMicrons(px, py, currentSteps), Is.EqualTo(expected).Within(1e-9), $"({px},{py})");
            }
        });
    }

    [Test]
    public void PureTilt_OppositeCornersHaveOppositeSignDefocus() {
        // Pure tilt along +X (φ=0), no curvature, no offset. Evaluated at best focus (steps = x0) so the flat
        // term cancels and the sign is set purely by the tilt gradient.
        var surface = Build(tiltAngleDeg: 0.0, tiltAmountUm: 50.0, backfocusUm: 0.0);
        var left = surface.LocalDefocusMicrons(0, H / 2, X0Steps);
        var right = surface.LocalDefocusMicrons(W - 1, H / 2, X0Steps);

        Assert.Multiple(() => {
            Assert.That(left, Is.GreaterThan(0.0), "left edge");
            Assert.That(right, Is.LessThan(0.0), "right edge");
            // Magnitudes ≈ TiltEffect at the edges (≈ ± half the corner swing) and opposite in sign.
            Assert.That(left, Is.EqualTo(surface.PredictedTiltEffectMicrons).Within(P), "left ≈ +tilt effect");
            Assert.That(right, Is.EqualTo(-surface.PredictedTiltEffectMicrons).Within(P), "right ≈ −tilt effect");
        });
    }

    [Test]
    public void PureTilt_TopVsBottom_AlongY() {
        // φ=90° ⇒ tilt along +Y; top and bottom edges get opposite-sign defocus.
        var surface = Build(tiltAngleDeg: 90.0, tiltAmountUm: 30.0);
        var top = surface.LocalDefocusMicrons(W / 2, 0, X0Steps);
        var bottom = surface.LocalDefocusMicrons(W / 2, H - 1, X0Steps);
        Assert.Multiple(() => {
            Assert.That(Math.Abs(surface.Gx), Is.LessThan(1e-12), "no X gradient");
            Assert.That(top, Is.GreaterThan(0.0));
            Assert.That(bottom, Is.LessThan(0.0));
        });
    }

    [Test]
    public void LocalDefocus_FlatComponentTracksFocuserOffset() {
        // With aberrations enabled but zero tilt/curvature, the surface is still flat at Z0, so the on-axis
        // (center) pixel sees exactly k·(steps − x0).
        var surface = Build(tiltAmountUm: 0.0, backfocusUm: 0.0);
        const int currentSteps = 9500;
        var expected = K_MicronsPerStep * (currentSteps - X0Steps);
        Assert.That(surface.LocalDefocusMicrons(W / 2, H / 2, currentSteps), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void FromRequest_DisabledYieldsFlatSurface() {
        var request = new RenderRequest {
            AberrationsEnabled = false,
            TiltAngleDegrees = 30.0,
            TiltAmountMicrons = 50.0,
            BackfocusErrorMicrons = 20.0,
            OpticalAxisOffsetXMicrons = 100.0,
            OpticalAxisOffsetYMicrons = -50.0,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0Steps,
        };
        var surface = AberrationSurface.FromRequest(request, SensorRegistry.Get(SonySensorModel.IMX455));
        Assert.Multiple(() => {
            Assert.That(surface.Gx, Is.EqualTo(0.0));
            Assert.That(surface.Gy, Is.EqualTo(0.0));
            Assert.That(surface.K, Is.EqualTo(0.0));
            Assert.That(surface.PredictedTiltEffectMicrons, Is.EqualTo(0.0));
            Assert.That(surface.PredictedCurvatureEffectMicrons, Is.EqualTo(0.0));
            Assert.That(surface.Z0, Is.EqualTo(X0Steps * K_MicronsPerStep).Within(1e-9));
        });
    }

    [Test]
    public void FromRequest_EnabledInvertsKnobsFromSensorDims() {
        var request = new RenderRequest {
            AberrationsEnabled = true,
            TiltAngleDegrees = 30.0,
            TiltAmountMicrons = 50.0,
            BackfocusErrorMicrons = 20.0,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0Steps,
        };
        var surface = AberrationSurface.FromRequest(request, SensorRegistry.Get(SonySensorModel.IMX455));
        Assert.Multiple(() => {
            Assert.That(surface.Phi, Is.EqualTo(30.0 * Math.PI / 180.0).Within(1e-9));
            Assert.That(surface.PredictedTiltEffectMicrons, Is.EqualTo(50.0).Within(1e-9));
            Assert.That(surface.PredictedCurvatureEffectMicrons, Is.EqualTo(20.0).Within(1e-9));
        });
    }

    [Test]
    public void NegativeTiltAmount_Throws() {
        // A negative magnitude would flip Phi by 180° and break inject⇄recover; reject it at the boundary.
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(tiltAngleDeg: 30.0, tiltAmountUm: -5.0));
    }

    [Test]
    public void ZeroTiltAmount_GivesZeroGradient() {
        var surface = Build(tiltAngleDeg: 45.0, tiltAmountUm: 0.0, backfocusUm: 10.0);
        Assert.Multiple(() => {
            Assert.That(surface.Gx, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(surface.Gy, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(surface.PredictedTiltEffectMicrons, Is.EqualTo(0.0).Within(1e-12));
            // Curvature still applies.
            Assert.That(surface.PredictedCurvatureEffectMicrons, Is.EqualTo(10.0).Within(1e-9));
        });
    }
}
