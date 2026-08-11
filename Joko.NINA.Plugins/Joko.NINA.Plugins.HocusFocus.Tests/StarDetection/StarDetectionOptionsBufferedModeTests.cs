using System;
using System.Collections.Generic;
using System.IO;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class StarDetectionOptionsBufferedModeTests {

    private static (StarDetectionOptions options, InMemoryPluginOptionsAccessor store) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new StarDetectionOptions(profile, store);
        return (options, store);
    }

    private static OptimizedStarDetectionSettings MakeSnapshot() {
        // Distinctive curated values, all within each property's valid range.
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
            HotpixelThresholdingEnabled = true,
            HotpixelThreshold = 0.02
        };
    }

    [Test]
    public void PersistToProfile_DefaultsTrue() {
        var (options, _) = Build();
        Assert.That(options.PersistToProfile, Is.True);
    }

    [Test]
    public void Suppressed_NormalSetter_UpdatesFieldRaisesButFreezesLegacyKey() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 7; // persisted pre-suppression

        options.PersistToProfile = false;
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.NoiseReductionRadius = 9;

        Assert.Multiple(() => {
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(9));
            Assert.That(raised, Does.Contain(nameof(StarDetectionOptions.NoiseReductionRadius)));
            Assert.That(store.GetValueInt32("NoiseReductionRadius", -1), Is.EqualTo(7));
        });
    }

    [Test]
    public void Suppressed_MachineLocalKeysStillWriteThrough() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.PersistToProfile = false;

        var path = Path.Combine(Path.GetTempPath(), "hf-buffered-mode-test");
        options.DebugMode = true;
        options.PSFParallelPartitionSize = 250;
        options.IntermediateSavePath = path;

        Assert.Multiple(() => {
            Assert.That(store.GetValueBoolean("DetectionDebugMode", false), Is.True);
            Assert.That(store.GetValueInt32("PSFParallelPartitionSize", -1), Is.EqualTo(250));
            Assert.That(store.GetValueString(nameof(StarDetectionOptions.IntermediateSavePath), null), Is.EqualTo(path));
        });
    }

    [Test]
    public void Suppressed_ApplyAndClearOptimizedSettings_SkipLegacyKeys() {
        var (options, store) = Build();
        options.PersistToProfile = false;

        options.ApplyOptimizedSettings(MakeSnapshot());
        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.True);
            Assert.That(options.UseOptimizedSettings, Is.True);
            Assert.That(store.Snapshot.ContainsKey("OptimizedSettingsJson"), Is.False);
            Assert.That(store.Snapshot.ContainsKey(nameof(StarDetectionOptions.UseOptimizedSettings)), Is.False);
        });

        options.ClearOptimizedSettings();
        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(store.Snapshot.ContainsKey("OptimizedSettingsJson"), Is.False);
        });
    }

    [Test]
    public void Suppressed_ResetDefaults_SkipsLegacyKeys() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 7;

        options.PersistToProfile = false;
        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.UseAdvanced, Is.False);
            // F70: 4, not 3 — ResetDefaults now ends in the Simple-mode derivation unconditionally, so the
            // in-memory value is the compensated default. Suppression is unaffected: the assertion below still
            // shows the LEGACY KEY frozen at the pre-suppression 7, which is what this test is about.
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(4));
            Assert.That(store.GetValueBoolean("UseAdvanced", false), Is.True);
            Assert.That(store.GetValueInt32("NoiseReductionRadius", -1), Is.EqualTo(7));
            Assert.That(store.Snapshot.ContainsKey("OptimizedSettingsJson"), Is.False);
        });
    }

    [Test]
    public void ReenableAndReload_RestoresPreSuppressionPersistedValues() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 7;
        options.MinHFR = 2.5;

        options.PersistToProfile = false;
        options.NoiseReductionRadius = 9;
        options.MinHFR = 0.8;
        options.ApplyOptimizedSettings(MakeSnapshot());

        options.PersistToProfile = true;
        options.ReloadFromProfile();

        Assert.Multiple(() => {
            Assert.That(options.UseAdvanced, Is.True);
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(7));
            Assert.That(options.MinHFR, Is.EqualTo(2.5));
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(options.UseOptimizedSettings, Is.False);
        });
    }

    [Test]
    public void Suppressed_SimpleModeDerivation_StillFires() {
        var (options, store) = Build();
        var persistedRadius = store.GetValueInt32("NoiseReductionRadius", -1);

        options.PersistToProfile = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.High;

        Assert.Multiple(() => {
            Assert.That(options.StarMeasurementNoiseReductionEnabled, Is.True);
            Assert.That(options.NoiseReductionRadius, Is.GreaterThanOrEqualTo(5));
            Assert.That(store.GetValueInt32("NoiseReductionRadius", -1), Is.EqualTo(persistedRadius));
            Assert.That(store.Snapshot.ContainsKey("Simple_NoiseLevel"), Is.False);
            Assert.That(store.Snapshot.ContainsKey(nameof(StarDetectionOptions.StarMeasurementNoiseReductionEnabled)), Is.False);
        });
    }
}
