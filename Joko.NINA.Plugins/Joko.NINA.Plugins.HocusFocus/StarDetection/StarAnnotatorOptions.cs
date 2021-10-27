using Joko.NINA.Plugins.HocusFocus.Interfaces;
using Joko.NINA.Plugins.HocusFocus.Properties;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {
    public class StarAnnotatorOptions : BaseINPC, IStarAnnotatorOptions {
        private readonly IProfileService profileService;
        public StarAnnotatorOptions(IProfileService profileService) {
            this.profileService = profileService;
        }

        public void ResetDefaults() {
            ShowAllStars = false;
            MaxStars = 200;
            ShowStarBounds = true;
            StarBoundsColor = Color.FromArgb(128, 255, 0, 0); // Red half transparency
            ShowHFR = true;
            TextFontFamily = new FontFamily("Arial");
            TextFontSizePoints = 18;
            HFRColor = Color.FromArgb(255, 255, 255, 0); // Red
            StarBoundsType = StarBoundsTypeEnum.Box;
            ShowROI = true;
            ROIColor = Color.FromArgb(255, 255, 255, 0); // Yellow
            ShowStarCenter = true;
            StarCenterColor = Color.FromArgb(128, 0, 0, 255); // Blue half transparency
            ShowDegenerate = false;
            DegenerateColor = Color.FromArgb(128, 0, 255, 0); // Green half transparency
            ShowSaturated = false;
            SaturatedColor = Color.FromArgb(128, 0, 255, 0); // Green half transparency
            ShowLowSensitivity = false;
            LowSensitivityColor = Color.FromArgb(128, 0, 255, 0); // Green half transparency
            ShowNotCentered = false;
            NotCenteredColor = Color.FromArgb(128, 0, 255, 0); // Green half transparency
            ShowTooFlat = false;
            TooFlatColor = Color.FromArgb(128, 0, 255, 0); // Green half transparency
        }

        public int MaxStars {
            get {
                return Settings.Default.MaxStars;
            }
            set {
                if (Settings.Default.MaxStars != value) {
                    Settings.Default.MaxStars = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowStarBounds {
            get {
                return Settings.Default.ShowStarBounds;
            }
            set {
                if (Settings.Default.ShowStarBounds != value) {
                    Settings.Default.ShowStarBounds = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color StarBoundsColor {
            get {
                return Settings.Default.StarBoundsColor;
            }
            set {
                if (Settings.Default.StarBoundsColor != value) {
                    Settings.Default.StarBoundsColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowHFR {
            get {
                return Settings.Default.ShowHFR;
            }
            set {
                if (Settings.Default.ShowHFR != value) {
                    Settings.Default.ShowHFR = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public FontFamily TextFontFamily {
            get {
                return Settings.Default.TextFontFamily;
            }
            set {
                if (value != Settings.Default.TextFontFamily) {
                    Settings.Default.TextFontFamily = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public float TextFontSizePoints {
            get {
                return Settings.Default.TextFontSizePoints;
            } set {
                if (value != Settings.Default.TextFontSizePoints) {
                    Settings.Default.TextFontSizePoints = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color HFRColor {
            get {
                return Settings.Default.HFRColor;
            }
            set {
                if (Settings.Default.HFRColor != value) {
                    Settings.Default.HFRColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowAllStars {
            get {
                return Settings.Default.ShowAllStars;
            }
            set {
                if (Settings.Default.ShowAllStars != value) {
                    Settings.Default.ShowAllStars = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public StarBoundsTypeEnum StarBoundsType {
            get {
                if (Enum.TryParse<StarBoundsTypeEnum>(Settings.Default.StarBoundsType, out var result)) {
                    return result;
                }
                return StarBoundsTypeEnum.Box;
            }
            set {
                var valueString = Enum.GetName(typeof(StarBoundsTypeEnum), value);
                if (Settings.Default.StarBoundsType != valueString) {
                    Settings.Default.StarBoundsType = valueString;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowROI {
            get {
                return Settings.Default.ShowROI;
            }
            set {
                if (Settings.Default.ShowROI != value) {
                    Settings.Default.ShowROI = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color ROIColor {
            get {
                return Settings.Default.ROIColor;
            }
            set {
                if (Settings.Default.ROIColor != value) {
                    Settings.Default.ROIColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowStarCenter {
            get {
                return Settings.Default.ShowStarCenter;
            }
            set {
                if (Settings.Default.ShowStarCenter != value) {
                    Settings.Default.ShowStarCenter = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color StarCenterColor {
            get {
                return Settings.Default.StarCenterColor;
            }
            set {
                if (Settings.Default.StarCenterColor != value) {
                    Settings.Default.StarCenterColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowDegenerate {
            get {
                return Settings.Default.ShowDegenerate;
            }
            set {
                if (Settings.Default.ShowDegenerate != value) {
                    Settings.Default.ShowDegenerate = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color DegenerateColor {
            get {
                return Settings.Default.DegenerateColor;
            }
            set {
                if (Settings.Default.DegenerateColor != value) {
                    Settings.Default.DegenerateColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowSaturated {
            get {
                return Settings.Default.ShowSaturated;
            }
            set {
                if (Settings.Default.ShowSaturated != value) {
                    Settings.Default.ShowSaturated = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color SaturatedColor {
            get {
                return Settings.Default.SaturatedColor;
            }
            set {
                if (Settings.Default.SaturatedColor != value) {
                    Settings.Default.SaturatedColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowLowSensitivity {
            get {
                return Settings.Default.ShowLowSensitivity;
            }
            set {
                if (Settings.Default.ShowLowSensitivity != value) {
                    Settings.Default.ShowLowSensitivity = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color LowSensitivityColor {
            get {
                return Settings.Default.LowSensitivityColor;
            }
            set {
                if (Settings.Default.LowSensitivityColor != value) {
                    Settings.Default.LowSensitivityColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowNotCentered {
            get {
                return Settings.Default.ShowNotCentered;
            }
            set {
                if (Settings.Default.ShowNotCentered != value) {
                    Settings.Default.ShowNotCentered = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color NotCenteredColor {
            get {
                return Settings.Default.NotCenteredColor;
            }
            set {
                if (Settings.Default.NotCenteredColor != value) {
                    Settings.Default.NotCenteredColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public bool ShowTooFlat {
            get {
                return Settings.Default.ShowTooFlat;
            }
            set {
                if (Settings.Default.ShowTooFlat != value) {
                    Settings.Default.ShowTooFlat = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }

        public Color TooFlatColor {
            get {
                return Settings.Default.TooFlatColor;
            }
            set {
                if (Settings.Default.TooFlatColor != value) {
                    Settings.Default.TooFlatColor = value;
                    Settings.Default.Save();
                    RaisePropertyChanged();
                }
            }
        }
    }
}
