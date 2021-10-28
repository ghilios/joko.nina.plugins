using Joko.NINA.Plugins.HocusFocus.Interfaces;
using Joko.NINA.Plugins.HocusFocus.Utility;
using NINA.Core.Enum;
using NINA.Core.Interfaces;
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {

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
    }

    public class HocusFocusStarDetectionResult : StarDetectionResult {
        public StarDetectorMetrics Metrics { get; set; }
        // TODO: Add new fields here
        // public double Eccentricity { get; set; } = double.NaN;
    }

    [Export(typeof(IPluggableBehavior))]
    class HocusFocusStarDetection : IStarDetection {
        private readonly IStarDetector starDetector;
        private readonly StarDetectionOptions starDetectionOptions;

        public IImageStatisticsVM ImageStatisticsVM { get; private set; }

        public string Name => "Hocus Focus";

        public string ContentId => GetType().FullName;

        [ImportingConstructor]
        public HocusFocusStarDetection(IImageStatisticsVM imageStatisticsVM) {
            this.starDetector = new StarDetector();
            this.starDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            ImageStatisticsVM = imageStatisticsVM;
        }

        public async Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var detectorParams = new StarDetectorParams() {
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
                AnalysisSamplingSize = (float)starDetectionOptions.PixelSampleSize
            };

            if (p.UseROI && p.InnerCropRatio < 1.0 && p.OuterCropRatio > 0.0) {
                detectorParams.CenterROICropRatio = p.OuterCropRatio >= 1.0 ? p.InnerCropRatio : p.OuterCropRatio;
            }

            var result = new HocusFocusStarDetectionResult() { Params = p };
            var starDetectorResult = await this.starDetector.Detect(image, detectorParams, progress, token);
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

            result.StarList = starList.Select(s => s.ToDetectedStar()).ToList();
            if (starList.Count > 1) {
                result.AverageHFR = starList.Average(s => s.HFR);
                var hfrVariance = starList.Sum(s => (s.HFR - result.AverageHFR) * (s.HFR - result.AverageHFR)) / (starList.Count - 1);
                result.HFRStdDev = Math.Sqrt(hfrVariance);

                Logger.Info($"Average HFR: {result.AverageHFR}, HFR σ: {result.HFRStdDev}, Detected Stars {result.StarList.Count}");
            }
            result.Metrics = starDetectorResult.Metrics;
            return result;
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
        }
    }
}
