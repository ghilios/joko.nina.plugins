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
using System.Windows.Input;

namespace TestApp.StarReview {

    public partial class StarReviewWindow : Window {

        private StarReviewVM Vm => DataContext as StarReviewVM;

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool hasFitOnce;

        public StarReviewWindow() {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnKeyDown;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is StarReviewVM oldVm) {
                oldVm.FitRequested -= OnFitRequested;
            }
            if (e.NewValue is StarReviewVM newVm) {
                newVm.FitRequested += OnFitRequested;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            switch (e.Key) {
                case Key.D1:
                case Key.NumPad1:
                    vm.Pass = LabelPass.Missed;
                    e.Handled = true;
                    break;
                case Key.D2:
                case Key.NumPad2:
                    vm.Pass = LabelPass.ShouldReject;
                    e.Handled = true;
                    break;
                case Key.D3:
                case Key.NumPad3:
                    vm.Pass = LabelPass.WronglyRejected;
                    e.Handled = true;
                    break;
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
            vm.Viewport.FitTo(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ApplyViewport();
        }

        private void ApplyViewport() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            ContentScale.ScaleX = vm.Viewport.Scale;
            ContentScale.ScaleY = vm.Viewport.Scale;
            ContentTranslate.X = vm.Viewport.OffsetX;
            ContentTranslate.Y = vm.Viewport.OffsetY;
            // Markers scale with the canvas transform, so refresh the zoom-inverse stroke thickness after any
            // viewport change (this covers wheel-zoom, fit, and pan since all route through ApplyViewport).
            vm.NotifyViewportChanged();
        }

        // The canvas RenderTransform maps image space -> screen space, so a mouse position taken relative to the
        // canvas's PARENT (the Border) is in screen space; convert to image space via the viewport. We read the
        // pointer relative to the Border (the untransformed container) to get true screen coords.
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

        private void ViewportCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            var screen = ScreenPoint(e);
            var (imgX, imgY) = vm.Viewport.ScreenToImage(screen.X, screen.Y);
            // Ignore clicks outside the image bounds.
            if (imgX < 0 || imgY < 0 || imgX > vm.ImageWidth || imgY > vm.ImageHeight) {
                return;
            }
            vm.ToggleLabelAt(imgX, imgY);
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
            if (!panning) {
                return;
            }
            var vm = Vm;
            if (vm == null) {
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
            // Re-fit only on the first meaningful size (initial layout); afterwards keep the user's zoom/pan so a
            // window resize doesn't blow away the current view.
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
