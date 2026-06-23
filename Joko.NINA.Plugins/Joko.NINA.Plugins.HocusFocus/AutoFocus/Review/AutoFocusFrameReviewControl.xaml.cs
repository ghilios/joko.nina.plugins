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
using System.Windows.Controls;
using System.Windows.Input;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Review {

    /// <summary>
    /// Read-only viewer for the manual AutoFocus "Review Frames" dialog: an MTF-stretched frame in a zoom/pan canvas
    /// with the Star Annotator's overlays (bounds, per-star text, center, rejection-reason + ROI boxes). Binds to an
    /// <see cref="AutoFocusFrameReviewVM"/>. The viewport plumbing (zoom/pan/fit/scroll) mirrors the Aberration
    /// Inspector's <c>FrameReviewControl</c>; there is no hover focus graph here.
    /// </summary>
    public partial class AutoFocusFrameReviewControl : UserControl {

        private AutoFocusFrameReviewVM Vm => DataContext as AutoFocusFrameReviewVM;

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool hasFitOnce;
        private bool windowSized;

        public AutoFocusFrameReviewControl() {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnKeyDown;
            Loaded += OnLoaded;
        }

        // Size the host window to FIT the screen on first load (clamped to the work area, centered), with a small
        // minimum so it can be shrunk freely. The WindowService opens the window sized-to-content, which — without this —
        // would honor whatever large size the content asks for and open off-screen on smaller displays. A small Min lets
        // the layout redistribute (the image column shrinks first, so the right-hand legend stays visible).
        private void OnLoaded(object sender, RoutedEventArgs e) {
            if (windowSized) {
                return;
            }
            var window = Window.GetWindow(this);
            if (window == null) {
                return;
            }
            windowSized = true;

            var work = SystemParameters.WorkArea; // device-independent pixels, excludes the taskbar
            const double desiredWidth = 1100.0;
            const double desiredHeight = 720.0;
            const double margin = 40.0; // leave a little breathing room around the window

            window.SizeToContent = SizeToContent.Manual;
            window.MinWidth = Math.Min(640.0, work.Width);
            window.MinHeight = Math.Min(440.0, work.Height);
            window.Width = Math.Min(desiredWidth, Math.Max(window.MinWidth, work.Width - margin));
            window.Height = Math.Min(desiredHeight, Math.Max(window.MinHeight, work.Height - margin));
            window.Left = work.Left + (work.Width - window.Width) / 2.0;
            window.Top = work.Top + (work.Height - window.Height) / 2.0;

            // The initial auto-fit (ViewportCanvas_SizeChanged) ran against the pre-resize canvas size, so re-fit
            // against the FINAL window size once the resize layout pass has settled (DispatcherPriority.Loaded runs
            // after layout). Latch hasFitOnce only if that deferred fit actually succeeds; if the canvas still
            // reports ActualWidth<=0 (or Vm isn't attached yet), leave hasFitOnce false so the SizeChanged auto-fit
            // can still perform the first real fit. Without this the image could open unfit on slow layout passes.
            Dispatcher.BeginInvoke(
                new Action(() => { if (TryFit()) { hasFitOnce = true; } }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is AutoFocusFrameReviewVM oldVm) {
                oldVm.FitRequested -= OnFitRequested;
            }
            if (e.NewValue is AutoFocusFrameReviewVM newVm) {
                newVm.FitRequested += OnFitRequested;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            switch (e.Key) {
                case Key.Left:
                    if (vm.PrevCommand.CanExecute(null)) {
                        vm.PrevCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (vm.NextCommand.CanExecute(null)) {
                        vm.NextCommand.Execute(null);
                    }
                    e.Handled = true;
                    break;
                case Key.F:
                    OnFitRequested(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        private void OnFitRequested(object sender, EventArgs e) {
            TryFit();
        }

        // Returns true only when a fit was actually applied (canvas measured and VM ready), so callers can gate
        // the one-shot hasFitOnce latch on a real fit rather than on a deferred attempt that returned early.
        private bool TryFit() {
            var vm = Vm;
            if (vm == null || ViewportCanvas.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                return false;
            }
            // Collapse the scrollbars and force a layout pass FIRST so the fit is measured against the final
            // (scrollbar-free) viewport — otherwise the image lands off-center until a second Fit.
            HScroll.Visibility = Visibility.Collapsed;
            VScroll.Visibility = Visibility.Collapsed;
            ViewportCanvas.UpdateLayout();
            if (ViewportCanvas.ActualWidth <= 0 || ViewportCanvas.ActualHeight <= 0) {
                return false;
            }
            vm.Viewport.FitTo(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ApplyViewport();
            return true;
        }

        private void ApplyViewport() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            vm.Viewport.ClampToBounds(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ContentScale.ScaleX = vm.Viewport.Scale;
            ContentScale.ScaleY = vm.Viewport.Scale;
            ContentTranslate.X = vm.Viewport.OffsetX;
            ContentTranslate.Y = vm.Viewport.OffsetY;
            UpdateScrollBars();
            // Markers scale with the canvas transform, so refresh the zoom-inverse stroke/label sizes after any change.
            vm.NotifyViewportChanged();
        }

        private bool suppressScrollEvents;

        private void UpdateScrollBars() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            suppressScrollEvents = true;
            try {
                UpdateScrollBar(HScroll, ViewportCanvas.ActualWidth, vm.ImageWidth * vm.Viewport.Scale, -vm.Viewport.OffsetX);
                UpdateScrollBar(VScroll, ViewportCanvas.ActualHeight, vm.ImageHeight * vm.Viewport.Scale, -vm.Viewport.OffsetY);
            } finally {
                suppressScrollEvents = false;
            }
        }

        private static void UpdateScrollBar(System.Windows.Controls.Primitives.ScrollBar bar, double viewport, double content, double scrollPos) {
            var scrollable = content - viewport;
            if (scrollable > 0.5 && viewport > 0) {
                bar.Visibility = Visibility.Visible;
                bar.Minimum = 0;
                bar.Maximum = scrollable;
                bar.ViewportSize = viewport;
                bar.LargeChange = viewport * 0.9;
                bar.SmallChange = Math.Max(1.0, viewport * 0.1);
                bar.Value = Math.Min(Math.Max(scrollPos, 0.0), scrollable);
            } else {
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
            }
        }

        private void HScroll_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e) {
            var vm = Vm;
            if (vm == null || suppressScrollEvents) {
                return;
            }
            vm.Viewport.Set(vm.Viewport.Scale, -e.NewValue, vm.Viewport.OffsetY);
            ApplyViewport();
        }

        private void VScroll_Scroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e) {
            var vm = Vm;
            if (vm == null || suppressScrollEvents) {
                return;
            }
            vm.Viewport.Set(vm.Viewport.Scale, vm.Viewport.OffsetX, -e.NewValue);
            ApplyViewport();
        }

        // The canvas RenderTransform maps image space -> screen space, so a mouse position taken relative to the
        // canvas's PARENT (the Border) is in screen space.
        private System.Windows.Point ScreenPoint(MouseEventArgs e) {
            var parent = (UIElement)ViewportCanvas.Parent;
            return e.GetPosition(parent);
        }

        private void ViewportCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            var anchor = ScreenPoint(e);
            var factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            vm.Viewport.ZoomAt(factor, anchor.X, anchor.Y);
            ApplyViewport();
            e.Handled = true;
        }

        private void ViewportCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            panning = true;
            lastPanScreen = ScreenPoint(e);
            ViewportCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void ViewportCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
            panning = false;
            ViewportCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void ViewportCanvas_MouseMove(object sender, MouseEventArgs e) {
            var vm = Vm;
            if (vm == null || !panning) {
                return;
            }
            var screen = ScreenPoint(e);
            var dx = screen.X - lastPanScreen.X;
            var dy = screen.Y - lastPanScreen.Y;
            lastPanScreen = screen;
            vm.Viewport.PanBy(dx, dy);
            ApplyViewport();
        }

        private void ViewportCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
            // Re-fit only on the first meaningful size (initial layout); afterwards keep the user's zoom/pan.
            if (hasFitOnce) {
                return;
            }
            // Latch only when a fit actually applies, so a too-early SizeChanged (ActualWidth still settling)
            // does not permanently suppress the first real fit.
            if (TryFit()) {
                hasFitOnce = true;
            }
        }
    }
}
