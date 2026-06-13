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
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
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
        private readonly IAlglibAPI alglibAPI;

        // Allocated in DetectImpl only when StarDetectorParams.CollectContaminationDiagnostics is set. Star
        // scanning is parallel, so accepted-star diagnostic records are collected thread-safely here and then
        // materialized into an ordered list before DetectImpl returns. Null in normal (non-diagnostic) runs.
        private ConcurrentBag<ContaminationDiagnosticRecord> contaminationDiagnosticsBag;

        public StarDetector(IAlglibAPI alglibAPI) {
            this.alglibAPI = alglibAPI;
        }

        private class StarCandidate {
            public Point2d Center;
            public double CenterBrightness;
            public double Background;
            public LocalBackgroundPlane BackgroundPlane;
            public double NormalizedBrightness;
            public double TotalFlux;
            public double Peak;
            public int PixelCount;
            public Rect StarBoundingBox;
            public double StarMedian;
            public bool ContaminationSuspected;

            // Contamination diagnostics carry fields, populated in ComputeStarParameters only when
            // StarDetectorParams.CollectContaminationDiagnostics is true. Null otherwise (zero overhead).
            public ContaminationDiagnosticRecord ContaminationDiagnostics;
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
            contaminationDiagnosticsBag = p.CollectContaminationDiagnostics ? new ConcurrentBag<ContaminationDiagnosticRecord>() : null;
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

                // F4 (σ consistency): thresholds applied to the image actually sampled must use that image's σ.
                // The estimate above is computed on the (possibly blurred) structure-map source; when a noise
                // reduction radius is set but measurement noise reduction is off, srcImage was never blurred and
                // its white-noise σ is ~4x larger. Measure it directly on srcImage (rather than applying an
                // analytic kernel factor) so correlated real-camera noise and hotpixel filtering are accounted
                // for automatically. srcImage is read-only from here until this task is awaited (before binarization), so the concurrent read is safe.
                var measurementImageDiffers = p.NoiseReductionRadius > 0 && !noiseReductionApplied;
                var measurementNoiseEstimateTask = measurementImageDiffers
                    ? Task.Run(() => {
                        var result = CvImageUtility.KappaSigmaNoiseEstimate(srcImage, clippingMultipler: p.NoiseClippingMultiplier);
                        var ksigmaTraceMeasurement = $"Measurement Image K-Sigma Noise Estimate: {result.Sigma}, Background Mean: {result.BackgroundMean}, NumIterations={result.NumIterations}";
                        Logger.Trace(ksigmaTraceMeasurement);
                        MaybeSaveIntermediateText(ksigmaTraceMeasurement, p, "02-ksigma-estimate-measurement.txt");
                        return result;
                    })
                    : null;

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
                var measurementImageNoise = measurementNoiseEstimateTask != null
                    ? await measurementNoiseEstimateTask
                    : noiseReducedImageNoise;
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
                var stars = ScanStars(srcImage, structureMap, p, measurementImageNoise.Sigma, metrics, token);
                stopWatch.RecordEntry("StarAnalysis");

                // Step 9: Fit PSF models
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                if (p.ModelPSF) {
                    progress?.Report(new ApplicationStatus() { Status = "Modeling PSFs" });
                    await ModelPSF(srcImage, measurementImageNoise.Sigma, stars, p, metrics, token);

                    stopWatch.RecordEntry("ModelPSF");
                }
                stopwatch.Stop();
                Console.WriteLine($"PSF time: {stopwatch.Elapsed}");
                MaybeSaveIntermediateStars(stars, p, "09-detected-stars.txt");

                var metricsTrace = $"Star Detection Metrics. Total={metrics.TotalDetected}, Candidates={metrics.StructureCandidates}, TooSmall={metrics.TooSmall}, OnBorder={metrics.OnBorder}, TooDistorted={metrics.TooDistorted}, Degenerate={metrics.Degenerate}, SaturatedMasked={metrics.Saturated}, LowSensitivity={metrics.LowSensitivity}, NotCentered={metrics.NotCentered}, TooFlat={metrics.TooFlat}, HFRAnalysisFailed={metrics.HFRAnalysisFailed}";
                MaybeSaveIntermediateText(metricsTrace, p, "10-detection-metrics.txt");
                Logger.Trace(metricsTrace);

                // Log structured rejection breakdown for debugging
                int totalCandidates = metrics.StructureCandidates;
                int accepted = metrics.TotalDetected;
                int rejected = totalCandidates - accepted;
                var rejectionLog = $"Star detection complete: Found={accepted}, Rejected={rejected}, TooSmall={metrics.TooSmall}, OnBorder={metrics.OnBorder}, TooFlat={metrics.TooFlat}, TooDistorted={metrics.TooDistorted}, Saturated(masked)={metrics.Saturated}, LowSensitivity={metrics.LowSensitivity}, OffCenter={metrics.NotCentered}, HFRFailed={metrics.HFRAnalysisFailed}, PSFFailed={metrics.PSFFitFailed}, Degenerate={metrics.Degenerate}, TooLowHFR={metrics.TooLowHFR}, ContaminationSuspected={metrics.ContaminationSuspected}";
                Logger.Debug(rejectionLog);
                if (roiRect.HasValue) {
                    // Apply correction for the ROI
                    stars = stars.Select(s => s.AddOffset(xOffset: roiRect.Value.Left, yOffset: roiRect.Value.Top)).ToList();
                    metrics.AddROIOffset(xOffset: roiRect.Value.Left, yOffset: roiRect.Value.Top);
                }

                // Materialize the (parallel-collected) contamination diagnostics, mirroring the ROI offset
                // applied to DetectedStars and ordering deterministically by image position so the CSV is stable.
                List<ContaminationDiagnosticRecord> contaminationDiagnostics = null;
                if (contaminationDiagnosticsBag != null) {
                    contaminationDiagnostics = contaminationDiagnosticsBag.ToList();
                    if (roiRect.HasValue) {
                        foreach (var rec in contaminationDiagnostics) {
                            rec.CenterX += roiRect.Value.Left;
                            rec.CenterY += roiRect.Value.Top;
                        }
                    }
                    contaminationDiagnostics = contaminationDiagnostics.OrderBy(r => r.CenterY).ThenBy(r => r.CenterX).ToList();
                    contaminationDiagnosticsBag = null;
                    Logger.Trace($"Collected {contaminationDiagnostics.Count} contamination diagnostic records ({contaminationDiagnostics.Count(r => r.ContaminationSuspected)} suspected)");
                }

                return new HocusFocusStarDetectorResult() {
                    DetectedStars = stars,
                    Metrics = metrics,
                    DebugData = debugData,
                    ContaminationDiagnostics = contaminationDiagnostics,
                    StructureNoiseSigma = noiseReducedImageNoise.Sigma,
                    MeasurementNoiseSigma = measurementImageNoise.Sigma
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
                            alglibAPI: alglibAPI,
                            fitType: p.PSFFitType,
                            psfResolution: p.PSFResolution,
                            detectedStar: detectedStar,
                            srcImage: srcImage,
                            pixelScale: p.PixelScale,
                            saturationThreshold: p.SaturationThreshold,
                            pixelIntegration: p.PSFPixelIntegration);
                        PSFModel psf = null;
                        if (modeler != null) {
                            try {
                                psf = PSFModeler.Solve(modeler, useAbsoluteResiduals: p.UsePSFAbsoluteDeviation, noiseSigma: noiseSigma, ct: ct);
                            } catch (Exception) {
                                // Ignore errors and continue
                            }
                        }

                        bool psfAccepted = false;
                        if (psf != null) {
                            // R² must be at or above the threshold
                            psfAccepted = psf.RSquared >= p.PSFGoodnessOfFitThreshold;
                        }
                        if (psfAccepted) {
                            detectedStar.PSF = psf;
                        } else {
                            ++metrics.PSFFitFailed;
                        }
                    }
                }, ct);
                allTasks.Add(psfPartitionTask);
            }

            var tcs = new TaskCompletionSource();
            using (ct.Register(() => tcs.SetCanceled(), useSynchronizationContext: false)) {
                await Task.WhenAny(Task.WhenAll(allTasks), tcs.Task);
            }
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

        /// <summary>
        /// Result of the local-background analysis: a robust background plane fit (always attempted) plus the
        /// gradient-robust contamination decision (only when sensitivity &gt; 0). Scalar fields are always set;
        /// the per-sector residual arrays are populated only when <c>fillSectors</c> is requested (diagnostics).
        /// </summary>
        internal struct GradientContaminationResult {
            public bool PlaneValid;               // true when B0/B1/B2 are a usable robust fit
            public double B0;                     // background at the annulus origin (dx = dy = 0)
            public double B1;                     // d(background)/dx
            public double B2;                     // d(background)/dy
            public bool Suspected;
            public double GradientSlope;
            public double LocalSigmaResidual;
            public double MaxSectorResidualOverSE;
            public int TrippingSector;
            public double[] SectorResidualMedian; // null unless fillSectors
            public int[] SectorResidualCount;     // null unless fillSectors
        }

        /// <summary>
        /// Fits a robust local background plane (b0 + b1*dx + b2*dy) to the annulus pixels via IRLS with Huber
        /// weights to model a smooth one-sided background (galaxy/nebula gradient), and runs the gradient-robust
        /// contamination test on the plane-subtracted residuals: it flags only a one-sided POSITIVE residual
        /// excess in a sector — a contaminant adds light, so this ignores both smooth gradients (removed by the
        /// fit) and edge-clipping deficits (negative residuals). The plane is fit whenever there are enough
        /// non-degenerate points (so it can serve as the local background even when the contamination test is
        /// disabled); the contamination decision additionally requires <paramref name="sensitivity"/> &gt; 0.
        /// </summary>
        internal static GradientContaminationResult ComputeGradientContamination(
                float[] dx, float[] dy, float[] val, int n,
                double fallbackSigma, double sensitivity, int minSectorPixels, bool fillSectors) {
            const int numSectors = 8;
            var result = new GradientContaminationResult {
                PlaneValid = false,
                B0 = double.NaN, B1 = double.NaN, B2 = double.NaN,
                Suspected = false,
                GradientSlope = double.NaN,
                LocalSigmaResidual = double.NaN,
                MaxSectorResidualOverSE = double.NaN,
                TrippingSector = -1
            };
            if (fillSectors) {
                result.SectorResidualMedian = new double[numSectors];
                result.SectorResidualCount = new int[numSectors];
                for (int s = 0; s < numSectors; ++s) result.SectorResidualMedian[s] = double.NaN;
            }

            // Too few points to fit a 3-parameter plane with margin, or null input → no plane, not suspected.
            if (dx == null || n < 12) {
                return result;
            }

            // Robust plane fit via iteratively reweighted least squares (Huber).
            double b0 = 0, b1 = 0, b2 = 0;
            var w = new double[n];
            for (int i = 0; i < n; ++i) w[i] = 1.0;
            const double huberC = 1.345;
            for (int iter = 0; iter < 4; ++iter) {
                if (!SolveWeightedPlane(dx, dy, val, w, n, out b0, out b1, out b2)) {
                    return result; // singular / collinear geometry → no usable plane
                }
                var absr = new double[n];
                for (int i = 0; i < n; ++i) absr[i] = Math.Abs(val[i] - (b0 + b1 * dx[i] + b2 * dy[i]));
                double sigma = 1.4826 * MedianInPlace(absr);
                if (sigma <= 0) break; // residuals essentially zero; fit is exact
                for (int i = 0; i < n; ++i) {
                    double z = Math.Abs(val[i] - (b0 + b1 * dx[i] + b2 * dy[i])) / sigma;
                    w[i] = z <= huberC ? 1.0 : huberC / z;
                }
            }

            result.PlaneValid = true;
            result.B0 = b0;
            result.B1 = b1;
            result.B2 = b2;
            result.GradientSlope = Math.Sqrt(b1 * b1 + b2 * b2);

            var absResid = new double[n];
            for (int i = 0; i < n; ++i) absResid[i] = Math.Abs(val[i] - (b0 + b1 * dx[i] + b2 * dy[i]));
            double localSigma = 1.4826 * MedianInPlace(absResid);
            if (localSigma <= 0) localSigma = fallbackSigma;
            result.LocalSigmaResidual = localSigma;

            // Contamination decision (only when enabled).
            if (sensitivity <= 0 || localSigma <= 0) {
                return result;
            }
            var bySector = new List<double>[numSectors];
            for (int s = 0; s < numSectors; ++s) bySector[s] = new List<double>();
            for (int i = 0; i < n; ++i) {
                bySector[OctantOf(dx[i], dy[i])].Add(val[i] - (b0 + b1 * dx[i] + b2 * dy[i]));
            }
            double maxStat = double.NaN;
            for (int s = 0; s < numSectors; ++s) {
                int count = bySector[s].Count;
                if (fillSectors) result.SectorResidualCount[s] = count;
                if (count == 0) continue;
                bySector[s].Sort();
                double med = bySector[s][count >> 1];
                if (fillSectors) result.SectorResidualMedian[s] = med;
                if (count < minSectorPixels) continue;
                double se = 1.2533 * localSigma / Math.Sqrt(count);
                if (se <= 0) continue;
                double stat = med / se; // one-sided: positive excess over the local plane
                if (double.IsNaN(maxStat) || stat > maxStat) maxStat = stat;
                if (med > sensitivity * se && result.TrippingSector < 0) {
                    result.TrippingSector = s;
                }
            }
            result.MaxSectorResidualOverSE = maxStat;
            result.Suspected = result.TrippingSector >= 0;
            return result;
        }

        // Solves the weighted least-squares plane val ≈ b0 + b1*dx + b2*dy. Returns false if the normal
        // matrix is singular (collinear / degenerate annulus geometry).
        private static bool SolveWeightedPlane(float[] dx, float[] dy, float[] val, double[] w, int n,
                out double b0, out double b1, out double b2) {
            double sw = 0, swx = 0, swy = 0, swxx = 0, swxy = 0, swyy = 0, swv = 0, swxv = 0, swyv = 0;
            for (int i = 0; i < n; ++i) {
                double wi = w[i], x = dx[i], y = dy[i], v = val[i];
                sw += wi; swx += wi * x; swy += wi * y;
                swxx += wi * x * x; swxy += wi * x * y; swyy += wi * y * y;
                swv += wi * v; swxv += wi * x * v; swyv += wi * y * v;
            }
            var a = new double[3, 3] { { sw, swx, swy }, { swx, swxx, swxy }, { swy, swxy, swyy } };
            var g = new double[3] { swv, swxv, swyv };
            b0 = b1 = b2 = 0;
            // Gaussian elimination with partial pivoting.
            for (int col = 0; col < 3; ++col) {
                int piv = col;
                for (int r = col + 1; r < 3; ++r) if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
                if (Math.Abs(a[piv, col]) < 1e-12) return false;
                if (piv != col) {
                    for (int c = 0; c < 3; ++c) { var t = a[col, c]; a[col, c] = a[piv, c]; a[piv, c] = t; }
                    var tg = g[col]; g[col] = g[piv]; g[piv] = tg;
                }
                for (int r = 0; r < 3; ++r) {
                    if (r == col) continue;
                    double f = a[r, col] / a[col, col];
                    for (int c = col; c < 3; ++c) a[r, c] -= f * a[col, c];
                    g[r] -= f * g[col];
                }
            }
            b0 = g[0] / a[0, 0];
            b1 = g[1] / a[1, 1];
            b2 = g[2] / a[2, 2];
            return true;
        }

        // Median of an array, sorting it in place (caller passes a scratch array it owns).
        private static double MedianInPlace(double[] a) {
            if (a.Length == 0) return 0;
            Array.Sort(a);
            return a[a.Length >> 1];
        }

        /// <summary>
        /// Assigns a (dx, dy) offset to one of 8 angular octants such that octant s and octant s+4
        /// are geometrically opposite. Uses sign/magnitude branches to avoid a per-pixel atan2.
        /// </summary>
        internal static int OctantOf(double dx, double dy) {
            if (dx >= 0) {
                if (dy >= 0) {
                    return Math.Abs(dx) >= Math.Abs(dy) ? 0 : 1;
                }
                return Math.Abs(dx) >= Math.Abs(dy) ? 7 : 6;
            }
            if (dy >= 0) {
                return Math.Abs(dx) >= Math.Abs(dy) ? 3 : 2;
            }
            return Math.Abs(dx) >= Math.Abs(dy) ? 4 : 5;
        }

        /// <summary>
        /// Computes the star's HFR — the flux-weighted mean radius over a circular aperture, sampled
        /// on an AnalysisSamplingSize grid through the centroid with per-pixel background-plane
        /// subtraction. Saturated pixels are NOT masked here (unlike the PSF fit, which is off during
        /// AF): a flat saturated core under-weights the center, biasing HFR high — most likely near
        /// focus. No correction is attempted because HFR is an empirical flux sum (masking core pixels
        /// would bias it further); median aggregation across stars limits the damage, and the
        /// Saturated metric tracks exposure (accuracy analysis F14, decided document-only).
        /// </summary>
        internal bool MeasureStar(Mat srcImage, Star star, StarDetectorParams p, double noiseSigma) {
            // Subtract the local background plane per pixel so a one-sided gradient does not bias HFR. Fall back
            // to a flat plane at the scalar background if no plane is available.
            var backgroundPlane = star.BackgroundPlane ?? LocalBackgroundPlane.Flat(star.Center.X, star.Center.Y, star.Background);
            double totalBrightness = 0.0;
            double totalWeightedDistance = 0.0;

            // Circular aperture radius — same convention as centroid refinement
            var apertureRadius = Math.Min(star.StarBoundingBox.Width, star.StarBoundingBox.Height) / 2.0;

            // Determine the start position to sample from the star bounding box so that we stay within the box *and* the center point is one of the samples. This ensures
            // we're sampling in a balanced manner around the center
            var startX = star.Center.X - p.AnalysisSamplingSize * Math.Floor((star.Center.X - star.StarBoundingBox.Left) / p.AnalysisSamplingSize);
            var startY = star.Center.Y - p.AnalysisSamplingSize * Math.Floor((star.Center.Y - star.StarBoundingBox.Top) / p.AnalysisSamplingSize);
            var endX = star.StarBoundingBox.Right;
            var endY = star.StarBoundingBox.Bottom;
            var noiseThreshold = p.StarClippingMultiplier * noiseSigma;
            for (var y = startY; y <= endY; y += p.AnalysisSamplingSize) {
                for (var x = startX; x <= endX; x += p.AnalysisSamplingSize) {
                    var dx = x - star.Center.X;
                    var dy = y - star.Center.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    // Exclude pixels entirely outside the circular aperture
                    if (distance > apertureRadius + 0.5) {
                        continue;
                    }

                    var background = backgroundPlane.ValueAt(x, y);
                    var flux = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background;
                    if (flux > noiseThreshold) {
                        // SubtractTau preserves the legacy soft-threshold (flux − τ): identical gating, but wing
                        // pixels lose relative weight, biasing HFR low on radial gradients (F3). GateOnly keeps
                        // the full flux of surviving pixels, matching the centroid's gate-only convention.
                        var value = p.HfrTauPolicy == TauClipPolicy.SubtractTau ? flux - noiseThreshold : flux;
                        // Apply partial-pixel weighting at the aperture boundary (linear interpolation)
                        var apertureWeight = 1.0 - Math.Max(0.0, distance - (apertureRadius - 0.5));
                        totalWeightedDistance += apertureWeight * value * distance;
                        totalBrightness += apertureWeight * value;
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
            //  1) Touching the border. We assume the star is clipped
            //  2) Elongated stars
            //  3) Star center too far away from the center of the bounding box
            //  4) Too flat
            // Note: partially-saturated stars (Background + Peak >= SaturationThreshold) are no longer rejected here.
            // Instead, they are passed to PSF fitting which masks saturated pixels during the fit.

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

            // Track partially-saturated stars in metrics (clipped pixels will be masked during PSF fit instead of rejecting)
            if ((starCandidate.Background + starCandidate.Peak) >= p.SaturationThreshold) {
                metrics.SaturatedBounds.Add(starBounds);
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

            // Too flat. Intentionally active during AutoFocus as well — see
            // HocusFocusStarDetection.GetStarDetectorParams (accuracy analysis F1).
            if (starCandidate.StarMedian >= (p.PeakResponse * starCandidate.Peak)) {
                metrics.TooFlatBounds.Add(starBounds);
                return null;
            }

            // Valid candidate!
            var star = new Star() {
                Center = starCandidate.Center,
                Background = starCandidate.Background,
                BackgroundPlane = starCandidate.BackgroundPlane,
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

            star.StarContaminationSuspected = starCandidate.ContaminationSuspected;
            if (star.StarContaminationSuspected) {
                // Record the bounds regardless of whether the star is rejected, so the annotator can mark
                // contaminated stars even when RejectContaminatedStars removes them from the detected set.
                metrics.ContaminatedBounds.Add(starBounds);
                // Quality gate: reject contaminated stars so HFR/PSF statistics stay clean of one-sided
                // contaminants. Disabled (flag-only) when RejectContaminatedStars is off (e.g. diagnostics).
                if (p.RejectContaminatedStars) {
                    return null;
                }
            }

            if (p.CollectContaminationDiagnostics && starCandidate.ContaminationDiagnostics != null && contaminationDiagnosticsBag != null) {
                var record = starCandidate.ContaminationDiagnostics;
                record.CenterX = star.Center.X;
                record.CenterY = star.Center.Y;
                record.Hfr = star.HFR;
                record.Background = star.Background;
                contaminationDiagnosticsBag.Add(record);
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

        /// <summary>
        /// Estimates a robust local background scatter (sigma) from a background-annulus sample, used as the
        /// noise scale for the contamination asymmetry test instead of the global image-noise floor. Near
        /// bright stars and in gradients/nebulosity the local background roughness is several times the global
        /// noise, so a bar built on the global sigma is far too tight and flags ordinary stars.
        ///
        /// Uses the median absolute deviation (MAD), whose 50% breakdown point means a single contaminated
        /// sector (≤ 1/8 of the annulus) cannot inflate it enough to hide itself, while smooth gradients that
        /// lift every sector are correctly absorbed into the bar. <paramref name="sortedPixels"/> must be sorted
        /// ascending over [0, <paramref name="count"/>). Returns the consistency-corrected sigma (1.4826 × MAD),
        /// or 0 if the sample is too small or perfectly flat — signaling the caller to fall back to the global
        /// noise sigma.
        /// </summary>
        internal static double ComputeLocalBackgroundSigma(float[] sortedPixels, int count, double median) {
            const int MinPixelsForLocalSigma = 8;
            if (sortedPixels == null || count < MinPixelsForLocalSigma) {
                return 0.0;
            }
            var deviations = new double[count];
            for (int i = 0; i < count; ++i) {
                deviations[i] = Math.Abs(sortedPixels[i] - median);
            }
            Array.Sort(deviations);
            var mad = ComputeMedian(deviations);
            return 1.4826 * mad;
        }

        /// <summary>
        /// Computes the median of a pre-sorted array of doubles.
        /// For an even-length array, returns the average of the two middle elements.
        /// </summary>
        internal static double ComputeMedian(double[] sortedPixels) {
            if (sortedPixels.Length == 0) {
                throw new ArgumentException("Array must not be empty", nameof(sortedPixels));
            }
            if (sortedPixels.Length % 2 == 1) {
                return sortedPixels[sortedPixels.Length >> 1];
            } else {
                return (sortedPixels[(sortedPixels.Length >> 1) - 1] + sortedPixels[sortedPixels.Length >> 1]) / 2.0;
            }
        }

        /// <summary>
        /// Computes an iterative flux-weighted centroid over a list of candidate star pixels.
        /// Pass 1 includes every pixel above <paramref name="backgroundThreshold"/>.
        /// Each subsequent pass restricts the contributing pixels to those that lie within
        /// a circle of radius <paramref name="apertureRadius"/> centered on the centroid
        /// estimate from the previous pass.  The circular aperture prevents bright
        /// off-axis pixels from biasing the centroid for asymmetric or tilted stars.
        /// </summary>
        /// <param name="imageData">Pointer to the flat float32 pixel array (unsafe).</param>
        /// <param name="imageWidth">Number of pixels per row in the image.</param>
        /// <param name="starPoints">Candidate pixels to consider (from the structure map).</param>
        /// <param name="backgroundPlane">Local background plane subtracted (per pixel) from each value.</param>
        /// <param name="clipMargin">Pixels must exceed plane + this margin to be included (clipping band).</param>
        /// <param name="apertureRadius">Circular aperture radius used from pass 2 onward.</param>
        /// <param name="numPasses">Total number of centroid passes (2 or 3 recommended).</param>
        /// <returns>
        /// Flux-weighted centroid in image-pixel coordinates, or a fallback centre-of-bounding-box
        /// value if no pixel survives the threshold cuts.
        /// </returns>
        internal static unsafe Point2d ComputeIterativeCentroid(
            float* imageData,
            int imageWidth,
            List<Point> starPoints,
            LocalBackgroundPlane backgroundPlane,
            double clipMargin,
            double apertureRadius,
            int numPasses) {

            // Seed the centroid with an unrestricted flux-weighted pass (pass 1).
            double sx = 0, sy = 0, sz = 0;
            foreach (var pt in starPoints) {
                var pixel = imageData[pt.Y * imageWidth + pt.X];
                var background = backgroundPlane.ValueAt(pt.X, pt.Y);
                if (pixel <= background + clipMargin) {
                    continue;
                }
                var flux = pixel - background;
                sx += flux * pt.X;
                sy += flux * pt.Y;
                sz += flux;
            }

            if (sz <= 0) {
                // No pixels above threshold — return the unweighted bounding-box centre
                // (caller can decide to discard this candidate).
                if (starPoints.Count == 0) {
                    return new Point2d(0, 0);
                }
                double sumX = 0, sumY = 0;
                foreach (var pt in starPoints) { sumX += pt.X; sumY += pt.Y; }
                return new Point2d(sumX / starPoints.Count, sumY / starPoints.Count);
            }

            var cx = sx / sz;
            var cy = sy / sz;

            // Passes 2..numPasses: restrict to circular aperture around previous estimate.
            var r2 = apertureRadius * apertureRadius;
            for (int pass = 2; pass <= numPasses; ++pass) {
                sx = 0; sy = 0; sz = 0;
                foreach (var pt in starPoints) {
                    var pixel = imageData[pt.Y * imageWidth + pt.X];
                    var background = backgroundPlane.ValueAt(pt.X, pt.Y);
                    if (pixel <= background + clipMargin) {
                        continue;
                    }
                    var dx = pt.X - cx;
                    var dy = pt.Y - cy;
                    if (dx * dx + dy * dy > r2) {
                        continue;
                    }
                    var flux = pixel - background;
                    sx += flux * pt.X;
                    sy += flux * pt.Y;
                    sz += flux;
                }

                if (sz <= 0) {
                    // Aperture excluded everything — keep the previous estimate
                    break;
                }
                cx = sx / sz;
                cy = sy / sz;
            }

            return new Point2d(cx, cy);
        }

        private StarCandidate ComputeStarParameters(Mat srcImage, Rect starBounds, StarDetectorParams p, double noiseSigma, List<Point> starPoints) {
            var expandedWidth = starBounds.Width + p.BackgroundBoxExpansion * 2;
            var expandedHeight = starBounds.Height + p.BackgroundBoxExpansion * 2;
            var surroundingPixels = new float[(expandedWidth * expandedHeight) - (starBounds.Width * starBounds.Height)];
            int surroundingPixelCount = 0;

            const int MinSectorPixels = 8;
            var cx = starBounds.X + starBounds.Width / 2.0;
            var cy = starBounds.Y + starBounds.Height / 2.0;

            // Retain each background-annulus pixel's (dx, dy, value) relative to the bounding-box center so the
            // robust background plane (used as the local background for centroid/flux/HFR/PSF) and the
            // gradient-robust contamination test can be computed below.
            float[] annDx = new float[surroundingPixels.Length];
            float[] annDy = new float[surroundingPixels.Length];
            float[] annVal = new float[surroundingPixels.Length];

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
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }

                // Center part of box
                for (int y = starBounds.Y; y < starBounds.Bottom; ++y) {
                    for (int x = backgroundStartX; x < starBounds.X; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Skip the inner hole
                    pixelPtr += starBounds.Width;
                    for (int x = starBounds.Right; x < backgroundEndX; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }

                // Bottom part of box
                for (int y = starBounds.Bottom; y < backgroundEndY; ++y) {
                    for (int x = backgroundStartX; x < backgroundEndX; ++x) {
                        var bgPixel = *pixelPtr;
                        int annIdx = surroundingPixelCount++;
                        surroundingPixels[annIdx] = bgPixel;
                        annDx[annIdx] = (float)(x - cx); annDy[annIdx] = (float)(y - cy); annVal[annIdx] = bgPixel;
                        ++pixelPtr;
                    }

                    // Move to the start of the next row
                    pixelPtr += rowStrideGap;
                }
            }

            Array.Sort(surroundingPixels, 0, surroundingPixelCount);
            var backgroundMedian = surroundingPixels[surroundingPixelCount >> 1];

            // Robust local background scatter from the annulus pixels themselves, used as the noise scale for
            // the contamination test. The global noiseSigma is the image-noise floor; near bright stars and in
            // gradients/nebulosity the real local roughness is several times larger, so feeding the global value
            // to the standard-error model makes the bar far too tight. Fall back to the global sigma when the
            // sample is too small or perfectly flat (helper returns 0).
            var localBackgroundSigma = ComputeLocalBackgroundSigma(surroundingPixels, surroundingPixelCount, backgroundMedian);
            var contaminationSigma = localBackgroundSigma > 0.0 ? localBackgroundSigma : noiseSigma;

            // Fit the robust local background plane and run the gradient-robust contamination test in one pass.
            var gr = ComputeGradientContamination(annDx, annDy, annVal, surroundingPixelCount,
                contaminationSigma, p.ContaminationSensitivity, MinSectorPixels, fillSectors: p.CollectContaminationDiagnostics);
            var contaminationSuspected = gr.Suspected;

            // Local background model: the fitted plane when usable, else a flat plane at the annulus median.
            // Used as the per-pixel background for clipping, flux, centroid, HFR and PSF so a one-sided gradient
            // (galaxy/nebula) does not bias any measurement; for flat fields the plane equals the median.
            var backgroundPlane = gr.PlaneValid
                ? new LocalBackgroundPlane(cx, cy, gr.B0, gr.B1, gr.B2, isFlat: false)
                : LocalBackgroundPlane.Flat(cx, cy, backgroundMedian);

            var clipMargin = p.StarClippingMultiplier * noiseSigma;
            double totalFlux = 0d, peak = 0d;
            int numUnclippedPixels = 0;
            double[] starPixels;
            unsafe {
                var imageData = (float*)srcImage.DataPointer;
                float minPixel = 1.0f, maxPixel = 0.0f;
                foreach (var starPoint in starPoints) {
                    var raw = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    var background = backgroundPlane.ValueAt(starPoint.X, starPoint.Y);
                    if (raw <= background + clipMargin) {
                        continue;
                    }

                    var pixel = (float)(raw - background);

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
                    var raw = imageData[starPoint.Y * srcImage.Width + starPoint.X];
                    var background = backgroundPlane.ValueAt(starPoint.X, starPoint.Y);
                    if (raw <= background + clipMargin) {
                        continue;
                    }

                    var pixel = raw - background;
                    totalFlux += pixel;
                    peak = pixel > peak ? pixel : peak;
                    starPixels[pixelCount++] = pixel;
                }
            }

            Array.Sort(starPixels);
            var starMedian = ComputeMedian(starPixels);

            var meanFlux = totalFlux / starPoints.Count;

            // Compute an iterative centroid: start with all threshold-clipped pixels, then on
            // subsequent passes restrict to pixels within a circular aperture centered on the
            // previous estimate.  2-3 passes are enough for sub-pixel convergence.
            var apertureRadius = Math.Min(starBounds.Width, starBounds.Height) / 2.0;
            Point2d center;
            unsafe {
                center = ComputeIterativeCentroid(
                    imageData: (float*)srcImage.DataPointer,
                    imageWidth: srcImage.Width,
                    starPoints: starPoints,
                    backgroundPlane: backgroundPlane,
                    clipMargin: clipMargin,
                    apertureRadius: apertureRadius,
                    numPasses: 3);
            }
            var backgroundAtCenter = backgroundPlane.ValueAt(center.X, center.Y);
            var centerBrightness = CvImageUtility.BilinearSamplePixelValue(srcImage, y: center.Y, x: center.X) - backgroundAtCenter;

            ContaminationDiagnosticRecord contaminationDiagnostics = null;
            if (p.CollectContaminationDiagnostics) {
                contaminationDiagnostics = new ContaminationDiagnosticRecord() {
                    NoiseSigma = contaminationSigma,
                    Sensitivity = p.ContaminationSensitivity,
                    MinSectorPixels = MinSectorPixels,
                    ContaminationSuspected = contaminationSuspected,
                    GradientSlope = gr.GradientSlope,
                    LocalSigmaResidual = gr.LocalSigmaResidual,
                    SectorResidualMedian = gr.SectorResidualMedian,
                    SectorResidualCount = gr.SectorResidualCount,
                    MaxSectorResidualOverSE = gr.MaxSectorResidualOverSE,
                    ResidualTrippingSector = gr.TrippingSector
                    // CenterX/CenterY/Hfr/Background are filled later in CreateStar.
                };
            }

            return new StarCandidate() {
                Center = center,
                CenterBrightness = (float)centerBrightness,
                Background = backgroundAtCenter,
                BackgroundPlane = backgroundPlane,
                TotalFlux = (float)totalFlux,
                Peak = (float)peak,
                // Detection level for the star's brightness corrected for the peak response
                NormalizedBrightness = (float)(peak - (1 - p.PeakResponse) * meanFlux),
                StarBoundingBox = starBounds,
                StarMedian = starMedian,
                PixelCount = starPoints.Count,
                ContaminationSuspected = contaminationSuspected,
                ContaminationDiagnostics = contaminationDiagnostics
            };
        }
    }
}
