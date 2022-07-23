#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugins.HocusFocus.Utility.CvImageUtility;
using MultiStopWatch = NINA.Joko.Plugins.HocusFocus.Utility.MultiStopWatch;
using System.IO;
using System.Text;
using System.Diagnostics;
using NINA.Image.ImageAnalysis;
using System.Windows.Media;
using NINA.Image.ImageData;
using NINA.Core.Utility.Notification;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

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
            public double StarMedian;
        }

        private static void MaybeSaveIntermediateImage(Mat image, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }

            var targetPath = Path.Combine(p.SaveIntermediateFilesPath, filename);
            image.SaveImage(targetPath);
        }

        private static void MaybeSaveIntermediateText(string text, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }
            File.WriteAllText(Path.Combine(p.SaveIntermediateFilesPath, filename), text);
        }

        private static void MaybeSaveIntermediateStars(List<Star> stars, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }

            var sb = new StringBuilder();
            int index = 0;
            foreach (var star in stars) {
                sb.AppendLine($"{index++} => {star.ToString()}");
            }
            File.WriteAllText(Path.Combine(p.SaveIntermediateFilesPath, filename), sb.ToString());
        }

        public async Task<HocusFocusStarDetectorResult> Detect(Mat srcImage, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var resourceTracker = new ResourcesTracker();
            try {
                return await DetectImpl(srcImage, resourceTracker, p, false, progress, token);
            } finally {
                // Cleanup
                resourceTracker.Dispose();
                progress?.Report(new ApplicationStatus() { });
            }
        }

        public async Task<HocusFocusStarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var resourceTracker = new ResourcesTracker();
            try {
                Mat srcImage;
                var debayeredImage = image as IDebayeredImage;
                var hotpixelFilteringApplied = false;
                long? numHotpixels = null;
                if (debayeredImage != null && p.HotpixelFiltering && p.HotpixelThresholdingEnabled) {
                    var rawImageDataCopy = new ushort[debayeredImage.RawImageData.Data.FlatArray.Length];
                    Buffer.BlockCopy(debayeredImage.RawImageData.Data.FlatArray, 0, rawImageDataCopy, 0, debayeredImage.RawImageData.Data.FlatArray.Length * sizeof(ushort));

                    var props = debayeredImage.RawImageData.Properties;
                    var rawImageData = new RawImageData(rawImageDataCopy, width: props.Width, height: props.Height);

                    var threshold = (ushort)(p.HotpixelThreshold * (1 << props.BitDepth));
                    numHotpixels = HotpixelFiltering.CFAHotpixelFilter(rawImageData, debayeredImage.BayerPattern, threshold);
                    var bitmapSource = ImageUtility.CreateSourceFromArray(new ImageArray(rawImageDataCopy), props, PixelFormats.Gray16);
                    var debayeredImageData = ImageUtility.Debayer(bitmapSource, pf: System.Drawing.Imaging.PixelFormat.Format16bppGrayScale, saveColorChannels: false, saveLumChannel: true, bayerPattern: debayeredImage.BayerPattern);
                    hotpixelFilteringApplied = true;

                    srcImage = resourceTracker.T(CvImageUtility.ToOpenCVMat(debayeredImageData.Data.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height));
                } else {
                    srcImage = resourceTracker.T(CvImageUtility.ToOpenCVMat(image));
                }
                var result = await DetectImpl(srcImage, resourceTracker, p, hotpixelFilteringApplied, progress, token);
                if (numHotpixels.HasValue) {
                    result.Metrics.HotpixelCount = numHotpixels.Value;
                }
                return result;
            } catch (TypeInitializationException e) {
                Logger.Error(e, "TypeInitialization exception while performing star detection. This indicates the OpenCV library couldn't be loaded. If you have a Windows N SKU, install the Media Pack");
                Notification.ShowError("Could ont load the OpenCV library. If you have a Windows N SKU, install the Media Pack");
                throw;
            } finally {
                // Cleanup
                resourceTracker.Dispose();
                progress?.Report(new ApplicationStatus() { });
            }
        }

        private async Task<HocusFocusStarDetectorResult> DetectImpl(Mat srcImage, ResourcesTracker resourceTracker, StarDetectorParams p, bool hotpixelFilterAlreadyApplied, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (p.HotpixelFiltering && p.HotpixelFilterRadius != 1) {
                throw new NotImplementedException("Only hotpixel filter radius of 1 currently supported");
            }

            MaybeSaveIntermediateText(p.ToString(), p, "00-params.txt");
            var metrics = new StarDetectorMetrics();
            using (var stopWatch = MultiStopWatch.Measure()) {
                var debugData = new DebugData();

                Rect? roiRect = null;
                if (p.Region.OuterBoundary.Height < 1.0d || p.Region.OuterBoundary.Width < 1.0d) {
                    roiRect = new Rect(
                        (int)Math.Floor(srcImage.Cols * p.Region.OuterBoundary.StartX),
                        (int)Math.Floor(srcImage.Rows * p.Region.OuterBoundary.StartY),
                        (int)(srcImage.Cols * p.Region.OuterBoundary.Width),
                        (int)(srcImage.Rows * p.Region.OuterBoundary.Height));
                    debugData.DetectionROI = roiRect.Value.ToDrawingRectangle();

                    var roiImage = srcImage.SubMat(roiRect.Value).Clone();
                    srcImage.Dispose();
                    srcImage = roiImage;
                } else {
                    debugData.DetectionROI = new System.Drawing.Rectangle(0, 0, srcImage.Width, srcImage.Height);
                }
                MaybeSaveIntermediateImage(srcImage, p, "01-source.tif");

                if (p.StoreStructureMap) {
                    debugData.StructureMap = new byte[debugData.DetectionROI.Width * debugData.DetectionROI.Height];
                }

                stopWatch.RecordEntry("LoadImage");

                // Step 1: Perform initial noise reduction and hotpixel filtering
                progress?.Report(new ApplicationStatus() { Status = "Noise Reduction" });
                var hotpixelFilteringApplied = hotpixelFilterAlreadyApplied;

                // Also apply hotpixel filtering if noise reduction will be done to the source image
                if (p.HotpixelFiltering || (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled)) {
                    // Apply a median box filter in place to the starting image
                    if (!hotpixelFilterAlreadyApplied) {
                        metrics.HotpixelCount = ApplyHotpixelFilter(srcImage, p);
                    }
                    hotpixelFilteringApplied = true;
                }

                var noiseReductionApplied = false;
                if (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled) {
                    CvImageUtility.ConvolveGaussian(srcImage, srcImage, p.NoiseReductionRadius * 2 + 1);
                    noiseReductionApplied = true;
                }

                MaybeSaveIntermediateImage(srcImage, p, "02-src-image-preparation.tif");
                stopWatch.RecordEntry("SrcImagePreparation");

                // Step 2: Prepare for structure detection by performing optional noise reduction
                progress?.Report(new ApplicationStatus() { Status = "Preparing for Structure Detection" });

                Mat noiseReducedImage = resourceTracker.NewMat();
                if (hotpixelFilteringApplied || noiseReductionApplied || p.NoiseReductionRadius <= 0) {
                    // In this case, we've already applied hotpixel filtering, so no need to do it again. The structure map can start from here
                    srcImage.CopyTo(noiseReducedImage);
                } else {
                    srcImage.CopyTo(noiseReducedImage);
                    metrics.HotpixelCount = ApplyHotpixelFilter(noiseReducedImage, p);
                }

                // Step 3: If we haven't yet applied noise reduction and it is configured, do so now
                if (p.NoiseReductionRadius > 0 && !noiseReductionApplied) {
                    CvImageUtility.ConvolveGaussian(noiseReducedImage, noiseReducedImage, p.NoiseReductionRadius * 2 + 1);
                }

                Mat structureMap = resourceTracker.NewMat();
                noiseReducedImage.CopyTo(structureMap);
                var noiseReducedNoiseEstimateTask = Task.Run(() => {
                    var result = CvImageUtility.KappaSigmaNoiseEstimate(noiseReducedImage, clippingMultipler: p.NoiseClippingMultiplier);
                    var ksigmaTraceStructureMap = $"Structure Map K-Sigma Noise Estimate: {result.Sigma}, Background Mean: {result.BackgroundMean}, NumIterations={result.NumIterations}";
                    Logger.Trace(ksigmaTraceStructureMap);
                    MaybeSaveIntermediateText(ksigmaTraceStructureMap, p, "02-ksigma-estimate-noise-reduced.txt");

                    noiseReducedImage.Dispose();
                    noiseReducedImage = null;
                    return result;
                });

                MaybeSaveIntermediateImage(structureMap, p, "03-structure-map-start.tif");
                stopWatch.RecordEntry("StructureMapPreparation");

                // Step 4: Compute b-spline wavelets to exclude large structures such as nebulae. If the pixel scale is very small or need a wide range for focus, you may need to increase the number of layers
                //         to keep stars from being excluded
                using (var residualLayer = ComputeResidualAtrousB3SplineDyadicWaveletLayer(structureMap, p.StructureLayers)) {
                    MaybeSaveIntermediateImage(residualLayer, p, "04-structure-wavelet-residual.tif");
                    CvImageUtility.SubtractInPlace(structureMap, residualLayer);
                }

                MaybeSaveIntermediateImage(structureMap, p, "04-structure-wavelet-subtracted.tif");
                stopWatch.RecordEntry("WaveletCalculation");

                // Step 5: Excluding large structures can cut off the outsides of large stars, or leave holes when far out of focus. Blurring smooths this out well for structure detection
                CvImageUtility.ConvolveGaussian(structureMap, structureMap, p.StructureLayers * 2 + 1);
                MaybeSaveIntermediateImage(structureMap, p, "05-structure-wavelet-blurred.tif");
                stopWatch.RecordEntry("PostWaveletConvolution");

                // Log histograms produce more accurate results due to clustering in very low ADUs, but are substantially more computationally expensive
                // The difference doesn't seem worth it based on tests done so far
                var structureMapStats = CalculateStatistics_Histogram(structureMap, useLogHistogram: false, flags: CvImageStatisticsFlags.Median);
                stopWatch.RecordEntry("BinarizationStatistics");

                var noiseReducedImageNoise = await noiseReducedNoiseEstimateTask;
                double binarizeThreshold = structureMapStats.Median + p.NoiseClippingMultiplier * noiseReducedImageNoise.Sigma;
                var binarizeTrace = $"Structure Map Binarization - Median: {structureMapStats.Median}, Threshold: {binarizeThreshold}, Clipping Multiplier: {p.NoiseClippingMultiplier}, Noise Sigma: {noiseReducedImageNoise.Sigma}";
                Logger.Trace(binarizeTrace);
                MaybeSaveIntermediateText(structureMapStats.ToString() + Environment.NewLine + binarizeTrace, p, "05-structure-map-statistics.txt");

                if (p.StoreStructureMap) {
                    UpdateStructureMapDebugData(structureMap, debugData.StructureMap, binarizeThreshold, 1);
                }

                // Step 6: Boost small structures with a dilation box filter
                if (p.StructureDilationCount > 0) {
                    using (var dilationStructure = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(p.StructureDilationSize, p.StructureDilationSize))) {
                        Cv2.MorphologyEx(structureMap, structureMap, MorphTypes.Dilate, dilationStructure, iterations: p.StructureDilationCount, borderType: BorderTypes.Reflect);
                    }
                    stopWatch.RecordEntry("StructureDilation");
                    MaybeSaveIntermediateImage(structureMap, p, "06-structure-map-dilated.tif");
                }

                if (p.StoreStructureMap) {
                    UpdateStructureMapDebugData(structureMap, debugData.StructureMap, binarizeThreshold, 2);
                }

                progress?.Report(new ApplicationStatus() { Status = "Structure Detection" });

                // Step 7: Binarize foreground structures based on noise estimates
                CvImageUtility.Binarize(structureMap, structureMap, binarizeThreshold);
                if (p.Region.InnerCropBoundary != null) {
                    var innerRoiRect = new Rect(
                        (int)Math.Floor(srcImage.Cols * p.Region.InnerCropBoundary.StartX),
                        (int)Math.Floor(srcImage.Rows * p.Region.InnerCropBoundary.StartY),
                        (int)(srcImage.Cols * p.Region.InnerCropBoundary.Width),
                        (int)(srcImage.Rows * p.Region.InnerCropBoundary.Height));
                    structureMap.SubMat(innerRoiRect).SetTo(0.0f);
                    Logger.Info($"Clearing structure map for inner ROI: {p.Region.InnerCropBoundary}");
                }

                stopWatch.RecordEntry("Binarization");
                MaybeSaveIntermediateImage(structureMap, p, "07-structure-binarized.tif");

                // Step 8: Scan structure map for stars
                progress?.Report(new ApplicationStatus() { Status = "Scan and Analyze Stars" });
                var stars = ScanStars(srcImage, structureMap, p, noiseReducedImageNoise.Sigma, metrics, token);
                stopWatch.RecordEntry("StarAnalysis");

                // Step 9: Fit PSF models
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                if (p.ModelPSF) {
                    progress?.Report(new ApplicationStatus() { Status = "Modeling PSFs" });
                    await ModelPSF(srcImage, noiseReducedImageNoise.Sigma, stars, p, metrics, token);

                    stopWatch.RecordEntry("ModelPSF");
                }
                stopwatch.Stop();
                Console.WriteLine($"PSF time: {stopwatch.Elapsed}");
                MaybeSaveIntermediateStars(stars, p, "09-detected-stars.txt");

                var metricsTrace = $"Star Detection Metrics. Total={metrics.TotalDetected}, Candidates={metrics.StructureCandidates}, TooSmall={metrics.TooSmall}, OnBorder={metrics.OnBorder}, TooDistorted={metrics.TooDistorted}, Degenerate={metrics.Degenerate}, Saturated={metrics.Saturated}, LowSensitivity={metrics.LowSensitivity}, NotCentered={metrics.NotCentered}, TooFlat={metrics.TooFlat}, HFRAnalysisFailed={metrics.HFRAnalysisFailed}";
                MaybeSaveIntermediateText(metricsTrace, p, "10-detection-metrics.txt");
                Logger.Trace(metricsTrace);
                if (roiRect.HasValue) {
                    // Apply correction for the ROI
                    stars = stars.Select(s => s.AddOffset(xOffset: roiRect.Value.Left, yOffset: roiRect.Value.Top)).ToList();
                    metrics.AddROIOffset(xOffset: roiRect.Value.Left, yOffset: roiRect.Value.Top);
                }

                return new HocusFocusStarDetectorResult() {
                    DetectedStars = stars,
                    Metrics = metrics,
                    DebugData = debugData
                };
            }
        }

        private long ApplyHotpixelFilter(Mat img, StarDetectorParams p) {
            if (p.HotpixelThresholdingEnabled) {
                return HotpixelFiltering.HotpixelFilterWithThresholding(img, p.HotpixelThreshold);
            } else {
                HotpixelFiltering.HotpixelFilter(img);
                return 0L;
            }
        }

        private async Task ModelPSF(Mat srcImage, double noiseSigma, List<Star> stars, StarDetectorParams p, StarDetectorMetrics metrics, CancellationToken ct) {
            var allTasks = new List<Task>();
            Logger.Debug($"Modeling PSFs using a parallel partition size of {p.PSFParallelPartitionSize} and a pixel scale of {p.PixelScale} arcsec/pixel");
            var partitions = p.PSFParallelPartitionSize > 0 ? stars.Partition(p.PSFParallelPartitionSize) : new List<IEnumerable<Star>>() { stars };
            foreach (var detectedStarsPartition in partitions) {
                var psfPartitionTask = Task.Run(() => {
                    foreach (var detectedStar in detectedStarsPartition) {
                        ct.ThrowIfCancellationRequested();

                        var modeler = PSFModeler.Create(
                            fitType: p.PSFFitType,
                            psfResolution: p.PSFResolution,
                            detectedStar: detectedStar,
                            srcImage: srcImage,
                            pixelScale: p.PixelScale,
                            useILNumerics: p.UseILNumerics);
                        PSFModel psf = null;
                        try {
                            psf = PSFModeler.Solve(modeler, useAbsoluteResiduals: p.UsePSFAbsoluteDeviation, noiseSigma: noiseSigma, ct: ct);
                        } catch (Exception) {
                            // Ignore errors and continue
                        }

                        if (psf != null && psf.RSquared >= p.PSFGoodnessOfFitThreshold) {
                            detectedStar.PSF = psf;
                        } else {
                            ++metrics.PSFFitFailed;
                        }
                    }
                }, ct);
                allTasks.Add(psfPartitionTask);
            }

            await Task.WhenAll(allTasks);
        }

        /// <summary>
        /// Updates a byte array containing debug information about a structure map. All pixels exceeding the binarization threshold are set to value in the debug data
        /// </summary>
        /// <param name="structureMap">The image representing the structure map</param>
        /// <param name="structureMapDebugData">The debug data to update</param>
        /// <param name="binarizationThreshold">Binarization threshold</param>
        /// <param name="value">The value to set when pixels exceed the binarization threshold</param>
        private void UpdateStructureMapDebugData(Mat structureMap, byte[] structureMapDebugData, double binarizationThreshold, byte value) {
            var numPixels = structureMap.Rows * structureMap.Cols;
            unsafe {
                var structureData = (float*)structureMap.DataPointer;
                for (int i = 0; i < numPixels; ++i) {
                    var pixel = *(structureData++);
                    if (pixel >= binarizationThreshold && structureMapDebugData[i] == 0) {
                        structureMapDebugData[i] = value;
                    }
                }
            }
        }

        private bool MeasureStar(Mat srcImage, Star star, StarDetectorParams p, double noiseSigma) {
            var background = star.Background;
            double totalBrightness = 0.0;
            double totalWeightedDistance = 0.0;

            // Determine the start position to sample from the star bounding box so that we stay within the box *and* the center point is one of the samples. This ensures
            // we're sampling in a balanced manner around the center
            var startX = star.Center.X - p.AnalysisSamplingSize * Math.Floor((star.Center.X - star.StarBoundingBox.Left) / p.AnalysisSamplingSize);
            var startY = star.Center.Y - p.AnalysisSamplingSize * Math.Floor((star.Center.Y - star.StarBoundingBox.Top) / p.AnalysisSamplingSize);
            var endX = star.StarBoundingBox.Right;
            var endY = star.StarBoundingBox.Bottom;
            var noiseThreshold = p.StarClippingMultiplier * noiseSigma;
            for (var y = startY; y <= endY; y += p.AnalysisSamplingSize) {
                for (var x = startX; x <= endX; x += p.AnalysisSamplingSize) {
                    var value = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background - noiseThreshold;
                    if (value > 0.0f) {
                        var dx = x - star.Center.X;
                        var dy = y - star.Center.Y;
                        var distance = Math.Sqrt(dx * dx + dy * dy);
                        totalWeightedDistance += value * distance;
                        totalBrightness += value;
                    }
                }
            }

            if (totalBrightness > 0.0d) {
                star.HFR = totalWeightedDistance / totalBrightness;
                return true;
            }
            return false;
        }

        private void EvaluateGlobalMetrics(Mat srcImage, StarDetectorParams p, StarDetectorMetrics metrics) {
            int width = srcImage.Width;
            int height = srcImage.Height;
            long numPixels = (long)width * height;
            unsafe {
                var srcImagePixel = (float*)srcImage.DataPointer;
                while (numPixels-- > 0) {
                    if (*srcImagePixel >= p.SaturationThreshold) {
                        ++metrics.SaturatedPixelCount;
                    }
                    srcImagePixel++;
                }
            }
        }

        private List<Star> ScanStars(Mat srcImage, Mat structureMap, StarDetectorParams p, double srcImageNoiseSigma, StarDetectorMetrics metrics, CancellationToken ct) {
            const float ZERO_THRESHOLD = 0.001f;

            var stars = new List<Star>();

            // TODO: Measure performance of allocating a new list for each star vs reusing the same list. Clear doesn't free memory, which is
            //       intentional here
            var starPoints = new List<Point>(1024);
            int width = structureMap.Width;
            int height = structureMap.Height;
            EvaluateGlobalMetrics(srcImage, p, metrics);

            unsafe {
                var structureData = (float*)structureMap.DataPointer;
                for (int yTop = 0, xRight = width - 1, yBottom = height - 1; yTop < yBottom; ++yTop) {
                    ct.ThrowIfCancellationRequested();

                    for (int xLeft = 0; xLeft < xRight; ++xLeft) {
                        // Skip background and pixels already visited
                        if (structureData[yTop * width + xLeft] < ZERO_THRESHOLD) {
                            continue;
                        }

                        starPoints.Clear();

                        // Grow the star bounding box as we walk around the image, downward and to the right
                        var starBounds = new Rect(xLeft, yTop, 1, 1);

                        for (int y = yTop, x = xLeft; ;) {
                            var rowOffsetStart = y * width;
                            int rowPointsAdded = 0;
                            if (structureData[rowOffsetStart + x] >= ZERO_THRESHOLD) {
                                starPoints.Add(new Point(x, y));
                                ++rowPointsAdded;
                            }

                            int rowStartX = x, rowEndX;
                            // Keep adding pixels to the left until you run into a background pixel, but only if the starting pixel belongs to a star
                            if (rowPointsAdded > 0) {
                                for (rowStartX = x; rowStartX > 0;) {
                                    if (structureData[rowOffsetStart + (rowStartX - 1)] < ZERO_THRESHOLD) {
                                        break;
                                    }
                                    starPoints.Add(new Point(--rowStartX, y));
                                    ++rowPointsAdded;
                                }
                            }

                            // Keep adding pixels to the right until you run into a background pixel
                            for (rowEndX = x; rowEndX < xRight;) {
                                if (structureData[rowOffsetStart + (rowEndX + 1)] < ZERO_THRESHOLD) {
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
                        var star = EvaluateStarCandidate(srcImage, p, starBounds, starPoints, srcImageNoiseSigma, metrics);
                        if (star != null) {
                            ++metrics.TotalDetected;
                            stars.Add(star);
                        }

                        // Now that we've evaluated the pixels within the star bounding box, we can zero them all out so we don't look again
                        for (int y = starBounds.Top; y < starBounds.Bottom; ++y) {
                            for (int x = starBounds.Left; x < starBounds.Right; ++x) {
                                structureData[y * width + x] = 0.0f;
                            }
                        }
                    }
                }
            }

            return stars;
        }

        private Star EvaluateStarCandidate(Mat srcImage, StarDetectorParams p, Rect starBounds, List<Point> starPoints, double srcImageNoiseSigma, StarDetectorMetrics metrics) {
            // Now we have a potential star bounding box as well as the coordinates of every star pixel. If this is a reliable star,
            // we compute its barycenter and include it.
            //
            // Rejection criteria:
            //  1) Peak values fully saturated
            //  2) Touching the border. We assume the star is clipped
            //  3) Elongated stars
            //  4) Star center too far away from the center of the bounding box
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
                metrics.TooDistortedBounds.Add(starBounds);
                return null;
            }

            var starCandidate = ComputeStarParameters(srcImage, starBounds, p, srcImageNoiseSigma, starPoints);
            if (starCandidate == null) {
                metrics.DegenerateBounds.Add(starBounds);
                return null;
            }

            // Fully saturated
            if ((starCandidate.Background + starCandidate.Peak) >= p.SaturationThreshold) {
                metrics.SaturatedBounds.Add(starBounds);
                return null;
            }

            // Not bright enough (background already subtracted out) relative to noise level
            var sensitivity = starCandidate.NormalizedBrightness / srcImageNoiseSigma;
            if (sensitivity <= p.Sensitivity) {
                metrics.LowSensitivityBounds.Add(starBounds);
                return null;
            }

            // Measured center too far away from being the peak
            if (!IsStarCentered(starCandidate, p)) {
                metrics.NotCenteredBounds.Add(starBounds);
                return null;
            }

            // Too flat
            if (starCandidate.StarMedian >= (p.PeakResponse * starCandidate.Peak)) {
                metrics.TooFlatBounds.Add(starBounds);
                return null;
            }

            // Valid candidate!
            var star = new Star() {
                Center = starCandidate.Center,
                Background = starCandidate.Background,
                MeanBrightness = starCandidate.TotalFlux / starCandidate.PixelCount,
                StarBoundingBox = starBounds,
                PeakBrightness = starCandidate.Peak
            };

            // Measure HFR, and discard if we couldn't calculate it
            if (!MeasureStar(srcImage, star, p, srcImageNoiseSigma)) {
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

        private static bool IsStarCentered(StarCandidate starCandidate, StarDetectorParams p) {
            var box = starCandidate.StarBoundingBox;
            var centerThresholdBoxWidth = box.Width * p.StarCenterTolerance;
            var centerThresholdBoxHeight = box.Height * p.StarCenterTolerance;
            var minX = box.X + (box.Width - centerThresholdBoxWidth) / 2.0;
            var maxX = minX + centerThresholdBoxWidth;
            var minY = box.Y + (box.Height - centerThresholdBoxHeight) / 2.0;
            var maxY = minY + centerThresholdBoxHeight;
            return starCandidate.Center.X >= minX && starCandidate.Center.X <= maxX && starCandidate.Center.Y >= minY && starCandidate.Center.Y <= maxY;
        }

        private StarCandidate ComputeStarParameters(Mat srcImage, Rect starBounds, StarDetectorParams p, double noiseSigma, List<Point> starPoints) {
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

            var backgroundThreshold = backgroundMedian + p.StarClippingMultiplier * noiseSigma;
            double sx = 0, sy = 0, sz = 0;
            double totalFlux = 0d, peak = 0d;
            int numUnclippedPixels = 0;
            double[] starPixels;
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                float minPixel = 1.0f, maxPixel = 0.0f;
                foreach (var starPoint in starPoints) {
                    var pixel = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    if (pixel <= backgroundThreshold) {
                        continue;
                    }

                    pixel -= backgroundMedian;

                    ++numUnclippedPixels;
                    if (pixel < minPixel) minPixel = pixel;
                    if (pixel > maxPixel) maxPixel = pixel;
                }

                if (numUnclippedPixels == 1 || maxPixel <= minPixel) {
                    // Degenerate case where surrounding pixels are similar to those within the structure. This seems to happen more often near the border
                    return null;
                }

                starPixels = new double[numUnclippedPixels];
                int pixelCount = 0;
                foreach (var starPoint in starPoints) {
                    var pixel = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    if (pixel <= backgroundThreshold) {
                        continue;
                    }

                    pixel -= backgroundMedian;
                    sx += pixel * starPoint.X;
                    sy += pixel * starPoint.Y;
                    sz += pixel;
                    totalFlux += pixel;
                    peak = pixel > peak ? pixel : peak;
                    starPixels[pixelCount++] = pixel;
                }
            }

            Array.Sort(starPixels);
            double starMedian;
            if (starPixels.Length % 2 == 1) {
                starMedian = starPixels[starPixels.Length >> 1];
            } else {
                starMedian = (starPixels[starPixels.Length >> 1 + 1] + starPixels[starPixels.Length >> 1]) / 2.0;
            }

            var meanFlux = totalFlux / starPoints.Count;
            var center = new Point2d(sx / sz, sy / sz);
            var centerBrightness = CvImageUtility.BilinearSamplePixelValue(srcImage, y: center.Y, x: center.X) - backgroundMedian;
            return new StarCandidate() {
                Center = center,
                CenterBrightness = (float)centerBrightness,
                Background = backgroundMedian,
                TotalFlux = (float)totalFlux,
                Peak = (float)peak,
                // Detection level for the star's brightness corrected for the peak response
                NormalizedBrightness = (float)(peak - (1 - p.PeakResponse) * meanFlux),
                StarBoundingBox = starBounds,
                StarMedian = starMedian,
                PixelCount = starPoints.Count
            };
        }
    }
}