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
        private readonly IProfileService profileService;

        public AutoFocusOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal AutoFocusOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
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
            autoFocusTimeoutSeconds = optionsAccessor.GetValueInt32("AutoFocusTimeoutSeconds", (int)TimeSpan.FromMinutes(10).TotalSeconds);
            validateHfrImprovement = optionsAccessor.GetValueBoolean("ValidateHfrImprovement", true);
            hfrImprovementThreshold = optionsAccessor.GetValueDouble("HFRImprovementThreshold", 0.15);
            savePath = optionsAccessor.GetValueString("SavePath", "");
            save = optionsAccessor.GetValueBoolean("Save", false);
            keepFramesForReview = optionsAccessor.GetValueBoolean(nameof(KeepFramesForReview), false);
            lastSelectedLoadPath = optionsAccessor.GetValueString("LastSelectedLoadPath", "");
            focuserOffset = optionsAccessor.GetValueInt32("FocuserOffset", 0);
            maxOutlierRejections = optionsAccessor.GetValueInt32(nameof(MaxOutlierRejections), 1);
            outlierRejectionConfidence = optionsAccessor.GetValueDouble(nameof(OutlierRejectionConfidence), 0.90);
            weightedHyperbolicFitEnabled = optionsAccessor.GetValueBoolean(nameof(WeightedHyperbolicFitEnabled), true);
            hyperbolicFitModel = optionsAccessor.GetValueEnum(nameof(HyperbolicFitModel), HyperbolicFitModel.Hybrid);
            fitRejectionCriterion = optionsAccessor.GetValueEnum(nameof(FitRejectionCriterion), FitRejectionCriterion.RSquared);
            reducedChiSquaredRejectionThreshold = optionsAccessor.GetValueDouble(nameof(ReducedChiSquaredRejectionThreshold), 5.0);
        }

        public void ResetDefaults() {
            maxConcurrent = 0;
            AutoFocusTimeoutSeconds = (int)TimeSpan.FromMinutes(10).TotalSeconds;
            ValidateHfrImprovement = true;
            HFRImprovementThreshold = 0.15;
            SavePath = "";
            Save = false;
            KeepFramesForReview = false;
            LastSelectedLoadPath = "";
            FocuserOffset = 0;
            MaxOutlierRejections = 1;
            OutlierRejectionConfidence = 0.90;
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

        private bool keepFramesForReview;

        public bool KeepFramesForReview {
            get => keepFramesForReview;
            set {
                if (keepFramesForReview != value) {
                    keepFramesForReview = value;
                    optionsAccessor.SetValueBoolean(nameof(KeepFramesForReview), keepFramesForReview);
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

        /// <summary>
        /// The R² rejection threshold used when <see cref="FitRejectionCriterion"/> is
        /// <see cref="FitRejectionCriterion.RSquared"/>. This is a proxy for NINA's own
        /// <c>FocuserSettings.RSquaredThreshold</c> (the value the engine actually compares against, also
        /// editable in NINA's Focuser settings) — surfaced here so the threshold for the selected criterion can
        /// be shown and tuned next to it. Not persisted as a HocusFocus option, so it is excluded from JSON.
        /// </summary>
        [JsonIgnore]
        public double RSquaredRejectionThreshold {
            get => profileService.ActiveProfile.FocuserSettings.RSquaredThreshold;
            set {
                if (double.IsNaN(value) || double.IsInfinity(value)) {
                    throw new ArgumentException("RSquaredRejectionThreshold must be a real, finite number", nameof(RSquaredRejectionThreshold));
                }
                if (profileService.ActiveProfile.FocuserSettings.RSquaredThreshold != value) {
                    profileService.ActiveProfile.FocuserSettings.RSquaredThreshold = value;
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