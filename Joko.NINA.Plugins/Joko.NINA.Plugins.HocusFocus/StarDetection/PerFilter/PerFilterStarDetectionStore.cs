#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    public class PerFilterStarDetectionStore : BaseINPC, IPerFilterStarDetectionStore {
        private const string EnabledKey = "PerFilterStarDetectionEnabled";
        private const string JsonKey = "PerFilterStarDetectionJson";

        private readonly IProfileService profileService;
        private readonly IPluginOptionsAccessor optionsAccessor;
        private readonly Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot;
        // Detection resolves snapshots from background threads while the UI edits them; the lock guards the map
        // and the seed, and every snapshot crossing the boundary is a clone.
        private readonly object storeLock = new object();
        private readonly Dictionary<string, StarDetectionSettingsSnapshot> snapshotsByFilterName = new(StringComparer.Ordinal);

        private bool enabled;
        private StarDetectionSettingsSnapshot globalSeed;

        public PerFilterStarDetectionStore(IProfileService profileService, Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot)
            : this(profileService, CreateDefaultAccessor(profileService), captureGlobalSnapshot) {
        }

        public PerFilterStarDetectionStore(IProfileService profileService, IPluginOptionsAccessor optionsAccessor, Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            this.captureGlobalSnapshot = captureGlobalSnapshot ?? throw new ArgumentNullException(nameof(captureGlobalSnapshot));
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(PerFilterStarDetectionStore));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        public event EventHandler EnabledChanged;

        public event EventHandler<PerFilterSnapshotChangedEventArgs> SnapshotChanged;

        public bool Enabled {
            get => enabled;
            set {
                if (enabled != value) {
                    enabled = value;
                    optionsAccessor.SetValueBoolean(EnabledKey, enabled);
                    if (enabled) {
                        SeedAllFromGlobal();
                    }
                    RaisePropertyChanged();
                    EnabledChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public StarDetectionSettingsSnapshot TryGetSnapshot(string filterName) {
            lock (storeLock) {
                return snapshotsByFilterName.TryGetValue(filterName, out var snapshot) ? snapshot.Clone() : null;
            }
        }

        public StarDetectionSettingsSnapshot GetOrSeedSnapshot(string filterName) {
            StarDetectionSettingsSnapshot result;
            bool seeded = false;
            lock (storeLock) {
                if (!snapshotsByFilterName.TryGetValue(filterName, out var snapshot)) {
                    snapshot = globalSeed != null ? globalSeed.Clone() : Scrub(captureGlobalSnapshot());
                    snapshotsByFilterName[filterName] = snapshot;
                    PersistLocked();
                    seeded = true;
                }
                result = snapshot.Clone();
            }
            if (seeded) {
                SnapshotChanged?.Invoke(this, new PerFilterSnapshotChangedEventArgs(filterName));
            }
            return result;
        }

        public void UpsertSnapshot(string filterName, StarDetectionSettingsSnapshot snapshot) {
            if (snapshot == null) {
                throw new ArgumentNullException(nameof(snapshot));
            }
            lock (storeLock) {
                snapshotsByFilterName[filterName] = Scrub(snapshot);
                PersistLocked();
            }
            SnapshotChanged?.Invoke(this, new PerFilterSnapshotChangedEventArgs(filterName));
        }

        public IReadOnlyList<string> GetKnownFilterNames() {
            lock (storeLock) {
                return new List<string>(snapshotsByFilterName.Keys);
            }
        }

        // Normalizes the machine-local fields on a clone (they stay global by scope decision): debug off, no
        // intermediate saves, and the PSFParallelPartitionSize factory default.
        internal static StarDetectionSettingsSnapshot Scrub(StarDetectionSettingsSnapshot s) {
            var scrubbed = s.Clone();
            scrubbed.DebugMode = false;
            scrubbed.IntermediateSavePath = "";
            scrubbed.SaveIntermediateImages = false;
            scrubbed.PSFParallelPartitionSize = 100;
            return scrubbed;
        }

        private void SeedAllFromGlobal() {
            lock (storeLock) {
                globalSeed = Scrub(captureGlobalSnapshot());
                var filters = profileService.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                if (filters != null) {
                    foreach (var filter in filters) {
                        var name = filter?.Name;
                        if (string.IsNullOrEmpty(name) || snapshotsByFilterName.ContainsKey(name)) {
                            continue;
                        }
                        snapshotsByFilterName[name] = globalSeed.Clone();
                    }
                }
                PersistLocked();
            }
        }

        private void PersistLocked() {
            var data = new PerFilterStarDetectionData() { GlobalSeed = globalSeed };
            foreach (var kvp in snapshotsByFilterName) {
                data.Filters.Add(new PerFilterStarDetectionEntry() { FilterName = kvp.Key, Settings = kvp.Value });
            }
            optionsAccessor.SetValueString(JsonKey, JsonConvert.SerializeObject(data));
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        private void InitializeOptions() {
            lock (storeLock) {
                enabled = optionsAccessor.GetValueBoolean(EnabledKey, false);
                globalSeed = null;
                snapshotsByFilterName.Clear();
                var json = optionsAccessor.GetValueString(JsonKey, "");
                if (string.IsNullOrEmpty(json)) {
                    return;
                }
                try {
                    var data = JsonConvert.DeserializeObject<PerFilterStarDetectionData>(json);
                    globalSeed = data?.GlobalSeed;
                    if (data?.Filters == null) {
                        return;
                    }
                    foreach (var entry in data.Filters) {
                        if (!string.IsNullOrEmpty(entry?.FilterName) && entry.Settings != null) {
                            snapshotsByFilterName[entry.FilterName] = entry.Settings;
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Discarding corrupt PerFilterStarDetectionJson: {ex.Message}");
                    globalSeed = null;
                    snapshotsByFilterName.Clear();
                }
            }
        }
    }
}
