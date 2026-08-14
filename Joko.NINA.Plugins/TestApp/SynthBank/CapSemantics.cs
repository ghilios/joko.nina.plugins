#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using System;

namespace TestApp.SynthBank {

    /// <summary>Which way <c>A4</c> came out for one round.</summary>
    public enum CapSemanticsVerdict {
        Pass,
        Flag,
        Fail,

        /// <summary>The band floor fired, so the cap predicate does not describe this round.</summary>
        NotApplicable
    }

    /// <summary>The full working of one <c>A4</c> decision, so the report can print what it compared.</summary>
    public readonly struct CapSemanticsResult {
        public CapSemanticsVerdict Verdict { get; init; }

        /// <summary>True when the ANALYTIC truth curve says this round's half-width had to be bounded.</summary>
        public bool PredictedBounded { get; init; }

        /// <summary>True when the recommender actually bounded it — by EITHER branch.</summary>
        public bool ActualBounded { get; init; }

        public double CapBoundary { get; init; }

        /// <summary>truthHalfWidth / capBoundary. 1.0 is exactly on the boundary.</summary>
        public double Ratio { get; init; }

        /// <summary>"fitted" when the product's own span was available, "requested" on the legacy fallback.</summary>
        public string SpanSource { get; init; }

        /// <summary>"capped", "band-floored" or "unbounded".</summary>
        public string Branch { get; init; }
    }

    /// <summary>
    /// The <c>A4</c> cap-semantics rule, extracted so it can be unit-tested without standing up the runner.
    /// <para>
    /// <b>What A4 asserts.</b> The recommender must bound a round's half-width exactly when the noiseless truth
    /// curve says the 3× crossing lies outside what the sweep could support. The boundary is
    /// <c>MaxHalfWidthSampledHalfSpanMultiple × (sampled half-span)</c>.
    /// </para>
    /// <para>
    /// <b>Two things this rule used to get wrong, both on the assertion side.</b>
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     It rebuilt the boundary from the <b>requested</b> sweep (<c>offsetSteps × stepSize</c>) while the
    ///     product builds it from the <b>fitted</b> span — <c>max(x) − min(x)</c> over the points that survived
    ///     into the fit, which is smaller whenever a position was dropped for a non-finite pooled HFR, held out
    ///     as a recovery frame, or rejected as a consensus outlier. On the bank's <c>D01</c> r1 that is a factor
    ///     of two (requested 24.0, fitted 12.0), so A4 was scoring the product against a bound it never applied.
    ///   </description></item>
    ///   <item><description>
    ///     It asserted the cap predicate on <b>band-floored</b> rounds, where it does not apply. The cap and the
    ///     floor are mutually exclusive by construction, and they bound in <b>opposite directions</b>: the cap
    ///     clamps a fitted crossing DOWN to <c>maxHalfWidth</c>, while the floor raises one UP to
    ///     <c>maxHalfWidth</c> when the sweep's own HFRs prove the band was never sampled. So "truth is above the
    ///     boundary" predicts the CAP and says nothing about the FLOOR — on <c>D03</c> r0 that produced a FAIL
    ///     (<c>truth 46.1 vs boundary 24.0</c>) against a round the floor had handled exactly as F81 designed.
    ///     A floored round is therefore reported <see cref="CapSemanticsVerdict.NotApplicable"/> and NAMED, not
    ///     silently dropped.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <b>What was considered and rejected:</b> treating the floor as satisfying the cap
    /// (<c>bounded = capped || floored</c>). It looks right — both branches end at the same
    /// <c>maxHalfWidth</c> — and it does repair <c>D03</c> r0. But it turns the two floored rounds whose truth
    /// sits BELOW the boundary into failures: <c>D01</c> r0 (ratio 0.87) would drop PASS → FLAG and <c>D02</c>
    /// r0 (ratio 0.70) PASS → FAIL, on rounds where the recommender did nothing wrong. Measured on the real
    /// reports before it was adopted, which is why it was not.
    /// </para>
    /// <para>
    /// Near the boundary a noisy fit can legitimately land on the other side, so a mismatch within
    /// <see cref="NearBoundaryFraction"/> is a FLAG rather than a FAIL.
    /// </para>
    /// </summary>
    public static class CapSemantics {

        /// <summary>Mismatches within this fraction of the boundary are FLAG, not FAIL — a noisy fit's coin-flip.</summary>
        public const double NearBoundaryFraction = 0.15;

        /// <summary>
        /// Scores one round. <paramref name="fittedSearchSpan"/> is the product's own span; pass
        /// <see cref="double.NaN"/> (as reports written before it was recorded do) to fall back to the requested
        /// sweep, which reproduces the historical boundary exactly.
        /// </summary>
        public static CapSemanticsResult Evaluate(
            double truthHalfWidth,
            double fittedSearchSpan,
            int requestedOffsetSteps,
            double requestedStepSize,
            bool wasCapped,
            bool wasBandFloored) {

            var useFitted = double.IsFinite(fittedSearchSpan) && fittedSearchSpan > 0.0;
            var sampledHalfSpan = useFitted
                ? 0.5 * fittedSearchSpan
                : requestedOffsetSteps * requestedStepSize;
            var capBoundary = StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple * sampledHalfSpan;

            var predictedBounded = capBoundary > 0.0 && truthHalfWidth > capBoundary;
            var actualBounded = wasCapped;
            var ratio = capBoundary > 0.0 ? truthHalfWidth / capBoundary : double.PositiveInfinity;

            CapSemanticsVerdict verdict;
            if (wasBandFloored) {
                // The floor and the cap are mutually exclusive and bound in opposite directions; the cap
                // predicate simply does not describe this round. Named, not dropped.
                verdict = CapSemanticsVerdict.NotApplicable;
            } else if (predictedBounded == actualBounded) {
                verdict = CapSemanticsVerdict.Pass;
            } else if (Math.Abs(ratio - 1.0) < NearBoundaryFraction) {
                verdict = CapSemanticsVerdict.Flag;
            } else {
                verdict = CapSemanticsVerdict.Fail;
            }

            return new CapSemanticsResult {
                Verdict = verdict,
                PredictedBounded = predictedBounded,
                ActualBounded = actualBounded,
                CapBoundary = capBoundary,
                Ratio = ratio,
                SpanSource = useFitted ? "fitted" : "requested",
                Branch = wasCapped ? "capped" : wasBandFloored ? "band-floored" : "unbounded"
            };
        }
    }
}
