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
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Review {

    /// <summary>A review-dialog VM that can be closed via a UI event and disposed when the window closes.</summary>
    public interface IReviewDialogViewModel : IDisposable {
        event EventHandler RequestClose;
    }

    /// <summary>
    /// Single owner of the modal "Review Frames" show/dispose lifecycle (F24): creates a WindowService, wires the
    /// self-unsubscribing RequestClose/OnClosed handler pair, disposes the VM on close, then runs an optional
    /// per-caller cleanup. Previously copy-pasted verbatim into HocusFocusVM and InspectorVM.
    /// </summary>
    public static class ReviewDialogHost {

        /// <param name="afterClosed">Optional per-VM cleanup invoked AFTER vm.Dispose() (e.g. releasing the owning
        /// VM's own snapshot/bitmaps), matching the pre-extraction order.</param>
        public static void Show(IWindowServiceFactory windowServiceFactory, IReviewDialogViewModel vm, string title, Action afterClosed = null) {
            ArgumentNullException.ThrowIfNull(windowServiceFactory);
            ArgumentNullException.ThrowIfNull(vm);
            var windowService = windowServiceFactory.Create();

            void onRequestClose(object s, EventArgs e) => _ = windowService.Close();

            EventHandler onClosed = null;
            onClosed = (s, e) => {
                // Defensive hygiene: OnClosed fires once, but detach both handlers before disposing so nothing dangles.
                windowService.OnClosed -= onClosed;
                vm.RequestClose -= onRequestClose;
                vm.Dispose();
                afterClosed?.Invoke();
            };
            windowService.OnClosed += onClosed;
            vm.RequestClose += onRequestClose;

            windowService.ShowDialog(vm, title, ResizeMode.CanResize, WindowStyle.SingleBorderWindow);
        }
    }
}
