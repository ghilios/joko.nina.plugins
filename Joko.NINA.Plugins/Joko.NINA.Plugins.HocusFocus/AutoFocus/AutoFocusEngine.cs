﻿#region "copyright"

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
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using Nito.AsyncEx;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class AutoFocusEngine : IAutoFocusEngine {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IImagingMediator imagingMediator;
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
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
            IAutoFocusOptions autoFocusOptions) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.focuserMediator = focuserMediator;
            this.imagingMediator = imagingMediator;
            this.guiderMediator = guiderMediator;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.autoFocusOptions = autoFocusOptions;
        }

        private class AutoFocusState {

            public AutoFocusState(
                FilterInfo autoFocusFilter,
                int framesPerPoint,
                int maxConcurrency,
                AFMethodEnum afMethod,
                AFCurveFittingEnum afCurveFittingType) {
                this.AutoFocusFilter = autoFocusFilter;
                this.FramesPerPoint = framesPerPoint;
                this.ExposureSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                this.MeasurementCompleteEvent = new AsyncAutoResetEvent(false);
                AttemptNumber = 0;
                ImageNumber = 0;
                InitialFocuserPosition = -1;

                this.Fittings.Method = afMethod;
                this.Fittings.CurveFittingType = afCurveFittingType;
            }

            public int AttemptNumber { get; private set; }
            public int ImageNumber { get; private set; }
            public FilterInfo AutoFocusFilter { get; private set; }
            public object SubMeasurementsLock { get; private set; } = new object();
            public SemaphoreSlim ExposureSemaphore { get; private set; }
            public int FramesPerPoint { get; private set; }
            public int InitialFocuserPosition { get; set; }
            public MeasureAndError? InitialHFR { get; set; }
            public MeasureAndError? FinalHFR { get; set; }
            public List<Task> InitialHFRTasks { get; private set; } = new List<Task>();
            public List<Task> AnalysisTasks { get; private set; } = new List<Task>();
            public List<MeasureAndError> InitialHFRSubMeasurements { get; private set; } = new List<MeasureAndError>();
            public List<MeasureAndError> FinalHFRSubMeasurements { get; private set; } = new List<MeasureAndError>();
            public Dictionary<int, MeasureAndError> MeasurementsByFocuserPoint { get; private set; } = new Dictionary<int, MeasureAndError>();
            public Dictionary<int, List<MeasureAndError>> SubMeasurementsByFocuserPoints { get; private set; } = new Dictionary<int, List<MeasureAndError>>();
            public AsyncAutoResetEvent MeasurementCompleteEvent { get; private set; }
            public AutoFocusFitting Fittings { get; private set; } = new AutoFocusFitting();
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
                lock (SubMeasurementsLock) {
                    this.MeasurementsByFocuserPoint.Clear();
                    this.SubMeasurementsByFocuserPoints.Clear();
                    this.FinalHFRSubMeasurements.Clear();
                    this.AnalysisTasks.Clear();
                    this.FinalHFR = null;
                    this.Fittings.Reset();
                }
            }

            public void UpdateCurveFittings(List<ScatterErrorPoint> validFocusPoints) {
                var method = this.Fittings.Method;
                var fitting = this.Fittings.CurveFittingType;
                if (AFMethodEnum.STARHFR == method) {
                    if (validFocusPoints.Count() >= 3) {
                        if (AFCurveFittingEnum.TRENDHYPERBOLIC == fitting || AFCurveFittingEnum.TRENDPARABOLIC == fitting || AFCurveFittingEnum.TRENDLINES == fitting) {
                            Fittings.TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method.ToString());
                        }

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

        private DataPoint DetermineFinalFocusPoint(AutoFocusState state) {
            using (MyStopWatch.Measure()) {
                var method = state.Fittings.Method;
                var fitting = state.Fittings.CurveFittingType;

                if (method == AFMethodEnum.STARHFR) {
                    if (fitting == AFCurveFittingEnum.TRENDLINES) {
                        return state.Fittings.TrendlineFitting.Intersection;
                    }

                    if (fitting == AFCurveFittingEnum.HYPERBOLIC) {
                        return state.Fittings.HyperbolicFitting.Minimum;
                    }

                    if (fitting == AFCurveFittingEnum.PARABOLIC) {
                        return state.Fittings.QuadraticFitting.Minimum;
                    }

                    if (fitting == AFCurveFittingEnum.TRENDPARABOLIC) {
                        return new DataPoint(Math.Round((state.Fittings.TrendlineFitting.Intersection.X + state.Fittings.QuadraticFitting.Minimum.X) / 2), (state.Fittings.TrendlineFitting.Intersection.Y + state.Fittings.QuadraticFitting.Minimum.Y) / 2);
                    }

                    if (fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                        return new DataPoint(Math.Round((state.Fittings.TrendlineFitting.Intersection.X + state.Fittings.HyperbolicFitting.Minimum.X) / 2), (state.Fittings.TrendlineFitting.Intersection.Y + state.Fittings.HyperbolicFitting.Minimum.Y) / 2);
                    }

                    Logger.Error($"Invalid AutoFocus Fitting {fitting} for method {method}");
                    return new DataPoint();
                } else {
                    return state.Fittings.GaussianFitting.Maximum;
                }
            }
        }

        private async Task<MeasureAndError> EvaluateExposure(AutoFocusState state, int attemptNumber, int imageNumber, int focuserPosition, IRenderedImage image, CancellationToken token) {
            Logger.Trace($"Evaluating auto focus exposure at position {focuserPosition}");

            var imageProperties = image.RawImageData.Properties;

            // Very simple to directly provide result if we use statistics based contrast detection
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                var imageStatistics = await image.RawImageData.Statistics.Task;
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
                    NumberOfAFStars = profileService.ActiveProfile.FocuserSettings.AutoFocusUseBrightestStars,
                    IsAutoFocus = true
                };
                if (profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && !IsSubSampleEnabled()) {
                    analysisParams.UseROI = true;
                    analysisParams.InnerCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio;
                    analysisParams.OuterCropRatio = profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio;
                }
                var starDetection = starDetectionSelector.GetBehavior();
                var analysisResult = await starDetection.Detect(image, pixelFormat, analysisParams, progress: null, token);
                if (!string.IsNullOrWhiteSpace(state.SaveFolder)) {
                    var resultFileName = $"{attemptNumber:00}_{imageNumber:00}_star_detection_result.json";
                    var resultTargetPath = Path.Combine(state.SaveFolder, resultFileName);
                    File.WriteAllText(resultTargetPath, JsonConvert.SerializeObject(analysisResult, Formatting.Indented));

                    var annotatedFileName = $"{attemptNumber:00}_{imageNumber:00}_annotated.png";
                    var annotatedTargetPath = Path.Combine(state.SaveFolder, annotatedFileName);
                    var annotator = starAnnotatorSelector.GetBehavior();
                    var annotatedImage = await annotator.GetAnnotatedImage(analysisParams, analysisResult, image.Image);
                    using (var fileStream = new FileStream(annotatedTargetPath, FileMode.Create)) {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(annotatedImage));
                        encoder.Save(fileStream);
                    }
                }

                Logger.Debug($"Current Focus - Position: {focuserPosition}, HFR: {analysisResult.AverageHFR}");
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
                var analysisResult = await analysis.Measure(image, analysisParams, progress: null, token);
                return new MeasureAndError() { Measure = analysisResult.AverageContrast, Stdev = analysisResult.ContrastStdev };
            }
        }

        private async Task<IExposureData> TakeExposure(FilterInfo filter, int focuserPosition, CancellationToken token, IProgress<ApplicationStatus> progress) {
            IExposureData image;
            var retries = 0;
            do {
                Logger.Trace($"Starting exposure for autofocus at position {focuserPosition}");
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

                // TODO: Make sure OperationCancelled propagates everywhere
                try {
                    image = await imagingMediator.CaptureImage(seq, token, progress);
                } catch (Exception e) {
                    if (!IsSubSampleEnabled()) {
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

        private Task FocusPointMeasurementAction(int focuserPosition, MeasureAndError measurement, AutoFocusState state) {
            try {
                lock (state.SubMeasurementsLock) {
                    if (!state.SubMeasurementsByFocuserPoints.TryGetValue(focuserPosition, out var values)) {
                        values = new List<MeasureAndError>();
                        state.SubMeasurementsByFocuserPoints.Add(focuserPosition, values);
                    }
                    values.Add(measurement);

                    if (values.Count < state.FramesPerPoint) {
                        return Task.CompletedTask;
                    }

                    var averageMeasurement = values.AverageMeasurement();
                    state.MeasurementsByFocuserPoint.Add(focuserPosition, averageMeasurement);

                    var validFocusPoints = state.MeasurementsByFocuserPoint.Where(fp => fp.Value.Measure > 0.0).Select(fp => new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, Math.Max(0.001, fp.Value.Stdev))).ToList();
                    if (validFocusPoints.Count >= 3) {
                        var autoFocusMethod = profileService.ActiveProfile.FocuserSettings.AutoFocusMethod.ToString();
                        state.UpdateCurveFittings(validFocusPoints);
                    }

                    this.OnMeasurementPointCompleted(focuserPosition, measurement, state.Fittings);
                }
                return Task.CompletedTask;
            } finally {
                state.MeasurementCompleted();
            }
        }

        private Task InitialHFRMeasurementAction(int focuserPosition, MeasureAndError measurement, AutoFocusState state) {
            try {
                lock (state.SubMeasurementsLock) {
                    state.InitialHFRSubMeasurements.Add(measurement);
                    if (state.InitialHFRSubMeasurements.Count < state.FramesPerPoint) {
                        return Task.CompletedTask;
                    }

                    state.InitialHFR = state.InitialHFRSubMeasurements.AverageMeasurement();
                    OnInitialHFRCalculated(state.InitialHFR.Value);
                }
                return Task.CompletedTask;
            } finally {
                state.MeasurementCompleted();
            }
        }

        private Task FinalHFRMeasurementAction(int focuserPosition, MeasureAndError measurement, AutoFocusState state) {
            try {
                lock (state.SubMeasurementsLock) {
                    state.FinalHFRSubMeasurements.Add(measurement);
                    if (state.FinalHFRSubMeasurements.Count < state.FramesPerPoint) {
                        return Task.CompletedTask;
                    }

                    state.FinalHFR = state.FinalHFRSubMeasurements.AverageMeasurement();
                }
                return Task.CompletedTask;
            } finally {
                state.MeasurementCompleted();
            }
        }

        private async Task PrepareAndAnalyzeExposure(IExposureData exposureData, int focuserPosition, AutoFocusState state, Func<int, MeasureAndError, AutoFocusState, Task> action, CancellationToken token) {
            // TODO: Add whether auto stretch is required to IStarDetection. For now, just set to true
            try {
                int attemptNumber = state.AttemptNumber;
                int imageNumber = state.ImageNumber;

                var autoStretch = true;
                // If using contrast based statistics, no need to stretch
                if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.CONTRASTDETECTION && profileService.ActiveProfile.FocuserSettings.ContrastDetectionMethod == ContrastDetectionMethodEnum.Statistics) {
                    autoStretch = false;
                }

                MeasureAndError partialMeasurement;
                try {
                    var prepareParameters = new PrepareImageParameters(autoStretch: autoStretch, detectStars: false);
                    var preparedImage = await imagingMediator.PrepareImage(exposureData, prepareParameters, token);
                    partialMeasurement = await EvaluateExposure(
                        state: state,
                        attemptNumber: attemptNumber,
                        imageNumber: imageNumber,
                        focuserPosition: focuserPosition,
                        image: preparedImage,
                        token: token);
                    if (!string.IsNullOrWhiteSpace(state.SaveFolder)) {
                        var fileName = $"{attemptNumber:00}_{imageNumber:00}_Focuser{focuserPosition}_HFR{partialMeasurement.Measure:00.00}";
                        var imageData = await exposureData.ToImageData();
                        var fsi = new FileSaveInfo(profileService) {
                            FilePath = state.SaveFolder,
                            FilePattern = fileName
                        };
                        await imageData.SaveToDisk(fsi, token);
                    }
                } catch (Exception e) {
                    Logger.Error(e, $"Error while preparing and analyzing exposure at {focuserPosition}");
                    // Setting a partial measurement representing a failure to ensure the action is executed
                    partialMeasurement = new MeasureAndError() { Measure = 0.0d, Stdev = double.NaN };
                }
                await action(focuserPosition, partialMeasurement, state);
            } finally {
                state.ExposureSemaphore.Release();
            }
        }

        private async Task StartAutoFocusPoint(
            int focuserPosition,
            AutoFocusState state,
            Func<int, MeasureAndError, AutoFocusState, Task> action,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            for (int i = 0; i < state.FramesPerPoint; ++i) {
                using (MyStopWatch.Measure("Waiting on ExposureSemaphore")) {
                    await state.ExposureSemaphore.WaitAsync(token);
                }
                token.ThrowIfCancellationRequested();

                state.OnNextImage();
                var exposureData = await TakeExposure(state.AutoFocusFilter, focuserPosition, token, progress);
                state.MeasurementStarted();
                try {
                    var analysisTask = PrepareAndAnalyzeExposure(exposureData, focuserPosition, state, action, token);
                    lock (state.SubMeasurementsLock) {
                        state.AnalysisTasks.Add(analysisTask);
                    }
                } catch (Exception e) {
                    state.MeasurementCompleted();
                    Logger.Error(e, $"Failed to start focus point analysis at {focuserPosition}");
                    throw;
                }
            }
        }

        private async Task StartInitialFocusPoints(int initialFocusPosition, AutoFocusState autoFocusState, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR && autoFocusOptions.ValidateHfrImprovement) {
                await StartAutoFocusPoint(initialFocusPosition, autoFocusState, InitialHFRMeasurementAction, token, progress);
            }
        }

        private async Task StartBlindFocusPoints(int initialFocusPosition, AutoFocusState autoFocusState, CancellationToken token, IProgress<ApplicationStatus> progress) {
            // Initial set of focus point acquisition getting back to at least the starting point
            var offsetSteps = profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps;
            var stepSize = profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize;
            var targetFocuserPosition = initialFocusPosition + ((offsetSteps + 1) * stepSize);
            int leftMostPosition = int.MaxValue;
            int rightMostPosition = int.MinValue;
            for (int i = 0; i < offsetSteps; ++i) {
                targetFocuserPosition -= stepSize;
                await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                leftMostPosition = Math.Min(leftMostPosition, targetFocuserPosition);
                rightMostPosition = Math.Max(rightMostPosition, targetFocuserPosition);
                await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, token, progress);
            }

            while (true) {
                token.ThrowIfCancellationRequested();

                TrendlineFitting trendlineFit;
                Dictionary<int, MeasureAndError> focusPoints;
                lock (autoFocusState.SubMeasurementsLock) {
                    trendlineFit = autoFocusState.Fittings.TrendlineFitting;
                    focusPoints = autoFocusState.MeasurementsByFocuserPoint;
                    if (autoFocusOptions.ValidateHfrImprovement && autoFocusState.InitialHFR.HasValue && autoFocusState.InitialHFR.Value.Measure == 0.0) {
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
                        await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, token, progress);
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
                        await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, token, progress);
                        token.ThrowIfCancellationRequested();
                    }
                    break;
                }

                if (leftTrendCount < offsetSteps) {
                    targetFocuserPosition = leftMostPosition - stepSize;
                    leftMostPosition = targetFocuserPosition;
                    await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                    token.ThrowIfCancellationRequested();
                    await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, token, progress);
                    token.ThrowIfCancellationRequested();
                } else { // if (rightTrendCount < offsetSteps) {
                    targetFocuserPosition = rightMostPosition + stepSize;
                    rightMostPosition = targetFocuserPosition;
                    await focuserMediator.MoveFocuser(targetFocuserPosition, token);
                    token.ThrowIfCancellationRequested();
                    await StartAutoFocusPoint(targetFocuserPosition, autoFocusState, FocusPointMeasurementAction, token, progress);
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
            FilterInfo imagingFilter,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var autofocusFilter = await SetAutofocusFilter(imagingFilter, token, progress);
            var maxConcurrent = autoFocusOptions.MaxConcurrent > 0 ? autoFocusOptions.MaxConcurrent : int.MaxValue;
            var framesPerPoint = profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint;
            return new AutoFocusState(
                autofocusFilter,
                framesPerPoint,
                maxConcurrent,
                profileService.ActiveProfile.FocuserSettings.AutoFocusMethod,
                profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting);
        }

        private async Task<bool> RunAutoFocus(
            AutoFocusState autoFocusState,
            Func<int, AutoFocusState, CancellationToken, IProgress<ApplicationStatus>, Task> pointGenerationAction,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            bool reattempt;

            using (var stopWatch = MyStopWatch.Measure()) {
                if (autoFocusOptions.Save) {
                    if (string.IsNullOrWhiteSpace(autoFocusOptions.SavePath)) {
                        Notification.ShowWarning("No save path specified in Hocus Focus Auto Focus Options");
                        Logger.Warning("No save path specified in Hocus Focus Auto Focus Options");
                    } else if (!Directory.Exists(autoFocusOptions.SavePath)) {
                        Notification.ShowWarning($"The save path {autoFocusOptions.SavePath} does not exist");
                        Logger.Warning($"The save path {autoFocusOptions.SavePath} specified in Hocus Focus Auto Focus Options does not exist");
                    } else {
                        var folderName = $"AutoFocus_{DateTime.Now:yyyyddMM_HHmmss}";
                        var targetPath = Path.Combine(autoFocusOptions.SavePath, folderName);
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

                    var finalFocusPoint = DetermineFinalFocusPoint(autoFocusState);
                    var duration = stopWatch.Elapsed;
                    bool goodAutoFocus = await ValidateCalculatedFocusPosition(autoFocusState, finalFocusPoint, iterationCts.Token, progress);
                    token.ThrowIfCancellationRequested();

                    if (!goodAutoFocus) {
                        // Ensure we cancel any remaining tasks from this iteration so we can start the next
                        iterationTaskCts.Cancel();
                        if (autoFocusState.AttemptNumber < profileService.ActiveProfile.FocuserSettings.AutoFocusTotalNumberOfAttempts) {
                            Notification.ShowWarning(Loc.Instance["LblAutoFocusReattempting"]);
                            Logger.Warning($"Potentially bad auto-focus. Setting focuser back to {initialFocusPosition} and re-attempting.");
                            await focuserMediator.MoveFocuser(initialFocusPosition, token);

                            OnIterationFailed(autoFocusState.AttemptNumber);
                            reattempt = true;
                        }
                    } else {
                        OnCompleted(
                            state: autoFocusState,
                            finalFocuserPosition: (int)Math.Round(finalFocusPoint.X),
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

        private bool IsSubSampleEnabled() {
            var cameraInfo = cameraMediator.GetInfo();
            return profileService.ActiveProfile.FocuserSettings.AutoFocusInnerCropRatio < 1 && profileService.ActiveProfile.FocuserSettings.AutoFocusOuterCropRatio == 1 && cameraInfo.CanSubSample;
        }

        private async Task<bool> ValidateCalculatedFocusPosition(
            AutoFocusState autoFocusState,
            DataPoint focusPoint,
            CancellationToken token,
            IProgress<ApplicationStatus> progress) {
            var rSquaredThreshold = profileService.ActiveProfile.FocuserSettings.RSquaredThreshold;
            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR) {
                // Evaluate R² for Fittings to be above threshold
                var fittings = autoFocusState.Fittings;

                if (rSquaredThreshold > 0) {
                    var hyperbolicBad = fittings.HyperbolicFitting.RSquared < rSquaredThreshold;
                    var quadraticBad = fittings.QuadraticFitting.RSquared < rSquaredThreshold;
                    var trendlineBad = fittings.TrendlineFitting.LeftTrend.RSquared < rSquaredThreshold || fittings.TrendlineFitting.RightTrend.RSquared < rSquaredThreshold;

                    var fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;

                    if ((fitting == AFCurveFittingEnum.HYPERBOLIC || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) && hyperbolicBad) {
                        Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Hyperbolic Fitting is below threshold. {Math.Round(fittings.HyperbolicFitting.RSquared, 2)} / {rSquaredThreshold}");
                        Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.HyperbolicFitting.RSquared, 2), rSquaredThreshold));
                        return false;
                    }

                    if ((fitting == AFCurveFittingEnum.PARABOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && quadraticBad) {
                        Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Parabolic Fitting is below threshold. {Math.Round(fittings.QuadraticFitting.RSquared, 2)} / {rSquaredThreshold}");
                        Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.QuadraticFitting.RSquared, 2), rSquaredThreshold));
                        return false;
                    }

                    if ((fitting == AFCurveFittingEnum.TRENDLINES || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && trendlineBad) {
                        Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Trendline Fitting is below threshold. Left: {Math.Round(fittings.TrendlineFitting.LeftTrend.RSquared, 2)} / {rSquaredThreshold}; Right: {Math.Round(fittings.TrendlineFitting.RightTrend.RSquared, 2)} / {rSquaredThreshold}");
                        Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.TrendlineFitting.LeftTrend.RSquared, 2), Math.Round(fittings.TrendlineFitting.RightTrend.RSquared, 2), rSquaredThreshold));
                        return false;
                    }
                }
            }

            var min = autoFocusState.MeasurementsByFocuserPoint.Min(x => x.Key);
            var max = autoFocusState.MeasurementsByFocuserPoint.Max(x => x.Key);

            if (focusPoint.X < min || focusPoint.X > max) {
                Logger.Error($"Determined focus point position is outside of the overall measurement points of the curve. Fitting is incorrect and autofocus settings are incorrect. FocusPosition {focusPoint.X}; Min: {min}; Max:{max}");
                Notification.ShowError(Loc.Instance["LblAutoFocusPointOutsideOfBounds"]);
                return false;
            }

            var finalFocusPosition = (int)Math.Round(focusPoint.X);
            await focuserMediator.MoveFocuser(finalFocusPosition, token);
            token.ThrowIfCancellationRequested();

            if (autoFocusOptions.ValidateHfrImprovement) {
                Logger.Info($"Validating HFR at final focus position {finalFocusPosition}");
                await StartAutoFocusPoint(finalFocusPosition, autoFocusState, FinalHFRMeasurementAction, token, progress);
                token.ThrowIfCancellationRequested();
            }

            await Task.WhenAll(autoFocusState.AnalysisTasks);
            token.ThrowIfCancellationRequested();

            if (profileService.ActiveProfile.FocuserSettings.AutoFocusMethod == AFMethodEnum.STARHFR && autoFocusOptions.ValidateHfrImprovement) {
                lock (autoFocusState.SubMeasurementsLock) {
                    if (!autoFocusState.FinalHFR.HasValue || autoFocusState.FinalHFR.Value.Measure == 0.0) {
                        Logger.Warning("Failed assessing HFR at the final focus point");
                        Notification.ShowWarning("Failed assessing HFR at the final focus point");
                        return false;
                    }
                    if (!autoFocusState.InitialHFR.HasValue || autoFocusState.InitialHFR.Value.Measure == 0.0) {
                        Logger.Warning("Failed assessing HFR at the initial position");
                        Notification.ShowWarning("Failed assessing HFR at the initial position");
                        return false;
                    }

                    var finalHfr = autoFocusState.FinalHFR?.Measure;
                    var initialHFR = autoFocusState.InitialHFR?.Measure;
                    if (finalHfr > (initialHFR * (1.0 + autoFocusOptions.HFRImprovementThreshold))) {
                        Logger.Warning($"New focus point HFR {finalHfr} is significantly worse than original HFR {initialHFR}");
                        Notification.ShowWarning(string.Format(Loc.Instance["LblAutoFocusNewWorseThanOriginal"], finalHfr, initialHFR));
                        return false;
                    }
                }
            }
            return true;
        }

        private async Task<FilterInfo> SetAutofocusFilter(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
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

        public async Task<AutoFocusResult> Run(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (AutoFocusInProgress) {
                Notification.ShowError("Another AutoFocus is already in progress");
                return null;
            }

            Logger.Trace("Starting Autofocus");
            OnStarted();

            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(autoFocusOptions.AutoFocusTimeoutSeconds));
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
                autoFocusState = await InitializeState(imagingFilter, autofocusCts.Token, progress);
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
                    Notification.ShowWarning($"AutoFocus timed out after {autoFocusOptions.AutoFocusTimeoutSeconds} seconds");
                    Logger.Warning($"AutoFocus timed out after {autoFocusOptions.AutoFocusTimeoutSeconds} seconds");
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
                Succeeded = completed
            };
        }

        private void OnInitialHFRCalculated(MeasureAndError initialHFRMeasurement) {
            InitialHFRCalculated?.Invoke(this, new AutoFocusInitialHFRCalculatedEventArgs() {
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

        private void OnMeasurementPointCompleted(int focuserPosition, MeasureAndError measurement, AutoFocusFitting fittings) {
            MeasurementPointCompleted?.Invoke(this, new AutoFocusMeasurementPointCompletedEventArgs() {
                FocuserPosition = focuserPosition,
                Measurement = measurement,
                Fittings = fittings.Clone()
            });
        }

        private void OnCompleted(
            AutoFocusState state,
            int finalFocuserPosition,
            double temperature,
            TimeSpan duration) {
            var initialFocuserPosition = state.InitialFocuserPosition;
            var filter = state.AutoFocusFilter?.Name ?? string.Empty;
            var iteration = state.AttemptNumber;
            var initialHFR = state.InitialHFR.Value.Measure;
            var finalHFR = state.FinalHFR.Value.Measure;
            Completed?.Invoke(this, new AutoFocusCompletedEventArgs() {
                Iteration = iteration,
                InitialFocusPosition = initialFocuserPosition,
                FinalFocuserPosition = finalFocuserPosition,
                InitialHFR = initialHFR,
                FinalHFR = finalHFR,
                Filter = filter,
                Temperature = temperature,
                Duration = duration,
                SaveFolder = state.SaveFolder
            });
        }

        public event EventHandler<AutoFocusInitialHFRCalculatedEventArgs> InitialHFRCalculated;

        public event EventHandler<AutoFocusIterationFailedEventArgs> IterationFailed;

        public event EventHandler<AutoFocusIterationStartedEventArgs> IterationStarted;

        public event EventHandler<AutoFocusStartedEventArgs> Started;

        public event EventHandler<AutoFocusMeasurementPointCompletedEventArgs> MeasurementPointCompleted;

        public event EventHandler<AutoFocusCompletedEventArgs> Completed;
    }
}