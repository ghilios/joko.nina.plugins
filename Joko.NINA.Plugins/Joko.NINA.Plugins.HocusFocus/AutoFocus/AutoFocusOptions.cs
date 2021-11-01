using Joko.NINA.Plugins.HocusFocus.Interfaces;
using Joko.NINA.Plugins.HocusFocus.Utility;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            maxConcurrent = optionsAccessor.GetValueInt("MaxConcurrent", 0);
            autoFocusMode = optionsAccessor.GetValueEnum<AutoFocusModeEnum>("AutoFocusMode", AutoFocusModeEnum.AutoFocusMode_Blind);
            fastStepSize = optionsAccessor.GetValueInt("FastStepSize", 1);
            fastOffsetSteps = optionsAccessor.GetValueInt("FastOffsetSteps", 4);
            fastThreshold_Celcius = optionsAccessor.GetValueInt("FastThreshold_Celcius", 5);
            fastThreshold_FocuserPosition = optionsAccessor.GetValueInt("FastThreshold_FocuserPosition", 100);
            fastThreshold_Seconds = optionsAccessor.GetValueInt("FastThreshold_Seconds", (int)TimeSpan.FromMinutes(60).TotalSeconds);
        }


        public void ResetDefaults() {
            maxConcurrent = 0;
            AutoFocusMode = AutoFocusModeEnum.AutoFocusMode_Blind;
            FastStepSize = 1;
            FastOffsetSteps = 4;
            FastThreshold_Celcius = 5;
            FastThreshold_FocuserPosition = 100;
            FastThreshold_Seconds = (int)TimeSpan.FromMinutes(60).TotalSeconds;
        }

        private int maxConcurrent;
        public int MaxConcurrent {
            get => maxConcurrent;
            set {
                if (maxConcurrent != value) {
                    maxConcurrent = value;
                    optionsAccessor.SetValueInt("MaxConcurrent", maxConcurrent);
                    RaisePropertyChanged();
                }
            }
        }

        private AutoFocusModeEnum autoFocusMode;
        public AutoFocusModeEnum AutoFocusMode {
            get => autoFocusMode;
            set {
                if (autoFocusMode != value) {
                    autoFocusMode = value;
                    optionsAccessor.SetValueEnum<AutoFocusModeEnum>("AutoFocusModeEnum", value);
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
                    optionsAccessor.SetValueInt("FastStepSize", fastStepSize);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastOffsetSteps;
        public int FastOffsetSteps {
            get => fastOffsetSteps;
            set {
                if (fastOffsetSteps != value) {
                    fastOffsetSteps = value;
                    optionsAccessor.SetValueInt("FastOffsetSteps", fastOffsetSteps);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastThreshold_Seconds;
        public int FastThreshold_Seconds {
            get => fastThreshold_Seconds;
            set {
                if (fastThreshold_Seconds != value) {
                    fastThreshold_Seconds = value;
                    optionsAccessor.SetValueInt("FastThreshold_Seconds", fastThreshold_Seconds);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastThreshold_Celcius;
        public int FastThreshold_Celcius {
            get => fastThreshold_Celcius;
            set {
                if (fastThreshold_Celcius != value) {
                    fastThreshold_Celcius = value;
                    optionsAccessor.SetValueInt("FastThreshold_Celcius", fastThreshold_Celcius);
                    RaisePropertyChanged();
                }
            }
        }

        private int fastThreshold_FocuserPosition;
        public int FastThreshold_FocuserPosition {
            get => fastThreshold_FocuserPosition;
            set {
                if (fastThreshold_FocuserPosition != value) {
                    fastThreshold_FocuserPosition = value;
                    optionsAccessor.SetValueInt("FastThreshold_FocuserPosition", fastThreshold_FocuserPosition);
                    RaisePropertyChanged();
                }
            }
        }
    }
}
