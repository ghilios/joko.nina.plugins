#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>
    /// Assembles an <see cref="AutoFocusReplayMetadata"/> from the public parts the auto-focus engine extracts at the
    /// end of a live run. Kept free of the engine's private state types so it stays unit-testable.
    /// </summary>
    public static class AutoFocusReplayMetadataBuilder {

        public static AutoFocusReplayMetadata Build(
            IStarDetectionOptions starDetectionOptions,
            AutoFocusEngineOptions autoFocusOptions,
            ReplayRegionGeometry regions,
            IEnumerable<ReplayRegionResultSummary> results,
            DateTime createdAtUtc,
            string pluginVersion,
            int starDetectorVersion) {
            if (autoFocusOptions == null) {
                throw new ArgumentNullException(nameof(autoFocusOptions));
            }
            return new AutoFocusReplayMetadata() {
                SchemaVersion = AutoFocusReplayMetadata.CurrentSchemaVersion,
                CreatedAtUtc = createdAtUtc,
                PluginVersion = pluginVersion,
                StarDetectorVersion = starDetectorVersion,
                StarDetection = starDetectionOptions == null ? null : StarDetectionSettingsSnapshot.FromOptions(starDetectionOptions),
                AutoFocus = AutoFocusReplayOptionsMapper.Capture(autoFocusOptions),
                Regions = regions,
                Results = results?.ToList()
            };
        }
    }
}
