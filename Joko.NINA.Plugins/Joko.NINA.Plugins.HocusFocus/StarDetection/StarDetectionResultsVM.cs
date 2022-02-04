#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageData;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.ViewModel;
using System.ComponentModel.Composition;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [Export(typeof(IDockableVM))]
    public class StarDetectionResultsVM : DockableVM {
        private readonly IImageStatisticsVM imageStatisticsVM;

        [ImportingConstructor]
        public StarDetectionResultsVM(IProfileService profileService, IImageStatisticsVM imageStatisticsVM)
            : base(profileService) {
            this.Title = "Star Detection Results";
            this.imageStatisticsVM = imageStatisticsVM;
            this.imageStatisticsVM.PropertyChanged += ImageStatisticsVM_PropertyChanged;
            this.Statistics = this.imageStatisticsVM.Statistics;
        }

        private void ImageStatisticsVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Statistics)) {
                this.Statistics = this.imageStatisticsVM.Statistics;
            }
        }

        public override bool IsTool { get; } = false;

        private AllImageStatistics statistics;

        public AllImageStatistics Statistics {
            get => statistics;
            private set {
                if (statistics != value) {
                    if (statistics != null) {
                        statistics.PropertyChanged -= Statistics_PropertyChanged;
                    }
                    statistics = value;
                    if (statistics != null) {
                        statistics.PropertyChanged += Statistics_PropertyChanged;
                    }
                    RaiseAllPropertiesChanged();
                }
            }
        }

        public StarDetectorMetrics Metrics {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.Metrics;
            }
        }

        public StarDetectorPSFFitType PSFType {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.PSFType ?? StarDetectorPSFFitType.Moffat_40;
            }
        }

        public double PSFRSquared {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.PSFRSquared ?? double.NaN;
            }
        }

        public double Sigma {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.Sigma ?? double.NaN;
            }
        }

        public double FWHM {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.FWHM ?? double.NaN;
            }
        }

        public double FWHMMAD {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.FWHMMAD ?? double.NaN;
            }
        }

        public double Eccentricity {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.Eccentricity ?? double.NaN;
            }
        }

        public double EccentricityMAD {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.EccentricityMAD ?? double.NaN;
            }
        }

        private void Statistics_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            this.ChildChanged(sender, e);
            RaisePropertyChanged(nameof(Statistics));
            RaisePropertyChanged(nameof(Metrics));
            RaisePropertyChanged(nameof(PSFType));
            RaisePropertyChanged(nameof(PSFRSquared));
            RaisePropertyChanged(nameof(Sigma));
            RaisePropertyChanged(nameof(FWHM));
            RaisePropertyChanged(nameof(FWHMMAD));
            RaisePropertyChanged(nameof(Eccentricity));
            RaisePropertyChanged(nameof(EccentricityMAD));
        }
    }
}