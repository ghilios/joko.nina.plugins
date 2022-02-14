#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Properties;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows.Input;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Interfaces;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus {

    [Export(typeof(IPluginManifest))]
    public class HocusFocusPlugin : PluginBase {

        [ImportingConstructor]
        public HocusFocusPlugin(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IFilterWheelMediator filterWheelMediator,
            IFocuserMediator focuserMediator,
            IGuiderMediator guiderMediator,
            IImagingMediator imagingMediator,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector) {
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
            if (AutoFocusEngineFactory == null) {
                AutoFocusEngineFactory = new AutoFocusEngineFactory(
                    profileService,
                    cameraMediator,
                    filterWheelMediator,
                    focuserMediator,
                    guiderMediator,
                    imagingMediator,
                    imageDataFactory,
                    starDetectionSelector,
                    starAnnotatorSelector,
                    AutoFocusOptions);
            }

            ResetStarDetectionDefaultsCommand = new RelayCommand((object o) => StarDetectionOptions.ResetDefaults());
            ResetStarAnnotatorDefaultsCommand = new RelayCommand((object o) => StarAnnotatorOptions.ResetDefaults());
            ResetAutoFocusDefaultsCommand = new RelayCommand((object o) => AutoFocusOptions.ResetDefaults());
            ChooseIntermediatePathDiagCommand = new RelayCommand(ChooseIntermediatePathDiag);
            ChooseSavePathDiagCommand = new RelayCommand(ChooseSavePathDiag);
        }

        private void ChooseIntermediatePathDiag(object obj) {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = StarDetectionOptions.IntermediateSavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    StarDetectionOptions.IntermediateSavePath = dialog.SelectedPath;
                }
            }
        }

        private void ChooseSavePathDiag(object obj) {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = AutoFocusOptions.SavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    AutoFocusOptions.SavePath = dialog.SelectedPath;
                }
            }
        }

        public static StarDetectionOptions StarDetectionOptions { get; private set; }

        public static StarAnnotatorOptions StarAnnotatorOptions { get; private set; }

        public static AutoFocusOptions AutoFocusOptions { get; private set; }

        public static AutoFocusEngineFactory AutoFocusEngineFactory { get; private set; }

        public ICommand ResetStarDetectionDefaultsCommand { get; private set; }

        public ICommand ResetStarAnnotatorDefaultsCommand { get; private set; }

        public ICommand ResetAutoFocusDefaultsCommand { get; private set; }

        public ICommand ChooseIntermediatePathDiagCommand { get; private set; }

        public ICommand ChooseSavePathDiagCommand { get; private set; }
    }
}