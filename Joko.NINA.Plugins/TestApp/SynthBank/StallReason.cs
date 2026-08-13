#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using System.Globalization;

namespace TestApp.SynthBank {

    /// <summary>
    /// P5 (F34) — what <c>synth-validate</c>'s round loop says when a round applies nothing and the step it stopped
    /// at is outside the scenario's tolerance band. HARNESS ONLY: this is the measuring device's own copy, not the
    /// plugin's.
    ///
    /// <para><b>The defect.</b> The message asserted a CAUSE it never checked. It read, on every stall,
    /// "a no-op recommendation from a degenerate fit, not convergence" — regardless of whether the recommendation
    /// was degenerate at all. Wave 24 measured the consequence on <c>D01</c>/S1: <c>degenerateReason</c> null,
    /// <c>halfWidth</c> 6.0877, R^2 0.99999999999994 — a perfectly usable fit — and the harness blamed a degenerate
    /// one. A wave-23 register entry then grouped that cell with two genuinely degenerate ones on the strength of
    /// the message. A wrong message became a wrong register entry, which is the whole reason a harness's copy is
    /// held to the same standard as the product's.</para>
    ///
    /// <para><b>The rule.</b> Read <c>StepRecommendationSnapshot.DegenerateReason</c> and branch on it. Non-null
    /// keeps the old wording and appends the token, so the exit is identifiable rather than merely asserted.
    /// Null says what is actually true and quotes the number that explains it — the HFR dynamic range the sweep
    /// MEASURED, against the band the step is sized from. That range is the discriminating variable of this whole
    /// stall family, so a reader gets the diagnosis and not just the verdict.</para>
    ///
    /// <para><b>Why it is here and not inline in the round loop.</b> <c>SynthValidateRunner</c> renders frames and
    /// runs the optimizer and cannot be source-linked into the test project, so a message built inline there cannot
    /// be tested at all — the same constraint that put <see cref="BinningRevisitPolicy"/> in its own file. Reverting
    /// P5 = restore <c>SynthValidateRunner.cs</c> from its post-P4 snapshot, delete this file, its csproj link line
    /// and its test. None of that touches P4.</para>
    /// </summary>
    public static class StallReason {

        /// <summary>
        /// The <c>stoppedReason</c> for a round that applied nothing and landed outside the tolerance band.
        /// </summary>
        /// <param name="stepSize">The step the loop stopped at.</param>
        /// <param name="stepBand">Half-width of the tolerance band (<see cref="ConvergenceBand.HalfWidth"/>).</param>
        /// <param name="stepBehavioral">The behavioral fixed point the band is centred on.</param>
        /// <param name="recommendation">The round's step recommendation, or null when the round recorded none.</param>
        public static string Describe(int stepSize, double stepBand, double stepBehavioral,
                                      StepRecommendationSnapshot recommendation) {
            var prefix = string.Format(CultureInfo.InvariantCulture,
                "stalled (round applied nothing, but step {0} is outside the {1:0.###} tolerance band of "
                + "step_behavioral {2:0.###}) -- ", stepSize, stepBand, stepBehavioral);
            return prefix + Cause(recommendation);
        }

        /// <summary>
        /// The half of the message that names a cause. Split out because it is the half that was WRONG, and a test
        /// that pins it should not have to reproduce the band arithmetic to do so.
        /// </summary>
        public static string Cause(StepRecommendationSnapshot recommendation) {
            // No snapshot at all is a could-not-look, and it gets said rather than defaulted to either cause. This
            // is the same rule the recommender's own DegenerateReason exists to express.
            if (recommendation == null) {
                return "a no-op recommendation whose step recommendation was not recorded, so the cause could not "
                       + "be read, not convergence";
            }
            var reason = recommendation.DegenerateReason;
            if (!string.IsNullOrEmpty(reason)) {
                return $"a no-op recommendation from a degenerate fit ({reason}), not convergence";
            }
            var range = recommendation.SampledHfrRange;
            var measured = double.IsFinite(range)
                ? string.Format(CultureInfo.InvariantCulture, "sampled HFR range {0:0.###}x", range)
                : "sampled HFR range unmeasurable";
            return string.Format(CultureInfo.InvariantCulture,
                "a no-op recommendation from a NON-degenerate fit ({0}, band {1:0.###}x), not convergence",
                measured, StepSizeRecommender.HfrThresholdMultiple);
        }
    }
}
