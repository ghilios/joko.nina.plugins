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

        // ── F5: extreme-frame donut-recall term (SExtreme) ─────────────────────────────────────────────────
        // The optimizer otherwise has NO incentive to keep faint donuts at the sweep EXTREMES: S_stars saturates
        // its nMin knee at NFloor (8), so 10 vs 30 stars on the most-defocused frame score identically, and a
        // cleaner focus curve / higher label precision actively reward REJECTING those donuts. This additive term
        // rewards star recall on the min/max focuser-position frames so the search keeps (and the defocus-aware
        // gates earn their place recovering) extreme-defocus donuts. OFF by default so the raw objective stays
        // bit-identical for existing tests; the production StarDetectionOptimizer constructs constants with it ON.
        public bool EnableExtremeRecall { get; set; } = false;

        // Additive weight of SExtreme in the JRun weighted sum (folded into wSum alongside Wf/Ws/Wc[/Wl]). Only
        // included when EnableExtremeRecall is on AND the run has >= 2 distinct focuser positions (so extremes
        // exist). 0.30 makes it strong enough to push back on the focus/precision terms' incentive to cull faint
        // extreme donuts (Wf = 0.55 dominates otherwise) without overriding a genuinely bad curve.
        public double We { get; set; } = 0.30;

        // Star count on an extreme frame at/above which SExtreme saturates to 1.0. SExtreme is the clamped mean,
        // over the min- and max-focuser-position frames, of (extremeFrameStarCount / ExtremeTarget). Set near the
        // achievable extreme-frame recall on a rich field so the term keeps a gradient across the operating range
        // (a too-low target saturates and stops rewarding additional recovery); capping the reward at the target
        // means there is no incentive to inflate extreme counts with junk past it. 50 tuned on the mufti AF run
        // (achievable extreme recall ≈ 66 with the defocus-aware gates on); revisit across the AF bank.
        public int ExtremeTarget { get; set; } = 50;
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
        /// S_extreme ∈ [0, 1]: the clamped mean, over the two EXTREME frames (lowest and highest focuser
        /// position — the most defocused), of <c>extremeStarCount / ExtremeTarget</c>. Rewards donut recall at the
        /// sweep boundaries, which S_stars (saturating at NFloor) and the focus/label terms do not. Returns
        /// <c>null</c> when the term is not applicable — gate off, no per-frame data, mismatched lengths, or fewer
        /// than 2 distinct focuser positions (no extremes) — so <see cref="JRun"/> omits it and keeps the existing
        /// weighting/renormalization unchanged.
        /// </summary>
        public static double? SExtreme(RunEvaluationMetrics m, ObjectiveConstants c) {
            if (m == null || !c.EnableExtremeRecall || c.ExtremeTarget <= 0) {
                return null;
            }
            var positions = m.FrameFocuserPositions;
            var counts = m.FrameStarCounts;
            if (positions == null || counts == null || positions.Count != counts.Count || positions.Count == 0) {
                return null;
            }

            int minPos = positions[0], maxPos = positions[0], minIdx = 0, maxIdx = 0;
            for (var i = 1; i < positions.Count; i++) {
                if (positions[i] < minPos) { minPos = positions[i]; minIdx = i; }
                if (positions[i] > maxPos) { maxPos = positions[i]; maxIdx = i; }
            }
            if (minPos == maxPos) {
                return null; // fewer than 2 distinct positions ⇒ no extremes
            }

            var meanExtreme = 0.5 * (counts[minIdx] + counts[maxIdx]);
            return Clamp01(meanExtreme / c.ExtremeTarget);
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

            // Hard constraint: too many starved frames.
            if (m.FrameStarCounts != null) {
                var below = m.FrameStarCounts.Count(n => n < c.NHard);
                if (below > c.MaxFramesBelowHardFloor) {
                    return 0.0;
                }
            }

            var sFocus = SFocus(m.SigmaFocus, m.LooStdError, m.StepSize, c);
            if (double.IsNaN(sFocus)) {
                return 0.0; // unusable focus σ => hard fail
            }
            var sStars = SStars(m.FrameStarCounts, c);
            var sFit = SFit(m.RSquared, m.ReducedChiSquared, c);

            // F5: extreme-frame donut-recall term. Additive (a first-class objective term, like S_label), folded
            // into the weighted sum and renormalized into wSum. Included ONLY when applicable (gate on, >= 2
            // distinct focuser positions); otherwise sExtreme is null and the term — and its weight — are omitted,
            // so the existing (Wf, Ws, Wc[, Wl]) weighting is exactly preserved.
            var sExtreme = SExtreme(m, c);

            double weighted = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit;
            double wSum = c.Wf + c.Ws + c.Wc;
            if (effRecall.HasValue && effPrecision.HasValue) {
                weighted += c.Wl * LabelScore(effRecall.Value, effPrecision.Value);
                wSum += c.Wl;
            }
            if (sExtreme.HasValue) {
                weighted += c.We * sExtreme.Value;
                wSum += c.We;
            }
            double j = weighted / wSum;

            // F3: MULTIPLICATIVE label-free precision penalty. NOT an additive Wd weight (that would change the
            // baseline). SDefocusPrecision returns exactly 1.0 when there is no relaxation data or zero relaxation
            // across the run (the gate-OFF baseline), so this leaves j — and therefore J — bit-identical. It only
            // drops below 1 for the near-focus junk signature; legitimate defocused-extreme donut recovery is not
            // penalized. Composes identically in the labeled and unlabeled cases (applied after the weighted sum).
            j *= SDefocusPrecision(m, c);
            return Clamp01(j);
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
            // bit-identity guarantee: when every per-frame relaxed count is 0, J is returned unchanged.
            long totalRelaxed = 0;
            for (var i = 0; i < relaxed.Count; i++) {
                totalRelaxed += relaxed[i];
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
            long totalAccepted = 0;
            if (counts != null) {
                for (var i = 0; i < counts.Count; i++) {
                    totalAccepted += counts[i];
                }
            }
            if (relaxed.Count < c.MinFramesForPenalty || totalAccepted < c.MinAcceptedForPenalty || totalAccepted <= 0) {
                return 1.0; // too thin to trust ⇒ no penalty
            }
            var frac = (double)totalRelaxed / totalAccepted;
            return PenaltyFromFraction(frac, c);
        }

        /// <summary>The shared penalty shape: <c>1 − Strength · max(0, frac − Threshold)</c>, clamped to
        /// [MinFactor, 1]. Returns exactly 1.0 at or below the threshold.</summary>
        private static double PenaltyFromFraction(double frac, ObjectiveConstants c) {
            var excess = frac - c.DefocusPrecisionThreshold;
            if (excess <= 0.0) {
                return 1.0;
            }
            var penalty = 1.0 - c.DefocusPrecisionStrength * excess;
            var floor = c.DefocusPrecisionMinFactor;
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
