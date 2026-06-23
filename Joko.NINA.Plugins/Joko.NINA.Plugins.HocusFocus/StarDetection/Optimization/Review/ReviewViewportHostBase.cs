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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>Minimal VM contract the shared review viewport host drives (F08): zoom/pan state + image size, a
    /// re-fit signal, a post-change notification so the inverse-zoom marker bindings refresh, and prev/next nav.</summary>
    public interface IViewportHostViewModel {
        StarReviewViewport Viewport { get; }
        double ImageWidth { get; }
        double ImageHeight { get; }
        void NotifyViewportChanged();
        event EventHandler FitRequested;
        ICommand PrevCommand { get; }
        ICommand NextCommand { get; }
    }

    /// <summary>
    /// Single home for the zoom/pan/fit/scroll viewport math shared by the AutoFocus and Aberration-Inspector
    /// "Review Frames" controls (F08). Subclasses supply the named XAML parts (canvas, scrollbars, transforms) and the
    /// bound VM, and may add their own input (e.g. the inspector's hover). Carries the corrected first-fit latch
    /// (latch only on a SUCCESSFUL fit — F19) and the Unloaded FitRequested-unsubscribe (F34), so both controls share
    /// the fixed lifecycle. The part accessors are named *Part to avoid colliding with the same-named x:Name fields
    /// the subclass partials generate.
    /// </summary>
    public abstract class ReviewViewportHostBase : UserControl {

        private bool panning;
        private System.Windows.Point lastPanScreen;
        private bool suppressScrollEvents;
        protected bool hasFitOnce;

        protected abstract Canvas ViewportCanvasPart { get; }
        protected abstract ScrollBar HScrollPart { get; }
        protected abstract ScrollBar VScrollPart { get; }
        protected abstract ScaleTransform ContentScalePart { get; }
        protected abstract TranslateTransform ContentTranslatePart { get; }
        protected abstract IViewportHostViewModel Vm { get; }

        protected ReviewViewportHostBase() {
            DataContextChanged += OnDataContextChanged;
            KeyDown += OnKeyDown;
            Unloaded += OnUnloaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is IViewportHostViewModel oldVm) {
                oldVm.FitRequested -= OnFitRequested;
            }
            if (e.NewValue is IViewportHostViewModel newVm) {
                newVm.FitRequested += OnFitRequested;
            }
        }

        // F34: detach the VM->control FitRequested edge when the control leaves the visual tree, so a long-lived host
        // that re-uses this control across DataContexts (or tears down without a DataContext swap) cannot leak the
        // control through the VM's event. The modal case is unaffected (it unloads on close).
        protected void OnUnloaded(object sender, RoutedEventArgs e) {
            if (Vm != null) {
                Vm.FitRequested -= OnFitRequested;
            }
        }

        protected void OnKeyDown(object sender, KeyEventArgs e) {
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

        protected void OnFitRequested(object sender, EventArgs e) {
            TryFit();
        }

        // F19: returns true only when a fit was actually applied (canvas measured and VM ready), so callers can gate
        // the one-shot hasFitOnce latch on a real fit rather than on a deferred attempt that returned early.
        protected bool TryFit() {
            var vm = Vm;
            if (vm == null || ViewportCanvasPart.ActualWidth <= 0 || vm.ImageWidth <= 0) {
                return false;
            }
            // Collapse the scrollbars and force a layout pass FIRST so the fit is measured against the final
            // (scrollbar-free) viewport — otherwise the image lands off-center until a second Fit.
            HScrollPart.Visibility = Visibility.Collapsed;
            VScrollPart.Visibility = Visibility.Collapsed;
            ViewportCanvasPart.UpdateLayout();
            if (ViewportCanvasPart.ActualWidth <= 0 || ViewportCanvasPart.ActualHeight <= 0) {
                return false;
            }
            vm.Viewport.FitTo(ViewportCanvasPart.ActualWidth, ViewportCanvasPart.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ApplyViewport();
            return true;
        }

        protected void ApplyViewport() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            vm.Viewport.ClampToBounds(ViewportCanvasPart.ActualWidth, ViewportCanvasPart.ActualHeight, vm.ImageWidth, vm.ImageHeight);
            ContentScalePart.ScaleX = vm.Viewport.Scale;
            ContentScalePart.ScaleY = vm.Viewport.Scale;
            ContentTranslatePart.X = vm.Viewport.OffsetX;
            ContentTranslatePart.Y = vm.Viewport.OffsetY;
            UpdateScrollBars();
            // Markers scale with the canvas transform, so refresh the zoom-inverse stroke/label sizes after any change.
            vm.NotifyViewportChanged();
        }

        private void UpdateScrollBars() {
            var vm = Vm;
            if (vm == null) {
                return;
            }
            suppressScrollEvents = true;
            try {
                UpdateScrollBar(HScrollPart, ViewportCanvasPart.ActualWidth, vm.ImageWidth * vm.Viewport.Scale, -vm.Viewport.OffsetX);
                UpdateScrollBar(VScrollPart, ViewportCanvasPart.ActualHeight, vm.ImageHeight * vm.Viewport.Scale, -vm.Viewport.OffsetY);
            } finally {
                suppressScrollEvents = false;
            }
        }

        private static void UpdateScrollBar(ScrollBar bar, double viewport, double content, double scrollPos) {
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

        protected void HScroll_Scroll(object sender, ScrollEventArgs e) {
            var vm = Vm;
            if (vm == null || suppressScrollEvents) {
                return;
            }
            vm.Viewport.Set(vm.Viewport.Scale, -e.NewValue, vm.Viewport.OffsetY);
            ApplyViewport();
        }

        protected void VScroll_Scroll(object sender, ScrollEventArgs e) {
            var vm = Vm;
            if (vm == null || suppressScrollEvents) {
                return;
            }
            vm.Viewport.Set(vm.Viewport.Scale, vm.Viewport.OffsetX, -e.NewValue);
            ApplyViewport();
        }

        // The canvas RenderTransform maps image space -> screen space, so a mouse position taken relative to the
        // canvas's PARENT (the Border) is in screen space.
        protected System.Windows.Point ScreenPoint(MouseEventArgs e) {
            var parent = (UIElement)ViewportCanvasPart.Parent;
            return e.GetPosition(parent);
        }

        protected void ViewportCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
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

        protected void ViewportCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            panning = true;
            lastPanScreen = ScreenPoint(e);
            ViewportCanvasPart.CaptureMouse();
            e.Handled = true;
        }

        protected void ViewportCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e) {
            panning = false;
            ViewportCanvasPart.ReleaseMouseCapture();
            e.Handled = true;
        }

        // Returns true if a pan was consumed, so subclasses with extra move handling (hover) can early-out.
        protected bool TryPan(MouseEventArgs e) {
            if (!panning) {
                return false;
            }
            var screen = ScreenPoint(e);
            var dx = screen.X - lastPanScreen.X;
            var dy = screen.Y - lastPanScreen.Y;
            lastPanScreen = screen;
            Vm?.Viewport.PanBy(dx, dy);
            ApplyViewport();
            return true;
        }

        // Virtual so the inspector subclass can fall through to hover when not panning. AF uses this base behavior.
        protected virtual void ViewportCanvas_MouseMove(object sender, MouseEventArgs e) => TryPan(e);

        protected void ViewportCanvas_SizeChanged(object sender, SizeChangedEventArgs e) {
            // Re-fit only on the first meaningful size (initial layout); afterwards keep the user's zoom/pan. Latch only
            // when a fit actually applies (F19), so a too-early SizeChanged does not permanently suppress the first fit.
            if (hasFitOnce) {
                return;
            }
            if (TryFit()) {
                hasFitOnce = true;
            }
        }
    }
}
