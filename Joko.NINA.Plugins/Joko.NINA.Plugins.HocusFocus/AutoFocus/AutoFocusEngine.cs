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
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IAutoFocusOptions autoFocusOptions;
        private readonly IStarAnnotatorOptions starAnnotatorOptions;
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
            IStarAnnotatorOptions starAnnotatorOptions,
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
            this.starAnnotatorOptions = starAnnotatorOptions;
            this.alglibAPI = alglibAPI;
        }

        private class CurveFittingResult {

            private CurveFittingResult() {
            }

            public AutoFocusFitting Fittings { get; private set; }

            public ImmutableList<ScatterErrorPoint> RejectedPoints { get; private set; }

            // Points excluded from the final fit because they fall outside the symmetric focus window (Behavior A).
            // A sibling of RejectedPoints (Grubbs outliers), never reclassified as rejected. Empty until Behavior A
            // populates it.
            public ImmutableList<ScatterErrorPoint> WindowExcludedPoints { get; private set; }

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
                            RejectedPoints = ImmutableList.CreateRange(rejectedPoints),
                            WindowExcludedPoints = ImmutableList<ScatterErrorPoint>.Empty
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

            // Points excluded from the final fit by the symmetric focus window (Behavior A), a sibling of
            // RejectedPoints (Grubbs outliers). Stays empty until Behavior A fills it.
            public Dictionary<int, MeasureAndError> WindowExcludedPoints { get; private set; } = new Dictionary<int, MeasureAndError>();

            public void ResetMeasurements() {
                lock (SubMeasurementsLock) {
                    this.MeasurementsByFocuserPoint.Clear();
                    this.SubMeasurementsByFocuserPoints.Clear();
                    this.RejectedPoints.Clear();
                    this.WindowExcludedPoints.Clear();
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
            /// Behavior A (symmetric-window exclusion at finalization only). Excludes fit-input points that fall
            /// outside the window <c>minimum ± (offsetSteps+0.5)*stepSize</c>, then refits on the kept subset, so a
            /// lopsided or far-from-focus sweep does not bias the final fit. The window is centered on the FITTED
            /// VERTEX (<see cref="DetermineFinalFocusPoint"/> — a post-Grubbs, weighted least-squares estimate), NOT
            /// the lowest raw point, so a single spurious low-HFR far point cannot drag the center out to itself.
            /// Bounded fit→window→refit fixed point: point removal is monotonic, so it terminates in at most
            /// <c>offsetSteps+1</c> passes (usually 1). The kept set is never allowed below 3 valid (Y &gt; 0) points,
            /// the floor under which <see cref="CurveFittingResult.Calculate"/> returns null. Dropped points are
            /// recorded in <see cref="WindowExcludedPoints"/>, a sibling of the Grubbs <see cref="RejectedPoints"/>
            /// and disjoint from it by construction (windowing runs before the fit; Grubbs runs inside the fit of the
            /// kept subset). Refitting goes through <see cref="UpdateCurveFittings"/>, which only swaps
            /// <c>lastValidFocusPoints</c> and the derived fittings — <see cref="MeasurementsByFocuserPoint"/> (the raw
            /// measurements for reports/charts) is never touched. Returns true when any point was excluded. No-op
            /// (returns false, byte-identical) when the internal <c>SymmetricFocusWindowEnabled</c> flag is off.
            /// </summary>
            public bool ApplyFinalSymmetricWindow(int offsetSteps, int stepSize) {
                if (!State.Options.SymmetricFocusWindowEnabled) {
                    return false;
                }
                if (lastValidFocusPoints == null) {
                    return false;
                }

                var anyExcluded = false;
                var maxPasses = offsetSteps + 1;
                for (var pass = 0; pass < maxPasses; pass++) {
                    var center = DetermineFinalFocusPoint()?.X;
                    if (!center.HasValue) {
                        break;
                    }

                    var (included, excluded) = PartitionByFocusWindow(lastValidFocusPoints, center.Value, offsetSteps, stepSize);
                    if (excluded.Count == 0) {
                        break;
                    }

                    // Never drop below the 3-valid-point floor the fit requires; if we would, keep the current fit as-is.
                    if (included.Count(p => p.Y > 0.0) < 3) {
                        break;
                    }

                    UpdateCurveFittings(included);
                    lock (SubMeasurementsLock) {
                        foreach (var ep in excluded) {
                            var focuserPosition = (int)Math.Round(ep.X);
                            this.WindowExcludedPoints[focuserPosition] = new MeasureAndError() { Measure = ep.Y, Stdev = ep.ErrorY };
                        }
                    }
                    anyExcluded = true;
                }
                return anyExcluded;
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

        // Distinguishes WHY ValidateCalculatedFocusPosition rejected a run so RunAutoFocus can decide whether a
        // re-centered retry is worthwhile. HfrRegression and FinalPointOutOfBounds are symptoms of a blind sweep
        // that started far from focus (e.g. a screw adjustment shoved the focuser), which re-centering the sweep on
        // the just-calculated focus point can fix. The other modes indicate bad data / a bad fit where re-centering
        // would not help, so they are not retry-eligible.
        internal enum AutoFocusFailureMode {
            None,
            FitQuality,
            InitialHfrFailed,
            FinalPointOutOfBounds,
            HfrRegression,
            FinalHfrMissing
        }

        // Decides whether a final-validation failure should trigger the single-shot retry that re-centers the blind
        // sweep on the just-calculated focus point. Pure so it is unit-testable without a full sweep harness.
        internal static bool ShouldRetryFromCalculatedPoint(AutoFocusFailureMode mode, int calculatedPoint, int currentSweepCenter, bool calculatedPointRetryUsed) {
            if (calculatedPointRetryUsed) {
                return false;
            }
            var retryEligibleMode = mode == AutoFocusFailureMode.HfrRegression
                                 || mode == AutoFocusFailureMode.FinalPointOutOfBounds;
            if (!retryEligibleMode) {
                return false;
            }
            // Guard a no-op / runaway re-sweep: require a valid point that differs from the center we just swept.
            return calculatedPoint >= 0 && calculatedPoint != currentSweepCenter;
        }

        // Which way the blind sweep is stepping. Only meaningful when Behavior B (the directional bootstrap cap) is
        // enabled; the initial seed points sit on the high side, so the pre-walk direction is treated as Right.
        private enum WalkDirection {
            Left,
            Right
        }

        private static WalkDirection OppositeDirection(WalkDirection direction) {
            return direction == WalkDirection.Left ? WalkDirection.Right : WalkDirection.Left;
        }

        // Behavior B decision (directional bootstrap cap + one-shot reversal). Pure so it is unit-testable without a
        // full sweep harness. Reverse only when the direction we are ABOUT to step has already reached the effective
        // cap, we have not yet formed a two-sided interior bracket, and we have not already reversed once (latch → at
        // most one reversal per sweep, so no oscillation).
        internal static bool ShouldReverseDirection(int stepOutsThisDir, int effectiveCap, bool bracketFormed, bool hasReversed) {
            if (hasReversed || bracketFormed) {
                return false;
            }
            return stepOutsThisDir >= effectiveCap;
        }

        // Builds the diagnostic explaining WHY the HFR-improvement validation rejected a region. The generic
        // "Failed assessing HFR at the initial position" gave no way to tell which region failed or why; this
        // names the region (the same integer the detector logs as "Region: N") and distinguishes the three
        // causes recoverable from the region's measurement data:
        //   - hfr == null            -> the sub-frame loop never reached FramesPerPoint (capture/analysis
        //                               incomplete for this region, e.g. cancellation).
        //   - Measure == 0, σ finite -> the detector ran but found no usable stars (AverageHFR stayed 0; see
        //                               HocusFocusStarDetection's "hfrStars.Count > 1" guard).
        //   - Measure == 0, σ = NaN  -> exposure analysis threw for a sub-frame (AnalyzeExposure's catch injects
        //                               {Measure: 0, Stdev: NaN}).
        // Pure/static so it is unit-testable in the same style as ShouldRetryFromCalculatedPoint.
        internal static string DescribeHfrValidationFailure(
            string phase,
            int regionIndex,
            MeasureAndError? hfr,
            IReadOnlyList<MeasureAndError> subMeasurements,
            int framesPerPoint) {
            var subCount = subMeasurements?.Count ?? 0;
            var nanCount = subMeasurements?.Count(m => double.IsNaN(m.Stdev)) ?? 0;

            string cause;
            if (!hfr.HasValue) {
                cause = $"no averaged HFR was recorded ({subCount} of {framesPerPoint} sub-frame(s) completed; capture/analysis did not finish for this region)";
            } else if (hfr.Value.Measure == 0.0) {
                cause = nanCount > 0
                    ? $"HFR measured 0 - exposure analysis errored on {nanCount} of {subCount} sub-frame(s) (σ=NaN)"
                    : $"HFR measured 0 - the detector found no usable stars in this region across {subCount} sub-frame(s)";
            } else {
                // Defensive: the two call sites only invoke this on null-or-zero, but never assert a false reason.
                cause = $"HFR {hfr.Value.Measure:0.00} was rejected";
            }

            var hfrText = hfr.HasValue ? hfr.Value.Measure.ToString("0.00") : "null";
            var subList = subCount == 0
                ? ""
                : " [" + string.Join(", ", subMeasurements.Select(m =>
                    $"{m.Measure:0.00} (σ={(double.IsNaN(m.Stdev) ? "NaN" : m.Stdev.ToString("0.00"))})")) + "]";

            return $"Failed assessing HFR at the {phase} for Region {regionIndex}: {cause}. " +
                   $"Measured HFR={hfrText}; sub-frames {subCount}/{framesPerPoint}{subList}. " +
                   $"Cross-reference the \"Region: {regionIndex}\" star-detection lines.";
        }

        // The single-region HFR-improvement decision. Returns the specific rejection mode, or None when the region
        // passes. The whole-run validation gates on region 0 only (see ValidateCalculatedFocusPosition), so a
        // transient dropout in a corner/sub-region cannot discard an otherwise-good autofocus. Pure/static so the
        // gate is unit-testable without the full sweep harness.
        internal static AutoFocusFailureMode EvaluateHfrImprovement(MeasureAndError? initialHfr, MeasureAndError? finalHfr, double improvementThreshold) {
            if (!finalHfr.HasValue || finalHfr.Value.Measure == 0.0) {
                return AutoFocusFailureMode.FinalHfrMissing;
            }
            if (!initialHfr.HasValue || initialHfr.Value.Measure == 0.0) {
                return AutoFocusFailureMode.InitialHfrFailed;
            }
            if (finalHfr.Value.Measure > initialHfr.Value.Measure * (1.0 + improvementThreshold)) {
                return AutoFocusFailureMode.HfrRegression;
            }
            return AutoFocusFailureMode.None;
        }

        // Whether this frame's star detection should be drawn onto the live imaging display. Pure/static so the gate is
        // unit-testable without the full sweep harness.
        //
        // showAnnotations is checked here rather than left to the annotator: GenerateAnnotatedImage no-ops when the master
        // switch is off, so testing it up front skips the (expensive) full-frame render entirely. The overlay is limited to
        // plain full-frame runs (isFullFrameRegion) because a multi-region run - the Aberration Inspector, the Tilt Adapter
        // Wizard - would have every region racing to paint the one display. Replay runs are excluded (isLiveRun) because
        // their bounded prefetch prepares frames ahead of analysis, so frameIsCurrentlyDisplayed would reject most of them;
        // the Review Frames UI already re-renders those overlays on demand.
        internal static bool ShouldAnnotateAutoFocusDisplay(
            bool annotateDuringAutoFocus,
            bool showAnnotations,
            bool isFullFrameRegion,
            bool isLiveRun,
            bool frameIsCurrentlyDisplayed) {
            return annotateDuringAutoFocus && showAnnotations && isFullFrameRegion && isLiveRun && frameIsCurrentlyDisplayed;
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

            // Set by ValidateCalculatedFocusPosition at each failure site so RunAutoFocus can branch on the cause.
            // LastCalculatedFocusPoint is the pre-offset region-0 fit center captured on a retry-eligible failure, so
            // a re-centered retry sweeps around the actual estimated focus rather than the contaminated start.
            public AutoFocusFailureMode LastFailureMode { get; set; } = AutoFocusFailureMode.None;
            public int LastCalculatedFocusPoint { get; set; } = -1;

            // Region index that tripped the last validation failure (HFR-improvement checks), so the metadata.json
            // FailureReason can name it. Null when the failure has no single associated region.
            public int? LastFailureRegionIndex { get; set; } = null;

            public List<Task> InitialHFRTasks { get; private set; } = new List<Task>();
            public List<Task> AnalysisTasks { get; private set; } = new List<Task>();
            public AsyncAutoResetEvent MeasurementCompleteEvent { get; private set; }
            public string SaveFolder { get; set; } = "";

            // The IRenderedImage most recently handed to imagingMediator.PrepareImage - i.e. the frame currently on the
            // imaging display. Written in PrepareExposure, read by the live-annotation stale-frame guard: analyses of
            // different frames overlap (ExposureSemaphore allows MaxConcurrent), so a slow detection must not paint its
            // overlay over a newer frame. Reference reads/writes are atomic; volatile for visibility across those tasks.
            private volatile IRenderedImage lastDisplayedRenderedImage;

            public IRenderedImage LastDisplayedRenderedImage {
                get => lastDisplayedRenderedImage;
                set => lastDisplayedRenderedImage = value;
            }

            // Fire-and-forget annotation renders spawned by MaybeDisplayAutoFocusAnnotation. Drained at run teardown so a
            // late SetImage can never paint over the next, non-AutoFocus image.
            private readonly List<Task> displayAnnotationTasks = new List<Task>();

            public void TrackDisplayAnnotationTask(Task task) {
                lock (StatesLock) {
                    displayAnnotationTasks.Add(task);
                }
            }

            public Task[] SnapshotDisplayAnnotationTasks() {
                lock (StatesLock) {
                    return displayAnnotationTasks.ToArray();
                }
            }

            // At most one annotation render is in flight at a time. Analyses of different frames overlap, and until now
            // IStarAnnotator.GetAnnotatedImage was only ever called from NINA's display pipeline, which serializes it. Two
            // concurrent AutoFocus renders would both queue a full-frame conversion+draw AND race the annotator's own
            // previousParams/previousResult/previousAnnotatedImageRef triple (which drives its live re-annotation on an
            // option change). Dropping the newcomer rather than queueing it is right: the in-flight render is for this
            // frame or a newer one, and a superseded render is discarded by the stale-frame re-check anyway. Under normal
            // AutoFocus (exposure time greatly exceeds render time) this never contends.
            private int annotationRenderInFlight;

            public bool TryClaimAnnotationRender() => Interlocked.CompareExchange(ref annotationRenderInFlight, 1, 0) == 0;

            public void ReleaseAnnotationRender() => Interlocked.Exchange(ref annotationRenderInFlight, 0);

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
                LastFailureMode = AutoFocusFailureMode.None;
                LastCalculatedFocusPoint = -1;
                LastFailureRegionIndex = null;
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
                    // Review-Frames runs request PSF modeling (which GetStarDetectorParams forces off for auto-focus) so
                    // the review can show PSF-derived per-star properties. The region==null path lifts this via the
                    // modelPSFForAutoFocus Detect overload; do the equivalent here so a capture-time replay routed
                    // through the explicit-region path (option b) keeps PSF data in the review. Detection HFR is unaffected.
                    if (state.Options.ModelPSF) {
                        starDetectorParams.ModelPSF = true;
                    }

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

                // cacheSource is non-null only on the replay path (RerunImpl always constructs one), so it doubles as the
                // live/replay discriminator.
                MaybeDisplayAutoFocusAnnotation(state, regionState, image, analysisParams, analysisResult, isLiveRun: cacheSource == null, token: token);

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

        /// <summary>
        /// Best-effort: draw this frame's star detection onto the live imaging display, reusing the detection auto focus
        /// already ran rather than paying for a second one. Never throws (annotation must not fail an AF measurement) and
        /// never blocks the caller - the render is a full-frame 16→8bpp conversion plus draw, so keeping it off the
        /// measurement path leaves AF timing and the ExposureSemaphore slot untouched. The spawned task is tracked on the
        /// state and drained at run teardown.
        ///
        /// annotationParams is only consulted by the annotator when the result is NOT a HocusFocusStarDetectionResult (the
        /// HF result carries its own DetectorParams.Region for the ROI). This is the region==null branch, where
        /// analysisParams carries the correct UseROI/InnerCropRatio/OuterCropRatio for NINA's built-in annotator.
        /// </summary>
        private void MaybeDisplayAutoFocusAnnotation(
            AutoFocusState state,
            AutoFocusRegionState regionState,
            IRenderedImage image,
            StarDetectionParams annotationParams,
            StarDetectionResult analysisResult,
            bool isLiveRun,
            CancellationToken token) {
            var originalImage = image?.OriginalImage;
            if (originalImage == null) {
                return;
            }

            if (!ShouldAnnotateAutoFocusDisplay(
                    annotateDuringAutoFocus: starAnnotatorOptions.ShowAnnotationsDuringAutoFocus,
                    showAnnotations: starAnnotatorOptions.ShowAnnotations,
                    isFullFrameRegion: regionState.Region == null,
                    isLiveRun: isLiveRun,
                    frameIsCurrentlyDisplayed: ReferenceEquals(state.LastDisplayedRenderedImage, image))) {
                return;
            }

            var annotator = starAnnotatorSelector.GetBehavior();
            if (annotator == null) {
                return;
            }

            if (!state.TryClaimAnnotationRender()) {
                Logger.Trace("Skipping auto focus display annotation - another render is already in flight");
                return;
            }

            // Mirrors NINA's RenderedImage.DetectStars. The Hocus Focus annotator ignores this in favor of its own MaxStars
            // option, but a different selected annotator (e.g. NINA's built-in) honors it.
            var maxStars = profileService.ActiveProfile.ImageSettings.AnnotateUnlimitedStars ? -1 : 200;

            // The token is deliberately not passed to Task.Run: the delegate owns cancellation so its catch always runs.
            var annotationTask = Task.Run(async () => {
                try {
                    var annotatedImage = await annotator.GetAnnotatedImage(annotationParams, analysisResult, originalImage, maxStars, token);

                    // Re-check after the render: a newer frame may have been prepared and displayed in the meantime.
                    if (annotatedImage != null && !token.IsCancellationRequested && ReferenceEquals(state.LastDisplayedRenderedImage, image)) {
                        imagingMediator.SetImage(annotatedImage);
                    }
                } catch (OperationCanceledException) {
                    // Auto focus was cancelled mid-render. Nothing to display.
                } catch (Exception e) {
                    Logger.Error(e, "Failed to render star detection annotations onto the auto focus display");
                } finally {
                    state.ReleaseAnnotationRender();
                }
            });
            state.TrackDisplayAnnotationTask(annotationTask);
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
            var preparedImage = await imagingMediator.PrepareImage(imageData, prepareParameters, token);

            // This is what NINA assigns to ImageControlVM.RenderedImage, so recording it here gives the live-annotation
            // stale-frame guard an exact "is this frame still on screen" test. Single choke point for live and replay.
            state.LastDisplayedRenderedImage = preparedImage;
            return preparedImage;
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
            IProgress<ApplicationStatus> progress,
            bool captureOnly = false) {
            var attemptNumber = state.AttemptNumber;
            for (int i = 0; i < state.Options.FramesPerPoint; ++i) {
                var imageState = await state.OnNextImage(i, focuserPosition, finalValidation, token);
                token.ThrowIfCancellationRequested();

                var imageNumber = state.ImageNumber;
                var frameNumber = i;
                // For the capture-only sweep, synthesize a per-exposure countdown alongside the exposure (NINA doesn't
                // forward the camera's countdown to our progress). The normal AF path is unchanged.
                var exposureData = captureOnly
                    ? await TakeExposureWithLiveCountdown(state, focuserPosition, token, progress)
                    : await TakeExposure(state, focuserPosition, token, progress);
                imageState.MeasurementStarted();
                try {
                    var prepareExposureTask = PrepareExposure(state, imageState, exposureData, token);
                    if (captureOnly) {
                        // Capture-only sweep: save the frame and record its size, but skip star detection entirely.
                        // The optimizer re-detects the saved frames offline, so detecting here — with the current
                        // settings that can't focus — is wasted work, and there is no curve to build.
                        var saveTask = Task.Run(async () => {
                            try {
                                var preparedExposure = await prepareExposureTask;
                                lock (state.StatesLock) {
                                    var imageProperties = preparedExposure.RawImageData.Properties;
                                    state.ImageSize = new DrawingSize(width: imageProperties.Width, height: imageProperties.Height);
                                }
                            } finally {
                                imageState.Dispose();
                            }
                        }, token);
                        lock (state.StatesLock) {
                            state.AnalysisTasks.Add(saveTask);
                        }
                    } else {
                        var exposureAnalysisTasks = new List<Task>();
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

            // Behavior B (directional bootstrap cap + one-shot reversal). When the configured cap is 0 the behavior
            // is DISABLED and the walk below is byte-identical to the original: no counter influences a decision and
            // the TooManyFailedMeasurements throw reverts to `failureCount >= offsetSteps`. effectiveCap can never be
            // tighter than a valid two-sided bracket needs (offsetSteps + 1). All of this is pure local state read
            // only under the same SubMeasurementsLock trend snapshot the walk already relies on, so the reversal
            // decision depends only on counts/trend state, not measurement completion order (replay determinism).
            var configuredCap = autoFocusState.Options.MaxBlindStepsPerDirection;
            var behaviorBEnabled = configuredCap > 0;
            var effectiveCap = Math.Max(configuredCap, offsetSteps + 1);
            var leftStepOuts = 0;
            var rightStepOuts = 0;
            var hasReversed = false;
            WalkDirection? forcedDirection = null;
            // The seed points sit on the high (right) side, so failures before the first walk step are attributed to Right.
            var lastStepDirection = WalkDirection.Right;
            // Failures accumulated BEFORE the (single) reversal are baselined out so the reversed direction gets a
            // fresh TooManyFailedMeasurements budget rather than immediately re-tripping on the pre-reversal failures.
            // Stays 0 when Behavior B is disabled, so the throw condition is byte-identical to `failureCount >= offsetSteps`.
            var failureBaseline = 0;

            // The single reversal action shared by both cap-out triggers (the failure-count throw site and the
            // directional step cap): latch hasReversed, force the opposite direction with a fresh step-out budget,
            // baseline out the pre-reversal failures, and physically return toward the start before exploring the
            // other side. Only ever called when Behavior B is enabled and !hasReversed, so it fires at most once.
            async Task ReverseWalkAsync(WalkDirection newForcedDirection, int currentFailureCount) {
                hasReversed = true;
                forcedDirection = newForcedDirection;
                if (newForcedDirection == WalkDirection.Left) {
                    leftStepOuts = 0;
                } else {
                    rightStepOuts = 0;
                }
                failureBaseline = currentFailureCount;
                Logger.Info($"Blind AutoFocus could not bracket focus within {effectiveCap} steps going {OppositeDirection(newForcedDirection)}; returning to {initialFocusPosition} and forcing the {newForcedDirection} direction");
                await focuserMediator.MoveFocuser(initialFocusPosition, token);
            }

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
                if (failureCount - failureBaseline >= offsetSteps) {
                    if (behaviorBEnabled && !hasReversed) {
                        // Behavior B: the direction we've been walking piled up too many failed detections without
                        // bracketing focus. Rather than failing the whole sweep, return to the start and try the
                        // other direction once (reversal-aware throw). Only once BOTH directions are exhausted
                        // (hasReversed, i.e. offsetSteps NEW failures after the reversal) do we actually throw.
                        await ReverseWalkAsync(OppositeDirection(lastStepDirection), failureCount);
                    } else {
                        throw new TooManyFailedMeasurementsException(failureCount);
                    }
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

                // Behavior B: choose the step direction, honoring a forced direction set by a prior reversal. When
                // the behavior is disabled, stepLeft is exactly the original `leftTrendCount < offsetSteps` and no
                // reversal can fire, so the branch taken (and everything inside it) is byte-identical to the original.
                var stepLeft = leftTrendCount < offsetSteps;
                if (behaviorBEnabled) {
                    if (forcedDirection.HasValue) {
                        stepLeft = forcedDirection.Value == WalkDirection.Left;
                    }
                    var bracketFormed = leftTrendCount > 0 && rightTrendCount > 0;
                    var stepOutsThisDir = stepLeft ? leftStepOuts : rightStepOuts;
                    if (ShouldReverseDirection(stepOutsThisDir, effectiveCap, bracketFormed, hasReversed)) {
                        await ReverseWalkAsync(stepLeft ? WalkDirection.Right : WalkDirection.Left, failureCount);
                        stepLeft = forcedDirection.Value == WalkDirection.Left;
                    }
                }

                if (stepLeft) {
                    ++leftStepOuts;
                    lastStepDirection = WalkDirection.Left;
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
                    ++rightStepOuts;
                    lastStepDirection = WalkDirection.Right;
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
            var autofocusFilter = forRerun ? imagingFilter : await SetAutofocusFilter(options, imagingFilter, token, progress);
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

                // The blind sweep is normally centered on the original starting position. When a final-validation
                // failure is fixable by re-centering (HFR regression / point outside the swept range) we retry ONCE
                // centered on the calculated focus point instead — independent of TotalNumberOfAttempts. InitialFocuserPosition
                // stays the true original so the focuser is restored there if the run ultimately fails.
                int sweepCenter = initialFocusPosition;
                bool calculatedPointRetryUsed = false;

                // Structural backstop so the retry loop can NEVER run unbounded (defense-in-depth for a hardware loop):
                // at most TotalNumberOfAttempts attempts plus the single calculated-point retry. The conditions below
                // already guarantee this — the calculated-point retry fires at most once (calculatedPointRetryUsed is
                // scoped OUTSIDE this loop and is only ever set true), and the standard reattempt is gated on the
                // strictly-increasing AttemptNumber — but the cap makes termination independent of those conditions
                // staying correct. It should never trigger in normal operation.
                int maxIterations = Math.Max(1, autoFocusState.Options.TotalNumberOfAttempts) + 1;
                int iteration = 0;

                do {
                    if (++iteration > maxIterations) {
                        Logger.Error($"AutoFocus retry loop exceeded its {maxIterations}-iteration backstop; aborting to prevent an infinite loop.");
                        break;
                    }
                    await StartInitialFocusPoints(initialFocusPosition, autoFocusState, token, progress);
                    reattempt = false;

                    autoFocusState.OnNextAttempt();
                    OnIterationStarted(autoFocusState.AttemptNumber);

                    var iterationTaskCts = new CancellationTokenSource();
                    var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(token, iterationTaskCts.Token);
                    bool goodFocusPosition = false;

                    try {
                        await pointGenerationAction(sweepCenter, autoFocusState, iterationCts.Token, progress);
                        token.ThrowIfCancellationRequested();

                        goodFocusPosition = await ValidateCalculatedFocusPosition(autoFocusState, iterationCts.Token, progress);
                    } catch (TooManyFailedMeasurementsException e) {
                        // Allow retries for too many failed points retries
                        Logger.Error($"Too many failed points ({e.NumFailures})");
                        Notification.ShowWarning(Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"]);
                        progress?.Report(new ApplicationStatus() { Status = Loc.Instance["LblAutoFocusNotEnoughtSpreadedPoints"] });
                    } catch (InitialHFRFailedException) {
                        // Allow retries for initial HFR failed
                        Logger.Error($"Initial HFR calculation failed");
                        Notification.ShowWarning("Calculating initial HFR failed");
                        progress?.Report(new ApplicationStatus() { Status = "Calculating initial HFR failed" });
                    }

                    var duration = stopWatch.Elapsed;
                    if (!goodFocusPosition) {
                        // Ensure we cancel any remaining tasks from this iteration so we can start the next
                        iterationTaskCts.Cancel();
                        if (ShouldRetryFromCalculatedPoint(autoFocusState.LastFailureMode, autoFocusState.LastCalculatedFocusPoint, sweepCenter, calculatedPointRetryUsed)) {
                            // The sweep started too far from focus and contaminated the curve. Re-center the next
                            // sweep on the calculated focus point and re-attempt once (capped independently of the
                            // normal attempt budget, so it fires even when TotalNumberOfAttempts == 1).
                            calculatedPointRetryUsed = true;
                            sweepCenter = autoFocusState.LastCalculatedFocusPoint;
                            Notification.ShowWarning(Loc.Instance["LblAutoFocusReattempting"]);
                            Logger.Warning($"AutoFocus failure ({autoFocusState.LastFailureMode}). Re-centering the sweep on the calculated focus point {sweepCenter} and re-attempting once.");
                            await focuserMediator.MoveFocuser(sweepCenter, token);

                            OnIterationFailed(
                                state: autoFocusState,
                                temperature: focuserMediator.GetInfo().Temperature,
                                duration: stopWatch.Elapsed);
                            reattempt = true;
                        } else if (autoFocusState.AttemptNumber < autoFocusState.Options.TotalNumberOfAttempts) {
                            // Standard reattempt restarts the sweep from the true original starting position.
                            sweepCenter = initialFocusPosition;
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

            // When the calculated point falls outside the swept range we DEFER the rejection (rather than returning
            // immediately) so the focuser still moves there and a final-validation image is captured for diagnosis
            // before we fail. Recorded here, applied after the move+exposure below.
            var anyRegionOutOfBounds = false;
            StarDetectionRegion outOfBoundsRegion = null;
            int outOfBoundsPosition = 0, outOfBoundsMin = 0, outOfBoundsMax = 0;

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
                                autoFocusState.LastFailureMode = AutoFocusFailureMode.FitQuality;
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
                            autoFocusState.LastFailureMode = AutoFocusFailureMode.FitQuality;
                            return false;
                        }

                        if ((fitting == AFCurveFittingEnum.TRENDLINES || fitting == AFCurveFittingEnum.TRENDHYPERBOLIC || fitting == AFCurveFittingEnum.TRENDPARABOLIC) && trendlineBad) {
                            Logger.Error($"Auto Focus Failed! R² (Coefficient of determination) for Trendline Fitting is below threshold. Left: {Math.Round(fittings.TrendlineFitting.LeftTrend.RSquared, 2)} / {rSquaredThreshold}; Right: {Math.Round(fittings.TrendlineFitting.RightTrend.RSquared, 2)} / {rSquaredThreshold}; Region: {autoFocusRegionState.Region}");
                            Notification.ShowError(string.Format(Loc.Instance["LblAutoFocusCurveCorrelationCoefficientLow"], Math.Round(fittings.TrendlineFitting.LeftTrend.RSquared, 2), Math.Round(fittings.TrendlineFitting.RightTrend.RSquared, 2), rSquaredThreshold));
                            autoFocusState.LastFailureMode = AutoFocusFailureMode.FitQuality;
                            return false;
                        }
                    }

                    // Out-of-bounds gate keeps using the FULL measured range so windowing can only reduce a
                    // FinalPointOutOfBounds, never narrow the range and mask an extrapolating fit.
                    var min = autoFocusRegionState.MeasurementsByFocuserPoint.Min(x => x.Key);
                    var max = autoFocusRegionState.MeasurementsByFocuserPoint.Max(x => x.Key);

                    // Behavior A: exclude far points outside the symmetric window (per region, around its own vertex)
                    // and refit BEFORE model selection, so a lopsided/far-from-focus sweep does not bias the final fit.
                    autoFocusRegionState.ApplyFinalSymmetricWindow(autoFocusState.Options.AutoFocusInitialOffsetSteps, autoFocusState.Options.AutoFocusStepSize);
                    autoFocusRegionState.SelectBestHyperbolicModel();
                    autoFocusRegionState.CalculateFinalFocusPoint();
                    autoFocusRegionState.ComputeLeaveOneOutStability();
                    var finalFocusPosition = (int)Math.Round(autoFocusRegionState.FinalFocusPoint?.X ?? -1);
                    if (finalFocusPosition < 0) {
                        Logger.Error("Fit failed. There likely weren't enough data points with detected stars");
                        Notification.ShowError("Fit failed. There likely weren't enough data points with detected stars");
                        autoFocusState.LastFailureMode = AutoFocusFailureMode.FitQuality;
                        return false;
                    }

                    if (finalFocusPosition < min || finalFocusPosition > max) {
                        // Defer this rejection until after the final-validation image is captured below. Record the
                        // first offending region (mirrors the original "first failure wins" ordering) and stop
                        // checking further regions.
                        anyRegionOutOfBounds = true;
                        outOfBoundsRegion = autoFocusRegionState.Region;
                        outOfBoundsPosition = finalFocusPosition;
                        outOfBoundsMin = min;
                        outOfBoundsMax = max;
                        break;
                    }
                }
            }

            var firstRegionFinalFocusPosition = (int)Math.Round(autoFocusState.FocusRegionStates[0].FinalFocusPoint?.X ?? -1);
            if (firstRegionFinalFocusPosition < 0) {
                Logger.Error("Fit failed. There likely weren't enough data points with detected stars");
                Notification.ShowError("Fit failed. There likely weren't enough data points with detected stars");
                autoFocusState.LastFailureMode = AutoFocusFailureMode.FitQuality;
                return false;
            }

            // Region-0 fit center BEFORE the focuser offset is applied — the best estimate of focus, and the center a
            // re-centered retry should sweep around when this validation fails (see RunAutoFocus).
            var calculatedFocusPoint = firstRegionFinalFocusPosition;

            if (this.autoFocusOptions.FocuserOffset != 0) {
                Logger.Info($"Applying focuser offset of {this.autoFocusOptions.FocuserOffset} to {firstRegionFinalFocusPosition}");
                firstRegionFinalFocusPosition += this.autoFocusOptions.FocuserOffset;
            }

            // Drive the focuser to the calculated point and (when HFR validation is on) capture a final-validation
            // image there. For an OUT-OF-BOUNDS point the only reason to move is that diagnostic image: clamp the target
            // to the swept range so an extreme/extrapolated fit center can't drive the focuser to its travel limit, and
            // skip the move entirely when HFR validation is off (no image to capture) — matching the pre-refactor
            // behavior of not moving on an out-of-bounds result. In-bounds (success) moves are unchanged.
            if (!anyRegionOutOfBounds || autoFocusState.Options.ValidateHfrImprovement) {
                var moveTarget = anyRegionOutOfBounds
                    ? Math.Min(outOfBoundsMax, Math.Max(outOfBoundsMin, firstRegionFinalFocusPosition))
                    : firstRegionFinalFocusPosition;
                await focuserMediator.MoveFocuser(moveTarget, token);
                token.ThrowIfCancellationRequested();

                if (autoFocusState.Options.ValidateHfrImprovement) {
                    Logger.Info($"Validating HFR at final focus position {moveTarget}");
                    await StartAutoFocusPoint(moveTarget, autoFocusState, FinalHFRMeasurementAction, true, token, progress);
                    token.ThrowIfCancellationRequested();
                }
            }

            await Task.WhenAll(autoFocusState.AnalysisTasks);
            token.ThrowIfCancellationRequested();

            // Apply the deferred out-of-bounds rejection now that the focuser has moved to the calculated point and
            // (when HFR validation is on) a final-validation image was captured for diagnosis. Re-centering the sweep
            // on the calculated point can fix this, so it is retry-eligible.
            if (anyRegionOutOfBounds) {
                Logger.Error($"Determined focus point position is outside of the overall measurement points of the curve. Fitting is incorrect and autofocus settings are incorrect. FocusPosition {outOfBoundsPosition}; Min: {outOfBoundsMin}; Max: {outOfBoundsMax}; Region: {outOfBoundsRegion}");
                Notification.ShowError(Loc.Instance["LblAutoFocusPointOutsideOfBounds"]);
                autoFocusState.LastFailureMode = AutoFocusFailureMode.FinalPointOutOfBounds;
                autoFocusState.LastCalculatedFocusPoint = calculatedFocusPoint;
                return false;
            }

            if (autoFocusState.Options.AutoFocusMethod == AFMethodEnum.STARHFR && autoFocusState.Options.ValidateHfrImprovement) {
                // Gate on region 0 — the primary/full-frame region that drives the focus decision — ONLY. A
                // transient dropout in a corner/sub-region (e.g. one initial frame where a region momentarily
                // detects no usable stars) must not discard an otherwise-good autofocus. This mirrors the
                // region-0-only early guard in StartBlindFocusPoints. Every region's HFR is still logged by the
                // detector for diagnosis; corner dropouts are simply non-fatal here.
                var primaryRegionState = autoFocusState.FocusRegionStates[0];
                lock (primaryRegionState.SubMeasurementsLock) {
                    var hfrFailureMode = EvaluateHfrImprovement(primaryRegionState.InitialHFR, primaryRegionState.FinalHFR, autoFocusState.Options.HFRImprovementThreshold);
                    if (hfrFailureMode != AutoFocusFailureMode.None) {
                        autoFocusState.LastFailureMode = hfrFailureMode;
                        autoFocusState.LastFailureRegionIndex = primaryRegionState.RegionIndex;
                        switch (hfrFailureMode) {
                            case AutoFocusFailureMode.FinalHfrMissing:
                                Logger.Warning(DescribeHfrValidationFailure("final focus point", primaryRegionState.RegionIndex, primaryRegionState.FinalHFR, primaryRegionState.FinalHFRSubMeasurements, autoFocusState.Options.FramesPerPoint));
                                Notification.ShowWarning($"Failed assessing HFR at the final focus point (Region {primaryRegionState.RegionIndex}). See log for details.");
                                break;
                            case AutoFocusFailureMode.InitialHfrFailed:
                                Logger.Warning(DescribeHfrValidationFailure("initial position", primaryRegionState.RegionIndex, primaryRegionState.InitialHFR, primaryRegionState.InitialHFRSubMeasurements, autoFocusState.Options.FramesPerPoint));
                                Notification.ShowWarning($"Failed assessing HFR at the initial position (Region {primaryRegionState.RegionIndex}). See log for details.");
                                break;
                            case AutoFocusFailureMode.HfrRegression:
                                Logger.Warning($"New focus point HFR {primaryRegionState.FinalHFR?.Measure} is significantly worse than original HFR {primaryRegionState.InitialHFR?.Measure}");
                                Notification.ShowWarning(string.Format(Loc.Instance["LblAutoFocusNewWorseThanOriginal"], primaryRegionState.FinalHFR?.Measure, primaryRegionState.InitialHFR?.Measure));
                                autoFocusState.LastCalculatedFocusPoint = calculatedFocusPoint;
                                break;
                        }
                        return false;
                    }
                }
            }
            return true;
        }

        // Options-aware wrapper used by InitializeState: honors UseExactImagingFilter by moving the wheel to
        // exactly the requested imaging filter (no designated-AF-filter substitution). A ChangeFilter failure
        // propagates so the caller's existing error handling surfaces it — an explicit target filter must never
        // silently fall back to a different filter. Internal for direct unit testing (InternalsVisibleTo).
        internal async Task<FilterInfo> SetAutofocusFilter(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (options.UseExactImagingFilter && imagingFilter != null) {
                await filterWheelMediator.ChangeFilter(imagingFilter, token, progress);
                return imagingFilter;
            }
            return await SetAutofocusFilter(imagingFilter, token, progress);
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

        // 0 = free, 1 = an AutoFocus is in progress. Static so it is shared across all engine instances and entry
        // points (manual AF, Inspector analyze, optimizer live attempt). Mutated only via Interlocked so the
        // check-and-set is atomic and two RunImpl entrants cannot both claim it (F11).
        private static int autoFocusInProgress = 0;

        public bool AutoFocusInProgress => Volatile.Read(ref autoFocusInProgress) != 0;

        // Returns true iff this caller transitioned the guard from free->in-progress (i.e. it now owns the run).
        internal static bool TryClaimAutoFocusInProgress() => Interlocked.CompareExchange(ref autoFocusInProgress, 1, 0) == 0;

        // Releases the guard unconditionally (idempotent).
        internal static void ReleaseAutoFocusInProgress() => Interlocked.Exchange(ref autoFocusInProgress, 0);

        // Test hook: force the process-wide guard back to free so a leaked flag cannot pollute other tests.
        internal static void ResetAutoFocusInProgressForTests() => Interlocked.Exchange(ref autoFocusInProgress, 0);

        /// <summary>
        /// The fixed focuser positions a non-convergent capture sweep visits, centered on <paramref name="initial"/>
        /// (the user's rough-focus position) and spanning ±<paramref name="offsetSteps"/> steps of
        /// <paramref name="stepSize"/>. Returns <c>2*offsetSteps + 1</c> positions in DESCENDING order so the sweep
        /// approaches every point from the same direction (matching the blind sweep's single-direction backlash
        /// handling). Unlike the blind trend-walk this list is fixed up front — no star detection is required to
        /// decide where to step — so a starless field still yields a full, loadable set of saved frames.
        /// </summary>
        // Tags the fixed sweep's own per-point progress reports (focuser position + frame count).
        internal const string LiveSweepProgressSource = "HocusFocus.LiveSweep";

        // Tags the fixed sweep's synthesized per-exposure countdown (Progress = elapsed seconds, MaxProgress = total).
        // NINA's ImagingVM.CaptureImage drops the IProgress we hand it (it reports to its own status bar), so the sweep
        // has to generate this countdown itself rather than relying on the camera's reports reaching the wizard.
        internal const string LiveSweepExposureSource = "HocusFocus.LiveSweep.Exposure";

        internal static IReadOnlyList<int> ComputeSweepPositions(int initial, int offsetSteps, int stepSize) {
            if (offsetSteps < 1) {
                throw new ArgumentOutOfRangeException(nameof(offsetSteps), offsetSteps, "offsetSteps must be at least 1 so the sweep visits at least 3 positions.");
            }
            if (stepSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(stepSize), stepSize, "stepSize must be positive.");
            }

            var positions = new List<int>(2 * offsetSteps + 1);
            for (int i = offsetSteps; i >= -offsetSteps; i--) {
                positions.Add(initial + i * stepSize);
            }
            return positions;
        }

        // Behavior A pure helpers for the final-fit symmetric window. Kept static and side-effect-free (mirroring
        // ComputeSweepPositions) so the window membership and partition logic can be unit-tested without a full sweep
        // harness. STRICT bounds per spec: a point exactly on minimum ± (offsetSteps+0.5)*stepSize is treated as outside.
        internal static bool IsWithinFocusWindow(double position, double minimumX, int offsetSteps, int stepSize) {
            return position > minimumX - (offsetSteps + 0.5) * stepSize
                && position < minimumX + (offsetSteps + 0.5) * stepSize;
        }

        internal static (List<ScatterErrorPoint> included, List<ScatterErrorPoint> excluded) PartitionByFocusWindow(
            IReadOnlyList<ScatterErrorPoint> points, double minimumX, int offsetSteps, int stepSize) {
            var included = new List<ScatterErrorPoint>();
            var excluded = new List<ScatterErrorPoint>();
            foreach (var point in points) {
                if (IsWithinFocusWindow(point.X, minimumX, offsetSteps, stepSize)) {
                    included.Add(point);
                } else {
                    excluded.Add(point);
                }
            }
            return (included, excluded);
        }

        private async Task<AutoFocusResult> RunImpl(AutoFocusEngineOptions options, FilterInfo imagingFilter, List<StarDetectionRegion> regions, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (!TryClaimAutoFocusInProgress()) {
                Notification.ShowError("Another AutoFocus is already in progress");
                Logger.Error("Another AutoFocus is already in progress");
                return null;
            }

            // Once the claim succeeds, EVERY subsequent path must release the static guard, otherwise the flag stays
            // set and EVERY future AutoFocus across the whole app is rejected until NINA restarts (the F11 leak). The
            // statements between the claim and the inner try below are throw-prone (OnStarted() raises the Started
            // event synchronously into subscribers that do real work; the CancellationTokenSource ctor throws for an
            // out-of-range AutoFocusTimeout), so the release lives in this OUTER finally rather than the inner one.
            try {
                Logger.Trace("Starting Autofocus");
                OnStarted();

                var timeoutCts = new CancellationTokenSource(options.AutoFocusTimeout);
                bool tempComp = false;
                bool guidingStopped = false;
                bool completed = false;
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
                    // Never let a live-display annotation outlive the run: a late SetImage would paint an auto focus frame
                    // over whatever image is shown next. Each task already swallows its own exceptions.
                    if (autoFocusState != null) {
                        try {
                            await Task.WhenAll(autoFocusState.SnapshotDisplayAnnotationTasks());
                        } catch (Exception ex) {
                            Logger.Warning($"Failure draining auto focus display annotations. {ex.Message}");
                        }
                    }

                    try {
                        await PerformPostAutoFocusActions(
                            successfulAutoFocus: completed, initialFocusPosition: autoFocusState?.InitialFocuserPosition, imagingFilter: imagingFilter, restoreTempComp: tempComp,
                            restoreGuiding: guidingStopped, progress: progress);
                    } catch (Exception ex) {
                        Logger.Warning($"Failure during post AF actions. {ex.Message}");
                    } finally {
                        // progress is optional (the Star Detection Optimizer's live attempt passes null), so report
                        // through it defensively. The static in-progress guard is released in the OUTER finally below
                        // so that a throw from OnStarted() or the CancellationTokenSource ctor above this inner try
                        // cannot leak it.
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
                        RejectedPoints = rs.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray(),
                        WindowExcludedPoints = rs.WindowExcludedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
                    }).OrderBy(r => r.RegionIndex).ToArray(),
                    SaveFolder = autoFocusState.SaveFolder
                };
            } finally {
                // Release the static guard on EVERY path after a successful claim — including a throw from OnStarted()
                // or the CancellationTokenSource construction above the inner try, and the normal return paths above.
                // Releasing here (after all inner cleanup) holds the guard until the run is fully torn down and can
                // never leak (the F11 leak-window fix). A bare finally does not swallow the in-flight exception or
                // alter the returned value.
                ReleaseAutoFocusInProgress();
            }
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

        public Task<AutoFocusResult> CaptureFixedSweepAsync(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            return CaptureFixedSweepImpl(options, imagingFilter, token, progress);
        }

        // A capture-only sibling of RunImpl. It reuses the exact same run scaffolding — the process-wide in-progress
        // guard, temp-comp/guiding suspend+restore, filter change, save-folder setup, and focuser restore — but swaps
        // the convergent trend-walk (StartBlindFocusPoints, with its initial-HFR gate and curve-fit validation that
        // both throw on a starless field) for a fixed sweep that just captures and saves. That decoupling is the whole
        // point: capture succeeds even when detection can't, and the optimizer searches the saved frames afterward.
        private async Task<AutoFocusResult> CaptureFixedSweepImpl(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            // A sweep that saves nothing is useless — fail fast (and loudly) rather than moving the focuser and
            // discarding every frame. InitializeSave only warns and no-ops on a missing path, so guard here too.
            if (!options.Save || string.IsNullOrWhiteSpace(options.SavePath) || !Directory.Exists(options.SavePath)) {
                Notification.ShowError("Choose an existing folder to save the captured frames before running a live sweep.");
                Logger.Error($"Fixed-sweep capture aborted: invalid save path '{options.SavePath}' (Save={options.Save}).");
                return new AutoFocusResult() { Succeeded = false, SaveFolder = null };
            }

            // Validate the sweep geometry up front (before InitializeSave creates a folder), so a misconfigured profile
            // fails with a clear message instead of an empty save folder that later trips the "attemptXX" loader error.
            if (options.AutoFocusInitialOffsetSteps < 1 || options.AutoFocusStepSize <= 0) {
                Notification.ShowError("Set a positive auto-focus step size and at least one offset step before running a live sweep.");
                Logger.Error($"Fixed-sweep capture aborted: offsetSteps={options.AutoFocusInitialOffsetSteps}, stepSize={options.AutoFocusStepSize}.");
                return new AutoFocusResult() { Succeeded = false, SaveFolder = null };
            }

            if (!TryClaimAutoFocusInProgress()) {
                Notification.ShowError("Another AutoFocus is already in progress");
                Logger.Error("Another AutoFocus is already in progress");
                return null;
            }

            // Mirror RunImpl's guard discipline: once claimed, EVERY path must release the static guard (the OUTER
            // finally), because OnStarted() and the CancellationTokenSource ctor below can throw before the inner try.
            try {
                Logger.Trace("Starting fixed-sweep capture");
                OnStarted();

                var timeoutCts = new CancellationTokenSource(options.AutoFocusTimeout);
                bool tempComp = false;
                bool guidingStopped = false;
                bool completed = false;
                AutoFocusState autoFocusState = null;
                try {
                    if (focuserMediator.GetInfo().TempCompAvailable && focuserMediator.GetInfo().TempComp) {
                        tempComp = true;
                        focuserMediator.ToggleTempComp(false);
                    }

                    if (profileService.ActiveProfile.FocuserSettings.AutoFocusDisableGuiding) {
                        guidingStopped = await this.guiderMediator.StopGuiding(token);
                    }

                    var sweepCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
                    autoFocusState = await InitializeState(options, imagingFilter, null, sweepCts.Token, progress);
                    completed = await RunFixedSweep(autoFocusState, sweepCts.Token, progress);
                } catch (OperationCanceledException) {
                    if (timeoutCts.IsCancellationRequested) {
                        Notification.ShowWarning($"Live sweep timed out after {options.AutoFocusTimeout}");
                        Logger.Warning($"Live sweep timed out after {options.AutoFocusTimeout}");
                    } else {
                        Logger.Warning("Live sweep cancelled");
                    }
                } catch (Exception ex) {
                    Notification.ShowError($"Live sweep failure. {ex.Message}");
                    Logger.Error("Failure during live sweep", ex);
                } finally {
                    if (autoFocusState != null) {
                        try {
                            await Task.WhenAll(autoFocusState.SnapshotDisplayAnnotationTasks());
                        } catch (Exception ex) {
                            Logger.Warning($"Failure draining auto focus display annotations. {ex.Message}");
                        }
                    }

                    try {
                        // successfulAutoFocus: completed — on success RunFixedSweep already restored the focuser to the
                        // rough-focus start (so this skips the move); on cancel/failure completed is false, so this
                        // restores the focuser from wherever the sweep left it. Filter/temp-comp/guiding restore either way.
                        await PerformPostAutoFocusActions(
                            successfulAutoFocus: completed, initialFocusPosition: autoFocusState?.InitialFocuserPosition, imagingFilter: imagingFilter, restoreTempComp: tempComp,
                            restoreGuiding: guidingStopped, progress: progress);
                    } catch (Exception ex) {
                        Logger.Warning($"Failure during post AF actions. {ex.Message}");
                    } finally {
                        progress?.Report(new ApplicationStatus() { Status = string.Empty });
                    }
                }

                if (autoFocusState == null) {
                    return null;
                }

                return new AutoFocusResult() {
                    Succeeded = completed,
                    InitialFocuserPosition = autoFocusState.InitialFocuserPosition,
                    ImageSize = autoFocusState.ImageSize,
                    StepSize = autoFocusState.Options.AutoFocusStepSize,
                    SaveFolder = autoFocusState.SaveFolder
                };
            } finally {
                ReleaseAutoFocusInProgress();
            }
        }

        // The non-convergent sweep. Captures FramesPerPoint frames at each of the 2N+1 fixed positions and saves them
        // into attempt01 (via OnNextAttempt), so the folder loads back exactly like any saved run. No trend logic, no
        // stopping condition, no curve fit — a starless frame is saved just the same.
        private async Task<bool> RunFixedSweep(AutoFocusState state, CancellationToken token, IProgress<ApplicationStatus> progress) {
            using (var stopWatch = MyStopWatch.Measure()) {
                InitializeSave(state);

                var initialFocusPosition = focuserMediator.GetInfo().Position;
                state.InitialFocuserPosition = initialFocusPosition;
                Logger.Info($"Starting fixed-sweep capture centered on position {initialFocusPosition}");

                // AttemptNumber 0 -> 1 so frames land in an attempt01 subfolder (LoadSavedAutoFocusAttempt requires
                // exactly one attempt* folder; the 'initial' folder AttemptNumber 0 would use does not match).
                state.OnNextAttempt();
                OnIterationStarted(state.AttemptNumber);

                var offsetSteps = state.Options.AutoFocusInitialOffsetSteps;
                var stepSize = state.Options.AutoFocusStepSize;
                var positions = ComputeSweepPositions(initialFocusPosition, offsetSteps, stepSize);

                var framesPerPoint = Math.Max(1, state.Options.FramesPerPoint);
                var totalFrames = positions.Count * framesPerPoint;
                var capturedFrames = 0;

                // Overshoot one extra step beyond the high extreme with NO capture, then step monotonically down
                // through the positions, so every captured point is reached by a decreasing move — the same
                // single-direction approach the blind sweep uses to keep backlash consistent (StartBlindFocusPoints).
                await focuserMediator.MoveFocuser(initialFocusPosition + (offsetSteps + 1) * stepSize, token);

                foreach (var targetPosition in positions) {
                    token.ThrowIfCancellationRequested();
                    var actualPosition = await focuserMediator.MoveFocuser(targetPosition, token);

                    // Report the capture context (focuser position + frame count) for the wizard's live readout; the
                    // camera's own exposure-progress reports flow through the same progress during StartAutoFocusPoint.
                    progress?.Report(new ApplicationStatus() {
                        Source = LiveSweepProgressSource,
                        Status = framesPerPoint > 1
                            ? $"Capturing frames {capturedFrames + 1}–{capturedFrames + framesPerPoint} of {totalFrames} at focuser position {actualPosition}"
                            : $"Capturing frame {capturedFrames + 1} of {totalFrames} at focuser position {actualPosition}",
                        Progress = capturedFrames,
                        MaxProgress = totalFrames
                    });

                    await StartAutoFocusPoint(actualPosition, state, action: null, finalValidation: false, token, progress, captureOnly: true);
                    capturedFrames += framesPerPoint;
                }

                Logger.Info("Waiting on fixed-sweep analysis tasks");
                await Task.WhenAll(state.AnalysisTasks);
                token.ThrowIfCancellationRequested();

                // The sweep computes no new focus point, so return the focuser to the user's rough-focus position.
                await focuserMediator.MoveFocuser(initialFocusPosition, token);

                OnCompleted(state, focuserMediator.GetInfo().Temperature, stopWatch.Elapsed);
                return true;
            }
        }

        // Wraps a capture-only exposure with a synthesized countdown reported to the wizard (LiveSweepExposureSource),
        // because NINA's ImagingVM.CaptureImage drops the IProgress we pass and reports the camera countdown elsewhere.
        private async Task<IExposureData> TakeExposureWithLiveCountdown(AutoFocusState state, int focuserPosition, CancellationToken token, IProgress<ApplicationStatus> progress) {
            var totalSeconds = ResolveSweepExposureSeconds(state);
            if (progress == null || totalSeconds <= 0) {
                return await TakeExposure(state, focuserPosition, token, progress);
            }

            using (var countdownCts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                var countdown = RunExposureCountdown(progress, totalSeconds, countdownCts.Token);
                try {
                    return await TakeExposure(state, focuserPosition, token, progress);
                } finally {
                    countdownCts.Cancel();
                    try { await countdown; } catch { /* best-effort readout */ }
                }
            }
        }

        // Reports elapsed / total seconds every quarter second until cancelled (when the exposure completes).
        private static async Task RunExposureCountdown(IProgress<ApplicationStatus> progress, double totalSeconds, CancellationToken token) {
            var maxProgress = Math.Max(1, (int)Math.Ceiling(totalSeconds));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try {
                while (!token.IsCancellationRequested) {
                    var elapsed = Math.Min(totalSeconds, stopwatch.Elapsed.TotalSeconds);
                    progress.Report(new ApplicationStatus() {
                        Source = LiveSweepExposureSource,
                        Progress = elapsed,
                        MaxProgress = maxProgress
                    });
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
            }
        }

        // The exposure the sweep uses: the wizard's per-run override when set, else the profile's AF exposure.
        private double ResolveSweepExposureSeconds(AutoFocusState state) {
            if (state.Options.OverrideAutoFocusExposureTime > TimeSpan.Zero) {
                return state.Options.OverrideAutoFocusExposureTime.TotalSeconds;
            }
            return profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime;
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
            progress?.Report(new ApplicationStatus() {
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
                                progress?.Report(new ApplicationStatus() {
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
                    // Behavior A: window each region around its own fitted vertex and refit before model selection.
                    regionState.ApplyFinalSymmetricWindow(state.Options.AutoFocusInitialOffsetSteps, state.Options.AutoFocusStepSize);
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
                        RejectedPoints = rs.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray(),
                        WindowExcludedPoints = rs.WindowExcludedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
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
                progress?.Report(new ApplicationStatus());
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
                RejectedPoints = regionState.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray(),
                WindowExcludedPoints = regionState.WindowExcludedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
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
        // Serializes the metadata to <saveFolder>/metadata.json. Isolated + internal so the file-write contract can be
        // unit-tested without standing up the whole engine.
        internal static void WriteMetadataFile(string saveFolder, AutoFocusReplayMetadata metadata) {
            var metadataPath = Path.Combine(saveFolder, "metadata.json");
            File.WriteAllText(metadataPath, metadata.Serialize());
            Logger.Info($"Wrote AutoFocus replay metadata to {metadataPath}");
        }

        private void WriteReplayMetadata(AutoFocusState state, bool succeeded, string failureReason) {
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
                    EstimatedFinalFocuserPosition = rs.FinalFocusPoint?.X,
                    EstimatedFinalHFR = rs.FinalFocusPoint?.Y,
                    FinalHFR = rs.FinalHFR?.Measure,
                    InitialHFR = rs.InitialHFR?.Measure,
                    RSquared = rs.Fittings?.HyperbolicFitting?.RSquared,
                    SelectedHyperbolicFitModel = rs.Fittings?.SelectedHyperbolicFitModel
                }).ToList();

                var pluginVersion = typeof(AutoFocusEngine).Assembly.GetName().Version?.ToString();
                var metadata = AutoFocusReplayMetadataBuilder.Build(
                    sdOptions, state.Options, regionGeometry, results, DateTime.UtcNow, pluginVersion, StarDetector.StarDetectorVersion,
                    succeeded: succeeded, failureReason: failureReason);

                WriteMetadataFile(state.SaveFolder, metadata);
            } catch (Exception e) {
                Logger.Warning($"Failed to write AutoFocus replay metadata.json: {e.Message}");
            }
        }

        // Short human-readable reason for the metadata.json failure record, derived from the validation failure mode.
        // When the failure is attributable to a single region (the HFR-improvement checks), its index is appended so
        // the replay record is self-describing; appended only when non-null to stay backward-compatible.
        private static string FailureReasonText(AutoFocusFailureMode mode, int? regionIndex = null) {
            string baseText;
            switch (mode) {
                case AutoFocusFailureMode.HfrRegression:
                    baseText = "Final HFR worse than original";
                    break;
                case AutoFocusFailureMode.FinalPointOutOfBounds:
                    baseText = "Calculated focus point outside the swept range";
                    break;
                case AutoFocusFailureMode.FitQuality:
                    baseText = "Fit/data quality rejected (low R²/χ² or insufficient stars)";
                    break;
                case AutoFocusFailureMode.InitialHfrFailed:
                    baseText = "Initial HFR measurement failed";
                    break;
                case AutoFocusFailureMode.FinalHfrMissing:
                    baseText = "Final HFR measurement failed";
                    break;
                default:
                    baseText = "AutoFocus failed";
                    break;
            }
            return regionIndex.HasValue ? $"{baseText}; Region {regionIndex.Value}" : baseText;
        }

        private void OnCompleted(
            AutoFocusState state,
            double temperature,
            TimeSpan duration) {
            WriteReplayMetadata(state, succeeded: true, failureReason: null);
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
                    RejectedPoints = s.RejectedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray(),
                    WindowExcludedPoints = s.WindowExcludedPoints.Select(p => new AutoFocusRegionPoint() { FocuserPosition = p.Key, Measurement = p.Value }).ToArray()
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
            // A failed run is still saved with a metadata.json (flagged as failed) so it can be inspected/replayed —
            // unlike OnIterationFailed, which is a mid-run retry boundary, this is the terminal failure.
            WriteReplayMetadata(state, succeeded: false, failureReason: FailureReasonText(state.LastFailureMode, state.LastFailureRegionIndex));
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
                MaxBlindStepsPerDirection = autoFocusOptions.MaxBlindStepsPerDirection,
                MaxOutlierRejections = autoFocusOptions.MaxOutlierRejections,
                OutlierRejectionConfidence = autoFocusOptions.OutlierRejectionConfidence,
                WeightedHyperbolicFitEnabled = autoFocusOptions.WeightedHyperbolicFitEnabled,
                HyperbolicFitModel = autoFocusOptions.HyperbolicFitModel,
                FitRejectionCriterion = autoFocusOptions.FitRejectionCriterion,
                ReducedChiSquaredRejectionThreshold = autoFocusOptions.ReducedChiSquaredRejectionThreshold,
                // Always-on internal behavior (Behavior A). Hard-wired true; the DTO flag is a test seam only, with
                // no persisted option or UI. Nothing consumes it yet at this checkpoint.
                SymmetricFocusWindowEnabled = true,
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