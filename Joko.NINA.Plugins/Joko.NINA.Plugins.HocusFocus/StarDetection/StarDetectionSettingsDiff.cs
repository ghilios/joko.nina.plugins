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
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>One changed star-detection setting, for the import confirmation table.</summary>
    public sealed class StarDetectionSettingDiffRow {
        public string Name { get; set; }
        public string CurrentValue { get; set; }
        public string NewValue { get; set; }
    }

    /// <summary>
    /// Computes the human-readable difference between the current star-detection settings and an imported snapshot,
    /// for the import confirmation dialog. The <see cref="ImportableSettings"/> map is the single source of truth for
    /// which <see cref="IStarDetectionOptions"/> properties transfer between machines and their UI labels (the labels
    /// match the Star Detection Options pane). The machine-local knobs in <see cref="ExcludedFromImport"/> are NOT
    /// shown or applied (see <see cref="StarDetectionOptions.ApplyImportedSnapshot"/>).
    /// </summary>
    public static class StarDetectionSettingsDiff {

        /// <summary>The machine-local knobs that are never imported (and therefore never shown in the diff). Kept here
        /// as the single source of truth; a unit test asserts ImportableSettings ∪ ExcludedFromImport covers exactly
        /// the read/write properties of <see cref="IStarDetectionOptions"/>.</summary>
        public static readonly IReadOnlyList<string> ExcludedFromImport = new[] {
            nameof(IStarDetectionOptions.PSFParallelPartitionSize),
            nameof(IStarDetectionOptions.DebugMode),
            nameof(IStarDetectionOptions.IntermediateSavePath),
            nameof(IStarDetectionOptions.SaveIntermediateImages),
        };

        /// <summary>Ordered (property name → UI label) for every importable star-detection setting. Labels are copied
        /// verbatim from the Star Detection Options pane (Resources/OptionsDataTemplates.xaml).</summary>
        public static readonly IReadOnlyList<(string Property, string Label)> ImportableSettings = new (string, string)[] {
            (nameof(IStarDetectionOptions.UseAdvanced), "Advanced Mode"),
            (nameof(IStarDetectionOptions.Simple_NoiseLevel), "Noise Level"),
            (nameof(IStarDetectionOptions.Simple_PixelScale), "Pixel Scale"),
            (nameof(IStarDetectionOptions.Simple_FocusRange), "Focus Range"),
            (nameof(IStarDetectionOptions.UseOptimizedSettings), "Use Optimized Settings"),
            (nameof(IStarDetectionOptions.HotpixelThresholdingEnabled), "Use Hotpixel Thresholding"),
            (nameof(IStarDetectionOptions.UseAutoFocusCrop), "Use AutoFocus Crop"),
            (nameof(IStarDetectionOptions.ModelPSF), "Fit PSF"),
            (nameof(IStarDetectionOptions.HotpixelFiltering), "Hotpixel Filtering"),
            (nameof(IStarDetectionOptions.StarMeasurementNoiseReductionEnabled), "Noise Reduced Star Measurement"),
            (nameof(IStarDetectionOptions.NoiseReductionRadius), "Noise Reduction Radius"),
            (nameof(IStarDetectionOptions.NoiseClippingMultiplier), "Noise Clipping Multiplier"),
            (nameof(IStarDetectionOptions.StarClippingMultiplier), "Star Clipping Multiplier"),
            (nameof(IStarDetectionOptions.ContaminationSensitivity), "Contamination Sensitivity"),
            (nameof(IStarDetectionOptions.RejectContaminatedStars), "Reject Contaminated Stars"),
            (nameof(IStarDetectionOptions.StructureLayers), "Structure Layers"),
            (nameof(IStarDetectionOptions.BrightnessSensitivity), "Brightness Sensitivity"),
            (nameof(IStarDetectionOptions.StarPeakResponse), "Star Peak Response"),
            (nameof(IStarDetectionOptions.MaxDistortion), "Max Distortion"),
            (nameof(IStarDetectionOptions.DefocusAwareGates), "Defocus-Aware Gates"),
            (nameof(IStarDetectionOptions.DefocusDistortionSizeReference), "Defocus Size Reference"),
            (nameof(IStarDetectionOptions.DefocusDistortionMinFactor), "Defocus Distortion Min Factor"),
            (nameof(IStarDetectionOptions.DefocusCenteringToleranceFactor), "Defocus Centering Tolerance Factor"),
            (nameof(IStarDetectionOptions.DefocusAwareStructure), "Defocus-Aware Structure"),
            (nameof(IStarDetectionOptions.StructureLayerBoost), "Structure Layer Boost"),
            (nameof(IStarDetectionOptions.DefocusAwareDonutDetection), "Defocus-Aware Donut Detection"),
            (nameof(IStarDetectionOptions.DonutMorphCloseSize), "Donut Morph Close Size"),
            (nameof(IStarDetectionOptions.DonutMinAnnularityHoleFraction), "Donut Min Annularity Hole Fraction"),
            (nameof(IStarDetectionOptions.DonutMaxStreakEccentricity), "Donut Max Streak Eccentricity"),
            (nameof(IStarDetectionOptions.DonutSaturationBloomRadius), "Donut Saturation Bloom Radius"),
            (nameof(IStarDetectionOptions.LocallyAdaptiveBinarization), "Locally Adaptive Binarization"),
            (nameof(IStarDetectionOptions.AdaptiveNoiseBlockSize), "Adaptive Noise Block Size"),
            (nameof(IStarDetectionOptions.StarCenterTolerance), "Star Center Tolerance"),
            (nameof(IStarDetectionOptions.StarBackgroundBoxExpansion), "Background Box Expansion"),
            (nameof(IStarDetectionOptions.MinStarBoundingBoxSize), "Min Bounding Box Size"),
            (nameof(IStarDetectionOptions.MinHFR), "Min HFR"),
            (nameof(IStarDetectionOptions.StructureDilationSize), "Structure Dilation Size"),
            (nameof(IStarDetectionOptions.StructureDilationCount), "Structure Dilation Iterations"),
            (nameof(IStarDetectionOptions.PixelSampleSize), "Pixel Sample Size"),
            (nameof(IStarDetectionOptions.PSFFitType), "PSF Type"),
            (nameof(IStarDetectionOptions.PSFResolution), "PSF Resolution"),
            (nameof(IStarDetectionOptions.PSFFitThreshold), "PSF Fit Threshold"),
            (nameof(IStarDetectionOptions.PSFPixelIntegration), "PSF Pixel Integration"),
            (nameof(IStarDetectionOptions.UsePSFAbsoluteDeviation), "PSF MAD Fitting"),
            (nameof(IStarDetectionOptions.HotpixelThreshold), "Hotpixel Threshold"),
            (nameof(IStarDetectionOptions.SaturationThreshold), "Saturation Threshold"),
            (nameof(IStarDetectionOptions.ExcludeSaturatedStarsFromHFR), "Exclude Saturated Stars From HFR"),
            (nameof(IStarDetectionOptions.MeasurementAverage), "Measurement Averaging"),
        };

        /// <summary>
        /// Returns one row per importable setting whose value differs between <paramref name="current"/> and
        /// <paramref name="imported"/>, plus a trailing "Optimizer result" summary row when the stored optimizer
        /// snapshot changes. An empty result means importing would change nothing.
        /// </summary>
        public static IReadOnlyList<StarDetectionSettingDiffRow> BuildDiff(IStarDetectionOptions current, IStarDetectionOptions imported) {
            if (current == null) {
                throw new ArgumentNullException(nameof(current));
            }
            if (imported == null) {
                throw new ArgumentNullException(nameof(imported));
            }

            var rows = new List<StarDetectionSettingDiffRow>();
            foreach (var (property, label) in ImportableSettings) {
                var info = typeof(IStarDetectionOptions).GetProperty(property);
                var currentValue = info.GetValue(current);
                var importedValue = info.GetValue(imported);
                if (!Equals(currentValue, importedValue)) {
                    rows.Add(new StarDetectionSettingDiffRow() {
                        Name = label,
                        CurrentValue = FormatValue(currentValue),
                        NewValue = FormatValue(importedValue)
                    });
                }
            }

            var currentOptimized = current.GetOptimizedSettings();
            var importedOptimized = imported.GetOptimizedSettings();
            if (!OptimizedEquivalent(currentOptimized, importedOptimized)) {
                rows.Add(new StarDetectionSettingDiffRow() {
                    Name = "Optimizer result",
                    CurrentValue = FormatOptimized(currentOptimized),
                    NewValue = FormatOptimized(importedOptimized)
                });
            }

            return rows;
        }

        // Identity comparison for the stored optimizer snapshot: same run (timestamp) and same final score is treated
        // as equivalent. This is for the human-facing summary row only — the actual import always restores the layer.
        private static bool OptimizedEquivalent(OptimizedStarDetectionSettings a, OptimizedStarDetectionSettings b) {
            if (a == null && b == null) {
                return true;
            }
            if (a == null || b == null) {
                return false;
            }
            return a.CreatedAtUtc == b.CreatedAtUtc && a.FinalJ.Equals(b.FinalJ) && a.RunCount == b.RunCount;
        }

        private static string FormatOptimized(OptimizedStarDetectionSettings s) {
            if (s == null) {
                return "none";
            }
            return $"present (J={s.FinalJ.ToString("0.###", CultureInfo.InvariantCulture)}, {s.RunCount} {(s.RunCount == 1 ? "round" : "rounds")})";
        }

        private static string FormatValue(object value) {
            switch (value) {
                case null:
                    return "(none)";
                case bool b:
                    return b ? "On" : "Off";
                case double d:
                    return d.ToString("0.###", CultureInfo.InvariantCulture);
                case Enum e:
                    return GetEnumDescription(e);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static string GetEnumDescription(Enum value) {
            var field = value.GetType().GetField(value.ToString());
            var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
            return attribute?.Description ?? value.ToString();
        }
    }
}
