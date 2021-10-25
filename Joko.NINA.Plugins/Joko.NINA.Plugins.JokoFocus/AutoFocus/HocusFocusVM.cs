using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.JokoFocus.AutoFocus {
    public class HocusFocusVM : BaseVM, IAutoFocusVM {
        private AFCurveFittingEnum _autoFocusChartCurveFitting;
        private AFMethodEnum _autoFocusChartMethod;
        private DataPoint _finalFocusPoint;
        private AsyncObservableCollection<ScatterErrorPoint> _focusPoints;
        private int _focusPosition;
        private GaussianFitting _gaussianFitting;
        private HyperbolicFitting _hyperbolicFitting;
        private ReportAutoFocusPoint _lastAutoFocusPoint;
        private AsyncObservableCollection<DataPoint> _plotFocusPoints;
        private QuadraticFitting _quadraticFitting;
        private TrendlineFitting _trendLineFitting;
        private int _durationSeconds;
        private ICameraMediator cameraMediator;
        private IFilterWheelMediator filterWheelMediator;
        private IFocuserMediator focuserMediator;
        private IGuiderMediator guiderMediator;
        private IImagingMediator imagingMediator;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        public static readonly string ReportDirectory = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus");

        static HocusFocusVM() {
            if (!Directory.Exists(ReportDirectory)) {
                Directory.CreateDirectory(ReportDirectory);
            } else {
                // CoreUtil.DirectoryCleanup(ReportDirectory, TimeSpan.FromDays(-180));
            }
        }

        public HocusFocusVM(
                IProfileService profileService,
                ICameraMediator cameraMediator,
                IFilterWheelMediator filterWheelMediator,
                IFocuserMediator focuserMediator,
                IGuiderMediator guiderMediator,
                IImagingMediator imagingMediator,
                IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
                IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector
        ) : base(profileService) {
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;

            this.imagingMediator = imagingMediator;
            this.guiderMediator = guiderMediator;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;

            FocusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
            PlotFocusPoints = new AsyncObservableCollection<DataPoint>();
        }

        public AFCurveFittingEnum AutoFocusChartCurveFitting {
            get {
                return _autoFocusChartCurveFitting;
            }
            set {
                _autoFocusChartCurveFitting = value;
                RaisePropertyChanged();
            }
        }

        public AFMethodEnum AutoFocusChartMethod {
            get {
                return _autoFocusChartMethod;
            }
            set {
                _autoFocusChartMethod = value;
                RaisePropertyChanged();
            }
        }

        public double AverageContrast { get; private set; }
        public double ContrastStdev { get; private set; }

        public DataPoint FinalFocusPoint {
            get {
                return _finalFocusPoint;
            }
            set {
                _finalFocusPoint = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<ScatterErrorPoint> FocusPoints {
            get {
                return _focusPoints;
            }
            set {
                _focusPoints = value;
                RaisePropertyChanged();
            }
        }

        public GaussianFitting GaussianFitting {
            get {
                return _gaussianFitting;
            }
            set {
                _gaussianFitting = value;
                RaisePropertyChanged();
            }
        }

        public HyperbolicFitting HyperbolicFitting {
            get {
                return _hyperbolicFitting;
            }
            set {
                _hyperbolicFitting = value;
                RaisePropertyChanged();
            }
        }

        public ReportAutoFocusPoint LastAutoFocusPoint {
            get {
                return _lastAutoFocusPoint;
            }
            set {
                _lastAutoFocusPoint = value;
                RaisePropertyChanged();
            }
        }

        public AsyncObservableCollection<DataPoint> PlotFocusPoints {
            get {
                return _plotFocusPoints;
            }
            set {
                _plotFocusPoints = value;
                RaisePropertyChanged();
            }
        }

        public QuadraticFitting QuadraticFitting {
            get => _quadraticFitting;
            set {
                _quadraticFitting = value;
                RaisePropertyChanged();
            }
        }

        public TrendlineFitting TrendlineFitting {
            get => _trendLineFitting;
            set {
                _trendLineFitting = value;
                RaisePropertyChanged();
            }
        }

        public int DurationSeconds {
            get => _durationSeconds;
            set {
                if (_durationSeconds != value) {
                    _durationSeconds = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void ClearCharts() {
            AutoFocusChartMethod = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod;
            AutoFocusChartCurveFitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;
            FocusPoints.Clear();
            PlotFocusPoints.Clear();
            TrendlineFitting = null;
            QuadraticFitting = null;
            HyperbolicFitting = null;
            GaussianFitting = null;
            FinalFocusPoint = new DataPoint(0, 0);
            LastAutoFocusPoint = null;
            DurationSeconds = 0;
        }

        private DataPoint DetermineFinalFocusPoint() {
            using (MyStopWatch.Measure()) {
                var method = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod;

                TrendlineFitting = new TrendlineFitting().Calculate(FocusPoints, method.ToString());

                HyperbolicFitting = new HyperbolicFitting().Calculate(FocusPoints);

                QuadraticFitting = new QuadraticFitting().Calculate(FocusPoints);

                GaussianFitting = new GaussianFitting().Calculate(FocusPoints);

                if (method == AFMethodEnum.STARHFR) {
                    var fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;
                    if (fitting == AFCurveFittingEnum.TRENDLINES) {
                        return TrendlineFitting.Intersection;
                    }

                    if (fitting == AFCurveFittingEnum.HYPERBOLIC) {
                        return HyperbolicFitting.Minimum;
                    }

                    if (fitting == AFCurveFittingEnum.PARABOLIC) {
                        return QuadraticFitting.Minimum;
                    }

                    if (fitting == AFCurveFittingEnum.TRENDPARABOLIC) {
                        return new DataPoint(Math.Round((TrendlineFitting.Intersection.X + QuadraticFitting.Minimum.X) / 2), (TrendlineFitting.Intersection.Y + QuadraticFitting.Minimum.Y) / 2);
                    }

                    if (fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                        return new DataPoint(Math.Round((TrendlineFitting.Intersection.X + HyperbolicFitting.Minimum.X) / 2), (TrendlineFitting.Intersection.Y + HyperbolicFitting.Minimum.Y) / 2);
                    }

                    Logger.Error($"Invalid AutoFocus Fitting {fitting} for method {method}");
                    return new DataPoint();
                } else {
                    return GaussianFitting.Maximum;
                }
            }
        }

        private async Task<MeasureAndError> EvaluateExposure(IRenderedImage image, CancellationToken token, IProgress<ApplicationStatus> progress) {
            Logger.Trace("Evaluating Exposure");

            var imageProperties = image.RawImageData.Properties;
            var imageStatistics = await image.RawImageData.Statistics.Task;

            //Very simple to directly provide result if we use statistics based contrast detection
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                return new MeasureAndError() { Measure = 100 * imageStatistics.StDev / imageStatistics.Mean, Stdev = 0.01 };
            }

            System.Windows.Media.PixelFormat pixelFormat;

            if (imageProperties.IsBayered && profileService.ActiveProfile.ImageSettings.DebayerImage) {
                pixelFormat = System.Windows.Media.PixelFormats.Rgb48;
            } else {
                pixelFormat = System.Windows.Media.PixelFormats.Gray16;
            }

            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR) {
                var analysisParams = new StarDetectionParams() {
                    Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    NumberOfAFStars = profileService.ActiveProfile.FocuserSettings.AutoFocusUseBrightestStars
                };
                if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && !IsSubSampleEnabled()) {
                    analysisParams.UseROI = true;
                    analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                    analysisParams.OuterCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio;
                }
                var starDetection = starDetectionSelector.GetBehavior();
                var analysisResult = await starDetection.Detect(image, pixelFormat, analysisParams, progress, token);

                if (profileService.ActiveProfile.ImageSettings.AnnotateImage) {
                    var starAnnotator = starAnnotatorSelector.GetBehavior();
                    var annotatedImage = await starAnnotator.GetAnnotatedImage(analysisParams, analysisResult, image.Image, token: token);
                    imagingMediator.SetImage(annotatedImage);
                }

                Logger.Debug($"Current Focus: Position: {_focusPosition}, HFR: {analysisResult.AverageHFR}");

                return new MeasureAndError() { Measure = analysisResult.AverageHFR, Stdev = analysisResult.HFRStdDev };
            } else {
                var analysis = new ContrastDetection();
                var analysisParams = new ContrastDetectionParams() {
                    Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    Method = profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod
                };
                if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && !IsSubSampleEnabled()) {
                    analysisParams.UseROI = true;
                    analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                }
                var analysisResult = await analysis.Measure(image, analysisParams, progress, token);

                MeasureAndError ContrastMeasurement = new MeasureAndError() { Measure = analysisResult.AverageContrast, Stdev = analysisResult.ContrastStdev };
                return ContrastMeasurement;
            }
        }

        /// <summary>
        /// Generates a JSON report into %localappdata%\NINA\AutoFocus for the complete autofocus run containing all the measurements
        /// </summary>
        /// <param name="initialFocusPosition"></param>
        /// <param name="initialHFR"></param>
        private AutoFocusReport GenerateReport(double initialFocusPosition, double initialHFR, string filter, TimeSpan duration) {
            try {
                var report = AutoFocusReport.GenerateReport(
                    profileService,
                    FocusPoints,
                    initialFocusPosition,
                    initialHFR,
                    FinalFocusPoint,
                    LastAutoFocusPoint,
                    TrendlineFitting,
                    QuadraticFitting,
                    HyperbolicFitting,
                    GaussianFitting,
                    focuserMediator.GetInfo().Temperature,
                    filter,
                    duration
                );

                string path = Path.Combine(ReportDirectory, DateTime.Now.ToString("yyyy-MM-dd--HH-mm-ss") + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(report));
                return report;
            } catch (Exception ex) {
                Logger.Error(ex);
                return null;
            }
        }

        private async Task<MeasureAndError> GetAverageMeasurement(FilterInfo filter, int exposuresPerFocusPoint, CancellationToken token, IProgress<ApplicationStatus> progress) {
            //Average HFR  of multiple exposures (if configured this way)
            double sumMeasure = 0;
            double sumVariances = 0;
            for (int i = 0; i < exposuresPerFocusPoint; i++) {
                var image = await TakeExposure(filter, token, progress);
                var partialMeasurement = await EvaluateExposure(image, token, progress);
                sumMeasure += partialMeasurement.Measure;
                sumVariances += partialMeasurement.Stdev * partialMeasurement.Stdev;
                token.ThrowIfCancellationRequested();
            }

            return new MeasureAndError() { Measure = sumMeasure / exposuresPerFocusPoint, Stdev = Math.Sqrt(sumVariances / exposuresPerFocusPoint) };
        }

        private async Task GetFocusPoints(FilterInfo filter, int nrOfSteps, IProgress<ApplicationStatus> progress, CancellationToken token, int offset = 0) {
            var stepSize = profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize;

            if (offset != 0) {
                //Move to initial position
                Logger.Trace($"Moving focuser from {_focusPosition} to initial position by moving {offset * stepSize} steps");
                _focusPosition = await focuserMediator.MoveFocuserRelative(offset * stepSize, token);
            }

            var comparer = new FocusPointComparer();
            var plotComparer = new PlotPointComparer();

            for (int i = 0; i < nrOfSteps; i++) {
                token.ThrowIfCancellationRequested();

                MeasureAndError measurement = await GetAverageMeasurement(filter, profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint, token, progress);

                //If star Measurement is 0, we didn't detect any stars or shapes, and want this point to be ignored by the fitting as much as possible. Setting a very high Stdev will do the trick.
                if (measurement.Measure == 0) {
                    Logger.Warning($"No stars detected in step {i + 1}. Setting a high stddev to ignore the point.");
                    measurement.Stdev = 1000;
                }

                token.ThrowIfCancellationRequested();

                FocusPoints.AddSorted(new ScatterErrorPoint(_focusPosition, measurement.Measure, 0, Math.Max(0.001, measurement.Stdev)), comparer);
                PlotFocusPoints.AddSorted(new DataPoint(_focusPosition, measurement.Measure), plotComparer);
                if (i < nrOfSteps - 1) {
                    Logger.Trace($"Moving focuser from {_focusPosition} to the next autofocus position using step size: {-stepSize}");
                    _focusPosition = await focuserMediator.MoveFocuserRelative(-stepSize, token);
                }

                token.ThrowIfCancellationRequested();

                SetCurveFittings(profileService.ActiveProfile.FocuserSettings.AutoFocusMethod.ToString(), profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting.ToString());
            }
        }

        private async Task<FilterInfo> SetAutofocusFilter(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (profileService.ActiveProfile.FocuserSettings.UseFilterWheelOffsets) {
                var filter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(f => f.AutoFocusFilter == true).FirstOrDefault();
                if (filter == null) {
                    return imagingFilter;
                }

                //Set the filter to the autofocus filter if necessary, and move to it so autofocus X indexing works properly when invoking GetFocusPoints()
                try {
                    return await filterWheelMediator.ChangeFilter(filter, token, progress);
                } catch (Exception e) {
                    Logger.Error("Failed to change filter during AutoFocus", e);
                    Notification.ShowWarning($"Failed to change filter: {e.Message}");
                    return imagingFilter;
                }
            } else {
                return imagingFilter;
            }
        }

        private ObservableRectangle GetSubSampleRectangle() {
            var cameraInfo = cameraMediator.GetInfo();
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio == 1 && cameraInfo.CanSubSample) {
                int subSampleWidth = (int)Math.Round(cameraInfo.XSize * profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio);
                int subSampleHeight = (int)Math.Round(cameraInfo.YSize * profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio);
                int subSampleX = (int)Math.Round((cameraInfo.XSize - subSampleWidth) / 2.0d);
                int subSampleY = (int)Math.Round((cameraInfo.YSize - subSampleHeight) / 2.0d);
                return new ObservableRectangle(subSampleX, subSampleY, subSampleWidth, subSampleHeight);
            }
            return null;
        }

        private async Task<IRenderedImage> TakeExposure(FilterInfo filter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            IRenderedImage image;
            var retries = 0;
            do {
                Logger.Trace("Starting Exposure for autofocus");
                double expTime = profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime;
                if (filter != null && filter.AutoFocusExposureTime > -1) {
                    expTime = filter.AutoFocusExposureTime;
                }
                var seq = new CaptureSequence(expTime, CaptureSequence.ImageTypes.SNAPSHOT, filter, null, 1);

                var subSampleRectangle = GetSubSampleRectangle();
                if (subSampleRectangle != null) {
                    seq.EnableSubSample = true;
                    seq.SubSambleRectangle = subSampleRectangle;
                }

                if (filter?.AutoFocusBinning != null) {
                    seq.Binning = filter.AutoFocusBinning;
                } else {
                    seq.Binning = new BinningMode(profileService.ActiveProfile.FocuserSettings.AutoFocusBinning, profileService.ActiveProfile.FocuserSettings.AutoFocusBinning);
                }

                if (filter?.AutoFocusOffset > -1) {
                    seq.Offset = filter.AutoFocusOffset;
                }

                if (filter?.AutoFocusGain > -1) {
                    seq.Gain = filter.AutoFocusGain;
                }

                bool autoStretch = true;
                //If using contrast based statistics, no need to stretch
                if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                    autoStretch = false;
                }
                var prepareParameters = new PrepareImageParameters(autoStretch: autoStretch, detectStars: false);
                try {
                    image = await imagingMediator.CaptureAndPrepareImage(seq, prepareParameters, token, progress);
                } catch (Exception e) {
                    if (!IsSubSampleEnabled()) {
                        throw;
                    }

                    Logger.Warning("Camera error, trying without subsample");
                    Logger.Error(e);
                    seq.EnableSubSample = false;
                    seq.SubSambleRectangle = null;
                    image = await imagingMediator.CaptureAndPrepareImage(seq, prepareParameters, token, progress);
                }
                retries++;
                if (image == null && retries < 3) {
                    Logger.Warning($"Image acquisition failed - Retrying {retries}/2");
                }
            } while (image == null && retries < 3);

            return image;
        }

        private bool IsSubSampleEnabled() {
            var cameraInfo = cameraMediator.GetInfo();
            return profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio == 1 && cameraInfo.CanSubSample;
        }

        private async Task<bool> ValidateCalculatedFocusPosition(DataPoint focusPoint, FilterInfo filter, CancellationToken token, IProgress<ApplicationStatus> progress, double initialHFR) {
            var rSquaredThreshold = profileService.ActiveProfile.FocuserSettings.RSquaredThreshold;
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR) {
                // Evaluate R² for Fittings to be above threshold

                if (rSquaredThreshold > 0) {
                    var hyperbolicBad = HyperbolicFitting.RSquared < rSquaredThreshold;
                    var quadraticBad = QuadraticFitting.RSquared < rSquaredThreshold;
                    var trendlineBad = TrendlineFitting.LeftTrend.RSquared < rSquaredThreshold || TrendlineFitting.RightTrend.RSquared < rSquaredThreshold;

                    var fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;

                    if ((fitting == AFCurveFittingEnum.HYPERBOLIC || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) && hyperbolicBad) {
                        Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Hyperbolic Fitting is below threshold. {Math.Round(HyperbolicFitting.RSquared, 2)} / {rSquaredThreshold}");
                        Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(HyperbolicFitting.RSquared, 2), rSquaredThreshold));
                        return false;
                    }

                    if ((fitting == AFCurveFittingEnum.PARABOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && quadraticBad) {
                        Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Parabolic Fitting is below threshold. {Math.Round(QuadraticFitting.RSquared, 2)} / {rSquaredThreshold}");
                        Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(QuadraticFitting.RSquared, 2), rSquaredThreshold));
                        return false;
                    }

                    if ((fitting == AFCurveFittingEnum.TRENDLINES || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && trendlineBad) {
                        Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Trendline Fitting is below threshold. Left: {Math.Round(TrendlineFitting.LeftTrend.RSquared, 2)} / {rSquaredThreshold}; Right: {Math.Round(TrendlineFitting.RightTrend.RSquared, 2)} / {rSquaredThreshold}");
                        Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(TrendlineFitting.LeftTrend.RSquared, 2), Math.Round(TrendlineFitting.RightTrend.RSquared, 2), rSquaredThreshold));
                        return false;
                    }
                }
            }

            var min = FocusPoints.Min(x => x.X);
            var max = FocusPoints.Max(x => x.X);

            if (focusPoint.X < min || focusPoint.X > max) {
                Logger.Error($"Determined focus point position is outside of the overall measurement points of the curve. Fitting is incorrect and autofocus settings are incorrect. FocusPosition {focusPoint.X}; Min: {min}; Max:{max}");
                Notification.ShowError(Loc.Instance["LblAutoFocusPointOutsideOfBounds"]);
                return false;
            }

            _focusPosition = await focuserMediator.MoveFocuser((int)focusPoint.X, token);
            double hfr = (await GetAverageMeasurement(filter, profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint, token, progress)).Measure;

            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR && rSquaredThreshold <= 0) {
                if (initialHFR != 0 && hfr > (initialHFR * 1.15)) {
                    Logger.Warning(string.Format("New focus point HFR {0} is significantly worse than original HFR {1}", hfr, initialHFR));
                    Notification.ShowWarning(string.Format(Loc.Instance["LblAutoFocusNewWorseThanOriginal"], hfr, initialHFR));
                    return false;
                }
            }
            return true;
        }

        public void SetCurveFittings(string method, string fitting) {
            TrendlineFitting = new TrendlineFitting().Calculate(FocusPoints, method);

            if (AFMethodEnum.STARHFR.ToString() == method) {
                if (FocusPoints.Count() >= 3
                    && (AFCurveFittingEnum.PARABOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDPARABOLIC.ToString() == fitting)) {
                    QuadraticFitting = new QuadraticFitting().Calculate(FocusPoints);
                }
                if (FocusPoints.Count() >= 3
                    && (AFCurveFittingEnum.HYPERBOLIC.ToString() == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC.ToString() == fitting)) {
                    HyperbolicFitting = new HyperbolicFitting().Calculate(FocusPoints);
                }
            } else if (FocusPoints.Count() >= 3) {
                GaussianFitting = new GaussianFitting().Calculate(FocusPoints);
            }
        }

        public async Task<AutoFocusReport> StartAutoFocus(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            Logger.Trace("Starting Autofocus");

            ClearCharts();

            AutoFocusReport report = null;

            int numberOfAttempts = 0;
            System.Drawing.Rectangle oldSubSample = new System.Drawing.Rectangle();
            int initialFocusPosition = focuserMediator.GetInfo().Position;
            double initialHFR = double.NaN;

            bool tempComp = false;
            bool guidingStopped = false;
            bool completed = false;
            using (var stopWatch = MyStopWatch.Measure()) {

                try {
                    if (focuserMediator.GetInfo().TempCompAvailable && focuserMediator.GetInfo().TempComp) {
                        tempComp = true;
                        focuserMediator.ToggleTempComp(false);
                    }

                    if (profileService.ActiveProfile.FocuserSettings.AutoFocusDisableGuiding) {
                        guidingStopped = await this.guiderMediator.StopGuiding(token);
                    }

                    FilterInfo autofocusFilter = await SetAutofocusFilter(imagingFilter, token, progress);

                    initialFocusPosition = focuserMediator.GetInfo().Position;

                    if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR && profileService.ActiveProfile.FocuserSettings.RSquaredThreshold <= 0) {
                        //Get initial position information, as average of multiple exposures, if configured this way
                        initialHFR = (await GetAverageMeasurement(autofocusFilter, profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint, token, progress)).Measure;
                    }

                    bool reattempt;
                    do {
                        reattempt = false;
                        numberOfAttempts = numberOfAttempts + 1;

                        var offsetSteps = profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps;
                        var offset = offsetSteps;

                        var nrOfSteps = offsetSteps + 1;

                        await GetFocusPoints(autofocusFilter, nrOfSteps, progress, token, offset);

                        var laststeps = offset;

                        int leftcount = TrendlineFitting.LeftTrend.DataPoints.Count(), rightcount = TrendlineFitting.RightTrend.DataPoints.Count();
                        //When datapoints are not sufficient analyze and take more
                        do {
                            if (leftcount == 0 && rightcount == 0) {
                                Notification.ShowWarning(Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"]);
                                progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
                                //Reattempting in this situation is very likely meaningless - just move back to initial focus position and call it a day
                                await focuserMediator.MoveFocuser(initialFocusPosition, token);
                                return null;
                            }

                            // Let's keep moving in, one step at a time, until we have enough left trend points. Then we can think about moving out to fill in the right trend points
                            if (TrendlineFitting.LeftTrend.DataPoints.Count() < offsetSteps && FocusPoints.Where(dp => dp.X < TrendlineFitting.Minimum.X && dp.Y == 0).Count() < offsetSteps) {
                                Logger.Trace("More datapoints needed to the left of the minimum");
                                //Move to the leftmost point - this should never be necessary since we're already there, but just in case
                                if (focuserMediator.GetInfo().Position != (int)Math.Round(FocusPoints.FirstOrDefault().X)) {
                                    await focuserMediator.MoveFocuser((int)Math.Round(FocusPoints.FirstOrDefault().X), token);
                                }
                                //More points needed to the left
                                await GetFocusPoints(autofocusFilter, 1, progress, token, -1);
                            } else if (TrendlineFitting.RightTrend.DataPoints.Count() < offsetSteps && FocusPoints.Where(dp => dp.X > TrendlineFitting.Minimum.X && dp.Y == 0).Count() < offsetSteps) { //Now we can go to the right, if necessary
                                Logger.Trace("More datapoints needed to the right of the minimum");
                                //More points needed to the right. Let's get to the rightmost point, and keep going right one point at a time
                                if (focuserMediator.GetInfo().Position != (int)Math.Round(FocusPoints.LastOrDefault().X)) {
                                    await focuserMediator.MoveFocuser((int)Math.Round(FocusPoints.LastOrDefault().X), token);
                                }
                                await GetFocusPoints(autofocusFilter, 1, progress, token, 1);
                            }

                            leftcount = TrendlineFitting.LeftTrend.DataPoints.Count();
                            rightcount = TrendlineFitting.RightTrend.DataPoints.Count();

                            token.ThrowIfCancellationRequested();
                        } while (rightcount + FocusPoints.Where(dp => dp.X > TrendlineFitting.Minimum.X && dp.Y == 0).Count() < offsetSteps || leftcount + FocusPoints.Where(dp => dp.X < TrendlineFitting.Minimum.X && dp.Y == 0).Count() < offsetSteps);

                        token.ThrowIfCancellationRequested();

                        FinalFocusPoint = DetermineFinalFocusPoint();

                        report = GenerateReport(initialFocusPosition, initialHFR, autofocusFilter?.Name ?? string.Empty, stopWatch.Elapsed);
                        DurationSeconds = (int)Math.Ceiling(stopWatch.Elapsed.TotalSeconds);

                        LastAutoFocusPoint = new ReportAutoFocusPoint { Focuspoint = FinalFocusPoint, Temperature = focuserMediator.GetInfo().Temperature, Timestamp = DateTime.Now, Filter = autofocusFilter?.Name };

                        bool goodAutoFocus = await ValidateCalculatedFocusPosition(FinalFocusPoint, autofocusFilter, token, progress, initialHFR);

                        if (!goodAutoFocus) {
                            if (numberOfAttempts < profileService.ActiveProfile.FocuserSettings.AutoFocusTotalNumberOfAttempts) {
                                Notification.ShowWarning(Loc.Instance["LblAutoFocusReattempting"]);
                                await focuserMediator.MoveFocuser(initialFocusPosition, token);
                                Logger.Warning("Potentially bad auto-focus. Reattempting.");
                                FocusPoints.Clear();
                                PlotFocusPoints.Clear();
                                TrendlineFitting = null;
                                QuadraticFitting = null;
                                HyperbolicFitting = null;
                                GaussianFitting = null;
                                FinalFocusPoint = new DataPoint(0, 0);
                                reattempt = true;
                            } else {
                                Notification.ShowWarning(Loc.Instance["LblAutoFocusRestoringOriginalPosition"]);
                                Logger.Warning("Potentially bad auto-focus. Restoring original focus position.");
                                reattempt = false;
                                await focuserMediator.MoveFocuser(initialFocusPosition, token);
                                return null;
                            }
                        }
                    } while (reattempt);
                    completed = true;
                    AutoFocusInfo info = new AutoFocusInfo(report.Temperature, report.CalculatedFocusPoint.Position, report.Filter, report.Timestamp);
                    focuserMediator.BroadcastSuccessfulAutoFocusRun(info);
                } catch (OperationCanceledException) {
                    Logger.Warning("AutoFocus cancelled");
                } catch (Exception ex) {
                    Notification.ShowError(ex.Message);
                    Logger.Error("Failure during AutoFocus", ex);
                } finally {
                    if (!completed) {
                        Logger.Warning($"AutoFocus did not complete successfully, so restoring the focuser position to {initialFocusPosition}");
                        try {
                            await focuserMediator.MoveFocuser(initialFocusPosition, default);
                        } catch (Exception e) {
                            Logger.Error("Failed to restore focuser position after AutoFocus failure", e);
                        }

                        FocusPoints.Clear();
                        PlotFocusPoints.Clear();
                    }

                    //Get back to original filter, if necessary
                    try {
                        await filterWheelMediator.ChangeFilter(imagingFilter);
                    } catch (Exception e) {
                        Logger.Error("Failed to restore previous filter position after AutoFocus", e);
                        Notification.ShowError($"Failed to restore previous filter position: {e.Message}");
                    }

                    //Restore the temperature compensation of the focuser
                    if (focuserMediator.GetInfo().TempCompAvailable && tempComp) {
                        focuserMediator.ToggleTempComp(true);
                    }

                    if (guidingStopped) {
                        var startGuidingTask = this.guiderMediator.StartGuiding(false, progress, default);
                        var completedTask = await Task.WhenAny(Task.Delay(60000), startGuidingTask);
                        if (startGuidingTask != completedTask) {
                            Notification.ShowWarning(Loc.Instance["LblStartGuidingFailed"]);
                        }
                    }
                    progress.Report(new ApplicationStatus() { Status = string.Empty });
                }
                return report;
            }
        }
    }
}
