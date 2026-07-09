#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NSubstitute;
using NUnit.Framework;
using OxyPlot;
using System;
using System.Collections.Immutable;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for the wizard's live auto-focus chart: which engine events it plots, how it selects the one fit the
/// profile uses, how it finalizes the best-focus point + σ(focus) bar, and that its subscription lifecycle is
/// symmetric. The engine is an NSubstitute double whose events are raised directly — no hardware, no images.
/// </summary>
[TestFixture]
public class LiveAutoFocusChartVMTests {

    /// <summary>A hyperbolic fit with a preset minimum and uncertainty, so the final-point error bar can be
    /// exercised without running a real solve (a real fit cannot be made to report a non-finite σ(focus)).</summary>
    private sealed class StubHyperbolicFit : AlglibHyperbolicFitting {

        public StubHyperbolicFit(DataPoint minimum, double minimumStdError, double leaveOneOutStdError) {
            Minimum = minimum;
            MinimumStdError = minimumStdError;
            LeaveOneOutStdError = leaveOneOutStdError;
            Fitting = x => x;
        }

        protected override int ParameterCount => 4;

        protected override bool TryComputeInitialState(out double[] initialGuess, out double[] lowerBounds, out double[] upperBounds, out double[] scale) {
            initialGuess = lowerBounds = upperBounds = scale = null;
            return false;
        }

        protected override double ModelValue(double[] parameters, double x) => 0.0;

        protected override DataPoint ComputeMinimum(double[] parameters) => Minimum;

        protected override string FormatExpression(double[] parameters) => string.Empty;
    }

    private static AutoFocusFitting Fittings(
        AFCurveFittingEnum curveFitting,
        AFMethodEnum method = AFMethodEnum.STARHFR,
        HyperbolicFitting hyperbolic = null) =>
        new AutoFocusFitting {
            Method = method,
            CurveFittingType = curveFitting,
            HyperbolicFitting = hyperbolic ?? new StubHyperbolicFit(new DataPoint(10000, 1.5), 12.0, 8.0),
            QuadraticFitting = new QuadraticFitting { Fitting = x => x },
            GaussianFitting = new GaussianFitting { Fitting = x => x },
            // Two real trend lines, as the engine's CurveFittingResult produces them.
            TrendlineFitting = new TrendlineFitting().Calculate(
                new[] {
                    new OxyPlot.Series.ScatterErrorPoint(9800, 3.0, 0, 0.1),
                    new OxyPlot.Series.ScatterErrorPoint(9900, 2.0, 0, 0.1),
                    new OxyPlot.Series.ScatterErrorPoint(10000, 1.5, 0, 0.1),
                    new OxyPlot.Series.ScatterErrorPoint(10100, 2.0, 0, 0.1),
                    new OxyPlot.Series.ScatterErrorPoint(10200, 3.0, 0, 0.1)
                },
                AFMethodEnum.STARHFR.ToString())
        };

    private static AutoFocusMeasurementPointCompletedEventArgs Point(
        int focuserPosition,
        double hfr,
        int regionIndex = 0,
        AutoFocusFitting fittings = null,
        AutoFocusRegionPoint[] rejected = null) =>
        new AutoFocusMeasurementPointCompletedEventArgs {
            FocuserPosition = focuserPosition,
            RegionIndex = regionIndex,
            Measurement = new MeasureAndError { Measure = hfr, Stdev = 0.1 },
            Fittings = fittings ?? Fittings(AFCurveFittingEnum.TRENDHYPERBOLIC),
            RejectedPoints = rejected ?? Array.Empty<AutoFocusRegionPoint>()
        };

    private static AutoFocusRegionPoint Rejected(int focuserPosition, double hfr) =>
        new AutoFocusRegionPoint { FocuserPosition = focuserPosition, Measurement = new MeasureAndError { Measure = hfr, Stdev = 0.1 } };

    private static AutoFocusCompletedEventArgs Completed(HyperbolicFitting hyperbolic, double finalPosition = 1005, double finalHfr = 2.9) =>
        new AutoFocusCompletedEventArgs {
            RegionHFRs = ImmutableList.Create(new AutoFocusRegionHFR {
                EstimatedFinalFocuserPosition = finalPosition,
                EstimatedFinalHFR = finalHfr,
                Fittings = Fittings(AFCurveFittingEnum.HYPERBOLIC, hyperbolic: hyperbolic),
                RejectedPoints = Array.Empty<AutoFocusRegionPoint>()
            })
        };

    private static (LiveAutoFocusChartVM Chart, IAutoFocusEngine Engine) NewAttachedChart() {
        var engine = Substitute.For<IAutoFocusEngine>();
        var chart = new LiveAutoFocusChartVM();
        chart.Attach(engine);
        return (chart, engine);
    }

    [Test]
    public void MeasurementPointCompleted_Region0_AddsPointLineAndSelectsActiveFit() {
        var (chart, engine) = NewAttachedChart();

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5));

        Assert.Multiple(() => {
            Assert.That(chart.FocusPoints, Has.Count.EqualTo(1));
            Assert.That(chart.PlotFocusPoints, Has.Count.EqualTo(1));
            // TRENDHYPERBOLIC => the hyperbolic curve, plus both trend lines.
            Assert.That(chart.CurveFitting, Is.Not.Null);
            Assert.That(chart.LeftTrendFitting, Is.Not.Null);
            Assert.That(chart.RightTrendFitting, Is.Not.Null);
        });
    }

    [Test]
    public void MeasurementPointCompleted_PureHyperbolic_LeavesTrendFittingsNull() {
        var (chart, engine) = NewAttachedChart();

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5, fittings: Fittings(AFCurveFittingEnum.HYPERBOLIC)));

        Assert.Multiple(() => {
            Assert.That(chart.CurveFitting, Is.Not.Null);
            Assert.That(chart.LeftTrendFitting, Is.Null, "a non-trend profile must not draw trend lines");
            Assert.That(chart.RightTrendFitting, Is.Null);
        });
    }

    [Test]
    public void MeasurementPointCompleted_ContrastDetection_SelectsGaussianCurve() {
        var (chart, engine) = NewAttachedChart();
        var fittings = Fittings(AFCurveFittingEnum.HYPERBOLIC, method: AFMethodEnum.CONTRASTDETECTION);

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5, fittings: fittings));

        Assert.Multiple(() => {
            Assert.That(chart.CurveFitting, Is.SameAs(fittings.GaussianFitting.Fitting));
            Assert.That(chart.LeftTrendFitting, Is.Null);
        });
    }

    [Test]
    public void MeasurementPointCompleted_TrendlinesOnly_HasTrendsButNoCurve() {
        var (chart, engine) = NewAttachedChart();

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5, fittings: Fittings(AFCurveFittingEnum.TRENDLINES)));

        Assert.Multiple(() => {
            Assert.That(chart.CurveFitting, Is.Null, "a pure-trendlines profile fits no curve");
            Assert.That(chart.LeftTrendFitting, Is.Not.Null);
            Assert.That(chart.RightTrendFitting, Is.Not.Null);
        });
    }

    [Test]
    public void MeasurementPointCompleted_NonZeroRegion_IsIgnored() {
        var (chart, engine) = NewAttachedChart();

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5, regionIndex: 1));

        Assert.Multiple(() => {
            Assert.That(chart.FocusPoints, Is.Empty);
            Assert.That(chart.PlotFocusPoints, Is.Empty);
            Assert.That(chart.CurveFitting, Is.Null);
        });
    }

    [Test]
    public void MeasurementPointCompleted_RebuildsRejectedOverlayEachPoint() {
        var (chart, engine) = NewAttachedChart();

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5, rejected: new[] { Rejected(9800, 3.0), Rejected(10200, 3.1) }));
        Assert.That(chart.PlotRejectedFocusPoints, Has.Count.EqualTo(2));

        // The next point's overlay REPLACES the previous one rather than accumulating.
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10100, 1.6, rejected: new[] { Rejected(9800, 3.0) }));
        Assert.That(chart.PlotRejectedFocusPoints, Has.Count.EqualTo(1));
    }

    [Test]
    public void MeasurementPointCompleted_PointsAreSortedByFocuserPosition() {
        var (chart, engine) = NewAttachedChart();

        engine.MeasurementPointCompleted += Raise.EventWith(Point(10200, 3.0));
        engine.MeasurementPointCompleted += Raise.EventWith(Point(9800, 3.1));
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5));

        Assert.That(new[] { chart.PlotFocusPoints[0].X, chart.PlotFocusPoints[1].X, chart.PlotFocusPoints[2].X },
            Is.EqualTo(new[] { 9800.0, 10000.0, 10200.0 }));
    }

    [Test]
    public void Started_ClearsAPriorAttempt() {
        var (chart, engine) = NewAttachedChart();
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5));

        engine.Started += Raise.EventWith(new AutoFocusStartedEventArgs());

        Assert.Multiple(() => {
            Assert.That(chart.FocusPoints, Is.Empty);
            Assert.That(chart.CurveFitting, Is.Null);
        });
    }

    [Test]
    public void IterationFailed_ClearsAllSeriesSoTheRetryStartsClean() {
        var (chart, engine) = NewAttachedChart();
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5, rejected: new[] { Rejected(9800, 3.0) }));
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10100, 1.6, rejected: new[] { Rejected(9800, 3.0) }));

        engine.IterationFailed += Raise.EventWith(new AutoFocusFailedEventArgs());

        Assert.Multiple(() => {
            Assert.That(chart.FocusPoints, Is.Empty);
            Assert.That(chart.PlotFocusPoints, Is.Empty);
            Assert.That(chart.PlotRejectedFocusPoints, Is.Empty);
            Assert.That(chart.PlotFinalFocusPointWithError, Is.Empty);
        });
    }

    [Test]
    public void Completed_SetsFinalFocusPointAndSigmaErrorBar() {
        var (chart, engine) = NewAttachedChart();
        var fit = new StubHyperbolicFit(new DataPoint(1005, 2.9), minimumStdError: 12.0, leaveOneOutStdError: 8.0);

        engine.Completed += Raise.EventWith(Completed(fit));

        Assert.Multiple(() => {
            Assert.That(chart.FinalFocusPoint.X, Is.EqualTo(1005));
            Assert.That(chart.FinalFocusPoint.Y, Is.EqualTo(2.9));
            Assert.That(chart.PlotFinalFocusPointWithError, Has.Count.EqualTo(1));
            Assert.That(chart.PlotFinalFocusPointWithError[0].ErrorX, Is.EqualTo(12.0));
        });
    }

    [Test]
    public void Completed_NonFiniteMinimumStdError_FallsBackToLeaveOneOut() {
        var (chart, engine) = NewAttachedChart();
        var fit = new StubHyperbolicFit(new DataPoint(1005, 2.9), minimumStdError: double.NaN, leaveOneOutStdError: 8.0);

        engine.Completed += Raise.EventWith(Completed(fit));

        Assert.That(chart.PlotFinalFocusPointWithError[0].ErrorX, Is.EqualTo(8.0));
    }

    [Test]
    public void Completed_NoUncertaintyEstimate_DrawsNoErrorBar() {
        var (chart, engine) = NewAttachedChart();
        var fit = new StubHyperbolicFit(new DataPoint(1005, 2.9), minimumStdError: double.NaN, leaveOneOutStdError: double.NaN);

        engine.Completed += Raise.EventWith(Completed(fit));

        Assert.Multiple(() => {
            Assert.That(chart.FinalFocusPoint.X, Is.EqualTo(1005), "the best-focus marker still shows");
            Assert.That(chart.PlotFinalFocusPointWithError, Is.Empty);
        });
    }

    [Test]
    public void Failed_WithRegions_SyncsTheSameFinalState() {
        var (chart, engine) = NewAttachedChart();
        var fit = new StubHyperbolicFit(new DataPoint(1005, 2.9), minimumStdError: 12.0, leaveOneOutStdError: 8.0);
        var failed = new AutoFocusFailedEventArgs { RegionHFRs = Completed(fit).RegionHFRs };

        engine.Failed += Raise.EventWith(failed);

        Assert.Multiple(() => {
            Assert.That(chart.FinalFocusPoint.X, Is.EqualTo(1005));
            Assert.That(chart.PlotFinalFocusPointWithError, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Completed_EmptyRegionHFRs_IsNoOp() {
        var (chart, engine) = NewAttachedChart();

        Assert.DoesNotThrow(() => engine.Completed += Raise.EventWith(new AutoFocusCompletedEventArgs { RegionHFRs = ImmutableList<AutoFocusRegionHFR>.Empty }));
        Assert.DoesNotThrow(() => engine.Failed += Raise.EventWith(new AutoFocusFailedEventArgs()));

        Assert.Multiple(() => {
            Assert.That(chart.FinalFocusPoint.X, Is.EqualTo(-1.0), "the sentinel is untouched");
            Assert.That(chart.PlotFinalFocusPointWithError, Is.Empty);
        });
    }

    [Test]
    public void Detach_StopsReceivingUpdates() {
        var (chart, engine) = NewAttachedChart();

        chart.Detach();
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5));

        Assert.That(chart.FocusPoints, Is.Empty);
    }

    [Test]
    public void Detach_IsIdempotent() {
        var (chart, _) = NewAttachedChart();

        Assert.DoesNotThrow(() => { chart.Detach(); chart.Detach(); chart.Dispose(); });
    }

    [Test]
    public void Attach_Twice_DoesNotDoubleSubscribe() {
        var (chart, engine) = NewAttachedChart();

        chart.Attach(engine);
        engine.MeasurementPointCompleted += Raise.EventWith(Point(10000, 1.5));

        Assert.That(chart.FocusPoints, Has.Count.EqualTo(1), "a re-attach must not register the handlers twice");
    }

    [Test]
    public void Attach_NullEngine_IsIgnored() {
        var chart = new LiveAutoFocusChartVM();

        Assert.DoesNotThrow(() => chart.Attach(null));
    }
}
