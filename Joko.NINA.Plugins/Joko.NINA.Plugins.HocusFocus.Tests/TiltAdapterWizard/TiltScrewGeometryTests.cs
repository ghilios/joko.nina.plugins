using System;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard;

[TestFixture]
public class TiltScrewGeometryTests {

    private const double R = 21000.0; // 21 mm in microns

    private static (double x, double y) Pos(double angleDeg, double r) => TiltScrewGeometry.ScrewPositionMicrons(angleDeg, r);

    // Forward least-squares plane gradient produced by per-screw axial moves d_i.
    // G = (2 / (n·R²)) · Σ d_i·p_i  (exact for n>=3 equally-spaced screws on a circle).
    private static (double gx, double gy) ForwardGradient(double[] angles, double[] moves, double r) {
        double sx = 0, sy = 0;
        for (int i = 0; i < angles.Length; i++) {
            var (x, y) = Pos(angles[i], r);
            sx += moves[i] * x;
            sy += moves[i] * y;
        }
        double scale = 2.0 / (angles.Length * r * r);
        return (scale * sx, scale * sy);
    }

    [Test]
    public void ScrewPositionMicrons_MatchesClockFaceConvention() {
        Assert.Multiple(() => {
            // 0° = up = -y; 90° = right = +x; 180° = down = +y; 270° = left = -x
            Assert.That(Pos(0, R).x, Is.EqualTo(0).Within(1e-6));
            Assert.That(Pos(0, R).y, Is.EqualTo(-R).Within(1e-6));
            Assert.That(Pos(90, R).x, Is.EqualTo(R).Within(1e-6));
            Assert.That(Pos(90, R).y, Is.EqualTo(0).Within(1e-6));
            Assert.That(Pos(180, R).y, Is.EqualTo(R).Within(1e-6));
            Assert.That(Pos(270, R).x, Is.EqualTo(-R).Within(1e-6));
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    public void SingleScrewMove_RoundTripsThroughGradient(int n) {
        // Move only screw 1 by a known axial amount; the recovered move must match.
        double delta = 137.0; // microns
        double step = 360.0 / n;
        var angles = new double[n];
        var moves = new double[n];
        for (int i = 0; i < n; i++) {
            angles[i] = 15.0 + i * step; // generic offset, still equally spaced
        }
        moves[0] = delta;
        var (gx, gy) = ForwardGradient(angles, moves, R);
        var recovered = TiltScrewGeometry.SingleScrewAxialMoveMicrons(gx, gy, n, R);
        Assert.That(recovered, Is.EqualTo(delta).Within(1e-6));
    }

    [TestCase(3)]
    [TestCase(4)]
    public void TiltCorrection_CancelsTheGradient(int n) {
        // Given a tilt gradient, applying the per-screw corrections must reproduce -G
        // (i.e. it flattens the tilt) under the same forward plane model.
        double gx = 0.0012, gy = -0.0008;
        double step = 360.0 / n;
        var angles = new double[n];
        var corrections = new double[n];
        for (int i = 0; i < n; i++) {
            angles[i] = 15.0 + i * step;
            corrections[i] = TiltScrewGeometry.TiltCorrectionMicrons(gx, gy, angles[i], R);
        }
        var (gxPrime, gyPrime) = ForwardGradient(angles, corrections, R);
        Assert.Multiple(() => {
            Assert.That(gxPrime, Is.EqualTo(-gx).Within(1e-9));
            Assert.That(gyPrime, Is.EqualTo(-gy).Within(1e-9));
        });
    }

    [TestCase(0.0)]
    [TestCase(37.0)]
    [TestCase(120.0)]
    [TestCase(255.0)]
    public void TiltCorrection_SignAgreesWithLegacyArrowProjection(double angleDeg) {
        // Legacy arrow uses (-a·sinθ + b·cosθ) with a∝gx, b∝gy (positive scale factors).
        // The physical correction must share its sign so numbers and arrows agree.
        double gx = 0.0009, gy = -0.0011;
        double theta = angleDeg * Math.PI / 180.0;
        double legacyProjection = -gx * Math.Sin(theta) + gy * Math.Cos(theta);
        double correction = TiltScrewGeometry.TiltCorrectionMicrons(gx, gy, angleDeg, R);
        Assert.That(Math.Sign(correction), Is.EqualTo(Math.Sign(legacyProjection)));
    }

    [Test]
    public void Calibration_ThreeScrew_RecoversSingleScrewMove() {
        double delta = 211.0; // microns the user moved screw 1 by
        var angles = new[] { 15.0, 135.0, 255.0 };
        var moves = new[] { delta, 0.0, 0.0 };
        var (gx, gy) = ForwardGradient(angles, moves, R);
        var recovered = TiltScrewGeometry.CalibrationAxialMoveMicrons(gx, gy, screwCount: 3, radiusMicrons: R);
        Assert.That(recovered, Is.EqualTo(delta).Within(1e-6));
    }

    [Test]
    public void Calibration_FourScrew_RecoversPushPullMove() {
        double delta = 180.0; // screw 1 in / screw 3 out by this amount each
        var angles = new[] { 15.0, 105.0, 195.0, 285.0 }; // screw 3 (index 2) is opposite screw 1
        var moves = new[] { delta, 0.0, -delta, 0.0 };
        var (gx, gy) = ForwardGradient(angles, moves, R);
        var recovered = TiltScrewGeometry.CalibrationAxialMoveMicrons(gx, gy, screwCount: 4, radiusMicrons: R);
        Assert.That(recovered, Is.EqualTo(delta).Within(1e-6));
    }

    [Test]
    public void Backfocus_IsLinearInCurvature_NoSquareRoot() {
        // Doubling the curvature coefficient must double the backfocus correction (it is a length,
        // not a square-rooted quantity).
        var a = TiltScrewGeometry.ScrewCorrectionMicrons(gx: 0, gy: 0, kx: 5e-7, ky: 5e-7, x0: 0, y0: 0, angleDegrees: 40, radiusMicrons: R);
        var b = TiltScrewGeometry.ScrewCorrectionMicrons(gx: 0, gy: 0, kx: 1e-6, ky: 1e-6, x0: 0, y0: 0, angleDegrees: 40, radiusMicrons: R);
        Assert.That(b.BackfocusMicrons, Is.EqualTo(2.0 * a.BackfocusMicrons).Within(1e-9));
    }

    [Test]
    public void Backfocus_IsotropicCurvature_EqualAcrossScrews() {
        // Kx == Ky and centered model => same radius => identical backfocus per screw.
        double k = 8e-7;
        var s1 = TiltScrewGeometry.ScrewCorrectionMicrons(0, 0, k, k, 0, 0, 0, R);
        var s2 = TiltScrewGeometry.ScrewCorrectionMicrons(0, 0, k, k, 0, 0, 120, R);
        var s3 = TiltScrewGeometry.ScrewCorrectionMicrons(0, 0, k, k, 0, 0, 240, R);
        Assert.Multiple(() => {
            Assert.That(s2.BackfocusMicrons, Is.EqualTo(s1.BackfocusMicrons).Within(1e-6));
            Assert.That(s3.BackfocusMicrons, Is.EqualTo(s1.BackfocusMicrons).Within(1e-6));
            Assert.That(s1.BackfocusMicrons, Is.EqualTo(-k * R * R).Within(1e-6));
        });
    }

    [Test]
    public void Backfocus_AstigmaticCurvature_VariesByAngle() {
        var up = TiltScrewGeometry.ScrewCorrectionMicrons(0, 0, kx: 1e-6, ky: 5e-7, x0: 0, y0: 0, angleDegrees: 0, radiusMicrons: R);   // on -y axis
        var right = TiltScrewGeometry.ScrewCorrectionMicrons(0, 0, kx: 1e-6, ky: 5e-7, x0: 0, y0: 0, angleDegrees: 90, radiusMicrons: R); // on +x axis
        // At 0° the point is on the y axis (ky dominates); at 90° on the x axis (kx dominates).
        Assert.That(Math.Abs(right.BackfocusMicrons), Is.GreaterThan(Math.Abs(up.BackfocusMicrons)));
    }

    [Test]
    public void Total_IsSumOfTiltAndBackfocus() {
        var c = TiltScrewGeometry.ScrewCorrectionMicrons(gx: 0.001, gy: -0.0006, kx: 7e-7, ky: 3e-7, x0: 1000, y0: -500, angleDegrees: 55, radiusMicrons: R);
        Assert.That(c.TotalMicrons, Is.EqualTo(c.TiltMicrons + c.BackfocusMicrons).Within(1e-12));
    }

    [TestCase(1)]
    [TestCase(-1)]
    public void InwardAdjustment_SignMatchesLegacyBackfocusRule(int curvatureSign) {
        // Legacy backfocus arrow: needsInward = curvatureEffect * sign < 0, where curvatureEffect is
        // the curvature deviation (= -correction). So needsInward <=> sign * correction > 0.
        double curvatureDeviation = 42.0;        // positive deviation
        double correction = -curvatureDeviation; // correction cancels it
        double inward = TiltScrewGeometry.InwardAdjustment(correction, unitMicrons: 500.0, curvatureSign: curvatureSign);
        bool legacyNeedsInward = curvatureDeviation * curvatureSign < 0;
        Assert.That(inward > 0, Is.EqualTo(legacyNeedsInward));
    }

    [Test]
    public void PitchMismatch_TriggersPastThresholdAndSilentWithin() {
        Assert.Multiple(() => {
            Assert.That(TiltScrewGeometry.PitchMismatchExceeds(500, 500, 0.15), Is.False);
            Assert.That(TiltScrewGeometry.PitchMismatchExceeds(560, 500, 0.15), Is.False); // 12% within
            Assert.That(TiltScrewGeometry.PitchMismatchExceeds(600, 500, 0.15), Is.True);  // 20% exceeds
            Assert.That(TiltScrewGeometry.PitchMismatchExceeds(-1, 500, 0.15), Is.False);  // unset
            Assert.That(TiltScrewGeometry.PitchMismatchExceeds(500, -1, 0.15), Is.False);  // none measured
        });
    }

    [Test]
    public void PlaneGradientToPhysical_DividesByPhysicalExtent() {
        // A = focuser steps per normalized coord; gx = A·stepMicrons / sensorWidthMicrons.
        var (gx, gy) = TiltScrewGeometry.PlaneGradientToPhysical(
            a: 4.0, b: -2.0, focuserStepMicrons: 5.0, sensorWidthMicrons: 10000.0, sensorHeightMicrons: 8000.0);
        Assert.Multiple(() => {
            Assert.That(gx, Is.EqualTo(4.0 * 5.0 / 10000.0).Within(1e-12));
            Assert.That(gy, Is.EqualTo(-2.0 * 5.0 / 8000.0).Within(1e-12));
        });
    }

    [Test]
    public void PhysicalGradientToPlane_IsExactInverseOfPlaneGradientToPhysical() {
        const double stepMicrons = 3.58, sensorW = 6000 * 3.76, sensorH = 4000 * 3.76;
        // Round-trip a plane (A,B) -> physical gradient -> back, and the reverse from a physical gradient.
        var (gx, gy) = TiltScrewGeometry.PlaneGradientToPhysical(7.0, -3.5, stepMicrons, sensorW, sensorH);
        var (a, b) = TiltScrewGeometry.PhysicalGradientToPlane(gx, gy, stepMicrons, sensorW, sensorH);
        Assert.Multiple(() => {
            Assert.That(a, Is.EqualTo(7.0).Within(1e-9));
            Assert.That(b, Is.EqualTo(-3.5).Within(1e-9));
            // Direct formula: A = gx·sensorW/step.
            Assert.That(TiltScrewGeometry.PhysicalGradientToPlane(0.01, 0.0, stepMicrons, sensorW, sensorH).a,
                Is.EqualTo(0.01 * sensorW / stepMicrons).Within(1e-9));
        });
    }

    [Test]
    public void PhysicalGradientToPlane_ReturnsNaN_OnNonPositiveStep() {
        var (a, b) = TiltScrewGeometry.PhysicalGradientToPlane(0.01, 0.02, 0.0, 1000, 1000);
        Assert.That(double.IsNaN(a) && double.IsNaN(b), Is.True);
    }
}
