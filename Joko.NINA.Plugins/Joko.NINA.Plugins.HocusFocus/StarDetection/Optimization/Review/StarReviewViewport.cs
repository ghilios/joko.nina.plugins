#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Pure zoom/pan viewport math for the review window, factored out of the WPF code so the screen↔image pixel
    /// mapping (the load-bearing part of the click-to-label UX) is unit-testable. The model is a simple uniform
    /// scale + translation: a point at image pixel (ix, iy) is drawn at screen (ix·Scale + OffsetX, iy·Scale +
    /// OffsetY); the inverse maps a mouse click back to an image pixel. Scale and Offset are exactly what the
    /// rendering layer applies (a ScaleTransform + TranslateTransform on the overlay/Image), so the two stay in
    /// lockstep.
    /// </summary>
    public sealed class StarReviewViewport {
        public const double MinScale = 0.05;
        public const double MaxScale = 40.0;

        public double Scale { get; private set; } = 1.0;
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }

        public StarReviewViewport() { }

        public StarReviewViewport(double scale, double offsetX, double offsetY) {
            Scale = ClampScale(scale);
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        /// <summary>Image pixel -> screen point.</summary>
        public (double X, double Y) ImageToScreen(double imageX, double imageY) {
            return (imageX * Scale + OffsetX, imageY * Scale + OffsetY);
        }

        /// <summary>Screen point -> image pixel (the inverse of <see cref="ImageToScreen"/>).</summary>
        public (double X, double Y) ScreenToImage(double screenX, double screenY) {
            return ((screenX - OffsetX) / Scale, (screenY - OffsetY) / Scale);
        }

        /// <summary>
        /// Sizes and centers the image so it fits within a viewport of (<paramref name="viewportWidth"/> ×
        /// <paramref name="viewportHeight"/>): scale = min(vw/iw, vh/ih) (never up-scaling past 1:1), then centered.
        /// </summary>
        public void FitTo(double viewportWidth, double viewportHeight, double imageWidth, double imageHeight) {
            if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0) {
                Scale = 1.0;
                OffsetX = 0;
                OffsetY = 0;
                return;
            }
            var fit = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
            Scale = ClampScale(Math.Min(fit, 1.0));
            OffsetX = (viewportWidth - imageWidth * Scale) / 2.0;
            OffsetY = (viewportHeight - imageHeight * Scale) / 2.0;
        }

        /// <summary>
        /// Zooms by <paramref name="factor"/> keeping the image point currently under the screen anchor
        /// (<paramref name="anchorScreenX"/>, <paramref name="anchorScreenY"/>) fixed — i.e. the pixel under the
        /// cursor stays under the cursor. This is the wheel-zoom behavior users expect.
        /// </summary>
        public void ZoomAt(double factor, double anchorScreenX, double anchorScreenY) {
            var (imgX, imgY) = ScreenToImage(anchorScreenX, anchorScreenY);
            var newScale = ClampScale(Scale * factor);
            // Solve for the offset that keeps (imgX, imgY) under the anchor at the new scale.
            OffsetX = anchorScreenX - imgX * newScale;
            OffsetY = anchorScreenY - imgY * newScale;
            Scale = newScale;
        }

        /// <summary>Pans by a screen-space delta (drag).</summary>
        public void PanBy(double dxScreen, double dyScreen) {
            OffsetX += dxScreen;
            OffsetY += dyScreen;
        }

        public void Set(double scale, double offsetX, double offsetY) {
            Scale = ClampScale(scale);
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public static double ClampScale(double s) {
            if (double.IsNaN(s) || s <= 0) {
                return 1.0;
            }
            if (s < MinScale) {
                return MinScale;
            }
            if (s > MaxScale) {
                return MaxScale;
            }
            return s;
        }
    }
}
