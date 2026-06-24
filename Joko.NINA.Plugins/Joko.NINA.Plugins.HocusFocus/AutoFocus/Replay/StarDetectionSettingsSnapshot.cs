#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>
    /// A flat, profile-detached <see cref="IStarDetectionOptions"/> implementation: plain auto-properties with no
    /// <see cref="NINA.Profile.Interfaces.IPluginOptionsAccessor"/> backing, no profile subscription, and none of
    /// <see cref="StarDetectionOptions"/>'s Simple-mode <c>ConfigureSimpleSettings</c> recompute side effects. It
    /// serves two roles in the replay feature:
    ///   1. the serialized <c>StarDetection</c> payload of <see cref="AutoFocusReplayMetadata"/> (it is concrete and
    ///      round-trips through Newtonsoft with no TypeNameHandling), and
    ///   2. the <c>StarDetectionOptionsOverride</c> the auto-focus engine consumes to replay a saved run with its
    ///      capture-time detection settings WITHOUT mutating the live profile singleton.
    /// <para>
    /// The snapshot stores the already-resolved advanced knob values (reading a live options object's interface
    /// getters returns the effective configuration regardless of its Simple/Advanced mode), and it has no recompute
    /// logic, so its getters return exactly what was stored. For the replay OVERRIDE (option b) only those resolved
    /// knobs matter (<see cref="HocusFocusStarDetection.BuildStarDetectorParams(IStarDetectionOptions)"/> reads the
    /// knobs, never the mode flags). For "update profile" (option c) the mode is restored faithfully, so the snapshot
    /// ALSO records the real <see cref="UseAdvanced"/>/<see cref="UseOptimizedSettings"/> flags and the curated
    /// <see cref="OptimizedSettings"/> snapshot.
    /// </para>
    /// </summary>
    public sealed class StarDetectionSettingsSnapshot : IStarDetectionOptions {
        public bool UseAdvanced { get; set; } = true;
        public bool ModelPSF { get; set; }
        public NoiseLevelEnum Simple_NoiseLevel { get; set; }
        public PixelScaleEnum Simple_PixelScale { get; set; }
        public FocusRangeEnum Simple_FocusRange { get; set; }
        public bool HotpixelFiltering { get; set; }
        public bool HotpixelThresholdingEnabled { get; set; }
        public bool UseAutoFocusCrop { get; set; }
        public int NoiseReductionRadius { get; set; }
        public double NoiseClippingMultiplier { get; set; }
        public double StarClippingMultiplier { get; set; }
        public double ContaminationSensitivity { get; set; }
        public bool RejectContaminatedStars { get; set; }
        public int StructureLayers { get; set; }
        public bool DefocusAwareStructure { get; set; }
        public int StructureLayerBoost { get; set; }
        public double BrightnessSensitivity { get; set; }
        public double StarPeakResponse { get; set; }
        public double MaxDistortion { get; set; }
        public bool DefocusAwareGates { get; set; }
        public double DefocusDistortionSizeReference { get; set; }
        public double DefocusDistortionMinFactor { get; set; }
        public double DefocusCenteringToleranceFactor { get; set; }
        public bool DefocusAwareDonutDetection { get; set; }
        public int DonutMorphCloseSize { get; set; }
        public bool LocallyAdaptiveBinarization { get; set; }
        public int AdaptiveNoiseBlockSize { get; set; } = 128;
        public double DonutMinAnnularityHoleFraction { get; set; }
        public double DonutMaxStreakEccentricity { get; set; }
        public double DonutSaturationBloomRadius { get; set; }
        public double StarCenterTolerance { get; set; }
        public int StarBackgroundBoxExpansion { get; set; }
        public int MinStarBoundingBoxSize { get; set; }
        public double MinHFR { get; set; }
        public int StructureDilationSize { get; set; }
        public int StructureDilationCount { get; set; }
        public double PixelSampleSize { get; set; }
        public bool DebugMode { get; set; }
        public string IntermediateSavePath { get; set; } = "";
        public bool SaveIntermediateImages { get; set; }
        public int PSFParallelPartitionSize { get; set; }
        public bool StarMeasurementNoiseReductionEnabled { get; set; }
        public StarDetectorPSFFitType PSFFitType { get; set; }
        public int PSFResolution { get; set; }
        public double PSFFitThreshold { get; set; }
        public bool UsePSFAbsoluteDeviation { get; set; }
        public double HotpixelThreshold { get; set; }
        public double SaturationThreshold { get; set; }
        public MeasurementAverageEnum MeasurementAverage { get; set; }
        public bool PSFPixelIntegration { get; set; }

        public bool UseOptimizedSettings { get; set; }

        /// <summary>The curated optimized-settings snapshot the run had stored (null if none). Recorded so "update
        /// profile" (option c) can restore the optimized layer + Simple-mode + the "Use Optimized Settings" toggle.</summary>
        public OptimizedStarDetectionSettings OptimizedSettings { get; set; }

        [JsonIgnore]
        public bool HasOptimizedSettings => OptimizedSettings != null;

        public OptimizedStarDetectionSettings GetOptimizedSettings() => OptimizedSettings?.Clone();

        public void ApplyOptimizedSettings(OptimizedStarDetectionSettings settings) => OptimizedSettings = settings?.Clone();

        /// <summary>Captures the fully-resolved effective configuration of a live options object into a flat,
        /// profile-detached snapshot, including the real mode flags and the optimized-settings snapshot so the
        /// capture-time mode can be restored.</summary>
        public static StarDetectionSettingsSnapshot FromOptions(IStarDetectionOptions o) {
            return new StarDetectionSettingsSnapshot() {
                UseAdvanced = o.UseAdvanced,
                OptimizedSettings = o.GetOptimizedSettings(),
                ModelPSF = o.ModelPSF,
                Simple_NoiseLevel = o.Simple_NoiseLevel,
                Simple_PixelScale = o.Simple_PixelScale,
                Simple_FocusRange = o.Simple_FocusRange,
                HotpixelFiltering = o.HotpixelFiltering,
                HotpixelThresholdingEnabled = o.HotpixelThresholdingEnabled,
                UseAutoFocusCrop = o.UseAutoFocusCrop,
                NoiseReductionRadius = o.NoiseReductionRadius,
                NoiseClippingMultiplier = o.NoiseClippingMultiplier,
                StarClippingMultiplier = o.StarClippingMultiplier,
                ContaminationSensitivity = o.ContaminationSensitivity,
                RejectContaminatedStars = o.RejectContaminatedStars,
                StructureLayers = o.StructureLayers,
                DefocusAwareStructure = o.DefocusAwareStructure,
                StructureLayerBoost = o.StructureLayerBoost,
                BrightnessSensitivity = o.BrightnessSensitivity,
                StarPeakResponse = o.StarPeakResponse,
                MaxDistortion = o.MaxDistortion,
                DefocusAwareGates = o.DefocusAwareGates,
                DefocusDistortionSizeReference = o.DefocusDistortionSizeReference,
                DefocusDistortionMinFactor = o.DefocusDistortionMinFactor,
                DefocusCenteringToleranceFactor = o.DefocusCenteringToleranceFactor,
                DefocusAwareDonutDetection = o.DefocusAwareDonutDetection,
                DonutMorphCloseSize = o.DonutMorphCloseSize,
                LocallyAdaptiveBinarization = o.LocallyAdaptiveBinarization,
                AdaptiveNoiseBlockSize = o.AdaptiveNoiseBlockSize,
                DonutMinAnnularityHoleFraction = o.DonutMinAnnularityHoleFraction,
                DonutMaxStreakEccentricity = o.DonutMaxStreakEccentricity,
                DonutSaturationBloomRadius = o.DonutSaturationBloomRadius,
                StarCenterTolerance = o.StarCenterTolerance,
                StarBackgroundBoxExpansion = o.StarBackgroundBoxExpansion,
                MinStarBoundingBoxSize = o.MinStarBoundingBoxSize,
                MinHFR = o.MinHFR,
                StructureDilationSize = o.StructureDilationSize,
                StructureDilationCount = o.StructureDilationCount,
                PixelSampleSize = o.PixelSampleSize,
                DebugMode = o.DebugMode,
                IntermediateSavePath = o.IntermediateSavePath,
                SaveIntermediateImages = o.SaveIntermediateImages,
                PSFParallelPartitionSize = o.PSFParallelPartitionSize,
                StarMeasurementNoiseReductionEnabled = o.StarMeasurementNoiseReductionEnabled,
                PSFFitType = o.PSFFitType,
                PSFResolution = o.PSFResolution,
                PSFFitThreshold = o.PSFFitThreshold,
                UsePSFAbsoluteDeviation = o.UsePSFAbsoluteDeviation,
                HotpixelThreshold = o.HotpixelThreshold,
                SaturationThreshold = o.SaturationThreshold,
                MeasurementAverage = o.MeasurementAverage,
                PSFPixelIntegration = o.PSFPixelIntegration,
                UseOptimizedSettings = o.UseOptimizedSettings
            };
        }
    }
}
