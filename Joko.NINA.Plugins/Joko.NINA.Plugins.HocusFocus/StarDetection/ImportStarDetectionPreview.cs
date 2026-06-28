#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility.WindowService;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>Shows the import-confirmation dialog modally and awaits the user's Apply/Cancel decision.</summary>
    public static class ImportStarDetectionPreview {

        /// <summary>
        /// Shows <paramref name="vm"/> via NINA's <see cref="IWindowService"/> (which marshals window creation onto the
        /// application dispatcher internally) and returns true when the user clicks Apply, false on Cancel or close.
        /// The dialog Task itself is fire-and-forget; the result flows back through the VM's TCS, which is awaited here.
        /// </summary>
        public static async Task<bool> ShowAsync(IWindowServiceFactory windowServiceFactory, ImportStarDetectionPreviewVM vm) {
            if (windowServiceFactory == null) {
                throw new ArgumentNullException(nameof(windowServiceFactory));
            }
            if (vm == null) {
                throw new ArgumentNullException(nameof(vm));
            }

            var windowService = windowServiceFactory.Create();

            // A button (or the X) raises RequestClose; close the host window, which fires OnClosed below.
            void onRequestClose(object s, EventArgs e) {
                _ = windowService.Close();
            }

            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                // Dispose resolves the result to Cancel if no button set it (window closed via the X). No-op otherwise.
                vm.Dispose();
            };
            windowService.OnClosed += onClosed;
            vm.RequestClose += onRequestClose;

            _ = windowService.ShowDialog(vm, "Import Star Detection Settings", ResizeMode.CanResize, WindowStyle.SingleBorderWindow);

            return await vm.Result;
        }
    }
}
