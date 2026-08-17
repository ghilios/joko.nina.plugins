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

        /// <summary>
        /// Bump ONLY for a change that cannot be expressed as an additive member with an inherit/absent default.
        /// Adding sweep geometry did not qualify: the same blob legitimately holds a mix of entries with and
        /// without it (a v1 blob that this build upserts one filter into becomes a hybrid), so a version bump
        /// would encode a claim — "geometry is present" — that is false for most real blobs, and the first reader
        /// to trust it would be wrong.
        /// </summary>
        private const int CurrentSchemaVersion = 1;

        private readonly IProfileService profileService;
        private readonly IPluginOptionsAccessor optionsAccessor;
        private readonly Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot;
        // Detection resolves snapshots from background threads while the UI edits them; the lock guards the map
        // and the seed, and every snapshot crossing the boundary is a clone.
        private readonly object storeLock = new object();
        private readonly Dictionary<string, StarDetectionSettingsSnapshot> snapshotsByFilterName = new(StringComparer.Ordinal);
        // Sweep geometry is a separate map, not a field on the snapshot, because a filter can legitimately have
        // one without the other: the Optimization Wizard can write geometry for a filter whose detection settings
        // page has never been opened. Guarded by the same storeLock.
        private readonly Dictionary<string, PerFilterSweepGeometry> geometryByFilterName = new(StringComparer.Ordinal);

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

        /// <summary>
        /// Reads ONLY the persisted enabled flag for the active profile, without constructing a store (and so
        /// without its <c>ProfileChanged</c> subscription, and with no chance of the seed-on-enable write path
        /// running). For callers that must know whether per-filter resolution WOULD apply but cannot participate in
        /// it — notably the headless harness, which has no filter wheel and therefore runs profile-level settings:
        /// it needs to warn about the divergence, and it must not learn <see cref="EnabledKey"/> to do so.
        /// </summary>
        public static bool IsEnabledForActiveProfile(IPluginOptionsAccessor optionsAccessor) {
            if (optionsAccessor == null) {
                throw new ArgumentNullException(nameof(optionsAccessor));
            }
            return optionsAccessor.GetValueBoolean(EnabledKey, false);
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

        /// <summary>
        /// Raised when one filter's sweep geometry changes. Separate from <see cref="SnapshotChanged"/> on purpose:
        /// the edit binder answers that one by reloading the entire detection buffer, which a geometry edit must
        /// not trigger.
        /// </summary>
        public event EventHandler<PerFilterSnapshotChangedEventArgs> SweepGeometryChanged;

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

        /// <summary>
        /// One filter's sweep-geometry override. Always a fresh instance and NEVER null: an unknown filter, or a
        /// known one with no override, yields an all-<see cref="PerFilterSweepGeometry.Inherit"/> instance, so
        /// callers never null-check and can never persist by accident through the value they were handed.
        ///
        /// <para><b>Unlike <see cref="GetOrSeedSnapshot"/>, this never writes.</b> Detection seeds on read because
        /// it has no fallback; geometry has one (the profile), so a read has no reason to persist. That matters
        /// concretely: the auto-focus engine calls this on its run path, where a seed-and-fan-out would push a
        /// <see cref="SnapshotChanged"/> through the edit binder mid-run.</para>
        /// </summary>
        public PerFilterSweepGeometry GetSweepGeometry(string filterName) {
            if (string.IsNullOrEmpty(filterName)) {
                return PerFilterSweepGeometry.Unset();
            }
            lock (storeLock) {
                return geometryByFilterName.TryGetValue(filterName, out var geometry)
                    ? geometry.Normalized()
                    : PerFilterSweepGeometry.Unset();
            }
        }

        /// <summary>
        /// Replaces one filter's sweep-geometry override, normalizing anything unusable back to
        /// <see cref="PerFilterSweepGeometry.Inherit"/>. A null <paramref name="geometry"/> means "clear the
        /// override" — unlike <see cref="UpsertSnapshot"/>, null is meaningful here rather than a programming error.
        /// </summary>
        public void SetSweepGeometry(string filterName, PerFilterSweepGeometry geometry) {
            if (string.IsNullOrEmpty(filterName)) {
                return;
            }
            lock (storeLock) {
                geometryByFilterName[filterName] = (geometry ?? PerFilterSweepGeometry.Unset()).Normalized();
                PersistLocked();
            }
            SweepGeometryChanged?.Invoke(this, new PerFilterSnapshotChangedEventArgs(filterName));
        }

        public IReadOnlyList<string> GetKnownFilterNames() {
            lock (storeLock) {
                var names = new List<string>(snapshotsByFilterName.Keys);
                foreach (var name in geometryByFilterName.Keys) {
                    if (!snapshotsByFilterName.ContainsKey(name)) {
                        names.Add(name);
                    }
                }
                return names;
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

        // Iterates the UNION of both maps, not just the snapshot map: a filter can carry sweep geometry without
        // detection settings (a wizard Accept on a filter whose options page was never opened), and iterating only
        // the snapshots would drop that filter's override on the very next persist.
        private void PersistLocked() {
            var data = new PerFilterStarDetectionData() { GlobalSeed = globalSeed };
            foreach (var kvp in snapshotsByFilterName) {
                data.Filters.Add(new PerFilterStarDetectionEntry() {
                    FilterName = kvp.Key,
                    Settings = kvp.Value,
                    SweepGeometry = GeometryToPersistLocked(kvp.Key)
                });
            }
            foreach (var kvp in geometryByFilterName) {
                if (snapshotsByFilterName.ContainsKey(kvp.Key)) {
                    continue;
                }
                var geometry = GeometryToPersistLocked(kvp.Key);
                if (geometry == null) {
                    continue;
                }
                data.Filters.Add(new PerFilterStarDetectionEntry() { FilterName = kvp.Key, SweepGeometry = geometry });
            }
            optionsAccessor.SetValueString(JsonKey, JsonConvert.SerializeObject(data));
        }

        // An all-Inherit override is indistinguishable from having none, so it is written as null to keep the blob
        // byte-comparable with one produced before this field existed.
        private PerFilterSweepGeometry GeometryToPersistLocked(string filterName) {
            if (!geometryByFilterName.TryGetValue(filterName, out var geometry) || geometry.IsUnset) {
                return null;
            }
            return geometry;
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
                geometryByFilterName.Clear();
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
                    if (data.SchemaVersion > CurrentSchemaVersion) {
                        // Load best-effort rather than discarding: silently wiping a user's per-filter sets because
                        // they briefly ran a newer build would be far worse than ignoring a field we don't know.
                        Logger.Warning(
                            $"PerFilterStarDetectionJson was written by a newer schema (v{data.SchemaVersion}); reading it as " +
                            $"v{CurrentSchemaVersion}. Settings this build does not understand are ignored.");
                    }
                    foreach (var entry in data.Filters) {
                        // Note the OR: an entry may carry sweep geometry with no detection settings, and dropping it
                        // for lack of a snapshot would lose the override.
                        if (string.IsNullOrEmpty(entry?.FilterName) || (entry.Settings == null && entry.SweepGeometry == null)) {
                            continue;
                        }
                        if (entry.Settings != null) {
                            snapshotsByFilterName[entry.FilterName] = entry.Settings;
                        }
                        if (entry.SweepGeometry != null) {
                            geometryByFilterName[entry.FilterName] = entry.SweepGeometry.Normalized();
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Discarding corrupt PerFilterStarDetectionJson: {ex.Message}");
                    globalSeed = null;
                    snapshotsByFilterName.Clear();
                    geometryByFilterName.Clear();
                }
            }
        }
    }
}
