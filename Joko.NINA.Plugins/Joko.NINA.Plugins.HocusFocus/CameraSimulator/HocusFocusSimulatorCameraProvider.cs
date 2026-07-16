#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator {

    /// <summary>
    /// MEF equipment provider that surfaces the Hocus Focus synthetic camera in NINA's camera dropdown. This
    /// is the plugin's first equipment export. It builds a single <see cref="HocusFocusSimulatorCamera"/> from
    /// the plugin's shared <see cref="CameraSimulatorOptions"/> singleton (or a fresh instance when the plugin
    /// has not been constructed yet, e.g. in unit tests).
    /// </summary>
    [Export(typeof(IEquipmentProvider))]
    public class HocusFocusSimulatorCameraProvider : IEquipmentProvider<ICamera> {
        private readonly IProfileService profileService;
        private readonly IExposureDataFactory exposureDataFactory;
        private readonly IImageDataFactory imageDataFactory;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFocuserMediator focuserMediator;

        [ImportingConstructor]
        public HocusFocusSimulatorCameraProvider(
            IProfileService profileService,
            IExposureDataFactory exposureDataFactory,
            IImageDataFactory imageDataFactory,
            ITelescopeMediator telescopeMediator,
            IFocuserMediator focuserMediator) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.exposureDataFactory = exposureDataFactory ?? throw new ArgumentNullException(nameof(exposureDataFactory));
            this.imageDataFactory = imageDataFactory ?? throw new ArgumentNullException(nameof(imageDataFactory));
            this.telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            this.focuserMediator = focuserMediator ?? throw new ArgumentNullException(nameof(focuserMediator));
        }

        public string Name => "Hocus Focus";

        public IList<ICamera> GetEquipment() {
            var options = HocusFocusPlugin.CameraSimulatorOptions ?? new CameraSimulatorOptions(profileService);
            var camera = new HocusFocusSimulatorCamera(
                profileService,
                exposureDataFactory,
                imageDataFactory,
                telescopeMediator,
                focuserMediator,
                options);
            return new List<ICamera> { camera };
        }
    }
}
