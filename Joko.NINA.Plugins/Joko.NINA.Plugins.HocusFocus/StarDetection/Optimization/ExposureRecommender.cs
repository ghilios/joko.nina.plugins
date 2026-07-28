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
        /// The value to pre-fill the exposure-time UI box with: <see cref="RawSeconds"/> capped at
        /// <c>min(CurrentSeconds × <see cref="ExposureRecommender.MaxExposureFactor"/>,
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/>)</c>, floored so it is never below
        /// <see cref="CurrentSeconds"/>, THEN rounded up to the exposure-time ladder
        /// (<see cref="ExposureRecommender.RoundExposureSeconds"/>). <see cref="double.NaN"/> when
        /// <see cref="HasRecommendation"/> is false.
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
        /// <see cref="RawSeconds"/> before rounding.
        /// </summary>
        public bool WasCapped { get; set; }

        /// <summary>
        /// When <see cref="WasCapped"/>, true if the binding bound was the absolute
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> ceiling rather than the run-relative
        /// <see cref="ExposureRecommender.MaxExposureFactor"/>. Meaningless (left false) when
        /// <see cref="WasCapped"/> is false.
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
        /// answer at all. Matches <see cref="ObjectiveConstants.MinFramesForPenalty"/> — the same "too thin to
        /// trust" bar the objective's own label-free penalties use. Precedent for staying silent rather than
        /// guessing on thin data: <c>OptimizationSummary.MinRSquaredForBinningRecommendation</c> /
        /// <c>HasDetectionBinningMeasurement</c> withholds the detection-binning recommendation the same way.
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
        /// <c>min(currentExposureSeconds × MaxExposureFactor, MaxRecommendedExposureSeconds)</c> → floor at
        /// <paramref name="currentExposureSeconds"/> (see below) → <see cref="RoundExposureSeconds"/>. Capping
        /// BEFORE rounding, and rounding UP, means the displayed value can never exceed the cap the capping step
        /// computed.</para>
        ///
        /// <para><b>Never shorter than current.</b> The floor step above is unconditional, not incidental to
        /// either branch's arithmetic: ordinarily it is moot when <c>S_now &lt; TargetSensitivity</c> (the raw
        /// factor exceeds 1 there, so <c>RawSeconds &gt; currentExposureSeconds</c> already), and when
        /// <c>S_now ≥ TargetSensitivity</c> the raw factor is ≤ 1 by construction so the floor is exactly what
        /// keeps the recommendation from suggesting a shorter exposure the user never asked to shorten — that case
        /// also sets <see cref="ExposureRecommendation.ExposureIsNotTheLimit"/>. But the floor is applied
        /// unconditionally (not only in that branch) because the absolute 30 s cap can itself sit below an
        /// already-long <paramref name="currentExposureSeconds"/>, which would otherwise recommend shortening a
        /// deliberately long exposure back down to the cap.</para>
        /// </summary>
        public static ExposureRecommendation Recommend(RunEvaluationMetrics metrics, ObjectiveConstants c, double currentExposureSeconds) {
            if (metrics?.FrameStarSnrs == null) {
                return NoRecommendation(currentExposureSeconds, usableFrameCount: 0, shortFrameCount: 0);
            }

            var nTarget = Math.Max(1, c.NTarget); // defensive; NTarget is always >= 1 in every real ObjectiveConstants
            var snrsByFrame = metrics.FrameStarSnrs;
            var isRecovery = metrics.FrameIsRecovery;

            var perFrameValues = new List<double>(snrsByFrame.Count);
            var shortFrameCount = 0;
            for (var i = 0; i < snrsByFrame.Count; i++) {
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue; // far-from-focus recovery wing: by design few/no stars, not representative of gate strength
                }
                var value = PerFrameQuantile(snrsByFrame[i], nTarget, out var wasShort);
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
            // remarks), not left to fall out of either branch's arithmetic.
            cappedSeconds = Math.Max(cappedSeconds, currentExposureSeconds);

            var recommendedSeconds = RoundExposureSeconds(cappedSeconds);

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
        /// after dropping non-finite and non-positive entries, or (<paramref name="wasShort"/> = true) the
        /// faintest surviving entry when fewer than <paramref name="nTarget"/> survive. Returns null when nothing
        /// survives filtering (including a null/empty input list) — see the Recommend remarks for why that is
        /// deliberately indistinguishable from, and treated identically to, a frame that had zero real detections.
        /// </summary>
        private static double? PerFrameQuantile(IReadOnlyList<double> frameSnrs, int nTarget, out bool wasShort) {
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
