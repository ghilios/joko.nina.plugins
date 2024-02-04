#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum FocuserDirectionEnum {

        [Description("Out")]
        Out = 0,

        [Description("In")]
        In = 1
    }

    public interface IAutoFocusOptions : INotifyPropertyChanged {

        void ResetDeveloperSettings();

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
        int MaxOutlierRejections { get; set; }
        double OutlierRejectionConfidence { get; set; }
        bool UnevenHyperbolicFitEnabled { get; set; }
        bool WeightedHyperbolicFitEnabled { get; set; }
        bool DeveloperSettingsEnabled { get; set; }
        FocuserDirectionEnum InitialDirection { get; set; }
    }
}