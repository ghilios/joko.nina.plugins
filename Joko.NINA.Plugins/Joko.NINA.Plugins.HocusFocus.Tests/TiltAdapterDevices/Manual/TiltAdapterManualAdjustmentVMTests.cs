using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.Manual;

/// <summary>
/// Behaviour of the Tilt Adapter Wizard's Manual Adjustment panel. The fixture wires a real
/// <see cref="TiltDeviceConnectionService"/> (so the exclusive lease and position publishing are the production
/// ones) to an NSubstitute <see cref="ITiltMotionController"/>, and drives the VM through a synchronous
/// dispatcher so every refresh has landed by the time an assertion runs.
/// </summary>
[TestFixture]
public class TiltAdapterManualAdjustmentVMTests {

    private const string PresetName = "ASG Electronic EAT - 90mm";
    private const double StepMicrons = 1.8;

    private sealed class FakeTimeSource : ITiltDeviceTimeSource {
        public DateTimeOffset UtcNow { get; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public event EventHandler Tick;

        public void Dispose() => Tick = null;
    }

    private sealed class Fixture : IDisposable {
        public ITiltAdapterOptions Options { get; }
        public ITiltMotionController Controller { get; }
        public TiltDeviceConnectionService Service { get; }
        public TiltAdapterManualAdjustmentVM VM { get; private set; }

        public List<TiltAdapterMove> ExecutedMoves { get; } = new();
        public int PublishCallCount { get; private set; }

        /// <summary>
        /// Return non-null to fail that move. Evaluated BEFORE the move is recorded, so
        /// <see cref="ExecutedMoves"/> holds exactly the moves that succeeded and its count is the index of the
        /// move about to run.
        /// </summary>
        public Func<TiltAdapterMove, Exception> FailureForMove { get; set; }

        /// <summary>Invoked after each successful move with the 1-based number of moves completed so far.</summary>
        public Action<int> AfterMove { get; set; }

        public TiltDeviceAdjustmentChoice NextChoice { get; set; } = TiltDeviceAdjustmentChoice.Cancelled;
        public int PromptCallCount { get; private set; }
        public Func<bool, bool, TiltDevicePlanPreview> LastReplanner { get; private set; }

        private int[] positions = { 500, 500, 500, 500 }; // device motor order: TR, TL, BR, BL

        /// <summary>False until a position read or a move has confirmed the counters.</summary>
        public bool PositionsConfirmed { get; private set; }

        public Fixture(int maxExcursion = 2000, int maxStepsPerCommand = 200) {
            Options = Substitute.For<ITiltAdapterOptions>();
            Options.DeviceName.Returns(PresetName);
            Options.ScrewCount.Returns(4);
            Options.AdjustmentType.Returns(TiltAdjustmentType.StepperMotors);
            Options.StepperStepSizeMicrons.Returns(StepMicrons);
            Options.TiltDeviceMaxExcursionSteps.Returns(maxExcursion);
            Options.TiltDeviceMaxStepsPerCommand.Returns(maxStepsPerCommand);
            Options.TiltDeviceSettleSeconds.Returns(0.0);

            Controller = Substitute.For<ITiltMotionController>();
            Controller.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Controller.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            Controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TiltDevicePositions.Unknown));
            // Unknown until something confirms them, exactly like a freshly connected device whose first
            // position read has not landed yet.
            Controller.LastKnownPositions.Returns(_ => PositionsConfirmed
                ? new TiltDevicePositions(positions.ToArray(), known: true)
                : TiltDevicePositions.Unknown);
            // Identity ordering by default: the manual panel's target mode is about the decomposition, and a
            // reordering/bias fixture would obscure which assertions are about which.
            Controller.OrderForMinimalPeakExcursion(Arg.Any<IReadOnlyList<TiltAdapterMove>>())
                .Returns(ci => ci.Arg<IReadOnlyList<TiltAdapterMove>>());
            Controller.ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
                .Returns(ci => {
                    // The real controller checks the token at entry and nowhere else — once past this line a
                    // move always runs to completion, which is what makes "stop" mean "send nothing further".
                    ci.Arg<CancellationToken>().ThrowIfCancellationRequested();
                    var move = ci.Arg<TiltAdapterMove>();
                    var failure = FailureForMove?.Invoke(move);
                    if (failure != null) {
                        throw failure;
                    }
                    ExecutedMoves.Add(move);
                    // Mirror the real controller: a move response carries fresh counters, so the shadow advances.
                    PositionsConfirmed = true;
                    var delta = EatTiltMotionController.PermuteWizardToDeviceMotorOrder(move.PerCornerSteps);
                    for (int i = 0; i < 4; ++i) {
                        positions[i] += (int)Math.Round(delta[i]);
                    }
                    AfterMove?.Invoke(ExecutedMoves.Count);
                    return Task.CompletedTask;
                });

            Service = new TiltDeviceConnectionService(Substitute.For<IProfileService>(), Options, (name, opts) => Controller, new FakeTimeSource());
            Service.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(TiltDeviceConnectionService.CurrentPositions)) {
                    ++PublishCallCount;
                }
            };
        }

        public async Task<TiltAdapterManualAdjustmentVM> BuildConnectedAsync(bool publishPositions = true) {
            await Service.ConnectAsync(PresetName, "COM5", CancellationToken.None);
            if (publishPositions) {
                PositionsConfirmed = true;
                Service.PublishControllerPositions();
            }
            PublishCallCount = 0;
            return BuildVM();
        }

        public TiltAdapterManualAdjustmentVM BuildVM() {
            VM = new TiltAdapterManualAdjustmentVM(Options, Service, new SynchronousApplicationDispatcher(), ShowPromptAsync);
            return VM;
        }

        private Task<TiltDeviceAdjustmentChoice> ShowPromptAsync(Func<bool, bool, TiltDevicePlanPreview> replanner, double unitMicrons) {
            ++PromptCallCount;
            LastReplanner = replanner;
            return Task.FromResult(NextChoice);
        }

        /// <summary>Overwrites the device's counters and republishes them, as a poll or a foreign move would.</summary>
        public void SetPositions(int tr, int tl, int br, int bl) {
            positions = new[] { tr, tl, br, bl };
            PositionsConfirmed = true;
            Service.PublishControllerPositions();
        }

        public int[] Positions => positions.ToArray();

        public void Dispose() {
            VM?.Dispose();
            Service.DisconnectAsync().GetAwaiter().GetResult();
        }
    }

    private static void Pick(TiltAdapterManualAdjustmentVM vm, string key, bool positive = true, int amount = 20) {
        vm.SelectedTargetKey = key;
        vm.PositiveDirection = positive;
        vm.AmountText = amount.ToString();
    }

    private static void SetTargets(TiltAdapterManualAdjustmentVM vm, int tl, int tr, int bl, int br) {
        // TargetCells are in display order TL, TR, BL, BR.
        var values = new[] { tl, tr, bl, br };
        for (int i = 0; i < 4; ++i) {
            vm.TargetCells[i].TargetText = values[i].ToString();
        }
    }

    // ======================================================================================================
    // Expander, summary, connection state
    // ======================================================================================================

    [Test]
    public async Task Expander_IsCollapsedByDefault() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();

        Assert.Multiple(() => {
            Assert.That(vm.IsExpanded, Is.False);
            Assert.That(vm.SummaryText, Is.EqualTo("Nudge a corner or set target positions"));
        });
    }

    [Test]
    public void Disconnected_ReplacesTheBodyAndSaysSoInTheSummary() {
        using var f = new Fixture();
        var vm = f.BuildVM();

        Assert.Multiple(() => {
            Assert.That(vm.IsDeviceConnected, Is.False);
            Assert.That(vm.SummaryText, Is.EqualTo("Connect to enable"));
            Assert.That(vm.NotConnectedText, Is.EqualTo("Connect the device above to make adjustments."));
        });
    }

    [Test]
    public async Task DeviceBusyElsewhere_BlocksBothModesWithTheOperationName() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "TR");

        using var foreignLease = f.Service.TryBeginOperation("Tilt Adapter Wizard Calibration");
        // The service raises INPC for the lease, which the VM's handler picks up.

        Assert.Multiple(() => {
            Assert.That(vm.SendDisabledReason, Is.EqualTo("Device busy — Tilt Adapter Wizard Calibration. Wait for it to finish."));
            Assert.That(vm.CanSend, Is.False);
            Assert.That(vm.SummaryText, Is.EqualTo("Device busy — Tilt Adapter Wizard Calibration"));
        });
    }

    // ======================================================================================================
    // Single move — preview
    // ======================================================================================================

    [Test]
    public async Task Preview_PredictsEveryMotorInDeviceOrder_AndNamesTheCoupledPair() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(tr: 480, tl: 512, br: 505, bl: 470);

        Pick(vm, "TR", positive: true, amount: 20);

        Assert.Multiple(() => {
            // Device motor order: [0]=TR, [1]=TL, [2]=BR, [3]=BL. Only TR and BL move.
            Assert.That(vm.PredictedPositions, Is.EqualTo(new[] { 500, 512, 505, 450 }));
            Assert.That(vm.PreviewSemanticText, Is.EqualTo("TR (Motor 1) +20 · BL (Motor 4) −20 steps"));
            Assert.That(vm.PreviewWireCommand, Is.EqualTo("tr,20"));
            Assert.That(vm.PreviewKindLabel, Is.EqualTo("Corner"));
        });

        // Cells are in display order TL, TR, BL, BR.
        var cells = vm.PreviewCells;
        Assert.Multiple(() => {
            Assert.That(cells.Select(c => c.Corner.Label), Is.EqualTo(new[] { "TL", "TR", "BL", "BR" }));
            Assert.That(cells[0].ValueText, Is.EqualTo("512 → 512"));
            Assert.That(cells[0].IsMoving, Is.False);
            Assert.That(cells[1].ValueText, Is.EqualTo("480 → 500 (+20)"));
            Assert.That(cells[1].IsMoving, Is.True);
            Assert.That(cells[2].ValueText, Is.EqualTo("470 → 450 (-20)"));
            Assert.That(cells[2].IsMoving, Is.True);
            Assert.That(cells[3].IsMoving, Is.False);
        });
    }

    [Test]
    public async Task Preview_SideMoveMovesAllFourMotorsInTwoPairs() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(tr: 500, tl: 500, br: 500, bl: 500);

        Pick(vm, "Top", positive: true, amount: 30);

        Assert.Multiple(() => {
            // Top edge up: TR & TL +30, BL & BR -30.
            Assert.That(vm.PredictedPositions, Is.EqualTo(new[] { 530, 530, 470, 470 }));
            Assert.That(vm.PreviewWireCommand, Is.EqualTo("tp,30"));
            Assert.That(vm.PreviewKindLabel, Is.EqualTo("Side"));
            Assert.That(vm.PreviewCells.All(c => c.IsMoving), Is.True);
        });
    }

    [Test]
    public async Task Preview_BackfocusMovesAllFourTheSameWayAndSaysTiltIsUnchanged() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);

        Pick(vm, "All", positive: true, amount: 20);

        Assert.Multiple(() => {
            Assert.That(vm.PredictedPositions, Is.EqualTo(new[] { 520, 520, 520, 520 }));
            Assert.That(vm.PreviewWireCommand, Is.EqualTo("bf,20"));
            Assert.That(vm.ConsequenceText, Is.EqualTo("Changes sensor spacing by 36.0 µm; tilt unchanged."));
        });
    }

    [Test]
    public async Task Preview_TiltConsequenceIsTwiceTheAmount_BecauseTheMoveIsDifferential() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();

        Pick(vm, "TR", positive: true, amount: 20);

        // TR +20 and BL -20 => the TR-vs-BL spacing changes by 40 steps = 72 µm.
        Assert.That(vm.ConsequenceText, Is.EqualTo("Changes TR-vs-BL spacing by 72.0 µm of screw travel."));
    }

    [Test]
    public async Task Preview_TagsPositionsNearEitherEndOfTheTravelWindow() {
        using var f = new Fixture(maxExcursion: 2000);
        var vm = await f.BuildConnectedAsync();
        // 5% of 2000 = 100. Put TR just inside the top band and BL just inside the bottom one after the move.
        f.SetPositions(tr: 1890, tl: 500, br: 500, bl: 110);

        Pick(vm, "TR", positive: true, amount: 20);

        var byLabel = vm.PreviewCells.ToDictionary(c => c.Corner.Label);
        Assert.Multiple(() => {
            Assert.That(byLabel["TR"].LimitTag, Is.EqualTo("near max"));
            Assert.That(byLabel["TR"].Severity, Is.EqualTo(ManualLimitSeverity.Near));
            Assert.That(byLabel["BL"].LimitTag, Is.EqualTo("near 0"));
            Assert.That(byLabel["TL"].HasLimitTag, Is.False);
            Assert.That(vm.CanSend, Is.True, "a near-limit tag is advisory, not blocking");
        });
    }

    [Test]
    public async Task Preview_BlocksAMoveThatWouldDriveAMotorBelowZero() {
        using var f = new Fixture(maxExcursion: 2000);
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(tr: 500, tl: 500, br: 500, bl: 10);

        Pick(vm, "TR", positive: true, amount: 20);

        var bl = vm.PreviewCells.Single(c => c.Corner.Label == "BL");
        Assert.Multiple(() => {
            Assert.That(bl.LimitTag, Is.EqualTo("below 0"));
            Assert.That(bl.IsBlocking, Is.True);
            Assert.That(vm.CanSend, Is.False);
            Assert.That(vm.SendDisabledReason,
                Is.EqualTo("This move would drive Motor 4 (BL) below 0. Reduce the amount, or send an All + move first to lift all motors."));
        });
    }

    [Test]
    public async Task Preview_BlocksAMoveThatWouldExceedTheMaxExcursion() {
        using var f = new Fixture(maxExcursion: 600);
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(tr: 590, tl: 300, br: 300, bl: 300);

        Pick(vm, "TR", positive: true, amount: 20);

        Assert.Multiple(() => {
            Assert.That(vm.CanSend, Is.False);
            Assert.That(vm.SendDisabledReason,
                Is.EqualTo("This move would drive Motor 1 (TR) past the max excursion (600). Reduce the amount, or lower all motors with an All − move first."));
        });
    }

    [Test]
    public async Task Preview_SplitsAnAmountAboveThePerCommandCapIntoSameAxisChunks() {
        using var f = new Fixture(maxStepsPerCommand: 200);
        var vm = await f.BuildConnectedAsync();

        Pick(vm, "All", positive: true, amount: 450);

        var moves = vm.PlannedSingleMoves;
        Assert.Multiple(() => {
            Assert.That(moves.Count, Is.EqualTo(3));
            Assert.That(moves.Select(m => m.Steps), Is.EqualTo(new[] { 200, 200, 50 }));
            Assert.That(moves.All(m => m.Axis == TiltMoveAxis.Backfocus), Is.True);
            Assert.That(vm.SendButtonText, Is.EqualTo("Send 3 moves"));
            Assert.That(vm.MoveCountAndDurationText, Is.EqualTo("3 moves · ~30 s"));
        });
    }

    [Test]
    public async Task PositionsUnknown_IsAdvisoryForSingleMove_NotABlock() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync(publishPositions: false);

        Pick(vm, "TR");

        Assert.Multiple(() => {
            Assert.That(vm.PositionsKnown, Is.False);
            Assert.That(vm.PositionsUnknownAdvisoryVisible, Is.True);
            Assert.That(vm.PredictedPositions, Is.Null);
            Assert.That(vm.PreviewCells.All(c => c.ValueText == "unknown → unknown"), Is.True);
            Assert.That(vm.CanSend, Is.True);
            Assert.That(vm.SendDisabledReason, Is.Empty);
        });
    }

    // ======================================================================================================
    // Disabled reasons
    // ======================================================================================================

    [Test]
    public async Task SendDisabledReason_NoSelection() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();

        Assert.Multiple(() => {
            Assert.That(vm.SendDisabledReason, Is.EqualTo("Pick a corner, side, or All to move."));
            Assert.That(vm.PreviewVisible, Is.False);
            Assert.That(vm.CanSend, Is.False);
        });
    }

    [TestCase("")]
    [TestCase("0")]
    [TestCase("-5")]
    [TestCase("abc")]
    [TestCase("2.5")]
    public async Task SendDisabledReason_InvalidAmount(string amountText) {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        vm.SelectedTargetKey = "TR";
        vm.AmountText = amountText;

        Assert.Multiple(() => {
            Assert.That(vm.SendDisabledReason, Is.EqualTo("Enter an amount of at least 1 step."));
            Assert.That(vm.CanSend, Is.False);
        });
    }

    [Test]
    public async Task SendDisabledReason_DeviceBusyOutranksAMissingSelection() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();

        using var foreignLease = f.Service.TryBeginOperation("Automatic Adjustment");

        Assert.That(vm.SendDisabledReason, Is.EqualTo("Device busy — Automatic Adjustment. Wait for it to finish."));
    }

    // ======================================================================================================
    // Target positions
    // ======================================================================================================

    [Test]
    public async Task TargetMode_PrefillsFromCurrentPositions() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(tr: 480, tl: 512, br: 505, bl: 470);

        vm.Mode = ManualAdjustmentMode.TargetPositions;

        // Display order TL, TR, BL, BR.
        Assert.Multiple(() => {
            Assert.That(vm.TargetCells.Select(c => c.TargetText), Is.EqualTo(new[] { "512", "480", "470", "505" }));
            Assert.That(vm.TargetCells.Select(c => c.NowText), Is.EqualTo(new[] { "now 512", "now 480", "now 470", "now 505" }));
            Assert.That(vm.TargetHintText, Is.EqualTo("Targets match the current positions — nothing to move."));
            Assert.That(vm.CanReview, Is.False);
        });
    }

    /// <summary>
    /// Regression: the hint line and the disabled-reason line both used to render "Targets match the current
    /// positions — nothing to move.", stuttering the same sentence twice, one directly under the other. Caught
    /// in the live app, not by a unit test — hence this one.
    /// </summary>
    [Test]
    public async Task TargetMode_NothingToMoveIsSaidOnce_NotTwice() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);

        vm.Mode = ManualAdjustmentMode.TargetPositions;

        Assert.Multiple(() => {
            Assert.That(vm.TargetHintText, Is.EqualTo(TiltAdapterManualAdjustmentVM.NothingToMoveCopy));
            Assert.That(vm.ReviewDisabledReason, Is.Empty, "the hint line already carries this sentence");
            Assert.That(vm.ReviewDisabledReasonVisible, Is.False);
            Assert.That(vm.CanReview, Is.False, "it must still block Review");
        });
    }

    [Test]
    public async Task TargetMode_DecomposesAPureCornerRequestIntoOneDiagonalMove() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;

        // TR up 40, BL down 40 — a pure DiagonalA request.
        SetTargets(vm, tl: 500, tr: 540, bl: 460, br: 500);

        var preview = vm.TargetPlanPreview;
        Assert.Multiple(() => {
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.Plan.Moves.Count, Is.EqualTo(1));
            Assert.That(preview.Plan.Moves[0].Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
            Assert.That(preview.Plan.Moves[0].Steps, Is.EqualTo(40));
            Assert.That(vm.TargetHintText, Is.EqualTo("Becomes 1 move · ~10 s."));
            Assert.That(vm.TwistWarningVisible, Is.False);
            Assert.That(vm.CanReview, Is.True);
        });
    }

    [Test]
    public async Task TargetMode_APureLiftBecomesASingleBackfocusMove() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;

        SetTargets(vm, tl: 560, tr: 560, bl: 560, br: 560);

        Assert.Multiple(() => {
            Assert.That(vm.TargetPlanPreview.Plan.Moves.Single().Axis, Is.EqualTo(TiltMoveAxis.Backfocus));
            Assert.That(vm.TargetPlanPreview.Plan.Moves.Single().Steps, Is.EqualTo(60));
            Assert.That(vm.TargetHintText, Is.EqualTo("Becomes 1 move · ~10 s."));
        });
    }

    [Test]
    public async Task TargetMode_TwistIsReportedBeforeTheDialog_AndCellsShowWhatTheyWillActuallyReach() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;

        // s = (s1..s4) in wizard order = (TR, TL, BL, BR) = (+40, -40, +40, -40): pure twist, a = b = f = 0,
        // t = (40 + 40 + 40 + 40)/4 = 40. No rigid plane can produce it.
        SetTargets(vm, tl: 460, tr: 540, bl: 540, br: 460);

        Assert.Multiple(() => {
            Assert.That(vm.TwistWarningVisible, Is.True);
            Assert.That(vm.TwistWarningBody, Does.StartWith("These four targets differ from a rigid plane by ±40 steps (about 72.0 µm across the sensor)."));
            // Nothing is reachable, so every motor stays where it is.
            Assert.That(vm.TargetCells.Select(c => c.WillReachText),
                Is.EqualTo(new[] { "will reach 500", "will reach 500", "will reach 500", "will reach 500" }));
        });
    }

    [Test]
    public async Task TargetMode_NoTwistMeansNoWillReachClutter() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;

        SetTargets(vm, tl: 500, tr: 540, bl: 460, br: 500);

        Assert.That(vm.TargetCells.All(c => !c.HasWillReach), Is.True);
    }

    [Test]
    public async Task TargetMode_BlocksWhenPositionsAreUnknown() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync(publishPositions: false);

        vm.Mode = ManualAdjustmentMode.TargetPositions;

        Assert.Multiple(() => {
            Assert.That(vm.CanReview, Is.False);
            Assert.That(vm.ReviewDisabledReason,
                Is.EqualTo("Motor positions are unknown — targets need a known starting point. They update after a successful poll or move."));
        });
    }

    [TestCase("-1")]
    [TestCase("2001")]
    [TestCase("nope")]
    public async Task TargetMode_BlocksAndFlagsAnOutOfRangeOrNonNumericTarget(string bad) {
        using var f = new Fixture(maxExcursion: 2000);
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;

        vm.TargetCells[0].TargetText = bad;

        Assert.Multiple(() => {
            Assert.That(vm.TargetCells[0].HasError, Is.True);
            Assert.That(vm.CanReview, Is.False);
            Assert.That(vm.ReviewDisabledReason, Is.EqualTo("Targets must be whole numbers between 0 and 2000 steps."));
        });
    }

    [Test]
    public async Task TargetMode_ResetToCurrentRestoresEveryCell() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;
        SetTargets(vm, tl: 100, tr: 200, bl: 300, br: 400);

        vm.ResetToCurrentCommand.Execute(null);

        Assert.That(vm.TargetCells.Select(c => c.TargetText), Is.EqualTo(new[] { "500", "500", "500", "500" }));
    }

    [Test]
    public async Task TargetMode_BackgroundPositionChangeUpdatesNowAndDelta_ButNeverOverwritesTypedTargets() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;
        SetTargets(vm, tl: 500, tr: 540, bl: 460, br: 500);

        // Something else moved the motors (a poll, another surface).
        f.SetPositions(tr: 510, tl: 500, br: 500, bl: 500);

        var tr = vm.TargetCells.Single(c => c.Corner.Label == "TR");
        Assert.Multiple(() => {
            Assert.That(tr.TargetText, Is.EqualTo("540"), "typed targets must survive a background refresh");
            Assert.That(tr.NowText, Is.EqualTo("now 510"));
            Assert.That(tr.DeltaText, Is.EqualTo("Δ +30 (54.0 µm)"));
        });
    }

    [Test]
    public async Task TargetMode_ReviewCancelled_SendsNothing() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;
        SetTargets(vm, tl: 500, tr: 540, bl: 460, br: 500);
        f.NextChoice = TiltDeviceAdjustmentChoice.Cancelled;

        await vm.ReviewTargetsAsync();

        Assert.Multiple(() => {
            Assert.That(f.PromptCallCount, Is.EqualTo(1));
            Assert.That(f.ExecutedMoves, Is.Empty);
        });
    }

    [Test]
    public async Task TargetMode_ProceedSendsExactlyTheReviewedPlan_ThenReprefillsFromWhereTheMotorsLanded() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;
        SetTargets(vm, tl: 500, tr: 540, bl: 460, br: 500);

        var reviewed = vm.TargetPlanPreview.Plan;
        f.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(applyTilt: true, applyBackfocus: true, finalPlan: reviewed);

        await vm.ReviewTargetsAsync();

        Assert.Multiple(() => {
            Assert.That(f.ExecutedMoves.Select(m => (m.Axis, m.Steps)), Is.EqualTo(new[] { (TiltMoveAxis.DiagonalA, 40) }));
            Assert.That(f.Positions, Is.EqualTo(new[] { 540, 500, 500, 460 }));
            Assert.That(vm.TargetCells.Select(c => c.TargetText), Is.EqualTo(new[] { "500", "540", "460", "500" }));
            Assert.That(vm.SuccessText, Does.StartWith("Adjustment complete — 1 move sent"));
            Assert.That(vm.SummaryText, Does.StartWith("Last sent: 1 move at"));
        });
    }

    // ======================================================================================================
    // Execution
    // ======================================================================================================

    [Test]
    public async Task Send_TakesTheLeaseUnderTheManualAdjustmentName_AndReleasesIt() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 20);

        string nameDuringSend = null;
        f.Controller.When(c => c.ExecuteMoveAsync(Arg.Any<TiltAdapterMove>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>()))
            .Do(_ => nameDuringSend ??= f.Service.CurrentOperationName);

        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(nameDuringSend, Is.EqualTo("Manual Adjustment"));
            Assert.That(f.Service.IsOperationActive, Is.False, "the lease must be released when the send ends");
        });
    }

    [Test]
    public async Task Send_PublishesPositionsAfterEveryMove() {
        using var f = new Fixture(maxStepsPerCommand: 200);
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 450); // 3 commands

        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(f.ExecutedMoves.Count, Is.EqualTo(3));
            Assert.That(f.PublishCallCount, Is.EqualTo(3));
            Assert.That(f.Positions, Is.EqualTo(new[] { 950, 950, 950, 950 }));
        });
    }

    [Test]
    public async Task Send_LeavesTheInputsAloneSoRepeatNudgingIsOneClick() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "TR", positive: true, amount: 20);

        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(vm.SelectedTargetKey, Is.EqualTo("TR"));
            Assert.That(vm.PositiveDirection, Is.True);
            Assert.That(vm.AmountText, Is.EqualTo("20"));
            Assert.That(vm.CanSend, Is.True);
            Assert.That(vm.SuccessText, Does.StartWith("Sent TR (Motor 1) +20 · BL (Motor 4) −20 steps · 1 move"));
            // The header summary is deliberately the short form, not a second copy of the body line.
            Assert.That(vm.SummaryText, Does.StartWith("Last sent: TR +20 at"));
        });
    }

    [Test]
    public async Task Send_LeaseUnavailable_ReportsBusyAndSendsNothing() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 20);

        using var foreignLease = f.Service.TryBeginOperation("Automatic Adjustment");
        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(f.ExecutedMoves, Is.Empty);
            Assert.That(vm.CanSend, Is.False);
        });
    }

    [Test]
    public async Task Stop_SkipsTheUnsentMovesAndSaysWhatWasAlreadyApplied() {
        using var f = new Fixture(maxStepsPerCommand: 200);
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 450); // 3 commands

        // Stop once the first command has completed; the rest must never be sent (ExecuteMoveAsync only honours
        // the token at entry, so cancellation can never interrupt an in-flight move).
        f.AfterMove = completed => {
            if (completed == 1) {
                vm.StopCommand.Execute(null);
            }
        };

        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(f.ExecutedMoves.Count, Is.EqualTo(1));
            Assert.That(vm.ResultTitle, Is.EqualTo("Stopped after 1 of 3 moves"));
            Assert.That(vm.ResultBody, Does.Contain("there is no automatic undo"));
            Assert.That(vm.ResultIsError, Is.False);
            Assert.That(vm.IsExpanded, Is.True, "a result the user has not dismissed must force the panel open");
        });
    }

    [Test]
    public async Task Send_SingleCommandFailure_SaysTheCountersWereNotAdvanced() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 20);
        f.FailureForMove = _ => new TiltDeviceCommandFailedException("no ack within 20s");

        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(vm.ResultTitle, Is.EqualTo("Move failed"));
            Assert.That(vm.ResultBody, Does.Contain("The command 'bf,20' got no valid response"));
            Assert.That(vm.ResultBody, Does.Contain("were not advanced"));
            Assert.That(vm.ResultIsError, Is.True);
            Assert.That(vm.SummaryText, Is.EqualTo("Last move failed — see details"));
        });
    }

    [Test]
    public async Task Send_SplitNudgePartialFailure_TellsTheUserTheExactRemainingAmount() {
        using var f = new Fixture(maxStepsPerCommand: 200);
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 450); // 200 + 200 + 50; fail the third command.
        f.FailureForMove = _ => f.ExecutedMoves.Count >= 2 ? new TiltDeviceCommandFailedException("link died") : null;
        Assert.That(vm.PlannedSingleMoves.Count, Is.EqualTo(3), "fixture precondition");

        await vm.SendSingleMoveAsync();

        Assert.Multiple(() => {
            Assert.That(vm.ResultTitle, Is.EqualTo("Adjustment stopped after 2 of 3 moves"));
            Assert.That(vm.ResultBody, Does.Contain("Sent 2 of 3 moves (400 of 450 steps)"));
            Assert.That(vm.ResultBody, Does.Contain("set Amount to 50 and press Send to finish"));
            Assert.That(vm.ResultIsError, Is.True);
        });
    }

    [Test]
    public async Task TargetMode_PartialFailure_PointsAtReviewingAgainFromWhereTheMotorsActuallyAre() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        vm.Mode = ManualAdjustmentMode.TargetPositions;
        // Both diagonals move => two moves.
        SetTargets(vm, tl: 530, tr: 540, bl: 460, br: 470);

        var reviewed = vm.TargetPlanPreview.Plan;
        Assert.That(reviewed.Moves.Count, Is.EqualTo(2), "fixture precondition");
        f.NextChoice = TiltDeviceAdjustmentChoice.Proceeded(true, true, reviewed);
        f.FailureForMove = _ => f.ExecutedMoves.Count >= 1 ? new TiltDeviceCommandFailedException("link died") : null;

        await vm.ReviewTargetsAsync();

        Assert.Multiple(() => {
            Assert.That(f.ExecutedMoves.Count, Is.EqualTo(1));
            Assert.That(vm.ResultTitle, Is.EqualTo("Adjustment stopped after 1 of 2 moves"));
            Assert.That(vm.ResultBody, Does.Contain("press Review moves"));
            Assert.That(vm.ResultIsError, Is.True);
        });
    }

    [Test]
    public async Task DismissResult_ClearsThePanelAndLetsItCollapseAgain() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        Pick(vm, "All", amount: 20);
        f.FailureForMove = _ => new TiltDeviceCommandFailedException("boom");
        await vm.SendSingleMoveAsync();
        Assert.That(vm.IsExpanded, Is.True, "precondition");

        vm.DismissResultCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.ResultVisible, Is.False);
            Assert.That(vm.IsExpanded, Is.False);
        });
    }

    [Test]
    public async Task Send_NeverAsksTheControllerToReorderOrBias() {
        using var f = new Fixture();
        var vm = await f.BuildConnectedAsync();
        f.SetPositions(500, 500, 500, 500);
        Pick(vm, "TR", amount: 20);

        await vm.SendSingleMoveAsync();

        // A single adjustment must be exactly the one move previewed — no silently prepended backfocus lift.
        f.Controller.DidNotReceive().OrderForMinimalPeakExcursion(Arg.Any<IReadOnlyList<TiltAdapterMove>>());
        Assert.That(f.ExecutedMoves.Single().Axis, Is.EqualTo(TiltMoveAxis.DiagonalA));
    }
}
