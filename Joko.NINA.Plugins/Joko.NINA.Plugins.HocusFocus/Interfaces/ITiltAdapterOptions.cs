#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public interface ITiltAdapterOptions : INotifyPropertyChanged {
        int ScrewCount { get; set; }             // 3 or 4
        bool IsCalibrated { get; set; }
        double Screw1AngleDegrees { get; set; }  // clockwise from top in image space
        double Screw2AngleDegrees { get; set; }
        double Screw3AngleDegrees { get; set; }
        double Screw4AngleDegrees { get; set; }  // double.NaN when 3-screw setup
        int CalibratedScrewCount { get; set; }      // screw count at time of last calibration; 0 = never calibrated
        int MeasurementAverageCount { get; set; }
        int ScrewInwardCurvatureSign { get; set; } // +1 or -1; 0 = not yet calibrated
    }
}
