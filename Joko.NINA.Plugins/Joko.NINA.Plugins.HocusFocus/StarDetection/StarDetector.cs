using Joko.NINA.Plugins.HocusFocus.Interfaces;
using Joko.NINA.Plugins.HocusFocus.Utility;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiStopWatch = Joko.NINA.Plugins.HocusFocus.Utility.MultiStopWatch;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {
    public class StarDetectorParams {
        public bool HotpixelFiltering { get; set; } = true;

        // Half size in pixels of a Gaussian convolution filter used for noise reduction. This is useful for low-SNR images
        // Setting this value also implies hotpixel filtering is enabled, since otherwise we would blend the hot pixels into their neighbors
        public int NoiseReductionRadius { get; set; } = 0;

        // Number of noise standard deviations above the median to binarize the structure map containing star candidates. Increasing this is useful for noisy images to reduce
        // spurious detected stars in combination with light noise reduction
        public double NoiseClippingMultiplier { get; set; } = 3.0;

        // Half size of a median box filter, used for hotpixel removal if HotpixelFiltering is enabled. Only 1 is supported for now, since OpenCV has native support for
        // a median box filter but not a general circular one
        public int HotpixelFilterRadius { get; set; } = 1;

        // Number of wavelet layers for structure detection
        public int StructureLayers { get; set; } = 5;

        // Size of the circle used to dilate the structure map
        public int StructureDilationSize { get; set; } = 3;

        // Number of times to perform dilation on the structure map
        public int StructureDilationCount { get; set; } = 2;

        // Sensitivity is the minimum value of a star's brightness with respect to its background (s - b)/b. Smaller values increase sensitivity
        public double Sensitivity { get; set; } = 0.1;

        // Maximum ratio of median pixel value to the peak for a candidate pixel to be rejected. Large values are more tolerant of flat structures
        public double PeakResponse { get; set; } = 0.85;

        // Maximum distortion allowed in each star, which is the ratio of "area" (number of pixels) to the area of a perfect square bounding box. A perfect
        // circle has distortion PI/4 which is about 0.8. Smaller values are more distorted
        public double MaxDistortion { get; set; } = 0.5;

        // Stretch factor for the barycenter search algorithm in sigma units. Increasing this makes it more robust against nearby structures,
        // but can be detrimental if too large
        public double BarycenterStretchSigmaUnits { get; set; } = 1.5;

        // The background is estimated by looking in an area around the star bounding box, increased on each side by this number of pixels
        public int BackgroundBoxExpansion { get; set; } = 3;

        // The minimum allowed size (length of either side) of a star candidate's bounding box. Increasing this can be helpful at very high focal lengths if too many small structures
        // are detected
        public int MinimumStarBoundingBoxSize { get; set; } = 5;

        // Minimum HFR for a star to be considered viable
        public double MinHFR { get; set; } = 1.5d;
    }

    public class Star {
        public Point2d Center;
        public Rect StarBoundingBox;
        public double Background;
        public double MeanBrightness;
        public double HFR;
    }

    public class StarDetectorMetrics {
        public int StructureCandidates;
        public int TotalDetected;
        public int TooSmall;
        public int OnBorder;
        public int TooDistorted;
        public int Degenerate;
        public int Saturated;
        public int LowSensitivity;
        public int Uneven;
        public int TooFlat;
        public int TooLowHFR;
        public int HFRAnalysisFailed;
    }

    public class StarDetector : IStarDetector {
        private class StarCandidate {
            public Point2d Center;
            public double CenterBrightness;
            public double Background;
            public double NormalizedBrightness;
            public double TotalFlux;
            public double Peak;
            public int PixelCount;
            public Rect StarBoundingBox;
            public CvImageStatistics StarBoundingBoxStats;
        }

        public Task<List<Star>> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (p.HotpixelFiltering && p.HotpixelFilterRadius != 1) {
                throw new NotImplementedException("Only hotpixel filter radius of 1 currently supported");
            }

            return Task.Run(() => {
                var resourceTracker = new ResourcesTracker();
                try {
                    var metrics = new StarDetectorMetrics();
                    using (var stopWatch = MultiStopWatch.Measure()) {
                        var srcImage = CvImageUtility.ToOpenCVMat(image);
                        stopWatch.RecordEntry("LoadImage");

                        // Step 1: Perform initial noise reduction and hotpixel filtering
                        progress.Report(new ApplicationStatus() { Status = "Noise Reduction" });
                        var hotpixelFilteringApplied = false;
                        if (p.HotpixelFiltering || p.NoiseReductionRadius > 0) {
                            // Apply a median box filter in place to the starting image
                            Cv2.MedianBlur(srcImage, srcImage, 3);
                            hotpixelFilteringApplied = true;
                        }
                        if (p.NoiseReductionRadius > 0) {
                            CvImageUtility.ConvolveGaussian(srcImage, srcImage, p.NoiseReductionRadius * 2 + 1);
                        }

                        // Step 2: Prepare for structure detection by applying hotpixel filtering to a copy if it hasn't been done already
                        progress.Report(new ApplicationStatus() { Status = "Preparing for Structure Detection" });
                        Mat structureMap;
                        if (!hotpixelFilteringApplied) {
                            structureMap = resourceTracker.NewMat();
                            Cv2.MedianBlur(srcImage, structureMap, 3);
                            hotpixelFilteringApplied = true;
                        } else {
                            structureMap = resourceTracker.NewMat();
                            srcImage.CopyTo(structureMap);
                        }
                        stopWatch.RecordEntry("InitialNoiseReduction");

                        // Step 3: Subtract a blur to emphasize edges, and rescale the clipped result to [0, 1)
                        using (var structureMapSrcBlurred = new Mat()) {
                            CvImageUtility.ConvolveGaussian(structureMap, structureMapSrcBlurred, 1 + (1 << p.StructureLayers));
                            CvImageUtility.SubtractInPlace(structureMap, structureMapSrcBlurred);
                        }
                        CvImageUtility.RescaleInPlace(structureMap);

                        // TODO: Consider whether doing this at multiple scales is worth the cost, particularly when out of focus
                        // Step 3: Subtract a blur to emphasize edges at multiple scales, and rescale the clipped result to [0, 1)
                        /*
                        var structureMapEmphasized = resourceTracker.T(MultiScaleEmphasizeEdges(structureMap, 4, 3));
                        structureMap.Dispose();
                        structureMap = structureMapEmphasized;
                        */
                        stopWatch.RecordEntry("EmphasizeEdges");

                        // Step 4: Boost small structures with a dilation box filter
                        if (p.StructureDilationCount > 0) {
                            using (var dilationStructure = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(p.StructureDilationSize, p.StructureDilationSize))) {
                                Cv2.MorphologyEx(structureMap, structureMap, MorphTypes.Dilate, dilationStructure, iterations: p.StructureDilationCount, borderType: BorderTypes.Reflect);
                            }
                            stopWatch.RecordEntry("StructureDilation");
                        }

                        // Step 5: Binarize foreground structures based on noise estimates
                        progress.Report(new ApplicationStatus() { Status = "Structure Detection" });

                        long numBackgroundPixels;
                        var ksigmaNoise = CvImageUtility.KappaSigmaNoiseEstimate(structureMap, out numBackgroundPixels, clippingMultipler: p.NoiseClippingMultiplier);
                        Logger.Trace($"K-Sigma Noise Estimate: {ksigmaNoise}, Background Percentage: {(double)numBackgroundPixels / (structureMap.Rows * structureMap.Cols)}");
                        stopWatch.RecordEntry("KSigmaNoiseCalculation");

                        // Log histograms produce more accurate results due to clustering in very low ADUs, but are substantially more computationally expensive
                        // The difference doesn't seem worth it based on tests done so far
                        var structureMapStats = CvImageUtility.CalculateStatistics_Histogram(structureMap, useLogHistogram: false, flags: CvImageStatisticsFlags.Median);
                        stopWatch.RecordEntry("BinarizationStatistics");

                        double binarizeThreshold = structureMapStats.Median + p.NoiseClippingMultiplier * ksigmaNoise;
                        Logger.Trace($"Structure Map Binarization - Median: {structureMapStats.Median}, Threshold: {binarizeThreshold}");
                        CvImageUtility.Binarize(structureMap, structureMap, binarizeThreshold);
                        stopWatch.RecordEntry("Binarization");

                        // Step 6: Scan structure map for stars
                        progress.Report(new ApplicationStatus() { Status = "Scan and Analyze Stars" });
                        var stars = ScanStars(srcImage, structureMap, p, metrics);
                        stopWatch.RecordEntry("StarAnalysis");

                        Logger.Trace($"Star Detection Metrics. Total={metrics.TotalDetected}, Candidates={metrics.StructureCandidates}, TooSmall={metrics.TooSmall}, OnBorder={metrics.OnBorder}, TooDistorted={metrics.TooDistorted}, Degenerate={metrics.Degenerate}, Saturated={metrics.Saturated}, LowSensitivity={metrics.LowSensitivity}, Uneven={metrics.Uneven}, TooFlat={metrics.TooFlat}, HFRAnalysisFailed={metrics.HFRAnalysisFailed}");

                        return stars;
                    }
                } finally {
                    // Cleanup
                    resourceTracker.Dispose();
                    progress.Report(new ApplicationStatus() { });
                }
            }, token);
        }

        // TODO: Consider whether doing this at multiple scales is worth the cost, particularly when out of focus
        private Mat MultiScaleEmphasizeEdges(Mat src, int firstLayer, int numLayers) {
            if (firstLayer <= 0) {
                throw new ArgumentException("firstLayer must be positive", "firstLayer");
            }
            if (numLayers <= 0) {
                throw new ArgumentException("numLayers must be positive", "numLayers");
            }

            var numPixels = src.Rows * src.Cols;
            Mat result = null;
            try {
                // For each scale layer, subtract a blurred image to emphasize edges. Then take the maximum across all layers and rescale the result
                using (var srcBlurred = new Mat()) {
                    result = Mat.Zeros(src.Size(), src.Type());
                    for (int layer = firstLayer; layer < firstLayer + numLayers; ++layer) {
                        CvImageUtility.ConvolveGaussian(src, srcBlurred, 1 + (1 << layer));
                        unsafe {
                            var lhsData = (float*)src.DataPointer;
                            var rhsData = (float*)srcBlurred.DataPointer;
                            var targetData = (float*)result.DataPointer;
                            for (int i = 0; i < numPixels; ++i) {
                                var value = *(lhsData++) - *(rhsData++);
                                if (value > 0.0f) {
                                    var newValue = (*targetData + value);
                                    if (newValue > 1.0f) {
                                        newValue = 1.0f;
                                    }
                                    *targetData = newValue;
                                }
                                ++targetData;
                            }
                        }
                    }
                    CvImageUtility.RescaleInPlace(result);
                }
                return result;
            } catch (Exception) {
                result?.Dispose();
                throw;
            }
        }

        private bool ComputeHFR(Mat srcImage, Star star) {
            int width = srcImage.Width;
            int height = srcImage.Height;
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                var background = star.Background;
                var starRowStride = width - star.StarBoundingBox.Width;
                double totalBrightness = 0.0;
                double totalWeightedDistance = 0.0;
                double largestRadius = star.Center.X - star.StarBoundingBox.Left;
                largestRadius = Math.Max(largestRadius, star.StarBoundingBox.Right - star.Center.X - 1);
                largestRadius = Math.Max(largestRadius, star.Center.Y - star.StarBoundingBox.Top);
                largestRadius = Math.Max(largestRadius, star.StarBoundingBox.Bottom - star.Center.Y - 1);

                float* p = imageData + (star.StarBoundingBox.Y * width + star.StarBoundingBox.X);
                for (int y = star.StarBoundingBox.Top; y < star.StarBoundingBox.Bottom; ++y) {
                    for (int x = star.StarBoundingBox.Left; x < star.StarBoundingBox.Right; ++x) {
                        var value = *p - background;
                        double distance = 0.0f;
                        if (value > 0.0f) {
                            var dx = x + 0.5d - star.Center.X;
                            var dy = y + 0.5d - star.Center.Y;
                            distance = Math.Sqrt(dx * dx + dy * dy);
                            if (distance <= largestRadius) {
                                totalWeightedDistance += value * distance;
                                totalBrightness += value;
                            }
                        }

                        ++p;
                    }
                    p += starRowStride;
                }

                if (totalBrightness > 0.0f) {
                    star.HFR = totalWeightedDistance / totalBrightness;
                    return true;
                }
                return false;
            }
        }

        private List<Star> ScanStars(Mat srcImage, Mat structureMap, StarDetectorParams p, StarDetectorMetrics metrics) {
            const float ZERO_THRESHOLD = 0.001f;

            var stars = new List<Star>();

            // TODO: Measure performance of allocating a new list for each star vs reusing the same list. Clear doesn't free memory, which is
            //       intentional here
            var starPoints = new List<Point>(1024);
            int width = structureMap.Width;
            int height = structureMap.Height;

            unsafe {
                var structureData = (float*)structureMap.DataPointer;
                for (int yTop = 0, xRight = width - 1, yBottom = height - 1; yTop < yBottom; ++yTop) {
                    for (int xLeft = 0; xLeft < xRight; ++xLeft) {
                        // Skip background and pixels already visited
                        if (structureData[yTop * width + xLeft] < ZERO_THRESHOLD) {
                            continue;
                        }

                        starPoints.Clear();

                        // Grow the star bounding box as we walk around the image, downward and to the right
                        var starBounds = new Rect(xLeft, yTop, 1, 1);

                        for (int y = yTop, x = xLeft; ;) {
                            int rowPointsAdded = 0;
                            if (structureData[y * width + x] >= ZERO_THRESHOLD) {
                                starPoints.Add(new Point(x, y));
                                ++rowPointsAdded;
                            }

                            int rowStartX = x, rowEndX;
                            // Keep adding pixels to the left until you run into a background pixel, but only if the starting pixel belongs to a star
                            if (rowPointsAdded > 0) {
                                for (rowStartX = x; rowStartX > 0;) {
                                    if (structureData[y * width + (rowStartX - 1)] < ZERO_THRESHOLD) {
                                        break;
                                    }
                                    starPoints.Add(new Point(--rowStartX, y));
                                    ++rowPointsAdded;
                                }
                            }

                            // Keep adding pixels to the right until you run into a background pixel
                            for (rowEndX = x; rowEndX < xRight;) {
                                if (structureData[y * width + (rowEndX + 1)] < ZERO_THRESHOLD) {
                                    if (rowPointsAdded > 0 || rowEndX >= starBounds.Right) {
                                        // We're expanding the star search area down and to the right. If we haven't encountered any star pixels on 
                                        // this row yet, we should keep iterating until we find the first one or we've hit the current right boundary of
                                        // star bounds
                                        break;
                                    }
                                    ++rowEndX;
                                } else {
                                    starPoints.Add(new Point(++rowEndX, y));
                                    ++rowPointsAdded;
                                }
                            }

                            if (rowStartX < starBounds.Left) {
                                starBounds.Width += (starBounds.Left - rowStartX);
                                starBounds.X = rowStartX;
                            }
                            if (rowEndX > (starBounds.Right - 1)) {
                                starBounds.Width += (rowEndX - starBounds.Right + 1);
                            }

                            if (rowPointsAdded == 0) {
                                starBounds.Height = y - yTop;
                                break;
                            }
                            if (y == yBottom) {
                                starBounds.Height = y - yTop + 1;
                                break;
                            }

                            // Move onto the next row
                            ++y;
                        }

                        ++metrics.StructureCandidates;
                        var star = EvaluateStarCandidate(srcImage, p, starBounds, starPoints, metrics);
                        if (star != null) {
                            ++metrics.TotalDetected;
                            stars.Add(star);
                        }

                        // Now that we've evaluated the pixels within the star bounding box, we can zero them all out so we don't look again
                        foreach (var starPoint in starPoints) {
                            structureData[starPoint.Y * width + starPoint.X] = 0.0f;
                        }
                    }
                }
            }

            return stars;
        }

        private Star EvaluateStarCandidate(Mat srcImage, StarDetectorParams p, Rect starBounds, List<Point> starPoints, StarDetectorMetrics metrics) {
            // Now we have a potential star bounding box as well as the coordinates of every star pixel. If this is a reliable star,
            // we compute its barycenter and include it.
            //
            // Rejection criteria:
            //  1) Peak values fully saturated
            //  2) Touching the border. We assume the star is clipped
            //  3) Elongated stars
            //  4) Center too far away from the peak pixel
            //  5) Too flat

            // Too small
            if (starBounds.Width < p.MinimumStarBoundingBoxSize || starBounds.Height < p.MinimumStarBoundingBoxSize) {
                ++metrics.TooSmall;
                return null;
            }

            // Touching the border
            if (starBounds.X == 0 || starBounds.Y == 0 || starBounds.Right == srcImage.Width || starBounds.Bottom == srcImage.Height) {
                ++metrics.OnBorder;
                return null;
            }

            // Too distorted
            double d = Math.Max(starBounds.Width, starBounds.Height);
            if ((starPoints.Count / d / d) < p.MaxDistortion) {
                ++metrics.TooDistorted;
                return null;
            }

            var starCandidate = ComputeStarParameters(srcImage, starBounds, p, starPoints);
            // Full saturated
            if (starCandidate == null) {
                ++metrics.Degenerate;
                return null;
            }

            if (starCandidate.Peak >= 1.0f) {
                ++metrics.Saturated;
                return null;
            }

            // Not bright enough relative to background
            var sensitivity = (starCandidate.NormalizedBrightness - starCandidate.Background) / starCandidate.Background;
            if (sensitivity <= p.Sensitivity) {
                ++metrics.LowSensitivity;
                return null;
            }

            // Measured center too far away from being the peak
            if (starCandidate.CenterBrightness < (0.85 * starCandidate.Peak)) {
                ++metrics.Uneven;
                return null;
            }

            // Too flat
            if (starCandidate.StarBoundingBoxStats.Median >= (p.PeakResponse * starCandidate.Peak)) {
                ++metrics.TooFlat;
                return null;
            }

            // Valid candidate!
            var star = new Star() {
                Center = starCandidate.Center,
                Background = starCandidate.Background,
                MeanBrightness = starCandidate.TotalFlux / starCandidate.PixelCount,
                StarBoundingBox = starBounds
            };

            // Measure HFR, and discard if we couldn't calculate it
            if (!ComputeHFR(srcImage, star)) {
                ++metrics.HFRAnalysisFailed;
                return null;
            }

            // HFR below minimum threshold
            if (star.HFR <= p.MinHFR) {
                ++metrics.TooLowHFR;
                return null;
            }

            return star;
        }

        private StarCandidate ComputeStarParameters(Mat srcImage, Rect starBounds, StarDetectorParams p, List<Point> starPoints) {
            var expandedWidth = starBounds.Width + p.BackgroundBoxExpansion * 2;
            var expandedHeight = starBounds.Height + p.BackgroundBoxExpansion * 2;
            var surroundingPixels = new float[(expandedWidth * expandedHeight) - (starBounds.Width * starBounds.Height)];
            int surroundingPixelCount = 0;

            // Search an expanded box to estimate the median background value
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                var backgroundStartY = Math.Max(0, starBounds.Y - p.BackgroundBoxExpansion);
                var backgroundStartX = Math.Max(0, starBounds.X - p.BackgroundBoxExpansion);
                var backgroundEndY = Math.Min(srcImage.Height, starBounds.Bottom + p.BackgroundBoxExpansion);
                var backgroundEndX = Math.Min(srcImage.Width, starBounds.Right + p.BackgroundBoxExpansion);
                var pixelPtr = imageData + backgroundStartY * srcImage.Width + backgroundStartX;

                // Pixel array gap between the end of one row and the beginning of the next
                int rowStrideGap = srcImage.Width - expandedWidth;
                // Top part of box
                for (int y = backgroundStartY; y < starBounds.Y; ++y) {
                    for (int x = backgroundStartX; x < backgroundEndX; ++x) {
                        surroundingPixels[surroundingPixelCount++] = *pixelPtr;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }

                // Center part of box
                for (int y = starBounds.Y; y < starBounds.Bottom; ++y) {
                    for (int x = backgroundStartX; x < starBounds.X; ++x) {
                        surroundingPixels[surroundingPixelCount++] = *pixelPtr;
                        ++pixelPtr;
                    }

                    // Skip the inner hole
                    pixelPtr += starBounds.Width;
                    for (int x = starBounds.Right; x < backgroundEndX; ++x) {
                        surroundingPixels[surroundingPixelCount++] = *pixelPtr;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }

                // Bottom part of box
                for (int y = starBounds.Bottom; y < backgroundEndY; ++y) {
                    for (int x = backgroundStartX; x < backgroundEndX; ++x) {
                        surroundingPixels[surroundingPixelCount++] = *pixelPtr;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }
            }

            Array.Sort(surroundingPixels, 0, surroundingPixelCount);
            var backgroundMedian = surroundingPixels[surroundingPixelCount >> 1];

            var starRectStats = CvImageUtility.CalculateStatistics(srcImage, starBounds, flags: CvImageStatisticsFlags.Median | CvImageStatisticsFlags.StdDev);
            var barycenterLowerBound = Math.Min(1.0f, starRectStats.Median + p.BarycenterStretchSigmaUnits * starRectStats.StdDev);
            double sx = 0, sy = 0, sz = 0;
            double totalFlux = 0d, peak = 0d;
            int numUnclippedPixels = 0;
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                var barycenterStretchFactor = 1.0d - barycenterLowerBound;
                float minPixel = 1.0f, maxPixel = 0.0f;
                foreach (var starPoint in starPoints) {
                    var pixel = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    if (pixel <= barycenterLowerBound) {
                        continue;
                    }

                    ++numUnclippedPixels;
                    if (pixel < minPixel) minPixel = pixel;
                    if (pixel > maxPixel) maxPixel = pixel;
                }

                if (numUnclippedPixels == 1 || maxPixel <= minPixel) {
                    // Degenerate case where surrounding pixels are similar to those within the structure. This seems to happen more often near the border
                    return null;
                }

                foreach (var starPoint in starPoints) {
                    var pixel = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    if (pixel <= barycenterLowerBound) {
                        continue;
                    }

                    var barycenterStretchedPixel = (pixel - minPixel) / (maxPixel - minPixel);
                    sx += barycenterStretchedPixel * starPoint.X;
                    sy += barycenterStretchedPixel * starPoint.Y;
                    sz += barycenterStretchedPixel;
                    totalFlux += pixel;
                    peak = pixel > peak ? pixel : peak;
                }
            }

            var meanFlux = totalFlux / starPoints.Count;
            var center = new Point2d(sx / sz + 0.5, sy / sz + 0.5);
            var centerBrightness = CvImageUtility.BilinearSamplePixelValue(srcImage, y: center.Y, x: center.X);
            return new StarCandidate() {
                Center = center,
                CenterBrightness = (float)centerBrightness,
                Background = backgroundMedian,
                TotalFlux = (float)totalFlux,
                Peak = (float)peak,
                // Detection level for the star's brightness corrected for the peak response
                NormalizedBrightness = (float)(peak - (1 - p.PeakResponse) * meanFlux),
                StarBoundingBox = starBounds,
                StarBoundingBoxStats = starRectStats,
                PixelCount = starPoints.Count
            };
        }
    }
}
