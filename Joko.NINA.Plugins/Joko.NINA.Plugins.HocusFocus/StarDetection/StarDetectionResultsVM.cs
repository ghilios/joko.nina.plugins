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

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {
    [Export(typeof(IDockableVM))]
    public class StarDetectionResultsVM : DockableVM {

        [ImportingConstructor]
        public StarDetectionResultsVM(IProfileService profileService)
            : base(profileService) {
            this.StarDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            this.Title = "Star Detection Results";
        }

        public override bool IsTool { get; } = false;

        public IStarDetectionOptions StarDetectionOptions { get; private set; }
    }
}
