#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// Attached behavior that marks the <see cref="HocusFocusVM"/> hosted in the AutoFocus pane as interactive while
    /// the pane is loaded. The AF pane and the sequencer's RunAutofocus both call <see cref="HocusFocusVM.StartAutoFocus"/>
    /// through the same <c>IAutoFocusVMFactory</c>, so the only reliable discriminator is that ONLY the pane's VM is
    /// ever rendered through the <c>HocusFocusVM_Dockable</c> DataTemplate. Applying
    /// <c>InteractiveHostBehavior.HostInteractive="True"</c> on that template's root sets <see cref="HocusFocusVM.IsInteractive"/>
    /// on the bound VM; a sequence-created VM is never rendered there, so it stays false and never retains frames.
    /// </summary>
    public static class InteractiveHostBehavior {

        public static readonly DependencyProperty HostInteractiveProperty =
            DependencyProperty.RegisterAttached(
                "HostInteractive",
                typeof(bool),
                typeof(InteractiveHostBehavior),
                new PropertyMetadata(false, OnHostInteractiveChanged));

        public static bool GetHostInteractive(DependencyObject obj) => (bool)obj.GetValue(HostInteractiveProperty);

        public static void SetHostInteractive(DependencyObject obj, bool value) => obj.SetValue(HostInteractiveProperty, value);

        private static void OnHostInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not FrameworkElement fe) {
                return;
            }
            if ((bool)e.NewValue) {
                fe.Loaded += OnLoaded;
                fe.Unloaded += OnUnloaded;
                if (fe.IsLoaded) {
                    SetInteractive(fe, true);
                }
            } else {
                fe.Loaded -= OnLoaded;
                fe.Unloaded -= OnUnloaded;
                SetInteractive(fe, false);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe) {
                SetInteractive(fe, true);
            }
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e) {
            if (sender is FrameworkElement fe) {
                SetInteractive(fe, false);
            }
        }

        private static void SetInteractive(FrameworkElement fe, bool value) {
            if (fe.DataContext is HocusFocusVM vm) {
                vm.IsInteractive = value;
            }
        }
    }
}
