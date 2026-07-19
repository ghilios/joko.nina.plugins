#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
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

        IReadOnlyList<string> GetKnownFilterNames();
    }

    public class PerFilterSnapshotChangedEventArgs : EventArgs {

        public PerFilterSnapshotChangedEventArgs(string filterName) {
            FilterName = filterName;
        }

        public string FilterName { get; }
    }
}
