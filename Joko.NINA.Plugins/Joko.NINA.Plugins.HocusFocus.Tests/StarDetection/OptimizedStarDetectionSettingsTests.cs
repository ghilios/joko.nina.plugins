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
            RejectContaminatedStars = false,
            ExcludeSaturatedStarsFromHFR = false,
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
    public void DefaultSchemaVersion_IsThree() {
        // v2 added the defocus-aware axes (master + donut/spike knobs); v3 added the RejectContaminatedStars /
        // ExcludeSaturatedStarsFromHFR gates as searchable, snapshot-carried knobs.
        Assert.That(new OptimizedStarDetectionSettings().SchemaVersion, Is.EqualTo(3));
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
            Assert.That(restored.RejectContaminatedStars, Is.EqualTo(original.RejectContaminatedStars));
            Assert.That(restored.ExcludeSaturatedStarsFromHFR, Is.EqualTo(original.ExcludeSaturatedStarsFromHFR));
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
            HotpixelThreshold = 0.02,
            RejectContaminatedStars = false,        // non-default (true) so a mis-wire is caught
            ExcludeSaturatedStarsFromHFR = false    // non-default (true) so a mis-wire is caught
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
            Assert.That(dto.RejectContaminatedStars, Is.False);
            Assert.That(dto.ExcludeSaturatedStarsFromHFR, Is.False);

            // Metadata.
            Assert.That(dto.RunCount, Is.EqualTo(5));
            Assert.That(dto.BaselineJ, Is.EqualTo(0.9));
            Assert.That(dto.FinalJ, Is.EqualTo(0.4));
            Assert.That(dto.RecommendedStepSize, Is.EqualTo(25));
            Assert.That(dto.RecommendedOffsetSteps, Is.EqualTo(6));
            Assert.That(dto.SchemaVersion, Is.EqualTo(3));
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
}
