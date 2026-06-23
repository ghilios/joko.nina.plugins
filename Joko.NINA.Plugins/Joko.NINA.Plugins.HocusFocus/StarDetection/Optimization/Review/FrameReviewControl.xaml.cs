#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// Read-only viewer for the Aberration Inspector "Review Frames" dialog. Shares all zoom/pan/fit/scroll plumbing
    /// with the AutoFocus review control via <see cref="ReviewViewportHostBase"/>; adds only the hover focus-graph.
    /// </summary>
    public partial class FrameReviewControl : ReviewViewportHostBase {

        private FrameReviewVM HoverVm => DataContext as FrameReviewVM;

        protected override Canvas ViewportCanvasPart => ViewportCanvas;
        protected override ScrollBar HScrollPart => HScroll;
        protected override ScrollBar VScrollPart => VScroll;
        protected override ScaleTransform ContentScalePart => ContentScale;
        protected override TranslateTransform ContentTranslatePart => ContentTranslate;
        protected override IViewportHostViewModel Vm => DataContext as IViewportHostViewModel;

        public FrameReviewControl() {
            InitializeComponent();
        }

        // Pan via the base; otherwise update the hover focus graph (inspector-only). Wired in XAML as
        // MouseMove="ViewportCanvas_MouseMove" — virtual dispatch routes to this override.
        protected override void ViewportCanvas_MouseMove(object sender, MouseEventArgs e) {
            if (TryPan(e)) {
                return;
            }
            UpdateHover(e);
        }

        // Show the focus graph for any matched star under the cursor (smallest box wins); clear it otherwise.
        // Boxes are in raw image coords (matching the displayed raw frame), so the cursor maps via the viewport directly.
        private void UpdateHover(MouseEventArgs e) {
            var vm = HoverVm;
            if (vm == null) {
                return;
            }
            var screen = ScreenPoint(e);
            var (imgX, imgY) = vm.Viewport.ScreenToImage(screen.X, screen.Y);
            FrameReviewMarker best = null;
            double bestArea = double.MaxValue;
            foreach (var m in vm.Markers) {
                if (!m.CanShowFocusGraph || m.RegistrationId == null) {
                    continue;
                }
                if (imgX >= m.BoxX && imgY >= m.BoxY && imgX <= m.BoxX + m.BoxWidth && imgY <= m.BoxY + m.BoxHeight) {
                    var area = m.BoxWidth * m.BoxHeight;
                    if (area < bestArea) {
                        bestArea = area;
                        best = m;
                    }
                }
            }
            if (best != null) {
                vm.SetHover(best.RegistrationId.Value);
            } else {
                vm.ClearHover();
            }
        }

        private void ViewportCanvas_MouseLeave(object sender, MouseEventArgs e) => HoverVm?.ClearHover();
    }
}
