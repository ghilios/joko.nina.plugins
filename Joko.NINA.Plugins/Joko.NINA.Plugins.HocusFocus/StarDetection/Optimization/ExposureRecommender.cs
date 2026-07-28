#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using NINA.Joko.Plugins.HocusFocus.Utility;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// An exposure-time recommendation derived from the measured Sensitivity-gate SNR of the accepted stars in an
    /// optimizer run. See <see cref="ExposureRecommender"/> for the derivation.
    /// </summary>
    public sealed class ExposureRecommendation {

        /// <summary>
        /// True when there was enough data to compute a recommendation (at least
        /// <see cref="ExposureRecommender.MinFramesForRecommendation"/> usable frames, a non-null
        /// <see cref="RunEvaluationMetrics.FrameStarSnrs"/>, and a positive <see cref="CurrentSeconds"/>). False ⇒
        /// <see cref="RawSeconds"/>, <see cref="RecommendedSeconds"/>, and <see cref="MeasuredSnr"/> are all
        /// <see cref="double.NaN"/>; <see cref="UsableFrameCount"/> / <see cref="ShortFrameCount"/> /
        /// <see cref="CurrentSeconds"/> are still reported (whatever was measurable before the guard tripped).
        /// </summary>
        public bool HasRecommendation { get; set; }

        /// <summary>The exposure time the scored run actually used, echoed back unchanged.</summary>
        public double CurrentSeconds { get; set; }

        /// <summary>
        /// <c>CurrentSeconds · (TargetSensitivity / MeasuredSnr)²</c> — UNCAPPED, UNROUNDED. Reported honestly even
        /// when <see cref="WasCapped"/> or <see cref="ExposureIsNotTheLimit"/>, so a caller can show the user what
        /// the raw derivation actually said before either guard modified it.
        /// </summary>
        public double RawSeconds { get; set; }

        /// <summary>
        /// The value to pre-fill the exposure-time UI box with. Let <c>cappedSeconds</c> be <see cref="RawSeconds"/>
        /// capped at <c>min(CurrentSeconds × <see cref="ExposureRecommender.MaxExposureFactor"/>,
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/>)</c>: when <c>cappedSeconds &gt;
        /// CurrentSeconds</c> this is <c>cappedSeconds</c> rounded up to the exposure-time ladder
        /// (<see cref="ExposureRecommender.RoundExposureSeconds"/>); OTHERWISE this is <see cref="CurrentSeconds"/>
        /// EXACTLY, un-rounded — there is nothing to raise, so nothing is rounded. <see cref="double.NaN"/> when
        /// <see cref="HasRecommendation"/> is false.
        ///
        /// <para>This is a two-way BRANCH, not "round then floor": an earlier version rounded unconditionally and
        /// floored the result with <c>Math.Max(rounded, CurrentSeconds)</c>, which is only an approximate floor —
        /// rounding can walk a <c>cappedSeconds</c> that sits just BELOW an off-grid <see cref="CurrentSeconds"/>
        /// back ABOVE it (e.g. <c>cappedSeconds</c> = 3.10 s, <c>CurrentSeconds</c> = 3.2 s: rounds to 3.5 s, and
        /// <c>Math.Max(3.5, 3.2)</c> leaves it at 3.5 s — past current). The branch form makes
        /// <see cref="IncreasesExposure"/> exactly equivalent to <c>cappedSeconds &gt; CurrentSeconds</c>, with no
        /// approximation window.</para>
        ///
        /// <para>NOTE: rounding UP after capping means this can exceed
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> by up to one ladder step (e.g. a capped
        /// 8.8 s rounds to 9.0 s) — do not assume it is bounded by the cap. Separately, the never-below-
        /// <see cref="CurrentSeconds"/> behavior means this can equal <see cref="CurrentSeconds"/> exactly (when
        /// the absolute cap sits below an already-long current exposure) while <see cref="HasRecommendation"/> is
        /// still true — check <see cref="IncreasesExposure"/> before rendering this as a "raise it to X"
        /// affordance.</para>
        /// </summary>
        public double RecommendedSeconds { get; set; }

        /// <summary>
        /// S_now: the median, across non-recovery frames, of each frame's <c>NTarget</c>-th-brightest (or, on a
        /// short frame, best-available) accepted-star Sensitivity-gate SNR. <see cref="double.NaN"/> when
        /// <see cref="HasRecommendation"/> is false.
        /// </summary>
        public double MeasuredSnr { get; set; }

        /// <summary>
        /// The number of non-recovery frames that contributed a value to <see cref="MeasuredSnr"/> (had at least
        /// one finite, positive entry in <see cref="RunEvaluationMetrics.FrameStarSnrs"/>).
        /// </summary>
        public int UsableFrameCount { get; set; }

        /// <summary>
        /// Of <see cref="UsableFrameCount"/>, how many had fewer than <c>NTarget</c> surviving stars and so fell
        /// back to their faintest surviving star instead of the true <c>NTarget</c>-th-brightest — a bounded
        /// UNDER-estimate of that frame's strength (see <see cref="ExposureRecommender.Recommend"/>).
        /// </summary>
        public int ShortFrameCount { get; set; }

        /// <summary>
        /// True when either the run-relative <see cref="ExposureRecommender.MaxExposureFactor"/> cap or the
        /// absolute <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> cap reduced
        /// <see cref="RawSeconds"/> before rounding. This is a statement about <see cref="RawSeconds"/> only — it
        /// does NOT mean the cap shaped the DELIVERED <see cref="RecommendedSeconds"/> (the never-shorter floor can
        /// still pull the answer back up past the cap to <see cref="CurrentSeconds"/>, leaving nothing capped in
        /// the final result). Use <see cref="CapLimitsRecommendation"/> for that question.
        /// </summary>
        public bool WasCapped { get; set; }

        /// <summary>
        /// When <see cref="WasCapped"/>, true if the binding bound was the absolute
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> ceiling rather than the run-relative
        /// <see cref="ExposureRecommender.MaxExposureFactor"/>. Meaningless (left false) when
        /// <see cref="WasCapped"/> is false. TIE: when <c>CurrentSeconds × MaxExposureFactor</c> equals
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> exactly (e.g. <c>CurrentSeconds = 7.5 s</c>,
        /// since <c>7.5 × 4 = 30</c>) both bounds bind at the same value and this reports true — the absolute limit
        /// is treated as "the reason" in a tie, since it is the one that would still bind even if
        /// <see cref="ExposureRecommender.MaxExposureFactor"/> were relaxed.
        /// </summary>
        public bool CappedByAbsoluteLimit { get; set; }

        /// <summary>
        /// True when <see cref="MeasuredSnr"/> already meets or exceeds
        /// <see cref="ExposureRecommender.TargetSensitivity"/>: exposure time is not what is limiting star
        /// recovery on this run. <see cref="RecommendedSeconds"/> is then never shorter than
        /// <see cref="CurrentSeconds"/> — this field exists so a caller can say "exposure isn't the bottleneck"
        /// instead of silently suggesting a shorter exposure the user never asked to shorten.
        /// </summary>
        public bool ExposureIsNotTheLimit { get; set; }

        /// <summary>
        /// True when this recommendation actually asks for a LONGER exposure than <see cref="CurrentSeconds"/>.
        /// Exactly equivalent to "the capped-but-unrounded exposure exceeds <see cref="CurrentSeconds"/>" (see
        /// <see cref="RecommendedSeconds"/> for why this is exact rather than approximate). False in two distinct
        /// situations: (1) <see cref="MeasuredSnr"/> already meets <see cref="ExposureRecommender.TargetSensitivity"/>
        /// (see <see cref="ExposureIsNotTheLimit"/>) — the raw factor is then ≤ 1, so there was never more exposure
        /// to ask for; or (2) the absolute cap pulls the capped value down to at or below an already-long
        /// <see cref="CurrentSeconds"/> (realistic for narrowband, <c>CurrentSeconds &gt;
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/></c>) even though the data still wants
        /// more. <see cref="HasRecommendation"/> stays true in either case — the situation can still be worth
        /// reporting ("you are at 40 s, the data wants ~49 s, and that is past what an AF sweep can sustain") — it
        /// just is not an increase. A consumer MUST check this before rendering a "raise it to X" affordance, or a
        /// false one renders a no-op "40 s → 40 s" row.
        /// </summary>
        public bool IncreasesExposure => RecommendedSeconds > CurrentSeconds;

        /// <summary>
        /// True when a cap genuinely shaped the delivered <see cref="RecommendedSeconds"/> — i.e. the
        /// recommendation both raises the exposure AND was constrained by one of the two caps — so a consumer can
        /// label the affordance "capped at N s" rather than a plain "raise it to N s". <see cref="WasCapped"/>
        /// alone is NOT this signal: it can be true while <see cref="IncreasesExposure"/> is false (the
        /// never-shorter floor pulled the answer back up past the cap to <see cref="CurrentSeconds"/> — see
        /// <see cref="WasCapped"/>), in which case there is nothing delivered for a cap to have "limited". This is
        /// the flag Task 3's UI should read; <see cref="WasCapped"/> stays as the lower-level "RawSeconds was
        /// reduced" fact for diagnostics.
        /// </summary>
        public bool CapLimitsRecommendation => WasCapped && IncreasesExposure;
    }

    /// <summary>
    /// Recommends a longer auto-focus exposure time from the measured Sensitivity-gate SNR of the stars an
    /// optimizer run actually accepted (<see cref="RunEvaluationMetrics.FrameStarSnrs"/>). Static, pure, no VM and
    /// no NINA types — the math is fully testable in isolation; a later task wires this into the optimizer UI.
    ///
    /// <para><b>Why this exists.</b> <c>StarDetectorParams.Sensitivity</c> (the "BrightnessSensitivity" gate: a
    /// candidate must satisfy <c>(s−b)/n &gt; Sensitivity</c> to be counted) is searched over [0, 50] by the Star
    /// Detection Optimizer. When the search lands it at (or near) its floor, it had to admit essentially anything
    /// above the noise to find stars at all — the frames are signal-starved, not just permissively gated, and the
    /// resulting focus measurement is untrustworthy. The fix is a longer exposure, not a lower gate.</para>
    ///
    /// <para><b>The scaling law.</b> <c>t_new = t_old · (TargetSensitivity / S_now)²</c>. Both branches of the gate
    /// statistic (<see cref="NINA.Joko.Plugins.HocusFocus.Interfaces.Star.MeasuredSensitivity"/>: a per-pixel <c>NormalizedBrightness/σ</c> ratio, or a
    /// donut matched-filter <c>TotalFlux/(σ√N)</c>, depending on <c>DefocusAwareDonutDetection</c>) are
    /// (background-subtracted signal) ÷ σ, so the scaling is set entirely by how σ scales with exposure time:
    /// sky-limited gives <c>σ ∝ √t</c> hence <c>SNR ∝ √t</c> (a required time factor of <c>r²</c> for an SNR ratio
    /// <c>r</c>); read-noise-limited gives constant σ hence <c>SNR ∝ t</c> (factor <c>r</c>). Sky-limited is the
    /// right model for THESE frames — an autofocus sweep is deliberately defocused, so star flux is spread thin
    /// across many pixels and sky background dominates σ even for a fairly bright star. It is also the
    /// CONSERVATIVE branch (<c>r² &gt; r</c> for <c>r &gt; 1</c>), and the caps below bound how far the overshoot
    /// can go.</para>
    ///
    /// <para><b>Why the NTarget-th star, not the median star.</b> The median accepted star over-recommends: on a
    /// 200-star frame it would demand 100 stars clear the default gate, far more than the objective needs. The
    /// <c>NTarget</c>-th star (the same star-count knee <see cref="OptimizationObjective.SStars"/> and
    /// <see cref="OptimizationObjective.TieBreakerScore"/> already use) asks the actual question this
    /// recommendation exists to answer — what exposure would put <c>NTarget</c> stars per frame above the healthy
    /// default gate — which is why <see cref="Recommend"/> reads <c>NTarget</c> from the caller's
    /// <see cref="ObjectiveConstants"/> rather than a literal: hard-coding 20 here would let an objective retune
    /// silently desync this recommendation from the knee it claims to invert.</para>
    ///
    /// <para><b>Why the median across FRAMES, not the worst frame.</b> The objective's own star-count knee is a
    /// median across frames (<c>0.4 · clamp01(Median(counts)/NTarget)</c> in <see cref="OptimizationObjective.SStars"/>).
    /// Pairing the per-frame <c>NTarget</c>-th-star statistic with a worst-FRAME aggregation would form a
    /// statistic the objective never actually computes, and would hand the entire recommendation to whichever
    /// single frame had a passing cloud, a satellite trail, or a guiding bump — exactly the kind of one-frame
    /// fluke a focus sweep is supposed to be robust to.</para>
    ///
    /// <para><b>Caveat — interaction with a detection-binning recommendation.</b>
    /// <see cref="RunEvaluationMetrics.FrameStarSnrs"/> values are in BINNED-PIXEL space (see that field's doc
    /// comment), so S_now is self-consistent within a single run — <see cref="TargetSensitivity"/> is the same
    /// gate threshold, compared in the same space, that the run's own detector used. But the wizard's Summary page
    /// can ALSO render a separate detection-binning-change recommendation for the same run, and a per-pixel SNR
    /// scales with the binning factor (roughly, since higher binning averages more source photons and more sky
    /// per output pixel). A user who accepts BOTH recommendations — a higher binning factor AND this longer
    /// exposure — would be over-recommended: the binning change alone already raises the measured SNR, so the
    /// exposure increase this class computed (from the run's pre-binning-change SNR) asks for more than is then
    /// actually needed. This class does not know about, and deliberately does not try to correct for, the other
    /// recommendation — a consumer presenting both must caveat that they are not independently additive.</para>
    /// </summary>
    public static class ExposureRecommender {

        /// <summary>
        /// The shipped default Sensitivity gate (<c>StarDetectionOptions.ResetDefaults</c> /
        /// <c>HocusFocusStarDetection.BuildDefaultStarDetectorParams().Sensitivity</c>). A star at this measured
        /// SNR is exactly the faintest one the healthy, out-of-the-box gate would still admit — the target this
        /// recommendation aims S_now at. Pinned by a test against the real default rather than read from it at
        /// runtime, the same choice <c>DetectionBinningResolver.TargetHfrPixels</c> makes for the analogous
        /// "recompute a knob from a measurement" recommendation.
        /// </summary>
        public const double TargetSensitivity = 10.0;

        /// <summary>
        /// The optimizer's Sensitivity axis floor is exactly <c>0.0</c> (<c>OptimizerVariable.SensitivityLower</c>),
        /// but the pattern search that lands there refines a Continuous variable's step by repeated halving down to
        /// <c>InitialStep × StepFloorFraction</c> (<c>StarDetectionOptimizer.StepFloorFraction = 0.125</c>, against
        /// Sensitivity's <c>InitialStep = 1.0</c>), so a genuinely-floored search routinely lands on 0.125, 0.25, or
        /// 0.5 rather than bit-exact 0 — an <c>== 0</c> test would miss most real cases. <c>1.0</c> is one full
        /// search step off the floor, and also exactly one tenth of <see cref="TargetSensitivity"/>. <c>2.0</c> was
        /// considered and rejected: a legitimately hand-tuned gate of 2 on a rich, well-exposed field is plausible
        /// and should not trigger an exposure-time nag.
        /// </summary>
        public const double SensitivityFloorThreshold = 1.0;

        /// <summary>
        /// The largest RUN-RELATIVE factor by which a single recommendation may lengthen the exposure. Under the
        /// sky-limited <c>SNR ∝ √t</c> scaling this is exactly "at most double the measured SNR in one step" — a
        /// physical statement about how far one exposure-time change can be trusted, not a taste choice. Matches
        /// <see cref="StepSizeRecommender"/>'s converge-over-runs philosophy
        /// (<see cref="StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple"/>): a signal-starved run is
        /// usually starved by more than 4x, so this bounds the jump and lets a second optimizer run — now with
        /// real signal — refine the answer, rather than accepting one large, poorly-grounded extrapolation.
        /// Iterating costs the user one re-run click, which is cheap next to recommending something absurd.
        /// </summary>
        public const double MaxExposureFactor = 4.0;

        /// <summary>
        /// The absolute ceiling on a recommended exposure, regardless of <see cref="MaxExposureFactor"/>. A 9-point
        /// sweep (the wizard's default <c>±4</c> offset steps) at 30 s/frame is already 4.5 minutes of pure
        /// integration before counting focuser moves and downloads, and a live run's <c>FocusRecoverySteps</c>
        /// widens the sweep further still. <c>AutoFocusEngineOptions.AutoFocusTimeout</c> hard-enforces a budget
        /// for the whole fixed sweep, so an uncapped recommendation for a badly-starved run could recommend an
        /// exposure that makes the sweep itself time out — the opposite of the fix this class exists to offer.
        /// </summary>
        public const double MaxRecommendedExposureSeconds = 30.0;

        /// <summary>
        /// The minimum number of usable (non-recovery, non-empty) frames before <see cref="Recommend"/> will
        /// answer at all. This is a HARD-CODED constant — deliberately NOT read from
        /// <see cref="ObjectiveConstants.MinFramesForPenalty"/>, even though both currently equal 3. The two
        /// thresholds answer genuinely different questions ("enough frames to compute a recommendation at all"
        /// here vs. "enough frames to apply the label-free precision penalty" there) and only coincide at the same
        /// "too thin to trust" bar by choice, not by definition; a future retune of one must not silently move the
        /// other. Precedent for staying silent rather than guessing on thin data:
        /// <c>OptimizationSummary.MinRSquaredForBinningRecommendation</c> / <c>HasDetectionBinningMeasurement</c>
        /// withholds the detection-binning recommendation the same way.
        /// </summary>
        public const int MinFramesForRecommendation = 3;

        /// <summary>True when <paramref name="sensitivity"/> is at or below <see cref="SensitivityFloorThreshold"/> — the search-floor band described there, not merely bit-exact zero.</summary>
        public static bool SensitivityIsAtFloor(double sensitivity) => sensitivity <= SensitivityFloorThreshold;

        /// <summary>
        /// Rounds an exposure time UP to the granularity of a practical exposure-time ladder: 0.5 s below 10 s, 1 s
        /// from 10 s up to and including 30 s, 5 s above 30 s (finer control where sub-second precision matters,
        /// coarser once the number is already large). ALWAYS rounds up, never down or to nearest — rounding down
        /// would partially undo the derivation that produced <paramref name="seconds"/>, silently handing back an
        /// exposure the math just said was insufficient.
        /// </summary>
        public static double RoundExposureSeconds(double seconds) {
            double granularity;
            if (seconds < 10.0) {
                granularity = 0.5;
            } else if (seconds <= 30.0) {
                granularity = 1.0;
            } else {
                granularity = 5.0;
            }
            // A tiny epsilon keeps a value that is mathematically exactly on the grid (e.g. 20.0) from landing a
            // hair below it due to upstream floating-point error and getting pushed to the NEXT grid step instead
            // of staying put.
            return Math.Ceiling(seconds / granularity - 1e-9) * granularity;
        }

        /// <summary>
        /// Recommends a longer auto-focus exposure from <paramref name="metrics"/>.<see cref="RunEvaluationMetrics.FrameStarSnrs"/>,
        /// or reports <see cref="ExposureRecommendation.HasRecommendation"/> = false when there isn't enough data
        /// to trust an answer (fewer than <see cref="MinFramesForRecommendation"/> usable frames, a null
        /// <c>FrameStarSnrs</c>, or a non-positive <paramref name="currentExposureSeconds"/>).
        ///
        /// <para><b>Per-frame reduction.</b> Each frame's SNR list is filtered to finite, positive entries (a
        /// filtered-out entry did not contribute a real detection), then reduced to one value: the
        /// <c>c.NTarget</c>-th brightest survivor. A frame with fewer than <c>NTarget</c> survivors cannot answer
        /// that question, so it falls back to its faintest survivor instead — a bounded UNDER-estimate (the true
        /// <c>NTarget</c>-th-brightest, had the frame had that many stars, could only have been brighter), so a
        /// short frame makes this recommendation UNDER-recommend rather than invent a number the frame never
        /// measured; <see cref="ExposureRecommendation.ShortFrameCount"/> reports how often that happened. A frame
        /// with zero surviving entries — whether because it truly had none, or because the caller's producer
        /// never populated per-star SNRs for it (the empty-does-not-mean-zero convention documented on
        /// <see cref="RunEvaluationMetrics.FrameStarSnrs"/>) — contributes nothing either way; the two cases are
        /// indistinguishable from here and are treated identically, on purpose. Frames tagged
        /// <see cref="RunEvaluationMetrics.FrameIsRecovery"/> are skipped entirely (by-design far-from-focus, so
        /// their SNR statistics are not representative of the run's actual gate strength); a null
        /// <c>FrameIsRecovery</c> treats every frame as non-recovery.</para>
        ///
        /// <para><b>S_now</b> is the MEDIAN of those per-frame values across all usable (non-recovery, non-empty)
        /// frames — see the class remarks for why the median, and why the <c>NTarget</c>-th star.</para>
        ///
        /// <para><b>Order of operations</b> (see <see cref="ExposureRecommendation.RawSeconds"/> /
        /// <see cref="ExposureRecommendation.RecommendedSeconds"/>): S_now → raw factor
        /// <c>(TargetSensitivity/S_now)²</c> → <c>RawSeconds</c> → cap at
        /// <c>min(currentExposureSeconds × MaxExposureFactor, MaxRecommendedExposureSeconds)</c> — then
        /// <see cref="RoundExposureSeconds"/> is applied ONLY IF the capped value actually exceeds
        /// <paramref name="currentExposureSeconds"/>; otherwise <paramref name="currentExposureSeconds"/> is
        /// returned EXACTLY, un-rounded. This is not "cap, round, floor" as three sequential steps — it is a
        /// two-way branch, because ANY unconditional rounding step applied to a value that might already be at or
        /// below current risks rounding it UP past current (see <see cref="ExposureRecommendation.RecommendedSeconds"/>
        /// for the concrete case that motivated this).</para>
        ///
        /// <para>Capping BEFORE rounding does NOT mean <see cref="ExposureRecommendation.RecommendedSeconds"/> can
        /// never exceed the cap — <see cref="RoundExposureSeconds"/> always rounds UP, so on the "capped value
        /// exceeds current" branch it can push a capped value past the cap by up to one ladder step (a capped
        /// value of 8.8 s rounds to 9.0 s). What cap-before-round actually buys is that the overshoot is BOUNDED to
        /// at most one ladder step, rather than the unbounded overshoot a round-then-cap ordering would leave
        /// uncorrected. A caller MUST NOT assume <c>RecommendedSeconds &lt;= MaxRecommendedExposureSeconds</c>.</para>
        ///
        /// <para><b>Never shorter than current.</b> By construction: on the branch where
        /// <c>cappedSeconds &gt; currentExposureSeconds</c>, <see cref="RoundExposureSeconds"/> only ever rounds UP
        /// (ceiling), so the result stays <c>&gt;= cappedSeconds &gt; currentExposureSeconds</c> — no separate floor
        /// is needed there. On the other branch <paramref name="currentExposureSeconds"/> is returned unchanged, so
        /// it trivially cannot be shorter than itself. <c>IncreasesExposure ⟺ cappedSeconds &gt; currentExposureSeconds</c>
        /// holds exactly (see <see cref="ExposureRecommendation.IncreasesExposure"/>) — an EARLIER version of this
        /// method rounded unconditionally and then applied <c>Math.Max(rounded, current)</c> as an approximate
        /// floor, which does not hold that equivalence: rounding a capped value that sits just BELOW current can
        /// still push it back ABOVE current (S_now=10.16, current=3.2 s: cappedSeconds=3.10 s rounds to 3.5 s,
        /// which the Math.Max form left at 3.5 s — past current, contradicting
        /// <see cref="ExposureRecommendation.ExposureIsNotTheLimit"/>'s "3.2 s is already enough"). This still
        /// applies even when <c>S_now &lt; TargetSensitivity</c> and the situation is ordinarily moot (the raw
        /// factor exceeds 1 there, so <c>RawSeconds &gt; currentExposureSeconds</c> already) or when the absolute
        /// 30 s cap sits below an already-long <paramref name="currentExposureSeconds"/> (which would otherwise
        /// recommend shortening a deliberately long exposure back down to the cap) — the branch is unconditional,
        /// not incidental to either scenario's arithmetic.</para>
        /// </summary>
        public static ExposureRecommendation Recommend(RunEvaluationMetrics metrics, ObjectiveConstants c, double currentExposureSeconds) {
            if (metrics?.FrameStarSnrs == null) {
                return NoRecommendation(currentExposureSeconds, usableFrameCount: 0, shortFrameCount: 0);
            }

            // c is trusted non-null, consistently with every other ObjectiveConstants-consuming method in this
            // namespace (OptimizationObjective.SFocus/SStars/JRun/... never null-check their ObjectiveConstants
            // either) -- a null c throws here too, rather than silently degrading.
            //
            // NTarget, however, is trusted POSITIVE -- a STRONGER requirement than those siblings impose. They use
            // NTarget only in floating-point arithmetic that degrades gracefully at 0 (SStars: Clamp01(nMed /
            // c.NTarget) -> Clamp01(+Infinity) -> 1.0; TieBreakerScore: nMean / (nMean + c.NTarget) -> 1.0 -- never
            // an exception). This method is the ONLY consumer that uses NTarget as an ARRAY INDEX
            // (PerFrameNthBrightest's filtered[filtered.Count - nTarget]): at NTarget = 0 that evaluates
            // filtered[filtered.Count], throwing ArgumentOutOfRangeException on the first non-recovery frame with
            // any finite positive SNR. Safe today because the only production assignment is
            // ObjectiveConstants.ForAberrationInspection's NTarget = 60 (every other construction is a bare `new
            // ObjectiveConstants()`, default 20; there is no persisted option, XAML binding, or deserialization
            // path that could set it to <= 0). If NTarget is ever exposed as a user-tunable value, clamp it at the
            // source or restore a guard here -- do not assume this method's array-index usage is as forgiving as
            // its floating-point siblings'.
            var nTarget = c.NTarget;
            var snrsByFrame = metrics.FrameStarSnrs;
            var isRecovery = metrics.FrameIsRecovery;

            var perFrameValues = new List<double>(snrsByFrame.Count);
            var shortFrameCount = 0;
            for (var i = 0; i < snrsByFrame.Count; i++) {
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue; // far-from-focus recovery wing: by design few/no stars, not representative of gate strength
                }
                var value = PerFrameNthBrightest(snrsByFrame[i], nTarget, out var wasShort);
                if (value == null) {
                    continue; // no usable SNR data on this frame (Trap: NOT proof the frame was starved -- see FrameStarSnrs)
                }
                perFrameValues.Add(value.Value);
                if (wasShort) {
                    shortFrameCount++;
                }
            }

            var usableFrameCount = perFrameValues.Count;
            if (usableFrameCount < MinFramesForRecommendation) {
                return NoRecommendation(currentExposureSeconds, usableFrameCount, shortFrameCount);
            }
            if (!(currentExposureSeconds > 0.0)) {
                return NoRecommendation(currentExposureSeconds, usableFrameCount, shortFrameCount);
            }

            var (sNow, _) = perFrameValues.MedianMAD(); // median across frames; all contributing values are already > 0
            var exposureIsNotTheLimit = sNow >= TargetSensitivity;

            var ratio = TargetSensitivity / sNow;
            var rawFactor = ratio * ratio;
            var rawSeconds = currentExposureSeconds * rawFactor;

            var factorCap = currentExposureSeconds * MaxExposureFactor;
            var upperCap = Math.Min(factorCap, MaxRecommendedExposureSeconds);
            var cappedSeconds = Math.Min(rawSeconds, upperCap);
            var wasCapped = cappedSeconds < rawSeconds;
            var cappedByAbsoluteLimit = wasCapped && MaxRecommendedExposureSeconds <= factorCap;

            // Never emit a shorter exposure than the user already uses -- guarded explicitly (see the method
            // remarks), not left to fall out of either branch's arithmetic. ROUND ONLY WHEN THE CAPPED VALUE
            // ACTUALLY EXCEEDS CURRENT, rather than rounding unconditionally and flooring the result with
            // Math.Max: that Math.Max form is only an approximate floor -- RoundExposureSeconds can round a
            // cappedSeconds that sits just BELOW currentExposureSeconds up past it (e.g. S_now=10.16,
            // currentExposureSeconds=3.2s: cappedSeconds=3.10s rounds to 3.5s, which Math.Max(3.5, 3.2) leaves at
            // 3.5s -- past current, contradicting ExposureIsNotTheLimit's "3.2s is already enough" the same way
            // the previous ordering bug did). Branching first makes the two cases exact: when cappedSeconds >
            // currentExposureSeconds, RoundExposureSeconds(cappedSeconds) is >= cappedSeconds > currentExposureSeconds
            // by construction (ceiling never decreases), so no separate floor is needed; otherwise there is
            // nothing to raise, and CurrentSeconds is returned EXACTLY, un-rounded (rounding an unchanged value
            // would just be the earlier bug's off-grid-overshoot risk in disguise). This makes
            // <c>IncreasesExposure ⟺ cappedSeconds &gt; currentExposureSeconds</c> hold exactly.
            var recommendedSeconds = cappedSeconds > currentExposureSeconds
                ? RoundExposureSeconds(cappedSeconds)
                : currentExposureSeconds;

            return new ExposureRecommendation {
                HasRecommendation = true,
                CurrentSeconds = currentExposureSeconds,
                RawSeconds = rawSeconds,
                RecommendedSeconds = recommendedSeconds,
                MeasuredSnr = sNow,
                UsableFrameCount = usableFrameCount,
                ShortFrameCount = shortFrameCount,
                WasCapped = wasCapped,
                CappedByAbsoluteLimit = cappedByAbsoluteLimit,
                ExposureIsNotTheLimit = exposureIsNotTheLimit
            };
        }

        private static ExposureRecommendation NoRecommendation(double currentExposureSeconds, int usableFrameCount, int shortFrameCount) {
            return new ExposureRecommendation {
                HasRecommendation = false,
                CurrentSeconds = currentExposureSeconds,
                RawSeconds = double.NaN,
                RecommendedSeconds = double.NaN,
                MeasuredSnr = double.NaN,
                UsableFrameCount = usableFrameCount,
                ShortFrameCount = shortFrameCount,
                WasCapped = false,
                CappedByAbsoluteLimit = false,
                ExposureIsNotTheLimit = false
            };
        }

        /// <summary>
        /// Reduces one frame's raw SNR list to a single value: the <paramref name="nTarget"/>-th brightest entry
        /// (a fixed ORDER STATISTIC / rank, not a quantile — a fraction of the distribution would drift with the
        /// star count, undermining the whole point of pinning the recommendation to the objective's own fixed
        /// <c>NTarget</c> knee; see the class remarks) after dropping non-finite and non-positive entries, or
        /// (<paramref name="wasShort"/> = true) the faintest surviving entry when fewer than
        /// <paramref name="nTarget"/> survive. Returns null when nothing survives filtering (including a
        /// null/empty input list) — see the Recommend remarks for why that is deliberately indistinguishable from,
        /// and treated identically to, a frame that had zero real detections.
        /// </summary>
        private static double? PerFrameNthBrightest(IReadOnlyList<double> frameSnrs, int nTarget, out bool wasShort) {
            wasShort = false;
            if (frameSnrs == null || frameSnrs.Count == 0) {
                return null;
            }
            List<double> filtered = null;
            for (var i = 0; i < frameSnrs.Count; i++) {
                var v = frameSnrs[i];
                if (double.IsFinite(v) && v > 0.0) {
                    (filtered ??= new List<double>(frameSnrs.Count)).Add(v);
                }
            }
            if (filtered == null || filtered.Count == 0) {
                return null;
            }
            filtered.Sort(); // ascending
            var k = filtered.Count;
            if (k >= nTarget) {
                // Descending rank (nTarget - 1) (0 = brightest) is ascending index (k - nTarget).
                return filtered[k - nTarget];
            }
            wasShort = true;
            return filtered[0]; // faintest survivor: the frame's best available (and only under-estimating) answer
        }

        /// <summary>
        /// Mirrors <c>OptimizationObjective</c>'s FrameIsRecovery convention (re-implemented here rather than
        /// shared, since that helper is private to <see cref="OptimizationObjective"/>): bounds-checked so a null
        /// <paramref name="flags"/> list (the baseline — every caller that doesn't populate recovery tagging) or a
        /// list shorter than the frame axis both resolve to "not recovery" rather than throwing.
        /// </summary>
        private static bool IsRecoveryFrame(IReadOnlyList<bool> flags, int i) => flags != null && i < flags.Count && flags[i];
    }
}
