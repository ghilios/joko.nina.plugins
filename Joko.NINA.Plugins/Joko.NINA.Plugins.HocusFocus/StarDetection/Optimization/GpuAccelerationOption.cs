#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Profile;
using NINA.Profile.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// The persisted "Allow GPU Acceleration" toggle for the Star Detection Optimization wizard — a
    /// standalone, OPTIMIZATION-SCOPED store rather than a <c>StarDetectionOptions</c> property (PR #209
    /// review): only the optimization wizard/harness consume it, so it does not belong in the general
    /// star-detection settings system at all. Living here it is structurally outside the per-filter
    /// snapshot, settings import/export, and replay payloads — no captured copy of it can exist, so the
    /// wizard start-page toggle is trivially the sole authority for the next analysis.
    ///
    /// Machine-local by construction (describes this computer's hardware): reads/writes go straight to the
    /// active profile's plugin store, bypassing any file/snapshot-backed options object and any per-filter
    /// edit buffering. Default ON — without a CUDA device the <see cref="Gpu.GpuAccelerationPolicy"/>
    /// heuristic declines and the CPU path runs, so ON is safe everywhere.
    /// </summary>
    public static class GpuAccelerationOption {

        // Kept identical to the retired StarDetectionOptions property name so values persisted by earlier
        // builds of this branch carry over.
        private const string Key = "GpuAccelerationEnabled";

        private static IPluginOptionsAccessor CreateAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new System.Exception("Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        public static bool Get(IProfileService profileService) =>
            CreateAccessor(profileService).GetValueBoolean(Key, true);

        public static void Set(IProfileService profileService, bool value) =>
            CreateAccessor(profileService).SetValueBoolean(Key, value);
    }
}
