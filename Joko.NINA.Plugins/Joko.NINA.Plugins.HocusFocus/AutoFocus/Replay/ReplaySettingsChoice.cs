#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>The user's choice in the replay-settings prompt when a saved run has a <c>metadata.json</c>.</summary>
    public enum ReplaySettingsChoice {

        /// <summary>The prompt was dismissed/cancelled — abort the replay.</summary>
        Cancel = 0,

        /// <summary>Replay with the current profile detection + AutoFocus settings (legacy behavior).</summary>
        UseCurrentSettings,

        /// <summary>Replay with the run's capture-time settings, held in memory; the profile is not modified.</summary>
        UseCaptureTimeSettingsInMemory,

        /// <summary>Overwrite the live profile with the run's capture-time settings, then replay.</summary>
        UpdateProfileToCaptureTime
    }
}
