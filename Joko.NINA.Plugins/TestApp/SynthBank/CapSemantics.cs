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
        Fail
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
    ///     It read <c>WasCapped</c> alone. F81 deliberately split <c>WasBandFloored</c> out as a separate signal,
    ///     but the two branches are mutually exclusive and clamp the half-width to the <b>same</b>
    ///     <c>maxHalfWidth</c> — so a truth curve predicting "this had to be bounded" is satisfied by either.
    ///     Reading only the cap turned every floored round into a FAIL, and the floor fires precisely on the
    ///     shape that flips the branch without changing the resulting half-width.
    ///   </description></item>
    /// </list>
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
            var actualBounded = wasCapped || wasBandFloored;
            var ratio = capBoundary > 0.0 ? truthHalfWidth / capBoundary : double.PositiveInfinity;

            CapSemanticsVerdict verdict;
            if (predictedBounded == actualBounded) {
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
