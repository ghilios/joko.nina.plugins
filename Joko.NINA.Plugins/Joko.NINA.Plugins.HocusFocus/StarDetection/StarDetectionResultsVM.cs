using Joko.NINA.Plugins.HocusFocus.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageData;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {
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

        public double Eccentricity {
            get {
                return (Statistics?.StarDetectionAnalysis as HocusFocusStarDetectionAnalysis)?.Eccentricity ?? double.NaN;
            }
        }

        private void Statistics_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            this.ChildChanged(sender, e);
            RaisePropertyChanged(nameof(Statistics));
            RaisePropertyChanged(nameof(Metrics));
            RaisePropertyChanged(nameof(Eccentricity));
        }
    }
}
