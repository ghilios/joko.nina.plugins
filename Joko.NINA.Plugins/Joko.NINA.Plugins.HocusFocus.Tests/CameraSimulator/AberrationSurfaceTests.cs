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
        double spacingUm = AberrationSurface.UnsetSpacingErrorMicrons,
        double ratio = 0.0) {
        return new AberrationSurface(enabled, tiltAngleDeg, tiltAmountUm, backfocusUm, offsetXUm, offsetYUm,
            W, H, P, K_MicronsPerStep, X0Steps, astigmatism, spacingUm, ratio);
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
    // ---------------------------------------------------------------------------------------------------

    /// <summary>A spread of field points, including the exact centre and all four corners.</summary>
    private static (int px, int py)[] FieldGrid() => new[] {
        (W / 2, H / 2), (0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1),
        (W / 4, H / 4), (3 * W / 4, H / 4), (W / 4, 3 * H / 4), (3 * W / 4, 3 * H / 4),
        (W / 2, 0), (W / 2, H - 1), (0, H / 2), (W - 1, H / 2),
    };

    [TestCase(0.0, 20.0, 15.0)]
    [TestCase(30.0, 50.0, -30.0)]
    [TestCase(120.0, 40.0, 25.0)]
    public void InjectRecover_IsIdentity_WithAstigmatismEnabled(double phiDeg, double tiltUm, double backfocusUm) {
        // The whole safety argument rests on astigmatism not touching the surface the inspector fits. Splitting
        // it into z_T = z + A and z_S = z - A leaves (Gx, Gy, K, Phi) alone by construction; this pins that.
        var plain = Build(tiltAngleDeg: phiDeg, tiltAmountUm: tiltUm, backfocusUm: backfocusUm);
        var astigmatic = Build(tiltAngleDeg: phiDeg, tiltAmountUm: tiltUm, backfocusUm: backfocusUm,
                               astigmatism: true, ratio: 0.7);
        Assert.Multiple(() => {
            Assert.That(astigmatic.Gx, Is.EqualTo(plain.Gx).Within(1e-15));
            Assert.That(astigmatic.Gy, Is.EqualTo(plain.Gy).Within(1e-15));
            Assert.That(astigmatic.K, Is.EqualTo(plain.K).Within(1e-15));
            Assert.That(astigmatic.Z0, Is.EqualTo(plain.Z0).Within(1e-15));
            Assert.That(astigmatic.Phi, Is.EqualTo(phiDeg * Math.PI / 180.0).Within(1e-9));
            Assert.That(astigmatic.PredictedTiltEffectMicrons, Is.EqualTo(tiltUm).Within(1e-9));
            Assert.That(astigmatic.PredictedCurvatureEffectMicrons, Is.EqualTo(backfocusUm).Within(1e-9));
        });
    }

    [Test]
    public void MeanOfTangentialAndSagittal_EqualsLocalDefocus() {
        // Δ_T = Δ − A and Δ_S = Δ + A, so their mean is Δ exactly. This identity is what makes the PSF at −Δ
        // the PSF at +Δ rotated 90°, hence HFR(Δ) still even about the same best focus.
        var surface = Build(tiltAngleDeg: 30.0, tiltAmountUm: 60.0, backfocusUm: 40.0,
                            astigmatism: true, ratio: 0.7);
        foreach (var (px, py) in FieldGrid()) {
            foreach (var steps in new[] { X0Steps - 400, X0Steps, X0Steps + 250 }) {
                surface.AstigmaticDefocusMicrons(px, py, steps, out var dT, out var dS, out _);
                Assert.That(0.5 * (dT + dS), Is.EqualTo(surface.LocalDefocusMicrons(px, py, steps)).Within(1e-12),
                    $"mean of the pair at ({px},{py}) step {steps}");
            }
        }
    }

    [Test]
    public void AstigmatismSplit_IsExactlyZero_WhenDisabled() {
        // Exactly 0.0, not merely small: the "astigmatism off renders byte-identically" guarantee depends on
        // every star collapsing onto the same quantized defocus level it would have had before.
        var offByToggle = Build(tiltAngleDeg: 30.0, tiltAmountUm: 60.0, backfocusUm: 40.0, astigmatism: false, ratio: 0.7);
        var offByAberrations = Build(enabled: false, tiltAngleDeg: 30.0, tiltAmountUm: 60.0, backfocusUm: 40.0, astigmatism: true, ratio: 0.7);
        var offByRatio = Build(tiltAngleDeg: 30.0, tiltAmountUm: 60.0, backfocusUm: 40.0, astigmatism: true, ratio: 0.0);

        foreach (var surface in new[] { offByToggle, offByAberrations, offByRatio }) {
            Assert.That(surface.AstigmatismCoefficient, Is.EqualTo(0.0));
            foreach (var (px, py) in FieldGrid()) {
                Assert.That(surface.AstigmatismSplitMicrons(px, py), Is.EqualTo(0.0));
                surface.AstigmaticDefocusMicrons(px, py, X0Steps + 200, out var dT, out var dS, out var theta);
                var delta = surface.LocalDefocusMicrons(px, py, X0Steps + 200);
                Assert.That(dT, Is.EqualTo(delta));
                Assert.That(dS, Is.EqualTo(delta));
                Assert.That(theta, Is.EqualTo(0.0));
            }
        }
    }

    [Test]
    public void PureTilt_WithZeroBackfocus_StillProducesAstigmatism() {
        // The reason this model was chosen over "astigmatism proportional to the backfocus knob": a tilted
        // sensor is genuinely mis-spaced over most of its area, so it is astigmatic on its own. With K = 0 the
        // inferred spacing is 0 too, and c_m falls back to the nominal constant rather than dividing by zero.
        var surface = Build(tiltAngleDeg: 0.0, tiltAmountUm: 200.0, backfocusUm: 0.0, astigmatism: true, ratio: 0.7);

        Assert.That(surface.CurvaturePerSpacing, Is.EqualTo(AberrationSurface.NominalCurvaturePerSpacingPerAreaMicrons).Within(1e-20));
        Assert.That(surface.EffectiveSpacingErrorMicrons, Is.EqualTo(0.0).Within(1e-12));

        // Tilt is along +x, so the two x-edges sit at opposite local spacing errors and their splits must be
        // equal and opposite, while the centre column stays unsplit.
        var left = surface.AstigmatismSplitMicrons(0, H / 2);
        var right = surface.AstigmatismSplitMicrons(W - 1, H / 2);
        Assert.Multiple(() => {
            Assert.That(Math.Abs(left), Is.GreaterThan(0.0), "pure tilt must produce a nonzero split");
            Assert.That(Math.Sign(left), Is.EqualTo(-Math.Sign(right)), "opposite edges are oppositely split");
            Assert.That(surface.AstigmatismSplitMicrons(W / 2, H / 2), Is.EqualTo(0.0).Within(1e-12), "round on axis");
        });
    }

    [Test]
    public void BlankSpacing_InfersTheNominalCurvaturePerSpacing() {
        // Leaving the spacing unset must land exactly on c_m0 -- e_c = K/c_m0 makes c_m = K/e_c = c_m0 by
        // construction. If that identity ever breaks, the inferred and explicit paths stop agreeing.
        var surface = Build(backfocusUm: 50.0, astigmatism: true, ratio: 0.7);
        var expectedSpacing = surface.K / AberrationSurface.NominalCurvaturePerSpacingPerAreaMicrons;
        Assert.Multiple(() => {
            Assert.That(surface.EffectiveSpacingErrorMicrons, Is.EqualTo(expectedSpacing).Within(1e-9 * Math.Abs(expectedSpacing)));
            Assert.That(surface.CurvaturePerSpacing, Is.EqualTo(AberrationSurface.NominalCurvaturePerSpacingPerAreaMicrons)
                .Within(1e-6 * AberrationSurface.NominalCurvaturePerSpacingPerAreaMicrons));
            // With c_m derived (or nominal, which is the same here), the corner split is just ρ × the curvature effect.
            Assert.That(surface.PredictedAstigmatismEffectMicrons, Is.EqualTo(0.7 * 50.0).Within(1e-6));
        });
    }

    [TestCase(200.0)]
    [TestCase(1000.0)]
    [TestCase(2500.0)]
    public void ExplicitSpacing_DerivesCurvaturePerSpacingFromTheUsersOwnNumbers(double spacingUm) {
        var surface = Build(backfocusUm: 50.0, astigmatism: true, spacingUm: spacingUm, ratio: 0.7);
        Assert.Multiple(() => {
            Assert.That(surface.EffectiveSpacingErrorMicrons, Is.EqualTo(spacingUm).Within(1e-12));
            Assert.That(surface.CurvaturePerSpacing, Is.EqualTo(surface.K / spacingUm).Within(1e-18));
            // Whatever the spacing, c_m·e_c == K, so the corner split stays ρ × the curvature effect.
            Assert.That(surface.PredictedAstigmatismEffectMicrons, Is.EqualTo(0.7 * 50.0).Within(1e-6));
        });
    }

    [Test]
    public void ExplicitSpacing_InheritsTheSignOfTheBackfocusError() {
        // The option is a magnitude -- its negative range is the "not entered" sentinel -- so the direction has
        // to come from K. A corrector's curvature response to spacing has one fixed sign, so c_m stays positive.
        var negative = Build(backfocusUm: -50.0, astigmatism: true, spacingUm: 1000.0, ratio: 0.7);
        Assert.Multiple(() => {
            Assert.That(negative.EffectiveSpacingErrorMicrons, Is.EqualTo(-1000.0).Within(1e-12));
            Assert.That(negative.CurvaturePerSpacing, Is.GreaterThan(0.0), "c_m is a positive corrector property");
        });
    }

    [Test]
    public void AstigmatismSplit_FlipsSignWithTheBackfocusSign() {
        var positive = Build(backfocusUm: 50.0, astigmatism: true, ratio: 0.7);
        var negative = Build(backfocusUm: -50.0, astigmatism: true, ratio: 0.7);
        foreach (var (px, py) in FieldGrid()) {
            Assert.That(negative.AstigmatismSplitMicrons(px, py),
                Is.EqualTo(-positive.AstigmatismSplitMicrons(px, py)).Within(1e-9),
                $"split at ({px},{py}) reverses with the spacing direction");
        }
    }

    [Test]
    public void AstigmatismSplit_ScalesAsRadiusSquaredAboutTheOpticalAxis() {
        // Not about the sensor centre: with an offset optical axis the split must vanish at (X0, Y0) and grow
        // as r'² from there. Offsetting by a quarter frame makes the two indistinguishable if r were measured
        // from the chip centre.
        var offsetX = W * P / 4.0;
        var surface = Build(backfocusUm: 50.0, offsetXUm: offsetX, astigmatism: true, ratio: 0.7);

        var axisPx = (int)Math.Round(W / 2.0 + offsetX / P);
        Assert.That(surface.AstigmatismSplitMicrons(axisPx, H / 2), Is.EqualTo(0.0).Within(1e-9),
            "the split vanishes on the optical axis, not the sensor centre");
        Assert.That(surface.FieldAngleRadians(axisPx, H / 2), Is.EqualTo(0.0), "angle is defined as 0 on the axis");

        // Doubling the radius from the axis quadruples the split (no tilt, so e(x,y) is constant).
        var near = surface.AstigmatismSplitMicrons(axisPx + 500, H / 2);
        var far = surface.AstigmatismSplitMicrons(axisPx + 1000, H / 2);
        Assert.That(far / near, Is.EqualTo(4.0).Within(1e-6));
    }

    [Test]
    public void OrientationRule_RadialWhereDeltaAndSplitDisagreeInSign() {
        // The one-line statement of the whole feature: a_rad > a_tan  <=>  |Δ−A| > |Δ+A|  <=>  Δ·A < 0.
        // Checked directly on the defocus pair, with no rendering involved.
        var surface = Build(tiltAngleDeg: 0.0, tiltAmountUm: 120.0, backfocusUm: 40.0, astigmatism: true, ratio: 0.7);
        var sawRadial = false;
        var sawTangential = false;

        foreach (var (px, py) in FieldGrid()) {
            foreach (var steps in new[] { X0Steps - 300, X0Steps - 100, X0Steps, X0Steps + 100, X0Steps + 300 }) {
                surface.AstigmaticDefocusMicrons(px, py, steps, out var dT, out var dS, out _);
                var delta = surface.LocalDefocusMicrons(px, py, steps);
                var split = surface.AstigmatismSplitMicrons(px, py);
                var product = delta * split;

                // |Δ_T| sets the radial semi-axis, |Δ_S| the tangential one.
                if (product < 0.0) {
                    Assert.That(Math.Abs(dT), Is.GreaterThan(Math.Abs(dS)), $"radial at ({px},{py}) step {steps}");
                    sawRadial = true;
                } else if (product > 0.0) {
                    Assert.That(Math.Abs(dT), Is.LessThan(Math.Abs(dS)), $"tangential at ({px},{py}) step {steps}");
                    sawTangential = true;
                } else {
                    Assert.That(Math.Abs(dT), Is.EqualTo(Math.Abs(dS)).Within(1e-9), $"round at ({px},{py}) step {steps}");
                }
            }
        }

        Assert.Multiple(() => {
            Assert.That(sawRadial, Is.True, "this configuration must exhibit radial elongation somewhere");
            Assert.That(sawTangential, Is.True, "...and tangential elongation somewhere else");
        });
    }

    /// <summary>Semi-axes (px) of the blur at a field point, up to the shared 1/(2Np) scale factor.</summary>
    private static (double radial, double tangential) SemiAxes(AberrationSurface surface, int px, int py, int steps) {
        surface.AstigmaticDefocusMicrons(px, py, steps, out var dT, out var dS, out _);
        return (Math.Abs(dT), Math.Abs(dS));
    }

    [TestCase(0.3)]
    [TestCase(0.7)]
    [TestCase(2.0)]
    public void AxisRatio_IsTheFirstOrderIdentity_AndIndependentOfTheBackfocusError(double ratio) {
        // Substituting Δ = −c_m·Δb·r'² and A = c_a·Δb·r'² gives semi-axes |(c_m ± c_a)·Δb·r'²|, so the axis
        // ratio is |1+ρ| / |1−ρ| with NO Δb in it. That identity is the whole first-order content of the
        // model, and it is why the elongation direction is a property of the corrector rather than of how
        // badly it is spaced.
        var expected = Math.Abs(1.0 + ratio) / Math.Abs(1.0 - ratio);
        foreach (var backfocus in new[] { 50.0, 500.0, 2000.0, -2000.0 }) {
            var surface = Build(backfocusUm: backfocus, astigmatism: true, ratio: ratio);
            var (radial, tangential) = SemiAxes(surface, 0, 0, X0Steps);   // corner, focuser at best focus
            Assert.That(radial / tangential, Is.EqualTo(expected).Within(1e-6 * expected),
                $"axis ratio at backfocus {backfocus} µm");
        }
    }

    [TestCase(200.0)]
    [TestCase(2000.0)]
    public void ReversingTheBackfocusError_RendersAnIdenticalEllipse(double backfocusUm) {
        // Reversing the spacing error flips BOTH the local defocus and the astigmatic split, and the semi-axes
        // |Δ−A| and |Δ+A| are invariant under that pair of flips. So too-much and too-little backfocus are not
        // distinguishable from star shapes in a single frame -- you tell them apart by refocusing, because the
        // corners come to focus on opposite sides of the centre. This is first-order optics, not a shortcut,
        // and it is pinned here because it reliably surprises people.
        var positive = Build(backfocusUm: backfocusUm, astigmatism: true, ratio: 0.7);
        var negative = Build(backfocusUm: -backfocusUm, astigmatism: true, ratio: 0.7);
        foreach (var (px, py) in FieldGrid()) {
            var a = SemiAxes(positive, px, py, X0Steps);
            var b = SemiAxes(negative, px, py, X0Steps);
            Assert.That(b.radial, Is.EqualTo(a.radial).Within(1e-9), $"radial semi-axis at ({px},{py})");
            Assert.That(b.tangential, Is.EqualTo(a.tangential).Within(1e-9), $"tangential semi-axis at ({px},{py})");
        }
    }

    [Test]
    public void TheSignOfTheRatio_SelectsRadialVersusTangential() {
        // The one knob that does change the direction. A negative ratio models a corrector whose astigmatism
        // opposes its field curvature; the two cases are the same ellipse rotated by 90 degrees.
        var radialCorrector = Build(backfocusUm: 500.0, astigmatism: true, ratio: 0.7);
        var tangentialCorrector = Build(backfocusUm: 500.0, astigmatism: true, ratio: -0.7);

        var r = SemiAxes(radialCorrector, 0, 0, X0Steps);
        var t = SemiAxes(tangentialCorrector, 0, 0, X0Steps);
        Assert.Multiple(() => {
            Assert.That(r.radial, Is.GreaterThan(r.tangential), "positive ratio elongates radially");
            Assert.That(t.tangential, Is.GreaterThan(t.radial), "negative ratio elongates tangentially");
            Assert.That(t.radial, Is.EqualTo(r.tangential).Within(1e-9), "and it is the same ellipse, rotated");
            Assert.That(t.tangential, Is.EqualTo(r.radial).Within(1e-9));
        });
    }

    [Test]
    public void NaNAstigmatismRatio_Throws() {
        // NaN fails every ordering comparison, so a naive `< 0` guard waves it through and the whole frame
        // renders NaN. The ratio is otherwise unrestricted in sign.
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(astigmatism: true, ratio: double.NaN));
    }

    [Test]
    public void FromRequest_CarriesTheAstigmatismKnobs() {
        var request = new RenderRequest {
            AberrationsEnabled = true,
            TiltAngleDegrees = 30.0,
            TiltAmountMicrons = 50.0,
            BackfocusErrorMicrons = 20.0,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0Steps,
            AstigmatismEnabled = true,
            BackfocusSpacingErrorMicrons = 800.0,
            AstigmatismRatio = 0.7,
        };
        var surface = AberrationSurface.FromRequest(request, SensorRegistry.Get(SonySensorModel.IMX455));
        Assert.Multiple(() => {
            Assert.That(surface.EffectiveSpacingErrorMicrons, Is.EqualTo(800.0).Within(1e-12));
            Assert.That(surface.AstigmatismCoefficient, Is.EqualTo(0.7 * surface.K / 800.0).Within(1e-20));
            Assert.That(surface.PredictedAstigmatismEffectMicrons, Is.EqualTo(0.7 * 20.0).Within(1e-6));
        });
    }

    [Test]
    public void FromRequest_DefaultsToNoAstigmatism() {
        // A request that says nothing about astigmatism -- every existing caller, including the synthetic bank
        // -- must render exactly as it did before.
        var request = new RenderRequest {
            AberrationsEnabled = true,
            TiltAngleDegrees = 30.0,
            TiltAmountMicrons = 50.0,
            BackfocusErrorMicrons = 20.0,
            FocuserStepSizeMicrons = K_MicronsPerStep,
            OptimalFocuserPosition = X0Steps,
        };
        var surface = AberrationSurface.FromRequest(request, SensorRegistry.Get(SonySensorModel.IMX455));
        Assert.That(surface.AstigmatismCoefficient, Is.EqualTo(0.0));
    }
}
