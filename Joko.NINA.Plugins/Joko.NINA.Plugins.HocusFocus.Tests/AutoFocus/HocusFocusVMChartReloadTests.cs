using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
using System.IO;
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

    // A private report directory per test, so the info-row lookup never sees the developer's real
    // %LOCALAPPDATA%\NINA\AutoFocus (which would make "no report on disk" depend on the machine).
    private string reportDir;

    [SetUp]
    public void SetUp() {
        reportDir = Path.Combine(Path.GetTempPath(), "hf-chart-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(reportDir);
    }

    [TearDown]
    public void TearDown() {
        try {
            if (reportDir != null && Directory.Exists(reportDir)) {
                Directory.Delete(reportDir, recursive: true);
            }
        } catch (IOException) {
            // A leaked temp directory must never fail a test run.
        }
    }

    private static MediatorBundle BundleWithSweepProfile() {
        var bundle = new MediatorBundle();
        bundle.ProfileService.ActiveProfile.FocuserSettings.AutoFocusStepSize.Returns(StepSize);
        bundle.ProfileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps.Returns(OffsetSteps);
        bundle.AutoFocusOptions.HyperbolicFitModel.Returns(HyperbolicFitModel.Symmetric);
        bundle.AutoFocusOptions.WeightedHyperbolicFitEnabled.Returns(false);
        return bundle;
    }

    // The VM under test, with its loaded-report lookup pointed at this test's own (initially empty) directory.
    private HocusFocusVM BuildVM() {
        var vm = BundleWithSweepProfile().BuildHocusFocusVM();
        vm.LoadedReportSource = new AutoFocusReportDirectorySource(reportDir);
        return vm;
    }

    /// <summary>
    /// Writes a report the way the product writes one — <see cref="HocusFocusReport"/> serialized by Newtonsoft into
    /// <c>yyyy-MM-dd--HH-mm-ss--{profileId}.json</c> (<c>HocusFocusVM.GenerateReport</c>) — so these tests exercise
    /// the real on-disk format rather than a hand-rolled approximation of it. <paramref name="fileNameStamp"/>
    /// defaults to <paramref name="timestamp"/>; passing a different value reproduces the real case where the
    /// report's own <c>DateTime.Now</c> and the file name's land on opposite sides of a second boundary.
    /// </summary>
    private void WriteReport(DateTime timestamp, double initialPosition, double initialHfr, double finalHfr,
                             DateTime? fileNameStamp = null) {
        var report = new HocusFocusReport {
            Timestamp = timestamp,
            InitialFocusPoint = new FocusPoint { Position = initialPosition, Value = initialHfr },
            CalculatedFocusPoint = new FocusPoint { Position = 5000, Value = 2.0 },
            FinalHFR = finalHfr,
            Method = AFMethodEnum.STARHFR.ToString(),
            Fitting = AFCurveFittingEnum.TRENDHYPERBOLIC.ToString(),
            MeasurePoints = Array.Empty<FocusPoint>()
        };
        var name = $"{(fileNameStamp ?? timestamp):yyyy-MM-dd--HH-mm-ss}--90d513b9-bd75-41db-9250-6f7b15b4ba3d.json";
        File.WriteAllText(Path.Combine(reportDir, name), SerializeLikeProduction(report));
    }

    /// <summary>
    /// Serializes a report the way <c>HocusFocusVM.GenerateReport</c> actually does — INCLUDING the option blocks.
    ///
    /// <para>This exists because omitting them is what hid a total failure of the loaded-report lookup. Those three
    /// properties are interface-typed (<c>IStarDetectionOptions</c> / <c>IAutoFocusOptions</c> /
    /// <c>IFocuserSettings</c>), so Newtonsoft can serialize them and CANNOT construct them on the way back: a real
    /// report threw "Could not create an instance of type ... Type is an interface or abstract class", the source
    /// caught it and returned null, and every info row collapsed. A fixture that writes reports the production
    /// writer would never produce cannot catch that, and did not — for a whole release.</para>
    ///
    /// <para>The blocks are injected as raw JSON rather than as real option objects on purpose: the point is to
    /// reproduce the on-disk SHAPE, and binding to the concrete option types would make this fixture depend on
    /// their constructors instead of on the format under test.</para>
    /// </summary>
    private static string SerializeLikeProduction(HocusFocusReport report) {
        var json = JObject.Parse(JsonConvert.SerializeObject(report, Formatting.Indented));
        json["HocusFocusStarDetectionOptions"] = new JObject { ["PersistToProfile"] = true, ["NoiseReductionRadius"] = 3 };
        json["HocusFocusAutoFocusOptions"] = new JObject { ["PersistToProfile"] = true, ["MaxConcurrent"] = 0 };
        json["FocuserOptions"] = new JObject { ["AutoFocusStepSize"] = 50, ["AutoFocusInitialOffsetSteps"] = 4 };
        return json.ToString();
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
        var vm = BuildVM();
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

    // GUARD (passes with and without the loaded-report lookup): with no report on disk there is nothing to render
    // the loaded run's own values from, so the rows must collapse rather than keep the previous live run's.
    [Test]
    public void CoreLoadChart_ForeignRun_NoReportOnDisk_ResetsLiveOnlyInfoFields() {
        var vm = BuildVM();
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

    // GUARD: same, on a VM that has never produced a report of its own.
    [Test]
    public void CoreLoadChart_NoLiveRunEver_ResetsLiveOnlyInfoFields() {
        var vm = BuildVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), new DateTime(2026, 7, 29, 0, 33, 29));

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(-1));
            Assert.That(vm.InitialHFR, Is.EqualTo(0.0));
            Assert.That(vm.FinalHFR, Is.EqualTo(0.0));
        });
    }

    // DISCRIMINATING: the whole point of the change. Browsing chart history must show the LOADED run's own starting
    // position and HFRs, which are in its report — not the sentinels the pre-fix guard wrote unconditionally.
    // Numbers are a real report from disk (2026-08-05--21-47-43--90d513b9….json).
    [Test]
    public void CoreLoadChart_ForeignRun_RendersTheLoadedRunsOwnStartingPositionAndHfrs() {
        var vm = BuildVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });
        vm.MarkReportGenerated(new DateTime(2026, 7, 28, 23, 0, 0));

        var loadedTimestamp = new DateTime(2026, 8, 5, 21, 47, 43, 829);
        WriteReport(loadedTimestamp, initialPosition: 25000.0, initialHfr: 0.7029822224498201, finalHfr: 0.70526003548489);

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(24999.996, 0.667), loadedTimestamp);

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(25000),
                "browsing chart history must show the loaded run's own starting focuser position");
            Assert.That(vm.InitialHFR, Is.EqualTo(0.7029822224498201).Within(1e-12));
            Assert.That(vm.FinalHFR, Is.EqualTo(0.70526003548489).Within(1e-12),
                "FinalHFR is plugin-only, so it can only come from re-reading the report as a HocusFocusReport");
        });
    }

    // DISCRIMINATING: a report that does not record an initial position must collapse THAT row only. Position 0 is
    // what `new FocusPoint()` deserializes to when the field is absent, so it means "not recorded", not "step 0".
    [Test]
    public void CoreLoadChart_ForeignRun_MissingInitialPositionCollapsesOnlyThatRow() {
        var vm = BuildVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });

        var loadedTimestamp = new DateTime(2026, 8, 5, 21, 47, 43, 829);
        WriteReport(loadedTimestamp, initialPosition: 0.0, initialHfr: 0.0, finalHfr: 1.23);

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), loadedTimestamp);

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(-1),
                "an unrecorded initial position must collapse the row, never render as 0");
            Assert.That(vm.InitialHFR, Is.EqualTo(0.0));
            Assert.That(vm.FinalHFR, Is.EqualTo(1.23).Within(1e-12),
                "the values the report DOES carry must still render");
        });
    }

    // DISCRIMINATING: the file name only narrows the candidate set; the report's own Timestamp decides. A report
    // one second away is a DIFFERENT run, and adopting it is exactly the stale-value bug this path guards against.
    [Test]
    public void CoreLoadChart_ForeignRun_NearMissTimestampIsNotAdopted() {
        var vm = BuildVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });

        var loadedTimestamp = new DateTime(2026, 8, 5, 21, 47, 43, 829);
        // Inside the ±5 s file-name window, so it IS considered — and must still be rejected on content.
        WriteReport(loadedTimestamp.AddSeconds(2), initialPosition: 25000.0, initialHfr: 0.703, finalHfr: 0.705);

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), loadedTimestamp);

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(-1));
            Assert.That(vm.InitialHFR, Is.EqualTo(0.0));
            Assert.That(vm.FinalHFR, Is.EqualTo(0.0));
        });
    }

    // DISCRIMINATING (of the lookup, not of the guard): the report's own DateTime.Now and the file name's are two
    // separate calls with a serialization between them, so they can land on opposite sides of a second boundary.
    // The ±5 s file-name window exists for exactly this, and the exact content match still identifies the run.
    [Test]
    public void CoreLoadChart_ForeignRun_FileNameSecondDiffersFromReportTimestamp_StillFound() {
        var vm = BuildVM();
        SeedLiveRunState(vm, new[] { 900, 950, 1000, 1050, 1100 });

        var loadedTimestamp = new DateTime(2026, 8, 5, 21, 47, 43, 992);
        WriteReport(loadedTimestamp, initialPosition: 25000.0, initialHfr: 0.703, finalHfr: 0.705,
            fileNameStamp: loadedTimestamp.AddSeconds(1)); // serialization crossed into the next second

        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), loadedTimestamp);

        Assert.That(vm.InitialFocuserPosition, Is.EqualTo(25000));
    }

    [Test]
    public void CoreLoadChart_SameRunReload_PreservesLiveInfoFields() {
        var vm = BuildVM();
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
    public void CoreLoadChart_OwnRun_AfterVisitingAForeignRun_StillShowsItsOwnInfoRows() {
        // FIELD REPORT 2026-08-06: "I just ran an autofocus which showed the before->after position. Then I
        // switched to an older AF run and back, and it only shows the latest."
        //
        // CoreLoadChart_SameRunReload_PreservesLiveInfoFields covers the watcher re-loading the just-written chart
        // IMMEDIATELY, and passes -- because the live fields are still intact at that point. It does not cover a
        // ROUND TRIP. Once a foreign chart has been loaded, the live fields have already been overwritten with that
        // run's values; coming back to this VM's own run then takes the "same run" branch and re-populates nothing,
        // so whatever the foreign visit left behind stays on screen.
        var vm = BuildVM();
        SeedLiveRunState(vm, new[] { 4900, 4950, 5000, 5050, 5100 });
        var ownTimestamp = new DateTime(2026, 8, 6, 18, 11, 17, 525);
        vm.MarkReportGenerated(ownTimestamp);

        // The live run's own report is on disk exactly as GenerateReport writes it.
        WriteReport(ownTimestamp, initialPosition: 5000, initialHfr: 1.79, finalHfr: 1.85);
        // An older run the user can pick from the chart list. Deliberately given DIFFERENT values, so a stale
        // reading and a correct one cannot be confused.
        var foreignTimestamp = new DateTime(2026, 7, 27, 10, 42, 13, 104);
        WriteReport(foreignTimestamp, initialPosition: 4986, initialHfr: 4.46, finalHfr: 4.47);

        // 1. Switch to the older run.
        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(4986, 4.4), foreignTimestamp);
        Assert.That(vm.InitialFocuserPosition, Is.EqualTo(4986), "sanity: the foreign visit renders the foreign run");

        // 2. Switch back to the run this VM produced.
        SimulateCoreLoadChart(vm, WellCenteredSweep(), new DataPoint(5000, 2.0), ownTimestamp);

        Assert.Multiple(() => {
            Assert.That(vm.InitialFocuserPosition, Is.EqualTo(5000),
                "returning to this VM's own run must show ITS starting position, not a collapsed row and not the foreign run's");
            Assert.That(vm.InitialHFR, Is.EqualTo(1.79).Within(1e-9));
            Assert.That(vm.FinalHFR, Is.EqualTo(1.85).Within(1e-9));
        });
    }

    [Test]
    public void CoreLoadChart_FarPoints_ExcludedByFocusWindowAndPrunedFromFitAndDisplay() {
        var vm = BuildVM();

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
