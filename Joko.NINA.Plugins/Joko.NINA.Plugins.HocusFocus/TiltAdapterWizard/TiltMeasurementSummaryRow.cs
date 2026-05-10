#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    public class TiltMeasurementSummaryRow {
        public int RunNumber { get; set; }          // 1-based; 0 = average row
        public double Direction { get; set; }       // clockwise-from-top angle in degrees: atan2(A, -B)
        public double TiltAngleDeg { get; set; } = double.NaN;  // geometric sensor tilt angle in degrees
        public bool IsAverage { get; set; }
        public string StepDescription { get; set; } = string.Empty;
        public string Label => IsAverage ? "Avg" : $"Run {RunNumber}";
        public string TiltAngleDisplay => double.IsNaN(TiltAngleDeg) ? "N/A" : $"{TiltAngleDeg:F3}°";
    }
}
