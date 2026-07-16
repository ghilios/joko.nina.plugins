#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// The panel VM is the *other half* of the interactive loop: SimulatedTiltAdapterCapstoneTests proves the pure
/// model inverts the guidance, and these prove the VM folds that model's delta into the simulator's persisted
/// aberration without losing a sign on the way. The load-bearing cases here all drive the REAL guidance math
/// (TiltScrewGeometry) rather than hand-rolled expectations, so a private reimplementation cannot pass them.
/// </summary>
[TestFixture]
public class SimulatedTiltAdapterVMTests {

    private const double PitchMicrons = 250.0;
    private const double RadiusMillimeters = 30.0;

    private static CameraSimulatorOptions BuildOptions() =>
        new CameraSimulatorOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

    private static TiltAdapterOptions BuildRealAdapterOptions() =>
        new TiltAdapterOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

    /// <summary>Angles in the stored response convention — the same quantity ITiltAdapterOptions holds.</summary>
    private static double[] Angles(int screwCount) =>
        screwCount == 3 ? new[] { 0.0, 120.0, 240.0 } : new[] { 45.0, 135.0, 225.0, 315.0 };

    private static CameraSimulatorOptions Configured(int screwCount, int curvatureSign = 1,
            double unitMicrons = PitchMicrons, TiltAdjustmentType type = TiltAdjustmentType.Screws) {
        var options = BuildOptions();
        var angles = Angles(screwCount);
        options.SimScrewCount = screwCount;
        options.SimScrew1AngleDegrees = angles[0];
        options.SimScrew2AngleDegrees = angles[1];
        options.SimScrew3AngleDegrees = angles[2];
        options.SimScrew4AngleDegrees = screwCount == 4 ? angles[3] : double.NaN;
        options.SimScrewInwardCurvatureSign = curvatureSign;
        options.SimAdjustmentType = type;
        if (type == TiltAdjustmentType.Screws) {
            options.SimThreadPitchMicrons = unitMicrons;
        } else {
            options.SimStepperStepSizeMicrons = unitMicrons;
        }
        options.SimScrewRadiusMillimeters = RadiusMillimeters;
        options.EnableAberrations = true;
        return options;
    }

    private static (double halfW, double halfH) SensorHalfDimensions(CameraSimulatorOptions options) {
        var sensor = SensorRegistry.Get(options.SensorModel);
        return (sensor.Width * sensor.PixelSizeMicrons / 2.0, sensor.Height * sensor.PixelSizeMicrons / 2.0);
    }

    /// <summary>The (Gx, Gy) the injected (azimuth, amount) pair encodes — AberrationSurface's inversion.</summary>
    private static (double gx, double gy) InjectedGradient(CameraSimulatorOptions options) {
        var (halfW, halfH) = SensorHalfDimensions(options);
        var phi = options.TiltAngleDegrees * Math.PI / 180.0;
        var den = Math.Abs(Math.Cos(phi)) * halfW + Math.Abs(Math.Sin(phi)) * halfH;
        var g = den > 0 ? options.TiltAmountMicrons / den : 0.0;
        return (g * Math.Cos(phi), g * Math.Sin(phi));
    }

    /// <summary>The isotropic curvature coefficient K the injected backfocus error encodes.</summary>
    private static double InjectedCurvature(CameraSimulatorOptions options) {
        var (halfW, halfH) = SensorHalfDimensions(options);
        return options.BackfocusErrorMicrons / (halfW * halfW + halfH * halfH);
    }

    // ---- The plan's two tests, strengthened -------------------------------------------------------

    [Test]
    public void Turn_FoldsDeltaIntoOptionsAndConverges() {
        var options = Configured(screwCount: 3);
        options.TiltAmountMicrons = 40.0;
        options.TiltAngleDegrees = 30.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        var before = options.TiltAmountMicrons;

        vm.AmountPerClick = 0.25;
        vm.TurnCommand.Execute(new ScrewTurn(screwIndex: 0, rotationSign: -1));

        Assert.That(options.TiltAmountMicrons, Is.Not.EqualTo(before), "a turn must change the injected tilt");
        Assert.That(vm.LastActionText, Does.Contain("Screw 1"));

        // The plan's assertions above pass against almost any implementation that writes *something*, so pin the
        // exact physics: a ⟲ 0.25 on screw 1 must land the plane where the pure model says it does.
        var adapter = new SimulatedTiltAdapter(Angles(3), RadiusMillimeters * 1000.0, PitchMicrons,
            options.SimScrewInwardCurvatureSign);
        var (gx0, gy0) = InjectedGradient(Configured3At(40.0, 30.0));
        var delta = adapter.ApplyMoves(new[] { adapter.AxialMicronsForUnits(-0.25), 0.0, 0.0 });
        var (halfW, halfH) = SensorHalfDimensions(options);
        var expectedAmount = Math.Abs(gx0 + delta.Gx) * halfW + Math.Abs(gy0 + delta.Gy) * halfH;

        Assert.That(options.TiltAmountMicrons, Is.EqualTo(expectedAmount).Within(1e-6),
            "the folded tilt must equal the pure model's plane change added to the injected gradient");
    }

    private static CameraSimulatorOptions Configured3At(double amount, double angle) {
        var o = Configured(3);
        o.TiltAmountMicrons = amount;
        o.TiltAngleDegrees = angle;
        return o;
    }

    [Test]
    public void Undo_RestoresThePreviousState() {
        var options = Configured(screwCount: 3);
        options.TiltAmountMicrons = 40.0;
        options.TiltAngleDegrees = 30.0;
        options.BackfocusErrorMicrons = -5.0;
        options.OptimalFocuserPosition = 5000;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        vm.AmountPerClick = 0.5;
        vm.TurnCommand.Execute(new ScrewTurn(0, +1));

        // A single screw on a 3-screw adapter is not a pure tilt — it pistons by δ/3, so backfocus and the
        // focuser position must move too. Assert they actually did, otherwise the undo assertions below would
        // pass vacuously against a VM that never touched them.
        Assert.Multiple(() => {
            Assert.That(options.TiltAmountMicrons, Is.Not.EqualTo(40.0).Within(1e-9));
            Assert.That(options.BackfocusErrorMicrons, Is.Not.EqualTo(-5.0).Within(1e-9));
            Assert.That(options.OptimalFocuserPosition, Is.Not.EqualTo(5000));
            Assert.That(vm.NetAxialMicrons[0], Is.Not.EqualTo(0.0).Within(1e-9));
        });

        vm.UndoCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(options.TiltAngleDegrees, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(-5.0).Within(1e-9));
            Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000));
            Assert.That(vm.NetAxialMicrons[0], Is.EqualTo(0.0).Within(1e-9), "undo must also rewind the net counter");
            Assert.That(vm.CanUndo, Is.False, "undo is single-level — it must disarm after use");
        });
    }

    // ---- The piston sign: the one place the rig direction survives ---------------------------------

    /// <summary>
    /// ScrewInwardCurvatureSign is DEFINED as "the sign of the curvature-effect response to a CW turn"
    /// (TiltScrewGeometry's empirical anchor). An all-screws-CW move is a pure piston, so this is the tightest
    /// possible statement of that definition — and it fails if the VM folds delta.PistonMicrons raw (no σ), or
    /// folds it with the wrong outer sign.
    /// </summary>
    [Test]
    public void BackfocusMove_ChangesBackfocusErrorWithTheSignOfTheRigDirection([Values(-1, 1)] int curvatureSign) {
        var options = Configured(screwCount: 4, curvatureSign: curvatureSign);
        options.BackfocusErrorMicrons = 0.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Backfocus, AmountPerClick = 0.5 };
        vm.TurnCommand.Execute(new ScrewTurn(0, +1)); // ⟳ on all four

        Assert.That(Math.Sign(options.BackfocusErrorMicrons), Is.EqualTo(curvatureSign),
            "a CW turn must move the curvature effect in the direction ScrewInwardCurvatureSign declares");
    }

    /// <summary>
    /// The explicit σ=−1 vs σ=+1 comparison. Without it, dropping PistonDirectionSign is invisible: it is
    /// correct by coincidence on default (σ=+1) rigs and silently backwards on the other half.
    /// </summary>
    [Test]
    public void BackfocusMove_OnOppositeRigs_MovesBackfocusInOppositeDirections() {
        double Apply(int curvatureSign) {
            var options = Configured(screwCount: 4, curvatureSign: curvatureSign);
            options.BackfocusErrorMicrons = 0.0;
            var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Backfocus, AmountPerClick = 0.5 };
            vm.TurnCommand.Execute(new ScrewTurn(0, +1));
            return options.BackfocusErrorMicrons;
        }

        var positiveRig = Apply(+1);
        var negativeRig = Apply(-1);

        Assert.Multiple(() => {
            Assert.That(positiveRig, Is.Not.EqualTo(0.0).Within(1e-9));
            Assert.That(positiveRig * negativeRig, Is.LessThan(0.0),
                "the same rotation must fold backfocus the opposite way on a σ=−1 rig");
            Assert.That(positiveRig, Is.EqualTo(-negativeRig).Within(1e-9), "and by an equal magnitude");
        });
    }

    /// <summary>The focuser shift is the same piston, so it carries the same sign asymmetry.</summary>
    [Test]
    public void BackfocusMove_OnOppositeRigs_ShiftsTheFocuserInOppositeDirections() {
        int Apply(int curvatureSign) {
            var options = Configured(screwCount: 4, curvatureSign: curvatureSign);
            options.OptimalFocuserPosition = 5000;
            var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Backfocus, AmountPerClick = 0.5 };
            vm.TurnCommand.Execute(new ScrewTurn(0, +1));
            return options.OptimalFocuserPosition - 5000;
        }

        var positiveRig = Apply(+1);
        var negativeRig = Apply(-1);

        Assert.Multiple(() => {
            Assert.That(positiveRig, Is.Not.Zero);
            Assert.That(positiveRig, Is.EqualTo(-negativeRig),
                "OptimalFocuserPosition must shift the opposite way on a σ=−1 rig — it is the same physical piston");
        });
    }

    /// <summary>
    /// The backfocus half of the closed loop, driven by the REAL guidance math the inspector prints
    /// (TiltScrewGeometry.ScrewCorrectionMicrons + the σ-on-backfocus-only convention of
    /// SignedTotalAdjustment). This is the honest end-to-end statement of "applying what the inspector says
    /// converges", and it fails on the σ=−1 half if PistonDirectionSign is dropped.
    /// </summary>
    [Test]
    [Combinatorial]
    public void InspectorBackfocusGuidance_AppliedThroughThePanel_ZeroesTheBackfocusError(
            [Values(3, 4)] int screwCount,
            [Values(-1, 1)] int curvatureSign) {
        var options = Configured(screwCount, curvatureSign);
        options.TiltAmountMicrons = 0.0;
        options.BackfocusErrorMicrons = 40.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        var radiusMicrons = RadiusMillimeters * 1000.0;
        var k = InjectedCurvature(options);

        // Exactly what InspectorVM.FillNumericGuidance emits on its Backfocus row: the physical axial
        // requirement, converted to a rotation by the curvature sign (and by nothing else).
        var corr = TiltScrewGeometry.ScrewCorrectionMicrons(
            gx: 0, gy: 0, kx: k, ky: k, x0: 0, y0: 0, angleDegrees: Angles(screwCount)[0], radiusMicrons: radiusMicrons);
        var turns = curvatureSign * corr.BackfocusMicrons / PitchMicrons;

        Assert.That(turns, Is.Not.EqualTo(0.0).Within(1e-9), "guard: the guidance must actually ask for a move");
        vm.AmountPerClick = Math.Abs(turns);
        var rotationSign = Math.Sign(turns);

        if (screwCount == 4) {
            // One Backfocus-mode click turns all four the same way.
            vm.MovementMode = SimTiltMovementMode.Backfocus;
            vm.TurnCommand.Execute(new ScrewTurn(0, rotationSign));
        } else {
            // A 3-screw rig has no Backfocus mode — the user executes the per-screw guidance, one row at a time.
            for (var i = 0; i < 3; i++) {
                vm.TurnCommand.Execute(new ScrewTurn(i, rotationSign));
            }
        }

        Assert.Multiple(() => {
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(0.0).Within(1e-6),
                "applying the inspector's backfocus guidance must null the injected backfocus error");
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0).Within(1e-6),
                "and must not introduce tilt");
        });
    }

    /// <summary>The tilt half of the closed loop — the panel's headline promise, per screw count.</summary>
    [Test]
    public void InspectorTiltGuidance_AppliedThroughThePanel_FlattensTheTilt([Values(3, 4)] int screwCount) {
        var options = Configured(screwCount);
        options.TiltAmountMicrons = 80.0;
        options.TiltAngleDegrees = 30.0;
        options.BackfocusErrorMicrons = 0.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        var (gx, gy) = InjectedGradient(options);
        var radiusMicrons = RadiusMillimeters * 1000.0;
        var angles = Angles(screwCount);

        // The inspector's Tilt row is sign-free: corr.TiltMicrons / unit, with no curvature factor.
        double Turns(int i) => TiltScrewGeometry
            .ScrewCorrectionMicrons(gx, gy, 0, 0, 0, 0, angles[i], radiusMicrons).TiltMicrons / PitchMicrons;

        // On a 4-screw adapter the guidance is pair-antisymmetric, so only the leading screw of each pair is
        // clicked — Corner mode counter-turns the partner (that is the whole point of the mode).
        var rowsToClick = screwCount == 4 ? 2 : 3;
        for (var i = 0; i < rowsToClick; i++) {
            var t = Turns(i);
            vm.AmountPerClick = Math.Abs(t);
            vm.TurnCommand.Execute(new ScrewTurn(i, Math.Sign(t)));
        }

        Assert.Multiple(() => {
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0).Within(1e-6), "the injected tilt must cancel");
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(0.0).Within(1e-6),
                "a pure-tilt correction cannot change backfocus");
            Assert.That(vm.IsFlat, Is.True, "the state strip's finish line must light up");
        });
    }

    // ---- Movement modes -----------------------------------------------------------------------------

    [Test]
    public void CornerMove_CounterTurnsTheOppositeScrewAndLeavesBackfocusAlone() {
        var options = Configured(screwCount: 4);
        options.BackfocusErrorMicrons = -5.0;
        options.OptimalFocuserPosition = 5000;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Corner, AmountPerClick = 0.5 };
        vm.TurnCommand.Execute(new ScrewTurn(1, +1)); // Screw 2 ⟳

        var delta = 0.5 * PitchMicrons;
        Assert.Multiple(() => {
            Assert.That(vm.NetAxialMicrons.ToArray(), Is.EqualTo(new[] { 0.0, delta, 0.0, -delta }).Within(1e-9),
                "the named screw follows the glyph; its opposite counter-turns");
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(-5.0).Within(1e-9),
                "a corner move is antisymmetric, so it cannot change backfocus");
            Assert.That(options.OptimalFocuserPosition, Is.EqualTo(5000), "nor best focus");
            Assert.That(options.TiltAmountMicrons, Is.GreaterThan(0.0), "but it must tilt");
        });
    }

    [Test]
    public void SideMove_TurnsTheNamedPairTogetherAndCounterTurnsTheOpposingPair() {
        var options = Configured(screwCount: 4);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Side, AmountPerClick = 0.5 };
        vm.TurnCommand.Execute(new ScrewTurn(0, +1)); // Side 1+2 ⟳

        var delta = 0.5 * PitchMicrons;
        Assert.Multiple(() => {
            Assert.That(vm.NetAxialMicrons.ToArray(), Is.EqualTo(new[] { delta, delta, -delta, -delta }).Within(1e-9));
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(0.0).Within(1e-9),
                "a side move is antisymmetric too — tilt about the edge axis only");
        });
    }

    [Test]
    public void BackfocusMode_TurnsAllFourTheSameWayAndLeavesTiltAlone() {
        var options = Configured(screwCount: 4);
        options.TiltAmountMicrons = 40.0;
        options.TiltAngleDegrees = 30.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Backfocus, AmountPerClick = 0.5 };
        vm.TurnCommand.Execute(new ScrewTurn(0, -1));

        var delta = -0.5 * PitchMicrons;
        Assert.Multiple(() => {
            Assert.That(vm.NetAxialMicrons.ToArray(), Is.EqualTo(new[] { delta, delta, delta, delta }).Within(1e-9));
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(40.0).Within(1e-6), "pure piston leaves tilt untouched");
            Assert.That(options.TiltAngleDegrees, Is.EqualTo(30.0).Within(1e-6));
        });
    }

    [Test]
    public void ThreeScrewRig_HasOneRowPerScrewAndNoMovementModeSelector() {
        var vm = new SimulatedTiltAdapterVM(Configured(screwCount: 3), realAdapterOptions: null);
        Assert.Multiple(() => {
            Assert.That(vm.ShowMovementModeSelector, Is.False);
            Assert.That(vm.Rows, Has.Count.EqualTo(3));
            Assert.That(vm.Rows[1].Label, Does.Contain("Screw 2"));
            Assert.That(vm.Rows[1].CouplingText, Is.Empty, "3-screw rows are independent — nothing opposes");
        });
    }

    [Test]
    public void FourScrewRig_RowsChangeIdentityWithTheMovementMode() {
        var vm = new SimulatedTiltAdapterVM(Configured(screwCount: 4), realAdapterOptions: null);
        Assert.That(vm.ShowMovementModeSelector, Is.True);

        vm.MovementMode = SimTiltMovementMode.Corner;
        Assert.Multiple(() => {
            Assert.That(vm.Rows, Has.Count.EqualTo(4));
            Assert.That(vm.Rows[1].Label, Does.Contain("Screw 2"));
            Assert.That(vm.Rows[1].CouplingText, Does.Contain("4 opposes"));
        });

        vm.MovementMode = SimTiltMovementMode.Side;
        Assert.Multiple(() => {
            Assert.That(vm.Rows, Has.Count.EqualTo(4));
            Assert.That(vm.Rows[0].Label, Does.Contain("Side 1+2"));
            Assert.That(vm.Rows[0].CouplingText, Does.Contain("3+4 oppose"));
        });

        vm.MovementMode = SimTiltMovementMode.Backfocus;
        Assert.Multiple(() => {
            Assert.That(vm.Rows, Has.Count.EqualTo(1));
            Assert.That(vm.Rows[0].Label, Does.Contain("All screws"));
        });
    }

    // ---- The glyph contract -------------------------------------------------------------------------

    [Test]
    public void Screws_SpeakRotationOnButtonsAndMotionInFeedback() {
        var options = Configured(screwCount: 4, curvatureSign: TiltScrewGeometry.DefaultScrewInwardCurvatureSign);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Corner, AmountPerClick = 0.75 };
        vm.TurnCommand.Execute(new ScrewTurn(1, +1)); // Screw 2 ⟳

        Assert.Multiple(() => {
            Assert.That(vm.PositiveGlyph, Is.EqualTo("⟳"), "buttons carry rotation");
            Assert.That(vm.NegativeGlyph, Is.EqualTo("⟲"));
            Assert.That(vm.AmountUnit, Is.EqualTo("turns"));

            Assert.That(vm.LastActionText, Does.Contain("⟳"), "the action names the rotation applied");
            Assert.That(vm.LastActionText, Does.Contain("S2 ⬇"), "and its consequence in honest motion arrows");
            Assert.That(vm.LastActionText, Does.Contain("S4 ⬆"), "including the counter-turned partner");

            Assert.That(vm.Rows[1].PositiveTooltip, Does.Contain("⟳"));
            Assert.That(vm.Rows[1].PositiveTooltip, Does.Contain("toward the camera"),
                "tooltips state the motion consequence, computed live from the rig direction");
            Assert.That(vm.Rows[1].PositiveTooltip, Does.Not.Contain("⬆ 0"), "never present a motion arrow as a rotation");
        });
    }

    /// <summary>Motion is rig-specific: the same ⟳ on a σ=−1 rig drives the plate the other way.</summary>
    [Test]
    public void MotionArrows_FollowTheRigDirection() {
        string Feedback(int curvatureSign) {
            var vm = new SimulatedTiltAdapterVM(Configured(screwCount: 4, curvatureSign: curvatureSign), realAdapterOptions: null) {
                MovementMode = SimTiltMovementMode.Corner,
                AmountPerClick = 0.5
            };
            vm.TurnCommand.Execute(new ScrewTurn(0, +1));
            return vm.LastActionText;
        }

        Assert.Multiple(() => {
            Assert.That(Feedback(+1), Does.Contain("S1 ⬇"));
            Assert.That(Feedback(-1), Does.Contain("S1 ⬆"));
        });
    }

    [Test]
    public void Steppers_ReplaceRotationGlyphsWithSignedSteps() {
        var options = Configured(screwCount: 3, unitMicrons: 1.5, type: TiltAdjustmentType.StepperMotors);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { AmountPerClick = 12 };
        vm.TurnCommand.Execute(new ScrewTurn(1, -1));

        Assert.Multiple(() => {
            Assert.That(vm.PositiveGlyph, Is.EqualTo("+"));
            Assert.That(vm.NegativeGlyph, Is.EqualTo("−"));
            Assert.That(vm.AmountUnit, Is.EqualTo("steps"));
            Assert.That(vm.NetUnitLabel, Is.EqualTo("steps"));
            Assert.That(vm.LastActionText, Does.Not.Contain("⟳"), "motors abstract rotation — no rotation glyphs anywhere");
            Assert.That(vm.LastActionText, Does.Not.Contain("⟲"));
            Assert.That(vm.Rows[1].NegativeTooltip, Does.Not.Contain("⟲"));
            Assert.That(vm.NetAxialMicrons[1], Is.EqualTo(-12 * 1.5).Within(1e-9), "steps × step size = axial µm");
        });
    }

    // ---- Net counters -------------------------------------------------------------------------------

    [Test]
    public void NetCounters_AreStoredInMicronsSoAPitchEditRescalesTheDisplay() {
        var options = Configured(screwCount: 3);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { AmountPerClick = 1.0 };
        vm.TurnCommand.Execute(new ScrewTurn(0, +1));

        Assert.That(vm.NetAxialMicrons[0], Is.EqualTo(PitchMicrons).Within(1e-9));
        Assert.That(vm.NetPositionText, Does.Contain("1: +1.00"));

        options.SimThreadPitchMicrons = PitchMicrons / 2.0;

        Assert.Multiple(() => {
            Assert.That(vm.NetAxialMicrons[0], Is.EqualTo(PitchMicrons).Within(1e-9), "µm is canonical — it must not be rewritten");
            Assert.That(vm.NetPositionText, Does.Contain("1: +2.00"), "the display re-scales to the new pitch");
        });
    }

    [Test]
    public void Rezero_RebasesTheDisplayOnlyAndNeverTheSimulatedPlane() {
        var options = Configured(screwCount: 3);
        options.TiltAmountMicrons = 0.0;
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { AmountPerClick = 0.5 };
        vm.TurnCommand.Execute(new ScrewTurn(0, +1));

        var tilt = options.TiltAmountMicrons;
        var backfocus = options.BackfocusErrorMicrons;
        Assert.That(tilt, Is.GreaterThan(0.0), "guard: the click must have moved the plane");

        vm.RezeroCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.NetAxialMicrons.All(v => v == 0.0), Is.True);
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(tilt).Within(1e-9), "re-zero must not move screws");
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(backfocus).Within(1e-9));
        });
    }

    // ---- State strip --------------------------------------------------------------------------------

    [Test]
    public void IsFlat_RequiresBothTiltAndBackfocusUnderOneMicron() {
        var options = Configured(screwCount: 3);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);

        options.TiltAmountMicrons = 0.4;
        options.BackfocusErrorMicrons = 0.4;
        Assert.That(vm.IsFlat, Is.True);

        options.BackfocusErrorMicrons = -5.0;
        Assert.That(vm.IsFlat, Is.False, "a flat-but-mis-spaced sensor is not the loop's finish line");

        options.BackfocusErrorMicrons = 0.0;
        options.TiltAmountMicrons = 12.4;
        Assert.That(vm.IsFlat, Is.False);
    }

    [Test]
    public void StateStrip_TracksDirectEditsOfTheInjectedAberration() {
        var options = Configured(screwCount: 3);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);

        options.TiltAmountMicrons = 12.4;
        options.TiltAngleDegrees = 214.0;
        options.BackfocusErrorMicrons = -5.0;

        Assert.Multiple(() => {
            Assert.That(vm.TiltAmountMicrons, Is.EqualTo(12.4));
            Assert.That(vm.TiltAngleDegrees, Is.EqualTo(214.0));
            Assert.That(vm.BackfocusErrorMicrons, Is.EqualTo(-5.0));
            Assert.That(vm.TiltDisplay, Is.EqualTo("12.4 µm @ 214°"));
            Assert.That(vm.BackfocusDisplay, Is.EqualTo("−5.0 µm"));
        });
    }

    [Test]
    public void ZeroAberrations_FlattensThePlaneInOneClick() {
        var options = Configured(screwCount: 3);
        options.TiltAmountMicrons = 40.0;
        options.BackfocusErrorMicrons = -5.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        vm.ZeroAberrationsCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(options.TiltAmountMicrons, Is.EqualTo(0.0));
            Assert.That(options.TiltAngleDegrees, Is.EqualTo(0.0));
            Assert.That(options.BackfocusErrorMicrons, Is.EqualTo(0.0));
            Assert.That(vm.IsFlat, Is.True);
        });
    }

    [Test]
    public void ExtremeMove_IsClampedToThePersistedBoundsAndSaysSo() {
        var options = Configured(screwCount: 4);
        options.BackfocusErrorMicrons = 0.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null) { MovementMode = SimTiltMovementMode.Backfocus, AmountPerClick = 100000 };
        vm.TurnCommand.Execute(new ScrewTurn(0, +1));

        Assert.Multiple(() => {
            Assert.That(Math.Abs(options.BackfocusErrorMicrons), Is.LessThanOrEqualTo(10000.0));
            Assert.That(vm.LastActionText, Does.Contain("clamped"));
        });
    }

    // ---- Configuration gating -----------------------------------------------------------------------

    [Test]
    public void UnsetHardware_DisablesTheOperateRowsAndBannersWhy() {
        var options = Configured(screwCount: 3);
        options.SimThreadPitchMicrons = -1;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);

        Assert.Multiple(() => {
            Assert.That(vm.IsAdapterConfigured, Is.False);
            Assert.That(vm.ConfigurationBannerText, Does.Contain("thread pitch"));
            Assert.That(vm.IsConfigurationExpanded, Is.True, "the offending boxes must be reachable without a click");
            Assert.That(vm.TurnCommand.CanExecute(new ScrewTurn(0, +1)), Is.False);
        });

        options.SimThreadPitchMicrons = PitchMicrons;

        Assert.Multiple(() => {
            Assert.That(vm.IsAdapterConfigured, Is.True);
            Assert.That(vm.TurnCommand.CanExecute(new ScrewTurn(0, +1)), Is.True);
        });
    }

    [Test]
    public void NonPositiveAmount_CannotBeApplied() {
        var vm = new SimulatedTiltAdapterVM(Configured(screwCount: 3), realAdapterOptions: null) { AmountPerClick = 0.0 };
        Assert.That(vm.TurnCommand.CanExecute(new ScrewTurn(0, +1)), Is.False,
            "direction comes only from the button — a zero/negative amount is a config error, not a direction");
    }

    [Test]
    public void FourScrewAngles_DeriveTheOppositePairAt180Degrees() {
        var options = Configured(screwCount: 4);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);

        options.SimScrew1AngleDegrees = 10.0;
        options.SimScrew2AngleDegrees = 100.0;

        Assert.Multiple(() => {
            Assert.That(options.SimScrew3AngleDegrees, Is.EqualTo(190.0).Within(1e-9));
            Assert.That(options.SimScrew4AngleDegrees, Is.EqualTo(280.0).Within(1e-9));
            Assert.That(vm.Screw3AngleDisplay, Is.EqualTo("190.0°"));
        });
    }

    [Test]
    public void SwitchingToThreeScrews_RetiresTheFourthAngle() {
        var options = Configured(screwCount: 4);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.False, "guard");

        options.SimScrewCount = 3;

        Assert.Multiple(() => {
            Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True, "mirrors the existing 3-screw convention");
            Assert.That(vm.Rows, Has.Count.EqualTo(3));
            Assert.That(vm.IsAdapterConfigured, Is.True, "a stale screw 4 must never be read on a 3-screw rig");
        });
    }

    [Test]
    public void AutoFillEvenly_WritesAValidGeometryInOneClick([Values(3, 4)] int screwCount) {
        var options = Configured(screwCount);
        options.SimScrew1AngleDegrees = double.NaN;
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        Assert.That(vm.IsAdapterConfigured, Is.False, "guard: a NaN angle must gate the panel");

        vm.AutoFillAnglesCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.IsAdapterConfigured, Is.True);
            Assert.That(options.SimScrew1AngleDegrees, Is.EqualTo(screwCount == 3 ? 0.0 : 45.0));
            Assert.That(options.SimScrew2AngleDegrees, Is.EqualTo(screwCount == 3 ? 120.0 : 135.0));
        });
    }

    [Test]
    public void AberrationsDisabled_WarnsThatThePlaneIsInertButKeepsOperateLive() {
        var options = Configured(screwCount: 3);
        options.EnableAberrations = false;
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);

        Assert.Multiple(() => {
            Assert.That(vm.ShowAberrationsDisabledBanner, Is.True);
            Assert.That(vm.TurnCommand.CanExecute(new ScrewTurn(0, +1)), Is.True, "state edits stay legal, just inert");
        });

        vm.EnableAberrationsCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(options.EnableAberrations, Is.True);
            Assert.That(vm.ShowAberrationsDisabledBanner, Is.False);
        });
    }

    // ---- Coherence with the real adapter ------------------------------------------------------------

    private static void MatchRealAdapterTo(TiltAdapterOptions real, CameraSimulatorOptions sim) {
        real.ScrewCount = sim.SimScrewCount;
        real.Screw1AngleDegrees = sim.SimScrew1AngleDegrees;
        real.Screw2AngleDegrees = sim.SimScrew2AngleDegrees;
        real.Screw3AngleDegrees = sim.SimScrew3AngleDegrees;
        real.Screw4AngleDegrees = sim.SimScrew4AngleDegrees;
        real.ScrewInwardCurvatureSign = sim.SimScrewInwardCurvatureSign;
        real.AdjustmentType = sim.SimAdjustmentType;
        real.ThreadPitchMicrons = sim.SimThreadPitchMicrons;
        real.StepperStepSizeMicrons = sim.SimStepperStepSizeMicrons;
        real.ScrewRadiusMillimeters = sim.SimScrewRadiusMillimeters;
    }

    [Test]
    public void AdapterMatchesReal_IsTrueOnlyWhenEveryComparedFieldAgrees() {
        var options = Configured(screwCount: 4);
        var real = BuildRealAdapterOptions();
        MatchRealAdapterTo(real, options);

        var vm = new SimulatedTiltAdapterVM(options, real);
        Assert.Multiple(() => {
            Assert.That(vm.AdapterMatchesReal, Is.True);
            Assert.That(vm.AdapterCoherenceText, Does.Contain("matches adapter"));
        });

        // Deliberate mismatch is a supported test scenario — it must be flagged, never prevented or silent.
        real.ScrewInwardCurvatureSign = -options.SimScrewInwardCurvatureSign;
        Assert.Multiple(() => {
            Assert.That(vm.AdapterMatchesReal, Is.False);
            Assert.That(vm.AdapterCoherenceText, Does.Contain("differs"));
            Assert.That(vm.AdapterCoherenceTooltip, Does.Contain("direction"), "the tooltip must name the differing field");
        });
    }

    [Test]
    public void AdapterCoherence_ToleratesSmallAngleAndPitchDrift() {
        var options = Configured(screwCount: 3);
        var real = BuildRealAdapterOptions();
        MatchRealAdapterTo(real, options);
        var vm = new SimulatedTiltAdapterVM(options, real);

        real.Screw1AngleDegrees = 358.5;                    // 1.5° away across the 0° wrap
        real.ThreadPitchMicrons = PitchMicrons * 1.04;       // 4% away
        Assert.That(vm.AdapterMatchesReal, Is.True, "angles ±2°, pitch ±5%");

        real.Screw1AngleDegrees = 355.0;                     // 5° away
        Assert.Multiple(() => {
            Assert.That(vm.AdapterMatchesReal, Is.False);
            Assert.That(vm.AdapterCoherenceTooltip, Does.Contain("Screw 1 angle"));
        });
    }

    [Test]
    public void CopyFromAdapter_PullsTheRealCalibrationIntoTheSimFields() {
        var options = Configured(screwCount: 3);
        var real = BuildRealAdapterOptions();
        real.ScrewCount = 4;
        real.Screw1AngleDegrees = 10;
        real.Screw2AngleDegrees = 100;
        real.Screw3AngleDegrees = 190;
        real.Screw4AngleDegrees = 280;
        real.ScrewInwardCurvatureSign = -1;
        real.AdjustmentType = TiltAdjustmentType.Screws;
        real.ThreadPitchMicrons = 212;
        real.ScrewRadiusMillimeters = 44;

        var vm = new SimulatedTiltAdapterVM(options, real);
        vm.CopyFromAdapterCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(options.SimScrewCount, Is.EqualTo(4));
            Assert.That(options.SimScrew1AngleDegrees, Is.EqualTo(10));
            Assert.That(options.SimScrew4AngleDegrees, Is.EqualTo(280));
            Assert.That(options.SimScrewInwardCurvatureSign, Is.EqualTo(-1));
            Assert.That(options.SimThreadPitchMicrons, Is.EqualTo(212));
            Assert.That(options.SimScrewRadiusMillimeters, Is.EqualTo(44));
            Assert.That(vm.AdapterMatchesReal, Is.True, "copying from the adapter must satisfy the badge");
        });
    }

    [Test]
    public void CopyToAdapter_RequiresAConfirmationThatNamesWhatItOverwrites() {
        var options = Configured(screwCount: 4, curvatureSign: -1);
        var real = BuildRealAdapterOptions();
        real.ScrewCount = 3;
        real.Screw1AngleDegrees = 0;
        real.IsCalibrated = true;

        var vm = new SimulatedTiltAdapterVM(options, real);
        vm.CopyToAdapterCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.IsCopyToAdapterPending, Is.True, "it overwrites real calibration — never on one click");
            Assert.That(real.ScrewCount, Is.EqualTo(3), "nothing may be written before confirmation");
            Assert.That(vm.CopyToAdapterConfirmText, Does.Contain("screw angles"));
            Assert.That(vm.CopyToAdapterConfirmText, Does.Contain("thread pitch"));
        });

        vm.CancelCopyToAdapterCommand.Execute(null);
        Assert.Multiple(() => {
            Assert.That(vm.IsCopyToAdapterPending, Is.False);
            Assert.That(real.ScrewCount, Is.EqualTo(3), "cancel must be a true no-op");
        });

        vm.CopyToAdapterCommand.Execute(null);
        vm.ConfirmCopyToAdapterCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.IsCopyToAdapterPending, Is.False);
            Assert.That(real.ScrewCount, Is.EqualTo(4));
            Assert.That(real.Screw1AngleDegrees, Is.EqualTo(45));
            Assert.That(real.Screw4AngleDegrees, Is.EqualTo(315));
            Assert.That(real.ScrewInwardCurvatureSign, Is.EqualTo(-1));
            Assert.That(real.ThreadPitchMicrons, Is.EqualTo(PitchMicrons));
            Assert.That(real.CalibrationIsManual, Is.True, "the result is a manual calibration, like the wizard's manual entry");
            Assert.That(real.IsCalibrated, Is.True);
            Assert.That(real.CalibratedScrewCount, Is.EqualTo(4));
            Assert.That(real.ScrewInwardCurvatureSignIsMeasured, Is.False);
            Assert.That(real.DeviceName, Is.EqualTo(TiltAdapterDevicePreset.ManualName),
                "the hardware fields we just wrote must stay editable, not be locked by a stale preset");
            Assert.That(vm.AdapterMatchesReal, Is.True);
        });
    }

    [Test]
    public void CoherenceBadge_IsHonestWhenThereIsNoRealAdapterToCompare() {
        var vm = new SimulatedTiltAdapterVM(Configured(screwCount: 3), realAdapterOptions: null);
        Assert.Multiple(() => {
            Assert.That(vm.AdapterMatchesReal, Is.False);
            Assert.That(vm.CanCopyAdapterSettings, Is.False);
            Assert.That(vm.AdapterCoherenceText, Does.Contain("unavailable"));
        });
    }

    // ---- Diagram ------------------------------------------------------------------------------------

    [Test]
    public void ScrewDiagram_RendersLiveFromTheEnteredAngles() {
        var options = Configured(screwCount: 3);
        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);

        Assert.That(vm.ScrewDiagramItems, Has.Count.EqualTo(3));
        Assert.That(vm.ScrewConnectionLines, Has.Count.EqualTo(3));

        options.SimScrewCount = 4;
        Assert.Multiple(() => {
            Assert.That(vm.ScrewDiagramItems, Has.Count.EqualTo(4));
            Assert.That(vm.ScrewDiagramItems[0].Number, Is.EqualTo(1));
            Assert.That(vm.ScrewConnectionLines, Has.Count.EqualTo(4));
        });
    }

    // ---- Profile switches ---------------------------------------------------------------------------

    [Test]
    public void ProfileSwitch_RebuildsFromTheNewProfilesRig() {
        // A profile switch is the one change that arrives with NO property name: CameraSimulatorOptions re-reads
        // every field and then broadcasts RaiseAllPropertiesChanged(), which BaseINPC sends as
        // PropertyChangedEventArgs(null). A panel that only switches on named properties matches nothing here and
        // silently keeps rendering the OLD profile's adapter — three rows for a rig that now has four.
        var profileService = Substitute.For<IProfileService>();
        var accessor = new InMemoryPluginOptionsAccessor();
        var options = new CameraSimulatorOptions(profileService, accessor);
        options.SimScrewCount = 3;
        options.SimScrew1AngleDegrees = 0.0;
        options.SimScrew2AngleDegrees = 120.0;

        var vm = new SimulatedTiltAdapterVM(options, realAdapterOptions: null);
        Assert.That(vm.Rows, Has.Count.EqualTo(3), "guard: the panel starts on the 3-screw profile");

        // What the new profile has stored — all four angles, as a real 4-screw profile persists them (the derived
        // 3/4 are written when the rig is configured, so a profile load never has to re-derive them). Written
        // straight to the accessor, behind the options object's setters, so the only notification the VM sees is
        // the nameless profile-change broadcast.
        accessor.SetValueInt32(nameof(ICameraSimulatorOptions.SimScrewCount), 4);
        accessor.SetValueDouble(nameof(ICameraSimulatorOptions.SimScrew1AngleDegrees), 45.0);
        accessor.SetValueDouble(nameof(ICameraSimulatorOptions.SimScrew2AngleDegrees), 135.0);
        accessor.SetValueDouble(nameof(ICameraSimulatorOptions.SimScrew3AngleDegrees), 225.0);
        accessor.SetValueDouble(nameof(ICameraSimulatorOptions.SimScrew4AngleDegrees), 315.0);
        profileService.ProfileChanged += Raise.Event<EventHandler>(profileService, EventArgs.Empty);

        Assert.Multiple(() => {
            Assert.That(vm.IsFourScrew, Is.True);
            Assert.That(vm.Rows, Has.Count.EqualTo(4));
            Assert.That(vm.ScrewDiagramItems, Has.Count.EqualTo(4));
        });
    }
}
