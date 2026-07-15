#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.AsgEat {

    /// <summary>
    /// The two candidate strategies the ASG EAT firmware might use to encode a negative move: either the
    /// mnemonic always stays the axis's positive corner/edge and the sign rides on the numeric argument
    /// (<c>tr,-5</c>), or the firmware only ever accepts non-negative arguments and the caller must instead
    /// select the mechanically opposite mnemonic (<c>bl,5</c>). The actual device behavior is UNKNOWN until
    /// a live hardware capture (plan task T15) — see <see cref="EatCommands.DefaultSignEncoding"/>.
    /// </summary>
    public enum EatSignEncoding {
        SignedArgument,
        OppositeMnemonic
    }

    /// <summary>
    /// Pure formatting module: converts a <see cref="TiltMoveAxis"/> + signed step count into the ASG EAT
    /// wire command string <c>&lt;mnemonic&gt;,&lt;value&gt;</c>. No I/O — see <c>EatSerialTransport.cs</c>
    /// for the actual serial send. The mnemonic table below is the correctness anchor for this file: it is
    /// derived from the device's physical command vocabulary (plan's "Device facts (ASG EAT)" section) and
    /// must not be edited without re-verifying against that source — a wrong mnemonic here is an
    /// EEPROM-persisted wrong-way motor move on real hardware.
    /// </summary>
    public static class EatCommands {

        // Positive mnemonic per axis (the mnemonic used under EatSignEncoding.SignedArgument, and for
        // non-negative steps under EatSignEncoding.OppositeMnemonic).
        private static readonly IReadOnlyDictionary<TiltMoveAxis, string> PositiveMnemonics = new Dictionary<TiltMoveAxis, string> {
            [TiltMoveAxis.DiagonalA] = "tr",
            [TiltMoveAxis.DiagonalB] = "tl",
            [TiltMoveAxis.EdgeVertical] = "tp",
            [TiltMoveAxis.EdgeHorizontal] = "rt",
            [TiltMoveAxis.Backfocus] = "bf",
        };

        // Opposite mnemonic per axis (used for negative steps under EatSignEncoding.OppositeMnemonic).
        // Backfocus has NO opposite mnemonic on the real device -- see the explicit guard in Format below;
        // this table intentionally omits a Backfocus entry so any accidental lookup fails loudly rather than
        // silently returning a wrong string.
        private static readonly IReadOnlyDictionary<TiltMoveAxis, string> OppositeMnemonics = new Dictionary<TiltMoveAxis, string> {
            [TiltMoveAxis.DiagonalA] = "bl",
            [TiltMoveAxis.DiagonalB] = "br",
            [TiltMoveAxis.EdgeVertical] = "bt",
            [TiltMoveAxis.EdgeHorizontal] = "lt",
        };

        /// <summary>
        /// The sign-encoding convention this formatter assumes until the live hardware capture (plan task
        /// T15) confirms or corrects it. LIVE-CAPTURE: this is the seam T15 flips if the device turns out to
        /// require the opposite-mnemonic convention instead.
        /// </summary>
        public const EatSignEncoding DefaultSignEncoding = EatSignEncoding.SignedArgument;

        /// <summary>The position-query command. Deliberately no zero (<c>zr</c>) command -- zeroing positions is the vendor app's job, never the plugin's.</summary>
        public static string PositionQuery() => "cp";

        /// <summary>
        /// Formats <paramref name="move"/>'s axis and step count into an ASG EAT wire command string.
        /// See <see cref="Format(TiltMoveAxis, int, EatSignEncoding)"/> for the full formatting rules.
        /// </summary>
        public static string Format(TiltAdapterMove move, EatSignEncoding encoding = DefaultSignEncoding) {
            if (move == null) {
                throw new ArgumentNullException(nameof(move));
            }
            return Format(move.Axis, move.Steps, encoding);
        }

        /// <summary>
        /// Formats an axis + signed step count into an ASG EAT wire command string (<c>&lt;mnemonic&gt;,&lt;value&gt;</c>).
        ///
        /// Under <see cref="EatSignEncoding.SignedArgument"/>: always the axis's positive mnemonic, with the
        /// sign carried on the numeric value (e.g. DiagonalA,-5 -&gt; "tr,-5").
        ///
        /// Under <see cref="EatSignEncoding.OppositeMnemonic"/>: positive steps use the positive mnemonic
        /// with the magnitude (e.g. DiagonalA,+5 -&gt; "tr,5"); negative steps use the OPPOSITE mnemonic
        /// with the magnitude (e.g. DiagonalA,-5 -&gt; "bl,5").
        ///
        /// Backfocus has no opposite mnemonic on the real device, so a negative Backfocus move ALWAYS uses a
        /// signed argument on the "bf" mnemonic, regardless of <paramref name="encoding"/> -- this is the one
        /// sign unknown that is truly blocking (see plan task T15 item 3), asserted here so a future
        /// encoding flip cannot silently break it.
        ///
        /// <paramref name="steps"/> == 0 is not a move the planner is ever expected to emit (it never emits
        /// zero-magnitude moves); this formatter treats it as a caller error and throws
        /// <see cref="ArgumentException"/> rather than silently emitting an ambiguous "no-op" command.
        /// </summary>
        public static string Format(TiltMoveAxis axis, int steps, EatSignEncoding encoding = DefaultSignEncoding) {
            if (steps == 0) {
                throw new ArgumentException("Cannot format a zero-magnitude move; the planner must never emit one.", nameof(steps));
            }
            if (!PositiveMnemonics.TryGetValue(axis, out var positiveMnemonic)) {
                throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown tilt move axis.");
            }

            // Backfocus has no opposite mnemonic on the real device: a negative backfocus move must ALWAYS be
            // formatted as a signed argument on "bf", regardless of the requested encoding. This is the one
            // blocking sign unknown -- do not let a future encoding change silently break it.
            if (axis == TiltMoveAxis.Backfocus) {
                return FormattableString.Invariant($"{positiveMnemonic},{steps}");
            }

            switch (encoding) {
                case EatSignEncoding.SignedArgument:
                    return FormattableString.Invariant($"{positiveMnemonic},{steps}");

                case EatSignEncoding.OppositeMnemonic:
                    if (steps > 0) {
                        return FormattableString.Invariant($"{positiveMnemonic},{steps}");
                    }
                    if (!OppositeMnemonics.TryGetValue(axis, out var oppositeMnemonic)) {
                        throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis has no opposite mnemonic.");
                    }
                    return FormattableString.Invariant($"{oppositeMnemonic},{-steps}");

                default:
                    throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown sign encoding.");
            }
        }
    }
}
