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

            double j;
            if (effRecall.HasValue && effPrecision.HasValue) {
                var sLabel = LabelScore(effRecall.Value, effPrecision.Value);
                var wSum = c.Wf + c.Ws + c.Wc + c.Wl;
                j = (c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit + c.Wl * sLabel) / wSum;
            } else {
                var wSum = c.Wf + c.Ws + c.Wc;
                j = (c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit) / wSum;
            }
            return Clamp01(j);
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
