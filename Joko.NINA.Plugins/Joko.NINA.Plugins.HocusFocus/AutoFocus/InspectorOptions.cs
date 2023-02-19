#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class InspectorOptions : BaseINPC, IInspectorOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public InspectorOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            InitializeOptions();
        }

        private void InitializeOptions() {
            stepCount = optionsAccessor.GetValueInt32(nameof(StepCount), -1);
            stepSize = optionsAccessor.GetValueInt32(nameof(StepSize), -1);
            framesPerPoint = optionsAccessor.GetValueInt32(nameof(FramesPerPoint), -1);
            timeoutSeconds = optionsAccessor.GetValueInt32(nameof(TimeoutSeconds), -1);
            simpleExposureSeconds = optionsAccessor.GetValueDouble(nameof(SimpleExposureSeconds), -1);
            detailedAnalysisExposureSeconds = optionsAccessor.GetValueDouble(nameof(DetailedAnalysisExposureSeconds), -1);
            numRegionsWide = optionsAccessor.GetValueInt32(nameof(NumRegionsWide), 7);
            loopingExposureAnalysisEnabled = optionsAccessor.GetValueBoolean(nameof(LoopingExposureAnalysisEnabled), false);
            micronsPerFocuserStep = optionsAccessor.GetValueDouble(nameof(MicronsPerFocuserStep), -1);
            eccentricityColorMapEnabled = optionsAccessor.GetValueBoolean(nameof(EccentricityColorMapEnabled), true);
            mouseOnChartsEnabled = optionsAccessor.GetValueBoolean(nameof(MouseOnChartsEnabled), true);
            sensorCurveModelEnabled = optionsAccessor.GetValueBoolean(nameof(SensorCurveModelEnabled), false);
            showSensorModel = optionsAccessor.GetValueBoolean(nameof(ShowSensorModel), true);
            sensorROI = optionsAccessor.GetValueDouble(nameof(SensorROI), 1.0);
            cornersROI = optionsAccessor.GetValueDouble(nameof(CornersROI), 1.0);
            renderingEnabled = optionsAccessor.GetValueBoolean(nameof(RenderingEnabled), false);
        }

        public void ResetDefaults() {
            StepCount = -1;
            StepSize = -1;
            FramesPerPoint = -1;
            TimeoutSeconds = -1;
            SimpleExposureSeconds = -1;
            NumRegionsWide = 7;
            LoopingExposureAnalysisEnabled = false;
            MicronsPerFocuserStep = -1;
            EccentricityColorMapEnabled = true;
            MouseOnChartsEnabled = true;
            SensorCurveModelEnabled = false;
            ShowSensorModel = true;
            SensorROI = 1.0;
            CornersROI = 1.0;
            RenderingEnabled = false;
        }

        private int stepCount;

        public int StepCount {
            get => stepCount;
            set {
                if (stepCount != value) {
                    stepCount = value;
                    optionsAccessor.SetValueInt32(nameof(StepCount), stepCount);
                    RaisePropertyChanged();
                }
            }
        }

        private int stepSize;

        public int StepSize {
            get => stepSize;
            set {
                if (stepSize != value) {
                    stepSize = value;
                    optionsAccessor.SetValueInt32(nameof(StepSize), stepSize);
                    RaisePropertyChanged();
                }
            }
        }

        private int framesPerPoint;

        public int FramesPerPoint {
            get => framesPerPoint;
            set {
                if (framesPerPoint != value) {
                    framesPerPoint = value;
                    optionsAccessor.SetValueInt32(nameof(FramesPerPoint), framesPerPoint);
                    RaisePropertyChanged();
                }
            }
        }

        private int timeoutSeconds;

        public int TimeoutSeconds {
            get => timeoutSeconds;
            set {
                if (timeoutSeconds != value) {
                    timeoutSeconds = value;
                    optionsAccessor.SetValueInt32(nameof(StepCount), timeoutSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private double simpleExposureSeconds;

        public double SimpleExposureSeconds {
            get => simpleExposureSeconds;
            set {
                if (simpleExposureSeconds != value) {
                    simpleExposureSeconds = value;
                    optionsAccessor.SetValueDouble(nameof(SimpleExposureSeconds), simpleExposureSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private double detailedAnalysisExposureSeconds;

        public double DetailedAnalysisExposureSeconds {
            get => detailedAnalysisExposureSeconds;
            set {
                if (detailedAnalysisExposureSeconds != value) {
                    detailedAnalysisExposureSeconds = value;
                    optionsAccessor.SetValueDouble(nameof(DetailedAnalysisExposureSeconds), detailedAnalysisExposureSeconds);
                    RaisePropertyChanged();
                }
            }
        }

        private int numRegionsWide;

        public int NumRegionsWide {
            get => numRegionsWide;
            set {
                if (numRegionsWide != value) {
                    numRegionsWide = value;
                    optionsAccessor.SetValueInt32(nameof(NumRegionsWide), numRegionsWide);
                    RaisePropertyChanged();
                }
            }
        }

        private bool loopingExposureAnalysisEnabled;

        public bool LoopingExposureAnalysisEnabled {
            get => loopingExposureAnalysisEnabled;
            set {
                if (loopingExposureAnalysisEnabled != value) {
                    loopingExposureAnalysisEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(LoopingExposureAnalysisEnabled), loopingExposureAnalysisEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private double micronsPerFocuserStep;

        public double MicronsPerFocuserStep {
            get => micronsPerFocuserStep;
            set {
                if (micronsPerFocuserStep != value) {
                    micronsPerFocuserStep = value;
                    optionsAccessor.SetValueDouble(nameof(MicronsPerFocuserStep), micronsPerFocuserStep);
                    RaisePropertyChanged();
                }
            }
        }

        private bool eccentricityColorMapEnabled;

        public bool EccentricityColorMapEnabled {
            get => eccentricityColorMapEnabled;
            set {
                if (eccentricityColorMapEnabled != value) {
                    eccentricityColorMapEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(EccentricityColorMapEnabled), eccentricityColorMapEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool mouseOnChartsEnabled;

        public bool MouseOnChartsEnabled {
            get => mouseOnChartsEnabled;
            set {
                if (mouseOnChartsEnabled != value) {
                    mouseOnChartsEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(MouseOnChartsEnabled), mouseOnChartsEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool sensorCurveModelEnabled;

        public bool SensorCurveModelEnabled {
            get => sensorCurveModelEnabled;
            set {
                if (sensorCurveModelEnabled != value) {
                    sensorCurveModelEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(SensorCurveModelEnabled), sensorCurveModelEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool showSensorModel;

        public bool ShowSensorModel {
            get => showSensorModel;
            set {
                if (showSensorModel != value) {
                    showSensorModel = value;
                    optionsAccessor.SetValueBoolean(nameof(ShowSensorModel), showSensorModel);
                    RaisePropertyChanged();
                }
            }
        }

        private double sensorROI;

        public double SensorROI {
            get => sensorROI;
            set {
                if (sensorROI != value) {
                    if (value < 0.1) {
                        sensorROI = 0.1;
                    } else if (value > 1.0 || double.IsNaN(value)) {
                        sensorROI = 1.0;
                    } else {
                        sensorROI = value;
                    }

                    optionsAccessor.SetValueDouble(nameof(SensorROI), sensorROI);
                    RaisePropertyChanged();
                }
            }
        }

        private double cornersROI;

        public double CornersROI {
            get => cornersROI;
            set {
                if (cornersROI != value) {
                    if (value < 0.1) {
                        cornersROI = 0.1;
                    } else if (value > 1.0 || double.IsNaN(value)) {
                        cornersROI = 1.0;
                    } else {
                        cornersROI = value;
                    }

                    optionsAccessor.SetValueDouble(nameof(CornersROI), cornersROI);
                    RaisePropertyChanged();
                }
            }
        }

        private bool renderingEnabled;

        public bool RenderingEnabled {
            get => renderingEnabled;
            set {
                if (renderingEnabled != value) {
                    renderingEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(RenderingEnabled), renderingEnabled);
                    RaisePropertyChanged();
                }
            }
        }
    }
}