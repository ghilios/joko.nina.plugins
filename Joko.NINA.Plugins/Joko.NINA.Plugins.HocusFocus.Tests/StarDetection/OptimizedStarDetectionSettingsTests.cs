using System;
using Newtonsoft.Json;
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
    public void DefaultSchemaVersion_IsOne() {
        Assert.That(new OptimizedStarDetectionSettings().SchemaVersion, Is.EqualTo(1));
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
