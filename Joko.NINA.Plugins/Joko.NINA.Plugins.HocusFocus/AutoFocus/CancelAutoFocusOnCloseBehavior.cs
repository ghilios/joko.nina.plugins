#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.ComponentModel;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// Attached behavior for the sequence-item AutoFocus popup (the implicit <see cref="HocusFocusVM"/> DataTemplate,
    /// which NINA's WindowService hosts in its own <c>CustomWindow</c>). While an AutoFocus run is in progress, clicking
    /// the window's close (X) button cancels the run instead of closing; the window then closes on its own once the run
    /// has actually stopped (<see cref="HocusFocusVM.AutoFocusInProgress"/> clears). With no run in progress the window
    /// closes immediately, as usual.
    ///
    /// Applied ONLY on the popup template — never the docked pane, whose VM lives in the always-open main window (where
    /// hooking Closing would fight NINA's own shutdown). The X's <c>CloseCommand</c> calls <c>Window.Close()</c>, so a
    /// standard Window.Closing hook is the interception point.
    /// </summary>
    public static class CancelAutoFocusOnCloseBehavior {

        public static readonly DependencyProperty CancelOnCloseProperty =
            DependencyProperty.RegisterAttached(
                "CancelOnClose",
                typeof(bool),
                typeof(CancelAutoFocusOnCloseBehavior),
                new PropertyMetadata(false, OnCancelOnCloseChanged));

        public static bool GetCancelOnClose(DependencyObject obj) => (bool)obj.GetValue(CancelOnCloseProperty);

        public static void SetCancelOnClose(DependencyObject obj, bool value) => obj.SetValue(CancelOnCloseProperty, value);

        // Per-host state so Unloaded can detach the exact Closing handler we attached (the host element outlives a
        // single Loaded/Unloaded cycle when the popup is shown, hidden, and re-shown).
        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached("State", typeof(HostState), typeof(CancelAutoFocusOnCloseBehavior), new PropertyMetadata(null));

        private sealed class HostState {
            public Window Window;
            public CancelEventHandler ClosingHandler;
            public bool CancelInitiated;
        }

        private static void OnCancelOnCloseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not FrameworkElement fe) {
                return;
            }
            if ((bool)e.NewValue) {
                fe.Loaded += OnLoaded;
                fe.Unloaded += OnUnloaded;
                if (fe.IsLoaded) {
                    Attach(fe);
                }
            } else {
                fe.Loaded -= OnLoaded;
                fe.Unloaded -= OnUnloaded;
                Detach(fe);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe) {
                Attach(fe);
            }
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe) {
                Detach(fe);
            }
        }

        private static void Attach(FrameworkElement fe) {
            if (fe.GetValue(StateProperty) is HostState) {
                return; // already hooked
            }
            var window = Window.GetWindow(fe);
            if (window == null) {
                return;
            }
            var state = new HostState { Window = window };
            state.ClosingHandler = (_, ce) => OnWindowClosing(fe, state, ce);
            window.Closing += state.ClosingHandler;
            fe.SetValue(StateProperty, state);
        }

        private static void Detach(FrameworkElement fe) {
            if (fe.GetValue(StateProperty) is not HostState state) {
                return;
            }
            if (state.Window != null && state.ClosingHandler != null) {
                state.Window.Closing -= state.ClosingHandler;
            }
            fe.ClearValue(StateProperty);
        }

        private static void OnWindowClosing(FrameworkElement fe, HostState state, CancelEventArgs ce) {
            // No run in flight → let the window close normally. This also covers the programmatic close we trigger once
            // the run has stopped: by then AutoFocusInProgress is false, so the second Closing pass sails through.
            if (fe.DataContext is not HocusFocusVM vm || !vm.AutoFocusInProgress) {
                return;
            }

            // A run is in progress → hold the window open and cancel the run instead.
            ce.Cancel = true;
            if (state.CancelInitiated) {
                return; // a prior X already started the cancel; keep waiting for it to stop.
            }
            state.CancelInitiated = true;

            var window = state.Window;
            PropertyChangedEventHandler onVmChanged = null;
            onVmChanged = (_, pe) => {
                if (pe.PropertyName == nameof(HocusFocusVM.AutoFocusInProgress) && !vm.AutoFocusInProgress) {
                    vm.PropertyChanged -= onVmChanged;
                    // Completion may be raised off the UI thread; marshal the close. Closing fires again, but with the
                    // run stopped it is allowed through.
                    window.Dispatcher.BeginInvoke(new Action(window.Close));
                }
            };
            vm.PropertyChanged += onVmChanged;
            vm.CancelAutoFocus();
        }
    }
}
