#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus.Review {

    /// <summary>
    /// Read-only viewer for the manual AutoFocus "Review Frames" dialog. Shares all zoom/pan/fit/scroll plumbing with
    /// the Aberration Inspector's review control via <see cref="ReviewViewportHostBase"/>; adds only the first-load
    /// window sizing. No hover focus graph here.
    /// </summary>
    public partial class AutoFocusFrameReviewControl : ReviewViewportHostBase {

        private bool windowSized;

        protected override Canvas ViewportCanvasPart => ViewportCanvas;
        protected override ScrollBar HScrollPart => HScroll;
        protected override ScrollBar VScrollPart => VScroll;
        protected override ScaleTransform ContentScalePart => ContentScale;
        protected override TranslateTransform ContentTranslatePart => ContentTranslate;
        protected override IViewportHostViewModel Vm => DataContext as IViewportHostViewModel;

        public AutoFocusFrameReviewControl() {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        // Size the host window to FIT the screen on first load (clamped to the work area, centered), with a small
        // minimum so it can be shrunk freely. The WindowService opens the window sized-to-content, which — without this —
        // would honor whatever large size the content asks for and open off-screen on smaller displays.
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
            const double margin = 40.0;

            window.SizeToContent = SizeToContent.Manual;
            window.MinWidth = Math.Min(640.0, work.Width);
            window.MinHeight = Math.Min(440.0, work.Height);
            window.Width = Math.Min(desiredWidth, Math.Max(window.MinWidth, work.Width - margin));
            window.Height = Math.Min(desiredHeight, Math.Max(window.MinHeight, work.Height - margin));
            window.Left = work.Left + (work.Width - window.Width) / 2.0;
            window.Top = work.Top + (work.Height - window.Height) / 2.0;

            // The initial auto-fit (ViewportCanvas_SizeChanged) ran against the pre-resize canvas size, so re-fit
            // against the FINAL window size once the resize layout pass has settled (DispatcherPriority.Loaded runs
            // after layout). Latch hasFitOnce only if that deferred fit actually succeeds (F19).
            Dispatcher.BeginInvoke(
                new Action(() => { if (TryFit()) { hasFitOnce = true; } }),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
