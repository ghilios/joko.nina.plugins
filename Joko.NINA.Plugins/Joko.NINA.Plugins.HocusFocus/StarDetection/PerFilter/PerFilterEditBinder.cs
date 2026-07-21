#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    /// <summary>
    /// Turns the <see cref="StarDetectionOptions"/> singleton into the edit buffer for one filter's per-filter
    /// snapshot while the feature is enabled: selecting <see cref="EditedFilterName"/> loads that filter's snapshot
    /// into the buffer (imported-snapshot semantics, so machine-local fields stay put), and buffer edits mirror back
    /// into the store. Legacy profile writes are suppressed for the duration (<c>PersistToProfile = false</c>) so
    /// disabling the feature returns exactly to the pre-enable global settings.
    ///
    /// All buffer mutation happens on the UI thread, like the options singleton it wraps. Detection threads read the
    /// store directly and never call into this binder — but they can still reach it *indirectly*: the store raises
    /// <see cref="IPerFilterStarDetectionStore.SnapshotChanged"/> from whatever thread mutated it, and
    /// <c>GetOrSeedSnapshot</c> mutates (seeds) when it meets an unknown filter name. So
    /// <see cref="Store_SnapshotChanged"/> — the one entry point a background thread can drive — marshals its whole
    /// body through <see cref="IApplicationDispatcher.PostSynchronizationContext"/>. Post, not Dispatch: a blocking
    /// Invoke from an imaging worker onto a busy UI thread deadlocks. Post runs inline when the caller is already on
    /// the UI thread (and when the dispatcher is null, as in unit tests), so the synchronous UI-driven paths —
    /// filter selection, copy-from-filter, <see cref="MutateFilterSettings"/> — keep their existing re-entrancy
    /// guards (<c>isLoading</c>/<c>isMirroring</c> are only meaningful when the handler runs nested inside the
    /// operation that raised the event).
    /// </summary>
    public class PerFilterEditBinder : BaseINPC {
        private readonly IPerFilterStarDetectionStore store;
        private readonly StarDetectionOptions buffer;
        private readonly IProfileService profileService;
        private readonly Func<string> getCurrentFilterName;
        // Connectivity is a SEPARATE input from the filter name because getCurrentFilterName cannot distinguish
        // "no wheel connected" from "wheel connected but no filter reported yet" — both yield null, and the two
        // states need different warnings (see ActiveFilterWarning). Null in hosts that do not supply it, which then
        // fall back to inferring connectivity from the presence of a filter name.
        private readonly Func<bool> getFilterWheelConnected;
        // Null in headless/test hosts; the marshaling helper below then runs inline, matching ApplicationDispatcher's
        // own null-dispatcher behavior.
        private readonly IApplicationDispatcher applicationDispatcher;

        // Profile whose filter collection is currently observed. Mirroring is gated on ActiveProfile still being
        // this instance: during a profile switch, StarDetectionOptions' ProfileChanged handler (subscribed before
        // this binder) re-reads the new profile's legacy keys and fires PropertyChanged BEFORE this binder's
        // handler runs — without the gate that burst would upsert stale data into the new profile's store blob.
        private IProfile observedProfile;
        private ObserveAllCollection<FilterInfo> observedFilters;
        private bool isLoading;
        private bool isMirroring;

        public PerFilterEditBinder(
            IPerFilterStarDetectionStore store,
            StarDetectionOptions buffer,
            IProfileService profileService,
            Func<string> getCurrentFilterName,
            IApplicationDispatcher applicationDispatcher = null,
            Func<bool> getFilterWheelConnected = null) {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.getCurrentFilterName = getCurrentFilterName ?? throw new ArgumentNullException(nameof(getCurrentFilterName));
            this.applicationDispatcher = applicationDispatcher;
            // Optional so existing fixtures (and any host without a wheel mediator to hand) keep constructing the
            // binder with plain delegates and no equipment. The fallback treats a reported filter name as proof of
            // a connected wheel, which is the best inference available without a connectivity signal.
            this.getFilterWheelConnected = getFilterWheelConnected
                ?? (() => !string.IsNullOrEmpty(getCurrentFilterName()));

            ObserveProfileFilters();
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            store.EnabledChanged += Store_EnabledChanged;
            store.SnapshotChanged += Store_SnapshotChanged;
            buffer.PropertyChanged += Buffer_PropertyChanged;
            if (store.Enabled) {
                // Feature already on at startup (persisted per-profile flag): enter buffered-edit mode immediately.
                OnFeatureEnabled();
            }
        }

        private string editedFilterName;

        public string EditedFilterName {
            get => editedFilterName;
            set {
                if (editedFilterName != value) {
                    editedFilterName = value;
                    RaisePropertyChanged();
                    RaiseActiveFilterWarningChanged();
                    if (!string.IsNullOrEmpty(editedFilterName)) {
                        LoadSnapshotIntoBuffer(editedFilterName);
                    }
                }
            }
        }

        /// <summary>
        /// Warning text for the options page when the filter being edited is not the filter light is actually
        /// coming through, so the user can see that their edits will not affect what they are currently imaging or
        /// focusing. Null (and <see cref="HasActiveFilterWarning"/> false) whenever there is nothing to say — the
        /// feature is off, or the wheel is connected and reporting the filter being edited.
        ///
        /// Two states warrant a warning. Without a connected wheel there is no capture-time filter name to key on
        /// at all, which is why the run entry points (HocusFocusVM / InspectorVM / RunAberrationInspector) refuse
        /// up front; this is the passive, always-visible counterpart to those gates. With a wheel connected but
        /// parked on a different filter, the edits are simply landing on the wrong set. A connected wheel that has
        /// not reported a filter yet (mid-move) is deliberately silent: it is transient and there is no second name
        /// to name.
        ///
        /// This is a computed property, so it only reaches the UI when something raises PropertyChanged for it —
        /// see <see cref="RefreshActiveFilter"/>, which the host's filter-wheel consumer drives.
        /// </summary>
        public string ActiveFilterWarning {
            get {
                if (!store.Enabled) {
                    return null;
                }
                if (!getFilterWheelConnected()) {
                    return "No filter wheel is connected. Per-filter star detection matches settings to the filter "
                        + "name recorded in each image, so there is no way to tell which filter's settings apply. "
                        + "Autofocus and the aberration inspector will refuse to run until a filter wheel is connected.";
                }
                var currentFilterName = getCurrentFilterName();
                if (string.IsNullOrEmpty(currentFilterName) || string.IsNullOrEmpty(editedFilterName)) {
                    return null;
                }
                if (string.Equals(currentFilterName, editedFilterName, StringComparison.Ordinal)) {
                    return null;
                }
                return $"The filter in the light path is '{currentFilterName}', but you are editing '{editedFilterName}'. "
                    + $"These changes will not affect star detection while imaging or focusing through '{currentFilterName}'.";
            }
        }

        public bool HasActiveFilterWarning => !string.IsNullOrEmpty(ActiveFilterWarning);

        private string copySourceFilterName;

        /// <summary>
        /// The filter chosen in the "Copy Settings From" dropdown, held here (rather than read straight off the
        /// ComboBox) so it resolves identically on both hosts, gates the Copy button's enablement via CanExecute,
        /// and can be cleared back to no-selection after a copy completes. Pure transient UI state — never persisted.
        /// </summary>
        public string CopySourceFilterName {
            get => copySourceFilterName;
            set {
                if (copySourceFilterName != value) {
                    copySourceFilterName = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(CanCopyFromFilter));
                }
            }
        }

        /// <summary>
        /// Drives the Copy button's enablement through an IsEnabled binding (not a command <c>canExecute</c>
        /// predicate: CommunityToolkit commands only requery on an explicit <c>NotifyCanExecuteChanged</c>, which a
        /// button parameter change never triggers). False until a source filter is chosen, so nothing is copied from
        /// nowhere, and back to false once a copy completes and the selection is cleared.
        /// </summary>
        public bool CanCopyFromFilter => !string.IsNullOrEmpty(copySourceFilterName);

        /// <summary>
        /// Re-syncs the edited filter to the wheel and re-evaluates <see cref="ActiveFilterWarning"/>. Called by the
        /// host whenever the filter wheel connects, disconnects, or changes filter — the binder has no way to observe
        /// that itself, by design: it takes plain delegates rather than NINA mediator types so it stays constructible
        /// with no equipment.
        ///
        /// The host drives this from an <c>IFilterWheelConsumer</c> broadcast, which arrives on whatever thread the
        /// mediator publishes from, so the work is marshaled through the same non-blocking Post as
        /// <see cref="Store_SnapshotChanged"/> — raising PropertyChanged for WPF-bound text (and mutating the buffer,
        /// which raises it too) off the UI thread is exactly what that dispatcher exists to prevent, and a blocking
        /// Invoke would risk the deadlock described in the class doc.
        /// </summary>
        public void RefreshActiveFilter() {
            PostToUiThread(() => {
                SelectActiveFilterIfConnected();
                RaiseActiveFilterWarningChanged();
            });
        }

        /// <summary>
        /// While the feature is on and the wheel is connected and parked on a filter this profile defines, point the
        /// editing selection at it, so the settings on the options page track the filter light is coming through — a
        /// focus run or exposure lands on the filter you were just editing. A silent no-op otherwise: feature off, no
        /// wheel, mid-move with no filter reported yet, or a wheel filter this profile does not define (all of which
        /// leave any manual selection in place). Because the physical filter is authoritative, a subsequent wheel
        /// change overrides a manual selection made in between — the mismatch warning covers that interim window.
        /// Sets the field directly rather than through <see cref="EditedFilterName"/> so the warning is raised once,
        /// by the caller, after this returns.
        /// </summary>
        private void SelectActiveFilterIfConnected() {
            if (!store.Enabled || !getFilterWheelConnected()) {
                return;
            }
            var current = getCurrentFilterName();
            if (string.IsNullOrEmpty(current) || string.Equals(current, editedFilterName, StringComparison.Ordinal)) {
                return;
            }
            if (!AvailableFilterNames.Contains(current)) {
                return;
            }
            editedFilterName = current;
            RaisePropertyChanged(nameof(EditedFilterName));
            LoadSnapshotIntoBuffer(current);
        }

        private void RaiseActiveFilterWarningChanged() {
            RaisePropertyChanged(nameof(ActiveFilterWarning));
            RaisePropertyChanged(nameof(HasActiveFilterWarning));
        }

        public IReadOnlyList<string> AvailableFilterNames {
            get {
                var filters = profileService.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                if (filters == null) {
                    return Array.Empty<string>();
                }
                return filters.Select(f => f.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            }
        }

        /// <summary>
        /// Applies <paramref name="mutate"/> to ONE filter's settings through whichever surface actually persists.
        /// The store's getters hand back <see cref="StarDetectionSettingsSnapshot.Clone"/>s, so mutating what
        /// <c>GetOrSeedSnapshot</c> returns writes to a detached copy and silently persists nothing — every write for
        /// a filter that is not currently being edited must go back through <c>UpsertSnapshot</c>. When the filter IS
        /// <see cref="EditedFilterName"/>, the buffer is that filter's live editing surface and the mirror below
        /// carries the change into the store; writing to the store directly would instead leave the buffer stale and
        /// risk being clobbered by the next mirror. With the feature off there is only the buffer.
        /// </summary>
        public void MutateFilterSettings(string filterName, Action<IStarDetectionOptions> mutate) {
            if (mutate == null) {
                throw new ArgumentNullException(nameof(mutate));
            }
            if (!store.Enabled || string.IsNullOrEmpty(filterName) || string.Equals(filterName, editedFilterName, StringComparison.Ordinal)) {
                mutate(buffer);
                return;
            }
            var snapshot = store.GetOrSeedSnapshot(filterName);
            mutate(snapshot);
            store.UpsertSnapshot(filterName, snapshot);
        }

        private void ObserveProfileFilters() {
            if (observedFilters != null) {
                observedFilters.CollectionChanged -= Filters_CollectionChanged;
            }
            observedProfile = profileService.ActiveProfile;
            observedFilters = observedProfile?.FilterWheelSettings?.FilterWheelFilters;
            if (observedFilters != null) {
                observedFilters.CollectionChanged += Filters_CollectionChanged;
            }
        }

        private void Filters_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            RaisePropertyChanged(nameof(AvailableFilterNames));
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            // Subscription order guarantees StarDetectionOptions and the store re-read the new profile before this
            // runs, so re-resolving here loads the new profile's snapshots. Enabled is per-profile, so the buffer's
            // persistence mode must be re-synced too.
            ObserveProfileFilters();
            RaisePropertyChanged(nameof(AvailableFilterNames));
            if (store.Enabled) {
                buffer.PersistToProfile = false;
                var names = AvailableFilterNames;
                var name = !string.IsNullOrEmpty(editedFilterName) && names.Contains(editedFilterName)
                    ? editedFilterName
                    : ResolveDefaultFilterName(names);
                editedFilterName = name;
                RaisePropertyChanged(nameof(EditedFilterName));
                if (!string.IsNullOrEmpty(name)) {
                    LoadSnapshotIntoBuffer(name);
                }
            } else {
                buffer.PersistToProfile = true;
            }
            // A new profile can bring a different filter set and a different edited filter.
            RaiseActiveFilterWarningChanged();
            // A copy source picked against the old profile's filters is meaningless now.
            CopySourceFilterName = null;
        }

        private string ResolveDefaultFilterName(IReadOnlyList<string> names) {
            var current = getCurrentFilterName();
            if (!string.IsNullOrEmpty(current)) {
                return current;
            }
            return names.Count > 0 ? names[0] : null;
        }

        private void Store_EnabledChanged(object sender, EventArgs e) {
            if (store.Enabled) {
                OnFeatureEnabled();
            } else {
                buffer.PersistToProfile = true;
                buffer.ReloadFromProfile();
            }
            // The warning is gated on Enabled, so toggling the feature always changes whether it shows. Raised
            // inline like the buffer mutations above: Enabled is flipped from the options UI.
            RaiseActiveFilterWarningChanged();
            // Start each enable/disable with the Copy source unselected (button disabled).
            CopySourceFilterName = null;
        }

        private void OnFeatureEnabled() {
            // Suppress legacy writes BEFORE the load mutates the buffer, so the pre-enable keys stay frozen.
            buffer.PersistToProfile = false;
            // Set the field directly: the public setter's equality guard would skip the reload when re-enabling
            // with an unchanged name, but the buffer still needs the per-filter snapshot loaded.
            var name = ResolveDefaultFilterName(AvailableFilterNames);
            editedFilterName = name;
            RaisePropertyChanged(nameof(EditedFilterName));
            if (!string.IsNullOrEmpty(name)) {
                LoadSnapshotIntoBuffer(name);
            }
        }

        private void LoadSnapshotIntoBuffer(string filterName) {
            if (isLoading) {
                return; // GetOrSeedSnapshot raises SnapshotChanged when it seeds; never re-enter the load
            }
            isLoading = true;
            try {
                var snapshot = store.GetOrSeedSnapshot(filterName);
                buffer.ApplyImportedSnapshot(snapshot);
            } finally {
                isLoading = false;
            }
        }

        private void Buffer_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (!store.Enabled || isLoading || isMirroring || string.IsNullOrEmpty(editedFilterName)) {
                return;
            }
            if (!ReferenceEquals(profileService.ActiveProfile, observedProfile)) {
                return; // mid profile-switch: an earlier ProfileChanged handler is re-reading the buffer
            }
            isMirroring = true;
            try {
                store.UpsertSnapshot(editedFilterName, StarDetectionSettingsSnapshot.FromOptions(buffer));
            } finally {
                isMirroring = false;
            }
        }

        // The store raises SnapshotChanged on whichever thread mutated it, and GetOrSeedSnapshot mutates: a detection
        // running on an imaging worker seeds a filter name the store has not seen yet and fires this handler
        // off-thread. The window is narrow but real — right after a profile switch, before this binder's
        // ProfileChanged handler has loaded the new profile's edited filter, a concurrent detection on that same
        // filter name seeds it first. Without marshaling, ApplyImportedSnapshot would raise PropertyChanged for the
        // bound options off the UI thread. The whole body is posted (not just the reload) so the re-entrancy guards
        // and the filter-name comparison are evaluated on the thread that owns them.
        private void Store_SnapshotChanged(object sender, PerFilterSnapshotChangedEventArgs e) {
            var filterName = e.FilterName;
            PostToUiThread(() => {
                if (isMirroring || isLoading || !store.Enabled) {
                    return;
                }
                if (!string.Equals(filterName, editedFilterName, StringComparison.Ordinal)) {
                    return;
                }
                LoadSnapshotIntoBuffer(editedFilterName);
            });
        }

        // Non-blocking by design (see the class doc): a blocking Invoke from an imaging worker onto a busy or
        // tearing-down UI thread deadlocks. Runs inline on the UI thread and when no dispatcher was supplied.
        private void PostToUiThread(Action action) {
            if (applicationDispatcher == null) {
                action();
                return;
            }
            applicationDispatcher.PostSynchronizationContext(action);
        }
    }
}
