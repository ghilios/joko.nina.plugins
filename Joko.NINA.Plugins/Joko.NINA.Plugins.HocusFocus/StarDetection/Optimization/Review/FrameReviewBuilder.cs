#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// A lightweight description of one reviewable frame: where it lives on disk, its focuser position, and the run
    /// it belongs to. Carries ONLY paths/positions (no Mats), so it survives the disposal of the in-memory
    /// <c>RunEvaluationData</c> source Mats — the in-NINA wizard snapshots these before the run frames are freed and
    /// rebuilds the review by detecting from disk afterward.
    /// </summary>
    public readonly struct FrameReviewDescriptor {
        public string RunId { get; }
        public int FocuserPosition { get; }
        public string FramePath { get; }

        public FrameReviewDescriptor(string runId, int focuserPosition, string framePath) {
            RunId = runId;
            FocuserPosition = focuserPosition;
            FramePath = framePath;
        }
    }

    /// <summary>
    /// The single source of truth for turning a set of <see cref="FrameReviewDescriptor"/>s into the
    /// <see cref="FrameReview"/>s the interactive <see cref="StarReviewVM"/> renders. BOTH the TestApp
    /// <c>review</c> dev tool and the in-NINA optimization wizard build their reviews through this helper, so the
    /// detection, accepted/rejected extraction, and the MTF-stretch image-provider wiring can never drift.
    ///
    /// <para>Disk loading is decoupled via the <paramref name="floatMatLoader"/> delegate: TestApp supplies its
    /// profile-aware <c>DiagnosticUtil.LoadFloatMat</c>; the wizard supplies a plugin-side loader. The plugin helper
    /// therefore never references TestApp. Detection is equivalent to the legacy StarReviewRunner path — load the
    /// frame's normalized float Mat, clone it (Detect mutates its input), run <see cref="StarDetector.Detect(Mat,
    /// StarDetectorParams, IProgress{NINA.Core.Model.ApplicationStatus}, CancellationToken)"/>, capture each accepted
    /// star's REAL bounding box + center + HFR, and flatten the per-reason rejection bounds.</para>
    /// </summary>
    public static class FrameReviewBuilder {

        /// <summary>
        /// Builds one <see cref="FrameReview"/> per descriptor, in input order, detecting each frame from disk with
        /// <paramref name="detectorParams"/>. Each review's <see cref="FrameReview.ImageProvider"/> reloads the frame
        /// from disk and runs the shared <see cref="StarReviewImaging.BuildStretchedBitmap"/> MTF stretch off-thread
        /// (the VM never touches disk). The build itself runs on the calling thread/task; callers that need it off the
        /// UI thread should wrap the call in <see cref="Task.Run(Func{Task})"/>.
        /// </summary>
        /// <param name="descriptors">The frames to build reviews for (paths + positions + runId).</param>
        /// <param name="detectorParams">The detector params to detect each frame with (e.g. the optimized BestParams).</param>
        /// <param name="detector">The star detector (constructed by the caller with its AlglibAPI).</param>
        /// <param name="floatMatLoader">Profile-aware (or .tif-direct) loader producing a CV_32F [0,1] Mat for a path.</param>
        /// <param name="token">Cancellation token honored between frames and during detection.</param>
        /// <param name="progress">Optional determinate per-frame progress (each detected frame), used by the wizard's
        /// "Detecting frames for review" bar. Null (the default) reports nothing — byte-identical to the old behavior,
        /// so TestApp and other callers are unaffected.</param>
        public static async Task<List<FrameReview>> BuildAsync(
            IEnumerable<FrameReviewDescriptor> descriptors,
            StarDetectorParams detectorParams,
            StarDetector detector,
            Func<string, Task<Mat>> floatMatLoader,
            CancellationToken token,
            IProgress<RunLoadProgress> progress = null) {
            if (descriptors == null) {
                throw new ArgumentNullException(nameof(descriptors));
            }
            if (detectorParams == null) {
                throw new ArgumentNullException(nameof(detectorParams));
            }
            if (detector == null) {
                throw new ArgumentNullException(nameof(detector));
            }
            if (floatMatLoader == null) {
                throw new ArgumentNullException(nameof(floatMatLoader));
            }

            // Materialize so we know the total up-front for determinate progress.
            var descriptorList = descriptors as IReadOnlyList<FrameReviewDescriptor> ?? descriptors.ToList();
            var reviews = new List<FrameReview>(descriptorList.Count);
            for (var i = 0; i < descriptorList.Count; i++) {
                token.ThrowIfCancellationRequested();
                reviews.Add(await BuildOneAsync(descriptorList[i], detectorParams, detector, floatMatLoader, token).ConfigureAwait(false));
                progress?.Report(new RunLoadProgress(i + 1, descriptorList.Count));
            }
            return reviews;
        }

        private static async Task<FrameReview> BuildOneAsync(
            FrameReviewDescriptor d,
            StarDetectorParams detectorParams,
            StarDetector detector,
            Func<string, Task<Mat>> floatMatLoader,
            CancellationToken token) {
            // Collect the rich per-rejected-candidate records so the in-wizard "Optimize with feedback" analyzer can
            // attribute each labeled star to the gate that killed it. The flag is a bit-identical side channel
            // (excluded from the cache key), so the accepted/rejected overlays are unchanged. Clone so the caller's
            // params are not mutated.
            var diagParams = detectorParams.Clone();
            diagParams.CollectRejectedCandidateDiagnostics = true;

            HocusFocusStarDetectorResult result;
            using (var mat = await floatMatLoader(d.FramePath).ConfigureAwait(false))
            using (var clone = mat.Clone()) { // Detect mutates its input in place.
                result = await detector.Detect(clone, diagParams, null, token).ConfigureAwait(false);
            }

            var accepted = result.DetectedStars ?? new List<Star>();
            var framePath = d.FramePath;
            return new FrameReview {
                RunId = d.RunId,
                FocuserPosition = d.FocuserPosition,
                FramePath = framePath,
                // The decoupling seam: reload the frame from disk and run the shared plugin MTF stretch off-thread.
                // The VM marshals the resulting frozen BitmapSource back to the UI.
                ImageProvider = () => Task.Run(async () => {
                    using var srcFloat = await floatMatLoader(framePath).ConfigureAwait(false);
                    return StarReviewImaging.BuildStretchedBitmap(srcFloat);
                }),
                // Each accepted star's REAL StarBoundingBox so the overlay draws actual-size boxes and the
                // should-reject click records the actual bounds.
                Accepted = accepted.Select(s => (s.Center.X, s.Center.Y, s.HFR, s.StarBoundingBox)).ToList(),
                Rejected = ExtractRejected(result),
                RejectedCandidates = result.RejectedCandidates ?? new List<RejectedCandidateRecord>(),
            };
        }

        /// <summary>
        /// Flattens the per-reason rejection bounds from <see cref="HocusFocusStarDetectorResult.Metrics"/> into a
        /// flat list of (reason, rect) tuples, using the SAME reason set as the annotated optimizer PNG so the colors
        /// line up across the review tools (and the wizard). Pure — unit-tested directly.
        /// </summary>
        public static List<(string Reason, Rect Bounds)> ExtractRejected(HocusFocusStarDetectorResult result) {
            var list = new List<(string, Rect)>();
            var m = result?.Metrics;
            if (m == null) {
                return list;
            }
            void Add(string reason, List<Rect> rects) {
                if (rects == null) {
                    return;
                }
                foreach (var r in rects) {
                    list.Add((reason, r));
                }
            }
            Add("TooDistorted", m.TooDistortedBounds);
            Add("Degenerate", m.DegenerateBounds);
            Add("Saturated", m.SaturatedBounds);
            Add("LowSensitivity", m.LowSensitivityBounds);
            Add("NotCentered", m.NotCenteredBounds);
            Add("TooFlat", m.TooFlatBounds);
            Add("Contaminated", m.ContaminatedBounds);
            return list;
        }
    }
}
