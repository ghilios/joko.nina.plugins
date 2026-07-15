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
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat {

    /// <summary>
    /// Thrown by <see cref="EatTiltMotionController"/> BEFORE any device I/O when a requested move (or,
    /// for <see cref="EatTiltMotionController.OrderForMinimalPeakExcursion"/>, every possible ordering of
    /// a small multi-move plan) would violate a configured soft limit -- <c>TiltDeviceMaxStepsPerCommand</c>
    /// (a single command's magnitude) or <c>TiltDeviceMaxExcursionSteps</c> (a motor's resulting absolute,
    /// or when positions are unknown this session, best-estimate position). This is the last line of
    /// software defense against an EEPROM-persisted, physically-damaging motor move: every check this
    /// exception guards runs strictly before <see cref="EatCommands.Format(TiltAdapterMove, EatSignEncoding)"/>
    /// is even called, let alone anything reaches <see cref="IEatTransport.SendAsync"/>.
    /// </summary>
    [Serializable]
    public sealed class TiltDeviceLimitException : Exception {

        public TiltDeviceLimitException() {
        }

        public TiltDeviceLimitException(string message) : base(message) {
        }

        public TiltDeviceLimitException(string message, Exception innerException) : base(message, innerException) {
        }
    }

    /// <summary>
    /// Thrown by <see cref="EatTiltMotionController.ExecuteMoveAsync"/> when a move WAS sent (it passed
    /// every pre-send limit check) but the device did not acknowledge success -- see
    /// <see cref="EatResponses.ParseMoveAck"/>. Deliberately a DIFFERENT exception type from
    /// <see cref="TiltDeviceLimitException"/>: a limit violation means nothing was ever sent (device state
    /// is untouched), while this means a command WAS sent and its outcome is ambiguous (state may be
    /// dirty -- see <c>EatSerialTransport</c>'s remarks on this exact ambiguity) -- callers (the wizard's
    /// failure-recovery path, the inspector's applied-move journal) need to tell the two apart to decide
    /// whether an inverse/recovery move is even meaningful.
    /// </summary>
    [Serializable]
    public sealed class TiltDeviceCommandFailedException : Exception {

        public TiltDeviceCommandFailedException() {
        }

        public TiltDeviceCommandFailedException(string message) : base(message) {
        }

        public TiltDeviceCommandFailedException(string message, Exception innerException) : base(message, innerException) {
        }
    }

    /// <summary>
    /// <see cref="ITiltMotionController"/> driver for the ASG EAT (plan task T7). Owns the safety-critical
    /// soft-limit enforcement (every check happens BEFORE any command reaches the transport), the per-motor
    /// shadow position tracking that makes excursion enforcement possible, and move sequencing.
    ///
    /// <para><b>Wizard-index -&gt; device-motor permutation (CRITICAL).</b> <see cref="TiltAdapterMove.PerCornerSteps"/>
    /// is in WIZARD screw-index order (wizard1..4 at [0..3]). Every shadow update and excursion check in
    /// this class needs DEVICE motor order (motor1..4 = TR, TL, BR, BL at [0..3] -- see
    /// <see cref="TiltDevicePositions"/>). The connected convention is wizard1=TR, wizard2=TL, wizard3=BL,
    /// wizard4=BR, so the device-motor vector is a PERMUTATION, not a straight copy, of the wizard vector:
    /// motor1(TR)&lt;-wizard1, motor2(TL)&lt;-wizard2, motor3(BR)&lt;-wizard4, motor4(BL)&lt;-wizard3. See
    /// <see cref="PermuteWizardToDeviceMotorOrder"/> -- every wizard-order-to-device-order conversion in
    /// this file MUST go through it. (The per-command step CAP is unaffected -- it's a max over motors
    /// either way -- but shadow tracking and 'cp' reconciliation are NOT permutation-invariant.)</para>
    ///
    /// <para><b>Shadow position tracking.</b> <see cref="ITiltAdapterOptions.TiltDeviceShadowPositions"/>
    /// persists a per-motor cumulative step counter (DEVICE motor order) plus a validity flag across app
    /// restarts -- see <see cref="SerializeShadowPositions"/> for the exact format. The validity flag
    /// (exposed as <see cref="AbsolutePositionsKnown"/>) is TRUE only once a 'cp' response has actually
    /// parsed (at connect or during a later poll); it is FALSE from a fresh install, or whenever the most
    /// recent <see cref="ConnectAsync"/> could not confirm positions (GUARANTEED pre-T15, since the 'cp'
    /// response format is unknown until the live hardware capture). Per the design doc's explicit
    /// instruction, the plugin NEVER zeroes the shadow itself: on an unparseable 'cp' at connect, the
    /// numeric counters are left exactly as loaded (from persisted state, or the zero-initialized default
    /// if never seeded) -- only the confidence flag changes. This means excursion enforcement while
    /// <see cref="AbsolutePositionsKnown"/> is false is enforced against the best available estimate (a
    /// possibly-stale persisted value, or a from-this-session-only delta counter if nothing was ever
    /// confirmed) rather than a verified absolute position -- a real but bounded and clearly-flagged risk,
    /// backstopped by the always-enforced per-command cap and the approval dialog's unknown-position
    /// warning (T13). A successfully-parsed 'cp' (at connect OR at any later poll) always overwrites the
    /// shadow with ground truth and marks it valid -- this is also how a vendor-app zero (or any other
    /// out-of-band device touch) gets picked up, since the plugin never sends its own zero command.</para>
    ///
    /// <para><b>Cancellation.</b> The ASG EAT has no abort command (plan's "Device facts"): once a move is
    /// sent, it WILL run for 5-10 s on real hardware no matter what the caller's token does afterward.
    /// <see cref="ExecuteMoveAsync"/> therefore only honors <c>ct</c> at ONE boundary -- the very start of
    /// the call, before any validation or I/O -- and then always runs the actual send/settle to completion
    /// using <see cref="CancellationToken.None"/> internally. A caller executing a multi-move plan
    /// sequentially (one <see cref="ExecuteMoveAsync"/> call per move) gets the documented "current move
    /// completes, then stops before the next" semantics for free: cancelling mid-move has no effect on the
    /// in-flight call, but the NEXT call's entry check throws immediately without sending anything.</para>
    /// </summary>
    public sealed class EatTiltMotionController : BaseINPC, ITiltMotionController {

        // Device motor order labels (index 0..3) -- for diagnostic/error text only, NEVER for a command
        // formatting or permutation decision (see PermuteWizardToDeviceMotorOrder for that).
        private static readonly string[] DeviceMotorLabels = { "TR", "TL", "BR", "BL" };

        // Move commands take 5-10 s (plan's Device facts) -- comfortably beyond the plan-mandated >= 15 s
        // floor so a legitimate slow move never trips the overall exchange budget.
        internal static readonly TimeSpan MoveTimeout = TimeSpan.FromSeconds(20);

        // 'cp' is a position query, not a multi-second move -- a much shorter overall exchange budget is
        // appropriate here. Tunable (see plan T15 item 7), not a wire-format LIVE-CAPTURE unknown.
        internal static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

        private readonly IEatTransport transport;
        private readonly ITiltAdapterOptions options;

        // Per-motor cumulative step counters, DEVICE motor order (TR, TL, BR, BL at [0..3]) -- see the
        // class remarks' "Shadow position tracking" section for the full seeding/reconciliation policy.
        // NOT internally locked: the read-validate-send-update sequence in ExecuteMoveAsync (and the
        // shadow mutations it makes) relies on T9's exclusive-operation token (TiltDeviceConnectionService's
        // TryBeginOperation lease / hardwareLock) for mutual exclusion between concurrent callers, rather
        // than a lock of its own here.
        private readonly int[] shadowPositions = new int[4];
        private bool shadowValid;
        private bool connected;

        /// <summary>Test seam: injects the transport so tests substitute <see cref="IEatTransport"/> instead of real serial I/O.</summary>
        internal EatTiltMotionController(IEatTransport transport, ITiltAdapterOptions options) {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            LoadPersistedShadow();
        }

        /// <summary>Production constructor: uses the real <see cref="EatSerialTransport"/>. Matches the <c>TiltMotionControllerRegistry</c> factory signature.</summary>
        public EatTiltMotionController(ITiltAdapterOptions options) : this(new EatSerialTransport(), options) {
        }

        public bool Connected {
            get => connected;
            private set {
                if (connected == value) {
                    return;
                }
                connected = value;
                RaisePropertyChanged();
            }
        }

        public TiltDeviceCapabilities Capabilities { get; } = new TiltDeviceCapabilities(
            TiltDeviceTopology.FourCornerCoupled,
            new[] { TiltMoveAxis.DiagonalA, TiltMoveAxis.DiagonalB, TiltMoveAxis.EdgeVertical, TiltMoveAxis.EdgeHorizontal, TiltMoveAxis.Backfocus },
            supportsPerScrewIndependent: false);

        /// <summary>
        /// True once this controller's per-motor absolute step counters are trusted (seeded/reconciled from
        /// a successfully-parsed 'cp' response, at connect or any later poll). False means excursion
        /// enforcement is running against a best-estimate rather than a verified absolute position -- see
        /// the class remarks' "Shadow position tracking" section. Callers (the T13 approval dialog, wizard
        /// status text) should surface this as a degraded-enforcement warning.
        /// </summary>
        public bool AbsolutePositionsKnown => shadowValid;

        public async Task ConnectAsync(string portName, CancellationToken ct) {
            if (string.IsNullOrWhiteSpace(portName)) {
                throw new ArgumentException("Port name must not be null or empty.", nameof(portName));
            }

            await transport.OpenAsync(portName, ct).ConfigureAwait(false);

            // The 'cp' query is the ONLY source of truth this class trusts for confirming the device's
            // current per-motor positions. A transport-level failure here (closed port, cancellation) is a
            // real failure and must abort the connect; only an UNPARSEABLE response (ParseCpPositions
            // throwing InvalidDeviceResponseException -- GUARANTEED pre-T15, since the wire format is
            // unknown until the live hardware capture) is tolerated.
            var exchange = await transport.SendAsync(EatCommands.PositionQuery(), QueryTimeout, ct).ConfigureAwait(false);
            try {
                var positions = EatResponses.ParseCpPositions(exchange);
                ApplyKnownPositions(positions);
            } catch (InvalidDeviceResponseException ex) {
                Logger.Warning($"EAT connect: 'cp' response did not parse; positions unknown for this session (excursion enforcement degraded). {ex.Message}");
                // Per the class remarks: NEVER zero the shadow -- leave the counters exactly as loaded by
                // LoadPersistedShadow (or their zero-initialized default if never seeded); only the
                // confidence flag changes. Do NOT fail the connect just because 'cp' didn't parse.
                SetShadowValid(false);
            }

            Connected = true;
        }

        public Task DisconnectAsync(CancellationToken ct) {
            transport.Close();
            Connected = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Validates and executes a single move against the connected EAT. See the class remarks'
        /// "Cancellation" section for the single (entry-only) cancellation boundary.
        ///
        /// <para><b>Exception taxonomy for callers (T14/wizard failure-recovery).</b> Every pre-send limit
        /// check throwing <see cref="TiltDeviceLimitException"/> means NOTHING was sent -- device state is
        /// untouched and the shadow is unchanged. Once <see cref="IEatTransport.SendAsync"/> has actually
        /// been called, two different outcomes both mean "a command may have physically executed and the
        /// shadow was NOT advanced -- treat this as a potential state-dirty condition and consider a
        /// revert/resync":
        /// <list type="bullet">
        /// <item><description><see cref="TiltDeviceCommandFailedException"/> -- covers both a write timeout
        /// (a <see cref="TimeoutException"/> from <see cref="IEatTransport.SendAsync"/>, which
        /// <c>EatSerialTransport</c> deliberately does not swallow -- see its <c>WriteCommand</c> remarks --
        /// and which is wrapped here as this exception's <see cref="Exception.InnerException"/>) and a move
        /// that sent cleanly but did not acknowledge success (<see cref="EatResponses.ParseMoveAck"/>
        /// returned false).</description></item>
        /// <item><description>A propagated <see cref="SerialPortClosedException"/> (e.g. the device was
        /// unplugged mid-move) -- deliberately NOT wrapped/reclassified here, so T9's port-unplug handling
        /// can still catch it by its own type, but it carries exactly the same "state may be dirty"
        /// implication as <see cref="TiltDeviceCommandFailedException"/> above.</description></item>
        /// </list>
        /// </para>
        /// </summary>
        public async Task ExecuteMoveAsync(TiltAdapterMove move, IProgress<string> progress, CancellationToken ct) {
            if (move == null) {
                throw new ArgumentNullException(nameof(move));
            }

            // BETWEEN-COMMANDS cancellation boundary (see class remarks' "Cancellation" section): checked
            // here, before ANY work for this move begins, and nowhere else in this method. The ASG EAT has
            // no abort command, so once we pass this line the move always runs to completion regardless of
            // what happens to ct afterward -- a caller executing a sequence of moves relies on the NEXT
            // call hitting this same check to stop the sequence, not on this call being interrupted.
            ct.ThrowIfCancellationRequested();

            if (!Connected) {
                throw new InvalidOperationException("EatTiltMotionController is not connected; call ConnectAsync first.");
            }

            // 1) Per-command cap -- BEFORE any device I/O.
            int maxStepsPerCommand = options.TiltDeviceMaxStepsPerCommand;
            int magnitude = Math.Abs(move.Steps);
            if (magnitude > maxStepsPerCommand) {
                throw new TiltDeviceLimitException(
                    $"Move '{move.Description}' ({move.Axis}, {FormatSigned(move.Steps)} steps) exceeds the configured max steps per command ({maxStepsPerCommand}). Nothing was sent.");
            }

            // 2) Excursion -- BEFORE any device I/O. Predicted = current shadow (already reflecting every
            // prior successfully-executed move) + this move's effect, permuted from WIZARD screw-index
            // order into DEVICE motor order. Because the shadow is updated after every successful move
            // (step 3 below), a caller executing a multi-move plan sequentially via repeated
            // ExecuteMoveAsync calls gets INTERMEDIATE-state validation for free -- there is no separate
            // plan-level excursion check beyond this per-call one.
            var deviceDelta = PermuteWizardToDeviceMotorOrder(move.PerCornerSteps);
            int maxExcursion = options.TiltDeviceMaxExcursionSteps;
            var predicted = new int[4];
            for (int i = 0; i < 4; ++i) {
                predicted[i] = shadowPositions[i] + RoundToInt(deviceDelta[i]);
                if (Math.Abs(predicted[i]) > maxExcursion) {
                    string degradedNote = shadowValid ? string.Empty :
                        " Absolute positions are unknown this session (excursion is enforced against the last known/persisted estimate, which may be stale) -- see AbsolutePositionsKnown.";
                    throw new TiltDeviceLimitException(
                        $"Move '{move.Description}' would move motor {i + 1} ({DeviceMotorLabels[i]}) to {predicted[i]} steps, exceeding the configured max excursion ({maxExcursion}).{degradedNote} Nothing was sent.");
                }
            }

            // 3) Only now: format + send. No abort capability on the device -- CancellationToken.None is
            // passed to the transport deliberately (see the cancellation remarks above): once we commit to
            // sending, the exchange always runs to completion so the shadow bookkeeping below stays
            // consistent with whatever actually happened on the device.
            string wire = EatCommands.Format(move);
            progress?.Report($"Sending {wire} ({move.Description})");

            EatRawExchange exchange;
            try {
                exchange = await transport.SendAsync(wire, MoveTimeout, CancellationToken.None).ConfigureAwait(false);
            } catch (TimeoutException ex) {
                // A write timeout (EatSerialTransport.WriteCommand deliberately does NOT swallow this --
                // WriteTimeout is sized well beyond any legitimate move, so tripping it means the link is
                // genuinely stuck) means the command may have been partially or fully transmitted before the
                // link died -- classify it exactly like an ack failure/timeout below: state is ambiguous, and
                // the shadow must NOT be advanced. Deliberately does NOT catch SerialPortClosedException --
                // see this method's XML doc for why that propagates unmodified instead.
                throw new TiltDeviceCommandFailedException(
                    $"EAT command '{wire}' ({move.Description}) failed to send (write timeout). Device state is ambiguous; positions were NOT advanced -- treat as a potential state-dirty condition.",
                    ex);
            }
            if (!EatResponses.ParseMoveAck(exchange)) {
                throw new TiltDeviceCommandFailedException(
                    $"EAT command '{wire}' ({move.Description}) did not acknowledge success within {MoveTimeout.TotalSeconds:0}s. Device state is ambiguous; positions were NOT advanced -- treat as a potential state-dirty condition.");
            }

            // Success: advance + persist shadow ONLY here -- the throw above (ack failure/timeout) never
            // reaches this line, so a failed/timed-out move never advances the shadow.
            ApplyMoveDeltaToShadow(deviceDelta);
            progress?.Report($"{wire} acknowledged");

            double settleSeconds = options.TiltDeviceSettleSeconds;
            if (settleSeconds > 0) {
                // Settle is a fixed post-move safety wait, not a device abort point -- CancellationToken.None
                // again, for the same "no abort, don't desync bookkeeping" reason as the send above.
                progress?.Report($"Settling {settleSeconds:0.#}s");
                await Task.Delay(TimeSpan.FromSeconds(settleSeconds), CancellationToken.None).ConfigureAwait(false);
            }
            progress?.Report($"{wire} complete");
        }

        public async Task<TiltDevicePositions> QueryPositionsAsync(CancellationToken ct) {
            if (!Connected) {
                throw new InvalidOperationException("EatTiltMotionController is not connected; call ConnectAsync first.");
            }

            var exchange = await transport.SendAsync(EatCommands.PositionQuery(), QueryTimeout, ct).ConfigureAwait(false);
            TiltDevicePositions positions;
            try {
                positions = EatResponses.ParseCpPositions(exchange);
            } catch (InvalidDeviceResponseException ex) {
                // GUARANTEED pre-T15 -- tolerant no-op: leave the shadow (and its confidence flag) exactly
                // as-is. A single failed poll must not retroactively invalidate an already-trusted shadow;
                // "reconcile once the format is known" (design doc) means "reconcile when it parses",
                // nothing more.
                Logger.Warning($"EAT 'cp' poll did not parse; positions unknown for this poll. {ex.Message}");
                return TiltDevicePositions.Unknown;
            }

            ApplyKnownPositions(positions);
            return positions;
        }

        /// <summary>
        /// Orders <paramref name="moves"/> (a small plan -- the design doc's minimal-move decomposition
        /// caps a plan at 3 moves before cap-splitting) to minimize the PEAK per-motor excursion across
        /// every intermediate state, starting from the CURRENT shadow. Pure/side-effect-free: does not
        /// touch the shadow and sends nothing -- callers (T14) call this once to decide send order, then
        /// invoke <see cref="ExecuteMoveAsync"/> for each move in the returned order (whose own per-move
        /// validation remains the defensive, authoritative check; this method only picks the least-risky
        /// order). Throws <see cref="TiltDeviceLimitException"/> if any move exceeds the per-command cap
        /// (no ordering can fix that) or if even the best ordering's peak excursion exceeds the configured
        /// max. Evaluates every permutation (at most 3! = 6 for the plans this feature ever produces) --
        /// deliberately not intended to scale beyond a handful of moves.
        /// </summary>
        public IReadOnlyList<TiltAdapterMove> OrderForMinimalPeakExcursion(IReadOnlyList<TiltAdapterMove> moves) {
            if (moves == null) {
                throw new ArgumentNullException(nameof(moves));
            }
            if (moves.Count == 0) {
                return Array.Empty<TiltAdapterMove>();
            }

            int maxStepsPerCommand = options.TiltDeviceMaxStepsPerCommand;
            foreach (var move in moves) {
                if (Math.Abs(move.Steps) > maxStepsPerCommand) {
                    throw new TiltDeviceLimitException(
                        $"Move '{move.Description}' ({move.Axis}, {FormatSigned(move.Steps)} steps) exceeds the configured max steps per command ({maxStepsPerCommand}); no ordering can fix this. Nothing was sent.");
                }
            }

            int maxExcursion = options.TiltDeviceMaxExcursionSteps;
            IReadOnlyList<TiltAdapterMove> bestOrder = null;
            int bestPeak = int.MaxValue;
            foreach (var candidate in GeneratePermutations(moves)) {
                int peak = PeakExcursion(candidate);
                if (peak < bestPeak) {
                    bestPeak = peak;
                    bestOrder = candidate;
                }
            }

            if (bestOrder == null || bestPeak > maxExcursion) {
                string degradedNote = shadowValid ? string.Empty :
                    " Absolute positions are unknown this session (excursion is enforced against the last known/persisted estimate, which may be stale) -- see AbsolutePositionsKnown.";
                throw new TiltDeviceLimitException(
                    $"No ordering of the given {moves.Count} move(s) keeps every intermediate/final per-motor position within the configured max excursion ({maxExcursion} steps); the best achievable peak is {bestPeak} steps.{degradedNote} Nothing was sent.");
            }

            return bestOrder;
        }

        private int PeakExcursion(IReadOnlyList<TiltAdapterMove> order) {
            var running = (int[])shadowPositions.Clone();
            int peak = 0;
            for (int i = 0; i < 4; ++i) {
                peak = Math.Max(peak, Math.Abs(running[i]));
            }
            foreach (var move in order) {
                var delta = PermuteWizardToDeviceMotorOrder(move.PerCornerSteps);
                for (int i = 0; i < 4; ++i) {
                    running[i] += RoundToInt(delta[i]);
                    peak = Math.Max(peak, Math.Abs(running[i]));
                }
            }
            return peak;
        }

        // Simple recursive permutation generator -- fine for the <= 3-move plans this feature ever
        // produces (see TiltMovePlanner's "never exceeds three" guarantee); factorial blowup is a non-issue
        // at n<=3 (at most 6 candidates).
        private static IEnumerable<IReadOnlyList<TiltAdapterMove>> GeneratePermutations(IReadOnlyList<TiltAdapterMove> moves) {
            if (moves.Count <= 1) {
                yield return moves;
                yield break;
            }
            for (int i = 0; i < moves.Count; ++i) {
                var rest = new List<TiltAdapterMove>(moves);
                var chosen = rest[i];
                rest.RemoveAt(i);
                foreach (var subPermutation in GeneratePermutations(rest)) {
                    var result = new List<TiltAdapterMove>(moves.Count) { chosen };
                    result.AddRange(subPermutation);
                    yield return result;
                }
            }
        }

        /// <summary>
        /// Permutes a WIZARD-screw-index-ordered per-corner vector (wizard screws 1..4 at [0..3], as
        /// produced by <see cref="TiltAdapterMove.PerCornerSteps"/>) into DEVICE motor order (TR, TL, BR,
        /// BL at [0..3]). THE correctness anchor for every shadow-tracking and 'cp'-reconciliation
        /// calculation in this class -- see the class remarks' "Wizard-index -&gt; device-motor
        /// permutation" section for the full derivation:
        /// <c>deviceMotorVec = [ wizardVec[0], wizardVec[1], wizardVec[3], wizardVec[2] ]</c> -- motor1
        /// (TR) &lt;- wizard1, motor2 (TL) &lt;- wizard2, motor3 (BR) &lt;- wizard4, motor4 (BL) &lt;-
        /// wizard3. Note this is NOT the identity permutation: wizard3 and wizard4 swap positions relative
        /// to a naive "wizard order == device order" copy.
        /// </summary>
        internal static double[] PermuteWizardToDeviceMotorOrder(IReadOnlyList<double> wizardPerCornerSteps) {
            if (wizardPerCornerSteps == null || wizardPerCornerSteps.Count != 4) {
                throw new ArgumentException("Wizard per-corner vector must contain exactly 4 elements (wizard screw indices 1..4).", nameof(wizardPerCornerSteps));
            }
            return new[] {
                wizardPerCornerSteps[0], // motor1 TR <- wizard1
                wizardPerCornerSteps[1], // motor2 TL <- wizard2
                wizardPerCornerSteps[3], // motor3 BR <- wizard4
                wizardPerCornerSteps[2], // motor4 BL <- wizard3
            };
        }

        /// <summary>
        /// Serializes the shadow position state into the format persisted in
        /// <see cref="ITiltAdapterOptions.TiltDeviceShadowPositions"/>:
        /// <c>&lt;0|1&gt;;&lt;TR&gt;;&lt;TL&gt;;&lt;BR&gt;;&lt;BL&gt;</c> -- a leading validity flag ('1' =
        /// <paramref name="valid"/>, the per-motor counters are trusted absolute positions; '0' = not yet
        /// confirmed by a successfully-parsed 'cp' response) followed by the four DEVICE-motor-order (TR,
        /// TL, BR, BL) counters, all semicolon-separated. Example: <c>1;10;-5;0;20</c>.
        /// </summary>
        internal static string SerializeShadowPositions(bool valid, IReadOnlyList<int> perMotorSteps) {
            if (perMotorSteps == null || perMotorSteps.Count != 4) {
                throw new ArgumentException("Per-motor step array must contain exactly 4 elements (DEVICE motor order TR, TL, BR, BL).", nameof(perMotorSteps));
            }
            return string.Join(";",
                valid ? "1" : "0",
                perMotorSteps[0].ToString(CultureInfo.InvariantCulture),
                perMotorSteps[1].ToString(CultureInfo.InvariantCulture),
                perMotorSteps[2].ToString(CultureInfo.InvariantCulture),
                perMotorSteps[3].ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Tolerant inverse of <see cref="SerializeShadowPositions"/>. Returns false (rather than throwing)
        /// for null/empty/malformed input -- e.g. the very first run ever, before anything has been
        /// persisted -- so callers treat that exactly like "never seeded" instead of crashing.
        /// </summary>
        internal static bool TryParseShadowPositions(string serialized, out bool valid, out int[] perMotorSteps) {
            valid = false;
            perMotorSteps = new int[4];
            if (string.IsNullOrWhiteSpace(serialized)) {
                return false;
            }

            var parts = serialized.Split(';');
            if (parts.Length != 5) {
                return false;
            }
            if (parts[0] != "0" && parts[0] != "1") {
                return false;
            }

            var steps = new int[4];
            for (int i = 0; i < 4; ++i) {
                if (!int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out steps[i])) {
                    return false;
                }
            }

            valid = parts[0] == "1";
            perMotorSteps = steps;
            return true;
        }

        private void LoadPersistedShadow() {
            if (TryParseShadowPositions(options.TiltDeviceShadowPositions, out var valid, out var steps)) {
                Array.Copy(steps, shadowPositions, 4);
                shadowValid = valid;
            }
            // Else: unparseable/empty (e.g. the very first run ever) -- leave shadowPositions at its
            // zero-initialized default and shadowValid false (never seeded). This is NOT the same as
            // "zeroing" the shadow (which the plugin never does) -- it's simply the natural default for
            // state that has never existed.
        }

        private void ApplyKnownPositions(TiltDevicePositions positions) {
            var steps = positions.PerMotorSteps;
            for (int i = 0; i < 4; ++i) {
                shadowPositions[i] = i < steps.Count ? steps[i] : 0;
            }
            SetShadowValid(true);
            PersistShadow();
        }

        private void ApplyMoveDeltaToShadow(IReadOnlyList<double> deviceDelta) {
            for (int i = 0; i < 4; ++i) {
                shadowPositions[i] += RoundToInt(deviceDelta[i]);
            }
            PersistShadow();
        }

        private void SetShadowValid(bool value) {
            if (shadowValid == value) {
                return;
            }
            shadowValid = value;
            RaisePropertyChanged(nameof(AbsolutePositionsKnown));
        }

        private void PersistShadow() {
            options.TiltDeviceShadowPositions = SerializeShadowPositions(shadowValid, shadowPositions);
        }

        private static int RoundToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        private static string FormatSigned(int steps) => steps.ToString("+0;-0;0", CultureInfo.InvariantCulture);
    }
}
