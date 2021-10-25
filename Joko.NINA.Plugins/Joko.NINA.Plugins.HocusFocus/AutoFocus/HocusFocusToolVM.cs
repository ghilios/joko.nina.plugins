using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using static NINA.WPF.Base.ViewModel.Imaging.AutoFocusToolVM;

namespace Joko.NINA.Plugins.HocusFocus.AutoFocus {
    // TODO: Remove when ready
    // [Export(typeof(IDockableVM))]
    public class HocusFocusToolVM : DockableVM, ICameraConsumer, IFocuserConsumer, IAutoFocusToolVM {
        private CancellationTokenSource _autoFocusCancelToken;
        private AsyncObservableCollection<Chart> _chartList;
        private bool _chartListSelectable;
        private Chart _selectedChart;
        private ApplicationStatus _status;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private CameraInfo cameraInfo;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private FocuserInfo focuserInfo;
        private readonly IFocuserMediator focuserMediator;

        [ImportingConstructor]
        public HocusFocusToolVM(
                IProfileService profileService,
                ICameraMediator cameraMediator,
                IFilterWheelMediator filterWheelMediator,
                IFocuserMediator focuserMediator,
                IApplicationStatusMediator applicationStatusMediator,
                // JokoAutoFocusVMFactory autoFocusVMFactory
                IGuiderMediator guiderMediator,
                IImagingMediator imagingMediator,
                IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
                IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector
        ) : base(profileService) {
            // TODO: Get a new logo?
            Title = "Hocus Focus";
            ImageGeometry = (System.Windows.Media.GeometryGroup)System.Windows.Application.Current?.Resources["AutoFocusSVG"];

            this.cameraMediator = cameraMediator;
            this.cameraMediator.RegisterConsumer(this);

            this.filterWheelMediator = filterWheelMediator;

            this.focuserMediator = focuserMediator;
            this.focuserMediator.RegisterConsumer(this);

            this.applicationStatusMediator = applicationStatusMediator;

            this.AutoFocusVM = new HocusFocusVMFactory(profileService, cameraMediator, filterWheelMediator, focuserMediator, guiderMediator, imagingMediator, starDetectionSelector, starAnnotatorSelector).Create();

            ChartList = new AsyncObservableCollection<Chart>();
            ChartListSelectable = true;
            Task.Run(() => { ListCharts(); });

            StartAutoFocusCommand = new AsyncCommand<AutoFocusReport>(
                () =>
                    Task.Run(
                        async () => {
                            cameraMediator.RegisterCaptureBlock(this);
                            ChartListSelectable = false;
                            try {
                                var result = await AutoFocusVM.StartAutoFocus(CommandInitialization(), _autoFocusCancelToken.Token, new Progress<ApplicationStatus>(p => Status = p));
                                var dir = new DirectoryInfo(global::NINA.WPF.Base.ViewModel.AutoFocus.AutoFocusVM.ReportDirectory);
                                var latestReport = (from f in dir.GetFiles()
                                                    orderby f.LastWriteTime descending
                                                    select f).FirstOrDefault();
                                if (latestReport != null) {
                                    ChartList.Add(new Chart(latestReport.Name, latestReport.FullName));
                                }
                                return result;
                            } finally {
                                cameraMediator.ReleaseCaptureBlock(this);
                                ChartListSelectable = true;
                            }
                        }
                    ),
                (p) => { return focuserInfo?.Connected == true && cameraInfo?.Connected == true && cameraMediator.IsFreeToCapture(this); }
            );
            CancelAutoFocusCommand = new RelayCommand(CancelAutoFocus);
            SelectionChangedCommand = new AsyncCommand<bool>(LoadChart);
        }

        public override bool IsTool { get; } = true;

        public IAutoFocusVM AutoFocusVM { get; }

        public ICommand CancelAutoFocusCommand { get; private set; }

        public AsyncObservableCollection<Chart> ChartList {
            get {
                return _chartList;
            }
            set {
                _chartList = value;
                RaisePropertyChanged();
            }
        }

        public bool ChartListSelectable {
            get {
                return _chartListSelectable;
            }
            set {
                _chartListSelectable = value;
                RaisePropertyChanged();
            }
        }

        public Chart SelectedChart {
            get => _selectedChart;
            set {
                _selectedChart = value;
                RaisePropertyChanged();
            }
        }

        public IAsyncCommand SelectionChangedCommand { get; private set; }

        public IAsyncCommand StartAutoFocusCommand { get; private set; }

        public ApplicationStatus Status {
            get {
                return _status;
            }
            set {
                _status = value;
                _status.Source = Title;
                RaisePropertyChanged();

                this.applicationStatusMediator.StatusUpdate(_status);
            }
        }

        private void CancelAutoFocus(object obj) {
            _autoFocusCancelToken?.Cancel();
        }

        private FilterInfo CommandInitialization() {
            _autoFocusCancelToken?.Dispose();
            _autoFocusCancelToken = new CancellationTokenSource();
            var filterInfo = filterWheelMediator.GetInfo();
            FilterInfo filter = null;
            if (filterInfo?.SelectedFilter != null) {
                filter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(x => x.Position == filterInfo.SelectedFilter.Position).FirstOrDefault();
            }
            return filter;
        }

        private AsyncObservableCollection<Chart> ListCharts() {
            var files = Directory.GetFiles(Path.Combine(global::NINA.WPF.Base.ViewModel.AutoFocus.AutoFocusVM.ReportDirectory));
            foreach (String file in files) {
                var item = new Chart(Path.GetFileName(file), file);
                if (!ChartList.Any(x => x.Name == item.Name))
                    ChartList.Add(item);
            }
            return ChartList;
        }

        public void Dispose() {
            this.cameraMediator.RemoveConsumer(this);
            this.focuserMediator.RemoveConsumer(this);
        }

        public async Task<bool> LoadChart() {
            if (SelectedChart != null) {
                ListCharts();
                var comparer = new FocusPointComparer();
                var plotComparer = new PlotPointComparer();
                AutoFocusVM.FocusPoints.Clear();
                AutoFocusVM.PlotFocusPoints.Clear();

                using (var reader = File.OpenText(SelectedChart.FilePath)) {
                    var text = await reader.ReadToEndAsync();
                    var report = JsonConvert.DeserializeObject<AutoFocusReport>(text);

                    if (Enum.TryParse<AFCurveFittingEnum>(report.Fitting, out var afCurveFittingEnum)) {
                        AutoFocusVM.FinalFocusPoint = new DataPoint(report.CalculatedFocusPoint.Position, report.CalculatedFocusPoint.Value);
                        AutoFocusVM.LastAutoFocusPoint = new ReportAutoFocusPoint { Focuspoint = AutoFocusVM.FinalFocusPoint, Temperature = report.Temperature, Timestamp = report.Timestamp, Filter = report.Filter };

                        foreach (FocusPoint fp in report.MeasurePoints) {
                            AutoFocusVM.FocusPoints.AddSorted(new ScatterErrorPoint(Convert.ToInt32(fp.Position), fp.Value, 0, fp.Error), comparer);
                            AutoFocusVM.PlotFocusPoints.AddSorted(new DataPoint(Convert.ToInt32(fp.Position), fp.Value), plotComparer);
                        }

                        AutoFocusVM.AutoFocusChartMethod = report.Method == AFMethodEnum.STARHFR.ToString() ? AFMethodEnum.STARHFR : AFMethodEnum.CONTRASTDETECTION;
                        AutoFocusVM.AutoFocusChartCurveFitting = afCurveFittingEnum;
                        AutoFocusVM.SetCurveFittings(report.Method, report.Fitting);
                    }

                    return true;
                }
            }
            return false;
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
            this.focuserInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(CameraInfo deviceInfo) {
            this.cameraInfo = deviceInfo;
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            ;
        }

        public void UpdateUserFocused(FocuserInfo info) {
            ;
        }
    }
}
