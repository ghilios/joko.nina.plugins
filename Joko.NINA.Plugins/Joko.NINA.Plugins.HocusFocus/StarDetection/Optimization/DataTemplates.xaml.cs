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

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Interaction logic for DataTemplates.xaml. Exported as a <see cref="ResourceDictionary"/> so NINA's plugin
    /// resource composition merges it into application resources; this is how the wizard's content DataTemplate
    /// (keyed by the VM's full type name) becomes discoverable to IWindowServiceFactory when T5 shows the dialog.
    /// </summary>
    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {

        public DataTemplates() {
            InitializeComponent();
        }
    }
}
