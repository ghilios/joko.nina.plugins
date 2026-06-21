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
    /// Shared headless detection helpers used by the offline harnesses (optimize + tilt). Keeps the HFR
    /// aggregation that mirrors the wizard's <see cref="RunEvaluationLoader"/> and the Mat-based split detector
    /// in one place so the two runners can never drift from each other or from production.
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
            double highSigmaOutlierRejection, double lowSigmaOutlierRejection) {
            var stars = result.DetectedStars ?? new List<Star>();

            if (stars.Count > 1 && measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                var (median, mad) = stars.Select(s => s.HFR).MedianMAD();
                var hi = median + highSigmaOutlierRejection * mad;
                var lo = median - lowSigmaOutlierRejection * mad;
                stars = stars.Where(s => s.HFR <= hi && s.HFR >= lo).ToList();
            }

            double averageHfr = 0.0;
            double hfrStdDev = 0.0;
            if (stars.Count > 1) {
                if (measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                    averageHfr = stars.Average(s => s.HFR);
                    var variance = stars.Sum(s => (s.HFR - averageHfr) * (s.HFR - averageHfr)) / (stars.Count - 1);
                    hfrStdDev = Math.Sqrt(variance);
                } else {
                    var (hfrMedian, hfrMAD) = stars.Select(s => s.HFR).MedianMAD();
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
                RelaxationAdmittedCount = stars.Count(s => s.RelaxationAdmitted)
            };
        }
    }

    /// <summary>
    /// Mat-based split detector for the offline harnesses: caches the expensive early detection per (frame, early
    /// key) and reuses it across candidate evaluations that change only late-stage gate params. The result mapping
    /// is <see cref="HarnessDetection.ToFrameDetectionResult"/>, identical to the wizard's loader.
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
            var result = detector.GateAndMeasure((StarDetector.DetectionContext)context, p, CancellationToken.None);
            return HarnessDetection.ToFrameDetectionResult(result, measurementAverage, highSigma, lowSigma);
        }
    }
}
