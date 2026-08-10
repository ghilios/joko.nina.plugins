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
    /// An optional re-expression of <see cref="MathUtility.RejectionTest"/>'s residuals in units of the
    /// <b>standard error of the plotted median</b> rather than the star-ensemble scatter — wave 16 §2.1's
    /// <c>s = |r|·√N*</c>, where <c>N*</c> is the number of detected stars the point's σ was measured over.
    /// <c>AlglibHyperbolicFitting</c> already documents that its σ "overstates the uncertainty of the plotted
    /// median HFR by roughly √(detected stars)", so a point can sit well inside its own standard error and still
    /// be thrown out at z ≈ 1600. Two families are pre-registered (wave 16 design §2.2) and this type makes them
    /// mutually exclusive <i>by construction</i>, exactly as <see cref="MadFloorSpec"/> does for the MAD floor:
    ///
    /// <list type="bullet">
    /// <item><b>Family V — veto</b>: after the test has selected its argmax point, the rejection is
    /// <b>suppressed</b> when that point's <c>s</c> is below <c>t</c>. <b>Monotone.</b> The argmax cannot move,
    /// no rejection is added and none is redirected, so each model's rejection set is a <i>prefix</i> of its
    /// unvetoed set — which is the lemma the wave's counterfactual gate (V4′) is proved for.</item>
    /// <item><b>Family R — rank</b>: <c>errors_i ← r_i·√N*_i</c> <b>before</b> the median/MAD, i.e. the Grubbs
    /// statistic itself is computed in SEM units. <b>Not monotone.</b> <c>√N*</c> varies within a run, so the
    /// argmax <i>can</i> move to a point the unranked test never selected. That is why family R is
    /// <b>excluded from V4′ and from RECOMMEND by pre-registration</b>: applying a subset gate to a mechanism
    /// the subset lemma was never proved for is wave 14's V4 exactly. R is measured, reported and kept
    /// separate.</item>
    /// </list>
    ///
    /// <para><b>Family V is applied strictly AFTER the Grubbs comparison, never instead of it</b> (design §2.2),
    /// and its only outcome is "no rejection". The load-bearing part is that second clause: a veto that could
    /// <i>return a point</i> — a SEM comparison promoted from veto to decision — would let a threshold CREATE a
    /// rejection on a round the Grubbs test cleared, and the prefix property every counterfactual in the wave
    /// rests on would be gone. The ordering is kept because it makes that obvious to a reader, not because
    /// reordering alone would change an answer.</para>
    ///
    /// <para><b>N* travels as a side map, not on the point.</b> The count is supplied to
    /// <see cref="MathUtility.RejectionTest"/> as a <c>Func&lt;double, double&gt;</c> keyed by <c>p.X</c> — the
    /// shape <c>AlglibHyperbolicFitting.BuildResidualWeights</c> already uses for the 1/σ weights. Riding it on
    /// <c>ScatterErrorPoint.Tag</c> was rejected because <c>WeightRegularization.Regularize</c> rebuilds every
    /// point and would have to be edited to forward it — a change to a production class on the production path,
    /// in a wave whose whole claim is that the product is bit-identically unchanged at the default.</para>
    ///
    /// <para><b>This is harness-only and off by default.</b> <see cref="None"/> is <c>default(SemScaleSpec)</c>,
    /// so every optional parameter that defaults to <c>default</c> means "the shipped product's behaviour,
    /// unchanged". There is no persisted option and no UI control, and there cannot be one yet: N* is available
    /// where σ is COMPUTED and gone by the time the fit's points are BUILT (design §2.3), so the best this
    /// mechanism can reach is a costed recommendation.</para>
    /// </summary>
    public readonly struct SemScaleSpec : IEquatable<SemScaleSpec> {
        private readonly double vetoThreshold;
        private readonly bool rank;

        // Private: the ONLY ways to build a non-empty spec are the two named factories, so a spec that is both a
        // veto and a re-ranking is unrepresentable. That is deliberate — "exactly one family" is the
        // pre-registered invariant, and an invariant a caller can violate is documentation.
        private SemScaleSpec(double vetoThreshold, bool rank) {
            this.vetoThreshold = vetoThreshold;
            this.rank = rank;
        }

        /// <summary>No SEM criterion: <see cref="MathUtility.RejectionTest"/> behaves exactly as it does today.</summary>
        public static SemScaleSpec None => default;

        /// <summary>
        /// Family V. The selected point's rejection is suppressed when <c>s = |r|·√N* &lt; <paramref name="t"/></c>.
        /// <paramref name="t"/> = 0 is the control rung and returns <see cref="None"/>, so an arm spelled
        /// <c>--sem-veto 0</c> is bit-identical to an arm that passed no flag at all — which is what makes it a
        /// control.
        /// </summary>
        public static SemScaleSpec Veto(double t) {
            if (double.IsNaN(t) || double.IsInfinity(t) || t < 0.0) {
                throw new ArgumentOutOfRangeException(nameof(t), t, "A SEM veto threshold must be finite and >= 0");
            }
            return t == 0.0 ? None : new SemScaleSpec(t, false);
        }

        /// <summary>
        /// Family R. The Grubbs statistic is computed on <c>r_i·√N*_i</c> instead of <c>r_i</c>. There is no
        /// threshold: the rung either re-ranks or it does not.
        /// </summary>
        public static SemScaleSpec Rank() => new SemScaleSpec(0.0, true);

        /// <summary>True when no SEM criterion applies at all — the shipped behaviour.</summary>
        public bool IsNone => vetoThreshold == 0.0 && !rank;

        /// <summary>True for family V.</summary>
        public bool IsVeto => vetoThreshold > 0.0;

        /// <summary>True for family R.</summary>
        public bool IsRank => rank;

        /// <summary>Family V's <c>t</c>, or 0.</summary>
        public double VetoThreshold => vetoThreshold;

        public bool Equals(SemScaleSpec other) => vetoThreshold.Equals(other.vetoThreshold) && rank == other.rank;

        public override bool Equals(object obj) => obj is SemScaleSpec other && Equals(other);

        public override int GetHashCode() => (vetoThreshold, rank).GetHashCode();

        public static bool operator ==(SemScaleSpec a, SemScaleSpec b) => a.Equals(b);

        public static bool operator !=(SemScaleSpec a, SemScaleSpec b) => !a.Equals(b);

        /// <summary>
        /// Names the rung, in the words the wave's own design uses, ASCII only. Reports quote this verbatim: a
        /// report that does not name its own rung is a report that will be quoted at the wrong one.
        /// </summary>
        public override string ToString() {
            if (IsNone) {
                return "none (shipped behaviour)";
            }
            return IsVeto
                ? $"family V (SEM veto), t = {vetoThreshold.ToString("R", CultureInfo.InvariantCulture)}"
                : "family R (SEM rank)";
        }
    }
}
