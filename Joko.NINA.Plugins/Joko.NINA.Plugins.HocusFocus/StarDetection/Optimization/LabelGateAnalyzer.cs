#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System.Collections.Generic;
using System.Linq;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>One frame's detection + the user's labels for that focuser position, the input to the analyzer.
    /// The caller (wizard or TestApp) detects each frame with
    /// <see cref="StarDetectorParams.CollectRejectedCandidateDiagnostics"/> on, then fills this in.</summary>
    public sealed class FrameDetectionForAnalysis {
        public int FocuserPosition { get; set; }
        public IReadOnlyList<Rect> AcceptedBounds { get; set; } = new List<Rect>();
        public IReadOnlyList<(double X, double Y)> AcceptedCenters { get; set; } = new List<(double, double)>();
        public IReadOnlyList<RejectedCandidateRecord> Rejected { get; set; } = new List<RejectedCandidateRecord>();

        /// <summary>Recall targets = union of "missed" and "wrongly-rejected" boxes (both are stars the user wants kept).</summary>
        public IReadOnlyList<RectD> RecallBoxes { get; set; } = new List<RectD>();

        /// <summary>Precision targets = "should-reject" boxes (accepted stars inside them are false positives).</summary>
        public IReadOnlyList<RectD> ShouldRejectBoxes { get; set; } = new List<RectD>();
    }

    /// <summary>A single rejected candidate, enriched with its labeling/precision context. The recommender groups
    /// these by <see cref="Gate"/> and inverts the gate from the <see cref="MeasuredValue"/> distribution.</summary>
    public sealed class AnalyzedRejection {
        public string Gate { get; set; }
        public double MeasuredValue { get; set; }
        public double ThresholdValue { get; set; }
        public double CandidateSize { get; set; }
        public int FocuserPosition { get; set; }
        public RectD Bounds { get; set; }

        /// <summary>True when this rejected candidate overlaps a recall (missed/wrongly-rejected) box — i.e. the
        /// user wants it recovered. False candidates are the "unlabeled" pool used to bound the precision cost.</summary>
        public bool IsRecallTarget { get; set; }

        /// <summary>True when this candidate's center lies inside a should-reject box — admitting it (by loosening
        /// the gate) is an explicit precision violation the recommender must never do.</summary>
        public bool InShouldRejectBox { get; set; }
    }

    public sealed class NoCandidateTarget {
        public int FocuserPosition { get; set; }
        public RectD Box { get; set; }
    }

    public sealed class ShouldRejectTarget {
        public int FocuserPosition { get; set; }
        public RectD Box { get; set; }

        /// <summary>True when an accepted star currently falls inside this should-reject box (a present false positive).</summary>
        public bool CurrentlyViolated { get; set; }
    }

    /// <summary>
    /// Result of analyzing a run's labels against a fresh detection: every rejected candidate enriched with its
    /// recall/precision context, the NO-CANDIDATE structure gaps, and the should-reject precision targets. Pure
    /// data — the <see cref="GateRecommender"/> consumes it to derive setting changes.
    /// </summary>
    public sealed class LabelGateAnalysis {
        public IReadOnlyList<AnalyzedRejection> Rejections { get; set; } = new List<AnalyzedRejection>();
        public IReadOnlyList<NoCandidateTarget> NoCandidate { get; set; } = new List<NoCandidateTarget>();
        public IReadOnlyList<ShouldRejectTarget> ShouldReject { get; set; } = new List<ShouldRejectTarget>();

        public int TotalRecallTargets { get; set; }
        public int AlreadyRecovered { get; set; }
        public int NoCandidateCount { get; set; }

        /// <summary>Distinct labeled focuser positions, ascending.</summary>
        public IReadOnlyList<int> FocuserPositions { get; set; } = new List<int>();

        /// <summary>Recall targets attributed to each gate (only gates that killed ≥1 recall target).</summary>
        public IReadOnlyDictionary<string, int> RecallTargetCountByGate =>
            Rejections.Where(r => r.IsRecallTarget)
                      .GroupBy(r => r.Gate)
                      .ToDictionary(g => g.Key, g => g.Count());

        /// <summary>The labeled recall targets killed by <paramref name="gate"/>.</summary>
        public IReadOnlyList<AnalyzedRejection> RecallTargets(string gate) =>
            Rejections.Where(r => r.IsRecallTarget && r.Gate == gate).ToList();

        /// <summary>The unlabeled rejected candidates of <paramref name="gate"/> (the precision proxy pool).</summary>
        public IReadOnlyList<AnalyzedRejection> Unlabeled(string gate) =>
            Rejections.Where(r => !r.IsRecallTarget && r.Gate == gate).ToList();
    }

    /// <summary>
    /// Attributes each labeled "wrongly-rejected"/"missed" star to the exact gate that killed it (or flags it as a
    /// NO-CANDIDATE structure gap), and collects the unlabeled rejected candidates as the precision proxy. Shared
    /// by the in-wizard "Optimize with feedback" flow and the offline <c>recommend</c> harness; pure (image-free)
    /// so it is fully unit-testable — the caller does the detection and fills <see cref="FrameDetectionForAnalysis"/>.
    /// </summary>
    public static class LabelGateAnalyzer {

        public static LabelGateAnalysis Analyze(IReadOnlyList<FrameDetectionForAnalysis> frames) {
            var rejections = new List<AnalyzedRejection>();
            var noCandidate = new List<NoCandidateTarget>();
            var shouldRejectTargets = new List<ShouldRejectTarget>();
            int totalRecall = 0, alreadyRecovered = 0;

            foreach (var frame in frames ?? new List<FrameDetectionForAnalysis>()) {
                var rejected = frame.Rejected ?? new List<RejectedCandidateRecord>();

                // Wrap every rejected candidate; tag should-reject membership by center containment.
                var wrapped = new List<AnalyzedRejection>(rejected.Count);
                foreach (var rec in rejected) {
                    var inShouldReject = (frame.ShouldRejectBoxes ?? new List<RectD>())
                        .Any(b => Contains(b, rec.CenterX, rec.CenterY));
                    wrapped.Add(new AnalyzedRejection {
                        Gate = rec.Gate,
                        MeasuredValue = rec.MeasuredValue,
                        ThresholdValue = rec.ThresholdValue,
                        CandidateSize = rec.CandidateSize,
                        FocuserPosition = frame.FocuserPosition,
                        Bounds = RectD.FromRect(rec.Bounds),
                        IsRecallTarget = false,
                        InShouldRejectBox = inShouldReject
                    });
                }

                // Classify each recall box: accepted ⇒ already recovered; rejected ⇒ mark the matched candidate as
                // a recall target; neither ⇒ NO CANDIDATE structure gap.
                foreach (var box in frame.RecallBoxes ?? new List<RectD>()) {
                    totalRecall++;
                    var match = BoxMatcher.Classify(box, frame.AcceptedBounds, rejected);
                    if (match.Kind == BoxClassification.Accepted) {
                        alreadyRecovered++;
                    } else if (match.Kind == BoxClassification.Rejected) {
                        // Mark the wrapped record that corresponds to the matched rejected candidate.
                        var idx = IndexOfMatch(rejected, match.Rejected);
                        if (idx >= 0) {
                            wrapped[idx].IsRecallTarget = true;
                        }
                    } else {
                        noCandidate.Add(new NoCandidateTarget { FocuserPosition = frame.FocuserPosition, Box = box });
                    }
                }

                rejections.AddRange(wrapped);

                // Should-reject precision targets: flag those currently violated (an accepted center inside).
                foreach (var box in frame.ShouldRejectBoxes ?? new List<RectD>()) {
                    var violated = (frame.AcceptedCenters ?? new List<(double, double)>())
                        .Any(c => Contains(box, c.X, c.Y));
                    shouldRejectTargets.Add(new ShouldRejectTarget {
                        FocuserPosition = frame.FocuserPosition,
                        Box = box,
                        CurrentlyViolated = violated
                    });
                }
            }

            return new LabelGateAnalysis {
                Rejections = rejections,
                NoCandidate = noCandidate,
                ShouldReject = shouldRejectTargets,
                TotalRecallTargets = totalRecall,
                AlreadyRecovered = alreadyRecovered,
                NoCandidateCount = noCandidate.Count,
                FocuserPositions = (frames ?? new List<FrameDetectionForAnalysis>())
                    .Select(f => f.FocuserPosition).Distinct().OrderBy(x => x).ToList()
            };
        }

        private static bool Contains(RectD box, double x, double y) =>
            x >= box.X && x <= box.X + box.W && y >= box.Y && y <= box.Y + box.H;

        // Reference-identity index of the matched record within the frame's rejected list (the wrapped list is
        // built 1:1 in the same order, so the index aligns).
        private static int IndexOfMatch(IReadOnlyList<RejectedCandidateRecord> rejected, RejectedCandidateRecord match) {
            if (match == null) {
                return -1;
            }
            for (var i = 0; i < rejected.Count; i++) {
                if (ReferenceEquals(rejected[i], match)) {
                    return i;
                }
            }
            return -1;
        }
    }
}
