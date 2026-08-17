#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    /// <summary>
    /// The persisted "PerFilterStarDetectionJson" blob: the scrubbed global settings captured when the feature
    /// was enabled (the seed for filter names first seen later) plus one self-keyed entry per filter name
    /// (NINA TrainedFlatExposureSetting precedent — a list of records, not a dictionary).
    /// </summary>
    public class PerFilterStarDetectionData {
        public int SchemaVersion = 1;
        public StarDetectionSettingsSnapshot GlobalSeed;
        public List<PerFilterStarDetectionEntry> Filters = new();
    }

    public class PerFilterStarDetectionEntry {
        public string FilterName;
        public StarDetectionSettingsSnapshot Settings;

        /// <summary>
        /// This filter's auto-focus sweep-geometry override, or null when it has none. Null is also what every
        /// blob written before this field existed deserializes to, and null means "inherit the profile" — so
        /// "missing" and "unset" are the same state by construction and no migration branch is needed.
        /// </summary>
        public PerFilterSweepGeometry SweepGeometry;
    }
}
