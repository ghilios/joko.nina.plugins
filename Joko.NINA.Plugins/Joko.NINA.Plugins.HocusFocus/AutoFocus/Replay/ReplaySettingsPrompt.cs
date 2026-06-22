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

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay {

    /// <summary>Shows the replay-settings prompt modally and awaits the user's choice.</summary>
    public static class ReplaySettingsPrompt {

        /// <summary>
        /// Builds the prompt VM, shows it via NINA's <see cref="IWindowService"/> (which marshals window creation onto
        /// the application dispatcher internally), and returns the user's choice. Safe to call from the background
        /// replay thread: the awaited <see cref="ReplaySettingsPromptVM.Choice"/> completes when the user clicks a
        /// button or closes the window, and continuations run off the captured context.
        /// </summary>
        public static async Task<ReplaySettingsChoice> ShowAsync(IWindowServiceFactory windowServiceFactory, AutoFocusReplayMetadata metadata) {
            if (windowServiceFactory == null) {
                throw new ArgumentNullException(nameof(windowServiceFactory));
            }

            var vm = new ReplaySettingsPromptVM(metadata);
            var windowService = windowServiceFactory.Create();

            // A button (or the X) raises RequestClose; close the host window, which fires OnClosed below.
            void onRequestClose(object s, EventArgs e) {
                _ = windowService.Close();
            }

            EventHandler onClosed = null;
            onClosed = (s, e) => {
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                // Dispose resolves the choice to Cancel if no button set it (window closed via the X). No-op otherwise.
                vm.Dispose();
            };
            windowService.OnClosed += onClosed;
            vm.RequestClose += onRequestClose;

            // Fire-and-forget (same as the other plugin dialogs); the result flows back through the VM's TCS, which we
            // await below. The dialog Task itself is not awaited (it completes only when the window closes).
            _ = windowService.ShowDialog(vm, "Replay Settings", ResizeMode.NoResize, WindowStyle.SingleBorderWindow);

            return await vm.Choice;
        }
    }
}
