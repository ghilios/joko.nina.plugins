using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class SimulatedTiltAdapterTests {

    private const double RadiusMicrons = 30_000.0;
    private const double PitchMicrons = 500.0;

    // A 3-screw adapter at 0/120/240°, R = 30 mm, 500 µm per turn.
    private static SimulatedTiltAdapter ThreeScrew(int? inwardCurvatureSign = null) =>
        new SimulatedTiltAdapter(
            screwAnglesDegrees: new[] { 0.0, 120.0, 240.0 },
            screwRadiusMicrons: RadiusMicrons,
            unitMicrons: PitchMicrons,
            inwardCurvatureSign: inwardCurvatureSign ?? TiltScrewGeometry.DefaultScrewInwardCurvatureSign);

    [Test]
    public void SingleScrewMove_ProducesTiltAndOneThirdPiston() {
        var adapter = ThreeScrew();
        // Turn screw 1 by exactly one unit's worth of axial travel.
        var delta = adapter.ApplyMoves(new[] { 500.0, 0.0, 0.0 });

        // A plane through (p1, 500), (p2, 0), (p3, 0): the mean of the three displacements is the piston.
        Assert.That(delta.PistonMicrons, Is.EqualTo(500.0 / 3.0).Within(1e-9));
        // ...and it must tilt: the gradient cannot be zero.
        Assert.That(Math.Sqrt(delta.Gx * delta.Gx + delta.Gy * delta.Gy), Is.GreaterThan(0.0));
    }

    [Test]
    public void EqualMovesOnAllScrews_ArePurePiston() {
        var adapter = ThreeScrew();
        var delta = adapter.ApplyMoves(new[] { 250.0, 250.0, 250.0 });
        Assert.Multiple(() => {
            Assert.That(delta.Gx, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.Gy, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.PistonMicrons, Is.EqualTo(250.0).Within(1e-9));
        });
    }

    [Test]
    public void MovesThatCancelAKnownGradient_ZeroIt() {
        var adapter = ThreeScrew();
        const double gx = 1.5e-4, gy = -0.8e-4;   // dimensionless focuser-µm per sensor-µm
        // TiltScrewGeometry says this is the axial move at each screw that cancels (gx,gy).
        var moves = new[] { 0.0, 120.0, 240.0 }
            .Select(a => TiltScrewGeometry.TiltCorrectionMicrons(gx, gy, a, RadiusMicrons)).ToArray();

        var delta = adapter.ApplyMoves(moves);

        // Applying the cancelling moves must produce exactly the negated gradient.
        Assert.Multiple(() => {
            Assert.That(delta.Gx, Is.EqualTo(-gx).Within(1e-12));
            Assert.That(delta.Gy, Is.EqualTo(-gy).Within(1e-12));
        });
    }

    [Test]
    public void AxialMicronsForUnits_ScalesByTheUnitAndCarriesTheButtonDirection() {
        var adapter = ThreeScrew();
        Assert.Multiple(() => {
            Assert.That(adapter.AxialMicronsForUnits(1.0), Is.EqualTo(500.0).Within(1e-12));
            Assert.That(adapter.AxialMicronsForUnits(0.5), Is.EqualTo(250.0).Within(1e-12));
            // Direction comes from the button (the sign of `units`), never from the rig.
            Assert.That(adapter.AxialMicronsForUnits(-0.25), Is.EqualTo(-125.0).Within(1e-12));
            Assert.That(adapter.AxialMicronsForUnits(0.0), Is.EqualTo(0.0).Within(1e-12));
        });
    }

    [Test]
    public void AxialMicronsForUnits_IsIndependentOfTheRigDirection() {
        // REGRESSION PIN — the mirror image of TiltScrewGeometryTests'
        // SignedTotalAdjustment_AppliesSignOnlyToBackfocus ("a pure-tilt correction must render the same
        // rotational direction on every rig — the curvature sign must NOT touch it").
        //
        // The inspector prints tilt turns as TiltCorrectionMicrons(...)/unit with NO sign factor, because
        // the stored response-convention screw angle already encodes the rig direction (a CW turn of the
        // screw stored at θ raises the gradient along +θ, by construction of the calibration). The exact
        // inverse must therefore also take no sign factor. Re-applying the curvature sign here would make
        // the simulator move BACKWARDS on one of the two rig directions, so the guidance loop would
        // diverge instead of converge — silently, and only on half the rigs.
        var plusRig = ThreeScrew(inwardCurvatureSign: +1);
        var minusRig = ThreeScrew(inwardCurvatureSign: -1);
        Assert.Multiple(() => {
            Assert.That(plusRig.AxialMicronsForUnits(0.5), Is.EqualTo(250.0).Within(1e-12));
            Assert.That(minusRig.AxialMicronsForUnits(0.5), Is.EqualTo(250.0).Within(1e-12));
        });
    }

    [Test]
    public void PistonDirectionSign_IsTheOnePlaceTheRigDirectionSurvives() {
        // The response frame is a point reflection of the physical frame on -1 rigs, so the fitted
        // GRADIENT is frame-invariant (both δ and p flip) but the PISTON is not (there is no angle to
        // absorb the flip) — exactly mirroring the guidance, which applies the curvature sign to the
        // backfocus row only. Capstone InspectorBackfocusGuidance_... proves this end to end.
        Assert.Multiple(() => {
            Assert.That(ThreeScrew(inwardCurvatureSign: +1).PistonDirectionSign, Is.EqualTo(+1));
            Assert.That(ThreeScrew(inwardCurvatureSign: -1).PistonDirectionSign, Is.EqualTo(-1));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(5)]
    public void Constructor_RejectsAdaptersThatAreNotThreeOrFourScrew(int screwCount) {
        var angles = Enumerable.Range(0, screwCount).Select(i => i * 90.0).ToArray();
        Assert.That(() => new SimulatedTiltAdapter(angles, RadiusMicrons, PitchMicrons, +1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ApplyMoves_RejectsAMoveArrayThatIsNotOnePerScrew() {
        var adapter = ThreeScrew();
        Assert.That(() => adapter.ApplyMoves(new[] { 1.0, 2.0 }), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Constructor_CopiesTheAngles_SoLaterMutationOfTheCallersArrayCannotChangeGeometry() {
        var angles = new[] { 0.0, 120.0, 240.0 };
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, PitchMicrons, +1);
        var before = adapter.ApplyMoves(new[] { 500.0, 0.0, 0.0 });
        angles[0] = 47.0;
        var after = adapter.ApplyMoves(new[] { 500.0, 0.0, 0.0 });
        Assert.Multiple(() => {
            Assert.That(after.Gx, Is.EqualTo(before.Gx).Within(1e-15));
            Assert.That(after.Gy, Is.EqualTo(before.Gy).Within(1e-15));
        });
    }
}
