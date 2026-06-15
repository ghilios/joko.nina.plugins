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

        // Rubber-band drag state for the MISSED pass (image-space). dragging is set on left-button-down in the Missed
        // pass; dragStart is the down point and the live DragRect tracks the cursor until button-up finalizes the box.
        private bool dragging;
        private System.Windows.Point dragStartImage;

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
            // Ignore presses outside the image bounds.
            if (imgX < 0 || imgY < 0 || imgX > vm.ImageWidth || imgY > vm.ImageHeight) {
                return;
            }

            // MISSED pass: begin a rubber-band drag (the box is finalized on button-up). Other passes are click-based
            // (hit-test an accepted/rejected box on button-up) so the user can see what they are about to label.
            if (vm.Pass == LabelPass.Missed) {
                dragging = true;
                dragStartImage = new System.Windows.Point(imgX, imgY);
                // Start a zero-size rect anchored at the down point (in image coords; the canvas transform scales it).
                System.Windows.Controls.Canvas.SetLeft(DragRect, imgX);
                System.Windows.Controls.Canvas.SetTop(DragRect, imgY);
                DragRect.Width = 0;
                DragRect.Height = 0;
                DragRect.Visibility = Visibility.Visible;
                ViewportCanvas.CaptureMouse();
            }
            e.Handled = true;
        }

        private void ViewportCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            var screen = ScreenPoint(e);
            var (imgX, imgY) = vm.Viewport.ScreenToImage(screen.X, screen.Y);

            if (dragging) {
                dragging = false;
                DragRect.Visibility = Visibility.Collapsed;
                ViewportCanvas.ReleaseMouseCapture();
                // Normalize so W,H >= 0 regardless of drag direction. AddMissedBox widens a tiny drag (effectively a
                // click) to a small default box itself.
                var x = Math.Min(dragStartImage.X, imgX);
                var y = Math.Min(dragStartImage.Y, imgY);
                var w = Math.Abs(imgX - dragStartImage.X);
                var h = Math.Abs(imgY - dragStartImage.Y);
                vm.AddMissedBox(x, y, w, h);
                e.Handled = true;
                return;
            }

            // Click-based passes (should-reject / wrongly-rejected): hit-test the detector boxes. Ignore clicks
            // outside the image bounds.
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
            var vm = Vm;
            if (vm == null) {
                return;
            }

            // Update the live rubber-band while dragging a MISSED box (image-space; the canvas transform scales it).
            if (dragging) {
                var screenNow = ScreenPoint(e);
                var (imgX, imgY) = vm.Viewport.ScreenToImage(screenNow.X, screenNow.Y);
                var x = Math.Min(dragStartImage.X, imgX);
                var y = Math.Min(dragStartImage.Y, imgY);
                System.Windows.Controls.Canvas.SetLeft(DragRect, x);
                System.Windows.Controls.Canvas.SetTop(DragRect, y);
                DragRect.Width = Math.Abs(imgX - dragStartImage.X);
                DragRect.Height = Math.Abs(imgY - dragStartImage.Y);
                return;
            }

            if (!panning) {
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
