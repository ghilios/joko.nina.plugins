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
using System.Collections.Generic;
using System.ComponentModel;

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
        public bool FilterWheelConnected = true;
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

    /// <summary>
    /// Stands in for a real WPF dispatcher seen from a NON-UI thread: <c>PostSynchronizationContext</c> queues the
    /// action instead of running it, so a test can assert what the calling (background) thread did and did not do
    /// before pumping. Deterministic — no second thread is involved.
    /// </summary>
    private sealed class DeferringApplicationDispatcher : IApplicationDispatcher {
        private readonly Queue<Action> posted = new Queue<Action>();

        public int PendingCount => posted.Count;

        public void DispatchSynchronizationContext(Action action) => throw new InvalidOperationException(
            "Blocking Invoke from a background thread onto the UI thread deadlocks; this path must Post.");

        public T DispatchSynchronizationContext<T>(Func<T> func) => throw new InvalidOperationException(
            "Blocking Invoke from a background thread onto the UI thread deadlocks; this path must Post.");

        public void PostSynchronizationContext(Action action) => posted.Enqueue(action);

        public T GetResource<T>(string name, T fallback) => fallback;

        public void Pump() {
            while (posted.Count > 0) {
                posted.Dequeue()();
            }
        }
    }

    private static Harness Build(bool enabled = false, string currentFilter = "Ha", string[] filterNames = null, IApplicationDispatcher dispatcher = null, bool connected = true) {
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
            FilterWheelConnected = connected,
        };
        harness.Binder = new PerFilterEditBinder(
            store, buffer, profileService, () => harness.CurrentFilterName, dispatcher, () => harness.FilterWheelConnected);
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

    // A detection running on an imaging worker calls GetOrSeedSnapshot; seeding an unseen filter name raises
    // SnapshotChanged on THAT thread. Reloading the buffer inline would raise PropertyChanged for WPF-bound options
    // off the UI thread, so the handler must marshal — and must Post, never Invoke (a blocking Invoke from an
    // imaging worker onto a busy UI thread deadlocks; see .claude/docs/mvvm-patterns.md).
    [Test]
    public void ExternalSnapshotChange_MarshalsTheReloadThroughTheDispatcherWithoutBlocking() {
        var dispatcher = new RecordingApplicationDispatcher();
        var h = Build(enabled: true, dispatcher: dispatcher);
        var updated = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.77;
        });
        h.Store.GetOrSeedSnapshot("Ha").Returns(updated);
        var postsBefore = dispatcher.PostCount;

        h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
            h.Store, new PerFilterSnapshotChangedEventArgs("Ha"));

        Assert.Multiple(() => {
            Assert.That(dispatcher.PostCount - postsBefore, Is.EqualTo(1), "the reload must go through the non-blocking Post");
            Assert.That(dispatcher.DispatchCount, Is.Zero, "a blocking Invoke from a detection thread would deadlock");
            // RecordingApplicationDispatcher runs inline, so the reload still lands.
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.77));
        });
    }

    // Same event, but with a dispatcher that behaves like a real one seen from a background thread: the buffer must
    // not be touched on the raising thread at all, only once the UI thread pumps the queued work.
    [Test]
    public void ExternalSnapshotChange_FromABackgroundThread_DefersTheBufferWriteUntilTheUiThreadPumps() {
        var dispatcher = new DeferringApplicationDispatcher();
        var h = Build(enabled: true, dispatcher: dispatcher);
        var updated = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.77;
        });
        h.Store.GetOrSeedSnapshot("Ha").Returns(updated);
        h.Store.ClearReceivedCalls();
        var beforeRaise = h.Buffer.MaxDistortion;

        h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
            h.Store, new PerFilterSnapshotChangedEventArgs("Ha"));

        Assert.Multiple(() => {
            Assert.That(dispatcher.PendingCount, Is.EqualTo(1));
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(beforeRaise), "the buffer must not be written on the raising thread");
        });
        h.Store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());

        dispatcher.Pump();

        Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.77));
    }

    // The dispatcher runs inline on the UI thread, so the UI-driven paths keep running nested inside the operation
    // that raised the event — which is what makes the isMirroring/isLoading re-entrancy guards meaningful.
    [Test]
    public void SelfOriginatedUpsert_WithADispatcher_StillDoesNotReloadTheBuffer() {
        var dispatcher = new RecordingApplicationDispatcher();
        var h = Build(enabled: true, dispatcher: dispatcher);
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

    // ActiveFilterWarning is the passive options-page counterpart to the up-front gates in HocusFocusVM /
    // InspectorVM / RunAberrationInspector: it tells the user, while they are editing, that the filter they are
    // editing is not the filter light is actually coming through — so their edits will not affect what they are
    // imaging. Only meaningful while the feature is on.

    [Test]
    public void ActiveFilterWarning_FeatureDisabled_IsSilent() {
        var h = Build(enabled: false, connected: false);

        Assert.Multiple(() => {
            Assert.That(h.Binder.ActiveFilterWarning, Is.Null);
            Assert.That(h.Binder.HasActiveFilterWarning, Is.False);
        });
    }

    [Test]
    public void ActiveFilterWarning_NoFilterWheelConnected_ExplainsThereIsNoFilterToMatchOn() {
        var h = Build(enabled: true, connected: false);

        Assert.Multiple(() => {
            Assert.That(h.Binder.HasActiveFilterWarning, Is.True);
            Assert.That(h.Binder.ActiveFilterWarning, Does.Contain("No filter wheel is connected"));
        });
    }

    // The current delegate cannot distinguish "no wheel" from "wheel connected, filter not yet reported" — both
    // yield a null name — so connectivity is a separate input and the disconnected message wins on it alone.
    [Test]
    public void ActiveFilterWarning_ConnectedButNoFilterReportedYet_IsSilent() {
        var h = Build(enabled: true, currentFilter: null, connected: true);

        Assert.That(h.Binder.ActiveFilterWarning, Is.Null);
    }

    [Test]
    public void ActiveFilterWarning_ConnectedAndEditingADifferentFilter_NamesBothFilters() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);   // resolves to editing "Ha"

        h.Binder.EditedFilterName = "Oiii";

        var warning = h.Binder.ActiveFilterWarning;
        Assert.Multiple(() => {
            Assert.That(h.Binder.HasActiveFilterWarning, Is.True);
            Assert.That(warning, Does.Contain("Ha"), "the filter actually in the light path");
            Assert.That(warning, Does.Contain("Oiii"), "the filter being edited");
        });
    }

    [Test]
    public void ActiveFilterWarning_ConnectedAndEditingTheActiveFilter_IsSilent() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
            Assert.That(h.Binder.ActiveFilterWarning, Is.Null);
            Assert.That(h.Binder.HasActiveFilterWarning, Is.False);
        });
    }

    [Test]
    public void ActiveFilterWarning_SwitchingTheEditedFilter_RaisesPropertyChanged() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.Binder.EditedFilterName = "Oiii";

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.ActiveFilterWarning)));
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.HasActiveFilterWarning)));
        });
    }

    // A computed property alone never updates the UI: WPF only re-reads on PropertyChanged. The filter-wheel
    // consumer registered in HocusFocusPlugin drives RefreshActiveFilter on every device-info update, which is what
    // makes the edited filter follow the wheel (and the warning appear/disappear) as it connects, disconnects, or
    // changes filter.
    [Test]
    public void RefreshActiveFilter_WheelMovesToAKnownFilter_FollowsItLoadsItsSnapshotAndGoesSilent() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);   // editing "Ha"
        var oiii = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.31;
        });
        h.Store.GetOrSeedSnapshot("Oiii").Returns(oiii);
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.CurrentFilterName = "Oiii";   // the wheel moved to a filter this profile defines
        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Oiii"), "the edited filter follows the wheel");
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.31), "and its snapshot is loaded into the buffer");
            Assert.That(h.Binder.ActiveFilterWarning, Is.Null, "editing the active filter is not a mismatch");
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.EditedFilterName)));
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.ActiveFilterWarning)));
        });
        h.Store.Received().GetOrSeedSnapshot("Oiii");
    }

    [Test]
    public void RefreshActiveFilter_WheelMovesToAFilterTheProfileDoesNotDefine_DoesNotFollowAndWarns() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);   // editing "Ha"

        h.CurrentFilterName = "Sii";   // not one of L/Ha/Oiii
        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"), "an unknown wheel filter is not auto-selected");
            Assert.That(h.Binder.ActiveFilterWarning, Does.Contain("Sii").And.Contains("Ha"));
        });
    }

    [Test]
    public void RefreshActiveFilter_WheelDisconnects_KeepsTheEditedFilterAndWarns() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);   // editing "Ha"

        h.FilterWheelConnected = false;
        h.CurrentFilterName = null;
        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"), "disconnect leaves the last edited filter in place");
            Assert.That(h.Binder.ActiveFilterWarning, Does.Contain("No filter wheel is connected"));
        });
    }

    [Test]
    public void RefreshActiveFilter_FeatureDisabled_DoesNotTouchTheEditedFilter() {
        var h = Build(enabled: false, currentFilter: "Ha", connected: true);
        h.Store.ClearReceivedCalls();

        h.CurrentFilterName = "Oiii";
        h.Binder.RefreshActiveFilter();

        Assert.That(h.Binder.EditedFilterName, Is.Null, "nothing is edited while the feature is off");
        h.Store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
    }

    // A manual pick different from the wheel stands until the wheel next moves; the mismatch warning covers the gap.
    [Test]
    public void RefreshActiveFilter_ManualPickThenWheelMoves_ResyncsToTheWheel() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);   // editing "Ha"
        h.Binder.EditedFilterName = "L";   // user deliberately edits a different filter
        Assert.That(h.Binder.ActiveFilterWarning, Does.Contain("Ha").And.Contains("L"));

        h.CurrentFilterName = "Oiii";   // wheel moves
        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Oiii"), "the physical filter wins over the manual pick");
            Assert.That(h.Binder.ActiveFilterWarning, Is.Null);
        });
    }

    [Test]
    public void CopySourceFilterName_GatesCanCopyAndRaisesBothProperties() {
        var h = Build(enabled: true);
        Assert.That(h.Binder.CanCopyFromFilter, Is.False, "no source chosen -> Copy disabled");
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.Binder.CopySourceFilterName = "Oiii";

        Assert.Multiple(() => {
            Assert.That(h.Binder.CopySourceFilterName, Is.EqualTo("Oiii"));
            Assert.That(h.Binder.CanCopyFromFilter, Is.True, "a source is chosen -> Copy enabled");
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.CopySourceFilterName)));
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.CanCopyFromFilter)));
        });
    }

    [Test]
    public void CopySourceFilterName_ClearedToEmpty_DisablesCopyAgain() {
        var h = Build(enabled: true);
        h.Binder.CopySourceFilterName = "Oiii";

        h.Binder.CopySourceFilterName = null;   // what the copy flow does when it completes

        Assert.That(h.Binder.CanCopyFromFilter, Is.False);
    }

    [Test]
    public void CopySourceFilterName_ClearedWhenTheFeatureIsDisabled() {
        var h = Build(enabled: true);
        h.Binder.CopySourceFilterName = "Oiii";

        SetEnabled(h, false);

        Assert.That(h.Binder.CopySourceFilterName, Is.Null);
    }

    [Test]
    public void RefreshActiveFilter_AfterTheWheelDisconnects_RaisesPropertyChangedAndWarns() {
        var h = Build(enabled: true, currentFilter: "Ha", connected: true);
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.FilterWheelConnected = false;
        h.CurrentFilterName = null;
        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.ActiveFilterWarning)));
            Assert.That(h.Binder.ActiveFilterWarning, Does.Contain("No filter wheel is connected"));
        });
    }

    [Test]
    public void ToggleTheFeature_RaisesActiveFilterWarningPropertyChanged() {
        var h = Build(enabled: false, connected: false);
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        SetEnabled(h, true);

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.ActiveFilterWarning)));
            Assert.That(h.Binder.HasActiveFilterWarning, Is.True);
        });
    }

    // Filter-wheel consumer callbacks arrive on whatever thread the mediator broadcasts from, and raising
    // PropertyChanged for WPF-bound text off the UI thread is exactly what the dispatcher exists to prevent.
    // Post, never a blocking Invoke (see .claude/docs/mvvm-patterns.md).
    [Test]
    public void RefreshActiveFilter_MarshalsThroughTheNonBlockingPost() {
        var dispatcher = new RecordingApplicationDispatcher();
        var h = Build(enabled: true, dispatcher: dispatcher);
        var postsBefore = dispatcher.PostCount;

        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(dispatcher.PostCount - postsBefore, Is.EqualTo(1));
            Assert.That(dispatcher.DispatchCount, Is.Zero, "a blocking Invoke from a mediator broadcast would deadlock");
        });
    }

    [Test]
    public void RefreshActiveFilter_FromABackgroundThread_DefersTheNotificationUntilTheUiThreadPumps() {
        var dispatcher = new DeferringApplicationDispatcher();
        var h = Build(enabled: true, currentFilter: "Ha", dispatcher: dispatcher);
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.CurrentFilterName = "Oiii";
        h.Binder.RefreshActiveFilter();

        Assert.Multiple(() => {
            Assert.That(dispatcher.PendingCount, Is.EqualTo(1));
            Assert.That(raised, Is.Empty, "PropertyChanged for bound text must not be raised on the mediator's thread");
        });

        dispatcher.Pump();

        Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.ActiveFilterWarning)));
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

    // --- Auto-focus sweep geometry ------------------------------------------------------------------------

    private static Harness BuildWithProfileGeometry(int stepSize = 100, int offsetSteps = 4) {
        var h = Build();
        h.ProfileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(stepSize);
        h.ProfileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps.Returns(offsetSteps);
        return h;
    }

    [Test]
    public void SweepGeometry_NoOverride_EffectiveValuesAreTheProfileValues() {
        var h = BuildWithProfileGeometry(stepSize: 100, offsetSteps: 4);
        h.Store.Enabled = true;

        Assert.Multiple(() => {
            Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(PerFilterSweepGeometry.Inherit));
            Assert.That(h.Binder.SweepOffsetStepsOverride, Is.EqualTo(PerFilterSweepGeometry.Inherit));
            Assert.That(h.Binder.ProfileSweepStepSize, Is.EqualTo(100));
            Assert.That(h.Binder.ProfileSweepOffsetSteps, Is.EqualTo(4));
            Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(100));
            Assert.That(h.Binder.EffectiveSweepOffsetSteps, Is.EqualTo(4));
            Assert.That(h.Binder.HasSweepGeometryOverride, Is.False);
        });
    }

    [Test]
    public void SweepGeometry_SettingAnOverride_PersistsToTheStoreForTheEditedFilter() {
        var h = BuildWithProfileGeometry();
        h.Store.Enabled = true;

        h.Binder.SweepStepSizeOverride = 30;
        h.Binder.SweepOffsetStepsOverride = 6;

        var stored = h.Store.GetSweepGeometry("Ha");
        Assert.Multiple(() => {
            Assert.That(stored.StepSize, Is.EqualTo(30));
            Assert.That(stored.InitialOffsetSteps, Is.EqualTo(6));
            Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(30));
            Assert.That(h.Binder.HasSweepGeometryOverride, Is.True);
            Assert.That(h.Store.GetSweepGeometry("L").IsUnset, Is.True, "the other filter is untouched");
        });
    }

    // The two fields are stored together but resolve separately, so a user can pin the step size for a
    // narrowband filter and still inherit however many points the profile sweeps.
    [Test]
    public void SweepGeometry_StepSizeOverriddenOnly_OffsetStillInheritsTheProfile() {
        var h = BuildWithProfileGeometry(stepSize: 100, offsetSteps: 4);
        h.Store.Enabled = true;

        h.Binder.SweepStepSizeOverride = 25;

        Assert.Multiple(() => {
            Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(25));
            Assert.That(h.Binder.EffectiveSweepOffsetSteps, Is.EqualTo(4));
        });
    }

    // The bound box has no validation rule (a rule would reject the deliberately blank "inherit" state), so the
    // setter is the only thing standing between a typo and the engine.
    [TestCase(0)]
    [TestCase(-5)]
    public void SweepGeometry_NonPositiveStepSize_CoercedToInherit(int typed) {
        var h = BuildWithProfileGeometry(stepSize: 100);
        h.Store.Enabled = true;
        h.Binder.SweepStepSizeOverride = 30;

        h.Binder.SweepStepSizeOverride = typed;

        Assert.Multiple(() => {
            Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(PerFilterSweepGeometry.Inherit));
            Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(100));
        });
    }

    [Test]
    public void SweepGeometry_ClearingTheOverride_FallsBackToTheProfile() {
        var h = BuildWithProfileGeometry(stepSize: 100);
        h.Store.Enabled = true;
        h.Binder.SweepStepSizeOverride = 30;

        // What the converter produces when the user empties the box.
        h.Binder.SweepStepSizeOverride = PerFilterSweepGeometry.Inherit;

        Assert.Multiple(() => {
            Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(100));
            Assert.That(h.Store.GetSweepGeometry("Ha").HasStepSize, Is.False);
        });
    }

    [Test]
    public void SweepGeometry_SwitchingTheEditedFilter_LoadsThatFiltersOverride() {
        var h = BuildWithProfileGeometry(stepSize: 100);
        h.Store.Enabled = true;
        h.Binder.SweepStepSizeOverride = 30;

        h.Binder.EditedFilterName = "L";

        Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(PerFilterSweepGeometry.Inherit), "L has no override");

        h.Binder.SweepStepSizeOverride = 55;
        h.Binder.EditedFilterName = "Ha";

        Assert.Multiple(() => {
            Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(30), "Ha's override comes back");
            Assert.That(h.Store.GetSweepGeometry("L").StepSize, Is.EqualTo(55));
        });
    }

    // The hint under a blank box shows the profile value, so it has to track an edit made in Options -> Focuser
    // while this page is open -- otherwise the page claims a number the next run will not use.
    [Test]
    public void SweepGeometry_ProfileValueChanges_RaisesTheHintAndEffectiveProperties() {
        var h = BuildWithProfileGeometry(stepSize: 100);
        h.Store.Enabled = true;
        var raised = new List<string>();
        h.Binder.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        h.ProfileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(140);
        h.ProfileService.ActiveProfile.FocuserSettings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
            h.ProfileService.ActiveProfile.FocuserSettings, new PropertyChangedEventArgs(nameof(IFocuserSettings.AutoFocusStepSize)));

        Assert.Multiple(() => {
            Assert.That(raised, Contains.Item(nameof(PerFilterEditBinder.ProfileSweepStepSize)));
            Assert.That(raised, Contains.Item(nameof(PerFilterEditBinder.EffectiveSweepStepSize)));
            Assert.That(h.Binder.EffectiveSweepStepSize, Is.EqualTo(140));
        });
    }

    [Test]
    public void MutateFilterSweepGeometry_NonEditedFilter_PersistsWithoutTouchingTheEditedOne() {
        var h = BuildWithProfileGeometry();
        h.Store.Enabled = true;
        h.Binder.SweepStepSizeOverride = 30;

        h.Binder.MutateFilterSweepGeometry("L", g => { g.StepSize = 77; g.InitialOffsetSteps = 9; });

        Assert.Multiple(() => {
            Assert.That(h.Store.GetSweepGeometry("L").StepSize, Is.EqualTo(77));
            Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(30), "the edited filter's bound value is unchanged");
        });
    }

    [Test]
    public void MutateFilterSweepGeometry_EditedFilter_AlsoRefreshesTheBoundProperties() {
        var h = BuildWithProfileGeometry();
        h.Store.Enabled = true;

        h.Binder.MutateFilterSweepGeometry("Ha", g => { g.StepSize = 44; g.InitialOffsetSteps = 3; });

        Assert.Multiple(() => {
            Assert.That(h.Binder.SweepStepSizeOverride, Is.EqualTo(44));
            Assert.That(h.Binder.SweepOffsetStepsOverride, Is.EqualTo(3));
        });
    }

    // The store hands back clones, so a caller that mutated what it was given would persist nothing -- the whole
    // reason MutateFilterSweepGeometry exists rather than "get, then change it".
    [Test]
    public void MutateFilterSweepGeometry_DoesNotAliasTheStoresInstance() {
        var h = BuildWithProfileGeometry();
        h.Store.Enabled = true;
        var handedOut = h.Store.GetSweepGeometry("Ha");
        handedOut.StepSize = 999;

        Assert.That(h.Store.GetSweepGeometry("Ha").HasStepSize, Is.False);
    }

    [Test]
    public void SweepGeometry_FeatureDisabled_WritesNothingAndReportsNoOverride() {
        var h = BuildWithProfileGeometry(stepSize: 100);
        h.Store.Enabled = true;
        h.Binder.SweepStepSizeOverride = 30;

        h.Store.Enabled = false;

        Assert.Multiple(() => {
            Assert.That(h.Binder.HasSweepGeometryOverride, Is.False, "nothing is exposed while the feature is off");
            Assert.That(h.Store.GetSweepGeometry("Ha").StepSize, Is.EqualTo(30), "but the override is retained for the next enable");
        });
    }
}
