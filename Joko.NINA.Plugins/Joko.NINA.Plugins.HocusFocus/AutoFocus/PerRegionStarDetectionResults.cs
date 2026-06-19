#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Image.ImageAnalysis;
using System.Collections.Concurrent;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    /// <summary>
    /// Thread-safe per-region store of star detection results for a single auto-focus frame.
    ///
    /// A single frame fans out one analysis task per region (7 regions) that all share one
    /// AutoFocusImageState and run concurrently. A previous single shared StarDetectionResult field
    /// was written by one region and read back by another between the write and the read, so the
    /// per-region SubMeasurementPointCompleted event (the Inspector's sensor model) carried a
    /// different region's star list / HFR (HFR misattribution). Keying by region index gives each
    /// region its own slot so no region can clobber another's result.
    ///
    /// This only feeds the SubMeasurementPointCompleted event. The auto-focus curve fit consumes the
    /// pooled MeasureAndError via SubMeasurementsByFocuserPoints instead, so this store has no effect
    /// on AF measurements or fitting.
    /// </summary>
    internal class PerRegionStarDetectionResults {
        private readonly ConcurrentDictionary<int, StarDetectionResult> resultsByRegion = new ConcurrentDictionary<int, StarDetectionResult>();

        public void Set(int regionIndex, StarDetectionResult result) {
            resultsByRegion[regionIndex] = result;
        }

        public StarDetectionResult Get(int regionIndex) {
            return resultsByRegion.TryGetValue(regionIndex, out var result) ? result : null;
        }
    }
}
