#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

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
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
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
            this.autoFocusOptions = autoFocusOptions;
            this.alglibAPI = alglibAPI;
        }

        private class CurveFittingResult {

            private CurveFittingResult() {
            }

            public AutoFocusFitting Fittings { get; private set; }

            public ImmutableList<ScatterErrorPoint> RejectedPoints { get; private set; }

            public static CurveFittingResult Calculate(
                AutoFocusState state,
                AFMethodEnum method,
                AFCurveFittingEnum fitting,
                List<ScatterErrorPoint> focusPoints) {
                // Canonicalize point order (by focuser position) so the fit and its iterative Grubbs outlier
                // rejection are independent of the order measurements arrive in. During replay the points
                // complete concurrently, so MeasurementsByFocuserPoint (a Dictionary) enumerates them in
                // nondeterministic completion order; the alglib fit (residual/Jacobian summation roundoff) and
                // RejectionTest (first-of-ties MaxBy) are order-sensitive, which would otherwise flip a borderline
                // outlier between otherwise-identical replays.
                focusPoints = focusPoints.OrderBy(p => p.X).ToList();
                // Weighted fitters — ours and NINA core's Trendline/QuadraticFitting, which weight by
                // 1/ErrorY² — must never see a degenerate σ: fit on regularized copies. Raw points
                // still feed reports/charts upstream; rejected points recorded from this path carry
                // the regularized σ.
                var validFocusPoints = WeightRegularization.Regularize(focusPoints.Where(p => p.Y > 0.0).ToList());
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
                                // NINA core's QuadraticFitting always weights by 1/ErrorY² (it has no unweighted
                                // mode), so its Grubbs test must be weighted too — unconditionally, unlike the
                                // hyperbolic site below where WeightedHyperbolicFitEnabled gates it. BuildResidualWeights
                                // yields the matching standardized-residual weight (1/ErrorY): a 1/ErrorY²-weighted fit
                                // makes (Y−f)/ErrorY the natural residual, which is what RejectionTest then ranks
                                // (analysis F12).
                                rejectedPoint = MathUtility.RejectionTest(points: validFocusPoints, fitting: fittings.QuadraticFitting.Fitting, confidence: rejectionConfidence, weights: AlglibHyperbolicFitting.BuildResidualWeights(validFocusPoints, useWeights: true));
                            }

                            if (AFCurveFittingEnum.HYPERBOLIC == fitting || AFCurveFittingEnum.TRENDHYPERBOLIC == fitting) {
                                var hf = AlglibHyperbolicFitting.Create(state.AlglibAPI, state.Options.HyperbolicFitModel, validFocusPoints, state.Options.AutoFocusStepSize, state.Options.WeightedHyperbolicFitEnabled);
                                if (!hf.Solve()) {
                                    Logger.Error("Hyperbolic fit failed");
                                } else {
                                    fittings.HyperbolicFitting = hf;
                                    rejectedPoint = MathUtility.RejectionTest(points: validFocusPoints, fitting: fittings.HyperbolicFitting.Fitting, confidence: rejectionConfidence, weights: AlglibHyperbolicFitting.BuildResidualWeights(validFocusPoints, state.Options.WeightedHyperbolicFitEnabled));
                                }
                            }
                        }
                    } else if (validFocusPoints.Count >= 3) {
                        fittings.TrendlineFitting = new TrendlineFitting().Calculate(validFocusPoints, method.ToString());
                        fittings.GaussianFitting = new GaussianFitting().Calculate(validFocusPoints);
                        rejectedPoint = MathUtility.RejectionTest(points: validFocusPoints, fitting: fittings.GaussianFitting.Fitting, confidence: rejectionConfidence);
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
                    this.selectedHyperbolicModel = null;
                }
            }

            public void ResetInitialHFRMeasurements() {
                lock (SubMeasurementsLock) {
                    this.InitialHFRSubMeasurements.Clear();
                    this.InitialHFR = null;
                }
            }

            private List<ScatterErrorPoint> lastValidFocusPoints;

            // The concrete hyperbolic model resolved at finalization when the option is Hybrid (null otherwise),
            // so the LOO stability below scores the same curve that was chosen rather than re-running selection.
            private HyperbolicFitModel? selectedHyperbolicModel;

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
                this.FinalFocusPoint = DetermineFinalFocusPoint();
            }

            /// <summary>
            /// Finalizes which concrete hyperbolic model is recorded for the run, so the panel and saved report
            /// always show it (not only for Hybrid). For a non-Hybrid run the model is fixed by the option, so it
            /// is recorded as-is. When the option is <see cref="HyperbolicFitModel.Hybrid"/>, this refits every
            /// concrete hyperbolic model on the final points and swaps the region's
            /// <see cref="AutoFocusFitting.HyperbolicFitting"/> to the one with the least expected best-focus error
            /// (see <see cref="AlglibHyperbolicFitting.SelectBestModel"/>), recording the concrete pick. No-op for
            /// non-STARHFR / non-hyperbolic fittings. The heavy multi-model solve runs outside the lock; only the
            /// field swap is taken under <see cref="SubMeasurementsLock"/> (mirrors <see cref="CalculateCurveFittings"/>).
            /// </summary>
            public void SelectBestHyperbolicModel() {
                if (Fittings.Method != AFMethodEnum.STARHFR) {
                    return;
                }
                if (Fittings.CurveFittingType != AFCurveFittingEnum.HYPERBOLIC && Fittings.CurveFittingType != AFCurveFittingEnum.TRENDHYPERBOLIC) {
                    return;
                }

                // Non-Hybrid: the model is fixed by the option — record it so the panel/report always show which
                // hyperbolic model produced the fit. No multi-model selection to run.
                if (State.Options.HyperbolicFitModel != HyperbolicFitModel.Hybrid) {
                    lock (SubMeasurementsLock) {
                        this.Fittings.SelectedHyperbolicFitModel = State.Options.HyperbolicFitModel;
                    }
                    return;
                }

                if (lastValidFocusPoints == null) {
                    return;
                }

                var validPoints = WeightRegularization.Regularize(lastValidFocusPoints.Where(p => p.Y > 0.0).ToList());
                if (validPoints.Count < 3) {
                    return;
                }

                // Each candidate rejects its own outliers (Grubbs test on that model's residuals) before competing,
                // since outlier-ness is model-specific. The winner's rejected set replaces the live (Tilted) one.
                var best = AlglibHyperbolicFitting.SelectBestModel(
                    State.AlglibAPI, validPoints, State.Options.AutoFocusStepSize, State.Options.WeightedHyperbolicFitEnabled,
                    State.Options.MaxOutlierRejections, State.Options.OutlierRejectionConfidence,
                    out var bestFit, out var bestRejectedPoints);
                if (bestFit == null) {
                    return;
                }

                lock (SubMeasurementsLock) {
                    this.Fittings.HyperbolicFitting = bestFit;
                    this.Fittings.SelectedHyperbolicFitModel = best;
                    this.selectedHyperbolicModel = best;

                    // Surface the chosen model's outliers (not the live Tilted model's) so the panel/report match
                    // the fit that actually determined focus.
                    this.RejectedPoints.Clear();
                    foreach (var rp in bestRejectedPoints) {
                        var focuserPosition = (int)Math.Round(rp.X);
                        this.RejectedPoints[focuserPosition] = new MeasureAndError() { Measure = rp.Y, Stdev = rp.ErrorY };
                    }
                }
                Logger.Info($"Hybrid auto-focus model selection chose {best} for region {RegionIndex} (rejected {bestRejectedPoints.Count} outlier(s))");
            }

            /// <summary>
            /// Computes the leave-one-out best-focus stability for the final hyperbolic fit and stores it on that
            /// fit object (for the panel and saved report). A run-completion diagnostic only — never a rejection
            /// gate — so it is intentionally not part of the live per-point fitting path. No-op unless this is a
            /// STARHFR hyperbolic run with an alglib-backed fit and enough points.
            /// </summary>
            public void ComputeLeaveOneOutStability() {
                if (Fittings.Method != AFMethodEnum.STARHFR) {
                    return;
                }
                if (Fittings.CurveFittingType != AFCurveFittingEnum.HYPERBOLIC && Fittings.CurveFittingType != AFCurveFittingEnum.TRENDHYPERBOLIC) {
                    return;
                }
                if (!(Fittings.HyperbolicFitting is AlglibHyperbolicFitting hyperbolicFitting) || lastValidFocusPoints == null) {
                    return;
                }

                // Use the model actually chosen for this run (Hybrid resolves to a concrete model in
                // SelectBestHyperbolicModel); for non-Hybrid runs this is the option model, preserving prior behavior.
                var modelForLoo = selectedHyperbolicModel ?? State.Options.HyperbolicFitModel;
                var validPoints = WeightRegularization.Regularize(lastValidFocusPoints.Where(p => p.Y > 0.0).ToList());
                hyperbolicFitting.LeaveOneOutStdError = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                    State.AlglibAPI, modelForLoo, validPoints, State.Options.AutoFocusStepSize, State.Options.WeightedHyperbolicFitEnabled);
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

            // Per-region star detection results. The 7 region-analysis tasks of a single frame run concurrently and
            // all share this one AutoFocusImageState, so a single shared StarDetectionResult field was written by one
            // region and read back by another (HFR misattribution). Keyed by region index so each region reads its own
            // result. Only feeds the SubMeasurementPointCompleted event (the Inspector's sensor model); the AF curve
            // consumes the pooled MeasureAndError via SubMeasurementsByFocuserPoints instead, so this is independent.
            private readonly PerRegionStarDetectionResults starDetectionResults = new PerRegionStarDetectionResults();

            public void SetStarDetectionResult(int regionIndex, StarDetectionResult result) {
                starDetectionResults.Set(regionIndex, result);
            }

            public StarDetectionResult GetStarDetectionResult(int regionIndex) {
                return starDetectionResults.Get(regionIndex);
            }

            public IRenderedImage PreservedExposure { get; set; }

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
            CancellationToken token,
            SavedDetectionCacheSource cacheSource = null) {
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
                    // For a Review-Frames run, model PSFs (auto-focus normally skips them) so the review shows the
                    // PSF-derived per-star properties — detection is otherwise identical, so the HFR curve is unaffected.
                    if (state.Options.ModelPSF && starDetection is IHocusFocusStarDetection hfReviewDetection) {
                        analysisResult = await hfReviewDetection.Detect(image, pixelFormat, analysisParams, null, token, modelPSFForAutoFocus: true);
                    } else {
                        analysisResult = await starDetection.Detect(image, pixelFormat, analysisParams, progress: null, token);
                    }
                } else {
                    var hfStarDetection = (IHocusFocusStarDetection)starDetection;
                    var hfParams = hfStarDetection.ToHocusFocusParams(analysisParams);
                    // When replaying with capture-time settings (option b), state.Options.StarDetectionOptionsOverride
                    // carries a detached snapshot; params are built from it instead of the live detector options. The
                    // overload delegates to the no-override path when it is null (live capture / "use current settings").
                    var starDetectorParams = hfStarDetection.GetStarDetectorParams(image, regionState.Region, true, state.Options.StarDetectionOptionsOverride);

                    // Replay reuse cache (Task 8, default OFF): when replaying a saved run and the option is on, reuse
                    // the saved per-region detection JSON in place of re-running the (expensive) Detect — but ONLY when
                    // TryLoadValidCachedDetection confirms the saved result's detector version + params (region
                    // included) still match. cacheSource is null on the live path, so the && short-circuits before any
                    // disk access and detection runs exactly as before. Any miss/mismatch/error ⇒ cached is null ⇒ we
                    // fall through to Detect. The cached result is a HocusFocusStarDetectionResult whose StarList is
                    // HocusFocusDetectedStar, so the downstream SubMeasurementPointCompleted event feeds the sensor
                    // model identically to a fresh detection.
                    HocusFocusStarDetectionResult hfAnalysisResult;
                    if (state.Options.ReuseSavedDetection
                        && cacheSource != null
                        && TryLoadValidCachedDetection(cacheSource.SourceFolder, cacheSource.ImageNumber, cacheSource.FrameNumber, regionState.RegionIndex, starDetectorParams, out var cachedResult)) {
                        Logger.Debug($"Reusing saved star detection result for image {cacheSource.ImageNumber}, frame {cacheSource.FrameNumber}, region {regionState.RegionIndex} (focuser {imageState.FocuserPosition})");
                        hfAnalysisResult = cachedResult;
                    } else {
                        hfAnalysisResult = (HocusFocusStarDetectionResult)await hfStarDetection.Detect(image, hfParams, starDetectorParams, null, token);
                    }
                    hfAnalysisResult.FocuserPosition = imageState.FocuserPosition;
                    analysisResult = hfAnalysisResult;
                }

                if (!state.Options.SaveExposuresOnly && !string.IsNullOrWhiteSpace(state.SaveFolder)) {
                    var saveAttemptFolder = GetSaveAttemptFolder(state, imageState.AttemptNumber, imageState.FinalValidation);
                    var resultFileName = BuildStarDetectionResultFileName(imageState.ImageNumber, imageState.FrameNumber, regionState.RegionIndex);
                    var resultTargetPath = Path.Combine(saveAttemptFolder, resultFileName);
                    // Use the dedicated cache serializer (not default Json.NET settings) so the polymorphic
                    // StarList entries (HocusFocusDetectedStar, incl. PSF) and the DetectorVersion/CacheKey
                    // survive a future reload — see StarDetectionResultCacheSerializer. The format change is
                    // safe today because nothing reads this file back yet.
                    File.WriteAllText(resultTargetPath, StarDetectionResultCacheSerializer.Serialize(analysisResult));

                    // Per-region annotated TIFFs are no longer written: the "Review Frames" feature re-renders the
                    // annotator overlays live (from raw frames + capture-time settings on replay), so a baked-in
                    // annotated image is redundant — and rendering/encoding it per frame was a needless cost.
                }

                imageState.SetStarDetectionResult(regionState.RegionIndex, analysisResult);
                if (state.Options.PreserveExposures) {
                    imageState.PreservedExposure = image;
                }

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

                // A focuser position can be revisited - most commonly when reprocessing a saved run whose frames
                // map more than one measurement point to the same focuser position. Complete each position only
                // once; the second completion previously threw "An item with the same key has already been added".
                if (!TryCompleteFocuserPoint(regionState.MeasurementsByFocuserPoint, focuserPosition, values, out var pooledMeasurement)) {
                    Logger.Trace($"Ignoring duplicate completion at focuser position {focuserPosition}");
                    return Task.CompletedTask;
                }

                var focusPoints = regionState.MeasurementsByFocuserPoint.Select(fp => new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, SafeDisplayError(fp.Value.Stdev))).ToList();
                regionState.UpdateCurveFittings(focusPoints);

                this.OnMeasurementPointCompleted(imageState, regionState, pooledMeasurement);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Records the averaged sub-measurements for a focuser position, completing that point exactly once.
        /// Returns false (leaving the map unchanged) when the position was already completed, which happens when
        /// a saved run being reprocessed maps more than one measurement point to the same focuser position.
        /// <paramref name="pooledMeasurement"/> is set to the pooled (averaged) value stored in the map on new
        /// completion, or the previously stored value on a duplicate — on new completion, callers should forward
        /// this to the MeasurementPointCompleted event so that charts, the NINA broadcast point, and saved-report
        /// MeasurePoints agree with the fit inputs when FramesPerPoint > 1.
        /// </summary>
        internal static bool TryCompleteFocuserPoint(Dictionary<int, MeasureAndError> measurementsByFocuserPoint, int focuserPosition, List<MeasureAndError> subMeasurements, out MeasureAndError pooledMeasurement) {
            if (measurementsByFocuserPoint.TryGetValue(focuserPosition, out pooledMeasurement)) {
                return false;
            }
            pooledMeasurement = subMeasurements.AverageMeasurement();
            measurementsByFocuserPoint.Add(focuserPosition, pooledMeasurement);
            return true;
        }

        /// <summary>
        /// σ for the display/report layer: keep the measured value; non-finite σ (NaN = no valid per-frame σ; ±Infinity) and
        /// negatives render as 0 = "no error bar". The old code fabricated a 0.001 floor here, which
        /// downstream 1/σ weighting turned into a 1000× weight (F5a). Fitters never consume this raw
        /// value directly — every weighted fit receives WeightRegularization copies, which map 0 or
        /// unknown σ to the sweep's median σ.
        /// </summary>
        internal static double SafeDisplayError(double stdev) {
            return double.IsFinite(stdev) ? Math.Max(0.0, stdev) : 0.0;
        }

        /// <summary>
        /// Identifies the on-disk source of a saved per-region detection result for the replay reuse cache. Only
        /// the REPLAY path supplies one (built from the <see cref="SavedAutoFocusImage"/> being replayed); the
        /// live AF path passes <c>null</c>, which (together with the default-off <c>ReuseSavedDetection</c> flag)
        /// keeps detection running exactly as before.
        ///
        /// <para><see cref="ImageNumber"/>/<see cref="FrameNumber"/> are the ORIGINAL numbers parsed from the
        /// saved filename — they name the saved <c>_star_detection_result.json</c>. The replay loop reassigns a
        /// fresh <c>imageState.ImageNumber</c> for ordering, which must NOT be used to look up the cache file.</para>
        /// </summary>
        internal sealed record SavedDetectionCacheSource(string SourceFolder, int ImageNumber, int FrameNumber);

        /// <summary>
        /// Returns the canonical filename for a per-region star-detection result JSON, derived from the original
        /// image/frame/region numbers. This is the single source of truth for the filename format used by both the
        /// save path and the cache-reuse path (<see cref="TryLoadValidCachedDetection"/>).
        /// </summary>
        internal static string BuildStarDetectionResultFileName(int imageNumber, int frameNumber, int regionIndex)
            => $"{imageNumber:00}_Frame{frameNumber:00}_Region{regionIndex:00}_star_detection_result.json";

        /// <summary>
        /// Reuse-side gate for the replay detection-result cache. Tries to load the saved per-region
        /// <c>_star_detection_result.json</c> for the given (original) image/frame/region and returns it ONLY when
        /// it is provably interchangeable with a fresh detection for <paramref name="currentParams"/>: the saved
        /// <see cref="HocusFocusStarDetectionResult.DetectorVersion"/> equals the current
        /// <see cref="StarDetector.StarDetectorVersion"/> AND the saved
        /// <see cref="HocusFocusStarDetectionResult.CacheKey"/> equals
        /// <see cref="StarDetector.ComputeCacheKey(StarDetectorParams)"/> for the current params (region included).
        ///
        /// <para>Every other outcome — file missing, unreadable/corrupt JSON, any deserialize exception, version
        /// mismatch, or key mismatch — returns <c>false</c> with <paramref name="cached"/> = <c>null</c>, so the
        /// caller falls back to a full detection. The helper never throws: its only failure mode is a (safe)
        /// cache miss, never a stale or incorrect reuse. The filename is built from the ORIGINAL
        /// <paramref name="imageNumber"/>/<paramref name="frameNumber"/> (the saved-file numbers), not any
        /// replay-reassigned counter.</para>
        /// </summary>
        internal static bool TryLoadValidCachedDetection(
            string sourceFolder,
            int imageNumber,
            int frameNumber,
            int regionIndex,
            StarDetectorParams currentParams,
            out HocusFocusStarDetectionResult cached) {
            cached = null;
            if (string.IsNullOrEmpty(sourceFolder) || currentParams == null) {
                return false;
            }

            var fileName = BuildStarDetectionResultFileName(imageNumber, frameNumber, regionIndex);
            var path = Path.Combine(sourceFolder, fileName);
            if (!File.Exists(path)) {
                Logger.Debug($"Saved detection cache miss (file not found): {path}");
                return false;
            }

            HocusFocusStarDetectionResult deserialized;
            try {
                var json = File.ReadAllText(path);
                deserialized = StarDetectionResultCacheSerializer.Deserialize(json);
            } catch (Exception e) {
                // Unreadable / corrupt / format-incompatible cache file. Treat as a miss and re-detect.
                Logger.Debug($"Saved detection cache miss (failed to read/deserialize {path}): {e.Message}");
                return false;
            }

            if (deserialized == null) {
                Logger.Debug($"Saved detection cache miss (deserialized to null): {path}");
                return false;
            }

            if (deserialized.DetectorVersion != StarDetector.StarDetectorVersion) {
                Logger.Debug($"Saved detection cache miss (detector version {deserialized.DetectorVersion} != current {StarDetector.StarDetectorVersion}): {path}");
                return false;
            }

            var expectedKey = StarDetector.ComputeCacheKey(currentParams);
            if (!string.Equals(deserialized.CacheKey, expectedKey, StringComparison.Ordinal)) {
                Logger.Debug($"Saved detection cache miss (cache key saved={deserialized.CacheKey} != current={expectedKey}): {path}");
                return false;
            }

            cached = deserialized;
            return true;
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
            IProgress<ApplicationStatus> progress,
            bool forRerun = false) {
            var autofocusFilter = forRerun ? imagingFilter : await SetAutofocusFilter(imagingFilter, token, progress);
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
                InitializeSave(autoFocusState);

                // Make sure this is set after changing the filter, in case offsets are used
                int initialFocusPosition = focuserMediator.GetInfo().Position;
                autoFocusState.InitialFocuserPosition = initialFocusPosition;
                Logger.Info($"Starting AutoFocus with initial position {initialFocusPosition}");

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

        /// <summary>
        /// Decides whether a hyperbolic fit passes the configured rejection gate. With
        /// <see cref="FitRejectionCriterion.RSquared"/> (default) this reproduces the legacy rule — reject when
        /// the R² threshold is positive and the fit's R² falls below it. With
        /// <see cref="FitRejectionCriterion.ReducedChiSquared"/> it rejects only when the threshold is positive
        /// and the reduced χ² is finite and above it (a non-finite reduced χ² never rejects). A threshold of zero
        /// or less disables the respective gate. Returns true when the fit is acceptable.
        /// </summary>
        public static bool IsHyperbolicFitAcceptable(FitRejectionCriterion criterion, double rSquared, double reducedChiSquared, double rSquaredThreshold, double reducedChiSquaredThreshold) {
            switch (criterion) {
                case FitRejectionCriterion.ReducedChiSquared:
                    var chiSquaredBad = reducedChiSquaredThreshold > 0
                        && !double.IsNaN(reducedChiSquared) && !double.IsInfinity(reducedChiSquared)
                        && reducedChiSquared > reducedChiSquaredThreshold;
                    return !chiSquaredBad;

                case FitRejectionCriterion.RSquared:
                default:
                    var rSquaredBad = rSquaredThreshold > 0 && rSquared < rSquaredThreshold;
                    return !rSquaredBad;
            }
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
                    if (fittings == null) {
                        throw new Exception($"Failed to fit curve to region {autoFocusRegionState.RegionIndex}");
                    }

                    var fitting = profileService.ActiveProfile.FocuserSettings.AutoFocusCurveFitting;

                    // Hyperbolic uses the configurable rejection criterion (R² or reduced χ²). It is evaluated
                    // independently of the R² threshold so a reduced-χ² gate still applies when R² gating is off.
                    // Default criterion is R² ⇒ identical behavior to the legacy R²-only test.
                    if (fitting == AFCurveFittingEnum.HYPERBOLIC || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                        var hyperbolicFitting = fittings.HyperbolicFitting;
                        if (hyperbolicFitting != null) {
                            var criterion = autoFocusState.Options.FitRejectionCriterion;
                            var reducedChiSquaredThreshold = autoFocusState.Options.ReducedChiSquaredRejectionThreshold;
                            var reducedChiSquared = (hyperbolicFitting as AlglibHyperbolicFitting)?.ReducedChiSquared ?? double.NaN;
                            if (!IsHyperbolicFitAcceptable(criterion, hyperbolicFitting.RSquared, reducedChiSquared, rSquaredThreshold, reducedChiSquaredThreshold)) {
                                if (criterion == FitRejectionCriterion.ReducedChiSquared) {
                                    Logger.Error($"Auto Focus Failed! Reduced χ² for Hyperbolic Fitting is above threshold. {Math.Round(reducedChiSquared, 2)} / {reducedChiSquaredThreshold}; Region: {autoFocusRegionState.Region}");
                                    Notification.ShowError(string.Format("Auto focus failed. Hyperbolic fit reduced χ² {0} exceeds the threshold {1}.", Math.Round(reducedChiSquared, 2), reducedChiSquaredThreshold));
                                } else {
                                    Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Hyperbolic Fitting is below threshold. {Math.Round(hyperbolicFitting.RSquared, 2)} / {rSquaredThreshold}; Region: {autoFocusRegionState.Region}");
                                    Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(hyperbolicFitting.RSquared, 2), rSquaredThreshold));
                                }
                                return false;
                            }
                        }
                    }

                    if (rSquaredThreshold > 0) {
                        var quadraticBad = fittings.QuadraticFitting != null && fittings.QuadraticFitting.RSquared < rSquaredThreshold;
                        var trendlineBad = (fittings.TrendlineFitting?.LeftTrend != null && fittings.TrendlineFitting.LeftTrend.RSquared < rSquaredThreshold) ||
                            (fittings.TrendlineFitting?.RightTrend != null && fittings.TrendlineFitting.RightTrend.RSquared < rSquaredThreshold);

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

                    autoFocusRegionState.SelectBestHyperbolicModel();
                    autoFocusRegionState.CalculateFinalFocusPoint();
                    autoFocusRegionState.ComputeLeaveOneOutStability();
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
                    // Clear the (static) in-progress guard FIRST and unconditionally. If anything below this throws,
                    // the flag stays set and EVERY future AutoFocus across the whole app is rejected with "Another
                    // AutoFocus is already in progress" until NINA is restarted. progress is optional (the Star
                    // Detection Optimizer's live attempt passes null), so report through it defensively.
                    AutoFocusInProgress = false;
                    progress?.Report(new ApplicationStatus() { Status = string.Empty });
                }
            }

            if (autoFocusState == null) {
                // InitializeState never produced state (cancelled, timed out, or an equipment error already logged
                // above). There is nothing to build a result from; return null like the in-progress guard does,
                // rather than dereferencing a null state below.
                return null;
            }

            return new AutoFocusResult() {
                Succeeded = completed,
                InitialFocuserPosition = autoFocusState.InitialFocuserPosition,
                ImageSize = autoFocusState.ImageSize,
                StepSize = autoFocusState.Options.AutoFocusStepSize,
                RegionResults = autoFocusState.FocusRegionStates.Select(rs => new AutoFocusRegionResult() {
                    RegionIndex = rs.RegionIndex,
                    Region = rs.Region,
                    EstimatedFinalFocuserPosition = rs.FinalFocusPoint?.X ?? double.NaN,
                    EstimatedFinalHFR = rs.FinalFocusPoint?.Y ?? double.NaN,
                    Fittings = rs.Fittings,
                    RejectedPoints = rs.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
                }).OrderBy(r => r.RegionIndex).ToArray(),
                SaveFolder = autoFocusState.SaveFolder
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
            SemaphoreSlim loadSerializer,
            CancellationToken token) {
            var isBayered = savedFile.IsBayered;
            var bitDepth = savedFile.BitDepth;

            // Serialize the decode + prepare so only ONE saved frame is being decoded/prepared at a time. The
            // NINA-core image pipeline (IImageDataFactory.CreateFromFile + IImagingMediator.PrepareImage) is not
            // safe for concurrent invocation; the live AF path only ever decodes one frame at a time, but replay's
            // bounded prefetch starts several loads at once. Two concurrent loads could otherwise corrupt/share pixel
            // data, so two distinct focuser positions occasionally got an identical HFR. Detection (which reads the
            // already-materialized RawImageData) stays fully parallel, and the prefetch still overlaps this
            // serialized load with the parallel detection of an already-loaded frame.
            await loadSerializer.WaitAsync(token);
            try {
                var sw = new Stopwatch();
                sw.Start();
                var imageData = await this.imageDataFactory.CreateFromFile(savedFile.Path, bitDepth, isBayered, profileService.ActiveProfile.CameraSettings.RawConverter, token);
                sw.Stop();
                Logger.Info($"Load file took {sw.Elapsed}");
                return await PrepareExposure(state, imageData, token);
            } finally {
                loadSerializer.Release();
            }
        }

        private async Task<MeasureAndError> AnalyzeSavedFile(
            AutoFocusState state,
            AutoFocusRegionState regionState,
            AutoFocusImageState imageState,
            IRenderedImage renderedImage,
            CancellationToken token,
            SavedDetectionCacheSource cacheSource = null) {
            try {
                state.MeasurementStarted();
                return await EvaluateExposure(
                    state: state,
                    regionState: regionState,
                    imageState: imageState,
                    image: renderedImage,
                    token: token,
                    cacheSource: cacheSource);
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

        /// <summary>
        /// Copies a reloaded run's original raw frame into the current save folder's attempt directory (preserving the
        /// original metadata-encoded filename) so a saving reprocess yields a complete, replayable run — the reload
        /// path itself never re-saves raw frames. No-op when the run is not saving. Best-effort: a copy failure is
        /// logged and never fails the run (the reprocess result is still valid; that run just won't be replayable).
        /// </summary>
        private void MaybeCopyReplayFrame(AutoFocusState state, SavedAutoFocusImage savedFile, int attemptNumber, bool finalValidation) {
            if (string.IsNullOrWhiteSpace(state.SaveFolder)) {
                return;
            }
            try {
                if (string.IsNullOrEmpty(savedFile.Path) || !File.Exists(savedFile.Path)) {
                    return;
                }
                var destFolder = GetSaveAttemptFolder(state, attemptNumber, finalValidation);
                var dest = Path.Combine(destFolder, Path.GetFileName(savedFile.Path));
                // Defensive: never copy a file onto itself (would not happen — the save folder is a fresh AutoFocus_<ts>).
                if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(savedFile.Path), StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
                File.Copy(savedFile.Path, dest, overwrite: true);
            } catch (Exception e) {
                Logger.Warning($"Failed to copy raw frame for replay ({savedFile.Path}): {e.Message}");
            }
        }

        private void InitializeSave(AutoFocusState autoFocusState) {
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
        }

        private async Task<AutoFocusResult> RerunImpl(AutoFocusEngineOptions options, SavedAutoFocusAttempt savedAttempt, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress) {
            OnStarted();

            var state = await InitializeState(options, imagingFilter, regions, token, progress, true);
            InitializeSave(state);

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

            // Bounded prefetch: allow several saved-file loads (disk read + decode + PrepareExposure) to be in
            // flight at once so loads overlap each other and the parallel star detection, instead of the loop
            // blocking on each load sequentially. The bound is deliberately SMALL (not ProcessorCount): each
            // decoded frame is multi-MB and must stay resident until all of its region-analysis tasks finish,
            // so maxPrefetch caps the number of decoded frames held in memory at once. A slot is acquired before
            // each load is started and released only after that frame's analysis completes and its imageState is
            // disposed, so acquire<->release are paired and resident decoded frames never exceed maxPrefetch.
            // Floor 2: one frame can load while the previous is analyzed; ceiling 4: memory (multi-MB decoded frames) grows linearly with little extra overlap benefit past this.
            var maxPrefetch = Math.Max(2, Math.Min(4, Environment.ProcessorCount));
            var prefetchSemaphore = new SemaphoreSlim(maxPrefetch, maxPrefetch);
            // Serializes the decode + prepare stage across all in-flight loads (see ReloadSavedFile): the NINA-core
            // CreateFromFile/PrepareImage pipeline is not safe for concurrent invocation, which the prefetch would
            // otherwise trigger. Detection stays parallel; only load<->load overlap is removed.
            var loadSerializer = new SemaphoreSlim(1, 1);
            // Flat list of every post-task spawned, so the finally can wait for ALL of them (each of which releases
            // its slot) before disposing the semaphore — even if the loop is left early via cancellation/exception
            // before a group's combined task is added to focuserPositionTasks. Avoids disposing while a Release is
            // still in flight.
            var allPostTasks = new List<Task>();
            try {
                var framesPerFile = savedFiles
                    .GroupBy(f => f.ImageNumber)
                    .Select(f => new {
                        ImageNumber = f.Key,
                        Frames = f.Select(g => g.FrameNumber).Max() + 1
                    })
                    .ToList();
                var framesPerPoint = framesPerFile.Min(f => f.Frames);
                savedFiles = savedFiles.Where(f => f.FrameNumber < framesPerPoint).ToList();
                state.Options.FramesPerPoint = framesPerPoint;
                foreach (var focuserPositionGroup in savedFiles.GroupBy(f => f.FocuserPosition)) {
                    var focuserPosition = focuserPositionGroup.Key;

                    var files = focuserPositionGroup.OrderBy(g => g.FrameNumber).ToList();
                    var allMeasurementTasks = new List<Task>();
                    foreach (var savedFile in files) {
                        // OnNextImage assigns ImageNumber/ordering and MUST stay sequential and ordered.
                        var imageState = await state.OnNextImage(savedFile.FrameNumber, savedFile.FocuserPosition, false, token);

                        // Acquire a prefetch slot BEFORE starting the load. If WaitAsync throws (e.g. cancellation),
                        // dispose imageState to release the ExposureSemaphore slot OnNextImage acquired — mirrors the
                        // live-run catch at ~line 882. Nothing between the successful WaitAsync and the post-task
                        // creation throws synchronously (ReloadSavedFile is async; Task.Run/List.Add don't throw), so
                        // the post-task reliably takes ownership of cleanup from that point on. If synchronously-
                        // throwing code is ever added in that window, this guard must be widened to also release the
                        // acquired prefetch slot.
                        try {
                            await prefetchSemaphore.WaitAsync(token);
                        } catch {
                            imageState.Dispose();
                            throw;
                        }

                        // Start the load without awaiting it inline so loads overlap each other and analysis. The
                        // returned Task is awaited by the region-analysis tasks below. Any load failure is captured
                        // in loadTask and surfaces when those tasks await it.
                        var loadTask = ReloadSavedFile(state, savedFile, loadSerializer, token);
                        // Source for the replay reuse cache (consumed only when Options.ReuseSavedDetection is on).
                        // Uses the ORIGINAL image/frame numbers from the saved filename (NOT imageState.ImageNumber,
                        // which OnNextImage reassigned as a fresh ordering counter) and the saved file's own folder,
                        // so it points at the matching _star_detection_result.json written next to this exposure.
                        var cacheSource = new SavedDetectionCacheSource(Path.GetDirectoryName(savedFile.Path), savedFile.ImageNumber, savedFile.FrameNumber);
                        var singleFileAnalysisTasks = new List<Task>();
                        foreach (var regionState in state.FocusRegionStates) {
                            var partialMeasurementTask = Task.Run(async () => {
                                // Image is read-only during detection and is shared across region tasks, so awaiting
                                // the same loadTask from each is safe.
                                var loadedImage = await loadTask;
                                lock (state.StatesLock) {
                                    var imageProperties = loadedImage.RawImageData.Properties;
                                    state.ImageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                                }

                                var measurement = await AnalyzeSavedFile(state, regionState, imageState, loadedImage, token, cacheSource);
                                await FocusPointMeasurementAction(imageState, measurement, state, regionState);
                            });

                            singleFileAnalysisTasks.Add(partialMeasurementTask);
                        }

                        // Created unconditionally and immediately after acquiring the slot (no awaitable code in
                        // between can throw synchronously), so this post-task is the sole, guaranteed owner of the
                        // slot release. Its finally releases the slot and disposes imageState EXACTLY ONCE on every
                        // path (success, load/analysis failure, or cancellation), pairing 1:1 with the WaitAsync
                        // above. The decoded image is held until WhenAll completes, so it is never disposed before
                        // all region-analysis tasks finish reading it. No token is passed to Task.Run so the finally
                        // always runs (a token-canceled Task.Run would skip the delegate and leak the slot/imageState).
                        var singleFilePostTask = Task.Run(async () => {
                            try {
                                await Task.WhenAll(singleFileAnalysisTasks);
                                // After the frame has been loaded + analyzed (so the source file is no longer open for
                                // decode), copy the original raw frame into this run's save folder so a saving reprocess
                                // produces a self-contained, independently-replayable run (the reload path does not
                                // otherwise re-save raw frames). No-op when not saving.
                                MaybeCopyReplayFrame(state, savedFile, imageState.AttemptNumber, imageState.FinalValidation);
                                var incrementedCompletedCount = Interlocked.Increment(ref completedCount);
                                progress.Report(new ApplicationStatus() {
                                    Status = "Data Points",
                                    MaxProgress = totalCount,
                                    Progress = incrementedCompletedCount,
                                    ProgressType = ApplicationStatus.StatusProgressType.ValueOfMaxValue
                                });
                            } finally {
                                imageState.Dispose();
                                prefetchSemaphore.Release();
                            }
                        });
                        allMeasurementTasks.Add(singleFilePostTask);
                        allPostTasks.Add(singleFilePostTask);
                    }

                    var focuserPositionTask = Task.WhenAll(allMeasurementTasks);
                    focuserPositionTasks.Add(focuserPositionTask);
                }

                await Task.WhenAll(focuserPositionTasks);
                foreach (var regionState in state.FocusRegionStates) {
                    regionState.SelectBestHyperbolicModel();
                    regionState.CalculateFinalFocusPoint();
                    // Mirror the live Run path: compute best-focus stability so a replayed/loaded run shows LOO in
                    // the panel instead of NaN (uses the model resolved by SelectBestHyperbolicModel for Hybrid runs).
                    regionState.ComputeLeaveOneOutStability();
                }

                OnCompleted(state, 0.0d, TimeSpan.Zero);
                return new AutoFocusResult() {
                    Succeeded = true,
                    InitialFocuserPosition = -1, // Not known
                    ImageSize = state.ImageSize,
                    StepSize = state.Options.AutoFocusStepSize,
                    RegionResults = state.FocusRegionStates.Select(rs => new AutoFocusRegionResult() {
                        RegionIndex = rs.RegionIndex,
                        Region = rs.Region,
                        EstimatedFinalFocuserPosition = rs.FinalFocusPoint?.X ?? double.NaN,
                        EstimatedFinalHFR = rs.FinalFocusPoint?.Y ?? double.NaN,
                        Fittings = rs.Fittings,
                        RejectedPoints = rs.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
                    }).OrderBy(r => r.RegionIndex).ToArray(),
                    SaveFolder = state.SaveFolder
                };
            } finally {
                // Wait for every spawned post-task to finish so all slot Releases have completed before disposing
                // the semaphore. This also covers the early-exit paths (e.g. a WaitAsync/OnNextImage cancellation
                // mid-loop) where some post-tasks were created but their group was never added to
                // focuserPositionTasks. Exceptions are swallowed here because the original (already-thrown)
                // exception must be the one that propagates out of RerunImpl; this await is cleanup only.
                try {
                    await Task.WhenAll(allPostTasks);
                } catch {
                }
                prefetchSemaphore.Dispose();
                loadSerializer.Dispose();
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
                StarDetectionResult = imageState.GetStarDetectionResult(regionState.RegionIndex),
                Image = imageState.PreservedExposure
            });
        }

        /// <summary>
        /// Writes the replay <c>metadata.json</c> (the star-detection + AutoFocus settings used, region geometry, and
        /// a result summary) to the run's save folder. Written for any run that produced a complete, replayable saved
        /// folder — both live captures and saving reprocesses (a saving reprocess re-saves the raw frames, see
        /// RerunImpl). Only meaningful for STARHFR (contrast detection has no star detector to snapshot). Captures the
        /// capture-time override when one was used (option b), otherwise the live detector's options. Never throws /
        /// never fails the run.
        /// </summary>
        private void WriteReplayMetadata(AutoFocusState state) {
            try {
                if (string.IsNullOrWhiteSpace(state.SaveFolder)) {
                    return;
                }
                if (state.Options.AutoFocusMethod != AFMethodEnum.STARHFR) {
                    return;
                }

                // Capture the settings ACTUALLY used: the capture-time override when replaying with it (option b),
                // otherwise the live detector's options (live capture, or replay with current/updated settings).
                var detector = starDetectionSelector.GetBehavior() as IHocusFocusStarDetection;
                var sdOptions = state.Options.StarDetectionOptionsOverride ?? detector?.StarDetectionOptions;
                if (sdOptions == null) {
                    // Not the Hocus Focus detector (or no options) — nothing meaningful to snapshot for replay.
                    return;
                }

                var focuserSettings = profileService.ActiveProfile.FocuserSettings;
                // >1 explicit region ⇒ an Aberration Inspector run (its region grid is always >= 6); an AF-pane run
                // uses no regions (live) or a single captured region (a capture-time replay routed through regions).
                var isInspectorRun = state.FocusRegions.Count > 1;
                var explicitRegions = state.FocusRegionStates.Select(rs => rs.Region).Where(r => r != null).ToList();

                List<StarDetectionRegion> regions;
                if (explicitRegions.Count > 0) {
                    // Inspector run, or any capture-time replay routed through the explicit-region path — store the
                    // regions actually used (they already encode the capture-time ROI).
                    regions = explicitRegions;
                } else {
                    // AF pane with no explicit region: derive the single region from the crop ROI actually in effect,
                    // so an in-memory replay can reproduce that ROI through the explicit-region path.
                    regions = new List<StarDetectionRegion>() {
                        StarDetectionRegion.FromStarDetectionParams(new StarDetectionParams() {
                            UseROI = focuserSettings.AutoFocusInnerCropRatio < 1.0,
                            InnerCropRatio = focuserSettings.AutoFocusInnerCropRatio,
                            OuterCropRatio = focuserSettings.AutoFocusOuterCropRatio
                        })
                    };
                }

                var inspectorOptions = HocusFocusPlugin.InspectorOptions;
                var regionGeometry = new ReplayRegionGeometry() {
                    AutoFocusInnerCropRatio = focuserSettings.AutoFocusInnerCropRatio,
                    AutoFocusOuterCropRatio = focuserSettings.AutoFocusOuterCropRatio,
                    IsInspectorRun = isInspectorRun,
                    SensorROI = inspectorOptions?.SensorROI ?? 0.0,
                    CornersROI = inspectorOptions?.CornersROI ?? 0.0,
                    SensorCurveModelEnabled = inspectorOptions?.SensorCurveModelEnabled ?? false,
                    Regions = regions
                };

                var results = state.FocusRegionStates.Select(rs => new ReplayRegionResultSummary() {
                    RegionIndex = rs.RegionIndex,
                    EstimatedFinalFocuserPosition = rs.FinalFocusPoint?.X ?? double.NaN,
                    EstimatedFinalHFR = rs.FinalFocusPoint?.Y ?? double.NaN,
                    FinalHFR = rs.FinalHFR?.Measure,
                    InitialHFR = rs.InitialHFR?.Measure,
                    RSquared = rs.Fittings?.HyperbolicFitting?.RSquared ?? double.NaN,
                    SelectedHyperbolicFitModel = rs.Fittings?.SelectedHyperbolicFitModel
                }).ToList();

                var pluginVersion = typeof(AutoFocusEngine).Assembly.GetName().Version?.ToString();
                var metadata = AutoFocusReplayMetadataBuilder.Build(
                    sdOptions, state.Options, regionGeometry, results, DateTime.UtcNow, pluginVersion, StarDetector.StarDetectorVersion);

                var metadataPath = Path.Combine(state.SaveFolder, "metadata.json");
                File.WriteAllText(metadataPath, metadata.Serialize());
                Logger.Info($"Wrote AutoFocus replay metadata to {metadataPath}");
            } catch (Exception e) {
                Logger.Warning($"Failed to write AutoFocus replay metadata.json: {e.Message}");
            }
        }

        private void OnCompleted(
            AutoFocusState state,
            double temperature,
            TimeSpan duration) {
            WriteReplayMetadata(state);
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
                AutoFocusStepSize = ((savedAttempt?.StepSize != null) && (savedAttempt?.StepSize > 0)) ? savedAttempt.StepSize.Value : profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize,
                FocuserOffset = autoFocusOptions.FocuserOffset,
                MaxOutlierRejections = autoFocusOptions.MaxOutlierRejections,
                OutlierRejectionConfidence = autoFocusOptions.OutlierRejectionConfidence,
                WeightedHyperbolicFitEnabled = autoFocusOptions.WeightedHyperbolicFitEnabled,
                HyperbolicFitModel = autoFocusOptions.HyperbolicFitModel,
                FitRejectionCriterion = autoFocusOptions.FitRejectionCriterion,
                ReducedChiSquaredRejectionThreshold = autoFocusOptions.ReducedChiSquaredRejectionThreshold,
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
            var attemptNumber = 0;
            if (!attemptMatch.Success || !int.TryParse(attemptMatch.Groups["ATTEMPT"].Value, out attemptNumber)) {
                if (attemptFolder.Exists) {
                    var subFolders = attemptFolder.EnumerateDirectories("attempt*");
                    if (subFolders.Count() == 1) {
                        attemptMatch = ATTEMPT_REGEX.Match(subFolders.FirstOrDefault().Name);
                        if (!attemptMatch.Success || (!int.TryParse(attemptMatch.Groups["ATTEMPT"].Value, out attemptNumber))) {
                            throw new Exception("A folder named attemptXX must be selected");
                        } else {
                            attemptFolder = subFolders.FirstOrDefault();
                        }
                    }
                    if (!attemptMatch.Success) {
                        throw new Exception("A folder named attemptXX must be selected");
                    }
                } else {
                    throw new Exception("A folder named attemptXX must be selected");
                }
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

            int stepSize = 0;
            if (savedImages.Count >= 2) {
                var focuserPositions = savedImages.Select(i => i.FocuserPosition).Distinct().Order().Take(2).ToList();
                stepSize = (focuserPositions.Count > 1) ? Math.Abs(focuserPositions[0] - focuserPositions[1]) : 0;
            }
            return new SavedAutoFocusAttempt() {
                Attempt = attemptNumber,
                SavedImages = savedImages,
                StepSize = stepSize,
                FolderPath = attemptFolder.FullName
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