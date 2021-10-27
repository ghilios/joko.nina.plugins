using Joko.NINA.Plugins.HocusFocus.Interfaces;
using Joko.NINA.Plugins.HocusFocus.Properties;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using System;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {
    public class StarDetectionOptions : BaseINPC, IStarDetectionOptions {
        private readonly IProfileService profileService;
        public StarDetectionOptions(IProfileService profileService) {
            this.profileService = profileService;
        }

        public void ResetDefaults() {
            HotpixelFiltering = true;
            NoiseReductionRadius = 0;
            NoiseClippingMultiplier = 5.0;
            StructureLayers = 5;
            BrightnessSensitivity = 5.0;
            StarPeakResponse = 0.5;
            MaxDistortion = 0.5;
            StarCenterTolerance = 0.3;
            StarBackgroundBoxExpansion = 3;
            MinStarBoundingBoxSize = 5;
            MinHFR = 1.5;
            StructureDilationSize = 7;
            StructureDilationCount = 1;
        }

        public bool HotpixelFiltering {
            get {
                return Settings.Default.HotpixelFiltering;
            }
            set {
                if (Settings.Default.HotpixelFiltering != value) {
                    Settings.Default.HotpixelFiltering = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public int NoiseReductionRadius {
            get {
                return Settings.Default.NoiseReductionRadius;
            }
            set {
                if (Settings.Default.NoiseReductionRadius != value) {
                    if (value < 0) {
                        throw new ArgumentException("NoiseReductionRadius must be non-negative", "NoiseReductionRadius");
                    }
                    Settings.Default.NoiseReductionRadius = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double NoiseClippingMultiplier {
            get {
                return Settings.Default.NoiseClippingMultiplier;
            }
            set {
                if (Settings.Default.NoiseClippingMultiplier != value) {
                    if (value < 0) {
                        throw new ArgumentException("NoiseClippingMultiplier must be non-negative", "NoiseClippingMultiplier");
                    }
                    Settings.Default.NoiseClippingMultiplier = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double StarClippingMultiplier {
            get {
                return Settings.Default.StarClippingMultiplier;
            }
            set {
                if (Settings.Default.StarClippingMultiplier != value) {
                    if (value < 0) {
                        throw new ArgumentException("StarClippingMultiplier must be non-negative", "StarClippingMultiplier");
                    }
                    Settings.Default.StarClippingMultiplier = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public int StructureLayers {
            get {
                return Settings.Default.StructureLayers;
            }
            set {
                if (Settings.Default.StructureLayers != value) {
                    if (value <= 0) {
                        throw new ArgumentException("StructureLayers must be positive", "StructureLayers");
                    }
                    Settings.Default.StructureLayers = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double StarCenterTolerance {
            get {
                return Settings.Default.StarCenterTolerance;
            }
            set {
                if (Settings.Default.StarCenterTolerance != value) {
                    if (value <= 0.0 || value > 1.0) {
                        throw new ArgumentException("StarCenterTolerance must be positive and <= 1.0", "StarCenterTolerance");
                    }
                    Settings.Default.StarCenterTolerance = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double StarPeakResponse {
            get {
                return Settings.Default.StarPeakResponse;
            }
            set {
                if (Settings.Default.StarPeakResponse != value) {
                    if (value <= 0) {
                        throw new ArgumentException("StarPeakResponse must be positive", "StarPeakResponse");
                    }
                    Settings.Default.StarPeakResponse = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double MaxDistortion {
            get {
                return Settings.Default.MaxDistortion;
            }
            set {
                if (Settings.Default.MaxDistortion != value) {
                    if (value < 0.0 || value > 1.0) {
                        throw new ArgumentException("MaxDistortion must be within [0, 1]", "MaxDistortion");
                    }
                    Settings.Default.MaxDistortion = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double BrightnessSensitivity {
            get {
                return Settings.Default.BrightnessSensitivity;
            }
            set {
                if (Settings.Default.BrightnessSensitivity != value) {
                    if (value < 0) {
                        throw new ArgumentException("BrightnessSensitivity must be non-negative", "BrightnessSensitivity");
                    }
                    Settings.Default.BrightnessSensitivity = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public int StarBackgroundBoxExpansion {
            get {
                return Settings.Default.StarBackgroundBoxExpansion;
            }
            set {
                if (Settings.Default.StarBackgroundBoxExpansion != value) {
                    if (value < 1) {
                        throw new ArgumentException("StarBackgroundBoxExpansion must be at least 1", "StarBackgroundBoxExpansion");
                    }
                    Settings.Default.StarBackgroundBoxExpansion = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public int MinStarBoundingBoxSize {
            get {
                return Settings.Default.MinStarBoundingBoxSize;
            }
            set {
                if (Settings.Default.MinStarBoundingBoxSize != value) {
                    if (value < 1) {
                        throw new ArgumentException("MinStarBoundingBoxSize must be at least 1", "MinStarBoundingBoxSize");
                    }
                    Settings.Default.MinStarBoundingBoxSize = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public double MinHFR {
            get {
                return Settings.Default.MinHFR;
            }
            set {
                if (Settings.Default.MinHFR != value) {
                    if (value < 0) {
                        throw new ArgumentException("MinHFR must be non-negative", "MinHFR");
                    }
                    Settings.Default.MinHFR = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public int StructureDilationSize {
            get {
                return Settings.Default.StructureDilationSize;
            }
            set {
                if (Settings.Default.StructureDilationSize != value) {
                    if (value < 3) {
                        throw new ArgumentException("StructureDilationSize must be at least 3", "StructureDilationSize");
                    }
                    Settings.Default.StructureDilationSize = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public int StructureDilationCount {
            get {
                return Settings.Default.StructureDilationCount;
            }
            set {
                if (Settings.Default.StructureDilationCount != value) {
                    if (value < 0) {
                        throw new ArgumentException("StructureDilationCount must be non-negative", "StructureDilationCount");
                    }
                    Settings.Default.StructureDilationCount = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }
    }
}
