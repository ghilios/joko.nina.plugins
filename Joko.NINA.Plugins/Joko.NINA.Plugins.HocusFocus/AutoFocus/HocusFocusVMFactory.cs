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
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.ComponentModel.Composition;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [Export(typeof(IPluggableBehavior))]
    public class HocusFocusVMFactory : IAutoFocusVMFactory {
        private readonly IProfileService profileService;
        private readonly IFocuserMediator focuserMediator;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IAutoFocusOptions autoFocusOptions;

        [ImportingConstructor]
        public HocusFocusVMFactory(
            IProfileService profileService,
            IFocuserMediator focuserMediator) : this(profileService, focuserMediator, HocusFocusPlugin.AutoFocusOptions, HocusFocusPlugin.AutoFocusEngineFactory) {
        }

        public HocusFocusVMFactory(
            IProfileService profileService,
            IFocuserMediator focuserMediator,
            IAutoFocusOptions autoFocusOptions,
            IAutoFocusEngineFactory autoFocusEngineFactory) {
            this.profileService = profileService;
            this.focuserMediator = focuserMediator;
            this.autoFocusOptions = autoFocusOptions;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
        }

        public string Name => "Hocus Focus";

        public string ContentId => this.GetType().FullName;

        public IAutoFocusVM Create() {
            return new HocusFocusVM(profileService, focuserMediator, autoFocusEngineFactory, autoFocusOptions);
        }
    }
}