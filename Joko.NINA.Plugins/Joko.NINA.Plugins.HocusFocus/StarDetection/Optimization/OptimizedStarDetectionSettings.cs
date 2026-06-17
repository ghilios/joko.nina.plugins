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

        // Defocus-aware family (set only when the wizard's defocus-recovery opt-in produced this snapshot). Defaults
        // MATCH StarDetectionOptions so a snapshot that predates these fields (SchemaVersion 1, no defocus keys in
        // its JSON) deserializes to the BASELINE defaults — gates OFF with the default knobs — rather than zeros
        // (which would, e.g., set an invalid 0 px size reference). DefocusAwareGates is a single flag because the
        // optimizer moves the three detector flags (distortion/centering/roundness) in lockstep.
        public bool DefocusAwareGates { get; set; } = false;
        public double DefocusDistortionSizeReference { get; set; } = 30.0;
        public double DefocusDistortionMinFactor { get; set; } = 0.25;
        public double DefocusCenteringToleranceFactor { get; set; } = 2.0;
        public double DefocusMaxElongation { get; set; } = 2.0;
        public bool DefocusAwareStructure { get; set; } = false;
        public int StructureLayerBoost { get; set; } = 0;

        // Metadata about the optimization run that produced this snapshot
        public DateTime CreatedAtUtc { get; set; }
        public int RunCount { get; set; }
        public double BaselineJ { get; set; }
        public double FinalJ { get; set; }
        public int RecommendedStepSize { get; set; }
        public int RecommendedOffsetSteps { get; set; }
        public int SchemaVersion { get; set; } = 2; // 2 adds the defocus-aware family

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

                // Defocus-aware family. The three gate flags move in lockstep (the optimizer's combined switch),
                // so the single DefocusAwareGates mirrors p.DefocusAwareDistortion.
                DefocusAwareGates = p.DefocusAwareDistortion,
                DefocusDistortionSizeReference = p.DefocusDistortionSizeReference,
                DefocusDistortionMinFactor = p.DefocusDistortionMinFactor,
                DefocusCenteringToleranceFactor = p.DefocusCenteringToleranceFactor,
                DefocusMaxElongation = p.DefocusMaxElongation,
                DefocusAwareStructure = p.DefocusAwareStructure,
                StructureLayerBoost = p.StructureLayerBoost,

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
