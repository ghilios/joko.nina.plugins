#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>Whether loosening a gate means LOWERING its threshold (admit iff measured &gt; threshold — e.g.
    /// Sensitivity, MinHFR, MaxDistortion) or RAISING it (admit iff measured &lt; threshold — e.g. PeakResponse,
    /// StarCenterTolerance).</summary>
    public enum GateDirection { LowerToAdmit, RaiseToAdmit }

    public sealed class RecommenderConfig {
        /// <summary>Cost-effectiveness ceiling: extend a gate's recovery only while the (near-focus-weighted)
        /// count of newly-admitted UNLABELED candidates stays ≤ this many per recovered labeled star. Bounds the
        /// precision cost in the absence of explicit should-reject labels.</summary>
        public double MaxUnlabeledPerRecovered { get; set; } = 2.0;

        public double NearFocusWindowSteps { get; set; } = 1.5;
        public double NearFocusWeight { get; set; } = 3.0;
        public double? BestFocusPosition { get; set; } = null;
        public double StepSize { get; set; } = 0.0;

        /// <summary>Candidates whose size exceeds this (px) are "defocused"; for the distortion/centering gates the
        /// recommender then prefers the defocus-aware relaxation over a global threshold change. Matches the
        /// detector default <see cref="StarDetectorParams.DefocusDistortionSizeReference"/>.</summary>
        public double DefocusSizeReference { get; set; } = 30.0;

        /// <summary>Recommend structure-gap recovery (Phase 5) when NO-CANDIDATE boxes are at least this fraction
        /// of all recall targets.</summary>
        public double NoCandidateRecommendFraction { get; set; } = 0.15;
    }

    public sealed class RecommendationRow {
        public string Gate { get; set; }
        public string ParamDisplayName { get; set; }
        public double? OldValue { get; set; }
        public double? NewValue { get; set; }
        public int Recovered { get; set; }
        public int Total { get; set; }
        public double WeightedPrecisionCost { get; set; }
        public bool Applied { get; set; }
        public string Note { get; set; }

        public override string ToString() {
            var inv = CultureInfo.InvariantCulture;
            if (OldValue.HasValue && NewValue.HasValue) {
                return $"{ParamDisplayName} {OldValue.Value.ToString("G4", inv)} → {NewValue.Value.ToString("G4", inv)}  " +
                       $"recovers {Recovered}/{Total} {Gate}, admits ~{WeightedPrecisionCost.ToString("G3", inv)} unlabeled" +
                       (string.IsNullOrEmpty(Note) ? "" : $" ({Note})");
            }
            return $"{Gate}: {Recovered}/{Total} — {Note}";
        }
    }

    public sealed class GateRecommendation {
        public StarDetectorParams Recommended { get; set; }
        public List<RecommendationRow> Rows { get; set; } = new List<RecommendationRow>();
        public bool RecommendStructureRecovery { get; set; }
        public bool RecommendDefocusAwareGates { get; set; }
        public double EstimatedWeightedPrecisionCost { get; set; }
        public int TotalRecovered { get; set; }
        public int TotalRecallTargets { get; set; }
        public int NoCandidateCount { get; set; }
    }

    /// <summary>
    /// Turns a <see cref="LabelGateAnalysis"/> into concrete detector setting changes: per gate, it inverts the
    /// gate from the measured-value distribution of the labeled "wrongly-rejected" stars to find the threshold
    /// that recovers as many as possible, bounded by a precision proxy (the near-focus-weighted count of
    /// currently-rejected UNLABELED candidates the change would newly admit, plus a hard veto on re-admitting any
    /// should-reject candidate). Pure and deterministic — fully unit-testable without images.
    /// </summary>
    public static class GateRecommender {

        // gate → (display name, direction). Gates absent here are non-invertible (flag only).
        private static readonly Dictionary<string, (string Display, GateDirection Dir)> GateMap =
            new Dictionary<string, (string, GateDirection)>(StringComparer.Ordinal) {
                { RejectionGate.LowSensitivity, ("BrightnessSensitivity", GateDirection.LowerToAdmit) },
                { RejectionGate.TooLowHFR, ("MinHFR", GateDirection.LowerToAdmit) },
                { RejectionGate.TooDistorted, ("MaxDistortion", GateDirection.LowerToAdmit) },
                { RejectionGate.TooSmall, ("MinStarBoundingBoxSize", GateDirection.LowerToAdmit) },
                { RejectionGate.TooFlat, ("StarPeakResponse", GateDirection.RaiseToAdmit) },
                { RejectionGate.NotCentered, ("StarCenterTolerance", GateDirection.RaiseToAdmit) },
            };

        public static GateRecommendation Recommend(LabelGateAnalysis analysis, StarDetectorParams current, RecommenderConfig config = null) {
            config = config ?? new RecommenderConfig();
            var rec = new GateRecommendation {
                Recommended = current.Clone(),
                TotalRecallTargets = analysis.TotalRecallTargets,
                NoCandidateCount = analysis.NoCandidateCount
            };

            // Process gates in descending recall-target count (fix the biggest buckets first).
            var gatesByImpact = analysis.RecallTargetCountByGate
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var gate in gatesByImpact) {
                var labeled = analysis.RecallTargets(gate);
                if (labeled.Count == 0) {
                    continue;
                }

                if (!GateMap.TryGetValue(gate, out var map)) {
                    // Non-invertible gate (Degenerate / OnBorder / HFRAnalysisFailed / Contaminated handled below).
                    if (gate == RejectionGate.Contaminated) {
                        rec.Rows.Add(new RecommendationRow {
                            Gate = gate, Total = labeled.Count, Recovered = 0, Applied = false,
                            Note = "contaminated — consider turning RejectContaminatedStars off or raising ContaminationSensitivity"
                        });
                    } else {
                        rec.Rows.Add(new RecommendationRow {
                            Gate = gate, Total = labeled.Count, Recovered = 0, Applied = false,
                            Note = "not recoverable by a threshold (needs detector-algorithm work)"
                        });
                    }
                    continue;
                }

                // Defocus-aware lever: for distortion/centering, if the labeled targets are large (defocused),
                // recommend the defocus-aware relaxation instead of a global threshold change that would hurt
                // precision on small near-focus stars.
                bool defocusGate = gate == RejectionGate.TooDistorted || gate == RejectionGate.NotCentered;
                if (defocusGate) {
                    var medianSize = Median(labeled.Select(t => t.CandidateSize).ToList());
                    if (medianSize > config.DefocusSizeReference) {
                        rec.RecommendDefocusAwareGates = true;
                        rec.Recommended.DefocusAwareDistortion = true;
                        rec.Recommended.DefocusAwareCentering = true;
                        rec.Rows.Add(new RecommendationRow {
                            Gate = gate, Total = labeled.Count, Recovered = labeled.Count, Applied = true,
                            ParamDisplayName = "DefocusAwareGates", OldValue = 0, NewValue = 1,
                            Note = $"targets are large (median size {medianSize:G3}px > {config.DefocusSizeReference:G3}) — enable defocus-aware relaxation"
                        });
                        continue;
                    }
                }

                var unlabeled = analysis.Unlabeled(gate)
                    .Select(u => (m: u.MeasuredValue, w: Weight(u.FocuserPosition, config), sr: u.InShouldRejectBox))
                    .Where(u => !double.IsNaN(u.m))
                    .ToList();
                var labeledMeasured = labeled.Select(t => t.MeasuredValue).Where(v => !double.IsNaN(v)).ToList();
                if (labeledMeasured.Count == 0) {
                    continue; // measured values unavailable (non-scalar) — can't invert
                }

                var current_threshold = ReadThreshold(current, gate);
                var sol = Solve(labeledMeasured, unlabeled, map.Dir, config.MaxUnlabeledPerRecovered);
                if (sol.Recovered <= 0) {
                    rec.Rows.Add(new RecommendationRow {
                        Gate = gate, ParamDisplayName = map.Display, Total = labeled.Count, Recovered = 0, Applied = false,
                        OldValue = current_threshold, WeightedPrecisionCost = 0,
                        Note = "blocked by should-reject labels / precision budget"
                    });
                    continue;
                }

                var newThreshold = ClampThreshold(gate, sol.Threshold);
                ApplyThreshold(rec.Recommended, gate, newThreshold);
                rec.TotalRecovered += sol.Recovered;
                rec.EstimatedWeightedPrecisionCost += sol.Cost;
                rec.Rows.Add(new RecommendationRow {
                    Gate = gate, ParamDisplayName = map.Display, Total = labeled.Count, Recovered = sol.Recovered,
                    OldValue = current_threshold, NewValue = newThreshold, WeightedPrecisionCost = sol.Cost, Applied = true
                });
            }

            // NO-CANDIDATE structure gaps → recommend structure recovery (Phase 5) when material.
            if (analysis.TotalRecallTargets > 0 &&
                analysis.NoCandidateCount >= config.NoCandidateRecommendFraction * analysis.TotalRecallTargets &&
                analysis.NoCandidateCount > 0) {
                rec.RecommendStructureRecovery = true;
                rec.Rows.Add(new RecommendationRow {
                    Gate = "NoCandidate", Total = analysis.NoCandidateCount, Recovered = 0, Applied = false,
                    Note = "stars never formed a candidate (likely large defocused donuts) — enable Defocus-Aware Structure Detection"
                });
            }

            return rec;
        }

        private struct GateSolution {
            public double Threshold;
            public int Recovered;
            public double Cost;
        }

        /// <summary>
        /// Chooses the threshold that admits the largest prefix of labeled targets (ordered by ease of admission)
        /// whose cumulative weighted unlabeled-admit count stays ≤ R · recovered, while never re-admitting a
        /// should-reject candidate. Returns the most aggressive feasible, cost-effective threshold.
        /// </summary>
        private static GateSolution Solve(List<double> labeled, List<(double m, double w, bool sr)> unlabeled, GateDirection dir, double r) {
            // Order labeled by ease: LowerToAdmit admits the largest measured with the least lowering; RaiseToAdmit
            // admits the smallest measured with the least raising.
            var byEase = dir == GateDirection.LowerToAdmit
                ? labeled.OrderByDescending(x => x).ToList()
                : labeled.OrderBy(x => x).ToList();

            // Should-reject hard bound. LowerToAdmit: T must stay ≥ max(sr measured) to exclude them; RaiseToAdmit:
            // T must stay ≤ min(sr measured).
            double? srBound = null;
            var srMeasured = unlabeled.Where(u => u.sr).Select(u => u.m).ToList();
            if (srMeasured.Count > 0) {
                srBound = dir == GateDirection.LowerToAdmit ? srMeasured.Max() : srMeasured.Min();
            }

            var best = new GateSolution { Threshold = double.NaN, Recovered = 0, Cost = 0 };
            for (var j = 1; j <= byEase.Count; j++) {
                var v = byEase[j - 1];
                var t = dir == GateDirection.LowerToAdmit ? JustBelow(v) : JustAbove(v);

                // Should-reject veto.
                if (srBound.HasValue) {
                    if (dir == GateDirection.LowerToAdmit && t < srBound.Value) break;
                    if (dir == GateDirection.RaiseToAdmit && t > srBound.Value) break;
                }

                var cost = WeightedCost(unlabeled, dir, t);
                if (cost <= r * j) {
                    best = new GateSolution { Threshold = t, Recovered = j, Cost = cost };
                } else {
                    break; // admitting more labeled exceeds the cost-effectiveness ceiling
                }
            }
            return best;
        }

        private static double WeightedCost(List<(double m, double w, bool sr)> unlabeled, GateDirection dir, double t) {
            double cost = 0;
            foreach (var u in unlabeled) {
                if (u.sr) {
                    continue; // excluded by the should-reject bound; not a precision "cost" (it's a hard veto)
                }
                bool admitted = dir == GateDirection.LowerToAdmit ? u.m > t : u.m < t;
                if (admitted) {
                    cost += u.w;
                }
            }
            return cost;
        }

        private static double Weight(int focuserPosition, RecommenderConfig c) {
            if (c.StepSize <= 0 || !c.BestFocusPosition.HasValue) {
                return 1.0;
            }
            var window = c.NearFocusWindowSteps * c.StepSize;
            return Math.Abs(focuserPosition - c.BestFocusPosition.Value) <= window ? c.NearFocusWeight : 1.0;
        }

        private static double JustBelow(double v) => v - Math.Max(1e-9, Math.Abs(v) * 1e-6);
        private static double JustAbove(double v) => v + Math.Max(1e-9, Math.Abs(v) * 1e-6);

        private static double Median(List<double> values) {
            if (values.Count == 0) {
                return 0.0;
            }
            var sorted = values.OrderBy(x => x).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }

        private static double ReadThreshold(StarDetectorParams p, string gate) {
            switch (gate) {
                case RejectionGate.LowSensitivity: return p.Sensitivity;
                case RejectionGate.TooLowHFR: return p.MinHFR;
                case RejectionGate.TooDistorted: return p.MaxDistortion;
                case RejectionGate.TooSmall: return p.MinimumStarBoundingBoxSize;
                case RejectionGate.TooFlat: return p.PeakResponse;
                case RejectionGate.NotCentered: return p.StarCenterTolerance;
                default: return double.NaN;
            }
        }

        private static double ClampThreshold(string gate, double t) {
            switch (gate) {
                case RejectionGate.LowSensitivity: return Math.Max(0.0, t);
                case RejectionGate.TooLowHFR: return Math.Max(0.0, t);
                case RejectionGate.TooDistorted: return Math.Min(1.0, Math.Max(0.01, t));
                case RejectionGate.TooSmall: return Math.Max(2.0, Math.Floor(t));
                case RejectionGate.TooFlat: return Math.Min(1.0, Math.Max(0.0, t));
                case RejectionGate.NotCentered: return Math.Min(1.0, Math.Max(0.0, t));
                default: return t;
            }
        }

        private static void ApplyThreshold(StarDetectorParams p, string gate, double t) {
            switch (gate) {
                case RejectionGate.LowSensitivity: p.Sensitivity = t; break;
                case RejectionGate.TooLowHFR: p.MinHFR = t; break;
                case RejectionGate.TooDistorted: p.MaxDistortion = t; break;
                case RejectionGate.TooSmall: p.MinimumStarBoundingBoxSize = (int)Math.Round(t); break;
                case RejectionGate.TooFlat: p.PeakResponse = t; break;
                case RejectionGate.NotCentered: p.StarCenterTolerance = t; break;
            }
        }
    }
}
