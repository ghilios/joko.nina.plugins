using Joko.NINA.Plugins.JokoFocus.Properties;
using Joko.NINA.Plugins.JokoFocus.StarDetection;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Joko.NINA.Plugins.JokoFocus {
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

            ResetStarDetectionDefaultsCommand = new RelayCommand((object o) => StarDetectionOptions.ResetDefaults());
            ResetStarAnnotatorDefaultsCommand = new RelayCommand((object o) => StarAnnotatorOptions.ResetDefaults());
        }

        public static StarDetectionOptions StarDetectionOptions { get; private set; }

        public static StarAnnotatorOptions StarAnnotatorOptions { get; private set; }

        public ICommand ResetStarDetectionDefaultsCommand { get; private set; }

        public ICommand ResetStarAnnotatorDefaultsCommand { get; private set; }
    }
}
