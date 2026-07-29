using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.WPF.Base.Utility.AutoFocus;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NSubstitute;
using NUnit.Framework;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

// Reload-survival tests for the results chart: NINA core's AutoFocusToolVM.LoadChart (history-dropdown
// selection and the report file-watcher's auto-select) rebuilds only the IAutoFocusVM-visible members —
// FocusPoints, PlotFocusPoints, FinalFocusPoint, LastAutoFocusPoint, chart method/fitting, then calls
// SetCurveFittings. The plugin-only display state (PlotCoreFocusPoints, PlotWindowExcludedFocusPoints,
// InitialFocuserPosition, InitialHFR, FinalHFR) must be reconciled there, or the chart renders one run's
// curve/summary over another run's markers (the 2026-07-29 field report this guards against).
[TestFixture]
public class HocusFocusVMChartReloadTests {

    private const int StepSize = 25;
    private const int OffsetSteps = 4;

    private static MediatorBundle BundleWithSweepProfile() {
        var bundle = new MediatorBundle();
        bundle.ProfileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(StepSize);
        bundle.ProfileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps.Returns(OffsetSteps);
        bundle.AutoFocusOptions.HyperbolicFitModel.Returns(HyperbolicFitModel.Symmetric);
        bundle.AutoFocusOptions.WeightedHyperbolicFitEnabled.Returns(false);
        return bundle;
    }

    // Seeds the VM the way a completed live run leaves it: every measured point mirrored into the
    // report/fit set, the filled marker series, and the connecting line, plus stale-prone info fields.
    private static void SeedLiveRunState(HocusFocusVM vm, int[] positions, double hfr = 3.0) {
        foreach (var x in positions) {
            var p = new ScatterErrorPoint(x, hfr, 0, 0.1);
            vm.FocusPoints.Add(p);
            vm.PlotCoreFocusPoints.Add(p);
            vm.PlotFocusPoints.Add(new DataPoint(x, hfr));
        }
        vm.InitialFocuserPosition = positions[positions.Length / 2];
        vm.InitialHFR = 1.79;
        vm.FinalHFR = 1.85;
    }

    // Byte-for-byte mirror of NINA core AutoFocusToolVM.LoadChart's writes against IAutoFocusVM, so the
    // tests exercise exactly the sequence the pane sees when a chart is selected or auto-loaded.
    private static void SimulateCoreLoadChart(HocusFocusVM vm, IEnumerable<ScatterErrorPoint> reportPoints, DataPoint calculatedFocusPoint, DateTime timestamp) {
        var comparer = new FocusPointComparer();
        var plotComparer = new PlotPointComparer();
        vm.FocusPoints.Clear();
        vm.PlotFocusPoints.Clear();

        vm.FinalFocusPoint = calculatedFocusPoint;
        vm.LastAutoFocusPoint = new ReportAutoFocusPoint {
            Focuspoint = calculatedFocusPoint,
            Temperature = 10.0,
            Timestamp = timestamp,
            Filter = "Ha"
        };

        var focusPoints = new AsyncObservableCollection<ScatterErrorPoint>();
        var plotFocusPoints = new AsyncObservableCollection<DataPoint>();
        foreach (var fp in reportPoints) {
            focusPoints.AddSorted(new ScatterErrorPoint(Convert.ToInt32(fp.X), fp.Y, 0, fp.ErrorY), comparer);
            plotFocusPoints.AddSorted(new DataPoint(Convert.ToInt32(fp.X), fp.Y), plotComparer);
        }
        vm.FocusPoints = focusPoints;
        vm.PlotFocusPoints = plotFocusPoints;

        vm.AutoFocusChartMethod = AFMethodEnum.STARHFR;
        vm.AutoFocusChartCurveFitting = AFCurveFittingEnum.TRENDHYPERBOLIC;
        vm.SetCurveFittings(AFMethodEnum.STARHFR.ToString(), AFCurveFittingEnum.TRENDHYPERBOLIC.ToString());
        vm.AutoFocusDuration = TimeSpan.FromMinutes(5);
    }

    private static List<ScatterErrorPoint> WellCenteredSweep() {
        // 4900..5100 in 25-step increments: every point within the ±(4+0.5)*25 = ±112.5 window of the
        // ~5000 vertex, so a faithful reload shows zero window exclusions.
        return SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
            x0: 5000, y0: 0.0, a: 2.0, b: 80.0, xStart: 4900, xStep: 25, count: 9);
    }

    [Test]
    public void CoreLoadChart_ForeignRun_RebuildsMarkerSeriesFromLoadedPoints() {
        var vm = BundleWithSweepProfile().BuildHocusFocusVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });
        // The live run also excluded one far point, so the hollow overlay and legend gate are non-empty.
        vm.ApplyWindowExclusionToDisplay(new List<AutoFocusRegionPoint> {
            new AutoFocusRegionPoint { FocuserPosition = 900, Measurement = new MeasureAndError { Measure = 3.0, Stdev = 0.1 } }
        });

        var loaded = WellCenteredSweep();
        SimulateCoreLoadChart(vm, loaded, new DataPoint(5000, 2.0), new DateTime(2026, 7, 29, 0, 33, 29));

        var loadedPositions = loaded.Select(p => (int)Math.Round(p.X)).ToArray();
        Assert.Multiple(() => {
            Assert.That(vm.PlotCoreFocusPoints.Select(p => (int)Math.Round(p.X)), Is.EquivalentTo(loadedPositions),
                "filled markers must show the loaded run, not the previous live run");
            Assert.That(vm.PlotFocusPoints.Select(p => (int)Math.Round(p.X)), Is.EquivalentTo(loadedPositions));
            Assert.That(vm.PlotWindowExcludedFocusPoints, Is.Empty,
                "the previous run's hollow overlay must not survive a chart load");
            Assert.That(vm.HasWindowExcludedFocusPoints, Is.False);
        });
    }

    [Test]
    public void CoreLoadChart_ForeignRun_ResetsLiveOnlyInfoFields() {
        var vm = BundleWithSweepProfile().BuildHocusFocusVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });
        vm.MarkReportGenerated(new DateTime(2026, 7, 28, 23, 0, 0));

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), new DateTime(2026, 7, 29, 0, 33, 29));

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(-1),
                "a foreign chart must not keep the previous live run's initial position");
            Assert.That(vm.InitialHFR, Is.EqualTo(0.0));
            Assert.That(vm.FinalHFR, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void CoreLoadChart_NoLiveRunEver_ResetsLiveOnlyInfoFields() {
        var vm = BundleWithSweepProfile().BuildHocusFocusVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), new DateTime(2026, 7, 29, 0, 33, 29));

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(-1));
            Assert.That(vm.InitialHFR, Is.EqualTo(0.0));
            Assert.That(vm.FinalHFR, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void CoreLoadChart_SameRunReload_PreservesLiveInfoFields() {
        var vm = BundleWithSweepProfile().BuildHocusFocusVM();
        SeedLiveRunState(vm, new[] { 4900, 4950, 5000, 5050, 5100 });
        var reportTimestamp = new DateTime(2026, 7, 29, 0, 33, 29, 804);
        vm.MarkReportGenerated(reportTimestamp);

        // The report file-watcher auto-loads the chart this VM just wrote: same timestamp, same run.
        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), reportTimestamp);

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(5000),
                "reloading this VM's own run must keep its live-only info rows");
            Assert.That(vm.InitialHFR, Is.EqualTo(1.79));
            Assert.That(vm.FinalHFR, Is.EqualTo(1.85));
        });
    }

    [Test]
    public void CoreLoadChart_FarPoints_ExcludedByFocusWindowAndPrunedFromFitAndDisplay() {
        var vm = BundleWithSweepProfile().BuildHocusFocusVM();

        // 4850..5150 in 25-step increments: the two points on each fringe sit 125 and 150 from the
        // vertex, outside the ±(4+0.5)*25 = ±112.5 window, so a faithful reload re-derives exactly
        // those four as hollow window exclusions and refits on the inner nine.
        var loaded = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
            x0: 5000, y0: 0.0, a: 2.0, b: 80.0, xStart: 4850, xStep: 25, count: 13);
        SimulateCoreLoadChart(vm, loaded, new DataPoint(5000, 2.0), new DateTime(2026, 7, 29, 0, 33, 29));

        var innerPositions = Enumerable.Range(0, 9).Select(i => 4900 + i * 25).ToArray();
        Assert.Multiple(() => {
            Assert.That(vm.PlotWindowExcludedFocusPoints.Select(p => (int)Math.Round(p.X)), Is.EquivalentTo(new[] { 4850, 4875, 5125, 5150 }));
            Assert.That(vm.PlotCoreFocusPoints.Select(p => (int)Math.Round(p.X)), Is.EquivalentTo(innerPositions));
            Assert.That(vm.PlotFocusPoints.Select(p => (int)Math.Round(p.X)), Is.EquivalentTo(innerPositions));
            Assert.That(vm.HasWindowExcludedFocusPoints, Is.True);
            Assert.That(vm.FocusPoints, Has.Count.EqualTo(13), "the report/fit set stays full");
            Assert.That(vm.HyperbolicFitting, Is.Not.Null);
            Assert.That(vm.HyperbolicFitting.Minimum.X, Is.EqualTo(5000).Within(StepSize));
        });
    }
}
