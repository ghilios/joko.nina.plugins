#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

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
        private readonly IPluginOptionsAccessor optionsAccessor;

        public AutoFocusOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal AutoFocusOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
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
            maxOutlierRejections = optionsAccessor.GetValueInt32(nameof(MaxOutlierRejections), 1);
            outlierRejectionConfidence = optionsAccessor.GetValueDouble(nameof(OutlierRejectionConfidence), 0.90);
            unevenHyperbolicFitEnabled = optionsAccessor.GetValueBoolean(nameof(UnevenHyperbolicFitEnabled), true);
            weightedHyperbolicFitEnabled = optionsAccessor.GetValueBoolean(nameof(WeightedHyperbolicFitEnabled), true);

            // HyperbolicFitModel supersedes the UnevenHyperbolicFitEnabled boolean, and new profiles default to the
            // Hybrid best-fit model. Migrate once: an upgrading user (the legacy boolean is actually present in the
            // store) keeps their effective behavior (true => uneven blend, false => symmetric); a brand-new profile
            // (boolean absent) gets Hybrid. Presence is detected by reading the boolean with two different defaults —
            // they agree only when a stored value exists. Thereafter the selector is read directly.
            if (!optionsAccessor.GetValueBoolean("HyperbolicFitModelMigrated", false)) {
                var legacyBooleanPresent = optionsAccessor.GetValueBoolean(nameof(UnevenHyperbolicFitEnabled), false)
                                        == optionsAccessor.GetValueBoolean(nameof(UnevenHyperbolicFitEnabled), true);
                if (legacyBooleanPresent) {
                    hyperbolicFitModel = unevenHyperbolicFitEnabled ? HyperbolicFitModel.UnevenBlendLegacy : HyperbolicFitModel.Symmetric;
                } else {
                    hyperbolicFitModel = HyperbolicFitModel.Hybrid;
                }
                optionsAccessor.SetValueEnum(nameof(HyperbolicFitModel), hyperbolicFitModel);
                optionsAccessor.SetValueBoolean("HyperbolicFitModelMigrated", true);
            } else {
                hyperbolicFitModel = optionsAccessor.GetValueEnum(nameof(HyperbolicFitModel), HyperbolicFitModel.Hybrid);
            }

            fitRejectionCriterion = optionsAccessor.GetValueEnum(nameof(FitRejectionCriterion), FitRejectionCriterion.RSquared);
            reducedChiSquaredRejectionThreshold = optionsAccessor.GetValueDouble(nameof(ReducedChiSquaredRejectionThreshold), 5.0);
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
            MaxOutlierRejections = 1;
            OutlierRejectionConfidence = 0.90;
            UnevenHyperbolicFitEnabled = true;
            WeightedHyperbolicFitEnabled = true;
            HyperbolicFitModel = HyperbolicFitModel.Hybrid;
            FitRejectionCriterion = FitRejectionCriterion.RSquared;
            ReducedChiSquaredRejectionThreshold = 5.0;
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

        private bool unevenHyperbolicFitEnabled;

        public bool UnevenHyperbolicFitEnabled {
            get => unevenHyperbolicFitEnabled;
            set {
                if (unevenHyperbolicFitEnabled != value) {
                    unevenHyperbolicFitEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(UnevenHyperbolicFitEnabled), unevenHyperbolicFitEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private bool weightedHyperbolicFitEnabled;

        public bool WeightedHyperbolicFitEnabled {
            get => weightedHyperbolicFitEnabled;
            set {
                if (weightedHyperbolicFitEnabled != value) {
                    weightedHyperbolicFitEnabled = value;
                    optionsAccessor.SetValueBoolean(nameof(WeightedHyperbolicFitEnabled), weightedHyperbolicFitEnabled);
                    RaisePropertyChanged();
                }
            }
        }

        private HyperbolicFitModel hyperbolicFitModel;

        public HyperbolicFitModel HyperbolicFitModel {
            get => hyperbolicFitModel;
            set {
                if (hyperbolicFitModel != value) {
                    hyperbolicFitModel = value;
                    optionsAccessor.SetValueEnum(nameof(HyperbolicFitModel), hyperbolicFitModel);
                    RaisePropertyChanged();
                }
            }
        }

        private FitRejectionCriterion fitRejectionCriterion;

        public FitRejectionCriterion FitRejectionCriterion {
            get => fitRejectionCriterion;
            set {
                if (fitRejectionCriterion != value) {
                    fitRejectionCriterion = value;
                    optionsAccessor.SetValueEnum(nameof(FitRejectionCriterion), fitRejectionCriterion);
                    RaisePropertyChanged();
                }
            }
        }

        private double reducedChiSquaredRejectionThreshold;

        public double ReducedChiSquaredRejectionThreshold {
            get => reducedChiSquaredRejectionThreshold;
            set {
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    throw new ArgumentException("ReducedChiSquaredRejectionThreshold must be a real, finite number", "ReducedChiSquaredRejectionThreshold");
                }
                if (reducedChiSquaredRejectionThreshold != value) {
                    reducedChiSquaredRejectionThreshold = value;
                    optionsAccessor.SetValueDouble(nameof(ReducedChiSquaredRejectionThreshold), reducedChiSquaredRejectionThreshold);
                    RaisePropertyChanged();
                }
            }
        }
    }
}