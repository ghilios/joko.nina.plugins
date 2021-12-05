#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.HocusFocus.Interfaces;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace Joko.NINA.Plugins.HocusFocus.AutoFocus {

    public class AutoFocusOptions : BaseINPC, IAutoFocusOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public AutoFocusOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            InitializeOptions();
        }

        private void InitializeOptions() {
            maxConcurrent = optionsAccessor.GetValueInt32("MaxConcurrent", 0);
            fastFocusModeEnabled = optionsAccessor.GetValueBoolean("FastFocusModeEnabled", false);
            fastStepSize = optionsAccessor.GetValueInt32("FastStepSize", 1);
            fastOffsetSteps = optionsAccessor.GetValueInt32("FastOffsetSteps", 4);
            fastThreshold_Celcius = optionsAccessor.GetValueInt32("FastThreshold_Celcius", 5);
            fastThreshold_FocuserPosition = optionsAccessor.GetValueInt32("FastThreshold_FocuserPosition", 100);
            fastThreshold_Seconds = optionsAccessor.GetValueInt32("FastThreshold_Seconds", (int)TimeSpan.FromMinutes(60).TotalSeconds);
            autoFocusTimeoutSeconds = optionsAccessor.GetValueInt32("AutoFocusTimeoutSeconds", (int)TimeSpan.FromMinutes(10).TotalSeconds);
            validateHfrImprovement = optionsAccessor.GetValueBoolean("ValidateHfrImprovement", true);
            hfrImprovementThreshold = optionsAccessor.GetValueDouble("HFRImprovementThreshold", 0.15);
        }

        public void ResetDefaults() {
            maxConcurrent = 0;
            FastFocusModeEnabled = false;
            FastStepSize = 1;
            FastOffsetSteps = 4;
            FastThreshold_Celcius = 5;
            FastThreshold_FocuserPosition = 100;
            FastThreshold_Seconds = (int)TimeSpan.FromMinutes(60).TotalSeconds;
            AutoFocusTimeoutSeconds = (int)TimeSpan.FromMinutes(10).TotalSeconds;
            ValidateHfrImprovement = true;
            HFRImprovementThreshold = 0.15;
        }

        private int maxConcurrent;

        public int MaxConcurrent {
            get => maxConcurrent;
            set {
                if (maxConcurrent != value) {
                    maxConcurrent = value;
                    optionsAccessor.SetValueInt32("MaxConcurrent", maxConcurrent);
                    RaisePropertyChanged();
                }
            }
        }

        private bool fastFocusModeEnabled;

        public bool FastFocusModeEnabled {
            get => fastFocusModeEnabled;
            set {
                if (fastFocusModeEnabled != value) {
                    fastFocusModeEnabled = value;
                    optionsAccessor.SetValueBoolean("FastFocusModeEnabled", value);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastStepSize;

        public int FastStepSize {
            get => fastStepSize;
            set {
                if (fastStepSize != value) {
                    fastStepSize = value;
                    optionsAccessor.SetValueInt32("FastStepSize", fastStepSize);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastOffsetSteps;

        public int FastOffsetSteps {
            get => fastOffsetSteps;
            set {
                if (fastOffsetSteps != value) {
                    if (value <= 1) {
                        throw new ArgumentException("FastOffsetSteps must be at least 2", "FastOffsetSteps");
                    }

                    fastOffsetSteps = value;
                    optionsAccessor.SetValueInt32("FastOffsetSteps", fastOffsetSteps);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastThreshold_Seconds;

        public int FastThreshold_Seconds {
            get => fastThreshold_Seconds;
            set {
                if (fastThreshold_Seconds != value) {
                    if (value < 0) {
                        throw new ArgumentException("FastThreshold_Seconds must be non-negative", "FastThreshold_Seconds");
                    }

                    fastThreshold_Seconds = value;
                    optionsAccessor.SetValueInt32("FastThreshold_Seconds", fastThreshold_Seconds);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastThreshold_Celcius;

        public int FastThreshold_Celcius {
            get => fastThreshold_Celcius;
            set {
                if (fastThreshold_Celcius != value) {
                    if (value < 0) {
                        throw new ArgumentException("FastThreshold_Celcius must be non-negative", "FastThreshold_Celcius");
                    }

                    fastThreshold_Celcius = value;
                    optionsAccessor.SetValueInt32("FastThreshold_Celcius", fastThreshold_Celcius);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastThreshold_FocuserPosition;

        public int FastThreshold_FocuserPosition {
            get => fastThreshold_FocuserPosition;
            set {
                if (fastThreshold_FocuserPosition != value) {
                    if (value < 0) {
                        throw new ArgumentException("FastThreshold_FocuserPosition must be non-negative", "FastThreshold_FocuserPosition");
                    }

                    fastThreshold_FocuserPosition = value;
                    optionsAccessor.SetValueInt32("FastThreshold_FocuserPosition", fastThreshold_FocuserPosition);
                    RaisePropertyChanged();
                }
            }
        }

        private bool validateHfrImprovement;

        public bool ValidateHfrImprovement {
            get => validateHfrImprovement;
            set {
                if (validateHfrImprovement != value) {
                    validateHfrImprovement = value;
                    optionsAccessor.SetValueBoolean("ValidateHfrImprovement", validateHfrImprovement);
                    RaisePropertyChanged();
                }
            }
        }

        private double hfrImprovementThreshold;

        public double HFRImprovementThreshold {
            get => hfrImprovementThreshold;
            set {
                if (hfrImprovementThreshold != value) {
                    if (double.IsNaN(value) || double.IsInfinity(value)) {
                        throw new ArgumentException("HFRImprovementThreshold must be real, finite number", "HFRImprovementThreshold");
                    }

                    hfrImprovementThreshold = value;
                    optionsAccessor.SetValueDouble("HFRImprovementThreshold", hfrImprovementThreshold);
                    RaisePropertyChanged();
                }
            }
        }

        private int autoFocusTimeoutSeconds;

        public int AutoFocusTimeoutSeconds {
            get => autoFocusTimeoutSeconds;
            set {
                if (autoFocusTimeoutSeconds != value) {
                    if (value <= 0) {
                        throw new ArgumentException("AutoFocusTimeoutSeconds must be positive", "AutoFocusTimeoutSeconds");
                    }

                    autoFocusTimeoutSeconds = value;
                    optionsAccessor.SetValueInt32("AutoFocusTimeoutSeconds", autoFocusTimeoutSeconds);
                    RaisePropertyChanged();
                }
            }
        }
    }
}