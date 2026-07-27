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
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
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

        /// <summary>
        /// The software binning factor detection ran at. <see cref="StructureMap"/> is a raster of the BINNED
        /// image, so it is <c>DetectionROI.Width / Binning</c> wide: each entry covers a
        /// <c>Binning × Binning</c> block of <see cref="DetectionROI"/>. 1 when binning is off.
        /// </summary>
        public int Binning = 1;
    }

    public class HocusFocusStarDetectionResult : StarDetectionResult {
        public StarDetectorParams DetectorParams { get; set; }
        public StarDetectorMetrics Metrics { get; set; }
        public HocusFocusDetectionParams HocusFocusParams { get; set; }
        public StarDetectionRegion Region { get; set; }
        public int FocuserPosition { get; set; }

        // Version of the star-detection logic that produced this result. Stamped from
        // StarDetector.StarDetectorVersion at construction and persisted to the saved
        // _star_detection_result.json so a later reuse-side task can reject a cache produced by a different
        // detector version. Defaults to the current version for in-memory results.
        public int DetectorVersion { get; set; } = StarDetector.StarDetectorVersion;

        // Stable hash of the effective detection params (region + version included), computed via
        // StarDetector.ComputeCacheKey at construction. A later reuse-side task compares this against the key
        // recomputed for the current params/version to decide whether a saved result may be reused.
        public string CacheKey { get; set; }

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
        public float NormalisedBrightness { get; set; }
        public Accord.Point OriginalPosition { get; set; }

        // The detector's bounding box BEFORE any registration/alignment transform overwrites BoundingBox. Captured
        // alongside OriginalPosition so the Review Frames overlay can draw boxes in the raw frame's own coordinates
        // (the displayed image is the raw frame; the aligned BoundingBox would be offset on non-reference frames).
        public System.Drawing.Rectangle OriginalBoundingBox { get; set; }

        public bool StarContaminationSuspected { get; set; }

        public override string ToString() {
            return $"{{{nameof(PSF)}={PSF}, {nameof(HFR)}={HFR.ToString()}, {nameof(Position)}={Position.ToString()}, {nameof(AverageBrightness)}={AverageBrightness.ToString()}, {nameof(MaxBrightness)}={MaxBrightness.ToString()}, {nameof(Background)}={Background.ToString()}, {nameof(BoundingBox)}={BoundingBox.ToString()}, {nameof(StarContaminationSuspected)}={StarContaminationSuspected.ToString()}}}";
        }
    }

    [Export(typeof(IPluggableBehavior))]
    public class HocusFocusStarDetection : IHocusFocusStarDetection {
        private readonly IStarDetector starDetector;
        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IProfileService profileService;
        private readonly IFocuserMediator focuserMediator;
        private readonly IPerFilterStarDetectionStore perFilterStore;
        private bool pixelScaleWarningShown = false;

        public IImageStatisticsVM ImageStatisticsVM { get; private set; }

        public string Name => "Hocus Focus";

        public string ContentId => GetType().FullName;

        [ImportingConstructor]
        public HocusFocusStarDetection(IImageStatisticsVM imageStatisticsVM, IProfileService profileService, IFocuserMediator focuserMediator) :
            this(imageStatisticsVM, profileService, focuserMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.AlglibAPI, HocusFocusPlugin.PerFilterStarDetection) {
        }

        public HocusFocusStarDetection(
            IImageStatisticsVM imageStatisticsVM,
            IProfileService profileService,
            IFocuserMediator focuserMediator,
            IStarDetectionOptions starDetectionOptions,
            IAlglibAPI alglibAPI,
            IPerFilterStarDetectionStore perFilterStore) {
            this.starDetector = new StarDetector(alglibAPI);
            this.starDetectionOptions = starDetectionOptions;
            this.profileService = profileService;
            this.focuserMediator = focuserMediator;
            this.perFilterStore = perFilterStore;
            ImageStatisticsVM = imageStatisticsVM;
        }

        public Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            return Detect(image, pf, p, progress, token, modelPSFForAutoFocus: false);
        }

        public async Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token, bool modelPSFForAutoFocus) {
            var selectedAutoFocusBehavior = profileService.ActiveProfile.ApplicationSettings.SelectedPluggableBehaviors.Where(k => k.Key == typeof(IAutoFocusVMFactory).FullName).ToList();
            var ninaStockAutoFocus = selectedAutoFocusBehavior.Count == 0 || selectedAutoFocusBehavior.First().Value == "NINA";
            var isNinaAutoFocus = ninaStockAutoFocus && p.IsAutoFocus;
            // UseAutoFocusCrop and ModelPSF are per-filter-scoped, so they must come from the SAME resolved
            // options object as every other knob in this detection (Task 7 review fix). Resolving here also
            // moves the indeterminate-filter throw inside the try below.
            IStarDetectionOptions effectiveOptions;
            StarDetectorParams detectorParams;
            try {
                effectiveOptions = ResolveEffectiveOptions(image);
                if (!effectiveOptions.UseAutoFocusCrop && !isNinaAutoFocus) {
                    p.UseROI = false;
                }
                var starDetectionRegion = StarDetectionRegion.FromStarDetectionParams(p);
                detectorParams = BuildStarDetectorParams(effectiveOptions, image, starDetectionRegion, p.IsAutoFocus);
            } catch (PerFilterSettingsUnavailableException e) {
                // Soft-fail: never throw into NINA's imaging pipeline. Warn on EVERY occurrence (no one-shot
                // latch) so a misconfigured session cannot silently zero out all of its detections.
                Logger.Warning(e.Message);
                Notification.ShowWarning(e.Message);
                return new HocusFocusStarDetectionResult() {
                    StarList = new List<DetectedStar>(),
                    DetectedStars = 0,
                    Params = p
                };
            }
            // BuildStarDetectorParams forces ModelPSF off for auto-focus (speed); lift that for a Review-Frames run
            // so the per-star PSF properties are populated, honoring the effective PSF setting + fit type.
            if (modelPSFForAutoFocus) {
                detectorParams.ModelPSF = effectiveOptions.ModelPSF;
            }
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

        /// <summary>
        /// Builds the option-derived portion of <see cref="StarDetectorParams"/> from
        /// <see cref="IStarDetectionOptions"/>. This is the single source of truth for the options→params
        /// mapping, so headless tooling (TestApp) reproduces exactly what NINA does. Image-dependent fields
        /// (<see cref="StarDetectorParams.PixelScale"/>, <see cref="StarDetectorParams.Region"/>) and the
        /// auto-focus overrides are layered on by <see cref="GetStarDetectorParams"/>.
        /// </summary>
        internal static StarDetectorParams BuildStarDetectorParams(IStarDetectionOptions options) {
            return new StarDetectorParams() {
                ModelPSF = options.ModelPSF,
                StarMeasurementNoiseReductionEnabled = options.StarMeasurementNoiseReductionEnabled,
                PSFFitType = options.PSFFitType,
                HotpixelFiltering = options.HotpixelFiltering,
                HotpixelThresholdingEnabled = options.HotpixelThresholdingEnabled,
                NoiseReductionRadius = options.NoiseReductionRadius,
                NoiseClippingMultiplier = options.NoiseClippingMultiplier,
                StarClippingMultiplier = options.StarClippingMultiplier,
                ContaminationSensitivity = options.ContaminationSensitivity,
                RejectContaminatedStars = options.RejectContaminatedStars,
                StructureLayers = options.StructureLayers,
                // The DefocusAwareDonutDetection MASTER toggle (default OFF) gates EVERY defocus-aware behavior:
                // when OFF, the structure-boost, the two gate relaxations, and all donut/spike knobs below are
                // forced off, so detection is bit-identical to legacy.
                DefocusAwareStructure = options.DefocusAwareStructure && options.DefocusAwareDonutDetection,
                StructureLayerBoost = options.StructureLayerBoost,
                Sensitivity = options.BrightnessSensitivity,
                PeakResponse = options.StarPeakResponse,
                MaxDistortion = options.MaxDistortion,
                // DefocusAwareGates drives BOTH gate relaxations (distortion + centering), gated by the master.
                // The detector keeps the two flags independent so TestApp's --defocus-distortion/--defocus-centering
                // switches can toggle them separately.
                DefocusAwareDistortion = options.DefocusAwareGates && options.DefocusAwareDonutDetection,
                DefocusAwareCentering = options.DefocusAwareGates && options.DefocusAwareDonutDetection,
                // Numeric tuning knobs (Advanced options); only take effect while the gates are ON.
                DefocusDistortionSizeReference = options.DefocusDistortionSizeReference,
                DefocusDistortionMinFactor = options.DefocusDistortionMinFactor,
                DefocusCenteringToleranceFactor = options.DefocusCenteringToleranceFactor,
                // Master + donut-recovery / spike-suppression knobs. Each is runtime-gated by
                // DefocusAwareDonutDetection inside the detector, so passing the option values verbatim is safe
                // (when the master is OFF none of them are consulted ⇒ bit-identical).
                DefocusAwareDonutDetection = options.DefocusAwareDonutDetection,
                DonutMorphCloseSize = options.DonutMorphCloseSize,
                // Spatially-adaptive binarization (independent of the donut master): passed verbatim. When OFF the
                // detector keeps the legacy scalar binarize threshold ⇒ bit-identical. EARLY param.
                LocallyAdaptiveBinarization = options.LocallyAdaptiveBinarization,
                AdaptiveNoiseBlockSize = options.AdaptiveNoiseBlockSize,
                DonutMinAnnularityHoleFraction = options.DonutMinAnnularityHoleFraction,
                DonutMaxStreakEccentricity = options.DonutMaxStreakEccentricity,
                DonutSaturationBloomRadius = options.DonutSaturationBloomRadius,
                StarCenterTolerance = options.StarCenterTolerance,
                BackgroundBoxExpansion = options.StarBackgroundBoxExpansion,
                MinimumStarBoundingBoxSize = options.MinStarBoundingBoxSize,
                MinHFR = options.MinHFR,
                StructureDilationSize = options.StructureDilationSize,
                StructureDilationCount = options.StructureDilationCount,
                AnalysisSamplingSize = (float)options.PixelSampleSize,
                StoreStructureMap = options.DebugMode,
                SaveIntermediateFilesPath = options.SaveIntermediateImages ? options.IntermediateSavePath : string.Empty,
                PSFParallelPartitionSize = options.PSFParallelPartitionSize,
                PSFResolution = options.PSFResolution,
                PSFGoodnessOfFitThreshold = options.PSFFitThreshold,
                UsePSFAbsoluteDeviation = options.UsePSFAbsoluteDeviation,
                HotpixelThreshold = options.HotpixelThreshold,
                SaturationThreshold = options.SaturationThreshold,
                ExcludeSaturatedStarsFromHFR = options.ExcludeSaturatedStarsFromHFR,
                PSFPixelIntegration = options.PSFPixelIntegration,
                // Internal parallelism knob — 0 = auto (Environment.ProcessorCount via ParallelExecution governor).
                // Not exposed in the options UI; callers may override after BuildStarDetectorParams returns.
                MaxStarEvaluationParallelism = 0,
                // Carried on the params so the detect site uses the options actually in play (live or a replay override).
                MeasurementAverage = options.MeasurementAverage
            };
        }

        /// <summary>
        /// The "fully-default" detector params — the analogue of <see cref="BuildStarDetectorParams"/> for an
        /// options object at <c>StarDetectionOptions.ResetDefaults()</c>. Every option-derived field is at its
        /// documented default. Used as the Optimization Wizard's seed so the search starts from a clean,
        /// reproducible point regardless of the user's current settings. Image-dependent fields (PixelScale,
        /// Region) and the auto-focus overrides are layered on by <see cref="GetDefaultStarDetectorParams"/>.
        /// The literals here are kept in lockstep with ResetDefaults by
        /// StarDetectionOptionsTests.BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild.
        /// </summary>
        internal static StarDetectorParams BuildDefaultStarDetectorParams() {
            return new StarDetectorParams() {
                ModelPSF = true,
                StarMeasurementNoiseReductionEnabled = false,
                PSFFitType = StarDetectorPSFFitType.Moffat_40,
                HotpixelFiltering = true,
                HotpixelThresholdingEnabled = true,
                NoiseReductionRadius = 3,
                NoiseClippingMultiplier = 4.0, // INTERIM revert to v3.0.0.26 (lockstep with ResetDefaults; drift-guard test)
                StarClippingMultiplier = 2.0,
                ContaminationSensitivity = 5.0,
                RejectContaminatedStars = true,
                StructureLayers = 4,
                DefocusAwareStructure = false,
                StructureLayerBoost = 0,
                Sensitivity = 10.0, // INTERIM revert to v3.0.0.26 (lockstep with ResetDefaults; drift-guard test)
                PeakResponse = 0.75,
                MaxDistortion = 0.5,
                DefocusAwareDistortion = false,
                DefocusAwareCentering = false,
                DefocusDistortionSizeReference = 30.0,
                DefocusDistortionMinFactor = 0.25,
                DefocusCenteringToleranceFactor = 2.0,
                // Donut master + knobs at their ResetDefaults values (master OFF ⇒ inert). Kept in lockstep with
                // StarDetectionOptions.ResetDefaults by BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild.
                DefocusAwareDonutDetection = false,
                DonutMorphCloseSize = 5,
                LocallyAdaptiveBinarization = true,   // default ON (AF-bank validated)
                AdaptiveNoiseBlockSize = 128,
                DonutMinAnnularityHoleFraction = 0.15,
                DonutMaxStreakEccentricity = 1.0,
                DonutSaturationBloomRadius = 0.0,
                StarCenterTolerance = 0.3,
                BackgroundBoxExpansion = 3,
                MinimumStarBoundingBoxSize = 5,
                MinHFR = 1.2,
                StructureDilationSize = 3,
                StructureDilationCount = 0,
                AnalysisSamplingSize = 1.0f,
                StoreStructureMap = false,
                SaveIntermediateFilesPath = string.Empty,
                PSFParallelPartitionSize = 100,
                PSFResolution = 10,
                PSFGoodnessOfFitThreshold = 0.9,
                UsePSFAbsoluteDeviation = false,
                HotpixelThreshold = 0.001d,
                SaturationThreshold = 0.99d,
                ExcludeSaturatedStarsFromHFR = true,
                PSFPixelIntegration = false,
                MaxStarEvaluationParallelism = 0,
                // Matches StarDetectionOptions.ResetDefaults (Median); kept in lockstep by
                // BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild.
                MeasurementAverage = MeasurementAverageEnum.Median
            };
        }

        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            return BuildStarDetectorParams(ResolveEffectiveOptions(image), image, starDetectionRegion, isAutoFocus);
        }

        /// <summary>
        /// Builds the full detector params from an ALREADY-RESOLVED options source. Callers that need other
        /// per-filter-scoped knobs for the same detection (see the 6-arg <see cref="Detect(IRenderedImage, PixelFormat, StarDetectionParams, IProgress{ApplicationStatus}, CancellationToken, bool)"/>)
        /// resolve once via <see cref="ResolveEffectiveOptions"/> and call this, so a single detection can never mix
        /// a filter's snapshot with the live global options.
        /// </summary>
        private StarDetectorParams BuildStarDetectorParams(IStarDetectionOptions effectiveOptions, IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var detectorParams = BuildStarDetectorParams(effectiveOptions);
            ApplyDetectionImageContext(detectorParams, image, starDetectionRegion, isAutoFocus, effectiveOptions.DetectionBinning);
            if (!isAutoFocus) {
                // Only save intermediate images for 1 detection. Doing this again should require the user to pick it again.
                // SaveIntermediateImages is machine-local by scope decision, so the one-shot reset always targets the
                // live options — never a per-filter snapshot clone.
                starDetectionOptions.SaveIntermediateImages = false;
            }
            return detectorParams;
        }

        /// <summary>
        /// Resolves the options source for a detection with strict precedence: the per-filter snapshot for the
        /// image's capture-time filter (feature enabled; machine-local fields copied from the live options so a
        /// snapshot never overrides debug/save/parallelism settings), else the injected singleton. Feature enabled
        /// with an indeterminate filter throws <see cref="PerFilterSettingsUnavailableException"/> — the NINA-facing
        /// Detect soft-fails on it; typed callers propagate it to their existing failure handling. The explicit
        /// optionsOverride overload of GetStarDetectorParams bypasses this entirely (replay wins).
        /// </summary>
        private IStarDetectionOptions ResolveEffectiveOptions(IRenderedImage image) {
            if (!perFilterStore.Enabled) {
                return starDetectionOptions;
            }
            var filterName = image?.RawImageData?.MetaData?.FilterWheel?.Filter;
            // Whitespace-only counts as indeterminate: seeding a whitespace-keyed snapshot would persist a row into
            // the profile blob that no UI filter row can ever match. The name is otherwise passed through untrimmed,
            // which keeps it consistent with the store's StringComparer.Ordinal keys and the binder's comparisons.
            if (string.IsNullOrWhiteSpace(filterName)) {
                throw new PerFilterSettingsUnavailableException("Per-filter star detection is enabled but the active filter is unknown - connect a filter wheel.");
            }
            var snapshot = perFilterStore.GetOrSeedSnapshot(filterName);
            snapshot.CopyMachineLocalFrom(starDetectionOptions);
            return snapshot;
        }

        /// <summary>The detector's injected star-detection options (read-only).</summary>
        public IStarDetectionOptions StarDetectionOptions => starDetectionOptions;

        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus, IStarDetectionOptions optionsOverride) {
            if (optionsOverride == null) {
                return GetStarDetectorParams(image, starDetectionRegion, isAutoFocus);
            }
            // Build the option-derived params from the override snapshot — never from (nor mutating) the injected
            // options — then layer on the same image context + auto-focus overrides as the standard path, so a
            // capture-time replay produces identical params to a live run configured with those settings.
            var detectorParams = BuildStarDetectorParams(optionsOverride);
            ApplyDetectionImageContext(detectorParams, image, starDetectionRegion, isAutoFocus, optionsOverride.DetectionBinning);
            return detectorParams;
        }

        /// <summary>
        /// The Optimization Wizard's seed: the fully-default detector params (<see cref="BuildDefaultStarDetectorParams"/>)
        /// with the SAME image-dependent fields + auto-focus overrides as <see cref="GetStarDetectorParams"/> layered on
        /// (PixelScale, Region, ModelPSF=false, SaveIntermediateFilesPath=""). Read-only with respect to options — it
        /// never touches <c>starDetectionOptions</c>. <paramref name="isAutoFocus"/> is expected to be true for the
        /// wizard's replay path.
        /// </summary>
        public StarDetectorParams GetDefaultStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var detectorParams = BuildDefaultStarDetectorParams();
            // Binning is the ONE image-context field the seed does NOT reset: it describes the rig (how many pixels
            // a star spans), not a tunable the optimizer explores. Seeding it at 1x while the user runs at 2x would
            // tune the search against pixels they will never analyze, and would make the seed's score incomparable
            // with the baseline's. Read from the live options (never mutated).
            ApplyDetectionImageContext(detectorParams, image, starDetectionRegion, isAutoFocus, starDetectionOptions.DetectionBinning);
            return detectorParams;
        }

        /// <summary>
        /// Layers the image-dependent fields (PixelScale from the profile × binning, the resolved software
        /// <see cref="StarDetectorParams.DetectionBinning"/>, Region) and the auto-focus overrides (ModelPSF=false,
        /// no intermediate-file save) onto an already-built params bundle. Pure with respect to options — shared by
        /// <see cref="GetStarDetectorParams"/> and <see cref="GetDefaultStarDetectorParams"/> so the two can never
        /// diverge in how they compute pixel scale or apply the AF overrides.
        /// </summary>
        private void ApplyDetectionImageContext(StarDetectorParams detectorParams, IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus, DetectionBinningEnum detectionBinning) {
            var binning = Math.Max(image.RawImageData.MetaData.Camera.BinX, 1);
            var pixelScale = MathUtility.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize, profileService.ActiveProfile.TelescopeSettings.FocalLength) * binning;
            if (double.IsNaN(pixelScale)) {
                if (!pixelScaleWarningShown) {
                    pixelScaleWarningShown = true;
                    Notification.ShowWarning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
                }
                Logger.Warning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
            }

            // The user's explicit factor. It is never derived here: a detection binning that resolved itself would
            // change how frames are analyzed the moment the plugin updated, silently invalidating tuned settings.
            // The UI recommends a factor (DetectionBinningResolver) and the user chooses.
            var softwareBinning = DetectionBinningResolver.ToFactor(detectionBinning);
            detectorParams.DetectionBinning = softwareBinning;

            // Detection runs in BINNED pixels, so the scale it reasons in is the binned scale. That keeps the one
            // arcsec-valued output (PSF.FWHMArcsecs) physical while every pixel-valued output is measured in binned
            // pixels — the detector scales those back to source pixels before returning. Consumers that want the
            // frame's own pixel scale divide back out by DetectionBinning (see BuildResultHeader).
            detectorParams.PixelScale = pixelScale * softwareBinning;
            detectorParams.Region = starDetectionRegion;

            // For AutoFocus, don't save intermediate data or model PSFs.
            if (isAutoFocus) {
                detectorParams.SaveIntermediateFilesPath = string.Empty;
                detectorParams.ModelPSF = false;
                // Design decision (accuracy analysis F1): the TooFlat gate (StarDetector rejects candidates whose
                // median >= PeakResponse*peak) is intentionally left ACTIVE during AutoFocus. It can reject bright,
                // heavily-defocused flat-top/donut stars, but relaxing it here risks admitting flat noise blobs, and
                // PeakResponse is also reused in the sensitivity (NormalizedBrightness) calc so loosening it has side
                // effects. Revisit with a real defocus dataset (TestApp focus-sweep) if AF star counts drop at sweep
                // extremes.
            }
        }

        /// <summary>
        /// The pixel scale of the frame AS CAPTURED, which is what results report and the UI shows.
        /// <see cref="StarDetectorParams.PixelScale"/> is the BINNED scale the detector reasons in (see
        /// <see cref="ApplyDetectionImageContext"/>), so divide the software binning factor back out — every
        /// pixel-valued field on the result is already back in source pixels, and this keeps the two consistent.
        /// </summary>
        private static double SourcePixelScale(StarDetectorParams detectorParams)
            => detectorParams.PixelScale / Math.Max(1, detectorParams.DetectionBinning);

        public async Task<StarDetectionResult> Detect(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var result = BuildResultHeader(image, hocusFocusParams, detectorParams, out var imageSize, out var _);
            var starDetectorResult = await this.starDetector.Detect(image, detectorParams, progress, token);
            if (!string.IsNullOrEmpty(detectorParams.SaveIntermediateFilesPath)) {
                Notification.ShowInformation("Saved intermediate star detection files");
                Logger.Info($"Saved intermediate star detection files to {detectorParams.SaveIntermediateFilesPath}");
            }

            return BuildStarDetectionResult(result, starDetectorResult, hocusFocusParams, detectorParams, imageSize);
        }

        /// <summary>Builds the pre-populated <see cref="HocusFocusStarDetectionResult"/> header (image geometry,
        /// pixel size/scale, focuser position, cache key) shared by the monolithic and split detect paths.</summary>
        private HocusFocusStarDetectionResult BuildResultHeader(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, out Size imageSize, out double pixelSize) {
            var binX = double.IsNaN(image.RawImageData.MetaData.Camera.BinX) ? 1 : image.RawImageData.MetaData.Camera.BinX;
            var metadataPixelSize = double.IsNaN(image.RawImageData.MetaData.Camera.PixelSize) ? 3.76 : image.RawImageData.MetaData.Camera.PixelSize;
            pixelSize = metadataPixelSize * Math.Max(binX, 1);
            imageSize = new Size(width: image.RawImageData.Properties.Width, height: image.RawImageData.Properties.Height);
            return new HocusFocusStarDetectionResult() {
                HocusFocusParams = hocusFocusParams,
                DetectorParams = detectorParams,
                ImageSize = imageSize,
                Region = detectorParams.Region,
                FocuserPosition = focuserMediator.GetInfo().Position,
                PixelSize = pixelSize,
                PixelScale = SourcePixelScale(detectorParams),
                MeasurementAverage = detectorParams.MeasurementAverage,
                DetectorVersion = StarDetector.StarDetectorVersion,
                CacheKey = StarDetector.ComputeCacheKey(detectorParams)
            };
        }

        public string ComputeEarlyCacheKey(StarDetectorParams detectorParams) => StarDetector.ComputeEarlyCacheKey(detectorParams);

        public async Task<HocusFocusDetectionContext> BuildDetectionContext(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token) {
            // EARLY phase: capture the header inputs (focuser position is read NOW, matching the monolithic path
            // which reads it before detection) + run the expensive early detector stage into a reusable context.
            BuildResultHeader(image, hocusFocusParams, detectorParams, out var imageSize, out var pixelSize);
            var detectorContext = await this.starDetector.BuildDetectionContext(image, detectorParams, progress, token).ConfigureAwait(false);
            return new HocusFocusDetectionContext {
                DetectorContext = detectorContext,
                HocusFocusParams = hocusFocusParams,
                ImageSize = imageSize,
                PixelSize = pixelSize,
                FocuserPosition = focuserMediator.GetInfo().Position
            };
        }

        public StarDetectionResult GateAndMeasure(HocusFocusDetectionContext context, StarDetectorParams detectorParams, CancellationToken token) {
            // LATE phase: re-build the header (deterministic from the carried inputs + current detectorParams) so the
            // late-param-dependent fields (DetectorParams, Region, CacheKey) reflect the candidate being evaluated,
            // then gate+measure the cached context and run the identical post-processing.
            var hocusFocusParams = context.HocusFocusParams;
            var result = new HocusFocusStarDetectionResult() {
                HocusFocusParams = hocusFocusParams,
                DetectorParams = detectorParams,
                ImageSize = context.ImageSize,
                Region = detectorParams.Region,
                FocuserPosition = context.FocuserPosition,
                PixelSize = context.PixelSize,
                PixelScale = SourcePixelScale(detectorParams),
                MeasurementAverage = detectorParams.MeasurementAverage,
                DetectorVersion = StarDetector.StarDetectorVersion,
                CacheKey = StarDetector.ComputeCacheKey(detectorParams)
            };
            var starDetectorResult = this.starDetector.GateAndMeasure(context.DetectorContext, detectorParams, token);
            return BuildStarDetectionResult(result, starDetectorResult, hocusFocusParams, detectorParams, context.ImageSize);
        }

        /// <summary>
        /// The post-detection processing (OutsideROI crop, optional outlier rejection, PSF aggregation, AF-star
        /// selection, HFR aggregation) shared by the monolithic <see cref="Detect(IRenderedImage, HocusFocusDetectionParams, StarDetectorParams, IProgress{ApplicationStatus}, CancellationToken)"/>
        /// path AND the optimizer's split path (so they cannot drift). <paramref name="result"/> is the
        /// pre-populated header; <paramref name="starDetectorResult"/> is the raw detector output to fold in.
        /// </summary>
        // Minimum unsaturated stars that must remain before saturated stars are dropped from the HFR aggregation.
        // Below this we keep every star so a bright/saturated-dominated frame still yields an HFR.
        internal const int MinUnsaturatedStarsForHfr = 3;

        /// <summary>
        /// The subset of accepted stars to use for HFR AGGREGATION (AverageHFR / HFRStdDev). When
        /// <paramref name="excludeSaturated"/> is on and at least <see cref="MinUnsaturatedStarsForHfr"/> unsaturated
        /// stars remain, partially-saturated stars (whose flat cores bias HFR HIGH by design — see StarDetector's
        /// MeasureStar) are dropped so they do not inflate the per-frame curve point; otherwise every star is kept.
        /// The accepted set itself — StarCount, centers, per-star HFRs — is unchanged: a saturated star is still a
        /// real, counted star (and the optimizer's HFR-outlier penalty still sees it). Saturation uses the detector's
        /// own test, <c>Background + PeakBrightness ≥ SaturationThreshold</c> (StarDetector.cs). When nothing is
        /// saturated (or exclusion is off) the input list is returned unchanged, so HFR is bit-identical.
        /// </summary>
        internal static IReadOnlyList<Star> StarsForHfrAggregation(IReadOnlyList<Star> stars, bool excludeSaturated, double saturationThreshold) {
            if (!excludeSaturated || stars == null || stars.Count == 0) {
                return stars;
            }
            var unsaturated = new List<Star>(stars.Count);
            for (var i = 0; i < stars.Count; i++) {
                var s = stars[i];
                if (s.Background + s.PeakBrightness < saturationThreshold) {
                    unsaturated.Add(s);
                }
            }
            return unsaturated.Count >= MinUnsaturatedStarsForHfr ? unsaturated : stars;
        }

        internal StarDetectionResult BuildStarDetectionResult(
                HocusFocusStarDetectionResult result,
                HocusFocusStarDetectorResult starDetectorResult,
                HocusFocusDetectionParams hocusFocusParams,
                StarDetectorParams detectorParams,
                Size imageSize) {
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

            if (starList.Count > 1 && detectorParams.MeasurementAverage == MeasurementAverageEnum.MeanOutliers) {
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

            // Re-tally RelaxationAdmittedCount over the FINAL post-filter survivor set (post ROI-crop + post
            // MeanOutliers), so it shares its denominator with result.DetectedStars / StarCount. StarDetector
            // tallies it on the pre-filter accepted set; the wizard/loader path (RunEvaluationLoader) reads this
            // metric but reports StarCount from the post-filter list, so without this re-tally the precision
            // fraction could mix a pre-filter numerator with a post-filter denominator (and exceed 1.0) and would
            // disagree with the offline harness, which counts over its post-filter survivors. starList is still a
            // List<Star> here (the RelaxationAdmitted flag survives; the DetectedStar projection below drops it).
            // Gate-OFF this is 0 either way, so detection bit-identity is preserved.
            starDetectorResult.Metrics.RelaxationAdmittedCount = starList.Count(s => s.RelaxationAdmitted);

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

            // TODO: Consider whether to remove the ordering to get reproducibility between runs
            result.StarList = starList.Select(s => ToDetectedStar(s)).OrderBy(s => s.Position.Y * imageSize.Width + s.Position.X).ToList();
            // Aggregate the per-frame HFR over the saturated-filtered subset: a partially-saturated bright star stays
            // counted and in StarList/StarCenters, but its flat-core HFR (biased high by design) is kept out of the
            // curve point when enough unsaturated stars remain. When nothing is saturated this is the full starList,
            // so AverageHFR/HFRStdDev are bit-identical.
            var hfrStars = StarsForHfrAggregation(starList, detectorParams.ExcludeSaturatedStarsFromHFR, detectorParams.SaturationThreshold);
            // AverageHFR/HFRStdDev need at least 2 usable stars to be meaningful; with 0 or 1 they stay 0 (a
            // rejected curve point), which preserves the fit inputs exactly.
            string hfrDispersionLabel;
            if (detectorParams.MeasurementAverage == MeasurementAverageEnum.MeanOutliers) {
                hfrDispersionLabel = "HFR σ";
                if (hfrStars.Count > 1) {
                    result.AverageHFR = hfrStars.Average(s => s.HFR);
                    var hfrVariance = hfrStars.Sum(s => (s.HFR - result.AverageHFR) * (s.HFR - result.AverageHFR)) / (hfrStars.Count - 1);
                    result.HFRStdDev = Math.Sqrt(hfrVariance);
                }
            } else {
                hfrDispersionLabel = "HFR MAD";
                if (hfrStars.Count > 1) {
                    var (hfrMedian, hfrMAD) = hfrStars.Select(s => s.HFR).MedianMAD();
                    result.AverageHFR = hfrMedian;
                    result.HFRStdDev = hfrMAD;
                }
            }

            // Log EVERY region's result — including regions with too few usable stars, where AverageHFR stays 0 —
            // so a region that dropped out (e.g. a transient no-star frame) is visible in the log rather than
            // silently absent. Previously this line lived inside the "count > 1" guard, so a 0/1-star region logged
            // nothing and its downstream HFR-validation failure had to be inferred from the missing line.
            if (!detectorParams.SuppressInfoLogging) {
                Logger.Info($"Average HFR: {result.AverageHFR}, {hfrDispersionLabel}: {result.HFRStdDev}, Detected Stars {result.StarList.Count}, Region: {result.Region?.Index ?? 0}");
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
                PSF = star.PSF,
                StarContaminationSuspected = star.StarContaminationSuspected
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
            hocusFocusAnalysis.StarList = hocusFocusResult.StarList;
        }
    }
}