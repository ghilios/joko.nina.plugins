#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Windows;

namespace TestApp.StarReview {

    /// <summary>
    /// Thin TestApp host window for the plugin's reusable <c>StarReviewControl</c>. All the review UI (canvas,
    /// overlays, drag-rect, mouse/keyboard handlers) and the VM now live in the plugin so the in-NINA wizard can
    /// reuse them; this window just gives the dev tool a top-level frame. The runner sets <see cref="Window.DataContext"/>
    /// to the plugin <c>StarReviewVM</c> (the control inherits it) and wires <c>Closing -> vm.SaveAll()</c>.
    /// </summary>
    public partial class StarReviewWindow : Window {

        public StarReviewWindow() {
            InitializeComponent();
        }
    }
}
