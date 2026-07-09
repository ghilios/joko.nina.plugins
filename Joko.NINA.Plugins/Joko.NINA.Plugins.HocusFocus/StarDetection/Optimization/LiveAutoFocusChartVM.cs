#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// The auto-focus curve streamed live while the wizard's Live source runs a fresh auto-focus. It mirrors the
    /// chart half of <see cref="AutoFocus.HocusFocusVM"/> — the same measured points, connecting line, rejected
    /// overlay, fitted curve and final-point σ(focus) bar — with none of its collaborators: no mediators, no
    /// report generation, no focuser broadcast, no frame-review retention. The wizard owns its own
    /// <see cref="IAutoFocusEngine"/> instance, so those side effects belong to the AF pane's run, not this one.
    ///
    /// <para>Unlike the AF pane, only the ONE fit the user's profile actually uses is exposed
    /// (<see cref="CurveFitting"/>, plus the two trend lines when the curve-fitting type is trend-based),
    /// selected exactly as <see cref="AutoFocus.InspectorVM"/> does. The pane instead renders all four fits and
    /// hides the inactive ones by routing every annotation's colour through AFFittingToColorConverter; picking the
    /// active fit here keeps the chart template as small as the sibling OptimizationCurve one.</para>
    ///
    /// <para>Threading: the engine raises its events on worker threads. The four collections are
    /// <see cref="AsyncObservableCollection{T}"/>, which marshals CollectionChanged to the dispatcher — but only
    /// to the dispatcher captured when the collection is CONSTRUCTED. This VM must therefore be constructed on the
    /// UI thread. It is: StartCommand is an AsyncRelayCommand and the StartAsync → AcquireAsync →
    /// RunLiveAttemptAsync chain is UI-affine throughout (ConfigureAwait(true), no Task.Run before construction).
    /// Wrapping acquisition in a Task.Run would silently break this.</para>
    /// </summary>
    public sealed class LiveAutoFocusChartVM : BaseINPC, IDisposable {
        private static readonly FocusPointComparer focusPointComparer = new FocusPointComparer();
        private static readonly PlotPointComparer plotPointComparer = new PlotPointComparer();

        /// <summary>Sentinel for "no final focus point yet" — off the left of any real focuser range, so the
        /// diamond annotation sits outside the plotted data (mirrors InspectorVM.ClearPlots).</summary>
        private static readonly DataPoint NoFinalFocusPoint = new DataPoint(-1.0d, 0.0d);

        private IAutoFocusEngine engine;

        /// <summary>Measured HFR per focuser position, with the star-ensemble scatter as a vertical error bar.</summary>
        public AsyncObservableCollection<ScatterErrorPoint> FocusPoints { get; } = new AsyncObservableCollection<ScatterErrorPoint>();

        /// <summary>The same points as a plain line series, connecting the measurements in focuser order.</summary>
        public AsyncObservableCollection<DataPoint> PlotFocusPoints { get; } = new AsyncObservableCollection<DataPoint>();

        /// <summary>Points the outlier rejection dropped from the fit, drawn as crosses.</summary>
        public AsyncObservableCollection<ScatterPoint> PlotRejectedFocusPoints { get; } = new AsyncObservableCollection<ScatterPoint>();

        /// <summary>A single point at the fitted best focus, carrying σ(focus) as a horizontal error bar. Empty
        /// until the run completes, and for fits that cannot estimate the uncertainty.</summary>
        public AsyncObservableCollection<ScatterErrorPoint> PlotFinalFocusPointWithError { get; } = new AsyncObservableCollection<ScatterErrorPoint>();

        private Func<double, double> curveFitting;

        /// <summary>The fitted curve (focuser position → HFR) for the profile's auto-focus method and curve-fitting
        /// type. Null before enough points exist to fit, and for a pure-trendlines profile, which has no curve.</summary>
        public Func<double, double> CurveFitting {
            get => curveFitting;
            private set { curveFitting = value; RaisePropertyChanged(); }
        }

        private Func<double, double> leftTrendFitting;

        /// <summary>The left-hand trend line as a function, or null when the profile's curve fitting is not
        /// trend-based. Exposed as a function rather than as slope/offset bindings on a LineAnnotation because a
        /// null TrendlineFitting would bind slope=0, offset=0 and draw a spurious horizontal line at y=0, whereas
        /// a FunctionAnnotation with a null Equation draws nothing.</summary>
        public Func<double, double> LeftTrendFitting {
            get => leftTrendFitting;
            private set { leftTrendFitting = value; RaisePropertyChanged(); }
        }

        private Func<double, double> rightTrendFitting;

        /// <summary>The right-hand trend line as a function. See <see cref="LeftTrendFitting"/>.</summary>
        public Func<double, double> RightTrendFitting {
            get => rightTrendFitting;
            private set { rightTrendFitting = value; RaisePropertyChanged(); }
        }

        private DataPoint finalFocusPoint = NoFinalFocusPoint;

        /// <summary>The calculated best-focus position and its estimated HFR, set when the run finishes.</summary>
        public DataPoint FinalFocusPoint {
            get => finalFocusPoint;
            private set { finalFocusPoint = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// Subscribes to <paramref name="engine"/> and starts streaming its curve. Idempotent: attaching twice
        /// detaches first, so the handlers are never registered more than once. Clears any prior run's points.
        /// </summary>
        public void Attach(IAutoFocusEngine engine) {
            if (engine == null) {
                return;
            }
            Detach();
            this.engine = engine;
            Clear();
            engine.Started += OnStarted;
            engine.MeasurementPointCompleted += OnMeasurementPointCompleted;
            engine.IterationFailed += OnIterationFailed;
            engine.Completed += OnCompleted;
            engine.Failed += OnFailed;
        }

        /// <summary>Unsubscribes from the engine. Idempotent, and safe to call from a finally on any exit path —
        /// without it the engine would keep this VM (and the wizard) alive through its event handlers.</summary>
        public void Detach() {
            if (engine == null) {
                return;
            }
            engine.Started -= OnStarted;
            engine.MeasurementPointCompleted -= OnMeasurementPointCompleted;
            engine.IterationFailed -= OnIterationFailed;
            engine.Completed -= OnCompleted;
            engine.Failed -= OnFailed;
            engine = null;
        }

        public void Dispose() => Detach();

        /// <summary>Resets the chart to its empty state.</summary>
        public void Clear() {
            ClearSeries();
            CurveFitting = null;
            LeftTrendFitting = null;
            RightTrendFitting = null;
        }

        private void ClearSeries() {
            FocusPoints.Clear();
            PlotFocusPoints.Clear();
            PlotRejectedFocusPoints.Clear();
            PlotFinalFocusPointWithError.Clear();
            FinalFocusPoint = NoFinalFocusPoint;
        }

        private void OnStarted(object sender, AutoFocusStartedEventArgs e) => Clear();

        /// <summary>Each completed focuser position: append the measurement, rebuild the rejected overlay from the
        /// step's current outlier set, and re-point the fit to the intermediate curve. Region 0 is the only region
        /// the wizard's plain Run() produces; the guard mirrors the AF pane.</summary>
        private void OnMeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            if (e.RegionIndex != 0) {
                return;
            }

            FocusPoints.AddSorted(new ScatterErrorPoint(e.FocuserPosition, e.Measurement.Measure, 0, AutoFocusEngine.SafeDisplayError(e.Measurement.Stdev)), focusPointComparer);
            PlotFocusPoints.AddSorted(new DataPoint(e.FocuserPosition, e.Measurement.Measure), plotPointComparer);
            SyncRejectedPoints(e.RejectedPoints);
            SyncFittings(e.Fittings);
        }

        /// <summary>An attempt failed and the engine is about to retry: wipe the plot so the next attempt's points
        /// aren't drawn on top of the abandoned ones (mirrors the AF pane).</summary>
        private void OnIterationFailed(object sender, AutoFocusFailedEventArgs e) => ClearSeries();

        private void OnCompleted(object sender, AutoFocusCompletedEventArgs e) => SyncFinalState(e);

        private void OnFailed(object sender, AutoFocusFailedEventArgs e) => SyncFinalState(e);

        /// <summary>
        /// Re-syncs the chart to the run's FINALIZED region fit, which carries the values the per-point
        /// intermediate fit lacks (the selected hyperbolic model, σ(focus), leave-one-out stability) and the
        /// model-fair consensus rejected set. A failed run can finish with no regions at all, so guard for it.
        /// </summary>
        private void SyncFinalState(AutoFocusFinishedEventArgsBase e) {
            if (e.RegionHFRs == null || e.RegionHFRs.Count == 0) {
                return;
            }

            var firstRegion = e.RegionHFRs[0];
            FinalFocusPoint = new DataPoint(firstRegion.EstimatedFinalFocuserPosition, firstRegion.EstimatedFinalHFR);
            SyncFittings(firstRegion.Fittings);
            SyncRejectedPoints(firstRegion.RejectedPoints);
            RefreshFinalFocusPointError(firstRegion.Fittings);
        }

        private void SyncRejectedPoints(AutoFocusRegionPoint[] rejectedPoints) {
            PlotRejectedFocusPoints.Clear();
            if (rejectedPoints == null) {
                return;
            }
            foreach (var rejected in rejectedPoints) {
                PlotRejectedFocusPoints.Add(new ScatterPoint(rejected.FocuserPosition, rejected.Measurement.Measure));
            }
        }

        private void SyncFittings(AutoFocusFitting fittings) {
            CurveFitting = GetCurveFitting(fittings);
            LeftTrendFitting = GetTrendFitting(fittings, left: true);
            RightTrendFitting = GetTrendFitting(fittings, left: false);
        }

        /// <summary>
        /// Rebuilds <see cref="PlotFinalFocusPointWithError"/> from the finalized hyperbolic fit: one point at the
        /// best-focus minimum with σ(focus) as a horizontal error bar, falling back to the leave-one-out stability
        /// when σ(focus) could not be propagated. Leaves the series empty — drawing nothing — for non-hyperbolic
        /// fits and when neither uncertainty estimate exists. Mirrors HocusFocusVM.RefreshFinalFocusPointError.
        /// </summary>
        private void RefreshFinalFocusPointError(AutoFocusFitting fittings) {
            PlotFinalFocusPointWithError.Clear();
            if (!(fittings?.HyperbolicFitting is AlglibHyperbolicFitting alglibFit)) {
                return;
            }
            var minimum = alglibFit.Minimum;
            if (!double.IsFinite(minimum.X)) {
                return;
            }
            var errorX = alglibFit.MinimumStdError;
            if (!double.IsFinite(errorX) || errorX <= 0.0) {
                errorX = alglibFit.LeaveOneOutStdError; // σ(focus) unavailable (degenerate fit) → use LOO stability
            }
            if (!double.IsFinite(errorX) || errorX <= 0.0) {
                return;
            }
            PlotFinalFocusPointWithError.Add(new ScatterErrorPoint(minimum.X, minimum.Y, errorX, 0.0));
        }

        /// <summary>The one fitted curve the profile's method + curve-fitting type actually uses. Mirrors
        /// InspectorVM.GetCurveFitting; a pure-trendlines profile has no curve, only the two trend lines.</summary>
        private static Func<double, double> GetCurveFitting(AutoFocusFitting fittings) {
            if (fittings == null) {
                return null;
            }
            if (fittings.Method == AFMethodEnum.CONTRASTDETECTION) {
                return fittings.GaussianFitting?.Fitting;
            }
            if (fittings.CurveFittingType == AFCurveFittingEnum.PARABOLIC || fittings.CurveFittingType == AFCurveFittingEnum.TRENDPARABOLIC) {
                return fittings.QuadraticFitting?.Fitting;
            }
            if (fittings.CurveFittingType == AFCurveFittingEnum.HYPERBOLIC || fittings.CurveFittingType == AFCurveFittingEnum.TRENDHYPERBOLIC) {
                return fittings.HyperbolicFitting?.Fitting;
            }
            return null;
        }

        /// <summary>One side's trend line as y = slope·x + offset, or null when the profile's curve fitting is not
        /// trend-based (the gate InspectorVM.GetLineFitting applies).</summary>
        private static Func<double, double> GetTrendFitting(AutoFocusFitting fittings, bool left) {
            if (fittings == null || fittings.Method != AFMethodEnum.STARHFR) {
                return null;
            }
            if (fittings.CurveFittingType != AFCurveFittingEnum.TRENDLINES
                && fittings.CurveFittingType != AFCurveFittingEnum.TRENDPARABOLIC
                && fittings.CurveFittingType != AFCurveFittingEnum.TRENDHYPERBOLIC) {
                return null;
            }
            var trend = left ? fittings.TrendlineFitting?.LeftTrend : fittings.TrendlineFitting?.RightTrend;
            if (trend == null) {
                return null;
            }
            var slope = trend.Slope;
            var offset = trend.Offset;
            if (!double.IsFinite(slope) || !double.IsFinite(offset)) {
                return null;
            }
            return x => slope * x + offset;
        }
    }
}
