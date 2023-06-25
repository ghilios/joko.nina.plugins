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
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using NINA.Core.Interfaces;
using NINA.Core.Utility.Notification;
using NINA.Profile.Interfaces;
using Star = NINA.Joko.Plugins.HocusFocus.Interfaces.Star;
using NINA.WPF.Base.Interfaces;
using Newtonsoft.Json;
using NINA.Equipment.Interfaces.Mediator;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class HocusFocusStarDetectionAnalysis : StarDetectionAnalysis {
        private StarDetectorMetrics metrics;

        public StarDetectorMetrics Metrics {
            get => metrics;
            set {
                if (metrics != value) {
                    metrics = value;
                    RaisePropertyChanged();
                }
            }
        }

        private StarDetectorPSFFitType psfType;

        public StarDetectorPSFFitType PSFType {
            get => psfType;
            set {
                if (psfType != value) {
                    psfType = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double psfRSquared;

        public double PSFRSquared {
            get => psfRSquared;
            set {
                if (psfRSquared != value) {
                    psfRSquared = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double sigma;

        public double Sigma {
            get => sigma;
            set {
                if (sigma != value) {
                    sigma = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double fwhm;

        public double FWHM {
            get => fwhm;
            set {
                if (fwhm != value) {
                    fwhm = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double fwhmMAD;

        public double FWHMMAD {
            get => fwhmMAD;
            set {
                if (fwhmMAD != value) {
                    fwhmMAD = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double eccentricity;

        public double Eccentricity {
            get => eccentricity;
            set {
                if (eccentricity != value) {
                    eccentricity = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double eccentricityMAD;

        public double EccentricityMAD {
            get => eccentricityMAD;
            set {
                if (eccentricityMAD != value) {
                    eccentricityMAD = value;
                    RaisePropertyChanged();
                }
            }
        }

        private MeasurementAverageEnum measurementAverage;

        public MeasurementAverageEnum MeasurementAverage {
            get => measurementAverage;
            set {
                if (measurementAverage != value) {
                    measurementAverage = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double pixelScale;

        public double PixelScale {
            get => pixelScale;
            set {
                if (pixelScale != value) {
                    pixelScale = value;
                    RaisePropertyChanged();
                }
            }
        }
    }

    public class DebugData {

        // 0 = No structure map at that pixel
        // 1 = Original
        // 2 = Dilated
        public byte[] StructureMap;

        public Rectangle DetectionROI;
    }

    public class HocusFocusStarDetectionResult : StarDetectionResult {
        public StarDetectorParams DetectorParams { get; set; }
        public StarDetectorMetrics Metrics { get; set; }
        public HocusFocusDetectionParams HocusFocusParams { get; set; }
        public StarDetectionRegion Region { get; set; }
        public int FocuserPosition { get; set; }

        [JsonIgnore]
        public DebugData DebugData { get; set; }

        public StarDetectorPSFFitType PSFType { get; set; } = StarDetectorPSFFitType.Moffat_40;
        public double PSFRSquared { get; set; } = double.NaN;
        public double Sigma { get; set; } = double.NaN;

        public double FWHM { get; set; } = double.NaN;
        public double FWHMMAD { get; set; } = double.NaN;
        public double Eccentricity { get; set; } = double.NaN;
        public double EccentricityMAD { get; set; } = double.NaN;
        public Size ImageSize { get; set; }
        public double PixelSize { get; set; } = double.NaN;
        public double PixelScale { get; set; } = double.NaN;
        public MeasurementAverageEnum MeasurementAverage { get; set; } = MeasurementAverageEnum.Median;
    }

    public class HocusFocusDetectedStar : DetectedStar {
        public PSFModel PSF { get; set; }

        public override string ToString() {
            return $"{{{nameof(PSF)}={PSF}, {nameof(HFR)}={HFR.ToString()}, {nameof(Position)}={Position.ToString()}, {nameof(AverageBrightness)}={AverageBrightness.ToString()}, {nameof(MaxBrightness)}={MaxBrightness.ToString()}, {nameof(Background)}={Background.ToString()}, {nameof(BoundingBox)}={BoundingBox.ToString()}}}";
        }
    }

    [Export(typeof(IPluggableBehavior))]
    public class HocusFocusStarDetection : IHocusFocusStarDetection {
        private readonly IStarDetector starDetector;
        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IProfileService profileService;
        private readonly IFocuserMediator focuserMediator;
        private bool pixelScaleWarningShown = false;

        public IImageStatisticsVM ImageStatisticsVM { get; private set; }

        public string Name => "Hocus Focus";

        public string ContentId => GetType().FullName;

        [ImportingConstructor]
        public HocusFocusStarDetection(IImageStatisticsVM imageStatisticsVM, IProfileService profileService, IFocuserMediator focuserMediator) :
            this(imageStatisticsVM, profileService, focuserMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.AlglibAPI) {
        }

        public HocusFocusStarDetection(
            IImageStatisticsVM imageStatisticsVM,
            IProfileService profileService,
            IFocuserMediator focuserMediator,
            IStarDetectionOptions starDetectionOptions,
            IAlglibAPI alglibAPI) {
            this.starDetector = new StarDetector(alglibAPI);
            this.starDetectionOptions = starDetectionOptions;
            this.profileService = profileService;
            this.focuserMediator = focuserMediator;
            ImageStatisticsVM = imageStatisticsVM;
        }

        public async Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var selectedAutoFocusBehavior = profileService.ActiveProfile.ApplicationSettings.SelectedPluggableBehaviors.Where(k => k.Key == typeof(IAutoFocusVMFactory).FullName).ToList();
            var ninaStockAutoFocus = selectedAutoFocusBehavior.Count == 0 || selectedAutoFocusBehavior.First().Value == "NINA";
            var isNinaAutoFocus = ninaStockAutoFocus && p.IsAutoFocus;
            if (!starDetectionOptions.UseAutoFocusCrop && !isNinaAutoFocus) {
                p.UseROI = false;
            }

            var starDetectionRegion = StarDetectionRegion.FromStarDetectionParams(p);
            var detectorParams = GetStarDetectorParams(image, starDetectionRegion, p.IsAutoFocus);
            var hocusFocusParams = ToHocusFocusParams(p);

            var detectionResult = await Detect(image, hocusFocusParams, detectorParams, progress, token);
            detectionResult.Params = p;
            return detectionResult;
        }

        public HocusFocusDetectionParams ToHocusFocusParams(StarDetectionParams p) {
            return new HocusFocusDetectionParams() {
                HighSigmaOutlierRejection = p.Sensitivity == StarSensitivityEnum.Normal ? 3.0d : 4.0d,
                LowSigmaOutlierRejection = 3.0d,
                MatchStarPositions = p.MatchStarPositions,
                NumberOfAFStars = p.NumberOfAFStars,
                IsAutoFocus = p.IsAutoFocus
            };
        }

        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var binning = Math.Max(image.RawImageData.MetaData.Camera.BinX, 1);
            var pixelScale = MathUtility.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize, profileService.ActiveProfile.TelescopeSettings.FocalLength) * binning;
            if (double.IsNaN(pixelScale)) {
                if (!pixelScaleWarningShown) {
                    pixelScaleWarningShown = true;
                    Notification.ShowWarning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
                }
                Logger.Warning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
            }

            var detectorParams = new StarDetectorParams() {
                ModelPSF = starDetectionOptions.ModelPSF,
                StarMeasurementNoiseReductionEnabled = starDetectionOptions.StarMeasurementNoiseReductionEnabled,
                PSFFitType = starDetectionOptions.PSFFitType,
                HotpixelFiltering = starDetectionOptions.HotpixelFiltering,
                HotpixelThresholdingEnabled = starDetectionOptions.HotpixelThresholdingEnabled,
                NoiseReductionRadius = starDetectionOptions.NoiseReductionRadius,
                NoiseClippingMultiplier = starDetectionOptions.NoiseClippingMultiplier,
                StarClippingMultiplier = starDetectionOptions.StarClippingMultiplier,
                StructureLayers = starDetectionOptions.StructureLayers,
                Sensitivity = starDetectionOptions.BrightnessSensitivity,
                PeakResponse = starDetectionOptions.StarPeakResponse,
                MaxDistortion = starDetectionOptions.MaxDistortion,
                StarCenterTolerance = starDetectionOptions.StarCenterTolerance,
                BackgroundBoxExpansion = starDetectionOptions.StarBackgroundBoxExpansion,
                MinimumStarBoundingBoxSize = starDetectionOptions.MinStarBoundingBoxSize,
                MinHFR = starDetectionOptions.MinHFR,
                StructureDilationSize = starDetectionOptions.StructureDilationSize,
                StructureDilationCount = starDetectionOptions.StructureDilationCount,
                AnalysisSamplingSize = (float)starDetectionOptions.PixelSampleSize,
                StoreStructureMap = starDetectionOptions.DebugMode,
                SaveIntermediateFilesPath = starDetectionOptions.SaveIntermediateImages ? starDetectionOptions.IntermediateSavePath : string.Empty,
                PixelScale = pixelScale,
                PSFParallelPartitionSize = starDetectionOptions.PSFParallelPartitionSize,
                PSFResolution = starDetectionOptions.PSFResolution,
                PSFGoodnessOfFitThreshold = starDetectionOptions.PSFFitThreshold,
                Region = starDetectionRegion,
                UsePSFAbsoluteDeviation = starDetectionOptions.UsePSFAbsoluteDeviation,
                HotpixelThreshold = starDetectionOptions.HotpixelThreshold,
                SaturationThreshold = starDetectionOptions.SaturationThreshold
            };

            // For AutoFocus, don't save intermediate data or model PSFs
            if (isAutoFocus) {
                detectorParams.SaveIntermediateFilesPath = string.Empty;
                detectorParams.ModelPSF = false;
            } else {
                // Only save intermediate images for 1 detection. Doing this again should require the user to pick it again
                starDetectionOptions.SaveIntermediateImages = false;
            }
            return detectorParams;
        }

        public async Task<StarDetectionResult> Detect(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var binX = double.IsNaN(image.RawImageData.MetaData.Camera.BinX) ? 1 : image.RawImageData.MetaData.Camera.BinX;
            var metadataPixelSize = double.IsNaN(image.RawImageData.MetaData.Camera.PixelSize) ? 3.76 : image.RawImageData.MetaData.Camera.PixelSize;
            var pixelSize = metadataPixelSize * Math.Max(binX, 1);
            var imageSize = new Size(width: image.RawImageData.Properties.Width, height: image.RawImageData.Properties.Height);
            var result = new HocusFocusStarDetectionResult() {
                HocusFocusParams = hocusFocusParams,
                DetectorParams = detectorParams,
                ImageSize = imageSize,
                Region = detectorParams.Region,
                FocuserPosition = focuserMediator.GetInfo().Position,
                PixelSize = pixelSize,
                PixelScale = detectorParams.PixelScale,
                MeasurementAverage = this.starDetectionOptions.MeasurementAverage
            };
            var starDetectorResult = await this.starDetector.Detect(image, detectorParams, progress, token);
            if (!string.IsNullOrEmpty(detectorParams.SaveIntermediateFilesPath)) {
                Notification.ShowInformation("Saved intermediate star detection files");
                Logger.Info($"Saved intermediate star detection files to {detectorParams.SaveIntermediateFilesPath}");
            }

            var starList = starDetectorResult.DetectedStars;

            if (!detectorParams.Region.IsFull() && detectorParams.Region.InnerCropBoundary != null) {
                var innerRegion = detectorParams.Region.InnerCropBoundary.ToRectangle(imageSize);
                var before = starList.Count;
                starList = starList.Where(s => OutsideROI(s, innerRegion)).ToList();
                var outsideRoi = before - starList.Count;
                if (outsideRoi > 0) {
                    starDetectorResult.Metrics.OutsideROI = outsideRoi;
                }
            }

            if (starList.Count > 1 && this.starDetectionOptions.MeasurementAverage == MeasurementAverageEnum.MeanOutliers) {
                int countBefore = starList.Count;

                // Now that we have a properly filtered star list, let's compute stats and further filter out from the average
                // Median and MAD are used as they are more robust to outliers
                var (hfrMedian, hfrMAD) = starList.Select(s => s.HFR).MedianMAD();
                starList = starList.Where(s => s.HFR <= hfrMedian + hocusFocusParams.HighSigmaOutlierRejection * hfrMAD && s.HFR >= hfrMedian - hocusFocusParams.LowSigmaOutlierRejection * hfrMAD).ToList<Star>();

                int countAfter = starList.Count;
                Logger.Trace($"Discarded {countBefore - countAfter} outlier stars");
            }

            if (detectorParams.ModelPSF) {
                var psfStarList = starList.Where(s => s.PSF != null).ToList();
                if (psfStarList.Count > 1) {
                    result.PSFType = detectorParams.PSFFitType;

                    var (sigma, _) = psfStarList.Select(s => s.PSF.Sigma).MedianMAD();
                    result.Sigma = sigma;

                    var (psfRSquared, _) = psfStarList.Select(s => s.PSF.RSquared).MedianMAD();
                    result.PSFRSquared = psfRSquared;

                    var (fwhm, fwhmMAD) = psfStarList.Select(s => s.PSF.FWHMArcsecs).MedianMAD();
                    result.FWHM = fwhm;
                    result.FWHMMAD = fwhmMAD;

                    var (eccentricity, eccentricityMAD) = psfStarList.Select(s => s.PSF.Eccentricity).MedianMAD();
                    result.Eccentricity = eccentricity;
                    result.EccentricityMAD = eccentricityMAD;
                }
            }

            result.DetectedStars = starList.Count;
            if (hocusFocusParams.NumberOfAFStars > 0) {
                if (starList.Count != 0 && (hocusFocusParams.MatchStarPositions == null || hocusFocusParams.MatchStarPositions.Count == 0)) {
                    if (starList.Count > hocusFocusParams.NumberOfAFStars) {
                        starList = starList.OrderByDescending(s => s.HFR * 0.3 + s.MeanBrightness * 0.7).Take(hocusFocusParams.NumberOfAFStars).ToList<Star>();
                    }
                    result.BrightestStarPositions = starList.Select(s => s.Center.ToAccordPoint()).ToList();
                } else { // find the closest stars to the brightest stars previously identified
                    var topStars = new List<Star>();
                    hocusFocusParams.MatchStarPositions.ForEach(pos => topStars.Add(starList.Aggregate((min, next) => min.Center.ToAccordPoint().DistanceTo(pos) < next.Center.ToAccordPoint().DistanceTo(pos) ? min : next)));
                    starList = topStars;
                }
            }

            result.StarList = starList.Select(s => ToDetectedStar(s)).ToList();
            if (starList.Count > 1) {
                if (this.starDetectionOptions.MeasurementAverage == MeasurementAverageEnum.MeanOutliers) {
                    result.AverageHFR = starList.Average(s => s.HFR);
                    var hfrVariance = starList.Sum(s => (s.HFR - result.AverageHFR) * (s.HFR - result.AverageHFR)) / (starList.Count - 1);
                    result.HFRStdDev = Math.Sqrt(hfrVariance);

                    Logger.Info($"Average HFR: {result.AverageHFR}, HFR σ: {result.HFRStdDev}, Detected Stars {result.StarList.Count}, Region: {result?.Region.Index ?? 0}");
                } else {
                    var (hfrMedian, hfrMAD) = starList.Select(s => s.HFR).MedianMAD();
                    result.AverageHFR = hfrMedian;
                    result.HFRStdDev = hfrMAD;

                    Logger.Info($"Average HFR: {result.AverageHFR}, HFR MAD: {result.HFRStdDev}, Detected Stars {result.StarList.Count}, Region: {result?.Region.Index ?? 0}");
                }
            }
            result.DebugData = starDetectorResult.DebugData;
            result.Metrics = starDetectorResult.Metrics;
            return result;
        }

        public static DetectedStar ToDetectedStar(Star star) {
            return new HocusFocusDetectedStar() {
                HFR = star.HFR,
                Position = star.Center.ToAccordPoint(),
                AverageBrightness = star.MeanBrightness,
                MaxBrightness = star.PeakBrightness,
                Background = star.Background,
                BoundingBox = star.StarBoundingBox.ToDrawingRectangle(),
                PSF = star.PSF
            };
        }

        private static bool OutsideROI(Star detectedStar, Rectangle innerCropBoundary) {
            var starRectangle = detectedStar.StarBoundingBox.ToDrawingRectangle();
            if (innerCropBoundary.Contains(starRectangle) || innerCropBoundary.IntersectsWith(starRectangle)) {
                return false;
            }
            return true;
        }

        public IStarDetectionAnalysis CreateAnalysis() {
            return new HocusFocusStarDetectionAnalysis();
        }

        public void UpdateAnalysis(IStarDetectionAnalysis analysis, StarDetectionParams p, StarDetectionResult result) {
            var hocusFocusAnalysis = (HocusFocusStarDetectionAnalysis)analysis;
            var hocusFocusResult = (HocusFocusStarDetectionResult)result;
            hocusFocusAnalysis.HFR = result.AverageHFR;
            hocusFocusAnalysis.HFRStDev = result.HFRStdDev;
            hocusFocusAnalysis.DetectedStars = result.DetectedStars;
            hocusFocusAnalysis.Metrics = hocusFocusResult.Metrics;
            hocusFocusAnalysis.PSFType = hocusFocusResult.PSFType;
            hocusFocusAnalysis.PSFRSquared = hocusFocusResult.PSFRSquared;
            hocusFocusAnalysis.Sigma = hocusFocusResult.Sigma;
            hocusFocusAnalysis.FWHM = hocusFocusResult.FWHM;
            hocusFocusAnalysis.FWHMMAD = hocusFocusResult.FWHMMAD;
            hocusFocusAnalysis.Eccentricity = hocusFocusResult.Eccentricity;
            hocusFocusAnalysis.EccentricityMAD = hocusFocusResult.EccentricityMAD;
            hocusFocusAnalysis.MeasurementAverage = hocusFocusResult.MeasurementAverage;
            hocusFocusAnalysis.PixelScale = hocusFocusResult.PixelScale;
        }
    }
}