#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Windows;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {

    // TODO: Add star detection back after updating the approach
    // [Export(typeof(IDockableVM))]
    public class StarDetectionOptionsVM : DockableVM {

        [ImportingConstructor]
        public StarDetectionOptionsVM(IProfileService profileService)
            : base(profileService) {
            this.StarDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            this.Title = "Star Detection Options";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("Joko.NINA.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusDetectStarsSVG"];
            ImageGeometry.Freeze();
        }

        public override bool IsTool { get; } = true;

        public IStarDetectionOptions StarDetectionOptions { get; private set; }
    }
}