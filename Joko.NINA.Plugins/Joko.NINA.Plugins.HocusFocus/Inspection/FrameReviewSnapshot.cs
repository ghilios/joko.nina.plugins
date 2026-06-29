#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.Inspection {

    /// <summary>The registration + focus-fit status of an accepted star, which drives its overlay box color.</summary>
    public enum FrameReviewRegistrationState {
        /// <summary>Accepted but never matched into a registered star (no cross-frame registration).</summary>
        Unmatched,

        /// <summary>Matched/registered, but it produced no accepted per-star focus fit (too few points, failed solve,
        /// or below the per-star R² gate), so it did not contribute to the sensor model.</summary>
        MatchedNoFit,

        /// <summary>Matched/registered with an accepted per-star focus fit (contributed to the model).</summary>
        MatchedWithFit
    }

    /// <summary>
    /// One accepted star in a reviewable frame, captured as PRIMITIVES in the frame's RAW (pre-alignment) coordinates,
    /// so the overlay sits on the displayed raw image and the snapshot is immutable against later mutation of the
    /// source <see cref="HocusFocusDetectedStar"/>. <see cref="CenterX"/>/<see cref="CenterY"/> are the raw detected
    /// centroid (the cyan registration marker); <see cref="BoxX"/>/<see cref="BoxY"/> are the raw bounding-box
    /// top-left; <see cref="TargetX"/>/<see cref="TargetY"/> are the star's ALIGNED (reference-frame) position — the
    /// registration target the orange line/marker points to.
    /// </summary>
    public sealed class FrameReviewStar {
        public double CenterX { get; init; }
        public double CenterY { get; init; }
        public double BoxX { get; init; }
        public double BoxY { get; init; }
        public double BoxWidth { get; init; }
        public double BoxHeight { get; init; }
        public double Hfr { get; init; }

        /// <summary>The index into the run's RegisteredStars for the physical star this maps to, or null if unmatched.</summary>
        public int? RegistrationId { get; init; }

        public FrameReviewRegistrationState RegistrationState { get; init; }

        public double TargetX { get; init; }
        public double TargetY { get; init; }

        /// <summary>Whether to draw the registration line (center → target): true only when RANSAC alignment was on,
        /// this is not the reference frame, the star is matched, and its aligned position actually moved.</summary>
        public bool HasRegistrationLine { get; init; }

        /// <summary>Signed offset (focuser steps) of this star's best focus from the field-mean best focus over all
        /// fitted stars; null when the star has no accepted focus fit.</summary>
        public double? FocusOffsetFromMean { get; init; }
    }

    /// <summary>
    /// One reviewable frame: its frozen raw <see cref="BitmapSource"/>, the accepted stars overlaid on it, and the
    /// registration metadata. Holds only the bitmap reference + primitives.
    /// </summary>
    public sealed class FrameReviewFrame {
        public int ImageIndex { get; init; }
        public double FocuserPosition { get; init; }
        public bool IsReference { get; init; }

        /// <summary>The alignment transform's human-readable scale/rotation/translation (with a degree symbol), or ""
        /// for the reference frame and when RANSAC alignment was off.</summary>
        public string TransformText { get; init; }

        public int DetectedStarCount { get; init; }
        public BitmapSource Image { get; init; }
        public IReadOnlyList<FrameReviewStar> Stars { get; init; }
    }

    /// <summary>
    /// Per-registered-star focus-curve data for the Review Frames hover graph (one per physical star, shared across
    /// frames). Disposal-safe: <see cref="Points"/> are value structs and <see cref="Fit"/>'s curve closure captures
    /// only a <c>double[]</c> solution, so it survives the disposal of the run's heavy data.
    /// </summary>
    public sealed class FrameReviewFocusCurve {
        public int RegistrationId { get; init; }
        public AlglibHyperbolicFitting Fit { get; init; }
        public IReadOnlyList<ScatterErrorPoint> Points { get; init; }

        /// <summary>The detections the fit rejected as outliers (subset of <see cref="Points"/> by coordinate), so the
        /// hover graph can mark them with a red X. Empty when none were rejected.</summary>
        public IReadOnlyList<ScatterErrorPoint> RejectedPoints { get; init; } = Array.Empty<ScatterErrorPoint>();

        public double RSquared { get; init; }

        /// <summary>Focuser position at best focus (the fit minimum's X).</summary>
        public double BestFocus { get; init; }

        public double? OffsetFromMean { get; init; }
    }

    /// <summary>An immutable, render-ready description of a completed sensor-model run's per-frame detections,
    /// cross-frame registration, and per-star focus fits, built once at the end of analysis.</summary>
    public sealed class FrameReviewSnapshot {
        public bool RansacEnabled { get; init; }
        public int ReferenceImageIndex { get; init; }
        public IReadOnlyList<FrameReviewFrame> Frames { get; init; }

        /// <summary>Per-registered-star focus curves, keyed by registration id (only stars with an accepted fit).</summary>
        public IReadOnlyDictionary<int, FrameReviewFocusCurve> FocusCurvesByRegistrationId { get; init; }
    }

    /// <summary>Formats a registration transform like <c>Matrix3x2.ToFullString</c> but with a "°" degree symbol, for
    /// the Review Frames header. Kept separate so the shared <c>ToFullString</c> (used by diagnostic image annotations)
    /// is not perturbed.</summary>
    public static class FrameReviewTransformFormatter {
        public static string Format(Matrix3x2 t) {
            if (t == null) {
                return string.Empty;
            }
            var scaleX = Math.Sqrt(t.M11 * t.M11 + t.M21 * t.M21);
            var scaleY = Math.Sqrt(t.M12 * t.M12 + t.M22 * t.M22);
            var rotationDegrees = Math.Atan2(t.M21, t.M11) * 180.0 / Math.PI;
            return $"Scale(x,y): ({scaleX:0.###},{scaleY:0.###}), rotation: {rotationDegrees:0.###}°, translation(x,y): {t.M31:0.###},{t.M32:0.###}";
        }
    }

    /// <summary>
    /// Builds a <see cref="FrameReviewSnapshot"/> from the full-sensor per-frame detections and the cross-frame
    /// registration + per-star fits the Sensor Curve Model already computed. Pure (no WPF/UI besides carrying the
    /// frozen bitmaps), so it is unit-tested directly.
    ///
    /// <para>The registration identifier is recovered by reference identity: each <see cref="SensorModel.MatchedStar"/>
    /// in <c>registeredStars[i].MatchedStars</c> carries the SAME <see cref="HocusFocusDetectedStar"/> object that
    /// lives in the frame's <c>StarList</c>, so a reference-equality dictionary maps each accepted star to its
    /// registered-star index <c>i</c>.</para>
    /// </summary>
    public static class FrameReviewSnapshotBuilder {

        public static FrameReviewSnapshot Build(
            IReadOnlyList<SensorDetectedStars> allDetectedStars,
            IReadOnlyList<SensorModel.RegisteredStar> registeredStars,
            int referenceImageIndex,
            bool ransacEnabled) {

            // Reference-identity map: each accepted star object -> its registered-star index.
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

            // Per-registered-star focus curves + the field-mean best-focus offset. Only stars with an accepted fit
            // (RegisteredStar.Fitting != null) have a curve; the offset is each fitted star's best focus minus the
            // mean best focus across all fitted stars.
            var focusCurves = new Dictionary<int, FrameReviewFocusCurve>();
            var offsetByRegId = new Dictionary<int, double>();
            if (registeredStars != null) {
                // Field-mean best focus over the FITTED stars only (drives the offset-from-mean label).
                var fittedBestFoci = new List<double>();
                for (int i = 0; i < registeredStars.Count; i++) {
                    if (registeredStars[i]?.Fitting != null) {
                        fittedBestFoci.Add(registeredStars[i].Fitting.Minimum.X);
                    }
                }
                double? meanBestFocus = fittedBestFoci.Count > 0 ? fittedBestFoci.Average() : (double?)null;

                // Build a focus curve for EVERY matched star, so a registered-but-unfitted star still shows how its
                // detections look across the other frames on hover. Fitted stars additionally carry the curve, R²,
                // best focus, and the offset from the field mean.
                for (int i = 0; i < registeredStars.Count; i++) {
                    var rs = registeredStars[i];
                    var matched = rs?.MatchedStars;
                    if (matched == null || matched.Count == 0) {
                        continue;
                    }
                    // Display points only: one detection per focuser position, so there is no sample spread to show.
                    // Zero error (the chart draws plain markers, no error bars); the per-star fit computes its own
                    // weights independently in SensorModel.FitImages.
                    var points = matched
                        .Select(m => new ScatterErrorPoint(m.FocuserPosition, m.Star.HFR, 0.0, 0.0))
                        .ToList();
                    double? offset = null;
                    if (rs.Fitting != null && meanBestFocus.HasValue) {
                        offset = rs.Fitting.Minimum.X - meanBestFocus.Value;
                        offsetByRegId[i] = offset.Value;
                    }
                    // Rejected outliers only for fitted stars (a fitless star has no fit to reject from); projected to
                    // zero-error points like the display Points, since the graph draws plain red-X markers.
                    var rejected = (rs.Fitting != null ? rs.RejectedPoints : null)?
                        .Select(p => new ScatterErrorPoint(p.X, p.Y, 0.0, 0.0))
                        .ToList() ?? (IReadOnlyList<ScatterErrorPoint>)Array.Empty<ScatterErrorPoint>();
                    focusCurves[i] = new FrameReviewFocusCurve {
                        RegistrationId = i,
                        Fit = rs.Fitting,
                        Points = points,
                        RejectedPoints = rejected,
                        RSquared = rs.Fitting?.RSquared ?? double.NaN,
                        BestFocus = rs.Fitting?.Minimum.X ?? double.NaN,
                        OffsetFromMean = offset,
                    };
                }
            }

            var frames = new List<FrameReviewFrame>();
            var frameCount = allDetectedStars?.Count ?? 0;
            // If the reference frame's image was dropped, no surviving frame is the reference; in that case the
            // alignment is relative to a frame the user can't see, so suppress registration lines/transform text
            // rather than rendering survivors as if aligned against a present reference (F37).
            bool referenceSurvives = referenceImageIndex >= 0 && referenceImageIndex < frameCount
                && allDetectedStars[referenceImageIndex]?.Image?.Image != null;
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
                        // Raw center + raw bounding box so the overlay sits on the displayed (raw) frame.
                        double centerX = hf?.OriginalPosition.X ?? detected.Position.X;
                        double centerY = hf?.OriginalPosition.Y ?? detected.Position.Y;
                        var box = hf != null ? hf.OriginalBoundingBox : detected.BoundingBox;
                        // Aligned (reference-frame) position = the registration target.
                        double targetX = detected.Position.X;
                        double targetY = detected.Position.Y;

                        int? registrationId = idByStar.TryGetValue(detected, out var id) ? id : (int?)null;
                        FrameReviewRegistrationState state;
                        if (registrationId == null) {
                            state = FrameReviewRegistrationState.Unmatched;
                        } else if (registeredStars != null && registeredStars[registrationId.Value]?.Fitting != null) {
                            state = FrameReviewRegistrationState.MatchedWithFit;
                        } else {
                            state = FrameReviewRegistrationState.MatchedNoFit;
                        }

                        double? focusOffset = (registrationId != null && offsetByRegId.TryGetValue(registrationId.Value, out var off))
                            ? off
                            : (double?)null;

                        // The line is only meaningful for a registered star on an aligned (RANSAC) non-reference frame
                        // whose aligned position differs from its raw position. Use a tolerance (not exact !=) so a
                        // star whose alignment maps it back to a near-identical position (sub-pixel float residual)
                        // does NOT draw a visually meaningless ~zero-length line; only a move whose |Δx|+|Δy| exceeds
                        // the epsilon is worth annotating.
                        const double registrationLineEpsilon = 1e-6;
                        bool hasRegistrationLine = ransacEnabled && referenceSurvives && !isReference && registrationId != null
                            && (Math.Abs(targetX - centerX) + Math.Abs(targetY - centerY) > registrationLineEpsilon);

                        stars.Add(new FrameReviewStar {
                            CenterX = centerX,
                            CenterY = centerY,
                            BoxX = box.X,
                            BoxY = box.Y,
                            BoxWidth = box.Width,
                            BoxHeight = box.Height,
                            Hfr = detected.HFR,
                            RegistrationId = registrationId,
                            RegistrationState = state,
                            TargetX = targetX,
                            TargetY = targetY,
                            HasRegistrationLine = hasRegistrationLine,
                            FocusOffsetFromMean = focusOffset,
                        });
                    }
                }

                var transformText = (referenceSurvives && !isReference && ransacEnabled && sds.AlignmentTransform != null)
                    ? FrameReviewTransformFormatter.Format(sds.AlignmentTransform)
                    : string.Empty;

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
                FocusCurvesByRegistrationId = focusCurves,
            };
        }
    }
}
