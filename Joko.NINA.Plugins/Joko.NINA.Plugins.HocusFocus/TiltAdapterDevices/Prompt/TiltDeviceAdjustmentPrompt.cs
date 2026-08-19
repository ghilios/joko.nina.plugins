#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using NINA.Core.Utility.WindowService;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt {

    /// <summary>Shows the tilt-device adjustment approval prompt modally and awaits the user's choice.</summary>
    public static class TiltDeviceAdjustmentPrompt {

        /// <summary>
        /// Builds the prompt VM, shows it via NINA's <see cref="IWindowService"/> (which marshals window creation onto
        /// the application dispatcher internally), and returns the user's choice. Safe to call from a background
        /// thread: the awaited <see cref="TiltDeviceAdjustmentPromptVM.Choice"/> completes when the user clicks a
        /// button or closes the window, and continuations run off the captured context. Cancel/close resolves to
        /// <see cref="TiltDeviceAdjustmentChoice.Cancelled"/> — no moves may be sent in that case.
        /// </summary>
        /// <param name="windowServiceFactory">NINA window service factory used to host the modal.</param>
        /// <param name="replanner">
        /// Recomputes the plan (and the controller's limit check) for a given (includeTilt, includeBackfocus) toggle
        /// state. Invoked once with (true, true) for the initial preview and again on every checkbox toggle.
        /// </param>
        /// <param name="screwInwardCurvatureSignIsMeasured">
        /// False when the wizard never measured the backfocus direction — shows the "(assumed direction)" warning
        /// whenever the current plan contains a backfocus move.
        /// </param>
        /// <param name="pitchMismatchWarning">Optional saved-vs-measured step-size mismatch advisory (null/empty ⇒ hidden).</param>
        /// <param name="positionsUnknown">True when device positions are unknown, degrading excursion enforcement.</param>
        /// <param name="unitMicrons">Microns represented by one motor step, used to express the twist warning in µm.</param>
        /// <param name="labels">The user's names for their screws; null falls back to the wizard's own numbering.</param>
        public static async Task<TiltDeviceAdjustmentChoice> ShowAsync(
            IWindowServiceFactory windowServiceFactory,
            Func<bool, bool, TiltDevicePlanPreview> replanner,
            bool screwInwardCurvatureSignIsMeasured,
            string pitchMismatchWarning,
            bool positionsUnknown,
            double unitMicrons,
            IScrewLabelProvider labels = null) {
            if (windowServiceFactory == null) {
                throw new ArgumentNullException(nameof(windowServiceFactory));
            }

            var vm = new TiltDeviceAdjustmentPromptVM(replanner, screwInwardCurvatureSignIsMeasured, pitchMismatchWarning, positionsUnknown, unitMicrons, labels);
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
            _ = windowService.ShowDialog(vm, "Tilt Adapter Adjustment", ResizeMode.NoResize, WindowStyle.SingleBorderWindow);

            return await vm.Choice;
        }
    }
}
