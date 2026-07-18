#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    public class TiltAdapterOptions : BaseINPC, ITiltAdapterOptions {
        private readonly IPluginOptionsAccessor optionsAccessor;

        public TiltAdapterOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal TiltAdapterOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        private void InitializeOptions() {
            screwCount = optionsAccessor.GetValueInt32(nameof(ScrewCount), 3);
            isCalibrated = optionsAccessor.GetValueBoolean(nameof(IsCalibrated), false);
            screw1AngleDegrees = optionsAccessor.GetValueDouble(nameof(Screw1AngleDegrees), double.NaN);
            screw2AngleDegrees = optionsAccessor.GetValueDouble(nameof(Screw2AngleDegrees), double.NaN);
            screw3AngleDegrees = optionsAccessor.GetValueDouble(nameof(Screw3AngleDegrees), double.NaN);
            screw4AngleDegrees = optionsAccessor.GetValueDouble(nameof(Screw4AngleDegrees), double.NaN);
            calibratedScrewCount = optionsAccessor.GetValueInt32(nameof(CalibratedScrewCount), 0);
            measurementAverageCount = optionsAccessor.GetValueInt32(nameof(MeasurementAverageCount), 1);
            screwInwardCurvatureSign = optionsAccessor.GetValueInt32(nameof(ScrewInwardCurvatureSign), TiltScrewGeometry.DefaultScrewInwardCurvatureSign);
            screwInwardCurvatureSignIsMeasured = optionsAccessor.GetValueBoolean(nameof(ScrewInwardCurvatureSignIsMeasured), false);
            measureCurvatureDuringCalibration = optionsAccessor.GetValueBoolean(nameof(MeasureCurvatureDuringCalibration), false);
            calibrationIsManual = optionsAccessor.GetValueBoolean(nameof(CalibrationIsManual), false);
            adjustmentType = optionsAccessor.GetValueEnum(nameof(AdjustmentType), TiltAdjustmentType.Screws);
            angleDisplayUnit = optionsAccessor.GetValueEnum(nameof(AngleDisplayUnit), TiltGuidanceAngleUnit.Turns);
            threadPitchMicrons = optionsAccessor.GetValueDouble(nameof(ThreadPitchMicrons), -1.0);
            stepperStepSizeMicrons = optionsAccessor.GetValueDouble(nameof(StepperStepSizeMicrons), -1.0);
            screwRadiusMillimeters = optionsAccessor.GetValueDouble(nameof(ScrewRadiusMillimeters), -1.0);
            lastMeasuredThreadPitchMicrons = optionsAccessor.GetValueDouble(nameof(LastMeasuredThreadPitchMicrons), -1.0);
            lastMeasuredStepperStepSizeMicrons = optionsAccessor.GetValueDouble(nameof(LastMeasuredStepperStepSizeMicrons), -1.0);
            deviceName = optionsAccessor.GetValueString(nameof(DeviceName), TiltAdapterDevicePreset.ManualName);
            saveAFRunsPath = optionsAccessor.GetValueString(nameof(SaveAFRunsPath), string.Empty);
            tiltDeviceSerialPortName = optionsAccessor.GetValueString(nameof(TiltDeviceSerialPortName), string.Empty);
            tiltDeviceMaxStepsPerCommand = optionsAccessor.GetValueInt32(nameof(TiltDeviceMaxStepsPerCommand), 200);
            // The EAT's step counters are absolute and EEPROM-persisted (never zeroed by the plugin), so they
            // carry over between sessions and sit wherever prior adjustments left them. Automation confines
            // them to [0, max]: travel below 0 is disallowed outright, so this is the UPPER bound only and
            // must comfortably exceed wherever the motors already are plus the working travel. Since a tilt
            // correction is differential, one near zero gets an automatic upward backfocus bias first, which
            // consumes headroom under this cap. See docs/asg-eat-serial-protocol-design.md.
            tiltDeviceMaxExcursionSteps = optionsAccessor.GetValueInt32(nameof(TiltDeviceMaxExcursionSteps), 2000);
            tiltDeviceSettleSeconds = optionsAccessor.GetValueDouble(nameof(TiltDeviceSettleSeconds), 3.0);
            deviceLinkedCalibrationDeviceName = optionsAccessor.GetValueString(nameof(DeviceLinkedCalibrationDeviceName), string.Empty);
            calibrationIsReliable = optionsAccessor.GetValueBoolean(nameof(CalibrationIsReliable), false);
            tiltDeviceShadowPositions = optionsAccessor.GetValueString(nameof(TiltDeviceShadowPositions), string.Empty);
            calibrationAppliedAmount = optionsAccessor.GetValueDouble(nameof(CalibrationAppliedAmount), -1.0);
        }

        private int screwCount;

        public int ScrewCount {
            get => screwCount;
            set {
                if (screwCount != value) {
                    screwCount = value;
                    optionsAccessor.SetValueInt32(nameof(ScrewCount), screwCount);
                    RaisePropertyChanged();
                }
            }
        }

        private bool isCalibrated;

        public bool IsCalibrated {
            get => isCalibrated;
            set {
                if (isCalibrated != value) {
                    isCalibrated = value;
                    optionsAccessor.SetValueBoolean(nameof(IsCalibrated), isCalibrated);
                    RaisePropertyChanged();
                }
            }
        }

        private double screw1AngleDegrees;

        public double Screw1AngleDegrees {
            get => screw1AngleDegrees;
            set {
                if (screw1AngleDegrees != value) {
                    screw1AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(Screw1AngleDegrees), screw1AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double screw2AngleDegrees;

        public double Screw2AngleDegrees {
            get => screw2AngleDegrees;
            set {
                if (screw2AngleDegrees != value) {
                    screw2AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(Screw2AngleDegrees), screw2AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double screw3AngleDegrees;

        public double Screw3AngleDegrees {
            get => screw3AngleDegrees;
            set {
                if (screw3AngleDegrees != value) {
                    screw3AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(Screw3AngleDegrees), screw3AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double screw4AngleDegrees;

        public double Screw4AngleDegrees {
            get => screw4AngleDegrees;
            set {
                if (screw4AngleDegrees != value) {
                    screw4AngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(Screw4AngleDegrees), screw4AngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private int calibratedScrewCount;

        public int CalibratedScrewCount {
            get => calibratedScrewCount;
            set {
                if (calibratedScrewCount != value) {
                    calibratedScrewCount = value;
                    optionsAccessor.SetValueInt32(nameof(CalibratedScrewCount), calibratedScrewCount);
                    RaisePropertyChanged();
                }
            }
        }

        private int measurementAverageCount;

        public int MeasurementAverageCount {
            get => measurementAverageCount;
            set {
                if (measurementAverageCount != value) {
                    measurementAverageCount = value;
                    optionsAccessor.SetValueInt32(nameof(MeasurementAverageCount), measurementAverageCount);
                    RaisePropertyChanged();
                }
            }
        }

        private int screwInwardCurvatureSign;

        public int ScrewInwardCurvatureSign {
            get => screwInwardCurvatureSign;
            set {
                if (screwInwardCurvatureSign != value) {
                    screwInwardCurvatureSign = value;
                    optionsAccessor.SetValueInt32(nameof(ScrewInwardCurvatureSign), screwInwardCurvatureSign);
                    RaisePropertyChanged();
                }
            }
        }

        private bool screwInwardCurvatureSignIsMeasured;

        public bool ScrewInwardCurvatureSignIsMeasured {
            get => screwInwardCurvatureSignIsMeasured;
            set {
                if (screwInwardCurvatureSignIsMeasured != value) {
                    screwInwardCurvatureSignIsMeasured = value;
                    optionsAccessor.SetValueBoolean(nameof(ScrewInwardCurvatureSignIsMeasured), screwInwardCurvatureSignIsMeasured);
                    RaisePropertyChanged();
                }
            }
        }

        private bool measureCurvatureDuringCalibration;

        public bool MeasureCurvatureDuringCalibration {
            get => measureCurvatureDuringCalibration;
            set {
                if (measureCurvatureDuringCalibration != value) {
                    measureCurvatureDuringCalibration = value;
                    optionsAccessor.SetValueBoolean(nameof(MeasureCurvatureDuringCalibration), measureCurvatureDuringCalibration);
                    RaisePropertyChanged();
                }
            }
        }

        private bool calibrationIsManual;

        public bool CalibrationIsManual {
            get => calibrationIsManual;
            set {
                if (calibrationIsManual != value) {
                    calibrationIsManual = value;
                    optionsAccessor.SetValueBoolean(nameof(CalibrationIsManual), calibrationIsManual);
                    RaisePropertyChanged();
                }
            }
        }

        private TiltAdjustmentType adjustmentType;

        public TiltAdjustmentType AdjustmentType {
            get => adjustmentType;
            set {
                if (adjustmentType != value) {
                    adjustmentType = value;
                    optionsAccessor.SetValueEnum(nameof(AdjustmentType), adjustmentType);
                    RaisePropertyChanged();
                }
            }
        }

        private TiltGuidanceAngleUnit angleDisplayUnit;

        public TiltGuidanceAngleUnit AngleDisplayUnit {
            get => angleDisplayUnit;
            set {
                if (angleDisplayUnit != value) {
                    angleDisplayUnit = value;
                    optionsAccessor.SetValueEnum(nameof(AngleDisplayUnit), angleDisplayUnit);
                    RaisePropertyChanged();
                }
            }
        }

        private double threadPitchMicrons;

        public double ThreadPitchMicrons {
            get => threadPitchMicrons;
            set {
                if (threadPitchMicrons != value) {
                    threadPitchMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(ThreadPitchMicrons), threadPitchMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double stepperStepSizeMicrons;

        public double StepperStepSizeMicrons {
            get => stepperStepSizeMicrons;
            set {
                if (stepperStepSizeMicrons != value) {
                    stepperStepSizeMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(StepperStepSizeMicrons), stepperStepSizeMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double screwRadiusMillimeters;

        public double ScrewRadiusMillimeters {
            get => screwRadiusMillimeters;
            set {
                if (screwRadiusMillimeters != value) {
                    screwRadiusMillimeters = value;
                    optionsAccessor.SetValueDouble(nameof(ScrewRadiusMillimeters), screwRadiusMillimeters);
                    RaisePropertyChanged();
                }
            }
        }

        private double lastMeasuredThreadPitchMicrons;

        public double LastMeasuredThreadPitchMicrons {
            get => lastMeasuredThreadPitchMicrons;
            set {
                if (lastMeasuredThreadPitchMicrons != value) {
                    lastMeasuredThreadPitchMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(LastMeasuredThreadPitchMicrons), lastMeasuredThreadPitchMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double lastMeasuredStepperStepSizeMicrons;

        public double LastMeasuredStepperStepSizeMicrons {
            get => lastMeasuredStepperStepSizeMicrons;
            set {
                if (lastMeasuredStepperStepSizeMicrons != value) {
                    lastMeasuredStepperStepSizeMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(LastMeasuredStepperStepSizeMicrons), lastMeasuredStepperStepSizeMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private string deviceName;

        public string DeviceName {
            get => deviceName;
            set {
                if (deviceName != value) {
                    deviceName = value;
                    optionsAccessor.SetValueString(nameof(DeviceName), deviceName);
                    RaisePropertyChanged();
                }
            }
        }

        private string saveAFRunsPath;

        public string SaveAFRunsPath {
            get => saveAFRunsPath;
            set {
                var newValue = value ?? string.Empty;
                if (saveAFRunsPath != newValue) {
                    saveAFRunsPath = newValue;
                    optionsAccessor.SetValueString(nameof(SaveAFRunsPath), saveAFRunsPath);
                    RaisePropertyChanged();
                }
            }
        }

        private string tiltDeviceSerialPortName;

        public string TiltDeviceSerialPortName {
            get => tiltDeviceSerialPortName;
            set {
                if (tiltDeviceSerialPortName != value) {
                    tiltDeviceSerialPortName = value;
                    optionsAccessor.SetValueString(nameof(TiltDeviceSerialPortName), tiltDeviceSerialPortName);
                    RaisePropertyChanged();
                }
            }
        }

        private int tiltDeviceMaxStepsPerCommand;

        public int TiltDeviceMaxStepsPerCommand {
            get => tiltDeviceMaxStepsPerCommand;
            set {
                if (tiltDeviceMaxStepsPerCommand != value) {
                    tiltDeviceMaxStepsPerCommand = value;
                    optionsAccessor.SetValueInt32(nameof(TiltDeviceMaxStepsPerCommand), tiltDeviceMaxStepsPerCommand);
                    RaisePropertyChanged();
                }
            }
        }

        private int tiltDeviceMaxExcursionSteps;

        public int TiltDeviceMaxExcursionSteps {
            get => tiltDeviceMaxExcursionSteps;
            set {
                if (tiltDeviceMaxExcursionSteps != value) {
                    tiltDeviceMaxExcursionSteps = value;
                    optionsAccessor.SetValueInt32(nameof(TiltDeviceMaxExcursionSteps), tiltDeviceMaxExcursionSteps);
                    RaisePropertyChanged();
                }
            }
        }

        private double tiltDeviceSettleSeconds;

        public double TiltDeviceSettleSeconds {
            get => tiltDeviceSettleSeconds;
            set {
                if (tiltDeviceSettleSeconds != value) {
                    tiltDeviceSettleSeconds = value;
                    optionsAccessor.SetValueDouble(nameof(TiltDeviceSettleSeconds), tiltDeviceSettleSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private string deviceLinkedCalibrationDeviceName;

        public string DeviceLinkedCalibrationDeviceName {
            get => deviceLinkedCalibrationDeviceName;
            set {
                if (deviceLinkedCalibrationDeviceName != value) {
                    deviceLinkedCalibrationDeviceName = value;
                    optionsAccessor.SetValueString(nameof(DeviceLinkedCalibrationDeviceName), deviceLinkedCalibrationDeviceName);
                    RaisePropertyChanged();
                }
            }
        }

        private bool calibrationIsReliable;

        public bool CalibrationIsReliable {
            get => calibrationIsReliable;
            set {
                if (calibrationIsReliable != value) {
                    calibrationIsReliable = value;
                    optionsAccessor.SetValueBoolean(nameof(CalibrationIsReliable), calibrationIsReliable);
                    RaisePropertyChanged();
                }
            }
        }

        private string tiltDeviceShadowPositions;

        public string TiltDeviceShadowPositions {
            get => tiltDeviceShadowPositions;
            set {
                if (tiltDeviceShadowPositions != value) {
                    tiltDeviceShadowPositions = value;
                    optionsAccessor.SetValueString(nameof(TiltDeviceShadowPositions), tiltDeviceShadowPositions);
                    RaisePropertyChanged();
                }
            }
        }

        private double calibrationAppliedAmount;

        public double CalibrationAppliedAmount {
            get => calibrationAppliedAmount;
            set {
                if (calibrationAppliedAmount != value) {
                    calibrationAppliedAmount = value;
                    optionsAccessor.SetValueDouble(nameof(CalibrationAppliedAmount), calibrationAppliedAmount);
                    RaisePropertyChanged();
                }
            }
        }
    }
}
