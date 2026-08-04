#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.SerialCommunication;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat {

    /// <summary>
    /// Immutable, raw, un-interpreted result of one request/response exchange with the ASG EAT over
    /// <see cref="IEatTransport"/>. This is deliberately a dumb bag of bytes-as-lines: it carries exactly
    /// what came back over the wire and nothing more. Interpreting <see cref="Lines"/> (ack recognition,
    /// <c>cp</c> position extraction, etc.) is <c>EatResponses</c>'s job, not this type's -- keeping the two
    /// concerns separate is what lets <c>EatResponses</c> be re-tuned in plan task T15 without touching the
    /// transport.
    /// </summary>
    public sealed class EatRawExchange {

        public EatRawExchange(string command, IReadOnlyList<string> lines, bool timedOut) {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Lines = new ReadOnlyCollection<string>((lines ?? Array.Empty<string>()).ToArray());
            TimedOut = timedOut;
        }

        /// <summary>The exact command string that was sent (without the line terminator).</summary>
        public string Command { get; }

        /// <summary>
        /// Raw response lines, verbatim, in the order received. Populated even when <see cref="TimedOut"/>
        /// is true (whatever arrived before the overall timeout elapsed is still useful for diagnosing a
        /// live capture transcript).
        /// </summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>
        /// True if the overall exchange timeout elapsed before the response was judged complete (see
        /// <see cref="IEatTransport.SendAsync"/>'s quiet-period/timeout race). False means the read loop
        /// settled on a quiet period after receiving at least one line -- NOT that the response was
        /// necessarily well-formed or successful; that judgment belongs to <c>EatResponses</c>.
        /// </summary>
        public bool TimedOut { get; }
    }

    /// <summary>
    /// Transport abstraction for talking to a connected ASG EAT over serial. Deliberately narrow -- three
    /// members plus a connectedness flag -- so <c>EatTiltMotionController</c> (plan task T7) can be built
    /// and tested against a fake without any real I/O. The production implementation is
    /// <see cref="EatSerialTransport"/>.
    /// </summary>
    public interface IEatTransport {

        /// <summary>True after a successful <see cref="OpenAsync"/> until <see cref="Close"/>.</summary>
        bool IsOpen { get; }

        /// <summary>Opens the serial connection to the device on <paramref name="portName"/> (e.g. "COM3").</summary>
        Task OpenAsync(string portName, CancellationToken ct);

        /// <summary>Closes the connection. Idempotent -- safe to call when already closed or never opened.</summary>
        void Close();

        /// <summary>
        /// Sends <paramref name="command"/> (without a line terminator -- the transport appends one) and
        /// collects the response. See <see cref="EatSerialTransport"/>'s class remarks for the read-loop
        /// contract. Never throws for a plain read timeout (returns <see cref="EatRawExchange.TimedOut"/>
        /// = true instead); a closed port propagates <see cref="SerialPortClosedException"/>.
        /// </summary>
        Task<EatRawExchange> SendAsync(string command, TimeSpan timeout, CancellationToken ct);
    }

    /// <summary>
    /// Custom serial transport for the ASG EAT -- THE SEAM FILE (together with <c>EatResponses.cs</c>) that
    /// contains every assumption about the device's response wire format, each marked
    /// <c>// LIVE-CAPTURE:</c> for the plan's T15 hardware session. Everything outside these two files must
    /// be contract-tested against this transport's documented behavior and must not need to change once
    /// T15 resolves the unknowns.
    ///
    /// Deliberately does NOT subclass <c>SerialSdk</c>: that base class does one fixed-timeout
    /// <c>ReadLine</c> (500 ms default, 2 retries) per command and silently swallows write timeouts --
    /// unusable for the EAT's 5-10 s moves under XON/XOFF flow control, where a write can legitimately
    /// block for a long time and a write timeout must be a hard, visible failure. See the design doc's
    /// "Key reuse" note.
    ///
    /// <para><b>Read-loop design: an exchange ends ONLY at a terminal sentinel.</b> After writing the command
    /// (+ line terminator), lines are collected by repeatedly calling the underlying port's blocking
    /// <c>ReadLine()</c> (each call bounded by the port's read timeout, <see cref="QuietWindow"/>) until the
    /// device's own end-of-response marker arrives (<see cref="MoveCompleteSentinel"/> /
    /// <see cref="QueryCompleteSentinel"/>, both confirmed against firmware 7.1.0), or the caller's overall
    /// budget is exhausted -- which means the response is INCOMPLETE (<c>TimedOut = true</c>), never "finished
    /// without a sentinel".</para>
    ///
    /// <para><b>A quiet gap is not end-of-response.</b> This transport used to treat "at least one line, then
    /// <see cref="QuietWindow"/> of silence" as completion. On a loaded machine the device pauses mid-dump for
    /// longer than that, and a real session shows the consequence: a <c>cp</c> response abandoned after 3 of
    /// its ~22 lines, its unread remainder becoming the head of the next command's response, and every exchange
    /// for the following hour running exactly one behind -- position queries returning a previous dump's
    /// counters (so displays froze), and moves "acknowledged" by an earlier command's sentinel (so a move that
    /// never completed was journaled as successful). It survived until the port was closed and reopened.
    /// <see cref="DrainStaleInputAsync"/> is the second half of that fix: it clears (and reports) leftover input
    /// before each command, so a link that does fall out of step resynchronizes itself.</para>
    /// </summary>
    public sealed class EatSerialTransport : IEatTransport, IDisposable {

        /// <summary>ASG EAT serial parameters (plan's "Device facts" section) -- NOT a LIVE-CAPTURE unknown.</summary>
        private const int BaudRate = 9600;

        private const int DataBits = 8;

        // Outgoing-command line terminator, and (via the `newLine` parameter passed to GetSerialPort) the
        // delimiter ReadLine() uses to frame INCOMING lines. Confirmed CRLF against ASG EAT firmware 7.1.0
        // (Arduino Serial.println framing) -- see docs/asg-eat-serial-protocol-design.md.
        internal const string LineTerminator = "\r\n";

        // Confirmed against ASG EAT firmware 7.1.0 (an Arduino Uno): asserting DTR on open triggers the classic
        // Arduino auto-reset, so the MCU reboots and, after a ~1.5 s bootloader delay, emits a boot banner that
        // ends in an "FW:<version>" line. DrainBootBannerAsync below waits that banner out before the first real
        // command. See docs/asg-eat-serial-protocol-design.md.
        internal const bool DefaultDtrEnable = true;

        // Maximum time to drain the reset-on-open boot banner before giving up and proceeding. Confirmed against
        // firmware 7.1.0: the MCU resets on open, is silent for ~1.5 s, then streams a banner ending ~2.5 s after
        // open with an "FW:<version>" line. DrainBootBannerAsync stops as soon as it sees that marker (or the
        // device goes quiet after the banner); this is only the safety cap.
        internal static readonly TimeSpan PostOpenSettleTimeout = TimeSpan.FromSeconds(5);

        // The banner's final line (e.g. "FW:7.1.0"); its arrival means the sketch is running and ready.
        internal const string BootBannerEndMarker = "FW:";

        // Terminal sentinels the device prints at the end of a response (confirmed, firmware 7.1.0): a move ends
        // with "***finished movement***", a query ('cp') with "***Action Processed***". The read loop returns as
        // soon as it sees either, rather than relying on the quiet-window heuristic -- robust even if the device
        // pauses mid-response (e.g. a slow multi-second move whose heartbeat gaps could otherwise look "quiet").
        internal const string MoveCompleteSentinel = "***finished movement***";
        internal const string QueryCompleteSentinel = "***Action Processed***";

        // Emitted repeatedly while motors are running (hundreds per move). Carries no information beyond "still
        // moving", so the read loop counts them and logs one summary line instead of one line each.
        internal const string MotionHeartbeat = "***moving***";

        // Cap on the resynchronizing drain that runs before a command when the input buffer is not empty (see
        // DrainStaleInputAsync). Only ever spent on a link that is already out of step.
        internal static readonly TimeSpan ResyncTimeout = TimeSpan.FromSeconds(3);

        // The "quiet window" -- the per-poll ReadLine() timeout (passed as `readTimeout` to GetSerialPort) and
        // the gap-since-last-line the read loop treats as "response complete" once at least one line has arrived
        // AND no terminal sentinel appeared (the fallback completion path). The device does NOT echo the command
        // it received (confirmed 7.1.0), so no echo-skipping is needed.
        internal static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(200);

        // NOT a wire-format unknown -- derived directly from the plan's device facts (moves take 5-10 s).
        // Sized well beyond the longest legitimate move, with slack for XON/XOFF backpressure, so a
        // legitimate slow move can never trip a write timeout. A write timeout is therefore a HARD failure
        // (see WriteCommand) -- it means the link is genuinely stuck, not just busy with a move.
        internal static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(20);

        private readonly ISerialPortProvider serialPortProvider;
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private ISerialPort serialPort;

        /// <summary>Production constructor: uses the real NINA <see cref="SerialPortProvider"/>.</summary>
        public EatSerialTransport() : this(new SerialPortProvider()) {
        }

        /// <summary>Test/DI constructor: accepts a substitute provider (and, transitively, a substitute port).</summary>
        public EatSerialTransport(ISerialPortProvider serialPortProvider) {
            this.serialPortProvider = serialPortProvider ?? throw new ArgumentNullException(nameof(serialPortProvider));
        }

        public bool IsOpen => serialPort != null;

        /// <summary>
        /// Opens the connection. Double-open guard (documented choice): if the transport is already open,
        /// this THROWS <see cref="InvalidOperationException"/> rather than silently tearing down and
        /// replacing the existing port -- a caller that wants to reconnect must <see cref="Close"/> first.
        /// This keeps "open while open" an explicit caller error instead of a silent port leak.
        /// </summary>
        public async Task OpenAsync(string portName, CancellationToken ct) {
            if (string.IsNullOrWhiteSpace(portName)) {
                throw new ArgumentException("Port name must not be null or empty.", nameof(portName));
            }
            ct.ThrowIfCancellationRequested();

            if (serialPort != null) {
                throw new InvalidOperationException(
                    "The EAT transport is already open; call Close() before opening a new connection.");
            }

            // Port config confirmed against ASG EAT firmware 7.1.0: 9600 baud, no parity, 8 data bits, 1 stop
            // bit, and NO flow control. The device is an Arduino Uno sketch with no software flow control, so
            // Handshake.None is correct -- Handshake.XOnXOff would let the PC driver inject XON/XOFF (0x11/0x13)
            // bytes into the sketch's command input. See docs/asg-eat-serial-protocol-design.md.
            var port = serialPortProvider.GetSerialPort(
                portName,
                BaudRate,
                Parity.None,
                DataBits,
                StopBits.One,
                Handshake.None,
                DefaultDtrEnable,
                LineTerminator,
                (int)QuietWindow.TotalMilliseconds,
                (int)WriteTimeout.TotalMilliseconds);

            if (port == null) {
                throw new InvalidOperationException($"Serial port provider returned no port for '{portName}'.");
            }

            try {
                // Set DtrEnable explicitly (in addition to passing it into GetSerialPort above) so the intent
                // is unambiguous and independently observable/testable, regardless of what a given provider
                // implementation does with the constructor-style parameter.
                port.DtrEnable = DefaultDtrEnable;
                port.Open();
                serialPort = port;

                // Reset-on-open (confirmed): opening asserts DTR, which resets the Arduino; drain and discard
                // its boot banner (ending in "FW:<version>") before the first real command so the banner can't
                // corrupt the first response.
                await DrainBootBannerAsync(port, ct).ConfigureAwait(false);
            } catch {
                // Half-open on failure: if ANY step above throws (DtrEnable set, Open, the boot-banner
                // drain/settle), the port must never be left open with IsOpen == true (mirrors NINA's
                // SerialSdk, which nulls its port on failure). Best-effort close the local port regardless of
                // how far it got, then rethrow the original exception unmodified.
                serialPort = null;
                CloseQuietly(port);
                throw;
            }
        }

        public void Close() {
            var port = serialPort;
            serialPort = null;
            if (port == null) {
                return;
            }
            CloseQuietly(port);
        }

        /// <summary>Best-effort <see cref="ISerialPort.Close"/> that never throws -- shared by <see cref="Close"/> and <see cref="OpenAsync"/>'s failure path.</summary>
        private static void CloseQuietly(ISerialPort port) {
            try {
                port.Close();
            } catch (Exception ex) {
                // Best-effort: the port is considered closed regardless (the serialPort field is already
                // nulled by the caller).
                Logger.Error("EAT transport: error while closing the serial port (ignored).", ex);
            }
        }

        /// <summary>Disposes the internal send-serialization semaphore. Does NOT close the port -- call <see cref="Close"/> for that.</summary>
        public void Dispose() {
            sendGate.Dispose();
        }

        public async Task<EatRawExchange> SendAsync(string command, TimeSpan timeout, CancellationToken ct) {
            if (string.IsNullOrEmpty(command)) {
                throw new ArgumentException("Command must not be null or empty.", nameof(command));
            }

            // Only one command in flight at a time -- acquired for the whole write+read so a caller can
            // never observe an interleaved response.
            await sendGate.WaitAsync(ct).ConfigureAwait(false);
            try {
                // Snapshot the field into a LOCAL once, up front, and use only the local for the rest of this
                // exchange. A concurrent Close() (e.g. from another thread while this write/read is still in
                // flight) nulls the FIELD, but never this local -- so the underlying ISerialPort calls below
                // still run against a live reference and fail (if at all) with the underlying port's own
                // "closed" InvalidOperationException -- translated below to SerialPortClosedException -- never
                // a NullReferenceException.
                var port = serialPort;
                if (port == null) {
                    throw new InvalidOperationException("The EAT transport is not open; call OpenAsync first.");
                }

                // Start every exchange from a known-empty input buffer -- see DrainStaleInputAsync.
                await DrainStaleInputAsync(port, command, ct).ConfigureAwait(false);
                await WriteCommandAsync(port, command, ct).ConfigureAwait(false);
                var (lines, timedOut) = await ReadResponseAsync(port, timeout, ct).ConfigureAwait(false);
                return new EatRawExchange(command, lines, timedOut);
            } finally {
                sendGate.Release();
            }
        }

        /// <summary>
        /// Writes <paramref name="command"/> (+ line terminator) to <paramref name="port"/>. Runs the
        /// underlying (synchronous, blocking) <see cref="ISerialPort.Write"/> on a background task -- for
        /// symmetry with the already-offloaded read, and because a blocking inline write could otherwise
        /// freeze the caller (e.g. the UI thread) for up to <see cref="WriteTimeout"/> under an XON/XOFF
        /// stall, which is exactly the backpressure scenario this transport exists to handle correctly.
        /// </summary>
        private async Task WriteCommandAsync(ISerialPort port, string command, CancellationToken ct) {
            // Transcript logging (REQUIRED -- this raw TX/RX log is what T15 uses to resolve every
            // LIVE-CAPTURE unknown against a real device).
            Logger.Info($"EAT TX: {command}");

            var wireCommand = command + LineTerminator;
            try {
                await Task.Run(() => port.Write(wireCommand), ct).ConfigureAwait(false);
            } catch (InvalidOperationException ex) {
                // The underlying SerialPort throws InvalidOperationException when the port is not open (e.g.
                // unplugged mid-session) -- surface it as the NINA-idiomatic SerialPortClosedException
                // instead of swallowing it (mirrors SerialSdk's own translation).
                throw new SerialPortClosedException(
                    $"Serial port '{port.PortName ?? "unknown"}' was closed while sending '{command}'.", ex);
            }
            // TimeoutException is intentionally NOT caught here. WriteTimeout is sized well beyond the
            // longest legitimate move (see WriteTimeout above), so tripping it means the link is genuinely
            // stuck, not just busy -- a HARD failure. NINA's own SerialSdk swallows write timeouts and
            // presses on to read anyway; we deliberately do not, and let the exception propagate so the
            // caller knows the device state may be dirty.
        }

        /// <summary>
        /// Drains and discards the reset-on-open boot banner right after <see cref="OpenAsync"/> opens the port
        /// (see the class remarks). Returns as soon as the banner's terminating "FW:&lt;version&gt;" line is seen,
        /// or once the device goes quiet after having emitted banner data, or after
        /// <see cref="PostOpenSettleTimeout"/> as a safety cap. The Arduino is silent for ~1.5 s after the reset
        /// before the banner streams, so an early quiet poll (before ANY data) must NOT end the drain -- only a
        /// quiet poll AFTER data has arrived.
        /// </summary>
        private async Task DrainBootBannerAsync(ISerialPort port, CancellationToken ct) {
            var deadline = DateTime.UtcNow + PostOpenSettleTimeout;
            var receivedAny = false;
            while (DateTime.UtcNow < deadline) {
                ct.ThrowIfCancellationRequested();
                try {
                    var line = await ReadLineOrThrowIfClosedAsync(port, ct).ConfigureAwait(false);
                    Logger.Info($"EAT RX (post-open banner, discarded): {line}");
                    receivedAny = true;
                    if (line != null && line.Contains(BootBannerEndMarker)) {
                        // Banner's final line ("FW:<version>") -- the sketch is up and idle; safe to send commands.
                        return;
                    }
                } catch (TimeoutException) {
                    // The MCU is silent for ~1.5 s after the reset before the banner starts, so a quiet poll
                    // before any data just means "still booting" -- keep waiting. A quiet poll AFTER the banner
                    // has streamed means it is complete.
                    if (receivedAny) {
                        return;
                    }
                }
            }
        }

        /// <summary>See the class remarks for the full read-loop contract.</summary>
        private async Task<(List<string> Lines, bool TimedOut)> ReadResponseAsync(ISerialPort port, TimeSpan timeout, CancellationToken ct) {
            var lines = new List<string>();
            var deadline = DateTime.UtcNow + timeout;
            int heartbeats = 0;
            while (true) {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline) {
                    // Overall budget exhausted with no terminal sentinel: the response is INCOMPLETE, not
                    // "finished without a sentinel". Whatever the device still had to say stays in the port
                    // buffer, which is precisely the desync the next command's DrainStaleInputAsync clears.
                    LogHeartbeats(heartbeats);
                    return (lines, true);
                }

                try {
                    var line = await ReadLineOrThrowIfClosedAsync(port, ct).ConfigureAwait(false);
                    if (IsHeartbeatLine(line)) {
                        // A single move emits hundreds of these; logging each one buries the rest of the
                        // transcript (one observed session logged 43k of them). Counted and logged once.
                        heartbeats++;
                    } else {
                        LogHeartbeats(heartbeats);
                        heartbeats = 0;
                        Logger.Info($"EAT RX: {line}");
                    }
                    lines.Add(line);
                    if (IsTerminalResponseLine(line)) {
                        LogHeartbeats(heartbeats);
                        return (lines, false);
                    }
                } catch (TimeoutException) {
                    // A quiet window is NOT end-of-response. It only means nothing has arrived yet -- normal for
                    // a still-executing multi-second move, and (as a real session proved) also for a device that
                    // simply pauses mid-dump while the machine is busy. Treating quiet as "complete" abandoned a
                    // response after 3 of its ~22 lines; the unread remainder then became the head of the NEXT
                    // command's response, and every exchange for the rest of the session was one behind -- moves
                    // "acked" by a previous command's sentinel, positions read from a stale dump. Only a
                    // terminal sentinel (or the overall budget) ends an exchange.
                }
            }
        }

        private static void LogHeartbeats(int count) {
            if (count > 0) {
                Logger.Info($"EAT RX: {MotionHeartbeat} x{count}");
            }
        }

        /// <summary>
        /// Discards anything still sitting in the input buffer before a command is written. Non-empty here can
        /// only mean output from an earlier exchange this transport gave up on (it never reads unsolicited), so
        /// keeping it would make this command read the previous command's response — the permanent one-behind
        /// desync that, in a real session, made every position query return stale counters and every move ack a
        /// previous command's sentinel, recoverable only by disconnecting and reconnecting the port.
        ///
        /// <para>Costs nothing on a healthy link (the buffer is empty and this returns immediately). When there
        /// IS leftover data the device may still be mid-transmission, so the drain confirms the line has gone
        /// quiet for a full <see cref="QuietWindow"/> before returning, bounded by <see cref="ResyncTimeout"/>.</para>
        /// </summary>
        private async Task<int> DrainStaleInputAsync(ISerialPort port, string command, CancellationToken ct) {
            int buffered;
            try {
                buffered = port.BytesToRead;
            } catch (InvalidOperationException ex) {
                throw new SerialPortClosedException(
                    $"Serial port '{port.PortName ?? "unknown"}' was closed while checking for stale input.", ex);
            }
            if (buffered <= 0) {
                return 0;
            }

            port.DiscardInBuffer();
            int discardedLines = 0;
            var deadline = DateTime.UtcNow + ResyncTimeout;
            while (DateTime.UtcNow < deadline) {
                try {
                    await ReadLineOrThrowIfClosedAsync(port, ct).ConfigureAwait(false);
                    ++discardedLines; // Still arriving: the device was mid-transmission. Keep draining.
                } catch (TimeoutException) {
                    break; // Quiet for a full window -- the stream is back in step.
                }
            }
            Logger.Warning(
                $"EAT transport: discarded {buffered} stale byte(s){(discardedLines > 0 ? $" plus {discardedLines} trailing line(s)" : string.Empty)} " +
                $"left over from a previous exchange before sending '{command}'. The link had fallen out of step (a response was abandoned before its terminal sentinel); it has been resynchronized.");
            return discardedLines;
        }

        /// <summary>True if <paramref name="line"/> is one of the device's end-of-response sentinels (see <see cref="MoveCompleteSentinel"/> / <see cref="QueryCompleteSentinel"/>).</summary>
        private static bool IsTerminalResponseLine(string line) =>
            line != null && (line.Contains(MoveCompleteSentinel) || line.Contains(QueryCompleteSentinel));

        /// <summary>True for the repeated in-motion heartbeat, which is summarized rather than logged per line.</summary>
        private static bool IsHeartbeatLine(string line) => line != null && line.Contains(MotionHeartbeat);

        /// <summary>
        /// Wraps the underlying (synchronous, blocking) <see cref="ISerialPort.ReadLine"/> in a background
        /// task so callers can await it, translating a closed-port <see cref="InvalidOperationException"/>
        /// into <see cref="SerialPortClosedException"/> (propagated, never swallowed). A plain read timeout
        /// (<see cref="TimeoutException"/>) is left for the caller to interpret -- it is NOT necessarily an
        /// error (see ReadResponseAsync). Note: because the underlying <c>SerialPort.ReadLine()</c> call
        /// itself has no cancellation support, <paramref name="ct"/> is only observed between poll attempts,
        /// not while a single ReadLine() call is blocked inside the port's own read timeout window -- the
        /// same limitation NINA's own SerialSdk has. <paramref name="port"/> is always the LOCAL snapshot
        /// taken by the caller (see <see cref="SendAsync"/>/<see cref="OpenAsync"/>), never a fresh read of
        /// the <c>serialPort</c> field -- so a concurrent <see cref="Close"/> (which nulls the field) cannot
        /// turn this into a <see cref="NullReferenceException"/>; the underlying port object itself simply
        /// throws its own closed-port exception, translated below.
        /// </summary>
        private async Task<string> ReadLineOrThrowIfClosedAsync(ISerialPort port, CancellationToken ct) {
            try {
                return await Task.Run(() => port.ReadLine(), ct).ConfigureAwait(false);
            } catch (InvalidOperationException ex) {
                throw new SerialPortClosedException(
                    $"Serial port '{port.PortName ?? "unknown"}' was closed while reading.", ex);
            }
        }
    }
}
