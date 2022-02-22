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
            numRegionsWide = optionsAccessor.GetValueInt32(nameof(NumRegionsWide), 7);
            loopingExposureAnalysisEnabled = optionsAccessor.GetValueBoolean(nameof(LoopingExposureAnalysisEnabled), false);
        }

        public void ResetDefaults() {
            StepCount = -1;
            StepSize = -1;
            FramesPerPoint = -1;
            TimeoutSeconds = -1;
            SimpleExposureSeconds = -1;
            NumRegionsWide = 7;
            LoopingExposureAnalysisEnabled = false;
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
    }
}