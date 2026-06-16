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
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>A double-precision axis-aligned rectangle (label boxes carry fractional coordinates).</summary>
    public readonly struct RectD {
        public readonly double X, Y, W, H;

        public RectD(double x, double y, double w, double h) {
            X = x; Y = y; W = w; H = h;
        }

        public double Area => Math.Max(0.0, W) * Math.Max(0.0, H);

        public static RectD FromRect(Rect r) => new RectD(r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>How a label box relates to a fresh detection of its frame.</summary>
    public enum BoxClassification {
        /// <summary>Overlaps an accepted star — already detected (a recall target here is already recovered).</summary>
        Accepted,

        /// <summary>Overlaps a rejected candidate — a gate killed it (recoverable by tuning that gate).</summary>
        Rejected,

        /// <summary>Overlaps neither — no candidate ever formed there (a structure-detection gap).</summary>
        NoCandidate
    }

    public sealed class BoxMatchResult {
        public BoxClassification Kind { get; set; }
        public double AcceptedIoU { get; set; }
        public Rect AcceptedBounds { get; set; }
        public double RejectedIoU { get; set; }

        /// <summary>The best-overlapping rejected candidate (set whenever any rejected candidate overlaps, even if
        /// <see cref="Kind"/> is <see cref="BoxClassification.Accepted"/> on a tie). Null when none overlaps.</summary>
        public RejectedCandidateRecord Rejected { get; set; }

        /// <summary>Number of DISTINCT gates that tie for the best rejected overlap (&gt;1 = ambiguous attribution).</summary>
        public int RejectedTieCount { get; set; }

        public string Gate => Rejected?.Gate;
    }

    /// <summary>
    /// Shared label-box → detection matcher used by the diagnose-labels harness, the in-wizard label analyzer,
    /// and the recommender, so all three agree on attribution by construction. A box is classified by BEST
    /// overlap (IoU) across the accepted-star pool and the rejected-candidate pool; accepted wins ties (it
    /// reflects the detector's actual outcome at that location). With no real overlap in either pool the box is a
    /// NO-CANDIDATE structure gap. This is the plugin-side home of the logic that previously lived only in
    /// TestApp's DiagnoseLabelsRunner.
    /// </summary>
    public static class BoxMatcher {

        /// <summary>Intersection-over-union of two double rectangles. Zero when disjoint or either has zero area.</summary>
        public static double IoU(RectD a, RectD b) {
            var ix = Math.Max(a.X, b.X);
            var iy = Math.Max(a.Y, b.Y);
            var ix2 = Math.Min(a.X + a.W, b.X + b.W);
            var iy2 = Math.Min(a.Y + a.H, b.Y + b.H);
            var iw = ix2 - ix;
            var ih = iy2 - iy;
            if (iw <= 0.0 || ih <= 0.0) {
                return 0.0;
            }
            var inter = iw * ih;
            var union = a.Area + b.Area - inter;
            return union <= 0.0 ? 0.0 : inter / union;
        }

        public static double IoU(RectD a, Rect b) => IoU(a, RectD.FromRect(b));

        /// <summary>
        /// Classifies <paramref name="labelBox"/> against the accepted and rejected candidate pools of a single
        /// frame's detection. <paramref name="rejected"/> are the rich per-gate records
        /// (<see cref="HocusFocusStarDetectorResult.RejectedCandidates"/>), so the gate that killed a
        /// wrongly-rejected star — including the counter-only gates (TooLowHFR/TooSmall) that have no metrics
        /// <c>*Bounds</c> list and so previously mis-classified as NO CANDIDATE — is reported.
        /// </summary>
        public static BoxMatchResult Classify(
            RectD labelBox,
            IReadOnlyList<Rect> acceptedBounds,
            IReadOnlyList<RejectedCandidateRecord> rejected) {

            double bestAcceptedIoU = 0.0;
            Rect bestAcceptedRect = default;
            bool haveAccepted = false;
            if (acceptedBounds != null) {
                foreach (var b in acceptedBounds) {
                    var iou = IoU(labelBox, b);
                    if (iou > bestAcceptedIoU) {
                        bestAcceptedIoU = iou;
                        bestAcceptedRect = b;
                        haveAccepted = true;
                    }
                }
            }

            double bestRejectedIoU = 0.0;
            RejectedCandidateRecord bestRejected = null;
            int tieCount = 0;
            if (rejected != null) {
                foreach (var rec in rejected) {
                    var iou = IoU(labelBox, rec.Bounds);
                    if (iou <= 0.0) {
                        continue;
                    }
                    if (iou > bestRejectedIoU) {
                        bestRejectedIoU = iou;
                        bestRejected = rec;
                        tieCount = 1;
                    } else if (Math.Abs(iou - bestRejectedIoU) < 1e-9 && bestRejected != null
                               && !string.Equals(rec.Gate, bestRejected.Gate, StringComparison.Ordinal)) {
                        tieCount++;
                    }
                }
            }

            bool acceptedReal = haveAccepted && bestAcceptedIoU > 0.0;
            bool rejectedReal = bestRejected != null && bestRejectedIoU > 0.0;

            var result = new BoxMatchResult {
                AcceptedIoU = bestAcceptedIoU,
                AcceptedBounds = bestAcceptedRect,
                RejectedIoU = bestRejectedIoU,
                Rejected = bestRejected,
                RejectedTieCount = tieCount
            };

            if (!acceptedReal && !rejectedReal) {
                result.Kind = BoxClassification.NoCandidate;
            } else if (acceptedReal && bestAcceptedIoU >= bestRejectedIoU) {
                // Accepted wins ties — reflects the detector's actual outcome at that location.
                result.Kind = BoxClassification.Accepted;
            } else {
                result.Kind = BoxClassification.Rejected;
            }
            return result;
        }
    }
}
