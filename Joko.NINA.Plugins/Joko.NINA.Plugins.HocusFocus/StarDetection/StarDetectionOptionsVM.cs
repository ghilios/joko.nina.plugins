#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [Export(typeof(IDockableVM))]
    public class StarDetectionOptionsVM : DockableVM {

        [ImportingConstructor]
        public StarDetectionOptionsVM(IProfileService profileService)
            : base(profileService) {
            this.StarDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            this.Title = "Star Detection Options";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusDetectStarsSVG"];
            ImageGeometry.Freeze();

            ChooseIntermediatePathDiagCommand = new RelayCommand(ChooseIntermediatePathDiag);
        }

        private void ChooseIntermediatePathDiag(object obj) {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = StarDetectionOptions.IntermediateSavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    StarDetectionOptions.IntermediateSavePath = dialog.SelectedPath;
                }
            }
        }

        public override bool IsTool { get; } = true;

        public IStarDetectionOptions StarDetectionOptions { get; private set; }

        public ICommand ChooseIntermediatePathDiagCommand { get; private set; }
    }
}