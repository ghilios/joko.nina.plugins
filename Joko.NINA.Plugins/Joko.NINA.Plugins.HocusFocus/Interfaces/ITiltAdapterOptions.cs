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

    public enum TiltAdjustmentType {
        Screws = 0,
        StepperMotors = 1
    }

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

        // Physical adapter hardware model, used to convert focus deviation into absolute
        // screw turns (or stepper steps). -1 = unset.
        TiltAdjustmentType AdjustmentType { get; set; }      // screws vs stepper motors
        double ThreadPitchMicrons { get; set; }              // axial microns per full screw turn
        double StepperStepSizeMicrons { get; set; }          // axial microns per stepper step
        double ScrewRadiusMillimeters { get; set; }          // screw distance from sensor center

        // Last value the wizard measured from a calibration run; compared against the saved
        // value above to warn when they diverge. -1 = none measured yet.
        double LastMeasuredThreadPitchMicrons { get; set; }
        double LastMeasuredStepperStepSizeMicrons { get; set; }

        // Selected device preset name ("Manual" = user-editable hardware fields). Choosing a preset
        // pre-fills and locks the hardware fields. See TiltAdapterDevicePreset.
        string DeviceName { get; set; }

        // Folder the wizard saves each calibration run's AutoFocus sweeps into when saving is enabled.
        // Persisted (independent of AutoFocusOptions.SavePath) so the location is reused; "" = unset.
        // NOTE: whether saving is ON is a transient per-run toggle on the VM, not persisted here.
        string SaveAFRunsPath { get; set; }
    }
}
