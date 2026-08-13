#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;

namespace TestApp.SynthBank {

    /// <summary>
    /// P2 -- the binning-revisit deferral bound for <see cref="SynthValidateRunner"/>'s round loop. HARNESS ONLY:
    /// nothing in the shipping plugin defers anything on a binning change, so this is an INSTRUMENT REPAIR and not
    /// a product fix. The wizard builds one summary carrying the step size and the binning factor together, from
    /// the same fit, in the same pass, and the user drives the next round by hand.
    ///
    /// <para><b>The defect.</b> The round loop applies binning FIRST and defers the exposure and step updates by a
    /// round, because changing the binning factor invalidates the SNR-per-binned-pixel exposure measurement and the
    /// HFR-derived step geometry the round just took. When the binning recommendation ENTERS A CYCLE -- 2, then 1,
    /// then 2, then 1 -- every round is a binning round, so the step update never runs at all and the run reports a
    /// step that was never allowed to move. Four rounds of four, on a real cell.</para>
    ///
    /// <para><b>The rule, and why it is a REVISIT rule and not a counter.</b> Defer only when the recommended
    /// factor has NEVER been in effect during this scenario. On a revisit the deferral's own justification is
    /// empty: a measurement at that factor already exists, because the run has already been there. Two counters
    /// were considered and both are wrong. "The applied value has not changed for N rounds" never fires on the
    /// oscillating form, where the applied value changes EVERY round. "N consecutive deferrals" does fire, but it
    /// also fires on a legitimate monotone walk 1 -> 2 -> 3, where both deferrals are honest because no
    /// measurement at factor 3 exists yet. The revisit rule changes behaviour if and only if the recommendation
    /// has entered a cycle, which is precisely the defect.</para>
    ///
    /// <para><b>Why it is here and not inline in the round loop.</b> Reverting P2 must not be able to take P1 or P3
    /// with it, and the round loop cannot be unit tested -- it renders frames and runs the optimizer. This file
    /// holds every decision P2 makes; <see cref="SynthValidateRunner"/> holds only the call, the state field and
    /// the branch. See this wave's design section 3.2.</para>
    /// </summary>
    public static class BinningRevisitPolicy {

        /// <summary>What the round loop should do about this round's binning recommendation.</summary>
        public sealed class Decision {

            /// <summary>True when the recommendation asks for a factor other than the one in effect.</summary>
            public bool BinningDiffers { get; set; }

            /// <summary>True when the round applies the binning change (identical to <see cref="BinningDiffers"/>;
            /// carried separately because the round record has a field of that name).</summary>
            public bool BinningApplied { get; set; }

            /// <summary>True when the exposure/step block is deferred out of this round.</summary>
            public bool StepDeferred { get; set; }

            /// <summary>P2's ENGAGEMENT MARKER: true when a binning change was applied WITHOUT deferring, because
            /// the recommended factor had already been in effect earlier in this scenario. A fix that cannot report
            /// whether it engaged is not finished.</summary>
            public bool DeferralBoundReached { get; set; }

            /// <summary>The factor in effect before this round's update policy ran.</summary>
            public int PreviousFactor { get; set; }

            /// <summary>The factor in effect after it ran.</summary>
            public int NewFactor { get; set; }

            /// <summary>The <c>applied.reasons</c> line for the binning change, or null when nothing changed.</summary>
            public string Reason { get; set; }

            /// <summary>True when the round should go on to run the exposure and step updates.</summary>
            public bool RunExposureAndStep => !StepDeferred;
        }

        /// <summary>The visited-factor set for a scenario, seeded with the factor the scenario STARTS at.
        /// Seeding matters: without it the very first change would look like a first visit to a factor the run had
        /// in fact already measured at, and the rule would be one round late.</summary>
        public static HashSet<int> NewVisitedSet(int initialFactor) {
            return new HashSet<int> { initialFactor };
        }

        /// <summary>
        /// Decides whether this round defers, AND records the newly applied factor in
        /// <paramref name="visitedFactors"/>. The recording lives here rather than at the call site because
        /// forgetting it is exactly the failure that would make the revisit rule silently never fire, and a rule
        /// that never fires reports the same thing as no rule at all.
        /// </summary>
        /// <param name="currentFactor">The detection binning factor in effect for this round.</param>
        /// <param name="recommendedFactor">This round's recommendation, or null when the gate did not produce one.</param>
        /// <param name="visitedFactors">Every factor that has been in effect in this scenario. Mutated.</param>
        public static Decision Decide(int currentFactor, int? recommendedFactor, ISet<int> visitedFactors) {
            var decision = new Decision {
                PreviousFactor = currentFactor,
                NewFactor = currentFactor
            };
            if (!recommendedFactor.HasValue || recommendedFactor.Value == currentFactor) {
                return decision;
            }

            var recommended = recommendedFactor.Value;
            var revisit = visitedFactors != null && visitedFactors.Contains(recommended);

            decision.BinningDiffers = true;
            decision.BinningApplied = true;
            decision.NewFactor = recommended;
            decision.StepDeferred = !revisit;
            decision.DeferralBoundReached = revisit;
            decision.Reason = revisit
                ? $"binning {currentFactor} -> {recommended} (already measured at factor {recommended}; not deferring)"
                : $"binning {currentFactor} -> {recommended} (deferring exposure/step this round)";

            visitedFactors?.Add(recommended);
            return decision;
        }
    }
}
