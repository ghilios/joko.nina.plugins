#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    /// <summary>
    /// One accepted star in a reviewable frame, captured as PRIMITIVES (image-pixel coordinates) so the snapshot is
    /// immutable against later mutation of the source <see cref="HocusFocusDetectedStar"/> (whose Position/BoundingBox
    /// the registration pass overwrites in place). <see cref="CenterX"/>/<see cref="CenterY"/> are the detector's
    /// (possibly aligned) Position; <see cref="BoxX"/>/<see cref="BoxY"/> are the bounding-box TOP-LEFT (for the
    /// overlay's Canvas.Left/Top binding); <see cref="OriginalX"/>/<see cref="OriginalY"/> are the raw pre-alignment
    /// detection position (the arrow start).
    /// </summary>
    public sealed class FrameReviewStar {
        public double CenterX { get; init; }
        public double CenterY { get; init; }
        public double BoxX { get; init; }
        public double BoxY { get; init; }
        public double BoxWidth { get; init; }
        public double BoxHeight { get; init; }
        public double Hfr { get; init; }

        /// <summary>The index into the run's RegisteredStars array for the physical star this maps to (stable across
        /// frames), or null when this star was not matched into any registered star.</summary>
        public int? RegistrationId { get; init; }

        public double OriginalX { get; init; }
        public double OriginalY { get; init; }

        /// <summary>Whether to draw a registration arrow from (OriginalX,OriginalY) to (CenterX,CenterY): true only
        /// when RANSAC alignment was on, this is not the reference frame, and the position actually moved.</summary>
        public bool HasArrow { get; init; }
    }

    /// <summary>
    /// One reviewable frame: its image (a frozen, display-ready <see cref="BitmapSource"/> the engine already held),
    /// the accepted stars overlaid on it, and the registration metadata. Holds only the bitmap reference + primitives,
    /// so disposing/mutating the source data after the snapshot is built does not affect it.
    /// </summary>
    public sealed class FrameReviewFrame {
        /// <summary>The frame's original index in the per-run detected-stars list (== the registration ReferenceImage
        /// index space), preserved even though <see cref="FrameReviewSnapshot.Frames"/> is ordered by focuser position.</summary>
        public int ImageIndex { get; init; }

        public double FocuserPosition { get; init; }
        public bool IsReference { get; init; }

        /// <summary>The alignment transform's human-readable scale/rotation/translation, or "" for the reference frame
        /// and when RANSAC alignment was off (no meaningful transform).</summary>
        public string TransformText { get; init; }

        public int DetectedStarCount { get; init; }
        public BitmapSource Image { get; init; }
        public IReadOnlyList<FrameReviewStar> Stars { get; init; }
    }

    /// <summary>An immutable, render-ready description of a completed sensor-model run's per-frame detections and
    /// cross-frame registration, built once at the end of analysis and consumed by the Review Frames window.</summary>
    public sealed class FrameReviewSnapshot {
        public bool RansacEnabled { get; init; }
        public int ReferenceImageIndex { get; init; }
        public IReadOnlyList<FrameReviewFrame> Frames { get; init; }
    }

    /// <summary>
    /// Builds a <see cref="FrameReviewSnapshot"/> from the full-sensor per-frame detections and the cross-frame
    /// registration the Sensor Curve Model already computed. Pure (no WPF/UI besides carrying the frozen bitmaps), so
    /// it is unit-tested directly.
    ///
    /// <para>The registration identifier is recovered by reference identity: each <see cref="SensorModel.MatchedStar"/>
    /// in <c>registeredStars[i].MatchedStars</c> carries the SAME <see cref="HocusFocusDetectedStar"/> object that
    /// lives in the frame's <c>StarList</c> (see <c>SensorModel.MatchStarsUsingKdTree</c>), so a reference-equality
    /// dictionary maps each accepted star to its registered-star index <c>i</c>.</para>
    /// </summary>
    public static class FrameReviewSnapshotBuilder {

        public static FrameReviewSnapshot Build(
            IReadOnlyList<SensorDetectedStars> allDetectedStars,
            IReadOnlyList<SensorModel.RegisteredStar> registeredStars,
            int referenceImageIndex,
            bool ransacEnabled) {

            // Reference-identity map: each accepted star object -> its registered-star index. MatchedStar.Star is the
            // SAME object reference as the frame's StarList entry, so reference equality recovers the registration id.
            var idByStar = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            if (registeredStars != null) {
                for (int i = 0; i < registeredStars.Count; i++) {
                    var matched = registeredStars[i]?.MatchedStars;
                    if (matched == null) {
                        continue;
                    }
                    foreach (var ms in matched) {
                        if (ms?.Star != null && !idByStar.ContainsKey(ms.Star)) {
                            idByStar[ms.Star] = i;
                        }
                    }
                }
            }

            var frames = new List<FrameReviewFrame>();
            var frameCount = allDetectedStars?.Count ?? 0;
            for (int imageIndex = 0; imageIndex < frameCount; imageIndex++) {
                var sds = allDetectedStars[imageIndex];
                var bitmap = sds?.Image?.Image;
                if (bitmap == null) {
                    continue; // Drop frames whose per-frame image was not retained.
                }

                var isReference = imageIndex == referenceImageIndex;
                var stars = new List<FrameReviewStar>();
                var starList = sds.StarDetectionResult?.StarList;
                if (starList != null) {
                    foreach (var detected in starList) {
                        var hf = detected as HocusFocusDetectedStar;
                        double centerX = detected.Position.X;
                        double centerY = detected.Position.Y;
                        double originalX = hf?.OriginalPosition.X ?? centerX;
                        double originalY = hf?.OriginalPosition.Y ?? centerY;
                        var box = detected.BoundingBox;
                        int? registrationId = idByStar.TryGetValue(detected, out var id) ? id : (int?)null;
                        // Arrows only make sense for a registered (aligned) non-reference frame whose position moved.
                        bool hasArrow = ransacEnabled && !isReference && (centerX != originalX || centerY != originalY);
                        stars.Add(new FrameReviewStar {
                            CenterX = centerX,
                            CenterY = centerY,
                            BoxX = box.X,
                            BoxY = box.Y,
                            BoxWidth = box.Width,
                            BoxHeight = box.Height,
                            Hfr = detected.HFR,
                            RegistrationId = registrationId,
                            OriginalX = originalX,
                            OriginalY = originalY,
                            HasArrow = hasArrow,
                        });
                    }
                }

                // The reference frame keeps the identity transform, and a non-RANSAC run never aligns — neither has a
                // meaningful transform to show.
                var transformText = (!isReference && ransacEnabled && sds.AlignmentTransform != null)
                    ? sds.AlignmentTransform.ToFullString()
                    : "";

                frames.Add(new FrameReviewFrame {
                    ImageIndex = imageIndex,
                    FocuserPosition = sds.FocuserPosition,
                    IsReference = isReference,
                    TransformText = transformText,
                    DetectedStarCount = stars.Count,
                    Image = bitmap,
                    Stars = stars,
                });
            }

            // Present frames in focuser-sweep order; ImageIndex still carries the original (registration) index.
            var ordered = frames.OrderBy(f => f.FocuserPosition).ToList();
            return new FrameReviewSnapshot {
                RansacEnabled = ransacEnabled,
                ReferenceImageIndex = referenceImageIndex,
                Frames = ordered,
            };
        }
    }
}
