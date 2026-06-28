#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [JsonObject]
    public class StarDetectionOptions : BaseINPC, IStarDetectionOptions {
        private readonly IPluginOptionsAccessor optionsAccessor;

        public StarDetectionOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal StarDetectionOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            this.PropertyChanged += StarDetectionOptions_PropertyChanged;
            InitializeOptions();
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        private HashSet<String> SimplePropertyNames = new HashSet<string>() {
            nameof(Simple_NoiseLevel),
            nameof(Simple_PixelScale),
            nameof(Simple_FocusRange),
            nameof(UseOptimizedSettings),
        };

        private void ConfigureSimpleSettings() {
            if (this.UseAdvanced) {
                return;
            }

            if (UseOptimizedSettings && HasOptimizedSettings) {
                ApplyOptimizedSnapshotToLiveProperties();
                return;
            }

            DerivePresetSettings();
        }

        // Applies the curated optimized snapshot on top of the Simple-mode preset baseline. The non-curated
        // advanced knobs (PSF, dilation, etc.) keep their Simple-mode preset defaults; the curated knobs win.
        // Setting these live properties while in Simple Mode does NOT recurse, because the PropertyChanged
        // handler only re-runs ConfigureSimpleSettings for properties in SimplePropertyNames (which the curated
        // knobs are not).
        private void ApplyOptimizedSnapshotToLiveProperties() {
            DerivePresetSettings();
            var s = optimizedSettings;
            if (s == null) {
                return;
            }
            BrightnessSensitivity = s.BrightnessSensitivity;
            StarClippingMultiplier = s.StarClippingMultiplier;
            NoiseClippingMultiplier = s.NoiseClippingMultiplier;
            StarPeakResponse = s.StarPeakResponse;
            MaxDistortion = s.MaxDistortion;
            MinHFR = s.MinHFR;
            StarCenterTolerance = s.StarCenterTolerance;
            StructureLayers = s.StructureLayers;
            NoiseReductionRadius = s.NoiseReductionRadius;
            MinStarBoundingBoxSize = s.MinStarBoundingBoxSize;
            HotpixelThresholdingEnabled = s.HotpixelThresholdingEnabled;
            HotpixelThreshold = s.HotpixelThreshold;
            // Defocus-aware axes (schema v2). A v1 snapshot deserializes these to ResetDefaults (master OFF), so
            // applying it is inert/legacy. The master + donut knobs persist the optimizer's donut result.
            DefocusAwareGates = s.DefocusAwareGates;
            DefocusDistortionSizeReference = s.DefocusDistortionSizeReference;
            DefocusDistortionMinFactor = s.DefocusDistortionMinFactor;
            DefocusCenteringToleranceFactor = s.DefocusCenteringToleranceFactor;
            DefocusAwareStructure = s.DefocusAwareStructure;
            StructureLayerBoost = s.StructureLayerBoost;
            DefocusAwareDonutDetection = s.DefocusAwareDonutDetection;
            DonutMorphCloseSize = s.DonutMorphCloseSize;
            LocallyAdaptiveBinarization = s.LocallyAdaptiveBinarization;
            AdaptiveNoiseBlockSize = s.AdaptiveNoiseBlockSize;
            DonutMinAnnularityHoleFraction = s.DonutMinAnnularityHoleFraction;
            DonutMaxStreakEccentricity = s.DonutMaxStreakEccentricity;
            DonutSaturationBloomRadius = s.DonutSaturationBloomRadius;
        }

        private void DerivePresetSettings() {
            HotpixelFiltering = Simple_NoiseLevel != NoiseLevelEnum.None;
            // F4: σ-based knobs are honest multiples of the measured image's σ. The old code measured σ on a
            // blurred copy (~4× understated for white noise) on the Low/Typical path only, so the same knob value
            // used to mean very different effective thresholds across noise presets. sensitivityScale compensates
            // per preset so each preset's EFFECTIVE behavior is approximately unchanged (the real-data before/after
            // sweep arbitrates the constant): ×0.2 where the mismatch existed, ×1 where σ was already honest
            // (None: no blur; High: measurement noise reduction blurs the measured image itself).
            double sensitivityScale = 1.0;
            switch (Simple_NoiseLevel) {
                case NoiseLevelEnum.None:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 0;
                    break;

                case NoiseLevelEnum.Low:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 3;
                    sensitivityScale = 0.2;
                    break;

                case NoiseLevelEnum.Typical:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 3;
                    sensitivityScale = 0.2;
                    break;

                case NoiseLevelEnum.High:
                    StarMeasurementNoiseReductionEnabled = true;
                    NoiseReductionRadius = 5;
                    break;
            }
            NoiseClippingMultiplier = 2.0; // structure-map binarize threshold: lowered 4→2 per the golden-set recall
                                           // audit — candidate formation was the recall bottleneck (~79% of real
                                           // stars never formed a candidate at 4σ). See docs/star-detection-golden-audit-cwhite-results.md
            StarClippingMultiplier = 2.0; // uniform honest τ level, chosen empirically (F3) — see docs/sigma-consistency-f3-results.md
            StructureLayers = 4;
            BrightnessSensitivity = 10.0 * sensitivityScale;
            if (Simple_FocusRange == FocusRangeEnum.WideRange) {
                StructureLayers += 1;
                // As we get further from focus, we want to be more sensitive as the chance for bad data
                // increases. BrightnessSensitivity is a threshold where SMALLER = more sensitive, so we LOWER it.
                BrightnessSensitivity -= 2.0 * sensitivityScale;
            }

            MinStarBoundingBoxSize = 5;
            PixelSampleSize = 1.0;
            if (Simple_PixelScale == PixelScaleEnum.WideField) {
                StructureLayers -= 1;
                MinStarBoundingBoxSize -= 1;
                PixelSampleSize = 0.5;
            } else if (Simple_PixelScale == PixelScaleEnum.LongFocalLength) {
                StructureLayers += 1;
                MinStarBoundingBoxSize += 1;
                // Longer focal length spreads star flux over more pixels, so we want to be more sensitive.
                // BrightnessSensitivity is a threshold where SMALLER = more sensitive, so we LOWER it.
                BrightnessSensitivity -= 2.0 * sensitivityScale;
            }

            if (HotpixelThresholdingEnabled && HotpixelFiltering) {
                // Without thresholding, hotpixel filtering does a blur. To compensate, we increase the noise reduction radius
                NoiseReductionRadius += 1;
            }

            StarPeakResponse = 0.75;
            MaxDistortion = 0.5;
            StarCenterTolerance = 0.3;
            StarBackgroundBoxExpansion = 3;
            MinHFR = 1.2; // honest-HFR floor; the old 1.5 was calibrated against noise-inflated faint HFRs (F3 follow-up)
            StructureDilationSize = 3;
            StructureDilationCount = 0;
            PSFFitType = StarDetectorPSFFitType.Moffat_40;
            // TODO: Consider increasing the resolution for long focal lengths
            PSFResolution = 10;
            PSFFitThreshold = 0.9;
            HotpixelThreshold = 0.001d;
        }

        private void StarDetectionOptions_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (this.UseAdvanced) {
                return;
            }

            if (!String.IsNullOrEmpty(e.PropertyName) && !SimplePropertyNames.Contains(e.PropertyName)) {
                return;
            }
            ConfigureSimpleSettings();
        }

        private void InitializeOptions() {
            debugMode = optionsAccessor.GetValueBoolean("DetectionDebugMode", false);
            modelPSF = optionsAccessor.GetValueBoolean("ModelPSF", true);
            useAdvanced = optionsAccessor.GetValueBoolean("UseAdvanced", false);
            psfFitType = optionsAccessor.GetValueEnum<StarDetectorPSFFitType>("PSFFitType", StarDetectorPSFFitType.Moffat_40);
            simple_NoiseLevel = optionsAccessor.GetValueEnum<NoiseLevelEnum>("Simple_NoiseLevel", NoiseLevelEnum.Typical);
            simple_PixelScale = optionsAccessor.GetValueEnum<PixelScaleEnum>("Simple_PixelScale", PixelScaleEnum.Typical);
            simple_FocusRange = optionsAccessor.GetValueEnum<FocusRangeEnum>("Simple_FocusRange", FocusRangeEnum.Typical);
            hotpixelFiltering = optionsAccessor.GetValueBoolean("HotpixelFiltering", true);
            hotpixelThresholdingEnabled = optionsAccessor.GetValueBoolean(nameof(HotpixelThresholdingEnabled), true);
            useAutoFocusCrop = optionsAccessor.GetValueBoolean("UseAutoFocusCrop", true);
            starMeasurementNoiseReductionEnabled = optionsAccessor.GetValueBoolean(nameof(StarMeasurementNoiseReductionEnabled), false);
            noiseReductionRadius = optionsAccessor.GetValueInt32("NoiseReductionRadius", 3);
            noiseClippingMultiplier = optionsAccessor.GetValueDouble("NoiseClippingMultiplier", 2.0);
            starClippingMultiplier = optionsAccessor.GetValueDouble("StarClippingMultiplier", 2.0);
            contaminationSensitivity = optionsAccessor.GetValueDouble("ContaminationSensitivity", 5.0);
            rejectContaminatedStars = optionsAccessor.GetValueBoolean("RejectContaminatedStars", true);
            structureLayers = optionsAccessor.GetValueInt32("StructureLayers", 4);
            defocusAwareStructure = optionsAccessor.GetValueBoolean("DefocusAwareStructure", false);
            structureLayerBoost = optionsAccessor.GetValueInt32("StructureLayerBoost", 0);
            brightnessSensitivity = optionsAccessor.GetValueDouble("BrightnessSensitivity", 2.0);
            starPeakResponse = optionsAccessor.GetValueDouble("StarPeakResponse", 0.75);
            maxDistortion = optionsAccessor.GetValueDouble("MaxDistortion", 0.5);
            defocusAwareGates = optionsAccessor.GetValueBoolean("DefocusAwareGates", false);
            defocusDistortionSizeReference = optionsAccessor.GetValueDouble("DefocusDistortionSizeReference", 30.0);
            defocusDistortionMinFactor = optionsAccessor.GetValueDouble("DefocusDistortionMinFactor", 0.25);
            defocusCenteringToleranceFactor = optionsAccessor.GetValueDouble("DefocusCenteringToleranceFactor", 2.0);
            defocusAwareDonutDetection = optionsAccessor.GetValueBoolean("DefocusAwareDonutDetection", false);
            donutMorphCloseSize = optionsAccessor.GetValueInt32("DonutMorphCloseSize", 5);
            locallyAdaptiveBinarization = optionsAccessor.GetValueBoolean("LocallyAdaptiveBinarization", true);
            adaptiveNoiseBlockSize = optionsAccessor.GetValueInt32("AdaptiveNoiseBlockSize", 128);
            donutMinAnnularityHoleFraction = optionsAccessor.GetValueDouble("DonutMinAnnularityHoleFraction", 0.15);
            donutMaxStreakEccentricity = optionsAccessor.GetValueDouble("DonutMaxStreakEccentricity", 1.0);
            donutSaturationBloomRadius = optionsAccessor.GetValueDouble("DonutSaturationBloomRadius", 0.0);
            starCenterTolerance = optionsAccessor.GetValueDouble("StarCenterTolerance", 0.3);
            starBackgroundBoxExpansion = optionsAccessor.GetValueInt32("StarBackgroundBoxExpansion", 3);
            minStarBoundingBoxSize = optionsAccessor.GetValueInt32("MinStarBoundingBoxSize", 5);
            minHFR = optionsAccessor.GetValueDouble("MinHFR", 1.2);
            structureDilationSize = optionsAccessor.GetValueInt32("StructureDilationSize", 3);
            structureDilationCount = optionsAccessor.GetValueInt32("StructureDilationCount", 0);
            pixelSampleSize = optionsAccessor.GetValueDouble("PixelSampleSize", 1.0);
            intermediateSavePath = optionsAccessor.GetValueString(nameof(IntermediateSavePath), "");
            if (string.IsNullOrEmpty(intermediateSavePath) || !Directory.Exists(intermediateSavePath)) {
                IntermediateSavePath = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "HocusFocusIntermediate");
                if (!Directory.Exists(IntermediateSavePath)) {
                    Directory.CreateDirectory(IntermediateSavePath);
                }
            }
            saveIntermediateImages = false;
            psfParallelPartitionSize = optionsAccessor.GetValueInt32("PSFParallelPartitionSize", 100);
            psfResolution = optionsAccessor.GetValueInt32("PSFResolution", 10);
            psfFitThreshold = optionsAccessor.GetValueDouble("PSFFitThreshold", 0.9);
            usePSFAbsoluteDeviation = optionsAccessor.GetValueBoolean(nameof(UsePSFAbsoluteDeviation), false);
            hotpixelThreshold = optionsAccessor.GetValueDouble(nameof(HotpixelThreshold), 0.001d);
            saturationThreshold = optionsAccessor.GetValueDouble(nameof(SaturationThreshold), 0.99d);
            excludeSaturatedStarsFromHFR = optionsAccessor.GetValueBoolean(nameof(ExcludeSaturatedStarsFromHFR), true);
            measurementAverage = optionsAccessor.GetValueEnum<MeasurementAverageEnum>(nameof(MeasurementAverage), MeasurementAverageEnum.Median);
            psfPixelIntegration = optionsAccessor.GetValueBoolean(nameof(PSFPixelIntegration), false);
            useOptimizedSettings = optionsAccessor.GetValueBoolean(nameof(UseOptimizedSettings), false);
            var optimizedSettingsJson = optionsAccessor.GetValueString(OptimizedSettingsJsonKey, "");
            optimizedSettings = null;
            if (!string.IsNullOrEmpty(optimizedSettingsJson)) {
                try {
                    optimizedSettings = JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(optimizedSettingsJson);
                } catch (Exception ex) {
                    Logger.Warning($"Discarding corrupt OptimizedSettingsJson: {ex.Message}");
                }
            }
            ConfigureSimpleSettings();
        }

        public void ResetDefaults() {
            UseAdvanced = false;
            DebugMode = false;
            ModelPSF = true;
            PSFFitType = StarDetectorPSFFitType.Moffat_40;
            Simple_NoiseLevel = NoiseLevelEnum.Typical;
            Simple_PixelScale = PixelScaleEnum.Typical;
            Simple_FocusRange = FocusRangeEnum.Typical;
            HotpixelFiltering = true;
            HotpixelThresholdingEnabled = true;
            UseAutoFocusCrop = true;
            StarMeasurementNoiseReductionEnabled = false;
            NoiseReductionRadius = 3;
            NoiseClippingMultiplier = 2.0; // lowered 4→2 per the golden-set recall audit (candidate-formation bottleneck)
            StarClippingMultiplier = 2.0;
            ContaminationSensitivity = 5.0;
            RejectContaminatedStars = true;
            StructureLayers = 4;
            DefocusAwareStructure = false;
            StructureLayerBoost = 0;
            BrightnessSensitivity = 2.0;
            StarPeakResponse = 0.75;
            MaxDistortion = 0.5;
            DefocusAwareGates = false;
            DefocusDistortionSizeReference = 30.0;
            DefocusDistortionMinFactor = 0.25;
            DefocusCenteringToleranceFactor = 2.0;
            DefocusAwareDonutDetection = false;
            DonutMorphCloseSize = 5;
            LocallyAdaptiveBinarization = true;   // default ON (AF-bank validated)
            AdaptiveNoiseBlockSize = 128;
            DonutMinAnnularityHoleFraction = 0.15;
            DonutMaxStreakEccentricity = 1.0;
            DonutSaturationBloomRadius = 0.0;
            StarCenterTolerance = 0.3;
            StarBackgroundBoxExpansion = 3;
            MinStarBoundingBoxSize = 5;
            MinHFR = 1.2;
            StructureDilationSize = 3;
            StructureDilationCount = 0;
            PixelSampleSize = 1.0;
            IntermediateSavePath = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "HocusFocusIntermediate");
            if (!Directory.Exists(IntermediateSavePath)) {
                Directory.CreateDirectory(IntermediateSavePath);
            }
            SaveIntermediateImages = false;
            PSFParallelPartitionSize = 100;
            PSFResolution = 10;
            PSFFitThreshold = 0.9;
            UsePSFAbsoluteDeviation = false;
            HotpixelThreshold = 0.001d;
            SaturationThreshold = 0.99d;
            ExcludeSaturatedStarsFromHFR = true;
            MeasurementAverage = MeasurementAverageEnum.Median;
            PSFPixelIntegration = false;
            optimizedSettings = null;
            optionsAccessor.SetValueString(OptimizedSettingsJsonKey, "");
            RaisePropertyChanged(nameof(HasOptimizedSettings));
            UseOptimizedSettings = false;
        }

        private bool debugMode;

        public bool DebugMode {
            get => debugMode;
            set {
                if (debugMode != value) {
                    debugMode = value;
                    optionsAccessor.SetValueBoolean("DetectionDebugMode", debugMode);
                    RaisePropertyChanged();
                }
            }
        }

        private bool useAdvanced;

        public bool UseAdvanced {
            get => useAdvanced;
            set {
                if (useAdvanced != value) {
                    useAdvanced = value;
                    optionsAccessor.SetValueBoolean("UseAdvanced", useAdvanced);
                    ConfigureSimpleSettings();
                    RaisePropertyChanged();
                }
            }
        }

        private bool modelPSF;

        public bool ModelPSF {
            get => modelPSF;
            set {
                if (modelPSF != value) {
                    modelPSF = value;
                    optionsAccessor.SetValueBoolean("ModelPSF", modelPSF);
                    RaisePropertyChanged();
                }
            }
        }

        private bool starMeasurementNoiseReductionEnabled;

        public bool StarMeasurementNoiseReductionEnabled {
            get => starMeasurementNoiseReductionEnabled;
            set {
                if (starMeasurementNoiseReductionEnabled != value) {
                    starMeasurementNoiseReductionEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(StarMeasurementNoiseReductionEnabled), starMeasurementNoiseReductionEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private StarDetectorPSFFitType psfFitType;

        public StarDetectorPSFFitType PSFFitType {
            get => psfFitType;
            set {
                if (psfFitType != value) {
                    psfFitType = value;
                    optionsAccessor.SetValueEnum<StarDetectorPSFFitType>("PSFFitType", value);
                    RaisePropertyChanged();
                }
            }
        }

        private NoiseLevelEnum simple_NoiseLevel;

        public NoiseLevelEnum Simple_NoiseLevel {
            get => simple_NoiseLevel;
            set {
                if (simple_NoiseLevel != value) {
                    simple_NoiseLevel = value;
                    optionsAccessor.SetValueEnum<NoiseLevelEnum>("Simple_NoiseLevel", value);
                    ConfigureSimpleSettings();
                    RaisePropertyChanged();
                }
            }
        }

        private PixelScaleEnum simple_PixelScale;

        public PixelScaleEnum Simple_PixelScale {
            get => simple_PixelScale;
            set {
                if (simple_PixelScale != value) {
                    simple_PixelScale = value;
                    optionsAccessor.SetValueEnum<PixelScaleEnum>("Simple_PixelScale", value);
                    ConfigureSimpleSettings();
                    RaisePropertyChanged();
                }
            }
        }

        private FocusRangeEnum simple_FocusRange;

        public FocusRangeEnum Simple_FocusRange {
            get => simple_FocusRange;
            set {
                if (simple_FocusRange != value) {
                    simple_FocusRange = value;
                    optionsAccessor.SetValueEnum<FocusRangeEnum>("Simple_FocusRange", value);
                    ConfigureSimpleSettings();
                    RaisePropertyChanged();
                }
            }
        }

        private bool hotpixelFiltering;

        public bool HotpixelFiltering {
            get => hotpixelFiltering;
            set {
                if (hotpixelFiltering != value) {
                    hotpixelFiltering = value;
                    optionsAccessor.SetValueBoolean("HotpixelFiltering", hotpixelFiltering);
                    RaisePropertyChanged();
                }
            }
        }

        private bool hotpixelThresholdingEnabled;

        public bool HotpixelThresholdingEnabled {
            get => hotpixelThresholdingEnabled;
            set {
                if (hotpixelThresholdingEnabled != value) {
                    hotpixelThresholdingEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(HotpixelThresholdingEnabled), hotpixelThresholdingEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool useAutoFocusCrop;

        public bool UseAutoFocusCrop {
            get => useAutoFocusCrop;
            set {
                if (useAutoFocusCrop != value) {
                    useAutoFocusCrop = value;
                    optionsAccessor.SetValueBoolean("UseAutoFocusCrop", useAutoFocusCrop);
                    RaisePropertyChanged();
                }
            }
        }

        private int noiseReductionRadius;

        public int NoiseReductionRadius {
            get => noiseReductionRadius;
            set {
                if (noiseReductionRadius != value) {
                    if (value < 0) {
                        throw new ArgumentException("NoiseReductionRadius must be non-negative", "NoiseReductionRadius");
                    }
                    noiseReductionRadius = value;
                    optionsAccessor.SetValueInt32("NoiseReductionRadius", noiseReductionRadius);
                    RaisePropertyChanged();
                }
            }
        }

        private double noiseClippingMultiplier;

        public double NoiseClippingMultiplier {
            get => noiseClippingMultiplier;
            set {
                if (noiseClippingMultiplier != value) {
                    if (value < 0) {
                        throw new ArgumentException("NoiseClippingMultiplier must be non-negative", "NoiseClippingMultiplier");
                    }
                    noiseClippingMultiplier = value;
                    optionsAccessor.SetValueDouble("NoiseClippingMultiplier", noiseClippingMultiplier);
                    RaisePropertyChanged();
                }
            }
        }

        private double starClippingMultiplier;

        private double contaminationSensitivity;
        public double ContaminationSensitivity {
            get => contaminationSensitivity;
            set {
                if (value < 0.0) {
                    value = 0.0;
                }
                if (contaminationSensitivity != value) {
                    contaminationSensitivity = value;
                    optionsAccessor.SetValueDouble("ContaminationSensitivity", contaminationSensitivity);
                    RaisePropertyChanged();
                }
            }
        }

        private bool rejectContaminatedStars;
        public bool RejectContaminatedStars {
            get => rejectContaminatedStars;
            set {
                if (rejectContaminatedStars != value) {
                    rejectContaminatedStars = value;
                    optionsAccessor.SetValueBoolean("RejectContaminatedStars", rejectContaminatedStars);
                    RaisePropertyChanged();
                }
            }
        }

        public double StarClippingMultiplier {
            get => starClippingMultiplier;
            set {
                if (starClippingMultiplier != value) {
                    if (value < 0) {
                        throw new ArgumentException("StarClippingMultiplier must be non-negative", "StarClippingMultiplier");
                    }
                    starClippingMultiplier = value;
                    optionsAccessor.SetValueDouble("StarClippingMultiplier", starClippingMultiplier);
                    RaisePropertyChanged();
                }
            }
        }

        private int structureLayers;

        public int StructureLayers {
            get => structureLayers;
            set {
                if (structureLayers != value) {
                    if (value <= 0) {
                        throw new ArgumentException("StructureLayers must be positive", "StructureLayers");
                    }
                    structureLayers = value;
                    optionsAccessor.SetValueInt32("StructureLayers", structureLayers);
                    RaisePropertyChanged();
                }
            }
        }

        private double starCenterTolerance;

        public double StarCenterTolerance {
            get => starCenterTolerance;
            set {
                if (starCenterTolerance != value) {
                    if (value <= 0.0 || value > 1.0) {
                        throw new ArgumentException("StarCenterTolerance must be positive and <= 1.0", "StarCenterTolerance");
                    }
                    starCenterTolerance = value;
                    optionsAccessor.SetValueDouble("StarCenterTolerance", starCenterTolerance);
                    RaisePropertyChanged();
                }
            }
        }

        private double starPeakResponse;

        public double StarPeakResponse {
            get => starPeakResponse;
            set {
                if (starPeakResponse != value) {
                    if (value <= 0) {
                        throw new ArgumentException("StarPeakResponse must be positive", "StarPeakResponse");
                    }
                    starPeakResponse = value;
                    optionsAccessor.SetValueDouble("StarPeakResponse", starPeakResponse);
                    RaisePropertyChanged();
                }
            }
        }

        private double maxDistortion;

        public double MaxDistortion {
            get => maxDistortion;
            set {
                if (maxDistortion != value) {
                    if (value < 0.0 || value > 1.0) {
                        throw new ArgumentException("MaxDistortion must be within [0, 1]", "MaxDistortion");
                    }
                    maxDistortion = value;
                    optionsAccessor.SetValueDouble("MaxDistortion", maxDistortion);
                    RaisePropertyChanged();
                }
            }
        }

        private bool defocusAwareGates;

        // Opt-in, Advanced-only. Default OFF so detection stays bit-identical when disabled. A single toggle that
        // drives BOTH defocus-aware gate relaxations (it maps to StarDetectorParams.DefocusAwareDistortion AND
        // DefocusAwareCentering in BuildStarDetectorParams). When ON: the TooDistorted gate relaxes its fill-ratio
        // threshold for LARGE candidates (large-defocus donut stars), recovering donuts the strict ratio would
        // reject; and the NotCentered gate relaxes its StarCenterTolerance (grows the centered acceptance sub-box)
        // for those same large candidates whose hollow ring destabilizes the centroid. The three numeric tuning
        // knobs (DefocusDistortionSizeReference / DefocusDistortionMinFactor / DefocusCenteringToleranceFactor) are
        // separate Advanced options that only take effect while this toggle is ON.
        public bool DefocusAwareGates {
            get => defocusAwareGates;
            set {
                if (defocusAwareGates != value) {
                    defocusAwareGates = value;
                    optionsAccessor.SetValueBoolean("DefocusAwareGates", defocusAwareGates);
                    RaisePropertyChanged();
                }
            }
        }

        private double defocusDistortionSizeReference;

        // Advanced-only tuning knob for the defocus-aware gates (only consulted while DefocusAwareGates is ON). The
        // candidate bbox max-dimension (px) at/below which the strict gate thresholds apply; larger candidates get
        // the relaxed thresholds. Shared by both the distortion and centering relaxations. Must be > 0. Default 30.
        public double DefocusDistortionSizeReference {
            get => defocusDistortionSizeReference;
            set {
                if (defocusDistortionSizeReference != value) {
                    if (value < 1.0 || value > 1000.0) {
                        throw new ArgumentException("DefocusDistortionSizeReference must be within [1, 1000]", "DefocusDistortionSizeReference");
                    }
                    defocusDistortionSizeReference = value;
                    optionsAccessor.SetValueDouble("DefocusDistortionSizeReference", defocusDistortionSizeReference);
                    RaisePropertyChanged();
                }
            }
        }

        private double defocusDistortionMinFactor;

        // Advanced-only tuning knob for the defocus-aware gates (only consulted while DefocusAwareGates is ON). The
        // floor multiplier on MaxDistortion for very large candidates (the most permissive the distortion gate ever
        // becomes). Must be in (0, 1]. Default 0.25.
        public double DefocusDistortionMinFactor {
            get => defocusDistortionMinFactor;
            set {
                if (defocusDistortionMinFactor != value) {
                    if (value < 0.01 || value > 1.0) {
                        throw new ArgumentException("DefocusDistortionMinFactor must be within [0.01, 1]", "DefocusDistortionMinFactor");
                    }
                    defocusDistortionMinFactor = value;
                    optionsAccessor.SetValueDouble("DefocusDistortionMinFactor", defocusDistortionMinFactor);
                    RaisePropertyChanged();
                }
            }
        }

        private double defocusCenteringToleranceFactor;

        // Advanced-only tuning knob for the defocus-aware gates (only consulted while DefocusAwareGates is ON). The
        // max multiplier applied to StarCenterTolerance for very large candidates (the most permissive the
        // NotCentered gate ever becomes). Must be >= 1.0 (>1 relaxes; 1.0 is a no-op). Default 2.0.
        public double DefocusCenteringToleranceFactor {
            get => defocusCenteringToleranceFactor;
            set {
                if (defocusCenteringToleranceFactor != value) {
                    if (value < 1.0 || value > 10.0) {
                        throw new ArgumentException("DefocusCenteringToleranceFactor must be within [1, 10]", "DefocusCenteringToleranceFactor");
                    }
                    defocusCenteringToleranceFactor = value;
                    optionsAccessor.SetValueDouble("DefocusCenteringToleranceFactor", defocusCenteringToleranceFactor);
                    RaisePropertyChanged();
                }
            }
        }

        private bool defocusAwareDonutDetection;

        // MASTER toggle for defocus-aware donut detection (opt-in, default OFF; surfaced on the optimizer wizard
        // start page AND in Advanced options). When OFF, detection is BIT-IDENTICAL to legacy: it forces every
        // defocus-aware behavior off in BuildStarDetectorParams (DefocusAwareDistortion/Centering/Structure AND the
        // new donut morph-close / hole-fill / streak / bloom knobs), and the optimizer omits all defocus axes. When
        // ON it recovers out-of-focus donut stars (morph-close + annularity hole-fill, active by default) and lets
        // the optimizer tune every defocus knob; spike/saturation suppression (streak + bloom) ships OFF and is
        // enabled by the optimizer via should-reject labels.
        public bool DefocusAwareDonutDetection {
            get => defocusAwareDonutDetection;
            set {
                if (defocusAwareDonutDetection != value) {
                    defocusAwareDonutDetection = value;
                    optionsAccessor.SetValueBoolean("DefocusAwareDonutDetection", defocusAwareDonutDetection);
                    RaisePropertyChanged();
                }
            }
        }

        private int donutMorphCloseSize;

        // Donut recovery (only while DefocusAwareDonutDetection is ON). Ellipse kernel diameter (px) for the
        // morphological CLOSE of the binarized structure map that reconnects fragmented donut rings into one
        // candidate (fixes TooSmall fragmentation). 1 = OFF (no close). EARLY param. Range [1, 25]. Default 5.
        public int DonutMorphCloseSize {
            get => donutMorphCloseSize;
            set {
                if (donutMorphCloseSize != value) {
                    if (value < 1 || value > 25) {
                        throw new ArgumentException("DonutMorphCloseSize must be within [1, 25]", "DonutMorphCloseSize");
                    }
                    donutMorphCloseSize = value;
                    optionsAccessor.SetValueInt32("DonutMorphCloseSize", donutMorphCloseSize);
                    RaisePropertyChanged();
                }
            }
        }

        private bool locallyAdaptiveBinarization;

        // Spatially-adaptive structure-map binarization (default OFF ⇒ bit-identical legacy detection). When ON the
        // single global binarize threshold is replaced by a local-median + NoiseClippingMultiplier·local-σ surface
        // (robust per-block statistics, bilinearly upsampled), making the same NoiseClippingMultiplier locally fair.
        // EARLY param. See docs/adaptive-noiseclip-design.md.
        public bool LocallyAdaptiveBinarization {
            get => locallyAdaptiveBinarization;
            set {
                if (locallyAdaptiveBinarization != value) {
                    locallyAdaptiveBinarization = value;
                    optionsAccessor.SetValueBoolean("LocallyAdaptiveBinarization", locallyAdaptiveBinarization);
                    RaisePropertyChanged();
                }
            }
        }

        private int adaptiveNoiseBlockSize;

        // Side length (px) of the square blocks the adaptive binarization surface is estimated on (only while
        // LocallyAdaptiveBinarization is ON). Larger = smoother/cheaper; smaller = more adaptive but noisier. EARLY
        // param. Range [16, 1024] (sane working range 64–256). Default 128 (matches snr_ref.coarse_bg grid).
        public int AdaptiveNoiseBlockSize {
            get => adaptiveNoiseBlockSize;
            set {
                if (adaptiveNoiseBlockSize != value) {
                    if (value < 16 || value > 1024) {
                        throw new ArgumentException("AdaptiveNoiseBlockSize must be within [16, 1024]", "AdaptiveNoiseBlockSize");
                    }
                    adaptiveNoiseBlockSize = value;
                    optionsAccessor.SetValueInt32("AdaptiveNoiseBlockSize", adaptiveNoiseBlockSize);
                    RaisePropertyChanged();
                }
            }
        }

        private double donutMinAnnularityHoleFraction;

        // Donut recovery (only while DefocusAwareDonutDetection is ON). Minimum enclosed-hole area (as a fraction of
        // the candidate bbox area) for a candidate to count as an annular donut whose hole is filled for the
        // TooDistorted fill-ratio test (DETECTION-ONLY: never changes flux/HFR/centroid). Range [0.02, 0.6].
        // Default 0.15.
        public double DonutMinAnnularityHoleFraction {
            get => donutMinAnnularityHoleFraction;
            set {
                if (donutMinAnnularityHoleFraction != value) {
                    if (value < 0.02 || value > 0.6) {
                        throw new ArgumentException("DonutMinAnnularityHoleFraction must be within [0.02, 0.6]", "DonutMinAnnularityHoleFraction");
                    }
                    donutMinAnnularityHoleFraction = value;
                    optionsAccessor.SetValueDouble("DonutMinAnnularityHoleFraction", donutMinAnnularityHoleFraction);
                    RaisePropertyChanged();
                }
            }
        }

        private double donutMaxStreakEccentricity;

        // Spike suppression (only while DefocusAwareDonutDetection is ON). Reject a candidate as a diffraction
        // spike / satellite trail when its point-cloud eccentricity >= this value. 1.0 = OFF (a perfect line has
        // eccentricity → 1.0, so the gate never fires). Lower it (e.g. 0.95) to enable. Range [0.8, 1.0].
        // Default 1.0 (OFF; the optimizer enables it via should-reject labels).
        public double DonutMaxStreakEccentricity {
            get => donutMaxStreakEccentricity;
            set {
                if (donutMaxStreakEccentricity != value) {
                    if (value < 0.8 || value > 1.0) {
                        throw new ArgumentException("DonutMaxStreakEccentricity must be within [0.8, 1.0]", "DonutMaxStreakEccentricity");
                    }
                    donutMaxStreakEccentricity = value;
                    optionsAccessor.SetValueDouble("DonutMaxStreakEccentricity", donutMaxStreakEccentricity);
                    RaisePropertyChanged();
                }
            }
        }

        private double donutSaturationBloomRadius;

        // Spike suppression (only while DefocusAwareDonutDetection is ON). Reject candidates whose centroid lies
        // within this many px of a saturated source (peak >= SaturationThreshold), suppressing bloom/halo
        // fragments around a bright saturated star. 0 = OFF. Range [0, 100]. Default 0 (OFF; optimizer enables it).
        public double DonutSaturationBloomRadius {
            get => donutSaturationBloomRadius;
            set {
                if (donutSaturationBloomRadius != value) {
                    if (value < 0.0 || value > 100.0) {
                        throw new ArgumentException("DonutSaturationBloomRadius must be within [0, 100]", "DonutSaturationBloomRadius");
                    }
                    donutSaturationBloomRadius = value;
                    optionsAccessor.SetValueDouble("DonutSaturationBloomRadius", donutSaturationBloomRadius);
                    RaisePropertyChanged();
                }
            }
        }

        private bool defocusAwareStructure;

        // Defocus-aware structure detection (opt-in, default OFF). When ON, the wavelet residual that removes
        // large-scale structure is computed at StructureLayers + StructureLayerBoost layers so large/donut
        // (heavily defocused) stars survive and form candidates. Default-OFF ⇒ candidate formation bit-identical.
        public bool DefocusAwareStructure {
            get => defocusAwareStructure;
            set {
                if (defocusAwareStructure != value) {
                    defocusAwareStructure = value;
                    optionsAccessor.SetValueBoolean("DefocusAwareStructure", defocusAwareStructure);
                    RaisePropertyChanged();
                }
            }
        }

        private int structureLayerBoost;

        // Advanced-only knob (only consulted while DefocusAwareStructure is ON): extra wavelet layers added to
        // StructureLayers for the residual-subtraction step. Higher ⇒ coarser residual ⇒ larger donuts survive.
        // Must be within [0, 6]. Default 0.
        public int StructureLayerBoost {
            get => structureLayerBoost;
            set {
                if (structureLayerBoost != value) {
                    if (value < 0 || value > 6) {
                        throw new ArgumentException("StructureLayerBoost must be within [0, 6]", "StructureLayerBoost");
                    }
                    structureLayerBoost = value;
                    optionsAccessor.SetValueInt32("StructureLayerBoost", structureLayerBoost);
                    RaisePropertyChanged();
                }
            }
        }

        private double brightnessSensitivity;

        public double BrightnessSensitivity {
            get => brightnessSensitivity;
            set {
                if (brightnessSensitivity != value) {
                    if (value < 0) {
                        throw new ArgumentException("BrightnessSensitivity must be non-negative", "BrightnessSensitivity");
                    }
                    brightnessSensitivity = value;
                    optionsAccessor.SetValueDouble("BrightnessSensitivity", brightnessSensitivity);
                    RaisePropertyChanged();
                }
            }
        }

        private int starBackgroundBoxExpansion;

        public int StarBackgroundBoxExpansion {
            get => starBackgroundBoxExpansion;
            set {
                if (starBackgroundBoxExpansion != value) {
                    if (value < 1) {
                        throw new ArgumentException("StarBackgroundBoxExpansion must be at least 1", "StarBackgroundBoxExpansion");
                    }
                    starBackgroundBoxExpansion = value;
                    optionsAccessor.SetValueInt32("StarBackgroundBoxExpansion", starBackgroundBoxExpansion);
                    RaisePropertyChanged();
                }
            }
        }

        private int minStarBoundingBoxSize;

        public int MinStarBoundingBoxSize {
            get => minStarBoundingBoxSize;
            set {
                if (minStarBoundingBoxSize != value) {
                    if (value < 1) {
                        throw new ArgumentException("MinStarBoundingBoxSize must be at least 1", "MinStarBoundingBoxSize");
                    }
                    minStarBoundingBoxSize = value;
                    optionsAccessor.SetValueInt32("MinStarBoundingBoxSize", minStarBoundingBoxSize);
                    RaisePropertyChanged();
                }
            }
        }

        private double minHFR;

        public double MinHFR {
            get => minHFR;
            set {
                if (minHFR != value) {
                    if (value < 0) {
                        throw new ArgumentException("MinHFR must be non-negative", "MinHFR");
                    }
                    minHFR = value;
                    optionsAccessor.SetValueDouble("MinHFR", minHFR);
                    RaisePropertyChanged();
                }
            }
        }

        private int structureDilationSize;

        public int StructureDilationSize {
            get => structureDilationSize;
            set {
                if (structureDilationSize != value) {
                    if (value < 3) {
                        throw new ArgumentException("StructureDilationSize must be at least 3", "StructureDilationSize");
                    }
                    structureDilationSize = value;
                    optionsAccessor.SetValueInt32("StructureDilationSize", structureDilationSize);
                    RaisePropertyChanged();
                }
            }
        }

        private int structureDilationCount;

        public int StructureDilationCount {
            get => structureDilationCount;
            set {
                if (structureDilationCount != value) {
                    if (value < 0) {
                        throw new ArgumentException("StructureDilationCount must be non-negative", "StructureDilationCount");
                    }
                    structureDilationCount = value;
                    optionsAccessor.SetValueInt32("StructureDilationCount", structureDilationCount);
                    RaisePropertyChanged();
                }
            }
        }

        private double pixelSampleSize;

        public double PixelSampleSize {
            get => pixelSampleSize;
            set {
                if (pixelSampleSize != value) {
                    if (value <= 0.0 || value > 1.0) {
                        throw new ArgumentException("PixelSampleSize must be within (0, 1]", "PixelSampleSize");
                    }
                    pixelSampleSize = value;
                    optionsAccessor.SetValueDouble("PixelSampleSize", pixelSampleSize);
                    RaisePropertyChanged();
                }
            }
        }

        private string intermediateSavePath;

        public string IntermediateSavePath {
            get => intermediateSavePath;
            set {
                if (intermediateSavePath != value) {
                    intermediateSavePath = value;
                    optionsAccessor.SetValueString(nameof(IntermediateSavePath), intermediateSavePath);
                    RaisePropertyChanged();
                }
            }
        }

        private bool saveIntermediateImages;

        public bool SaveIntermediateImages {
            get => saveIntermediateImages;
            set {
                if (saveIntermediateImages != value) {
                    saveIntermediateImages = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int psfParallelPartitionSize;

        public int PSFParallelPartitionSize {
            get => psfParallelPartitionSize;
            set {
                if (psfParallelPartitionSize != value) {
                    if (value < 0) {
                        throw new ArgumentException("PSFParallelPartitionSize must be non-negative", "PSFParallelPartitionSize");
                    }
                    psfParallelPartitionSize = value;
                    optionsAccessor.SetValueInt32("PSFParallelPartitionSize", psfParallelPartitionSize);
                    RaisePropertyChanged();
                }
            }
        }

        private int psfResolution;

        public int PSFResolution {
            get => psfResolution;
            set {
                if (psfResolution != value) {
                    if (value < 0) {
                        throw new ArgumentException("PSFResolution must be non-negative", "PSFResolution");
                    }
                    psfResolution = value;
                    optionsAccessor.SetValueInt32("PSFResolution", psfResolution);
                    RaisePropertyChanged();
                }
            }
        }

        private double psfFitThreshold;

        public double PSFFitThreshold {
            get => psfFitThreshold;
            set {
                if (psfFitThreshold != value) {
                    if (value <= 0.0 || value > 1.0) {
                        throw new ArgumentException("PSFFitThreshold must be within (0, 1]", "PSFFitThreshold");
                    }
                    psfFitThreshold = value;
                    optionsAccessor.SetValueDouble("PSFFitThreshold", psfFitThreshold);
                    RaisePropertyChanged();
                }
            }
        }

        private bool usePSFAbsoluteDeviation;

        public bool UsePSFAbsoluteDeviation {
            get => usePSFAbsoluteDeviation;
            set {
                if (usePSFAbsoluteDeviation != value) {
                    usePSFAbsoluteDeviation = value;
                    optionsAccessor.SetValueBoolean(nameof(UsePSFAbsoluteDeviation), usePSFAbsoluteDeviation);
                    RaisePropertyChanged();
                }
            }
        }

        private double hotpixelThreshold;

        public double HotpixelThreshold {
            get => hotpixelThreshold;
            set {
                if (hotpixelThreshold != value) {
                    if (value <= 0.0 || value > 1.0) {
                        throw new ArgumentException("HotpixelThreshold must be within (0, 1]", "HotpixelThreshold");
                    }
                    hotpixelThreshold = value;
                    optionsAccessor.SetValueDouble("HotpixelThreshold", hotpixelThreshold);
                    RaisePropertyChanged();
                }
            }
        }

        private double saturationThreshold;

        public double SaturationThreshold {
            get => saturationThreshold;
            set {
                if (saturationThreshold != value) {
                    if (value <= 0.0 || value > 1.0) {
                        throw new ArgumentException("SaturationThreshold must be within (0, 1]", "SaturationThreshold");
                    }
                    saturationThreshold = value;
                    optionsAccessor.SetValueDouble(nameof(SaturationThreshold), saturationThreshold);
                    RaisePropertyChanged();
                }
            }
        }

        private bool excludeSaturatedStarsFromHFR;

        public bool ExcludeSaturatedStarsFromHFR {
            get => excludeSaturatedStarsFromHFR;
            set {
                if (excludeSaturatedStarsFromHFR != value) {
                    excludeSaturatedStarsFromHFR = value;
                    optionsAccessor.SetValueBoolean(nameof(ExcludeSaturatedStarsFromHFR), excludeSaturatedStarsFromHFR);
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
                    optionsAccessor.SetValueEnum(nameof(MeasurementAverage), value);
                    RaisePropertyChanged();
                }
            }
        }

        private bool psfPixelIntegration;

        public bool PSFPixelIntegration {
            get => psfPixelIntegration;
            set {
                if (psfPixelIntegration != value) {
                    psfPixelIntegration = value;
                    optionsAccessor.SetValueBoolean(nameof(PSFPixelIntegration), psfPixelIntegration);
                    RaisePropertyChanged();
                }
            }
        }

        // Persisted JSON blob holding the optimized star-detection snapshot (Optimization Wizard, T1).
        private const string OptimizedSettingsJsonKey = "OptimizedSettingsJson";

        private OptimizedStarDetectionSettings optimizedSettings;

        public bool HasOptimizedSettings => optimizedSettings != null;

        private bool useOptimizedSettings;

        public bool UseOptimizedSettings {
            get => useOptimizedSettings;
            set {
                if (useOptimizedSettings != value) {
                    useOptimizedSettings = value;
                    optionsAccessor.SetValueBoolean(nameof(UseOptimizedSettings), useOptimizedSettings);
                    RaisePropertyChanged();
                }
            }
        }

        public OptimizedStarDetectionSettings GetOptimizedSettings() {
            return optimizedSettings?.Clone();
        }

        public void ApplyOptimizedSettings(OptimizedStarDetectionSettings settings) {
            if (settings == null) {
                throw new ArgumentNullException(nameof(settings));
            }
            optimizedSettings = settings.Clone();
            optionsAccessor.SetValueString(OptimizedSettingsJsonKey, JsonConvert.SerializeObject(settings));
            RaisePropertyChanged(nameof(HasOptimizedSettings));
            UseAdvanced = false;
            UseOptimizedSettings = true; // fires ConfigureSimpleSettings via PropertyChanged
            ConfigureSimpleSettings(); // ensure applied even if UseOptimizedSettings was already true
            RaiseAllPropertiesChanged(); // refresh UI bindings for all live advanced props
        }

        /// <summary>
        /// Restores the full star-detection configuration from <paramref name="source"/> — used by AutoFocus replay's
        /// "update profile to capture-time settings" option (c). Faithfully reproduces the captured MODE, not just the
        /// effective values: the optimized-settings snapshot, the Simple/Advanced flag, the "Use Optimized Settings"
        /// flag, the Simple-mode presets, and every advanced knob (including the machine-local
        /// <see cref="PSFParallelPartitionSize"/> and <see cref="DebugMode"/>). The local
        /// <see cref="IntermediateSavePath"/> / <see cref="SaveIntermediateImages"/> are intentionally NOT copied —
        /// they are machine-local and detection-irrelevant for replay.
        /// </summary>
        public void ApplyFullSnapshot(IStarDetectionOptions source) => ApplySnapshotCore(source, includeMachineLocalPerfKnobs: true);

        /// <summary>
        /// Cross-machine IMPORT of star-detection parameters. Same faithful mode / optimized-layer / preset / knob
        /// restore as <see cref="ApplyFullSnapshot"/>, but additionally leaves the machine-local
        /// <see cref="PSFParallelPartitionSize"/> (CPU parallelism) and <see cref="DebugMode"/> (local diagnostics)
        /// untouched — alongside the already-excluded <see cref="IntermediateSavePath"/> /
        /// <see cref="SaveIntermediateImages"/> — so an imaging machine keeps its own parallelism and diagnostics when
        /// importing settings tuned on a different machine.
        /// </summary>
        public void ApplyImportedSnapshot(IStarDetectionOptions source) => ApplySnapshotCore(source, includeMachineLocalPerfKnobs: false);

        private void ApplySnapshotCore(IStarDetectionOptions source, bool includeMachineLocalPerfKnobs) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            // 1) Restore the curated optimized-settings snapshot storage (or clear it). ApplyOptimizedSettings flips
            //    UseAdvanced/UseOptimizedSettings and recomputes the live knobs — all overridden below.
            var optimized = source.GetOptimizedSettings();
            if (optimized != null) {
                ApplyOptimizedSettings(optimized);
            } else {
                ClearOptimizedSettings();
            }

            // 2) Restore the Simple-mode presets and the exact mode flags. In Simple mode these trigger the recompute;
            //    that is fine — step 3 overwrites every knob afterward.
            Simple_NoiseLevel = source.Simple_NoiseLevel;
            Simple_PixelScale = source.Simple_PixelScale;
            Simple_FocusRange = source.Simple_FocusRange;
            UseOptimizedSettings = source.UseOptimizedSettings;
            UseAdvanced = source.UseAdvanced;

            // 3) Copy every knob verbatim LAST. Setting these non-"Simple" properties does NOT re-trigger the
            //    Simple-mode recompute (only Simple_*/UseAdvanced/UseOptimizedSettings do), so the captured resolved
            //    values stick exactly in either mode — and they already equal what the recompute would produce.
            ApplyKnobs(source, includeMachineLocalPerfKnobs);

            RaiseAllPropertiesChanged();
        }

        // Copies every advanced knob (not the Simple_* presets, the mode flags, or the machine-local intermediate-save
        // settings) from a source. Used as the final step of ApplySnapshotCore. When includeMachineLocalPerfKnobs is
        // false (cross-machine import), the machine-local DebugMode and PSFParallelPartitionSize are left untouched.
        private void ApplyKnobs(IStarDetectionOptions source, bool includeMachineLocalPerfKnobs) {
            ModelPSF = source.ModelPSF;
            HotpixelFiltering = source.HotpixelFiltering;
            HotpixelThresholdingEnabled = source.HotpixelThresholdingEnabled;
            UseAutoFocusCrop = source.UseAutoFocusCrop;
            StarMeasurementNoiseReductionEnabled = source.StarMeasurementNoiseReductionEnabled;
            NoiseReductionRadius = source.NoiseReductionRadius;
            NoiseClippingMultiplier = source.NoiseClippingMultiplier;
            StarClippingMultiplier = source.StarClippingMultiplier;
            ContaminationSensitivity = source.ContaminationSensitivity;
            RejectContaminatedStars = source.RejectContaminatedStars;
            StructureLayers = source.StructureLayers;
            DefocusAwareStructure = source.DefocusAwareStructure;
            StructureLayerBoost = source.StructureLayerBoost;
            BrightnessSensitivity = source.BrightnessSensitivity;
            StarPeakResponse = source.StarPeakResponse;
            MaxDistortion = source.MaxDistortion;
            DefocusAwareGates = source.DefocusAwareGates;
            DefocusDistortionSizeReference = source.DefocusDistortionSizeReference;
            DefocusDistortionMinFactor = source.DefocusDistortionMinFactor;
            DefocusCenteringToleranceFactor = source.DefocusCenteringToleranceFactor;
            DefocusAwareDonutDetection = source.DefocusAwareDonutDetection;
            DonutMorphCloseSize = source.DonutMorphCloseSize;
            LocallyAdaptiveBinarization = source.LocallyAdaptiveBinarization;
            AdaptiveNoiseBlockSize = source.AdaptiveNoiseBlockSize;
            DonutMinAnnularityHoleFraction = source.DonutMinAnnularityHoleFraction;
            DonutMaxStreakEccentricity = source.DonutMaxStreakEccentricity;
            DonutSaturationBloomRadius = source.DonutSaturationBloomRadius;
            StarCenterTolerance = source.StarCenterTolerance;
            StarBackgroundBoxExpansion = source.StarBackgroundBoxExpansion;
            MinStarBoundingBoxSize = source.MinStarBoundingBoxSize;
            MinHFR = source.MinHFR;
            StructureDilationSize = source.StructureDilationSize;
            StructureDilationCount = source.StructureDilationCount;
            PixelSampleSize = source.PixelSampleSize;
            if (includeMachineLocalPerfKnobs) {
                // Machine-local: copied for same-machine replay (option c), skipped for cross-machine import so the
                // imaging machine keeps its own parallelism / diagnostics settings.
                DebugMode = source.DebugMode;
                PSFParallelPartitionSize = source.PSFParallelPartitionSize;
            }
            PSFFitType = source.PSFFitType;
            PSFResolution = source.PSFResolution;
            PSFFitThreshold = source.PSFFitThreshold;
            UsePSFAbsoluteDeviation = source.UsePSFAbsoluteDeviation;
            HotpixelThreshold = source.HotpixelThreshold;
            SaturationThreshold = source.SaturationThreshold;
            ExcludeSaturatedStarsFromHFR = source.ExcludeSaturatedStarsFromHFR;
            MeasurementAverage = source.MeasurementAverage;
            PSFPixelIntegration = source.PSFPixelIntegration;
        }

        /// <summary>Clears any stored optimized-settings snapshot (and its persisted JSON), leaving the options with
        /// no optimized settings. Used to undo a transient <see cref="ApplyOptimizedSettings"/> (e.g. after a tilt
        /// calibration replay) for a profile that had none to begin with. Does not change the Use* flags.</summary>
        public void ClearOptimizedSettings() {
            optimizedSettings = null;
            optionsAccessor.SetValueString(OptimizedSettingsJsonKey, "");
            RaisePropertyChanged(nameof(HasOptimizedSettings));
            ConfigureSimpleSettings();
            RaiseAllPropertiesChanged();
        }

    }
}