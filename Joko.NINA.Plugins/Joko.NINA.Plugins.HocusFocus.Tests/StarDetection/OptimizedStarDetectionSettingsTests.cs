using System;
using Newtonsoft.Json;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class OptimizedStarDetectionSettingsTests {

    private static OptimizedStarDetectionSettings Make() {
        return new OptimizedStarDetectionSettings {
            BrightnessSensitivity = 3.3,
            StarClippingMultiplier = 2.7,
            NoiseClippingMultiplier = 5.5,
            StarPeakResponse = 0.66,
            MaxDistortion = 0.42,
            MinHFR = 1.1,
            StarCenterTolerance = 0.45,
            StructureLayers = 7,
            NoiseReductionRadius = 6,
            MinStarBoundingBoxSize = 8,
            HotpixelThresholdingEnabled = false,
            HotpixelThreshold = 0.02,
            CreatedAtUtc = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc),
            RunCount = 5,
            BaselineJ = 0.9,
            FinalJ = 0.4,
            RecommendedStepSize = 25,
            RecommendedOffsetSteps = 6,
            SchemaVersion = 1
        };
    }

    [Test]
    public void DefaultSchemaVersion_IsCurrent() {
        // v2 added the defocus-aware axes (master + donut/spike knobs); v3 added the optional Provenance block
        // (F30) and changed no knob semantics. Asserted against the constant as well as the literal, so the next
        // bump does not silently pass while leaving the default behind.
        Assert.Multiple(() => {
            Assert.That(new OptimizedStarDetectionSettings().SchemaVersion,
                Is.EqualTo(OptimizedStarDetectionSettings.CurrentSchemaVersion));
            Assert.That(OptimizedStarDetectionSettings.CurrentSchemaVersion, Is.EqualTo(3));
        });
    }

    [Test]
    public void JsonRoundTrip_PreservesAllFields() {
        var original = Make();

        var json = JsonConvert.SerializeObject(original);
        var restored = JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(json);

        Assert.Multiple(() => {
            Assert.That(restored.BrightnessSensitivity, Is.EqualTo(original.BrightnessSensitivity));
            Assert.That(restored.StarClippingMultiplier, Is.EqualTo(original.StarClippingMultiplier));
            Assert.That(restored.NoiseClippingMultiplier, Is.EqualTo(original.NoiseClippingMultiplier));
            Assert.That(restored.StarPeakResponse, Is.EqualTo(original.StarPeakResponse));
            Assert.That(restored.MaxDistortion, Is.EqualTo(original.MaxDistortion));
            Assert.That(restored.MinHFR, Is.EqualTo(original.MinHFR));
            Assert.That(restored.StarCenterTolerance, Is.EqualTo(original.StarCenterTolerance));
            Assert.That(restored.StructureLayers, Is.EqualTo(original.StructureLayers));
            Assert.That(restored.NoiseReductionRadius, Is.EqualTo(original.NoiseReductionRadius));
            Assert.That(restored.MinStarBoundingBoxSize, Is.EqualTo(original.MinStarBoundingBoxSize));
            Assert.That(restored.HotpixelThresholdingEnabled, Is.EqualTo(original.HotpixelThresholdingEnabled));
            Assert.That(restored.HotpixelThreshold, Is.EqualTo(original.HotpixelThreshold));
            Assert.That(restored.CreatedAtUtc, Is.EqualTo(original.CreatedAtUtc));
            Assert.That(restored.RunCount, Is.EqualTo(original.RunCount));
            Assert.That(restored.BaselineJ, Is.EqualTo(original.BaselineJ));
            Assert.That(restored.FinalJ, Is.EqualTo(original.FinalJ));
            Assert.That(restored.RecommendedStepSize, Is.EqualTo(original.RecommendedStepSize));
            Assert.That(restored.RecommendedOffsetSteps, Is.EqualTo(original.RecommendedOffsetSteps));
            Assert.That(restored.SchemaVersion, Is.EqualTo(original.SchemaVersion));
        });
    }

    [Test]
    public void FromParams_MapsCuratedKnobsAndMetadata() {
        // Distinct, recognizable values so a mis-wired field (esp. the renamed ones) is caught.
        var p = new StarDetectorParams {
            Sensitivity = 3.3,
            StarClippingMultiplier = 2.7,
            NoiseClippingMultiplier = 5.5,
            PeakResponse = 0.66,
            MaxDistortion = 0.42,
            MinHFR = 1.1,
            StarCenterTolerance = 0.45,
            StructureLayers = 7,
            NoiseReductionRadius = 6,
            MinimumStarBoundingBoxSize = 8,
            HotpixelThresholdingEnabled = false,
            HotpixelThreshold = 0.02
        };

        var before = DateTime.UtcNow;
        var dto = OptimizedStarDetectionSettings.FromParams(p, runCount: 5, baselineJ: 0.9, finalJ: 0.4, recommendedStepSize: 25, recommendedOffsetSteps: 6);
        var after = DateTime.UtcNow;

        Assert.Multiple(() => {
            // Curated knobs (note the renames: Sensitivity->BrightnessSensitivity, PeakResponse->StarPeakResponse,
            // MinimumStarBoundingBoxSize->MinStarBoundingBoxSize).
            Assert.That(dto.BrightnessSensitivity, Is.EqualTo(3.3));
            Assert.That(dto.StarClippingMultiplier, Is.EqualTo(2.7));
            Assert.That(dto.NoiseClippingMultiplier, Is.EqualTo(5.5));
            Assert.That(dto.StarPeakResponse, Is.EqualTo(0.66));
            Assert.That(dto.MaxDistortion, Is.EqualTo(0.42));
            Assert.That(dto.MinHFR, Is.EqualTo(1.1));
            Assert.That(dto.StarCenterTolerance, Is.EqualTo(0.45));
            Assert.That(dto.StructureLayers, Is.EqualTo(7));
            Assert.That(dto.NoiseReductionRadius, Is.EqualTo(6));
            Assert.That(dto.MinStarBoundingBoxSize, Is.EqualTo(8));
            Assert.That(dto.HotpixelThresholdingEnabled, Is.False);
            Assert.That(dto.HotpixelThreshold, Is.EqualTo(0.02));

            // Metadata.
            Assert.That(dto.RunCount, Is.EqualTo(5));
            Assert.That(dto.BaselineJ, Is.EqualTo(0.9));
            Assert.That(dto.FinalJ, Is.EqualTo(0.4));
            Assert.That(dto.RecommendedStepSize, Is.EqualTo(25));
            Assert.That(dto.RecommendedOffsetSteps, Is.EqualTo(6));
            Assert.That(dto.SchemaVersion, Is.EqualTo(OptimizedStarDetectionSettings.CurrentSchemaVersion));
            Assert.That(dto.CreatedAtUtc, Is.InRange(before, after));
            Assert.That(dto.CreatedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public void FromParams_NullParams_Throws() {
        Assert.Throws<ArgumentNullException>(() => OptimizedStarDetectionSettings.FromParams(null, 1, 0.0, 0.0, 1, 1));
    }

    [Test]
    public void Clone_ProducesIndependentCopy() {
        var original = Make();
        var clone = original.Clone();
        clone.BrightnessSensitivity = 99.0;

        Assert.Multiple(() => {
            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(original.BrightnessSensitivity, Is.EqualTo(3.3));
            Assert.That(clone.BrightnessSensitivity, Is.EqualTo(99.0));
        });
    }

    // ── F30: a landing must say which invocation produced it ────────────────────────────────────────────────

    [Test]
    public void FromParams_WithoutProvenance_OmitsTheBlockEntirely() {
        // A snapshot written by the SHIPPING plugin has no argv and no harness settings file. It must stay
        // byte-identical to before this field existed, and a reader must see "unattributable" rather than a
        // fabricated producer.
        var dto = OptimizedStarDetectionSettings.FromParams(new StarDetectorParams(), 1, 0.5, 0.9, 10, 4);
        var json = JsonConvert.SerializeObject(dto);

        Assert.Multiple(() => {
            Assert.That(dto.Provenance, Is.Null);
            Assert.That(json, Does.Not.Contain("Provenance"));
        });
    }

    [Test]
    public void FromParams_WithProvenance_RoundTripsTheInvocation() {
        var prov = new OptimizerProvenance {
            Producer = "TestApp optimize",
            CommandLine = "optimize --per-run --runs D:\\SyntheticAutofocusBank --donut",
            SettingsFingerprint = "a1b2c3d4e5f6",
            ProducerVersion = "3.4.0.1"
        };
        var dto = OptimizedStarDetectionSettings.FromParams(new StarDetectorParams(), 17, 0.5, 0.9, 10, 4, prov);
        var round = JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(JsonConvert.SerializeObject(dto));

        Assert.Multiple(() => {
            // THE point of F30: the two F23 arms differed only by a flag, and this is where that shows.
            Assert.That(round.Provenance.CommandLine, Does.Contain("--donut"));
            Assert.That(round.Provenance.Producer, Is.EqualTo("TestApp optimize"));
            Assert.That(round.Provenance.SettingsFingerprint, Is.EqualTo("a1b2c3d4e5f6"));
            Assert.That(round.SchemaVersion, Is.EqualTo(OptimizedStarDetectionSettings.CurrentSchemaVersion));
        });
    }

    [Test]
    public void FromParams_CopiesProvenance_SoTheProducerCannotMutateAStoredLanding() {
        var prov = new OptimizerProvenance { CommandLine = "optimize --per-run" };
        var dto = OptimizedStarDetectionSettings.FromParams(new StarDetectorParams(), 1, 0.0, 0.0, 1, 1, prov);
        prov.CommandLine = "optimize --per-run --donut"; // the caller reuses one instance across the batch

        Assert.That(dto.Provenance.CommandLine, Is.EqualTo("optimize --per-run"),
            "a landing records the invocation as it was when written");
    }

    [Test]
    public void Clone_DeepCopiesProvenance_NotJustTheReference() {
        // MemberwiseClone is shallow: without an explicit copy every clone would ALIAS one provenance instance,
        // and a mutation through any of them would rewrite the history of all the others.
        var original = OptimizedStarDetectionSettings.FromParams(
            new StarDetectorParams(), 1, 0.0, 0.0, 1, 1, new OptimizerProvenance { CommandLine = "original" });
        var clone = original.Clone();
        clone.Provenance.CommandLine = "mutated";

        Assert.Multiple(() => {
            Assert.That(clone.Provenance, Is.Not.SameAs(original.Provenance));
            Assert.That(original.Provenance.CommandLine, Is.EqualTo("original"));
        });
    }

    [Test]
    public void ASchemaV2File_StillLoads_WithNoProvenance() {
        // Forward/backward knob compatibility: v3 added only metadata, so every landing already on disk must
        // still deserialize — reporting "no provenance" rather than failing or inventing one.
        const string v2 = "{\"BrightnessSensitivity\":3.3,\"SchemaVersion\":2,\"RunCount\":4}";
        var dto = JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(v2);

        Assert.Multiple(() => {
            Assert.That(dto.SchemaVersion, Is.EqualTo(2));
            Assert.That(dto.BrightnessSensitivity, Is.EqualTo(3.3));
            Assert.That(dto.Provenance, Is.Null);
        });
    }

    [Test]
    public void Provenance_ToString_SkipsWhatIsUnset() {
        Assert.Multiple(() => {
            Assert.That(new OptimizerProvenance().ToString(), Is.EqualTo("(no provenance)"));
            Assert.That(new OptimizerProvenance { Producer = "TestApp optimize", CommandLine = "--donut" }.ToString(),
                Is.EqualTo("TestApp optimize | --donut"));
        });
    }
}
