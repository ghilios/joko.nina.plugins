using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.PerFilter;

[TestFixture]
public class PerFilterEditBinderTests {

    private sealed class Harness {
        public IProfileService ProfileService;
        public IProfile Profile;
        public ObserveAllCollection<FilterInfo> Filters;
        public InMemoryPluginOptionsAccessor LegacyAccessor;
        public StarDetectionOptions Buffer;
        public IPerFilterStarDetectionStore Store;
        public PerFilterEditBinder Binder;
        public string CurrentFilterName;
    }

    private static ObserveAllCollection<FilterInfo> MakeFilters(params string[] names) {
        var filters = new ObserveAllCollection<FilterInfo>();
        foreach (var name in names) {
            filters.Add(new FilterInfo() { Name = name });
        }
        return filters;
    }

    private static IProfile MakeProfile(ObserveAllCollection<FilterInfo> filters) {
        var profile = Substitute.For<IProfile>();
        profile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
        return profile;
    }

    // Snapshots built through a real options object so every knob holds validated values (several
    // StarDetectionOptions setters throw on out-of-range input; a default-constructed snapshot has zeros).
    private static StarDetectionSettingsSnapshot SnapshotWith(Action<StarDetectionOptions> mutate) {
        var scratch = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
        mutate(scratch);
        return StarDetectionSettingsSnapshot.FromOptions(scratch);
    }

    private static Harness Build(bool enabled = false, string currentFilter = "Ha", string[] filterNames = null) {
        var filters = MakeFilters(filterNames ?? new[] { "L", "Ha", "Oiii" });
        var profile = MakeProfile(filters);
        var profileService = Substitute.For<IProfileService>();
        profileService.ActiveProfile.Returns(profile);

        var legacyAccessor = new InMemoryPluginOptionsAccessor();
        // Subscribes to ProfileChanged first, exactly like production construction order.
        var buffer = new StarDetectionOptions(profileService, legacyAccessor);

        var store = Substitute.For<IPerFilterStarDetectionStore>();
        store.Enabled.Returns(enabled);
        store.GetOrSeedSnapshot(Arg.Any<string>()).Returns(_ => StarDetectionSettingsSnapshot.FromOptions(buffer));

        var harness = new Harness() {
            ProfileService = profileService,
            Profile = profile,
            Filters = filters,
            LegacyAccessor = legacyAccessor,
            Buffer = buffer,
            Store = store,
            CurrentFilterName = currentFilter,
        };
        harness.Binder = new PerFilterEditBinder(store, buffer, profileService, () => harness.CurrentFilterName);
        return harness;
    }

    private static void SetEnabled(Harness h, bool enabled) {
        h.Store.Enabled.Returns(enabled);
        h.Store.EnabledChanged += Raise.Event<EventHandler>(h.Store, EventArgs.Empty);
    }

    [Test]
    public void Construction_FeatureAlreadyEnabled_EntersBufferedModeAndLoadsCurrentFilter() {
        var h = Build(enabled: true);
        Assert.Multiple(() => {
            Assert.That(h.Buffer.PersistToProfile, Is.False);
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
        });
        h.Store.Received().GetOrSeedSnapshot("Ha");
    }

    [Test]
    public void Enable_FlipsPersistToProfileOffAndLoadsCurrentWheelFilter() {
        var h = Build();
        Assert.That(h.Buffer.PersistToProfile, Is.True);

        SetEnabled(h, true);

        Assert.Multiple(() => {
            Assert.That(h.Buffer.PersistToProfile, Is.False);
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
        });
        h.Store.Received().GetOrSeedSnapshot("Ha");
    }

    [Test]
    public void Enable_NoCurrentWheelFilter_FallsBackToFirstProfileFilter() {
        var h = Build(currentFilter: null);

        SetEnabled(h, true);

        Assert.That(h.Binder.EditedFilterName, Is.EqualTo("L"));
        h.Store.Received().GetOrSeedSnapshot("L");
    }

    [Test]
    public void Disable_RestoresPersistToProfileAndReloadsPreEnableValues() {
        var h = Build();
        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.45;
        SetEnabled(h, true);

        h.Buffer.MaxDistortion = 0.91; // buffered edit: mirrored to the store, not the legacy keys
        Assert.That((double)h.LegacyAccessor.Snapshot["MaxDistortion"], Is.EqualTo(0.45));

        SetEnabled(h, false);

        Assert.Multiple(() => {
            Assert.That(h.Buffer.PersistToProfile, Is.True);
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.45));
        });
    }

    [Test]
    public void EditedFilterNameSwitch_LoadsThatFiltersSnapshotWithoutMirroring() {
        var h = Build(enabled: true);
        var oiii = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.31;
            o.MinHFR = 1.05;
        });
        h.Store.GetOrSeedSnapshot("Oiii").Returns(oiii);
        h.Store.ClearReceivedCalls();

        h.Binder.EditedFilterName = "Oiii";

        Assert.Multiple(() => {
            Assert.That(h.Buffer.UseAdvanced, Is.True);
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.31));
            Assert.That(h.Buffer.MinHFR, Is.EqualTo(1.05));
        });
        h.Store.Received().GetOrSeedSnapshot("Oiii");
        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());
    }

    [Test]
    public void BufferEdit_MirrorsToTheEditedFiltersStoreEntry() {
        var h = Build(enabled: true);
        h.Store.ClearReceivedCalls();

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.42;

        h.Store.Received().UpsertSnapshot("Ha", Arg.Is<StarDetectionSettingsSnapshot>(s => s.MaxDistortion == 0.42 && s.UseAdvanced));
    }

    [Test]
    public void BufferEdit_FeatureDisabled_DoesNotMirror() {
        var h = Build();

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.42;

        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());
    }

    // MutateFilterSettings is the write path for callers that target ONE filter which is not necessarily the one on
    // the options page (the optimization wizard's target filter). The store's getters return clones, so a write for a
    // non-edited filter has to go back through UpsertSnapshot or it silently persists nothing.

    [Test]
    public void MutateFilterSettings_NonEditedFilter_UpsertsTheStoreAndLeavesTheBufferAlone() {
        var h = Build(enabled: true);   // editing "Ha"
        h.Buffer.DefocusAwareDonutDetection = false;
        h.Store.ClearReceivedCalls();

        h.Binder.MutateFilterSettings("Oiii", o => o.DefocusAwareDonutDetection = true);

        h.Store.Received().UpsertSnapshot("Oiii", Arg.Is<StarDetectionSettingsSnapshot>(s => s.DefocusAwareDonutDetection));
        Assert.Multiple(() => {
            Assert.That(h.Buffer.DefocusAwareDonutDetection, Is.False, "the edited filter's buffer is untouched");
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"), "and the options page stays on its filter");
        });
    }

    [Test]
    public void MutateFilterSettings_EditedFilter_GoesThroughTheBufferSoTheMirrorPersistsIt() {
        var h = Build(enabled: true);   // editing "Ha"
        h.Buffer.DefocusAwareDonutDetection = false;
        h.Store.ClearReceivedCalls();

        h.Binder.MutateFilterSettings("Ha", o => o.DefocusAwareDonutDetection = true);

        Assert.That(h.Buffer.DefocusAwareDonutDetection, Is.True, "the buffer IS this filter's live editing surface");
        h.Store.Received().UpsertSnapshot("Ha", Arg.Is<StarDetectionSettingsSnapshot>(s => s.DefocusAwareDonutDetection));
    }

    [Test]
    public void MutateFilterSettings_FeatureDisabled_WritesTheBufferOnly() {
        var h = Build();

        h.Binder.MutateFilterSettings("Oiii", o => o.DefocusAwareDonutDetection = true);

        Assert.That(h.Buffer.DefocusAwareDonutDetection, Is.True);
        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());
    }

    [Test]
    public void ExternalSnapshotChange_ForEditedFilter_ReloadsTheBuffer() {
        var h = Build(enabled: true);
        var updated = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.77;
        });
        h.Store.GetOrSeedSnapshot("Ha").Returns(updated);

        h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
            h.Store, new PerFilterSnapshotChangedEventArgs("Ha"));

        Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.77));
    }

    [Test]
    public void ExternalSnapshotChange_ForOtherFilter_DoesNotTouchTheBuffer() {
        var h = Build(enabled: true);
        h.Store.ClearReceivedCalls();

        h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
            h.Store, new PerFilterSnapshotChangedEventArgs("Sii"));

        h.Store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
    }

    [Test]
    public void SelfOriginatedUpsert_DoesNotReloadTheBuffer() {
        var h = Build(enabled: true);
        // Mimic the real store: UpsertSnapshot raises SnapshotChanged synchronously for the upserted filter.
        h.Store.When(s => s.UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>()))
            .Do(ci => h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
                h.Store, new PerFilterSnapshotChangedEventArgs(ci.Arg<string>())));
        h.Store.ClearReceivedCalls();

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.42;

        Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.42));
        h.Store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
    }

    [Test]
    public void ProfileChanged_EditedFilterStillPresent_KeepsItAndReloads() {
        var h = Build(enabled: true);
        var newProfile = MakeProfile(MakeFilters("Ha", "Sii"));
        h.ProfileService.ActiveProfile.Returns(newProfile);
        h.Store.ClearReceivedCalls();

        h.ProfileService.ProfileChanged += Raise.Event<EventHandler>(h.ProfileService, EventArgs.Empty);

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
            Assert.That(h.Binder.AvailableFilterNames, Is.EqualTo(new[] { "Ha", "Sii" }));
        });
        h.Store.Received().GetOrSeedSnapshot("Ha");
    }

    [Test]
    public void ProfileChanged_EditedFilterMissing_ResolvesToDefault() {
        var h = Build(enabled: true);
        h.CurrentFilterName = null;
        var newProfile = MakeProfile(MakeFilters("Sii", "Oiii"));
        h.ProfileService.ActiveProfile.Returns(newProfile);
        h.Store.ClearReceivedCalls();

        h.ProfileService.ProfileChanged += Raise.Event<EventHandler>(h.ProfileService, EventArgs.Empty);

        Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Sii"));
        h.Store.Received().GetOrSeedSnapshot("Sii");
    }

    [Test]
    public void ProfileChanged_OptionsReReadBurst_DoesNotMirrorStaleDataIntoTheStore() {
        var h = Build();
        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.45;
        SetEnabled(h, true);
        h.Buffer.MaxDistortion = 0.91;

        var newProfile = MakeProfile(MakeFilters("L", "Ha", "Oiii"));
        h.ProfileService.ActiveProfile.Returns(newProfile);
        h.Store.ClearReceivedCalls();

        // StarDetectionOptions subscribed to ProfileChanged before the binder, so its re-read notification
        // burst fires while the binder still observes the old profile — it must not upsert during that window.
        h.ProfileService.ProfileChanged += Raise.Event<EventHandler>(h.ProfileService, EventArgs.Empty);
        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());

        // After the switch completes, edits mirror again.
        h.Buffer.MinHFR = 1.31;
        h.Store.Received().UpsertSnapshot("Ha", Arg.Is<StarDetectionSettingsSnapshot>(s => s.MinHFR == 1.31));
    }

    [Test]
    public void AvailableFilterNames_TracksProfileCollectionChanges() {
        var h = Build();
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.Filters.Add(new FilterInfo() { Name = "Sii" });

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.AvailableFilterNames)));
            Assert.That(h.Binder.AvailableFilterNames, Is.EqualTo(new[] { "L", "Ha", "Oiii", "Sii" }));
        });
    }
}

// End-to-end binder <-> real store wiring: seeding on enable, scrubbing on upsert, synchronous
// SnapshotChanged reload, and data retention across disable — the production event chain, no substitutes.
[TestFixture]
public class PerFilterEditBinderStoreIntegrationTests {

    private sealed class Harness {
        public IProfileService ProfileService;
        public InMemoryPluginOptionsAccessor LegacyAccessor;
        public InMemoryPluginOptionsAccessor StoreAccessor;
        public StarDetectionOptions Buffer;
        public PerFilterStarDetectionStore Store;
        public PerFilterEditBinder Binder;
        public string CurrentFilterName = "Ha";
    }

    private static Harness Build() {
        var filters = new ObserveAllCollection<FilterInfo>();
        filters.Add(new FilterInfo() { Name = "L" });
        filters.Add(new FilterInfo() { Name = "Ha" });
        var profile = Substitute.For<IProfile>();
        profile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
        var profileService = Substitute.For<IProfileService>();
        profileService.ActiveProfile.Returns(profile);

        var h = new Harness() {
            ProfileService = profileService,
            LegacyAccessor = new InMemoryPluginOptionsAccessor(),
            StoreAccessor = new InMemoryPluginOptionsAccessor(),
        };
        // Production construction order: options -> store -> binder (ProfileChanged handlers run in this order).
        h.Buffer = new StarDetectionOptions(profileService, h.LegacyAccessor);
        h.Store = new PerFilterStarDetectionStore(
            profileService, h.StoreAccessor, () => StarDetectionSettingsSnapshot.FromOptions(h.Buffer));
        h.Binder = new PerFilterEditBinder(h.Store, h.Buffer, profileService, () => h.CurrentFilterName);
        return h;
    }

    [Test]
    public void BufferEdit_RoundTripsThroughTheRealStore() {
        var h = Build();
        h.Store.Enabled = true;

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.37;

        var stored = h.Store.TryGetSnapshot("Ha");
        Assert.Multiple(() => {
            Assert.That(stored.UseAdvanced, Is.True);
            Assert.That(stored.MaxDistortion, Is.EqualTo(0.37));
        });
    }

    [Test]
    public void MachineLocalBufferEdits_DoNotClobberScrubbedStoreFields() {
        var h = Build();
        h.Store.Enabled = true;

        h.Buffer.DebugMode = true;
        h.Buffer.PSFParallelPartitionSize = 999;
        h.Buffer.SaveIntermediateImages = true;

        var stored = h.Store.TryGetSnapshot("Ha");
        Assert.Multiple(() => {
            Assert.That(stored.DebugMode, Is.False);
            Assert.That(stored.PSFParallelPartitionSize, Is.EqualTo(100));
            Assert.That(stored.SaveIntermediateImages, Is.False);
            Assert.That(stored.IntermediateSavePath, Is.EqualTo(""));
            // The buffer itself keeps the machine-local values (they stay global by scope decision).
            Assert.That(h.Buffer.DebugMode, Is.True);
            Assert.That(h.Buffer.PSFParallelPartitionSize, Is.EqualTo(999));
        });
    }

    [Test]
    public void ExternalUpsert_ReloadsTheBufferWithTheNewSnapshot() {
        var h = Build();
        h.Store.Enabled = true;

        var external = h.Store.TryGetSnapshot("Ha");
        external.UseAdvanced = true;
        external.MaxDistortion = 0.66;
        h.Store.UpsertSnapshot("Ha", external);

        Assert.Multiple(() => {
            Assert.That(h.Buffer.UseAdvanced, Is.True);
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.66));
        });
    }

    [Test]
    public void DisableAfterEdits_RestoresPreEnableGlobalsAndRetainsStoreEntries() {
        var h = Build();
        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.45;
        h.Store.Enabled = true;
        h.Buffer.MaxDistortion = 0.91;

        h.Store.Enabled = false;

        Assert.Multiple(() => {
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.45));
            Assert.That(h.Store.TryGetSnapshot("Ha").MaxDistortion, Is.EqualTo(0.91));
        });
    }
}
