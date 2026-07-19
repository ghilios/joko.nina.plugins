#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    /// <summary>
    /// Thrown when per-filter star detection is enabled but the capture-time filter of an image cannot be
    /// determined (no filter wheel metadata). The NINA-facing Detect entry point converts this into a
    /// soft-fail (empty result + warning notification); HocusFocus-owned entry points gate up front instead,
    /// and typed callers let it propagate to their existing failure handling.
    /// </summary>
    public class PerFilterSettingsUnavailableException : Exception {

        public PerFilterSettingsUnavailableException(string message) : base(message) {
        }
    }
}
