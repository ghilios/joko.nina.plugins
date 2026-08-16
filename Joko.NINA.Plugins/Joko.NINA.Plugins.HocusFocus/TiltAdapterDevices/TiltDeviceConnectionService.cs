#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices {

    /// <summary>
    /// Minimal injectable time abstraction for <see cref="TiltDeviceConnectionService"/>'s position-polling
    /// and 30-minute idle-disconnect timers. The service never reads <see cref="DateTime.Now"/> or starts a
    /// real <see cref="System.Threading.Timer"/> directly — it only ever reacts to <see cref="Tick"/> and
    /// compares wall-clock deltas against <see cref="UtcNow"/>. That makes cadence a property of the time
    /// source, not the service: production uses <see cref="SystemTiltDeviceTimeSource"/> (a real timer
    /// firing every <see cref="TiltDeviceConnectionService.PollInterval"/>), while tests substitute a fake
    /// with a settable clock that fires <see cref="Tick"/> synchronously on demand — so advancing virtual
    /// time by 5 seconds or 30 minutes deterministically drives a poll or the idle prompt without a single
    /// real-time wait.
    /// </summary>
    public interface ITiltDeviceTimeSource : IDisposable {

        /// <summary>Current time. Compared against stored timestamps (last poll, last user activity) — never assumed to advance by any particular amount between two reads.</summary>
        DateTimeOffset UtcNow { get; }

        /// <summary>Fires whenever the service should re-evaluate whether a poll or the idle prompt is due.</summary>
        event EventHandler Tick;
    }

    /// <summary>
    /// Production <see cref="ITiltDeviceTimeSource"/>: the real UTC clock plus a real
    /// <see cref="System.Threading.Timer"/> ticking once per <paramref name="period"/>. Because
    /// <see cref="TiltDeviceConnectionService"/> computes due-ness from wall-clock deltas rather than tick
    /// counts, the exact period only bounds latency (how stale a displayed position or the idle prompt can
    /// be) — it is not itself the unit callers reason about.
    /// </summary>
    internal sealed class SystemTiltDeviceTimeSource : ITiltDeviceTimeSource {
        private readonly Timer timer;

        public SystemTiltDeviceTimeSource(TimeSpan period) {
            timer = new Timer(_ => Tick?.Invoke(this, EventArgs.Empty), null, period, period);
        }

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public event EventHandler Tick;

        public void Dispose() => timer.Dispose();
    }

    /// <summary>
    /// Shared singleton (wired as <c>HocusFocusPlugin.TiltDeviceConnectionService</c>) owning the single
    /// live connection to a motorized tilt-adapter device (e.g. the ASG EAT). Coordinates every consumer
    /// that needs hardware access — the wizard's hands-off calibration (T11) and the inspector's Automatic
    /// Adjustment (T14) — so they can never command the device at the same time, and owns the "is anyone
    /// home" position-polling and 30-minute idle-disconnect prompt described in the design doc's user
    /// decisions #6 and #7.
    ///
    /// <para><b>Concurrency model.</b> A single <see cref="SemaphoreSlim"/> (<c>hardwareLock</c>) guards
    /// every actual touch of the device: <see cref="ConnectAsync"/>, <see cref="DisconnectAsync"/>, and the
    /// timer-driven position poll all hold it for the duration of their device call. An exclusive
    /// <see cref="TryBeginOperation"/> lease (see below) also claims this same lock for its entire
    /// lifetime — that is what makes it a real cross-command lease rather than a per-command semaphore:
    /// while a wizard calibration or an inspector adjustment plan holds the lease, every other hardware
    /// touch (poll ticks, a stray Connect/Disconnect, a forced profile-change disconnect) is blocked out
    /// until the lease is disposed. Poll ticks use a non-blocking <c>Wait(0)</c> and simply skip the tick if
    /// the lock is busy (never queue up behind a move); Connect/Disconnect/the profile-change forced
    /// disconnect use an async <c>WaitAsync</c> so they patiently wait their turn without blocking a
    /// thread.</para>
    ///
    /// <para><b>Connect-while-connected.</b> <see cref="ConnectAsync"/> always tears down any existing
    /// controller/session first (disconnect-then-reconnect), then connects fresh under the newly requested
    /// preset/port. This lets "Connect" double as "reconnect" (e.g. the user picked a different COM port or
    /// device preset) without requiring a separate Disconnect click first, and it is always safe: the old
    /// controller is cleanly disconnected before the new one is ever touched.</para>
    /// </summary>
    public class TiltDeviceConnectionService : BaseINPC {

        /// <summary>Position-poll cadence (design doc user decision #6: "refreshed by polling cp" at a constant cadence).</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        /// <summary>Idle threshold, after which the auto-disconnect countdown arms.</summary>
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

        /// <summary>
        /// How long the on-screen countdown runs before the idle disconnect actually fires. The user has already
        /// had <see cref="IdleTimeout"/>; this is a courtesy decline window, not the decision window, and the cost
        /// of a wrong disconnect is one Connect click (the adapter keeps its motor counters across sessions).
        /// Deliberately NOT configurable: a short value would faithfully recreate the yanked-without-warning
        /// failure this design exists to remove.
        /// </summary>
        public static readonly TimeSpan IdleDisconnectGrace = TimeSpan.FromSeconds(10);

        private readonly IProfileService profileService;
        private readonly ITiltAdapterOptions options;
        private readonly Func<string, ITiltAdapterOptions, ITiltMotionController> controllerFactory;
        // Builds the controller used when the caller selects the SimulatedTiltPort sentinel instead of a real COM
        // port (the plugin wires this to an EatTiltMotionController over a SimulatedEatTransport). Null when no
        // simulator is available (e.g. a headless/test host that never wires one) -- the sentinel then simply
        // falls through to the serial registry, so this stays inert for every existing caller.
        private readonly Func<ITiltMotionController> simulatedControllerFactory;
        private readonly ITiltDeviceTimeSource timeSource;

        // Guards every actual device touch (connect/disconnect/poll) AND is held for the entire lifetime of
        // an active TryBeginOperation lease — see the class doc's "Concurrency model" section.
        private readonly SemaphoreSlim hardwareLock = new SemaphoreSlim(1, 1);

        // Guards the isOperationActive flag itself (TryBeginOperation/EndOperation are called from whatever
        // thread the caller is on — typically the UI thread — and must never race each other).
        private readonly object operationGate = new object();
        private bool isOperationActive;

        // lastActivityUtc/lastPollUtc are written on caller threads (Connect/TryBeginOperation/EndOperation,
        // typically the UI thread) and read on the timer thread (TimeSource_Tick) without any lock guarding
        // BOTH sides -- a plain DateTimeOffset field is > 8 bytes (a DateTime plus an offset), so a torn
        // cross-thread read is possible. Stored as UTC ticks (a single 64-bit value) and accessed exclusively
        // through the LastActivityUtc/LastPollUtc properties below via Interlocked, which guarantees an
        // atomic, non-torn 64-bit read/write on every platform (unlike a plain `long` field access, which is
        // only guaranteed atomic on 64-bit runtimes).
        private long lastActivityUtcTicks;
        private long lastPollUtcTicks = DateTimeOffset.MinValue.UtcTicks;
        // Ticks of the instant the idle disconnect fires, or MinValue when no countdown is armed. Same
        // Interlocked treatment and for the same reason as the two above: written on the timer thread and on
        // caller threads, read on both.
        private long idleDisconnectDeadlineUtcTicks = DateTimeOffset.MinValue.UtcTicks;

        // Guards against the idle-prompt TOCTOU double-fire: the underlying System.Threading.Timer does NOT
        // serialize its callback (TimeSource_Tick can be re-entered on a different pool thread if a previous
        // tick's callback -- including this field's own check-and-set -- hasn't returned yet). 0 = not
        // outstanding, 1 = outstanding. Always mutated via Interlocked so the check-and-set in CheckIdle is
        // atomic; see CheckIdle for the full race this defends against.
        private int idlePromptOutstandingFlag;

        private DateTimeOffset LastActivityUtc {
            get => new DateTimeOffset(Interlocked.Read(ref lastActivityUtcTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref lastActivityUtcTicks, value.UtcTicks);
        }

        private DateTimeOffset LastPollUtc {
            get => new DateTimeOffset(Interlocked.Read(ref lastPollUtcTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref lastPollUtcTicks, value.UtcTicks);
        }

        private DateTimeOffset IdleDisconnectDeadlineUtc {
            get => new DateTimeOffset(Interlocked.Read(ref idleDisconnectDeadlineUtcTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref idleDisconnectDeadlineUtcTicks, value.UtcTicks);
        }

        /// <summary>True while the auto-disconnect countdown is running and the banner should be shown.</summary>
        public bool IdleDisconnectPending => Interlocked.CompareExchange(ref idlePromptOutstandingFlag, 0, 0) != 0;

        /// <summary>
        /// Time left before the idle disconnect fires, clamped at zero. Computed from the SAME clock the fire
        /// check uses, so the number on screen and the moment of disconnect cannot disagree.
        /// </summary>
        public TimeSpan IdleDisconnectRemaining {
            get {
                if (!IdleDisconnectPending) {
                    return TimeSpan.Zero;
                }
                var remaining = IdleDisconnectDeadlineUtc - timeSource.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        /// <summary>When the device was last disconnected by the idle timer, for the reconnect-side status line. Null otherwise.</summary>
        public DateTimeOffset? LastIdleAutoDisconnectUtc { get; private set; }

        private IReadOnlyList<int> currentPositions = Array.Empty<int>();

        public TiltDeviceConnectionService(IProfileService profileService, ITiltAdapterOptions options,
                Func<ITiltMotionController> simulatedControllerFactory = null)
            : this(profileService, options, TiltMotionControllerRegistry.Create, new SystemTiltDeviceTimeSource(PollInterval), simulatedControllerFactory) {
        }

        /// <summary>Test seam: injects the controller factory (so tests substitute an <see cref="ITiltMotionController"/> instead of the real, not-yet-implemented EAT factory) and the time source (so tests drive virtual time deterministically). The optional <paramref name="simulatedControllerFactory"/> is the branch taken when the caller selects the <see cref="SimulatedTiltPort"/> sentinel.</summary>
        internal TiltDeviceConnectionService(
            IProfileService profileService,
            ITiltAdapterOptions options,
            Func<string, ITiltAdapterOptions, ITiltMotionController> controllerFactory,
            ITiltDeviceTimeSource timeSource,
            Func<ITiltMotionController> simulatedControllerFactory = null) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));
            this.simulatedControllerFactory = simulatedControllerFactory; // nullable: sentinel routing is inert when unset
            this.timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));

            LastActivityUtc = this.timeSource.UtcNow;
            this.profileService.ProfileChanged += ProfileService_ProfileChanged;
            this.timeSource.Tick += TimeSource_Tick;
        }

        public ITiltMotionController Controller { get; private set; }

        public bool Connected { get; private set; }

        /// <summary>True while an exclusive operation lease (see <see cref="TryBeginOperation"/>) is held.</summary>
        public bool IsOperationActive => isOperationActive;

        /// <summary>The <c>name</c> passed to the currently-active <see cref="TryBeginOperation"/> call, or null when no operation is active. For UI status text.</summary>
        public string CurrentOperationName { get; private set; }

        /// <summary>Per-motor positions from the most recent successful poll, in DEVICE motor order (see <see cref="TiltDevicePositions"/>). Empty/stale whenever <see cref="PositionsKnown"/> is false.</summary>
        public IReadOnlyList<int> CurrentPositions => currentPositions;

        /// <summary>False when the last poll failed, threw, or reported <see cref="TiltDevicePositions.Known"/> == false — the UI should show "unknown" rather than trusting <see cref="CurrentPositions"/>.</summary>
        public bool PositionsKnown { get; private set; }

        /// <summary>
        /// Test-observability hook, mirroring <see cref="LastForcedDisconnectTask"/>: the task started by the most
        /// recent idle auto-disconnect. Production never awaits it (the timer callback is necessarily
        /// fire-and-forget); tests await it so the disconnect completes deterministically under the fake clock.
        /// </summary>
        internal Task LastIdleAutoDisconnectTask { get; private set; }

        /// <summary>
        /// Test-observability hook: the <see cref="Task"/> started by the most recent
        /// <see cref="IProfileService.ProfileChanged"/>-triggered forced disconnect. Production code never
        /// awaits this (NINA's <c>ProfileChanged</c> is a synchronous <see cref="EventHandler"/>, so the
        /// forced disconnect is necessarily fire-and-forget there) — it exists purely so tests can await
        /// deterministic completion of the forced disconnect instead of racing a background continuation.
        /// </summary>
        internal Task LastForcedDisconnectTask { get; private set; }

        /// <summary>
        /// Connects to the given device preset over the given serial port. If already connected, the
        /// existing controller is disconnected first (see the class doc's "Connect-while-connected"
        /// section) — this method always leaves the service connected to exactly the requested
        /// preset/port on success, and disconnected on failure. Waits for any active
        /// <see cref="TryBeginOperation"/> lease to be released before proceeding.
        /// </summary>
        public async Task ConnectAsync(string presetName, string portName, CancellationToken ct) {
            if (string.IsNullOrEmpty(presetName)) {
                throw new ArgumentException("Device preset name must not be empty.", nameof(presetName));
            }
            if (string.IsNullOrEmpty(portName)) {
                throw new ArgumentException("Port name must not be empty.", nameof(portName));
            }

            await hardwareLock.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (Connected) {
                    await DisconnectCoreAsync().ConfigureAwait(false);
                }

                // The "auto-disconnected at HH:mm" note exists to explain why the device is not connected; once
                // the user reconnects it has served its purpose.
                if (LastIdleAutoDisconnectUtc.HasValue) {
                    LastIdleAutoDisconnectUtc = null;
                    RaisePropertyChanged(nameof(LastIdleAutoDisconnectUtc));
                }

                // The SimulatedTiltPort sentinel routes to the simulated controller (camera simulator) instead of
                // the serial registry; every real COM port goes through the registry unchanged. If no simulator
                // factory was wired (e.g. a headless host), the sentinel falls through to the registry.
                var useSimulator = SimulatedTiltPort.IsSimulator(portName) && simulatedControllerFactory != null;
                var controller = (useSimulator ? simulatedControllerFactory() : controllerFactory(presetName, options))
                    ?? throw new InvalidOperationException($"Controller factory returned null for preset \"{presetName}\".");
                try {
                    await controller.ConnectAsync(portName, ct).ConfigureAwait(false);
                } catch {
                    // Controller leak / port-in-use lockout guard: the controller's own ConnectAsync may have
                    // already opened the underlying port before failing on a later step (e.g. a 'cp'
                    // handshake) -- best-effort disconnect it so the COM port isn't left open behind us, or
                    // the NEXT connect attempt fails with "port in use". Secondary disconnect errors are
                    // swallowed; the original connect failure is what the caller needs to see.
                    try {
                        await controller.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                    } catch (Exception disconnectEx) {
                        Logger.Error(disconnectEx, "Error disconnecting tilt device controller after a failed connect attempt (ignored).");
                    }
                    throw;
                }

                Controller = controller;
                // Record activity BEFORE flipping Connected -- both still inside hardwareLock. A timer tick
                // racing this method must never be able to observe Connected == true together with a stale
                // (possibly > 30-min-old) LastActivityUtc, which would fire the idle prompt immediately after
                // a fresh connect.
                RecordUserActivity();
                SetConnected(true);
                SetPositionsUnknown(); // Drop the previous connection's counters before republishing below.
                // A controller's own ConnectAsync queries positions as part of connecting, so publish what it
                // already knows instead of showing "unknown" until the first poll tick. No-op (leaving
                // "unknown") when that query could not confirm them.
                PublishControllerPositions();
                RaisePropertyChanged(nameof(Controller));
            } finally {
                hardwareLock.Release();
            }
        }

        /// <summary>Disconnects (no-op if not already connected). Waits for any active <see cref="TryBeginOperation"/> lease, in-flight poll, or in-flight Connect to finish first — never yanks the port mid-command.</summary>
        public async Task DisconnectAsync() {
            await hardwareLock.WaitAsync().ConfigureAwait(false);
            try {
                await DisconnectCoreAsync().ConfigureAwait(false);
            } finally {
                hardwareLock.Release();
            }
        }

        // Assumes hardwareLock is already held by the caller. Never throws for a well-behaved controller;
        // if the controller's own DisconnectAsync throws (e.g. SerialPortClosedException from an unplugged
        // device), the connection state is still cleared — a failed disconnect must not leave the service
        // reporting a connection that's actually gone.
        private async Task DisconnectCoreAsync() {
            var controller = Controller;
            if (controller == null) {
                SetConnected(false);
                return;
            }
            try {
                await controller.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex, "Error disconnecting tilt device controller; clearing connection state regardless.");
            } finally {
                Controller = null;
                SetConnected(false);
                SetPositionsUnknown();
                LastPollUtc = DateTimeOffset.MinValue; // next connect polls promptly rather than waiting out a stale interval
                ClearIdleCountdown();
                RaisePropertyChanged(nameof(Controller));
            }
        }

        /// <summary>
        /// Publishes the connected controller's latest known per-motor counters (see
        /// <see cref="ITiltMotionController.LastKnownPositions"/>) to <see cref="CurrentPositions"/> /
        /// <see cref="PositionsKnown"/> immediately, with NO device I/O.
        ///
        /// <para>Position polling is suspended for the entire lifetime of a <see cref="TryBeginOperation"/>
        /// lease — which spans a whole calibration run or adjustment plan, including its confirming analysis
        /// and any revert. Without this, every panel's counters would sit frozen at their pre-operation values
        /// for minutes, and then silently jump once the lease was released. The lease holder therefore calls
        /// this after each executed move: because a move response already carries the device's fresh counters,
        /// the controller has them in hand and no extra round trip (nor its failure modes) is involved.</para>
        ///
        /// <para>Returns the published positions, or null when the controller has no confirmed positions — in
        /// which case the previously published values are deliberately LEFT in place rather than flipped to
        /// "unknown", so a mid-plan gap does not blank a display that was correct a moment ago.</para>
        /// </summary>
        public IReadOnlyList<int> PublishControllerPositions() {
            var controller = Controller;
            if (controller == null) {
                return null;
            }
            var positions = controller.LastKnownPositions;
            if (positions == null || !positions.Known || positions.PerMotorSteps.Count < 4) {
                // Worth a warning, not silence: this is exactly the state in which a panel keeps showing
                // counters that no longer match the hardware, which is indistinguishable from "nothing moved".
                Logger.Warning("Tilt device positions could not be refreshed from the controller (none confirmed yet); displayed counters may be stale.");
                return null;
            }
            ApplyPositions(positions);
            return currentPositions;
        }

        /// <summary>
        /// Exclusive lease for a whole multi-command plan (a wizard calibration run or an inspector
        /// Automatic Adjustment plan): while held, position polling is paused and any Connect/Disconnect
        /// call (including a profile-change forced disconnect) blocks until the lease is disposed. Returns
        /// null if another lease is already active OR a Connect/Disconnect/poll is currently in flight
        /// (both cases mean "device busy" to the caller — this method never blocks waiting for either).
        /// Disposing the returned token ends the lease, records user activity (resets the idle timer), and
        /// resumes position polling.
        /// </summary>
        public IDisposable TryBeginOperation(string name) {
            lock (operationGate) {
                if (isOperationActive) {
                    return null;
                }
                if (!hardwareLock.Wait(0)) {
                    return null;
                }
                isOperationActive = true;
            }

            CurrentOperationName = name;
            RaisePropertyChanged(nameof(IsOperationActive));
            RaisePropertyChanged(nameof(CurrentOperationName));
            RecordUserActivity();
            return new OperationToken(this);
        }

        private void EndOperation() {
            hardwareLock.Release();
            // Record activity BEFORE clearing isOperationActive (mirrors the ConnectAsync ordering fix,
            // which records activity before flipping Connected) -- a timer tick racing this method must
            // never be able to observe isOperationActive == false together with a stale (possibly held past
            // the 30-minute idle window, e.g. a long calibration run) LastActivityUtc, which would fire a
            // spurious idle-disconnect prompt right as the operation ends. While isOperationActive is still
            // true, CheckIdle returns early regardless of LastActivityUtc, so by the time the flag actually
            // flips false below, activity is already fresh.
            RecordUserActivity();
            lock (operationGate) {
                isOperationActive = false;
            }
            CurrentOperationName = null;
            RaisePropertyChanged(nameof(IsOperationActive));
            RaisePropertyChanged(nameof(CurrentOperationName));
            // No explicit "resume" call needed: the next timer tick sees Connected && !IsOperationActive
            // and polls again on its own (see TryPollTick).
        }

        /// <summary>
        /// The banner's "Stay connected" button: cancels the countdown, resets the idle timer, and re-arms the
        /// next window. Does not touch the connection.
        /// </summary>
        public void KeepConnectedResetIdle() {
            ClearIdleCountdown();
            RecordUserActivity();
        }

        // User-initiated activity only (Connect, TryBeginOperation, and lease disposal, i.e. a completed
        // move/plan/calibration step) — deliberately NOT called from the poll path, per the design doc:
        // "Polling does NOT count as activity."
        //
        // Touching the device IS declining the auto-disconnect: moving a motor or starting a calibration takes
        // an operation lease, which lands here, so the countdown vanishes without the user also having to find
        // the banner's button.
        private void RecordUserActivity() {
            LastActivityUtc = timeSource.UtcNow;
            ClearIdleCountdown();
        }

        private void ClearIdleCountdown() {
            if (Interlocked.Exchange(ref idlePromptOutstandingFlag, 0) == 0) {
                return;
            }
            IdleDisconnectDeadlineUtc = DateTimeOffset.MinValue;
            RaiseIdleCountdownChanged();
        }

        private void RaiseIdleCountdownChanged() {
            RaisePropertyChanged(nameof(IdleDisconnectPending));
            RaisePropertyChanged(nameof(IdleDisconnectRemaining));
        }

        private void TimeSource_Tick(object sender, EventArgs e) {
            var now = timeSource.UtcNow;
            CheckIdle(now);
            TryPollTick(now);
        }

        private void CheckIdle(DateTimeOffset now) {
            if (!Connected || isOperationActive) {
                // Suppressed while an operation lease is held: T11's hands-off calibration run can hold the
                // lease past the 30-minute idle window. TryBeginOperation/EndOperation already record
                // activity on begin/end, so the idle clock naturally resumes once the lease is released.
                return;
            }
            if (now - LastActivityUtc < IdleTimeout) {
                return;
            }
            // Atomic check-and-set (TOCTOU fix): the underlying System.Threading.Timer does not serialize its
            // callback, so TimeSource_Tick (and therefore this method) can be re-entered concurrently on
            // another pool thread if a previous tick's callback hasn't returned yet. Interlocked.CompareExchange
            // ensures only ONE concurrent caller ever transitions the flag from "not outstanding" (0) to
            // "outstanding" (1) and actually raises the event -- design doc user decision #7 requires the
            // prompt fire EXACTLY once per idle window.
            if (Interlocked.CompareExchange(ref idlePromptOutstandingFlag, 1, 0) != 0) {
                // Already counting down. Fire once the grace period is up -- re-validating first, because the
                // deadline was set up to a minute ago and the world may have moved since.
                if (now >= IdleDisconnectDeadlineUtc) {
                    FireIdleDisconnect(now);
                }
                return;
            }
            IdleDisconnectDeadlineUtc = now + IdleDisconnectGrace;
            Logger.Info($"Tilt adapter idle for {IdleTimeout.TotalMinutes:0} minutes; disconnecting in {IdleDisconnectGrace.TotalSeconds:0}s unless cancelled.");
            RaiseIdleCountdownChanged();
        }

        // Re-validated against the same conditions CheckIdle used to arm the countdown. Activity recorded inside
        // the race window between the deadline passing and this tick therefore still vetoes the disconnect, which
        // is the belt to RecordUserActivity's suspenders.
        private void FireIdleDisconnect(DateTimeOffset now) {
            if (!Connected || isOperationActive || now - LastActivityUtc < IdleTimeout) {
                ClearIdleCountdown();
                return;
            }
            Interlocked.Exchange(ref idlePromptOutstandingFlag, 0);
            IdleDisconnectDeadlineUtc = DateTimeOffset.MinValue;
            LastIdleAutoDisconnectUtc = now;
            Logger.Info("Tilt adapter disconnected automatically after the idle countdown expired.");
            RaisePropertyChanged(nameof(LastIdleAutoDisconnectUtc));
            RaiseIdleCountdownChanged();
            LastIdleAutoDisconnectTask = DisconnectAsync();
        }

        private void TryPollTick(DateTimeOffset now) {
            if (!Connected || isOperationActive) {
                return; // Paused while an operation lease is held; resumes automatically once it's released.
            }
            if (now - LastPollUtc < PollInterval) {
                return;
            }
            if (!hardwareLock.Wait(0)) {
                return; // Connect/Disconnect/an operation just claimed the lock; try again next tick.
            }
            LastPollUtc = now;
            var controllerSnapshot = Controller;
            _ = RunPollAsync(controllerSnapshot);
        }

        private async Task RunPollAsync(ITiltMotionController controllerSnapshot) {
            try {
                if (controllerSnapshot == null) {
                    SetPositionsUnknown();
                    return;
                }
                TiltDevicePositions positions;
                try {
                    positions = await controllerSnapshot.QueryPositionsAsync(CancellationToken.None).ConfigureAwait(false);
                } catch (Exception ex) {
                    // Expected pre-T15 (the cp response format is unparseable): swallow and surface as "unknown" rather than crashing the poll loop.
                    Logger.Error(ex, "Tilt device position poll failed; showing positions as unknown.");
                    SetPositionsUnknown();
                    return;
                }
                ApplyPositions(positions);
            } finally {
                hardwareLock.Release();
            }
        }

        private void ApplyPositions(TiltDevicePositions positions) {
            if (positions == null || !positions.Known) {
                SetPositionsUnknown();
                return;
            }
            currentPositions = new ReadOnlyCollection<int>(new List<int>(positions.PerMotorSteps));
            PositionsKnown = true;
            RaisePropertyChanged(nameof(CurrentPositions));
            RaisePropertyChanged(nameof(PositionsKnown));
        }

        private void SetConnected(bool value) {
            if (Connected == value) {
                return;
            }
            Connected = value;
            RaisePropertyChanged(nameof(Connected));
        }

        private void SetPositionsUnknown() {
            if (!PositionsKnown && currentPositions.Count == 0) {
                return;
            }
            currentPositions = Array.Empty<int>();
            PositionsKnown = false;
            RaisePropertyChanged(nameof(CurrentPositions));
            RaisePropertyChanged(nameof(PositionsKnown));
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            // NINA's ProfileChanged is a synchronous EventHandler; the forced disconnect must wait
            // (possibly for a long-running operation lease) so it cannot run inline here. Stored for test
            // observability — see LastForcedDisconnectTask's doc comment.
            LastForcedDisconnectTask = ForceDisconnectForProfileChangeAsync();
        }

        private async Task ForceDisconnectForProfileChangeAsync() {
            try {
                // DisconnectAsync awaits hardwareLock, which is held for the entire duration of an active
                // TryBeginOperation lease (and of any in-flight Connect/poll) — so this naturally waits for
                // the in-flight command/operation to finish before yanking the port, per the design doc.
                await DisconnectAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                Logger.Error(ex, "Error force-disconnecting tilt device on profile change.");
            }
        }

        private sealed class OperationToken : IDisposable {
            private TiltDeviceConnectionService owner;

            public OperationToken(TiltDeviceConnectionService owner) {
                this.owner = owner;
            }

            public void Dispose() {
                var o = Interlocked.Exchange(ref owner, null);
                o?.EndOperation();
            }
        }
    }
}
