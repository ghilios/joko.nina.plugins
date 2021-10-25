using Joko.NINA.Plugins.JokoFocus.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.JokoFocus.StarDetection {
    [Export(typeof(IDockableVM))]
    public class StarAnnotatorOptionsVM : DockableVM {

        [ImportingConstructor]
        public StarAnnotatorOptionsVM(IProfileService profileService)
            : base(profileService) {
            this.StarAnnotatorOptions = HocusFocusPlugin.StarAnnotatorOptions;
            this.Title = "Star Annotation Options";
        }

        public override bool IsTool { get; } = false;

        public StarAnnotatorOptions StarAnnotatorOptions { get; private set; }
    }
}
