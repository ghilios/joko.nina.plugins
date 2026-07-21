#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public enum TiltAdjustmentType {
        Screws = 0,
        StepperMotors = 1
    }

    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum TiltGuidanceAngleUnit {

        [Description("Turns")]
        Turns = 0,

        [Description("Degrees")]
        Degrees = 1,

        [Description("Minutes")]
        Minutes = 2
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
        // Sign of the curvature/backfocus response to a CW ("inward") screw turn: +1 = CW turns
        // raise the curvature effect, -1 = lower it. Defaults to the assumed mechanical direction
        // (TiltScrewGeometry.DefaultScrewInwardCurvatureSign); a 6-step wizard run measures it.
        int ScrewInwardCurvatureSign { get; set; }

        // True only when a wizard run measured ScrewInwardCurvatureSign (Baseline -> AllInward
        // steps). Cleared when the user edits the direction manually or applies a manual entry.
        bool ScrewInwardCurvatureSignIsMeasured { get; set; }

        // Include the 2 curvature-direction steps (Baseline + AllInward) in a calibration run:
        // 6 steps instead of 4. Off by default — the assumed/manual direction is used instead.
        bool MeasureCurvatureDuringCalibration { get; set; }

        // True when the current calibration came from Manual Calibration Entry, not a wizard run.
        bool CalibrationIsManual { get; set; }

        // Physical adapter hardware model, used to convert focus deviation into absolute
        // screw turns (or stepper steps). -1 = unset.
        TiltAdjustmentType AdjustmentType { get; set; }      // screws vs stepper motors

        // Display unit for the Tilt Adapter Guidance numeric amounts on screw adapters:
        // Turns (default), Degrees (1 turn = 360°), or Minutes (1 turn = 60 min). Ignored for
        // stepper adapters (always whole steps).
        TiltGuidanceAngleUnit AngleDisplayUnit { get; set; }

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

        // --- Motorized tilt-adapter device connection ---

        // Serial port name the tilt-adapter device connects over (e.g. "COM3"). "" = unset.
        string TiltDeviceSerialPortName { get; set; }

        // Safety cap on the magnitude of a single commanded move, in device steps. Guards against
        // sending an oversized command to the hardware in one shot.
        int TiltDeviceMaxStepsPerCommand { get; set; }

        // Safety cap on the total cumulative excursion (in device steps) a motor may travel from its
        // shadow-tracked home position before automation refuses to move it further.
        int TiltDeviceMaxExcursionSteps { get; set; }

        // Seconds to wait after a commanded move before treating the device as settled (e.g. before
        // polling position or issuing the next command).
        double TiltDeviceSettleSeconds { get; set; }

        // [CRITICAL AUTOMATION GATE] The device preset name (see TiltAdapterDevicePreset) the CURRENT
        // stored calibration is linked to. "" means the calibration is NOT device-linked (e.g. it came
        // from manual screw entry, ApplyManualCalibration, or a replay), and hands-off device automation
        // (the wizard's auto-apply and the inspector's Automatic Adjustment button) MUST be blocked: a
        // pre-existing manual calibration's screw numbering/orientation may not match how the device's
        // motors are wired, and applying corrections against the wrong mapping would worsen tilt
        // unattended. Only a completed hands-off (connected-device) calibration run may set this; manual
        // entry, replay-driven recalculation, and disconnected runs must clear it back to "".
        string DeviceLinkedCalibrationDeviceName { get; set; }

        // [CRITICAL AUTOMATION GATE] True only when the CURRENT stored calibration's own confidence/quality
        // result (TiltCalibrationCalculator.ComputeConfidence's IsReliable — signal-to-noise across the
        // per-step tilt vectors) passed. False for a noise-dominated calibration (SNR below
        // TiltCalibrationCalculator.MinReliableSignalToNoise), a manual entry (ApplyManualCalibration has no
        // confidence to evaluate), or any completion where reliability could not be established. Automation
        // (the inspector's Automatic Adjustment, T14, and any future wizard auto-apply) MUST be blocked
        // whenever this is false, even when the calibration IS device-linked: a device-linked-but-unreliable
        // calibration is safe to review but not safe to trust unattended. Written at every calibration-math
        // completion (RunCalibrationMath and ApplyManualCalibration — the same places that set/clear
        // DeviceLinkedCalibrationDeviceName above); conservative by default (false) whenever reliability
        // cannot be established. Internal state marker, not a user setting — it needs NO UI control, exactly
        // like DeviceLinkedCalibrationDeviceName above (also intentionally absent from the options UI).
        bool CalibrationIsReliable { get; set; }

        // Opaque serialized per-motor cumulative step counters + a validity flag, tracking each motor's
        // position relative to its last-known home/reference so absolute travel limits can be enforced
        // across app restarts. "" = no shadow position recorded yet. The serialization format is defined
        // and consumed by the device controller; this option only stores/round-trips the raw string.
        string TiltDeviceShadowPositions { get; set; }

        // Amount the user (or automation) moves each screw/motor during the per-screw calibration steps
        // (full turns for screws, steps for steppers). -1 = unset, resolved to the selected device
        // preset's default (TiltAdapterDevicePreset.DefaultCalibrationAmount) — still user-editable
        // afterward.
        double CalibrationAppliedAmount { get; set; }
    }
}
