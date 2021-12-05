#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [Export(typeof(IDockableVM))]
    public class StarAnnotatorOptionsVM : DockableVM {

        [ImportingConstructor]
        public StarAnnotatorOptionsVM(IProfileService profileService)
            : base(profileService) {
            this.StarAnnotatorOptions = HocusFocusPlugin.StarAnnotatorOptions;
            this.Title = "Star Annotation Options";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusAnnotateStarsSVG"];
            ImageGeometry.Freeze();
        }

        public override bool IsTool { get; } = true;

        public StarAnnotatorOptions StarAnnotatorOptions { get; private set; }
    }
}