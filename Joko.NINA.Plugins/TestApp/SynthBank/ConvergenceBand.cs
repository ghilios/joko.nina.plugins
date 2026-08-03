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
    /// The one definition of "this step counts as converged", shared by <c>SynthValidateRunner</c>'s two stop
    /// branches so they cannot drift apart — which they had.
    ///
    /// <para><b>The bug this exists to prevent.</b> A round that applied nothing used to set
    /// <c>stoppedReason = "converged (round applied nothing)"</c> unconditionally, and the terminal's
    /// <c>Converged</c> flag was then derived by string-matching that reason for a "converged" prefix. But
    /// <c>StepSizeRecommender.Degenerate</c> holds the CURRENT step whenever the fit is unusable — no
    /// <c>Fitting</c>, a non-finite vertex, a non-positive minimum HFR, or the 3× band never crossed — so a
    /// broken fit produces a no-op recommendation that is byte-identical to the recommender genuinely agreeing.
    /// The loop read "nothing changed" as success. Measured on <c>D05_tec140_1000mm</c> S2: <c>converged: true</c>
    /// at <c>finalStepSize</c> 140 against <c>stepBehavioral</c> 35, while assertion A3 scored that same number
    /// against [21, 56] and FAILED it — the two verdicts contradicting each other inside one JSON object. The
    /// effect is to inflate convergence counts on exactly the runs that are most broken.</para>
    ///
    /// <para><b>Why the tolerance band and not A3's.</b> A3 asserts <c>[0.6, 1.6] × step_behavioral</c>; this band
    /// is <c>±max(0.5, tolerance · |step_behavioral|)</c>, i.e. <c>[0.6, 1.4] ×</c> at the default tolerance of
    /// 0.4. It is a strict SUBSET sharing A3's lower bound, so a run this calls converged can never be one A3
    /// rejects. Choosing the looser band would have re-opened the same contradiction from the other side.</para>
    ///
    /// <para>Pure, dependency-free and source-linked into the test project, because a predicate that decides
    /// whether a harness is reporting success needs its own tests more than most code does. Three of the four
    /// harness-calibration bugs found on this bank were in this family.</para>
    /// </summary>
    public static class ConvergenceBand {

        /// <summary>
        /// Half-width of the band a step must land inside to be called converged, or <see cref="double.NaN"/> when
        /// <paramref name="stepBehavioral"/> never reached a finite positive fixed point and no band is definable.
        ///
        /// <para>The <c>0.5</c> floor keeps the band meaningful for very small behavioral steps, where a pure
        /// fractional tolerance would demand exactness the integer step grid cannot express.</para>
        /// </summary>
        public static double HalfWidth(double stepBehavioral, double tolerance) {
            if (!double.IsFinite(stepBehavioral) || stepBehavioral <= 0.0) {
                return double.NaN;
            }
            return Math.Max(0.5, tolerance * Math.Abs(stepBehavioral));
        }

        /// <summary>
        /// Whether <paramref name="step"/> is inside the band. <b>False when the band is undefined</b> — an
        /// unknown band is not a passing one, and <see cref="Contains"/> is only ever asked in order to make a
        /// positive claim. Callers that must still stop on an undefined band handle that case explicitly rather
        /// than inheriting a permissive default from here.
        /// </summary>
        public static bool Contains(int step, double stepBehavioral, double halfWidth) =>
            double.IsFinite(halfWidth) && double.IsFinite(stepBehavioral)
                && Math.Abs(step - stepBehavioral) <= halfWidth;
    }
}
