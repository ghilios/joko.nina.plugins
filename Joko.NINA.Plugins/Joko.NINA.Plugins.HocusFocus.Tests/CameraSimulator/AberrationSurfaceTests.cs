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
        double offsetYUm = 0.0,
        bool astigmatism = false,
        double cornerAstigUm = 0.0) {
        return new AberrationSurface(enabled, tiltAngleDeg, tiltAmountUm, backfocusUm, offsetXUm, offsetYUm,
            W, H, P, K_MicronsPerStep, X0Steps, astigmatism, cornerAstigUm);
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

    // ---------------------------------------------------------------------------------------------------
    // Astigmatism: the surface becomes a PAIR whose mean is the surface above.
    //
    // A(x,y) = a2 * r'^2,  a2 = K/2 + a_c/r_c^2. Two things this deliberately does NOT contain: tilt, and a
    // free ratio. Tilt cannot change the beam's aberrations -- a tilted sensor only chooses where along each
    // beam it samples -- and Seidel's 3:1 rule pins the spacing-induced split to exactly half the induced
    // mean curvature. What makes a tilted rig show eccentric corners is the corrector's RESIDUAL split a_c,
    // which tilt reveals by defocusing the field.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>A spread of field points, including the exact centre and all four corners.</summary>
    private static (int px, int py)[] FieldGrid() => new[] {
        (W / 2, H / 2), (0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1),
        (W / 4, H / 4), (3 * W / 4, H / 4), (W / 4, 3 * H / 4), (3 * W / 4, 3 * H / 4),
        (W / 2, 0), (W / 2, H - 1), (0, H / 2), (W - 1, H / 2),
    };

    /// <summary>Semi-axes of the blur at a field point, up to the shared 1/(2Np) scale factor.</summary>
    private static (double radial, double tangential) SemiAxes(AberrationSurface surface, int px, int py, int steps) {
        surface.AstigmaticDefocusMicrons(px, py, steps, out var dT, out var dS, out _);
        return (Math.Abs(dT), Math.Abs(dS));
    }

    [TestCase(0.0, 20.0, 15.0)]
    [TestCase(30.0, 50.0, -30.0)]
    [TestCase(120.0, 40.0, 25.0)]
    public void InjectRecover_IsIdentity_WithAstigmatismEnabled(double phiDeg, double tiltUm, double backfocusUm) {
        // The whole safety argument rests on astigmatism not touching the surface the inspector fits.
        var plain = Build(tiltAngleDeg: phiDeg, tiltAmountUm: tiltUm, backfocusUm: backfocusUm);
        var astigmatic = Build(tiltAngleDeg: phiDeg, tiltAmountUm: tiltUm, backfocusUm: backfocusUm,
                               astigmatism: true, cornerAstigUm: 15.0);
        Assert.Multiple(() => {
            Assert.That(astigmatic.Gx, Is.EqualTo(plain.Gx).Within(1e-15));
            Assert.That(astigmatic.Gy, Is.EqualTo(plain.Gy).Within(1e-15));
            Assert.That(astigmatic.K, Is.EqualTo(plain.K).Within(1e-15));
            Assert.That(astigmatic.Z0, Is.EqualTo(plain.Z0).Within(1e-15));
            Assert.That(astigmatic.PredictedTiltEffectMicrons, Is.EqualTo(tiltUm).Within(1e-9));
            Assert.That(astigmatic.PredictedCurvatureEffectMicrons, Is.EqualTo(backfocusUm).Within(1e-9));
        });
    }

    [Test]
    public void MeanOfTangentialAndSagittal_EqualsLocalDefocus() {
        var surface = Build(tiltAngleDeg: 30.0, tiltAmountUm: 60.0, backfocusUm: 40.0,
                            astigmatism: true, cornerAstigUm: 15.0);
        foreach (var (px, py) in FieldGrid()) {
            foreach (var steps in new[] { X0Steps - 400, X0Steps, X0Steps + 250 }) {
                surface.AstigmaticDefocusMicrons(px, py, steps, out var dT, out var dS, out _);
                Assert.That(0.5 * (dT + dS), Is.EqualTo(surface.LocalDefocusMicrons(px, py, steps)).Within(1e-12),
                    $"mean of the pair at ({px},{py}) step {steps}");
            }
        }
    }

    [Test]
    public void AstigmatismSplit_IsExactlyZero_WhenDisabledOrWhenTheOpticIsPerfect() {
        // Exactly 0.0, not merely small: "astigmatism off renders byte-identically" depends on every star
        // collapsing onto the same quantized defocus level it would have had before.
        var offByToggle = Build(tiltAmountUm: 60.0, backfocusUm: 40.0, astigmatism: false, cornerAstigUm: 15.0);
        var offByAberrations = Build(enabled: false, tiltAmountUm: 60.0, backfocusUm: 40.0, astigmatism: true, cornerAstigUm: 15.0);
        // A perfectly corrected optic perfectly spaced: no residual, no induced split.
        var perfectOptic = Build(tiltAmountUm: 500.0, backfocusUm: 0.0, astigmatism: true, cornerAstigUm: 0.0);

        foreach (var surface in new[] { offByToggle, offByAberrations, perfectOptic }) {
            foreach (var (px, py) in FieldGrid()) {
                Assert.That(surface.AstigmatismSplitMicrons(px, py), Is.EqualTo(0.0));
                surface.AstigmaticDefocusMicrons(px, py, X0Steps + 200, out var dT, out var dS, out _);
                var delta = surface.LocalDefocusMicrons(px, py, X0Steps + 200);
                Assert.That(dT, Is.EqualTo(delta));
                Assert.That(dS, Is.EqualTo(delta));
            }
        }
    }

    [Test]
    public void PureTilt_RevealsTheResidual_RadialOnOneEdgeAndTangentialOnTheOther() {
        // The headline behaviour, and the one the previous model could not produce at any setting. Tilt does
        // not create astigmatism -- it injects defocus, which drags each edge to a different Δ against a FIXED
        // residual split. Δ and A disagree in sign on one edge and agree on the other, so the two edges
        // elongate perpendicular to each other.
        var surface = Build(tiltAngleDeg: 0.0, tiltAmountUm: 100.0, backfocusUm: 0.0,
                            astigmatism: true, cornerAstigUm: 15.0);

        var left = SemiAxes(surface, 0, H / 2, X0Steps);
        var right = SemiAxes(surface, W - 1, H / 2, X0Steps);
        var centre = SemiAxes(surface, W / 2, H / 2, X0Steps);

        Assert.Multiple(() => {
            Assert.That(left.tangential, Is.GreaterThan(left.radial * 1.05), "one edge elongates tangentially");
            Assert.That(right.radial, Is.GreaterThan(right.tangential * 1.05), "the opposite edge radially");
            Assert.That(centre.radial, Is.EqualTo(0.0).Within(1e-9), "and the on-axis star stays a point");
            Assert.That(centre.tangential, Is.EqualTo(0.0).Within(1e-9));
        });
    }

    [Test]
    public void AstigmatismSplit_IsIndependentOfTilt() {
        // The physical claim, pinned: the sensor's own position cannot alter the beam's aberrations. Only the
        // mean surface sees tilt.
        var noTilt = Build(tiltAmountUm: 0.0, backfocusUm: 40.0, astigmatism: true, cornerAstigUm: 15.0);
        var hugeTilt = Build(tiltAngleDeg: 37.0, tiltAmountUm: 2000.0, backfocusUm: 40.0, astigmatism: true, cornerAstigUm: 15.0);
        foreach (var (px, py) in FieldGrid()) {
            Assert.That(hugeTilt.AstigmatismSplitMicrons(px, py),
                Is.EqualTo(noTilt.AstigmatismSplitMicrons(px, py)).Within(1e-12),
                $"split at ({px},{py}) must not depend on tilt");
        }
    }

    [Test]
    public void SpacingInducedSplit_IsExactlyHalfTheCurvature() {
        // Seidel's 3:1 rule: the tangential surface departs from Petzval three times as far as the sagittal,
        // so the medial surface moves 2s·r² while the half-split moves s·r². The ratio is not a free knob.
        foreach (var backfocus in new[] { 20.0, 50.0, -80.0, 200.0 }) {
            var surface = Build(backfocusUm: backfocus, astigmatism: true, cornerAstigUm: 0.0);
            Assert.That(surface.PredictedAstigmatismEffectMicrons, Is.EqualTo(0.5 * backfocus).Within(1e-9),
                $"corner split at backfocus {backfocus} µm");
        }
    }

    [Test]
    public void CornerResidual_AddsToTheSpacingInducedSplit() {
        var surface = Build(backfocusUm: 50.0, astigmatism: true, cornerAstigUm: 15.0);
        Assert.That(surface.PredictedAstigmatismEffectMicrons, Is.EqualTo(25.0 + 15.0).Within(1e-9));
    }

    [Test]
    public void ReversingTheBackfocusError_FlipsTheOrientation_InsideTheResidualWindow() {
        // Near design spacing the residual dominates the induced split, so reversing the spacer moves the
        // corner from one side of the astigmatic pair to the other and the orientation flips. The window is
        // |C| < 2·a_c; beyond it both signs converge to radial, which is what several bench reports describe.
        const double residual = 15.0;
        var tooLong = Build(backfocusUm: 12.5, astigmatism: true, cornerAstigUm: residual);
        var tooShort = Build(backfocusUm: -12.5, astigmatism: true, cornerAstigUm: residual);
        var farOutPositive = Build(backfocusUm: 200.0, astigmatism: true, cornerAstigUm: residual);
        var farOutNegative = Build(backfocusUm: -200.0, astigmatism: true, cornerAstigUm: residual);

        var a = SemiAxes(tooLong, 0, 0, X0Steps);
        var b = SemiAxes(tooShort, 0, 0, X0Steps);
        var c = SemiAxes(farOutPositive, 0, 0, X0Steps);
        var d = SemiAxes(farOutNegative, 0, 0, X0Steps);

        Assert.Multiple(() => {
            Assert.That(a.radial, Is.GreaterThan(a.tangential), "inside the window: one sign is radial");
            Assert.That(b.tangential, Is.GreaterThan(b.radial), "and the other tangential");
            Assert.That(c.radial, Is.GreaterThan(c.tangential), "far outside it, both signs are radial");
            Assert.That(d.radial, Is.GreaterThan(d.tangential));
        });
    }

    [Test]
    public void AstigmatismSplit_ScalesAsRadiusSquaredAboutTheOpticalAxis() {
        var offsetX = W * P / 4.0;
        var surface = Build(backfocusUm: 50.0, offsetXUm: offsetX, astigmatism: true, cornerAstigUm: 15.0);

        var axisPx = (int)Math.Round(W / 2.0 + offsetX / P);
        Assert.That(surface.AstigmatismSplitMicrons(axisPx, H / 2), Is.EqualTo(0.0).Within(1e-9),
            "the split vanishes on the optical axis, not the sensor centre");
        Assert.That(surface.FieldAngleRadians(axisPx, H / 2), Is.EqualTo(0.0), "angle is defined as 0 on the axis");

        var near = surface.AstigmatismSplitMicrons(axisPx + 500, H / 2);
        var far = surface.AstigmatismSplitMicrons(axisPx + 1000, H / 2);
        Assert.That(far / near, Is.EqualTo(4.0).Within(1e-6));
    }

    [Test]
    public void TheSignOfTheResidual_SelectsWhichSpacingDirectionIsRadial() {
        var positive = Build(backfocusUm: 12.5, astigmatism: true, cornerAstigUm: 15.0);
        var negative = Build(backfocusUm: 12.5, astigmatism: true, cornerAstigUm: -15.0);
        var p = SemiAxes(positive, 0, 0, X0Steps);
        var n = SemiAxes(negative, 0, 0, X0Steps);
        Assert.Multiple(() => {
            Assert.That(p.radial, Is.GreaterThan(p.tangential), "positive residual elongates radially here");
            Assert.That(n.tangential, Is.GreaterThan(n.radial), "negative residual flips it");
        });
    }

    [Test]
    public void OrientationRule_RadialWhereDeltaAndSplitDisagreeInSign() {
        var surface = Build(tiltAngleDeg: 0.0, tiltAmountUm: 120.0, backfocusUm: 40.0,
                            astigmatism: true, cornerAstigUm: 15.0);
        var sawRadial = false;
        var sawTangential = false;

        foreach (var (px, py) in FieldGrid()) {
            foreach (var steps in new[] { X0Steps - 300, X0Steps - 100, X0Steps, X0Steps + 100, X0Steps + 300 }) {
                surface.AstigmaticDefocusMicrons(px, py, steps, out var dT, out var dS, out _);
                var product = surface.LocalDefocusMicrons(px, py, steps) * surface.AstigmatismSplitMicrons(px, py);
                if (product < 0.0) {
                    Assert.That(Math.Abs(dT), Is.GreaterThan(Math.Abs(dS)), $"radial at ({px},{py}) step {steps}");
                    sawRadial = true;
                } else if (product > 0.0) {
                    Assert.That(Math.Abs(dT), Is.LessThan(Math.Abs(dS)), $"tangential at ({px},{py}) step {steps}");
                    sawTangential = true;
                }
            }
        }

        Assert.Multiple(() => {
            Assert.That(sawRadial, Is.True, "this configuration must exhibit radial elongation somewhere");
            Assert.That(sawTangential, Is.True, "...and tangential elongation somewhere else");
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void NonFiniteCornerAstigmatism_Throws(double value) {
        // NaN fails every ordering comparison and an infinity sails through a naive `< 0` guard; either one
        // renders an all-NaN frame with no error anywhere.
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(astigmatism: true, cornerAstigUm: value));
    }

    [Test]
    public void FromRequest_CarriesTheResidual_AndDefaultsToNoAstigmatism() {
        var withAstigmatism = new RenderRequest {
            AberrationsEnabled = true,
            BackfocusErrorMicrons = 20.0,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0Steps,
            AstigmatismEnabled = true,
            CornerAstigmatismMicrons = 15.0,
        };
        var surface = AberrationSurface.FromRequest(withAstigmatism, SensorRegistry.Get(SonySensorModel.IMX455));
        Assert.That(surface.PredictedAstigmatismEffectMicrons, Is.EqualTo(10.0 + 15.0).Within(1e-6));

        // A request that says nothing about astigmatism -- every existing caller, the synthetic bank included --
        // must render exactly as it did before.
        var silent = new RenderRequest {
            AberrationsEnabled = true,
            BackfocusErrorMicrons = 20.0,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0Steps,
        };
        Assert.That(AberrationSurface.FromRequest(silent, SensorRegistry.Get(SonySensorModel.IMX455)).AstigmatismCoefficient,
            Is.EqualTo(0.0));
    }
}
