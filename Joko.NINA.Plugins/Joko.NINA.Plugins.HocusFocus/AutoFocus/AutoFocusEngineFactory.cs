#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class AutoFocusEngineFactory : IAutoFocusEngineFactory {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAlglibAPI alglibAPI;

        public AutoFocusEngineFactory(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IFilterWheelMediator filterWheelMediator,
            IFocuserMediator focuserMediator,
            IGuiderMediator guiderMediator,
            IImagingMediator imagingMediator,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
            IAutoFocusOptions autoFocusOptions,
            IAlglibAPI alglibAPI) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.imagingMediator = imagingMediator;
            this.guiderMediator = guiderMediator;
            this.imageDataFactory = imageDataFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
        }

        public IAutoFocusEngine Create() {
            return new AutoFocusEngine(
                profileService,
                cameraMediator,
                filterWheelMediator,
                focuserMediator,
                guiderMediator,
                imagingMediator,
                imageDataFactory,
                starDetectionSelector,
                starAnnotatorSelector,
                autoFocusOptions,
                alglibAPI);
        }
    }
}