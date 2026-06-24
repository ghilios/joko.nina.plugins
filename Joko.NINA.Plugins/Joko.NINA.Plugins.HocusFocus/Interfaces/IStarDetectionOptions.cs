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

    public interface IStarDetectionOptions {
        bool UseAdvanced { get; set; }
        bool ModelPSF { get; set; }

        // Simple configuration, which produces the detailed configuration with fewer knobs
        NoiseLevelEnum Simple_NoiseLevel { get; set; }

        PixelScaleEnum Simple_PixelScale { get; set; }
        FocusRangeEnum Simple_FocusRange { get; set; }

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
        bool StarMeasurementNoiseReductionEnabled { get; set; }
        StarDetectorPSFFitType PSFFitType { get; set; }
        int PSFResolution { get; set; }
        double PSFFitThreshold { get; set; }
        bool UsePSFAbsoluteDeviation { get; set; }
        double HotpixelThreshold { get; set; }
        double SaturationThreshold { get; set; }
        MeasurementAverageEnum MeasurementAverage { get; set; }
        bool PSFPixelIntegration { get; set; }

        // Optimized settings snapshot (Star Detection Optimization Wizard)
        bool HasOptimizedSettings { get; }

        bool UseOptimizedSettings { get; set; }

        OptimizedStarDetectionSettings GetOptimizedSettings();

        void ApplyOptimizedSettings(OptimizedStarDetectionSettings settings);
    }
}