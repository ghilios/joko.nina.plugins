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
        public DebugData DebugData { get; set; }
        public double FWHM { get; set; } = double.NaN;
        public double FWHMMAD { get; set; } = double.NaN;
        public double Eccentricity { get; set; } = double.NaN;
        public double EccentricityMAD { get; set; } = double.NaN;
    }

    public class HocusFocusDetectedStar : DetectedStar {
        public PSFModel PSF { get; set; }
    }

    [Export(typeof(IPluggableBehavior))]
    public class HocusFocusStarDetection : IStarDetection {
        private readonly IStarDetector starDetector;
        private readonly StarDetectionOptions starDetectionOptions;
        private readonly IProfileService profileService;

        public IImageStatisticsVM ImageStatisticsVM { get; private set; }

        public string Name => "Hocus Focus";

        public string ContentId => GetType().FullName;

        [ImportingConstructor]
        public HocusFocusStarDetection(IImageStatisticsVM imageStatisticsVM, IProfileService profileService) {
            this.starDetector = new StarDetector();
            this.starDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            this.profileService = profileService;
            ImageStatisticsVM = imageStatisticsVM;
        }

        public async Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var binning = Math.Max(image.RawImageData.MetaData.Camera.BinX, 1);
            var pixelScale = MathUtility.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize, profileService.ActiveProfile.TelescopeSettings.FocalLength) * binning;
            var detectorParams = new StarDetectorParams() {
                ModelPSF = starDetectionOptions.ModelPSF,
                PSFFitType = starDetectionOptions.PSFFitType,
                HotpixelFiltering = starDetectionOptions.HotpixelFiltering,
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
                PSFResolution = starDetectionOptions.PSFResolution
            };

            // Only save intermediate images for 1 detection. Doing this again should require the user to pick it again
            starDetectionOptions.SaveIntermediateImages = false;

            if (p.UseROI && p.InnerCropRatio < 1.0 && p.OuterCropRatio > 0.0) {
                detectorParams.CenterROICropRatio = p.OuterCropRatio >= 1.0 ? p.InnerCropRatio : p.OuterCropRatio;
                if (p.OuterCropRatio < 1.0) {
                    detectorParams.InnerROICropRatio = p.InnerCropRatio / p.OuterCropRatio;
                }
            }

            var result = new HocusFocusStarDetectionResult() { Params = p, DetectorParams = detectorParams };
            var starDetectorResult = await this.starDetector.Detect(image, detectorParams, progress, token);
            if (!string.IsNullOrEmpty(detectorParams.SaveIntermediateFilesPath)) {
                Notification.ShowInformation("Saved intermediate star detection files");
                Logger.Info($"Saved intermediate star detection files to {detectorParams.SaveIntermediateFilesPath}");
            }

            var imageSize = new Size(width: image.RawImageData.Properties.Width, height: image.RawImageData.Properties.Height);
            var starList = starDetectorResult.DetectedStars;
            if (!starDetectionOptions.UseAutoFocusCrop && !p.IsAutoFocus) {
                p.UseROI = false;
            }

            if (p.UseROI) {
                var before = starList.Count;
                starList = starList.Where(s => InROI(s, imageSize, p)).ToList();
                var outsideRoi = before - starList.Count;
                if (outsideRoi > 0) {
                    starDetectorResult.Metrics.OutsideROI = outsideRoi;
                }
            }

            if (starList.Count > 1) {
                int countBefore = starList.Count;

                // Now that we have a properly filtered star list, let's compute stats and further filter out from the mean
                var hfrMean = starList.Average(s => s.HFR);
                var hfrVariance = starList.Sum(s => (s.HFR - hfrMean) * (s.HFR - hfrMean)) / (starList.Count - 1);
                var hfrStdDev = Math.Sqrt(hfrVariance);
                if (p.Sensitivity == StarSensitivityEnum.Normal) {
                    starList = starList.Where(s => s.HFR <= hfrMean + 3.0 * hfrStdDev && s.HFR >= hfrMean - 3.0 * hfrStdDev).ToList<Star>();
                } else {
                    // More sensitivity means getting fainter and smaller stars, and maybe some noise, skewing the distribution towards low hfr. Let's be more permissive towards the large star end.
                    starList = starList.Where(s => s.HFR <= hfrMean + 4 * hfrStdDev && s.HFR >= hfrMean - 3.0 * hfrStdDev).ToList<Star>();
                }

                int countAfter = starList.Count;
                Logger.Trace($"Discarded {countBefore - countAfter} outlier stars");
            }

            if (detectorParams.ModelPSF) {
                var psfStarList = starList.Where(s => s.PSF != null).ToList();
                if (psfStarList.Count > 1) {
                    var (fwhm, fwhmMAD) = psfStarList.Select(s => s.PSF.FWHMArcsecs).MedianMAD();
                    result.FWHM = fwhm;
                    result.FWHMMAD = fwhmMAD;

                    var (eccentricity, eccentricityMAD) = psfStarList.Select(s => s.PSF.Eccentricity).MedianMAD();
                    result.Eccentricity = eccentricity;
                    result.EccentricityMAD = eccentricityMAD;
                }
            }

            result.DetectedStars = starList.Count;
            if (p.NumberOfAFStars > 0) {
                if (starList.Count != 0 && (p.MatchStarPositions == null || p.MatchStarPositions.Count == 0)) {
                    if (starList.Count > p.NumberOfAFStars) {
                        starList = starList.OrderByDescending(s => s.HFR * 0.3 + s.MeanBrightness * 0.7).Take(p.NumberOfAFStars).ToList<Star>();
                    }
                    result.BrightestStarPositions = starList.Select(s => s.Center.ToAccordPoint()).ToList();
                } else { // find the closest stars to the brightest stars previously identified
                    var topStars = new List<Star>();
                    p.MatchStarPositions.ForEach(pos => topStars.Add(starList.Aggregate((min, next) => min.Center.ToAccordPoint().DistanceTo(pos) < next.Center.ToAccordPoint().DistanceTo(pos) ? min : next)));
                    starList = topStars;
                }
            }

            result.StarList = starList.Select(s => ToDetectedStar(s)).ToList();
            if (starList.Count > 1) {
                result.AverageHFR = starList.Average(s => s.HFR);
                var hfrVariance = starList.Sum(s => (s.HFR - result.AverageHFR) * (s.HFR - result.AverageHFR)) / (starList.Count - 1);
                result.HFRStdDev = Math.Sqrt(hfrVariance);

                Logger.Info($"Average HFR: {result.AverageHFR}, HFR σ: {result.HFRStdDev}, Detected Stars {result.StarList.Count}");
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
                Background = star.Background,
                BoundingBox = star.StarBoundingBox.ToDrawingRectangle(),
                PSF = star.PSF
            };
        }

        private static bool InROI(Star detectedStar, Size imageSize, StarDetectionParams p) {
            return DetectionUtility.InROI(imageSize, detectedStar.StarBoundingBox.ToDrawingRectangle(), outerCropRatio: p.OuterCropRatio, innerCropRatio: p.InnerCropRatio);
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
            hocusFocusAnalysis.FWHM = hocusFocusResult.FWHM;
            hocusFocusAnalysis.FWHMMAD = hocusFocusResult.FWHMMAD;
            hocusFocusAnalysis.Eccentricity = hocusFocusResult.Eccentricity;
            hocusFocusAnalysis.EccentricityMAD = hocusFocusResult.EccentricityMAD;
        }
    }
}