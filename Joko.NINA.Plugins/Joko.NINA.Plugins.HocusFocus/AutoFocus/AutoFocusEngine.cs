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
using System.IO;
using System.Linq;
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
            IAutoFocusOptions autoFocusOptions) {
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
        }

        private class AutoFocusRegionState {

            public AutoFocusRegionState(
                int regionIndex,
                StarDetectionRegion region,
                AFMethodEnum afMethod,
                AFCurveFittingEnum afCurveFittingType) {
                this.RegionIndex = regionIndex;
                this.Region = region;
                this.Fittings.Method = afMethod;
                this.Fittings.CurveFittingType = afCurveFittingType;
            }

            public int RegionIndex { get; private set; }
            public StarDetectionRegion Region { get; private set; }
            public object SubMeasurementsLock { get; private set; } = new object();
            public DataPoint FinalFocusPoint { get; private set; }
            public MeasureAndError? InitialHFR { get; set; }
            public MeasureAndError? FinalHFR { get; set; }
            public List<MeasureAndError> InitialHFRSubMeasurements { get; private set; } = new List<MeasureAndError>();
            public List<MeasureAndError> FinalHFRSubMeasurements { get; private set; } = new List<MeasureAndError>();
            public Dictionary<int, MeasureAndError> MeasurementsByFocuserPoint { get; private set; } = new Dictionary<int, MeasureAndError>();
            public Dictionary<int, List<MeasureAndError>> SubMeasurementsByFocuserPoints { get; private set; } = new Dictionary<int, List<MeasureAndError>>();
            public AutoFocusFitting Fittings { get; private set; } = new AutoFocusFitting();

            public void ResetMeasurements() {
                lock (SubMeasurementsLock) {
                    this.MeasurementsByFocuserPoint.Clear();
                    this.SubMeasurementsByFocuserPoints.Clear();
                    this.FinalHFRSubMeasurements.Clear();
                    this.FinalHFR = null;
                    this.Fittings.Reset();
                }
            }

            public void UpdateCurveFittings(List<ScatterErrorPoint> validFocusPoints) {
                lock (SubMeasurementsLock) {
                    var method = this.Fittings.Method;
                    var fitting = this.Fittings.CurveFittingType;
                    if (AFMethodEnum.STARHFR == method) {
                        if (validFocusPoints.Count() >= 2) {
                            // Always calculate a trendline fit, since that is used to determine when to end the focus routine
                            Fittings.TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method.ToString());
                        }
                        if (validFocusPoints.Count() >= 3) {
                            if (AFCurveFittingEnum.PARABOLIC == fitting || AFCurveFittingEnum.TRENDPARABOLIC == fitting) {
                                Fittings.QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                            }

                            if (AFCurveFittingEnum.HYPERBOLIC == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC == fitting) {
                                Fittings.HyperbolicFitting = new HyperbolicFitting().Calculate(validFocusPoints);
                            }
                        }
                    } else if (validFocusPoints.Count() >= 3) {
                        Fittings.TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method.ToString());
                        Fittings.GaussianFitting = new GaussianFitting().Calculate(validFocusPoints);
                    }
                }
            }

            public void CalculateFinalFocusPoint() {
                this.FinalFocusPoint = DetermineFinalFocusPoint();
            }

            private DataPoint DetermineFinalFocusPoint() {
                using (MyStopWatch.Measure()) {
                    var method = Fittings.Method;
                    var fitting = Fittings.CurveFittingType;

                    if (method == AFMethodEnum.STARHFR) {
                        if (fitting == AFCurveFittingEnum.TRENDLINES) {
                            return Fittings.TrendlineFitting.Intersection;
                        }

                        if (fitting == AFCurveFittingEnum.HYPERBOLIC) {
                            return Fittings.HyperbolicFitting.Minimum;
                        }

                        if (fitting == AFCurveFittingEnum.PARABOLIC) {
                            return Fittings.QuadraticFitting.Minimum;
                        }

                        if (fitting == AFCurveFittingEnum.TRENDPARABOLIC) {
                            return new DataPoint(Math.Round((Fittings.TrendlineFitting.Intersection.X + Fittings.QuadraticFitting.Minimum.X) / 2), (Fittings.TrendlineFitting.Intersection.Y + Fittings.QuadraticFitting.Minimum.Y) / 2);
                        }

                        if (fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                            return new DataPoint(Math.Round((Fittings.TrendlineFitting.Intersection.X + Fittings.HyperbolicFitting.Minimum.X) / 2), (Fittings.TrendlineFitting.Intersection.Y + Fittings.HyperbolicFitting.Minimum.Y) / 2);
                        }

                        Logger.Error($"Invalid AutoFocus Fitting {fitting} for method {method}");
                        return new DataPoint();
                    } else {
                        return Fittings.GaussianFitting.Maximum;
                    }
                }
            }
        }

        private class AutoFocusState {

            public AutoFocusState(
                AutoFocusEngineOptions options,
                FilterInfo autoFocusFilter,
                List<StarDetectionRegion> regions) {
                this.Options = options;
                this.AutoFocusFilter = autoFocusFilter;
                this.ExposureSemaphore = new SemaphoreSlim(options.MaxConcurrent, options.MaxConcurrent);
                this.MeasurementCompleteEvent = new AsyncAutoResetEvent(false);
                AttemptNumber = 0;
                ImageNumber = 0;
                InitialFocuserPosition = -1;
                this.FocusRegions = ImmutableList.ToImmutableList(regions ?? Enumerable.Empty<StarDetectionRegion>());
                if (regions == null) {
                    this.FocusRegionStates = ImmutableList.Create(new AutoFocusRegionState(0, null, options.AutoFocusMethod, options.AutoFocusCurveFitting));
                } else {
                    this.FocusRegionStates = ImmutableList.ToImmutableList(regions.Select((r, i) => new AutoFocusRegionState(i, r, options.AutoFocusMethod, options.AutoFocusCurveFitting)));
                }
            }

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

            public void OnNextImage() {
                ++ImageNumber;
            }

            public void ResetFocusMeasurements() {
                lock (StatesLock) {
                    this.AnalysisTasks.Clear();
                    this.FocusRegionStates.ForEach(s => s.ResetMeasurements());
                }
            }

            public void UpdateCurveFittings(List<ScatterErrorPoint> validFocusPoints) {
                if (FocusRegions.Count > 1) {
                    throw new InvalidOperationException($"Cannot update curve fittings when multiple regions are being used");
                }

                FocusRegionStates[0].UpdateCurveFittings(validFocusPoints);
            }
        }

        private async Task<MeasureAndError> EvaluateExposure(
            AutoFocusState state,
            AutoFocusRegionState regionState,
            bool finalValidation,
            int attemptNumber,
            int imageNumber,
            int frameNumber,
            int focuserPosition,
            IRenderedImage image,
            CancellationToken token) {
            Logger.Trace($"Evaluating auto focus exposure at position {focuserPosition}");

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
                    analysisResult = await hfStarDetection.Detect(image, hfParams, starDetectorParams, null, token);
                }

                if (!string.IsNullOrWhiteSpace(state.SaveFolder)) {
                    var saveAttemptFolder = GetSaveAttemptFolder(state, attemptNumber, finalValidation);
                    var resultFileName = $"{imageNumber:00}_Frame{frameNumber:00}_Region{regionState.RegionIndex:00}_star_detection_result.json";
                    var resultTargetPath = Path.Combine(saveAttemptFolder, resultFileName);
                    File.WriteAllText(resultTargetPath, JsonConvert.SerializeObject(analysisResult, Formatting.Indented));

                    var annotatedFileName = $"{imageNumber:00}_Frame{frameNumber:00}_Region{regionState.RegionIndex:00}_annotated.png";
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

                Logger.Debug($"Current Focus - Position: {focuserPosition}, HFR: {analysisResult.AverageHFR}");
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

        private Task FocusPointMeasurementAction(int focuserPosition, MeasureAndError measurement, AutoFocusState state, AutoFocusRegionState regionState) {
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

                var validFocusPoints = regionState.MeasurementsByFocuserPoint.Where(fp => fp.Value.Measure > 0.0).Select(fp => new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, Math.Max(0.001, fp.Value.Stdev))).ToList();
                if (validFocusPoints.Count >= 3) {
                    var autoFocusMethod = state.Options.AutoFocusMethod.ToString();
                    regionState.UpdateCurveFittings(validFocusPoints);
                }

                this.OnMeasurementPointCompleted(focuserPosition, regionState, measurement);
            }
            return Task.CompletedTask;
        }

        private Task InitialHFRMeasurementAction(int focuserPosition, MeasureAndError measurement, AutoFocusState state, AutoFocusRegionState regionState) {
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

        private Task FinalHFRMeasurementAction(int focuserPosition, MeasureAndError measurement, AutoFocusState state, AutoFocusRegionState regionState) {
            lock (regionState.SubMeasurementsLock) {
                regionState.FinalHFRSubMeasurements.Add(measurement);
                if (regionState.FinalHFRSubMeasurements.Count < state.Options.FramesPerPoint) {
                    return Task.CompletedTask;
                }

                regionState.FinalHFR = regionState.FinalHFRSubMeasurements.AverageMeasurement();
            }
            return Task.CompletedTask;
        }

        private async Task<IRenderedImage> PrepareExposure(AutoFocusState state, IExposureData exposureData, CancellationToken token) {
            return await PrepareExposure(state, await exposureData.ToImageData(null, token), token);
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
            int focuserPosition,
            int attemptNumber,
            int imageNumber,
            int frameNumber,
            bool finalValidation,
            AutoFocusState state,
            AutoFocusRegionState regionState,
            Func<int, MeasureAndError, AutoFocusState, AutoFocusRegionState, Task> action,
            CancellationToken token) {
            MeasureAndError partialMeasurement;
            try {
                partialMeasurement = await EvaluateExposure(
                    state: state,
                    regionState: regionState,
                    attemptNumber: attemptNumber,
                    imageNumber: imageNumber,
                    frameNumber: frameNumber,
                    focuserPosition: focuserPosition,
                    finalValidation: finalValidation,
                    image: preparedImage,
                    token: token);
                if (!string.IsNullOrWhiteSpace(state.SaveFolder)) {
                    var bitDepth = preparedImage.RawImageData.Properties.BitDepth;
                    var isBayered = preparedImage.RawImageData.Properties.IsBayered;
                    var fileName = $"{imageNumber:00}_Frame{frameNumber:00}_BitDepth{bitDepth}_Bayered{(isBayered ? 1 : 0)}_Focuser{focuserPosition}_HFR{partialMeasurement.Measure:00.00}";
                    var imageData = preparedImage.RawImageData;
                    var fsi = new FileSaveInfo(profileService) {
                        FilePath = GetSaveAttemptFolder(state, attemptNumber, finalValidation),
                        FilePattern = fileName
                    };
                    await imageData.SaveToDisk(fsi, token);
                }
            } catch (Exception e) {
                Logger.Error(e, $"Error while preparing and analyzing exposure at {focuserPosition}");
                // Setting a partial measurement representing a failure to ensure the action is executed
                partialMeasurement = new MeasureAndError() { Measure = 0.0d, Stdev = double.NaN };
            }
            await action(focuserPosition, partialMeasurement, state, regionState);
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
            Func<int, MeasureAndError, AutoFocusState, AutoFocusRegionState, Task> action,
            bool finalValidation,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var attemptNumber = state.AttemptNumber;
            for (int i = 0; i < state.Options.FramesPerPoint; ++i) {
                using (MyStopWatch.Measure("Waiting on ExposureSemaphore")) {
                    await state.ExposureSemaphore.WaitAsync(token);
                }
                token.ThrowIfCancellationRequested();

                state.OnNextImage();
                var imageNumber = state.ImageNumber;
                var frameNumber = i;
                var exposureData = await TakeExposure(state, focuserPosition, token, progress);
                state.MeasurementStarted();
                try {
                    var exposureAnalysisTasks = new List<Task>();
                    var prepareExposureTask = PrepareExposure(state, exposureData, token);
                    foreach (var regionState in state.FocusRegionStates) {
                        var analysisTask = Task.Run(async () => {
                            var preparedExposure = await prepareExposureTask;
                            await AnalyzeExposure(
                                preparedExposure,
                                focuserPosition: focuserPosition,
                                attemptNumber: attemptNumber,
                                imageNumber: imageNumber,
                                frameNumber: frameNumber,
                                finalValidation: finalValidation,
                                state: state,
                                regionState: regionState,
                                action: action,
                                token: token);
                            lock (state.StatesLock) {
                                var imageProperties = preparedExposure.RawImageData.Properties;
                                state.ImageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                            }
                        });
                        exposureAnalysisTasks.Add(analysisTask);
                        lock (state.StatesLock) {
                            state.AnalysisTasks.Add(analysisTask);
                        }
                    }

                    var releaseSemaphoreTask = Task.Run(async () => {
                        await Task.WhenAll(exposureAnalysisTasks);
                        state.MeasurementCompleted();
                        state.ExposureSemaphore.Release();
                    });
                    lock (state.StatesLock) {
                        state.AnalysisTasks.Add(releaseSemaphoreTask);
                    }
                } catch (Exception e) {
                    state.MeasurementCompleted();
                    state.ExposureSemaphore.Release();
                    Logger.Error(e, $"Failed to start focus point analysis at {focuserPosition}");
                    throw;
                }
            }
        }

        private async Task StartInitialFocusPoints(int initialFocusPosition, AutoFocusState autoFocusState, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (autoFocusState.Options.AutoFocusMethod == AFMethodEnum.STARHFR && autoFocusState.Options.ValidateHfrImprovement) {
                await StartAutoFocusPoint(initialFocusPosition, autoFocusState, InitialHFRMeasurementAction, false, token, progress);
            }
        }

        private async Task StartBlindFocusPoints(int initialFocusPosition, AutoFocusState autoFocusState, CancellationToken token, IProgress<ApplicationStatus> progress) {
            // Initial set of focus point acquisition getting back to at least the starting point
            var offsetSteps = autoFocusState.Options.AutoFocusInitialOffsetSteps;
            var stepSize = autoFocusState.Options.AutoFocusStepSize;
            var targetFocuserPosition = initialFocusPosition + ((offsetSteps + 1) * stepSize);
            int leftMostPosition = int.MaxValue;
            int rightMostPosition = int.MinValue;
            for (int i = 0; i < offsetSteps; ++i) {
                targetFocuserPosition -= stepSize;
                await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                leftMostPosition = Math.Min(leftMostPosition, targetFocuserPosition);
                rightMostPosition = Math.Max(rightMostPosition, targetFocuserPosition);
                await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
            }

            while (true) {
                token.ThrowIfCancellationRequested();

                TrendlineFitting trendlineFit;
                Dictionary<int, MeasureAndError> focusPoints;
                var firstRegionState = autoFocusState.FocusRegionStates[0];
                lock (firstRegionState.SubMeasurementsLock) {
                    trendlineFit = firstRegionState.Fittings.TrendlineFitting;
                    focusPoints = firstRegionState.MeasurementsByFocuserPoint;
                    if (autoFocusState.Options.ValidateHfrImprovement && firstRegionState.InitialHFR.HasValue && firstRegionState.InitialHFR.Value.Measure == 0.0) {
                        throw new InitialHFRFailedException();
                    }
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
                        targetFocuserPosition = rightMostPosition + stepSize;
                        rightMostPosition = targetFocuserPosition;
                        await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                        token.ThrowIfCancellationRequested();
                        await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                        token.ThrowIfCancellationRequested();
                    }
                    break;
                } else if (rightTrendCount >= offsetSteps && leftTrendCount > 0) {
                    var failedLeftPoints = focusPoints.Where(fp => fp.Key < trendlineFit.Minimum.X && fp.Value.Measure == 0).Count();
                    var targetMinFocuserPosition = trendlineFit.Minimum.X - (failedLeftPoints + offsetSteps) * stepSize;
                    Logger.Info($"Enough right trend points ({rightTrendCount}) with an established minimum ({trendlineFit.Minimum.X}) to queue remaining left focus points down to {targetMinFocuserPosition}");
                    while (leftMostPosition > targetMinFocuserPosition) {
                        targetFocuserPosition = leftMostPosition - stepSize;
                        leftMostPosition = targetFocuserPosition;
                        await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                        token.ThrowIfCancellationRequested();
                        await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                        token.ThrowIfCancellationRequested();
                    }
                    break;
                }

                if (leftTrendCount < offsetSteps) {
                    targetFocuserPosition = leftMostPosition - stepSize;
                    leftMostPosition = targetFocuserPosition;
                    await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                    token.ThrowIfCancellationRequested();
                    await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                    token.ThrowIfCancellationRequested();
                } else { // if (rightTrendCount < offsetSteps) {
                    targetFocuserPosition = rightMostPosition + stepSize;
                    rightMostPosition = targetFocuserPosition;
                    await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                    token.ThrowIfCancellationRequested();
                    await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, false, token, progress);
                    token.ThrowIfCancellationRequested();
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
                regions);
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
                        var folderName = $"AutoFocus_{DateTime.Now:yyyyddMM_HHmmss}";
                        var targetPath = Path.Combine(autoFocusState.Options.SavePath, folderName);
                        Logger.Info($"Saving AutoFocus run to {targetPath}");
                        Directory.CreateDirectory(targetPath);
                        autoFocusState.SaveFolder = targetPath;
                    }
                }

                // Make sure this is set after changing the filter, in case offsets are used
                int initialFocusPosition = focuserMediator.GetInfo().Position;
                autoFocusState.InitialFocuserPosition = initialFocusPosition;

                await StartInitialFocusPoints(initialFocusPosition, autoFocusState, token, progress);

                do {
                    autoFocusState.OnNextAttempt();
                    OnIterationStarted(autoFocusState.AttemptNumber);

                    reattempt = false;

                    var iterationTaskCts = new CancellationTokenSource();
                    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(token, iterationTaskCts.Token);

                    await pointGenerationAction(initialFocusPosition, autoFocusState, iterationCts.Token, progress);
                    token.ThrowIfCancellationRequested();

                    bool goodFocusPosition = await ValidateCalculatedFocusPosition(autoFocusState, iterationCts.Token, progress);
                    var duration = stopWatch.Elapsed;
                    if (!goodFocusPosition) {
                        // Ensure we cancel any remaining tasks from this iteration so we can start the next
                        iterationTaskCts.Cancel();
                        if (autoFocusState.AttemptNumber < autoFocusState.Options.TotalNumberOfAttempts) {
                            Notification.ShowWarning(Loc.Instance["LblAutoFocusReattempting"]);
                            Logger.Warning($"Potentially bad auto-focus. Setting focuser back to {initialFocusPosition} and re-attempting.");
                            await focuserMediator.MoveFocuser(initialFocusPosition, token);

                            OnIterationFailed(autoFocusState.AttemptNumber);
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
            if (state.FocusRegions.Count > 1) {
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
                        var trendlineBad = fittings.TrendlineFitting != null && (fittings.TrendlineFitting.LeftTrend.RSquared < rSquaredThreshold || fittings.TrendlineFitting.RightTrend.RSquared < rSquaredThreshold);

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
                    var finalFocusPosition = (int)Math.Round(autoFocusRegionState.FinalFocusPoint.X);
                    if (finalFocusPosition < min || finalFocusPosition > max) {
                        Logger.Error($"Determined focus point position is outside of the overall measurement points of the curve. Fitting is incorrect and autofocus settings are incorrect. FocusPosition {finalFocusPosition}; Min: {min}; Max: {max}; Region: {autoFocusRegionState.Region}");
                        Notification.ShowError(Loc.Instance["LblAutoFocusPointOutsideOfBounds"]);
                        return false;
                    }
                }
            }

            var firstRegionFinalFocusPosition = (int)Math.Round(autoFocusState.FocusRegionStates[0].FinalFocusPoint.X);

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
            } catch (TooManyFailedMeasurementsException e) {
                Logger.Error($"Too many failed points ({e.NumFailures}). Aborting auto focus");
                Notification.ShowWarning(Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"]);
                progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
            } catch (InitialHFRFailedException) {
                Logger.Error($"Initial HFR calculation failed. Aborting auto focus");
                Notification.ShowWarning("Calculating initial HFR failed. Aborting auto focus");
                progress.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
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
                await PerformPostAutoFocusActions(
                    successfulAutoFocus: completed, initialFocusPosition: autoFocusState?.InitialFocuserPosition, imagingFilter: imagingFilter, restoreTempComp: tempComp,
                    restoreGuiding: guidingStopped, progress: progress);
                progress.Report(new ApplicationStatus() { Status = string.Empty });
                AutoFocusInProgress = false;
            }

            return new AutoFocusResult() {
                Succeeded = completed,
                ImageSize = autoFocusState.ImageSize,
                RegionResults = autoFocusState.FocusRegionStates.Select(rs => new AutoFocusRegionResult() {
                    RegionIndex = rs.RegionIndex,
                    Region = rs.Region,
                    EstimatedFinalFocuserPosition = rs.FinalFocusPoint.X,
                    EstimatedFinalHFR = rs.FinalFocusPoint.Y,
                    Fittings = rs.Fittings
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

        private async Task<MeasureAndError> ReloadAndAnalyzeSavedFile(
            AutoFocusState state,
            AutoFocusRegionState regionState,
            int attemptNumber,
            SavedAutoFocusImage savedFile,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            try {
                state.MeasurementStarted();
                var isBayered = savedFile.IsBayered;
                var bitDepth = savedFile.BitDepth;
                var imageData = await this.imageDataFactory.CreateFromFile(savedFile.Path, bitDepth, isBayered, profileService.ActiveProfile.CameraSettings.RawConverter, token);
                var preparedImage = await PrepareExposure(state, imageData, token);
                return await EvaluateExposure(
                    state: state,
                    regionState: regionState,
                    attemptNumber: attemptNumber,
                    imageNumber: savedFile.ImageNumber,
                    frameNumber: savedFile.FrameNumber,
                    finalValidation: false,
                    focuserPosition: savedFile.FocuserPosition,
                    image: preparedImage,
                    token: token);
            } finally {
                state.MeasurementCompleted();
            }
        }

        public async Task<AutoFocusResult> Rerun(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            OnStarted();
            var state = await InitializeState(options, imagingFilter, null, token, progress);
            state.OnNextAttempt();
            OnIterationStarted(state.AttemptNumber);

            var regionState = state.FocusRegionStates[0];
            regionState.InitialHFR = new MeasureAndError() { Measure = 0.1d, Stdev = 0.0d };

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
                foreach (var focuserPositionGroup in savedFiles.GroupBy(f => f.FocuserPosition)) {
                    var focuserPosition = focuserPositionGroup.Key;
                    var files = focuserPositionGroup.OrderBy(g => g.FrameNumber).ToList();
                    var partialMeasurements = new List<MeasureAndError>();
                    var partialMeasurementTasks = new List<Task>();
                    foreach (var savedFile in files) {
                        using (MyStopWatch.Measure("Waiting on ExposureSemaphore")) {
                            await state.ExposureSemaphore.WaitAsync(token);
                        }

                        var partialMeasurementTask = ReloadAndAnalyzeSavedFile(state, regionState, savedAttempt.Attempt, savedFile, token, progress);
                        var postPartialMeasurementTask = Task.Run(async () => {
                            try {
                                var partialMeasurement = await partialMeasurementTask;
                                lock (partialMeasurements) {
                                    partialMeasurements.Add(partialMeasurement);
                                }

                                var incrementedCompletedCount = Interlocked.Increment(ref completedCount);
                                progress.Report(new ApplicationStatus() {
                                    Status = "Data Points",
                                    MaxProgress = totalCount,
                                    Progress = incrementedCompletedCount,
                                    ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue
                                });
                            } finally {
                                state.ExposureSemaphore.Release();
                            }
                        });
                        partialMeasurementTasks.Add(postPartialMeasurementTask);
                    }

                    var focuserPositionTask = Task.Run(async () => {
                        await Task.WhenAll(partialMeasurementTasks);
                        var focuserPositionMeasurement = partialMeasurements.AverageMeasurement();
                        await FocusPointMeasurementAction(focuserPosition, focuserPositionMeasurement, state, regionState);
                    });
                    focuserPositionTasks.Add(focuserPositionTask);
                }

                await Task.WhenAll(focuserPositionTasks);

                regionState.CalculateFinalFocusPoint();
                OnCompleted(state, 0.0d, TimeSpan.Zero);
                return new AutoFocusResult() {
                    Succeeded = true
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

        private void OnIterationFailed(int iteration) {
            IterationFailed?.Invoke(this, new AutoFocusIterationFailedEventArgs() {
                Iteration = iteration
            });
        }

        private void OnMeasurementPointCompleted(int focuserPosition, AutoFocusRegionState regionState, MeasureAndError measurement) {
            MeasurementPointCompleted?.Invoke(this, new AutoFocusMeasurementPointCompletedEventArgs() {
                RegionIndex = regionState.RegionIndex,
                Region = regionState.Region,
                FocuserPosition = focuserPosition,
                Measurement = measurement,
                Fittings = regionState.Fittings.Clone()
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
                    InitialHFR = s.InitialHFR.Value.Measure,
                    EstimatedFinalHFR = s.FinalFocusPoint.Y,
                    FinalHFR = s.FinalHFR?.Measure,
                    EstimatedFinalFocuserPosition = s.FinalFocusPoint.X,
                    FinalFocuserPosition = (int)Math.Round(s.FinalFocusPoint.X),
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

        public AutoFocusEngineOptions GetOptions() {
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
                AutoFocusStepSize = profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize
            };
        }

        public event EventHandler<AutoFocusInitialHFRCalculatedEventArgs> InitialHFRCalculated;

        public event EventHandler<AutoFocusIterationFailedEventArgs> IterationFailed;

        public event EventHandler<AutoFocusIterationStartedEventArgs> IterationStarted;

        public event EventHandler<AutoFocusStartedEventArgs> Started;

        public event EventHandler<AutoFocusMeasurementPointCompletedEventArgs> MeasurementPointCompleted;

        public event EventHandler<AutoFocusCompletedEventArgs> Completed;
    }
}