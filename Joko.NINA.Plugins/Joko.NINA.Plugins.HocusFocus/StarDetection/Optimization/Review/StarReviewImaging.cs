#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Shared imaging helpers for the interactive star-review UI: the MTF stretch that turns a normalized float Mat
    /// into a display-ready 16-bit <see cref="BitmapSource"/>, plus the raw Mat→<see cref="BitmapSource"/> conversion.
    /// Lives in the plugin so BOTH the TestApp <c>review</c> dev tool and the in-NINA wizard reuse exactly the same
    /// stretch (single source of truth, no drift). Pure imaging — no profile/IO coupling.
    /// </summary>
    public static class StarReviewImaging {

        /// <summary>
        /// MTF-stretches a normalized float Mat (values in [0,1]) into a display-ready 16-bit grayscale
        /// <see cref="BitmapSource"/>. Converts to CV_16U, runs the histogram-based MTF stretch
        /// (<see cref="CvImageUtility.CalculateStatistics_Histogram"/> → <see cref="CvImageUtility.CreateMTFLookup"/>
        /// → <see cref="CvImageUtility.ApplyLUT"/>), falling back to a linear min/max normalization if the stretch
        /// throws. The returned bitmap is frozen and safe to hand to any thread's UI.
        /// </summary>
        public static BitmapSource BuildStretchedBitmap(Mat srcFloat) {
            if (srcFloat == null) {
                throw new ArgumentNullException(nameof(srcFloat));
            }
            using var src16 = new Mat();
            srcFloat.ConvertTo(src16, MatType.CV_16U, ushort.MaxValue);
            using var stretched16 = new Mat();
            try {
                var stats = CvImageUtility.CalculateStatistics_Histogram(src16);
                using var lut = CvImageUtility.CreateMTFLookup(stats);
                CvImageUtility.ApplyLUT(src16, lut, stretched16);
            } catch (Exception ex) {
                Logger.Warning($"MTF stretch failed ({ex.Message}); falling back to linear normalization");
                Cv2.Normalize(src16, stretched16, 0, ushort.MaxValue, NormTypes.MinMax);
            }
            return ToBitmapSource(stretched16, PixelFormats.Gray16);
        }

        /// <summary>Wraps an OpenCV <see cref="Mat"/>'s pixel buffer in a frozen <see cref="BitmapSource"/> of the
        /// given pixel format (no copy beyond what <see cref="BitmapSource.Create(int,int,double,double,PixelFormat,System.Windows.Media.Imaging.BitmapPalette,IntPtr,int,int)"/> does).</summary>
        public static BitmapSource ToBitmapSource(Mat src, PixelFormat pf) {
            int stride = (src.Width * pf.BitsPerPixel + 7) / 8;
            double dpi = 96;

            var dataSize = (long)src.DataEnd - (long)src.DataStart;
            var source = BitmapSource.Create(src.Width, src.Height, dpi, dpi, pf, null, src.DataStart, (int)dataSize, stride);
            source.Freeze();
            return source;
        }
    }
}
