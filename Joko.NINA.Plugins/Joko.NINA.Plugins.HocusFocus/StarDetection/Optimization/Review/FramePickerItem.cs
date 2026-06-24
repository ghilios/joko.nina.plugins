#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>One entry in a review toolbar's frame-number dropdown: the 1-based frame <see cref="Number"/> (the
    /// ComboBox selection value, tracking the current frame) and a human <see cref="Label"/> like "3 — 2745"
    /// (number — focuser position).</summary>
    public sealed class FramePickerItem {
        public FramePickerItem(int number, string label) {
            Number = number;
            Label = label;
        }

        public int Number { get; }
        public string Label { get; }
    }
}
