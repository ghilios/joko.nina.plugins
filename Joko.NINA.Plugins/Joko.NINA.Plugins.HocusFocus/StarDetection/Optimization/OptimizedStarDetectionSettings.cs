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
        /// F32 — the detection-keep floor that was IN FORCE while this landing was searched, or null when the
        /// search was unconstrained. Bookkeeping about the run, not a detector knob: it changes which candidates
        /// were eligible, never what the detector does with the ones recorded here.
        ///
        /// <para>Recorded because F39's complaint generalizes — a file that records settings a run did not use is
        /// worse than one that records nothing, and the converse holds too: a landing produced under a constraint
        /// is not comparable to one produced without, and nothing else in this file would say so. Omitted from the
        /// JSON when null, so an unconstrained landing stays byte-identical to before this field existed.</para>
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? MinDetectionKeepFraction { get; set; }

        /// <summary>
        /// F32 — what this landing actually kept: accepted stars as a fraction of the SEED's, MIN over runs.
        /// Reported, never scored.
        ///
        /// <para><b>Written whether or not a floor was in force</b>, unlike
        /// <see cref="MinDetectionKeepFraction"/>. It costs nothing (the counts are already in hand) and it is
        /// precisely the "keep%" F32 had to reconstruct by hand from stored landings across two waves. An
        /// UNCONSTRAINED landing is exactly where this number is most needed: it is what says whether a floor
        /// would have bound, so a control arm can be classified without re-running it. Null only when the
        /// evaluator reported no star counts to measure.</para>
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? LandingDetectionKeepFraction { get; set; }

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

        /// <summary>
        /// The names on this DTO that are ALSO live star-detection knobs, i.e. the curated axes. Excludes the
        /// bookkeeping fields (<see cref="SchemaVersion"/>, <see cref="CreatedAtUtc"/>, <see cref="RunCount"/>,
        /// <see cref="BaselineJ"/>, <see cref="FinalJ"/>, the recommended sweep geometry and
        /// <see cref="Provenance"/>), which describe the RUN rather than the detector and have no option to write.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> NonKnobFields =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) {
                nameof(SchemaVersion), nameof(CreatedAtUtc), nameof(RunCount), nameof(BaselineJ), nameof(FinalJ),
                nameof(RecommendedStepSize), nameof(RecommendedOffsetSteps), nameof(Provenance),
                nameof(EffectiveSensitivityGate),
                // F32 — describe the SEARCH that produced this landing (which candidates were eligible), not the
                // detector. Registering them here is load-bearing: the DTO→options mapping is by reflected name,
                // so an unregistered bookkeeping field would be hunted for as a live knob.
                nameof(MinDetectionKeepFraction), nameof(LandingDetectionKeepFraction)
            };

        /// <summary>
        /// Copies every curated axis onto the same-named property of a flat options snapshot.
        ///
        /// <para>Matched BY NAME through reflection rather than by a hand-written assignment list, deliberately: the
        /// DTO's property names already ARE the option names, and a hand-written list silently stops covering an axis
        /// the moment one is added to the curated set. <see cref="UnmappedKnobs"/> exposes the residue so a test can
        /// fail on exactly that.</para>
        /// </summary>
        public void ApplyToFlatOptions(object flatOptions) {
            if (flatOptions == null) {
                throw new ArgumentNullException(nameof(flatOptions));
            }
            var targetType = flatOptions.GetType();
            foreach (var source in KnobProperties()) {
                var target = targetType.GetProperty(source.Name);
                if (target == null || !target.CanWrite || target.PropertyType != source.PropertyType) {
                    continue;
                }
                target.SetValue(flatOptions, source.GetValue(this));
            }
        }

        /// <summary>
        /// The curated axes whose VALUE on <paramref name="flatOptions"/> differs from this landing's — empty means
        /// the landing survived a round trip through a settings file intact.
        ///
        /// <para>Routes through the same <see cref="KnobProperties"/> enumeration as
        /// <see cref="ApplyToFlatOptions"/> and <see cref="UnmappedKnobs"/>, so a newly added axis is covered by
        /// all three at once and none of them can silently stop checking one. An axis that does not exist on the
        /// target at all is reported here too — a missing knob and a wrong knob are the same defect to a reader
        /// who imports the file.</para>
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> DiffKnobs(object flatOptions) {
            if (flatOptions == null) {
                throw new ArgumentNullException(nameof(flatOptions));
            }
            var targetType = flatOptions.GetType();
            var diffs = new System.Collections.Generic.List<string>();
            foreach (var source in KnobProperties()) {
                var target = targetType.GetProperty(source.Name);
                if (target == null || !target.CanRead || target.PropertyType != source.PropertyType) {
                    diffs.Add(source.Name + " (unmapped)");
                    continue;
                }
                if (!Equals(source.GetValue(this), target.GetValue(flatOptions))) {
                    diffs.Add($"{source.Name} ({source.GetValue(this)} != {target.GetValue(flatOptions)})");
                }
            }
            return diffs;
        }

        /// <summary>
        /// The names of the curated detector axes on this DTO — everything that is NOT run bookkeeping.
        ///
        /// <para>Exposed so that consumers which must cover "every knob" enumerate from THIS list rather than
        /// keeping their own copy of the exclusions. A second copy drifts: the tilt replay overlay's coverage
        /// guard kept its own <c>metadataOnly</c> set and started failing the moment two bookkeeping fields were
        /// added here, reporting them as unapplied detector knobs. One list, checked by everyone.</para>
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<string> CuratedKnobNames { get; } =
            KnobPropertyNames();

        private static string[] KnobPropertyNames() {
            var names = new System.Collections.Generic.List<string>();
            foreach (var p in KnobProperties()) {
                names.Add(p.Name);
            }
            return names.ToArray();
        }

        /// <summary>The curated axes that would NOT land on <paramref name="flatOptionsType"/> — empty is the
        /// invariant; anything else means a landing does not fully round-trip into a settings file.</summary>
        public static System.Collections.Generic.IReadOnlyList<string> UnmappedKnobs(Type flatOptionsType) {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var source in KnobProperties()) {
                var target = flatOptionsType.GetProperty(source.Name);
                if (target == null || !target.CanWrite || target.PropertyType != source.PropertyType) {
                    missing.Add(source.Name);
                }
            }
            return missing;
        }

        private static System.Collections.Generic.IEnumerable<System.Reflection.PropertyInfo> KnobProperties() {
            // INSTANCE properties only. GetProperties() with no flags also returns STATIC ones, and a static
            // member is by definition not a per-landing detector knob — CuratedKnobNames itself was picked up as
            // one the moment it was added, and reported as an axis with no matching option property.
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (var p in typeof(OptimizedStarDetectionSettings).GetProperties(flags)) {
                if (p.CanRead && !NonKnobFields.Contains(p.Name)) {
                    yield return p;
                }
            }
        }

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
        /// <param name="minDetectionKeepFraction">F32 — the keep floor in force during the search, or null when
        /// unconstrained. Optional so every existing caller (and every snapshot that captures live settings
        /// rather than an optimizer result) keeps writing byte-identical files.</param>
        /// <param name="landingDetectionKeepFraction">F32 — what the landing kept, min over runs. Pass null
        /// whenever <paramref name="minDetectionKeepFraction"/> is null; a keep fraction with no floor beside it
        /// would read as a constraint that was never applied.</param>
        public static OptimizedStarDetectionSettings FromParams(
            StarDetectorParams p, int runCount, double baselineJ, double finalJ, int recommendedStepSize, int recommendedOffsetSteps,
            OptimizerProvenance provenance = null,
            double? minDetectionKeepFraction = null, double? landingDetectionKeepFraction = null) {
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
                Provenance = provenance?.Clone(),
                MinDetectionKeepFraction = minDetectionKeepFraction,
                // Recorded whenever it is measurable, floor or no floor — see the property doc. NaN is filtered
                // because it is not a number a JSON reader should have to handle.
                LandingDetectionKeepFraction =
                    landingDetectionKeepFraction is double lk && double.IsFinite(lk) ? lk : (double?)null
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

        /// <summary>Assembly informational version of whatever produced this.
        ///
        /// <para><b>This field does NOT identify the build, and F53 is the proof.</b> It used to claim it did.
        /// Two builds of the same source — or of two different sources between version bumps — carry the same
        /// informational version, so wave 8's arm X and the binary that later overwrote its `exe` directory were
        /// indistinguishable by this field. Use <see cref="BuildId"/> for build identity.</para></summary>
        public string ProducerVersion { get; set; }

        /// <summary>
        /// The <b>build</b> that produced this: the plugin assembly's Module Version ID, which the compiler
        /// regenerates on every build even when the source is byte-identical.
        ///
        /// <para><b>F53.</b> Wave 8's arm X was recorded with <c>Reproduce: D:\hf_w8\armX\arm_x.sh</c>, and
        /// running that script on that binary today does not reproduce its numbers, because a later step in the
        /// same wave rebuilt the directory and an artifact directory keeps only the LAST build. Identifying that
        /// took reading the log for the ABSENCE of an unrelated line. With this field it is a diff. The entry's
        /// durable lesson — <i>a "Reproduce:" line names a COMMAND, not a result</i> — is not repealed by
        /// stamping the build; what is repealed is having to infer the build from its side effects.</para>
        /// </summary>
        public string BuildId { get; set; }

        /// <summary>
        /// <c>StarDetector.StarDetectorVersion</c> at the time of the run — the detector's OUTPUT contract.
        ///
        /// <para>Wave 10 exists because wave 9 measured everything on version 1 and <c>develop</c> then shipped
        /// version 2 (PR #187's <c>AtrousWaveletFast</c>: equivalent to ≤ 3e-8, deliberately not bit-identical).
        /// Every wave-9 number carries a hand-written provenance banner for want of this field. A reader diffs a
        /// field; nobody diffs a banner — the same argument that gave F39(a) its <c>DetectionBinningSource</c>.</para>
        /// </summary>
        public int? DetectorVersion { get; set; }

        /// <summary>
        /// Whether this run had the machine to itself: <c>"exclusive"</c>, <c>"concurrent"</c>, or
        /// <c>"unknown"</c>. Null when the producer does not check.
        ///
        /// <para><b>F55.</b> Concurrent `optimize` processes move **44 % of landings** and 15 % of seed
        /// evaluations, so an arm read on landings is invalid if it ran beside another one. F55(c) asks for that
        /// to be "said in the run instructions" — and wave 10 then ran a 39-run pass TWICE AT ONCE anyway,
        /// because a background launcher that reported "completed" had only had its launcher shell exit. The
        /// rule was known, written down, and still violated, because **the violation was invisible**. A field on
        /// the landing makes it visible after the fact, to a scorer, without anyone having to have been
        /// watching.</para>
        ///
        /// <para><b>Three values, not two</b>, for the same reason
        /// <c>ExposureRecommendation.WingRejectedFraction</c> is NaN-never-0 and
        /// <c>StepSizeRecommendation.MaxUsefulHalfSpan</c> is NaN-never-0: *"we could not look"* and *"we looked
        /// and were alone"* must not be the same value, because a scorer turns one of them into a verdict.</para>
        /// </summary>
        public string ConcurrencyCheck { get; set; }

        /// <summary>
        /// The NINA profile the run was loaded under, as <c>"name (id)"</c>. Null when the producer does not
        /// record it.
        ///
        /// <para><b>F57.</b> <c>--settings</c> pins the DETECTOR knobs, and this project treated that as pinning
        /// the arm. It does not: the harness also loads whichever profile is ACTIVE, and wave 10 measured that
        /// moving <c>BaselineJ</c> — the objective of a fixed seed on fixed frames, with no search — by
        /// <b>0.0144</b> on <c>toml999</c>, which is larger than the entire Δ<c>J</c> any wave has argued about.
        /// It was found only because a control pre-registered for a different hypothesis refuted that
        /// hypothesis and left the profile as the last surviving difference.</para>
        ///
        /// <para>F42 predicted this in words — <i>"TryLoad("") picks whichever profile is ACTIVE"</i> — and the
        /// remedy it prompted (pin the detector settings) did not close it. A field closes it: a reader diffs a
        /// field, and nobody diffs a prediction.</para>
        /// </summary>
        public string ProfileId { get; set; }

        /// <summary>
        /// The values that reach the AF <b>fit</b>, rendered as <c>key=value;key=value</c>. Null when the
        /// producer does not record them.
        ///
        /// <para><b>F58.</b> <see cref="ProfileId"/> says which profile ran; this says what the profile (or the
        /// pinned settings file) actually supplied. The two are not the same question, and the difference is the
        /// entire defect: the fit reads four values, and wave 11 found that the machine's nine profiles partition
        /// <b>2 / 7</b> on just one of them (<c>MaxOutlierRejections</c>) — which is why concurrent runs, each
        /// having silently acquired a different profile, landed on two discrete values of <c>J</c> and looked
        /// like a floating-point race for two waves.</para>
        ///
        /// <para><b>Values, not a hash.</b> A fingerprint tells a reader that something moved; these tell them
        /// WHICH, without a second run. That is the same reason <see cref="BuildId"/> is an MVID rather than a
        /// version string and <c>DetectionBinningSource</c> (F39(a)) is a field rather than a sentence.</para>
        /// </summary>
        public string FitInputs { get; set; }

        /// <summary>
        /// The identity of the currently-loaded plugin build: <see cref="BuildId"/> and
        /// <see cref="DetectorVersion"/>, read off this assembly. Static so the harness and the shipping wizard
        /// stamp the same two values from the same place rather than each deriving its own.
        /// </summary>
        public static (string BuildId, int DetectorVersion) CurrentBuild() => (
            typeof(OptimizerProvenance).Assembly.ManifestModule.ModuleVersionId.ToString("N"),
            StarDetector.StarDetectorVersion);

        public OptimizerProvenance Clone() => (OptimizerProvenance)MemberwiseClone();

        /// <summary>One-line summary for a log or a console line; skips whatever is unset.</summary>
        public override string ToString() {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Producer)) { parts.Add(Producer); }
            if (!string.IsNullOrWhiteSpace(ProducerVersion)) { parts.Add($"v{ProducerVersion}"); }
            if (!string.IsNullOrWhiteSpace(BuildId)) { parts.Add($"build#{BuildId}"); }
            if (DetectorVersion.HasValue) { parts.Add($"detector v{DetectorVersion.Value}"); }
            // Loud in the one-liner rather than tucked into the JSON: "concurrent" means the landing beside it
            // is not comparable to anything (F55), and that has to be readable in a log tail.
            if (!string.IsNullOrWhiteSpace(ConcurrencyCheck) && ConcurrencyCheck != "exclusive") {
                parts.Add($"CONCURRENCY={ConcurrencyCheck.ToUpperInvariant()}");
            }
            if (!string.IsNullOrWhiteSpace(ProfileId)) { parts.Add($"profile {ProfileId}"); }
            if (!string.IsNullOrWhiteSpace(FitInputs)) { parts.Add($"fit[{FitInputs}]"); }
            if (!string.IsNullOrWhiteSpace(CommandLine)) { parts.Add(CommandLine); }
            if (!string.IsNullOrWhiteSpace(SettingsFingerprint)) { parts.Add($"settings#{SettingsFingerprint}"); }
            return parts.Count > 0 ? string.Join(" | ", parts) : "(no provenance)";
        }
    }
}
