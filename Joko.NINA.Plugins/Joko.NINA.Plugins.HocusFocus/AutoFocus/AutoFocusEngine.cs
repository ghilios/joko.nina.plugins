#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.IO;
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
using NINA.Image.FileFormat;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using Nito.AsyncEx;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DrawingSize = System.Drawing.Size;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class AutoFocusEngine : IAutoFocusEngine {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IAlglibAPI alglibAPI;

        public AutoFocusEngine(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IFilterWheelMediator filterWheelMediator,
            IFocuserMediator focuserMediator,
            IGuiderMediator guiderMediator,
            IImagingMediator imagingMediator,
            IImageDataFactory imageDataFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
            IAutoFocusOptions autoFocusOptions,
            IAlglibAPI alglibAPI) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.imagingMediator = imagingMediator;
            this.guiderMediator = guiderMediator;
            this.imageDataFactory = imageDataFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
        }

        private class CurveFittingResult {

            private CurveFittingResult() {
            }

            public AutoFocusFitting Fittings { get; private set; }

            public ImmutableList<ScatterErrorPoint> RejectedPoints { get; private set; }

            private static ScatterErrorPoint RejectionTest(
                List<ScatterErrorPoint> points,
                Func<double, double> fitting,
                double confidence) {
                if (points.Count <= 3) {
                    return null;
                }

                var errors = points.Select(pt => pt.Y - fitting(pt.X)).ToArray();
                var (errorsMean, errorsStdDev) = MathNet.Numerics.Statistics.Statistics.MeanStandardDeviation(errors);
                var N = points.Count;
                var p = (1.0 - confidence) / (2 * N); // Two-tailed test
                var t = MathNet.Numerics.Distributions.StudentT.InvCDF(location: 0.0d, scale: 1.0d, freedom: (double)(N - 2), p: p);
                var t2 = t * t;
                var grubbZLimit = (double)(N - 1) / Math.Sqrt(N) * Math.Sqrt(t2 / (t2 + N - 2));

                var maxError = errors.Select((e, i) => (e, i)).OrderByDescending(v => Math.Abs(v.e)).First();
                var maxErrorZScore = Math.Abs(maxError.e) / errorsStdDev;
                if (maxErrorZScore < grubbZLimit) {
                    return null;
                }
                return points[maxError.i];
            }

            public static CurveFittingResult Calculate(
                AutoFocusState state,
                AFMethodEnum method,
                AFCurveFittingEnum fitting,
                List<ScatterErrorPoint> focusPoints) {
                var validFocusPoints = focusPoints.Where(p => p.Y > 0.0).ToList();
                if (validFocusPoints.Count < 3) {
                    return null;
                }

                var maxOutlierRejectedPoints = state.Options.MaxOutlierRejections;
                var rejectionConfidence = state.Options.OutlierRejectionConfidence;
                var outlierRejectedPoints = 0;

                var rejectedPoints = focusPoints.Where(p => p.Y <= 0.0).ToList();
                while (true) {
                    var fittings = new AutoFocusFitting() { Method = method, CurveFittingType = fitting };
                    ScatterErrorPoint rejectedPoint = null;
                    if (AFMethodEnum.STARHFR == method) {
                        if (validFocusPoints.Count >= 2) {
                            // Always calculate a trendline fit, since that is used to determine when to end the focus routine
                            fittings.TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method.ToString());
                        }
                        if (validFocusPoints.Count >= 3) {
                            if (AFCurveFittingEnum.PARABOLIC == fitting || AFCurveFittingEnum.TRENDPARABOLIC == fitting) {
                                fittings.QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                                rejectedPoint = RejectionTest(points: validFocusPoints, fitting: fittings.QuadraticFitting.Fitting, confidence: rejectionConfidence);
                            }

                            if (AFCurveFittingEnum.HYPERBOLIC == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC == fitting) {
                                AlglibHyperbolicFitting hf;
                                if (state.Options.UnevenHyperbolicFitEnabled) {
                                    hf = HyperbolicUnevenFittingAlglib.Create(state.AlglibAPI, validFocusPoints, state.Options.AutoFocusStepSize, state.Options.WeightedHyperbolicFitEnabled);
                                } else {
                                    hf = HyperbolicFittingAlglib.Create(state.AlglibAPI, validFocusPoints, state.Options.WeightedHyperbolicFitEnabled);
                                }
                                if (!hf.Solve()) {
                                    Logger.Trace($"Hyperbolic fit failed");
                                }
                                fittings.HyperbolicFitting = hf;
                                rejectedPoint = RejectionTest(points: validFocusPoints, fitting: fittings.HyperbolicFitting.Fitting, confidence: rejectionConfidence);
                            }
                        }
                    } else if (validFocusPoints.Count >= 3) {
                        fittings.TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method.ToString());
                        fittings.GaussianFitting = new GaussianFitting().Calculate(validFocusPoints);
                        rejectedPoint = RejectionTest(points: validFocusPoints, fitting: fittings.GaussianFitting.Fitting, confidence: rejectionConfidence);
                    }

                    if (rejectedPoint == null || outlierRejectedPoints >= maxOutlierRejectedPoints) {
                        return new CurveFittingResult() {
                            Fittings = fittings,
                            RejectedPoints = ImmutableList.CreateRange(rejectedPoints)
                        };
                    }

                    outlierRejectedPoints++;
                    rejectedPoints.Add(rejectedPoint);
                    validFocusPoints.Remove(rejectedPoint);
                }
            }
        }

        private class AutoFocusRegionState {

            public AutoFocusRegionState(
                AutoFocusState state,
                int regionIndex,
                StarDetectionRegion region,
                AFMethodEnum afMethod,
                AFCurveFittingEnum afCurveFittingType) {
                this.State = state;
                this.RegionIndex = regionIndex;
                this.Region = region;
                this.Fittings.Method = afMethod;
                this.Fittings.CurveFittingType = afCurveFittingType;
            }

            public AutoFocusState State { get; private set; }
            public int RegionIndex { get; private set; }
            public StarDetectionRegion Region { get; private set; }
            public object SubMeasurementsLock { get; private set; } = new object();
            public DataPoint? FinalFocusPoint { get; private set; }
            public MeasureAndError? InitialHFR { get; set; }
            public MeasureAndError? FinalHFR { get; set; }
            public List<MeasureAndError> InitialHFRSubMeasurements { get; private set; } = new List<MeasureAndError>();
            public List<MeasureAndError> FinalHFRSubMeasurements { get; private set; } = new List<MeasureAndError>();
            public Dictionary<int, MeasureAndError> MeasurementsByFocuserPoint { get; private set; } = new Dictionary<int, MeasureAndError>();
            public Dictionary<int, List<MeasureAndError>> SubMeasurementsByFocuserPoints { get; private set; } = new Dictionary<int, List<MeasureAndError>>();
            public AutoFocusFitting Fittings { get; private set; } = new AutoFocusFitting();
            public Dictionary<int, MeasureAndError> RejectedPoints { get; private set; } = new Dictionary<int, MeasureAndError>();

            public void ResetMeasurements() {
                lock (SubMeasurementsLock) {
                    this.MeasurementsByFocuserPoint.Clear();
                    this.SubMeasurementsByFocuserPoints.Clear();
                    this.RejectedPoints.Clear();
                    this.FinalHFRSubMeasurements.Clear();
                    this.FinalHFR = null;
                    this.Fittings.Reset();
                }
            }

            public void ResetInitialHFRMeasurements() {
                lock (SubMeasurementsLock) {
                    this.InitialHFRSubMeasurements.Clear();
                    this.InitialHFR = null;
                }
            }

            private List<ScatterErrorPoint> lastValidFocusPoints;

            public void UpdateCurveFittings(List<ScatterErrorPoint> validFocusPoints) {
                this.lastValidFocusPoints = validFocusPoints;
                CalculateCurveFittings();
            }

            private void CalculateCurveFittings() {
                var fittingsResult = CurveFittingResult.Calculate(state: this.State, method: this.Fittings.Method, fitting: this.Fittings.CurveFittingType, focusPoints: this.lastValidFocusPoints);
                if (fittingsResult == null) {
                    return;
                }

                lock (SubMeasurementsLock) {
                    this.Fittings.TrendlineFitting = fittingsResult.Fittings.TrendlineFitting;
                    this.Fittings.GaussianFitting = fittingsResult.Fittings.GaussianFitting;
                    this.Fittings.QuadraticFitting = fittingsResult.Fittings.QuadraticFitting;
                    this.Fittings.HyperbolicFitting = fittingsResult.Fittings.HyperbolicFitting;
                    this.RejectedPoints.Clear();
                    foreach (var rp in fittingsResult.RejectedPoints) {
                        var focuserPosition = (int)Math.Round(rp.X);
                        this.RejectedPoints[focuserPosition] = new MeasureAndError() { Measure = rp.Y, Stdev = rp.ErrorY };
                    }
                }
            }

            public void CalculateFinalFocusPoint() {
                // TODO: Only do this for Hyperbolic fit when uneven is configured
                // this.CalculateCurveFittings(true);
                this.FinalFocusPoint = DetermineFinalFocusPoint();
            }

            private DataPoint? DetermineFinalFocusPoint() {
                using (MyStopWatch.Measure()) {
                    var method = Fittings.Method;
                    var fitting = Fittings.CurveFittingType;

                    if (method == AFMethodEnum.STARHFR) {
                        if (fitting == AFCurveFittingEnum.TRENDLINES) {
                            return Fittings.TrendlineFitting?.Intersection;
                        }

                        if (fitting == AFCurveFittingEnum.HYPERBOLIC) {
                            return Fittings.HyperbolicFitting?.Minimum;
                        }

                        if (fitting == AFCurveFittingEnum.PARABOLIC) {
                            return Fittings.QuadraticFitting?.Minimum;
                        }

                        if (fitting == AFCurveFittingEnum.TRENDPARABOLIC) {
                            if (Fittings.TrendlineFitting == null || Fittings.QuadraticFitting == null) {
                                return null;
                            }

                            return new DataPoint(Math.Round((Fittings.TrendlineFitting.Intersection.X + Fittings.QuadraticFitting.Minimum.X) / 2), (Fittings.TrendlineFitting.Intersection.Y + Fittings.QuadraticFitting.Minimum.Y) / 2);
                        }

                        if (fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                            if (Fittings.TrendlineFitting == null || Fittings.HyperbolicFitting == null) {
                                return null;
                            }

                            return new DataPoint(Math.Round((Fittings.TrendlineFitting.Intersection.X + Fittings.HyperbolicFitting.Minimum.X) / 2), (Fittings.TrendlineFitting.Intersection.Y + Fittings.HyperbolicFitting.Minimum.Y) / 2);
                        }

                        Logger.Error($"Invalid AutoFocus Fitting {fitting} for method {method}");
                        return new DataPoint();
                    } else {
                        return Fittings.GaussianFitting?.Maximum;
                    }
                }
            }
        }

        private class AutoFocusImageState : IDisposable {

            public AutoFocusImageState(AutoFocusState state, int attemptNumber, int imageNumber, int frameNumber, int focuserPosition, bool finalValidation) {
                this.state = state;
                this.AttemptNumber = attemptNumber;
                this.ImageNumber = imageNumber;
                this.FrameNumber = frameNumber;
                this.FocuserPosition = focuserPosition;
                this.FinalValidation = finalValidation;
            }

            public int AttemptNumber { get; private set; }
            public int ImageNumber { get; private set; }
            public int FrameNumber { get; private set; }
            public int FocuserPosition { get; private set; }
            public bool FinalValidation { get; private set; }
            public StarDetectionResult StarDetectionResult { get; set; }

            private bool measurementStarted = false;
            private readonly AutoFocusState state;
            private bool disposed = false;

            public void Dispose() {
                Logger.Trace($"Dispose - Attempt: {AttemptNumber}, Image: {ImageNumber + 1}, Frame: {FrameNumber}");
                if (!disposed) {
                    if (measurementStarted) {
                        state.MeasurementCompleted();
                    }
                    state.ExposureSemaphore.Release();
                }
                disposed = true;
            }

            private void EnsureNotDisposed() {
                if (disposed) {
                    throw new InvalidOperationException("AutoFocusImageState disposed already");
                }
            }

            public void MeasurementStarted() {
                EnsureNotDisposed();
                if (measurementStarted) {
                    throw new InvalidOperationException("MeasurementStarted can be called only once");
                }
                measurementStarted = true;
                state.MeasurementStarted();
            }
        }

        private class AutoFocusState {

            public AutoFocusState(
                AutoFocusEngineOptions options,
                FilterInfo autoFocusFilter,
                List<StarDetectionRegion> regions,
                IAlglibAPI alglibAPI) {
                this.Options = options;
                this.AutoFocusFilter = autoFocusFilter;
                this.ExposureSemaphore = new SemaphoreSlim(options.MaxConcurrent, options.MaxConcurrent);
                this.MeasurementCompleteEvent = new AsyncAutoResetEvent(false);
                AttemptNumber = 0;
                ImageNumber = 0;
                InitialFocuserPosition = -1;
                this.FocusRegions = ImmutableList.ToImmutableList(regions ?? Enumerable.Empty<StarDetectionRegion>());
                if (regions == null) {
                    this.FocusRegionStates = ImmutableList.Create(new AutoFocusRegionState(this, 0, null, options.AutoFocusMethod, options.AutoFocusCurveFitting));
                } else {
                    this.FocusRegionStates = ImmutableList.ToImmutableList(regions.Select((r, i) => new AutoFocusRegionState(this, i, r, options.AutoFocusMethod, options.AutoFocusCurveFitting)));
                }
                this.AlglibAPI = alglibAPI;
            }

            public IAlglibAPI AlglibAPI { get; private set; }
            public AutoFocusEngineOptions Options { get; private set; }
            public DrawingSize ImageSize { get; set; }
            public ImmutableList<StarDetectionRegion> FocusRegions { get; private set; }
            public ImmutableList<AutoFocusRegionState> FocusRegionStates { get; private set; }
            public int AttemptNumber { get; private set; }
            public int ImageNumber { get; private set; }
            public FilterInfo AutoFocusFilter { get; private set; }
            public object StatesLock { get; private set; } = new object();
            public SemaphoreSlim ExposureSemaphore { get; private set; }
            public int InitialFocuserPosition { get; set; }
            public List<Task> InitialHFRTasks { get; private set; } = new List<Task>();
            public List<Task> AnalysisTasks { get; private set; } = new List<Task>();
            public AsyncAutoResetEvent MeasurementCompleteEvent { get; private set; }
            public string SaveFolder { get; set; } = "";

            private volatile int measurementsInProgress;
            public int MeasurementsInProgress { get => measurementsInProgress; }

            public void MeasurementStarted() {
                Interlocked.Increment(ref measurementsInProgress);
            }

            public void MeasurementCompleted() {
                Interlocked.Decrement(ref measurementsInProgress);
                MeasurementCompleteEvent.Set();
            }

            public void OnNextAttempt() {
                ResetFocusMeasurements();
                ImageNumber = 0;
                ++AttemptNumber;
            }

            public async Task<AutoFocusImageState> OnNextImage(int frameNumber, int focuserPosition, bool finalValidation, CancellationToken token) {
                Logger.Trace($"OnNextImage - Attempt: {AttemptNumber}, Image: {ImageNumber + 1}, Frame: {frameNumber}, FocuserPosition: {focuserPosition}, FinalValidation: {finalValidation}");
                using (MyStopWatch.Measure("Waiting on ExposureSemaphore")) {
                    await ExposureSemaphore.WaitAsync(token);
                }

                var imageNumber = ++ImageNumber;
                return new AutoFocusImageState(this, attemptNumber: AttemptNumber, imageNumber: imageNumber, frameNumber: frameNumber, focuserPosition: focuserPosition, finalValidation: finalValidation);
            }

            public void ResetFocusMeasurements() {
                lock (StatesLock) {
                    this.AnalysisTasks.Clear();
                    this.FocusRegionStates.ForEach(s => s.ResetMeasurements());
                }
            }

            public void ResetInitialHFRMeasurements() {
                lock (StatesLock) {
                    this.AnalysisTasks.Clear();
                    this.FocusRegionStates.ForEach(s => s.ResetInitialHFRMeasurements());
                }
            }

            public void UpdateCurveFittings(List<ScatterErrorPoint> validFocusPoints) {
                if (FocusRegionStates.Count > 1) {
                    throw new InvalidOperationException($"Cannot update curve fittings when multiple regions are being used");
                }

                FocusRegionStates[0].UpdateCurveFittings(validFocusPoints);
            }
        }

        private async Task<MeasureAndError> EvaluateExposure(
            AutoFocusState state,
            AutoFocusRegionState regionState,
            AutoFocusImageState imageState,
            IRenderedImage image,
            CancellationToken token) {
            Logger.Trace($"Evaluating auto focus exposure at position {imageState.FocuserPosition}");

            var imageProperties = image.RawImageData.Properties;

            // Very simple to directly provide result if we use statistics based contrast detection
            if (state.Options.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                var imageStatistics = await image.RawImageData.Statistics.Task;
                return new MeasureAndError() { Measure = 100 * imageStatistics.StDev / imageStatistics.Mean, Stdev = 0.01 };
            }

            System.Windows.Media.PixelFormat pixelFormat;

            if (imageProperties.IsBayered && state.Options.DebayerImage) {
                pixelFormat = System.Windows.Media.PixelFormats.Rgb48;
            } else {
                pixelFormat = System.Windows.Media.PixelFormats.Gray16;
            }

            if (state.Options.AutoFocusMethod == AFMethodEnum.STARHFR) {
                var starDetection = starDetectionSelector.GetBehavior();
                var analysisParams = new StarDetectionParams() {
                    Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    NumberOfAFStars = state.Options.NumberOfAFStars,
                    IsAutoFocus = true
                };

                StarDetectionResult analysisResult;
                if (regionState.Region == null) {
                    if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && !IsSubSampleEnabled(state)) {
                        analysisParams.UseROI = true;
                        analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                        analysisParams.OuterCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio;
                    }
                    analysisResult = await starDetection.Detect(image, pixelFormat, analysisParams, progress: null, token);
                } else {
                    var hfStarDetection = (IHocusFocusStarDetection)starDetection;
                    var hfParams = hfStarDetection.ToHocusFocusParams(analysisParams);
                    var starDetectorParams = hfStarDetection.GetStarDetectorParams(image, regionState.Region, true);
                    var hfAnalysisResult = (HocusFocusStarDetectionResult)await hfStarDetection.Detect(image, hfParams, starDetectorParams, null, token);
                    hfAnalysisResult.FocuserPosition = imageState.FocuserPosition;
                    analysisResult = hfAnalysisResult;
                }

                if (!string.IsNullOrWhiteSpace(state.SaveFolder)) {
                    var saveAttemptFolder = GetSaveAttemptFolder(state, imageState.AttemptNumber, imageState.FinalValidation);
                    var resultFileName = $"{imageState.ImageNumber:00}_Frame{imageState.FrameNumber:00}_Region{regionState.RegionIndex:00}_star_detection_result.json";
                    var resultTargetPath = Path.Combine(saveAttemptFolder, resultFileName);
                    File.WriteAllText(resultTargetPath, JsonConvert.SerializeObject(analysisResult, Formatting.Indented));

                    var annotatedFileName = $"{imageState.ImageNumber:00}_Frame{imageState.FrameNumber:00}_Region{regionState.RegionIndex:00}_annotated.png";
                    var annotatedTargetPath = Path.Combine(saveAttemptFolder, annotatedFileName);
                    var annotator = starAnnotatorSelector.GetBehavior();
                    var annotatedImage = await annotator.GetAnnotatedImage(analysisParams, analysisResult, image.Image);

                    // If this is null, then we didn't subsample the image. Thus we can crop the annotated image and save only the relevant part
                    if (!IsSubSampleEnabled(state)) {
                        var starDetectionRegion = regionState.Region ?? StarDetectionRegion.FromStarDetectionParams(analysisParams);
                        if (!starDetectionRegion.IsFull()) {
                            var imageSize = new System.Drawing.Size(width: image.RawImageData.Properties.Width, height: image.RawImageData.Properties.Height);
                            var cropRect = starDetectionRegion.OuterBoundary.ToInt32Rect(imageSize);
                            annotatedImage = new CroppedBitmap(annotatedImage, cropRect);
                        }
                    }

                    using (var fileStream = new FileStream(annotatedTargetPath, FileMode.Create)) {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(annotatedImage));
                        encoder.Save(fileStream);
                    }
                }

                imageState.StarDetectionResult = analysisResult;

                Logger.Debug($"Current Focus - Position: {imageState.FocuserPosition}, HFR: {analysisResult.AverageHFR}");
                return new MeasureAndError() { Measure = analysisResult.AverageHFR, Stdev = analysisResult.HFRStdDev };
            } else {
                if (regionState.Region != null) {
                    throw new InvalidOperationException("Cannot use Contrast Detection with explicit regions");
                }

                var analysis = new ContrastDetection();
                var analysisParams = new ContrastDetectionParams() {
                    Sensitivity = profileService.ActiveProfile.ImageSettings.StarSensitivity,
                    NoiseReduction = profileService.ActiveProfile.ImageSettings.NoiseReduction,
                    Method = profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod
                };
                if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && !IsSubSampleEnabled(state)) {
                    analysisParams.UseROI = true;
                    analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                }
                var analysisResult = await analysis.Measure(image, analysisParams, progress: null, token);
                return new MeasureAndError() { Measure = analysisResult.AverageContrast, Stdev = analysisResult.ContrastStdev };
            }
        }

        private async Task<IExposureData> TakeExposure(AutoFocusState state, int focuserPosition, CancellationToken token, IProgress<ApplicationStatus> progress) {
            IExposureData image;
            var retries = 0;
            do {
                Logger.Trace($"Starting exposure for autofocus at position {focuserPosition}");
                double expTime = profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime;
                var filter = state.AutoFocusFilter;
                if (filter != null && filter.AutoFocusExposureTime > -1) {
                    expTime = filter.AutoFocusExposureTime;
                }

                if (state.Options.OverrideAutoFocusExposureTime > TimeSpan.Zero) {
                    Logger.Debug($"Overriding AutoFocus exposure time to {state.Options.OverrideAutoFocusExposureTime}");
                    expTime = state.Options.OverrideAutoFocusExposureTime.TotalSeconds;
                }
                var seq = new CaptureSequence(expTime, CaptureSequence.ImageTypes.SNAPSHOT, filter, null, 1);

                var subSampleRectangle = GetSubSampleRectangle(state);
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

                // TODO: Make sure OperationCancelled propagates everywhere
                try {
                    image = await imagingMediator.CaptureImage(seq, token, progress);
                } catch (Exception e) {
                    if (!IsSubSampleEnabled(state)) {
                        throw;
                    }

                    Logger.Warning("Camera error, trying without subsample");
                    Logger.Error(e);
                    seq.EnableSubSample = false;
                    seq.SubSambleRectangle = null;
                    image = await imagingMediator.CaptureImage(seq, token, progress);
                }
                retries++;
                if (image == null && retries < 3) {
                    Logger.Warning($"Image acquisition failed - Retrying {retries}/2");
                }
            } while (image == null && retries < 3);

            return image;
        }

        private Task FocusPointMeasurementAction(AutoFocusImageState imageState, MeasureAndError measurement, AutoFocusState state, AutoFocusRegionState regionState) {
            var focuserPosition = imageState.FocuserPosition;
            this.OnSubMeasurementPointCompleted(imageState, regionState);

            lock (regionState.SubMeasurementsLock) {
                if (!regionState.SubMeasurementsByFocuserPoints.TryGetValue(focuserPosition, out var values)) {
                    values = new List<MeasureAndError>();
                    regionState.SubMeasurementsByFocuserPoints.Add(focuserPosition, values);
                }
                values.Add(measurement);
                if (values.Count < state.Options.FramesPerPoint) {
                    return Task.CompletedTask;
                }

                var averageMeasurement = values.AverageMeasurement();
                regionState.MeasurementsByFocuserPoint.Add(focuserPosition, averageMeasurement);

                var focusPoints = regionState.MeasurementsByFocuserPoint.Select(fp => new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, Math.Max(0.001, fp.Value.Stdev))).ToList();
                regionState.UpdateCurveFittings(focusPoints);

                this.OnMeasurementPointCompleted(imageState, regionState, measurement);
            }
            return Task.CompletedTask;
        }

        private Task InitialHFRMeasurementAction(AutoFocusImageState imageState, MeasureAndError measurement, AutoFocusState state, AutoFocusRegionState regionState) {
            lock (regionState.SubMeasurementsLock) {
                regionState.InitialHFRSubMeasurements.Add(measurement);
                if (regionState.InitialHFRSubMeasurements.Count < state.Options.FramesPerPoint) {
                    return Task.CompletedTask;
                }

                regionState.InitialHFR = regionState.InitialHFRSubMeasurements.AverageMeasurement();
                OnInitialHFRCalculated(regionState.Region, regionState.InitialHFR.Value);
            }
            return Task.CompletedTask;
        }

        private Task FinalHFRMeasurementAction(AutoFocusImageState imageState, MeasureAndError measurement, AutoFocusState state, AutoFocusRegionState regionState) {
            lock (regionState.SubMeasurementsLock) {
                regionState.FinalHFRSubMeasurements.Add(measurement);
                if (regionState.FinalHFRSubMeasurements.Count < state.Options.FramesPerPoint) {
                    return Task.CompletedTask;
                }

                regionState.FinalHFR = regionState.FinalHFRSubMeasurements.AverageMeasurement();
            }
            return Task.CompletedTask;
        }

        private async Task<IRenderedImage> PrepareExposure(AutoFocusState state, AutoFocusImageState imageState, IExposureData exposureData, CancellationToken token) {
            var preparedImage = await PrepareExposure(state, await exposureData.ToImageData(null, token), token);
            if (!string.IsNullOrWhiteSpace(state.SaveFolder)) {
                var bitDepth = preparedImage.RawImageData.Properties.BitDepth;
                var isBayered = preparedImage.RawImageData.Properties.IsBayered;
                var fileName = $"{imageState.ImageNumber:00}_Frame{imageState.FrameNumber:00}_BitDepth{bitDepth}_Bayered{(isBayered ? 1 : 0)}_Focuser{imageState.FocuserPosition}";
                var imageData = preparedImage.RawImageData;
                var fsi = new FileSaveInfo(profileService) {
                    FilePath = GetSaveAttemptFolder(state, imageState.AttemptNumber, imageState.FinalValidation),
                    FilePattern = fileName
                };
                await imageData.SaveToDisk(fsi, token);
            }
            return preparedImage;
        }

        private async Task<IRenderedImage> PrepareExposure(AutoFocusState state, IImageData imageData, CancellationToken token) {
            var autoStretch = true;
            // If using contrast based statistics, no need to stretch
            if (state.Options.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                autoStretch = false;
            }

            var prepareParameters = new PrepareImageParameters(autoStretch: autoStretch, detectStars: false);
            return await imagingMediator.PrepareImage(imageData, prepareParameters, token);
        }

        private async Task AnalyzeExposure(
            IRenderedImage preparedImage,
            AutoFocusImageState imageState,
            AutoFocusState state,
            AutoFocusRegionState regionState,
            Func<AutoFocusImageState, MeasureAndError, AutoFocusState, AutoFocusRegionState, Task> action,
            CancellationToken token) {
            MeasureAndError partialMeasurement;
            try {
                partialMeasurement = await EvaluateExposure(
                    state: state,
                    regionState: regionState,
                    imageState: imageState,
                    image: preparedImage,
                    token: token);
            } catch (Exception e) {
                Logger.Error(e, $"Error while preparing and analyzing exposure at {imageState.FocuserPosition}");
                // Setting a partial measurement representing a failure to ensure the action is executed
                partialMeasurement = new MeasureAndError() { Measure = 0.0d, Stdev = double.NaN };
            }
            await action(imageState, partialMeasurement, state, regionState);
        }

        private string GetSaveAttemptFolder(AutoFocusState state, int attemptNumber, bool finalValidation) {
            string attemptFolder;
            if (finalValidation) {
                attemptFolder = Path.Combine(state.SaveFolder, $"final");
            } else if (attemptNumber == 0) {
                attemptFolder = Path.Combine(state.SaveFolder, $"initial");
            } else {
                attemptFolder = Path.Combine(state.SaveFolder, $"attempt{attemptNumber:00}");
            }

            Directory.CreateDirectory(attemptFolder);
            return attemptFolder;
        }

        private async Task StartAutoFocusPoint(
            int focuserPosition,
            AutoFocusState state,
            Func<AutoFocusImageState, MeasureAndError, AutoFocusState, AutoFocusRegionState, Task> action,
            bool finalValidation,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var attemptNumber = state.AttemptNumber;
            for (int i = 0; i < state.Options.FramesPerPoint; ++i) {
                var imageState = await state.OnNextImage(i, focuserPosition, finalValidation, token);
                token.ThrowIfCancellationRequested();

                var imageNumber = state.ImageNumber;
                var frameNumber = i;
                var exposureData = await TakeExposure(state, focuserPosition, token, progress);
                imageState.MeasurementStarted();
                try {
                    var exposureAnalysisTasks = new List<Task>();
                    var prepareExposureTask = PrepareExposure(state, imageState, exposureData, token);
                    foreach (var regionState in state.FocusRegionStates) {
                        var analysisTask = Task.Run(async () => {
                            var preparedExposure = await prepareExposureTask;
                            await AnalyzeExposure(
                                preparedExposure,
                                imageState: imageState,
                                state: state,
                                regionState: regionState,
                                action: action,
                                token: token);
                            lock (state.StatesLock) {
                                var imageProperties = preparedExposure.RawImageData.Properties;
                                state.ImageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                            }
                        }, token);
                        exposureAnalysisTasks.Add(analysisTask);
                        lock (state.StatesLock) {
                            state.AnalysisTasks.Add(analysisTask);
                        }
                    }

                    var releaseSemaphoreTask = Task.Run(async () => {
                        try {
                            await Task.WhenAll(exposureAnalysisTasks);
                        } finally {
                            imageState.Dispose();
                        }
                    }, token);
                    lock (state.StatesLock) {
                        state.AnalysisTasks.Add(releaseSemaphoreTask);
                    }
                } catch (Exception e) {
                    imageState.Dispose();
                    Logger.Error(e, $"Failed to start focus point analysis at {focuserPosition}");
                    throw;
                }
            }
        }

        private async Task StartInitialFocusPoints(int initialFocusPosition, AutoFocusState autoFocusState, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (autoFocusState.Options.AutoFocusMethod == AFMethodEnum.STARHFR && autoFocusState.Options.ValidateHfrImprovement) {
                var firstRegionState = autoFocusState.FocusRegionStates[0];
                if (firstRegionState.InitialHFR == null) {
                    autoFocusState.ResetInitialHFRMeasurements();
                    await StartAutoFocusPoint(initialFocusPosition, autoFocusState, InitialHFRMeasurementAction, false, token, progress);
                }
            }
        }

        private async Task StartBlindFocusPoints(int initialFocusPosition, AutoFocusState autoFocusState, CancellationToken token, IProgress<ApplicationStatus> progress) {
            Logger.Info("Waiting on initial HFR analysis");
            await Task.WhenAll(autoFocusState.AnalysisTasks);

            var firstRegionState = autoFocusState.FocusRegionStates[0];
            lock (firstRegionState.SubMeasurementsLock) {
                if (autoFocusState.Options.ValidateHfrImprovement && firstRegionState.InitialHFR.HasValue && firstRegionState.InitialHFR.Value.Measure == 0.0) {
                    throw new InitialHFRFailedException();
                }
            }

            // Initial set of focus point acquisition getting back to at least the starting point
            var offsetSteps = autoFocusState.Options.AutoFocusInitialOffsetSteps;
            var stepSize = autoFocusState.Options.AutoFocusStepSize;
            var targetFocuserPosition = initialFocusPosition + ((offsetSteps + 1) * stepSize);
            int leftMostPosition = int.MaxValue;
            int rightMostPosition = int.MinValue;
            for (int i = 0; i < offsetSteps; ++i) {
                var previousFocuserPosition = targetFocuserPosition;
                targetFocuserPosition = await focuserMediator.MoveFocuser(targetFocuserPosition - stepSize, token);
                if (targetFocuserPosition >= previousFocuserPosition) {
                    throw new Exception($"Focuser reached its limit at {targetFocuserPosition}");
                }

                leftMostPosition = Math.Min(leftMostPosition, targetFocuserPosition);
                rightMostPosition = Math.Max(rightMostPosition, targetFocuserPosition);
                await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
            }

            Logger.Info("Waiting on initial focuser move analyses");
            await Task.WhenAll(autoFocusState.AnalysisTasks);

            while (true) {
                token.ThrowIfCancellationRequested();

                TrendlineFitting trendlineFit;
                Dictionary<int, MeasureAndError> focusPoints;
                lock (firstRegionState.SubMeasurementsLock) {
                    trendlineFit = firstRegionState.Fittings.TrendlineFitting;
                    focusPoints = firstRegionState.MeasurementsByFocuserPoint;
                }

                var currentPosition = focuserMediator.GetInfo().Position;
                var failureCount = focusPoints.Count(fp => fp.Value.Measure == 0.0);
                if (failureCount >= offsetSteps) {
                    throw new TooManyFailedMeasurementsException(failureCount);
                }

                // When we've reached a limit on either end of the potential minimum based on trends, then we can queue up the remaining points
                // and execute the loop
                var leftTrendCount = trendlineFit.LeftTrend != null ? trendlineFit.LeftTrend.DataPoints.Count() : 0;
                var rightTrendCount = trendlineFit.RightTrend != null ? trendlineFit.RightTrend.DataPoints.Count() : 0;
                if (leftTrendCount >= offsetSteps && rightTrendCount > 0) {
                    var failedRightPoints = focusPoints.Where(fp => fp.Key > trendlineFit.Minimum.X && fp.Value.Measure == 0).Count();
                    var targetMaxFocuserPosition = trendlineFit.Minimum.X + (failedRightPoints + offsetSteps) * stepSize;
                    Logger.Info($"Enough left trend points ({leftTrendCount}) with an established minimum ({trendlineFit.Minimum.X}) to queue remaining right focus points up to {targetMaxFocuserPosition}");
                    while (rightMostPosition < targetMaxFocuserPosition) {
                        var previousTarget = rightMostPosition;
                        targetFocuserPosition = rightMostPosition + stepSize;
                        var actualFocuserPosition = await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                        if (actualFocuserPosition <= previousTarget) {
                            throw new Exception($"Focuser reached its limit at {actualFocuserPosition}");
                        }

                        rightMostPosition = targetFocuserPosition;
                        token.ThrowIfCancellationRequested();
                        await StartAutoFocusPoint(actualFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                        token.ThrowIfCancellationRequested();
                    }
                    break;
                } else if (rightTrendCount >= offsetSteps && leftTrendCount > 0) {
                    var failedLeftPoints = focusPoints.Where(fp => fp.Key < trendlineFit.Minimum.X && fp.Value.Measure == 0).Count();
                    var targetMinFocuserPosition = trendlineFit.Minimum.X - (failedLeftPoints + offsetSteps) * stepSize;
                    Logger.Info($"Enough right trend points ({rightTrendCount}) with an established minimum ({trendlineFit.Minimum.X}) to queue remaining left focus points down to {targetMinFocuserPosition}");
                    while (leftMostPosition > targetMinFocuserPosition) {
                        var previousTarget = leftMostPosition;
                        targetFocuserPosition = leftMostPosition - stepSize;
                        var actualFocuserPosition = await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                        if (actualFocuserPosition >= previousTarget) {
                            throw new Exception($"Focuser reached its limit at {actualFocuserPosition}");
                        }

                        leftMostPosition = targetFocuserPosition;
                        token.ThrowIfCancellationRequested();
                        await StartAutoFocusPoint(actualFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                        token.ThrowIfCancellationRequested();
                    }
                    break;
                }

                if (leftTrendCount < offsetSteps) {
                    var previousTarget = leftMostPosition;
                    leftMostPosition -= stepSize;
                    var actualFocuserPosition = await focuserMediator.MoveFocuser(leftMostPosition, token);
                    if (actualFocuserPosition >= previousTarget) {
                        throw new Exception($"Focuser reached its limit at {actualFocuserPosition}");
                    }

                    token.ThrowIfCancellationRequested();
                    await StartAutoFocusPoint(actualFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                    token.ThrowIfCancellationRequested();

                    Logger.Info("Waiting on next left movement analysis");
                    await Task.WhenAll(autoFocusState.AnalysisTasks);
                } else { // if (rightTrendCount < offsetSteps) {
                    var previousTarget = rightMostPosition;
                    rightMostPosition += stepSize;
                    var actualFocuserPosition = await focuserMediator.MoveFocuser(rightMostPosition, token);
                    if (actualFocuserPosition <= previousTarget) {
                        throw new Exception($"Focuser reached its limit at {actualFocuserPosition}");
                    }

                    token.ThrowIfCancellationRequested();
                    await StartAutoFocusPoint(actualFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                    token.ThrowIfCancellationRequested();

                    Logger.Info("Waiting on next right movement analysis");
                    await Task.WhenAll(autoFocusState.AnalysisTasks);
                }

                // Ensure we don't have too many measurements in flight, since we need completed analyses to determine stopping conditions
                while (autoFocusState.MeasurementsInProgress >= offsetSteps) {
                    Logger.Info($"Waiting for measurements in progress {autoFocusState.MeasurementsInProgress} to get below {offsetSteps}");
                    await autoFocusState.MeasurementCompleteEvent.WaitAsync(token);
                    token.ThrowIfCancellationRequested();
                }
            }

            Logger.Info("Waiting on remaining AutoFocus analysis tasks");
            await Task.WhenAll(autoFocusState.AnalysisTasks);
            token.ThrowIfCancellationRequested();
        }

        private async Task<AutoFocusState> InitializeState(
            AutoFocusEngineOptions options,
            FilterInfo imagingFilter,
            List<StarDetectionRegion> regions,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var autofocusFilter = await SetAutofocusFilter(imagingFilter, token, progress);
            return new AutoFocusState(
                options,
                autofocusFilter,
                regions,
                this.alglibAPI);
        }

        private async Task<bool> RunAutoFocus(
            AutoFocusState autoFocusState,
            Func<int, AutoFocusState, CancellationToken, IProgress<ApplicationStatus>, Task> pointGenerationAction,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            bool reattempt;

            using (var stopWatch = MyStopWatch.Measure()) {
                if (autoFocusState.Options.Save) {
                    if (string.IsNullOrWhiteSpace(autoFocusState.Options.SavePath)) {
                        Notification.ShowWarning("No save path specified in Hocus Focus Auto Focus Options");
                        Logger.Warning("No save path specified in Hocus Focus Auto Focus Options");
                    } else if (!Directory.Exists(autoFocusState.Options.SavePath)) {
                        Notification.ShowWarning($"The save path {autoFocusState.Options.SavePath} does not exist");
                        Logger.Warning($"The save path {autoFocusState.Options.SavePath} specified in Hocus Focus Auto Focus Options does not exist");
                    } else {
                        var folderName = $"AutoFocus_{DateTime.Now:yyyyMMdd_HHmmss}";
                        var targetPath = Path.Combine(autoFocusState.Options.SavePath, folderName);
                        Logger.Info($"Saving AutoFocus run to {targetPath}");
                        Directory.CreateDirectory(targetPath);
                        autoFocusState.SaveFolder = targetPath;
                    }
                }

                // Make sure this is set after changing the filter, in case offsets are used
                int initialFocusPosition = focuserMediator.GetInfo().Position;
                autoFocusState.InitialFocuserPosition = initialFocusPosition;

                do {
                    await StartInitialFocusPoints(initialFocusPosition, autoFocusState, token, progress);
                    reattempt = false;

                    autoFocusState.OnNextAttempt();
                    OnIterationStarted(autoFocusState.AttemptNumber);

                    var iterationTaskCts = new CancellationTokenSource();
                    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(token, iterationTaskCts.Token);
                    bool goodFocusPosition = false;

                    try {
                        await pointGenerationAction(initialFocusPosition, autoFocusState, iterationCts.Token, progress);
                        token.ThrowIfCancellationRequested();

                        goodFocusPosition = await ValidateCalculatedFocusPosition(autoFocusState, iterationCts.Token, progress);
                    } catch (TooManyFailedMeasurementsException e) {
                        // Allow retries for too many failed points retries
                        Logger.Error($"Too many failed points ({e.NumFailures})");
                        Notification.ShowWarning(Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"]);
                        progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
                    } catch (InitialHFRFailedException) {
                        // Allow retries for initial HFR failed
                        Logger.Error($"Initial HFR calculation failed");
                        Notification.ShowWarning("Calculating initial HFR failed");
                        progress.Report(new ApplicationStatus() { Status = "Calculating initial HFR failed" });
                    }

                    var duration = stopWatch.Elapsed;
                    if (!goodFocusPosition) {
                        // Ensure we cancel any remaining tasks from this iteration so we can start the next
                        iterationTaskCts.Cancel();
                        if (autoFocusState.AttemptNumber < autoFocusState.Options.TotalNumberOfAttempts) {
                            Notification.ShowWarning(Loc.Instance["LblAutoFocusReattempting"]);
                            Logger.Warning($"Potentially bad auto-focus. Setting focuser back to {initialFocusPosition} and re-attempting.");
                            await focuserMediator.MoveFocuser(initialFocusPosition, token);

                            OnIterationFailed(
                                state: autoFocusState,
                                temperature: focuserMediator.GetInfo().Temperature,
                                duration: stopWatch.Elapsed);
                            reattempt = true;
                        }
                    } else {
                        OnCompleted(
                            state: autoFocusState,
                            temperature: focuserMediator.GetInfo().Temperature,
                            duration: duration);
                        return true;
                    }
                } while (reattempt);

                OnFailed(
                    state: autoFocusState,
                    temperature: focuserMediator.GetInfo().Temperature,
                    duration: stopWatch.Elapsed);
                return false;
            }
        }

        private async Task PerformPostAutoFocusActions(
            bool successfulAutoFocus,
            int? initialFocusPosition,
            FilterInfo imagingFilter,
            bool restoreTempComp,
            bool restoreGuiding,
            IProgress<ApplicationStatus> progress) {
            var completionOperationTimeout = TimeSpan.FromMinutes(1);

            // If this fails before the initial focuser position is even set, then there's no need to restore
            if (!successfulAutoFocus && initialFocusPosition.HasValue && initialFocusPosition.Value >= 0) {
                Logger.Warning($"AutoFocus did not complete successfully, so restoring the focuser position to {initialFocusPosition}");
                try {
                    var completionTimeoutCts = new CancellationTokenSource(completionOperationTimeout);
                    await focuserMediator.MoveFocuser(initialFocusPosition.Value, completionTimeoutCts.Token);
                } catch (Exception e) {
                    Logger.Error("Failed to restore focuser position after AutoFocus failure", e);
                }
            }

            // Get back to original filter, if necessary
            try {
                var completionTimeoutCts = new CancellationTokenSource(completionOperationTimeout);
                await filterWheelMediator.ChangeFilter(imagingFilter, completionTimeoutCts.Token);
            } catch (Exception e) {
                Logger.Error("Failed to restore previous filter position after AutoFocus", e);
                Notification.ShowError($"Failed to restore previous filter position: {e.Message}");
            }

            // Restore the temperature compensation of the focuser
            if (focuserMediator.GetInfo().TempCompAvailable && restoreTempComp) {
                Logger.Info("Re-enabling temperature compensation after AutoFocus");
                focuserMediator.ToggleTempComp(true);
            }

            if (restoreGuiding) {
                var completionTimeoutCts = new CancellationTokenSource(completionOperationTimeout);
                var startGuiding = await this.guiderMediator.StartGuiding(false, progress, completionTimeoutCts.Token);
                if (completionTimeoutCts.IsCancellationRequested || !startGuiding) {
                    Logger.Warning("Failed to resume guiding after AutoFocus");
                    Notification.ShowWarning(Loc.Instance["LblStartGuidingFailed"]);
                }
            }
        }

        private ObservableRectangle GetSubSampleRectangle(AutoFocusState state) {
            if (!IsSubSampleEnabled(state)) {
                return null;
            }

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

        private bool IsSubSampleEnabled(AutoFocusState state) {
            if (state.FocusRegionStates.Count > 1) {
                return false;
            }

            var cameraInfo = cameraMediator.GetInfo();
            if (!cameraInfo.CanSubSample) {
                return false;
            }

            return profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio == 1;
        }

        private async Task<bool> ValidateCalculatedFocusPosition(
            AutoFocusState autoFocusState,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var rSquaredThreshold = profileService.ActiveProfile.FocuserSettings.RSquaredThreshold;
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR) {
                // Evaluate R² for Fittings to be above threshold
                foreach (var autoFocusRegionState in autoFocusState.FocusRegionStates) {
                    var fittings = autoFocusRegionState.Fittings;
                    if (rSquaredThreshold > 0) {
                        var hyperbolicBad = fittings.HyperbolicFitting != null && fittings.HyperbolicFitting.RSquared < rSquaredThreshold;
                        var quadraticBad = fittings.QuadraticFitting != null && fittings.QuadraticFitting.RSquared < rSquaredThreshold;
                        var trendlineBad = (fittings.TrendlineFitting?.LeftTrend != null && fittings.TrendlineFitting.LeftTrend.RSquared < rSquaredThreshold) ||
                            (fittings.TrendlineFitting?.RightTrend != null && fittings.TrendlineFitting.RightTrend.RSquared < rSquaredThreshold);

                        var fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;

                        if ((fitting == AFCurveFittingEnum.HYPERBOLIC || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) && hyperbolicBad) {
                            Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Hyperbolic Fitting is below threshold. {Math.Round(fittings.HyperbolicFitting.RSquared, 2)} / {rSquaredThreshold}; Region: {autoFocusRegionState.Region}");
                            Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.HyperbolicFitting.RSquared, 2), rSquaredThreshold));
                            return false;
                        }

                        if ((fitting == AFCurveFittingEnum.PARABOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && quadraticBad) {
                            Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Parabolic Fitting is below threshold. {Math.Round(fittings.QuadraticFitting.RSquared, 2)} / {rSquaredThreshold}; Region: {autoFocusRegionState.Region}");
                            Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.QuadraticFitting.RSquared, 2), rSquaredThreshold));
                            return false;
                        }

                        if ((fitting == AFCurveFittingEnum.TRENDLINES || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && trendlineBad) {
                            Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Trendline Fitting is below threshold. Left: {Math.Round(fittings.TrendlineFitting.LeftTrend.RSquared, 2)} / {rSquaredThreshold}; Right: {Math.Round(fittings.TrendlineFitting.RightTrend.RSquared, 2)} / {rSquaredThreshold}; Region: {autoFocusRegionState.Region}");
                            Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.TrendlineFitting.LeftTrend.RSquared, 2), Math.Round(fittings.TrendlineFitting.RightTrend.RSquared, 2), rSquaredThreshold));
                            return false;
                        }
                    }

                    var min = autoFocusRegionState.MeasurementsByFocuserPoint.Min(x => x.Key);
                    var max = autoFocusRegionState.MeasurementsByFocuserPoint.Max(x => x.Key);

                    autoFocusRegionState.CalculateFinalFocusPoint();
                    var finalFocusPosition = (int)Math.Round(autoFocusRegionState.FinalFocusPoint?.X ?? -1);
                    if (finalFocusPosition < 0) {
                        Logger.Error("Fit failed. There likely weren't enough data points with detected stars");
                        Notification.ShowError("Fit failed. There likely weren't enough data points with detected stars");
                        return false;
                    }

                    if (finalFocusPosition < min || finalFocusPosition > max) {
                        Logger.Error($"Determined focus point position is outside of the overall measurement points of the curve. Fitting is incorrect and autofocus settings are incorrect. FocusPosition {finalFocusPosition}; Min: {min}; Max: {max}; Region: {autoFocusRegionState.Region}");
                        Notification.ShowError(Loc.Instance["LblAutoFocusPointOutsideOfBounds"]);
                        return false;
                    }
                }
            }

            var firstRegionFinalFocusPosition = (int)Math.Round(autoFocusState.FocusRegionStates[0].FinalFocusPoint?.X ?? -1);
            if (firstRegionFinalFocusPosition < 0) {
                Logger.Error("Fit failed. There likely weren't enough data points with detected stars");
                Notification.ShowError("Fit failed. There likely weren't enough data points with detected stars");
                return false;
            }

            if (this.autoFocusOptions.FocuserOffset != 0) {
                Logger.Info($"Applying focuser offset of {this.autoFocusOptions.FocuserOffset} to {firstRegionFinalFocusPosition}");
                firstRegionFinalFocusPosition += this.autoFocusOptions.FocuserOffset;
            }

            await focuserMediator.MoveFocuser(firstRegionFinalFocusPosition, token);
            token.ThrowIfCancellationRequested();

            if (autoFocusState.Options.ValidateHfrImprovement) {
                Logger.Info($"Validating HFR at final focus position {firstRegionFinalFocusPosition}");
                await StartAutoFocusPoint(firstRegionFinalFocusPosition, autoFocusState, FinalHFRMeasurementAction, true, token, progress);
                token.ThrowIfCancellationRequested();
            }

            await Task.WhenAll(autoFocusState.AnalysisTasks);
            token.ThrowIfCancellationRequested();

            if (autoFocusState.Options.AutoFocusMethod == AFMethodEnum.STARHFR && autoFocusState.Options.ValidateHfrImprovement) {
                foreach (var autoFocusRegionState in autoFocusState.FocusRegionStates) {
                    lock (autoFocusRegionState.SubMeasurementsLock) {
                        if (!autoFocusRegionState.FinalHFR.HasValue || autoFocusRegionState.FinalHFR.Value.Measure == 0.0) {
                            Logger.Warning("Failed assessing HFR at the final focus point");
                            Notification.ShowWarning("Failed assessing HFR at the final focus point");
                            return false;
                        }
                        if (!autoFocusRegionState.InitialHFR.HasValue || autoFocusRegionState.InitialHFR.Value.Measure == 0.0) {
                            Logger.Warning("Failed assessing HFR at the initial position");
                            Notification.ShowWarning("Failed assessing HFR at the initial position");
                            return false;
                        }

                        var finalHfr = autoFocusRegionState.FinalHFR?.Measure;
                        var initialHFR = autoFocusRegionState.InitialHFR?.Measure;
                        if (finalHfr > (initialHFR * (1.0 + autoFocusState.Options.HFRImprovementThreshold))) {
                            Logger.Warning($"New focus point HFR {finalHfr} is significantly worse than original HFR {initialHFR}");
                            Notification.ShowWarning(string.Format(Loc.Instance["LblAutoFocusNewWorseThanOriginal"], finalHfr, initialHFR));
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public async Task<FilterInfo> SetAutofocusFilter(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (profileService.ActiveProfile.FocuserSettings.UseFilterWheelOffsets) {
                var filter = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Where(f => f.AutoFocusFilter == true).FirstOrDefault();
                if (filter == null) {
                    return imagingFilter;
                }

                // Set the filter to the autofocus filter if necessary, and move to it so autofocus X indexing works properly when invoking GetFocusPoints()
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

        private static bool autoFocusInProgress = false;

        public bool AutoFocusInProgress {
            get => autoFocusInProgress;
            private set {
                autoFocusInProgress = value;
            }
        }

        private async Task<AutoFocusResult> RunImpl(AutoFocusEngineOptions options, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (AutoFocusInProgress) {
                Notification.ShowError("Another AutoFocus is already in progress");
                Logger.Error("Another AutoFocus is already in progress");
                return null;
            }

            Logger.Trace("Starting Autofocus");
            OnStarted();

            var timeoutCts = new CancellationTokenSource(options.AutoFocusTimeout);
            bool tempComp = false;
            bool guidingStopped = false;
            bool completed = false;
            AutoFocusInProgress = true;
            AutoFocusState autoFocusState = null;
            try {
                if (focuserMediator.GetInfo().TempCompAvailable && focuserMediator.GetInfo().TempComp) {
                    tempComp = true;
                    focuserMediator.ToggleTempComp(false);
                }

                if (profileService.ActiveProfile.FocuserSettings.AutoFocusDisableGuiding) {
                    guidingStopped = await this.guiderMediator.StopGuiding(token);
                }

                var autofocusCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                autoFocusState = await InitializeState(options, imagingFilter, regions, autofocusCts.Token, progress);
                completed = await RunAutoFocus(autoFocusState, StartBlindFocusPoints, autofocusCts.Token, progress);
            } catch (OperationCanceledException) {
                if (timeoutCts.IsCancellationRequested) {
                    Notification.ShowWarning($"AutoFocus timed out after {options.AutoFocusTimeout}");
                    Logger.Warning($"AutoFocus timed out after {options.AutoFocusTimeout}");
                } else {
                    Logger.Warning("AutoFocus cancelled");
                }
            } catch (Exception ex) {
                Notification.ShowError($"Auto Focus Failure. {ex.Message}");
                Logger.Error("Failure during AutoFocus", ex);
            } finally {
                try {
                    await PerformPostAutoFocusActions(
                        successfulAutoFocus: completed, initialFocusPosition: autoFocusState?.InitialFocuserPosition, imagingFilter: imagingFilter, restoreTempComp: tempComp,
                        restoreGuiding: guidingStopped, progress: progress);
                } catch (Exception ex) {
                    Logger.Warning($"Failure during post AF actions. {ex.Message}");
                } finally {
                    progress.Report(new ApplicationStatus() { Status = string.Empty });
                    AutoFocusInProgress = false;
                }
            }

            return new AutoFocusResult() {
                Succeeded = completed,
                InitialFocuserPosition = autoFocusState.InitialFocuserPosition,
                ImageSize = autoFocusState.ImageSize,
                RegionResults = autoFocusState.FocusRegionStates.Select(rs => new AutoFocusRegionResult() {
                    RegionIndex = rs.RegionIndex,
                    Region = rs.Region,
                    EstimatedFinalFocuserPosition = rs.FinalFocusPoint?.X ?? double.NaN,
                    EstimatedFinalHFR = rs.FinalFocusPoint?.Y ?? double.NaN,
                    Fittings = rs.Fittings,
                    RejectedPoints = rs.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
                }).OrderBy(r => r.RegionIndex).ToArray()
            };
        }

        public Task<AutoFocusResult> Run(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            return RunImpl(options, imagingFilter, null, token, progress);
        }

        public Task<AutoFocusResult> RunWithRegions(AutoFocusEngineOptions options, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (regions == null || regions.Count == 0) {
                throw new ArgumentException("At least one star detection region must be provided");
            }
            var selectedDetector = starDetectionSelector.SelectedBehavior;
            if (!(selectedDetector is HocusFocusStarDetection)) {
                throw new ArgumentException($"Hocus Focus must be used as the star detector to auto focus with specific regions");
            }
            return RunImpl(options, imagingFilter, regions, token, progress);
        }

        private async Task<IRenderedImage> ReloadSavedFile(
            AutoFocusState state,
            SavedAutoFocusImage savedFile,
            CancellationToken token) {
            var isBayered = savedFile.IsBayered;
            var bitDepth = savedFile.BitDepth;

            var sw = new Stopwatch();
            sw.Start();
            var imageData = await this.imageDataFactory.CreateFromFile(savedFile.Path, bitDepth, isBayered, profileService.ActiveProfile.CameraSettings.RawConverter, token);
            sw.Stop();
            Logger.Info($"Load file took {sw.Elapsed}");
            return await PrepareExposure(state, imageData, token);
        }

        private async Task<MeasureAndError> AnalyzeSavedFile(
            AutoFocusState state,
            AutoFocusRegionState regionState,
            AutoFocusImageState imageState,
            IRenderedImage renderedImage,
            CancellationToken token) {
            try {
                state.MeasurementStarted();
                return await EvaluateExposure(
                    state: state,
                    regionState: regionState,
                    imageState: imageState,
                    image: renderedImage,
                    token: token);
            } finally {
                state.MeasurementCompleted();
            }
        }

        public Task<AutoFocusResult> Rerun(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            return RerunImpl(options, savedAttempt, imagingFilter, null, token, progress);
        }

        public Task<AutoFocusResult> RerunWithRegions(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (regions == null || regions.Count == 0) {
                throw new ArgumentException("At least one star detection region must be provided");
            }
            var selectedDetector = starDetectionSelector.SelectedBehavior;
            if (!(selectedDetector is HocusFocusStarDetection)) {
                throw new ArgumentException($"Hocus Focus must be used as the star detector to auto focus with specific regions");
            }
            return RerunImpl(options, savedAttempt, imagingFilter, regions, token, progress);
        }

        private async Task<AutoFocusResult> RerunImpl(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress) {
            OnStarted();

            var state = await InitializeState(options, imagingFilter, regions, token, progress);
            if (state.Options.Save) {
                if (string.IsNullOrWhiteSpace(state.Options.SavePath)) {
                    Notification.ShowWarning("No save path specified in Hocus Focus Auto Focus Options");
                    Logger.Warning("No save path specified in Hocus Focus Auto Focus Options");
                } else if (!Directory.Exists(state.Options.SavePath)) {
                    Notification.ShowWarning($"The save path {state.Options.SavePath} does not exist");
                    Logger.Warning($"The save path {state.Options.SavePath} specified in Hocus Focus Auto Focus Options does not exist");
                } else {
                    var folderName = $"AutoFocus_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var targetPath = Path.Combine(state.Options.SavePath, folderName);
                    Logger.Info($"Saving AutoFocus run to {targetPath}");
                    Directory.CreateDirectory(targetPath);
                    state.SaveFolder = targetPath;
                }
            }

            state.OnNextAttempt();
            OnIterationStarted(state.AttemptNumber);
            foreach (var regionState in state.FocusRegionStates) {
                regionState.InitialHFR = new MeasureAndError() { Measure = 0.1d, Stdev = 0.0d };
            }

            var savedFiles = savedAttempt.SavedImages;
            var focuserPositionTasks = new List<Task>();
            int completedCount = 0;
            int totalCount = savedFiles.Count;
            progress.Report(new ApplicationStatus() {
                Status = "Data Points",
                MaxProgress = totalCount,
                Progress = 0,
                ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue
            });

            try {
                var framesPerPoint = savedFiles.Max(f => f.FrameNumber);
                state.Options.FramesPerPoint = framesPerPoint;
                foreach (var focuserPositionGroup in savedFiles.GroupBy(f => f.FocuserPosition)) {
                    var focuserPosition = focuserPositionGroup.Key;

                    // Previous versions mistakenly saved the initial image in the attempt folder, which led to duplicate key exceptions
                    var imageNumber = focuserPositionGroup.Max(g => g.ImageNumber);
                    var files = focuserPositionGroup.Where(g => g.ImageNumber == imageNumber).OrderBy(g => g.FrameNumber).ToList();
                    var allMeasurementTasks = new List<Task>();
                    foreach (var savedFile in files) {
                        var imageState = await state.OnNextImage(savedFile.FrameNumber, savedFile.FocuserPosition, false, token);

                        var localSavedFile = savedFile;
                        var loadedImage = await ReloadSavedFile(state, localSavedFile, token);
                        var singleFileAnalysisTasks = new List<Task>();
                        foreach (var regionState in state.FocusRegionStates) {
                            var partialMeasurementTask = Task.Run(async () => {
                                lock (state.StatesLock) {
                                    var imageProperties = loadedImage.RawImageData.Properties;
                                    state.ImageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                                }

                                var measurement = await AnalyzeSavedFile(state, regionState, imageState, loadedImage, token);
                                await FocusPointMeasurementAction(imageState, measurement, state, regionState);
                            });

                            singleFileAnalysisTasks.Add(partialMeasurementTask);
                        }

                        var singleFilePostTask = Task.Run(async () => {
                            try {
                                await Task.WhenAll(singleFileAnalysisTasks);
                                var incrementedCompletedCount = Interlocked.Increment(ref completedCount);
                                progress.Report(new ApplicationStatus() {
                                    Status = "Data Points",
                                    MaxProgress = totalCount,
                                    Progress = incrementedCompletedCount,
                                    ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue
                                });
                            } finally {
                                imageState.Dispose();
                            }
                        }, token);
                        allMeasurementTasks.Add(singleFilePostTask);
                    }

                    var focuserPositionTask = Task.WhenAll(allMeasurementTasks);
                    focuserPositionTasks.Add(focuserPositionTask);
                }

                await Task.WhenAll(focuserPositionTasks);
                foreach (var regionState in state.FocusRegionStates) {
                    regionState.CalculateFinalFocusPoint();
                }

                OnCompleted(state, 0.0d, TimeSpan.Zero);
                return new AutoFocusResult() {
                    Succeeded = true,
                    InitialFocuserPosition = -1, // Not known
                    ImageSize = state.ImageSize,
                    RegionResults = state.FocusRegionStates.Select(rs => new AutoFocusRegionResult() {
                        RegionIndex = rs.RegionIndex,
                        Region = rs.Region,
                        EstimatedFinalFocuserPosition = rs.FinalFocusPoint?.X ?? double.NaN,
                        EstimatedFinalHFR = rs.FinalFocusPoint?.Y ?? double.NaN,
                        Fittings = rs.Fittings,
                        RejectedPoints = rs.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
                    }).OrderBy(r => r.RegionIndex).ToArray()
                };
            } finally {
                await Task.Delay(1000);
                progress.Report(new ApplicationStatus());
            }
        }

        private void OnInitialHFRCalculated(StarDetectionRegion region, MeasureAndError initialHFRMeasurement) {
            InitialHFRCalculated?.Invoke(this, new AutoFocusInitialHFRCalculatedEventArgs() {
                Region = region,
                InitialHFR = initialHFRMeasurement
            });
        }

        private void OnIterationStarted(int iteration) {
            IterationStarted?.Invoke(this, new AutoFocusIterationStartedEventArgs() {
                Iteration = iteration
            });
        }

        private void OnStarted() {
            Started?.Invoke(this, new AutoFocusStartedEventArgs());
        }

        private void OnIterationFailed(
            AutoFocusState state,
            double temperature,
            TimeSpan duration) {
            IterationFailed?.Invoke(this, GetFailedEventArgs(state, temperature, duration));
        }

        private void OnMeasurementPointCompleted(AutoFocusImageState imageState, AutoFocusRegionState regionState, MeasureAndError measurement) {
            MeasurementPointCompleted?.Invoke(this, new AutoFocusMeasurementPointCompletedEventArgs() {
                RegionIndex = regionState.RegionIndex,
                Region = regionState.Region,
                FocuserPosition = imageState.FocuserPosition,
                Measurement = measurement,
                Fittings = regionState.Fittings.Clone(),
                RejectedPoints = regionState.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
            });
        }

        private void OnSubMeasurementPointCompleted(AutoFocusImageState imageState, AutoFocusRegionState regionState) {
            SubMeasurementPointCompleted?.Invoke(this, new AutoFocusSubMeasurementPointCompletedEventArgs() {
                RegionIndex = regionState.RegionIndex,
                Region = regionState.Region,
                FocuserPosition = imageState.FocuserPosition,
                StarDetectionResult = imageState.StarDetectionResult
            });
        }

        private void OnCompleted(
            AutoFocusState state,
            double temperature,
            TimeSpan duration) {
            var initialFocuserPosition = state.InitialFocuserPosition;
            var filter = state.AutoFocusFilter?.Name ?? string.Empty;
            var iteration = state.AttemptNumber;
            var regionHFRs = state.FocusRegionStates
                .Select(s => new AutoFocusRegionHFR() {
                    Region = s.Region,
                    InitialHFR = s.InitialHFR?.Measure,
                    EstimatedFinalHFR = s.FinalFocusPoint?.Y ?? double.NaN,
                    FinalHFR = s.FinalHFR?.Measure,
                    EstimatedFinalFocuserPosition = s.FinalFocusPoint?.X ?? double.NaN,
                    FinalFocuserPosition = (int)Math.Round(s.FinalFocusPoint?.X ?? -1),
                    Fittings = s.Fittings,
                    RejectedPoints = s.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
                }).ToImmutableList();
            Completed?.Invoke(this, new AutoFocusCompletedEventArgs() {
                Iteration = iteration,
                InitialFocusPosition = initialFocuserPosition,
                RegionHFRs = regionHFRs,
                Filter = filter,
                Temperature = temperature,
                Duration = duration,
                SaveFolder = state.SaveFolder,
                ImageSize = state.ImageSize
            });
        }

        private AutoFocusFailedEventArgs GetFailedEventArgs(
            AutoFocusState state,
            double temperature,
            TimeSpan duration) {
            var initialFocuserPosition = state.InitialFocuserPosition;
            var filter = state.AutoFocusFilter?.Name ?? string.Empty;
            var iteration = state.AttemptNumber;
            var regionHFRs = state.FocusRegionStates
                .Select(s => new AutoFocusRegionHFR() {
                    Region = s.Region,
                    InitialHFR = s.InitialHFR?.Measure,
                    EstimatedFinalHFR = s.FinalFocusPoint?.Y ?? double.NaN,
                    FinalHFR = s.FinalHFR?.Measure,
                    EstimatedFinalFocuserPosition = s.FinalFocusPoint?.X ?? double.NaN,
                    FinalFocuserPosition = (int)Math.Round(s.FinalFocusPoint?.X ?? -1),
                    Fittings = s.Fittings
                }).ToImmutableList();
            return new AutoFocusFailedEventArgs() {
                Iteration = state.AttemptNumber,
                InitialFocusPosition = initialFocuserPosition,
                RegionHFRs = regionHFRs,
                Filter = filter,
                Temperature = temperature,
                Duration = duration,
                SaveFolder = state.SaveFolder,
                ImageSize = state.ImageSize
            };
        }

        private void OnFailed(
            AutoFocusState state,
            double temperature,
            TimeSpan duration) {
            Failed?.Invoke(this, GetFailedEventArgs(state, temperature, duration));
        }

        public AutoFocusEngineOptions GetOptions(SavedAutoFocusAttempt savedAttempt = null) {
            return new AutoFocusEngineOptions() {
                DebayerImage = profileService.ActiveProfile.ImageSettings.DebayerImage,
                NumberOfAFStars = profileService.ActiveProfile.FocuserSettings.AutoFocusUseBrightestStars,
                TotalNumberOfAttempts = profileService.ActiveProfile.FocuserSettings.AutoFocusTotalNumberOfAttempts,
                ValidateHfrImprovement = autoFocusOptions.ValidateHfrImprovement,
                MaxConcurrent = autoFocusOptions.MaxConcurrent > 0 ? autoFocusOptions.MaxConcurrent : int.MaxValue,
                FramesPerPoint = profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint,
                AutoFocusMethod = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod,
                AutoFocusCurveFitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting,
                Save = autoFocusOptions.Save,
                SavePath = autoFocusOptions.SavePath,
                HFRImprovementThreshold = autoFocusOptions.HFRImprovementThreshold,
                AutoFocusTimeout = TimeSpan.FromSeconds(autoFocusOptions.AutoFocusTimeoutSeconds),
                AutoFocusInitialOffsetSteps = profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps,
                AutoFocusStepSize = savedAttempt?.StepSize ?? profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize,
                FocuserOffset = autoFocusOptions.FocuserOffset,
                MaxOutlierRejections = autoFocusOptions.MaxOutlierRejections,
                OutlierRejectionConfidence = autoFocusOptions.OutlierRejectionConfidence,
                UnevenHyperbolicFitEnabled = autoFocusOptions.UnevenHyperbolicFitEnabled,
                WeightedHyperbolicFitEnabled = autoFocusOptions.WeightedHyperbolicFitEnabled,
            };
        }

        private static readonly Regex ATTEMPT_REGEX = new Regex(@"^attempt(?<ATTEMPT>\d+)$", RegexOptions.Compiled);
        private static readonly Regex IMAGE_FILE_REGEX = new Regex(@"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public SavedAutoFocusAttempt LoadSavedFinalAttempt(string path) {
            var attemptFolder = new DirectoryInfo(path);
            return LoadSavedAttemptImpl(attemptFolder, -1, 0);
        }

        public SavedAutoFocusAttempt LoadSavedAutoFocusAttempt(string path) {
            var attemptFolder = new DirectoryInfo(path);
            var attemptMatch = ATTEMPT_REGEX.Match(attemptFolder.Name);
            if (!attemptMatch.Success || !int.TryParse(attemptMatch.Groups["ATTEMPT"].Value, out var attemptNumber)) {
                throw new Exception("A folder named attemptXX must be selected");
            }
            return LoadSavedAttemptImpl(attemptFolder, attemptNumber, 3);
        }

        private SavedAutoFocusAttempt LoadSavedAttemptImpl(DirectoryInfo attemptFolder, int attemptNumber, int minNumImages) {
            var allFiles = attemptFolder.GetFiles();
            var savedImages = new List<SavedAutoFocusImage>();
            foreach (var file in allFiles) {
                var fileNameNoExtension = Path.GetFileNameWithoutExtension(file.Name);
                var match = IMAGE_FILE_REGEX.Match(fileNameNoExtension);
                if (!match.Success) {
                    continue;
                }
                if (!int.TryParse(match.Groups["IMAGE_INDEX"].Value, out var imageIndex)) {
                    continue;
                }
                if (!int.TryParse(match.Groups["FRAME_NUMBER"].Value, out var frameNumber)) {
                    continue;
                }
                if (!int.TryParse(match.Groups["FOCUSER"].Value, out var focuserPosition)) {
                    continue;
                }
                if (!int.TryParse(match.Groups["BITDEPTH"].Value, out var bitdepth)) {
                    continue;
                }
                if (!int.TryParse(match.Groups["BAYERED"].Value, out var isBayeredInt)) {
                    continue;
                }

                var isBayered = isBayeredInt != 0;
                var savedImage = new SavedAutoFocusImage() {
                    Path = file.FullName,
                    BitDepth = bitdepth,
                    IsBayered = isBayered,
                    FocuserPosition = focuserPosition,
                    FrameNumber = frameNumber,
                    ImageNumber = imageIndex,
                };
                savedImages.Add(savedImage);
            }
            if (savedImages.Count < minNumImages) {
                throw new Exception($"Must be at least {minNumImages} saved AF images in {attemptFolder.FullName}");
            }

            int? stepSize = null;
            if (savedImages.Count >= 2) {
                var focuserPositions = savedImages.Select(i => i.FocuserPosition).OrderBy(i => i).Take(2).ToList();
                stepSize = Math.Abs(focuserPositions[0] - focuserPositions[1]);
            }
            return new SavedAutoFocusAttempt() {
                Attempt = attemptNumber,
                SavedImages = savedImages,
                StepSize = stepSize
            };
        }

        public event EventHandler<AutoFocusInitialHFRCalculatedEventArgs> InitialHFRCalculated;

        public event EventHandler<AutoFocusFailedEventArgs> IterationFailed;

        public event EventHandler<AutoFocusIterationStartedEventArgs> IterationStarted;

        public event EventHandler<AutoFocusStartedEventArgs> Started;

        public event EventHandler<AutoFocusMeasurementPointCompletedEventArgs> MeasurementPointCompleted;

        public event EventHandler<AutoFocusSubMeasurementPointCompletedEventArgs> SubMeasurementPointCompleted;

        public event EventHandler<AutoFocusCompletedEventArgs> Completed;

        public event EventHandler<AutoFocusFailedEventArgs> Failed;
    }
}