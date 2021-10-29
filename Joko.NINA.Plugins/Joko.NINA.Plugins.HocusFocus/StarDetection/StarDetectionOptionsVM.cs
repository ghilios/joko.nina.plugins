using Joko.NINA.Plugins.HocusFocus.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {
    [Export(typeof(IDockableVM))]
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
