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

        // Metadata about the optimization run that produced this snapshot
        public DateTime CreatedAtUtc { get; set; }
        public int RunCount { get; set; }
        public double BaselineJ { get; set; }
        public double FinalJ { get; set; }
        public int RecommendedStepSize { get; set; }
        public int RecommendedOffsetSteps { get; set; }

        /// <summary>
        /// The gate this landing ACTUALLY enforces — <c>max(BrightnessSensitivity, StarPeakResponse ×
        /// effective StarClippingMultiplier)</c> (followup F33). Derived, get-only: it adds no state, so it needs
        /// no schema bump and is computed for every landing already on disk when one is read back.
        ///
        /// <para>Reported because <see cref="BrightnessSensitivity"/> alone misclassifies a whole shape of landing.
        /// A run can record <c>0.0</c> here — which reads as "the optimizer drove the gate to its floor", the
        /// synthetic bank's pathology — while the structure/clip stage enforces a gate many times the shipped
        /// default. Read <see cref="EffectiveSensitivityGate"/> instead whenever the question is "how hard is this
        /// landing culling stars".</para>
        /// </summary>
        [JsonProperty(Order = 100)]
        public double EffectiveSensitivityGate => StarDetector.EffectiveSensitivityGate(GateParams());

        /// <summary>
        /// The minimal <see cref="StarDetectorParams"/> the gate algebra reads, rebuilt from this DTO. Deliberately
        /// partial — it exists so <see cref="EffectiveSensitivityGate"/> routes through the SAME
        /// <see cref="StarDetector"/> helpers the detector itself uses (the donut clip cap in particular), rather
        /// than restating that algebra here where it could silently drift.
        /// </summary>
        private StarDetectorParams GateParams() => new StarDetectorParams {
            Sensitivity = BrightnessSensitivity,
            PeakResponse = StarPeakResponse,
            StarClippingMultiplier = StarClippingMultiplier,
            DefocusAwareDonutDetection = DefocusAwareDonutDetection,
            DefocusDistortionSizeReference = DefocusDistortionSizeReference,
        };

        /// <summary>
        /// v1 = the original curated knob set. v2 added the defocus-aware axes. v3 added the optional
        /// <see cref="Provenance"/> block (F30) and changed NO knob semantics, so a v3 file stays knob-compatible
        /// with v2 in both directions — an older build ignores the unknown key, and a newer build reads a v1/v2
        /// file with <see cref="Provenance"/> left null. Nothing branches on this number and nothing should start
        /// REJECTING a higher one: the whole AF-bank toolchain reads this DTO, and a hard version gate would break
        /// it on the next bump.
        /// </summary>
        public const int CurrentSchemaVersion = 3;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// WHICH INVOCATION produced this landing — null when that is unknown, which covers every file written
        /// before schema 3 and every snapshot that captures live settings rather than an optimizer result. Null
        /// means <b>unattributable</b> and must never be read as "matches me".
        ///
        /// <para><b>Why this exists.</b> A landing recorded when it was written and how well it scored, and nothing
        /// about what produced it. <c>optimize --per-run</c> writes each landing back into the RUN's own folder as
        /// well as into <c>--out</c> (F15), so a bank folder accumulates whichever prepass went last and a reader
        /// has to INFER the arm from a knob value. That inference holds only while exactly one knob varies between
        /// arms. F23 wave 1 ran three arms differing by objective constants and search domain, the inference
        /// silently broke, and the misattribution produced a wrong followup entry that was committed twice before
        /// a control arm disproved it.</para>
        ///
        /// <para>Omitted from the JSON entirely when null, so a plugin-written snapshot is byte-identical to
        /// before this field existed.</para>
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public OptimizerProvenance Provenance { get; set; }

        public OptimizedStarDetectionSettings Clone() {
            var copy = (OptimizedStarDetectionSettings)MemberwiseClone();
            // MemberwiseClone is shallow, so without this every copy would ALIAS one provenance instance — and a
            // mutation through any of them would rewrite the history of all the others.
            copy.Provenance = Provenance?.Clone();
            return copy;
        }

        /// <summary>
        /// Builds a snapshot DTO from the optimizer's winning <see cref="StarDetectorParams"/> plus the run
        /// metadata. This is the SINGLE source of truth for the params→DTO mapping (the DTO renames a few knobs:
        /// Sensitivity→BrightnessSensitivity, PeakResponse→StarPeakResponse, MinimumStarBoundingBoxSize→
        /// MinStarBoundingBoxSize). Both the in-app wizard (<c>StarDetectionOptimizerWizardVM.Apply</c>) and the
        /// headless harness (<c>OptimizationDiagnosticRunner</c>) call this so the two paths can never drift.
        /// <see cref="CreatedAtUtc"/> is stamped with <see cref="DateTime.UtcNow"/> at the call site.
        /// </summary>
        /// <param name="provenance">Optional record of the invocation that produced this landing. Leave it null
        /// for a snapshot that CAPTURES live settings rather than reporting an optimizer result — the tilt
        /// wizard's <c>CaptureDetectionSettings</c> is exactly that, and stamping a producer on it would be a
        /// lie.</param>
        public static OptimizedStarDetectionSettings FromParams(
            StarDetectorParams p, int runCount, double baselineJ, double finalJ, int recommendedStepSize, int recommendedOffsetSteps,
            OptimizerProvenance provenance = null) {
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

                CreatedAtUtc = DateTime.UtcNow,
                RunCount = runCount,
                BaselineJ = baselineJ,
                FinalJ = finalJ,
                RecommendedStepSize = recommendedStepSize,
                RecommendedOffsetSteps = recommendedOffsetSteps,
                Provenance = provenance?.Clone()
            };
        }
    }

    /// <summary>
    /// Which invocation produced an <see cref="OptimizedStarDetectionSettings"/> landing, and under what
    /// configuration (F30). Metadata only — nothing here is ever applied to
    /// <see cref="StarDetectorParams"/>, and every overlay helper ignores it.
    ///
    /// <para>Every field is optional and the block is omitted from the JSON when null, so a snapshot written by
    /// the SHIPPING plugin — which has no argv and no harness settings file — stays exactly as it was, and a file
    /// written before this existed still loads, reporting "no provenance" rather than a fabricated one.</para>
    /// </summary>
    [JsonObject(MemberSerialization.OptOut, ItemNullValueHandling = NullValueHandling.Ignore)]
    public sealed class OptimizerProvenance {

        /// <summary>What wrote this, e.g. <c>"TestApp optimize"</c> or <c>"HocusFocus wizard"</c>. The coarse
        /// discriminator: argv is meaningful only for the harness.</summary>
        public string Producer { get; set; }

        /// <summary>The effective command line, arguments joined by a single space. This is the field F30 is
        /// about: two prepasses differing by one flag are otherwise indistinguishable once written.</summary>
        public string CommandLine { get; set; }

        /// <summary>Stable fingerprint of the settings the run was DRIVEN by. Hashes the semantic content rather
        /// than the file's bytes, so re-exporting or re-indenting a settings file does not make a landing look
        /// like it came from a different configuration.</summary>
        public string SettingsFingerprint { get; set; }

        /// <summary>Assembly informational version of whatever produced this, so a landing also identifies the
        /// build it came from.</summary>
        public string ProducerVersion { get; set; }

        public OptimizerProvenance Clone() => (OptimizerProvenance)MemberwiseClone();

        /// <summary>One-line summary for a log or a console line; skips whatever is unset.</summary>
        public override string ToString() {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Producer)) { parts.Add(Producer); }
            if (!string.IsNullOrWhiteSpace(ProducerVersion)) { parts.Add($"v{ProducerVersion}"); }
            if (!string.IsNullOrWhiteSpace(CommandLine)) { parts.Add(CommandLine); }
            if (!string.IsNullOrWhiteSpace(SettingsFingerprint)) { parts.Add($"settings#{SettingsFingerprint}"); }
            return parts.Count > 0 ? string.Join(" | ", parts) : "(no provenance)";
        }
    }
}
