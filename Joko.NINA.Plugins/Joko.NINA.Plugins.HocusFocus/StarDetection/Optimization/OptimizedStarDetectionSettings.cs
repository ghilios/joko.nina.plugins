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
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Serializable snapshot of the curated star-detection knob values produced by the Star Detection
    /// Optimization Wizard, plus metadata about the run that produced them. This is a pure data class — it
    /// holds only the curated subset of <see cref="StarDetectionOptions"/> properties that the optimizer
    /// tunes; the remaining advanced knobs continue to follow Simple-mode preset defaults when this snapshot
    /// is applied.
    /// </summary>
    [JsonObject(MemberSerialization.OptOut)]
    public class OptimizedStarDetectionSettings {

        public OptimizedStarDetectionSettings() {
        }

        // Curated knobs (mirror the matching StarDetectionOptions property names/types)
        public double BrightnessSensitivity { get; set; }
        public double StarClippingMultiplier { get; set; }
        public double NoiseClippingMultiplier { get; set; }
        public double StarPeakResponse { get; set; }
        public double MaxDistortion { get; set; }
        public double MinHFR { get; set; }
        public double StarCenterTolerance { get; set; }
        public int StructureLayers { get; set; }
        public int NoiseReductionRadius { get; set; }
        public int MinStarBoundingBoxSize { get; set; }
        public bool HotpixelThresholdingEnabled { get; set; }
        public double HotpixelThreshold { get; set; }

        // Defocus-aware axes (added schema v2). Initialized to valid ResetDefaults values so a v1 snapshot
        // (missing these keys) deserializes to an inert/legacy configuration (master OFF).
        public bool DefocusAwareGates { get; set; } = false;
        public double DefocusDistortionSizeReference { get; set; } = 30.0;
        public double DefocusDistortionMinFactor { get; set; } = 0.25;
        public double DefocusCenteringToleranceFactor { get; set; } = 2.0;
        public bool DefocusAwareStructure { get; set; } = false;
        public int StructureLayerBoost { get; set; } = 0;
        public bool DefocusAwareDonutDetection { get; set; } = false;
        public int DonutMorphCloseSize { get; set; } = 5;
        // Spatially-adaptive binarization (not optimizer-tuned; carried so the headless harness overlays can exercise
        // it). Inert defaults so an older snapshot missing these keys deserializes to legacy scalar binarization.
        public bool LocallyAdaptiveBinarization { get; set; } = false;
        public int AdaptiveNoiseBlockSize { get; set; } = 128;
        public double DonutMinAnnularityHoleFraction { get; set; } = 0.15;
        public double DonutMaxStreakEccentricity { get; set; } = 1.0;
        public double DonutSaturationBloomRadius { get; set; } = 0.0;
        // Detection-quality gates (added schema v3). Defaulted to the live-profile defaults (both ON) so an older
        // snapshot missing these keys deserializes to the SAME values the pre-v3 apply path left untouched — i.e. no
        // silent flip for an existing snapshot. The optimizer searches these axes and FromParams persists its result.
        public bool RejectContaminatedStars { get; set; } = true;
        public bool ExcludeSaturatedStarsFromHFR { get; set; } = true;

        // Metadata about the optimization run that produced this snapshot
        public DateTime CreatedAtUtc { get; set; }
        public int RunCount { get; set; }
        public double BaselineJ { get; set; }
        public double FinalJ { get; set; }
        public int RecommendedStepSize { get; set; }
        public int RecommendedOffsetSteps { get; set; }
        public int SchemaVersion { get; set; } = 3;

        public OptimizedStarDetectionSettings Clone() {
            return (OptimizedStarDetectionSettings)MemberwiseClone();
        }

        /// <summary>
        /// Builds a snapshot DTO from the optimizer's winning <see cref="StarDetectorParams"/> plus the run
        /// metadata. This is the SINGLE source of truth for the params→DTO mapping (the DTO renames a few knobs:
        /// Sensitivity→BrightnessSensitivity, PeakResponse→StarPeakResponse, MinimumStarBoundingBoxSize→
        /// MinStarBoundingBoxSize). Both the in-app wizard (<c>StarDetectionOptimizerWizardVM.Apply</c>) and the
        /// headless harness (<c>OptimizationDiagnosticRunner</c>) call this so the two paths can never drift.
        /// <see cref="CreatedAtUtc"/> is stamped with <see cref="DateTime.UtcNow"/> at the call site.
        /// </summary>
        public static OptimizedStarDetectionSettings FromParams(
            StarDetectorParams p, int runCount, double baselineJ, double finalJ, int recommendedStepSize, int recommendedOffsetSteps) {
            if (p == null) {
                throw new ArgumentNullException(nameof(p));
            }
            return new OptimizedStarDetectionSettings {
                // Curated knobs — note the DTO renames a few (Sensitivity->BrightnessSensitivity etc.).
                BrightnessSensitivity = p.Sensitivity,
                StarClippingMultiplier = p.StarClippingMultiplier,
                NoiseClippingMultiplier = p.NoiseClippingMultiplier,
                StarPeakResponse = p.PeakResponse,
                MaxDistortion = p.MaxDistortion,
                MinHFR = p.MinHFR,
                StarCenterTolerance = p.StarCenterTolerance,
                StructureLayers = p.StructureLayers,
                NoiseReductionRadius = p.NoiseReductionRadius,
                MinStarBoundingBoxSize = p.MinimumStarBoundingBoxSize,
                HotpixelThresholdingEnabled = p.HotpixelThresholdingEnabled,
                HotpixelThreshold = p.HotpixelThreshold,

                // Defocus-aware axes (schema v2). DefocusAwareGates reflects the combined gate flag (distortion ==
                // centering by construction). The master + donut knobs persist the optimizer's donut-recovery /
                // spike-suppression result so it survives Accept.
                DefocusAwareGates = p.DefocusAwareDistortion,
                DefocusDistortionSizeReference = p.DefocusDistortionSizeReference,
                DefocusDistortionMinFactor = p.DefocusDistortionMinFactor,
                DefocusCenteringToleranceFactor = p.DefocusCenteringToleranceFactor,
                DefocusAwareStructure = p.DefocusAwareStructure,
                StructureLayerBoost = p.StructureLayerBoost,
                DefocusAwareDonutDetection = p.DefocusAwareDonutDetection,
                DonutMorphCloseSize = p.DonutMorphCloseSize,
                LocallyAdaptiveBinarization = p.LocallyAdaptiveBinarization,
                AdaptiveNoiseBlockSize = p.AdaptiveNoiseBlockSize,
                DonutMinAnnularityHoleFraction = p.DonutMinAnnularityHoleFraction,
                DonutMaxStreakEccentricity = p.DonutMaxStreakEccentricity,
                DonutSaturationBloomRadius = p.DonutSaturationBloomRadius,
                RejectContaminatedStars = p.RejectContaminatedStars,
                ExcludeSaturatedStarsFromHFR = p.ExcludeSaturatedStarsFromHFR,

                CreatedAtUtc = DateTime.UtcNow,
                RunCount = runCount,
                BaselineJ = baselineJ,
                FinalJ = finalJ,
                RecommendedStepSize = recommendedStepSize,
                RecommendedOffsetSteps = recommendedOffsetSteps
            };
        }
    }
}
