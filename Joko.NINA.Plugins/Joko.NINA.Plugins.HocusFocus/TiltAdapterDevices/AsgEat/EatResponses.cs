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

        // LIVE-CAPTURE: the real ack format (success string, error string(s)) and WHEN it arrives (on
        // command receipt vs on move completion) are unknown until T15's live transcript capture. Until
        // then this recognizes no specific text at all -- it only distinguishes "the exchange did not time
        // out" (tolerant success) from "it did" (failure). Structured as its own method (rather than being
        // inlined at call sites) so T15 can add real ack/error-string recognition without changing this
        // method's signature or any caller.
        /// <summary>
        /// Tolerant interpretation of a move command's response: true unless <paramref name="exchange"/>
        /// timed out. Does NOT yet recognize any specific ack or error text -- see the LIVE-CAPTURE note
        /// above.
        /// </summary>
        public static bool ParseMoveAck(EatRawExchange exchange) {
            if (exchange == null) {
                throw new ArgumentNullException(nameof(exchange));
            }
            return !exchange.TimedOut;
        }

        // LIVE-CAPTURE: assumed `cp` reply layout. The real EAT wire format -- delimiter(s), whether the
        // four values are absolute counters or relative deltas, whether they arrive on one line or split
        // across several, and even whether they're in TR/TL/BR/BL order at all -- is unconfirmed until
        // T15's live transcript capture. The assumption implemented here: the first four integers found (in
        // textual order, scanning the raw lines top to bottom) are taken directly as DEVICE motor order
        // 1..4 = TR, TL, BR, BL (see TiltDevicePositions' own doc comment). Any non-digit character is
        // treated as a delimiter, so this tolerates commas, spaces, or a mix, across one or many lines.
        private static readonly Regex IntegerToken = new Regex(@"-?\d+", RegexOptions.Compiled);

        /// <summary>
        /// Tolerant extraction of the four per-motor position counters from a <c>cp</c> response. Throws
        /// <see cref="InvalidDeviceResponseException"/> (with the raw lines included in the message, for
        /// diagnosability) if fewer than four integers can be found -- expected to happen routinely on real
        /// hardware before T15 confirms the real format; callers treat that as "positions unknown", not a
        /// crash.
        /// </summary>
        public static TiltDevicePositions ParseCpPositions(EatRawExchange exchange) {
            if (exchange == null) {
                throw new ArgumentNullException(nameof(exchange));
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
    }
}
