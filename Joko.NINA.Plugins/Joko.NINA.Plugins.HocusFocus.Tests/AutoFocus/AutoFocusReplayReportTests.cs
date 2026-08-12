using Newtonsoft.Json.Linq;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.WPF.Base.Utility.AutoFocus;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus;

/// <summary>
/// Covers the per-region AutoFocus report files (<c>attemptNN/autofocus_report_Region{N}.json</c>) — the run config +
/// region geometry the bank tooling (golden eval, OptimizationRunDiscovery) reads back — and specifically that a
/// REPLAY writes them. Replay paths deliberately bypass the live completion handler, so before the report write was
/// split out of GenerateReport a re-analysis produced a save folder with frames but no reports, no matter how the AF
/// Save option was set.
/// </summary>
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class AutoFocusReplayReportTests {

    private string tempRoot;

    [SetUp]
    public void SetUp() {
        tempRoot = Path.Combine(Path.GetTempPath(), "hf-replay-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [TearDown]
    public void TearDown() {
        try {
            if (tempRoot != null && Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        } catch (IOException) {
            // A leaked temp folder must never fail the suite.
        }
    }

    private static HocusFocusReport[] Reports(int count) =>
        Enumerable.Range(0, count).Select(i => new HocusFocusReport { FinalHFR = 1.0 + i }).ToArray();

    private static string ReportPath(string saveFolder, string attempt, int regionIndex) =>
        Path.Combine(saveFolder, attempt, $"autofocus_report_Region{regionIndex}.json");

    /// <summary>
    /// Builds completion args carrying <paramref name="regionCount"/> regions. Fittings are left default (all null):
    /// HocusFocusReport.GenerateReport is null-tolerant there, and the fit values are not what these tests pin.
    /// </summary>
    private static AutoFocusCompletedEventArgs CompletedArgs(string saveFolder, int regionCount, int iteration = 1) {
        var regionHFRs = Enumerable.Range(0, regionCount).Select(i => new AutoFocusRegionHFR {
            Region = new StarDetectionRegion(new RatioRect(0.0, 0.0, 1.0, 1.0), index: i),
            InitialHFR = 5.0 + i,
            EstimatedFinalHFR = 3.0 + i,
            FinalHFR = 3.0 + i,
            EstimatedFinalFocuserPosition = 10000 + i,
            FinalFocuserPosition = 10000 + i,
            Fittings = new AutoFocusFitting()
        }).ToImmutableList();
        return new AutoFocusCompletedEventArgs {
            Iteration = iteration,
            InitialFocusPosition = 9000,
            RegionHFRs = regionHFRs,
            Filter = "L",
            Temperature = 4.5,
            Duration = TimeSpan.FromMinutes(2),
            SaveFolder = saveFolder
        };
    }

    // The replay completion handlers are private event handlers; invoke them the way the engine's Completed event
    // would. Asserting the method exists also pins that the replay paths still have a report-writing handler at all.
    private static void RaiseCompletedReplay(object vm, AutoFocusCompletedEventArgs e) {
        var handler = vm.GetType().GetMethod("AutoFocusEngine_CompletedReplay", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(handler, Is.Not.Null, $"{vm.GetType().Name} must wire a replay completion handler that writes region reports");
        try {
            handler.Invoke(vm, new object[] { null, e });
        } catch (TargetInvocationException tie) {
            throw tie.InnerException ?? tie;
        }
    }

    [Test]
    public void SaveRegionReports_WritesOneFilePerRegionIntoTheAttemptFolder() {
        var saveFolder = Path.Combine(tempRoot, "AutoFocus_20260812_120000");
        Directory.CreateDirectory(saveFolder);

        InspectorVM.SaveRegionReports(saveFolder, iteration: 1, Reports(6));

        Assert.Multiple(() => {
            for (int i = 0; i < 6; ++i) {
                Assert.That(File.Exists(ReportPath(saveFolder, "attempt01", i)), Is.True, $"region {i} report");
            }
            Assert.That(File.Exists(ReportPath(saveFolder, "attempt01", 6)), Is.False, "no report beyond the region count");
        });

        // Each file must carry ITS OWN region's report, not region 0's repeated.
        var third = JObject.Parse(File.ReadAllText(ReportPath(saveFolder, "attempt01", 3)));
        Assert.That(third.Value<double>("FinalHFR"), Is.EqualTo(4.0));
    }

    [Test]
    public void SaveRegionReports_CreatesTheAttemptFolderWhenTheEngineDidNot() {
        // The engine only creates attemptNN while saving frames. A rerun re-analyzes frames that are already on disk
        // and saves none, so on that path the folder does not exist yet and a plain write would throw
        // DirectoryNotFoundException — the reason a saving replay produced no reports.
        var saveFolder = Path.Combine(tempRoot, "AutoFocus_20260812_130000");
        Directory.CreateDirectory(saveFolder);
        Assert.That(Directory.Exists(Path.Combine(saveFolder, "attempt01")), Is.False, "precondition: no attempt folder");

        Assert.DoesNotThrow(() => InspectorVM.SaveRegionReports(saveFolder, iteration: 1, Reports(6)));

        Assert.That(File.Exists(ReportPath(saveFolder, "attempt01", 0)), Is.True);
    }

    [TestCase(1, "attempt01")]
    [TestCase(2, "attempt02")]
    [TestCase(12, "attempt12")]
    public void SaveRegionReports_PadsTheAttemptNumberToTwoDigits(int iteration, string expectedFolder) {
        // The bank tooling globs attemptNN; a bare "attempt1" would be invisible to it.
        var saveFolder = Path.Combine(tempRoot, $"AutoFocus_iter{iteration}");
        Directory.CreateDirectory(saveFolder);

        InspectorVM.SaveRegionReports(saveFolder, iteration, Reports(1));

        Assert.That(File.Exists(ReportPath(saveFolder, expectedFolder, 0)), Is.True);
    }

    [Test]
    public void SaveRegionReports_OverwritesAnExistingReport() {
        var saveFolder = Path.Combine(tempRoot, "AutoFocus_rewrite");
        Directory.CreateDirectory(saveFolder);

        InspectorVM.SaveRegionReports(saveFolder, iteration: 1, new[] { new HocusFocusReport { FinalHFR = 9.0 } });
        InspectorVM.SaveRegionReports(saveFolder, iteration: 1, new[] { new HocusFocusReport { FinalHFR = 2.5 } });

        var written = JObject.Parse(File.ReadAllText(ReportPath(saveFolder, "attempt01", 0)));
        Assert.That(written.Value<double>("FinalHFR"), Is.EqualTo(2.5), "a re-analysis replaces the previous report");
    }

    [Test]
    public void InspectorReplay_WithSaveFolder_WritesRegionReportsWithoutAnnouncingAFocusEvent() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var saveFolder = Path.Combine(tempRoot, "AutoFocus_inspector_replay");
        Directory.CreateDirectory(saveFolder);

        RaiseCompletedReplay(vm, CompletedArgs(saveFolder, regionCount: 6));

        Assert.Multiple(() => {
            for (int i = 0; i < 6; ++i) {
                Assert.That(File.Exists(ReportPath(saveFolder, "attempt01", i)), Is.True, $"region {i} report");
            }
            // A replay re-analyzes old frames; it must not tell the rest of NINA that a focus run just succeeded.
            bundle.FocuserMediator.DidNotReceive().BroadcastSuccessfulAutoFocusRun(Arg.Any<NINA.Equipment.Interfaces.Mediator.AutoFocusInfo>());
        });
    }

    [Test]
    public void InspectorReplay_WithoutSaveFolder_WritesNothing() {
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();

        Assert.DoesNotThrow(() => RaiseCompletedReplay(vm, CompletedArgs(saveFolder: null, regionCount: 6)));

        Assert.That(Directory.EnumerateFileSystemEntries(tempRoot), Is.Empty, "AF Save off ⇒ nothing on disk");
    }

    [Test]
    public void InspectorReplay_WithFewerThanSixRegions_WritesNothingInsteadOfThrowing() {
        // Replaying a plain single-region AutoFocus run through the Inspector: the region grid the per-region report
        // indexes is absent, which must degrade to a logged skip rather than an IndexOutOfRange out of the engine.
        var bundle = new MediatorBundle();
        var vm = bundle.BuildInspectorVM();
        var saveFolder = Path.Combine(tempRoot, "AutoFocus_single_region");
        Directory.CreateDirectory(saveFolder);

        Assert.DoesNotThrow(() => RaiseCompletedReplay(vm, CompletedArgs(saveFolder, regionCount: 1)));

        Assert.That(Directory.EnumerateFileSystemEntries(saveFolder), Is.Empty);
    }

    [Test]
    public void AutoFocusPaneReplay_WithSaveFolder_WritesRegion0WithoutAnnouncingAFocusEvent() {
        var bundle = new MediatorBundle();
        bundle.FocuserMediator.GetInfo().Returns(new NINA.Equipment.Equipment.MyFocuser.FocuserInfo { Connected = true });
        var vm = bundle.BuildHocusFocusVM();
        var saveFolder = Path.Combine(tempRoot, "AutoFocus_pane_replay");
        Directory.CreateDirectory(saveFolder);

        RaiseCompletedReplay(vm, CompletedArgs(saveFolder, regionCount: 1));

        Assert.Multiple(() => {
            Assert.That(File.Exists(ReportPath(saveFolder, "attempt01", 0)), Is.True);
            bundle.FocuserMediator.DidNotReceive().BroadcastSuccessfulAutoFocusRun(Arg.Any<NINA.Equipment.Interfaces.Mediator.AutoFocusInfo>());
            // The pane keeps showing the report of the run that actually moved the focuser.
            Assert.That(vm.LastReport, Is.Null, "a replay must not overwrite LastReport");
        });
    }

    [Test]
    public void AutoFocusPaneReplay_WithoutSaveFolder_WritesNothing() {
        var bundle = new MediatorBundle();
        bundle.FocuserMediator.GetInfo().Returns(new NINA.Equipment.Equipment.MyFocuser.FocuserInfo { Connected = true });
        var vm = bundle.BuildHocusFocusVM();

        Assert.DoesNotThrow(() => RaiseCompletedReplay(vm, CompletedArgs(saveFolder: "", regionCount: 1)));

        Assert.That(Directory.EnumerateFileSystemEntries(tempRoot), Is.Empty);
    }
}
