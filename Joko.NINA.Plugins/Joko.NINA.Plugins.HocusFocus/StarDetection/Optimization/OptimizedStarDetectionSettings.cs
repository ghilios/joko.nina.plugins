#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
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

        // Metadata about the optimization run that produced this snapshot
        public DateTime CreatedAtUtc { get; set; }
        public int RunCount { get; set; }
        public double BaselineJ { get; set; }
        public double FinalJ { get; set; }
        public int RecommendedStepSize { get; set; }
        public int RecommendedOffsetSteps { get; set; }
        public int SchemaVersion { get; set; } = 1;

        public OptimizedStarDetectionSettings Clone() {
            return (OptimizedStarDetectionSettings)MemberwiseClone();
        }
    }
}
