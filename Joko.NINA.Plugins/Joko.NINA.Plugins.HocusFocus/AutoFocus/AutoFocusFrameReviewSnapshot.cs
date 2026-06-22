#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// One detected star in a reviewable manual-AF frame, captured as PRIMITIVES in the frame's image-pixel
    /// coordinates so the overlay sits directly on the displayed raw image and the snapshot is immutable against later
    /// mutation of the source <see cref="HocusFocusDetectedStar"/>. Carries every field needed to render any
    /// <see cref="ShowAnnotationTypeEnum"/> per-star text, the bounding box (for the Box/Ellipse bounds), and the PSF
    /// geometry (for the PSF-ellipse bounds + star-center). PSF-derived values are <see cref="double.NaN"/> when the
    /// star has no PSF fit (<see cref="HasPsf"/> false), matching the annotator which draws nothing for them.
    /// </summary>
    public sealed class AutoFocusReviewStar {

        /// <summary>The detected centroid (<c>star.Position</c>), i.e. the star-bounds Box/Ellipse anchor and the
        /// non-PSF star-center.</summary>
        public double CenterX { get; init; }

        public double CenterY { get; init; }

        public double BoxX { get; init; }
        public double BoxY { get; init; }
        public double BoxWidth { get; init; }
        public double BoxHeight { get; init; }

        /// <summary>Whether a PSF fit is available; gates the PSF-ellipse bounds, the PSF-offset star-center, and all
        /// PSF-derived annotation text.</summary>
        public bool HasPsf { get; init; }

        /// <summary>PSF sub-pixel offset from <see cref="CenterX"/>/<see cref="CenterY"/>, applied to the star-center
        /// crosshair when the bounds type is PSF (matching <c>HocusFocusStarAnnotator</c>).</summary>
        public double PsfOffsetX { get; init; }

        public double PsfOffsetY { get; init; }

        /// <summary>PSF FWHM along the major/minor axes (pixels), used to size the rotated PSF-ellipse bounds
        /// (width = 2·FWHMx, height = 2·FWHMy, centered at the centroid).</summary>
        public double PsfFwhmX { get; init; }

        public double PsfFwhmY { get; init; }

        /// <summary>PSF rotation in degrees (the annotator rotates the ellipse clockwise by this, and the PSFTheta text
        /// shows it rounded with a degree symbol).</summary>
        public double PsfThetaDegrees { get; init; }

        // ---- annotation values (one per ShowAnnotationTypeEnum) ----
        public double Hfr { get; init; }
        public double Background { get; init; }
        public double FwhmArcsec { get; init; }
        public double FwhmPixels { get; init; }
        public double FwhmX { get; init; }
        public double FwhmY { get; init; }
        public double Eccentricity { get; init; }
        public double PsfBackground { get; init; }
        public double PsfPeak { get; init; }
        public double MoffatBeta { get; init; }
    }

    /// <summary>
    /// One reviewable manual-AF frame: its frozen <see cref="BitmapSource"/>, the detected stars overlaid on it, and
    /// the detector's rejection-reason / ROI rectangles (image-pixel coords) so the review can reproduce the Hocus
    /// Focus Star Annotator's overlays. Holds only the bitmap reference + primitives.
    /// </summary>
    public sealed class AutoFocusReviewFrame {
        public int FrameIndex { get; init; }
        public double FocuserPosition { get; init; }
        public int DetectedStarCount { get; init; }
        public BitmapSource Image { get; init; }
        public IReadOnlyList<AutoFocusReviewStar> Stars { get; init; }

        // The detector's per-reason rejection rectangles (the same seven the annotator draws), each colored by the
        // matching IStarAnnotatorOptions color/show-flag at render time. Always non-null (empty when none).
        public IReadOnlyList<Rect> TooDistorted { get; init; }
        public IReadOnlyList<Rect> Degenerate { get; init; }
        public IReadOnlyList<Rect> Saturated { get; init; }
        public IReadOnlyList<Rect> LowSensitivity { get; init; }
        public IReadOnlyList<Rect> NotCentered { get; init; }
        public IReadOnlyList<Rect> TooFlat { get; init; }
        public IReadOnlyList<Rect> Contaminated { get; init; }

        /// <summary>The detection ROI rectangles (outer, plus inner crop when present); empty for a full-frame
        /// detection.</summary>
        public IReadOnlyList<Rect> RoiRects { get; init; }
    }

    /// <summary>An immutable, render-ready description of a completed manual-AF run's per-frame detections, built once
    /// at the end of the run from the retained per-frame images + star-detection results.</summary>
    public sealed class AutoFocusFrameReviewSnapshot {
        public IReadOnlyList<AutoFocusReviewFrame> Frames { get; init; }
    }

    /// <summary>
    /// Formats the per-star annotation text for the review overlay, byte-for-byte matching
    /// <c>HocusFocusStarAnnotator</c>'s <c>DrawString</c> output for each <see cref="ShowAnnotationTypeEnum"/>. PSF-only
    /// properties yield an empty string when the star has no PSF (so nothing is drawn, exactly as the annotator skips
    /// the <c>psf != null</c> branches), and Moffat Beta is blank when NaN.
    /// </summary>
    public static class AutoFocusAnnotationText {

        public static string Format(AutoFocusReviewStar star, ShowAnnotationTypeEnum type) {
            if (star == null) {
                return string.Empty;
            }
            switch (type) {
                case ShowAnnotationTypeEnum.HFR:
                    return star.Hfr.ToString("#0.00", CultureInfo.CurrentCulture);
                case ShowAnnotationTypeEnum.Background:
                    return star.Background.ToString("#.0000", CultureInfo.CurrentCulture);
                case ShowAnnotationTypeEnum.FWHM:
                    return star.HasPsf ? star.FwhmArcsec.ToString("#0.00", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.FWHMPixels:
                    return star.HasPsf ? star.FwhmPixels.ToString("#0.00", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.FWHM_X:
                    return star.HasPsf ? star.FwhmX.ToString("#0.00", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.FWHM_Y:
                    return star.HasPsf ? star.FwhmY.ToString("#0.00", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.Eccentricity:
                    return star.HasPsf ? star.Eccentricity.ToString("#.00", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.PSFTheta:
                    return star.HasPsf ? Math.Round(star.PsfThetaDegrees).ToString("##0", CultureInfo.CurrentCulture) + "°" : string.Empty;
                case ShowAnnotationTypeEnum.PSFBackground:
                    return star.HasPsf ? star.PsfBackground.ToString("#.0000", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.PSFPeak:
                    return star.HasPsf ? star.PsfPeak.ToString("#.0000", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.MoffatBeta:
                    return star.HasPsf && !double.IsNaN(star.MoffatBeta) ? star.MoffatBeta.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
                case ShowAnnotationTypeEnum.None:
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// Builds an <see cref="AutoFocusFrameReviewSnapshot"/> from the per-frame star-detection results + retained images
    /// of a completed manual auto-focus run. Pure (no WPF beyond carrying the frozen bitmaps), so it is unit-tested
    /// directly. Unlike the Aberration Inspector's <c>FrameReviewSnapshotBuilder</c>, a manual AF run uses a single
    /// region with no cross-frame registration, so there is no alignment/target/fit machinery — the box and center are
    /// the detector's own <c>BoundingBox</c>/<c>Position</c> (NOT the Original* fields, which are only populated by the
    /// inspector's alignment pass).
    /// </summary>
    public static class AutoFocusFrameReviewSnapshotBuilder {

        public static AutoFocusFrameReviewSnapshot Build(
            IReadOnlyList<(double FocuserPosition, HocusFocusStarDetectionResult Result, IRenderedImage Image)> frames) {

            var built = new List<AutoFocusReviewFrame>();
            var frameCount = frames?.Count ?? 0;
            for (int i = 0; i < frameCount; i++) {
                var (focuserPosition, result, image) = frames[i];
                var bitmap = image?.Image;
                if (bitmap == null) {
                    continue; // Drop frames whose per-frame image was not retained.
                }

                var stars = new List<AutoFocusReviewStar>();
                var starList = result?.StarList;
                if (starList != null) {
                    foreach (var detected in starList) {
                        var hf = detected as HocusFocusDetectedStar;
                        var psf = hf?.PSF;
                        var box = detected.BoundingBox;
                        stars.Add(new AutoFocusReviewStar {
                            CenterX = detected.Position.X,
                            CenterY = detected.Position.Y,
                            BoxX = box.X,
                            BoxY = box.Y,
                            BoxWidth = box.Width,
                            BoxHeight = box.Height,
                            HasPsf = psf != null,
                            PsfOffsetX = psf?.OffsetX ?? 0.0,
                            PsfOffsetY = psf?.OffsetY ?? 0.0,
                            PsfFwhmX = psf?.FWHMx ?? double.NaN,
                            PsfFwhmY = psf?.FWHMy ?? double.NaN,
                            PsfThetaDegrees = psf != null ? MathUtility.RadiansToDegrees(psf.ThetaRadians) : double.NaN,
                            Hfr = detected.HFR,
                            Background = detected.Background,
                            FwhmArcsec = psf?.FWHMArcsecs ?? double.NaN,
                            FwhmPixels = psf?.FWHMPixels ?? double.NaN,
                            FwhmX = psf?.FWHMx ?? double.NaN,
                            FwhmY = psf?.FWHMy ?? double.NaN,
                            Eccentricity = psf?.Eccentricity ?? double.NaN,
                            PsfBackground = psf?.Background ?? double.NaN,
                            PsfPeak = psf?.Peak ?? double.NaN,
                            MoffatBeta = psf?.Beta ?? double.NaN,
                        });
                    }
                }

                var metrics = result?.Metrics;
                built.Add(new AutoFocusReviewFrame {
                    FrameIndex = i,
                    FocuserPosition = focuserPosition,
                    DetectedStarCount = stars.Count,
                    Image = bitmap,
                    Stars = stars,
                    TooDistorted = ToRects(metrics?.TooDistortedBounds),
                    Degenerate = ToRects(metrics?.DegenerateBounds),
                    Saturated = ToRects(metrics?.SaturatedBounds),
                    LowSensitivity = ToRects(metrics?.LowSensitivityBounds),
                    NotCentered = ToRects(metrics?.NotCenteredBounds),
                    TooFlat = ToRects(metrics?.TooFlatBounds),
                    Contaminated = ToRects(metrics?.ContaminatedBounds),
                    RoiRects = BuildRoiRects(result, bitmap.PixelWidth, bitmap.PixelHeight),
                });
            }

            // Present frames in focuser-sweep order; OrderBy is stable so multiple frames per position keep their
            // capture order (and FrameIndex still records the original capture index).
            var ordered = built.OrderBy(f => f.FocuserPosition).ToList();
            return new AutoFocusFrameReviewSnapshot { Frames = ordered };
        }

        private static IReadOnlyList<Rect> ToRects(IEnumerable<OpenCvSharp.Rect> bounds) {
            if (bounds == null) {
                return Array.Empty<Rect>();
            }
            return bounds.Select(r => new Rect(r.X, r.Y, r.Width, r.Height)).ToList();
        }

        // Mirrors HocusFocusStarAnnotator's ROI drawing: the outer boundary, plus the inner crop boundary when present,
        // scaled from normalized region coordinates to image pixels. Empty when the region covers the full frame.
        private static IReadOnlyList<Rect> BuildRoiRects(HocusFocusStarDetectionResult result, int pixelWidth, int pixelHeight) {
            var region = result?.DetectorParams?.Region;
            if (region == null || region.IsFull()) {
                return Array.Empty<Rect>();
            }
            var rects = new List<Rect>();
            var outer = region.OuterBoundary;
            rects.Add(new Rect(outer.StartX * pixelWidth, outer.StartY * pixelHeight, outer.Width * pixelWidth, outer.Height * pixelHeight));
            var inner = region.InnerCropBoundary;
            if (inner != null) {
                rects.Add(new Rect(inner.StartX * pixelWidth, inner.StartY * pixelHeight, inner.Width * pixelWidth, inner.Height * pixelHeight));
            }
            return rects;
        }
    }
}
