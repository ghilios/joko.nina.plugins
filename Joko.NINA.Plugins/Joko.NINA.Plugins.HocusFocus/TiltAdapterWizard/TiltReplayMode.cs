#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// How a tilt-calibration replay sources its settings, derived from the user's choice in the shared
    /// replay-settings modal. Decouples the two independent decisions (which geometry, which star-detection settings)
    /// that the old two-button UI coupled into a single bool.
    /// </summary>
    internal readonly struct TiltReplayMode {

        public TiltReplayMode(bool useMetadataGeometry, bool applyCaptureTimeOverridePerStep, bool updateProfileToCaptureTime) {
            UseMetadataGeometry = useMetadataGeometry;
            ApplyCaptureTimeOverridePerStep = applyCaptureTimeOverridePerStep;
            UpdateProfileToCaptureTime = updateProfileToCaptureTime;
        }

        /// <summary>Recompute the calibration from the run's stored geometry (true) or the current profile/tilt settings (false).</summary>
        public bool UseMetadataGeometry { get; }

        /// <summary>Pass the per-step capture-time star-detection snapshot as an in-memory override (true) or use the live profile (false).</summary>
        public bool ApplyCaptureTimeOverridePerStep { get; }

        /// <summary>Persist the run's capture-time star-detection settings to the live profile AFTER a successful replay.</summary>
        public bool UpdateProfileToCaptureTime { get; }
    }

    internal static class TiltReplayModeResolver {

        /// <summary>
        /// Maps a <see cref="ReplaySettingsChoice"/> to a <see cref="TiltReplayMode"/>.
        /// <see cref="ReplaySettingsChoice.Cancel"/> must be handled by the caller before resolving (it has no replay mode).
        /// </summary>
        public static TiltReplayMode Resolve(ReplaySettingsChoice choice) {
            switch (choice) {
                case ReplaySettingsChoice.UseCurrentSettings:
                    return new TiltReplayMode(useMetadataGeometry: false, applyCaptureTimeOverridePerStep: false, updateProfileToCaptureTime: false);
                case ReplaySettingsChoice.UseCaptureTimeSettingsInMemory:
                    return new TiltReplayMode(useMetadataGeometry: true, applyCaptureTimeOverridePerStep: true, updateProfileToCaptureTime: false);
                case ReplaySettingsChoice.UpdateProfileToCaptureTime:
                    return new TiltReplayMode(useMetadataGeometry: true, applyCaptureTimeOverridePerStep: true, updateProfileToCaptureTime: true);
                default:
                    throw new ArgumentOutOfRangeException(nameof(choice), choice, "Cancel must be handled before resolving a replay mode.");
            }
        }
    }
}
