using NINA.Core.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.JokoFocus.AutoFocus {

    [Export(typeof(IPluggableBehavior))]
    public class HocusFocusVMFactory : IAutoFocusVMFactory {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;

        [ImportingConstructor]
        public HocusFocusVMFactory(
                IProfileService profileService,
                ICameraMediator cameraMediator,
                IFilterWheelMediator filterWheelMediator,
                IFocuserMediator focuserMediator,
                IGuiderMediator guiderMediator,
                IImagingMediator imagingMediator,
                IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
                IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.guiderMediator = guiderMediator;

            this.imagingMediator = imagingMediator;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
        }

        public string Name => "Joko AutoFocus";

        public string ContentId => this.GetType().FullName;

        public IAutoFocusVM Create() {
            return new HocusFocusVM(profileService, cameraMediator, filterWheelMediator, focuserMediator, guiderMediator, imagingMediator, starDetectionSelector, starAnnotatorSelector);
        }
    }
}
