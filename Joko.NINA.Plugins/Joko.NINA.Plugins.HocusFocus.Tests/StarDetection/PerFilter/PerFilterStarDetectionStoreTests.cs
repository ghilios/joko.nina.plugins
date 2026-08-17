using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.PerFilter;

[TestFixture]
public class PerFilterStarDetectionStoreTests {

    // Distinctive global settings, machine-local fields deliberately dirty so scrubbing is observable.
    private static StarDetectionSettingsSnapshot MakeGlobalSnapshot() {
        return new StarDetectionSettingsSnapshot {
            UseAdvanced = true,
            BrightnessSensitivity = 7.5,
            StructureLayers = 6,
            MinHFR = 1.05,
            DebugMode = true,
            IntermediateSavePath = @"C:\hf\debug",
            SaveIntermediateImages = true,
            PSFParallelPartitionSize = 250,
            OptimizedSettings = new OptimizedStarDetectionSettings { BrightnessSensitivity = 3.3, StructureLayers = 7 }
        };
    }

    private static (PerFilterStarDetectionStore store, InMemoryPluginOptionsAccessor accessor, IProfileService profile) Build(params string[] profileFilterNames) {
        var profile = Substitute.For<IProfileService>();
        // List ctor avoids the WPF SynchronizationContext path that per-item Add would take in AsyncObservableCollection.
        var filters = new ObserveAllCollection<FilterInfo>(profileFilterNames.Select((n, i) => new FilterInfo(n, 0, (short)i)));
        profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
        var accessor = new InMemoryPluginOptionsAccessor();
        var store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);
        return (store, accessor, profile);
    }

    [Test]
    public void Enable_SeedsEveryProfileFilterFromScrubbedGlobal() {
        var (store, accessor, _) = Build("Ha", "OIII");
        var enabledEvents = 0;
        store.EnabledChanged += (_, _) => enabledEvents++;

        store.Enabled = true;

        var ha = store.TryGetSnapshot("Ha");
        var oiii = store.TryGetSnapshot("OIII");
        Assert.Multiple(() => {
            Assert.That(enabledEvents, Is.EqualTo(1));
            Assert.That(accessor.Snapshot["PerFilterStarDetectionEnabled"], Is.True);
            Assert.That(store.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha", "OIII" }));
            Assert.That(ha, Is.Not.Null);
            Assert.That(oiii, Is.Not.Null);
            // Detection settings copied from the captured global snapshot...
            Assert.That(ha.BrightnessSensitivity, Is.EqualTo(7.5));
            Assert.That(ha.StructureLayers, Is.EqualTo(6));
            Assert.That(ha.OptimizedSettings.BrightnessSensitivity, Is.EqualTo(3.3));
            // ...with the machine-local fields scrubbed.
            Assert.That(ha.DebugMode, Is.False);
            Assert.That(ha.IntermediateSavePath, Is.EqualTo(""));
            Assert.That(ha.SaveIntermediateImages, Is.False);
            Assert.That(ha.PSFParallelPartitionSize, Is.EqualTo(100));
        });
    }

    [Test]
    public void Enable_PersistsBlobReloadableByNewStoreInstance() {
        var (store, accessor, profile) = Build("Ha");
        store.Enabled = true;

        var reloaded = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

        Assert.Multiple(() => {
            Assert.That(reloaded.Enabled, Is.True);
            Assert.That(reloaded.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha" }));
            Assert.That(reloaded.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(7.5));
        });
    }

    [Test]
    public void GlobalSeed_PersistsAcrossReload_AndSeedsNewNames() {
        var (store, accessor, profile) = Build("Ha");
        store.Enabled = true;

        // Reload with a capture delegate producing DIFFERENT values — the persisted GlobalSeed must win.
        var reloaded = new PerFilterStarDetectionStore(profile, accessor, () => new StarDetectionSettingsSnapshot { BrightnessSensitivity = 1.0 });
        var seeded = reloaded.GetOrSeedSnapshot("SII");

        Assert.That(seeded.BrightnessSensitivity, Is.EqualTo(7.5));
    }

    [Test]
    public void GetOrSeedSnapshot_UnknownName_SeedsFromGlobalSeedAndRaisesSnapshotChanged() {
        var (store, _, _) = Build("Ha");
        store.Enabled = true;
        var changed = new List<string>();
        store.SnapshotChanged += (_, e) => changed.Add(e.FilterName);

        var sii = store.GetOrSeedSnapshot("SII");

        Assert.Multiple(() => {
            Assert.That(sii.BrightnessSensitivity, Is.EqualTo(7.5));
            Assert.That(sii.PSFParallelPartitionSize, Is.EqualTo(100));
            Assert.That(changed, Is.EqualTo(new[] { "SII" }));
            Assert.That(store.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha", "SII" }));
        });

        // Second fetch returns the stored entry without another seed event.
        store.GetOrSeedSnapshot("SII");
        Assert.That(changed, Has.Count.EqualTo(1));
    }

    [Test]
    public void GetOrSeedSnapshot_NoGlobalSeed_FallsBackToScrubbedCapture() {
        var (store, _, _) = Build("Ha"); // never enabled -> GlobalSeed is null

        var snap = store.GetOrSeedSnapshot("Ha");

        Assert.Multiple(() => {
            Assert.That(snap.BrightnessSensitivity, Is.EqualTo(7.5));
            Assert.That(snap.DebugMode, Is.False);
            Assert.That(snap.PSFParallelPartitionSize, Is.EqualTo(100));
        });
    }

    [Test]
    public void UpsertSnapshot_RoundTripsScrubbedAndRaisesSnapshotChanged() {
        var (store, accessor, profile) = Build("Ha");
        var changed = new List<string>();
        store.SnapshotChanged += (_, e) => changed.Add(e.FilterName);
        var custom = MakeGlobalSnapshot();
        custom.BrightnessSensitivity = 12.25;
        custom.MinHFR = 2.5;

        store.UpsertSnapshot("Ha", custom);

        var fetched = store.TryGetSnapshot("Ha");
        Assert.Multiple(() => {
            Assert.That(changed, Is.EqualTo(new[] { "Ha" }));
            Assert.That(fetched.BrightnessSensitivity, Is.EqualTo(12.25));
            Assert.That(fetched.MinHFR, Is.EqualTo(2.5));
            // Stored scrubbed: machine-local fields normalized even though the input had them set.
            Assert.That(fetched.DebugMode, Is.False);
            Assert.That(fetched.IntermediateSavePath, Is.EqualTo(""));
            Assert.That(fetched.SaveIntermediateImages, Is.False);
            Assert.That(fetched.PSFParallelPartitionSize, Is.EqualTo(100));
        });

        var reloaded = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);
        Assert.That(reloaded.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(12.25));
    }

    [Test]
    public void Snapshots_AreClonesNotReferences() {
        var (store, _, _) = Build("Ha");
        var custom = MakeGlobalSnapshot();
        store.UpsertSnapshot("Ha", custom);

        // Mutating the input after the upsert must not change the stored copy.
        custom.BrightnessSensitivity = 999.0;
        custom.OptimizedSettings.StructureLayers = 42;
        var first = store.TryGetSnapshot("Ha");
        // Mutating a fetched snapshot must not change the stored copy either.
        first.BrightnessSensitivity = 555.0;
        first.OptimizedSettings.StructureLayers = 41;
        var second = store.TryGetSnapshot("Ha");

        Assert.Multiple(() => {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.BrightnessSensitivity, Is.EqualTo(7.5));
            Assert.That(second.OptimizedSettings.StructureLayers, Is.EqualTo(7));
        });
    }

    [Test]
    public void Disable_RetainsStoredSnapshots_AndReEnableDoesNotOverwrite() {
        var (store, accessor, _) = Build("Ha", "OIII");
        store.Enabled = true;
        var custom = MakeGlobalSnapshot();
        custom.BrightnessSensitivity = 12.25;
        store.UpsertSnapshot("Ha", custom);

        store.Enabled = false;

        Assert.Multiple(() => {
            Assert.That(accessor.Snapshot["PerFilterStarDetectionEnabled"], Is.False);
            Assert.That(store.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha", "OIII" }));
        });

        store.Enabled = true;

        Assert.That(store.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(12.25));
    }

    [Test]
    public void CorruptJson_IsDiscardedAndDoesNotThrow() {
        var profile = Substitute.For<IProfileService>();
        var accessor = new InMemoryPluginOptionsAccessor();
        accessor.SetValueBoolean("PerFilterStarDetectionEnabled", true);
        accessor.SetValueString("PerFilterStarDetectionJson", "{ this is not valid json");

        PerFilterStarDetectionStore store = null;
        Assert.DoesNotThrow(() => store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot));
        Assert.Multiple(() => {
            // The flag survives; the blob is discarded so filters fall back to lazy re-seeding.
            Assert.That(store.Enabled, Is.True);
            Assert.That(store.GetKnownFilterNames(), Is.Empty);
        });
    }

    [Test]
    public void ProfileChanged_ReReadsBothKeysFromAccessor() {
        var (store, accessor, profile) = Build("Ha");
        store.Enabled = true;
        Assert.That(store.GetKnownFilterNames(), Is.Not.Empty);

        accessor.Clear();
        profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

        Assert.Multiple(() => {
            Assert.That(store.Enabled, Is.False);
            Assert.That(store.GetKnownFilterNames(), Is.Empty);
            Assert.That(store.TryGetSnapshot("Ha"), Is.Null);
        });
    }

    [Test]
    public void Scrub_NormalizesMachineLocalFieldsOnAClone() {
        var source = MakeGlobalSnapshot();

        var scrubbed = PerFilterStarDetectionStore.Scrub(source);

        Assert.Multiple(() => {
            Assert.That(scrubbed, Is.Not.SameAs(source));
            Assert.That(scrubbed.DebugMode, Is.False);
            Assert.That(scrubbed.IntermediateSavePath, Is.EqualTo(""));
            Assert.That(scrubbed.SaveIntermediateImages, Is.False);
            Assert.That(scrubbed.PSFParallelPartitionSize, Is.EqualTo(100));
            // Non-machine-local values intact on the clone; the source is untouched.
            Assert.That(scrubbed.BrightnessSensitivity, Is.EqualTo(7.5));
            Assert.That(source.DebugMode, Is.True);
            Assert.That(source.PSFParallelPartitionSize, Is.EqualTo(250));
        });
    }

    [Test]
    public void PerFilterStarDetectionData_JsonRoundTrip_PreservesEntries() {
        var haSettings = MakeGlobalSnapshot();
        haSettings.BrightnessSensitivity = 11.5;
        var data = new PerFilterStarDetectionData { GlobalSeed = MakeGlobalSnapshot() };
        data.Filters.Add(new PerFilterStarDetectionEntry { FilterName = "Ha", Settings = haSettings });
        data.Filters.Add(new PerFilterStarDetectionEntry { FilterName = "OIII", Settings = new StarDetectionSettingsSnapshot { BrightnessSensitivity = 4.5 } });

        var json = JsonConvert.SerializeObject(data);
        var restored = JsonConvert.DeserializeObject<PerFilterStarDetectionData>(json);

        Assert.Multiple(() => {
            Assert.That(restored.SchemaVersion, Is.EqualTo(1));
            Assert.That(restored.GlobalSeed.BrightnessSensitivity, Is.EqualTo(7.5));
            Assert.That(restored.Filters, Has.Count.EqualTo(2));
            Assert.That(restored.Filters[0].FilterName, Is.EqualTo("Ha"));
            Assert.That(restored.Filters[0].Settings.BrightnessSensitivity, Is.EqualTo(11.5));
            Assert.That(restored.Filters[0].Settings.OptimizedSettings.BrightnessSensitivity, Is.EqualTo(3.3));
            Assert.That(restored.Filters[0].Settings.OptimizedSettings.StructureLayers, Is.EqualTo(7));
            Assert.That(restored.Filters[1].FilterName, Is.EqualTo("OIII"));
            Assert.That(restored.Filters[1].Settings.BrightnessSensitivity, Is.EqualTo(4.5));
            Assert.That(restored.Filters[1].Settings.OptimizedSettings, Is.Null);
        });
    }

    // --- Auto-focus sweep geometry ------------------------------------------------------------------------

    [Test]
    public void GetSweepGeometry_UnknownFilter_ReturnsUnsetAndPersistsNothing() {
        // The seed asymmetry against GetOrSeedSnapshot, pinned: detection has no fallback so it must seed on
        // read, geometry falls back to the profile so it must not. The engine calls this on its run path.
        var (store, accessor, _) = Build("Ha");
        store.Enabled = true;
        var before = accessor.Snapshot["PerFilterStarDetectionJson"];

        var geometry = store.GetSweepGeometry("Ha");

        Assert.Multiple(() => {
            Assert.That(geometry, Is.Not.Null);
            Assert.That(geometry.IsUnset, Is.True);
            Assert.That(accessor.Snapshot["PerFilterStarDetectionJson"], Is.EqualTo(before));
        });
    }

    [Test]
    public void GetSweepGeometry_ReturnsAClone_MutatingItPersistsNothing() {
        var (store, _, _) = Build("Ha");
        store.Enabled = true;
        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 30, InitialOffsetSteps = 4 });

        var handedOut = store.GetSweepGeometry("Ha");
        handedOut.StepSize = 999;

        Assert.That(store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(30));
    }

    [Test]
    public void SetSweepGeometry_RoundTripsThroughANewStoreInstance() {
        var (store, accessor, profile) = Build("Ha", "OIII");
        store.Enabled = true;
        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 42, InitialOffsetSteps = 6 });

        var reloaded = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

        Assert.Multiple(() => {
            Assert.That(reloaded.GetSweepGeometry("Ha").StepSize, Is.EqualTo(42));
            Assert.That(reloaded.GetSweepGeometry("Ha").InitialOffsetSteps, Is.EqualTo(6));
            Assert.That(reloaded.GetSweepGeometry("OIII").IsUnset, Is.True);
        });
    }

    [Test]
    public void SetSweepGeometry_RaisesSweepGeometryChangedButNotSnapshotChanged() {
        // The binder answers SnapshotChanged by reloading the entire detection buffer, which a geometry edit
        // must not trigger -- that is the whole reason the two events are separate.
        var (store, _, _) = Build("Ha");
        store.Enabled = true;
        var geometryEvents = new List<string>();
        var snapshotEvents = new List<string>();
        store.SweepGeometryChanged += (_, e) => geometryEvents.Add(e.FilterName);
        store.SnapshotChanged += (_, e) => snapshotEvents.Add(e.FilterName);

        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 15 });

        Assert.Multiple(() => {
            Assert.That(geometryEvents, Is.EqualTo(new[] { "Ha" }));
            Assert.That(snapshotEvents, Is.Empty);
        });
    }

    [TestCase(0)]
    [TestCase(-7)]
    public void SetSweepGeometry_NonPositiveStepSize_NormalizedToInherit(int stepSize) {
        var (store, _, _) = Build("Ha");
        store.Enabled = true;

        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = stepSize, InitialOffsetSteps = 5 });

        Assert.Multiple(() => {
            Assert.That(store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(PerFilterSweepGeometry.Inherit));
            Assert.That(store.GetSweepGeometry("Ha").HasStepSize, Is.False);
            Assert.That(store.GetSweepGeometry("Ha").InitialOffsetSteps, Is.EqualTo(5), "the other field is unaffected");
        });
    }

    [Test]
    public void SetSweepGeometry_ZeroOffsetSteps_NormalizedToInherit() {
        // A sweep needs at least one point per side, matching the engine's own validation.
        var (store, _, _) = Build("Ha");
        store.Enabled = true;

        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 0 });

        Assert.That(store.GetSweepGeometry("Ha").HasOffsetSteps, Is.False);
    }

    [Test]
    public void SetSweepGeometry_Null_ClearsTheOverride() {
        var (store, _, _) = Build("Ha");
        store.Enabled = true;
        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = 3 });

        store.SetSweepGeometry("Ha", null);

        Assert.That(store.GetSweepGeometry("Ha").IsUnset, Is.True);
    }

    [Test]
    public void SetSweepGeometry_ForAFilterWithNoSnapshot_SurvivesPersist() {
        // A wizard Accept can write geometry for a filter whose options page was never opened, so that filter
        // has geometry and no snapshot. Persisting only the snapshot map would silently drop it.
        var (store, accessor, profile) = Build("Ha");
        store.SetSweepGeometry("NeverOpened", new PerFilterSweepGeometry { StepSize = 77, InitialOffsetSteps = 2 });

        var reloaded = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

        Assert.Multiple(() => {
            Assert.That(reloaded.GetSweepGeometry("NeverOpened").StepSize, Is.EqualTo(77));
            Assert.That(reloaded.TryGetSnapshot("NeverOpened"), Is.Null);
            Assert.That(reloaded.GetKnownFilterNames(), Contains.Item("NeverOpened"));
        });
    }

    [Test]
    public void DetectionUpsert_DoesNotClobberSweepGeometry() {
        var (store, _, _) = Build("Ha");
        store.Enabled = true;
        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 33, InitialOffsetSteps = 4 });

        var snapshot = store.GetOrSeedSnapshot("Ha");
        snapshot.BrightnessSensitivity = 9.9;
        store.UpsertSnapshot("Ha", snapshot);

        Assert.Multiple(() => {
            Assert.That(store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(33));
            Assert.That(store.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(9.9));
        });
    }

    [Test]
    public void SweepGeometryUpsert_DoesNotClobberDetectionSettings() {
        var (store, _, _) = Build("Ha");
        store.Enabled = true;
        var snapshot = store.GetOrSeedSnapshot("Ha");
        snapshot.BrightnessSensitivity = 2.25;
        store.UpsertSnapshot("Ha", snapshot);

        store.SetSweepGeometry("Ha", new PerFilterSweepGeometry { StepSize = 12 });

        Assert.That(store.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(2.25));
    }

    [Test]
    public void Enable_DoesNotSeedAnySweepGeometry() {
        // Detection must seed on enable because it has no fallback. Geometry must NOT: a copy of the profile
        // value would freeze and drift silently the moment the profile value changed, and would destroy the
        // "never set" state that makes clearing an override meaningful.
        var (store, _, _) = Build("Ha", "OIII");

        store.Enabled = true;

        Assert.Multiple(() => {
            Assert.That(store.GetSweepGeometry("Ha").IsUnset, Is.True);
            Assert.That(store.GetSweepGeometry("OIII").IsUnset, Is.True);
        });
    }

    [Test]
    public void LegacyBlobWithoutSweepGeometry_LoadsWithEveryFilterInheriting() {
        var (_, accessor, profile) = Build("Ha");
        // Hand-written v1 blob, exactly as a build predating this field would have produced.
        accessor.SetValueString(
            "PerFilterStarDetectionJson",
            "{\"SchemaVersion\":1,\"GlobalSeed\":null,\"Filters\":[{\"FilterName\":\"Ha\",\"Settings\":{\"BrightnessSensitivity\":5.5}}]}");

        var store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

        Assert.Multiple(() => {
            Assert.That(store.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(5.5), "detection settings still load");
            Assert.That(store.GetSweepGeometry("Ha").IsUnset, Is.True, "a missing field means inherit");
        });
    }

    [Test]
    public void CorruptSweepGeometryValues_AreNormalizedOnRead() {
        var (_, accessor, profile) = Build("Ha");
        accessor.SetValueString(
            "PerFilterStarDetectionJson",
            "{\"SchemaVersion\":1,\"Filters\":[{\"FilterName\":\"Ha\",\"SweepGeometry\":{\"StepSize\":0,\"InitialOffsetSteps\":-3}}]}");

        var store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

        Assert.That(store.GetSweepGeometry("Ha").IsUnset, Is.True);
    }

    [Test]
    public void NewerSchemaVersion_LoadsBestEffortAndDoesNotDiscard() {
        var (_, accessor, profile) = Build("Ha");
        accessor.SetValueString(
            "PerFilterStarDetectionJson",
            "{\"SchemaVersion\":99,\"Filters\":[{\"FilterName\":\"Ha\",\"SweepGeometry\":{\"StepSize\":21,\"InitialOffsetSteps\":3}}]}");

        var store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

        Assert.That(store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(21));
    }

    [Test]
    public void PerFilterSweepGeometry_JsonRoundTrip_PreservesBothFields() {
        var data = new PerFilterStarDetectionData();
        data.Filters.Add(new PerFilterStarDetectionEntry {
            FilterName = "Ha",
            SweepGeometry = new PerFilterSweepGeometry { StepSize = 18, InitialOffsetSteps = 7 }
        });

        var restored = JsonConvert.DeserializeObject<PerFilterStarDetectionData>(JsonConvert.SerializeObject(data));

        Assert.Multiple(() => {
            Assert.That(restored.Filters[0].SweepGeometry.StepSize, Is.EqualTo(18));
            Assert.That(restored.Filters[0].SweepGeometry.InitialOffsetSteps, Is.EqualTo(7));
        });
    }

    [Test]
    public void PerFilterSweepGeometry_ResolvesFieldsIndependently() {
        var geometry = new PerFilterSweepGeometry { StepSize = 25, InitialOffsetSteps = PerFilterSweepGeometry.Inherit };

        Assert.Multiple(() => {
            Assert.That(geometry.HasStepSize, Is.True);
            Assert.That(geometry.HasOffsetSteps, Is.False);
            Assert.That(geometry.IsUnset, Is.False);
        });
    }
}
