#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterDevices;

[TestFixture]
public class TiltDeviceConnectionServiceTests {

    // Deterministic ITiltDeviceTimeSource test double: UtcNow is a plain settable clock, and Advance()
    // moves it forward then fires Tick synchronously once — no real Timer, no real wall-clock wait. Because
    // the service computes due-ness from (now - storedTimestamp) rather than counting ticks, a single big
    // Advance(30 minutes) and many small Advance(5 seconds) calls both drive the service correctly.
    private sealed class FakeTiltDeviceTimeSource : ITiltDeviceTimeSource {
        public DateTimeOffset UtcNow { get; private set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public event EventHandler Tick;

        public void Advance(TimeSpan delta) {
            UtcNow += delta;
            Tick?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Moves virtual time WITHOUT firing a tick, so a test can set up state the next tick will observe.</summary>
        public void SetNow(DateTimeOffset value) => UtcNow = value;

        public void Dispose() {
        }
    }

    private static ITiltMotionController NewControllerSubstitute() {
        var controller = Substitute.For<ITiltMotionController>();
        controller.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        controller.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TiltDevicePositions.Unknown));
        return controller;
    }

    private static (TiltDeviceConnectionService service, IProfileService profile, FakeTiltDeviceTimeSource time, List<ITiltMotionController> createdControllers) Build() {
        var profile = Substitute.For<IProfileService>();
        var options = Substitute.For<ITiltAdapterOptions>();
        var time = new FakeTiltDeviceTimeSource();
        var createdControllers = new List<ITiltMotionController>();

        ITiltMotionController Factory(string presetName, ITiltAdapterOptions opts) {
            var controller = NewControllerSubstitute();
            createdControllers.Add(controller);
            return controller;
        }

        var service = new TiltDeviceConnectionService(profile, options, Factory, time);
        return (service, profile, time, createdControllers);
    }

    // --- Connect / Disconnect -------------------------------------------------------------------------

    [Test]
    public async Task ConnectAsync_SetsConnectedAndController_RaisesINPC() {
        var (service, _, _, controllers) = Build();
        var raised = new List<string>();
        service.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        await service.ConnectAsync("ASG Electronic EAT - 90mm", "COM5", CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(service.Connected, Is.True);
            Assert.That(service.Controller, Is.SameAs(controllers[0]));
            Assert.That(raised, Does.Contain(nameof(TiltDeviceConnectionService.Connected)));
            Assert.That(raised, Does.Contain(nameof(TiltDeviceConnectionService.Controller)));
        });
        await controllers[0].Received(1).ConnectAsync("COM5", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisconnectAsync_ClearsConnectedAndController_RaisesINPC() {
        var (service, _, _, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        var raised = new List<string>();
        service.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        await service.DisconnectAsync();

        Assert.Multiple(() => {
            Assert.That(service.Connected, Is.False);
            Assert.That(service.Controller, Is.Null);
            Assert.That(raised, Does.Contain(nameof(TiltDeviceConnectionService.Connected)));
            Assert.That(raised, Does.Contain(nameof(TiltDeviceConnectionService.Controller)));
        });
        await controllers[0].Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisconnectAsync_WhenNotConnected_IsSafeNoOp() {
        var (service, _, _, _) = Build();
        Assert.DoesNotThrowAsync(async () => await service.DisconnectAsync());
        Assert.That(service.Connected, Is.False);
    }

    // Documented choice: Connect-while-connected tears down the existing controller/session first, then
    // connects fresh under the newly requested preset/port (rather than no-op-if-same or throwing). This
    // lets "Connect" double as "reconnect" (e.g. a different COM port) without a separate Disconnect click.
    [Test]
    public async Task ConnectAsync_WhileConnected_DisconnectsOldControllerThenReconnects() {
        var (service, _, _, controllers) = Build();
        await service.ConnectAsync("preset-a", "COM1", CancellationToken.None);
        var first = controllers[0];

        await service.ConnectAsync("preset-b", "COM2", CancellationToken.None);

        Assert.That(controllers, Has.Count.EqualTo(2));
        var second = controllers[1];

        Assert.Multiple(() => {
            Assert.That(service.Connected, Is.True);
            Assert.That(service.Controller, Is.SameAs(second));
        });
        await first.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        await second.Received(1).ConnectAsync("COM2", Arg.Any<CancellationToken>());
    }

    // --- Simulated-port routing (connect to the camera simulator instead of a serial device) ----------

    private static (TiltDeviceConnectionService service, List<ITiltMotionController> registryControllers, ITiltMotionController simController)
            BuildWithSimulator() {
        var profile = Substitute.For<IProfileService>();
        var options = Substitute.For<ITiltAdapterOptions>();
        var time = new FakeTiltDeviceTimeSource();
        var registryControllers = new List<ITiltMotionController>();

        ITiltMotionController Registry(string presetName, ITiltAdapterOptions opts) {
            var controller = NewControllerSubstitute();
            registryControllers.Add(controller);
            return controller;
        }

        var simController = NewControllerSubstitute();
        var service = new TiltDeviceConnectionService(profile, options, Registry, time, () => simController);
        return (service, registryControllers, simController);
    }

    [Test]
    public async Task ConnectAsync_SentinelPort_UsesSimulatedFactory_NotRegistry() {
        var (service, registryControllers, simController) = BuildWithSimulator();

        await service.ConnectAsync("ASG Electronic EAT - 90mm", SimulatedTiltPort.PortName, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(service.Connected, Is.True);
            Assert.That(service.Controller, Is.SameAs(simController));
            Assert.That(registryControllers, Is.Empty, "the serial registry factory must NOT be used for the Simulator port");
        });
        await simController.Received(1).ConnectAsync(SimulatedTiltPort.PortName, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConnectAsync_RealPort_UsesRegistryFactory_NotSimulator() {
        var (service, registryControllers, simController) = BuildWithSimulator();

        await service.ConnectAsync("ASG Electronic EAT - 90mm", "COM7", CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(service.Controller, Is.SameAs(registryControllers[0]));
            Assert.That(service.Controller, Is.Not.SameAs(simController));
        });
        await registryControllers[0].Received(1).ConnectAsync("COM7", Arg.Any<CancellationToken>());
    }

    // Documents the inert fallback: a host that wired no simulator factory (every existing caller) treats the
    // sentinel like any other port name and routes it through the registry -- so existing behavior is unchanged.
    [Test]
    public async Task ConnectAsync_SentinelPort_NoSimulatorFactory_FallsBackToRegistry() {
        var (service, _, _, controllers) = Build();

        await service.ConnectAsync("ASG Electronic EAT - 90mm", SimulatedTiltPort.PortName, CancellationToken.None);

        Assert.That(service.Controller, Is.SameAs(controllers[0]));
        await controllers[0].Received(1).ConnectAsync(SimulatedTiltPort.PortName, Arg.Any<CancellationToken>());
    }

    [Test]
    public void SimulatedTiltPort_IsSimulator_MatchesOnlyTheSentinel() {
        Assert.Multiple(() => {
            Assert.That(SimulatedTiltPort.PortName, Is.EqualTo("Simulator"));
            Assert.That(SimulatedTiltPort.IsSimulator("Simulator"), Is.True);
            Assert.That(SimulatedTiltPort.IsSimulator("COM3"), Is.False);
            Assert.That(SimulatedTiltPort.IsSimulator(null), Is.False);
            Assert.That(SimulatedTiltPort.IsSimulator("simulator"), Is.False, "sentinel match is case-sensitive");
        });
    }

    // Regression lock (fix #4): if the controller's own ConnectAsync throws AFTER it may have already
    // opened the underlying port, ConnectAsync must best-effort disconnect the controller before rethrowing
    // -- otherwise the COM port is leaked and the NEXT connect attempt fails with "port in use".
    [Test]
    public async Task ConnectAsync_ControllerConnectThrows_DisconnectsControllerBeforeRethrowing_AndStaysDisconnected() {
        var profile = Substitute.For<IProfileService>();
        var options = Substitute.For<ITiltAdapterOptions>();
        var time = new FakeTiltDeviceTimeSource();
        var controller = Substitute.For<ITiltMotionController>();
        controller.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("port already in use")));
        controller.DisconnectAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = new TiltDeviceConnectionService(profile, options, (presetName, opts) => controller, time);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ConnectAsync("preset", "COM3", CancellationToken.None));

        await controller.Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
        Assert.That(service.Connected, Is.False);
    }

    [Test]
    public void ConnectAsync_ControllerConnectThrows_SecondaryDisconnectErrorIsSwallowed_OriginalThrows() {
        var profile = Substitute.For<IProfileService>();
        var options = Substitute.For<ITiltAdapterOptions>();
        var time = new FakeTiltDeviceTimeSource();
        var controller = Substitute.For<ITiltMotionController>();
        controller.ConnectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("original connect failure")));
        controller.DisconnectAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("secondary disconnect failure")));

        var service = new TiltDeviceConnectionService(profile, options, (presetName, opts) => controller, time);

        // The ORIGINAL connect failure must be what surfaces to the caller, not the secondary cleanup error.
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ConnectAsync("preset", "COM3", CancellationToken.None));
        Assert.That(ex.Message, Is.EqualTo("original connect failure"));
    }

    // --- Operation-token exclusivity -------------------------------------------------------------------

    [Test]
    public void TryBeginOperation_SecondCallWhileActive_ReturnsNull() {
        var (service, _, _, _) = Build();

        var first = service.TryBeginOperation("wizard calibration");
        Assert.That(first, Is.Not.Null);
        Assert.That(service.IsOperationActive, Is.True);

        var second = service.TryBeginOperation("inspector adjustment");
        Assert.That(second, Is.Null);

        first.Dispose();
    }

    [Test]
    public void TryBeginOperation_DisposeReleases_AllowsNewOperation() {
        var (service, _, _, _) = Build();

        var first = service.TryBeginOperation("wizard calibration");
        first.Dispose();

        Assert.That(service.IsOperationActive, Is.False);

        var second = service.TryBeginOperation("inspector adjustment");
        Assert.That(second, Is.Not.Null);
        second.Dispose();
    }

    [Test]
    public void OperationToken_DoubleDispose_IsSafeAndDoesNotOverReleaseTheLease() {
        var (service, _, _, _) = Build();

        var token = service.TryBeginOperation("op");
        token.Dispose();
        Assert.DoesNotThrow(() => token.Dispose());
        Assert.That(service.IsOperationActive, Is.False);

        // A second lease must be obtainable exactly once — proves the double Dispose() didn't leave the
        // underlying hardware lock over-released (which would let two operations be "active" at once).
        var a = service.TryBeginOperation("a");
        Assert.That(a, Is.Not.Null);
        var b = service.TryBeginOperation("b");
        Assert.That(b, Is.Null);
        a.Dispose();
    }

    // --- Profile change ---------------------------------------------------------------------------------

    [Test]
    public async Task ProfileChanged_ForcesDisconnect() {
        var (service, profile, _, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);
        await service.LastForcedDisconnectTask;

        Assert.That(service.Connected, Is.False);
        await controllers[0].Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProfileChanged_WhileOperationActive_WaitsForOperationToRelease() {
        var (service, profile, _, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);
        var token = service.TryBeginOperation("calibration");
        Assert.That(token, Is.Not.Null);

        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        // The forced disconnect must not proceed while the operation lease is held.
        Assert.That(service.Connected, Is.True, "Must not disconnect mid-operation.");

        token.Dispose();
        await service.LastForcedDisconnectTask;

        Assert.That(service.Connected, Is.False, "Must disconnect once the operation lease is released.");
    }

    // --- Position polling --------------------------------------------------------------------------------

    [Test]
    public async Task Poll_AdvancingPollInterval_QueriesPositionsAndUpdatesState() {
        var (service, _, time, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);
        var controller = controllers[0];

        var known = new TiltDevicePositions(new[] { 10, 20, 30, 40 }, known: true);
        controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(known));

        time.Advance(TiltDeviceConnectionService.PollInterval);

        Assert.Multiple(() => {
            Assert.That(service.PositionsKnown, Is.True);
            Assert.That(service.CurrentPositions, Is.EqualTo(new[] { 10, 20, 30, 40 }));
        });
        await controller.Received(1).QueryPositionsAsync(Arg.Any<CancellationToken>());

        // Flip back to Known == false: PositionsKnown must track the latest poll, not stick at true.
        controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(TiltDevicePositions.Unknown));
        time.Advance(TiltDeviceConnectionService.PollInterval);
        Assert.That(service.PositionsKnown, Is.False);
    }

    [Test]
    public async Task Poll_QueryThrows_SetsPositionsUnknownWithoutCrashingPollLoop() {
        var (service, _, time, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);
        var controller = controllers[0];

        var known = new TiltDevicePositions(new[] { 1, 2, 3, 4 }, known: true);
        controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(known));
        time.Advance(TiltDeviceConnectionService.PollInterval);
        Assert.That(service.PositionsKnown, Is.True);

        controller.QueryPositionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<TiltDevicePositions>(new InvalidOperationException("cp response unparseable")));

        Assert.DoesNotThrow(() => time.Advance(TiltDeviceConnectionService.PollInterval));
        Assert.That(service.PositionsKnown, Is.False);

        // The poll loop itself must have survived the throw: a subsequent tick still polls normally.
        controller.QueryPositionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(known));
        time.Advance(TiltDeviceConnectionService.PollInterval);
        Assert.That(service.PositionsKnown, Is.True);
    }

    [Test]
    public async Task Poll_PausesWhileOperationActive_ResumesAfterDispose() {
        var (service, _, time, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);
        var controller = controllers[0];
        controller.ClearReceivedCalls();

        var token = service.TryBeginOperation("calibration");
        Assert.That(token, Is.Not.Null);

        time.Advance(TiltDeviceConnectionService.PollInterval);
        await controller.DidNotReceive().QueryPositionsAsync(Arg.Any<CancellationToken>());

        token.Dispose();

        time.Advance(TiltDeviceConnectionService.PollInterval);
        await controller.Received(1).QueryPositionsAsync(Arg.Any<CancellationToken>());
    }

    // --- Idle auto-disconnect -----------------------------------------------------------------------------
    //
    // The modal that used to ask "disconnect it?" is gone. After the idle threshold the service arms a visible
    // countdown; if nothing intervenes before the grace period expires it disconnects on its own. The service
    // owns the whole policy so that hardware behavior never depends on a UI thread being responsive.

    private static void AdvanceBy(FakeTiltDeviceTimeSource time, TimeSpan total) {
        // Step at the real poll cadence so ticks land the way they do in production.
        var elapsed = TimeSpan.Zero;
        while (elapsed < total) {
            var step = TiltDeviceConnectionService.PollInterval;
            if (elapsed + step > total) {
                step = total - elapsed;
            }
            time.Advance(step);
            elapsed += step;
        }
    }

    [Test]
    public async Task Idle_ThirtyMinutesNoActivity_ArmsTheCountdownWithoutDisconnecting() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);

        Assert.Multiple(() => {
            Assert.That(service.IdleDisconnectPending, Is.True);
            Assert.That(service.Connected, Is.True, "arming the countdown must not itself disconnect");
            Assert.That(service.IdleDisconnectRemaining, Is.EqualTo(TiltDeviceConnectionService.IdleDisconnectGrace));
        });
    }

    [Test]
    public async Task Idle_CountdownExpires_DisconnectsAndRecordsWhy() {
        var (service, _, time, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        AdvanceBy(time, TiltDeviceConnectionService.IdleDisconnectGrace);
        await service.LastIdleAutoDisconnectTask;

        Assert.Multiple(() => {
            Assert.That(service.Connected, Is.False);
            Assert.That(service.IdleDisconnectPending, Is.False);
            Assert.That(service.LastIdleAutoDisconnectUtc, Is.Not.Null, "the reconnect-side status line needs this");
        });
        await controllers[0].Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    // The countdown must not fire early, or the number on screen would be a lie.
    [Test]
    public async Task Idle_WithinTheGracePeriod_StaysConnected() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        AdvanceBy(time, TiltDeviceConnectionService.IdleDisconnectGrace - TimeSpan.FromSeconds(10));

        Assert.Multiple(() => {
            Assert.That(service.Connected, Is.True);
            Assert.That(service.IdleDisconnectPending, Is.True);
            Assert.That(service.IdleDisconnectRemaining, Is.EqualTo(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public async Task Idle_PollingTicksDoNotResetIdleTimer() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout - TimeSpan.FromSeconds(5));
        Assert.That(service.IdleDisconnectPending, Is.False, "must not arm before the idle timeout elapses");

        AdvanceBy(time, TimeSpan.FromSeconds(10));
        Assert.That(service.IdleDisconnectPending, Is.True, "polling in between must not have pushed the deadline out");
    }

    [Test]
    public async Task KeepConnectedResetIdle_CancelsTheCountdownAndReArms() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        Assert.That(service.IdleDisconnectPending, Is.True);

        service.KeepConnectedResetIdle();

        Assert.Multiple(() => {
            Assert.That(service.IdleDisconnectPending, Is.False);
            Assert.That(service.Connected, Is.True, "keep-connected must not disconnect");
        });

        var pastTheOldGrace = TiltDeviceConnectionService.IdleDisconnectGrace + TimeSpan.FromSeconds(30);
        AdvanceBy(time, pastTheOldGrace);
        Assert.That(service.Connected, Is.True, "the cancelled countdown must not fire later");

        // Advance only to the new threshold -- going further would arm AND expire a fresh countdown, which is a
        // different behavior than the re-arm this test is about.
        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout - pastTheOldGrace);
        Assert.Multiple(() => {
            Assert.That(service.IdleDisconnectPending, Is.True, "a full new idle window must arm it again");
            Assert.That(service.Connected, Is.True);
        });
    }

    // Touching the device IS declining. Every device operation takes a lease, which records activity -- so a
    // user who is actually working never has to find the banner's button.
    [Test]
    public async Task Idle_DeviceActivityDuringTheCountdown_CancelsIt() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        Assert.That(service.IdleDisconnectPending, Is.True);

        using (var token = service.TryBeginOperation("a motor move")) {
            Assert.That(token, Is.Not.Null);
            Assert.That(service.IdleDisconnectPending, Is.False, "taking a lease cancels the countdown");
        }

        AdvanceBy(time, TiltDeviceConnectionService.IdleDisconnectGrace + TimeSpan.FromSeconds(30));
        Assert.That(service.Connected, Is.True);
    }

    // The deadline is set up to a minute before it is acted on, so the fire path re-validates rather than
    // trusting that the world stood still.
    [Test]
    public async Task Idle_ActivityRecordedAfterTheDeadlinePasses_VetoesTheDisconnect() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        // Move virtual time past the deadline WITHOUT ticking, then record activity, then let a tick land.
        time.SetNow(time.UtcNow + TiltDeviceConnectionService.IdleDisconnectGrace + TimeSpan.FromSeconds(5));
        service.KeepConnectedResetIdle();
        time.Advance(TimeSpan.Zero);

        Assert.That(service.Connected, Is.True);
    }

    [Test]
    public async Task Reconnecting_ClearsTheAutoDisconnectNote() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);
        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        AdvanceBy(time, TiltDeviceConnectionService.IdleDisconnectGrace);
        await service.LastIdleAutoDisconnectTask;
        Assert.That(service.LastIdleAutoDisconnectUtc, Is.Not.Null);

        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        Assert.That(service.LastIdleAutoDisconnectUtc, Is.Null);
    }

    // Regression lock (fix #5): T11's hands-off calibration run holds an operation lease for a whole
    // multi-command plan that can legitimately exceed 30 minutes -- the countdown must not arm while it
    // is held, however far past the threshold virtual time advances.
    [Test]
    public async Task Idle_SuppressedWhileOperationActive_EvenWellPastThreshold() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        var token = service.TryBeginOperation("hands-off calibration");
        Assert.That(token, Is.Not.Null);

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout + TimeSpan.FromMinutes(10));
        Assert.That(service.IdleDisconnectPending, Is.False, "suppressed for the entire duration of an active operation lease");

        // EndOperation() itself records activity, so releasing the lease resets the idle clock.
        token.Dispose();
        AdvanceBy(time, TimeSpan.FromSeconds(5));
        Assert.That(service.IdleDisconnectPending, Is.False, "releasing the lease resets the idle timer");

        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout);
        Assert.That(service.IdleDisconnectPending, Is.True, "suppression must not be sticky");
    }

    // Regression lock (T9 fix E): EndOperation used to release the lock / clear isOperationActive and only
    // THEN call RecordUserActivity() -- a timer tick landing in that window (for a lease held past the idle
    // timeout) would see isOperationActive == false together with a STALE LastActivityUtc and arm a spurious
    // countdown right as the operation ends. The fix reorders RecordUserActivity() before the flag clear.
    [Test]
    public async Task EndOperation_TickLandingAsOperationEnds_DoesNotArmASpuriousCountdown() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        var token = service.TryBeginOperation("long calibration");
        Assert.That(token, Is.Not.Null);
        AdvanceBy(time, TiltDeviceConnectionService.IdleTimeout + TimeSpan.FromMinutes(5));

        void OnPropertyChanged(object s, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(TiltDeviceConnectionService.IsOperationActive) && !service.IsOperationActive) {
                time.Advance(TimeSpan.Zero);
            }
        }
        service.PropertyChanged += OnPropertyChanged;
        try {
            token.Dispose();
        } finally {
            service.PropertyChanged -= OnPropertyChanged;
        }

        Assert.That(service.IdleDisconnectPending, Is.False,
            "a tick landing exactly as the operation ends must not see stale activity and arm a countdown");
    }

    // Regression lock (fix #1): the arm check-and-set must be atomic, or concurrent ticks could each arm a
    // countdown. FakeTiltDeviceTimeSource.Advance is single-threaded, so this fires the SAME Tick event from
    // many threads at a virtual time already past the idle threshold.
    private sealed class ConcurrentTickTimeSource : ITiltDeviceTimeSource {
        public DateTimeOffset UtcNow { get; set; }
        public event EventHandler Tick;
        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
        public void Dispose() {
        }
    }

    [Test]
    public async Task Idle_ConcurrentTicksPastThreshold_ArmExactlyOneCountdown() {
        var profile = Substitute.For<IProfileService>();
        var options = Substitute.For<ITiltAdapterOptions>();
        var time = new ConcurrentTickTimeSource { UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var service = new TiltDeviceConnectionService(profile, options, (presetName, opts) => NewControllerSubstitute(), time);
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        time.UtcNow += TiltDeviceConnectionService.IdleTimeout + TimeSpan.FromSeconds(1);

        int changeCount = 0;
        service.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(TiltDeviceConnectionService.IdleDisconnectPending)) {
                Interlocked.Increment(ref changeCount);
            }
        };

        const int concurrency = 8;
        var barrier = new Barrier(concurrency);
        var tasks = new Task[concurrency];
        for (int i = 0; i < concurrency; i++) {
            tasks[i] = Task.Run(() => {
                barrier.SignalAndWait();
                time.FireTick();
            });
        }
        await Task.WhenAll(tasks);

        Assert.Multiple(() => {
            Assert.That(changeCount, Is.EqualTo(1));
            Assert.That(service.Connected, Is.True, "the grace period has not elapsed yet");
        });
    }
}
