#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Core.Model;
using NINA.Core.Utility.SerialCommunication;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

// T14 (Inspector "Automatic Adjustment" end-to-end): SAFETY-CRITICAL feature -- these tests are the
// backstop for the design doc's "[CRITICAL GATE]" (device-linked calibration) and user decision #9
// (one adjustment per measurement, no auto-looping). Two layers:
//   1. Pure `internal static` helper tests (canExecute gate, per-screw targets, plan preview, worsening
//      check) -- directly unit-testable, no VM needed.
//   2. A VM-level harness (mirrors InspectorVMBehavioralTests) driving AutomaticAdjustmentCommand through
//      a REAL TiltDeviceConnectionService (built via its internal test ctor, exactly like
//      TiltDeviceConnectionServiceTests) wired to an NSubstitute ITiltMotionController, with the
//      dialog-showing and confirm-prompt steps replaced by injectable fakes (TiltDeviceAdjustmentPrompt.ShowAsync
//      opens a real WPF window and cannot run headless; AnalyzeAutoFocus needs a working AutoFocus engine and
//      is exercised elsewhere) -- see InspectorVM's showAdjustmentPromptAsync/confirmPromptAsync/reRunAnalysisAsync.
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class InspectorVMAutomaticAdjustmentTests {

    // ======================================================================================================
    // Pure static helpers
    // ======================================================================================================

    #region IsCalibrationDeviceLinked

    [Test]
    public void IsCalibrationDeviceLinked_NullOptions_ReturnsFalse() {
        Assert.That(InspectorVM.IsCalibrationDeviceLinked(null), Is.False);
    }

    [TestCase("", "ASG Electronic EAT - 90mm", false, TestName = "EmptyLinkedMarker")]
    [TestCase(null, "ASG Electronic EAT - 90mm", false, TestName = "NullLinkedMarker")]
    [TestCase("ASG Electronic EAT - 90mm", "ASG Electronic EAT - ZWO 461", false, TestName = "MismatchedPreset")]
    [TestCase("ASG Electronic EAT - 90mm", "ASG Electronic EAT - 90mm", true, TestName = "MatchingNonEmpty")]
    public void IsCalibrationDeviceLinked_VariousCombinations(string linked, string deviceName, bool expected) {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.DeviceLinkedCalibrationDeviceName.Returns(linked);
        options.DeviceName.Returns(deviceName);
        Assert.That(InspectorVM.IsCalibrationDeviceLinked(options), Is.EqualTo(expected));
    }

    #endregion

    #region CanExecuteAutomaticAdjustment

    [Test]
    public void CanExecuteAutomaticAdjustment_AllGatesSatisfied_ReturnsTrue() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.True);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_NotConnected_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: false, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_NoController_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: false, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_NotDeviceLinked_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: false, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.False);
    }

    // [CRITICAL GATE] The second half of the automation gate: a device-linked calibration that failed its
    // own confidence/quality check must still be blocked -- device-linked is necessary but not sufficient.
    [Test]
    public void CanExecuteAutomaticAdjustment_CalibrationNotReliable_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: false,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_NoNumericGuidance_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: false, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_OperationActive_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: true, currentGeneration: 1, lastExecutedGeneration: 0),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_GenerationAlreadyExecuted_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 1),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_NoMeasurementYet_BothGenerationsZero_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 0, lastExecutedGeneration: 0),
            Is.False);
    }

    // The generation counter only advances when an analysis COMPLETES, so mid-sweep it still reports the
    // previous measurement as fresh -- without this term the adapter could be moved out from under a run.
    [Test]
    public void CanExecuteAutomaticAdjustment_AnalysisRunning_ReturnsFalse() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0,
            analysisRunning: true),
            Is.False);
    }

    [Test]
    public void CanExecuteAutomaticAdjustment_AnalysisNotRunning_KeepsTheOtherGatesDeciding() {
        Assert.That(InspectorVM.CanExecuteAutomaticAdjustment(
            serviceConnected: true, controllerAvailable: true, deviceLinked: true, calibrationIsReliable: true,
            hasNumericGuidance: true, isOperationActive: false, currentGeneration: 1, lastExecutedGeneration: 0,
            analysisRunning: false),
            Is.True);
    }

    #endregion

    #region BuildPerScrewTargets

    private static ITiltAdapterOptions NewFourScrewOptions() {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.ScrewCount.Returns(4);
        options.AdjustmentType.Returns(TiltAdjustmentType.StepperMotors);
        options.StepperStepSizeMicrons.Returns(1.8);
        options.ScrewRadiusMillimeters.Returns(55.0);
        options.Screw1AngleDegrees.Returns(0.0);
        options.Screw2AngleDegrees.Returns(90.0);
        options.Screw3AngleDegrees.Returns(180.0);
        options.Screw4AngleDegrees.Returns(270.0);
        options.ScrewInwardCurvatureSign.Returns(1);
        return options;
    }

    // The whole point of this helper is that automation reads targets straight off the fitted model --
    // NEVER off the formatted display strings FillNumericGuidance builds for the guidance table -- so this
    // asserts equality against TiltScrewTargets.ComputePerScrewTargets directly (the same call InspectorVM
    // makes), not against any formatted text.
    [Test]
    public void BuildPerScrewTargets_MatchesTiltScrewTargetsDirectly_NotDisplayStrings() {
        var options = NewFourScrewOptions();
        var model = new SensorParaboloidModel(x0: 3.0, y0: -2.0, z0: 0, gx: 0.0015, gy: -0.0007, k: 4e-6);

        var (sPerScrew, unitMicrons) = InspectorVM.BuildPerScrewTargets(model, options);

        var angles = new[] { 0.0, 90.0, 180.0, 270.0 };
        var expected = TiltScrewTargets.ComputePerScrewTargets(
            model.Gx, model.Gy, model.Kx, model.Ky, model.X0, model.Y0,
            angles, 55.0 * 1000.0, 1.8, 1);

        Assert.Multiple(() => {
            Assert.That(unitMicrons, Is.EqualTo(1.8));
            Assert.That(sPerScrew, Has.Length.EqualTo(4));
            for (int i = 0; i < 4; i++) {
                Assert.That(sPerScrew[i], Is.EqualTo(expected[i].TotalSteps).Within(1e-9), $"screw {i + 1}");
            }
        });
    }

    [Test]
    public void BuildPerScrewTargets_ThreeScrewAdapter_Throws() {
        var options = NewFourScrewOptions();
        options.ScrewCount.Returns(3);
        var model = new SensorParaboloidModel(0, 0, 0, 0.001, 0, 0);
        Assert.Throws<InvalidOperationException>(() => InspectorVM.BuildPerScrewTargets(model, options));
    }

    [Test]
    public void BuildPerScrewTargets_UnconfiguredUnitSize_Throws() {
        var options = NewFourScrewOptions();
        options.StepperStepSizeMicrons.Returns(-1.0);
        var model = new SensorParaboloidModel(0, 0, 0, 0.001, 0, 0);
        Assert.Throws<InvalidOperationException>(() => InspectorVM.BuildPerScrewTargets(model, options));
    }

    [Test]
    public void BuildPerScrewTargets_UnconfiguredRadius_Throws() {
        var options = NewFourScrewOptions();
        options.ScrewRadiusMillimeters.Returns(-1.0);
        var model = new SensorParaboloidModel(0, 0, 0, 0.001, 0, 0);
        Assert.Throws<InvalidOperationException>(() => InspectorVM.BuildPerScrewTargets(model, options));
    }

    [Test]
    public void BuildPerScrewTargets_NaNScrewAngle_Throws() {
        var options = NewFourScrewOptions();
        options.Screw4AngleDegrees.Returns(double.NaN);
        var model = new SensorParaboloidModel(0, 0, 0, 0.001, 0, 0);
        Assert.Throws<InvalidOperationException>(() => InspectorVM.BuildPerScrewTargets(model, options));
    }

    [Test]
    public void BuildPerScrewTargets_NullModel_Throws() {
        Assert.Throws<ArgumentNullException>(() => InspectorVM.BuildPerScrewTargets(null, NewFourScrewOptions()));
    }

    [Test]
    public void BuildPerScrewTargets_NullOptions_Throws() {
        var model = new SensorParaboloidModel(0, 0, 0, 0.001, 0, 0);
        Assert.Throws<ArgumentNullException>(() => InspectorVM.BuildPerScrewTargets(model, null));
    }

    // T14 fix D: options.ScrewInwardCurvatureSign is never persisted as 0 in practice, but if it ever were,
    // the planner (this method) and the displayed guidance table (FillNumericGuidance) must resolve it
    // IDENTICALLY -- otherwise the plan a user approves in the dialog could silently differ from the numbers
    // they saw in the guidance table.
    [Test]
    public void BuildPerScrewTargets_ZeroSign_ResolvesToDefaultSign_LikeFillNumericGuidance() {
        var options = NewFourScrewOptions();
        options.ScrewInwardCurvatureSign.Returns(0);
        var model = new SensorParaboloidModel(x0: 3.0, y0: -2.0, z0: 0, gx: 0.0015, gy: -0.0007, k: 4e-6);

        var (sPerScrew, unitMicrons) = InspectorVM.BuildPerScrewTargets(model, options);

        var angles = new[] { 0.0, 90.0, 180.0, 270.0 };
        var expected = TiltScrewTargets.ComputePerScrewTargets(
            model.Gx, model.Gy, model.Kx, model.Ky, model.X0, model.Y0,
            angles, 55.0 * 1000.0, 1.8, TiltScrewGeometry.DefaultScrewInwardCurvatureSign);

        Assert.Multiple(() => {
            Assert.That(unitMicrons, Is.EqualTo(1.8));
            Assert.That(sPerScrew, Has.Length.EqualTo(4));
            for (int i = 0; i < 4; i++) {
                Assert.That(sPerScrew[i], Is.EqualTo(expected[i].TotalSteps).Within(1e-9), $"screw {i + 1}");
            }
        });
    }

    #endregion

    #region BuildPlanPreview

    // s -> a=(s1-s3)/2=10, b=(s2-s4)/2=-3 -> two diagonal moves (ra != +-rb): DiagonalA(10), DiagonalB(-3),
    // in that order out of TiltMovePlanner.Plan. Used to prove BuildPlanPreview shows the CONTROLLER's
    // ordering, not the planner's original order.
    private static readonly double[] TwoMoveTargets = { 10.0, -3.0, -10.0, 3.0 };

    [Test]
    public void BuildPlanPreview_NoLimitViolation_UsesControllerOrderedMoves() {
        var controller = Substitute.For<ITiltMotionController>();
        controller.OrderForMinimalPeakExcursion(Arg.Any<IReadOnlyList<TiltAdapterMove>>())
            .Returns(ci => ((IReadOnlyList<TiltAdapterMove>)ci.Arg<IReadOnlyList<TiltAdapterMove>>()).Reverse().ToList());

        var preview = InspectorVM.BuildPlanPreview(TwoMoveTargets, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8, maxStepsPerCommand: 500, controller);

        Assert.Multiple(() => {
            Assert.That(preview.HardLimitViolated, Is.False);
            Assert.That(preview.Plan.Moves, Has.Count.EqualTo(2));
            Assert.That(preview.Plan.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.DiagonalB), "must reflect the controller's (reversed) order, not the planner's original order");
            Assert.That(preview.Plan.Moves[1].Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
        });
    }

    [Test]
    public void BuildPlanPreview_LimitException_ReturnsUnorderedMovesWithHardLimitFlag() {
        var controller = Substitute.For<ITiltMotionController>();
        controller.OrderForMinimalPeakExcursion(Arg.Any<IReadOnlyList<TiltAdapterMove>>())
            .Returns(ci => throw new TiltDeviceLimitException("excursion exceeded"));

        var preview = InspectorVM.BuildPlanPreview(TwoMoveTargets, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8, maxStepsPerCommand: 500, controller);

        Assert.Multiple(() => {
            Assert.That(preview.HardLimitViolated, Is.True);
            Assert.That(preview.LimitWarning, Does.Contain("excursion exceeded"));
            Assert.That(preview.Plan.Moves, Has.Count.EqualTo(2), "the unordered plan must still be shown so the dialog can display it");
        });
    }

    [Test]
    public void BuildPlanPreview_ResidualsTwistAndDurationCarryOverUnaffectedByOrdering() {
        var controller = Substitute.For<ITiltMotionController>();
        controller.OrderForMinimalPeakExcursion(Arg.Any<IReadOnlyList<TiltAdapterMove>>())
            .Returns(ci => ((IReadOnlyList<TiltAdapterMove>)ci.Arg<IReadOnlyList<TiltAdapterMove>>()).Reverse().ToList());

        var unordered = TiltMovePlanner.Plan(TwoMoveTargets, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8, maxStepsPerCommand: 500);
        var preview = InspectorVM.BuildPlanPreview(TwoMoveTargets, includeTilt: true, includeBackfocus: true, unitMicrons: 1.8, maxStepsPerCommand: 500, controller);

        Assert.Multiple(() => {
            Assert.That(preview.Plan.ResidualMicronsPerCorner, Is.EqualTo(unordered.ResidualMicronsPerCorner));
            Assert.That(preview.Plan.TwistResidualSteps, Is.EqualTo(unordered.TwistResidualSteps));
            Assert.That(preview.Plan.EstimatedSeconds, Is.EqualTo(unordered.EstimatedSeconds));
        });
    }

    #endregion

    #region TiltMagnitude / TiltWorsened

    [Test]
    public void TiltMagnitude_ComputesSqrtGxSquaredPlusGySquared() {
        var model = new SensorParaboloidModel(0, 0, 0, gx: 3.0, gy: 4.0, k: 0);
        Assert.That(InspectorVM.TiltMagnitude(model), Is.EqualTo(5.0).Within(1e-9));
    }

    [Test]
    public void TiltMagnitude_NullModel_ReturnsNaN() {
        Assert.That(InspectorVM.TiltMagnitude(null), Is.NaN);
    }

    [TestCase(0.10, 0.10, false, TestName = "TiltWorsened_Unchanged")]
    [TestCase(0.10, 0.08, false, TestName = "TiltWorsened_Improved")]
    [TestCase(0.10, 0.105, false, TestName = "TiltWorsened_TinyUptickWithinMargin")]
    [TestCase(0.10, 0.16, true, TestName = "TiltWorsened_MeaningfullyWorse")]
    public void TiltWorsened_VariousDeltas(double before, double after, bool expected) {
        Assert.That(InspectorVM.TiltWorsened(before, after), Is.EqualTo(expected));
    }

    [Test]
    public void TiltWorsened_NearZeroBefore_RequiresAbsoluteFloorNotJustRelativeMargin() {
        // A purely-relative test would flag ANY increase off a near-zero baseline as "worse" by a huge
        // relative factor. The absolute floor prevents that until the increase itself is non-negligible.
        Assert.Multiple(() => {
            Assert.That(InspectorVM.TiltWorsened(1e-8, 5e-8), Is.False);
            Assert.That(InspectorVM.TiltWorsened(1e-8, 1e-3), Is.True);
        });
    }

    [Test]
    public void TiltWorsened_NaNInputs_ReturnsFalse() {
        Assert.Multiple(() => {
            Assert.That(InspectorVM.TiltWorsened(double.NaN, 1.0), Is.False);
            Assert.That(InspectorVM.TiltWorsened(1.0, double.NaN), Is.False);
        });
    }

    #endregion

    #region FirstMoveFailureMeansDeviceUntouched / DescribeFirstMoveFailureNotification

    // T14 fix: per EatTiltMotionController's exception taxonomy (see its ExecuteMoveAsync XML doc), only
    // TiltDeviceLimitException is thrown BEFORE any device I/O -- everything else on the first move means a
    // command may already have been sent and the device's position is ambiguous. These pure-helper tests are
    // the only way to verify the notification text directly: NINA.Core.Utility.Notification.Notification is
    // a no-op in a headless test process (Application.Current == null -> its internal manager stays null), so
    // nothing in the VM-level harness below can observe the actual message text.

    [Test]
    public void FirstMoveFailureMeansDeviceUntouched_LimitException_ReturnsTrue() {
        Assert.That(InspectorVM.FirstMoveFailureMeansDeviceUntouched(new TiltDeviceLimitException("exceeds cap")), Is.True);
    }

    [Test]
    public void FirstMoveFailureMeansDeviceUntouched_CommandFailedException_ReturnsFalse() {
        Assert.That(InspectorVM.FirstMoveFailureMeansDeviceUntouched(new TiltDeviceCommandFailedException("ack timeout")), Is.False);
    }

    [Test]
    public void FirstMoveFailureMeansDeviceUntouched_SerialPortClosedException_ReturnsFalse() {
        Assert.That(InspectorVM.FirstMoveFailureMeansDeviceUntouched(new SerialPortClosedException("unplugged")), Is.False);
    }

    [Test]
    public void FirstMoveFailureMeansDeviceUntouched_UnexpectedException_ReturnsFalse() {
        Assert.That(InspectorVM.FirstMoveFailureMeansDeviceUntouched(new InvalidOperationException("boom")), Is.False);
    }

    [Test]
    public void DescribeFirstMoveFailureNotification_LimitException_SaysNothingWasSent() {
        var message = InspectorVM.DescribeFirstMoveFailureNotification(new TiltDeviceLimitException("exceeds cap"), "a device travel limit was violated (exceeds cap)");
        Assert.Multiple(() => {
            Assert.That(message, Does.Contain("Nothing was sent to the device."));
            Assert.That(message, Does.Not.Contain("UNCERTAIN"));
        });
    }

    [TestCaseSource(nameof(AmbiguousFirstMoveExceptions))]
    public void DescribeFirstMoveFailureNotification_AmbiguousException_DoesNotClaimNothingWasSent(Exception failure) {
        var message = InspectorVM.DescribeFirstMoveFailureNotification(failure, "ack timeout");
        Assert.Multiple(() => {
            Assert.That(message, Does.Not.Contain("Nothing was sent"), "must not mislabel an ambiguous device state as safe");
            Assert.That(message, Does.Contain("UNCERTAIN"), "must surface the ambiguous-state message instead");
            Assert.That(message, Does.Contain("vendor app").Or.Contain("disconnect and reconnect"));
        });
    }

    private static IEnumerable<Exception> AmbiguousFirstMoveExceptions() {
        yield return new TiltDeviceCommandFailedException("ack timeout");
        yield return new SerialPortClosedException("unplugged");
    }

    #endregion

    // ======================================================================================================
    // VM-level harness
    // ======================================================================================================

    private sealed class FakeTimeSource : ITiltDeviceTimeSource {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public event EventHandler Tick;
        public void Dispose() {
        }
    }

    private static TiltAdapterMove Move(TiltMoveAxis axis, int steps, TiltMoveGroup group, string description) =>
        new TiltAdapterMove(axis, steps, group, description);

    private sealed class AdjustmentFixture {
        public const string PresetName = "ASG Electronic EAT - 90mm";

        public MediatorBundle Bundle { get; } = new MediatorBundle();
        public ITiltMotionController Controller { get; }
        public TiltDeviceConnectionService Service { get; }

        // Every move ExecuteMoveAsync was called with, in call order (forward moves AND revert moves alike).
        public List<TiltAdapterMove> ExecutedMoves { get; } = new();

        public List<(string Message, string Title)> ConfirmPrompts { get; } = new();
        public Queue<bool> ConfirmAnswers { get; } = new();

        public TiltDeviceAdjustmentChoice NextChoice { get; set; } = TiltDeviceAdjustmentChoice.Cancelled;
        public int ShowPromptCallCount { get; private set; }

        public bool ReRunResult { get; set; } = true;
        public int ReRunCallCount { get; private set; }
        public Action OnReRun { get; set; }

        // Per-move failure injection: return non-null to make that ExecuteMoveAsync call fail. Matched by
        // reference for forward moves, by (axis, steps) for the freshly-constructed inverse moves reverts send.
        public Func<TiltAdapterMove, Exception> FailureForMove { get; set; }

        // Every ApplicationStatus.Status the VM reported. ProgressFactory wraps the mediator in a Progress<T>,
        // which -- with no SynchronizationContext installed (see the note on this fixture's synchronous style)
        // -- delivers through the thread pool: arrival is both asynchronous AND unordered, so assertions poll
        // and must not depend on which report landed last. What matters is that the clearing (empty) status is
        // reported at all; NINA's real dispatcher context delivers in order.
        private readonly object statusGate = new object();

        private readonly List<string> reportedStatuses = new();

        /// <summary>True once the flow has reported the empty status that clears NINA's status bar.</summary>
        public bool ReportedAClearingStatus {
            get {
                lock (statusGate) {
                    return reportedStatuses.Exists(string.IsNullOrEmpty);
                }
            }
        }

        public AdjustmentFixture() {
            Bundle.ApplicationStatusMediator
                .When(m => m.StatusUpdate(Arg.Any<ApplicationStatus>()))
                .Do(ci => {
                    lock (statusGate) {
                        reportedStatuses.Add(ci.Arg<ApplicationStatus>()?.Status ?? string.Empty);
                    }
                });

            Controller = Substitute.For<ITiltMotionController>();
            Controller.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TiltDevicePositions.Unknown));
            Controller.ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
                .Returns(ci => {
                    var move = ci.Arg<TiltAdapterMove>();
                    ExecutedMoves.Add(move);
                    var ex = FailureForMove?.Invoke(move);
                    return ex != null ? Task.FromException(ex) : Task.CompletedTask;
                });

            var time = new FakeTimeSource();
            Service = new TiltDeviceConnectionService(Bundle.ProfileService, Bundle.TiltAdapterOptions, (name, opts) => Controller, time);

            Bundle.TiltAdapterOptions.ScrewCount.Returns(4);
            Bundle.TiltAdapterOptions.CalibratedScrewCount.Returns(4);
            Bundle.TiltAdapterOptions.IsCalibrated.Returns(true);
            Bundle.TiltAdapterOptions.DeviceName.Returns(PresetName);
            Bundle.TiltAdapterOptions.DeviceLinkedCalibrationDeviceName.Returns(PresetName);
            // The reliability gate defaults to reliable so every existing scenario in this fixture (which is
            // about the OTHER gates) keeps exercising a calibration that would otherwise be automation-trusted;
            // ReliabilityGateTests below overrides this to false to exercise the gate itself.
            Bundle.TiltAdapterOptions.CalibrationIsReliable.Returns(true);
            Bundle.TiltAdapterOptions.AdjustmentType.Returns(TiltAdjustmentType.StepperMotors);
            Bundle.TiltAdapterOptions.StepperStepSizeMicrons.Returns(1.8);
            Bundle.TiltAdapterOptions.ScrewRadiusMillimeters.Returns(55.0);
            Bundle.TiltAdapterOptions.Screw1AngleDegrees.Returns(0.0);
            Bundle.TiltAdapterOptions.Screw2AngleDegrees.Returns(90.0);
            Bundle.TiltAdapterOptions.Screw3AngleDegrees.Returns(180.0);
            Bundle.TiltAdapterOptions.Screw4AngleDegrees.Returns(270.0);
            Bundle.TiltAdapterOptions.TiltDeviceMaxStepsPerCommand.Returns(500);
            Bundle.TiltAdapterOptions.ScrewInwardCurvatureSign.Returns(1);
        }

        public Task ConnectAsync() => Service.ConnectAsync(PresetName, "COM5", CancellationToken.None);

        private Task<bool> ConfirmPromptAsync(string message, string title) {
            ConfirmPrompts.Add((message, title));
            return Task.FromResult(ConfirmAnswers.Count > 0 && ConfirmAnswers.Dequeue());
        }

        /// <summary>
        /// The preview the real approval dialog would have rendered, captured by invoking the replanner the same
        /// way it does. Lets a test observe WHICH sensor model the plan was computed from without reaching into
        /// the VM's private plan-building.
        /// </summary>
        public TiltDevicePlanPreview CapturedPreview { get; private set; }

        private Task<TiltDeviceAdjustmentChoice> ShowPromptAsync(
            Func<bool, bool, TiltDevicePlanPreview> replanner, bool screwInwardCurvatureSignIsMeasured, string pitchMismatchWarning, bool positionsUnknown, double unitMicrons) {
            ShowPromptCallCount++;
            CapturedPreview = replanner?.Invoke(true, true);
            return Task.FromResult(NextChoice);
        }

        // Set by BuildVM below -- ReRunAnalysisAsync needs it to simulate a completed analysis's effect on
        // measurementGeneration (see the T14 fix B comment there), but the delegate is captured by
        // MediatorBundle.BuildInspectorVM before the VM instance exists.
        private InspectorVM vm;

        private Task<bool> ReRunAnalysisAsync(CancellationToken ct) {
            ReRunCallCount++;
            OnReRun?.Invoke();
            if (ReRunResult) {
                // Fix B (T14): production's reRunAnalysisAsync ultimately calls AnalyzeAutoFocusResult, which
                // increments measurementGeneration once per COMPLETED analysis (see its doc comment). This fake
                // must mirror that -- otherwise the worsening-triggered-revert bug this fixture exists to catch
                // (lastExecutedMeasurementGeneration left pointing at the STALE, pre-revert generation while the
                // re-run has already moved measurementGeneration forward) is invisible to every test here.
                vm.MeasurementGenerationForTest++;
            }
            return Task.FromResult(ReRunResult);
        }

        public InspectorVM BuildVM() {
            vm = Bundle.BuildInspectorVM(Service, ConfirmPromptAsync, ShowPromptAsync, ReRunAnalysisAsync);
            return vm;
        }

        // Seeds a valid, minimal sensor model and forces a RebuildTiltGuidance pass (mirrors
        // InspectorVMBehavioralTests.TiltGuidance_SigmaFlip_FlipsMotionArrowsNotTiltGlyphs's pattern: set the
        // model directly on SensorModel, then raise tiltAdapterOptions.PropertyChanged to trigger the rebuild
        // InspectorVM only does in response to that event) so HasNumericGuidance becomes true.
        public void SeedValidModel(InspectorVM vm, double gx = 0.001, double gy = 0.0, double k = 0.0) {
            var imageSize = new System.Drawing.Size(1000, 1000);
            var paraboloid = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: gx, gy: gy, k: k);
            vm.SensorModel.SelectedTiltHistoryModel = new SensorParaboloidTiltHistoryModel(
                historyId: 1, imageSize: imageSize, pixelSizeMicrons: 4.0, fRatio: 5.0,
                focuserSizeMicrons: 1.0, finalFocusPosition: 0.0, tiltEffectMicrons: 0.0,
                curvatureEffectMicrons: 0.0, autoFocusOffset: 0.0, tiltPlaneModel: null,
                sensorModel: paraboloid);
            // The line above drives the DISPLAY (guidance rebuilds from it). Automatic Adjustment plans from the
            // latest MEASURED model instead, which only a completed analysis writes -- so stand in for that here,
            // or every flow test would plan from a null model.
            vm.SensorModel.LatestSensorModelForTest = paraboloid;
            Bundle.TiltAdapterOptions.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                Bundle.TiltAdapterOptions, new PropertyChangedEventArgs(nameof(ITiltAdapterOptions.IsCalibrated)));
        }
    }

    // --- canExecute gates (live VM) -----------------------------------------------------------------------

    [Test]
    public void Disconnected_CannotExecute() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;

        Assert.Multiple(() => {
            Assert.That(vm.IsTiltDeviceConnected, Is.False);
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void ConnectedButNotDeviceLinked_CannotExecute_RemediationShown() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.Bundle.TiltAdapterOptions.DeviceLinkedCalibrationDeviceName.Returns(string.Empty);
        fx.ConnectAsync().GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(vm.IsTiltDeviceConnected, Is.True, "precondition");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
            Assert.That(vm.AutomaticAdjustmentRemediationVisible, Is.True);
            Assert.That(vm.AutomaticAdjustmentRemediationText, Does.Contain("Re-run calibration"));
        });
    }

    [Test]
    public void ConnectedDeviceLinkedToADifferentPreset_CannotExecute() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.Bundle.TiltAdapterOptions.DeviceLinkedCalibrationDeviceName.Returns("ASG Electronic EAT - ZWO 461");
        fx.ConnectAsync().GetAwaiter().GetResult();
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
    }

    // [CRITICAL GATE] T14 canExecute must be FALSE when CalibrationIsReliable=false even though the
    // calibration IS device-linked and connected -- device-linked is necessary but not sufficient; a
    // noise-dominated calibration must never drive unattended automation.
    [Test]
    public void ConnectedAndDeviceLinked_ButNotReliable_CannotExecute_RemediationShown() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.Bundle.TiltAdapterOptions.CalibrationIsReliable.Returns(false);
        fx.ConnectAsync().GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(vm.IsTiltDeviceConnected, Is.True, "precondition");
            Assert.That(InspectorVM.IsCalibrationDeviceLinked(fx.Bundle.TiltAdapterOptions), Is.True, "precondition: still device-linked");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
            Assert.That(vm.AutomaticAdjustmentRemediationVisible, Is.True);
            Assert.That(vm.AutomaticAdjustmentRemediationText, Does.Contain("low-confidence"));
        });
    }

    [Test]
    public void WizardOperationActive_CannotExecute_ReEnablesOnRelease() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True, "precondition");

        var token = fx.Service.TryBeginOperation("wizard calibration");
        Assert.That(token, Is.Not.Null);
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);

        token.Dispose();
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void NoNumericGuidance_CannotExecute() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        // Deliberately no SeedValidModel call -- HasNumericGuidance stays false.
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(vm.TiltGuidance.HasNumericGuidance, Is.False);
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void NoFreshMeasurementYet_CannotExecute() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        fx.ConnectAsync().GetAwaiter().GetResult();
        // MeasurementGenerationForTest left at its default (0) -- no analysis has completed yet.

        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void AllGatesSatisfied_CanExecute() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True);
    }

    // --- Dialog cancel / empty plan: measurement not consumed ---------------------------------------------

    [Test]
    public void DialogCancel_NoMovesExecuted_MeasurementNotConsumed() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        fx.NextChoice = TiltDeviceAdjustmentChoice.Cancelled;

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Is.Empty);
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(0));
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True, "cancel must not consume the measurement");
        });
    }

    [Test]
    public void ApprovedEmptyPlan_IsNoOp_MeasurementNotConsumed() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var emptyPlan = new TiltAdapterMovePlan(Array.Empty<TiltAdapterMove>(), new double[4], twistResidualSteps: 0, estimatedSeconds: 0);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, emptyPlan);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Is.Empty);
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(0));
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True, "an approved no-op must not consume the measurement");
        });
    }

    // --- Approved plan: executed in order, measurement consumed, stale lockout --------------------------------

    [Test]
    public void ApprovedPlan_MovesExecutedInOrder_MeasurementConsumed() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.EdgeVertical, 12, TiltMoveGroup.Tilt, "B");
        var moveC = Move(TiltMoveAxis.Backfocus, -8, TiltMoveGroup.Backfocus, "C");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB, moveC }, new double[4], twistResidualSteps: 0, estimatedSeconds: 30);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(false); // decline the "re-run to confirm" prompt

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(3));
            Assert.That(fx.ExecutedMoves[0], Is.SameAs(moveA));
            Assert.That(fx.ExecutedMoves[1], Is.SameAs(moveB));
            Assert.That(fx.ExecutedMoves[2], Is.SameAs(moveC));
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(1), "consumed once execution started");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False, "the same measurement cannot drive a second plan");
            Assert.That(fx.ReRunCallCount, Is.EqualTo(0), "declining the re-run prompt must not invoke it");
        });
    }

    [Test]
    public void StaleGeneration_DirectReExecution_IsIgnored_FreshMeasurementReEnables() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(false); // decline re-run

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult(); // first execution consumes generation 1
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False, "precondition: measurement consumed");
        fx.ExecutedMoves.Clear();
        int promptCallsBefore = fx.ShowPromptCallCount;

        // Directly invoke again (a second click, or any programmatic re-invocation): the defensive re-check
        // inside RunAutomaticAdjustmentAsync itself -- not just the disabled button -- must refuse. A second
        // execution off the same measurement must be impossible.
        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Is.Empty, "no moves may be sent off a stale measurement");
            Assert.That(fx.ShowPromptCallCount, Is.EqualTo(promptCallsBefore), "must not even show the approval dialog again");
        });

        // A fresh measurement (a new completed analysis) re-enables it.
        vm.MeasurementGenerationForTest = 2;
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True);
    }

    // --- Re-run on accept ---------------------------------------------------------------------------------

    [Test]
    public void SuccessfulExecution_ReRunAccepted_InvokesReRun_NoWorseningPromptWhenUnchanged() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(true); // accept "re-run to confirm"
        fx.ReRunResult = true; // the model is not touched by the fake re-run -> unchanged tilt -> not worsened

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ReRunCallCount, Is.EqualTo(1));
            Assert.That(fx.ConfirmPrompts, Has.Count.EqualTo(1), "no worsening-revert prompt should follow an unchanged/improved result");
        });
    }

    [Test]
    public void SuccessfulExecution_ReRunDeclined_DoesNotInvokeReRun() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(false);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.That(fx.ReRunCallCount, Is.EqualTo(0));
    }

    // --- Adapter state recorded with each run --------------------------------------------------------------
    //
    // Reads the SERVICE's positions rather than the controller's, because that is the number the panel shows and
    // the one kept correct during an adjustment lease (when the poll is suspended and only the post-move
    // publishes advance it).

    [Test]
    public void CaptureAdapterState_NoDeviceConnected_RecordsAnUnknownSnapshotThatStillHasATimestamp() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();

        var snapshot = vm.CaptureAdapterStateForTest();

        Assert.Multiple(() => {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.HasPositions, Is.False);
            Assert.That(snapshot.PositionsKnown, Is.False);
            Assert.That(snapshot.CapturedUtc, Is.Not.EqualTo(default(DateTime)));
        });
    }

    [Test]
    public void CaptureAdapterState_PositionsUnknown_MarksNotKnown() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.ConnectAsync().GetAwaiter().GetResult();

        var snapshot = vm.CaptureAdapterStateForTest();

        Assert.Multiple(() => {
            Assert.That(snapshot.PositionsKnown, Is.False, "the fixture's controller reports Unknown positions");
            Assert.That(snapshot.DevicePresetName, Is.EqualTo(AdjustmentFixture.PresetName),
                "the preset is recorded even without positions, so a later snapshot cannot be misattributed");
        });
    }

    [Test]
    public void AdapterStateSnapshot_CopiesThePositionsItWasGiven() {
        // The service hands out its live list, which the next poll replaces.
        var live = new List<int> { 1, 2, 3, 4 };
        var snapshot = new TiltAdapterStateSnapshot(DateTime.UtcNow, live, positionsKnown: true, devicePresetName: "p");

        live[0] = 999;

        Assert.That(snapshot.PerMotorSteps[0], Is.EqualTo(1));
    }

    [Test]
    public void SensorParaboloidTiltHistoryModel_DefaultsToNoAdapterState() {
        var run = new SensorParaboloidTiltHistoryModel(
            historyId: 1, imageSize: new System.Drawing.Size(10, 10), pixelSizeMicrons: 1, fRatio: 5,
            focuserSizeMicrons: 1, finalFocusPosition: 0, tiltEffectMicrons: 0, curvatureEffectMicrons: 0,
            autoFocusOffset: 0, tiltPlaneModel: null, sensorModel: null);

        Assert.Multiple(() => {
            Assert.That(run.AdapterState, Is.Null, "an optional trailing parameter keeps every existing caller working");
            Assert.That(run.PositionsRecordedDisplay, Is.EqualTo("—"));
            Assert.That(run.AdjustmentAppliedDisplay, Is.Empty);
        });
    }

    // --- Plans come from the newest MEASUREMENT, never from the history selection ---------------------------

    // Selecting a row in the history grid rewrites SensorModel.DisplayedSensorModel with that past run's fit,
    // but leaves the measurement-generation gate reporting "fresh". Planning from the displayed model therefore
    // let a user complete a run, click an old row, and drive the device from a stale measurement. Both fixtures
    // below share one latest measurement; only the history selection differs, so an identical plan is the proof
    // the selection has no influence.
    [Test]
    public void AutomaticAdjustment_WithAnOldHistoryRowSelected_PlansFromTheNewestRun() {
        var baseline = new AdjustmentFixture();
        var baselineVm = baseline.BuildVM();
        baseline.SeedValidModel(baselineVm, gx: 0.001, gy: 0.0);
        baselineVm.MeasurementGenerationForTest = 1;
        baseline.ConnectAsync().GetAwaiter().GetResult();
        baseline.NextChoice = TiltDeviceAdjustmentChoice.Cancelled;
        baselineVm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        // The user clicks a far-more-tilted OLD run to look at it. Display follows; the measurement does not.
        vm.SensorModel.SelectedTiltHistoryModel = new SensorParaboloidTiltHistoryModel(
            historyId: 99, imageSize: new System.Drawing.Size(1000, 1000), pixelSizeMicrons: 4.0, fRatio: 5.0,
            focuserSizeMicrons: 1.0, finalFocusPosition: 0.0, tiltEffectMicrons: 0.0,
            curvatureEffectMicrons: 0.0, autoFocusOffset: 0.0, tiltPlaneModel: null,
            sensorModel: new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0.05, gy: 0.0, k: 0.0));
        fx.NextChoice = TiltDeviceAdjustmentChoice.Cancelled;
        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(vm.SensorModel.DisplayedSensorModel.Gx, Is.EqualTo(0.05).Within(1e-12),
                "precondition: the DISPLAY must follow the history selection");
            Assert.That(vm.SensorModel.LatestSensorModel.Gx, Is.EqualTo(0.001).Within(1e-12),
                "precondition: the latest MEASUREMENT must be untouched by the selection");
            Assert.That(baseline.CapturedPreview, Is.Not.Null);
            Assert.That(fx.CapturedPreview, Is.Not.Null);
            Assert.That(
                fx.CapturedPreview.Plan.Moves.Sum(m => Math.Abs(m.Steps)),
                Is.EqualTo(baseline.CapturedPreview.Plan.Moves.Sum(m => Math.Abs(m.Steps))),
                "the plan must be computed from the newest measurement, not from the selected history row");
        });
    }

    // --- Worsening check ------------------------------------------------------------------------------------

    [Test]
    public void ReRunConfirmed_TiltWorsened_WarnsAndRevertsOnConfirm() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0); // before magnitude = 0.001
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(true); // accept the re-run
        // The fake re-run "measures" a substantially worse tilt (10x the magnitude) before completing.
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        // The adjustment itself now ENDS with the banner raised. No modal, and nothing reverted yet.
        Assert.Multiple(() => {
            Assert.That(vm.TiltWorseningBannerVisible, Is.True, "the banner must be raised");
            Assert.That(vm.PendingRevertMoveCountForTest, Is.EqualTo(1), "the journal must survive for the banner's button");
            Assert.That(fx.ConfirmPrompts.Any(p => p.Title.Contains("Worsened")), Is.False, "the modal is gone");
            Assert.That(fx.ConfirmPrompts, Has.Count.EqualTo(1), "only the re-run prompt remains");
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(1), "only the forward move so far");
        });

        vm.RevertLastAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(2), "the button sends the inverse");
            Assert.That(fx.ExecutedMoves[1].Axis, Is.EqualTo(move.Axis));
            Assert.That(fx.ExecutedMoves[1].Steps, Is.EqualTo(-move.Steps));
            Assert.That(vm.TiltWorseningBannerVisible, Is.False, "and clears itself afterwards");
        });
    }

    // While the offer is outstanding the adjustment button is dead regardless of generation. This is the term
    // that replaces the lock the modal used to provide implicitly by blocking the thread.
    [Test]
    public void TiltWorseningBanner_WhileVisible_AutomaticAdjustmentIsDisabled() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10));
        fx.ConfirmAnswers.Enqueue(true);
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(vm.TiltWorseningBannerVisible, Is.True, "precondition");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
        });
    }

    // Dismiss records a decision; it must not pretend the moves were undone, and it must not consume the
    // measurement -- a user who accepts the worse state may legitimately want to correct FROM it.
    [Test]
    public void TiltWorseningBanner_Dismissed_LeavesMovesInPlaceAndReEnablesAdjustment() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10));
        fx.ConfirmAnswers.Enqueue(true);
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);
        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        vm.DismissWorseningBannerCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.TiltWorseningBannerVisible, Is.False);
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(1), "nothing was reverted");
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(1),
                "the confirming measurement is NOT consumed by a dismiss");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True,
                "correcting forward from the worse state is legitimate");
        });
    }

    [Test]
    public void TiltWorseningBanner_DeviceDisconnected_StaysVisibleWithABlockedReason() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10));
        fx.ConfirmAnswers.Enqueue(true);
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);
        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        fx.Service.DisconnectAsync().GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(vm.TiltWorseningBannerVisible, Is.True, "the warning is still true after a disconnect");
            Assert.That(vm.TiltWorseningRevertBlockedReason, Is.Not.Empty);
            Assert.That(vm.CanShowWorseningRevertButton, Is.False);
        });
    }

    [Test]
    public void TiltWorseningBanner_ClearedByClearAnalyses() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10));
        fx.ConfirmAnswers.Enqueue(true);
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);
        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        vm.ClearAnalysesCommand.Execute(null);

        Assert.That(vm.TiltWorseningBannerVisible, Is.False);
    }

    [Test]
    public void BuildWorseningBannerText_NamesBothMagnitudesAndTheMoveCount() {
        var text = InspectorVM.BuildWorseningBannerText(0.001, 0.01, 6);

        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("0.001"));
            Assert.That(text, Does.Contain("0.01"));
            Assert.That(text, Does.Contain("6 moves"));
        });
    }

    // T14 fix B: the confirming re-run above already moves measurementGeneration forward (a genuinely
    // completed analysis), but that analysis was of the PRE-revert (still-bad) state -- once the revert has
    // ALSO happened, lastExecutedMeasurementGeneration must be bumped to match, or canExecute would
    // immediately re-enable Automatic Adjustment against the stale, now-reverted DisplayedSensorModel that
    // was captured before the revert ever ran.
    [Test]
    public void ReRunConfirmed_TiltWorsened_RevertConfirmed_MeasurementConsumed_ReEnablesOnlyAfterFreshAnalysis() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0); // before magnitude = 0.001
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(true); // accept the re-run
        // The fake re-run "measures" a substantially worse tilt (10x the magnitude) before completing; this
        // also bumps measurementGeneration to 2 (see ReRunAnalysisAsync's fake behavior above).
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.RevertLastAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(2), "precondition: the revert actually ran");
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(2), "consumed against the CURRENT (post-revert) generation, not the stale pre-revert one");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False,
                "must stay disabled against the just-consumed, now-reverted measurement -- re-enabling here would let a second plan over-correct an already-reverted device");
        });

        // A genuinely NEW completed analysis (generation 3) re-enables it, exactly like the non-revert case.
        vm.MeasurementGenerationForTest = 3;
        Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True, "a fresh post-revert analysis must re-enable Automatic Adjustment");
    }

    [Test]
    // Was "declining the revert offer leaves the moves in place". There is no offer to decline any more -- the
    // adjustment ends with a banner and the user acts later, or not at all -- so the honest form of the same
    // guarantee is that simply LEAVING the banner alone reverts nothing.
    public void ReRunConfirmed_TiltWorsened_BannerLeftAlone_RevertsNothing() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(true);  // accept the re-run
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(1), "the applied move stays applied until the user asks otherwise");
            Assert.That(vm.TiltWorseningBannerVisible, Is.True, "and the offer is still standing");
        });
    }

    // --- Mid-plan failure: journal + revert ----------------------------------------------------------------

    [Test]
    public void MidPlanFailure_JournalHoldsAppliedMoves_RevertExecutesInverseInReverseOrder() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.DiagonalB, -12, TiltMoveGroup.Tilt, "B");
        var moveC = Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus, "C");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB, moveC }, new double[4], 0, 30);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.FailureForMove = m => ReferenceEquals(m, moveC) ? new TiltDeviceCommandFailedException("ack timeout") : null;
        fx.ConfirmAnswers.Enqueue(true); // confirm revert

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            // Forward: A, B succeed; C is attempted and throws (journal = [A, B]).
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(5), "3 forward attempts + 2 successful reverts");
            Assert.That(fx.ExecutedMoves[0], Is.SameAs(moveA));
            Assert.That(fx.ExecutedMoves[1], Is.SameAs(moveB));
            Assert.That(fx.ExecutedMoves[2], Is.SameAs(moveC));
            // Revert in REVERSE order of the journal [A, B]: inverse(B) then inverse(A).
            Assert.That(fx.ExecutedMoves[3].Axis, Is.EqualTo(moveB.Axis));
            Assert.That(fx.ExecutedMoves[3].Steps, Is.EqualTo(-moveB.Steps));
            Assert.That(fx.ExecutedMoves[4].Axis, Is.EqualTo(moveA.Axis));
            Assert.That(fx.ExecutedMoves[4].Steps, Is.EqualTo(-moveA.Steps));
            // Consumed even though it failed -- execution started (>= 1 move sent).
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(1));
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.False);
            Assert.That(fx.ReRunCallCount, Is.EqualTo(0), "a failed execution must not offer the re-run confirm");
        });
    }

    [Test]
    public void MidPlanFailure_FirstMoveThrowsLimitException_NothingSent_MeasurementNotConsumed() {
        // A TiltDeviceLimitException on the very FIRST move means nothing was ever sent (the check runs
        // before any device I/O) -- no revert offer, and the measurement is NOT consumed.
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.FailureForMove = m => new TiltDeviceLimitException("would exceed max excursion");

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(1), "the move was attempted (and rejected) but nothing physical happened");
            Assert.That(fx.ConfirmPrompts, Is.Empty, "no revert should ever be offered when nothing was sent");
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(0), "execution never started -- the measurement is not consumed");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True, "the button must stay enabled for another attempt off this same measurement");
        });
    }

    // T14 fix: unlike TiltDeviceLimitException above, a TiltDeviceCommandFailedException or
    // SerialPortClosedException on the FIRST move means a command may have already been sent -- the journal
    // stays empty (nothing was CONFIRMED successful) so the measurement-consumption bookkeeping is unchanged,
    // but see DescribeFirstMoveFailureNotification_AmbiguousException_DoesNotClaimNothingWasSent above for the
    // notification-text half of this fix (unobservable at this VM level since Notification is a no-op headless).
    [TestCaseSource(nameof(AmbiguousFirstMoveExceptions))]
    public void MidPlanFailure_FirstMoveThrowsAmbiguousException_NoRevertOffered_MeasurementNotConsumed(Exception firstMoveFailure) {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.FailureForMove = m => firstMoveFailure;

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(1), "the move was attempted and may have been sent before failing");
            Assert.That(fx.ConfirmPrompts, Is.Empty, "there is nothing in the journal to revert (the ambiguity is handled by the notification text, not an automated revert)");
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(0), "the journal is still empty -- consumption bookkeeping is unaffected by this fix");
            Assert.That(vm.AutomaticAdjustmentCommand.CanExecute(null), Is.True, "the button must stay enabled for another attempt off this same measurement");
        });
    }

    [Test]
    public void MidPlanFailure_RevertItselfFails_StopsAfterFirstFailedRevert() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.DiagonalB, -12, TiltMoveGroup.Tilt, "B");
        var moveC = Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus, "C");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB, moveC }, new double[4], 0, 30);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        // C fails to send; the revert of B (the first move reverted) ALSO fails.
        fx.FailureForMove = m => {
            if (ReferenceEquals(m, moveC)) return new TiltDeviceCommandFailedException("ack timeout");
            if (m.Axis == moveB.Axis && m.Steps == -moveB.Steps) return new TiltDeviceCommandFailedException("revert ack timeout");
            return null;
        };
        fx.ConfirmAnswers.Enqueue(true); // confirm revert

        Assert.DoesNotThrow(() => vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult());

        Assert.Multiple(() => {
            // A, B, C forward (3) + ONE revert attempt (inverse(B), which fails) -- inverse(A) must NEVER be
            // attempted once state is dirty.
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(4));
            Assert.That(fx.ExecutedMoves[3].Axis, Is.EqualTo(moveB.Axis));
            Assert.That(fx.ExecutedMoves[3].Steps, Is.EqualTo(-moveB.Steps));
            Assert.That(fx.ExecutedMoves.Any(m => m.Axis == moveA.Axis && m.Steps == -moveA.Steps), Is.False,
                "revert must stop after its first failure -- inverse(A) must never be attempted once state is dirty");
        });
    }

    [Test]
    public void MidPlanFailure_RevertDeclined_LeavesAppliedMovesInPlace() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus, "B");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB }, new double[4], 0, 20);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.FailureForMove = m => ReferenceEquals(m, moveB) ? new TiltDeviceCommandFailedException("ack timeout") : null;
        fx.ConfirmAnswers.Enqueue(false); // DECLINE revert

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(2), "declining revert must not send any inverse moves");
    }

    [Test]
    public void MidPlanFailure_OperationCanceledException_TreatedAsFailure_JournalPreserved() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus, "B");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB }, new double[4], 0, 20);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.FailureForMove = m => ReferenceEquals(m, moveB) ? new OperationCanceledException("cancelled") : null;
        fx.ConfirmAnswers.Enqueue(true);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Multiple(() => {
            Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(3), "A + B(throws) forward, then 1 revert of A");
            Assert.That(fx.ExecutedMoves[2].Axis, Is.EqualTo(moveA.Axis));
            Assert.That(fx.ExecutedMoves[2].Steps, Is.EqualTo(-moveA.Steps));
            Assert.That(vm.LastExecutedMeasurementGenerationForTest, Is.EqualTo(1));
        });
    }

    // --- Status bar: a transient per-move line must never outlive the operation -----------------------------
    //
    // ".claude/docs/mvvm-patterns.md" (UI Threading & Dispatcher Marshaling): "Status-bar lines don't clear
    // themselves ... After a transient operation (a device move, a recovery sweep), clear it in a finally".
    // Every exit from Automatic Adjustment is such an exit -- including the revert paths, which are exactly
    // where a leftover "reverting move N of M" line was observed hanging in NINA's status bar forever.

    [Test]
    public void ApprovedPlan_WhenComplete_ClearsTheStatusBar() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(false); // decline the re-run prompt

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(() => fx.ReportedAClearingStatus, Is.True.After(2000).PollEvery(20),
            "finishing the plan must report the empty status that clears NINA's status bar");
    }

    [Test]
    public void ReRunConfirmed_TiltWorsened_AfterRevert_ClearsTheStatusBar() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "B");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB }, new double[4], 0, 20);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(true); // accept the re-run
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.RevertLastAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(4), "precondition: 2 forward moves + 2 reverts");
        Assert.That(() => fx.ReportedAClearingStatus, Is.True.After(2000).PollEvery(20),
            "the 'reverting move N of M' line must not survive the revert");
    }

    [Test]
    public void MidPlanFailure_RevertFailsPartway_StillClearsTheStatusBar() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var moveA = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var moveB = Move(TiltMoveAxis.DiagonalB, -12, TiltMoveGroup.Tilt, "B");
        var moveC = Move(TiltMoveAxis.Backfocus, 8, TiltMoveGroup.Backfocus, "C");
        var plan = new TiltAdapterMovePlan(new[] { moveA, moveB, moveC }, new double[4], 0, 30);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.FailureForMove = m => {
            if (ReferenceEquals(m, moveC)) return new TiltDeviceCommandFailedException("ack timeout");
            if (m.Axis == moveB.Axis && m.Steps == -moveB.Steps) return new TiltDeviceCommandFailedException("revert ack timeout");
            return null;
        };
        fx.ConfirmAnswers.Enqueue(true); // confirm revert

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(() => fx.ReportedAClearingStatus, Is.True.After(2000).PollEvery(20),
            "even a revert that bails out partway must leave the status bar clean");
    }

    // --- Live device positions ------------------------------------------------------------------------------
    //
    // The connection service's 'cp' poll is suspended for the WHOLE operation lease -- which spans the moves,
    // the confirming Aberration Inspector re-run (minutes) and any revert -- so the panel's motor counters are
    // frozen for the entire adjustment unless this flow refreshes them itself. The FakeTimeSource here never
    // ticks, so a poll can never be what satisfies these tests.

    [Test]
    public void ApprovedPlan_RefreshesTheDisplayedMotorPositionsAfterEachMove() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        // What the device reports back as part of executing the move -- no follow-up query involved.
        fx.Controller.LastKnownPositions.Returns(new TiltDevicePositions(new[] { 640, 600, 560, 600 }, known: true));
        var move = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(false); // decline the re-run prompt

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Multiple(() => {
            Assert.That(vm.ScrewPositionTopRightDisplay, Is.EqualTo("640"), "TR = device motor 1 = index 0");
            Assert.That(vm.ScrewPositionBottomRightDisplay, Is.EqualTo("560"), "BR = device motor 3 = index 2");
        });
    }

    [Test]
    public void ReRunConfirmed_TiltWorsened_AfterRevert_ShowsThePostRevertPositions() {
        var fx = new AdjustmentFixture();
        var vm = fx.BuildVM();
        fx.SeedValidModel(vm, gx: 0.001, gy: 0.0);
        vm.MeasurementGenerationForTest = 1;
        fx.ConnectAsync().GetAwaiter().GetResult();
        var move = Move(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A");
        var plan = new TiltAdapterMovePlan(new[] { move }, new double[4], 0, 10);
        fx.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, plan);
        fx.ConfirmAnswers.Enqueue(true); // accept the re-run
        fx.OnReRun = () => fx.SeedValidModel(vm, gx: 0.01, gy: 0.0);
        // The forward move reports 640/600/560/600; the revert puts every motor back to 600.
        fx.Controller.LastKnownPositions.Returns(
            new TiltDevicePositions(new[] { 640, 600, 560, 600 }, known: true),
            new TiltDevicePositions(new[] { 600, 600, 600, 600 }, known: true));

        vm.AutomaticAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.RevertLastAdjustmentCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.That(fx.ExecutedMoves, Has.Count.EqualTo(2), "precondition: 1 forward move + 1 revert");
        Assert.That(vm.ScrewPositionTopRightDisplay, Is.EqualTo("600"),
            "after a revert the panel must show the reverted position without waiting for the poll to resume");
    }
}
