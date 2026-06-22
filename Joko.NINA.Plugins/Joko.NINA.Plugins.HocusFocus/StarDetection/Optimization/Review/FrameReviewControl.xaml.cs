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

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Read-only fork of <see cref="StarReviewControl"/> for the Aberration Inspector "Review Frames" dialog: an
    /// MTF-stretched frame in a zoom/pan canvas with accepted-star boxes, HFR + registration-id labels, and
    /// registration arrows. Binds to a <see cref="FrameReviewVM"/>. Only the viewport plumbing (zoom/pan/fit/scroll)
    /// is kept from StarReviewControl — all labeling/drag/undo input is dropped.
    /// </summary>
    public partial class FrameReviewControl : UserControl {

        private FrameReviewVM Vm => DataContext as FrameReviewVM;

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool hasFitOnce;

        public FrameReviewControl() {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnKeyDown;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is FrameReviewVM oldVm) {
                oldVm.FitRequested -= OnFitRequested;
            }
            if (e.NewValue is FrameReviewVM newVm) {
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
            var vm = Vm;
            if (vm == null || ViewportCanvas.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                return;
            }
            // Collapse the scrollbars and force a layout pass FIRST so the fit is measured against the final
            // (scrollbar-free) viewport — otherwise the image lands off-center until a second Fit (see StarReviewControl).
            HScroll.Visibility = Visibility.Collapsed;
            VScroll.Visibility = Visibility.Collapsed;
            ViewportCanvas.UpdateLayout();
            if (ViewportCanvas.ActualWidth <= 0 || ViewportCanvas.ActualHeight <= 0) {
                return;
            }
            vm.Viewport.FitTo(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ApplyViewport();
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
            if (ViewportCanvas.ActualWidth > 0 && Vm?.ImageWidth > 0) {
                hasFitOnce = true;
                OnFitRequested(this, EventArgs.Empty);
            }
        }
    }
}
