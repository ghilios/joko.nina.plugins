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
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class HocusFocusVM : BaseVM, IAutoFocusVM {
        private static readonly FocusPointComparer focusPointComparer = new FocusPointComparer();
        private static readonly PlotPointComparer plotPointComparer = new PlotPointComparer();

        private AFCurveFittingEnum autoFocusChartCurveFitting;
        private AFMethodEnum autoFocusChartMethod;
        private DataPoint finalFocusPoint;
        private AsyncObservableCollection<ScatterErrorPoint> focusPointsObservable;
        private GaussianFitting gaussianFitting;
        private HyperbolicFitting hyperbolicFitting;
        private ReportAutoFocusPoint lastAutoFocusPoint;
        private AsyncObservableCollection<DataPoint> plotFocusPointsObservable;
        private AsyncObservableCollection<ScatterPoint> plotRejectedFocusPointsObservable;
        private QuadraticFitting quadraticFitting;
        private TrendlineFitting trendLineFitting;
        private TimeSpan autoFocusDuration;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IFocuserMediator focuserMediator;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IProgress<ApplicationStatus> progress;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        public static readonly string ReportDirectory = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus");

        static HocusFocusVM() {
            if (!Directory.Exists(ReportDirectory)) {
                Directory.CreateDirectory(ReportDirectory);
            } else {
                CoreUtil.DirectoryCleanup(ReportDirectory, TimeSpan.FromDays(-180));
            }
        }

        public HocusFocusVM(
            IProfileService profileService,
            IFocuserMediator focuserMediator,
            IAutoFocusEngineFactory autoFocusEngineFactory,
            IAutoFocusOptions autoFocusOptions,
            IStarDetectionOptions starDetectionOptions,
            IFilterWheelMediator filterWheelMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IAlglibAPI alglibAPI
        ) : base(profileService) {
            this.focuserMediator = focuserMediator;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionSelector = starDetectionSelector;
            this.starDetectionOptions = starDetectionOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;

            FocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotFocusPoints = new AsyncObservableCollection<DataPoint>();
            PlotRejectedFocusPoints = new AsyncObservableCollection<ScatterPoint>();
            ClearCharts();

            this.progress = ProgressFactory.Create(applicationStatusMediator, "Hocus Focus");

            LoadSavedAutoFocusRunCommand = new AsyncCommand<bool>(() => {
                string path = "";
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                    if (!String.IsNullOrEmpty(autoFocusOptions.LastSelectedLoadPath)) {
                        dialog.SelectedPath = autoFocusOptions.LastSelectedLoadPath;
                    }
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                        return Task.FromResult(false);
                    }

                    path = dialog.SelectedPath;
                    autoFocusOptions.LastSelectedLoadPath = path;
                }
                return Task.Run(() => LoadSavedAutoFocusRun(path));
            });
            CancelLoadSavedAutoFocusRunCommand = new RelayCommand(CancelLoadSavedAutoFocusRun);
        }

        private readonly IAlglibAPI alglibAPI;

        public AFCurveFittingEnum AutoFocusChartCurveFitting {
            get {
                return autoFocusChartCurveFitting;
            }
            set {
                autoFocusChartCurveFitting = value;
                RaisePropertyChanged();
            }
        }

        public AFMethodEnum AutoFocusChartMethod {
            get {
                return autoFocusChartMethod;
            }
            set {
                autoFocusChartMethod = value;
                RaisePropertyChanged();
            }
        }

        private int initialFocuserPosition = -1;

        public int InitialFocuserPosition {
            get => initialFocuserPosition;
            set {
                if (initialFocuserPosition != value) {
                    initialFocuserPosition = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int finalFocuserPosition = -1;

        public int FinalFocuserPosition {
            get => finalFocuserPosition;
            set {
                if (finalFocuserPosition != value) {
                    finalFocuserPosition = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double initialHFR = 0.0d;

        public double InitialHFR {
            get => initialHFR;
            set {
                if (initialHFR != value) {
                    initialHFR = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double finalHFR = 0.0d;

        public double FinalHFR {
            get => finalHFR;
            set {
                if (finalHFR != value) {
                    finalHFR = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double AverageContrast { get; private set; }
        public double ContrastStdev { get; private set; }

        public DataPoint FinalFocusPoint {
            get {
                return finalFocusPoint;
            }
            set {
                finalFocusPoint = value;
                FinalFocuserPosition = (int)Math.Round(finalFocusPoint.X);
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<ScatterErrorPoint> FocusPoints {
            get {
                return focusPointsObservable;
            }
            set {
                focusPointsObservable = value;
                RaisePropertyChanged();
            }
        }

        public GaussianFitting GaussianFitting {
            get {
                return gaussianFitting;
            }
            set {
                gaussianFitting = value;
                RaisePropertyChanged();
            }
        }

        public HyperbolicFitting HyperbolicFitting {
            get {
                return hyperbolicFitting;
            }
            set {
                hyperbolicFitting = value;
                RaisePropertyChanged();
            }
        }

        public ReportAutoFocusPoint LastAutoFocusPoint {
            get {
                return lastAutoFocusPoint;
            }
            set {
                lastAutoFocusPoint = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<DataPoint> PlotFocusPoints {
            get {
                return plotFocusPointsObservable;
            }
            set {
                plotFocusPointsObservable = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<ScatterPoint> PlotRejectedFocusPoints {
            get {
                return plotRejectedFocusPointsObservable;
            }
            set {
                plotRejectedFocusPointsObservable = value;
                RaisePropertyChanged();
            }
        }

        public QuadraticFitting QuadraticFitting {
            get => quadraticFitting;
            set {
                quadraticFitting = value;
                RaisePropertyChanged();
            }
        }

        public TrendlineFitting TrendlineFitting {
            get => trendLineFitting;
            set {
                trendLineFitting = value;
                RaisePropertyChanged();
            }
        }

        public TimeSpan AutoFocusDuration {
            get => autoFocusDuration;
            set {
                if (autoFocusDuration != value) {
                    autoFocusDuration = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void ClearCharts() {
            InitialHFR = 0.0d;
            FinalHFR = 0.0d;
            InitialFocuserPosition = -1;
            FinalFocuserPosition = -1;
            AutoFocusChartMethod = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod;
            AutoFocusChartCurveFitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;
            FocusPoints.Clear();
            PlotFocusPoints.Clear();
            PlotRejectedFocusPoints.Clear();
            TrendlineFitting = null;
            QuadraticFitting = null;
            HyperbolicFitting = null;
            GaussianFitting = null;
            FinalFocusPoint = new DataPoint(-1.0d, 0);
            LastAutoFocusPoint = new ReportAutoFocusPoint() {
                Focuspoint = new DataPoint(-1.0d, 0.0d),
                Temperature = double.NaN
            };
            AutoFocusDuration = TimeSpan.Zero;
        }

        /// <summary>
        /// Generates a JSON report into %localappdata%\NINA\AutoFocus for the complete autofocus run containing all the measurements
        /// </summary>
        /// <param name="initialFocusPosition"></param>
        /// <param name="initialHFR"></param>
        private AutoFocusReport GenerateReport(
            double initialFocusPosition,
            double initialHFR,
            double finalHFR,
            string filter,
            DataPoint finalFocusPoint,
            ReportAutoFocusPoint lastAutoFocusPoint,
            StarDetectionRegion region,
            TimeSpan duration) {
            var temperature = focuserMediator.GetInfo().Temperature;
            var fittings = new AutoFocusFitting() {
                GaussianFitting = GaussianFitting,
                QuadraticFitting = QuadraticFitting,
                HyperbolicFitting = HyperbolicFitting,
                TrendlineFitting = TrendlineFitting,
                Method = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod,
                CurveFittingType = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting
            };
            return GenerateReport(
                profileService: profileService,
                starDetector: starDetectionSelector.GetBehavior(),
                focusPoints: FocusPoints,
                fittings: fittings,
                initialFocusPosition: initialFocusPosition,
                initialHFR: initialHFR,
                finalHFR: finalHFR,
                filter: filter,
                temperature: temperature,
                finalFocusPoint: finalFocusPoint,
                lastAutoFocusPoint: lastAutoFocusPoint,
                region: region,
                starDetectionOptions: this.starDetectionOptions,
                autoFocusOptions: this.autoFocusOptions,
                duration: duration);
        }

        public static AutoFocusReport GenerateReport(
            IProfileService profileService,
            IAutoFocusOptions autoFocusOptions,
            IStarDetectionOptions starDetectionOptions,
            IStarDetection starDetector,
            ICollection<ScatterErrorPoint> focusPoints,
            AutoFocusFitting fittings,
            double initialFocusPosition,
            double initialHFR,
            double finalHFR,
            string filter,
            double temperature,
            DataPoint finalFocusPoint,
            ReportAutoFocusPoint lastAutoFocusPoint,
            StarDetectionRegion region,
            TimeSpan duration) {
            try {
                var report = HocusFocusReport.GenerateReport(
                    profileService,
                    starDetector,
                    focusPoints,
                    initialFocusPosition,
                    initialHFR,
                    finalHFR,
                    finalFocusPoint,
                    fittings,
                    lastAutoFocusPoint,
                    temperature,
                    filter,
                    region,
                    starDetectionOptions,
                    autoFocusOptions,
                    duration
                );

                var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                string path = Path.Combine(ReportDirectory, DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + ".json");
                File.WriteAllText(path, reportText);
                return report;
            } catch (Exception ex) {
                Logger.Error(ex);
                return null;
            }
        }

        public void SetCurveFittings(string method, string fitting) {
            var validFocusPoints = FocusPoints.Where(fp => fp.Y > 0.0).ToList();

            if (AFMethodEnum.STARHFR.ToString() == method) {
                if (validFocusPoints.Count() >= 3) {
                    if (AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDLINES.ToString() == fitting) {
                        TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method);
                    }

                    if (AFCurveFittingEnum.PARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting) {
                        QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                    }

                    if (AFCurveFittingEnum.HYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting) {
                        AlglibHyperbolicFitting hf;
                        if (!autoFocusOptions.UnevenHyperbolicFitEnabled) {
                            hf = HyperbolicUnevenFittingAlglib.Create(this.alglibAPI, validFocusPoints, profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                        } else {
                            hf = HyperbolicFittingAlglib.Create(this.alglibAPI, validFocusPoints, autoFocusOptions.WeightedHyperbolicFitEnabled);
                        }

                        hf.Solve();
                        HyperbolicFitting = hf;
                    }
                }
            } else if (validFocusPoints.Count() >= 3) {
                TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method);
                GaussianFitting = new GaussianFitting().Calculate(validFocusPoints);
            }
        }

        public async Task<AutoFocusReport> StartAutoFocus(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            try {
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return null;
                }
                AutoFocusInProgress = true;

                var autoFocusEngine = autoFocusEngineFactory.Create();
                autoFocusEngine.Started += AutoFocusEngine_AutoFocusStarted;
                autoFocusEngine.InitialHFRCalculated += AutoFocusEngine_InitialHFRCalculated;
                autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.Completed += AutoFocusEngine_Completed;
                autoFocusEngine.Failed += AutoFocusEngine_Failed;
                var options = autoFocusEngine.GetOptions();
                var result = await autoFocusEngine.Run(options, imagingFilter, token, progress);
                if (result == null || !result.Succeeded) {
                    return null;
                }
                InitialFocuserPosition = result.InitialFocuserPosition;
                return LastReport;
            } finally {
                AutoFocusInProgress = false;
            }
        }

        public AutoFocusReport LastReport { get; private set; }

        private void AutoFocusEngine_Completed(object sender, AutoFocusCompletedEventArgs e) {
            var firstRegion = e.RegionHFRs[0];
            AutoFocusEngine_CompletedNoReport(sender, e);

            var report = GenerateReport(
                initialFocusPosition: e.InitialFocusPosition,
                initialHFR: firstRegion.InitialHFR ?? 0.0d,
                finalHFR: firstRegion.FinalHFR ?? firstRegion.EstimatedFinalHFR,
                filter: e.Filter,
                finalFocusPoint: FinalFocusPoint,
                lastAutoFocusPoint: LastAutoFocusPoint,
                region: firstRegion.Region,
                duration: e.Duration);
            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                var regionIndex = 0;
                var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                File.WriteAllText(targetFilePath, reportText);
            }

            var autoFocusInfo = new AutoFocusInfo(report.Temperature, report.CalculatedFocusPoint.Position, report.Filter, report.Timestamp);
            focuserMediator.BroadcastSuccessfulAutoFocusRun(autoFocusInfo);

            LastReport = report;
        }

        private void AutoFocusEngine_Failed(object sender, AutoFocusFailedEventArgs e) {
            var firstRegion = e.RegionHFRs[0];
            AutoFocusEngine_CompletedNoReport(sender, e);
            var report = GenerateReport(
                initialFocusPosition: e.InitialFocusPosition,
                initialHFR: firstRegion.InitialHFR ?? 0.0d,
                finalHFR: firstRegion.FinalHFR ?? firstRegion.EstimatedFinalHFR,
                filter: e.Filter,
                finalFocusPoint: FinalFocusPoint,
                lastAutoFocusPoint: LastAutoFocusPoint,
                region: firstRegion.Region,
                duration: e.Duration);

            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                var regionIndex = 0;
                var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                File.WriteAllText(targetFilePath, reportText);
            }
            LastReport = report;
        }

        private void AutoFocusEngine_IterationFailed(object sender, AutoFocusFailedEventArgs e) {
            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                var regionIndex = 0;
                var firstRegion = e.RegionHFRs[regionIndex];
                AutoFocusEngine_CompletedNoReport(sender, e);
                var report = HocusFocusReport.GenerateReport(
                    profileService: this.profileService,
                    starDetector: starDetectionSelector.GetBehavior(),
                    focusPoints: FocusPoints,
                    fittings: firstRegion.Fittings,
                    initialFocusPosition: e.InitialFocusPosition,
                    initialHFR: firstRegion.InitialHFR ?? 0.0d,
                    finalHFR: firstRegion.FinalHFR ?? firstRegion.EstimatedFinalHFR,
                    filter: e.Filter,
                    temperature: e.Temperature,
                    focusPoint: FinalFocusPoint,
                    lastFocusPoint: LastAutoFocusPoint,
                    region: firstRegion.Region,
                    hocusFocusStarDetectionOptions: this.starDetectionOptions,
                    hocusFocusAutoFocusOptions: this.autoFocusOptions,
                    duration: e.Duration);

                var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                var targetFilePath = Path.Combine(e.SaveFolder, $"attempt{e.Iteration:00}", $"autofocus_report_Region{regionIndex}.json");
                File.WriteAllText(targetFilePath, reportText);
            }

            FocusPoints.Clear();
            PlotFocusPoints.Clear();
            PlotRejectedFocusPoints.Clear();
        }

        private void AutoFocusEngine_CompletedNoReport(object sender, AutoFocusFinishedEventArgsBase e) {
            var firstRegion = e.RegionHFRs[0];

            FinalFocusPoint = new DataPoint(firstRegion.EstimatedFinalFocuserPosition, firstRegion.EstimatedFinalHFR);
            LastAutoFocusPoint = new ReportAutoFocusPoint {
                Focuspoint = FinalFocusPoint,
                Temperature = e.Temperature,
                Timestamp = DateTime.Now,
                Filter = e.Filter
            };
            if (firstRegion.FinalHFR.HasValue) {
                FinalHFR = firstRegion.FinalHFR.Value;
            }

            AutoFocusDuration = e.Duration;
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex != 0) {
                return;
            }

            FocusPoints.AddSorted(new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, Math.Max(0.001, e.Measurement.Stdev)), focusPointComparer);
            PlotFocusPoints.AddSorted(new DataPoint(e.FocuserPosition, e.Measurement.Measure), plotPointComparer);

            PlotRejectedFocusPoints.Clear();
            foreach (var rp in e.RejectedPoints) {
                PlotRejectedFocusPoints.Add(new ScatterPoint(rp.FocuserPosition, rp.Measurement.Measure));
            }

            this.TrendlineFitting = e.Fittings.TrendlineFitting;
            this.GaussianFitting = e.Fittings.GaussianFitting;
            this.HyperbolicFitting = e.Fittings.HyperbolicFitting;
            this.QuadraticFitting = e.Fittings.QuadraticFitting;
        }

        private void AutoFocusEngine_InitialHFRCalculated(object sender, AutoFocusInitialHFRCalculatedEventArgs e) {
            this.InitialHFR = e.InitialHFR.Measure;
        }

        private void AutoFocusEngine_AutoFocusStarted(object sender, AutoFocusStartedEventArgs e) {
            this.ClearCharts();

            this.LastAutoFocusPoint = new ReportAutoFocusPoint() {
                Focuspoint = new DataPoint(-1.0d, 0.0d),
                Temperature = focuserMediator.GetInfo().Temperature,
                Timestamp = DateTime.Now
            };
        }

        public ICommand LoadSavedAutoFocusRunCommand { get; private set; }
        public ICommand CancelLoadSavedAutoFocusRunCommand { get; private set; }

        private CancellationTokenSource loadSavedAutoFocusRunCts;

        private async Task<bool> LoadSavedAutoFocusRun(string selectedPath) {
            try {
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return false;
                }
                AutoFocusInProgress = true;

                loadSavedAutoFocusRunCts?.Cancel();
                loadSavedAutoFocusRunCts = new CancellationTokenSource();
                var autoFocusEngine = autoFocusEngineFactory.Create();
                SavedAutoFocusAttempt savedAttempt;

                try {
                    savedAttempt = autoFocusEngine.LoadSavedAutoFocusAttempt(selectedPath);
                } catch (Exception e) {
                    Notification.ShowError(e.Message);
                    Logger.Error($"Failed to load saved auto focus attempt from {selectedPath}", e);
                    return false;
                }

                autoFocusEngine.Started += AutoFocusEngine_AutoFocusStarted;
                autoFocusEngine.InitialHFRCalculated += AutoFocusEngine_InitialHFRCalculated;
                autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.Completed += AutoFocusEngine_CompletedNoReport;

                var filterInfo = filterWheelMediator.GetInfo();
                FilterInfo imagingFilter = null;
                if (filterInfo?.SelectedFilter != null) {
                    imagingFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(x => x.Position == filterInfo.SelectedFilter.Position).FirstOrDefault();
                }

                var options = autoFocusEngine.GetOptions(savedAttempt);
                var result = await autoFocusEngine.Rerun(options, savedAttempt, imagingFilter, loadSavedAutoFocusRunCts.Token, this.progress);
                if (result != null) {
                    InitialFocuserPosition = result.InitialFocuserPosition;
                    return result.Succeeded;
                }
                return false;
            } catch (OperationCanceledException) {
                Logger.Info("Load saved auto focus run canceled");
                return false;
            } catch (Exception e) {
                Notification.ShowError($"Failed reprocessing saved AF: {e.Message}");
                Logger.Error("Failed reprocessing saved AF", e);
                return false;
            } finally {
                AutoFocusInProgress = false;
            }
        }

        private void CancelLoadSavedAutoFocusRun(object o) {
            loadSavedAutoFocusRunCts?.Cancel();
        }

        private bool autoFocusInProgress;

        public bool AutoFocusInProgress {
            get => autoFocusInProgress;
            private set {
                if (autoFocusInProgress != value) {
                    autoFocusInProgress = value;
                    RaisePropertyChanged();
                }
            }
        }
    }
}