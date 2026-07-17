using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// The automated form of the interactive loop: inject a tilt, ask the REAL guidance math what to turn,
/// turn exactly that on the simulated adapter, and assert the tilt is gone. A private reimplementation of
/// the geometry would pass the unit tests and fail here.
///
/// These tests deliberately cross the UNITS boundary — they compute turns/steps exactly the way
/// InspectorVM.FillNumericGuidance does (corr.TiltMicrons / unitMicrons, and
/// resolvedSign * corr.BackfocusMicrons / unitMicrons) and feed them through
/// SimulatedTiltAdapter.AxialMicronsForUnits. Handing ApplyMoves pre-computed axial microns instead would
/// leave unitMicrons and curvatureSign inert, and a turn→axial sign error — the one defect that makes the
/// loop diverge rather than converge — would go undetected.
/// </summary>
[TestFixture]
public class SimulatedTiltAdapterCapstoneTests {

    private const double RadiusMicrons = 30_000.0;

    // Equally spaced screws, optionally rotated off the axes so no test can lean on screw 1 sitting at 0°.
    private static double[] Angles(int screwCount, double rotationOffsetDegrees = 0.0) =>
        Enumerable.Range(0, screwCount)
            .Select(i => rotationOffsetDegrees + i * 360.0 / screwCount)
            .ToArray();

    /// <summary>Exactly what InspectorVM.FillNumericGuidance computes for each screw row.</summary>
    private static ScrewAxialCorrection Guidance(
            double gx, double gy, double kx, double ky, double angleDegrees) =>
        TiltScrewGeometry.ScrewCorrectionMicrons(gx, gy, kx, ky, x0: 0.0, y0: 0.0,
            angleDegrees: angleDegrees, radiusMicrons: RadiusMicrons);

    [Test]
    [Combinatorial]
    public void InspectorGuidance_AppliedToSimulator_ZeroesTheTilt(
            [Values(3, 4)] int screwCount,
            [Values(250.0, 1.5)] double unitMicrons,     // a screw pitch, and a stepper step size
            [Values(-1, 1)] int curvatureSign,
            [Values(0.0, 37.0)] double rotationOffset) {

        var angles = Angles(screwCount, rotationOffset);
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, unitMicrons, curvatureSign);

        // An injected tilt, in the paraboloid model's units (focuser µm per sensor µm).
        const double gx = 2.1e-4, gy = -1.3e-4;

        // What the inspector would print on each screw row, in TURNS (or steps) — the real guidance helper,
        // divided by the unit exactly as FillNumericGuidance does. No curvature sign: the tilt component
        // never takes one (TiltScrewGeometryTests.SignedTotalAdjustment_AppliesSignOnlyToBackfocus).
        var turns = angles
            .Select(a => Guidance(gx, gy, kx: 0.0, ky: 0.0, angleDegrees: a).TiltMicrons / unitMicrons)
            .ToArray();

        // The user dials in that many turns on each row; the adapter converts back to axial travel.
        var delta = adapter.ApplyMoves(turns.Select(adapter.AxialMicronsForUnits).ToArray());

        // The injected tilt plus the guidance-driven change must cancel.
        Assert.Multiple(() => {
            Assert.That(gx + delta.Gx, Is.EqualTo(0.0).Within(1e-12), "Gx did not cancel");
            Assert.That(gy + delta.Gy, Is.EqualTo(0.0).Within(1e-12), "Gy did not cancel");
        });
    }

    [Test]
    [Combinatorial]
    public void InspectorBackfocusGuidance_AppliedToSimulator_CancelsTheCurvature(
            [Values(3, 4)] int screwCount,
            [Values(250.0, 1.5)] double unitMicrons,
            [Values(-1, 1)] int curvatureSign) {

        var angles = Angles(screwCount);
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, unitMicrons, curvatureSign);

        // A pure field-curvature error: no tilt, symmetric curvature centred on the sensor.
        const double k = 3.0e-9;   // focuser µm per sensor µm²

        // The inspector's backfocus row DOES carry the curvature sign (FillNumericGuidance:
        // resolvedSign * corr.BackfocusMicrons / unitMicrons) — this is the one component that does.
        var turns = angles
            .Select(a => curvatureSign * Guidance(0.0, 0.0, k, k, a).BackfocusMicrons / unitMicrons)
            .ToArray();

        var delta = adapter.ApplyMoves(turns.Select(adapter.AxialMicronsForUnits).ToArray());

        // Curvature is rotationally symmetric, so every screw is asked for the same axial correction.
        var requestedAxialMicrons = Guidance(0.0, 0.0, k, k, angles[0]).BackfocusMicrons;

        Assert.Multiple(() => {
            // The two sign applications (guidance's, and the response frame's) cancel: the physical piston
            // is exactly the axial correction the curvature model asked for, on BOTH rig directions.
            Assert.That(adapter.PistonDirectionSign * delta.PistonMicrons,
                Is.EqualTo(requestedAxialMicrons).Within(1e-9),
                "backfocus guidance must deliver the requested axial correction on either rig direction");
            // ...and a symmetric correction must not introduce tilt.
            Assert.That(delta.Gx, Is.EqualTo(0.0).Within(1e-15), "backfocus guidance must not tilt the sensor");
            Assert.That(delta.Gy, Is.EqualTo(0.0).Within(1e-15), "backfocus guidance must not tilt the sensor");
        });
    }

    [Test]
    public void PureBackfocusMove_LeavesTiltUntouched([Values(3, 4)] int screwCount) {
        var angles = Angles(screwCount);
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, 250.0, -1);

        var delta = adapter.ApplyMoves(Enumerable.Repeat(100.0, screwCount).ToArray());

        Assert.Multiple(() => {
            Assert.That(delta.Gx, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.Gy, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.PistonMicrons, Is.EqualTo(100.0).Within(1e-9), "all-equal moves are pure piston");
        });
    }

    [Test]
    public void FourScrewCornerMove_PistonsZero() {
        // A diagonal pair turned opposite ways tilts toward a corner and must not change backfocus.
        var adapter = new SimulatedTiltAdapter(Angles(4), RadiusMicrons, 250.0, -1);
        var delta = adapter.ApplyMoves(new[] { 100.0, 0.0, -100.0, 0.0 });

        Assert.That(delta.PistonMicrons, Is.EqualTo(0.0).Within(1e-9),
            "a corner move is antisymmetric, so it cannot change backfocus");
    }

    [Test]
    public void RepeatedGuidanceRounds_Converge([Values(3, 4)] int screwCount) {
        // The loop the user actually runs: measure, turn, re-measure. Each round applies the guidance for
        // the CURRENT residual, so a sign error shows up as divergence (doubling) rather than convergence.
        var angles = Angles(screwCount, rotationOffsetDegrees: 12.0);
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, 250.0,
            TiltScrewGeometry.DefaultScrewInwardCurvatureSign);

        double gx = 5.0e-4, gy = 3.0e-4;
        var initialMagnitude = Math.Sqrt(gx * gx + gy * gy);

        for (var round = 0; round < 3; round++) {
            // Only 90% of the guidance is applied, modelling a user who under-turns; the residual must
            // still shrink monotonically towards zero rather than grow or oscillate.
            var localGx = gx;
            var localGy = gy;
            var turns = angles
                .Select(a => 0.9 * Guidance(localGx, localGy, 0.0, 0.0, a).TiltMicrons / adapter.UnitMicrons)
                .ToArray();
            var delta = adapter.ApplyMoves(turns.Select(adapter.AxialMicronsForUnits).ToArray());
            gx += delta.Gx;
            gy += delta.Gy;
        }

        // Three rounds at 90% each leaves (0.1)³ = 0.1% of the original tilt.
        Assert.That(Math.Sqrt(gx * gx + gy * gy),
            Is.EqualTo(0.001 * initialMagnitude).Within(1e-9 * initialMagnitude),
            "each guidance round must remove 90% of the residual tilt");
    }
}
