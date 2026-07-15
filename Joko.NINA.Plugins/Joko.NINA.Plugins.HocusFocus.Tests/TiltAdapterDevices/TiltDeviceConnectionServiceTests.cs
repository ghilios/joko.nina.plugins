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

    // --- Idle tracking ------------------------------------------------------------------------------------

    [Test]
    public async Task Idle_ThirtyMinutesNoActivity_RaisesPromptExactlyOnce() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        int raiseCount = 0;
        service.IdlePromptRequested += (s, e) => raiseCount++;

        // Many small (poll-cadence) ticks past the idle threshold; polling happens on every one of them.
        for (int i = 0; i < 400; i++) { // 400 * 5s = 2000s > 1800s (30 min)
            time.Advance(TiltDeviceConnectionService.PollInterval);
        }

        Assert.That(raiseCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Idle_PollingTicksDoNotResetIdleTimer() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        bool raised = false;
        service.IdlePromptRequested += (s, e) => raised = true;

        // Advance to just under 30 minutes with a poll-cadence tick (which itself performs a poll). If
        // polling incorrectly counted as activity, the subsequent small advance would never reach the
        // (now pushed-out) idle deadline.
        time.Advance(TiltDeviceConnectionService.IdleTimeout - TimeSpan.FromSeconds(1));
        Assert.That(raised, Is.False, "Must not fire before the idle timeout elapses.");

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.That(raised, Is.True, "Must fire once idle timeout elapses, unaffected by intervening polling.");
    }

    [Test]
    public async Task KeepConnectedResetIdle_ResetsTimerAndDoesNotDisconnect() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        int raiseCount = 0;
        service.IdlePromptRequested += (s, e) => raiseCount++;

        time.Advance(TiltDeviceConnectionService.IdleTimeout);
        Assert.That(raiseCount, Is.EqualTo(1));

        service.KeepConnectedResetIdle();

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(raiseCount, Is.EqualTo(1), "Must not immediately re-raise after being kept connected.");

        time.Advance(TiltDeviceConnectionService.IdleTimeout);
        Assert.That(raiseCount, Is.EqualTo(2), "Must re-arm and fire again after a full new idle window.");

        Assert.That(service.Connected, Is.True, "Keep-connected must not disconnect.");
    }

    [Test]
    public async Task ConfirmIdleDisconnectAsync_Disconnects() {
        var (service, _, time, controllers) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        time.Advance(TiltDeviceConnectionService.IdleTimeout);
        Assert.That(service.Connected, Is.True, "The prompt firing must not itself disconnect.");

        await service.ConfirmIdleDisconnectAsync();

        Assert.That(service.Connected, Is.False);
        await controllers[0].Received(1).DisconnectAsync(Arg.Any<CancellationToken>());
    }

    // Regression lock (fix #5): T11's hands-off calibration run holds an operation lease for a whole
    // multi-command plan that can legitimately exceed 30 minutes -- the idle prompt must not fire while it
    // is held, however far past the threshold virtual time advances.
    [Test]
    public async Task Idle_SuppressedWhileOperationActive_EvenWellPastThreshold() {
        var (service, _, time, _) = Build();
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        var token = service.TryBeginOperation("hands-off calibration");
        Assert.That(token, Is.Not.Null);

        bool raised = false;
        service.IdlePromptRequested += (s, e) => raised = true;

        time.Advance(TiltDeviceConnectionService.IdleTimeout + TimeSpan.FromMinutes(10));
        Assert.That(raised, Is.False, "Idle prompt must be suppressed for the entire duration of an active operation lease.");

        // EndOperation() itself records activity, so releasing the lease resets the idle clock -- a small
        // subsequent advance must not immediately fire either.
        token.Dispose();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(raised, Is.False, "Releasing the lease resets the idle timer; must not fire immediately.");

        // But a full new idle window after release must still fire normally -- suppression must not be sticky.
        time.Advance(TiltDeviceConnectionService.IdleTimeout);
        Assert.That(raised, Is.True, "Must resume firing normally once the lease is released and a full idle window elapses.");
    }

    // Regression lock (fix #1): the idle-prompt check-and-set must be atomic. FakeTiltDeviceTimeSource.Advance
    // is single-threaded, so it cannot itself reproduce the race (a real System.Threading.Timer can re-enter
    // its callback on another pool thread if a previous tick hasn't returned) -- this test instead fires the
    // SAME Tick event concurrently from many threads once virtual time is already past the idle threshold,
    // which exercises the exact TOCTOU window (concurrent CheckIdle invocations racing the outstanding-flag
    // check-and-set) that the fix guards.
    private sealed class ConcurrentTickTimeSource : ITiltDeviceTimeSource {
        public DateTimeOffset UtcNow { get; set; }
        public event EventHandler Tick;
        public void FireTick() => Tick?.Invoke(this, EventArgs.Empty);
        public void Dispose() {
        }
    }

    [Test]
    public async Task Idle_ConcurrentTicksPastThreshold_RaisesPromptExactlyOnce() {
        var profile = Substitute.For<IProfileService>();
        var options = Substitute.For<ITiltAdapterOptions>();
        var time = new ConcurrentTickTimeSource { UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var service = new TiltDeviceConnectionService(profile, options, (presetName, opts) => NewControllerSubstitute(), time);
        await service.ConnectAsync("preset", "COM3", CancellationToken.None);

        time.UtcNow += TiltDeviceConnectionService.IdleTimeout + TimeSpan.FromSeconds(1);

        int raiseCount = 0;
        service.IdlePromptRequested += (s, e) => Interlocked.Increment(ref raiseCount);

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

        Assert.That(raiseCount, Is.EqualTo(1));
    }
}
