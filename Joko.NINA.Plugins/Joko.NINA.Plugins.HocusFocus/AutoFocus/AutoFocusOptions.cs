#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using Newtonsoft.Json;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    [JsonObject]
    public class AutoFocusOptions : BaseINPC, IAutoFocusOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public AutoFocusOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            InitializeOptions();
            RaiseAllPropertiesChanged();
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
            savePath = optionsAccessor.GetValueString("SavePath", "");
            save = optionsAccessor.GetValueBoolean("Save", false);
            lastSelectedLoadPath = optionsAccessor.GetValueString("LastSelectedLoadPath", "");
            focuserOffset = optionsAccessor.GetValueInt32("FocuserOffset", 0);
            allowHyperbolaRotation = optionsAccessor.GetValueBoolean(nameof(AllowHyperbolaRotation), false);
            maxOutlierRejections = optionsAccessor.GetValueInt32(nameof(MaxOutlierRejections), 1);
            outlierRejectionConfidence = optionsAccessor.GetValueDouble(nameof(OutlierRejectionConfidence), 0.90);
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
            SavePath = "";
            Save = false;
            LastSelectedLoadPath = "";
            FocuserOffset = 0;
            AllowHyperbolaRotation = false;
            MaxOutlierRejections = 1;
            OutlierRejectionConfidence = 0.90;
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

        private string savePath;

        public string SavePath {
            get => savePath;
            set {
                if (savePath != value) {
                    savePath = value;
                    optionsAccessor.SetValueString(nameof(SavePath), savePath);
                    RaisePropertyChanged();
                }
            }
        }

        private bool save;

        public bool Save {
            get => save;
            set {
                if (save != value) {
                    save = value;
                    optionsAccessor.SetValueBoolean(nameof(Save), save);
                    RaisePropertyChanged();
                }
            }
        }

        private string lastSelectedLoadPath;

        public string LastSelectedLoadPath {
            get => lastSelectedLoadPath;
            set {
                if (lastSelectedLoadPath != value) {
                    lastSelectedLoadPath = value;
                    optionsAccessor.SetValueString(nameof(LastSelectedLoadPath), lastSelectedLoadPath);
                    RaisePropertyChanged();
                }
            }
        }

        private int focuserOffset;

        public int FocuserOffset {
            get => focuserOffset;
            set {
                if (focuserOffset != value) {
                    focuserOffset = value;
                    optionsAccessor.SetValueInt32("FocuserOffset", focuserOffset);
                    RaisePropertyChanged();
                }
            }
        }

        private bool allowHyperbolaRotation;

        public bool AllowHyperbolaRotation {
            get => allowHyperbolaRotation;
            set {
                if (allowHyperbolaRotation != value) {
                    allowHyperbolaRotation = value;
                    optionsAccessor.SetValueBoolean(nameof(AllowHyperbolaRotation), allowHyperbolaRotation);
                    RaisePropertyChanged();
                }
            }
        }

        private int maxOutlierRejections;

        public int MaxOutlierRejections {
            get => maxOutlierRejections;
            set {
                if (maxOutlierRejections != value) {
                    if (value < 0) {
                        throw new ArgumentException("MaxOutlierRejections must be non-negative", nameof(MaxOutlierRejections));
                    }

                    maxOutlierRejections = value;
                    optionsAccessor.SetValueInt32(nameof(MaxOutlierRejections), maxOutlierRejections);
                    RaisePropertyChanged();
                }
            }
        }

        private double outlierRejectionConfidence;

        public double OutlierRejectionConfidence {
            get => outlierRejectionConfidence;
            set {
                if (outlierRejectionConfidence != value) {
                    if (value <= 0.5 || value >= 1.0) {
                        throw new ArgumentException("OutlierRejectionConfidence must be between 0.5 and 1.0, exclusive", nameof(OutlierRejectionConfidence));
                    }

                    outlierRejectionConfidence = value;
                    optionsAccessor.SetValueDouble(nameof(OutlierRejectionConfidence), outlierRejectionConfidence);
                    RaisePropertyChanged();
                }
            }
        }
    }
}