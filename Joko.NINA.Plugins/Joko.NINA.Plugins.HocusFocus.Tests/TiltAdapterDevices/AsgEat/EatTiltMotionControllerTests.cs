#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility.SerialCommunication;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices.AsgEat;

// Safety-critical: every test here guards the last line of software defense between the plugin and an
// EEPROM-persisted, physically-damaging motor move. Expected per-corner/per-motor vectors are hand-derived
// from the design doc's generator table and the T7 wizard-index -> device-motor permutation, never computed
// from the implementation under test on both sides of an assertion.
[TestFixture]
public class EatTiltMotionControllerTests {

    // --- Test doubles / builders -------------------------------------------------------------------------

    private static IEatTransport NewTransport() {
        var transport = Substitute.For<IEatTransport>();
        transport.OpenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        // Default: any command acks immediately with no lines. For "cp" specifically this means
        // ParseCpPositions finds zero integers and throws InvalidDeviceResponseException -- i.e. the
        // GUARANTEED pre-T15 "positions unknown" case is the test default; tests that need a real position
        // reading call WithCpResponse. For any move command this means ParseMoveAck (= !TimedOut) succeeds
        // by default; tests that need a failed/timed-out ack call WithTimedOutResponse for that exact wire
        // string (configured AFTER this default so it takes precedence for matching calls).
        transport.SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(new EatRawExchange((string)callInfo[0], Array.Empty<string>(), timedOut: false)));
        return transport;
    }

    private static void WithCpResponse(IEatTransport transport, params int[] values) {
        var line = string.Join(",", values);
        transport.SendAsync("cp", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EatRawExchange("cp", new[] { line }, timedOut: false)));
    }

    private static void WithUnparseableCpResponse(IEatTransport transport) {
        transport.SendAsync("cp", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EatRawExchange("cp", Array.Empty<string>(), timedOut: false)));
    }

    private static void WithTimedOutResponse(IEatTransport transport, string command) {
        transport.SendAsync(command, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EatRawExchange(command, Array.Empty<string>(), timedOut: true)));
    }

    private static ITiltAdapterOptions NewOptions(int maxStepsPerCommand = 200, int maxExcursionSteps = 500, double settleSeconds = 0) {
        var options = Substitute.For<ITiltAdapterOptions>();
        options.TiltDeviceMaxStepsPerCommand = maxStepsPerCommand;
        options.TiltDeviceMaxExcursionSteps = maxExcursionSteps;
        options.TiltDeviceSettleSeconds = settleSeconds;
        return options;
    }

    // Synchronous IProgress<T> double: System.Progress<T> posts through a SynchronizationContext (often
    // asynchronously), which would make a "cancel from inside the progress callback" test non-deterministic.
    // This invokes the callback inline, exactly when EatTiltMotionController calls Report(...).
    private sealed class SyncProgress : IProgress<string> {
        private readonly Action<string> action;
        public SyncProgress(Action<string> action) => this.action = action;
        public void Report(string value) => action(value);
    }

    // --- Constructors -------------------------------------------------------------------------------------

    [Test]
    public void Constructor_NullTransport_Throws() {
        Assert.Throws<ArgumentNullException>(() => new EatTiltMotionController(null, NewOptions()));
    }

    [Test]
    public void Constructor_NullOptions_Throws() {
        Assert.Throws<ArgumentNullException>(() => new EatTiltMotionController(Substitute.For<IEatTransport>(), null));
    }

    [Test]
    public void ProductionConstructor_DoesNotThrow() {
        Assert.DoesNotThrow(() => new EatTiltMotionController(NewOptions()));
    }

    [Test]
    public void Capabilities_DeclaresFourCornerCoupled_NoPerScrewIndependent() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        Assert.Multiple(() => {
            Assert.That(controller.Capabilities.Topology, Is.EqualTo(TiltDeviceTopology.FourCornerCoupled));
            Assert.That(controller.Capabilities.SupportsPerScrewIndependent, Is.False);
            Assert.That(controller.Capabilities.SupportedAxes, Is.EquivalentTo(new[] {
                TiltMoveAxis.DiagonalA, TiltMoveAxis.DiagonalB, TiltMoveAxis.EdgeVertical, TiltMoveAxis.EdgeHorizontal, TiltMoveAxis.Backfocus
            }));
        });
    }

    // --- ConnectAsync ---------------------------------------------------------------------------------------

    [Test]
    public async Task ConnectAsync_SendsCp() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions());

        await controller.ConnectAsync("COM5", CancellationToken.None);

        await transport.Received(1).SendAsync("cp", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectAsync_CpParses_CachesPositions_SeedsShadow_SetsAbsolutePositionsKnownTrue() {
        var transport = NewTransport();
        WithCpResponse(transport, 10, -20, 30, -40);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);

        await controller.ConnectAsync("COM5", CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(controller.Connected, Is.True);
            Assert.That(controller.AbsolutePositionsKnown, Is.True);
        });
        var expected = EatTiltMotionController.SerializeShadowPositions(true, new[] { 10, -20, 30, -40 });
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(expected));
    }

    [Test]
    public async Task ConnectAsync_CpDoesNotParse_StillConnects_SetsAbsolutePositionsKnownFalse() {
        var transport = NewTransport(); // default: 'cp' has zero integers -> unparseable
        var controller = new EatTiltMotionController(transport, NewOptions());

        await controller.ConnectAsync("COM5", CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(controller.Connected, Is.True, "must still connect even though 'cp' did not parse");
            Assert.That(controller.AbsolutePositionsKnown, Is.False);
        });
    }

    [Test]
    public void ConnectAsync_CpDoesNotParse_DoesNotZeroAPreviouslyPersistedShadow() {
        var options = NewOptions();
        // Simulate a previously-persisted, trusted shadow from an earlier session (e.g. a prior connect
        // that DID parse 'cp').
        var persisted = EatTiltMotionController.SerializeShadowPositions(true, new[] { 5, 6, 7, 8 });
        options.TiltDeviceShadowPositions = persisted;

        var transport = NewTransport(); // 'cp' unparseable this session
        var controller = new EatTiltMotionController(transport, options);

        controller.ConnectAsync("COM5", CancellationToken.None).GetAwaiter().GetResult();

        // The plugin NEVER zeroes the shadow -- only the confidence flag changes. The persisted numeric
        // counters must be exactly what was loaded (the confidence flag flips to invalid=false, but the
        // stored option string itself was never overwritten by this failed connect).
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(persisted));
        Assert.That(controller.AbsolutePositionsKnown, Is.False);
    }

    [Test]
    public void ConnectAsync_EmptyPortName_Throws() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        Assert.ThrowsAsync<ArgumentException>(async () => await controller.ConnectAsync("", CancellationToken.None));
    }

    // --- DisconnectAsync --------------------------------------------------------------------------------------

    [Test]
    public async Task DisconnectAsync_ClosesTransport_SetsConnectedFalse() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions());
        await controller.ConnectAsync("COM5", CancellationToken.None);

        await controller.DisconnectAsync(CancellationToken.None);

        Assert.That(controller.Connected, Is.False);
        transport.Received(1).Close();
    }

    // --- ExecuteMoveAsync: formatting + sending ------------------------------------------------------------

    [Test]
    public async Task ExecuteMoveAsync_FormatsCorrectWireCommand_AndSends() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions());
        await controller.ConnectAsync("COM5", CancellationToken.None);
        transport.ClearReceivedCalls();

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 37, TiltMoveGroup.Backfocus, "bf test");
        await controller.ExecuteMoveAsync(move, null, CancellationToken.None);

        await transport.Received(1).SendAsync("bf,37", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteMoveAsync_NotConnected_Throws() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "bf");

        Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
    }

    [Test]
    public void ExecuteMoveAsync_NullMove_Throws() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        Assert.ThrowsAsync<ArgumentNullException>(async () => await controller.ExecuteMoveAsync(null, null, CancellationToken.None));
    }

    // --- ExecuteMoveAsync: cap violation => NO send ---------------------------------------------------------

    [Test]
    public async Task ExecuteMoveAsync_ExceedsMaxStepsPerCommand_ThrowsBeforeSending() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions(maxStepsPerCommand: 50));
        await controller.ConnectAsync("COM5", CancellationToken.None);
        transport.ClearReceivedCalls();

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 51, TiltMoveGroup.Backfocus, "too big");

        Assert.ThrowsAsync<TiltDeviceLimitException>(async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
        await transport.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteMoveAsync_ExactlyAtCap_DoesNotThrow() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions(maxStepsPerCommand: 50, maxExcursionSteps: 500));
        await controller.ConnectAsync("COM5", CancellationToken.None);

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 50, TiltMoveGroup.Backfocus, "exactly at cap");

        Assert.DoesNotThrowAsync(async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
    }

    // --- ExecuteMoveAsync: excursion violation (incl. intermediate) => NO send -----------------------------

    [Test]
    public async Task ExecuteMoveAsync_ExceedsMaxExcursion_ThrowsBeforeSending_ShadowUnchanged() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 40);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);
        var shadowBefore = options.TiltDeviceShadowPositions;
        transport.ClearReceivedCalls();

        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 45, TiltMoveGroup.Tilt, "too far");

        Assert.ThrowsAsync<TiltDeviceLimitException>(async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
        await transport.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
    }

    [Test]
    public async Task ExecuteMoveAsync_SecondMoveWouldExceedExcursionAtIntermediateState_ThrowsBeforeSending() {
        // moveA alone is within the excursion cap; moveA THEN moveB would push a shared motor beyond it.
        // moveB's own validation (using the shadow AFTER moveA already succeeded) must catch this.
        var transport = NewTransport();
        // Baseline 50 with a ceiling of 100: moveA fits under both bounds, and the pair breaches the ceiling.
        // (A zero baseline cannot express this any more -- moveA's own -40 corner would be refused by the floor
        // before the intermediate-state ceiling check this test is about ever came into play.)
        WithCpResponse(transport, 50, 50, 50, 50);
        var options = NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 100);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        var moveA = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "A"); // TR=90, BL=10 (inside [0,100])
        await controller.ExecuteMoveAsync(moveA, null, CancellationToken.None);
        transport.ClearReceivedCalls();

        var moveB = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "B"); // would push TR to 110 (> 100)

        Assert.ThrowsAsync<TiltDeviceLimitException>(async () => await controller.ExecuteMoveAsync(moveB, null, CancellationToken.None));
        await transport.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    // --- ExecuteMoveAsync: shadow updates ONLY on success ---------------------------------------------------

    [Test]
    public async Task ExecuteMoveAsync_AckTimesOut_ThrowsCommandFailed_ShadowUnchanged() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);
        var shadowBefore = options.TiltDeviceShadowPositions;

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "will time out");
        WithTimedOutResponse(transport, EatCommands.Format(move));

        Assert.ThrowsAsync<TiltDeviceCommandFailedException>(async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
    }

    // FINDING 1 regression lock: a write timeout (EatSerialTransport.SendAsync throwing TimeoutException,
    // which it deliberately does NOT swallow -- see its WriteCommand remarks) must be classified as
    // TiltDeviceCommandFailedException, exactly like an ack timeout/failure, and must NOT advance the shadow.
    [Test]
    public async Task ExecuteMoveAsync_SendThrowsTimeoutException_ThrowsCommandFailed_ShadowUnchanged() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);
        var shadowBefore = options.TiltDeviceShadowPositions;

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "write timeout");
        var wire = EatCommands.Format(move);
        // Simulate EatSerialTransport's own write-timeout behavior: SendAsync's returned Task faults with
        // TimeoutException rather than completing with a tolerant EatRawExchange.
        transport.SendAsync(wire, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EatRawExchange>(new TimeoutException("write timeout")));

        var ex = Assert.ThrowsAsync<TiltDeviceCommandFailedException>(async () =>
            await controller.ExecuteMoveAsync(move, null, CancellationToken.None));

        Assert.Multiple(() => {
            Assert.That(ex.InnerException, Is.InstanceOf<TimeoutException>());
            Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
        });
    }

    // FINDING 1 regression lock: a propagated SerialPortClosedException during a move must NOT be
    // reclassified/wrapped -- T9's port-unplug handling depends on catching it by its own type -- and must
    // also leave the shadow unchanged.
    [Test]
    public async Task ExecuteMoveAsync_SendThrowsSerialPortClosedException_PropagatesUnwrapped_ShadowUnchanged() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);
        var shadowBefore = options.TiltDeviceShadowPositions;

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "unplugged");
        var wire = EatCommands.Format(move);
        transport.SendAsync(wire, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EatRawExchange>(
                new SerialPortClosedException("unplugged mid-move", new InvalidOperationException("port closed"))));

        Assert.ThrowsAsync<SerialPortClosedException>(async () =>
            await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
    }

    // FINDING 2 regression lock: connecting with an UNPARSEABLE 'cp' (AbsolutePositionsKnown == false,
    // shadow seeded from zero/persisted) must NOT disable excursion protection -- it must still be enforced
    // against the best-estimate shadow, accumulated from zero across successive moves.
    [Test]
    public async Task ExecuteMoveAsync_PositionsUnknown_ExcursionStillEnforced_AccumulatingFromZero() {
        var transport = NewTransport(); // default: 'cp' unparseable
        var options = NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 50);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);
        Assert.That(controller.AbsolutePositionsKnown, Is.False, "precondition: 'cp' must not have parsed");

        // Accumulate per-motor position toward the excursion cap via successive within-cap moves (Backfocus
        // moves all four motors equally -- see ExecuteMoveAsync_Success_AdvancesAndPersistsShadow above).
        var move1 = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "step 1"); // -> 20
        await controller.ExecuteMoveAsync(move1, null, CancellationToken.None);
        var move2 = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "step 2"); // -> 40
        await controller.ExecuteMoveAsync(move2, null, CancellationToken.None);
        transport.ClearReceivedCalls();

        // A third move of the same size would push every motor from 40 to 60, exceeding the 50-step cap.
        var move3 = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "step 3 - over the line");

        Assert.ThrowsAsync<TiltDeviceLimitException>(async () => await controller.ExecuteMoveAsync(move3, null, CancellationToken.None));
        await transport.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.That(controller.AbsolutePositionsKnown, Is.False, "must remain degraded; excursion enforcement being active must not itself confirm positions");
    }

    [Test]
    public async Task ExecuteMoveAsync_Success_AdvancesAndPersistsShadow() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 20, TiltMoveGroup.Backfocus, "bf");
        await controller.ExecuteMoveAsync(move, null, CancellationToken.None);

        var expected = EatTiltMotionController.SerializeShadowPositions(true, new[] { 20, 20, 20, 20 });
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(expected));
    }

    // --- Wizard -> device-motor permutation ------------------------------------------------------------------

    [Test]
    public void PermuteWizardToDeviceMotorOrder_AppliesExactMapping() {
        var wizard = new double[] { 1, 2, 3, 4 };
        var result = EatTiltMotionController.PermuteWizardToDeviceMotorOrder(wizard);
        // motor1(TR)<-wizard1, motor2(TL)<-wizard2, motor3(BR)<-wizard4, motor4(BL)<-wizard3.
        Assert.That(result, Is.EqualTo(new double[] { 1, 2, 4, 3 }));
    }

    [Test]
    public void PermuteWizardToDeviceMotorOrder_WrongLength_Throws() {
        Assert.Throws<ArgumentException>(() => EatTiltMotionController.PermuteWizardToDeviceMotorOrder(new double[] { 1, 2, 3 }));
    }

    [Test]
    public async Task ExecuteMoveAsync_DiagonalAMove_UpdatesShadowAtTRAndBL_NotTLOrBR() {
        var transport = NewTransport();
        // Nonzero baseline: a diagonal move is differential (one corner up, the opposite down), and travel
        // below 0 is refused, so this permutation check needs headroom underneath to exercise the move at all.
        WithCpResponse(transport, 100, 100, 100, 100);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // DiagonalA(+45) in WIZARD space is (wizard1=+45, wizard2=0, wizard3=-45, wizard4=0). Permuted to
        // DEVICE order this lands on motor1(TR)<-wizard1=+45 and motor4(BL)<-wizard3=-45 -- NOT motor3(BR).
        // A naive, unpermuted "wizard order == device order" implementation would incorrectly move BR
        // instead of BL here (wizard3 sits at array index 2, which is BR's position in device order).
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 45, TiltMoveGroup.Tilt, "diagonal A");
        await controller.ExecuteMoveAsync(move, null, CancellationToken.None);

        var expected = EatTiltMotionController.SerializeShadowPositions(true, new[] { 145, 100, 100, 55 });
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(expected));
    }

    [Test]
    public async Task ExecuteMoveAsync_DiagonalBMove_UpdatesShadowAtTLAndBR_NotTROrBL() {
        var transport = NewTransport();
        // Nonzero baseline for the same reason as the DiagonalA case above.
        WithCpResponse(transport, 100, 100, 100, 100);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // DiagonalB(+30) in WIZARD space is (wizard1=0, wizard2=+30, wizard3=0, wizard4=-30). Permuted to
        // DEVICE order this lands on motor2(TL)<-wizard2=+30 and motor3(BR)<-wizard4=-30 -- NOT motor4(BL).
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalB, 30, TiltMoveGroup.Tilt, "diagonal B");
        await controller.ExecuteMoveAsync(move, null, CancellationToken.None);

        var expected = EatTiltMotionController.SerializeShadowPositions(true, new[] { 100, 130, 70, 100 });
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(expected));
    }

    // --- Cancellation: between commands only ---------------------------------------------------------------

    [Test]
    public async Task ExecuteMoveAsync_CancellationRequestedViaProgressDuringMove_StillCompletesCurrentMove() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions());
        await controller.ConnectAsync("COM5", CancellationToken.None);
        transport.ClearReceivedCalls();

        using var cts = new CancellationTokenSource();
        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "move1");
        var progress = new SyncProgress(_ => cts.Cancel()); // fires synchronously, BEFORE the send completes

        // No abort capability on the device -- ExecuteMoveAsync must ignore the caller's token for the
        // actual exchange, so this call must complete normally despite ct flipping to cancelled mid-call.
        Assert.DoesNotThrowAsync(async () => await controller.ExecuteMoveAsync(move, progress, cts.Token));

        await transport.Received(1).SendAsync("bf,10", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteMoveAsync_TokenCancelledAfterFirstMove_SkipsSecondMove_WithoutSending() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions());
        await controller.ConnectAsync("COM5", CancellationToken.None);
        transport.ClearReceivedCalls();

        using var cts = new CancellationTokenSource();
        var move1 = new TiltAdapterMove(TiltMoveAxis.Backfocus, 10, TiltMoveGroup.Backfocus, "move1");
        var move2 = new TiltAdapterMove(TiltMoveAxis.Backfocus, 5, TiltMoveGroup.Backfocus, "move2");

        await controller.ExecuteMoveAsync(move1, null, cts.Token);
        cts.Cancel(); // caller decides to stop AFTER move1 completed, BEFORE starting move2

        Assert.ThrowsAsync<OperationCanceledException>(async () => await controller.ExecuteMoveAsync(move2, null, cts.Token));
        // Only move1's send happened -- move2's entry check throws before any validation or I/O.
        await transport.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    // --- QueryPositionsAsync ---------------------------------------------------------------------------------

    [Test]
    public void QueryPositionsAsync_NotConnected_Throws() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.QueryPositionsAsync(CancellationToken.None));
    }

    [Test]
    public async Task QueryPositionsAsync_CpParses_ReconcilesShadow_ReturnsKnownPositions() {
        var transport = NewTransport();
        var controller = new EatTiltMotionController(transport, NewOptions());
        await controller.ConnectAsync("COM5", CancellationToken.None); // degraded: 'cp' didn't parse at connect
        Assert.That(controller.AbsolutePositionsKnown, Is.False);

        WithCpResponse(transport, 5, 6, 7, 8);
        var positions = await controller.QueryPositionsAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(positions.Known, Is.True);
            Assert.That(positions.PerMotorSteps, Is.EqualTo(new[] { 5, 6, 7, 8 }));
            Assert.That(controller.AbsolutePositionsKnown, Is.True);
        });
    }

    [Test]
    public async Task QueryPositionsAsync_CpDoesNotParse_ReturnsUnknown_LeavesShadowUntouched() {
        var transport = NewTransport();
        WithCpResponse(transport, 1, 2, 3, 4);
        var options = NewOptions();
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None); // seeds a valid shadow [1,2,3,4]
        Assert.That(controller.AbsolutePositionsKnown, Is.True);
        var shadowBefore = options.TiltDeviceShadowPositions;

        WithUnparseableCpResponse(transport);
        var positions = await controller.QueryPositionsAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(positions.Known, Is.False);
            // A single failed poll is a no-op -- it must NOT retroactively invalidate an already-trusted shadow.
            Assert.That(controller.AbsolutePositionsKnown, Is.True);
            Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
        });
    }

    // --- Shadow serialization format --------------------------------------------------------------------------

    [Test]
    public void ShadowPositions_SerializeThenParse_RoundTrips() {
        var serialized = EatTiltMotionController.SerializeShadowPositions(true, new[] { 10, -20, 30, -40 });
        bool ok = EatTiltMotionController.TryParseShadowPositions(serialized, out var valid, out var steps);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(valid, Is.True);
            Assert.That(steps, Is.EqualTo(new[] { 10, -20, 30, -40 }));
        });
    }

    [Test]
    public void ShadowPositions_SerializeInvalidFlag_RoundTrips() {
        var serialized = EatTiltMotionController.SerializeShadowPositions(false, new[] { 0, 0, 0, 0 });
        bool ok = EatTiltMotionController.TryParseShadowPositions(serialized, out var valid, out var steps);
        Assert.Multiple(() => {
            Assert.That(ok, Is.True);
            Assert.That(valid, Is.False);
            Assert.That(steps, Is.EqualTo(new[] { 0, 0, 0, 0 }));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("garbage")]
    [TestCase("1;2;3;4")]
    [TestCase("2;1;2;3;4")]
    [TestCase("1;a;2;3;4")]
    public void ShadowPositions_TryParse_MalformedInput_ReturnsFalse(string input) {
        bool ok = EatTiltMotionController.TryParseShadowPositions(input, out _, out _);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void SerializeShadowPositions_WrongLength_Throws() {
        Assert.Throws<ArgumentException>(() => EatTiltMotionController.SerializeShadowPositions(true, new[] { 1, 2, 3 }));
    }

    // --- OrderForMinimalPeakExcursion --------------------------------------------------------------------------

    [Test]
    public async Task OrderForMinimalPeakExcursion_PicksOrderingWithSmallestPeak() {
        var transport = NewTransport();
        // Baseline 60 keeps BOTH orderings clear of the floor (A-first dips to 5, B-first to 15), so the
        // choice below is driven purely by peak excursion -- what this test is about -- with no bias in play.
        WithCpResponse(transport, 60, 60, 60, 60);
        var options = NewOptions(maxStepsPerCommand: 100, maxExcursionSteps: 110);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // moveA alone spikes TR to 115 (> 110). moveB alone is small. Applying B THEN A never spikes past 105;
        // applying A THEN B spikes to 115 first. The final combined state (105) is identical either way.
        var moveA = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 55, TiltMoveGroup.Tilt, "A");
        var moveB = new TiltAdapterMove(TiltMoveAxis.DiagonalA, -10, TiltMoveGroup.Tilt, "B");

        var order = controller.OrderForMinimalPeakExcursion(new[] { moveA, moveB });

        Assert.That(order, Is.EqualTo(new[] { moveB, moveA }));

        // Executing in the returned order must never violate the excursion limit at any intermediate/final state.
        foreach (var move in order) {
            await controller.ExecuteMoveAsync(move, null, CancellationToken.None);
        }
    }

    [Test]
    public async Task OrderForMinimalPeakExcursion_NoValidOrdering_Throws() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions(maxStepsPerCommand: 100, maxExcursionSteps: 50);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // Neither move ALONE exceeds the 50-step excursion cap (45 <= 50), but both push the SAME motor
        // further in the SAME direction, so their combined effect (90) exceeds it regardless of which is
        // sent first -- a genuine "no ordering escapes it" case, not just a single oversized move.
        var moveA = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 45, TiltMoveGroup.Tilt, "A");
        var moveB = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 45, TiltMoveGroup.Tilt, "B");

        Assert.Throws<TiltDeviceLimitException>(() => controller.OrderForMinimalPeakExcursion(new[] { moveA, moveB }));
    }

    [Test]
    public void OrderForMinimalPeakExcursion_MoveExceedsCap_ThrowsRegardlessOfOrdering() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions(maxStepsPerCommand: 50, maxExcursionSteps: 500));
        var move = new TiltAdapterMove(TiltMoveAxis.Backfocus, 51, TiltMoveGroup.Backfocus, "too big");

        Assert.Throws<TiltDeviceLimitException>(() => controller.OrderForMinimalPeakExcursion(new[] { move }));
    }

    // --- Travel window [0, maxExcursion]: the floor and the automatic upward bias --------------------------

    [Test]
    public async Task ExecuteMoveAsync_WouldDriveMotorBelowZero_ThrowsBeforeSending_ShadowUnchanged() {
        var transport = NewTransport();
        WithCpResponse(transport, 30, 30, 30, 30);
        var options = NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 1000);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);
        var shadowBefore = options.TiltDeviceShadowPositions;
        transport.ClearReceivedCalls();

        // Well inside the ceiling, but BL would land at -10. The floor, not the cap, must stop this.
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "drives BL negative");

        var ex = Assert.ThrowsAsync<TiltDeviceLimitException>(
            async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("below 0"));
        await transport.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
    }

    [Test]
    public async Task ExecuteMoveAsync_LandsExactlyAtZero_IsAllowed() {
        var transport = NewTransport();
        WithCpResponse(transport, 40, 40, 40, 40);
        var controller = new EatTiltMotionController(transport, NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 1000));
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // BL lands on exactly 0 -- the floor is inclusive, so this is legal.
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 40, TiltMoveGroup.Tilt, "BL to exactly zero");

        Assert.DoesNotThrowAsync(async () => await controller.ExecuteMoveAsync(move, null, CancellationToken.None));
    }

    [Test]
    public async Task OrderForMinimalPeakExcursion_PlanWouldGoBelowZero_PrependsExactBackfocusBias() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var options = NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 1000);
        var controller = new EatTiltMotionController(transport, options);
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // From zeroed counters a diagonal correction drives BL to -25, so the sequence must be lifted by
        // exactly 25 -- no more (bias is a real backfocus change, so overshooting it is a defect, not slack).
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 25, TiltMoveGroup.Tilt, "tilt");

        var order = controller.OrderForMinimalPeakExcursion(new[] { move });

        Assert.That(order, Has.Count.EqualTo(2));
        Assert.That(order[0].Axis, Is.EqualTo(TiltMoveAxis.Backfocus));
        Assert.That(order[0].Steps, Is.EqualTo(25));
        Assert.That(order[0].Description, Does.Contain("bias"));
        Assert.That(order[1], Is.SameAs(move));

        // The whole returned sequence must actually execute -- the bias is only correct if it makes the
        // controller's own per-move floor check pass for every move that follows it.
        foreach (var m in order) {
            await controller.ExecuteMoveAsync(m, null, CancellationToken.None);
        }
    }

    [Test]
    public async Task OrderForMinimalPeakExcursion_PlanStaysAboveZero_AddsNoBias() {
        var transport = NewTransport();
        WithCpResponse(transport, 500, 500, 500, 500);
        var controller = new EatTiltMotionController(transport, NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 1000));
        await controller.ConnectAsync("COM5", CancellationToken.None);

        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 25, TiltMoveGroup.Tilt, "tilt");

        var order = controller.OrderForMinimalPeakExcursion(new[] { move });

        Assert.That(order, Is.EqualTo(new[] { move }), "ample headroom below zero means no bias is warranted");
    }

    [Test]
    public async Task OrderForMinimalPeakExcursion_BiasExceedsPerCommandCap_IsSplit() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var controller = new EatTiltMotionController(transport, NewOptions(maxStepsPerCommand: 60, maxExcursionSteps: 1000));
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // Needs a 50-step lift... but the move itself is 50, and the cap is 60, so the bias fits in one part.
        // Use a larger correction to force the bias itself past the cap: BL would reach -150.
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 60, TiltMoveGroup.Tilt, "tilt");
        var order = controller.OrderForMinimalPeakExcursion(new[] { move });

        var biasMoves = order.Take(order.Count - 1).ToArray();
        Assert.That(biasMoves.Sum(m => m.Steps), Is.EqualTo(60), "the split parts must sum to the exact lift needed");
        Assert.That(biasMoves.All(m => Math.Abs(m.Steps) <= 60), Is.True, "no bias part may exceed the per-command cap");
        Assert.That(order[order.Count - 1], Is.SameAs(move));
    }

    [Test]
    public async Task OrderForMinimalPeakExcursion_BiasWouldBreachCeiling_Throws() {
        var transport = NewTransport();
        WithCpResponse(transport, 0, 0, 0, 0);
        var controller = new EatTiltMotionController(transport, NewOptions(maxStepsPerCommand: 200, maxExcursionSteps: 60));
        await controller.ConnectAsync("COM5", CancellationToken.None);

        // The correction alone fits under 60, and the 50-step lift alone fits too -- but lifted, the high
        // corner reaches 100. The ceiling has to be checked AFTER the bias is added, not before.
        var move = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 50, TiltMoveGroup.Tilt, "tilt");

        var ex = Assert.Throws<TiltDeviceLimitException>(() => controller.OrderForMinimalPeakExcursion(new[] { move }));
        Assert.That(ex.Message, Does.Contain("bias"));
    }

    [Test]
    public void OrderForMinimalPeakExcursion_EmptyList_ReturnsEmpty() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        var order = controller.OrderForMinimalPeakExcursion(Array.Empty<TiltAdapterMove>());
        Assert.That(order, Is.Empty);
    }

    [Test]
    public void OrderForMinimalPeakExcursion_Null_Throws() {
        var controller = new EatTiltMotionController(NewTransport(), NewOptions());
        Assert.Throws<ArgumentNullException>(() => controller.OrderForMinimalPeakExcursion(null));
    }

    [Test]
    public void OrderForMinimalPeakExcursion_IsPure_DoesNotMutateShadow() {
        var options = NewOptions(maxStepsPerCommand: 100, maxExcursionSteps: 500);
        var controller = new EatTiltMotionController(NewTransport(), options);
        var shadowBefore = options.TiltDeviceShadowPositions;

        var moveA = new TiltAdapterMove(TiltMoveAxis.DiagonalA, 10, TiltMoveGroup.Tilt, "A");
        var moveB = new TiltAdapterMove(TiltMoveAxis.DiagonalB, 5, TiltMoveGroup.Tilt, "B");
        controller.OrderForMinimalPeakExcursion(new[] { moveA, moveB });

        Assert.That(options.TiltDeviceShadowPositions, Is.EqualTo(shadowBefore));
    }
}
