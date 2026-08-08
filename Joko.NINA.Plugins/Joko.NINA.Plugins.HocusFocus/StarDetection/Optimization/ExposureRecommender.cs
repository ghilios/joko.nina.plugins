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
using NINA.Joko.Plugins.HocusFocus.Interfaces;
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
        /// <para>NOTE: rounding UP after capping means this can exceed whichever cap actually bound
        /// <c>cappedSeconds</c> by up to one ladder step (e.g. a capped 8.8 s — from the RUN-RELATIVE
        /// <see cref="ExposureRecommender.MaxExposureFactor"/> cap on a 2.2 s current exposure — rounds to 9.0 s).
        /// This overshoot can never carry the result past the ABSOLUTE
        /// <see cref="ExposureRecommender.MaxRecommendedExposureSeconds"/> ceiling itself, though:
        /// <c>cappedSeconds</c> is always &lt;= 30 by construction, and every granularity
        /// <see cref="ExposureRecommender.RoundExposureSeconds"/> uses for an input &lt;= 30 (0.5 s or 1.0 s)
        /// divides 30 evenly, so the ceiling can land AT 30 but never past it. This CAN still exceed 30 overall,
        /// though — just not via rounding: the never-below-<see cref="CurrentSeconds"/> behavior means this can
        /// equal <see cref="CurrentSeconds"/> exactly (when the absolute cap sits below an already-long current
        /// exposure) while <see cref="HasRecommendation"/> is still true — check <see cref="IncreasesExposure"/>
        /// before rendering this as a "raise it to X" affordance.</para>
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
        /// True when <see cref="MeasuredSnr"/> meets <see cref="ExposureRecommender.TargetSensitivity"/> but EVERY
        /// usable frame still found fewer than <c>NTarget</c> stars: the stars that were found are bright enough,
        /// there are just too few of them.
        ///
        /// <para><b>Why this is not <see cref="ExposureIsNotTheLimit"/>.</b> The per-frame statistic answers "are
        /// the stars I found bright enough?" — and under all-frames-short it is a median of FAINTEST SURVIVORS
        /// (see <see cref="ExposureRecommender.Recommend"/>'s per-frame reduction), so it cannot also answer "are
        /// there enough stars?". On a star-poor field the two questions have opposite answers, and treating a
        /// healthy S/N as proof that a longer exposure cannot help is simply wrong: exposure acts UPSTREAM of the
        /// gate, on candidate formation. Measured on a 3800 mm rig, 2 s → 5 s raised structure candidates 181 →
        /// 201 and detected stars 76 → 101 while the stars already found were comfortably above the gate.</para>
        ///
        /// <para><b>What it promises.</b> Only a direction. <see cref="RecommendedSeconds"/> is a fixed
        /// <see cref="ExposureRecommender.StarCountProbeFactor"/> probe, not a derived figure — whether more
        /// exposure reveals more stars depends on the field. If the star count does not rise, the field is the
        /// limit and no exposure will fix it; callers must say so rather than let the user climb to the cap.</para>
        /// </summary>
        public bool StarCountIsTheLimit { get; set; }

        /// <summary>
        /// True when the S/N target is met, every frame is short of the star-count target, AND the gate rejected
        /// NOTHING on any non-recovery frame — the run has run out of stars to find, not out of signal.
        ///
        /// <para><b>Why zero rejections settles it.</b> More exposure raises the star count two ways: by pushing
        /// existing candidates over the gate, or by forming new ones. Zero rejections empties the first by
        /// observation. And a field with more to give would already be forming candidates from its fainter stars
        /// and depositing them in the rejected pile — an empty pile means the detector is seeing everything the
        /// frame contains. Measured on the reported rig: gate rejections near focus went 5 → 2 → 0 across 2/7/14 s
        /// while the star count held at 10 per frame and candidate formation FELL 62 → 55.</para>
        ///
        /// <para>Mutually exclusive with <see cref="StarCountIsTheLimit"/>: same short-frame situation, opposite
        /// answer on whether an exposure probe has any mechanism to work through. No longer exposure is offered
        /// here — <see cref="IncreasesExposure"/> is false — because the honest answer is that the field supports
        /// fewer stars than the target asks for.</para>
        /// </summary>
        public bool StarFieldIsExhausted { get; set; }

        /// <summary>
        /// Candidates rejected by the Sensitivity gate across the non-recovery frames — the evidence behind
        /// <see cref="StarCountIsTheLimit"/> vs <see cref="StarFieldIsExhausted"/>. 0 when the caller did not
        /// populate <see cref="RunEvaluationMetrics.FrameLowSensitivityCounts"/>, which reads as "exhausted"; that
        /// is the conservative direction, since it withholds an exposure recommendation rather than inventing one.
        /// </summary>
        public int GateRejectedCount { get; set; }

        /// <summary>
        /// <see cref="StarDetector.InertSensitivityBound"/> for the detector settings this recommendation was
        /// computed from — the gate value at or below which the Sensitivity gate provably cannot reject anything.
        /// <see cref="double.NaN"/> when <see cref="ExposureRecommender.Recommend"/> was called without detector
        /// params, which is treated as "unknown", never as "inert".
        /// </summary>
        public double InertGateBound { get; set; } = double.NaN;

        /// <summary>
        /// F19 — the fraction of CANDIDATES FORMED on the sweep's WING frames that the Sensitivity gate rejected:
        /// <c>rejected / (rejected + accepted)</c> over the outer
        /// <see cref="ExposureRecommender.WingFrameFraction"/> of the non-recovery frames, ordered by distance
        /// from the fitted focus. <see cref="double.NaN"/> when the run cannot place its frames on that axis (no
        /// fitted focus, no step size, or no per-frame focuser positions) — never 0, because "we could not look"
        /// and "we looked and nothing was shedding" must not be the same number.
        ///
        /// <para><b>Why this quantity and not another.</b> Every accepted star's gate statistic strictly exceeds
        /// <see cref="StarDetector.EffectiveSensitivityGate"/> — that is what
        /// <see cref="StarDetector.InertSensitivityBound"/> proves — so ANY order statistic over accepted stars,
        /// at any rank, on any subset of frames, is bounded below by that gate. When the optimizer lands a gate at
        /// or above <see cref="ExposureRecommender.TargetSensitivity"/>, <see cref="ExposureIsNotTheLimit"/> is
        /// therefore true BY CONSTRUCTION, whatever the sky contains. Measured: <c>D02_rich_135mm</c> lands its
        /// effective gate at 16.7 / 50.0 / 34.3 / 10.0 / 14.0 across a 16x exposure ladder over which σ_focus
        /// improves 48 %. <b>The REJECTED candidates are the only population in the run that is not floored by the
        /// gate</b>, and they are exactly the stars a longer exposure could convert.</para>
        ///
        /// <para><b>Why the WING frames.</b> The fit's precision rests on the sweep's outer points; the near-focus
        /// frames are the richest and dominate any median across frames. Aggregating over a wing SET (not a single
        /// frame) answers this class's own objection to a worst-frame statistic — a passing cloud or a satellite
        /// trail cannot carry the recommendation.</para>
        /// </summary>
        public double WingRejectedFraction { get; set; } = double.NaN;

        /// <summary>
        /// F19 — <see cref="WingRejectedFraction"/> divided by the SAME quantity computed on the INNER third:
        /// the control the absolute fraction never had. <b>A MEASUREMENT ONLY. Nothing acts on it</b>, and
        /// nothing may until RULE W1–W6 have been met on an arm run for it.
        ///
        /// <para><b>Why the ratio, and why it is not obviously right either.</b> Wave 10's population check
        /// refuted the absolute fraction on 39 runs: it fired on the great majority, and four real-bank runs
        /// fired while rejecting FEWER candidates in their wings than in their cores. The claim was always
        /// comparative — <i>the wings are losing faint stars a longer exposure would convert</i> — so the ratio is
        /// the coordinate the claim was actually about. But <c>D17_cdk14_oiii5</c>, one of the two datasets a
        /// wing statistic exists to catch, has a ratio of <b>0.59</b>, and <c>D20_m24_bright_control</c> — the
        /// bank's BRIGHT CONTROL — has an inner fraction of exactly 0.000 and therefore an INFINITE ratio.
        /// <b>Both are expected to refute it</b>, and they are written down here before the pass so that meeting
        /// them cannot be presented as a surprise.</para>
        ///
        /// <para><b>Four states, because two different things divide by zero</b> — the NaN-never-0 rule extended
        /// rather than reinterpreted:</para>
        /// <list type="bullet">
        /// <item><see cref="double.NaN"/> — the run cannot be placed on the wing axis, or the inner third formed
        ///   no candidates at all. <i>"We could not look."</i></item>
        /// <item><see cref="double.PositiveInfinity"/> — the inner third formed candidates and rejected NONE
        ///   while the wings rejected some. <i>"The wings reject and the cores do not"</i> — a real measurement
        ///   and the strongest signal this statistic can carry, not an error.</item>
        /// <item><c>1.0</c> — both thirds formed candidates and both rejected nothing. Equal rates, and equal is
        ///   what a ratio of equal things is. It is NOT NaN: the instrument looked and found symmetry.</item>
        /// <item>otherwise the ratio.</item>
        /// </list>
        ///
        /// <para><b>Newtonsoft writes the first two as the STRINGS <c>"NaN"</c> and <c>"Infinity"</c>.</b> A
        /// scorer that coerces either to a number disables its own falsification rule — which is exactly how wave
        /// 10's RULE P scorer came to read an unmeasured dataset as one that did not fire.</para>
        /// </summary>
        public double WingRejectedRatio { get; set; } = double.NaN;

        /// <summary>
        /// True when the run's Sensitivity gate sat at or below <see cref="InertGateBound"/> AND
        /// <see cref="GateRejectedCount"/> is 0 — the gate could not have rejected anything, so its zero rejection
        /// count is empty by construction and is NOT evidence that the star field is exhausted (F28). Always false
        /// when the bound is unknown.
        ///
        /// <para>The <c>GateRejectedCount == 0</c> conjunct is a consistency guard rather than part of the proof:
        /// an observed rejection refutes the derivation and wins, which can only happen when the params handed to
        /// <see cref="ExposureRecommender.Recommend"/> do not describe the run that produced the metrics.</para>
        /// </summary>
        public bool GateIsProvablyInert { get; set; }

        /// <summary>
        /// Candidates rejected as flat-topped across the whole run (recovery frames included). Heavily defocused
        /// stars go flat-topped and that gate has no defocus-aware relaxation, so a non-zero count here points at
        /// SWEEP GEOMETRY — the sweep reaches further from focus than the detector can follow — rather than at
        /// exposure or the gate. Callers surface it as "narrow the sweep", never as "expose longer".
        /// </summary>
        public int FlatRejectedCount { get; set; }

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
        /// the flag <see cref="StarSignalCopy"/> reads when it decides whether to label the row "capped at N s";
        /// <see cref="WasCapped"/> stays as the lower-level "RawSeconds was reduced" fact for diagnostics.
        /// </summary>
        public bool CapLimitsRecommendation => WasCapped && IncreasesExposure;
    }

    /// <summary>
    /// Recommends a longer auto-focus exposure time from the measured Sensitivity-gate SNR of the stars an
    /// optimizer run actually accepted (<see cref="RunEvaluationMetrics.FrameStarSnrs"/>). Static, pure, no VM and
    /// no NINA types — the math is fully testable in isolation. The optimizer wizard's Summary renders it through
    /// <see cref="StarSignalCopy"/> and acts on it through that page's "Capture a new sweep and optimize" button.
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
        /// The factor to probe with under <see cref="ExposureRecommendation.StarCountIsTheLimit"/>, where the
        /// sky-limited derivation does not apply and there is nothing to derive a magnitude FROM. Whether a longer
        /// exposure reveals more stars depends on the field's luminosity function — how many stars sit just below
        /// the structure-detection threshold — which a single sweep cannot measure. So this state offers a
        /// DIRECTION to test rather than an answer: double it, look at the star count, and decide from that. A
        /// doubling is the smallest step big enough to read off the star count unambiguously, and it stays well
        /// inside <see cref="MaxExposureFactor"/> so the user can repeat it before the run-relative cap binds.
        /// Measured on a 3800 mm rig: 2 s → 5 s took detected stars 76 → 101 and structure candidates 181 → 201,
        /// so the probe does find stars when they are there — but that is one field, which is precisely why this
        /// is a probe and not a formula.
        /// </summary>
        public const double StarCountProbeFactor = 2.0;

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

        /// <summary>
        /// The outer fraction of a run's non-recovery frames, by distance from the fitted focus, that counts as
        /// the WING for <see cref="ExposureRecommendation.WingRejectedFraction"/>. A third of a 9-point sweep is
        /// its outermost 3 frames — one full offset step past the shoulder on a default sweep, which is where the
        /// V-curve's slope is measured and where a defocused star is faintest per pixel.
        ///
        /// <para>A SET rather than the single outermost frame, which is what answers this class's own objection to
        /// a worst-frame aggregation: one passing cloud, satellite trail or guiding bump cannot carry the
        /// recommendation, because the fraction is pooled over the whole wing.</para>
        /// </summary>
        public const double WingFrameFraction = 1.0 / 3.0;

        // ── F19: THE WING VERDICT IS WITHDRAWN (wave 10) ────────────────────────────────────────────────────
        //
        // Wave 9 shipped WingIsShedding (fraction >= 0.20) and a 2x probe, validated on TWO datasets. Wave 10 ran
        // the population check wave 9 recorded as still owed, over both full banks, and it fired:
        //
        //   * it fires on the great majority of runs, so it is a constant rather than a diagnosis;
        //   * FOUR real-bank runs reject FEWER candidates in their WINGS than in their CORES -- LinwoodFocus 0.70,
        //     vsn07 0.94, toml999 0.96, cwhite_2026 0.98 as a wing/inner ratio -- and every one of them fires. The
        //     statistic cannot separate "the wings are losing faint stars a longer exposure would convert" from
        //     "the detector rejects noise everywhere", and on the bank it measures the second;
        //   * the threshold does NO WORK. Measured fractions are 0.000, 0.000, then 0.352-0.879 with nothing in
        //     between, so EVERY threshold in (0, 0.352) selects the same runs. What the test actually asks is
        //     "did the gate reject anything at all?", which is F28's question and which GateIsProvablyInert
        //     already answers with its own field.
        //
        // WHY IT LOOKED RIGHT: the unit fixtures span a rejected fraction of 0.002 ("healthy wings") to 0.75
        // ("shedding"), and 0.20 sits sensibly between them. NO REAL RUN RESEMBLES THE HEALTHY POLE. A unit test
        // cannot notice that its fixtures span a range the data does not occupy; only a population can.
        //
        // WHAT IS WITHDRAWN: the VERDICT (WingIsShedding, its threshold, and the probe factor) and the three
        // things that acted on it -- the 2x ask, the ExposureIsNotTheLimit override, and the wizard's
        // "consider cancelling this search" advice (F52(c), which therefore returns to wave 8's position of being
        // withheld for want of a statistic -- the same decision, made again, on better evidence).
        //
        // WHAT IS KEPT: WingRejectedFraction, the MEASUREMENT. It is real, it is correctly computed, and the
        // successor is specified against it. A public bool named "is shedding" that nothing acts on would be
        // worse than either shipping or removing it, so the boolean goes and the number stays.
        //
        // THE SUCCESSOR, pre-registered in the wave-10 design SS1.2a and DELIBERATELY NOT ADOPTED HERE: the
        // wing-to-inner RATIO, which is what the claim was always about. It must clear RULE W1-W4 unchanged, plus
        // W5 (its threshold must sit above the bank's median wing/inner ratio, or it is the same defect in a new
        // coordinate) and W6 (it may NOT be validated on the population that refuted its predecessor). A
        // statistic tuned on the data that killed the last one has been fitted, not tested.

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
        /// never exceed the cap that produced it — <see cref="RoundExposureSeconds"/> always rounds UP, so on the
        /// "capped value exceeds current" branch it can push <c>cappedSeconds</c> past whichever cap bound it by up
        /// to one ladder step (a capped value of 8.8 s — the RUN-RELATIVE <see cref="MaxExposureFactor"/> cap on a
        /// 2.2 s current exposure — rounds to 9.0 s). What cap-before-round actually buys is that the overshoot is
        /// BOUNDED to at most one ladder step, rather than the unbounded overshoot a round-then-cap ordering would
        /// leave uncorrected. That overshoot can never carry the result past the ABSOLUTE
        /// <see cref="MaxRecommendedExposureSeconds"/> ceiling itself on this branch: <c>cappedSeconds &lt;= 30</c>
        /// always, and every granularity <see cref="RoundExposureSeconds"/> uses for an input &lt;= 30 (0.5 s or
        /// 1.0 s) divides 30 evenly, so the ceiling lands AT 30 at most, never past it. A caller still MUST NOT
        /// assume <c>RecommendedSeconds &lt;= MaxRecommendedExposureSeconds</c> overall, though: the OTHER branch
        /// (an already-long <paramref name="currentExposureSeconds"/> past the absolute cap, e.g. narrowband)
        /// returns <paramref name="currentExposureSeconds"/> unchanged, which can exceed 30 with nothing to do with
        /// rounding.</para>
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
        /// <param name="detectorParams">
        /// The detector settings the scored run was evaluated WITH, used only to compute
        /// <see cref="StarDetector.InertSensitivityBound"/> so a Sensitivity gate that provably cannot reject
        /// anything is not mistaken for a field with nothing left (F28). MUST be the variant's own params — pair
        /// optimized metrics with optimized params and baseline metrics with baseline params, the same way
        /// <c>OptimizationSummary.VariantSensitivity</c>/<c>BaselineSensitivity</c> are paired. Null ⇒ the bound is
        /// unknown, and every verdict is identical to the pre-F28 behaviour.
        /// </param>
        public static ExposureRecommendation Recommend(RunEvaluationMetrics metrics, ObjectiveConstants c, double currentExposureSeconds,
                                                       StarDetectorParams detectorParams = null) {
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
            // Gate rejections on the SAME non-recovery frames the SNR statistic is built from, so the two describe
            // one population. A rejection here is a candidate that EXISTS and merely fell short of the gate -- the
            // only mechanism by which more exposure can raise the star count on frames the detector already sees.
            var gateRejectedCount = 0;
            var lowSensByFrame = metrics.FrameLowSensitivityCounts;
            for (var i = 0; i < snrsByFrame.Count; i++) {
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue; // far-from-focus recovery wing: by design few/no stars, not representative of gate strength
                }
                if (lowSensByFrame != null && i < lowSensByFrame.Count) {
                    gateRejectedCount += lowSensByFrame[i];
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

            // Flat-topped rejections concentrated on the OUTER frames mean the sweep runs past where the detector
            // can follow the star profile, which is a sweep-geometry problem no exposure fixes. Measured over the
            // whole run (recovery frames included): those wings are exactly where it shows up.
            var flatRejectedCount = 0;
            var flatByFrame = metrics.FrameTooFlatCounts;
            if (flatByFrame != null) {
                for (var i = 0; i < flatByFrame.Count; i++) {
                    flatRejectedCount += flatByFrame[i];
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

            // A sufficient S/N splits two ways, because sNow answers only "are the stars I found bright enough?".
            // When every frame is short of NTarget, sNow is a median of faintest-survivors and there is no
            // un-degraded sample anywhere in the run, so it cannot also answer "are there enough stars?" — see
            // StarCountIsTheLimit. A run with any full frame keeps a real NTarget-th-star measurement, which is why
            // the split is all-short rather than any-short (it also leaves the star-flooding corner, rich frames at
            // a floored gate, reporting ExposureIsNotTheLimit exactly as before).
            var signalIsSufficient = sNow >= TargetSensitivity;
            var everyFrameShort = usableFrameCount > 0 && shortFrameCount == usableFrameCount;

            // The probe is only honest while there is a MECHANISM for more exposure to help. A longer exposure
            // raises the star count two ways: it pushes existing candidates over the gate, or it forms new ones.
            // When the gate rejected NOTHING, the first is empty by observation; and if the field also had more to
            // give, those fainter stars would already be forming candidates and landing in the rejected pile. So
            // zero rejections across every non-recovery frame is the run telling us the population is exhausted.
            //
            // Measured on the reported rig: at 2 s the gate rejected 5 near-focus candidates, at 7 s two, at 14 s
            // ZERO -- while the star count sat at 10/frame throughout and candidate formation fell 62 -> 55. Seven
            // times the exposure bought six stars across five frames. Without this test the probe recommended
            // another doubling every run, and the user climbed 2 s -> 14 s on its advice for nothing.
            var gateIsHoldingStarsBack = gateRejectedCount > 0;

            // ...but a count of zero only MEANS anything when the gate could have produced a non-zero one. The
            // clip/normalize stage guarantees every candidate's gate statistic exceeds
            // PeakResponse × EffectiveClipMultiplier (1.5 at shipped defaults), so a Sensitivity at or below that
            // rejects NOTHING regardless of what the frames contain, and LowSensitivity is identically 0. That
            // inert region strictly CONTAINS the band this advice is surfaced in — OptimizationSummary
            // .HasLowStarSignal fires at Sensitivity <= 1.0 — so reading the empty counter as exhaustion made
            // StarFieldIsExhausted unconditional and StarCountIsTheLimit unreachable for precisely the population
            // the affordance was written for. That is F28.
            //
            // The bound is computed, never hard-coded: PeakResponse and StarClippingMultiplier are both searched
            // axes, and the donut master caps the effective clip for extended candidates.
            //
            // This does NOT walk back the 2/7/14 s measurement above. That rig rejected 5 candidates at 2 s, so
            // its gate was demonstrably live; it still lands on starFieldIsExhausted, unchanged. The two
            // populations are disjoint by construction — an inert gate requires gateRejectedCount == 0.
            var inertGateBound = StarDetector.InertSensitivityBound(detectorParams);
            // Observation beats derivation: an actual rejection refutes the proof (which can only mean the params
            // do not describe the run that produced these metrics), so it clears the flag rather than arguing with
            // it. Unknown params leave the flag false, i.e. byte-identical to the pre-F28 behaviour.
            var gateIsProvablyInert = !gateIsHoldingStarsBack && double.IsFinite(inertGateBound)
                && detectorParams.Sensitivity <= inertGateBound;
            // An absent measurement is not a negative one. An inert gate falls to the PROBE — which ships with its
            // own stopping rule — rather than to a verdict of exhaustion nothing in the run supports.
            var starCountIsTheLimit = signalIsSufficient && everyFrameShort && (gateIsHoldingStarsBack || gateIsProvablyInert);
            var starFieldIsExhausted = signalIsSufficient && everyFrameShort && !gateIsHoldingStarsBack && !gateIsProvablyInert;

            // F19 — THE WING TEST. Everything above is computed over ACCEPTED stars, and acceptance floors every
            // one of them at StarDetector.EffectiveSensitivityGate; so when the optimizer lands a gate at or above
            // TargetSensitivity, `signalIsSufficient` is true BY CONSTRUCTION and no rank, on no subset of frames,
            // could ever say otherwise. That is why widening the TRIGGER (wave 8, rule R5) and changing NTarget
            // (wave 7) were both refuted: neither touches the population being measured.
            //
            // The candidates the gate REJECTED on the wing frames are the only population in the run that is not
            // floored by the gate, and they are exactly the stars a longer exposure can convert. See
            // ExposureRecommendation.WingRejectedFraction.
            var wingRejectedFraction = WingRejectedFractionOf(metrics, isRecovery);
            // And its CONTROL, which the absolute fraction never had: the same quantity on the INNER third. Wave
            // 10's population check showed the absolute fraction cannot separate "the wings are losing faint
            // stars" from "the detector rejects noise everywhere" -- four real-bank runs fired with wings CLEANER
            // than their cores. The ratio is the coordinate the claim was always about. It is a MEASUREMENT ONLY
            // and is expected to be refuted in its turn (D17 sits at 0.59; D20, the bright control, is infinite).
            var wingRejectedRatio = WingRejectedRatioOf(metrics, isRecovery);
            // NO VERDICT IS DERIVED FROM EITHER (wave 10 -- see the withdrawal note above the constants). They are
            // reported; nothing acts on them. Wave 9's `wingIsShedding` overrode the line below, which is how a
            // statistic that fires on most runs came to flip most users' exposure verdict.
            var exposureIsNotTheLimit = signalIsSufficient && !everyFrameShort;

            // Sky-limited scaling answers the S/N question only. Under starCountIsTheLimit the ratio is <= 1, so it
            // would ask for a SHORTER exposure — backwards for a run whose problem is too few stars. That state
            // probes with a fixed factor instead; there is nothing here to derive a magnitude from.
            var ratio = TargetSensitivity / sNow;
            // starFieldIsExhausted takes the plain ratio (<= 1 here, since signalIsSufficient), which the
            // never-shorter floor then collapses onto the current exposure -- so IncreasesExposure is false and no
            // caller can offer a longer exposure for a run that has demonstrably nothing to gain from one.
            var rawFactor = starCountIsTheLimit ? StarCountProbeFactor : ratio * ratio;
            // The wing PROBE is withdrawn (wave 10). It raised the ask to exactly 2x on every run it fired on --
            // the accepted-star term measured 0.000-0.30 on all of them, so max() always chose the probe -- which
            // means the recommendation carried no information beyond "it fired".
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
                ExposureIsNotTheLimit = exposureIsNotTheLimit,
                StarCountIsTheLimit = starCountIsTheLimit,
                StarFieldIsExhausted = starFieldIsExhausted,
                GateRejectedCount = gateRejectedCount,
                FlatRejectedCount = flatRejectedCount,
                InertGateBound = inertGateBound,
                GateIsProvablyInert = gateIsProvablyInert,
                WingRejectedFraction = wingRejectedFraction,
                WingRejectedRatio = wingRejectedRatio
            };
        }

        /// <summary>
        /// F19 — the fraction of candidates FORMED on the sweep's wing frames that the Sensitivity gate rejected.
        /// See <see cref="ExposureRecommendation.WingRejectedFraction"/> for why this population and not another.
        ///
        /// <para>Returns <see cref="double.NaN"/> — never 0 — when the run cannot be placed on the wing axis: a
        /// non-finite <see cref="RunEvaluationMetrics.BestFocusPosition"/> (no usable fit), a non-positive
        /// <see cref="RunEvaluationMetrics.StepSize"/>, absent focuser positions, or absent per-frame rejection
        /// counts. "We could not look" and "we looked and nothing was shedding" must not be the same number,
        /// because the caller turns one of them into an instruction. Every one of those absences is the DEFAULT for
        /// a caller that does not populate the optional per-frame data, so this is inert unless the data supports
        /// it.</para>
        ///
        /// <para>Recovery frames are excluded, matching <see cref="Recommend"/>'s own per-frame loop: they are
        /// by-design far from focus, so including them would put the deliberately-starved frames in the population
        /// whose starvation is being measured.</para>
        /// </summary>
        internal static double WingRejectedFractionOf(RunEvaluationMetrics metrics, IReadOnlyList<bool> isRecovery) {
            var usable = WingAxis(metrics, isRecovery);
            if (usable == null) {
                return double.NaN;
            }

            // At least one frame, so a short sweep still answers rather than silently declining to.
            var wingCount = Math.Max(1, (int)Math.Round(usable.Count * WingFrameFraction));
            var (rejected, accepted) = PoolRange(usable, 0, wingCount);
            var formed = rejected + accepted;
            // No candidates formed at all out here is not a shedding wing -- it is a frame with nothing on it, which
            // is StarCountIsTheLimit's question, not this one.
            return formed > 0 ? (double)rejected / formed : 0.0;
        }

        /// <summary>
        /// F19's successor — <see cref="WingRejectedFractionOf"/> over the OUTER third divided by the same
        /// quantity over the INNER third. See <see cref="ExposureRecommendation.WingRejectedRatio"/> for the
        /// four-state contract and for why both <c>D17</c> and <c>D20</c> are expected to refute it.
        ///
        /// <para><b>A MEASUREMENT ONLY.</b> Nothing in the product reads it. Wave 10 withdrew its predecessor's
        /// verdict on a 39-run population and pre-registered this coordinate together with the rule that must be
        /// met before anything acts on it — precisely so it could not be adopted on the data that killed the last
        /// one.</para>
        /// </summary>
        internal static double WingRejectedRatioOf(RunEvaluationMetrics metrics, IReadOnlyList<bool> isRecovery) {
            var usable = WingAxis(metrics, isRecovery);
            if (usable == null) {
                return double.NaN;
            }

            var thirdCount = Math.Max(1, (int)Math.Round(usable.Count * WingFrameFraction));
            // The two thirds must be DISJOINT or the ratio compares a set with itself and returns 1.0 for every
            // short sweep -- a number that looks like a measurement and is not one. Declining is the honest
            // answer, and it is NaN rather than 0 for the reason the whole statistic is NaN-never-0.
            if (thirdCount * 2 > usable.Count) {
                return double.NaN;
            }

            var (wingRejected, wingAccepted) = PoolRange(usable, 0, thirdCount);
            var (innerRejected, innerAccepted) = PoolRange(usable, usable.Count - thirdCount, thirdCount);
            var wingFormed = wingRejected + wingAccepted;
            var innerFormed = innerRejected + innerAccepted;
            if (innerFormed == 0) {
                // Nothing was FORMED in the core, so there is no rate to compare against. "We could not look."
                return double.NaN;
            }

            var wingFraction = wingFormed > 0 ? (double)wingRejected / wingFormed : 0.0;
            var innerFraction = (double)innerRejected / innerFormed;
            if (innerFraction == 0.0) {
                // The core formed candidates and rejected NONE. If the wings rejected some, that is the strongest
                // signal this statistic can carry and it is genuinely unbounded -- not an error to be clamped.
                // If the wings also rejected none, the two rates are EQUAL, and the ratio of equal things is 1.
                return wingFraction > 0.0 ? double.PositiveInfinity : 1.0;
            }
            return wingFraction / innerFraction;
        }

        /// <summary>
        /// The non-recovery frames that can be placed on the wing axis — <c>(|offset from the fitted focus|,
        /// rejected, accepted)</c>, FARTHEST FROM FOCUS FIRST. Null when the run cannot be placed at all.
        ///
        /// <para>A frame missing any of the three is left OUT rather than defaulted to zero: a defaulted frame
        /// would enter the pool as "formed nothing and rejected nothing", which reads as a healthy wing.</para>
        /// </summary>
        private static List<(double Offset, int Rejected, int Accepted)> WingAxis(
                RunEvaluationMetrics metrics, IReadOnlyList<bool> isRecovery) {
            var positions = metrics?.FrameFocuserPositions;
            var lowSens = metrics?.FrameLowSensitivityCounts;
            var counts = metrics?.FrameStarCounts;
            if (positions == null || lowSens == null || counts == null
                || !double.IsFinite(metrics.BestFocusPosition) || !(metrics.StepSize > 0.0)) {
                return null;
            }

            var usable = new List<(double Offset, int Rejected, int Accepted)>(positions.Count);
            for (var i = 0; i < positions.Count; i++) {
                if (IsRecoveryFrame(isRecovery, i) || i >= lowSens.Count || i >= counts.Count) {
                    continue;
                }
                usable.Add((Math.Abs(positions[i] - metrics.BestFocusPosition), lowSens[i], counts[i]));
            }
            if (usable.Count == 0) {
                return null;
            }
            usable.Sort((a, b) => b.Offset.CompareTo(a.Offset)); // farthest from focus first
            return usable;
        }

        private static (long Rejected, long Accepted) PoolRange(
                List<(double Offset, int Rejected, int Accepted)> usable, int start, int count) {
            long rejected = 0, accepted = 0;
            for (var i = start; i < start + count; i++) {
                rejected += usable[i].Rejected;
                accepted += usable[i].Accepted;
            }
            return (rejected, accepted);
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
                ExposureIsNotTheLimit = false,
                StarCountIsTheLimit = false,
                StarFieldIsExhausted = false,
                GateRejectedCount = 0,
                FlatRejectedCount = 0,
                InertGateBound = double.NaN,
                GateIsProvablyInert = false,
                // NaN, not 0: this run produced no recommendation at all, so it did not look at its wings either,
                // and a consumer must not read a confident zero out of that.
                WingRejectedFraction = double.NaN,
                WingRejectedRatio = double.NaN
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
