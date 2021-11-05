﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.HocusFocus.AutoFocus;
using Joko.NINA.Plugins.HocusFocus.Properties;
using Joko.NINA.Plugins.HocusFocus.StarDetection;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows.Input;

namespace Joko.NINA.Plugins.HocusFocus {

    [Export(typeof(IPluginManifest))]
    public class HocusFocusPlugin : PluginBase {

        [ImportingConstructor]
        public HocusFocusPlugin(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            if (StarDetectionOptions == null) {
                StarDetectionOptions = new StarDetectionOptions(profileService);
            }
            if (StarAnnotatorOptions == null) {
                StarAnnotatorOptions = new StarAnnotatorOptions(profileService);
            }
            if (AutoFocusOptions == null) {
                AutoFocusOptions = new AutoFocusOptions(profileService);
            }

            ResetStarDetectionDefaultsCommand = new RelayCommand((object o) => StarDetectionOptions.ResetDefaults());
            ResetStarAnnotatorDefaultsCommand = new RelayCommand((object o) => StarAnnotatorOptions.ResetDefaults());
            ResetAutoFocusDefaultsCommand = new RelayCommand((object o) => AutoFocusOptions.ResetDefaults());
        }

        public static StarDetectionOptions StarDetectionOptions { get; private set; }

        public static StarAnnotatorOptions StarAnnotatorOptions { get; private set; }

        public static AutoFocusOptions AutoFocusOptions { get; private set; }

        public ICommand ResetStarDetectionDefaultsCommand { get; private set; }

        public ICommand ResetStarAnnotatorDefaultsCommand { get; private set; }

        public ICommand ResetAutoFocusDefaultsCommand { get; private set; }
    }
}