#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility.SerialCommunication;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat {

    /// <summary>
    /// Pure parsing of an <see cref="EatRawExchange"/> into interpreted results -- no I/O (see
    /// <c>EatSerialTransport.cs</c> for the actual serial send/receive). THE SEAM FILE (together with
    /// <c>EatSerialTransport.cs</c>) that contains every assumption about the ASG EAT's response wire
    /// format, each marked <c>// LIVE-CAPTURE:</c> for the plan's T15 hardware session. Every method here
    /// is deliberately TOLERANT: pre-T15, real hardware responses are guaranteed to sometimes fail to parse
    /// (the exact format is unknown), and callers (plan task T7's <c>EatTiltMotionController</c>) are
    /// designed to treat that as "unknown", not as a crash.
    /// </summary>
    public static class EatResponses {

        // Move-ack policy, confirmed against ASG EAT firmware 7.1.0 (see docs/asg-eat-serial-protocol-design.md):
        // a successful move ends with a "***finished movement***" sentinel line, and EatSerialTransport's read
        // loop returns as soon as it sees that sentinel (TimedOut=false). A move that never completes leaves the
        // read loop to exhaust its timeout budget (TimedOut=true). So "did not time out" IS the ack signal here --
        // the sentinel recognition has already happened one layer down, in the transport. SimulatedEatTransport
        // likewise returns a non-timed-out exchange for a successful move. NOTE: an explicit device-side ERROR
        // response format was not captured (see the doc's open items), so a rejected move that still returns a
        // terminal sentinel is not yet distinguished; tighten here if such a response is ever captured.
        /// <summary>
        /// Interpretation of a move command's response: success unless <paramref name="exchange"/> timed out.
        /// The completion decision (reading through to "***finished movement***") lives in the transport's read
        /// loop; this only maps that outcome to success/failure.
        /// </summary>
        public static bool ParseMoveAck(EatRawExchange exchange) {
            if (exchange == null) {
                throw new ArgumentNullException(nameof(exchange));
            }
            return !exchange.TimedOut;
        }

        // `cp` reply layout, confirmed against ASG EAT firmware 7.1.0 (see docs/asg-eat-serial-protocol-design.md):
        // the four positions are bare integer lines wrapped between "***Get Current Positions***" and
        // "***End Current Positions***" marker lines, in WIRE order [TL, TR, BL, BR] (raster order), amid a stream
        // of {UI|SET|...} GUI-driver lines that must be ignored. They are absolute EEPROM-persisted counters.
        // SimulatedEatTransport and the pre-capture unit fixtures instead emit a plain integer list with no
        // markers, already in device order [TR, TL, BR, BL]; those take the fallback path below (first four
        // integers found, any non-digit char a delimiter -- tolerant of commas/spaces across one or many lines).
        private static readonly Regex IntegerToken = new Regex(@"-?\d+", RegexOptions.Compiled);

        // Substring markers (matched with Contains, so the surrounding '***' and CR/LF framing are irrelevant).
        private const string PositionBlockStartMarker = "Get Current Positions";
        private const string PositionBlockEndMarker = "End Current Positions";

        /// <summary>
        /// Extracts the four per-motor position counters from a <c>cp</c> response, in
        /// <see cref="TiltDevicePositions"/> device motor order (TR, TL, BR, BL). Prefers the real device's
        /// marked block (reordering its wire order [TL, TR, BL, BR] into device order); falls back to "the first
        /// four integers found" for the simulator and pre-capture fixtures. Throws
        /// <see cref="InvalidDeviceResponseException"/> (with the raw lines in the message) if neither yields
        /// four integers; callers treat that as "positions unknown", not a crash.
        /// </summary>
        public static TiltDevicePositions ParseCpPositions(EatRawExchange exchange) {
            if (exchange == null) {
                throw new ArgumentNullException(nameof(exchange));
            }

            if (TryParseMarkedPositionBlock(exchange, out var fromBlock)) {
                return fromBlock;
            }

            var values = new List<int>();
            foreach (var line in exchange.Lines) {
                if (line == null) {
                    continue;
                }
                foreach (Match match in IntegerToken.Matches(line)) {
                    if (values.Count >= 4) {
                        break;
                    }
                    if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) {
                        values.Add(value);
                    }
                }
                if (values.Count >= 4) {
                    break;
                }
            }

            if (values.Count != 4) {
                var rawLines = string.Join(" | ", exchange.Lines);
                throw new InvalidDeviceResponseException(
                    $"Could not parse four EAT position integers from a 'cp' response (found {values.Count}). " +
                    $"Command: '{exchange.Command}'. Raw lines: [{rawLines}]");
            }

            return new TiltDevicePositions(values, known: true);
        }

        /// <summary>
        /// Parses the real ASG EAT's marked position block if present. Returns false (so the caller falls back to
        /// the first-four-integers path) when there is no "***Get Current Positions***" marker at all. Throws
        /// <see cref="InvalidDeviceResponseException"/> if the marker IS present but does not enclose exactly four
        /// integers -- a genuinely malformed real-device response, not a "different format" case. The block's wire
        /// order is [TL, TR, BL, BR]; it is reordered here into device motor order [TR, TL, BR, BL] to match
        /// <see cref="TiltDevicePositions"/>.
        /// </summary>
        private static bool TryParseMarkedPositionBlock(EatRawExchange exchange, out TiltDevicePositions positions) {
            positions = null;

            int startIndex = -1;
            for (int i = 0; i < exchange.Lines.Count; ++i) {
                if (exchange.Lines[i] != null && exchange.Lines[i].Contains(PositionBlockStartMarker)) {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex < 0) {
                return false;
            }

            var wire = new List<int>(4);
            for (int i = startIndex + 1; i < exchange.Lines.Count && wire.Count < 4; ++i) {
                var line = exchange.Lines[i];
                if (line == null) {
                    continue;
                }
                if (line.Contains(PositionBlockEndMarker)) {
                    break;
                }
                if (int.TryParse(line.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) {
                    wire.Add(value);
                }
            }

            if (wire.Count != 4) {
                var rawLines = string.Join(" | ", exchange.Lines);
                throw new InvalidDeviceResponseException(
                    $"'cp' response had a '{PositionBlockStartMarker}' marker but did not enclose four integers (found {wire.Count}). " +
                    $"Command: '{exchange.Command}'. Raw lines: [{rawLines}]");
            }

            // Wire order [TL, TR, BL, BR] -> device motor order [TR, TL, BR, BL].
            var deviceOrder = new[] { wire[1], wire[0], wire[3], wire[2] };
            positions = new TiltDevicePositions(deviceOrder, known: true);
            return true;
        }
    }
}
