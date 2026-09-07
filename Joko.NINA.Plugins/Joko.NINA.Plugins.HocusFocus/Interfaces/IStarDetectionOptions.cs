#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Converters;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum NoiseLevelEnum {

        [Description("Typical")]
        Typical,

        [Description("None")]
        None,

        [Description("High")]
        High,

        [Description("Low")]
        Low
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum PixelScaleEnum {

        [Description("Typical")]
        Typical,

        [Description("Wide Field")]
        WideField,

        [Description("Long Focal Length")]
        LongFocalLength
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum FocusRangeEnum {

        [Description("Typical")]
        Typical,

        [Description("Wide Range")]
        WideRange
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum MeasurementAverageEnum {

        [Description("Median")]
        Median,

        [Description("Mean + Outlier Detection")]
        MeanOutliers
    }

    /// <summary>
    /// The software binning factor star detection resamples the frame by BEFORE analyzing it. Detection-only:
    /// the image NINA displays, and every HFR/position Hocus Focus reports, stay at the resolution the camera
    /// delivered. The underlying values ARE the factors, so a factor round-trips through this enum.
    ///
    /// <para>There is deliberately NO "Auto" member. A factor that resolved itself from the profile would change
    /// detection behavior the moment a user upgraded, silently invalidating settings they had already tuned.
    /// <c>DetectionBinningResolver</c> instead RECOMMENDS a factor in the UI and leaves the choice explicit.</para>
    /// </summary>
    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum DetectionBinningEnum {

        [Description("1x1 (Off)")]
        Bin1 = 1,

        [Description("2x2")]
        Bin2 = 2,

        [Description("3x3")]
        Bin3 = 3,

        [Description("4x4")]
        Bin4 = 4
    }

    public interface IStarDetectionOptions {
        bool UseAdvanced { get; set; }
        bool ModelPSF { get; set; }

        // Simple configuration, which produces the detailed configuration with fewer knobs
        NoiseLevelEnum Simple_NoiseLevel { get; set; }

        PixelScaleEnum Simple_PixelScale { get; set; }
        FocusRangeEnum Simple_FocusRange { get; set; }

        // Detection-only software binning. Not derived by the Simple-mode presets — it applies in both modes.
        DetectionBinningEnum DetectionBinning { get; set; }

        // Fine-grained configuration
        bool HotpixelFiltering { get; set; }

        bool HotpixelThresholdingEnabled { get; set; }

        bool UseAutoFocusCrop { get; set; }
        int NoiseReductionRadius { get; set; }
        double NoiseClippingMultiplier { get; set; }
        double StarClippingMultiplier { get; set; }
        double ContaminationSensitivity { get; set; }
        bool RejectContaminatedStars { get; set; }
        int StructureLayers { get; set; }
        bool DefocusAwareStructure { get; set; }
        int StructureLayerBoost { get; set; }
        double BrightnessSensitivity { get; set; }
        double StarPeakResponse { get; set; }
        double MaxDistortion { get; set; }
        bool DefocusAwareGates { get; set; }
        double DefocusDistortionSizeReference { get; set; }
        double DefocusDistortionMinFactor { get; set; }
        double DefocusCenteringToleranceFactor { get; set; }
        bool DefocusAwareDonutDetection { get; set; }
        int DonutMorphCloseSize { get; set; }
        bool LocallyAdaptiveBinarization { get; set; }
        int AdaptiveNoiseBlockSize { get; set; }
        double DonutMinAnnularityHoleFraction { get; set; }
        double DonutMaxStreakEccentricity { get; set; }
        double DonutSaturationBloomRadius { get; set; }
        double StarCenterTolerance { get; set; }
        int StarBackgroundBoxExpansion { get; set; }
        int MinStarBoundingBoxSize { get; set; }
        double MinHFR { get; set; }
        int StructureDilationSize { get; set; }
        int StructureDilationCount { get; set; }
        double PixelSampleSize { get; set; }
        bool DebugMode { get; set; }
        string IntermediateSavePath { get; set; }
        bool SaveIntermediateImages { get; set; }
        int PSFParallelPartitionSize { get; set; }

        /// <summary>Machine-local (describes this computer's hardware, never imported/per-filter): allow the
        /// star-detection OPTIMIZATION wizard to run its early detection pipeline on a CUDA GPU when one is
        /// present and the GpuAccelerationPolicy heuristic approves. Autofocus, sensor modeling, and
        /// single-frame detection never use the GPU regardless of this setting.</summary>
        bool GpuAccelerationEnabled { get; set; }
        bool StarMeasurementNoiseReductionEnabled { get; set; }
        StarDetectorPSFFitType PSFFitType { get; set; }
        int PSFResolution { get; set; }
        double PSFFitThreshold { get; set; }
        bool UsePSFAbsoluteDeviation { get; set; }
        double HotpixelThreshold { get; set; }
        double SaturationThreshold { get; set; }
        bool ExcludeSaturatedStarsFromHFR { get; set; }
        MeasurementAverageEnum MeasurementAverage { get; set; }
        bool PSFPixelIntegration { get; set; }

        // Optimized settings snapshot (Star Detection Optimization Wizard)
        bool HasOptimizedSettings { get; }

        bool UseOptimizedSettings { get; set; }

        OptimizedStarDetectionSettings GetOptimizedSettings();

        void ApplyOptimizedSettings(OptimizedStarDetectionSettings settings);
    }
}