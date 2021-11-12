#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using Joko.NINA.Plugins.TenMicron.Properties;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows.Input;

namespace Joko.NINA.Plugins.TenMicron {

    [Export(typeof(IPluginManifest))]
    public class TenMicronPlugin : PluginBase {

        [ImportingConstructor]
        public TenMicronPlugin(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            if (ModelBuilderOptions == null) {
                ModelBuilderOptions = new ModelBuilderOptions(profileService);
            }

            ResetModelBuilderDefaultsCommand = new RelayCommand((object o) => ModelBuilderOptions.ResetDefaults());
        }

        public static ModelBuilderOptions ModelBuilderOptions { get; private set; }

        public ICommand ResetModelBuilderDefaultsCommand { get; private set; }
    }
}