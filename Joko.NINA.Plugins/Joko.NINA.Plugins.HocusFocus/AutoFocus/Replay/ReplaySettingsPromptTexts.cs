#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>
    /// User-facing strings for the replay-settings modal, so the same dialog describes different replay flows
    /// accurately: the AutoFocus pane / Aberration Inspector update detection + AutoFocus + ROI on "update profile",
    /// while the Tilt Adapter wizard updates only star-detection. <see cref="AutoFocus"/> is the default.
    /// </summary>
    public sealed class ReplaySettingsPromptTexts {
        public string Title { get; init; }
        public string Intro { get; init; }
        public string UseCurrentCaption { get; init; }
        public string UseCurrentDescription { get; init; }
        public string UseCaptureCaption { get; init; }
        public string UseCaptureDescription { get; init; }
        public string UpdateProfileCaption { get; init; }
        public string UpdateProfileDescription { get; init; }

        /// <summary>Default wording for the AutoFocus pane / Aberration Inspector replay (detection + AutoFocus + ROI).</summary>
        public static ReplaySettingsPromptTexts AutoFocus { get; } = new ReplaySettingsPromptTexts {
            Title = "Replay Saved AutoFocus Run",
            Intro = "This run was saved with the detection and AutoFocus settings used at capture time. Choose how to replay it:",
            UseCurrentCaption = "Use current settings",
            UseCurrentDescription = "Replay using your current profile's detection and AutoFocus settings.",
            UseCaptureCaption = "Use the captured settings (don't change my profile)",
            UseCaptureDescription = "Replay using the settings from when this run was captured, held in memory only. Your profile settings are left untouched.",
            UpdateProfileCaption = "Update my profile to the captured settings",
            UpdateProfileDescription = "Overwrite your current profile's detection, AutoFocus, and ROI settings with the captured ones, then replay.",
        };

        /// <summary>
        /// Wording for the Aberration Inspector replay. Same as <see cref="AutoFocus"/> except option (b): the Inspector
        /// always analyzes with the CURRENT region grid / ROI / sensor-curve-model (so a regular single-region AutoFocus
        /// run can be inspected), and only the capture-time star-DETECTION settings are replayed in memory.
        /// </summary>
        public static ReplaySettingsPromptTexts Inspector { get; } = new ReplaySettingsPromptTexts {
            Title = "Replay Saved AutoFocus Run",
            Intro = "This run was saved with the detection and AutoFocus settings used at capture time. Choose how to replay it:",
            UseCurrentCaption = "Use current settings",
            UseCurrentDescription = "Replay using your current profile's detection and AutoFocus settings.",
            UseCaptureCaption = "Use the captured detection settings (don't change my profile)",
            UseCaptureDescription = "Replay using the captured star-detection settings, held in memory only. The Aberration Inspector always analyzes with your current region grid and ROI, so those use your current Inspector settings. Your profile is left untouched.",
            UpdateProfileCaption = "Update my profile to the captured settings",
            UpdateProfileDescription = "Overwrite your current profile's detection, AutoFocus, and ROI settings with the captured ones, then replay.",
        };

        /// <summary>Wording for the Tilt Adapter wizard replay, which applies only star-detection settings.</summary>
        public static ReplaySettingsPromptTexts TiltCalibration { get; } = new ReplaySettingsPromptTexts {
            Title = "Replay Saved Tilt Calibration",
            Intro = "This calibration was saved with the star-detection settings used at capture time. Choose how to replay it:",
            UseCurrentCaption = "Use current settings",
            UseCurrentDescription = "Replay using your current profile's star-detection and tilt-calibration settings.",
            UseCaptureCaption = "Use the captured settings (don't change my profile)",
            UseCaptureDescription = "Replay using the star-detection settings from when this calibration was captured, held in memory only. Your profile is left untouched.",
            UpdateProfileCaption = "Update my profile to the captured settings",
            UpdateProfileDescription = "Overwrite your current profile's star-detection settings with the captured ones, then replay.",
        };
    }
}
