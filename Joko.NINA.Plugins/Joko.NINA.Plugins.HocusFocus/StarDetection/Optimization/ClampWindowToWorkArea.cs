#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Windows;
using System.Windows.Interop;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Attached behavior that clamps the host <see cref="Window"/>'s <see cref="FrameworkElement.MaxHeight"/> to the
    /// working area (screen height minus the taskbar) of the monitor the window is on, so the wizard never opens or
    /// resizes taller than the visible desktop. NINA's WindowService sizes the dialog to its content, which on a small
    /// laptop can exceed the screen; this caps it. Paired with the wizard body's ScrollViewer, anything taller than the
    /// work area then scrolls inside the body while the footer buttons stay pinned and reachable.
    ///
    /// Applied as <c>local:ClampWindowToWorkArea.Enabled="True"</c> on the wizard DataTemplate's root element.
    /// </summary>
    public static class ClampWindowToWorkArea {

        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(ClampWindowToWorkArea),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not FrameworkElement fe) {
                return;
            }
            if ((bool)e.NewValue) {
                fe.Loaded += OnLoaded;
                if (fe.IsLoaded) {
                    ApplyClamp(fe);
                }
            } else {
                fe.Loaded -= OnLoaded;
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe) {
                ApplyClamp(fe);
            }
        }

        private static void ApplyClamp(FrameworkElement fe) {
            var window = Window.GetWindow(fe);
            if (window is null) {
                return;
            }
            try {
                window.MaxHeight = GetWorkAreaHeightDip(window);
            } catch {
                // A sizing convenience must never break showing the dialog; fall back to the primary work area.
                window.MaxHeight = SystemParameters.WorkArea.Height;
            }
        }

        /// <summary>
        /// Work-area height (screen minus taskbar) of the window's monitor, in WPF device-independent units. Uses the
        /// per-monitor work area in physical pixels and converts it through the window's own device transform so it is
        /// correct under high-DPI scaling; falls back to the primary screen's work area (already in DIPs) if the window
        /// is not yet connected to a presentation source.
        /// </summary>
        private static double GetWorkAreaHeightDip(Window window) {
            var source = PresentationSource.FromVisual(window);
            var handle = new WindowInteropHelper(window).Handle;
            if (source?.CompositionTarget is null || handle == IntPtr.Zero) {
                return SystemParameters.WorkArea.Height;
            }
            var workAreaPx = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var deviceToDip = source.CompositionTarget.TransformFromDevice; // M22 = 1 / vertical DPI scale
            return workAreaPx.Height * deviceToDip.M22;
        }
    }
}
