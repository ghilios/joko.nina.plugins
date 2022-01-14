#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using static NINA.WPF.Base.ViewModel.Imaging.AutoFocusToolVM;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    // TODO: Remove this code after merging the UI changes into NINA core
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
        private readonly FileSystemWatcher reportFileWatcher;

        [ImportingConstructor]
        public HocusFocusToolVM(
                IProfileService profileService,
                ICameraMediator cameraMediator,
                IFilterWheelMediator filterWheelMediator,
                IFocuserMediator focuserMediator,
                IApplicationStatusMediator applicationStatusMediator,
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

            this.HocusFocusVM = (HocusFocusVM)new HocusFocusVMFactory(profileService, cameraMediator, filterWheelMediator, focuserMediator, guiderMediator, imagingMediator, starDetectionSelector).Create();

            ChartList = new AsyncObservableCollection<Chart>();
            ChartListSelectable = true;

            reportFileWatcher = new FileSystemWatcher() {
                Path = HocusFocusVM.ReportDirectory,
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.json",
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };
            reportFileWatcher.Created += ReportFileWatcher_Created;
            reportFileWatcher.Deleted += ReportFileWatcher_Deleted;

            Task.Run(() => { InitializeChartList(); });

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
                                    lock (lockobj) {
                                        ChartList.Add(new Chart(latestReport.Name, latestReport.FullName));
                                    }
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

        public IAutoFocusVM AutoFocusVM { get => HocusFocusVM; }

        public HocusFocusVM HocusFocusVM { get; }

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

        private object lockobj = new object();

        private void InitializeChartList() {
            var files = Directory.GetFiles(Path.Combine(HocusFocusVM.ReportDirectory));
            var l = new SortedSet<Chart>(new ChartComparer());

            foreach (string file in files) {
                var item = new Chart(Path.GetFileName(file), file);
                l.Add(item);
            }
            lock (lockobj) {
                ChartList = new AsyncObservableCollection<Chart>(l);
                SelectedChart = ChartList.FirstOrDefault();
            }
        }

        private void ReportFileWatcher_Created(object sender, FileSystemEventArgs e) {
            var item = new Chart(Path.GetFileName(e.FullPath), e.FullPath);

            lock (lockobj) {
                ChartList.Insert(0, item);
                // If an AutoFocus in this instance is in progress, we'll skip reloading the chart later but at least show the new item here
                // If multiple AFs are happening within NINA, there are bigger problems
                SelectedChart = item;
            }
        }

        private void ReportFileWatcher_Deleted(object sender, FileSystemEventArgs e) {
            lock (lockobj) {
                var toRemove = ChartList.FirstOrDefault(x => x.FilePath == e.FullPath);
                if (toRemove != null) {
                    ChartList.Remove(toRemove);
                    if (SelectedChart == null) {
                        SelectedChart = ChartList.FirstOrDefault();
                    }
                }
            }
        }

        public void Dispose() {
            this.cameraMediator.RemoveConsumer(this);
            this.focuserMediator.RemoveConsumer(this);
        }

        public async Task<bool> LoadChart() {
            Chart selectedChart;
            lock (lockobj) {
                selectedChart = SelectedChart;
            }

            if (selectedChart != null && !HocusFocusVM.AutoFocusInProgress) {
                HocusFocusReport report;
                using (var reader = File.OpenText(selectedChart.FilePath)) {
                    var text = await reader.ReadToEndAsync();
                    report = JsonConvert.DeserializeObject<HocusFocusReport>(text);
                }

                var comparer = new WPF.Base.ViewModel.AutoFocus.FocusPointComparer();
                var plotComparer = new WPF.Base.ViewModel.AutoFocus.PlotPointComparer();
                HocusFocusVM.FocusPoints.Clear();
                HocusFocusVM.PlotFocusPoints.Clear();

                if (Enum.TryParse<AFCurveFittingEnum>(report.Fitting, out var afCurveFittingEnum)) {
                    HocusFocusVM.FinalFocusPoint = new DataPoint(report.CalculatedFocusPoint.Position, report.CalculatedFocusPoint.Value);
                    HocusFocusVM.LastAutoFocusPoint = new ReportAutoFocusPoint { Focuspoint = AutoFocusVM.FinalFocusPoint, Temperature = report.Temperature, Timestamp = report.Timestamp, Filter = report.Filter };
                    HocusFocusVM.InitialFocuserPosition = (int)Math.Round(report.InitialFocusPoint.Position);
                    HocusFocusVM.InitialHFR = report.InitialFocusPoint.Value;
                    HocusFocusVM.FinalHFR = report.FinalHFR;
                    HocusFocusVM.FinalFocuserPosition = (int)Math.Round(report.CalculatedFocusPoint.Position);

                    foreach (FocusPoint fp in report.MeasurePoints) {
                        HocusFocusVM.FocusPoints.AddSorted(new ScatterErrorPoint(Convert.ToInt32(fp.Position), fp.Value, 0, fp.Error), comparer);
                        HocusFocusVM.PlotFocusPoints.AddSorted(new DataPoint(Convert.ToInt32(fp.Position), fp.Value), plotComparer);
                    }

                    HocusFocusVM.AutoFocusChartMethod = report.Method == AFMethodEnum.STARHFR.ToString() ? AFMethodEnum.STARHFR : AFMethodEnum.CONTRASTDETECTION;
                    HocusFocusVM.AutoFocusChartCurveFitting = afCurveFittingEnum;
                    HocusFocusVM.SetCurveFittings(report.Method, report.Fitting);
                }
                return true;
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