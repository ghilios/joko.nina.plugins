using Joko.NINA.Plugins.JokoFocus.Interfaces;
using Joko.NINA.Plugins.JokoFocus.Properties;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Joko.NINA.Plugins.JokoFocus.StarDetection {
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
    }
}
