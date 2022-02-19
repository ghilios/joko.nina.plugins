#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Windows;

namespace TestApp {

    public partial class App : Application {

        protected override void OnStartup(StartupEventArgs e) {
            var mainWindowVM = new MainWindowVM();

            var mainWindow = new MainWindow();
            mainWindow.DataContext = mainWindowVM;
            mainWindow.Show();
        }
    }
}