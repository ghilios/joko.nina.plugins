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

        // Minimum drag (image px for left, screen px for right) below which a press+release counts as a click, not a
        // drag — so a blank left-click creates no missed box and a right-click deletes a label instead of panning.
        private const double MinDragPx = 3.0;

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool hasFitOnce;

        // Right-button: distinguish a right-CLICK (delete the label under the cursor) from a right-DRAG (pan).
        // rightMoved flips true once the pointer moves past MinDragPx while the right button is held.
        private bool rightMoved;
        private System.Windows.Point rightDownScreen;

        // Left-button interaction state (image-space). pressArmed is set on a valid left-button-down inside the image
        // and consumed on button-up (so a press that started outside the image, or after the VM went away, is ignored).
        // dragging is set only when the press lands on BLANK space (begin a MISSED rubber-band); a press on a detector
        // box leaves dragging false (a click that toggles the inferred label on button-up). dragStart is the down point
        // and the live DragRect tracks the cursor until button-up finalizes the box.
        private bool pressArmed;
        private bool dragging;
        private System.Windows.Point dragStartImage;

        // Drawn-box (missed) move/resize: editingBox is set while a grabbed box is being moved/resized. EdgeHandlePx
        // is the on-screen grab margin around an edge/corner (converted to image pixels via the current zoom).
        private bool editingBox;
        private const double EdgeHandlePx = 7.0;

        private static Cursor CursorForHandle(BoxHandle handle) => handle switch {
            BoxHandle.Inside => Cursors.SizeAll,
            BoxHandle.Left or BoxHandle.Right => Cursors.SizeWE,
            BoxHandle.Top or BoxHandle.Bottom => Cursors.SizeNS,
            BoxHandle.TopLeft or BoxHandle.BottomRight => Cursors.SizeNWSE,
            BoxHandle.TopRight or BoxHandle.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.Arrow
        };

        // Image-pixel grab margin at the current zoom (so the handles are a constant on-screen size).
        private double EdgeHandleImagePx => EdgeHandlePx / Math.Max(StarReviewViewport.MinScale, Vm?.Viewport.Scale ?? 1.0);

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
                case Key.Z:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && vm.UndoCommand.CanExecute(null)) {
                        vm.UndoCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
                case Key.Y:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && vm.RedoCommand.CanExecute(null)) {
                        vm.RedoCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void OnFitRequested(object sender, EventArgs e) {
            var vm = Vm;
            if (vm == null || ViewportCanvas.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                return;
            }
            // A fitted image always ends with the scrollbars HIDDEN, and their disappearance widens/heightens the
            // canvas. If we measured now (while still zoomed in, scrollbars visible) the fit would be computed
            // against the smaller, scrollbar-occupied viewport and the image would land off-center until a second
            // Fit click. So collapse the scrollbars and force a synchronous layout pass FIRST, then measure the
            // final (scrollbar-free) viewport. (ApplyViewport's UpdateScrollBars keeps them hidden since the fitted
            // image is smaller than the viewport, so there is no flip-back.)
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
            // Keep the image within the viewport (bounded pan; centered when smaller than the viewport).
            vm.Viewport.ClampToBounds(ViewportCanvas.ActualWidth, ViewportCanvas.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ContentScale.ScaleX = vm.Viewport.Scale;
            ContentScale.ScaleY = vm.Viewport.Scale;
            ContentTranslate.X = vm.Viewport.OffsetX;
            ContentTranslate.Y = vm.Viewport.OffsetY;
            UpdateScrollBars();
            // Markers scale with the canvas transform, so refresh the zoom-inverse stroke thickness after any
            // viewport change (this covers wheel-zoom, fit, and pan since all route through ApplyViewport).
            vm.NotifyViewportChanged();
        }

        private bool suppressScrollEvents;

        /// <summary>Reflects the current viewport into the two scrollbars (shown only when the scaled image exceeds
        /// the viewport on that axis). Scroll position on each axis is -Offset; the scrollable extent is
        /// image·Scale - viewport.</summary>
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
                bar.ViewportSize = viewport; // proportional thumb
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

            // Editing a DRAWN (missed) box takes priority over labeling: a press on an edge/corner resizes it, a
            // press inside moves it. Only when the press misses every drawn box do we fall through to labeling.
            var (editIdx, editHandle) = vm.HitTestMissedBox(imgX, imgY, EdgeHandleImagePx);
            if (editIdx >= 0 && editHandle != BoxHandle.None) {
                editingBox = true;
                vm.BeginMissedBoxEdit(editIdx, editHandle, imgX, imgY);
                ViewportCanvas.CaptureMouse();
                e.Handled = true;
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
            // Finish an in-progress drawn-box move/resize (commits one undoable edit if the geometry changed).
            if (editingBox) {
                editingBox = false;
                ViewportCanvas.ReleaseMouseCapture();
                vm.EndMissedBoxEdit();
                e.Handled = true;
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
                // Normalize so W,H >= 0 regardless of drag direction.
                var x = Math.Min(dragStartImage.X, imgX);
                var y = Math.Min(dragStartImage.Y, imgY);
                var w = Math.Abs(imgX - dragStartImage.X);
                var h = Math.Abs(imgY - dragStartImage.Y);
                // A plain click on blank space (a negligible drag) must NOT create a box — only a real drag does.
                if (w >= MinDragPx && h >= MinDragPx) {
                    vm.AddMissedBox(x, y, w, h);
                }
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
            rightMoved = false;
            rightDownScreen = ScreenPoint(e);
            lastPanScreen = rightDownScreen;
            ViewportCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void ViewportCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
            panning = false;
            ViewportCanvas.ReleaseMouseCapture();
            e.Handled = true;

            // A right-CLICK (no meaningful drag) deletes the label under the cursor; a right-DRAG was a pan.
            if (rightMoved) {
                return;
            }
            var vm = Vm;
            if (vm == null) {
                return;
            }
            var screen = ScreenPoint(e);
            var (imgX, imgY) = vm.Viewport.ScreenToImage(screen.X, screen.Y);
            if (imgX < 0 || imgY < 0 || imgX > vm.ImageWidth || imgY > vm.ImageHeight) {
                return;
            }
            vm.RemoveLabelAt(imgX, imgY);
        }

        private void ViewportCanvas_MouseMove(object sender, MouseEventArgs e) {
            var vm = Vm;
            if (vm == null) {
                return;
            }

            // Sensor-coordinate readout: show the image pixel under the cursor (cleared when off the image), so a
            // region can be described precisely. Runs on every move, independent of the drag/pan gestures below.
            var cursor = ScreenPoint(e);
            var (cursorX, cursorY) = vm.Viewport.ScreenToImage(cursor.X, cursor.Y);
            vm.CursorPositionText = (cursorX >= 0 && cursorY >= 0 && cursorX <= vm.ImageWidth && cursorY <= vm.ImageHeight)
                ? $"x: {cursorX:F0}, y: {cursorY:F0}"
                : null;

            // An active move/resize of a drawn box owns the gesture.
            if (editingBox) {
                vm.UpdateMissedBoxEdit(cursorX, cursorY);
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
                // Hover feedback: over a drawn (missed) box show the move cursor inside and resize cursors on the
                // edges/corners; otherwise the default arrow.
                var (hoverIdx, hoverHandle) = vm.HitTestMissedBox(cursorX, cursorY, EdgeHandleImagePx);
                ViewportCanvas.Cursor = hoverIdx >= 0 ? CursorForHandle(hoverHandle) : null;
                return;
            }
            var screen = ScreenPoint(e);
            // Once the right-button pointer travels past the threshold it's a pan, not a click-to-delete.
            if (!rightMoved &&
                (Math.Abs(screen.X - rightDownScreen.X) > MinDragPx || Math.Abs(screen.Y - rightDownScreen.Y) > MinDragPx)) {
                rightMoved = true;
            }
            var dx = screen.X - lastPanScreen.X;
            var dy = screen.Y - lastPanScreen.Y;
            lastPanScreen = screen;
            vm.Viewport.PanBy(dx, dy);
            ApplyViewport();
        }

        private void ViewportCanvas_MouseLeave(object sender, MouseEventArgs e) {
            if (Vm != null) {
                Vm.CursorPositionText = null;
            }
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
