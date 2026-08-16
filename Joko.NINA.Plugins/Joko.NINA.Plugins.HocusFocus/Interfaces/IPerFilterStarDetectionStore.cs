#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    /// <summary>
    /// Owns the opt-in per-filter star detection settings: the enabled flag plus one
    /// <see cref="StarDetectionSettingsSnapshot"/> per filter name, persisted per-profile as a single JSON blob.
    /// Every snapshot handed out is a clone — mutate freely, write back via <see cref="UpsertSnapshot"/>.
    /// </summary>
    public interface IPerFilterStarDetectionStore {
        bool Enabled { get; set; }

        event EventHandler EnabledChanged;

        event EventHandler<PerFilterSnapshotChangedEventArgs> SnapshotChanged;

        StarDetectionSettingsSnapshot TryGetSnapshot(string filterName);

        StarDetectionSettingsSnapshot GetOrSeedSnapshot(string filterName);

        void UpsertSnapshot(string filterName, StarDetectionSettingsSnapshot snapshot);

        /// <summary>
        /// Raised when one filter's sweep geometry changes. Separate from <see cref="SnapshotChanged"/> because
        /// the edit binder answers that one by reloading the whole detection buffer.
        /// </summary>
        event EventHandler<PerFilterSnapshotChangedEventArgs> SweepGeometryChanged;

        /// <summary>
        /// One filter's auto-focus sweep-geometry override — always a fresh instance, never null, and this read
        /// NEVER writes (unlike <see cref="GetOrSeedSnapshot"/>: geometry has a live fallback in the profile, so
        /// there is nothing to seed, and the auto-focus engine calls this on its run path).
        /// </summary>
        PerFilterSweepGeometry GetSweepGeometry(string filterName);

        /// <summary>Replaces one filter's sweep-geometry override; null clears it.</summary>
        void SetSweepGeometry(string filterName, PerFilterSweepGeometry geometry);

        IReadOnlyList<string> GetKnownFilterNames();
    }

    public class PerFilterSnapshotChangedEventArgs : EventArgs {

        public PerFilterSnapshotChangedEventArgs(string filterName) {
            FilterName = filterName;
        }

        public string FilterName { get; }
    }
}
