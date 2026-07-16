#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>
    /// Exported so NINA's PluginLoader.Compose merges it into Application.Current.Resources, which is what puts
    /// <c>HocusFocus_SimTiltAdapter_Panel</c> in scope for both of the panel's hosts (the simulator camera's
    /// setup dialog and the Imaging dockable).
    /// </summary>
    [Export(typeof(ResourceDictionary))]
    public partial class TiltAdapterDataTemplates : ResourceDictionary {

        public TiltAdapterDataTemplates() {
            InitializeComponent();
        }
    }
}
