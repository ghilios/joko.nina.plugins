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

        protected override Canvas ViewportCanvasPart => ViewportCanvas;
        protected override ScrollBar HScrollPart => HScroll;
        protected override ScrollBar VScrollPart => VScroll;
        protected override ScaleTransform ContentScalePart => ContentScale;
        protected override TranslateTransform ContentTranslatePart => ContentTranslate;
        protected override IViewportHostViewModel Vm => DataContext as IViewportHostViewModel;

        // First-load window sizing (fit-to-screen + deferred re-fit) is shared with the Aberration Inspector review via
        // ReviewViewportHostBase. The manual-AF review keeps the base's default desired size.
        public AutoFocusFrameReviewControl() {
            InitializeComponent();
        }
    }
}
