#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp {

    /// <summary>
    /// Mat-based detection helpers for the TILT harness only. <c>optimize</c> and <c>bank-verify</c> no longer use
    /// them: they carry the <c>IRenderedImage</c> the live app detects on and drive the plugin's own
    /// <c>RunEvaluationLoader.HocusFocusSplitFrameDetector</c>, so their mapping IS the wizard's rather than a copy
    /// of it. What remains here is the last mirror, kept alive by <c>TiltCalibrationRunner</c>, whose Mat-based
    /// pipeline (with its own explicit <c>--debayer</c> opt-in) has not been migrated.
    ///
    /// <para>Because this path detects on whatever Mat it is handed, a BAYERED frame reaches the detector as a raw
    /// Bayer mosaic unless the caller debayered it at load time — which is not what the live app does (it CFA
    /// hotpixel filters and debayers inside <c>Detect</c>, at the caller's params). Treat tilt results on bayered
    /// runs accordingly.</para>
    /// </summary>
    internal static class HarnessDetection {

        /// <summary>
        /// Adapts a <see cref="HocusFocusStarDetectorResult"/> into the optimizer's <see cref="FrameDetectionResult"/>,
        /// REPLICATING the HFR aggregation in <c>HocusFocusStarDetection.Detect</c> AS THE WIZARD'S
        /// <c>RunEvaluationLoader</c> drives it — NumberOfAFStars=0 (every accepted star scored) with the sigma
        /// rejections at the <c>HocusFocusDetectionParams</c> class defaults (high=4.0/low=3.0):
        ///   - MeanOutliers: re-filter to HFR ∈ [median − low·MAD, median + high·MAD], then AverageHFR = mean,
        ///     HFRStdDev = sample std-dev (n-1).
        ///   - Median (default): AverageHFR = median, HFRStdDev = MAD.
        /// Both only when the surviving list has &gt; 1 star; otherwise AverageHFR/HFRStdDev stay 0. StarCount and
        /// StarCenters come from the SURVIVING accepted set.
        /// </summary>
        public static FrameDetectionResult ToFrameDetectionResult(
            HocusFocusStarDetectorResult result, MeasurementAverageEnum measurementAverage,
            double highSigmaOutlierRejection, double lowSigmaOutlierRejection, System.Drawing.Size fullImageSize,
            bool excludeSaturatedFromHfr = true, double saturationThreshold = 0.99) {
            var stars = result.DetectedStars ?? new List<Star>();

            if (stars.Count > 1 && measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                var (median, mad) = stars.Select(s => s.HFR).MedianMAD();
                var hi = median + highSigmaOutlierRejection * mad;
                var lo = median - lowSigmaOutlierRejection * mad;
                stars = stars.Where(s => s.HFR <= hi && s.HFR >= lo).ToList();
            }

            // HFR aggregation over the saturated-filtered subset (mirrors HocusFocusStarDetection.BuildStarDetectionResult):
            // a saturated bright star stays counted/in StarCenters/StarHFRs but is kept out of the curve point.
            var hfrStars = HocusFocusStarDetection.StarsForHfrAggregation(stars, excludeSaturatedFromHfr, saturationThreshold);
            double averageHfr = 0.0;
            double hfrStdDev = 0.0;
            if (hfrStars.Count > 1) {
                if (measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                    averageHfr = hfrStars.Average(s => s.HFR);
                    var variance = hfrStars.Sum(s => (s.HFR - averageHfr) * (s.HFR - averageHfr)) / (hfrStars.Count - 1);
                    hfrStdDev = Math.Sqrt(variance);
                } else {
                    var (hfrMedian, hfrMAD) = hfrStars.Select(s => s.HFR).MedianMAD();
                    averageHfr = hfrMedian;
                    hfrStdDev = hfrMAD;
                }
            }

            var centers = stars.Select(s => (s.Center.X, s.Center.Y)).ToList();
            return new FrameDetectionResult {
                AverageHFR = averageHfr,
                HFRStdDev = hfrStdDev,
                StarCount = stars.Count,
                StarCenters = centers,
                // Per-star HFRs PARALLEL to centers (same surviving set) for the extreme-HFR outlier penalty.
                StarHFRs = stars.Select(s => s.HFR).ToList(),
                ImageWidth = fullImageSize.Width,
                ImageHeight = fullImageSize.Height,
                RelaxationAdmittedCount = stars.Count(s => s.RelaxationAdmitted)
            };
        }
    }

    /// <summary>
    /// Mat-based split detector for the TILT harness: caches the expensive early detection per (frame, early key)
    /// and reuses it across candidate evaluations that change only late-stage gate params. The result mapping is
    /// <see cref="HarnessDetection.ToFrameDetectionResult"/>, a copy of the wizard loader's. optimize/bank-verify use
    /// the plugin's <c>HocusFocusSplitFrameDetector</c> directly instead.
    /// </summary>
    internal sealed class MatSplitFrameDetector : RunEvaluationData.ISplitFrameDetector {
        private readonly StarDetector detector;
        private readonly MeasurementAverageEnum measurementAverage;
        private readonly double highSigma;
        private readonly double lowSigma;

        public MatSplitFrameDetector(StarDetector detector, MeasurementAverageEnum measurementAverage, double highSigma, double lowSigma) {
            this.detector = detector;
            this.measurementAverage = measurementAverage;
            this.highSigma = highSigma;
            this.lowSigma = lowSigma;
        }

        public string ComputeEarlyKey(StarDetectorParams p) => StarDetector.ComputeEarlyCacheKey(p);

        public async Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token) {
            return await detector.BuildDetectionContext((Mat)frameImage, p, null, token).ConfigureAwait(false);
        }

        public FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p) {
            var ctx = (StarDetector.DetectionContext)context;
            var result = detector.GateAndMeasure(ctx, p, CancellationToken.None);
            return HarnessDetection.ToFrameDetectionResult(result, measurementAverage, highSigma, lowSigma, ctx.FullImageSize,
                p.ExcludeSaturatedStarsFromHFR, p.SaturationThreshold);
        }
    }
}
