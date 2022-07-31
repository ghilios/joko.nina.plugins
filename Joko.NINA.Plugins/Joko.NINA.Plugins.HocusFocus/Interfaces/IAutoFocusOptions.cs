#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface IAutoFocusOptions {
        int MaxConcurrent { get; set; }
        bool FastFocusModeEnabled { get; set; }
        int FastStepSize { get; set; }
        int FastOffsetSteps { get; set; }
        int FastThreshold_Seconds { get; set; }
        int FastThreshold_Celcius { get; set; }
        int FastThreshold_FocuserPosition { get; set; }
        bool ValidateHfrImprovement { get; set; }
        double HFRImprovementThreshold { get; set; }
        int AutoFocusTimeoutSeconds { get; set; }
        string SavePath { get; set; }
        bool Save { get; set; }
        string LastSelectedLoadPath { get; set; }
        int FocuserOffset { get; set; }
        bool AllowHyperbolaRotation { get; set; }
        bool RegisterStars { get; set; }
    }
}