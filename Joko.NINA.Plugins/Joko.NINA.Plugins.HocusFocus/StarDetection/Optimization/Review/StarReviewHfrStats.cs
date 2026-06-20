#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Pure, unit-tested per-frame HFR statistics for the review overlay. The center + deviation choice mirrors how
    /// <c>HocusFocusStarDetection</c> reduces a frame to <c>AverageHFR</c>/<c>HFRStdDev</c>: in
    /// <see cref="MeasurementAverageEnum.Median"/> mode the center is the median and the deviation is the scaled MAD
    /// (the codebase's robust σ estimate, 1.483·MAD, via <see cref="MathUtility.MedianMAD"/>); in
    /// <see cref="MeasurementAverageEnum.MeanOutliers"/> mode the center is the mean and the deviation is the sample
    /// standard deviation (sqrt of <see cref="MathUtility.MeanVar"/>). A star is flagged an outlier when its HFR is
    /// more than <see cref="OutlierDeviations"/> deviations from the center on EITHER side.
    /// </summary>
    public static class StarReviewHfrStats {

        /// <summary>How many deviations from the frame center an HFR must be (either side) to be flagged an outlier.</summary>
        public const double OutlierDeviations = 3.0;

        /// <summary>
        /// Computes the frame's HFR center and deviation over the valid (HFR &gt; 0) values, per the averaging mode.
        /// Returns (NaN, NaN) when no valid value exists, and (value, NaN) for a single value (no spread is defined).
        /// </summary>
        public static (double Center, double Deviation) Compute(IEnumerable<double> hfrs, MeasurementAverageEnum mode) {
            var valid = (hfrs ?? Enumerable.Empty<double>()).Where(h => !double.IsNaN(h) && h > 0.0).ToList();
            if (valid.Count == 0) {
                return (double.NaN, double.NaN);
            }
            if (valid.Count == 1) {
                return (valid[0], double.NaN);
            }
            if (mode == MeasurementAverageEnum.MeanOutliers) {
                var (mean, variance) = valid.MeanVar();
                return (mean, Math.Sqrt(variance));
            }
            // Median mode: MedianMAD already scales the MAD by 1.483 into a robust σ estimate.
            var (median, mad) = valid.MedianMAD();
            return (median, mad);
        }

        /// <summary>True when <paramref name="hfr"/> lies more than <see cref="OutlierDeviations"/> deviations from the
        /// center on either side. False when the stats are undefined (NaN) or the deviation is non-positive (e.g. an
        /// all-identical frame, where nothing is an outlier).</summary>
        public static bool IsOutlier(double hfr, double center, double deviation) {
            if (double.IsNaN(hfr) || double.IsNaN(center) || double.IsNaN(deviation) || deviation <= 0.0) {
                return false;
            }
            return Math.Abs(hfr - center) > OutlierDeviations * deviation;
        }

        /// <summary>
        /// Formats the corner overlay caption, e.g. "Median HFR: 2.34 ± 0.12  (n=42)" or "Mean HFR: 2.34 ± 0.12
        /// (n=42)". The "± deviation" term is omitted when the deviation is undefined (a single star), and an empty
        /// string is returned when there are no valid stars (the overlay hides itself).
        /// </summary>
        public static string FormatStats(double center, double deviation, int starCount, MeasurementAverageEnum mode) {
            if (starCount <= 0 || double.IsNaN(center)) {
                return string.Empty;
            }
            var label = mode == MeasurementAverageEnum.MeanOutliers ? "Mean HFR" : "Median HFR";
            var c = center.ToString("F2", CultureInfo.InvariantCulture);
            var dev = double.IsNaN(deviation)
                ? string.Empty
                : " ± " + deviation.ToString("F2", CultureInfo.InvariantCulture);
            return $"{label}: {c}{dev}  (n={starCount})";
        }
    }
}
