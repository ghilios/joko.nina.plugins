#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// An optional lower bound on the residual <b>scale</b> that <see cref="MathUtility.RejectionTest"/> divides
    /// by — F45(b)'s "a point sitting well inside its own error bar should not be an outlier". Two families are
    /// pre-registered (wave 14 design §2.5) and this type makes them mutually exclusive <i>by construction</i>,
    /// so "which family is this?" is never a question about how a caller happened to fill two loose doubles:
    ///
    /// <list type="bullet">
    /// <item><b>Family A — absolute</b>: <c>scale_k ← max(scale_k, f)</c>, the same <c>f</c> every round.</item>
    /// <item><b>Family B — non-collapsing</b>: <c>scale_k ← max(scale_k, α·scale_1)</c>, anchored to <i>that
    /// model's own round-1 scale</i>. On round 1 there is no anchor yet, so the resolved floor is 0 and
    /// <b>round 1 is bit-identical to the unfloored run by construction</b>, not by tuning.</item>
    /// </list>
    ///
    /// <para><b>The floor is applied AFTER the degenerate-scale guards in <see cref="MathUtility.RejectionTest"/>,
    /// never instead of them</b> (design §2.5). Flooring the MAD <i>before</i> the stdDev fallback would make the
    /// fallback dead code and could make the scale <i>smaller</i> than it is today (MAD 0 → floor <c>f</c>, where
    /// the fallback would have produced a larger stdDev) — which would let a floor <b>create</b> a rejection.
    /// Flooring the effective scale is monotone: <c>max(scale, f) ≥ scale</c>, so every z can only shrink and a
    /// floor can only ever <b>suppress</b> a rejection, never add or redirect one. The whole counterfactual
    /// framework of the wave rests on that monotonicity, so the variant that breaks it is excluded here rather
    /// than hoped away.</para>
    ///
    /// <para><b>This is harness-only and off by default.</b> <see cref="None"/> is <c>default(MadFloorSpec)</c>,
    /// so every optional parameter that defaults to <c>default</c> means "the shipped product's behaviour,
    /// unchanged". There is no persisted option and no UI control.</para>
    /// </summary>
    public readonly struct MadFloorSpec : IEquatable<MadFloorSpec> {
        private readonly double absolute;
        private readonly double firstRoundFactor;

        // Private: the ONLY ways to build a non-empty spec are the two named factories, so a spec that carries
        // both an absolute floor and a round-1 factor is unrepresentable. That is deliberate — "exactly one
        // family" is the pre-registered invariant, and an invariant a caller can violate is documentation.
        private MadFloorSpec(double absolute, double firstRoundFactor) {
            this.absolute = absolute;
            this.firstRoundFactor = firstRoundFactor;
        }

        /// <summary>No floor: <see cref="MathUtility.RejectionTest"/> behaves exactly as it does today.</summary>
        public static MadFloorSpec None => default;

        /// <summary>
        /// Family A. <c>scale ← max(scale, <paramref name="f"/>)</c> on every round.
        /// <paramref name="f"/> = 0 is the control rung and returns <see cref="None"/>, so the <c>f = 0.00</c>
        /// arm is bit-identical to an arm that passed no flag at all — which is what makes it a control.
        /// </summary>
        public static MadFloorSpec Absolute(double f) {
            if (double.IsNaN(f) || double.IsInfinity(f) || f < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(f), f, "An absolute MAD floor must be finite and >= 0");
            }
            return f == 0.0 ? None : new MadFloorSpec(f, 0.0);
        }

        /// <summary>
        /// Family B. <c>scale_k ← max(scale_k, <paramref name="alpha"/>·scale_1)</c>, where <c>scale_1</c> is the
        /// effective scale the same model used on its own round 1. <paramref name="alpha"/> = 0 returns
        /// <see cref="None"/>.
        /// </summary>
        public static MadFloorSpec RelativeToFirstRound(double alpha) {
            if (double.IsNaN(alpha) || double.IsInfinity(alpha) || alpha < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "A round-1 floor factor must be finite and >= 0");
            }
            return alpha == 0.0 ? None : new MadFloorSpec(0.0, alpha);
        }

        /// <summary>True when no floor applies at all — the shipped behaviour.</summary>
        public bool IsNone => absolute == 0.0 && firstRoundFactor == 0.0;

        /// <summary>True for family A.</summary>
        public bool IsAbsolute => absolute > 0.0;

        /// <summary>True for family B.</summary>
        public bool IsRelativeToFirstRound => firstRoundFactor > 0.0;

        /// <summary>Family A's <c>f</c>, or 0.</summary>
        public double AbsoluteFloor => absolute;

        /// <summary>Family B's <c>α</c>, or 0.</summary>
        public double FirstRoundFactor => firstRoundFactor;

        /// <summary>
        /// The concrete floor to apply on a given round. <paramref name="firstRoundScale"/> is the effective
        /// scale this model used on its own round 1, or 0 / NaN when there is no round-1 anchor yet (i.e. on
        /// round 1 itself, or when round 1 was degenerate). A missing anchor resolves to <b>0</b>, never to
        /// something guessed — that is precisely why family B cannot touch round 1.
        /// </summary>
        public double ResolveFloor(double firstRoundScale) {
            if (absolute > 0.0) {
                return absolute;
            }
            if (firstRoundFactor <= 0.0) {
                return 0.0;
            }
            if (double.IsNaN(firstRoundScale) || double.IsInfinity(firstRoundScale) || firstRoundScale <= 0.0) {
                return 0.0;
            }
            return firstRoundFactor * firstRoundScale;
        }

        public bool Equals(MadFloorSpec other) => absolute.Equals(other.absolute) && firstRoundFactor.Equals(other.firstRoundFactor);

        public override bool Equals(object obj) => obj is MadFloorSpec other && Equals(other);

        public override int GetHashCode() => (absolute, firstRoundFactor).GetHashCode();

        public static bool operator ==(MadFloorSpec a, MadFloorSpec b) => a.Equals(b);

        public static bool operator !=(MadFloorSpec a, MadFloorSpec b) => !a.Equals(b);

        /// <summary>
        /// Names the rung, in the words the wave's own design uses. Reports quote this verbatim: a report that
        /// does not name its own rung is a report that will be quoted at the wrong one.
        /// </summary>
        public override string ToString() {
            if (IsNone) {
                return "none (shipped behaviour)";
            }
            return IsAbsolute
                ? $"family A (absolute), f = {absolute.ToString("R", CultureInfo.InvariantCulture)}"
                : $"family B (relative to round 1), alpha = {firstRoundFactor.ToString("R", CultureInfo.InvariantCulture)}";
        }
    }
}
