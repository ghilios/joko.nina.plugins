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
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.IO;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator {

    /// <summary>
    /// Persisted options for the synthetic star-field camera, modeled on <see cref="InspectorOptions"/>:
    /// a public constructor for production (backed by a real <see cref="PluginOptionsAccessor"/>) and an
    /// internal constructor for tests (injected accessor). Defaults come from the design's config table.
    /// </summary>
    public class CameraSimulatorOptions : BaseINPC, ICameraSimulatorOptions {
        private readonly IPluginOptionsAccessor optionsAccessor;

        public CameraSimulatorOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal CameraSimulatorOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }

        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(CameraSimulatorOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }

        /// <summary>The default ASTAP database directory: <c>%ProgramFiles%\astap</c>.</summary>
        public static string DefaultAstapCatalogPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "astap");

        /// <summary>Clamp a raw gain into the usable <c>[0, MaxGain]</c> range of the given sensor.</summary>
        private static int ClampGain(int value, SonySensorModel model) {
            return Math.Clamp(value, 0, SensorRegistry.Get(model).MaxGain);
        }

        private void InitializeOptions() {
            optimalFocuserPosition = optionsAccessor.GetValueInt32(nameof(OptimalFocuserPosition), 5000);
            focuserStepSizeMicrons = optionsAccessor.GetValueDouble(nameof(FocuserStepSizeMicrons), 2.0);
            apertureMillimeters = optionsAccessor.GetValueDouble(nameof(ApertureMillimeters), 100.0);
            focalLengthMillimeters = optionsAccessor.GetValueDouble(nameof(FocalLengthMillimeters), 0.0);
            centralObstructionEnabled = optionsAccessor.GetValueBoolean(nameof(CentralObstructionEnabled), true);
            centralObstructionFraction = optionsAccessor.GetValueDouble(nameof(CentralObstructionFraction), 0.3);
            opticalThroughput = optionsAccessor.GetValueDouble(nameof(OpticalThroughput), 0.85);
            sensorModel = optionsAccessor.GetValueEnum(nameof(SensorModel), SonySensorModel.IMX455);
            // Heal a stored gain that exceeds the loaded sensor's usable range (e.g. after switching sensors in an
            // older build). SensorModel is loaded first so the clamp uses the correct sensor.
            gain = ClampGain(optionsAccessor.GetValueInt32(nameof(Gain), 100), sensorModel);
            biasPedestalAdu = optionsAccessor.GetValueInt32(nameof(BiasPedestalAdu), 500);
            sensorTemperatureCelsius = optionsAccessor.GetValueDouble(nameof(SensorTemperatureCelsius), -10.0);
            filter = optionsAccessor.GetValueEnum(nameof(Filter), SimulatorFilter.L);
            skyBrightnessMagPerArcsec2 = optionsAccessor.GetValueDouble(nameof(SkyBrightnessMagPerArcsec2), 20.5);
            seeingArcsec = optionsAccessor.GetValueDouble(nameof(SeeingArcsec), 2.5);
            astapCatalogPath = optionsAccessor.GetValueString(nameof(AstapCatalogPath), DefaultAstapCatalogPath);
            limitingMagnitude = optionsAccessor.GetValueDouble(nameof(LimitingMagnitude), 16.0);
            rotationDegrees = optionsAccessor.GetValueDouble(nameof(RotationDegrees), 0.0);
            noiseSeed = optionsAccessor.GetValueInt32(nameof(NoiseSeed), 42);
            enableAberrations = optionsAccessor.GetValueBoolean(nameof(EnableAberrations), false);
            tiltAngleDegrees = optionsAccessor.GetValueDouble(nameof(TiltAngleDegrees), 0.0);
            tiltAmountMicrons = optionsAccessor.GetValueDouble(nameof(TiltAmountMicrons), 0.0);
            backfocusErrorMicrons = optionsAccessor.GetValueDouble(nameof(BackfocusErrorMicrons), 0.0);
            opticalAxisOffsetXMicrons = optionsAccessor.GetValueDouble(nameof(OpticalAxisOffsetXMicrons), 0.0);
            opticalAxisOffsetYMicrons = optionsAccessor.GetValueDouble(nameof(OpticalAxisOffsetYMicrons), 0.0);
        }

        public void ResetDefaults() {
            OptimalFocuserPosition = 5000;
            FocuserStepSizeMicrons = 2.0;
            ApertureMillimeters = 100.0;
            FocalLengthMillimeters = 0.0;
            CentralObstructionEnabled = true;
            CentralObstructionFraction = 0.3;
            OpticalThroughput = 0.85;
            SensorModel = SonySensorModel.IMX455;
            Gain = 100;
            BiasPedestalAdu = 500;
            SensorTemperatureCelsius = -10.0;
            Filter = SimulatorFilter.L;
            SkyBrightnessMagPerArcsec2 = 20.5;
            SeeingArcsec = 2.5;
            AstapCatalogPath = DefaultAstapCatalogPath;
            LimitingMagnitude = 16.0;
            RotationDegrees = 0.0;
            NoiseSeed = 42;
            EnableAberrations = false;
            TiltAngleDegrees = 0.0;
            TiltAmountMicrons = 0.0;
            BackfocusErrorMicrons = 0.0;
            OpticalAxisOffsetXMicrons = 0.0;
            OpticalAxisOffsetYMicrons = 0.0;
        }

        private int optimalFocuserPosition;

        public int OptimalFocuserPosition {
            get => optimalFocuserPosition;
            set {
                if (optimalFocuserPosition != value) {
                    optimalFocuserPosition = value;
                    optionsAccessor.SetValueInt32(nameof(OptimalFocuserPosition), optimalFocuserPosition);
                    RaisePropertyChanged();
                }
            }
        }

        private double focuserStepSizeMicrons;

        public double FocuserStepSizeMicrons {
            get => focuserStepSizeMicrons;
            set {
                if (focuserStepSizeMicrons != value) {
                    focuserStepSizeMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(FocuserStepSizeMicrons), focuserStepSizeMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double apertureMillimeters;

        public double ApertureMillimeters {
            get => apertureMillimeters;
            set {
                if (apertureMillimeters != value) {
                    apertureMillimeters = value;
                    optionsAccessor.SetValueDouble(nameof(ApertureMillimeters), apertureMillimeters);
                    RaisePropertyChanged();
                }
            }
        }

        private double focalLengthMillimeters;

        public double FocalLengthMillimeters {
            get => focalLengthMillimeters;
            set {
                if (focalLengthMillimeters != value) {
                    focalLengthMillimeters = value;
                    optionsAccessor.SetValueDouble(nameof(FocalLengthMillimeters), focalLengthMillimeters);
                    RaisePropertyChanged();
                }
            }
        }

        private bool centralObstructionEnabled;

        public bool CentralObstructionEnabled {
            get => centralObstructionEnabled;
            set {
                if (centralObstructionEnabled != value) {
                    centralObstructionEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(CentralObstructionEnabled), centralObstructionEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private double centralObstructionFraction;

        public double CentralObstructionFraction {
            get => centralObstructionFraction;
            set {
                if (centralObstructionFraction != value) {
                    centralObstructionFraction = value;
                    optionsAccessor.SetValueDouble(nameof(CentralObstructionFraction), centralObstructionFraction);
                    RaisePropertyChanged();
                }
            }
        }

        private double opticalThroughput;

        public double OpticalThroughput {
            get => opticalThroughput;
            set {
                if (opticalThroughput != value) {
                    opticalThroughput = value;
                    optionsAccessor.SetValueDouble(nameof(OpticalThroughput), opticalThroughput);
                    RaisePropertyChanged();
                }
            }
        }

        private SonySensorModel sensorModel;

        public SonySensorModel SensorModel {
            get => sensorModel;
            set {
                if (sensorModel != value) {
                    sensorModel = value;
                    optionsAccessor.SetValueEnum(nameof(SensorModel), sensorModel);
                    RaisePropertyChanged();
                    // Different sensors have different MaxGain (e.g. IMX455=300, IMX294=400). Re-clamp the current
                    // gain into the new sensor's range so it can never exceed the selected sensor's usable maximum.
                    var reclampedGain = ClampGain(gain, sensorModel);
                    if (gain != reclampedGain) {
                        gain = reclampedGain;
                        optionsAccessor.SetValueInt32(nameof(Gain), gain);
                        RaisePropertyChanged(nameof(Gain));
                    }
                }
            }
        }

        private int gain;

        public int Gain {
            get => gain;
            set {
                var clamped = ClampGain(value, sensorModel);
                if (gain != clamped) {
                    gain = clamped;
                    optionsAccessor.SetValueInt32(nameof(Gain), gain);
                    RaisePropertyChanged();
                }
            }
        }

        private int biasPedestalAdu;

        public int BiasPedestalAdu {
            get => biasPedestalAdu;
            set {
                if (biasPedestalAdu != value) {
                    biasPedestalAdu = value;
                    optionsAccessor.SetValueInt32(nameof(BiasPedestalAdu), biasPedestalAdu);
                    RaisePropertyChanged();
                }
            }
        }

        private double sensorTemperatureCelsius;

        public double SensorTemperatureCelsius {
            get => sensorTemperatureCelsius;
            set {
                if (sensorTemperatureCelsius != value) {
                    sensorTemperatureCelsius = value;
                    optionsAccessor.SetValueDouble(nameof(SensorTemperatureCelsius), sensorTemperatureCelsius);
                    RaisePropertyChanged();
                }
            }
        }

        private SimulatorFilter filter;

        public SimulatorFilter Filter {
            get => filter;
            set {
                if (filter != value) {
                    filter = value;
                    optionsAccessor.SetValueEnum(nameof(Filter), filter);
                    RaisePropertyChanged();
                }
            }
        }

        private double skyBrightnessMagPerArcsec2;

        public double SkyBrightnessMagPerArcsec2 {
            get => skyBrightnessMagPerArcsec2;
            set {
                if (skyBrightnessMagPerArcsec2 != value) {
                    skyBrightnessMagPerArcsec2 = value;
                    optionsAccessor.SetValueDouble(nameof(SkyBrightnessMagPerArcsec2), skyBrightnessMagPerArcsec2);
                    RaisePropertyChanged();
                }
            }
        }

        private double seeingArcsec;

        public double SeeingArcsec {
            get => seeingArcsec;
            set {
                if (seeingArcsec != value) {
                    seeingArcsec = value;
                    optionsAccessor.SetValueDouble(nameof(SeeingArcsec), seeingArcsec);
                    RaisePropertyChanged();
                }
            }
        }

        private string astapCatalogPath;

        public string AstapCatalogPath {
            get => astapCatalogPath;
            set {
                if (astapCatalogPath != value) {
                    astapCatalogPath = value;
                    optionsAccessor.SetValueString(nameof(AstapCatalogPath), astapCatalogPath);
                    RaisePropertyChanged();
                }
            }
        }

        private double limitingMagnitude;

        public double LimitingMagnitude {
            get => limitingMagnitude;
            set {
                if (limitingMagnitude != value) {
                    limitingMagnitude = value;
                    optionsAccessor.SetValueDouble(nameof(LimitingMagnitude), limitingMagnitude);
                    RaisePropertyChanged();
                }
            }
        }

        private double rotationDegrees;

        public double RotationDegrees {
            get => rotationDegrees;
            set {
                if (rotationDegrees != value) {
                    rotationDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(RotationDegrees), rotationDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private int noiseSeed;

        public int NoiseSeed {
            get => noiseSeed;
            set {
                if (noiseSeed != value) {
                    noiseSeed = value;
                    optionsAccessor.SetValueInt32(nameof(NoiseSeed), noiseSeed);
                    RaisePropertyChanged();
                }
            }
        }

        private bool enableAberrations;

        public bool EnableAberrations {
            get => enableAberrations;
            set {
                if (enableAberrations != value) {
                    enableAberrations = value;
                    optionsAccessor.SetValueBoolean(nameof(EnableAberrations), enableAberrations);
                    RaisePropertyChanged();
                }
            }
        }

        private double tiltAngleDegrees;

        public double TiltAngleDegrees {
            get => tiltAngleDegrees;
            set {
                if (tiltAngleDegrees != value) {
                    tiltAngleDegrees = value;
                    optionsAccessor.SetValueDouble(nameof(TiltAngleDegrees), tiltAngleDegrees);
                    RaisePropertyChanged();
                }
            }
        }

        private double tiltAmountMicrons;

        public double TiltAmountMicrons {
            get => tiltAmountMicrons;
            set {
                if (tiltAmountMicrons != value) {
                    tiltAmountMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(TiltAmountMicrons), tiltAmountMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double backfocusErrorMicrons;

        public double BackfocusErrorMicrons {
            get => backfocusErrorMicrons;
            set {
                if (backfocusErrorMicrons != value) {
                    backfocusErrorMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(BackfocusErrorMicrons), backfocusErrorMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double opticalAxisOffsetXMicrons;

        public double OpticalAxisOffsetXMicrons {
            get => opticalAxisOffsetXMicrons;
            set {
                if (opticalAxisOffsetXMicrons != value) {
                    opticalAxisOffsetXMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(OpticalAxisOffsetXMicrons), opticalAxisOffsetXMicrons);
                    RaisePropertyChanged();
                }
            }
        }

        private double opticalAxisOffsetYMicrons;

        public double OpticalAxisOffsetYMicrons {
            get => opticalAxisOffsetYMicrons;
            set {
                if (opticalAxisOffsetYMicrons != value) {
                    opticalAxisOffsetYMicrons = value;
                    optionsAccessor.SetValueDouble(nameof(OpticalAxisOffsetYMicrons), opticalAxisOffsetYMicrons);
                    RaisePropertyChanged();
                }
            }
        }
    }
}
