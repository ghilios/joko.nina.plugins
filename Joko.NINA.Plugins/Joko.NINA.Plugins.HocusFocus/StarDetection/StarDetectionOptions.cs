#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [JsonObject]
    public class StarDetectionOptions : BaseINPC, IStarDetectionOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public StarDetectionOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            profileService.ProfileChanged += ProfileService_ProfileChanged;

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            this.PropertyChanged += StarDetectionOptions_PropertyChanged;
            InitializeOptions();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        private HashSet<String> SimplePropertyNames = new HashSet<string>() {
            nameof(Simple_NoiseLevel),
            nameof(Simple_PixelScale),
            nameof(Simple_FocusRange),
        };

        private void ConfigureSimpleSettings() {
            if (this.UseAdvanced) {
                return;
            }

            HotpixelFiltering = Simple_NoiseLevel != NoiseLevelEnum.None;
            switch (Simple_NoiseLevel) {
                case NoiseLevelEnum.None:
                    NoiseReductionRadius = 0;
                    break;

                case NoiseLevelEnum.Typical:
                    NoiseReductionRadius = 3;
                    break;

                case NoiseLevelEnum.High:
                    NoiseReductionRadius = 5;
                    break;
            }
            NoiseClippingMultiplier = 4;
            StarClippingMultiplier = 2;
            StructureLayers = 4;
            if (Simple_FocusRange == FocusRangeEnum.WideRange) {
                StructureLayers += 1;
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
            }
            BrightnessSensitivity = 10.0;
            StarPeakResponse = 0.75;
            MaxDistortion = 0.5;
            StarCenterTolerance = 0.3;
            StarBackgroundBoxExpansion = 3;
            MinHFR = 1.5;
            StructureDilationSize = 3;
            StructureDilationCount = 0;
            PSFFitType = StarDetectorPSFFitType.Moffat_40;
            // TODO: Consider increasing the resolution for long focal lengths
            PSFResolution = 10;
            PSFFitThreshold = 0.9;
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
            useAutoFocusCrop = optionsAccessor.GetValueBoolean("UseAutoFocusCrop", true);
            noiseReductionRadius = optionsAccessor.GetValueInt32("NoiseReductionRadius", 3);
            noiseClippingMultiplier = optionsAccessor.GetValueDouble("NoiseClippingMultiplier", 4.0);
            starClippingMultiplier = optionsAccessor.GetValueDouble("StarClippingMultiplier", 2.0);
            structureLayers = optionsAccessor.GetValueInt32("StructureLayers", 4);
            brightnessSensitivity = optionsAccessor.GetValueDouble("BrightnessSensitivity", 10.0);
            starPeakResponse = optionsAccessor.GetValueDouble("StarPeakResponse", 0.75);
            maxDistortion = optionsAccessor.GetValueDouble("MaxDistortion", 0.5);
            starCenterTolerance = optionsAccessor.GetValueDouble("StarCenterTolerance", 0.3);
            starBackgroundBoxExpansion = optionsAccessor.GetValueInt32("StarBackgroundBoxExpansion", 3);
            minStarBoundingBoxSize = optionsAccessor.GetValueInt32("MinStarBoundingBoxSize", 5);
            minHFR = optionsAccessor.GetValueDouble("MinHFR", 1.5);
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
            ConfigureSimpleSettings();
        }

        public void ResetDefaults() {
            UseAdvanced = false;
            DebugMode = false;
            ModelPSF = true;
            PSFFitType = StarDetectorPSFFitType.Moffat_40;
            Simple_NoiseLevel = NoiseLevelEnum.Typical;
            Simple_PixelScale = PixelScaleEnum.Typical;
            simple_FocusRange = FocusRangeEnum.Typical;
            HotpixelFiltering = true;
            UseAutoFocusCrop = true;
            NoiseReductionRadius = 3;
            NoiseClippingMultiplier = 4.0;
            StarClippingMultiplier = 2.0;
            StructureLayers = 4;
            BrightnessSensitivity = 10.0;
            StarPeakResponse = 0.6;
            MaxDistortion = 0.5;
            StarCenterTolerance = 0.3;
            StarBackgroundBoxExpansion = 3;
            MinStarBoundingBoxSize = 5;
            MinHFR = 1.5;
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
        }

        private bool debugMode;

        public bool DebugMode {
            get => debugMode;
            set {
                if (debugMode != value) {
                    debugMode = value;
                    optionsAccessor.SetValueBoolean("DetectionDebugMode", hotpixelFiltering);
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

        private bool useAutoFocusCrop;

        public bool UseAutoFocusCrop {
            get => useAutoFocusCrop;
            set {
                if (useAutoFocusCrop != value) {
                    useAutoFocusCrop = value;
                    optionsAccessor.SetValueBoolean("UseAutoFocusCrop", hotpixelFiltering);
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
    }
}