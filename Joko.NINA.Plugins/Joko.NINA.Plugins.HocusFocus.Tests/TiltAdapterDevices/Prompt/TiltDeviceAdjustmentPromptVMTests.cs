#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.Prompt;

[TestFixture]
public class TiltDeviceAdjustmentPromptVMTests {

    private static TiltAdapterMove Move(TiltMoveAxis axis, int steps, TiltMoveGroup group) =>
        new TiltAdapterMove(axis, steps, group, $"Test move {axis} {steps}");

    private static TiltAdapterMovePlan MakePlan(
        IReadOnlyList<TiltAdapterMove> moves = null,
        double[] residualMicrons = null,
        double twistResidualSteps = 0.0,
        double estimatedSeconds = 0.0) =>
        new TiltAdapterMovePlan(
            moves ?? Array.Empty<TiltAdapterMove>(),
            residualMicrons ?? new double[4],
            twistResidualSteps,
            estimatedSeconds);

    /// <summary>Replanner test double: records every (includeTilt, includeBackfocus) call and delegates to Produce.</summary>
    private sealed class RecordingReplanner {
        public readonly List<(bool IncludeTilt, bool IncludeBackfocus)> Calls = new();

        public Func<bool, bool, TiltDevicePlanPreview> Produce { get; set; } =
            (_, _) => new TiltDevicePlanPreview(MakePlan(), hardLimitViolated: false, limitWarning: null);

        public TiltDevicePlanPreview Replan(bool includeTilt, bool includeBackfocus) {
            Calls.Add((includeTilt, includeBackfocus));
            return Produce(includeTilt, includeBackfocus);
        }
    }

    private static TiltDeviceAdjustmentPromptVM BuildVM(
        RecordingReplanner replanner,
        bool signMeasured = true,
        string pitchMismatchWarning = null,
        bool positionsUnknown = false) =>
        new TiltDeviceAdjustmentPromptVM(replanner.Replan, signMeasured, pitchMismatchWarning, positionsUnknown);

    [Test]
    public void Ctor_NullReplanner_Throws() {
        Assert.Throws<ArgumentNullException>(() => new TiltDeviceAdjustmentPromptVM(null, true, null, false));
    }

    [Test]
    public void Ctor_ComputesInitialPreview_WithBothGroupsOn() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(
                    moves: new[] {
                        Move(TiltMoveAxis.DiagonalA, 150, TiltMoveGroup.Tilt),
                        Move(TiltMoveAxis.Backfocus, -20, TiltMoveGroup.Backfocus),
                    },
                    residualMicrons: new[] { 0.9, -0.9, 0.0, 1.26 },
                    estimatedSeconds: 20),
                hardLimitViolated: false,
                limitWarning: null),
        };
        var vm = BuildVM(replanner);

        Assert.Multiple(() => {
            Assert.That(replanner.Calls, Is.EqualTo(new[] { (true, true) }));
            Assert.That(vm.ApplyTilt, Is.True);
            Assert.That(vm.ApplyBackfocus, Is.True);
            Assert.That(vm.Moves, Has.Count.EqualTo(2));
            Assert.That(vm.Moves[0].WireCommand, Is.EqualTo("tr,150"));
            Assert.That(vm.Moves[0].GroupLabel, Is.EqualTo("Tilt"));
            Assert.That(vm.Moves[1].WireCommand, Is.EqualTo("bf,-20"));
            Assert.That(vm.Moves[1].GroupLabel, Is.EqualTo("Backfocus"));
            Assert.That(vm.HasMoves, Is.True);
            Assert.That(vm.HasNoMoves, Is.False);
            Assert.That(vm.MoveCountText, Is.EqualTo("2 moves"));
            Assert.That(vm.ProceedButtonText, Is.EqualTo("Send 2 moves"));
            Assert.That(vm.EstimatedDurationText, Is.EqualTo("~20 s"));
            Assert.That(vm.CornerResiduals.Select(r => r.Label), Is.EqualTo(new[] { "Screw 1 (TR)", "Screw 2 (TL)", "Screw 3 (BL)", "Screw 4 (BR)" }));
            Assert.That(vm.CornerResiduals.Select(r => r.ValueText), Is.EqualTo(new[] { "+0.9 µm", "-0.9 µm", "0.0 µm", "+1.3 µm" }));
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public void ToggleTiltOff_ReplansWithNewFlags_AndRefreshesMovesResidualsDuration() {
        var replanner = new RecordingReplanner {
            Produce = (includeTilt, _) => includeTilt
                ? new TiltDevicePlanPreview(
                    MakePlan(
                        moves: new[] {
                            Move(TiltMoveAxis.DiagonalB, -25, TiltMoveGroup.Tilt),
                            Move(TiltMoveAxis.Backfocus, 12, TiltMoveGroup.Backfocus),
                        },
                        residualMicrons: new[] { 0.5, 0.5, 0.5, 0.5 },
                        estimatedSeconds: 20),
                    false, null)
                : new TiltDevicePlanPreview(
                    MakePlan(
                        moves: new[] { Move(TiltMoveAxis.Backfocus, 12, TiltMoveGroup.Backfocus) },
                        residualMicrons: new[] { 2.0, -2.0, 2.0, -2.0 },
                        estimatedSeconds: 10),
                    false, null),
        };
        var vm = BuildVM(replanner);
        var raised = new List<string>();
        vm.PropertyChanged += (object s, PropertyChangedEventArgs e) => raised.Add(e.PropertyName);

        vm.ApplyTilt = false;

        Assert.Multiple(() => {
            Assert.That(replanner.Calls, Is.EqualTo(new[] { (true, true), (false, true) }));
            Assert.That(vm.Moves, Has.Count.EqualTo(1));
            Assert.That(vm.Moves[0].WireCommand, Is.EqualTo("bf,12"));
            Assert.That(vm.MoveCountText, Is.EqualTo("1 move"));
            Assert.That(vm.EstimatedDurationText, Is.EqualTo("~10 s"));
            Assert.That(vm.CornerResiduals.Select(r => r.ValueText), Is.EqualTo(new[] { "+2.0 µm", "-2.0 µm", "+2.0 µm", "-2.0 µm" }));
            Assert.That(raised, Does.Contain(nameof(vm.ApplyTilt)));
            Assert.That(raised, Does.Contain(nameof(vm.Moves)));
            Assert.That(raised, Does.Contain(nameof(vm.CornerResiduals)));
            Assert.That(raised, Does.Contain(nameof(vm.EstimatedDurationText)));
        });
    }

    [Test]
    public void ToggleToSameValue_DoesNotReplan() {
        var replanner = new RecordingReplanner();
        var vm = BuildVM(replanner);

        vm.ApplyTilt = true;
        vm.ApplyBackfocus = true;

        Assert.That(replanner.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void BothGroupsOff_ProceedDisabled_ReenabledWhenAGroupComesBackOn() {
        var replanner = new RecordingReplanner();
        var vm = BuildVM(replanner);

        vm.ApplyTilt = false;
        vm.ApplyBackfocus = false;
        Assert.That(vm.ProceedCommand.CanExecute(null), Is.False);

        vm.ApplyBackfocus = true;
        Assert.That(vm.ProceedCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void HardLimitViolated_DisablesProceed_AndSurfacesWarning() {
        const string warning = "Motor 2 would exceed the configured max excursion (300 steps).";
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalA, 500, TiltMoveGroup.Tilt) }),
                hardLimitViolated: true,
                limitWarning: warning),
        };
        var vm = BuildVM(replanner);

        Assert.Multiple(() => {
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.False);
            Assert.That(vm.HardLimitViolated, Is.True);
            Assert.That(vm.HardLimitWarningVisible, Is.True);
            Assert.That(vm.SoftLimitWarningVisible, Is.False);
            Assert.That(vm.LimitWarningText, Is.EqualTo(warning));
        });
    }

    [Test]
    public void HardLimitViolated_WithEmptyWarningText_StillShowsFallbackExplanation() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(MakePlan(), hardLimitViolated: true, limitWarning: null),
        };
        var vm = BuildVM(replanner);

        Assert.Multiple(() => {
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.False);
            Assert.That(vm.HardLimitWarningVisible, Is.True);
            Assert.That(vm.LimitWarningText, Is.Not.Empty);
        });
    }

    [Test]
    public void SoftLimitWarning_ShownWithoutDisablingProceed() {
        const string warning = "Positions are near the configured excursion limit.";
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalA, 10, TiltMoveGroup.Tilt) }),
                hardLimitViolated: false,
                limitWarning: warning),
        };
        var vm = BuildVM(replanner);

        Assert.Multiple(() => {
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.True);
            Assert.That(vm.HardLimitWarningVisible, Is.False);
            Assert.That(vm.SoftLimitWarningVisible, Is.True);
            Assert.That(vm.LimitWarningText, Is.EqualTo(warning));
        });
    }

    [Test]
    public void HardLimitCleared_ByTogglingAGroupOff_ReenablesProceed() {
        var replanner = new RecordingReplanner {
            Produce = (includeTilt, _) => includeTilt
                ? new TiltDevicePlanPreview(
                    MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalA, 500, TiltMoveGroup.Tilt) }),
                    hardLimitViolated: true,
                    limitWarning: "Too far.")
                : new TiltDevicePlanPreview(MakePlan(), hardLimitViolated: false, limitWarning: null),
        };
        var vm = BuildVM(replanner);
        Assert.That(vm.ProceedCommand.CanExecute(null), Is.False);

        vm.ApplyTilt = false;

        Assert.Multiple(() => {
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.True);
            Assert.That(vm.HardLimitWarningVisible, Is.False);
        });
    }

    [Test]
    public void Proceed_ResolvesChoice_WithCurrentTogglesAndDisplayedPlan() {
        var tiltOnlyPlan = MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalA, 30, TiltMoveGroup.Tilt) });
        var bothPlan = MakePlan(moves: new[] {
            Move(TiltMoveAxis.DiagonalA, 30, TiltMoveGroup.Tilt),
            Move(TiltMoveAxis.Backfocus, 5, TiltMoveGroup.Backfocus),
        });
        var replanner = new RecordingReplanner {
            Produce = (_, includeBackfocus) => new TiltDevicePlanPreview(
                includeBackfocus ? bothPlan : tiltOnlyPlan, false, null),
        };
        var vm = BuildVM(replanner);
        var closeRaised = 0;
        vm.RequestClose += (s, e) => closeRaised++;

        vm.ApplyBackfocus = false;
        vm.ProceedCommand.Execute(null);

        Assert.That(vm.Choice.IsCompleted, Is.True);
        var choice = vm.Choice.Result;
        Assert.Multiple(() => {
            Assert.That(choice.Proceed, Is.True);
            Assert.That(choice.ApplyTilt, Is.True);
            Assert.That(choice.ApplyBackfocus, Is.False);
            Assert.That(choice.FinalPlan, Is.SameAs(tiltOnlyPlan));
            Assert.That(closeRaised, Is.EqualTo(1));
        });
    }

    [Test]
    public void Cancel_ResolvesProceedFalse_AndRaisesRequestClose() {
        var vm = BuildVM(new RecordingReplanner());
        var closeRaised = 0;
        vm.RequestClose += (s, e) => closeRaised++;

        vm.CancelCommand.Execute(null);

        Assert.That(vm.Choice.IsCompleted, Is.True);
        var choice = vm.Choice.Result;
        Assert.Multiple(() => {
            Assert.That(choice.Proceed, Is.False);
            Assert.That(choice.FinalPlan, Is.Null);
            Assert.That(closeRaised, Is.EqualTo(1));
        });
    }

    [Test]
    public void Dispose_WithoutExplicitPick_ResolvesCancel() {
        var vm = BuildVM(new RecordingReplanner());

        vm.Dispose();

        Assert.That(vm.Choice.IsCompleted, Is.True);
        Assert.That(vm.Choice.Result.Proceed, Is.False);
    }

    [Test]
    public void Dispose_AfterProceed_KeepsProceedResult() {
        var vm = BuildVM(new RecordingReplanner());

        vm.ProceedCommand.Execute(null);
        vm.Dispose();

        Assert.That(vm.Choice.Result.Proceed, Is.True);
    }

    [TestCase(0.0, false)]
    [TestCase(0.4, false)]
    [TestCase(-0.99, false)]
    [TestCase(1.0, true)]
    [TestCase(-1.3, true)]
    public void TwistWarning_VisibleIffAtLeastOneStepOfTwist(double twistSteps, bool expectedVisible) {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(twistResidualSteps: twistSteps), false, null),
        };
        var vm = BuildVM(replanner);

        Assert.That(vm.TwistWarningVisible, Is.EqualTo(expectedVisible));
        if (expectedVisible) {
            Assert.That(vm.TwistWarningText, Does.Contain("twist"));
        }
    }

    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, false)]
    public void AssumedDirectionWarning_VisibleIffUnmeasuredAndBackfocusMovePresent(
        bool signMeasured, bool planHasBackfocusMove, bool expectedVisible) {
        var moves = planHasBackfocusMove
            ? new[] { Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus) }
            : new[] { Move(TiltMoveAxis.DiagonalA, 8, TiltMoveGroup.Tilt) };
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(MakePlan(moves: moves), false, null),
        };
        var vm = BuildVM(replanner, signMeasured: signMeasured);

        Assert.That(vm.AssumedDirectionWarningVisible, Is.EqualTo(expectedVisible));
    }

    [Test]
    public void AssumedDirectionWarning_ClearsWhenBackfocusToggledOff() {
        var replanner = new RecordingReplanner {
            Produce = (_, includeBackfocus) => new TiltDevicePlanPreview(
                MakePlan(moves: includeBackfocus
                    ? new[] { Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus) }
                    : Array.Empty<TiltAdapterMove>()),
                false, null),
        };
        var vm = BuildVM(replanner, signMeasured: false);
        Assert.That(vm.AssumedDirectionWarningVisible, Is.True);

        vm.ApplyBackfocus = false;

        Assert.That(vm.AssumedDirectionWarningVisible, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PositionsUnknownWarning_TracksCtorFlag(bool positionsUnknown) {
        var vm = BuildVM(new RecordingReplanner(), positionsUnknown: positionsUnknown);

        Assert.That(vm.PositionsUnknownWarningVisible, Is.EqualTo(positionsUnknown));
    }

    [Test]
    public void PitchMismatchWarning_ShownIffTextProvided() {
        const string warning = "Saved step size (1.8 µm/step) differs from the wizard's last measured value (2.4 µm/step).";
        var withWarning = BuildVM(new RecordingReplanner(), pitchMismatchWarning: warning);
        var withoutWarning = BuildVM(new RecordingReplanner(), pitchMismatchWarning: null);

        Assert.Multiple(() => {
            Assert.That(withWarning.PitchMismatchWarningVisible, Is.True);
            Assert.That(withWarning.PitchMismatchWarning, Is.EqualTo(warning));
            Assert.That(withoutWarning.PitchMismatchWarningVisible, Is.False);
        });
    }

    [Test]
    public void EmptyPlan_ShowsNoMovesState() {
        var vm = BuildVM(new RecordingReplanner());

        Assert.Multiple(() => {
            Assert.That(vm.HasMoves, Is.False);
            Assert.That(vm.HasNoMoves, Is.True);
            Assert.That(vm.MoveCountText, Is.EqualTo("No moves"));
            Assert.That(vm.ProceedButtonText, Is.EqualTo("Proceed"));
            Assert.That(vm.EstimatedDurationText, Is.EqualTo("0 s"));
        });
    }

    [TestCase(0.0, "0 s")]
    [TestCase(-5.0, "0 s")]
    [TestCase(9.2, "~10 s")]
    [TestCase(30.0, "~30 s")]
    [TestCase(59.0, "~59 s")]
    [TestCase(59.5, "~1 min")]
    [TestCase(60.0, "~1 min")]
    [TestCase(95.0, "~1 min 35 s")]
    [TestCase(120.0, "~2 min")]
    public void FormatDuration_IsHumanFriendly_AndRoundsUp(double seconds, string expected) {
        Assert.That(TiltDeviceAdjustmentPromptVM.FormatDuration(seconds), Is.EqualTo(expected));
    }

    [TestCase(0.94, "+0.9 µm")]
    [TestCase(-1.26, "-1.3 µm")]
    [TestCase(0.0, "0.0 µm")]
    public void FormatResidualMicrons_IsSignedOneDecimal(double microns, string expected) {
        Assert.That(TiltDeviceAdjustmentPromptVM.FormatResidualMicrons(microns), Is.EqualTo(expected));
    }

    [TestCase(TiltMoveAxis.DiagonalA, "Corner")]
    [TestCase(TiltMoveAxis.DiagonalB, "Corner")]
    [TestCase(TiltMoveAxis.EdgeVertical, "Side")]
    [TestCase(TiltMoveAxis.EdgeHorizontal, "Side")]
    [TestCase(TiltMoveAxis.Backfocus, "Backfocus")]
    public void BuildMoveKindLabel_MapsAxisToKind(TiltMoveAxis axis, string expected) {
        Assert.That(TiltDeviceAdjustmentPromptVM.BuildMoveKindLabel(axis), Is.EqualTo(expected));
    }

    [Test]
    public void BuildSemanticText_CornerMove_ListsBothDiagonalScrewsWithSigns() {
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 142, TiltMoveGroup.Tilt, "x");
        Assert.That(TiltDeviceAdjustmentPromptVM.BuildSemanticText(move),
            Is.EqualTo("Corner move — Screw 1 +142, Screw 3 -142 steps"));
    }

    [Test]
    public void BuildSemanticText_CornerMove_NegativeSteps_FlipsWhichScrewGoesPositive() {
        // DiagonalA,-142 => PerCornerSteps (-142,0,+142,0): screw 3 positive, screw 1 negative.
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, -142, TiltMoveGroup.Tilt, "x");
        Assert.That(TiltDeviceAdjustmentPromptVM.BuildSemanticText(move),
            Is.EqualTo("Corner move — Screw 3 +142, Screw 1 -142 steps"));
    }

    [Test]
    public void BuildSemanticText_SideMove_GroupsTheTwoScrewsMovingTheSameDirection() {
        // EdgeVertical,+20 => (+1,+1,-1,-1): screws 1 & 2 together, 3 & 4 together the other way.
        var move = new TiltAdapterMove(TiltMoveAxis.EdgeVertical, 20, TiltMoveGroup.Tilt, "x");
        Assert.That(TiltDeviceAdjustmentPromptVM.BuildSemanticText(move),
            Is.EqualTo("Side move — Screws 1 & 2 +20, Screws 3 & 4 -20 steps"));
    }

    [Test]
    public void BuildSemanticText_EdgeHorizontal_GroupsScrews1And4() {
        // EdgeHorizontal,+20 => (+1,-1,-1,+1): screws 1 & 4 together, 2 & 3 the other way.
        var move = new TiltAdapterMove(TiltMoveAxis.EdgeHorizontal, 20, TiltMoveGroup.Tilt, "x");
        Assert.That(TiltDeviceAdjustmentPromptVM.BuildSemanticText(move),
            Is.EqualTo("Side move — Screws 1 & 4 +20, Screws 2 & 3 -20 steps"));
    }

    [Test]
    public void BuildSemanticText_Backfocus_SaysAllFourTogether() {
        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 150, TiltMoveGroup.Backfocus, "x");
        Assert.That(TiltDeviceAdjustmentPromptVM.BuildSemanticText(move),
            Is.EqualTo("All four screws +150 steps together — changes backfocus (sensor spacing), not tilt."));
    }

    [Test]
    public void ProceedDisabledReason_ExplainsBothGroupsOff_ButNotWhenEnabledOrHardLimited() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalA, 10, TiltMoveGroup.Tilt) }), false, null),
        };
        var vm = BuildVM(replanner);

        // Enabled: no reason shown.
        Assert.That(vm.ProceedDisabledReason, Is.Empty);
        Assert.That(vm.ProceedDisabledReasonVisible, Is.False);

        // Both groups off: explain why Proceed is disabled.
        vm.ApplyTilt = false;
        vm.ApplyBackfocus = false;
        Assert.Multiple(() => {
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.False);
            Assert.That(vm.ProceedDisabledReason, Is.EqualTo("Select at least one correction to apply."));
            Assert.That(vm.ProceedDisabledReasonVisible, Is.True);
        });
    }

    [Test]
    public void ProceedDisabledReason_EmptyWhenHardLimited_RedPanelExplainsInstead() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalA, 500, TiltMoveGroup.Tilt) }),
                hardLimitViolated: true, limitWarning: "Too far."),
        };
        var vm = BuildVM(replanner);

        Assert.Multiple(() => {
            Assert.That(vm.ProceedCommand.CanExecute(null), Is.False);
            Assert.That(vm.ProceedDisabledReason, Is.Empty, "the red hard-limit panel already explains this");
            Assert.That(vm.ProceedDisabledReasonVisible, Is.False);
        });
    }

    [Test]
    public void MoveRow_AssumedDirection_FlagsOnlyBackfocusRow_WhenSignUnmeasured() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(moves: new[] {
                    Move(TiltMoveAxis.DiagonalA, 10, TiltMoveGroup.Tilt),
                    Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus),
                }), false, null),
        };

        var unmeasured = BuildVM(replanner, signMeasured: false);
        Assert.Multiple(() => {
            Assert.That(unmeasured.Moves[0].AssumedDirection, Is.False, "tilt rows are never direction-assumed");
            Assert.That(unmeasured.Moves[1].AssumedDirection, Is.True, "the backfocus row carries the assumed-direction tag");
        });

        var measured = BuildVM(replanner, signMeasured: true);
        Assert.That(measured.Moves[1].AssumedDirection, Is.False, "measured sign ⇒ no assumed-direction tag");
    }

    [Test]
    public void CornerResiduals_AppendPhysicalCornerToEachScrew() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(residualMicrons: new[] { 0.0, 0.0, 0.0, 0.0 }), false, null),
        };
        var vm = BuildVM(replanner);
        Assert.That(vm.CornerResiduals.Select(r => r.Label),
            Is.EqualTo(new[] { "Screw 1 (TR)", "Screw 2 (TL)", "Screw 3 (BL)", "Screw 4 (BR)" }));
    }

    [Test]
    public void Moves_ExposeSemanticTextAndKind_ForBinding() {
        var replanner = new RecordingReplanner {
            Produce = (_, _) => new TiltDevicePlanPreview(
                MakePlan(moves: new[] { Move(TiltMoveAxis.DiagonalB, 30, TiltMoveGroup.Tilt) }), false, null),
        };
        var vm = BuildVM(replanner);

        Assert.Multiple(() => {
            Assert.That(vm.Moves[0].MoveKindLabel, Is.EqualTo("Corner"));
            Assert.That(vm.Moves[0].SemanticText, Is.EqualTo("Corner move — Screw 2 +30, Screw 4 -30 steps"));
            // The exact wire command is still available (demoted to an auditability chip in the UI).
            Assert.That(vm.Moves[0].WireCommand, Is.EqualTo("tl,30"));
        });
    }
}
