#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace TestApp.SynthBank {

    /// <summary>
    /// F19(b) — names which bound, if any, decided a clamped derived exposure.
    ///
    /// <para><b>Why this exists as its own unit.</b> A value that IS the clamp and a free solution inside the band
    /// are reported identically by <c>expectedOptimal.exposureSeconds</c>, and they are not the same fact. On
    /// <c>D02_rich_135mm</c> the solve asks for far less than the 0.5 s floor: the stored exposure is the floor and
    /// the stored band collapses to [0.5, 0.5]. The zero width is the tell that nothing was measured — not that
    /// everything agreed — and wave 7's arm E then measured that dataset gaining 44 % of its σ_focus at 8× that
    /// "derived" exposure. The saturated SET is the work list for re-checking the floor itself, so it has to be
    /// readable rather than inferred.</para>
    /// </summary>
    public static class ExposureClamp {

        /// <summary>Neither bound decided the value: the solve landed inside the band.</summary>
        public const string None = "none";

        /// <summary>The solve asked for LESS than the minimum, so the reported value is the minimum.</summary>
        public const string Floor = "floor";

        /// <summary>The solve asked for MORE than the maximum, so the reported value is the maximum.</summary>
        public const string Ceiling = "ceiling";

        /// <summary>There was no solve at all (no catalog reader, or no on-frame star to solve against).</summary>
        public const string NotDerived = "not-derived";

        /// <summary>
        /// Which bound decided the clamped value, given the RAW pre-clamp solve.
        ///
        /// <para>Classifies the raw value against the bounds rather than comparing the clamped result to them,
        /// because a solve that legitimately lands exactly ON a bound is NOT saturated — the arithmetic asked for
        /// that number and got it. Comparing outputs cannot tell those two cases apart; comparing the input can.</para>
        ///
        /// <para>A non-finite raw (NaN from an unsolvable configuration, or an infinite one) is
        /// <see cref="NotDerived"/>, never a clamp verdict: "unmeasurable" is not "saturated", the same
        /// distinction F18's <c>W_detect</c> guard draws at the other end of the harness.</para>
        /// </summary>
        public static string Classify(double rawSeconds, double minSeconds, double maxSeconds) {
            if (!double.IsFinite(rawSeconds)) {
                return NotDerived;
            }
            if (rawSeconds < minSeconds) {
                return Floor;
            }
            if (rawSeconds > maxSeconds) {
                return Ceiling;
            }
            return None;
        }

        /// <summary>Whether <paramref name="clamp"/> is a verdict that a bound actually decided the value.</summary>
        public static bool Saturated(string clamp)
            => string.Equals(clamp, Floor, StringComparison.Ordinal)
            || string.Equals(clamp, Ceiling, StringComparison.Ordinal);
    }
}
