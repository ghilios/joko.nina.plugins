#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Review;
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
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class HocusFocusVM : BaseVM, IAutoFocusVM {
        private static readonly FocusPointComparer focusPointComparer = new FocusPointComparer();
        private static readonly PlotPointComparer plotPointComparer = new PlotPointComparer();

        private AFCurveFittingEnum autoFocusChartCurveFitting;
        private AFMethodEnum autoFocusChartMethod;
        private DataPoint finalFocusPoint;
        private AsyncObservableCollection<ScatterErrorPoint> focusPointsObservable;
        private AsyncObservableCollection<ScatterErrorPoint> plotFinalFocusPointWithErrorObservable;
        private GaussianFitting gaussianFitting;
        private HyperbolicFitting hyperbolicFitting;
        private HyperbolicFitModel? selectedHyperbolicFitModel;
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
        private readonly IApplicationDispatcher applicationDispatcher;

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type), so it is instantiated directly,
        // mirroring InspectorVM / HocusFocusPlugin.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

        // ---- Review Frames (manual-AF only) -------------------------------------------------------------
        // Each retained per-frame (focuser position, detection result, image) tuple is accumulated under the lock as
        // SubMeasurementPointCompleted fires concurrently across focuser positions, then built into the immutable
        // reviewSnapshot at run completion. frameReviewRequestedForRun freezes the "keep frames" decision at run start
        // (when PreserveExposures is decided) so a mid-run toggle change can't desync image retention from the build.
        private readonly object frameReviewLock = new object();
        private readonly List<(double FocuserPosition, HocusFocusStarDetectionResult Result, IRenderedImage Image)> reviewFrames
            = new List<(double, HocusFocusStarDetectionResult, IRenderedImage)>();
        private bool frameReviewRequestedForRun;
        // Written on the engine's background completion thread (BuildFrameReviewSnapshotIfRequested) and on the
        // close handler, read on the UI thread (ReviewFramesAvailable / ShowFrameReview). Reference reads are
        // atomic; volatile (plus the blocking Send in NotifyReviewFramesAvailabilityChanged) establishes the
        // happens-before so UI reads see the built snapshot rather than a stale reference. (F21)
        private volatile AutoFocusFrameReviewSnapshot reviewSnapshot;

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
            IAlglibAPI alglibAPI,
            IApplicationDispatcher applicationDispatcher
        ) : base(profileService) {
            this.focuserMediator = focuserMediator;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionSelector = starDetectionSelector;
            this.starDetectionOptions = starDetectionOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
            this.applicationDispatcher = applicationDispatcher;

            FocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotFinalFocusPointWithError = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotFocusPoints = new AsyncObservableCollection<DataPoint>();
            PlotRejectedFocusPoints = new AsyncObservableCollection<ScatterPoint>();
            ClearCharts();

            this.progress = ProgressFactory.Create(applicationStatusMediator, "Hocus Focus");

            LoadSavedAutoFocusRunCommand = new AsyncRelayCommand(() => {
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
            ReviewFramesCommand = new RelayCommand(ShowFrameReview, () => ReviewFramesAvailable);
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

        /// <summary>
        /// Single-element series carrying the final best-focus point with a horizontal error bar (ErrorX) equal to
        /// σ(focus) — or the leave-one-out stability when σ(focus) is unavailable — so the chart shows the
        /// uncertainty of the calculated focus position along the focuser axis. Empty when neither estimate exists
        /// (e.g. non-hyperbolic fits), so nothing is drawn.
        /// </summary>
        public AsyncObservableCollection<ScatterErrorPoint> PlotFinalFocusPointWithError {
            get {
                return plotFinalFocusPointWithErrorObservable;
            }
            set {
                plotFinalFocusPointWithErrorObservable = value;
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

        /// <summary>
        /// The concrete hyperbolic model chosen for a Hybrid run (null for non-Hybrid runs), surfaced in the AF
        /// metrics panel so the user can see which model the run actually settled on.
        /// </summary>
        public HyperbolicFitModel? SelectedHyperbolicFitModel {
            get => selectedHyperbolicFitModel;
            set {
                selectedHyperbolicFitModel = value;
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
            PlotFinalFocusPointWithError.Clear();
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
                SelectedHyperbolicFitModel = SelectedHyperbolicFitModel,
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
                string path = Path.Combine(ReportDirectory, $"{DateTime.Now:yyyy-MM-dd--HH-mm-ss}--{profileService.ActiveProfile.Id}.json");
                File.WriteAllText(path, reportText);
                return report;
            } catch (Exception ex) {
                Logger.Error(ex);
                return null;
            }
        }

        public void SetCurveFittings(string method, string fitting) {
            // NINA core's saved-chart reload (AutoFocusToolVM.LoadChart) rebuilds FocusPoints from a
            // saved report's raw Error values and calls this method — another fit entry point, so the
            // same regularization the live engine applies must happen here too.
            var validFocusPoints = WeightRegularization.Regularize(FocusPoints.Where(fp => fp.Y > 0.0).ToList());

            if (AFMethodEnum.STARHFR.ToString() == method) {
                if (validFocusPoints.Count() >= 3) {
                    if (AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDLINES.ToString() == fitting) {
                        TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method);
                    }

                    if (AFCurveFittingEnum.PARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting) {
                        QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                    }

                    if (AFCurveFittingEnum.HYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting) {
                        var stepSize = profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize;
                        // When the option is Hybrid, reproduce the engine's best-fit pick on this saved point set so
                        // a reloaded run shows the concrete chosen model (and its LOO) instead of falling back to
                        // Tilted. SelectBestModel returns the already-solved winning fit, so no extra Solve() is needed.
                        AlglibHyperbolicFitting hf;
                        var modelForRun = autoFocusOptions.HyperbolicFitModel;
                        if (modelForRun == HyperbolicFitModel.Hybrid) {
                            modelForRun = AlglibHyperbolicFitting.SelectBestModel(this.alglibAPI, validFocusPoints, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled, out hf);
                            if (hf == null) {
                                hf = AlglibHyperbolicFitting.Create(this.alglibAPI, modelForRun, validFocusPoints, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                                hf.Solve();
                            }
                        } else {
                            hf = AlglibHyperbolicFitting.Create(this.alglibAPI, modelForRun, validFocusPoints, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                            hf.Solve();
                        }
                        // Best-focus stability is a one-time computation here (saved-run display), so unlike the
                        // live engine path it is safe to compute it directly after solving the final curve. Use the
                        // resolved concrete model so the LOO matches the chosen curve.
                        hf.LeaveOneOutStdError = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                            this.alglibAPI, modelForRun, validFocusPoints, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                        HyperbolicFitting = hf;
                    }
                }
            } else if (validFocusPoints.Count() >= 3) {
                TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method);
                GaussianFitting = new GaussianFitting().Calculate(validFocusPoints);
            }
            RefreshFinalFocusPointError();
        }

        /// <summary>
        /// Rebuilds <see cref="PlotFinalFocusPointWithError"/> from the current hyperbolic fit: a single point at the
        /// best-focus minimum with a horizontal error bar (ErrorX) of σ(focus), falling back to the leave-one-out
        /// stability when σ(focus) is unavailable (e.g. a degenerate fit that can't localize focus). Leaves the series empty —
        /// so nothing is drawn — for non-hyperbolic fits or when no uncertainty estimate exists.
        /// </summary>
        private void RefreshFinalFocusPointError() {
            PlotFinalFocusPointWithError.Clear();
            if (!(HyperbolicFitting is AlglibHyperbolicFitting alglibFit)) {
                return;
            }
            var minimum = alglibFit.Minimum;
            if (double.IsNaN(minimum.X) || double.IsInfinity(minimum.X)) {
                return;
            }
            var errorX = alglibFit.MinimumStdError;
            if (double.IsNaN(errorX) || double.IsInfinity(errorX) || errorX <= 0.0) {
                errorX = alglibFit.LeaveOneOutStdError; // σ(focus) unavailable (degenerate fit) → use LOO stability
            }
            if (double.IsNaN(errorX) || double.IsInfinity(errorX) || errorX <= 0.0) {
                return;
            }
            PlotFinalFocusPointWithError.Add(new ScatterErrorPoint(minimum.X, minimum.Y, errorX, 0.0));
        }

        // Single source for the interactive frame-review retention policy used by BOTH StartAutoFocus and
        // LoadSavedAutoFocusRun: retain per-frame images for review only when this VM is interactive (pane) AND the
        // persisted "Keep frames for review" toggle is on. When retaining, force the engine to preserve exposures and
        // model PSFs (iff PSF modeling is enabled in star-detection options) so the review can show PSF-derived
        // per-star properties (AF normally skips PSF fitting for speed). Sets frameReviewRequestedForRun as a side effect.
        private void ApplyFrameReviewOptions(AutoFocusEngineOptions options) {
            frameReviewRequestedForRun = IsInteractive && autoFocusOptions.KeepFramesForReview;
            if (frameReviewRequestedForRun) {
                options.PreserveExposures = true;
                options.ModelPSF = starDetectionOptions.ModelPSF;
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
                autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;
                autoFocusEngine.Completed += AutoFocusEngine_Completed;
                autoFocusEngine.Failed += AutoFocusEngine_Failed;
                var options = autoFocusEngine.GetOptions();

                ApplyFrameReviewOptions(options);
                var result = await autoFocusEngine.Run(options, imagingFilter, token, progress);
                if (result == null || !result.Succeeded) {
                    return null;
                }
                InitialFocuserPosition = result.InitialFocuserPosition;
                return LastReport;
            } finally {
                // A successful run already moved frames into the snapshot (and cleared reviewFrames); this
                // covers cancellation / null-init paths where Completed/Failed never fired, so captured
                // exposures aren't pinned until the next run. (F22)
                ReleaseUnsnapshottedReviewFrames();
                AutoFocusInProgress = false;
            }
        }

        public AutoFocusReport LastReport { get; private set; }

        private String GetAttemptSaveFolder(string saveFolder, int iteration) {
            var parentFolder = Path.Combine(saveFolder, $"attempt{iteration:00}");
            if (!Directory.Exists(parentFolder)) {
                Directory.CreateDirectory(parentFolder);
            }
            return parentFolder;
        }

        private void AutoFocusEngine_Completed(object sender, AutoFocusCompletedEventArgs e) {
            var firstRegion = e.RegionHFRs[0];
            AutoFocusEngine_CompletedNoReport(sender, e);
            Logger.Info($"AutoFocus completed with focuser starting at {e.InitialFocusPosition} and ending at {FinalFocuserPosition}");
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
                var targetFilePath = Path.Combine(GetAttemptSaveFolder(e.SaveFolder, e.Iteration), $"autofocus_report_Region{regionIndex}.json");
                File.WriteAllText(targetFilePath, reportText);
            }

            var autoFocusInfo = new AutoFocusInfo(report.Temperature, report.CalculatedFocusPoint.Position, report.Filter, report.Timestamp);
            focuserMediator.BroadcastSuccessfulAutoFocusRun(autoFocusInfo);

            LastReport = report;
            BuildFrameReviewSnapshotIfRequested();
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
                var targetFilePath = Path.Combine(GetAttemptSaveFolder(e.SaveFolder, e.Iteration), $"autofocus_report_Region{regionIndex}.json");
                File.WriteAllText(targetFilePath, reportText);
            }
            LastReport = report;
            // Build the review snapshot even for a failed sweep — seeing why detection struggled is often the most
            // useful case (the frames were only retained for an interactive pane run with the toggle on).
            BuildFrameReviewSnapshotIfRequested();
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
                var targetFilePath = Path.Combine(GetAttemptSaveFolder(e.SaveFolder, e.Iteration), $"autofocus_report_Region{regionIndex}.json");
                File.WriteAllText(targetFilePath, reportText);
            }

            FocusPoints.Clear();
            PlotFinalFocusPointWithError.Clear();
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

            // During the run the fit properties are updated only from the intermediate per-point fit
            // (AutoFocusEngine_MeasurementPointCompleted). The finalized region fit carries the values computed
            // in the engine's final pass — leave-one-out stability, plus the final-curve σ(focus)/reduced χ² —
            // which the intermediate fit lacks (LOO is always NaN there). Re-sync to it on completion so the
            // panel shows them instead of NaN/stale intermediates, and so the report generated right after this
            // (which serializes these VM properties) records them too. Runs for both live and re-run completions.
            if (firstRegion.Fittings != null) {
                this.TrendlineFitting = firstRegion.Fittings.TrendlineFitting;
                this.QuadraticFitting = firstRegion.Fittings.QuadraticFitting;
                this.GaussianFitting = firstRegion.Fittings.GaussianFitting;
                this.HyperbolicFitting = firstRegion.Fittings.HyperbolicFitting;
                this.SelectedHyperbolicFitModel = firstRegion.Fittings.SelectedHyperbolicFitModel;
            }

            // The graph's rejected-point overlay is filled live, per frame, from each step's winning-model Grubbs
            // flags (AutoFocusEngine_MeasurementPointCompleted). On a Hybrid run the live model is Tilted, so the
            // overlay collects Tilted's own outliers — which the model-fair consensus often keeps (a point is removed
            // only when EVERY model flags it). Re-sync the overlay to the FINAL consensus rejected set so the panel
            // marks only the points selection actually removed, consistent with the fit just re-synced above.
            PlotRejectedFocusPoints.Clear();
            if (firstRegion.RejectedPoints != null) {
                foreach (var rp in firstRegion.RejectedPoints) {
                    PlotRejectedFocusPoints.Add(new ScatterPoint(rp.FocuserPosition, rp.Measurement.Measure));
                }
            }

            RefreshFinalFocusPointError();
            AutoFocusDuration = e.Duration;
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex != 0) {
                return;
            }

            FocusPoints.AddSorted(new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, AutoFocusEngine.SafeDisplayError(e.Measurement.Stdev)), focusPointComparer);
            var dataPoint = new DataPoint(e.FocuserPosition, e.Measurement.Measure);
            PlotFocusPoints.AddSorted(dataPoint, plotPointComparer);
            this.focuserMediator.BroadcastNewAutoFocusPoint(dataPoint);

            PlotRejectedFocusPoints.Clear();
            foreach (var rp in e.RejectedPoints) {
                PlotRejectedFocusPoints.Add(new ScatterPoint(rp.FocuserPosition, rp.Measurement.Measure));
            }

            this.TrendlineFitting = e.Fittings.TrendlineFitting;
            this.GaussianFitting = e.Fittings.GaussianFitting;
            this.HyperbolicFitting = e.Fittings.HyperbolicFitting;
            this.QuadraticFitting = e.Fittings.QuadraticFitting;
        }

        // ---- Review Frames (manual-AF only) -------------------------------------------------------------

        private bool isInteractive;

        /// <summary>
        /// True only for the VM instance hosted in the AutoFocus pane. Toggled exclusively through
        /// <see cref="MarkInteractive"/> / <see cref="MarkNonInteractive"/> (called by InteractiveHostBehavior while the
        /// pane DataTemplate is loaded). Sequence-triggered AF runs construct their own transient VM via the factory,
        /// which is never rendered through the pane template, so it stays false — gating frame retention to interactive
        /// runs. No public setter, so the flag can never be flipped from arbitrary binding/code (F07/F20).
        /// </summary>
        public bool IsInteractive {
            get => isInteractive;
            private set {
                if (isInteractive != value) {
                    isInteractive = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>Explicit opt-in: marks THIS VM instance interactive so its runs retain per-frame images for review.
        /// Called by InteractiveHostBehavior when the pane DataTemplate hosting this VM is loaded (F07/F20).</summary>
        public void MarkInteractive() => IsInteractive = true;

        /// <summary>Explicit opt-out: reverts the interactive flag when the pane unloads or its DataContext is swapped
        /// away, so a recycled host never leaves a non-pane VM marked interactive (F20).</summary>
        public void MarkNonInteractive() => IsInteractive = false;

        /// <summary>Exposes the persisted AF options for the pane's "Keep frames for review" toggle binding (mirrors
        /// InspectorVM.InspectorOptions).</summary>
        public IAutoFocusOptions AutoFocusOptions => this.autoFocusOptions;

        public RelayCommand ReviewFramesCommand { get; }

        /// <summary>Whether a completed run produced reviewable frames (drives the "Review Frames" button).</summary>
        public bool ReviewFramesAvailable => reviewSnapshot?.Frames.Count > 0;

        private void AutoFocusEngine_SubMeasurementPointCompleted(object sender, AutoFocusSubMeasurementPointCompletedEventArgs e) {
            // Standard (non-inspector) AF uses a single region 0; the image is only present when PreserveExposures was
            // forced on (i.e. frameReviewRequestedForRun). Accumulate one tuple per fired frame.
            if (!frameReviewRequestedForRun || e.RegionIndex != 0) {
                return;
            }
            if (e.StarDetectionResult is not HocusFocusStarDetectionResult hf || e.Image == null) {
                return;
            }
            lock (frameReviewLock) {
                reviewFrames.Add((e.FocuserPosition, hf, e.Image));
            }
        }

        private void BuildFrameReviewSnapshotIfRequested() {
            if (!frameReviewRequestedForRun) {
                return;
            }
            List<(double, HocusFocusStarDetectionResult, IRenderedImage)> framesForReview;
            lock (frameReviewLock) {
                framesForReview = reviewFrames
                    .Select(f => (f.FocuserPosition, f.Result, f.Image))
                    .ToList();
                // The snapshot captures only each frame's display BitmapSource; once built, the source
                // IRenderedImage buffers (raw 16-bit pixels + statistics) are no longer needed, so drop
                // our references here instead of keeping them pinned until the next run (F05).
                reviewFrames.Clear();
            }
            reviewSnapshot = AutoFocusFrameReviewSnapshotBuilder.Build(framesForReview);
            NotifyReviewFramesAvailabilityChanged();
        }

        // Release any per-frame images accumulated this run when no snapshot was built (cancellation, or a
        // run that returned without firing Completed/Failed). Safe to call unconditionally — it is a no-op
        // when nothing was retained. (F22)
        private void ReleaseUnsnapshottedReviewFrames() {
            lock (frameReviewLock) {
                reviewFrames.Clear();
            }
        }

        // Raise the availability binding + re-evaluate the command, marshaled to the UI thread (the snapshot is built /
        // cleared on the engine's background task). DispatchSynchronizationContext is a synchronous Send with a
        // same-context fast path, so it is safe to call from either thread.
        private void NotifyReviewFramesAvailabilityChanged() {
            applicationDispatcher.DispatchSynchronizationContext(() => {
                RaisePropertyChanged(nameof(ReviewFramesAvailable));
                ReviewFramesCommand.NotifyCanExecuteChanged();
            });
        }

        // Shows the modal Review Frames dialog. The VM is presented in a ContentPresenter resolved by the implicit
        // DataType DataTemplate for AutoFocusFrameReviewVM, and is disposed when the window closes (releasing the
        // retained frame bitmaps).
        private void ShowFrameReview() {
            var snapshot = reviewSnapshot;
            if (snapshot == null || snapshot.Frames.Count == 0) {
                return;
            }

            var vm = new AutoFocusFrameReviewVM(snapshot, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.ApplicationDispatcher, starDetectionOptions.MeasurementAverage);
            ReviewDialogHost.Show(windowServiceFactory, vm, "Review Frames", () => {
                // vm.Dispose() only nulls the child VM's copy; the pane VM still holds the snapshot's frozen bitmaps
                // (F04) and source IRenderedImage buffers (F06). Release them on close so "released when you close the
                // review window" is honored without waiting for the next run.
                lock (frameReviewLock) {
                    reviewFrames.Clear();
                }
                reviewSnapshot = null;
                NotifyReviewFramesAvailabilityChanged();
            });
        }

        private void AutoFocusEngine_InitialHFRCalculated(object sender, AutoFocusInitialHFRCalculatedEventArgs e) {
            this.InitialHFR = e.InitialHFR.Measure;
        }

        private void AutoFocusEngine_AutoFocusStarted(object sender, AutoFocusStartedEventArgs e) {
            this.ClearCharts();

            // Drop any frames/snapshot from a prior run (a new run supersedes the previous review). frameReviewRequestedForRun
            // is NOT reset here — it is set per-entry-path (StartAutoFocus / LoadSavedAutoFocusRun) before Run() raises Started.
            var hadReviewState = reviewSnapshot != null || frameReviewRequestedForRun;
            lock (frameReviewLock) {
                reviewFrames.Clear();
            }
            reviewSnapshot = null;
            // Only marshal to the UI thread when availability could actually change. On the common non-review end
            // path (sequence-triggered transient VM bound to no UI) the clear is a no-op, so skip the blocking
            // Send that would otherwise stall the AF worker if the dispatcher is busy. (F27)
            if (hadReviewState) {
                NotifyReviewFramesAvailabilityChanged();
            }

            this.focuserMediator.BroadcastAutoFocusRunStarting();
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
                    selectedPath = savedAttempt.FolderPath;
                } catch (Exception e) {
                    Notification.ShowError(e.Message);
                    Logger.Error($"Failed to load saved auto focus attempt from {selectedPath}", e);
                    return false;
                }

                autoFocusEngine.Started += AutoFocusEngine_AutoFocusStarted;
                autoFocusEngine.InitialHFRCalculated += AutoFocusEngine_InitialHFRCalculated;
                autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;
                autoFocusEngine.Completed += AutoFocusEngine_CompletedNoReport;

                var filterInfo = filterWheelMediator.GetInfo();
                FilterInfo imagingFilter = null;
                if (filterInfo?.SelectedFilter != null) {
                    imagingFilter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(x => x.Position == filterInfo.SelectedFilter.Position).FirstOrDefault();
                }

                // If the saved run has a metadata.json, prompt (when interactive) for whether to replay with current
                // settings, the run's capture-time settings in memory, or after updating the profile. With no
                // metadata (or non-interactive), this returns current-settings options and no prompt is shown.
                var resolution = await AutoFocusReplayCoordinator.ResolveAsync(
                    windowServiceFactory,
                    applicationDispatcher,
                    profileService,
                    savedAttempt.FolderPath,
                    IsInteractive,
                    () => autoFocusEngine.GetOptions(savedAttempt));
                if (resolution.Cancelled) {
                    return false;
                }
                var options = resolution.Options;

                // Replay is an interactive pane action, so it supports Review Frames on the same terms as a live run.
                ApplyFrameReviewOptions(options);

                // Option (b) supplies the run's capture-time regions so its ROI is honored through the explicit-region
                // path (the engine consumes the star-detection override there) without mutating the profile. Otherwise
                // use the legacy single-region path, which reads the (current or just-updated) profile crop ROI.
                var result = (resolution.CaptureTimeRegions != null && resolution.CaptureTimeRegions.Count > 0)
                    ? await autoFocusEngine.RerunWithRegions(options, savedAttempt, imagingFilter, resolution.CaptureTimeRegions, loadSavedAutoFocusRunCts.Token, this.progress)
                    : await autoFocusEngine.Rerun(options, savedAttempt, imagingFilter, loadSavedAutoFocusRunCts.Token, this.progress);
                if (result != null) {
                    // The reprocess path wires Completed -> CompletedNoReport (no report handler), so build the review
                    // snapshot here once the replay has finished and every reloaded frame has been collected.
                    BuildFrameReviewSnapshotIfRequested();
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
                ReleaseUnsnapshottedReviewFrames();
                AutoFocusInProgress = false;
            }
        }

        private void CancelLoadSavedAutoFocusRun() {
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
