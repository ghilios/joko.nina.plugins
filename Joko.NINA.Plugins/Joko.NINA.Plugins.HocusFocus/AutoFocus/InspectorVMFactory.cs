#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System.ComponentModel.Composition;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [Export(typeof(IInspectorVMFactory))]
    public class InspectorVMFactory : IInspectorVMFactory {
        private readonly InspectorVM inspectorVM;

        [ImportingConstructor]
        public InspectorVMFactory(InspectorVM inspectorVM) {
            this.inspectorVM = inspectorVM;
        }

        public InspectorVM Create() {
            return this.inspectorVM;
        }
    }
}