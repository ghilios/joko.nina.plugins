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
    /// disabling the feature returns exactly to the pre-enable global settings. UI-thread only, like the options
    /// singleton it wraps — detection threads read the store directly, never this binder.
    /// </summary>
    public class PerFilterEditBinder : BaseINPC {
        private readonly IPerFilterStarDetectionStore store;
        private readonly StarDetectionOptions buffer;
        private readonly IProfileService profileService;
        private readonly Func<string> getCurrentFilterName;

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
            Func<string> getCurrentFilterName) {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.getCurrentFilterName = getCurrentFilterName ?? throw new ArgumentNullException(nameof(getCurrentFilterName));

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
                    if (!string.IsNullOrEmpty(editedFilterName)) {
                        LoadSnapshotIntoBuffer(editedFilterName);
                    }
                }
            }
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

        private void Store_SnapshotChanged(object sender, PerFilterSnapshotChangedEventArgs e) {
            if (isMirroring || isLoading || !store.Enabled) {
                return;
            }
            if (!string.Equals(e.FilterName, editedFilterName, StringComparison.Ordinal)) {
                return;
            }
            LoadSnapshotIntoBuffer(editedFilterName);
        }
    }
}
