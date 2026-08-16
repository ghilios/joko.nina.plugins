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
using System.Globalization;
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
        private AsyncObservableCollection<ScatterErrorPoint> plotWindowExcludedFocusPointsObservable;
        private AsyncObservableCollection<ScatterErrorPoint> plotCoreFocusPointsObservable;
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
        private readonly IPerFilterStarDetectionStore perFilterStore;

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
            IApplicationDispatcher applicationDispatcher,
            IPerFilterStarDetectionStore perFilterStore = null
        ) : base(profileService) {
            this.focuserMediator = focuserMediator;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.filterWheelMediator = filterWheelMediator;
            this.starDetectionSelector = starDetectionSelector;
            this.starDetectionOptions = starDetectionOptions;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
            this.applicationDispatcher = applicationDispatcher;
            this.perFilterStore = perFilterStore;

            FocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotFinalFocusPointWithError = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotFocusPoints = new AsyncObservableCollection<DataPoint>();
            PlotRejectedFocusPoints = new AsyncObservableCollection<ScatterPoint>();
            PlotWindowExcludedFocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotCoreFocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
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
        /// The filled AF-curve markers actually drawn on the chart: the measured points MINUS the window-excluded
        /// ones (Behavior A). The chart's filled ScatterErrorSeries binds here, while <see cref="FocusPoints"/> stays
        /// the full measured set used by the saved report and the fit. Keeping them separate is what lets the hollow
        /// "excluded" overlay read as hollow: a window-excluded point left in the filled series would be drawn over
        /// its ring and look identical to an included point. Mirrors the optimizer chart's CorePoints/RecoveryPoints split.
        /// </summary>
        public AsyncObservableCollection<ScatterErrorPoint> PlotCoreFocusPoints {
            get {
                return plotCoreFocusPointsObservable;
            }
            set {
                plotCoreFocusPointsObservable = value;
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

        /// <summary>
        /// Sweep points excluded from the final fit by the symmetric focus window (Behavior A) — points that lie outside
        /// minimum ± (offsetSteps+1)*stepSize. Rendered on the results chart as hollow rings, deliberately distinct from
        /// the red-cross <see cref="PlotRejectedFocusPoints"/> (Grubbs outliers): these points are present and valid, just
        /// too far from focus to inform the fit. Uses ScatterErrorPoint (not ScatterPoint) so the dimmed HFR error bar
        /// renders like the main <see cref="FocusPoints"/> series. Empty when nothing is excluded (well-centered runs).
        /// </summary>
        public AsyncObservableCollection<ScatterErrorPoint> PlotWindowExcludedFocusPoints {
            get {
                return plotWindowExcludedFocusPointsObservable;
            }
            set {
                plotWindowExcludedFocusPointsObservable = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>Gates the "○ excluded — outside focus window" chart legend; true only once the window excludes a point.</summary>
        public bool HasWindowExcludedFocusPoints => PlotWindowExcludedFocusPoints?.Count > 0;

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

        // ---- Stars per accepted curve point --------------------------------------------------------------
        //
        // How many stars the curve was actually built from is the difference between a result to trust and one
        // that happened to land: an eleven-point sweep whose best point found 40 stars is a different object from
        // one whose worst found 400, and nothing else on the panel says which you have.
        //
        // Sourced from SubMeasurementPointCompleted, which already carries each frame's StarDetectionResult, so
        // no engine or event change is needed. That event is raised ONLY by FocusPointMeasurementAction, so the
        // initial-HFR and final-validation frames — which are not curve points — never enter the map.
        // The handler fires concurrently across focuser positions, hence the lock.
        private readonly object starCountLock = new object();

        private readonly Dictionary<int, List<int>> starCountsByFocuserPosition = new Dictionary<int, List<int>>();

        private int acceptedStarCountMin = -1;
        private int acceptedStarCountMax = -1;

        /// <summary>Fewest accepted stars found at any point the curve was fitted on; -1 when not known.</summary>
        public int AcceptedStarCountMin {
            get => acceptedStarCountMin;
            private set {
                if (acceptedStarCountMin != value) {
                    acceptedStarCountMin = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(AcceptedStarCountRangeText));
                    RaisePropertyChanged(nameof(HasAcceptedStarCountRange));
                }
            }
        }

        /// <summary>Most accepted stars found at any point the curve was fitted on; -1 when not known.</summary>
        public int AcceptedStarCountMax {
            get => acceptedStarCountMax;
            private set {
                if (acceptedStarCountMax != value) {
                    acceptedStarCountMax = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(AcceptedStarCountRangeText));
                    RaisePropertyChanged(nameof(HasAcceptedStarCountRange));
                }
            }
        }

        /// <summary>False collapses the row, which is what a run (or a loaded report) that never recorded star
        /// counts must do — showing 0 would read as "found no stars".</summary>
        public bool HasAcceptedStarCountRange => acceptedStarCountMin >= 0 && acceptedStarCountMax >= acceptedStarCountMin;

        /// <summary>"412 – 1,067" over the accepted points, or the single value when the range is degenerate.</summary>
        public string AcceptedStarCountRangeText =>
            !HasAcceptedStarCountRange
                ? string.Empty
                : acceptedStarCountMin == acceptedStarCountMax
                    ? acceptedStarCountMin.ToString("N0", CultureInfo.CurrentCulture)
                    : $"{acceptedStarCountMin.ToString("N0", CultureInfo.CurrentCulture)} – {acceptedStarCountMax.ToString("N0", CultureInfo.CurrentCulture)}";

        /// <summary>Records one frame's accepted-star count against its focuser position. Silently ignores a
        /// measurement with no star detection behind it (contrast-detection AF), which correctly leaves the row
        /// collapsed rather than reporting a range of zeros.
        /// internal so tests can feed per-frame counts without driving a full engine run.</summary>
        internal void RecordFrameStarCount(int focuserPosition, StarDetectionResult result) {
            if (result == null) {
                return;
            }
            lock (starCountLock) {
                if (!starCountsByFocuserPosition.TryGetValue(focuserPosition, out var counts)) {
                    counts = new List<int>();
                    starCountsByFocuserPosition[focuserPosition] = counts;
                }
                counts.Add(result.DetectedStars);
            }
        }

        /// <summary>
        /// Recomputes the min/max over the points the curve is ACTUALLY fitted on: every measured position minus
        /// the Grubbs-rejected ones and minus the symmetric-window exclusions. Both sets are re-synced to their
        /// final values at completion, so this is called from the live per-point handler AND from completion.
        ///
        /// <para>A position measured with FramesPerPoint &gt; 1 is pooled by MEAN across its frames, matching how
        /// <see cref="AutoFocusEngine.TryCompleteFocuserPoint"/> pools that position's HFR into the one point the
        /// fit sees.</para>
        ///
        /// internal so the accepted-vs-rejected partition is testable without driving a full engine run.
        /// </summary>
        internal void UpdateAcceptedStarCountRange(
                IReadOnlyList<AutoFocusRegionPoint> rejectedPoints,
                IReadOnlyList<AutoFocusRegionPoint> windowExcludedPoints) {
            var excluded = new HashSet<int>();
            if (rejectedPoints != null) {
                foreach (var p in rejectedPoints) {
                    excluded.Add(p.FocuserPosition);
                }
            }
            if (windowExcludedPoints != null) {
                foreach (var p in windowExcludedPoints) {
                    excluded.Add(p.FocuserPosition);
                }
            }

            var min = int.MaxValue;
            var max = int.MinValue;
            lock (starCountLock) {
                foreach (var entry in starCountsByFocuserPosition) {
                    if (entry.Value.Count == 0 || excluded.Contains(entry.Key)) {
                        continue;
                    }
                    var pooled = (int)Math.Round(entry.Value.Average(), MidpointRounding.AwayFromZero);
                    if (pooled < min) { min = pooled; }
                    if (pooled > max) { max = pooled; }
                }
            }

            if (min > max) {
                AcceptedStarCountMin = -1;
                AcceptedStarCountMax = -1;
            } else {
                AcceptedStarCountMin = min;
                AcceptedStarCountMax = max;
            }
        }

        /// <summary>Drops the accumulated per-frame counts (a new run, or a failed iteration whose points the
        /// engine has just discarded, supersedes them) and collapses the row.</summary>
        private void ClearAcceptedStarCounts() {
            lock (starCountLock) {
                starCountsByFocuserPosition.Clear();
            }
            AcceptedStarCountMin = -1;
            AcceptedStarCountMax = -1;
        }

        private void ClearCharts() {
            ClearAcceptedStarCounts();
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
            PlotWindowExcludedFocusPoints.Clear();
            PlotCoreFocusPoints.Clear();
            RaisePropertyChanged(nameof(HasWindowExcludedFocusPoints));
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
            var report = GenerateReport(
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
                duration: duration,
                acceptedStarCountMin: AcceptedStarCountMin,
                acceptedStarCountMax: AcceptedStarCountMax);
            if (report != null) {
                MarkReportGenerated(report.Timestamp);
            }
            return report;
        }

        /// <summary>
        /// Records the report timestamp of the run this VM itself produced, so <see cref="SetCurveFittings"/> can tell
        /// "the watcher re-loaded my own just-written chart" (keep the live-only info rows) apart from "a foreign chart
        /// was loaded" (reset them). Internal so reload-survival tests can stamp it without running a full engine cycle.
        /// </summary>
        internal void MarkReportGenerated(DateTime timestamp) {
            lastGeneratedReportTimestamp = timestamp;
            // Snapshot the live-only info rows alongside the timestamp. They are about to become recoverable ONLY
            // from disk: the moment a foreign chart is loaded these three fields are overwritten with that run's
            // values, and returning here must not depend on this VM's own report still being readable.
            liveInitialFocuserPosition = InitialFocuserPosition;
            liveInitialHFR = InitialHFR;
            liveFinalHFR = FinalHFR;
            liveAcceptedStarCountMin = AcceptedStarCountMin;
            liveAcceptedStarCountMax = AcceptedStarCountMax;
        }

        // Timestamp of the last report generated BY this VM (null until a run completes here). See MarkReportGenerated.
        private DateTime? lastGeneratedReportTimestamp;

        // The live-only info rows as this VM's own completed run left them, captured by MarkReportGenerated.
        private int liveInitialFocuserPosition = -1;
        private double liveInitialHFR;
        private double liveFinalHFR;
        private int liveAcceptedStarCountMin = -1;
        private int liveAcceptedStarCountMax = -1;

        /// <summary>
        /// How <see cref="SetCurveFittings"/> recovers a FOREIGN chart's own initial position / Start HFR / HFR
        /// change. Core's <c>LoadChart</c> hands the plugin nothing but the report's <c>Timestamp</c>, so the report
        /// has to be re-read from disk. Internal and settable so the reload-survival tests can point it at a
        /// temporary directory holding real report JSON, and so a test can assert the no-report path without
        /// depending on whatever the developer's own report directory happens to contain.
        /// </summary>
        internal ILoadedAutoFocusReportSource LoadedReportSource { get; set; } = new AutoFocusReportDirectorySource(ReportDirectory);

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
            TimeSpan duration,
            int acceptedStarCountMin = -1,
            int acceptedStarCountMax = -1) {
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
                    duration,
                    acceptedStarCountMin,
                    acceptedStarCountMax
                );

                var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
                string path = Path.Combine(ReportDirectory, $"{DateTime.Now:yyyy-MM-dd--HH-mm-ss}--{profileService.ActiveProfile.Id}.json");
                // Atomic write: NINA core watches ReportDirectory and File.OpenText's new reports; a plain
                // File.WriteAllText's open write handle races that reader into a sharing-violation IOException.
                PathUtility.WriteAllTextAtomic(path, reportText);
                return report;
            } catch (Exception ex) {
                Logger.Error(ex);
                return null;
            }
        }

        /// <summary>
        /// The sweep geometry to re-render a RELOADED chart with: the per-filter override belonging to the filter
        /// that run was actually taken through, else the profile.
        ///
        /// <para>Keyed on the loaded report's own <c>Filter</c> — which the engine wrote from the resolved
        /// auto-focus filter — and deliberately NOT on whatever is in the wheel right now: the chart on screen may
        /// be from another night through another filter, and keying it on the present would be the same
        /// stale-value class of bug the info-row loading already guards against.</para>
        /// </summary>
        private (int StepSize, int OffsetSteps) ResolveReloadedChartSweepGeometry(HocusFocusReport loadedReport) {
            var focuserSettings = profileService.ActiveProfile.FocuserSettings;
            var profileGeometry = (focuserSettings.AutoFocusStepSize, focuserSettings.AutoFocusInitialOffsetSteps);
            var filterName = loadedReport?.Filter;
            if (perFilterStore?.Enabled != true || string.IsNullOrWhiteSpace(filterName)) {
                return profileGeometry;
            }
            var geometry = perFilterStore.GetSweepGeometry(filterName);
            if (geometry == null) {
                return profileGeometry;
            }
            return (
                geometry.HasStepSize ? geometry.StepSize : focuserSettings.AutoFocusStepSize,
                geometry.HasOffsetSteps ? geometry.InitialOffsetSteps : focuserSettings.AutoFocusInitialOffsetSteps);
        }

        public void SetCurveFittings(string method, string fitting) {
            // Read ONCE, at the top: the fit below needs the report's filter to resolve this chart's sweep
            // geometry, and ApplyInfoRowsFromLoadedReport further down needs the report itself.
            var loadedReport = TryFindLoadedReport(LastAutoFocusPoint?.Timestamp);
            // NINA core's saved-chart reload (AutoFocusToolVM.LoadChart) rebuilds FocusPoints from a
            // saved report's raw Error values and calls this method — another fit entry point, so the
            // same regularization the live engine applies must happen here too.
            var validFocusPoints = WeightRegularization.Regularize(FocusPoints.Where(fp => fp.Y > 0.0).ToList());
            var windowExcluded = new List<ScatterErrorPoint>();

            if (AFMethodEnum.STARHFR.ToString() == method) {
                if (validFocusPoints.Count() >= 3) {
                    if (AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDLINES.ToString() == fitting) {
                        TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method);
                    }

                    if (AFCurveFittingEnum.PARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting) {
                        QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                    }

                    if (AFCurveFittingEnum.HYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting) {
                        var (stepSize, offsetSteps) = ResolveReloadedChartSweepGeometry(loadedReport);
                        var fitInput = validFocusPoints;
                        var hf = SolveHyperbolicForPoints(fitInput, stepSize, out var modelForRun);

                        // Behavior A parity for reloaded charts: the engine's final pass excludes points outside
                        // minimum ± (offsetSteps+0.5)*stepSize and refits on the kept subset (ApplyFinalSymmetricWindow).
                        // Saved reports carry the FULL measured set, so re-derive the same bounded fit→window→refit
                        // fixed point here — otherwise a reloaded chart would fit (and fill-render) far points the live
                        // run excluded. Same guards as the engine: ≤ offsetSteps+1 passes, never below 3 valid points.
                        if (offsetSteps >= 1 && stepSize > 0) {
                            for (var pass = 0; pass < offsetSteps + 1; pass++) {
                                var center = hf.Minimum.X;
                                if (double.IsNaN(center) || double.IsInfinity(center)) {
                                    break;
                                }
                                var (included, excluded) = AutoFocusEngine.PartitionByFocusWindow(fitInput, center, offsetSteps, stepSize);
                                if (excluded.Count == 0) {
                                    break;
                                }
                                if (included.Count(p => p.Y > 0.0) < 3) {
                                    break;
                                }
                                windowExcluded.AddRange(excluded);
                                fitInput = included;
                                hf = SolveHyperbolicForPoints(fitInput, stepSize, out modelForRun);
                                if (AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting) {
                                    TrendlineFitting = new TrendlineFitting().Calculate(fitInput, method);
                                }
                            }
                        }

                        // Best-focus stability is a one-time computation here (saved-run display), so unlike the
                        // live engine path it is safe to compute it directly after solving the final curve. Use the
                        // resolved concrete model so the LOO matches the chosen curve.
                        hf.LeaveOneOutStdError = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                            this.alglibAPI, modelForRun, fitInput, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                        HyperbolicFitting = hf;
                    }
                }
            } else if (validFocusPoints.Count() >= 3) {
                TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method);
                GaussianFitting = new GaussianFitting().Calculate(validFocusPoints);
            }

            // Core's LoadChart replaced FocusPoints/PlotFocusPoints, but it cannot see the plugin-only display state
            // this method is now responsible for reconciling: the filled/hollow marker split and the live-only info
            // rows. Without this, a loaded chart renders its curve and summary over the PREVIOUS live run's markers.
            RebuildDisplaySeriesFromFocusPoints(windowExcluded);
            // The initial-position / Start-HFR / HFR-change rows are plugin-only: core's LoadChart cannot restore
            // them, so after ANY load they still describe whichever run was on screen before. Re-populate them from
            // the LOADED run's own report — which carries all three — falling back to the sentinels (collapsed
            // rows) when it cannot be found. Leaving the previous run's numbers on screen is the 2026-07-29 field
            // report; showing 0 as though it were a measurement is the same defect wearing a different hat.
            //
            // This runs for THIS VM's own run too, and the 2026-08-06 field report is why. Skipping it when the
            // timestamps matched was correct for the only sequence it was tested on — the watcher re-loading the
            // just-written chart, where the live fields are still intact — but wrong after a ROUND TRIP: once a
            // foreign chart has been loaded those fields already hold the foreign run's values, and "keep what is
            // on screen" then keeps exactly the wrong thing. Re-reading unconditionally makes the rows a function
            // of the LOADED run rather than of the path taken to it.
            ApplyInfoRowsFromLoadedReport(loadedReport, LastAutoFocusPoint?.Timestamp);
            RefreshFinalFocusPointError();
        }

        /// <summary>
        /// Re-reads the loaded chart's own report and renders ITS initial focuser position, Start HFR and final HFR
        /// — the three rows core's <c>LoadChart</c> cannot restore, because they are plugin-only and (for
        /// <see cref="HocusFocusReport.FinalHFR"/>) not even on the type core deserializes.
        ///
        /// <para><b>Fail-visible by construction.</b> Every path that cannot produce a value from THIS report writes
        /// the sentinel that collapses the row (-1 / 0.0 / 0.0), which is byte-identical to the behaviour before
        /// this method existed. So the change can only ever add information about the loaded run; it can never
        /// substitute another run's number, and it can never present a missing value as a measured 0.</para>
        /// </summary>
        /// <summary>Reads the saved report for a chart timestamp, or null. Never throws — a chart must still render.</summary>
        private HocusFocusReport TryFindLoadedReport(DateTime? timestamp) {
            if (!timestamp.HasValue) {
                return null;
            }
            try {
                return LoadedReportSource?.TryFind(timestamp.Value);
            } catch (Exception ex) {
                // TryFind's contract is not to throw; this is belt-and-braces so a chart still renders.
                Logger.Debug($"Could not read the loaded AutoFocus report for {timestamp.Value:o}: {ex.Message}");
                return null;
            }
        }

        private void ApplyInfoRowsFromLoadedReport(HocusFocusReport report, DateTime? timestamp) {
            if (report == null && timestamp.HasValue && timestamp == lastGeneratedReportTimestamp) {
                // This VM's OWN run, whose report could not be read back (not yet flushed, deleted, or a directory
                // that has moved). The live values are still the truth for it, so prefer them over collapsing the
                // rows. This is the one case where a value not sourced from the loaded report is still ABOUT the
                // loaded run — every other path stays fail-visible.
                InitialFocuserPosition = liveInitialFocuserPosition;
                InitialHFR = liveInitialHFR;
                FinalHFR = liveFinalHFR;
                AcceptedStarCountMin = liveAcceptedStarCountMin;
                AcceptedStarCountMax = liveAcceptedStarCountMax;
                return;
            }
            InitialFocuserPosition = ResolveReportInitialFocuserPosition(report);
            InitialHFR = ResolveReportHfr(report?.InitialFocusPoint?.Value);
            FinalHFR = ResolveReportHfr(report?.FinalHFR);
            // The loaded run's own star range, or the collapsed sentinel. Core's LoadChart rebuilds FocusPoints
            // but carries no per-point star counts, so this row can only ever come from the report.
            var (starMin, starMax) = ResolveReportStarCountRange(report);
            AcceptedStarCountMin = starMin;
            AcceptedStarCountMax = starMax;
        }

        /// <summary>
        /// The loaded report's accepted-star range, or <c>(-1, -1)</c> — the collapsed row — for anything that is
        /// not a coherent recorded pair. A report from core or another auto-focuser has no such field at all and
        /// lands here, which is correct: it genuinely does not know.
        /// </summary>
        private static (int Min, int Max) ResolveReportStarCountRange(HocusFocusReport report) {
            var min = report?.AcceptedStarCountMin ?? -1;
            var max = report?.AcceptedStarCountMax ?? -1;
            return min >= 0 && max >= min ? (min, max) : (-1, -1);
        }

        /// <summary>
        /// The loaded report's initial focuser position, or <c>-1</c> ("not known", the same sentinel
        /// <c>AutoFocusEngine</c> writes when it has none) when the report does not actually record one.
        ///
        /// <para>A missing <c>InitialFocusPoint</c> deserializes to <c>new FocusPoint()</c> — <c>Position = 0</c> —
        /// which is indistinguishable from a genuinely recorded 0. Treating non-positive as "not recorded" means an
        /// absent value collapses the row instead of claiming the focuser started at 0, and costs only the
        /// unreachable case of a rig actually focusing at step 0.</para>
        /// </summary>
        private static int ResolveReportInitialFocuserPosition(HocusFocusReport report) {
            var position = report?.InitialFocusPoint?.Position ?? double.NaN;
            if (!double.IsFinite(position) || position <= 0.0) {
                return -1;
            }
            return (int)Math.Round(position, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// An HFR from the loaded report, or <c>0.0</c> — the value the XAML's zero-to-collapsed converters read as
        /// "no row" — for anything that is not a real positive measurement. A report written by core or another
        /// auto-focuser carries no <see cref="HocusFocusReport.FinalHFR"/> at all and lands here, which is correct:
        /// that report genuinely does not know the value.
        /// </summary>
        private static double ResolveReportHfr(double? value) {
            var hfr = value ?? double.NaN;
            return double.IsFinite(hfr) && hfr > 0.0 ? hfr : 0.0;
        }

        /// <summary>
        /// Solves the configured hyperbolic model on <paramref name="points"/>. When the option is Hybrid, reproduces
        /// the engine's best-fit pick on this point set so a reloaded run shows the concrete chosen model (and its LOO)
        /// instead of falling back to Tilted. SelectBestModel returns the already-solved winning fit, so no extra
        /// Solve() is needed.
        /// </summary>
        private AlglibHyperbolicFitting SolveHyperbolicForPoints(List<ScatterErrorPoint> points, int stepSize, out HyperbolicFitModel resolvedModel) {
            AlglibHyperbolicFitting hf;
            resolvedModel = autoFocusOptions.HyperbolicFitModel;
            if (resolvedModel == HyperbolicFitModel.Hybrid) {
                resolvedModel = AlglibHyperbolicFitting.SelectBestModel(this.alglibAPI, points, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled, out hf);
                if (hf == null) {
                    hf = AlglibHyperbolicFitting.Create(this.alglibAPI, resolvedModel, points, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                    hf.Solve();
                }
            } else {
                hf = AlglibHyperbolicFitting.Create(this.alglibAPI, resolvedModel, points, stepSize, autoFocusOptions.WeightedHyperbolicFitEnabled);
                hf.Solve();
            }
            return hf;
        }

        /// <summary>
        /// Rebuilds the marker display series from <see cref="FocusPoints"/> after an external chart load: refills the
        /// filled series (<see cref="PlotCoreFocusPoints"/>) from the full measured set, then routes the re-derived
        /// window exclusions through <see cref="ApplyWindowExclusionToDisplay"/> — the same partition the live
        /// completion handler applies — so excluded points render only as hollow rings and the legend gate refreshes.
        /// </summary>
        private void RebuildDisplaySeriesFromFocusPoints(IReadOnlyList<ScatterErrorPoint> windowExcluded) {
            PlotCoreFocusPoints.Clear();
            foreach (var fp in FocusPoints) {
                PlotCoreFocusPoints.AddSorted(fp, focusPointComparer);
            }
            var excludedRegionPoints = windowExcluded.Select(p => {
                var position = (int)Math.Round(p.X);
                // Show the raw measured error on the hollow ring (like the live path), not the regularized fit weight.
                var raw = FocusPoints.FirstOrDefault(fp => (int)Math.Round(fp.X) == position);
                return new AutoFocusRegionPoint() {
                    FocuserPosition = position,
                    Measurement = new MeasureAndError() { Measure = p.Y, Stdev = raw?.ErrorY ?? p.ErrorY },
                };
            }).ToList();
            ApplyWindowExclusionToDisplay(excludedRegionPoints);
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

        // Cancellation source for the in-flight live AutoFocus run started via StartAutoFocus, linked to the caller's
        // token (the sequence's or the pane's). CancelAutoFocus() trips it so the sequence-item popup's close (X)
        // button can stop the run and then close. Null whenever no live run is in flight. The replay path has its own
        // loadSavedAutoFocusRunCts.
        private CancellationTokenSource autoFocusRunCts;

        public async Task<AutoFocusReport> StartAutoFocus(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            IAutoFocusEngine autoFocusEngine = null;
            try {
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return null;
                }
                // Per-filter star detection keys settings off the capture-time filter name; without a
                // connected wheel every exposure would soft-fail, so refuse the run up front.
                if (perFilterStore?.Enabled == true && filterWheelMediator.GetInfo()?.Connected != true) {
                    Notification.ShowError("Per-filter star detection requires a connected filter wheel");
                    return null;
                }
                // Link the caller's token so this run is independently cancelable (window-close cancel). Created before
                // AutoFocusInProgress flips true so a cancel observed off that flag never races a null source.
                autoFocusRunCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                AutoFocusInProgress = true;

                autoFocusEngine = autoFocusEngineFactory.Create();
                autoFocusEngine.Started += AutoFocusEngine_AutoFocusStarted;
                autoFocusEngine.InitialHFRCalculated += AutoFocusEngine_InitialHFRCalculated;
                autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
                autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;
                autoFocusEngine.SubMeasurementPointCompleted += AutoFocusEngine_SubMeasurementPointCompleted;
                autoFocusEngine.Completed += AutoFocusEngine_Completed;
                autoFocusEngine.Failed += AutoFocusEngine_Failed;
                // The imaging filter is passed so a per-filter sweep-geometry override can be resolved for the
                // filter this run will actually expose through (the engine applies the AF-filter substitution
                // itself). The wheel-connected gate above already ran.
                var options = autoFocusEngine.GetOptions(imagingFilter: imagingFilter);

                ApplyFrameReviewOptions(options);
                var result = await autoFocusEngine.Run(options, imagingFilter, autoFocusRunCts.Token, progress);
                if (result == null || !result.Succeeded) {
                    return null;
                }
                InitialFocuserPosition = result.InitialFocuserPosition;
                return LastReport;
            } finally {
                // Detach the per-run engine handlers symmetrically. Correctness doesn't depend on it today (the factory
                // returns a fresh engine each run), but it makes the subscribe/unsubscribe contract explicit and guards
                // against any future engine reuse leaking handlers across runs. (F28)
                if (autoFocusEngine != null) {
                    autoFocusEngine.Started -= AutoFocusEngine_AutoFocusStarted;
                    autoFocusEngine.InitialHFRCalculated -= AutoFocusEngine_InitialHFRCalculated;
                    autoFocusEngine.IterationFailed -= AutoFocusEngine_IterationFailed;
                    autoFocusEngine.MeasurementPointCompleted -= AutoFocusEngine_MeasurementPointCompleted;
                    autoFocusEngine.SubMeasurementPointCompleted -= AutoFocusEngine_SubMeasurementPointCompleted;
                    autoFocusEngine.Completed -= AutoFocusEngine_Completed;
                    autoFocusEngine.Failed -= AutoFocusEngine_Failed;
                }
                // A successful run already moved frames into the snapshot (and cleared reviewFrames); this
                // covers cancellation / null-init paths where Completed/Failed never fired, so captured
                // exposures aren't pinned until the next run. (F22)
                ReleaseUnsnapshottedReviewFrames();
                autoFocusRunCts?.Dispose();
                autoFocusRunCts = null;
                AutoFocusInProgress = false;
            }
        }

        /// <summary>
        /// Cancels the in-flight live AutoFocus run started via <see cref="StartAutoFocus"/>, if any. The sequence-item
        /// AutoFocus popup's close (X) button calls this so closing the window stops the run rather than orphaning it;
        /// the window then closes once <see cref="AutoFocusInProgress"/> clears. No-op when no run is in flight.
        /// </summary>
        public void CancelAutoFocus() {
            autoFocusRunCts?.Cancel();
        }

        public AutoFocusReport LastReport { get; private set; }

        private static String GetAttemptSaveFolder(string saveFolder, int iteration) {
            var parentFolder = Path.Combine(saveFolder, $"attempt{iteration:00}");
            if (!Directory.Exists(parentFolder)) {
                Directory.CreateDirectory(parentFolder);
            }
            return parentFolder;
        }

        /// <summary>
        /// Writes the single-region AutoFocus report as <c>attemptNN/autofocus_report_Region0.json</c> under
        /// <paramref name="saveFolder"/>. The plain AutoFocus pane always analyzes one region, so region 0 is the
        /// whole report set here (the Inspector's multi-region equivalent is InspectorVM.SaveRegionReports).
        /// </summary>
        private static void SaveRegionReport(string saveFolder, int iteration, AutoFocusReport report) {
            var reportText = JsonConvert.SerializeObject(report, Formatting.Indented);
            var targetFilePath = Path.Combine(GetAttemptSaveFolder(saveFolder, iteration), "autofocus_report_Region0.json");
            File.WriteAllText(targetFilePath, reportText);
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
                SaveRegionReport(e.SaveFolder, e.Iteration, report);
            }

            var autoFocusInfo = new AutoFocusInfo(report.Temperature, report.CalculatedFocusPoint.Position, report.Filter, report.Timestamp);
            focuserMediator.BroadcastSuccessfulAutoFocusRun(autoFocusInfo);

            LastReport = report;
            BuildFrameReviewSnapshotIfRequested();
        }

        /// <summary>
        /// Completion handler for the reprocess (replay) path. Mirrors <see cref="AutoFocusEngine_Completed"/> minus
        /// everything that would announce a focus event that never happened: no BroadcastSuccessfulAutoFocusRun, and
        /// no report written into NINA's watched report directory. <see cref="LastReport"/> is left alone too, so the
        /// pane keeps showing the report of the run that actually moved the focuser. It does write the per-region
        /// report when the engine created a save folder (AF Options -> Save), so a re-analysis is as inspectable as
        /// the live run that produced the frames. Best-effort: the replay has already succeeded by the time this
        /// runs, so a write failure is logged rather than surfaced through the engine.
        /// </summary>
        private void AutoFocusEngine_CompletedReplay(object sender, AutoFocusCompletedEventArgs e) {
            AutoFocusEngine_CompletedNoReport(sender, e);
            if (string.IsNullOrEmpty(e.SaveFolder)) {
                return;
            }
            try {
                var firstRegion = e.RegionHFRs[0];
                var report = GenerateReport(
                    initialFocusPosition: e.InitialFocusPosition,
                    initialHFR: firstRegion.InitialHFR ?? 0.0d,
                    finalHFR: firstRegion.FinalHFR ?? firstRegion.EstimatedFinalHFR,
                    filter: e.Filter,
                    finalFocusPoint: FinalFocusPoint,
                    lastAutoFocusPoint: LastAutoFocusPoint,
                    region: firstRegion.Region,
                    duration: e.Duration);
                SaveRegionReport(e.SaveFolder, e.Iteration, report);
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to save the reprocessed AutoFocus report to {e.SaveFolder}");
            }
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
                SaveRegionReport(e.SaveFolder, e.Iteration, report);
            }
            LastReport = report;
            // Build the review snapshot even for a failed sweep — seeing why detection struggled is often the most
            // useful case (the frames were only retained for an interactive pane run with the toggle on).
            BuildFrameReviewSnapshotIfRequested();
        }

        private void AutoFocusEngine_IterationFailed(object sender, AutoFocusFailedEventArgs e) {
            if (!string.IsNullOrEmpty(e.SaveFolder)) {
                var firstRegion = e.RegionHFRs[0];
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
                    duration: e.Duration,
                    acceptedStarCountMin: AcceptedStarCountMin,
                    acceptedStarCountMax: AcceptedStarCountMax);

                SaveRegionReport(e.SaveFolder, e.Iteration, report);
            }

            FocusPoints.Clear();
            PlotFinalFocusPointWithError.Clear();
            PlotFocusPoints.Clear();
            PlotRejectedFocusPoints.Clear();
            PlotCoreFocusPoints.Clear();
            PlotWindowExcludedFocusPoints.Clear();
            RaisePropertyChanged(nameof(HasWindowExcludedFocusPoints));
            // The next attempt re-measures from scratch, so this attempt's per-frame counts must not survive into
            // its range — exactly as its focus points do not survive into its curve.
            ClearAcceptedStarCounts();
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

            // Symmetric-window exclusions (Behavior A) are produced only by the finalized region fit, so the real set
            // arrives here (empty on the live per-point events). Move them into the hollow-ring overlay and out of the
            // filled/line display series (see ApplyWindowExclusionToDisplay); FocusPoints is left intact.
            ApplyWindowExclusionToDisplay(firstRegion.WindowExcludedPoints);

            // Re-sync the star-count range to the FINAL accepted set, for the same reason the two overlays above
            // are re-synced: the live per-point events see only the intermediate Grubbs flags and never see the
            // window exclusions at all.
            UpdateAcceptedStarCountRange(firstRegion.RejectedPoints, firstRegion.WindowExcludedPoints);

            RefreshFinalFocusPointError();
            AutoFocusDuration = e.Duration;
        }

        /// <summary>
        /// Reconciles the chart's display series with the finalized window-excluded set (Behavior A): fills the hollow
        /// "excluded" overlay and removes those focuser positions from the filled-marker (<see cref="PlotCoreFocusPoints"/>)
        /// and connecting-line (<see cref="PlotFocusPoints"/>) series, so each excluded point renders ONLY as a hollow
        /// ring. Without the removal the filled marker is drawn over the ring and an excluded point looks identical to an
        /// included one. <see cref="FocusPoints"/> — the full measured set feeding the report and fit — is left intact.
        /// internal so the partition is unit-testable without driving a full engine run.
        /// </summary>
        internal void ApplyWindowExclusionToDisplay(IReadOnlyList<AutoFocusRegionPoint> windowExcludedPoints) {
            PlotWindowExcludedFocusPoints.Clear();
            if (windowExcludedPoints != null && windowExcludedPoints.Count > 0) {
                var excludedPositions = new HashSet<int>();
                foreach (var wp in windowExcludedPoints) {
                    excludedPositions.Add(wp.FocuserPosition);
                    PlotWindowExcludedFocusPoints.Add(new ScatterErrorPoint(wp.FocuserPosition, wp.Measurement.Measure, 0, AutoFocusEngine.SafeDisplayError(wp.Measurement.Stdev)));
                }
                foreach (var core in PlotCoreFocusPoints.Where(p => excludedPositions.Contains((int)Math.Round(p.X))).ToList()) {
                    PlotCoreFocusPoints.Remove(core);
                }
                foreach (var line in PlotFocusPoints.Where(p => excludedPositions.Contains((int)Math.Round(p.X))).ToList()) {
                    PlotFocusPoints.Remove(line);
                }
            }
            RaisePropertyChanged(nameof(HasWindowExcludedFocusPoints));
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex != 0) {
                return;
            }

            var focusPoint = new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, AutoFocusEngine.SafeDisplayError(e.Measurement.Stdev));
            FocusPoints.AddSorted(focusPoint, focusPointComparer);
            // Mirror into the filled DISPLAY series. Window exclusions are known only at finalization, so during the
            // live sweep the two are identical; AutoFocusEngine_CompletedNoReport prunes the excluded points from the
            // core/line series (not from FocusPoints) so they end up rendered only as hollow rings.
            PlotCoreFocusPoints.AddSorted(focusPoint, focusPointComparer);
            var dataPoint = new DataPoint(e.FocuserPosition, e.Measurement.Measure);
            PlotFocusPoints.AddSorted(dataPoint, plotPointComparer);
            this.focuserMediator.BroadcastNewAutoFocusPoint(dataPoint);

            PlotRejectedFocusPoints.Clear();
            foreach (var rp in e.RejectedPoints) {
                PlotRejectedFocusPoints.Add(new ScatterPoint(rp.FocuserPosition, rp.Measurement.Measure));
            }

            // Window exclusions are decided only at finalization, so e.WindowExcludedPoints is empty on the live events;
            // clear-and-refill keeps the sibling overlay consistent with the rejected overlay (the real set lands via
            // AutoFocusEngine_CompletedNoReport). Built like FocusPoints so the hollow-ring overlay keeps its HFR error bar.
            PlotWindowExcludedFocusPoints.Clear();
            foreach (var wp in e.WindowExcludedPoints) {
                PlotWindowExcludedFocusPoints.Add(new ScatterErrorPoint(wp.FocuserPosition, wp.Measurement.Measure, 0, AutoFocusEngine.SafeDisplayError(wp.Measurement.Stdev)));
            }
            RaisePropertyChanged(nameof(HasWindowExcludedFocusPoints));

            UpdateAcceptedStarCountRange(e.RejectedPoints, e.WindowExcludedPoints);

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
            // Standard (non-inspector) AF uses a single region 0.
            if (e.RegionIndex != 0) {
                return;
            }

            // Accepted-star count for this frame, accumulated for EVERY run (not just review runs) — it is one int
            // and it is the only place the panel can learn how many stars the curve was built from.
            RecordFrameStarCount(e.FocuserPosition, e.StarDetectionResult);

            // The image is only present when PreserveExposures was forced on (i.e. frameReviewRequestedForRun).
            // Accumulate one tuple per fired frame.
            if (!frameReviewRequestedForRun) {
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
            IAutoFocusEngine autoFocusEngine = null;
            try {
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return false;
                }
                AutoFocusInProgress = true;

                loadSavedAutoFocusRunCts?.Cancel();
                loadSavedAutoFocusRunCts = new CancellationTokenSource();
                autoFocusEngine = autoFocusEngineFactory.Create();
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
                autoFocusEngine.Completed += AutoFocusEngine_CompletedReplay;

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
                    // The reprocess path wires Completed -> CompletedReplay (which never sets LastReport), so build the review
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
                // Detach the per-run engine handlers symmetrically (F28). This path wires Completed ->
                // AutoFocusEngine_CompletedReplay and does not subscribe Failed, so the -= list mirrors that exactly.
                if (autoFocusEngine != null) {
                    autoFocusEngine.Started -= AutoFocusEngine_AutoFocusStarted;
                    autoFocusEngine.InitialHFRCalculated -= AutoFocusEngine_InitialHFRCalculated;
                    autoFocusEngine.IterationFailed -= AutoFocusEngine_IterationFailed;
                    autoFocusEngine.MeasurementPointCompleted -= AutoFocusEngine_MeasurementPointCompleted;
                    autoFocusEngine.SubMeasurementPointCompleted -= AutoFocusEngine_SubMeasurementPointCompleted;
                    autoFocusEngine.Completed -= AutoFocusEngine_CompletedReplay;
                }
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
