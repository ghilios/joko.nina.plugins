#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Globalization;

namespace TestApp {

    /// <summary>
    /// Command-line surface for the wave-16 SEM ladder (F45(b) in the units the plotted error bar actually
    /// implies), exposed on <c>af-fit</c> only. Two pre-registered families, one flag each:
    ///
    /// <list type="bullet">
    /// <item><c>--sem-veto &lt;t&gt;</c> — family V, suppress a rejection whose selected point sits at
    /// <c>s = |r|·√N* &lt; t</c>.</item>
    /// <item><c>--sem-rank</c> — family R, compute the Grubbs statistic on <c>r·√N*</c>. A switch, not a value:
    /// the rung either re-ranks or it does not.</item>
    /// </list>
    ///
    /// <para><b>Naming more than one RUNG is an error, not a precedence rule</b> — and that includes naming a SEM
    /// family <i>and</i> a MAD-floor family in the same invocation, because two mechanisms in one arm is an arm
    /// nobody pre-registered. As in <see cref="MadFloorArgs"/>, what counts as "naming a rung" is a
    /// <b>non-zero</b> value, so <c>--sem-veto 0</c> is the control and composes with anything.</para>
    ///
    /// <para><b>The flags are matched EXACTLY, never by prefix.</b> Same lesson as <c>--mad-floor</c> and
    /// <c>--update-run-folder</c>: with a prefix match a future <c>--sem-veto-never</c> or <c>--no-sem-rank</c>
    /// would silently switch the criterion ON, and the arm would report a rung it did not run.</para>
    /// </summary>
    internal static class SemScaleArgs {

        /// <summary>Family V's flag. Takes a threshold.</summary>
        internal const string VetoFlag = "--sem-veto";

        /// <summary>Family R's flag. A switch — it takes no value.</summary>
        internal const string RankFlag = "--sem-rank";

        /// <summary>
        /// Builds the rung this invocation asked for. Returns <see cref="SemScaleSpec.None"/> when no SEM flag
        /// names a non-zero rung — whether that is because no flag was given at all, or because
        /// <c>--sem-veto</c> carried 0. So <c>--sem-veto 0.00</c> and no flags are the same control rung, which
        /// is what lets the control's report be the previous wave's report, byte for byte.
        ///
        /// <para>The MAD-floor flags are parsed here too, purely to reject an invocation that names one of each.
        /// They are two different answers to F45(b) and were pre-registered as alternatives; an arm running both
        /// is measuring neither.</para>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Both SEM families were named, a SEM family and a MAD-floor family were named together, or
        /// <c>--sem-veto</c> was given without a parseable non-negative value.
        /// </exception>
        internal static SemScaleSpec Parse(string[] args) {
            SemScaleSpec veto = SemScaleSpec.None;
            if (TryFindValue(args, VetoFlag, out var raw)) {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) {
                    throw new ArgumentException($"{VetoFlag} needs a number; got '{raw}'.");
                }
                try {
                    veto = SemScaleSpec.Veto(t);
                } catch (ArgumentOutOfRangeException ex) {
                    throw new ArgumentException($"{VetoFlag} {raw}: {ex.Message}", ex);
                }
            }
            var rank = HasFlag(args, RankFlag);

            if (!veto.IsNone && rank) {
                throw new ArgumentException(
                    $"{VetoFlag} and {RankFlag} each name a rung. The SEM families are mutually exclusive: family " +
                    "V vetoes a rejection the ordinary test already made, family R changes which point the test " +
                    "picks. There is no precedence rule and there will not be one.");
            }
            var spec = rank ? SemScaleSpec.Rank() : veto;

            // The cross-family check. MadFloorArgs.Parse is the ONE definition of what a MAD rung is, so a zero
            // floor stays a non-rung here exactly as it does there, and the wave-14 driver's `--mad-floor 0.00
            // --round1-floor 0.0` spelling composes with a SEM rung instead of aborting the arm.
            var madFloor = MadFloorArgs.Parse(args);
            if (!spec.IsNone && !madFloor.IsNone) {
                throw new ArgumentException(
                    $"A SEM rung ({spec}) and a MAD-floor rung ({madFloor}) were named in the same invocation. " +
                    "They are alternative answers to the same question and were pre-registered as alternatives; " +
                    "an arm that applies both is an arm nobody named, and its report would quote one of the two.");
            }
            return spec;
        }

        /// <summary>The rung, named the way the wave's reports quote it.</summary>
        internal static string Describe(SemScaleSpec spec) => spec.ToString();

        /// <summary>
        /// The short, space-free tag the per-round <c>SEM detail</c> line carries: <c>none</c>, <c>R</c>, or
        /// <c>V&lt;t&gt;</c> spelled the way the driver spells its rungs (<c>V0.07</c>, <c>V1.00</c>,
        /// <c>V2.00</c>, <c>V4.00</c>). A threshold that does not round-trip at two decimals gets full precision
        /// instead — a tag is what a report is quoted BY, and one that rounds two rungs to the same name is worse
        /// than one that looks untidy. ASCII only.
        /// </summary>
        internal static string Tag(SemScaleSpec spec) {
            if (spec.IsNone) {
                return "none";
            }
            if (spec.IsRank) {
                return "R";
            }
            var t = spec.VetoThreshold;
            var twoDecimals = t.ToString("0.00", CultureInfo.InvariantCulture);
            var roundTrips = double.TryParse(twoDecimals, NumberStyles.Float, CultureInfo.InvariantCulture, out var back) && back == t;
            return "V" + (roundTrips ? twoDecimals : t.ToString("R", CultureInfo.InvariantCulture));
        }

        // Exact, case-insensitive match on the flag token itself — string.Equals, never StartsWith/Contains.
        // A flag that appears with no value following it THROWS rather than reading as absent: "the flag was
        // typed but the arm ran at the control rung" is exactly the silent miss this whole item is measuring.
        private static bool TryFindValue(string[] args, string flag, out string value) {
            value = null;
            if (args == null) {
                return false;
            }
            for (int i = 0; i < args.Length; ++i) {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (i + 1 >= args.Length) {
                    throw new ArgumentException($"{flag} needs a number; it was given with no value.");
                }
                value = args[i + 1];
                return true;
            }
            return false;
        }

        // Family R is a switch: presence is the rung. Exact match for the same reason as above.
        private static bool HasFlag(string[] args, string flag) {
            if (args == null) {
                return false;
            }
            foreach (var a in args) {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }
    }
}
