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
using System.Windows.Controls;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {

        public DataTemplates() {
            InitializeComponent();
        }

        // Enabling "Save AutoFocus runs" immediately prompts for the folder so the user picks it in one click.
        private void SaveAFRuns_Checked(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe && fe.DataContext is TiltAdapterWizardVM vm) {
                vm.PromptForSaveFolderIfNeeded();
            }
        }
    }
}
