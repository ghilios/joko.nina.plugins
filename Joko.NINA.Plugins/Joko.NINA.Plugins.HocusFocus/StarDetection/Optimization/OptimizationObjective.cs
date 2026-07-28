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
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// All weights and thresholds of the composite optimization objective, in one editable place. Defaults
    /// come from the Star Detection Optimization Wizard design (the constant names match the design):
    ///   Wf/Ws/Wc/Wl — sub-score weights (focus / star-count / curve-fit / label);
    ///   RhoRef      — reference normalized focus σ for S_focus;
    ///   NFloor/NTarget — star-count knees for S_stars;
    ///   ChiTau      — reduced-χ² knee for S_fit;
    ///   NHard / MaxFramesBelowHardFloor — hard constraint on per-frame star count;
    ///   Beta        — multi-run min/mean blend.
    /// </summary>
    public sealed class ObjectiveConstants {
        public double Wf { get; set; } = 0.55;   // focus weight
        public double Ws { get; set; } = 0.20;   // star-count weight
        public double Wc { get; set; } = 0.25;   // curve-fit weight
        public double Wl { get; set; } = 0.25;   // label weight (only when labels are present)

        public double RhoRef { get; set; } = 0.25;   // S_focus reference ρ = σ/step

        public int NFloor { get; set; } = 8;     // S_stars: nMin knee
        public int NTarget { get; set; } = 20;   // S_stars: nMedian knee

        // S_fit penalty knee on reduced-χ². reducedχ² runs ≪1 for star-rich fields (over-fit-looking but
        // benign), so ONLY the high side is penalized — below ChiTau there is no penalty at all.
        public double ChiTau { get; set; } = 2.0;

        public int NHard { get; set; } = 3;                  // hard floor: frames with starCount < NHard are "bad"
        public int MaxFramesBelowHardFloor { get; set; } = 0; // more than this many bad frames => J_run = 0

        public double Beta { get; set; } = 0.5;  // J_total = (1-β)·mean + β·min over runs

        // ── Plateau tie-breaker (Wtie) ─────────────────────────────────────────────────────────────────────
        // On an easy AF run every primary sub-score clamps to 1.0 (sharp focus, perfect fit, counts past the
        // S_stars knees), so J saturates at 1.0 over a whole plateau of settings and the search — which only
        // accepts strictly-improving moves and seeds from the DEFAULT params — wanders to an arbitrary tie-point
        // far from (and often worse than) the user's settings (e.g. it halves the star count for no J gain).
        // TieBreakerScore is an UNSATURATING secondary score (more stars / lower σ ⇒ higher, forever) blended
        // into J as a convex combination: J = (1−Wtie)·J_primary + Wtie·T. Wtie is small so a GENUINE primary-J
        // difference still dominates and off-plateau ranking is unchanged; on the (near-)plateau (J_primary tied to
        // within the σ-wiggle) T decides, steering toward the star-rich / sharper basin. Wtie = 0 ⇒ J is
        // bit-identical to the pre-tie-breaker objective. See docs/optimizer-plateau-tiebreaker-design.md.
        //
        // Strengthened 1e-3 → 0.02 (docs/optimizer-sensitivity-pinning-design.md): the objective is BISTABLE in the
        // (sensitivity, star-clip) plane — a star-SHEDDING corner (high sensitivity/clip, few stars) and a
        // star-RICH corner score essentially the same J, and on a saturated plateau the shedding corner can hold a
        // ~1e-3 σ-focus edge that the original tiny Wtie could not out-vote (bobp_m101: it pinned Sensitivity 50 /
        // StarClip 9.5 → recall@SNR≥12 0.13 while a star-rich config scored ~0.87 at the same J). 0.02 is calibrated
        // to bracket the two regimes: it reliably out-votes a fine-step plateau σ-wiggle (primary-Δ ≲ 4e-3, the
        // bobp_m101 case) yet stays below a GENUINE focus-quality gap (primary-Δ ~9e-3 for a 30% σ difference at a
        // coarse step — ForAberrationInspection_FlipsRankingTowardMoreStars — and ≫1e-2 for
        // JRun_TieBreaker_DoesNotOverrideRealPrimaryDifference), so it only ever decides genuine ties.
        public double Wtie { get; set; } = 0.02;

        // ── F3: label-free precision / false-positive penalty (SDefocusPrecision) ──────────────────────────
        // These gate the MULTIPLICATIVE penalty the objective applies for defocus-relaxation that admits junk.
        // They are deliberately CONSERVATIVE: with the defocus-aware gates OFF (the baseline) every per-frame
        // relaxed count is 0, so the penalty is exactly 1.0 (J bit-identical); and even with the gates ON the
        // LEGITIMATE donut recovery seen on the defocused EXTREMES is never penalized (only NEAR-FOCUS relaxation
        // is, since by the gate's size-scaling a near-focus real star is small and never needs relaxation, so a
        // near-focus relaxed star is necessarily a large/low-fill junk blob). Tuned empirically on the AF bank.

        // Half-width of the "near focus" window, in units of the AF step size: frames whose focuser position is
        // within NearFocusWindowSteps · stepSize of the fitted best-focus position count as near-focus. 1.5 steps
        // keeps the window tight around the minimum (where real stars are smallest/sharpest).
        public double NearFocusWindowSteps { get; set; } = 1.5;

        // The fraction of near-focus accepted stars that may be relaxation-admitted before the penalty begins to
        // bite. Chosen generously (0.20) so a couple of borderline near-focus admissions are tolerated and only a
        // sustained near-focus relaxed fraction (the junk signature) is penalized.
        public double DefocusPrecisionThreshold { get; set; } = 0.20;

        // Penalty strength: how hard J is scaled down per unit of excess near-focus relaxed fraction above the
        // threshold. The penalty is 1 − Strength · max(0, relaxedFrac − Threshold), clamped to [MinFactor, 1].
        // Conservative default (0.5) so even a fully-relaxed near-focus frame can at most halve-ish J, never zero it.
        public double DefocusPrecisionStrength { get; set; } = 0.5;

        // Floor on the multiplicative penalty so the precision term can never drive J to 0 on its own (the hard
        // floors / σ checks own the hard-fail path). 0.5 ⇒ at most a 2× reduction from this term alone.
        public double DefocusPrecisionMinFactor { get; set; } = 0.5;

        // Fallback signal guard (used only when per-frame focuser positions / fitted minimum are unavailable, so
        // the near-focus window cannot be formed): require at least this many frames AND this many total accepted
        // stars before the run-level relaxed-fraction fallback may apply any penalty.
        public int MinFramesForPenalty { get; set; } = 3;
        public int MinAcceptedForPenalty { get; set; } = 1;

        // ── Aberration-inspection fit guard (SFitGuard) ────────────────────────────────────────────────────
        // The "Optimize for Aberration Inspection" mode reweights J toward star count (raised Ws + NFloor/NTarget
        // knees, lowered Wf) so the optimizer trades some AF-curve sharpness for many more stars across the frame
        // (what a tilt/curvature model needs). To keep the fit from degrading without bound, SFitGuard applies a
        // MULTIPLICATIVE penalty once the focus σ grows past a margin over a reference σ (the sharpest curve the
        // data supports — see StarDetectionOptimizerWizardVM, which seeds ReferenceSigmaFocus from the current
        // settings' σ and ratchets it down to the best σ the search has seen).
        //
        // ReferenceSigmaFocus is NaN for the STANDARD objective, which makes SFitGuard return exactly 1.0 (J
        // bit-identical to the pre-guard objective — every existing test is unaffected). It is set to a finite σ
        // only by ForAberrationInspection / the wizard's inspection path.
        public double ReferenceSigmaFocus { get; set; } = double.NaN;

        // Allowed σ degradation before the penalty starts, as a fraction of ReferenceSigmaFocus: the penalty is
        // exactly 1.0 while σ ≤ (1 + FitGuardMarginFraction)·ReferenceSigmaFocus. CALIBRATED on a real defocus-aware
        // AF run (astrodet) so the star-recovery optimum (low sensitivity) lands inside the margin while a genuinely
        // bad fit does not — see docs / the plan's calibration step. Only consulted when ReferenceSigmaFocus is finite.
        public double FitGuardMarginFraction { get; set; } = 0.5;

        // Penalty strength: how hard J is scaled down per unit of excess σ fraction above the margin. The penalty is
        // 1 − Strength · max(0, σ/refσ − (1 + Margin)), clamped to [MinFactor, 1].
        public double FitGuardStrength { get; set; } = 1.5;

        // Floor on the fit-guard penalty so a single very-soft fit can't drive J to 0 on its own (the hard σ checks
        // own the hard-fail path). 0.25 ⇒ at most a 4× reduction from this term alone.
        public double FitGuardMinFactor { get; set; } = 0.25;

        // ── Extreme-HFR outlier penalty (SHfrOutlier) ──────────────────────────────────────────────────────
        // MULTIPLICATIVE penalty (modeled on SDefocusPrecision) for accepting a near-focus star whose HFR is an
        // extreme HIGH outlier — the saturated bright-blob signature that inflates the per-frame HFR. The signal is
        // an OUTLIER FRACTION (outliers / accepted) over the POOLED near-focus stars, so it SHRINKS as a lower
        // sensitivity admits more normal stars (dilution). That is what makes it actually MOVE the optimum: it
        // pulls toward higher recall instead of penalizing every config equally (the bright star is admitted at all
        // sensitivities, so an absolute max/median ratio would be inert). Returns exactly 1.0 when FrameStarHFRs is
        // null/empty OR no near-focus extreme outlier exists ⇒ J bit-identical at the baseline.
        public double HfrOutlierMadK { get; set; } = 4.0;       // robust-σ multiple (mad already scaled ×1.483)
        public double HfrOutlierRelMargin { get; set; } = 1.5;  // ALSO require HFR ≥ 1.5×median (MAD≈0 tight-frame guard)
        public double HfrOutlierThreshold { get; set; } = 0.05; // tolerate ≤5% outlier fraction before the penalty bites
        public double HfrOutlierStrength { get; set; } = 1.0;   // penalty = 1 − Strength·max(0, frac − Threshold)
        public double HfrOutlierMinFactor { get; set; } = 0.5;  // floor: at most a 2× reduction from this term alone

        // ── Region-coverage reward (SCoverage) ─────────────────────────────────────────────────────────────
        // ADDITIVE sub-score folded into the renormalized weighted sum: the near-focus mean fraction of an N×M
        // sensor tiling that holds ≥1 accepted star. Rewards SPREAD (a cluster of stars in one corner scores worse
        // than the same count spread across the sensor), pulling toward a lower sensitivity / more stars. A small,
        // REAL weight so it can cost a little SFocus off-plateau — the user explicitly wants coverage to be able to
        // cost a small amount of focus tightness, so this is NOT merely a plateau tie-breaker. Wcov = 0 OR no
        // occupancy data ⇒ the term is excluded from BOTH the numerator and the denominator ⇒ J bit-identical.
        public double Wcov { get; set; } = 0.05;

        /// <summary>
        /// Builds the constants for the "Optimize for Aberration Inspection" objective: reweighted toward star
        /// count (so the optimizer recovers many more stars across the frame for tilt/curvature modeling) while the
        /// fit stays bounded by <see cref="SFitGuard"/> relative to <paramref name="referenceSigmaFocus"/> (the
        /// sharpest AF curve the data supports). All other knobs (curve-fit weight, hard floors, tie-breaker, the
        /// defocus-precision penalty) are left at their standard values. When <paramref name="referenceSigmaFocus"/>
        /// is NaN the fit guard is inert (no σ anchor), so the mode then degrades to a pure reweight.
        /// </summary>
        public static ObjectiveConstants ForAberrationInspection(double referenceSigmaFocus) {
            return new ObjectiveConstants {
                Wf = 0.30,   // focus matters less — we accept a slightly noisier curve for more stars
                Ws = 0.45,   // star count dominates the primary trade
                NFloor = 20, // raise the knees so the extra recovered stars keep paying off (don't clamp early)
                NTarget = 60,
                ReferenceSigmaFocus = referenceSigmaFocus
            };
        }
    }

    /// <summary>
    /// Everything <see cref="OptimizationObjective.JRun"/> needs to score a single AF run. This is the contract
    /// that Task T3 (which actually runs detection, pools frames, and fits the AF curve) fills in. Label
    /// recall/precision are computed by the evaluator when labels exist (it owns the accepted-star centers),
    /// so the objective and the search engine remain label-agnostic.
    /// </summary>
    public sealed class RunEvaluationMetrics {
        public double SigmaFocus { get; set; }        // bestFit.MinimumStdError (may be NaN)
        public double LooStdError { get; set; }       // leave-one-out best-focus std-error fallback (may be NaN)
        public double StepSize { get; set; }          // AF step size used in the fit (> 0)
        public double RSquared { get; set; }          // fit R^2
        public double ReducedChiSquared { get; set; } // fit reduced χ² (NaN => treated as no penalty)
        public IReadOnlyList<int> FrameStarCounts { get; set; } // accepted star count per frame in the run

        // Per-frame relaxation-admitted accepted-star count (PARALLEL to FrameStarCounts). Each entry is the count
        // of accepted stars on that frame that survived a defocus-RELAXED gate but would have failed the strict
        // gate. ALL ZERO whenever the defocus-aware gates are OFF (the baseline), so the label-free precision
        // penalty (SDefocusPrecision) returns 1.0 and J stays bit-identical. May be null for callers that don't
        // populate it (treated as "no relaxation data" ⇒ no penalty).
        public IReadOnlyList<int> FrameRelaxationAdmittedCounts { get; set; }

        // Per-frame focuser position (PARALLEL to FrameStarCounts), needed to identify NEAR-FOCUS frames for the
        // precision penalty. May be null for callers that don't populate it (then SDefocusPrecision falls back to
        // the run-level relaxed-fraction signal).
        public IReadOnlyList<int> FrameFocuserPositions { get; set; }

        // The fitted best-focus (curve-minimum) focuser position, or NaN when no usable fit was produced. Used
        // with StepSize to define the near-focus window for the precision penalty.
        public double BestFocusPosition { get; set; } = double.NaN;

        // Per-frame accepted-star HFRs (JAGGED; PARALLEL to FrameStarCounts, with FrameStarHFRs[i].Count ==
        // FrameStarCounts[i]) WHEN POPULATED. Feeds the extreme-HFR outlier penalty (SHfrOutlier). Null (the whole
        // list) ⇒ no per-star HFR data ⇒ SHfrOutlier returns exactly 1.0 (J bit-identical at the baseline).
        // CONVENTION (also applies to the per-star-SNR field below): RunEvaluationData substitutes Array.Empty&lt;double&gt;()
        // per FRAME when that frame's producer didn't populate the per-star list, which is indistinguishable from
        // "this frame legitimately had zero accepted stars" — an empty FrameStarHFRs[i] does NOT prove
        // FrameStarCounts[i] == 0. A reader must check Count before assuming parallelism with FrameStarCounts;
        // don't index FrameStarHFRs[i][k] assuming it always has FrameStarCounts[i] entries.
        public IReadOnlyList<IReadOnlyList<double>> FrameStarHFRs { get; set; }

        // Per-frame accepted-star measured Sensitivity-gate SNRs (JAGGED; PARALLEL to FrameStarCounts, with element
        // counts matching per frame WHEN POPULATED — see the FrameStarHFRs convention note above; the same
        // empty-does-not-mean-zero-stars caveat applies here), mirroring FrameStarHFRs above. Also inherits
        // Star.MeasuredSensitivity's caveats: values are in binned-pixel space (not comparable across
        // DetectionBinning factors) and each entry is EITHER a per-pixel ratio OR a donut matched-filter SNR,
        // depending on DefocusAwareDonutDetection — two different statistics that must not be pooled as one. INERT
        // DATA — no sub-score and no term of JRun reads it; it is plumbed through purely so a later
        // exposure-recommendation feature can read the measured per-star SNR when the optimizer floors the
        // Sensitivity gate.
        public IReadOnlyList<IReadOnlyList<double>> FrameStarSnrs { get; set; }

        // Per-frame region-occupancy fraction in [0, 1] (or NaN where geometry was unavailable), PARALLEL to
        // FrameStarCounts. Feeds the region-coverage reward (SCoverage). Null ⇒ coverage is excluded from JRun
        // entirely (J bit-identical at the baseline).
        public IReadOnlyList<double> FrameRegionOccupancy { get; set; }

        // Parallel to FrameStarCounts; true ⇒ this frame is a far-from-focus recovery frame
        // (down-weighted in the fit, exempt from star-count gates). null ⇒ baseline (no recovery).
        public IReadOnlyList<bool> FrameIsRecovery { get; set; }

        // Label scores for this run, supplied by the evaluator only when labels exist; null otherwise. The
        // optimizer passes these straight through to JRun, keeping itself label-agnostic.
        public double? Recall { get; set; }
        public double? Precision { get; set; }
    }

    /// <summary>
    /// The composite objective J (maximized; every sub-score and J ∈ [0, 1]). All methods are pure and static.
    /// The math mirrors the Star Detection Optimization Wizard design; constants live in
    /// <see cref="ObjectiveConstants"/>.
    /// </summary>
    public static class OptimizationObjective {

        /// <summary>Clamps to the unit interval [0, 1].</summary>
        public static double Clamp01(double v) {
            if (double.IsNaN(v)) {
                return 0.0;
            }
            if (v < 0.0) {
                return 0.0;
            }
            if (v > 1.0) {
                return 1.0;
            }
            return v;
        }

        /// <summary>
        /// S_focus = 1 / (1 + (ρ / RhoRef)²) where ρ = σ / stepSize. σ is <paramref name="sigmaFocus"/> when
        /// finite, otherwise the leave-one-out fallback <paramref name="looStdError"/>. If NEITHER is finite,
        /// returns NaN to signal a hard failure to the caller (JRun maps that to 0). Monotone-decreasing in σ:
        /// a sharper focus relative to the step size scores higher.
        /// </summary>
        public static double SFocus(double sigmaFocus, double looStdError, double stepSize, ObjectiveConstants c) {
            var sigma = IsFinite(sigmaFocus) ? sigmaFocus : (IsFinite(looStdError) ? looStdError : double.NaN);
            if (!IsFinite(sigma)) {
                return double.NaN;
            }
            if (!(stepSize > 0.0)) {
                return double.NaN;
            }
            var rho = sigma / stepSize;
            var ratio = rho / c.RhoRef;
            return 1.0 / (1.0 + ratio * ratio);
        }

        /// <summary>
        /// S_stars = 0.6·clamp01(nMin / NFloor) + 0.4·clamp01(nMedian / NTarget). The minimum carries the
        /// larger weight so a single starved frame is penalized harder than a merely-thin median.
        /// </summary>
        public static double SStars(IReadOnlyList<int> counts, ObjectiveConstants c) {
            if (counts == null || counts.Count == 0) {
                return 0.0;
            }
            var nMin = counts.Min();
            var nMed = Median(counts);
            var minTerm = Clamp01((double)nMin / c.NFloor);
            var medTerm = Clamp01(nMed / c.NTarget);
            return 0.6 * minTerm + 0.4 * medTerm;
        }

        /// <summary>
        /// S_fit = clamp01(R²) · penalty(reducedχ²). The penalty is 1 when reducedχ² ≤ ChiTau (or NaN — no
        /// information, no penalty), and otherwise the smooth, monotone-decreasing, (0,1]-valued
        /// <c>ChiTau / reducedχ²</c> (i.e. only the high side is penalized; equals 1 exactly at the knee).
        /// </summary>
        public static double SFit(double rSquared, double reducedChiSquared, ObjectiveConstants c) {
            var penalty = 1.0;
            if (IsFinite(reducedChiSquared) && reducedChiSquared > c.ChiTau) {
                penalty = c.ChiTau / reducedChiSquared; // ∈ (0, 1) above the knee
                penalty = Clamp01(penalty);
            }
            return Clamp01(rSquared) * penalty;
        }

        /// <summary>S_label = 0.5·recall + 0.5·precision.</summary>
        public static double LabelScore(double recall, double precision) {
            return 0.5 * recall + 0.5 * precision;
        }

        /// <summary>
        /// Composite score for a single run. Returns 0 (hard fail) when the focus σ is unusable (both
        /// sigmaFocus and looStdError non-finite) OR when more than <see cref="ObjectiveConstants.MaxFramesBelowHardFloor"/>
        /// frames have a star count below <see cref="ObjectiveConstants.NHard"/>. Otherwise returns the
        /// weighted sum of the sub-scores, clamped to [0, 1]. The weights are renormalized to sum to 1: with
        /// labels they are (Wf, Ws, Wc, Wl); without, (Wf, Ws, Wc).
        /// </summary>
        public static double JRun(RunEvaluationMetrics m, ObjectiveConstants c, double? recall = null, double? precision = null) {
            // Prefer the per-run recall/precision supplied on the metrics (T3 fills these in when labels
            // exist); fall back to any explicitly passed values.
            var effRecall = recall ?? m.Recall;
            var effPrecision = precision ?? m.Precision;

            // Hard constraint: too many starved frames. Far-from-focus RECOVERY frames (m.FrameIsRecovery[i] == true)
            // are EXEMPT — they routinely detect < NHard stars by design, so counting them would force J = 0 for every
            // candidate and make the wizard unusable. When FrameIsRecovery is null (the baseline — every legacy caller /
            // Replay / production autofocus) the recovery guard is never taken, so `below` reduces EXACTLY to
            // FrameStarCounts.Count(n => n < c.NHard) and J stays byte-identical.
            if (m.FrameStarCounts != null) {
                var counts = m.FrameStarCounts;
                var isRecovery = m.FrameIsRecovery;
                var below = 0;
                for (var i = 0; i < counts.Count; i++) {
                    if (counts[i] >= c.NHard) {
                        continue;
                    }
                    if (IsRecoveryFrame(isRecovery, i)) {
                        continue; // exempt tagged recovery frame from the hard floor
                    }
                    below++;
                }
                if (below > c.MaxFramesBelowHardFloor) {
                    return 0.0;
                }
            }

            var sFocus = SFocus(m.SigmaFocus, m.LooStdError, m.StepSize, c);
            if (double.IsNaN(sFocus)) {
                return 0.0; // unusable focus σ => hard fail
            }
            // S_stars is computed over the NON-recovery frames only: nMin/nMedian must not be dragged down by the
            // heavily-defocused recovery wings. NonRecoveryStarCounts returns the ORIGINAL list reference when
            // FrameIsRecovery is null, so this is byte-identical at the baseline.
            var sStars = SStars(NonRecoveryStarCounts(m), c);
            var sFit = SFit(m.RSquared, m.ReducedChiSquared, c);

            // Region-coverage reward: an ADDITIVE sub-score folded into the renormalized weighted sum. Active only
            // when Wcov > 0 AND occupancy data exists; otherwise it is excluded from BOTH the numerator and the
            // denominator, so J is bit-identical to the pre-coverage objective (even with the default Wcov, because
            // legacy callers carry no FrameRegionOccupancy). Composes identically in the labeled/unlabeled cases.
            var sCoverage = SCoverage(m, c);
            var coverageOn = c.Wcov > 0.0 && IsFinite(sCoverage);

            double num, den;
            if (effRecall.HasValue && effPrecision.HasValue) {
                var sLabel = LabelScore(effRecall.Value, effPrecision.Value);
                num = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit + c.Wl * sLabel;
                den = c.Wf + c.Ws + c.Wc + c.Wl;
            } else {
                num = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit;
                den = c.Wf + c.Ws + c.Wc;
            }
            if (coverageOn) {
                num += c.Wcov * sCoverage;
                den += c.Wcov;
            }
            var j = num / den;

            // F3: MULTIPLICATIVE label-free precision penalty. NOT an additive Wd weight (that would change the
            // baseline). SDefocusPrecision returns exactly 1.0 when there is no relaxation data or zero relaxation
            // across the run (the gate-OFF baseline), so this leaves j — and therefore J — bit-identical. It only
            // drops below 1 for the near-focus junk signature; legitimate defocused-extreme donut recovery is not
            // penalized. Composes identically in the labeled and unlabeled cases (applied after the weighted sum).
            j *= SDefocusPrecision(m, c);

            // Aberration-inspection fit guard: MULTIPLICATIVE penalty that bounds how far the focus σ may degrade
            // relative to the reference σ. Returns exactly 1.0 for the STANDARD objective (ReferenceSigmaFocus NaN)
            // and while σ stays within the margin, so J is bit-identical unless the inspection mode set a finite
            // reference AND the fit has degraded past it. Applied after the weighted sum, like SDefocusPrecision.
            j *= SFitGuard(m, c);

            // Extreme-HFR outlier penalty: MULTIPLICATIVE, like SDefocusPrecision. Returns exactly 1.0 when
            // FrameStarHFRs is null/empty or no near-focus extreme outlier exists, so J is bit-identical at the
            // baseline. The dilution-sensitive outlier FRACTION discourages keeping a saturated bright blob as one
            // of only a few accepted near-focus stars, pulling the optimizer toward a lower sensitivity / higher
            // recall. Applied after the weighted sum, like the other multiplicative penalties.
            j *= SHfrOutlier(m, c);

            // Plateau tie-breaker: blend in the unsaturating secondary score so that when the primary objective is
            // flat (J saturated at 1.0 over a region) the search still prefers more stars / lower σ. Applied ONLY on
            // this feasible path (the hard-fail returns above keep returning 0.0, so it never rescues an infeasible
            // run). Wtie = 0 ⇒ exactly j (bit-identical to the pre-tie-breaker objective). Convex ⇒ J stays in [0,1].
            if (c.Wtie > 0.0) {
                j = (1.0 - c.Wtie) * j + c.Wtie * TieBreakerScore(m, c);
            }
            return Clamp01(j);
        }

        /// <summary>
        /// Unsaturating secondary "quality" score in [0, 1), used by <see cref="JRun"/> to break ties on the
        /// saturated objective plateau. Rewards MORE stars and LOWER focus σ with strictly-monotone, never-clamping
        /// Michaelis–Menten terms (<c>n/(n+k)</c>, <c>RhoRef/(RhoRef+ρ)</c>) so it retains a gradient exactly where
        /// the primary sub-scores (which CLAMP at the NFloor/NTarget/RhoRef knees) have none. The knees are reused as
        /// half-saturation points so no new tuning constants are introduced; star count dominates (0.6) because it is
        /// the robust, large-signal indicator of sensor-model quality, with σ a gentle secondary (0.4). Returns 0 for
        /// an empty run.
        ///
        /// <para>The star term uses the per-run MEAN (total richness across the whole sweep), NOT a minimum-dominated
        /// blend: on the plateau every frame is already well past the S_stars knees, so minimum-frame protection is
        /// redundant (the hard floor + primary S_stars own it), and a min-dominated tie-breaker is GAMEABLE — the
        /// search can lift only the worst frame while shedding stars on the rich frames (observed on the mufti run:
        /// it raised the min 82→91 while dropping total 1247→904). Mean rewards keeping stars everywhere, which is
        /// what a tilt/sensor model needs.</para>
        /// </summary>
        public static double TieBreakerScore(RunEvaluationMetrics m, ObjectiveConstants c) {
            if (m?.FrameStarCounts == null || m.FrameStarCounts.Count == 0) {
                return 0.0;
            }
            // The tie-breaker is active by default (Wtie > 0) and is NOT near-focus-windowed, so the far-out recovery
            // wings WOULD bias this plateau-search mean. Take it over the NON-recovery frames only. NonRecoveryStarCounts
            // returns the ORIGINAL list reference when FrameIsRecovery is null ⇒ this mean is byte-identical at the baseline.
            var nMean = NonRecoveryStarCounts(m).Average();
            // Unsaturating total-star richness: mean/(mean+k), half-saturated at the S_stars target knee.
            var tStars = nMean / (nMean + c.NTarget);

            // Unsaturating focus sharpness: lower ρ = σ/step ⇒ higher. Falls back to the LOO σ like SFocus; if no
            // usable σ/step, contribute the neutral 0.5 so the term is well-defined (the star term still dominates).
            var sigma = IsFinite(m.SigmaFocus) ? m.SigmaFocus : (IsFinite(m.LooStdError) ? m.LooStdError : double.NaN);
            double tFocus;
            if (IsFinite(sigma) && m.StepSize > 0.0 && c.RhoRef > 0.0) {
                var rho = sigma / m.StepSize;
                tFocus = c.RhoRef / (c.RhoRef + rho);
            } else {
                tFocus = 0.5;
            }

            return 0.6 * tStars + 0.4 * tFocus;
        }

        /// <summary>
        /// Label-free precision / false-positive penalty in (0, 1], MULTIPLIED into J by <see cref="JRun"/>.
        /// Returns <b>exactly 1.0</b> (no penalty) when there is no relaxation data (null counts) OR zero
        /// relaxation-admitted stars across the run — this is the gate-OFF baseline, so J is unchanged and
        /// bit-identical.
        ///
        /// <para>Preferred signal — NEAR-FOCUS relaxation: when per-frame focuser positions and a finite fitted
        /// best-focus position are available, only relaxation-admitted stars on frames within
        /// <see cref="ObjectiveConstants.NearFocusWindowSteps"/> · stepSize of the minimum are counted. By the
        /// defocus gate's size-scaled design, a near-focus REAL star is small and never needs relaxation, so a
        /// near-focus relaxation-admitted star is necessarily a large/low-fill junk blob. The penalty is a function
        /// of the near-focus relaxed FRACTION (relaxed / accepted, over the near-focus frames). Legitimate donut
        /// recovery happens on the DEFOCUSED EXTREMES (far from the minimum) and is intentionally NOT penalized.</para>
        ///
        /// <para>Fallback — run-level relaxed fraction: when the near-focus window cannot be formed (no per-frame
        /// positions, or a non-finite fitted minimum), the penalty falls back to the run-level relaxed fraction
        /// (total relaxed / total accepted), guarded by <see cref="ObjectiveConstants.MinFramesForPenalty"/> and
        /// <see cref="ObjectiveConstants.MinAcceptedForPenalty"/> so a thin run can't be spuriously penalized.</para>
        ///
        /// <para>Recovery frames (<see cref="RunEvaluationMetrics.FrameIsRecovery"/>[i] == true) are EXEMPT on BOTH
        /// paths — numerator, denominator, the zero-relaxation short-circuit, and the fallback frame-count gate all
        /// skip them. A recovery frame is heavily defocused by design, so its by-design donut relaxation must never
        /// drive a NEAR-FOCUS precision penalty even when a short/skewed sweep places a recovery position inside the
        /// near-focus window. When <see cref="RunEvaluationMetrics.FrameIsRecovery"/> is null (the baseline) every skip
        /// is inert, so J is byte-identical.</para>
        ///
        /// <para>Penalty shape (both signals): <c>1 − Strength · max(0, relaxedFrac − Threshold)</c>, clamped to
        /// [<see cref="ObjectiveConstants.DefocusPrecisionMinFactor"/>, 1]. At or below the threshold the factor is
        /// exactly 1.0.</para>
        /// </summary>
        public static double SDefocusPrecision(RunEvaluationMetrics m, ObjectiveConstants c) {
            if (m == null) {
                return 1.0;
            }
            var relaxed = m.FrameRelaxationAdmittedCounts;
            // No relaxation data at all ⇒ no penalty (and the baseline gate-OFF path passes all-zero lists below).
            if (relaxed == null || relaxed.Count == 0) {
                return 1.0;
            }

            // Cheap short-circuit: zero relaxation anywhere ⇒ exactly 1.0 (the gate-OFF baseline). This is the
            // bit-identity guarantee: when every per-frame relaxed count is 0, J is returned unchanged. RECOVERY frames
            // are skipped here so their by-design relaxation never counts toward the penalty (relaxation confined to
            // recovery frames ⇒ no penalty). nonRecoveryRelaxedFrames counts the NON-recovery frames over the SAME list
            // the fallback frame-count gate references, so null FrameIsRecovery ⇒ it equals relaxed.Count (byte-identical).
            var isRecovery = m.FrameIsRecovery;
            long totalRelaxed = 0;
            var nonRecoveryRelaxedFrames = 0;
            for (var i = 0; i < relaxed.Count; i++) {
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue; // recovery relaxation is by-design donut recovery, never a precision defect
                }
                totalRelaxed += relaxed[i];
                nonRecoveryRelaxedFrames++;
            }
            if (totalRelaxed == 0) {
                return 1.0;
            }

            var counts = m.FrameStarCounts;
            var positions = m.FrameFocuserPositions;

            // ── Preferred: near-focus signal ──────────────────────────────────────────────────────────────
            var canUseNearFocus =
                positions != null && counts != null &&
                positions.Count == relaxed.Count && counts.Count == relaxed.Count &&
                IsFinite(m.BestFocusPosition) && m.StepSize > 0.0 && c.NearFocusWindowSteps > 0.0;

            if (canUseNearFocus) {
                var window = c.NearFocusWindowSteps * m.StepSize;
                long nearRelaxed = 0;
                long nearAccepted = 0;
                for (var i = 0; i < relaxed.Count; i++) {
                    // A recovery frame inside the window (the poor-start scenario) is exempt: skipping it drops BOTH its
                    // relaxed and accepted counts together, so nearFrac stays a pure NON-recovery signal.
                    if (IsRecoveryFrame(isRecovery, i)) {
                        continue;
                    }
                    if (Math.Abs(positions[i] - m.BestFocusPosition) <= window) {
                        nearRelaxed += relaxed[i];
                        nearAccepted += counts[i];
                    }
                }
                // No accepted stars in the window (or none relaxed near focus = the legitimate-donut case where all
                // relaxation is on the defocused extremes) ⇒ no penalty.
                if (nearAccepted <= 0 || nearRelaxed == 0) {
                    return 1.0;
                }
                var nearFrac = (double)nearRelaxed / nearAccepted;
                return PenaltyFromFraction(nearFrac, c);
            }

            // ── Fallback: run-level relaxed fraction (documented choice when no positions/fit minimum) ───────
            // Denominator skips recovery frames to stay consistent with the recovery-exempt totalRelaxed numerator (so
            // the fraction can never exceed 1 or spuriously penalize a recovery-heavy run). The frame-count gate uses
            // nonRecoveryRelaxedFrames (not counts.Count, which can differ from relaxed.Count on the fallback path).
            long totalAccepted = 0;
            if (counts != null) {
                for (var i = 0; i < counts.Count; i++) {
                    if (IsRecoveryFrame(isRecovery, i)) {
                        continue;
                    }
                    totalAccepted += counts[i];
                }
            }
            if (nonRecoveryRelaxedFrames < c.MinFramesForPenalty || totalAccepted < c.MinAcceptedForPenalty || totalAccepted <= 0) {
                return 1.0; // too thin to trust ⇒ no penalty
            }
            var frac = (double)totalRelaxed / totalAccepted;
            return PenaltyFromFraction(frac, c);
        }

        /// <summary>The shared penalty shape, using the DEFOCUS-PRECISION constants: <c>1 − Strength · max(0, frac −
        /// Threshold)</c>, clamped to [MinFactor, 1]. Returns exactly 1.0 at or below the threshold.</summary>
        private static double PenaltyFromFraction(double frac, ObjectiveConstants c) {
            return PenaltyFromFraction(frac, c.DefocusPrecisionThreshold, c.DefocusPrecisionStrength, c.DefocusPrecisionMinFactor);
        }

        /// <summary>The shared penalty shape with EXPLICIT knobs (so SHfrOutlier can reuse it with its own
        /// threshold/strength/floor): <c>1 − strength · max(0, frac − threshold)</c>, clamped to [minFactor, 1].
        /// Returns exactly 1.0 at or below the threshold.</summary>
        private static double PenaltyFromFraction(double frac, double threshold, double strength, double minFactor) {
            var excess = frac - threshold;
            if (excess <= 0.0) {
                return 1.0;
            }
            var penalty = 1.0 - strength * excess;
            if (penalty < minFactor) {
                penalty = minFactor;
            }
            if (penalty > 1.0) {
                penalty = 1.0;
            }
            return penalty;
        }

        /// <summary>
        /// Extreme-HFR outlier penalty in (0, 1], MULTIPLIED into J by <see cref="JRun"/>. Returns <b>exactly 1.0</b>
        /// (no penalty) when there is no per-star HFR data (null/empty <see cref="RunEvaluationMetrics.FrameStarHFRs"/>)
        /// OR no near-focus extreme outlier exists — the baseline, so J is bit-identical.
        ///
        /// <para>Signal — NEAR-FOCUS pooled outlier FRACTION: the accepted-star HFRs of the near-focus frames (within
        /// <see cref="ObjectiveConstants.NearFocusWindowSteps"/> · stepSize of the fitted minimum, exactly as
        /// <see cref="SDefocusPrecision"/>) are pooled into one robust sample. A star is an extreme-HFR outlier when
        /// BOTH <c>HFR ≥ median + HfrOutlierMadK · MAD</c> AND <c>HFR ≥ HfrOutlierRelMargin · median</c> — the k·MAD
        /// term guards loose frames; the relative-margin term guards the MAD≈0 tight-frame case (else any star a hair
        /// above the median would flag). The penalty is a function of <c>outliers / pooled accepted</c>. Because the
        /// denominator GROWS as a lower sensitivity admits more normal stars, the fraction SHRINKS — so the penalty
        /// relaxes toward 1.0 as recall rises, which is what makes it move the optimum toward higher recall instead of
        /// penalizing every config equally (the bright blob is admitted at all sensitivities).</para>
        ///
        /// <para>Fallback — run-level pool: when no near-focus window can be formed (no per-frame positions, or a
        /// non-finite fitted minimum), all frames are pooled, guarded by <see cref="ObjectiveConstants.MinFramesForPenalty"/>
        /// and <see cref="ObjectiveConstants.MinAcceptedForPenalty"/> so a thin run is not spuriously penalized.</para>
        ///
        /// <para>Penalty shape: <c>1 − HfrOutlierStrength · max(0, frac − HfrOutlierThreshold)</c>, clamped to
        /// [<see cref="ObjectiveConstants.HfrOutlierMinFactor"/>, 1].</para>
        /// </summary>
        public static double SHfrOutlier(RunEvaluationMetrics m, ObjectiveConstants c) {
            if (m == null) {
                return 1.0;
            }
            var hfrs = m.FrameStarHFRs;
            if (hfrs == null || hfrs.Count == 0) {
                return 1.0; // no per-star HFR data ⇒ bit-identical baseline
            }
            var positions = m.FrameFocuserPositions;

            var canUseNearFocus =
                positions != null && positions.Count == hfrs.Count &&
                IsFinite(m.BestFocusPosition) && m.StepSize > 0.0 && c.NearFocusWindowSteps > 0.0;
            var window = canUseNearFocus ? c.NearFocusWindowSteps * m.StepSize : 0.0;

            // Pool the eligible frames' accepted-star HFRs into one robust sample (maximizes n for the median/MAD).
            var pooled = new List<double>();
            var eligibleFrames = 0;
            var isRecovery = m.FrameIsRecovery;
            for (var i = 0; i < hfrs.Count; i++) {
                if (canUseNearFocus && Math.Abs(positions[i] - m.BestFocusPosition) > window) {
                    continue;
                }
                // Exempt tagged recovery frames from the pooled HFR sample on BOTH paths. On the near-focus (primary)
                // path this matters only when a short/skewed sweep places a recovery position inside the window; it also
                // protects the FALLBACK pool (taken when BestFocusPosition is NaN), which otherwise pools every frame.
                // Null FrameIsRecovery ⇒ guard never fires ⇒ byte-identical baseline.
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue;
                }
                var fr = hfrs[i];
                if (fr == null || fr.Count == 0) {
                    continue;
                }
                eligibleFrames++;
                for (var k = 0; k < fr.Count; k++) {
                    pooled.Add(fr[k]);
                }
            }

            // Fallback guard: with no near-focus window, require enough frames/stars before penalizing a thin run.
            if (!canUseNearFocus && (eligibleFrames < c.MinFramesForPenalty || pooled.Count < c.MinAcceptedForPenalty)) {
                return 1.0;
            }
            if (pooled.Count == 0) {
                return 1.0;
            }

            var (median, mad) = MedianAndMad(pooled);
            if (!IsFinite(median) || median <= 0.0) {
                return 1.0;
            }
            var kThresh = median + c.HfrOutlierMadK * (IsFinite(mad) ? mad : 0.0);
            var relThresh = c.HfrOutlierRelMargin * median;
            long outliers = 0;
            for (var i = 0; i < pooled.Count; i++) {
                if (pooled[i] >= kThresh && pooled[i] >= relThresh) {
                    outliers++;
                }
            }
            if (outliers == 0) {
                return 1.0; // no extreme outliers ⇒ bit-identical
            }
            var frac = (double)outliers / pooled.Count;
            return PenaltyFromFraction(frac, c.HfrOutlierThreshold, c.HfrOutlierStrength, c.HfrOutlierMinFactor);
        }

        /// <summary>Robust center/scale of a sample: the median, and the MAD scaled by 1.483 so it estimates σ for a
        /// Gaussian (matching the codebase's <c>MedianMAD</c> convention, keeping the objective dependency-free).</summary>
        private static (double median, double mad) MedianAndMad(List<double> values) {
            if (values == null || values.Count == 0) {
                return (double.NaN, double.NaN);
            }
            var a = values.ToArray();
            Array.Sort(a);
            var median = MedianSorted(a);
            for (var i = 0; i < a.Length; i++) {
                a[i] = Math.Abs(a[i] - median);
            }
            Array.Sort(a);
            return (median, 1.483 * MedianSorted(a));
        }

        private static double MedianSorted(double[] sorted) {
            var n = sorted.Length;
            if (n == 0) {
                return double.NaN;
            }
            if ((n & 1) == 1) {
                return sorted[n / 2];
            }
            return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        /// <summary>
        /// Region-coverage sub-score in [0, 1] (or NaN ⇒ no data ⇒ EXCLUDED from <see cref="JRun"/>): the near-focus
        /// mean of the per-frame region-occupancy fractions in <see cref="RunEvaluationMetrics.FrameRegionOccupancy"/>
        /// (the fraction of an N×M sensor tiling that holds ≥1 accepted star). Near-focus frames are selected with the
        /// same window plumbing as <see cref="SDefocusPrecision"/>; when no window can be formed it averages all finite
        /// frames. Frames whose occupancy is NaN (no usable geometry) are skipped. Higher ⇒ stars spread across the
        /// sensor; lower ⇒ stars clustered, which the additive Wcov term in <see cref="JRun"/> discourages.
        /// </summary>
        public static double SCoverage(RunEvaluationMetrics m, ObjectiveConstants c) {
            if (m == null) {
                return double.NaN;
            }
            var occ = m.FrameRegionOccupancy;
            if (occ == null || occ.Count == 0) {
                return double.NaN; // no occupancy data ⇒ excluded from JRun
            }
            var positions = m.FrameFocuserPositions;
            var canUseNearFocus =
                positions != null && positions.Count == occ.Count &&
                IsFinite(m.BestFocusPosition) && m.StepSize > 0.0 && c.NearFocusWindowSteps > 0.0;
            var window = canUseNearFocus ? c.NearFocusWindowSteps * m.StepSize : 0.0;

            var isRecovery = m.FrameIsRecovery;
            var sum = 0.0;
            var n = 0;
            for (var i = 0; i < occ.Count; i++) {
                if (canUseNearFocus && Math.Abs(positions[i] - m.BestFocusPosition) > window) {
                    continue;
                }
                // Exempt tagged recovery frames from the coverage mean on BOTH paths. On the near-focus (primary) path
                // this matters only when a short/skewed sweep places a recovery position inside the window; it also
                // protects the FALLBACK average (taken when BestFocusPosition is NaN), which otherwise averages every
                // finite frame. Null FrameIsRecovery ⇒ guard never fires ⇒ byte-identical baseline.
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue;
                }
                if (!IsFinite(occ[i])) {
                    continue;
                }
                sum += occ[i];
                n++;
            }
            return n == 0 ? double.NaN : sum / n;
        }

        /// <summary>
        /// Aberration-inspection fit guard in (0, 1], MULTIPLIED into J by <see cref="JRun"/>. Returns
        /// <b>exactly 1.0</b> (no penalty) when <see cref="ObjectiveConstants.ReferenceSigmaFocus"/> is non-finite
        /// (the STANDARD objective — so J is bit-identical) OR when the run's focus σ is within the allowed margin
        /// over the reference σ. Beyond the margin it decays linearly with the excess σ fraction, clamped to
        /// [<see cref="ObjectiveConstants.FitGuardMinFactor"/>, 1].
        ///
        /// <para>σ is selected exactly like <see cref="SFocus"/> (SigmaFocus, else the leave-one-out fallback); if
        /// neither is finite the guard is inert (1.0) — JRun already hard-fails an unusable σ before reaching here.
        /// Penalty shape: <c>1 − Strength · max(0, σ/refσ − (1 + Margin))</c>.</para>
        /// </summary>
        public static double SFitGuard(RunEvaluationMetrics m, ObjectiveConstants c) {
            if (m == null || !IsFinite(c.ReferenceSigmaFocus) || c.ReferenceSigmaFocus <= 0.0) {
                return 1.0; // standard objective (no anchor) ⇒ no guard, J bit-identical
            }
            var sigma = IsFinite(m.SigmaFocus) ? m.SigmaFocus : (IsFinite(m.LooStdError) ? m.LooStdError : double.NaN);
            if (!IsFinite(sigma) || sigma <= 0.0) {
                return 1.0; // no usable σ ⇒ nothing to bound (the hard-fail path owns unusable σ)
            }
            var ratio = sigma / c.ReferenceSigmaFocus;
            var excess = ratio - (1.0 + c.FitGuardMarginFraction);
            if (excess <= 0.0) {
                return 1.0; // within the allowed degradation margin
            }
            var penalty = 1.0 - c.FitGuardStrength * excess;
            var floor = c.FitGuardMinFactor;
            if (penalty < floor) {
                penalty = floor;
            }
            if (penalty > 1.0) {
                penalty = 1.0;
            }
            return penalty;
        }

        /// <summary>
        /// Aggregates per-run J into a single score: J_total = (1 − β)·mean + β·min. A single run reduces to
        /// that run's J (mean == min). β = 1 is pure worst-case (min); β = 0 is the average.
        /// </summary>
        public static double JTotal(IReadOnlyList<double> perRunJ, ObjectiveConstants c) {
            if (perRunJ == null || perRunJ.Count == 0) {
                return 0.0;
            }
            var mean = perRunJ.Average();
            var min = perRunJ.Min();
            return (1.0 - c.Beta) * mean + c.Beta * min;
        }

        /// <summary>
        /// Box-containment label scores against the accepted-star centers:
        ///   recall    = fraction of <paramref name="labeledMissed"/> boxes that CONTAIN at least one accepted
        ///               center — i.e. correctly recovered. Empty => 1.0.
        ///   precision = fraction of <paramref name="labeledShouldReject"/> boxes that contain NO accepted center
        ///               (the flagged accepted star is now excluded) — i.e. correctly rejected. Empty => 1.0.
        /// Containment is point-in-box: cx ∈ [X, X+W] && cy ∈ [Y, Y+H] (see <see cref="LabelBox.Contains"/>). The
        /// box defines the region directly, so no radius is needed.
        /// </summary>
        public static (double recall, double precision) ComputeLabelScores(
            IReadOnlyList<(double X, double Y)> acceptedCenters,
            IReadOnlyList<LabelBox> labeledMissed,
            IReadOnlyList<LabelBox> labeledShouldReject) {
            double recall;
            if (labeledMissed == null || labeledMissed.Count == 0) {
                recall = 1.0; // nothing to recover
            } else {
                var recovered = labeledMissed.Count(box => ContainsAnyCenter(acceptedCenters, box));
                recall = (double)recovered / labeledMissed.Count;
            }

            double precision;
            if (labeledShouldReject == null || labeledShouldReject.Count == 0) {
                precision = 1.0; // nothing that should be rejected
            } else {
                var correctlyExcluded = labeledShouldReject.Count(box => !ContainsAnyCenter(acceptedCenters, box));
                precision = (double)correctlyExcluded / labeledShouldReject.Count;
            }

            return (recall, precision);
        }

        private static bool ContainsAnyCenter(IReadOnlyList<(double X, double Y)> centers, LabelBox box) {
            if (centers == null) {
                return false;
            }
            for (var i = 0; i < centers.Count; i++) {
                if (box.Contains(centers[i].X, centers[i].Y)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The per-frame accepted-star counts with far-from-focus RECOVERY frames removed (those tagged in
        /// <see cref="RunEvaluationMetrics.FrameIsRecovery"/>). Recovery frames are the outer, heavily-defocused sweep
        /// positions that detect few/no stars; they are down-weighted in the fit and must not bias the star-count
        /// sub-scores or gates. When <see cref="RunEvaluationMetrics.FrameIsRecovery"/> is null (the baseline — every
        /// legacy caller / Replay / production autofocus) this returns the ORIGINAL list reference unchanged, so all
        /// downstream star-count math (Min/Median/Average) is byte-identical. If filtering would remove EVERY frame it
        /// also falls back to the original list, so a caller using Min/Average never sees an empty sequence (in practice
        /// this can't happen — the recovery tagging always leaves ≥ 3 non-recovery frames).
        /// </summary>
        private static IReadOnlyList<int> NonRecoveryStarCounts(RunEvaluationMetrics m) {
            var counts = m.FrameStarCounts;
            var isRecovery = m.FrameIsRecovery;
            if (counts == null || isRecovery == null) {
                return counts; // baseline: same reference => byte-identical downstream
            }
            var filtered = new List<int>(counts.Count);
            for (var i = 0; i < counts.Count; i++) {
                if (IsRecoveryFrame(isRecovery, i)) {
                    continue; // exempt far-from-focus recovery frame
                }
                filtered.Add(counts[i]);
            }
            return filtered.Count == 0 ? counts : filtered; // never hand back an empty sequence
        }

        /// <summary>
        /// True iff frame <paramref name="i"/> is tagged a far-from-focus RECOVERY frame. Bounds-checked so a caller can
        /// pass a <paramref name="flags"/> list that is null (the baseline ⇒ always false ⇒ byte-identical) or, defensively,
        /// shorter than the frame axis. The single guard used at every recovery-exemption site (hard floor, SDefocusPrecision,
        /// SHfrOutlier, SCoverage) so the null/bounds semantics can't drift between them.
        /// </summary>
        private static bool IsRecoveryFrame(IReadOnlyList<bool> flags, int i) => flags != null && i < flags.Count && flags[i];

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static double Median(IReadOnlyList<int> counts) {
            var sorted = counts.OrderBy(x => x).ToArray();
            var n = sorted.Length;
            if (n == 0) {
                return 0.0;
            }
            if ((n & 1) == 1) {
                return sorted[n / 2];
            }
            return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
