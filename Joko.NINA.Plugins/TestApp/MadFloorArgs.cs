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
using System.Collections.Generic;
using System.Globalization;

namespace TestApp {

    /// <summary>
    /// Command-line surface for the wave-14 MAD-floor ladder (F45(b)), exposed on <c>af-fit</c> only. Two
    /// pre-registered families, one flag each:
    ///
    /// <list type="bullet">
    /// <item><c>--mad-floor &lt;f&gt;</c> — family A, the absolute floor <c>scale ← max(scale, f)</c>.</item>
    /// <item><c>--mad-floor-rel &lt;α&gt;</c> (also spelled <c>--round1-floor</c>) — family B,
    /// <c>scale_k ← max(scale_k, α·scale_1)</c>.</item>
    /// </list>
    ///
    /// <para><b>Naming more than one RUNG is an error, not a precedence rule.</b> The families are mutually
    /// exclusive by pre-registration, and a silent "last one wins" would let an arm run at a rung nobody named
    /// while its report claimed the other one. What counts as "naming a rung" is a <b>non-zero</b> value: a
    /// value of 0 is the control, i.e. "this family contributes no floor", so
    /// <c>--mad-floor 0.0 --round1-floor 0.50</c> names exactly one rung and is accepted — which is precisely
    /// how the wave's own driver spells every arm, including the control (<c>--mad-floor 0.00
    /// --round1-floor 0.0</c>). Two non-zero values is the error, and no precedence is involved in accepting the
    /// zero form because there is nothing to choose between.</para>
    ///
    /// <para><b>The flags are matched EXACTLY, never by prefix.</b> This is the same point wave 13's
    /// <c>LandingWritebackTests</c> made about <c>--update-run-folder</c>: with a prefix match, a future
    /// <c>--mad-floor-never</c> or <c>--no-mad-floor</c> would silently switch the floor ON. One definition of
    /// each spelling lives here, so the runner and its tests cannot disagree about it.</para>
    /// </summary>
    internal static class MadFloorArgs {

        /// <summary>Family A's flag.</summary>
        internal const string AbsoluteFlag = "--mad-floor";

        /// <summary>Family B's flag.</summary>
        internal const string RelativeFlag = "--mad-floor-rel";

        /// <summary>
        /// Family B's other accepted spelling. The wave-14 design (§2.8) and plan both write the family-B flag as
        /// <c>--round1-floor</c> while the Stage-B build instruction writes it as <c>--mad-floor-rel</c>; both are
        /// accepted so a driver script written against either document reaches the same rung rather than silently
        /// running the control. Naming the same family twice on one command line is still an error.
        /// </summary>
        internal const string RelativeFlagAlias = "--round1-floor";

        /// <summary>
        /// Builds the rung this invocation asked for. Returns <see cref="MadFloorSpec.None"/> when no floor flag
        /// names a non-zero rung — whether that is because no flag was given at all, or because every flag given
        /// carried 0. So <c>--mad-floor 0.00</c>, <c>--mad-floor 0.0 --round1-floor 0.0</c> and no flags are all
        /// the same control rung, which is what lets the control's report be the previous wave's report.
        ///
        /// <para>Every flag present is still syntax-checked even when its value is 0, so a typo on the family
        /// that happens to be switched off is still a loud failure rather than a silent one.</para>
        /// </summary>
        /// <exception cref="ArgumentException">
        /// More than one flag named a non-zero rung, or a flag was given without a parseable non-negative value.
        /// </exception>
        internal static MadFloorSpec Parse(string[] args) {
            var named = new List<(string Flag, MadFloorSpec Spec)>();
            foreach (var flag in new[] { AbsoluteFlag, RelativeFlag, RelativeFlagAlias }) {
                if (!TryFindValue(args, flag, out var raw)) {
                    continue;
                }
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) {
                    throw new ArgumentException($"{flag} needs a number; got '{raw}'.");
                }
                MadFloorSpec spec;
                try {
                    spec = flag == AbsoluteFlag ? MadFloorSpec.Absolute(value) : MadFloorSpec.RelativeToFirstRound(value);
                } catch (ArgumentOutOfRangeException ex) {
                    throw new ArgumentException($"{flag} {raw}: {ex.Message}", ex);
                }
                if (!spec.IsNone) {
                    named.Add((flag, spec));
                }
            }
            if (named.Count > 1) {
                throw new ArgumentException(
                    $"{string.Join(" and ", named.ConvertAll(g => g.Flag))} each name a non-zero rung. The MAD-floor " +
                    $"families are mutually exclusive: at most ONE of {AbsoluteFlag} (family A), {RelativeFlag} / " +
                    $"{RelativeFlagAlias} (family B) may be non-zero. There is no precedence rule and there will not be one.");
            }
            return named.Count == 0 ? MadFloorSpec.None : named[0].Spec;
        }

        /// <summary>The rung, named the way the wave's reports quote it.</summary>
        internal static string Describe(MadFloorSpec spec) => spec.ToString();

        /// <summary>
        /// The suffix a caption carries so the table itself says which rung produced it — empty when no floor
        /// applies, so a control-rung report is byte-for-byte the report the previous wave produced.
        /// </summary>
        internal static string CaptionTag(MadFloorSpec spec) => spec.IsNone ? string.Empty : $" [MAD floor: {Describe(spec)}]";

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
    }
}
