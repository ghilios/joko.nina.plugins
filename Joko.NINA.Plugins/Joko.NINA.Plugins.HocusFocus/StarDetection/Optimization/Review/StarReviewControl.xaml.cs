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
    /// Reusable interactive star-review UserControl: an MTF-stretched frame in a zoom/pan canvas with accepted/
    /// rejected/label overlays and the three labeling passes. Binds to a <see cref="StarReviewVM"/> via its
    /// DataContext. Hosted by TestApp's <c>review</c> dev tool today (inside a thin Window) and by the in-NINA
    /// optimization wizard later — the visual tree + input handling live here so there is no drift between them.
    /// </summary>
    public partial class StarReviewControl : UserControl {

        private StarReviewVM Vm => DataContext as StarReviewVM;

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool hasFitOnce;

        // Left-button interaction state (image-space). pressArmed is set on a valid left-button-down inside the image
        // and consumed on button-up (so a press that started outside the image, or after the VM went away, is ignored).
        // dragging is set only when the press lands on BLANK space (begin a MISSED rubber-band); a press on a detector
        // box leaves dragging false (a click that toggles the inferred label on button-up). dragStart is the down point
        // and the live DragRect tracks the cursor until button-up finalizes the box.
        private bool pressArmed;
        private bool dragging;
        private System.Windows.Point dragStartImage;

        public StarReviewControl() {
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

            // Mode-less routing: a press over a detector box (accepted or rejected) is a CLICK — on button-up it
            // toggles the inferred label. A press over BLANK space begins a MISSED rubber-band drag (finalized on
            // button-up). Disambiguating on the press keeps a click on a star from dropping a stray missed box.
            pressArmed = true;
            if (vm.HitTestCandidate(imgX, imgY)) {
                dragging = false; // click on a detector box: no rubber-band
            } else {
                dragging = true;
                dragStartImage = new System.Windows.Point(imgX, imgY);
                // Start a zero-size rect anchored at the down point (in image coords; the canvas transform scales it).
                System.Windows.Controls.Canvas.SetLeft(DragRect, imgX);
                System.Windows.Controls.Canvas.SetTop(DragRect, imgY);
                DragRect.Width = 0;
                DragRect.Height = 0;
                DragRect.Visibility = Visibility.Visible;
            }
            ViewportCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void ViewportCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            if (!pressArmed) {
                return; // press started outside the image (or VM swapped mid-gesture): ignore the up.
            }
            pressArmed = false;
            ViewportCanvas.ReleaseMouseCapture();

            var screen = ScreenPoint(e);
            var (imgX, imgY) = vm.Viewport.ScreenToImage(screen.X, screen.Y);

            if (dragging) {
                dragging = false;
                DragRect.Visibility = Visibility.Collapsed;
                // Normalize so W,H >= 0 regardless of drag direction. AddMissedBox widens a tiny drag (effectively a
                // click on blank space) to a small default box itself.
                var x = Math.Min(dragStartImage.X, imgX);
                var y = Math.Min(dragStartImage.Y, imgY);
                var w = Math.Abs(imgX - dragStartImage.X);
                var h = Math.Abs(imgY - dragStartImage.Y);
                vm.AddMissedBox(x, y, w, h);
                e.Handled = true;
                return;
            }

            // Click on a detector box: toggle the inferred label (accepted -> should-reject, rejected -> wrongly-
            // rejected). The VM re-runs the combined hit-test against the up-point and no-ops if it landed outside.
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
