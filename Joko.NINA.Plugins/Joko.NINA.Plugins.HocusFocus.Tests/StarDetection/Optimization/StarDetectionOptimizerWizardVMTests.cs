#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel.AutoFocus;
using NSubstitute;
using NSubstitute.Core;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Unit tests for the wizard VM step machine, the STARHFR/seed guard, the summary model, Apply, cancellation,
/// and multi-run handling. Everything runs in-memory: a fake <see cref="IRunEvaluationLoader"/> returns a
/// <see cref="RunEvaluationData"/> built over a synthetic detection delegate (mirroring the T3 tests), and a
/// substitute <see cref="IStarDetectionOptions"/> records the applied DTO. No UI, no real images.
/// </summary>
[TestFixture]
public class StarDetectionOptimizerWizardVMTests {

    private static AlglibAPI NewAlglib() => new AlglibAPI();

    // A clean symmetric hyperbola hfr(pos) = sqrt(a^2 + ((pos - p0)/b)^2), as in RunEvaluationDataTests.
    private const double HyperbolaA = 1.5;
    private const double HyperbolaB = 8.0;
    private const int HyperbolaP0 = 10000;
    private const int DefaultStepSize = 100;

    private static double Hfr(int pos) {
        var dx = (pos - HyperbolaP0) / HyperbolaB;
        return Math.Sqrt(HyperbolaA * HyperbolaA + dx * dx);
    }

    private static RunFitConfig DefaultFitConfig() => new RunFitConfig {
        StepSize = DefaultStepSize,
        UseWeights = true,
        MaxOutlierRejections = 0,
        RejectionConfidence = 0.0,
        PreferredModel = null
    };

    private static List<RunFrame> NineFrames() {
        var frames = new List<RunFrame>();
        for (var i = -4; i <= 4; i++) {
            var pos = HyperbolaP0 + i * DefaultStepSize;
            frames.Add(new RunFrame { FrameId = $"pos_{pos}", FocuserPosition = pos, Image = pos });
        }
        return frames;
    }

    // Detection where star count peaks at a target Sensitivity, so J responds to Sensitivity and the optimizer
    // has something to improve (mirrors CreateEvaluator_PluggedIntoOptimizer in RunEvaluationDataTests).
    private static Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> OptimizableDetect(
        double optSensitivity = 10.0) {
        return (image, p, token) => {
            var pos = (int)image;
            var dist = Math.Abs(p.Sensitivity - optSensitivity);
            var starCount = (int)Math.Max(10, Math.Round(22 - 1.5 * dist));
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(pos),
                HFRStdDev = 0.05,
                StarCount = starCount,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };
    }

    // Same curve shape, but with an in-focus HFR of 5 px instead of 1.5 — big enough that the detection-binning
    // rule asks for 2x2 while the run analyzed at 1x1. Needed by any test of the recommendation's ACTIONS: with
    // the standard fixture the recommendation matches the run, so every gate is false and such a test is vacuous.
    private const double LargeStarHyperbolaA = 5.0;

    // Flatter than HyperbolaB so the sweep spans a realistic 5.0 -> ~9.4 px rather than 5 -> 50; the seed guard
    // rejects a curve that steep as unusable.
    private const double LargeStarHyperbolaB = 50.0;

    private static double LargeStarHfr(int pos) {
        var dx = (pos - HyperbolaP0) / LargeStarHyperbolaB;
        return Math.Sqrt(LargeStarHyperbolaA * LargeStarHyperbolaA + dx * dx);
    }

    private static LoadedRun LargeStarRun(string id = "largestars", double optSensitivity = 10.0) {
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            var dist = Math.Abs(p.Sensitivity - optSensitivity);
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = LargeStarHfr(pos),
                HFRStdDev = 0.05,
                StarCount = (int)Math.Max(10, Math.Round(22 - 1.5 * dist)),
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };
        var data = new RunEvaluationData(id, NineFrames(), detect, NewAlglib(), DefaultFitConfig());
        return new LoadedRun {
            Data = data,
            Seed = new StarDetectorParams { Sensitivity = 2, StarClippingMultiplier = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
        };
    }

    private static LoadedRun GoodRun(string id = "good", double optSensitivity = 10.0, int seedSensitivity = 2) {
        var data = new RunEvaluationData(id, NineFrames(), OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig());
        var seed = new StarDetectorParams { Sensitivity = seedSensitivity, StarClippingMultiplier = 2.0 };
        var afOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 };
        return new LoadedRun { Data = data, Seed = seed, AfOptions = afOptions };
    }

    // A run whose optimizer SEED (default) and displayed BASELINE (current settings) differ, so a test can prove
    // the optimizer starts from Seed while the summary's "before" column reflects Baseline.
    private static LoadedRun SeedBaselineSplitRun(
        string id = "split", double optSensitivity = 10.0, int seedSensitivity = 2, int baselineSensitivity = 8) {
        var data = new RunEvaluationData(id, NineFrames(), OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig());
        return new LoadedRun {
            Data = data,
            Seed = new StarDetectorParams { Sensitivity = seedSensitivity, StarClippingMultiplier = 2.0 },
            Baseline = new StarDetectorParams { Sensitivity = baselineSensitivity, StarClippingMultiplier = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
        };
    }

    // A degenerate run: only two distinct focuser positions => the fit cannot be determined (NaN sigma), which
    // is exactly the STARHFR/seed guard condition.
    private static LoadedRun DegenerateRun(string id = "degenerate") {
        var frames = new List<RunFrame> {
            new RunFrame { FrameId = "a", FocuserPosition = 9900, Image = 9900 },
            new RunFrame { FrameId = "b", FocuserPosition = 10100, Image = 10100 },
        };
        var data = new RunEvaluationData(id, frames, OptimizableDetect(), NewAlglib(), DefaultFitConfig());
        var seed = new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.0 };
        var afOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 };
        return new LoadedRun { Data = data, Seed = seed, AfOptions = afOptions };
    }

    // A run where detection finds stars ONLY at low Sensitivity: the optimizer SEED (2) yields a clean hyperbola,
    // but the displayed BASELINE / current settings (8) are effectively blind (no stars, so no curve). This isolates
    // which params the seed guard evaluates — the Live sweep must gate on the seed, not the failing current settings.
    private static LoadedRun SeedGoodBaselineBlindRun(string id = "splitguard") {
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            var blind = p.Sensitivity >= 5.0;
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = blind ? 0.0 : Hfr(pos),
                HFRStdDev = 0.05,
                StarCount = blind ? 0 : 15,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };
        var data = new RunEvaluationData(id, NineFrames(), detect, NewAlglib(), DefaultFitConfig());
        return new LoadedRun {
            Data = data,
            Seed = new StarDetectorParams { Sensitivity = 2, StarClippingMultiplier = 2.0 },
            Baseline = new StarDetectorParams { Sensitivity = 8, StarClippingMultiplier = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
        };
    }

    private static IRunEvaluationLoader LoaderReturning(params LoadedRun[] runs) {
        var loader = Substitute.For<IRunEvaluationLoader>();
        var queue = new Queue<LoadedRun>(runs);
        loader.LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : runs[runs.Length - 1]));
        // The labels-overload (used by the re-optimize path) delegates to the same source so re-loads keep working.
        loader.LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : runs[runs.Length - 1]));
        // The progress-bearing overload is the one the VM actually calls (acquire + re-load); feed it the same queue.
        loader.LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : runs[runs.Length - 1]));
        // The baseline-override overload is the one the VM calls once per-filter routing lands; same queue.
        loader.LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<IStarDetectionOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : runs[runs.Length - 1]));
        return loader;
    }

    /// <summary>
    /// An HONEST recording loader for the re-optimize path: it manufactures a fresh <see cref="LoadedRun"/> on EVERY
    /// call (the first optimization disposed the prior runs, so re-optimize must re-load), keyed by the folder it was
    /// asked to load. It records the <see cref="FrameLabels"/> it received per folder so a test can assert the
    /// converted labels actually flowed into the re-loaded run, and counts the labels-overload invocations. The run
    /// id is derived from the folder so the wizard's runId→labels match is exercised end-to-end.
    /// </summary>
    private sealed class RecordingLoader : IRunEvaluationLoader {
        private readonly double optSensitivity;
        private readonly int seedSensitivity;
        public readonly Dictionary<string, IReadOnlyList<FrameLabels>> LabelsByFolder = new(StringComparer.Ordinal);
        public int LabelOverloadCalls { get; private set; }
        public int NoLabelCalls { get; private set; }

        // Every LoadedRun this fake manufactures, in creation order. The VM's load-and-stamp choke-point mutates the
        // SAME RunEvaluationData instance AFTER the loader returns it, so RecoveryStepsPerSide read here (post-flow)
        // reflects the stamp — letting a test assert reload paths preserve the recovery tag.
        public List<LoadedRun> ProducedRuns { get; } = new List<LoadedRun>();

        public RecordingLoader(double optSensitivity = 10.0, int seedSensitivity = 2) {
            this.optSensitivity = optSensitivity;
            this.seedSensitivity = seedSensitivity;
        }

        // The wizard sets RunId = attempt.FolderPath; here we mirror that by stamping the run id from the folder, so
        // the runId→labels match in ReOptimizeWithLabelsAsync resolves by id (not the positional fallback).
        private static string RunIdFor(string folder) => "run::" + folder;

        private LoadedRun Make(string folder, IReadOnlyList<FrameLabels> labels) {
            var data = new RunEvaluationData(RunIdFor(folder), NineFrames(), OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig(), labels);
            var run = new LoadedRun {
                Data = data,
                Seed = new StarDetectorParams { Sensitivity = seedSensitivity, StarClippingMultiplier = 2.0 },
                AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
            };
            ProducedRuns.Add(run);
            return run;
        }

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels: null, progress: null, token);

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels, progress: null, token);

        // The progress overload is the one the VM calls; it is the single recorder so the existing counters/labels
        // assertions keep their meaning (labels present => the labels overload; null => the no-label overload).
        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, CancellationToken token) {
            if (labels != null) {
                LabelOverloadCalls++;
                LabelsByFolder[attemptFolderPath] = labels;
            } else {
                NoLabelCalls++;
            }
            return Task.FromResult(Make(attemptFolderPath, labels));
        }

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, IStarDetectionOptions baselineOptionsOverride, CancellationToken token) {
            // Record the override alongside the folder so the fake stays lossless if a test ever asserts on it.
            BaselineOverridesByFolder[attemptFolderPath] = baselineOptionsOverride;
            return LoadSavedRunAsync(attemptFolderPath, region, labels, progress, token);
        }

        public Dictionary<string, IStarDetectionOptions> BaselineOverridesByFolder { get; } = new Dictionary<string, IStarDetectionOptions>();
    }

    private static StarDetectionOptimizerWizardVM NewVM(
        IRunEvaluationLoader loader,
        IStarDetectionOptions options = null,
        IProfileService profileService = null,
        Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>> frameReviewBuilder = null,
        Func<bool> isCameraConnected = null,
        Func<bool> isFocuserConnected = null,
        IAutoFocusEngine autoFocusEngine = null,
        OptimizerSettings optimizerSettings = null,
        IAutoFocusOptions autoFocusOptions = null,
        Func<bool> confirmRoughFocus = null,
        Func<double, double, bool> confirmCaptureNewSweep = null,
        Func<string> currentFilterName = null,
        Func<int?> currentGain = null,
        Func<bool> perFilterEnabled = null,
        Func<bool> isFilterWheelConnected = null,
        Func<IReadOnlyList<string>> getFilterNames = null,
        Func<string> getCurrentFilterName = null,
        Func<string, FilterInfo> resolveFilterByName = null,
        Func<string, IStarDetectionOptions> getFilterDetectionOptions = null,
        Action<string, OptimizedStarDetectionSettings> applyOptimizedToFilter = null,
        Action<string, bool> setFilterDonutDetection = null) {
        options ??= Substitute.For<IStarDetectionOptions>();
        profileService ??= Substitute.For<IProfileService>();
        return new StarDetectionOptimizerWizardVM(
            profileService,
            options,
            loader,
            autoFocusEngine: autoFocusEngine ?? Substitute.For<IAutoFocusEngine>(),
            folderPicker: () => @"C:\fake\attempt",
            region: StarDetectionRegion.Full,
            optimizerSettings: optimizerSettings ?? new OptimizerSettings { MaxEvaluations = 200, CoarseGridLevels = 4, StepFloorFraction = 0.125 },
            frameReviewBuilder: frameReviewBuilder,
            isCameraConnected: isCameraConnected,
            isFocuserConnected: isFocuserConnected,
            autoFocusOptions: autoFocusOptions,
            confirmRoughFocus: confirmRoughFocus,
            confirmCaptureNewSweep: confirmCaptureNewSweep,
            currentFilterName: currentFilterName,
            currentGain: currentGain,
            perFilterEnabled: perFilterEnabled,
            isFilterWheelConnected: isFilterWheelConnected,
            getFilterNames: getFilterNames,
            getCurrentFilterName: getCurrentFilterName,
            resolveFilterByName: resolveFilterByName,
            getFilterDetectionOptions: getFilterDetectionOptions,
            applyOptimizedToFilter: applyOptimizedToFilter,
            setFilterDonutDetection: setFilterDonutDetection);
    }

    // An HONEST fake review builder: it maps EACH supplied descriptor to one FrameReview (so the ReviewVM's queue
    // count reflects the real per-frame descriptor count flowing through GetFrameDescriptors() — the logic under
    // test). It records the params it was invoked with so a test can assert the BEST params were used. The reviews
    // carry no ImageProvider (no real images in a unit test) and no detector boxes — the wizard's wiring, queue
    // count, navigation, and label persistence are what these tests exercise.
    private sealed class FakeReviewBuilder {
        public int Invocations { get; private set; }
        public StarDetectorParams LastParams { get; private set; }
        public IReadOnlyList<FrameReviewDescriptor> LastDescriptors { get; private set; }

        public Task<List<FrameReview>> Build(
            IReadOnlyList<FrameReviewDescriptor> descriptors, StarDetectorParams p, IProgress<RunLoadProgress> progress, CancellationToken token) {
            Invocations++;
            LastParams = p;
            LastDescriptors = descriptors;
            var reviews = descriptors
                .Select(d => new FrameReview {
                    RunId = d.RunId,
                    FocuserPosition = d.FocuserPosition,
                    FramePath = d.FramePath
                })
                .ToList();
            return Task.FromResult(reviews);
        }
    }

    // A run whose frames carry REAL temp-file paths as their FrameId, so the descriptors' FramePath resolves to a
    // writable folder and DeriveLabelsDir yields "<tempDir>/labels" — used by the on-disk label-persistence test.
    private static LoadedRun GoodRunWithFramePaths(string framesDir, string id = "good", double optSensitivity = 10.0, int seedSensitivity = 2) {
        var frames = new List<RunFrame>();
        for (var i = -4; i <= 4; i++) {
            var pos = HyperbolaP0 + i * DefaultStepSize;
            // FrameId == the on-disk frame path; GetFrameDescriptors() forwards it to FrameReviewDescriptor.FramePath.
            var framePath = System.IO.Path.Combine(framesDir, $"frame_{pos}.fits");
            frames.Add(new RunFrame { FrameId = framePath, FocuserPosition = pos, Image = pos });
        }
        var data = new RunEvaluationData(id, frames, OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig());
        var seed = new StarDetectorParams { Sensitivity = seedSensitivity, StarClippingMultiplier = 2.0 };
        var afOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 };
        return new LoadedRun { Data = data, Seed = seed, AfOptions = afOptions };
    }

    [Test]
    public void InitialState_IsSelectSource() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.SelectSource));
            Assert.That(vm.RunCount, Is.EqualTo(1), "default to a single run");
            Assert.That(vm.SourceMode, Is.EqualTo(SourceMode.Replay));
        });
    }

    [Test]
    public async Task Start_GoodReplayRun_AdvancesToSummary() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(vm.Summary, Is.Not.Null);
        });
    }

    [Test]
    public async Task Start_UseCurrentSettings_SkipsOptimization_AndReviewIsEnterable() {
        // The "Use current settings" toggle must skip the optimization pass entirely: Result.BestParams are the
        // seed (Sensitivity unchanged at 2, not driven toward the optimizable 10), zero evaluations, and the user
        // can still go to Review to validate/label today's detection.
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.Result, Is.Not.Null);
            Assert.That(vm.Result.Evaluations, Is.EqualTo(0), "no optimization pass ran");
            Assert.That(vm.Result.BestParams.Sensitivity, Is.EqualTo(2.0), "BestParams are the current/seed settings");
            Assert.That(vm.ReviewCommand.CanExecute(null), Is.True, "Review must be reachable to validate current settings");
        });
    }

    [Test]
    public async Task Start_DegenerateSeed_SetsErrorAndDoesNotAdvanceToSummary() {
        var vm = NewVM(LoaderReturning(DegenerateRun()));
        vm.SourcePaths[0] = @"C:\bad";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary), "the guard must block the summary");
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty, "a degenerate seed must surface a user-facing error");
            Assert.That(vm.Summary, Is.Null);
        });
    }

    [Test]
    public async Task Start_GoodRun_PopulatesSummaryModel() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        var s = vm.Summary;
        Assert.Multiple(() => {
            Assert.That(s, Is.Not.Null);
            Assert.That(s.BestJ, Is.GreaterThanOrEqualTo(s.SeedJ), "best J must never regress below the seed");
            Assert.That(s.ChangedParameters, Is.Not.Null);
            Assert.That(s.ChangedParameters.Count, Is.GreaterThan(0), "an improvable run should change at least one parameter");
            Assert.That(s.RecommendedStepSize, Is.GreaterThanOrEqualTo(1), "a recommended step size must be produced");
            Assert.That(double.IsNaN(s.SeedSigmaFocus), Is.False, "seed sigma must be finite for a clean run");
            Assert.That(double.IsNaN(s.BestSigmaFocus), Is.False, "best sigma must be finite for a clean run");
        });
    }

    [Test]
    public async Task Start_ChangedParametersRow_CarriesSeedAndBestValues() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        var sensitivityRow = vm.Summary.ChangedParameters
            .FirstOrDefault(r => r.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(sensitivityRow, Is.Not.Null, "Sensitivity is expected to move toward the star-count optimum");
        Assert.Multiple(() => {
            Assert.That(sensitivityRow.SeedValue, Is.EqualTo(2.0).Within(1e-9));
            Assert.That(Math.Abs(sensitivityRow.OptimizedValue - 10.0),
                Is.LessThan(Math.Abs(sensitivityRow.SeedValue - 10.0)), "optimized Sensitivity is closer to the optimum");
        });
    }

    [Test]
    public async Task Start_OptimizerStartsFromSeed_ButSummaryBaselineIsCurrentSettings() {
        // The optimizer must start from the (default) Seed, while the displayed improvement/changed-parameters
        // "before" column must reflect the user's CURRENT settings (Baseline). Seed.Sensitivity=2 (default),
        // Baseline.Sensitivity=8 (current); both optimize toward 10.
        var vm = NewVM(LoaderReturning(SeedBaselineSplitRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        // The optimizer's own record (Result.ChangedVariables) starts from the Seed (2).
        var rawSensitivity = vm.Result.ChangedVariables
            .FirstOrDefault(c => c.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(rawSensitivity.Name, Is.EqualTo(nameof(StarDetectorParams.Sensitivity)),
            "the optimizer changed Sensitivity from its seed");
        Assert.That(rawSensitivity.SeedValue, Is.EqualTo(2.0).Within(1e-9), "the optimizer started from the default Seed (2)");

        // The displayed summary's "before" column reflects the current-settings Baseline (8).
        var displayedSensitivity = vm.Summary.ChangedParameters
            .FirstOrDefault(r => r.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(displayedSensitivity, Is.Not.Null, "the summary shows the Sensitivity change vs current settings");
        Assert.Multiple(() => {
            Assert.That(displayedSensitivity.SeedValue, Is.EqualTo(8.0).Within(1e-9),
                "the summary 'before' value is the user's current setting (8), not the default seed (2)");
            Assert.That(Math.Abs(displayedSensitivity.OptimizedValue - 10.0),
                Is.LessThan(Math.Abs(8.0 - 10.0)), "optimized Sensitivity is closer to the optimum than current");
        });
    }

    [Test]
    public void StartFromCurrentSettings_DefaultsOff() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.That(vm.StartFromCurrentSettings, Is.False);
    }

    [Test]
    public async Task Start_StartFromCurrentSettings_OptimizerSeedsFromCurrentSettingsNotDefault() {
        // Complement of Start_OptimizerStartsFromSeed_...: with the toggle ON the optimizer must START from the
        // user's CURRENT settings (Baseline.Sensitivity=8), not the default Seed (2). Result.ChangedVariables is
        // the optimizer's own seed->best record, so its SeedValue is the actual seed the search began from.
        var vm = NewVM(LoaderReturning(SeedBaselineSplitRun()));
        vm.StartFromCurrentSettings = true;
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        var rawSensitivity = vm.Result.ChangedVariables
            .FirstOrDefault(c => c.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(rawSensitivity.Name, Is.EqualTo(nameof(StarDetectorParams.Sensitivity)),
            "the optimizer changed Sensitivity from its seed");
        Assert.That(rawSensitivity.SeedValue, Is.EqualTo(8.0).Within(1e-9),
            "with StartFromCurrentSettings the optimizer started from the current settings (8), not the default seed (2)");
    }

    [Test]
    public async Task Apply_CallsApplyOptimizedSettingsWithCuratedValuesAndMetadata() {
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), options);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.AcceptCommand.Execute(null);

        var dto = options.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IStarDetectionOptions.ApplyOptimizedSettings))
            .GetArguments()[0] as OptimizedStarDetectionSettings;
        Assert.That(dto, Is.Not.Null);

        var best = vm.Result.BestParams;
        Assert.Multiple(() => {
            // Curated values equal result.BestParams (note the DTO uses different property names for some knobs).
            Assert.That(dto.BrightnessSensitivity, Is.EqualTo(best.Sensitivity).Within(1e-9));
            Assert.That(dto.StarClippingMultiplier, Is.EqualTo(best.StarClippingMultiplier).Within(1e-9));
            Assert.That(dto.NoiseClippingMultiplier, Is.EqualTo(best.NoiseClippingMultiplier).Within(1e-9));
            Assert.That(dto.StarPeakResponse, Is.EqualTo(best.PeakResponse).Within(1e-9));
            Assert.That(dto.MaxDistortion, Is.EqualTo(best.MaxDistortion).Within(1e-9));
            Assert.That(dto.MinHFR, Is.EqualTo(best.MinHFR).Within(1e-9));
            Assert.That(dto.StarCenterTolerance, Is.EqualTo(best.StarCenterTolerance).Within(1e-9));
            Assert.That(dto.StructureLayers, Is.EqualTo(best.StructureLayers));
            Assert.That(dto.NoiseReductionRadius, Is.EqualTo(best.NoiseReductionRadius));
            Assert.That(dto.MinStarBoundingBoxSize, Is.EqualTo(best.MinimumStarBoundingBoxSize));
            Assert.That(dto.HotpixelThresholdingEnabled, Is.EqualTo(best.HotpixelThresholdingEnabled));
            Assert.That(dto.HotpixelThreshold, Is.EqualTo(best.HotpixelThreshold).Within(1e-9));

            // Metadata.
            Assert.That(dto.RunCount, Is.EqualTo(1));
            Assert.That(dto.BaselineJ, Is.EqualTo(vm.Result.SeedJ).Within(1e-12));
            Assert.That(dto.FinalJ, Is.EqualTo(vm.Result.BestJ).Within(1e-12));
            Assert.That(dto.RecommendedStepSize, Is.EqualTo(vm.Summary.RecommendedStepSize));
            Assert.That(dto.RecommendedOffsetSteps, Is.EqualTo(vm.Summary.RecommendedOffsetSteps));
        });
    }

    [Test]
    public async Task Apply_WhenApplyRecommendedStepSizeTrue_WritesProfileStepSize() {
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        vm.ApplyRecommendedStepSize = true;

        vm.AcceptCommand.Execute(null);

        focuserSettings.Received(1).AutoFocusStepSize = vm.Summary.RecommendedStepSize;
        focuserSettings.Received(1).AutoFocusInitialOffsetSteps = vm.Summary.RecommendedOffsetSteps;
    }

    [Test]
    public async Task Apply_WhenApplyRecommendedStepSizeFalse_DoesNotWriteProfileStepSize() {
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        vm.ApplyRecommendedStepSize = false;

        vm.AcceptCommand.Execute(null);

        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusStepSize = default;
        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusInitialOffsetSteps = default;
        // The optimized settings DTO must still be applied even when the step size is declined.
        options.Received(1).ApplyOptimizedSettings(Arg.Any<OptimizedStarDetectionSettings>());
    }

    [Test]
    public async Task Accept_AppliesOptimizedSettingsAndRaisesRequestClose() {
        // Accept is the terminal "apply + select + close" decision: it must call ApplyOptimizedSettings (which
        // selects the optimized settings) AND dismiss the wizard via RequestClose.
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), options);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        var closeRaised = false;
        vm.RequestClose += (s, e) => closeRaised = true;

        vm.AcceptCommand.Execute(null);

        Assert.Multiple(() => {
            options.Received(1).ApplyOptimizedSettings(Arg.Any<OptimizedStarDetectionSettings>());
            Assert.That(closeRaised, Is.True, "Accept must close the wizard");
        });
    }

    [Test]
    public void Accept_BeforeSummary_CanExecuteIsFalse() {
        // Until a run has produced a Summary, Accept must be disabled (mirrors the old Apply gating).
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.That(vm.AcceptCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task Cancel_OnSummary_RaisesRequestCloseWithoutMutatingOptions() {
        // The Summary "Cancel" (close path) must discard: dismiss the wizard WITHOUT applying — no options
        // mutation. CloseCommand is the wiring behind the Summary Cancel button.
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), options);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        var closeRaised = false;
        vm.RequestClose += (s, e) => closeRaised = true;

        vm.CloseCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(closeRaised, Is.True, "Cancel must close the wizard");
            options.DidNotReceiveWithAnyArgs().ApplyOptimizedSettings(default);
        });
    }

    [Test]
    public async Task RunCountTwo_LoadsTwoRuns_SummaryReportsRunCountTwo() {
        var loader = LoaderReturning(GoodRun("a"), GoodRun("b"));
        var vm = NewVM(loader);
        vm.RunCount = 2;
        vm.SourcePaths[0] = @"C:\run1";
        vm.SourcePaths[1] = @"C:\run2";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.Summary.RunCount, Is.EqualTo(2));
            loader.Received(2).LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<IStarDetectionOptions>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public void RunCount_Setter_ResizesSourcePaths() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.RunCount = 3;
        Assert.That(vm.SourcePaths.Count, Is.EqualTo(3));
        vm.RunCount = 1;
        Assert.That(vm.SourcePaths.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Cancel_MidOptimize_LeavesVMReRunnable() {
        // A detect delegate that blocks until cancellation, so we can cancel mid-optimize deterministically.
        var gate = new SemaphoreSlim(0);
        var firstHit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> blockingDetect = async (image, p, token) => {
            firstHit.TrySetResult(true);
            await gate.WaitAsync(token).ConfigureAwait(false); // cancelled => throws OperationCanceledException
            var pos = (int)image;
            return new FrameDetectionResult { AverageHFR = Hfr(pos), HFRStdDev = 0.05, StarCount = 20, StarCenters = Array.Empty<(double X, double Y)>() };
        };
        var blockingRun = new LoadedRun {
            Data = new RunEvaluationData("block", NineFrames(), blockingDetect, NewAlglib(), DefaultFitConfig()),
            Seed = new StarDetectorParams { Sensitivity = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
        };

        var cts = new CancellationTokenSource();
        // Start blocks (returns null run on the second StartAsync); first loader hit returns the blocking run.
        var vm = NewVM(LoaderReturning(blockingRun, GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        var startTask = vm.StartAsync(cts.Token);
        await firstHit.Task; // we are now inside the blocking evaluator
        cts.Cancel();
        Assert.DoesNotThrowAsync(async () => await startTask, "cancellation must not crash the VM");

        Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary), "a cancelled run does not reach the summary");

        // Re-runnable: a fresh start with a good run completes.
        var freshCts = new CancellationTokenSource();
        vm.SourcePaths[0] = @"C:\run2";
        await vm.StartAsync(freshCts.Token);
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "the VM must be re-runnable after a cancel");
    }

    [Test]
    public async Task Dispose_AfterRun_IsIdempotentAndDoesNotThrow() {
        // T5 disposes the VM when the wizard window closes; disposing must release the CTS safely and be
        // callable more than once (window-close + GC finalization paths).
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.DoesNotThrow(() => vm.Dispose());
            Assert.DoesNotThrow(() => vm.Dispose(), "Dispose must be idempotent");
        });
    }

    [Test]
    public void Dispose_BeforeAnyRun_DoesNotThrow() {
        // No Start has run yet, so the CTS is still null; Dispose must be null-safe.
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.DoesNotThrow(() => vm.Dispose());
    }

    // ---- Review step (T8) ---------------------------------------------------------------------------------

    [Test]
    public async Task EnterReview_AfterRun_BuildsReviewVMWithEveryFrame() {
        // The review must cover ALL loaded frames (NineFrames => 9), using the optimized BEST params, and land on
        // the Review step. The honest fake builder reflects the descriptors flowing through GetFrameDescriptors().
        var fake = new FakeReviewBuilder();
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: fake.Build);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.ReviewCommand.CanExecute(null), Is.True, "Review must be enterable from a completed Summary");
        await vm.ReviewCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Review));
            Assert.That(vm.IsReview, Is.True);
            Assert.That(vm.ReviewVM, Is.Not.Null);
            Assert.That(vm.ReviewVM.QueueCount, Is.EqualTo(9), "every loaded frame is reviewable");
            Assert.That(fake.Invocations, Is.EqualTo(1));
            Assert.That(fake.LastParams, Is.SameAs(vm.Result.BestParams), "the review uses the optimized BEST params");
            Assert.That(fake.LastDescriptors.Count, Is.EqualTo(9));
        });
    }

    [Test]
    public void EnterReview_BeforeRun_CanExecuteIsFalse() {
        // Until a run has produced a Summary + descriptors, Review must be disabled (the StarReviewVM requires a
        // non-empty queue).
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        Assert.That(vm.ReviewCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task Review_BackToSummary_ReturnsToSummary() {
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Review));

        Assert.That(vm.BackToSummaryCommand.CanExecute(null), Is.True);
        vm.BackToSummaryCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.IsSummary, Is.True);
        });
    }

    [Test]
    public async Task Accept_AfterReview_StillApplies() {
        // Accept must apply whether or not the user visited Review. Accept is NOT offered on the Review step itself
        // (CanExecute is false there) — the user returns to the results page first, then Accepts.
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), options, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);

        Assert.That(vm.AcceptCommand.CanExecute(null), Is.False, "Accept must be disabled on the Review (labeling) step");

        vm.BackToSummaryCommand.Execute(null);

        var closeRaised = false;
        vm.RequestClose += (s, e) => closeRaised = true;

        vm.AcceptCommand.Execute(null);

        Assert.Multiple(() => {
            options.Received(1).ApplyOptimizedSettings(Arg.Any<OptimizedStarDetectionSettings>());
            Assert.That(closeRaised, Is.True, "Accept from the results page must close the wizard");
        });
    }

    [Test]
    public async Task Accept_WithoutReview_StillApplies() {
        // The "skip review and Accept directly from Summary" path must remain intact.
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), options, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.AcceptCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(vm.ReviewVM, Is.Null, "Accept without entering Review never builds a ReviewVM");
            options.Received(1).ApplyOptimizedSettings(Arg.Any<OptimizedStarDetectionSettings>());
        });
    }

    [Test]
    public async Task EnterReview_ThenRerun_ResetsReviewState() {
        // A fresh run invalidates the prior review (it belonged to the previous result).
        var vm = NewVM(LoaderReturning(GoodRun(), GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        Assert.That(vm.ReviewVM, Is.Not.Null);

        vm.SourcePaths[0] = @"C:\run2";
        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "re-run lands back on Summary");
            Assert.That(vm.ReviewVM, Is.Null, "the prior ReviewVM is cleared by a fresh run");
            Assert.That(vm.HasLabels, Is.False);
        });
    }

    [Test]
    public async Task Start_WithoutInjectedReviewBuilder_DisablesReview() {
        // When no builder seam is supplied (e.g. a host that didn't wire one), Review must be disabled rather than
        // throwing — Accept-directly must still work.
        var vm = NewVM(LoaderReturning(GoodRun())); // no frameReviewBuilder
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.ReviewCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task EnterReview_WhenBuilderThrows_SetsErrorAndStaysOnSummary() {
        // If the review build fails, the VM must surface the error, stay on Summary, and NOT construct a ReviewVM —
        // the user can still Accept or retry. (Matches EnterReviewAsync's catch-all error path.)
        Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>> throwingBuilder =
            (descriptors, p, progress, token) => throw new InvalidOperationException("boom while detecting");
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: throwingBuilder);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.ReviewCommand.CanExecute(null), Is.True, "precondition: Review is enterable");

        await vm.ReviewCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.HasError, Is.True, "a failed review build must surface an error");
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "a failed review build stays on Summary");
            Assert.That(vm.ReviewVM, Is.Null, "no ReviewVM is built when the review build fails");
        });
    }

    [Test]
    public async Task EnterReview_ApplyLabel_SetsHasLabelsAndPersistsRunFile() {
        // Enter Review, apply a label (a missed box on the current frame), and assert HasLabels becomes true and the
        // captured labels are non-empty. Then SaveAll (via Accept's PersistReviewLabels) writes the <runId>.json into
        // the derived labels dir on disk. Frames carry real temp paths so DeriveLabelsDir resolves to a writable dir.
        var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hf-wizard-labels-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempRoot);
        try {
            var fake = new FakeReviewBuilder();
            var vm = NewVM(LoaderReturning(GoodRunWithFramePaths(tempRoot, id: "attempt01")), frameReviewBuilder: fake.Build);
            vm.SourcePaths[0] = tempRoot;
            await vm.StartAsync(CancellationToken.None);
            await vm.ReviewCommand.ExecuteAsync(null);
            Assert.That(vm.ReviewVM, Is.Not.Null, "precondition: review built");
            Assert.That(vm.HasLabels, Is.False, "no labels applied yet");

            // Apply a missed-box label on the current frame (a drag the user would make over a missed star). A box
            // well above the click-threshold so it is recorded as-is.
            vm.ReviewVM.AddMissedBox(100.0, 120.0, 20.0, 20.0);

            Assert.That(vm.HasLabels, Is.True, "applying a label must flip HasLabels true");
            Assert.That(vm.CapturedLabels, Is.Not.Empty, "the captured per-run labels must hold the applied label");

            // Accept persists labels via PersistReviewLabels -> SaveAll; the derived labels dir is <tempRoot>/labels.
            vm.AcceptCommand.Execute(null);

            var labelsDir = System.IO.Path.Combine(tempRoot, "labels");
            var labelFile = System.IO.Path.Combine(labelsDir, "attempt01.json");
            Assert.That(System.IO.File.Exists(labelFile), Is.True, "Accept must persist the run's <runId>.json to disk");
            var json = System.IO.File.ReadAllText(labelFile);
            Assert.That(json, Does.Contain("missed"), "the persisted file must carry the missed label");
        } finally {
            try { System.IO.Directory.Delete(tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    // ---- Re-optimize with labels (T9) -------------------------------------------------------------------

    [Test]
    public void ReOptimize_BeforeAnyLabels_CanExecuteIsFalse() {
        // No labels => the re-optimize command is disabled.
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        Assert.That(vm.ReOptimizeCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task ReOptimize_AfterRunWithoutLabels_CanExecuteIsFalse() {
        // A completed run with NO labels still leaves re-optimize disabled (nothing to feed the objective).
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.ReOptimizeCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task ReOptimize_AfterApplyingLabel_IsEnabled_AndReLoadsRunsWithConvertedLabels() {
        // Run → Review → apply a missed-box label (HasLabels true) → ReOptimizeCommand enabled. Invoking it re-loads
        // the run from disk THROUGH THE LABELS OVERLOAD, carrying the converted FrameLabels, and lands on a fresh
        // Summary. The honest RecordingLoader records the labels it received so we can assert the conversion happened.
        var loader = new RecordingLoader();
        var fake = new FakeReviewBuilder();
        var vm = NewVM(loader, frameReviewBuilder: fake.Build);
        var folder = @"C:\reopt-run";
        vm.SourcePaths[0] = folder;

        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        Assert.That(vm.ReviewVM, Is.Not.Null, "precondition: review built");

        // Label a missed star on the current frame (a drag the user makes over a missed star).
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        Assert.That(vm.HasLabels, Is.True, "applying a label flips HasLabels true");

        // Back to Summary so the re-optimize affordance is reachable and the command re-evaluates.
        vm.BackToSummaryCommand.Execute(null);
        Assert.That(vm.ReOptimizeCommand.CanExecute(null), Is.True, "with labels and idle, re-optimize is enabled");

        await vm.ReOptimizeCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "re-optimize returns to an updated Summary");
            Assert.That(vm.Summary, Is.Not.Null, "a fresh summary is produced");
            Assert.That(vm.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(loader.LabelOverloadCalls, Is.GreaterThanOrEqualTo(1), "the run is re-loaded via the labels overload");
            // The labels overload received the converted labels for the re-loaded folder.
            Assert.That(loader.LabelsByFolder.ContainsKey(folder), Is.True);
            var fl = loader.LabelsByFolder[folder];
            Assert.That(fl, Is.Not.Null.And.Not.Empty, "the converted FrameLabels flowed into the re-load");
            // The missed box was applied at one focuser position; that position carries a Missed box.
            Assert.That(fl.SelectMany(p => p.Missed).Any(), Is.True, "the missed label round-tripped through LabelConverter");
        });
    }

    [Test]
    public async Task ReOptimize_AfterApplyingLabel_RemainsReviewableAgain() {
        // After a re-optimize the user can enter Review again (the review inputs were re-snapshotted from the
        // re-loaded runs before they were disposed).
        var loader = new RecordingLoader();
        var fake = new FakeReviewBuilder();
        var vm = NewVM(loader, frameReviewBuilder: fake.Build);
        vm.SourcePaths[0] = @"C:\reopt-run";

        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);

        await vm.ReOptimizeCommand.ExecuteAsync(null);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.ReviewCommand.CanExecute(null), Is.True, "Review must be enterable again after a re-optimize");
        await vm.ReviewCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Review));
        Assert.That(vm.ReviewVM, Is.Not.Null);
    }

    // ---- Continue optimizing (chained rounds) -----------------------------------------------------------

    [Test]
    public void Continue_BeforeAnyRun_CanExecuteIsFalse() {
        var vm = NewVM(new RecordingLoader());
        Assert.Multiple(() => {
            Assert.That(vm.CanContinueOptimization, Is.False);
            Assert.That(vm.ContinueOptimizationCommand.CanExecute(null), Is.False);
            Assert.That(vm.RoundsCompleted, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Continue_AfterOptimize_AppendsRound_AndStaysOptimizedVariant() {
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\cont-run";

        await vm.StartAsync(CancellationToken.None);
        Assert.Multiple(() => {
            Assert.That(vm.HasOptimized, Is.True, "precondition: an optimization pass produced the Optimized variant");
            Assert.That(vm.RoundsCompleted, Is.EqualTo(1), "the initial optimize is round 1");
            Assert.That(vm.CanContinueOptimization, Is.True, "continue is available after the first pass");
        });

        await vm.ContinueOptimizationCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "continue returns to an updated Summary");
            Assert.That(vm.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(vm.RoundsCompleted, Is.EqualTo(2), "continue appended a round");
            Assert.That(vm.IsOptimizedVariant, Is.True, "the latest round IS the Optimized variant (chart + Accept follow it)");
        });
    }

    [Test]
    public async Task Continue_CapsAtThreeTotalRounds() {
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\cont-run";

        await vm.StartAsync(CancellationToken.None);     // round 1
        await vm.ContinueOptimizationCommand.ExecuteAsync(null); // round 2
        await vm.ContinueOptimizationCommand.ExecuteAsync(null); // round 3
        Assert.Multiple(() => {
            Assert.That(vm.RoundsCompleted, Is.EqualTo(3));
            Assert.That(vm.CanContinueOptimization, Is.False, "capped at 3 total passes");
            Assert.That(vm.ContinueOptimizationCommand.CanExecute(null), Is.False);
        });

        // A further invocation must be a guarded no-op (no 4th round, still on Summary).
        await vm.ContinueOptimizationCommand.ExecuteAsync(null);
        Assert.Multiple(() => {
            Assert.That(vm.RoundsCompleted, Is.EqualTo(3), "the cap blocks a 4th round");
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task Continue_TrajectoryRows_CarryEveryStage() {
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\cont-run";

        await vm.StartAsync(CancellationToken.None);
        await vm.ContinueOptimizationCommand.ExecuteAsync(null); // 2 rounds -> chain [baseline, r1, r2]

        // The curated detector rows carry the full per-round path; the appended AF step-size/offset rows are a
        // separate 2-stage concern, so scope to the trajectory (multi-stage) rows.
        var trajectoryRows = vm.ChangedParametersDisplay.Where(r => r.Stages != null).ToList();
        Assert.That(trajectoryRows, Is.Not.Empty, "the optimizer changed at least one curated param (Sensitivity)");
        // Each multi-stage row carries one value per stage: current + each round (= RoundsCompleted + 1).
        Assert.Multiple(() => {
            foreach (var r in trajectoryRows) {
                Assert.That(r.Stages.Count, Is.EqualTo(vm.RoundsCompleted + 1));
            }
            Assert.That(trajectoryRows[0].Trajectory, Does.Contain("→"), "the trajectory renders the arrowed per-round path");
            Assert.That(vm.HasRoundsSummary, Is.True, "the multi-round J header is shown");
        });
    }

    [Test]
    public async Task Continue_FocusPrecision_ShowsEveryRound() {
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\cont-run";

        await vm.StartAsync(CancellationToken.None);
        // A single pass has no chain to show, so focus precision is the start→finish SigmaText (no per-round path).
        Assert.That(vm.FocusPrecisionText, Is.EqualTo(vm.Summary.SigmaText));

        await vm.ContinueOptimizationCommand.ExecuteAsync(null); // 2 rounds -> baseline + R1 + R2
        // The σ row now shows the full per-round path (baseline → R1 → R2), mirroring the J rounds header — two arrows.
        Assert.Multiple(() => {
            Assert.That(vm.IsOptimizedVariant, Is.True);
            Assert.That(vm.FocusPrecisionText.Count(c => c == '→'), Is.EqualTo(2),
                "baseline → R1 → R2 renders one σ per stage");
        });
    }

    [Test]
    public async Task Continue_LiveImprovementBaseline_IsMostRecentRound() {
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\cont-run";

        await vm.StartAsync(CancellationToken.None);
        vm.IsOptimizedVariant = true;            // read the optimized best regardless of the default selection
        var priorBestJ = vm.Result.BestJ;
        var priorBestSigma = vm.Summary.BestSigmaFocus;
        Assume.That(double.IsFinite(priorBestSigma), "the σ assertion below is only meaningful for a finite σ");

        await vm.ContinueOptimizationCommand.ExecuteAsync(null);

        // The live baseline for a Continue pass is the PRIOR round's best (the seed for this pass), not the initial
        // current-settings baseline — so ProgressSeedJ/ProgressSeedSigma track where the last round left off, and the
        // live "σ before → after" reads as the leg the continued search is adding to the summary's σ trajectory.
        Assert.Multiple(() => {
            Assert.That(vm.ProgressSeedJ, Is.EqualTo(priorBestJ).Within(1e-9));
            Assert.That(vm.ProgressSeedSigma, Is.EqualTo(priorBestSigma).Within(1e-9));
        });
    }

    [Test]
    public async Task Continue_LiveImprovementGate_StaysAtCurrentSettings_WhenThePriorRoundLostToThem() {
        // A Continue pass may be seeded from a round that never beat the user's current settings —
        // CanContinueOptimization does not require OptimizerImprovedOverCurrent. Gating the live line on that losing
        // round alone would let it announce a win while the results page still reports "could not improve on your
        // current settings", so ProgressGateJ takes the stricter of the pass baseline and the current-settings J.
        //
        // Fixture: the current settings sit ON the synthetic optimum (Sensitivity 10) and the optimizer gets a
        // one-evaluation budget, so it never leaves its seed (Sensitivity 2) and finishes strictly below current.
        var vm = NewVM(
            LoaderReturning(SeedBaselineSplitRun(baselineSensitivity: 10), SeedBaselineSplitRun(baselineSensitivity: 10)),
            frameReviewBuilder: new FakeReviewBuilder().Build,
            optimizerSettings: new OptimizerSettings { MaxEvaluations = 1, CoarseGridLevels = 4, StepFloorFraction = 0.125 });
        vm.SourcePaths[0] = @"C:\gate-run";

        await vm.StartAsync(CancellationToken.None);
        var currentSettingsJ = vm.Summary.SeedJ;   // BuildSummaryAsync sets SeedJ = currentBaselineJ
        vm.IsOptimizedVariant = true;
        var priorBestJ = vm.Result.BestJ;

        Assert.Multiple(() => {
            Assert.That(priorBestJ, Is.LessThan(currentSettingsJ), "the fixture must produce a round that LOSES to current");
            Assert.That(vm.OptimizerImprovedOverCurrent, Is.False, "so the results page keeps Current");
            Assert.That(vm.CanContinueOptimization, Is.True, "and Continue is still offered");
        });

        await vm.ContinueOptimizationCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            // The σ pair is still measured from the prior round (the leg this pass is adding)…
            Assert.That(vm.ProgressSeedJ, Is.EqualTo(priorBestJ).Within(1e-9));
            // …but the CLAIM gate is the current settings, the same baseline OptimizerImprovedOverCurrent uses.
            Assert.That(vm.ProgressGateJ, Is.EqualTo(currentSettingsJ).Within(1e-12));
            Assert.That(vm.ProgressGateJ, Is.GreaterThan(vm.ProgressSeedJ),
                "gating on the losing prior round would let the live line promise what the page withholds");
        });
    }

    [Test]
    public async Task Start_LiveSigmaBaseline_IsTheCurrentSettingsSigma() {
        // The summary's "Focus precision" row anchors at the CURRENT settings' σ (Summary.SeedSigmaFocus). The live
        // readout must anchor at the same value, or the σ pair it shows mid-run won't match the summary's.
        var vm = NewVM(new RecordingLoader(), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\sigma-run";

        await vm.StartAsync(CancellationToken.None);
        vm.IsOptimizedVariant = true;

        Assert.Multiple(() => {
            Assert.That(double.IsFinite(vm.ProgressSeedSigma), Is.True, "a baseline σ was measured");
            Assert.That(vm.ProgressSeedSigma, Is.EqualTo(vm.Summary.SeedSigmaFocus).Within(1e-9));
        });
    }

    // ---- Optimize for aberration inspection -------------------------------------------------------------

    [Test]
    public void OptimizeForAberrationInspection_DefaultsOff() {
        var vm = NewVM(new RecordingLoader());
        Assert.That(vm.OptimizeForAberrationInspection, Is.False);
    }

    [Test]
    public async Task OptimizeForAberrationInspection_Enabled_RunCompletesToSummary() {
        // Exercises the inspection plumbing end-to-end: ComputeBaselineJAsync builds ForAberrationInspection from the
        // measured current-settings σ and hands it to the optimizer, and the run still lands on a valid Summary.
        var vm = NewVM(new RecordingLoader(), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\insp-run";
        vm.OptimizeForAberrationInspection = true;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(vm.Summary, Is.Not.Null);
        });
    }

    // ---- Elapsed / run-duration clock -------------------------------------------------------------------

    [Test]
    public void FormatDuration_RendersMinutesAndZeroPaddedSeconds() {
        Assert.Multiple(() => {
            Assert.That(StarDetectionOptimizerWizardVM.FormatDuration(TimeSpan.Zero), Is.EqualTo("0:00"));
            Assert.That(StarDetectionOptimizerWizardVM.FormatDuration(TimeSpan.FromSeconds(9)), Is.EqualTo("0:09"));
            Assert.That(StarDetectionOptimizerWizardVM.FormatDuration(TimeSpan.FromSeconds(65)), Is.EqualTo("1:05"));
            Assert.That(StarDetectionOptimizerWizardVM.FormatDuration(TimeSpan.FromSeconds(605)), Is.EqualTo("10:05"));
        });
    }

    [Test]
    public void BeforeAnyRun_NoRunDuration() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.Multiple(() => {
            Assert.That(vm.HasRunDuration, Is.False);
            Assert.That(vm.RunDurationText, Is.Empty);
        });
    }

    [Test]
    public async Task AfterRun_RunDuration_IsRecordedAndFormatted() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.HasRunDuration, Is.True, "the completed run records how long it took");
            Assert.That(vm.RunDurationText, Does.Match(@"^\d+:\d{2}$"), "shown as M:SS on the summary");
        });
    }

    // ---- Stars per frame (per-run star-count trajectory) ------------------------------------------------

    [Test]
    public void FrameStarCountTrajectory_RendersCountsAndNetDelta() {
        var single = new FrameStarCountTrajectory { FocuserPosition = 100, Stages = new[] { 52 } };
        var rising = new FrameStarCountTrajectory { FocuserPosition = 200, Stages = new[] { 52, 61, 78 } };
        var falling = new FrameStarCountTrajectory { FocuserPosition = 300, Stages = new[] { 80, 74 } };
        Assert.Multiple(() => {
            Assert.That(single.CountsText, Is.EqualTo("52"));
            Assert.That(single.HasDelta, Is.False);
            Assert.That(single.DeltaText, Is.Empty);

            Assert.That(rising.CountsText, Is.EqualTo("52→61→78"));
            Assert.That(rising.HasDelta, Is.True);
            Assert.That(rising.Delta, Is.EqualTo(26));
            Assert.That(rising.DeltaText, Is.EqualTo("+26"));

            Assert.That(falling.DeltaText, Is.EqualTo("-6"));
        });
    }

    [Test]
    public void BeforeAnyRun_NoStarsPerFrame() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.That(vm.HasStarsPerFrame, Is.False);
    }

    [Test]
    public async Task OptimizedVariant_StarsPerFrame_ShowsCurrentToOptimizedTrajectory() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);
        vm.IsOptimizedVariant = true; // ensure the Optimized variant is selected

        Assert.Multiple(() => {
            Assert.That(vm.IsOptimizedVariant, Is.True);
            Assert.That(vm.HasStarsPerFrame, Is.True);
            Assert.That(vm.StarsPerFrameLabel, Does.Contain("current"));
            foreach (var r in vm.StarsPerFrame) {
                Assert.That(r.Stages.Count, Is.EqualTo(vm.RoundsCompleted + 1), "current + one round");
                Assert.That(r.CountsText, Does.Contain("→"));
            }
        });
    }

    [Test]
    public async Task Continue_ExtendsStarsPerFrameTrajectory() {
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\cont-run";

        await vm.StartAsync(CancellationToken.None);
        await vm.ContinueOptimizationCommand.ExecuteAsync(null); // chain -> [current, r1, r2]

        Assert.Multiple(() => {
            Assert.That(vm.IsOptimizedVariant, Is.True);
            Assert.That(vm.HasStarsPerFrame, Is.True);
            foreach (var r in vm.StarsPerFrame) {
                Assert.That(r.Stages.Count, Is.EqualTo(vm.RoundsCompleted + 1), "current + each round");
            }
        });
    }

    [Test]
    public async Task CurrentVariant_StarsPerFrame_ShowsCountsOnly_NoDelta() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);
        vm.IsCurrentVariant = true;

        Assert.Multiple(() => {
            Assert.That(vm.HasStarsPerFrame, Is.True, "Current variant shows the per-frame counts");
            foreach (var r in vm.StarsPerFrame) {
                Assert.That(r.Stages.Count, Is.EqualTo(1), "a single (current) stage");
                Assert.That(r.HasDelta, Is.False);
                Assert.That(r.DeltaText, Is.Empty);
            }
        });
    }

    // ---- Source mode + summary UX (starry-hopper PR1) ---------------------------------------------------

    // ---- Start validation (starry-hopper) ---------------------------------------------------------------

    [Test]
    public void CanStart_SavedMode_DisabledUntilEveryRunHasAPath() {
        var vm = NewVM(LoaderReturning(GoodRun("a"), GoodRun("b")));
        vm.RunCount = 2;
        Assert.That(vm.StartCommand.CanExecute(null), Is.False, "no paths yet");
        vm.SourcePaths[0] = @"C:\run1";
        Assert.That(vm.StartCommand.CanExecute(null), Is.False, "only one of two paths set");
        vm.SourcePaths[1] = @"C:\run2";
        Assert.That(vm.StartCommand.CanExecute(null), Is.True, "both paths set");
    }

    [Test]
    public void CanStart_Live_DisabledUntilSaveFolderSet() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = null;
        vm.SourceMode = SourceMode.Live;
        Assert.That(vm.StartCommand.CanExecute(null), Is.False, "Live needs a save folder");

        vm.SaveFolderPath = @"C:\live";
        Assert.That(vm.StartCommand.CanExecute(null), Is.True, "a save folder is chosen");

        vm.SaveFolderPath = "  ";
        Assert.That(vm.StartCommand.CanExecute(null), Is.False, "clearing the folder disables Start again");
    }

    [Test]
    public async Task Start_Live_RoughFocusDeclined_DoesNotSweepAndStaysIdle() {
        var swept = false;
        var engine = LiveEngine(_ => { swept = true; return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" }; });
        // confirmRoughFocus returns false: the user declined the "is it in focus?" dialog.
        var vm = NewVM(LoaderReturning(GoodRun()), isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine, confirmRoughFocus: () => false);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(swept, Is.False, "declining rough-focus must not move the focuser or capture");
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.SelectSource));
            Assert.That(vm.ErrorMessage, Is.Null, "declining is a user choice, not an error");
        });
    }

    [Test]
    public async Task Start_SavedMode_DuplicatePaths_SetsErrorAndDoesNotLoad() {
        var loader = LoaderReturning(GoodRun("a"), GoodRun("b"));
        var vm = NewVM(loader);
        vm.RunCount = 2;
        vm.SourcePaths[0] = @"C:\same\run";
        vm.SourcePaths[1] = @"C:\same\run";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("different"));
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
            loader.DidNotReceiveWithAnyArgs().LoadSavedRunAsync(default, default, default);
        });
    }

    [Test]
    public async Task Start_LiveMode_CameraDisconnected_SetsError() {
        var vm = NewVM(LoaderReturning(GoodRun()), isCameraConnected: () => false, isFocuserConnected: () => true);
        vm.SourceMode = SourceMode.Live;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("camera").IgnoreCase);
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task Start_LiveMode_FocuserDisconnected_SetsError() {
        var vm = NewVM(LoaderReturning(GoodRun()), isCameraConnected: () => true, isFocuserConnected: () => false);
        vm.SourceMode = SourceMode.Live;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("focuser").IgnoreCase);
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
        });
    }

    // ---- Live sweep (fixed-sweep capture) ----------------------------------------------------------------
    //
    // A bare Substitute.For<IAutoFocusEngine>() returns null from GetOptions(), which RunLiveAttemptAsync
    // dereferences (options.Save) — so every test that reaches the live sweep must stub it, along with
    // CaptureFixedSweepAsync(...) returning a SaveFolder the fake loader will accept. The connection probes are
    // stubbed connected so the pre-flight check passes.
    private static IAutoFocusEngine LiveEngine(Func<CallInfo, AutoFocusResult> onSweep) {
        var engine = Substitute.For<IAutoFocusEngine>();
        engine.GetOptions().Returns(_ => new AutoFocusEngineOptions());
        engine.CaptureFixedSweepAsync(default, default, default, default).ReturnsForAnyArgs(ci => Task.FromResult(onSweep(ci)));
        return engine;
    }

    // Live source, confirmed + save folder set so Start passes the new gating. UseCurrentSettings skips the
    // optimization pass the chart tests don't exercise.
    private static StarDetectionOptimizerWizardVM NewLiveVM(IAutoFocusEngine engine) {
        var vm = NewVM(LoaderReturning(GoodRun()), isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;
        vm.SaveFolderPath = @"C:\live";
        return vm;
    }

    [Test]
    public async Task Replay_NeverEntersCapturingState() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        var everCapturing = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsCapturing) && vm.IsCapturing) { everCapturing = true; } };
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(everCapturing, Is.False, "a replay captures nothing");
            Assert.That(vm.IsCapturing, Is.False);
        });
    }

    [Test]
    public async Task Live_IsCapturingDuringTheSweepAndClearsAfter() {
        StarDetectionOptimizerWizardVM vm = null;
        var capturingDuringRun = false;
        var engine = LiveEngine(_ => {
            capturingDuringRun = vm.IsCapturing;
            return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" };
        });
        vm = NewLiveVM(engine);

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capturingDuringRun, Is.True, "IsCapturing is set while the sweep runs");
            Assert.That(vm.IsCapturing, Is.False, "and cleared once the sweep ends");
        });
    }

    [Test]
    public async Task Live_FailedSweep_ClearsCapturingState() {
        StarDetectionOptimizerWizardVM vm = null;
        var engine = LiveEngine(_ => throw new InvalidOperationException("focuser exploded"));
        vm = NewLiveVM(engine);

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("focuser exploded"));
            Assert.That(vm.IsCapturing, Is.False);
        });
    }

    [Test]
    public void IsReplay_TracksSourceMode() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.That(vm.IsReplay, Is.True, "default source is Saved Auto-Focus (Replay)");
        vm.SourceMode = SourceMode.Live;
        Assert.That(vm.IsReplay, Is.False, "Live mode hides the runs/browse inputs");
        vm.SourceMode = SourceMode.Replay;
        Assert.That(vm.IsReplay, Is.True);
    }

    [Test]
    public void IsLive_TracksSourceMode() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.That(vm.IsLive, Is.False, "default source is Saved Auto-Focus (Replay)");
        vm.SourceMode = SourceMode.Live;
        Assert.That(vm.IsLive, Is.True, "Live mode shows the confirmation panel");
    }

    // ---- Focus recovery (Task C): session-only knob, widened Live sweep, run tagging --------------------

    [Test]
    public void FocusRecoverySteps_DefaultsToOne_AndClampsNegativeToZero() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.That(vm.FocusRecoverySteps, Is.EqualTo(1), "focus recovery defaults to 1");
        vm.FocusRecoverySteps = -3;
        Assert.That(vm.FocusRecoverySteps, Is.EqualTo(0), "a negative value clamps to 0");
        vm.FocusRecoverySteps = 4;
        Assert.That(vm.FocusRecoverySteps, Is.EqualTo(4), "a positive value is kept");
    }

    [Test]
    public void FocusRecoverySteps_Live_WidensSweepReadouts_Replay_LeavesThemUnchanged() {
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        focuserSettings.AutoFocusInitialOffsetSteps.Returns(4);
        focuserSettings.AutoFocusNumberOfFramesPerPoint.Returns(1);
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);
        var vm = NewVM(LoaderReturning(GoodRun()), profileService: profileService);

        // Live mode: recovery widens the effective sweep readouts, and editing it raises change notifications.
        vm.SourceMode = SourceMode.Live;
        Assert.That(vm.SweepEffectiveOffsetSteps, Is.EqualTo(4 + 1), "the default recovery=1 already widens the Live sweep");

        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.FocusRecoverySteps = 3;

        Assert.Multiple(() => {
            Assert.That(vm.SweepEffectiveOffsetSteps, Is.EqualTo(4 + 3), "profile offset (4) + recovery (3)");
            Assert.That(vm.SweepPointCount, Is.EqualTo(2 * (4 + 3) + 1), "point count follows the widened offset");
            Assert.That(vm.SweepEstimatedFrames, Is.EqualTo((2 * (4 + 3) + 1) * 1), "estimated frames follow the widened point count");
            Assert.That(raised, Does.Contain(nameof(vm.FocusRecoverySteps)));
            Assert.That(raised, Does.Contain(nameof(vm.SweepEffectiveOffsetSteps)));
            Assert.That(raised, Does.Contain(nameof(vm.SweepPointCount)));
            Assert.That(raised, Does.Contain(nameof(vm.SweepEstimatedFrames)));
        });

        // Replay mode: the SAME edit leaves the readouts unchanged (recovery adds 0 off the Live path).
        vm.SourceMode = SourceMode.Replay;
        var effBefore = vm.SweepEffectiveOffsetSteps;
        var pointsBefore = vm.SweepPointCount;
        var framesBefore = vm.SweepEstimatedFrames;
        vm.FocusRecoverySteps = 6;
        Assert.Multiple(() => {
            Assert.That(vm.SweepEffectiveOffsetSteps, Is.EqualTo(4), "Replay ignores recovery: effective offset == profile offset");
            Assert.That(vm.SweepEffectiveOffsetSteps, Is.EqualTo(effBefore));
            Assert.That(vm.SweepPointCount, Is.EqualTo(pointsBefore), "Replay point count is unchanged by recovery");
            Assert.That(vm.SweepEstimatedFrames, Is.EqualTo(framesBefore), "Replay estimated frames are unchanged by recovery");
        });
    }

    [Test]
    public async Task Start_LiveSweep_PassesWidenedOffsetToEngine() {
        const int P = 3;   // profile offset the engine's GetOptions reports
        const int N = 2;   // recovery steps per side
        var baseTimeout = TimeSpan.FromSeconds(300);
        AutoFocusEngineOptions captured = null;
        var engine = Substitute.For<IAutoFocusEngine>();
        engine.GetOptions().Returns(_ => new AutoFocusEngineOptions {
            AutoFocusInitialOffsetSteps = P,
            AutoFocusStepSize = DefaultStepSize,
            AutoFocusTimeout = baseTimeout
        });
        engine.CaptureFixedSweepAsync(default, default, default, default).ReturnsForAnyArgs(ci => {
            captured = ci.ArgAt<AutoFocusEngineOptions>(0);
            return Task.FromResult(new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        });
        var vm = NewVM(LoaderReturning(GoodRun()), isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;
        vm.SaveFolderPath = @"C:\live";
        vm.FocusRecoverySteps = N;

        await vm.StartAsync(CancellationToken.None);

        Assert.That(captured, Is.Not.Null, "the widened options were handed to the engine's fixed sweep");
        var oldPoints = 2 * P + 1;
        var newPoints = 2 * (P + N) + 1;
        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.Null.Or.Empty, "the widened Live run completes cleanly");
            Assert.That(captured.AutoFocusInitialOffsetSteps, Is.EqualTo(P + N), "offset widened by N recovery steps");
            Assert.That(captured.AutoFocusStepSize, Is.EqualTo(DefaultStepSize), "step size is untouched (recovery widens, not refines)");
            Assert.That(captured.AutoFocusTimeout.Ticks,
                Is.EqualTo((long)(baseTimeout.Ticks * (double)newPoints / oldPoints)),
                "timeout scaled by the point-count ratio (2(P+N)+1)/(2P+1)");
        });
    }

    [Test]
    public void ApplyFocusRecovery_WidensOffsetAndScalesTimeout_NeverStepSize() {
        var options = new AutoFocusEngineOptions {
            AutoFocusInitialOffsetSteps = 5,
            AutoFocusStepSize = 100,
            AutoFocusTimeout = TimeSpan.FromSeconds(600)
        };
        StarDetectionOptimizerWizardVM.ApplyFocusRecovery(options, 2);
        var oldPoints = 2 * 5 + 1;   // 11
        var newPoints = 2 * 7 + 1;   // 15
        Assert.Multiple(() => {
            Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(7), "offset bumped by N");
            Assert.That(options.AutoFocusStepSize, Is.EqualTo(100), "step size never changes");
            Assert.That(options.AutoFocusTimeout.Ticks,
                Is.EqualTo((long)(TimeSpan.FromSeconds(600).Ticks * (double)newPoints / oldPoints)),
                "timeout scaled by the point-count ratio");
        });
    }

    [Test]
    public void ApplyFocusRecovery_NonPositiveSteps_IsCompleteNoOp() {
        foreach (var n in new[] { 0, -1, -5 }) {
            var options = new AutoFocusEngineOptions {
                AutoFocusInitialOffsetSteps = 5,
                AutoFocusStepSize = 100,
                AutoFocusTimeout = TimeSpan.FromSeconds(600)
            };
            StarDetectionOptimizerWizardVM.ApplyFocusRecovery(options, n);
            Assert.Multiple(() => {
                Assert.That(options.AutoFocusInitialOffsetSteps, Is.EqualTo(5), $"offset unchanged at N={n}");
                Assert.That(options.AutoFocusStepSize, Is.EqualTo(100), $"step size unchanged at N={n}");
                Assert.That(options.AutoFocusTimeout, Is.EqualTo(TimeSpan.FromSeconds(600)), $"timeout unchanged at N={n}");
            });
        }
    }

    [Test]
    public async Task Start_Live_StampsRecoverySnapshotOntoLoadedRun() {
        // The loaded run's RunEvaluationData must carry the snapshotted recovery steps so the evaluator tags the outer
        // frames. The fake loader hands back the same LoadedRun instance we hold, and RecoveryStepsPerSide survives the
        // post-run Dispose (it is a plain int), so we can read the stamp after Start completes.
        var run = GoodRun();
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(run), isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;
        vm.SaveFolderPath = @"C:\live";
        vm.FocusRecoverySteps = 2;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.Null.Or.Empty);
            Assert.That(run.Data.RecoveryStepsPerSide, Is.EqualTo(2), "a Live start stamps the recovery snapshot onto the loaded run");
        });
    }

    [Test]
    public async Task Start_Replay_LeavesRecoverySnapshotAtZero() {
        // Recovery must be inert for Replay: even with the box set, a Replay start stamps 0 so the run is untagged.
        var run = GoodRun();
        var vm = NewVM(LoaderReturning(run));
        vm.FocusRecoverySteps = 5; // Replay must ignore this
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(run.Data.RecoveryStepsPerSide, Is.EqualTo(0), "a Replay start leaves recovery inert regardless of the box value");
        });
    }

    // Reload-survival regression: the load-and-stamp choke-point must keep the recovery tag on EVERY reload path
    // (re-optimize AND continue). A future change that drops the stamp from a reload path must fail these. The
    // RecordingLoader manufactures a fresh run per load and records them, so we can inspect the runs the RELOAD
    // produced (after the originating Start's runs) and assert their stamped RecoveryStepsPerSide.

    [Test]
    public async Task ReOptimize_Live_ReloadedRunKeepsRecoveryTag() {
        var loader = new RecordingLoader();
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(loader, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine,
            frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.FocusRecoverySteps = 2;

        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);
        Assert.That(vm.ReOptimizeCommand.CanExecute(null), Is.True, "precondition: labeled + idle => re-optimize enabled");

        var producedBeforeReload = loader.ProducedRuns.Count;
        await vm.ReOptimizeCommand.ExecuteAsync(null);

        var reloaded = loader.ProducedRuns.Skip(producedBeforeReload).ToList();
        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(reloaded, Is.Not.Empty, "the re-optimize path re-loaded at least one run");
            Assert.That(reloaded.All(r => r.Data.RecoveryStepsPerSide == 2), Is.True,
                "the re-optimize reload preserved the recovery tag (snapshot N=2)");
        });
    }

    [Test]
    public async Task Continue_Live_ReloadedRunKeepsRecoveryTag() {
        var loader = new RecordingLoader();
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(loader, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine,
            frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.FocusRecoverySteps = 2;

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CanContinueOptimization, Is.True, "precondition: an optimize pass ran so continue is available");

        var producedBeforeReload = loader.ProducedRuns.Count;
        await vm.ContinueOptimizationCommand.ExecuteAsync(null);

        var reloaded = loader.ProducedRuns.Skip(producedBeforeReload).ToList();
        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(reloaded, Is.Not.Empty, "the continue path re-loaded at least one run");
            Assert.That(reloaded.All(r => r.Data.RecoveryStepsPerSide == 2), Is.True,
                "the continue reload preserved the recovery tag (snapshot N=2)");
        });
    }

    [Test]
    public async Task ReOptimize_Replay_ReloadedRunStaysUntagged() {
        // The mirror case: a Replay reload must stay inert (0) even with the recovery box set, because the snapshot
        // was taken as 0 at a Replay Start — so the tag never leaks onto a replay's re-optimize pass.
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.FocusRecoverySteps = 5; // Replay must ignore this
        vm.SourcePaths[0] = @"C:\reopt-run";

        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);

        var producedBeforeReload = loader.ProducedRuns.Count;
        await vm.ReOptimizeCommand.ExecuteAsync(null);

        var reloaded = loader.ProducedRuns.Skip(producedBeforeReload).ToList();
        Assert.Multiple(() => {
            Assert.That(reloaded, Is.Not.Empty, "the re-optimize path re-loaded at least one run");
            Assert.That(reloaded.All(r => r.Data.RecoveryStepsPerSide == 0), Is.True,
                "a Replay reload stays untagged regardless of the box value");
        });
    }

    // ---- Per-filter star detection: target filter state + Start validation -----------------------------

    [Test]
    public void TargetFilterName_DefaultsToCurrentWheelFilter_WhenPerFilterEnabled() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha", "OIII" },
            getCurrentFilterName: () => "Ha");
        Assert.Multiple(() => {
            Assert.That(vm.IsPerFilterEnabled, Is.True);
            Assert.That(vm.TargetFilterName, Is.EqualTo("Ha"));
            Assert.That(vm.AvailableFilterNames, Is.EqualTo(new[] { "Lum", "Ha", "OIII" }));
        });
    }

    [Test]
    public void TargetFilterName_DefaultsToFirstProfileFilter_WhenWheelFilterUnknown() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" },
            getCurrentFilterName: () => null);
        Assert.That(vm.TargetFilterName, Is.EqualTo("Lum"));
    }

    [Test]
    public void IsPerFilterEnabled_False_ByDefault() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.Multiple(() => {
            Assert.That(vm.IsPerFilterEnabled, Is.False);
            Assert.That(vm.TargetFilterName, Is.Null, "feature off: no target filter is seeded");
        });
    }

    [Test]
    public async Task Start_PerFilterOn_NoTargetFilter_SetsErrorAndDoesNotLoad() {
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader, perFilterEnabled: () => true, getFilterNames: () => Array.Empty<string>());
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.EqualTo("Select a target filter."));
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
            loader.DidNotReceiveWithAnyArgs().LoadSavedRunAsync(default, default, default);
        });
    }

    [Test]
    public async Task Start_PerFilterOn_Live_FilterWheelDisconnected_SetsError() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            isCameraConnected: () => true, isFocuserConnected: () => true,
            perFilterEnabled: () => true,
            isFilterWheelConnected: () => false,
            getFilterNames: () => new[] { "Ha" });
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.EqualTo("Connect a filter wheel before running a live optimization."));
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task Start_PerFilterOn_Live_TargetFilterNotInProfile_SetsErrorAndDoesNotCapture() {
        // The wheel reports a filter name the profile doesn't have: the sweep could not select it, so Start must
        // refuse rather than capture through some other filter and attribute the result to the target.
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(GoodRun()),
            isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine,
            perFilterEnabled: () => true, isFilterWheelConnected: () => true,
            getFilterNames: () => new[] { "Ha" }, getCurrentFilterName: () => "Ha",
            resolveFilterByName: _ => null);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("Ha").And.Contain("not found in the profile"));
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
        });
        _ = engine.DidNotReceiveWithAnyArgs().CaptureFixedSweepAsync(default, default, default, default);
    }

    [Test]
    public void SweepFilterName_FlagsTargetFilterMissingFromProfile() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Ha" }, getCurrentFilterName: () => "Ha",
            resolveFilterByName: _ => null);
        Assert.That(vm.SweepFilterName, Is.EqualTo("Ha (not in profile)"));
    }

    [Test]
    public async Task Start_PerFilterOn_Replay_DoesNotRequireFilterWheel() {
        // Replay needs no equipment: the wheel-connected gate applies to Live only.
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            isFilterWheelConnected: () => false,
            getFilterNames: () => new[] { "Ha" });
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
    }

    [Test]
    public void SweepReadouts_ReflectTargetFilter_WhenPerFilterEnabled() {
        var target = new FilterInfo("Ha", 0, 1) { AutoFocusGain = 200 };
        var vm = NewVM(LoaderReturning(GoodRun()),
            currentFilterName: () => "Lum", currentGain: () => 100,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" },
            getCurrentFilterName: () => "Ha",
            resolveFilterByName: name => name == "Ha" ? target : null);
        Assert.Multiple(() => {
            Assert.That(vm.SweepFilterName, Is.EqualTo("Ha"), "the target filter name, not the AF/current filter");
            Assert.That(vm.SweepGain, Is.EqualTo("200"), "the target filter's per-filter AF gain");
        });
    }

    [Test]
    public void SweepReadouts_FallBackToProviders_WhenPerFilterOff() {
        var vm = NewVM(LoaderReturning(GoodRun()), currentFilterName: () => "Lum", currentGain: () => 100);
        Assert.Multiple(() => {
            Assert.That(vm.SweepFilterName, Is.EqualTo("Lum"));
            Assert.That(vm.SweepGain, Is.EqualTo("100"));
        });
    }

    [Test]
    public void SummaryFilter_ShowsTargetFilter_WhenPerFilterEnabled() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" },
            getCurrentFilterName: () => "Ha");
        Assert.Multiple(() => {
            Assert.That(vm.HasSummaryFilter, Is.True);
            Assert.That(vm.SummaryFilterName, Is.EqualTo("Ha"));
        });
    }

    [Test]
    public void SummaryFilter_Hidden_WhenPerFilterDisabled() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.TargetFilterName = "Ha"; // even if somehow set, per-filter off means no filter to name
        Assert.Multiple(() => {
            Assert.That(vm.HasSummaryFilter, Is.False);
            Assert.That(vm.SummaryFilterName, Is.Null);
        });
    }

    [Test]
    public void TargetFilterName_Set_RaisesPropertyChanged_ForSummaryFilterReadouts() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" },
            getCurrentFilterName: () => "Lum");
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.TargetFilterName = "Ha";

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(vm.SummaryFilterName)));
            Assert.That(raised, Does.Contain(nameof(vm.HasSummaryFilter)));
        });
    }

    // ---- Per-filter star detection: capture / baseline / Accept routing --------------------------------

    [Test]
    public async Task Start_PerFilterLive_PassesTargetFilterWithUseExactImagingFilter() {
        var target = new FilterInfo("Ha", 0, 1);
        FilterInfo capturedFilter = null;
        AutoFocusEngineOptions capturedOptions = null;
        var engine = LiveEngine(ci => {
            capturedOptions = ci.ArgAt<AutoFocusEngineOptions>(0);
            capturedFilter = ci.ArgAt<FilterInfo>(1);
            return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" };
        });
        var vm = NewVM(LoaderReturning(GoodRun()),
            isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine,
            perFilterEnabled: () => true, isFilterWheelConnected: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" }, getCurrentFilterName: () => "Ha",
            resolveFilterByName: name => name == "Ha" ? target : null);
        vm.SourceMode = SourceMode.Live;
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capturedFilter, Is.SameAs(target), "the sweep exposes through the resolved target filter");
            Assert.That(capturedOptions.UseExactImagingFilter, Is.True, "the AF-filter substitution is suppressed");
        });
    }

    [Test]
    public async Task Start_Live_PerFilterOff_KeepsNullFilterAndDefaultEngineOptions() {
        var capturedFilter = new FilterInfo("sentinel", 0, 0);
        AutoFocusEngineOptions capturedOptions = null;
        var engine = LiveEngine(ci => {
            capturedOptions = ci.ArgAt<AutoFocusEngineOptions>(0);
            capturedFilter = ci.ArgAt<FilterInfo>(1);
            return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" };
        });
        var vm = NewLiveVM(engine);

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capturedFilter, Is.Null, "feature off: no imaging filter is passed (existing behavior)");
            Assert.That(capturedOptions.UseExactImagingFilter, Is.False);
        });
    }

    [Test]
    public async Task Start_PerFilterOn_ThreadsTargetFilterOptionsIntoLoaderBaseline() {
        var filterOptions = Substitute.For<IStarDetectionOptions>();
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Ha" }, getCurrentFilterName: () => "Ha",
            getFilterDetectionOptions: name => name == "Ha" ? filterOptions : null);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        await loader.Received(1).LoadSavedRunAsync(
            @"C:\run1", Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(),
            Arg.Any<IProgress<RunLoadProgress>>(), filterOptions, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Start_PerFilterOff_PassesNullBaselineOverrideToLoader() {
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        await loader.Received(1).LoadSavedRunAsync(
            @"C:\run1", Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(),
            Arg.Any<IProgress<RunLoadProgress>>(), (IStarDetectionOptions)null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Accept_PerFilterOn_RoutesThroughApplyOptimizedToFilter() {
        var options = Substitute.For<IStarDetectionOptions>();
        string appliedFilter = null;
        OptimizedStarDetectionSettings appliedDto = null;
        var vm = NewVM(LoaderReturning(GoodRun()), options,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Ha" }, getCurrentFilterName: () => "Ha",
            applyOptimizedToFilter: (name, dto) => { appliedFilter = name; appliedDto = dto; });
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.AcceptCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(appliedFilter, Is.EqualTo("Ha"), "the DTO lands in the TARGET filter's settings set");
            Assert.That(appliedDto, Is.Not.Null);
            Assert.That(appliedDto.BrightnessSensitivity, Is.EqualTo(vm.Result.BestParams.Sensitivity).Within(1e-9));
        });
        options.DidNotReceiveWithAnyArgs().ApplyOptimizedSettings(default);
    }

    // The donut master is a per-filter setting like every other detection knob. The wizard start page's checkbox and
    // the optimizer's seed/budget must therefore read the TARGET filter's stored set — NOT the options-page edit
    // buffer, which belongs to whichever filter happens to be selected on the Star Detection options page and is
    // routinely a different filter (that is the whole point of the target picker).

    [Test]
    public void DefocusAwareDonutDetection_PerFilterOn_ReadsTheTargetFilter_NotTheEditBuffer() {
        var buffer = Substitute.For<IStarDetectionOptions>();   // the options page is editing "Lum"
        buffer.DefocusAwareDonutDetection.Returns(false);
        var haOptions = Substitute.For<IStarDetectionOptions>();
        haOptions.DefocusAwareDonutDetection.Returns(true);
        var vm = NewVM(LoaderReturning(GoodRun()), buffer,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" }, getCurrentFilterName: () => "Ha",
            getFilterDetectionOptions: name => name == "Ha" ? haOptions : buffer);

        Assert.Multiple(() => {
            Assert.That(vm.TargetFilterName, Is.EqualTo("Ha"));
            Assert.That(vm.DefocusAwareDonutDetection, Is.True, "the wizard's donut master reflects the TARGET filter");
        });
    }

    [Test]
    public void DefocusAwareDonutDetection_PerFilterOn_WritesTheTargetFilter_NotTheEditBuffer() {
        var buffer = Substitute.For<IStarDetectionOptions>();
        buffer.DefocusAwareDonutDetection.Returns(false);
        var haOptions = Substitute.For<IStarDetectionOptions>();
        haOptions.DefocusAwareDonutDetection.Returns(false);
        var writes = new List<(string Filter, bool Value)>();
        var vm = NewVM(LoaderReturning(GoodRun()), buffer,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" }, getCurrentFilterName: () => "Ha",
            getFilterDetectionOptions: name => name == "Ha" ? haOptions : buffer,
            setFilterDonutDetection: (name, value) => writes.Add((name, value)));

        vm.DefocusAwareDonutDetection = true;

        Assert.That(writes, Is.EqualTo(new[] { ("Ha", true) }), "the donut master persists into the TARGET filter's set");
        // The options-page buffer belongs to a different filter; ticking the wizard's checkbox must not touch it.
        buffer.DidNotReceiveWithAnyArgs().DefocusAwareDonutDetection = default;
    }

    [Test]
    public void DefocusAwareDonutDetection_PerFilterOff_StillWritesTheOptionsSingletonDirectly() {
        var options = Substitute.For<IStarDetectionOptions>();
        options.DefocusAwareDonutDetection.Returns(false);
        var writes = new List<(string Filter, bool Value)>();
        var vm = NewVM(LoaderReturning(GoodRun()), options,
            setFilterDonutDetection: (name, value) => writes.Add((name, value)));

        vm.DefocusAwareDonutDetection = true;

        Assert.Multiple(() => {
            Assert.That(writes, Is.Empty, "with the feature off there is no filter to route to");
            Assert.That(vm.IsPerFilterEnabled, Is.False);
        });
        options.Received().DefocusAwareDonutDetection = true;
    }

    [Test]
    public async Task Start_PerFilterOn_SeedDonutMasterAndBudgetFollowTheTargetFilter_WhenTheBufferHasItOff() {
        var buffer = Substitute.For<IStarDetectionOptions>();
        buffer.DefocusAwareDonutDetection.Returns(false);
        var haOptions = Substitute.For<IStarDetectionOptions>();
        haOptions.DefocusAwareDonutDetection.Returns(true);
        var run = GoodRun();
        var settings = new OptimizerSettings { MaxEvaluations = 12, CoarseGridLevels = 2, StepFloorFraction = 0.125 };
        var vm = NewVM(LoaderReturning(run), buffer,
            optimizerSettings: settings,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" }, getCurrentFilterName: () => "Ha",
            getFilterDetectionOptions: name => name == "Ha" ? haOptions : buffer);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(run.Seed.DefocusAwareDonutDetection, Is.True,
                "the seed's donut master is stamped from the target filter, so CreateCuratedSet unlocks the defocus axes");
            Assert.That(settings.MaxEvaluations, Is.EqualTo(400),
                "and the wider donut search space gets the larger evaluation budget");
        });
    }

    [Test]
    public async Task Start_PerFilterOn_TargetFilterDonutOff_KeepsStandardBudget_EvenWhenTheBufferHasItOn() {
        var buffer = Substitute.For<IStarDetectionOptions>();
        buffer.DefocusAwareDonutDetection.Returns(true);   // the edited filter wants donuts; the target does not
        var haOptions = Substitute.For<IStarDetectionOptions>();
        haOptions.DefocusAwareDonutDetection.Returns(false);
        var run = GoodRun();
        var settings = new OptimizerSettings { MaxEvaluations = 12, CoarseGridLevels = 2, StepFloorFraction = 0.125 };
        var vm = NewVM(LoaderReturning(run), buffer,
            optimizerSettings: settings,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" }, getCurrentFilterName: () => "Ha",
            getFilterDetectionOptions: name => name == "Ha" ? haOptions : buffer);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(run.Seed.DefocusAwareDonutDetection, Is.False);
            Assert.That(settings.MaxEvaluations, Is.EqualTo(12), "the edited filter's donut master must not widen this run's search");
        });
    }

    [Test]
    public async Task Start_PerFilterOff_SeedDonutMasterAndBudgetStillComeFromTheOptionsSingleton() {
        var options = Substitute.For<IStarDetectionOptions>();
        options.DefocusAwareDonutDetection.Returns(true);
        var run = GoodRun();
        var settings = new OptimizerSettings { MaxEvaluations = 12, CoarseGridLevels = 2, StepFloorFraction = 0.125 };
        var vm = NewVM(LoaderReturning(run), options, optimizerSettings: settings);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(run.Seed.DefocusAwareDonutDetection, Is.True);
            Assert.That(settings.MaxEvaluations, Is.EqualTo(400));
        });
    }

    [Test]
    public void SweepFilterAndGain_ReflectInjectedProviders() {
        var vm = NewVM(LoaderReturning(GoodRun()), currentFilterName: () => "Ha", currentGain: () => 139);
        Assert.Multiple(() => {
            Assert.That(vm.SweepFilterName, Is.EqualTo("Ha"));
            Assert.That(vm.SweepGain, Is.EqualTo("139"));
        });
    }

    [Test]
    public void SweepFilterAndGain_Unavailable_WhenProvidersReturnNothing() {
        var vm = NewVM(LoaderReturning(GoodRun()), currentFilterName: () => null, currentGain: () => (int?)null);
        Assert.Multiple(() => {
            Assert.That(vm.SweepFilterName, Is.EqualTo("Unavailable"));
            Assert.That(vm.SweepGain, Is.EqualTo("Unavailable"));
        });
    }

    [Test]
    public void HandleCaptureProgress_ContextReport_SetsContextTextAndFrameBar() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.HandleCaptureProgress(new ApplicationStatus {
            Source = AutoFocusEngine.LiveSweepProgressSource,
            Status = "Capturing frame 3 of 9 at focuser position 12345",
            Progress = 2,
            MaxProgress = 9
        });
        Assert.Multiple(() => {
            Assert.That(vm.CaptureContextText, Is.EqualTo("Capturing frame 3 of 9 at focuser position 12345"));
            Assert.That(vm.ProgressCurrent, Is.EqualTo(2));
            Assert.That(vm.ProgressTotal, Is.EqualTo(9));
        });
    }

    [Test]
    public void HandleCaptureProgress_UntaggedReport_Ignored() {
        // NINA's own camera reports go to its status bar, not to the wizard (ImagingVM drops the IProgress we pass),
        // so an untagged report must not touch either readout.
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.HandleCaptureProgress(new ApplicationStatus { Source = "Camera", Status = "Exposing", Progress = 2, MaxProgress = 5 });
        Assert.Multiple(() => {
            Assert.That(vm.CaptureExposureText, Is.Null);
            Assert.That(vm.CaptureContextText, Is.Null);
            Assert.That(vm.HasExposureProgress, Is.False);
        });
    }

    [Test]
    public void HandleCaptureProgress_ExposureCountdown_DrivesExposureBarAndRemaining() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.HandleCaptureProgress(new ApplicationStatus {
            Source = AutoFocusEngine.LiveSweepExposureSource,
            Progress = 2,
            MaxProgress = 5
        });
        Assert.Multiple(() => {
            Assert.That(vm.ExposureProgressCurrent, Is.EqualTo(2));
            Assert.That(vm.ExposureProgressMax, Is.EqualTo(5));
            Assert.That(vm.HasExposureProgress, Is.True);
            Assert.That(vm.CaptureExposureText, Is.EqualTo("Exposing, 3s remaining"));
        });
    }

    [Test]
    public async Task Start_Live_MissingSaveFolder_SetsErrorAndDoesNotSweep() {
        var swept = false;
        var engine = LiveEngine(_ => { swept = true; return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" }; });
        var vm = NewVM(LoaderReturning(GoodRun()), isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        // No SaveFolderPath chosen.

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("save").IgnoreCase);
            Assert.That(swept, Is.False, "the sweep must not run without a save folder");
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task Live_FailedSweep_WithFolder_SurfacesCleanErrorAndDoesNotOptimize() {
        // A failed/partial sweep can still return a (possibly empty) SaveFolder. The wizard must treat "not
        // succeeded" as no capture and surface a clean message, not hand a broken folder to the loader.
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = false, SaveFolder = @"C:\live\attempt" });
        var vm = NewLiveVM(engine);

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.SelectSource));
            Assert.That(vm.ErrorMessage, Does.Contain("did not produce"));
        });
    }

    // ---- Detection binning ---------------------------------------------------------------------------------

    [Test]
    public void SweepDetectionBinning_WritesThroughToThePersistedOption() {
        // The confirmation panel edits the real setting, not a session-local copy: the search is tuned at this
        // factor, so Accept must not be able to apply settings tuned at a factor the profile does not have.
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var vm = NewVM(LoaderReturning(GoodRun()), options);

        vm.SweepDetectionBinning = DetectionBinningEnum.Bin3;

        options.Received(1).DetectionBinning = DetectionBinningEnum.Bin3;
    }

    [Test]
    public void SweepCaptureBinning_ReportsNinasAutoFocusBinning() {
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        focuserSettings.AutoFocusBinning.Returns((short)2);
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService);

        Assert.That(vm.SweepCaptureBinning, Is.EqualTo("2x2"));
    }

    [Test]
    public async Task Accept_WithNoPendingBinning_DoesNotTouchTheFactor() {
        // A plain run analyzed at the persisted factor. Accept applies the tuned settings and leaves binning alone;
        // the recommendation, if any, is advice the user did not act on.
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var vm = NewVM(LoaderReturning(GoodRun()), options);
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.Summary.DetectionBinningPendingApply, Is.False);

        options.ClearReceivedCalls();
        vm.AcceptCommand.Execute(null);

        options.DidNotReceiveWithAnyArgs().DetectionBinning = default;
    }

    [Test]
    public async Task AfterARun_TheBinningBlockIsToldTheFramesAreAvailable() {
        // Regression: every run path raises the summary's dependents BEFORE SnapshotReviewInputs records the run
        // folders, so the detection-binning block was last NOTIFIED while those folders were still empty. The
        // button sat permanently disabled under copy claiming "the frames from this run are no longer available
        // to re-read" - while they were on disk exactly where the snapshot had just put them.
        //
        // This has to assert on what the UI was TOLD, not on the property's value afterwards: these are computed
        // properties, so by the time a test reads them the folders are populated and the stale notification is
        // invisible. Bindings only re-read on notification, which is precisely what was missing.
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var vm = NewVM(LoaderReturning(LargeStarRun()), options);
        vm.SourcePaths[0] = @"C:\fake\attempt";

        bool? lastNotifiedCanReOptimize = null;
        string lastNotifiedBody = null;
        vm.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(vm.CanOptimizeAgainAtRecommendedBinning)) {
                lastNotifiedCanReOptimize = vm.CanOptimizeAgainAtRecommendedBinning;
            } else if (e.PropertyName == nameof(vm.DetectionBinningBodyText)) {
                lastNotifiedBody = vm.DetectionBinningBodyText;
            }
        };

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.ShowOptimizeAgainAtRecommendedBinning, Is.True,
            "fixture guard: this run's 5 px stars must make the recommendation differ, or the assertions below are vacuous");
        Assert.Multiple(() => {
            Assert.That(lastNotifiedCanReOptimize, Is.True,
                "the last thing the UI was told must be that the re-run can proceed - the frames are on disk");
            Assert.That(lastNotifiedBody ?? string.Empty, Does.Not.Contain("no longer available"),
                "a run that just recorded its folders must never tell the user its frames are gone");
        });
    }

    [Test]
    public async Task AfterARun_TheOptimizeAgainCommandIsToldItCanRun() {
        // Regression, and NOT the same bug as the test above. That one was about stale run folders; this one is
        // about IsBusy. The command's CanExecute is a superset of the property - "CanOptimizeAgainAtRecommendedBinning
        // && !IsBusy" - and the only moment it was ever asked was from inside the run's try block, where IsBusy is
        // true by construction. IsBusy = false in the finally then notified six other commands but not this one, so
        // the button stayed disabled forever, underneath body copy (a plain property, no IsBusy term) cheerfully
        // promising that clicking it would re-run on the captured frames.
        //
        // Asserting CanExecute() after the run passes either way - IsBusy is false by then. Only the last value the
        // UI was NOTIFIED of distinguishes the two.
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var vm = NewVM(LoaderReturning(LargeStarRun()), options);
        vm.SourcePaths[0] = @"C:\fake\attempt";

        bool? lastNotifiedCanExecute = null;
        vm.OptimizeAgainAtRecommendedBinningCommand.CanExecuteChanged +=
            (s, e) => lastNotifiedCanExecute = vm.OptimizeAgainAtRecommendedBinningCommand.CanExecute(null);

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.ShowOptimizeAgainAtRecommendedBinning, Is.True,
            "fixture guard: this run's 5 px stars must make the recommendation differ, or the assertion below is vacuous");
        Assert.That(lastNotifiedCanExecute, Is.True,
            "the last thing the button was told must be that it can run - it is on screen and the frames are on disk");
    }

    [Test]
    public async Task EveryCommandDependingOnIsBusy_IsNotifiedWhenIsBusyChanges() {
        // The general form of the bug above: IsBusy's setter hand-lists the commands it notifies, so a command added
        // later silently misses out and freezes in whatever state it was last asked about. Rather than re-listing
        // them here (which would rot the same way), toggle IsBusy and catch any command whose CanExecute VALUE moved
        // without a CanExecuteChanged to tell the UI about it.
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var vm = NewVM(LoaderReturning(LargeStarRun()), options);
        vm.SourcePaths[0] = @"C:\fake\attempt";
        await vm.StartAsync(CancellationToken.None);
        Assume.That(vm.IsBusy, Is.False, "the run must have settled before the toggle means anything");

        var commands = typeof(StarDetectionOptimizerWizardVM)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType))
            .Select(p => new { p.Name, Command = (System.Windows.Input.ICommand)p.GetValue(vm) })
            .Where(c => c.Command != null)
            .ToList();
        Assert.That(commands.Count, Is.GreaterThan(5),
            $"only {commands.Count} commands were discovered - the reflection walk is broken, not the VM");

        var notified = new HashSet<string>();
        foreach (var c in commands) {
            var name = c.Name;
            c.Command.CanExecuteChanged += (s, e) => notified.Add(name);
        }
        var before = commands.ToDictionary(c => c.Name, c => c.Command.CanExecute(null));

        typeof(StarDetectionOptimizerWizardVM).GetProperty(nameof(StarDetectionOptimizerWizardVM.IsBusy))
            .GetSetMethod(nonPublic: true).Invoke(vm, new object[] { true });

        var moved = commands.Where(c => c.Command.CanExecute(null) != before[c.Name]).Select(c => c.Name).ToList();
        Assert.That(moved, Is.Not.Empty,
            "no command changed state across the toggle - the guard would pass no matter what IsBusy forgot to notify");

        var silent = moved.Where(name => !notified.Contains(name)).ToList();
        Assert.That(silent, Is.Empty,
            "these commands change their enabled state with IsBusy but IsBusy's setter never tells the UI, so their\n" +
            "buttons freeze in whatever state they were last asked about:\n  " + string.Join("\n  ", silent) + "\n" +
            "Fix: add a NotifyCanExecuteChanged() call for each in the IsBusy setter.");
    }

    [Test]
    public async Task OptimizeAgainButton_AndItsBodyCopy_AppearTogether() {
        // The body copy and the button were gated on different conditions once, so the summary could describe an
        // action whose control was hidden. They must appear and disappear together: body copy with no button
        // describes an action the user cannot take, and a button with no copy leaves the consequence unstated.
        //
        // This deliberately does NOT sniff the copy for the button's name. The earlier version did, and matched
        // only because the fixture produced empty copy - it passed for a run where neither appeared, which is the
        // one case that proves nothing.
        async Task<StarDetectionOptimizerWizardVM> RunWith(LoadedRun run) {
            var options = Substitute.For<IStarDetectionOptions>();
            options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
            var vm = NewVM(LoaderReturning(run), options);
            vm.SourcePaths[0] = @"C:\fake\attempt";
            await vm.StartAsync(CancellationToken.None);
            return vm;
        }

        // 5 px stars at 1x1: the measurement calls for a different factor, so both must appear.
        var differs = await RunWith(LargeStarRun());
        Assert.Multiple(() => {
            Assert.That(differs.ShowOptimizeAgainAtRecommendedBinning, Is.True);
            Assert.That(differs.HasDetectionBinningBody, Is.True);
            Assert.That(differs.DetectionBinningBodyText, Does.Contain("2x2"),
                "the copy must name the factor the button would switch to");
        });

        // 1.5 px stars at 1x1: the run's factor is already right, so neither must appear.
        var agrees = await RunWith(GoodRun());
        Assert.Multiple(() => {
            Assert.That(agrees.ShowOptimizeAgainAtRecommendedBinning, Is.False);
            Assert.That(agrees.HasDetectionBinningBody, Is.False);
        });
    }

    [Test]
    public async Task OptimizeAgainAtRecommendedBinning_IsOnlyOfferedWhenTheMeasurementDisagrees() {
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var vm = NewVM(LoaderReturning(GoodRun()), options);
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);

        // Whatever the synthetic run measures, the offer must agree with the summary's own verdict — and it must
        // never be offered without frames on disk to re-read.
        Assert.That(vm.CanOptimizeAgainAtRecommendedBinning, Is.EqualTo(vm.Summary.DetectionBinningDiffers));
        Assert.That(vm.HasDetectionBinningBlock, Is.EqualTo(vm.Summary.HasDetectionBinningMeasurement));
    }

    [Test]
    public async Task Apply_Live_NonPositiveExposure_DoesNotWriteProfileExposure() {
        // A non-positive exposure is never actually used by the sweep (it falls back to the profile/filter exposure),
        // so Accept must not write it back and corrupt the profile's AF exposure.
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 0.0;

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));

        vm.AcceptCommand.Execute(null);

        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusExposureTime = default;
    }

    [Test]
    public async Task Live_PassesChosenExposureAndSaveFolderToTheSweep() {
        AutoFocusEngineOptions captured = null;
        var engine = LiveEngine(ci => {
            captured = ci.Arg<AutoFocusEngineOptions>();
            return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" };
        });
        var vm = NewLiveVM(engine);
        vm.SaveFolderPath = @"D:\sweeps";
        vm.LiveExposureSeconds = 7.5;

        await vm.StartAsync(CancellationToken.None);

        Assert.That(captured, Is.Not.Null, "the sweep must be invoked");
        Assert.Multiple(() => {
            Assert.That(captured.Save, Is.True);
            Assert.That(captured.SavePath, Is.EqualTo(@"D:\sweeps"));
            Assert.That(captured.OverrideAutoFocusExposureTime, Is.EqualTo(TimeSpan.FromSeconds(7.5)));
        });
    }

    [Test]
    public void BrowseSaveFolder_SetsSaveFolderPathAndPersistsToOptions() {
        var afOptions = Substitute.For<IAutoFocusOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), autoFocusOptions: afOptions);

        vm.BrowseSaveFolderCommand.Execute(null);

        Assert.That(vm.SaveFolderPath, Is.EqualTo(@"C:\fake\attempt"), "the picked folder is shown");
        afOptions.Received(1).SavePath = @"C:\fake\attempt";
    }

    [Test]
    public async Task SeedGuard_LiveMode_GatesOnSeed_ProceedsWhenDefaultsFindStars() {
        // Current settings (Baseline) are blind on this run, but the seed (defaults) find stars — a Live sweep must
        // still be optimizable, because gating on the seed is the whole point of the live path.
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(SeedGoodBaselineBlindRun()), isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.Null, "the seed finds stars, so the sweep is optimizable");
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task SeedGuard_ReplayMode_GatesOnBaseline_AbortsWhenCurrentSettingsAreBlind() {
        // The same run under Replay gates on the CURRENT settings (Baseline), which are blind here — so Replay
        // correctly refuses (its premise is that a saved run already focused at the current settings).
        var vm = NewVM(LoaderReturning(SeedGoodBaselineBlindRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.SelectSource));
            Assert.That(vm.ErrorMessage, Does.Contain("usable focus curve"));
        });
    }

    [Test]
    public async Task Apply_Live_WritesProfileExposureWithAfSettings() {
        // The single "apply auto-focus settings" toggle (on by default) writes the sweep exposure too.
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 9.0;

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "the live sweep should reach the summary");

        vm.AcceptCommand.Execute(null);

        focuserSettings.Received(1).AutoFocusExposureTime = 9.0;
    }

    [Test]
    public async Task Apply_Live_ExposureNotWrittenWhenAfSettingsToggleOff() {
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 9.0;

        await vm.StartAsync(CancellationToken.None);
        vm.ApplyRecommendedStepSize = false; // declining the combined toggle skips the exposure too

        vm.AcceptCommand.Execute(null);

        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusExposureTime = default;
    }

    [Test]
    public async Task Apply_Replay_DoesNotWriteProfileExposure() {
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.AcceptCommand.Execute(null);

        Assert.That(vm.CanApplyExposureTime, Is.False, "Replay never chose a sweep exposure, so no exposure row/write-back");
        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusExposureTime = default;
    }

    [Test]
    public async Task SweepExposureChangeText_ShowsBeforeAfterAndUnchanged() {
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        focuserSettings.AutoFocusExposureTime.Returns(5.0);
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(GoodRun()), profileService: profileService, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 12.0;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.CanApplyExposureTime, Is.True, "the exposure row shows for a live run");
            Assert.That(vm.SweepExposureChangeText, Is.EqualTo("5 s → 12 s"));
        });
    }

    [Test]
    public async Task Accept_LiveCurrentVariant_AppliesExposureButKeepsDetectorSettings() {
        // When the optimizer can't beat the current detector settings the summary defaults to Current. A live run must
        // still be able to accept just the recommended auto-focus settings (the chosen exposure) without switching to
        // Optimized (which would swap in detector settings that were no better).
        var options = Substitute.For<IStarDetectionOptions>();
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);

        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(GoodRun()), options, profileService, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 9.0;

        await vm.StartAsync(CancellationToken.None);
        vm.SelectedVariant = OptimizationVariant.Current; // keep current detector settings

        Assert.That(vm.AcceptCommand.CanExecute(null), Is.True, "Current can be accepted to apply the exposure");
        vm.AcceptCommand.Execute(null);

        Assert.Multiple(() => {
            options.DidNotReceiveWithAnyArgs().ApplyOptimizedSettings(default); // detector settings untouched
            focuserSettings.Received(1).AutoFocusExposureTime = 9.0;            // the chosen exposure is applied
        });
    }

    [Test]
    public async Task Accept_ReplayCurrentVariant_StaysDisabled() {
        // Replay's Current variant has nothing to apply (no chosen exposure, step size unchanged), so Accept stays off.
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.SelectedVariant = OptimizationVariant.Current;

        Assert.That(vm.AcceptCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task CanAccept_LiveCurrentVariant_FollowsApplyAfSettingsToggle() {
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);
        var vm = NewVM(LoaderReturning(GoodRun()), profileService: profileService, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 9.0;

        await vm.StartAsync(CancellationToken.None);
        vm.SelectedVariant = OptimizationVariant.Current;

        vm.ApplyRecommendedStepSize = true;
        Assert.That(vm.AcceptCommand.CanExecute(null), Is.True);
        vm.ApplyRecommendedStepSize = false;
        Assert.That(vm.AcceptCommand.CanExecute(null), Is.False, "nothing to apply when the AF-settings toggle is off");
    }

    [Test]
    public async Task CanApplyRecommendedStepSize_MatchesSummaryChange() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CanApplyRecommendedStepSize, Is.EqualTo(vm.Summary.StepSizeOrOffsetChanged));
    }

    [Test]
    public async Task ChangedParametersDisplay_IncludesAfRows_OnlyWhenToggleOnAndChanged() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        var s = vm.Summary;
        var baseCount = s.ChangedParameters.Count;
        var afRows = (s.RecommendedStepSize != s.CurrentStepSize ? 1 : 0)
                   + (s.RecommendedOffsetSteps != s.CurrentOffsetSteps ? 1 : 0);

        vm.ApplyRecommendedStepSize = false;
        Assert.That(vm.ChangedParametersDisplay.Count, Is.EqualTo(baseCount), "AF rows hidden when the toggle is off");

        vm.ApplyRecommendedStepSize = true;
        var expected = vm.CanApplyRecommendedStepSize ? baseCount + afRows : baseCount;
        Assert.That(vm.ChangedParametersDisplay.Count, Is.EqualTo(expected),
            "AF rows appear only when the toggle is on AND the recommendation actually changed");
    }

    [Test]
    public async Task Start_MissingSourcePath_SetsErrorAndDoesNotLoad() {
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader);
        // Leave SourcePaths[0] empty.
        vm.SourcePaths[0] = null;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
            loader.DidNotReceiveWithAnyArgs().LoadSavedRunAsync(default, default, default);
        });
    }

    // ---- Variant model + curve display (curve toggle) ---------------------------------------------------

    [Test]
    public async Task Start_Optimized_RetainsCurrentAndOptimizedVariants_DefaultsToOptimized() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.HasCurrent, Is.True);
            Assert.That(vm.HasOptimized, Is.True);
            Assert.That(vm.HasFeedback, Is.False, "no feedback until a re-optimize");
            Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Optimized), "default shows the improvement");
            Assert.That(vm.HasSelectedCurve, Is.True);
            Assert.That(vm.SelectedCurve.Points.Count, Is.EqualTo(9), "a curve point per pooled focuser position");
            Assert.That(vm.SelectedCurve.Fit, Is.Not.Null, "the optimized curve carries a fit");
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.True, "the optimized variant is acceptable");
        });
    }

    // A run where the user's CURRENT settings (Baseline) are ALREADY at the detection optimum (Sensitivity =
    // optSensitivity), while the optimizer seeds from the default (Sensitivity 2). The search can only climb
    // TOWARD the same optimum, so it can never beat current — the saturated-plateau / local-optimum case.
    private static LoadedRun AlreadyOptimalBaselineRun(string id = "optimal", double optSensitivity = 10.0) {
        var data = new RunEvaluationData(id, NineFrames(), OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig());
        return new LoadedRun {
            Data = data,
            Seed = new StarDetectorParams { Sensitivity = 2, StarClippingMultiplier = 2.0 },
            Baseline = new StarDetectorParams { Sensitivity = optSensitivity, StarClippingMultiplier = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
        };
    }

    [Test]
    public async Task Start_OptimizerCannotBeatCurrent_DefaultsToCurrentVariant() {
        var vm = NewVM(LoaderReturning(AlreadyOptimalBaselineRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.OptimizerImprovedOverCurrent, Is.False, "the optimizer could not beat the already-optimal current settings");
            Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Current),
                "when the optimizer can't beat current, default to Current so the default Accept keeps current settings");
            Assert.That(vm.HasOptimized, Is.True, "the optimized variant is still available to inspect");
        });
    }

    [Test]
    public async Task SelectingCurrentVariant_SwitchesCurveAndSummary_AndDisablesAccept() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.Summary.ChangedParameters.Count, Is.GreaterThan(0), "precondition: optimized changed parameters");

        vm.SelectedVariant = OptimizationVariant.Current;

        Assert.Multiple(() => {
            Assert.That(vm.SelectedCurve.Label, Is.EqualTo("Current"));
            Assert.That(vm.Summary.ChangedParameters, Is.Empty, "the current variant has no changes");
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.False, "Current is the baseline and cannot be accepted");
        });
    }

    [Test]
    public async Task Start_UseCurrentSettings_HasOnlyCurrentVariant_AcceptDisabled() {
        var vm = NewVM(LoaderReturning(GoodRun()), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\run1";
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.HasCurrent, Is.True);
            Assert.That(vm.HasOptimized, Is.False, "review-only produces no optimized variant");
            Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Current));
            Assert.That(vm.HasSelectedCurve, Is.True, "the current curve is still plotted");
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.False, "nothing changed to accept in review-only");
            Assert.That(vm.ReviewCommand.CanExecute(null), Is.True, "review-only still allows labeling → feedback");
        });
    }

    [Test]
    public async Task ReOptimize_PreservesOptimizedVariant_AndReplacesFeedback() {
        // Requirement: the FIRST optimized variant is preserved across re-optimizes; only the LATEST feedback persists.
        var loader = new RecordingLoader();
        var vm = NewVM(loader, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\reopt-run";
        await vm.StartAsync(CancellationToken.None);

        vm.SelectedVariant = OptimizationVariant.Optimized;
        var optimizedRef = vm.Result;
        Assert.That(optimizedRef, Is.Not.Null);

        // First feedback round.
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);
        await vm.ReOptimizeCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Feedback), "the newest result is shown");
            Assert.That(vm.HasOptimized, Is.True);
            Assert.That(vm.HasFeedback, Is.True);
        });
        vm.SelectedVariant = OptimizationVariant.Optimized;
        Assert.That(vm.Result, Is.SameAs(optimizedRef), "the first optimized variant is preserved");
        vm.SelectedVariant = OptimizationVariant.Feedback;
        var feedbackRef1 = vm.Result;

        // Second feedback round.
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(160.0, 185.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);
        await vm.ReOptimizeCommand.ExecuteAsync(null);

        vm.SelectedVariant = OptimizationVariant.Optimized;
        Assert.That(vm.Result, Is.SameAs(optimizedRef), "optimized still preserved after a second re-optimize");
        vm.SelectedVariant = OptimizationVariant.Feedback;
        Assert.That(vm.Result, Is.Not.SameAs(feedbackRef1), "only the latest feedback is retained");
    }

    [Test]
    public async Task Accept_FeedbackVariantSelected_AppliesFeedbackResult() {
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(new RecordingLoader(), options, frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\reopt-run";
        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);
        await vm.ReOptimizeCommand.ExecuteAsync(null);
        Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Feedback));

        var feedbackBest = vm.Result.BestParams; // Feedback selected
        vm.AcceptCommand.Execute(null);

        var dto = options.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IStarDetectionOptions.ApplyOptimizedSettings))
            .GetArguments()[0] as OptimizedStarDetectionSettings;
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto.BrightnessSensitivity, Is.EqualTo(feedbackBest.Sensitivity).Within(1e-9),
            "Accept applies the SELECTED (feedback) variant's BestParams");
    }

    [Test]
    public async Task ReOptimize_FeedbackSummary_CarriesOptimizedBaselineComparison() {
        var vm = NewVM(new RecordingLoader(), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\reopt-run";
        await vm.StartAsync(CancellationToken.None);
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);
        await vm.ReOptimizeCommand.ExecuteAsync(null);

        Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Feedback));
        Assert.Multiple(() => {
            Assert.That(vm.Summary.HasFeedbackComparison, Is.True, "the feedback summary compares against the optimized baseline");
            Assert.That(vm.Summary.PriorSigmaFocus, Is.Not.Null);
            Assert.That(vm.Summary.FeedbackVsOptimizedText, Is.Not.Empty);
        });
    }

    [Test]
    public void FeedbackVsOptimizedText_ReportsTighterPercentVsOptimizedBaseline() {
        var s = new OptimizationSummary { BestSigmaFocus = 1.80, PriorSigmaFocus = 2.00 };
        Assert.Multiple(() => {
            Assert.That(s.HasFeedbackComparison, Is.True);
            Assert.That(s.FeedbackVsOptimizedText, Does.Contain("2.00").And.Contains("1.80").And.Contains("tighter"));
        });

        var noPrior = new OptimizationSummary { BestSigmaFocus = 1.80 };
        Assert.Multiple(() => {
            Assert.That(noPrior.HasFeedbackComparison, Is.False);
            Assert.That(noPrior.FeedbackVsOptimizedText, Is.Empty);
        });
    }

    [Test]
    public async Task StarCountChanges_PresentForFeedbackVariant_AndReoptimizePromptReplaced() {
        var vm = NewVM(new RecordingLoader(), frameReviewBuilder: new FakeReviewBuilder().Build);
        vm.SourcePaths[0] = @"C:\reopt-run";
        await vm.StartAsync(CancellationToken.None);

        // Just optimized (Optimized selected): no feedback comparison, no labels → no re-run prompt.
        Assert.Multiple(() => {
            Assert.That(vm.StarCountChanges, Is.Empty);
            Assert.That(vm.HasStarCountChanges, Is.False);
            Assert.That(vm.ShowReoptimizePrompt, Is.False, "no labels yet");
        });

        // Label, return to summary: the re-run prompt appears (labeled, but no feedback run yet).
        await vm.ReviewCommand.ExecuteAsync(null);
        vm.ReviewVM.AddMissedBox(150.0, 175.0, 18.0, 18.0);
        vm.BackToSummaryCommand.Execute(null);
        Assert.That(vm.ShowReoptimizePrompt, Is.True, "labeled but not re-optimized → prompt to re-run");

        // Re-optimize: now on the feedback variant — the prompt is replaced by the star-count comparison.
        await vm.ReOptimizeCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Feedback));
            Assert.That(vm.ShowReoptimizePrompt, Is.False, "after a feedback run the prompt is gone");
            Assert.That(vm.HasStarCountChanges, Is.True);
            Assert.That(vm.StarCountChanges.Count, Is.EqualTo(9), "one cell per distinct focuser position");
            Assert.That(vm.StarCountChanges.All(c => c.Delta == c.FeedbackCount - c.BaselineCount), Is.True);
            Assert.That(vm.StarCountChangeBaselineLabel, Is.EqualTo("optimized"));
        });

        // Toggling away from Feedback hides the comparison.
        vm.SelectedVariant = OptimizationVariant.Optimized;
        Assert.That(vm.HasStarCountChanges, Is.False, "the comparison only shows on the feedback variant");
    }

    // ---- Recovery-point partitioning (chart's hollow-marker overlay) -----------------------------------

    private static ScatterErrorPoint Sep(double x, double y = 2.0) => new ScatterErrorPoint(x, y, 0, 0.1);

    [Test]
    public void PartitionRecoveryPoints_RecoveryOff_AllCore_NoRecovery() {
        // FrameIsRecovery == null (the baseline / feature-off case) ⇒ every point is core, the overlay is empty.
        var points = new List<ScatterErrorPoint> { Sep(100), Sep(200), Sep(300) };
        var eval = new RunEvaluationResult {
            Points = points,
            Metrics = new RunEvaluationMetrics {
                FrameIsRecovery = null,
                FrameFocuserPositions = new[] { 100, 200, 300 }
            }
        };

        var (core, recovery) = StarDetectionOptimizerWizardVM.PartitionRecoveryPoints(eval);

        Assert.Multiple(() => {
            Assert.That(recovery, Is.Empty, "no recovery data ⇒ empty overlay");
            Assert.That(core.Select(p => p.X), Is.EquivalentTo(points.Select(p => p.X)), "all points are core");
            Assert.That(core.Count, Is.EqualTo(points.Count));
        });
    }

    [Test]
    public void PartitionRecoveryPoints_SplitsByRecoveryPosition_PartitionIsExactAndDisjoint() {
        // Positions 100 and 500 are the far-from-focus recovery extremes; 200/300/400 are core. Two frames share
        // position 100 (both flagged) to prove distinct-position handling doesn't duplicate the single pooled point.
        var points = new List<ScatterErrorPoint> { Sep(100), Sep(200), Sep(300), Sep(400), Sep(500) };
        var eval = new RunEvaluationResult {
            Points = points,
            Metrics = new RunEvaluationMetrics {
                FrameFocuserPositions = new[] { 100, 100, 200, 300, 400, 500 },
                FrameIsRecovery = new[] { true, true, false, false, false, true }
            }
        };

        var (core, recovery) = StarDetectionOptimizerWizardVM.PartitionRecoveryPoints(eval);

        Assert.Multiple(() => {
            Assert.That(recovery.Select(p => p.X), Is.EquivalentTo(new[] { 100.0, 500.0 }), "recovery positions ⇒ overlay");
            Assert.That(core.Select(p => p.X), Is.EquivalentTo(new[] { 200.0, 300.0, 400.0 }), "the rest ⇒ main series");
            // core ∪ recovery == points, with no duplication.
            Assert.That(core.Count + recovery.Count, Is.EqualTo(points.Count));
            Assert.That(core.Concat(recovery).Select(p => p.X), Is.EquivalentTo(points.Select(p => p.X)));
        });
    }

    [Test]
    public void PartitionRecoveryPoints_RecoveryPositionAbsentFromPoints_EmptyRecovery() {
        // Un-weighted-fit case: a recovery position is flagged in the metrics but was EXCLUDED from eval.Points
        // upstream. It therefore matches no point ⇒ the overlay is empty and every present point is core.
        var points = new List<ScatterErrorPoint> { Sep(200), Sep(300), Sep(400) };
        var eval = new RunEvaluationResult {
            Points = points,
            Metrics = new RunEvaluationMetrics {
                FrameFocuserPositions = new[] { 100, 200, 300, 400, 500 },
                FrameIsRecovery = new[] { true, false, false, false, true } // 100 & 500 flagged but not in Points
            }
        };

        var (core, recovery) = StarDetectionOptimizerWizardVM.PartitionRecoveryPoints(eval);

        Assert.Multiple(() => {
            Assert.That(recovery, Is.Empty, "flagged recovery positions absent from Points ⇒ nothing to overlay");
            Assert.That(core.Select(p => p.X), Is.EquivalentTo(points.Select(p => p.X)), "all present points are core");
        });
    }

    [Test]
    public void OptimizationCurve_HasRecoveryPoints_TrueOnlyWhenNonEmpty() {
        var none = new OptimizationCurve { RecoveryPoints = null };
        var empty = new OptimizationCurve { RecoveryPoints = System.Array.Empty<ScatterErrorPoint>() };
        var some = new OptimizationCurve { RecoveryPoints = new[] { Sep(100) } };
        Assert.Multiple(() => {
            Assert.That(none.HasRecoveryPoints, Is.False, "null ⇒ no overlay/caption");
            Assert.That(empty.HasRecoveryPoints, Is.False, "empty ⇒ no overlay/caption");
            Assert.That(some.HasRecoveryPoints, Is.True, "non-empty ⇒ overlay/caption shown");
        });
    }

    // ---- Star signal: the exposure recommendation on a signal-starved run --------------------------------
    //
    // Detection here finds MORE stars the LOWER the gate, and the focus curve is identical at every gate, so J is
    // a monotone function of Sensitivity alone and the search lands on the axis floor — the coarse grid's level 0
    // IS Lower (0.0), so this does not depend on the compass walking all the way down. Every accepted star carries
    // the same modest SNR, so each frame's NTarget-th-brightest is exactly starSnr and the derived exposure is
    // plain arithmetic rather than a property of the fixture's ordering.
    private static LoadedRun StarvedRun(
        string id = "starved", double starSnr = 7.0, int starsPerFrame = 30, double capturedExposureSeconds = 3.0,
        bool largeStars = false, double baselineSensitivity = 10.0) {
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = (image, p, token) => {
            var pos = (int)image;
            return Task.FromResult(new FrameDetectionResult {
                // largeStars swaps in the 5 px in-focus curve so a starved run can ALSO carry a detection-binning
                // recommendation — the only way to exercise how a pending "Optimize again at 2x2" interacts with a
                // fresh capture. The gate still floors either way: the curve is identical at every Sensitivity, so
                // J responds to the star count alone.
                AverageHFR = largeStars ? LargeStarHfr(pos) : Hfr(pos),
                HFRStdDev = 0.05,
                StarCount = (int)Math.Max(6, Math.Round(26.0 - 2.0 * p.Sensitivity)),
                StarCenters = Array.Empty<(double X, double Y)>(),
                StarSnrs = Enumerable.Repeat(starSnr, starsPerFrame).ToList()
            });
        };
        var data = new RunEvaluationData(id, NineFrames(), detect, NewAlglib(), DefaultFitConfig());
        return new LoadedRun {
            Data = data,
            // Seed AND baseline start at the healthy default gate, so the Current variant stays healthy while the
            // optimized one floors — which is what makes the follows-the-variant test below non-vacuous. Lowering
            // baselineSensitivity models a user who hand-set their OWN gate to the floor, the only way to reach a
            // floored block in "use current settings" mode (which never runs the search, so the Current variant's
            // own gate is all there is).
            Seed = new StarDetectorParams { Sensitivity = 10, StarClippingMultiplier = 2.0 },
            Baseline = new StarDetectorParams { Sensitivity = baselineSensitivity, StarClippingMultiplier = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 },
            CapturedExposureSeconds = capturedExposureSeconds
        };
    }

    [Test]
    public async Task Replay_FlooredGate_ShowsTheStarSignalBlockAndKeepsAcceptEnabled() {
        var vm = NewVM(LoaderReturning(StarvedRun()));
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.Summary.VariantSensitivity, Is.LessThanOrEqualTo(ExposureRecommender.SensitivityFloorThreshold),
            "fixture guard: the search must actually land at the gate floor, or every assertion below is vacuous");
        Assert.Multiple(() => {
            Assert.That(vm.HasExposureBlock, Is.True);
            Assert.That(vm.LowSignalChartNote, Is.EqualTo(StarDetectionOptimizerWizardVM.LowSignalChartNoteText),
                "the chart note is the only thing a user who never scrolls will see");
            Assert.That(vm.HasRecommendedExposure, Is.True);
            // t_old is the exposure recorded in the saved run's frames (3 s); S_now = 7 against a target of 10
            // needs (10/7)^2 = 2.04x, i.e. 6.12 s, rounded up the sub-10-second ladder to 6.5 s.
            Assert.That(vm.RecommendedExposureText, Is.EqualTo("3 s → 6.5 s (measured star S/N 7; target 10)"));
            // The derivation is background, so it rides on the row's tooltip rather than in the paragraph.
            Assert.That(vm.ExposureDerivationDetail, Does.Contain("Sky-limited scaling: 3 s × (10 / 7)² = 6.1 s per frame"));
            Assert.That(vm.ExposureBodyText, Does.Contain("You can still accept these settings"));
            Assert.That(vm.ExposureBodyText, Does.Contain("Live mode"), "Replay has no capture action to offer");
            // The block reports a confidence problem; it is NOT a veto. A Replay run whose gate floored is still
            // the best fit for frames like these, and Accept must stay available — HasLowStarSignal must
            // never enter CanAccept in any form.
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task Live_FlooredGate_ScalesFromTheSweepExposureNotTheRecordedOne() {
        // A Live sweep knows exactly what it captured with, and prefers that over anything read back out of the
        // frames — the fixture records a DIFFERENT 3 s so a regression to the header value is visible.
        var engine = LiveEngine(_ => new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" });
        var vm = NewVM(LoaderReturning(StarvedRun(capturedExposureSeconds: 3.0)),
            isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 5.0;

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.Summary.VariantSensitivity, Is.LessThanOrEqualTo(ExposureRecommender.SensitivityFloorThreshold),
            "fixture guard");
        Assert.Multiple(() => {
            // 5 s x (10/7)^2 = 10.2 s, rounded up the 10-30 s ladder to 11 s.
            Assert.That(vm.RecommendedExposureText, Is.EqualTo("5 s → 11 s (measured star S/N 7; target 10)"));
            Assert.That(vm.ExposureBodyText, Does.Not.Contain("NINA's focuser options"),
                "the Replay-only 'set it by hand' instruction must not appear on a Live run");
        });
    }

    [Test]
    public async Task HealthyGate_HidesTheStarSignalBlockEntirely() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.Summary.VariantSensitivity, Is.GreaterThan(ExposureRecommender.SensitivityFloorThreshold),
            "fixture guard: this run's gate must land well clear of the floor");
        Assert.Multiple(() => {
            Assert.That(vm.HasExposureBlock, Is.False, "no new noise on the happy path");
            Assert.That(vm.LowSignalChartNote, Is.Empty);
            Assert.That(vm.RecommendedExposureText, Is.Empty);
            Assert.That(vm.ExposureBodyText, Is.Empty);
        });
    }

    [Test]
    public async Task StarSignalBlock_FollowsTheSelectedVariant_AndIsRenotifiedOnTheSwitch() {
        // The optimized gate floored; the user's own gate (10) did not. Toggling to Current must therefore hide the
        // block — and must SAY so: these are computed properties, so a missing notification leaves the binding
        // showing the previous variant's verdict while the value read from a test looks correct.
        var vm = NewVM(LoaderReturning(StarvedRun()));
        vm.SourcePaths[0] = @"C:\fake\attempt";
        await vm.StartAsync(CancellationToken.None);
        // Assert, not Assume: NUnit reports a failed Assume as Inconclusive, which `dotnet test` does not fail
        // on — so a broken fixture would silently disable the re-notification assertion below while the suite
        // stayed green. Assume is for environmental preconditions, not for the premise of the test.
        Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Optimized), "fixture guard");
        Assert.That(vm.HasExposureBlock, Is.True, "fixture guard");

        var notified = 0;
        vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.HasExposureBlock)) { notified++; } };
        vm.SelectedVariant = OptimizationVariant.Current;

        Assert.Multiple(() => {
            Assert.That(notified, Is.GreaterThan(0), "bindings only re-read on notification");
            Assert.That(vm.HasExposureBlock, Is.False, "the Current view's own gate is healthy");
            Assert.That(vm.LowSignalChartNote, Is.Empty);
        });
    }

    [Test]
    public async Task Replay_NoKnownExposure_GivesTheDiagnosisWithoutInventingAFigure() {
        // Neither the frames nor the profile yield a positive exposure. Never scale a factor off an unknown base:
        // the block still names the problem, it just has no seconds to offer.
        var vm = NewVM(LoaderReturning(StarvedRun(capturedExposureSeconds: double.NaN)));
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.Summary.VariantSensitivity, Is.LessThanOrEqualTo(ExposureRecommender.SensitivityFloorThreshold),
            "fixture guard");
        Assert.Multiple(() => {
            Assert.That(vm.HasExposureBlock, Is.True);
            Assert.That(vm.HasRecommendedExposure, Is.False);
            Assert.That(vm.RecommendedExposureText, Is.Empty);
            Assert.That(vm.ExposureBodyText, Does.Contain("low-confidence detections"));
            Assert.That(vm.ExposureBodyText, Does.Not.Contain("acceptance level"));
        });
    }

    [Test]
    public async Task Replay_NoRecordedExposure_FallsBackToTheProfileAutoFocusExposure() {
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        focuserSettings.AutoFocusExposureTime.Returns(3.0);
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);
        var vm = NewVM(LoaderReturning(StarvedRun(capturedExposureSeconds: double.NaN)), profileService: profileService);
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.Summary.VariantSensitivity, Is.LessThanOrEqualTo(ExposureRecommender.SensitivityFloorThreshold),
            "fixture guard");
        Assert.Multiple(() => {
            Assert.That(vm.RecommendedExposureText, Is.EqualTo("3 s → 6.5 s (measured star S/N 7; target 10)"),
                "with no exposure in the frames, the profile's auto-focus exposure is the base");
            // The ONLY coverage of RunExposureIsAssumed's production writer: everything else asserts it on a
            // hand-built summary or through BuildCurrentSummary's carry-over, so hard-coding the flag false in
            // BuildSummaryAsync used to pass the whole suite. A derived number computed off an assumed input must
            // say so in visible copy.
            Assert.That(vm.ExposureBodyText, Does.Contain("assumes your profile's 3 s auto-focus exposure"));
        });
    }

    // ---- Star signal: capture a new sweep at the recommended exposure ------------------------------------
    //
    // The Live counterpart of "Optimize again at NxN": that action re-reads frames already on disk, this one
    // replaces them. Every run path DISPOSES the RunEvaluationData it was handed, so a test that runs twice
    // (Start, then a fresh capture) cannot reuse one LoadedRun — hence a loader that manufactures one per call,
    // the same reason RecordingLoader exists for the re-optimize tests.

    private sealed class ProducingLoader : IRunEvaluationLoader {
        private readonly Func<string, LoadedRun> factory;

        /// <summary>Every run handed out, in load order — the VM stamps these AFTER the loader returns them, so a
        /// test can read the recovery/binning stamp off them post-flow.</summary>
        public List<LoadedRun> Produced { get; } = new List<LoadedRun>();

        /// <summary>Every folder asked for, in load order.</summary>
        public List<string> Folders { get; } = new List<string>();

        public ProducingLoader(Func<string, LoadedRun> factory) {
            this.factory = factory;
        }

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, null, null, null, token);

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels, null, null, token);

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels, progress, null, token);

        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, IStarDetectionOptions baselineOptionsOverride, CancellationToken token) {
            Folders.Add(attemptFolderPath);
            var run = factory(attemptFolderPath);
            Produced.Add(run);
            return Task.FromResult(run);
        }
    }

    private static ProducingLoader StarvedLoader(bool largeStars = false, double baselineSensitivity = 10.0) =>
        new ProducingLoader(folder => StarvedRun(
            id: "starved::" + folder, largeStars: largeStars, baselineSensitivity: baselineSensitivity));

    // A Live, signal-starved wizard sitting on the Summary: SweepExposureSeconds 5 s and a measured S/N of 7 make
    // the recommendation 5 x (10/7)^2 = 10.2 s, rounded up the 10-30 s ladder to 11 s — a genuine increase, so the
    // capture action is on offer. Optimize mode (NOT NewLiveVM's use-current), since the whole point is re-tuning.
    private static StarDetectionOptimizerWizardVM NewStarvedLiveVM(
        IAutoFocusEngine engine, IRunEvaluationLoader loader,
        IStarDetectionOptions options = null, IProfileService profileService = null,
        Func<double, double, bool> confirmCaptureNewSweep = null) {
        var vm = NewVM(loader, options, profileService,
            isCameraConnected: () => true, isFocuserConnected: () => true,
            autoFocusEngine: engine, confirmCaptureNewSweep: confirmCaptureNewSweep);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 5.0;
        return vm;
    }

    private static IAutoFocusEngine LiveEngineReturningTasks(Func<CallInfo, Task<AutoFocusResult>> onSweep) {
        var engine = Substitute.For<IAutoFocusEngine>();
        engine.GetOptions().Returns(_ => new AutoFocusEngineOptions());
        engine.CaptureFixedSweepAsync(default, default, default, default).ReturnsForAnyArgs(ci => onSweep(ci));
        return engine;
    }

    private static AutoFocusResult SweptOk(int n = 0) =>
        new AutoFocusResult { Succeeded = true, SaveFolder = $@"C:\live\attempt{n}" };

    [Test]
    public async Task CaptureNewSweep_IsOfferedAfterAStarvedLiveRun_AndPrefilledWithTheRecommendation() {
        var engine = LiveEngine(_ => SweptOk());
        var vm = NewStarvedLiveVM(engine, StarvedLoader());

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.HasExposureBlock, Is.True, "fixture guard: the gate must actually floor");
        Assert.Multiple(() => {
            Assert.That(vm.ShowCaptureNewSweep, Is.True);
            Assert.That(vm.CanCaptureNewSweep, Is.True);
            Assert.That(vm.CaptureNewSweepCommand.CanExecute(null), Is.True,
                "the command is asked from inside the run, while IsBusy is still true - it must be re-asked after");
            Assert.That(vm.RecaptureExposureSeconds, Is.EqualTo(11.0),
                "5 s x (10/7)^2 = 10.2 s, rounded up the 10-30 s ladder");
            // The body names the action; the number lives in the row and the box beside the button.
            Assert.That(vm.ExposureBodyText, Does.Contain("Capture a new sweep at the longer exposure"));
            Assert.That(vm.ExposureBodyText, Does.Not.Contain("NINA's focuser options"));
        });
    }

    [Test]
    public async Task CaptureNewSweep_Declined_CapturesNothingAndLeavesTheSweepExposureAlone() {
        // The dialog is the last chance to back out of minutes of sky time, so declining must be inert - in
        // particular it must NOT leave LiveExposureSeconds armed at the recommendation for some later sweep.
        var captures = 0;
        var engine = LiveEngine(_ => { captures++; return SweptOk(captures); });
        var vm = NewStarvedLiveVM(engine, StarvedLoader(), confirmCaptureNewSweep: (from, to) => false);

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.ShowCaptureNewSweep, Is.True, "fixture guard");
        var capturesAfterStart = captures;
        var summaryAfterStart = vm.Summary;

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(captures, Is.EqualTo(capturesAfterStart), "declining must not capture");
            Assert.That(vm.LiveExposureSeconds, Is.EqualTo(5.0), "and must not arm the sweep exposure");
            Assert.That(vm.Summary, Is.SameAs(summaryAfterStart), "nor disturb the summary");
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.IsBusy, Is.False, "the interlock must be released on the declined path too");
        });
    }

    [Test]
    public async Task CaptureNewSweep_CapturesAtTheRecommendedExposure() {
        var captured = new List<AutoFocusEngineOptions>();
        var engine = LiveEngine(ci => { captured.Add(ci.Arg<AutoFocusEngineOptions>()); return SweptOk(captured.Count); });
        var vm = NewStarvedLiveVM(engine, StarvedLoader());

        await vm.StartAsync(CancellationToken.None);
        Assert.That(captured, Has.Count.EqualTo(1), "fixture guard: the Start sweep");
        Assert.That(captured[0].OverrideAutoFocusExposureTime, Is.EqualTo(TimeSpan.FromSeconds(5.0)));

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(captured, Has.Count.EqualTo(2), "the action captures a fresh sweep");
            Assert.That(captured[1].OverrideAutoFocusExposureTime, Is.EqualTo(TimeSpan.FromSeconds(11.0)),
                "the sweep is taken at the box's exposure, not the one the previous run used");
            Assert.That(captured[1].Save, Is.True);
            Assert.That(captured[1].SavePath, Is.EqualTo(@"C:\live"));
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task CaptureNewSweep_EditedExposure_BeatsTheRecommendation() {
        // The box is editable precisely because the recommendation is an extrapolation. Whatever is in it at click
        // time is what gets captured.
        var captured = new List<AutoFocusEngineOptions>();
        var engine = LiveEngine(ci => { captured.Add(ci.Arg<AutoFocusEngineOptions>()); return SweptOk(captured.Count); });
        var vm = NewStarvedLiveVM(engine, StarvedLoader());
        await vm.StartAsync(CancellationToken.None);

        vm.RecaptureExposureSeconds = 20.0;
        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.That(captured[1].OverrideAutoFocusExposureTime, Is.EqualTo(TimeSpan.FromSeconds(20.0)));
    }

    [Test]
    public async Task CaptureNewSweep_HonorsRunCount() {
        // A single-shot re-capture would silently drop runs 2..N and then compare a 1-run result against the
        // 2-run baseline the summary was built from.
        var captures = 0;
        var engine = LiveEngine(_ => { captures++; return SweptOk(captures); });
        var vm = NewStarvedLiveVM(engine, StarvedLoader());
        vm.RunCount = 2;

        await vm.StartAsync(CancellationToken.None);
        Assert.That(captures, Is.EqualTo(2), "fixture guard: Start captures one sweep per run");
        Assert.That(vm.Summary.RunCount, Is.EqualTo(2));

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(captures, Is.EqualTo(4), "the re-capture takes RunCount sweeps, not one");
            Assert.That(vm.Summary.RunCount, Is.EqualTo(2), "and the summary still describes both");
        });
    }

    [Test]
    public async Task CaptureNewSweep_PreservesAPendingOptimizeAgainBinningFactor() {
        // pendingDetectionBinning is deliberately NOT cleared: the user is mid-way through evaluating 2x2, and the
        // fresh frames must be analyzed at the factor Accept would write. Clearing it would silently drop them back
        // to 1x1 while the summary still offered to apply 2x2.
        var options = Substitute.For<IStarDetectionOptions>();
        options.DetectionBinning.Returns(DetectionBinningEnum.Bin1);
        var engine = LiveEngine(_ => SweptOk());
        var vm = NewStarvedLiveVM(engine, StarvedLoader(largeStars: true), options);

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.ShowOptimizeAgainAtRecommendedBinning, Is.True,
            "fixture guard: the 5 px stars must call for 2x2, or there is no pending factor to preserve");
        Assert.That(vm.HasExposureBlock, Is.True, "fixture guard: and the gate must still floor");

        await vm.OptimizeAgainAtRecommendedBinningCommand.ExecuteAsync(null);
        Assert.That(vm.Summary.RunDetectionBinning, Is.EqualTo(2), "fixture guard: the pending factor is in force");
        Assert.That(vm.ShowCaptureNewSweep, Is.True, "fixture guard: the capture action survives the re-optimize");

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.Summary.RunDetectionBinning, Is.EqualTo(2),
                "the freshly captured frames are analyzed at the pending factor");
            Assert.That(vm.Summary.DetectionBinningPendingApply, Is.True,
                "so Accept still writes the factor with the settings measured at it");
        });
    }

    [Test]
    public async Task CaptureNewSweep_UsesTheSnapshottedRecoverySteps_NotTheLiveBox() {
        // LoadRunStampedAsync's contract: every load after the originating Start reuses that Start's snapshot, so
        // the widened CAPTURE and the recovery TAGGING can never disagree. Re-reading the box here would widen the
        // sweep by 5 while the evaluator still tagged 1 frame per side as recovery.
        var captured = new List<AutoFocusEngineOptions>();
        var engine = LiveEngine(ci => { captured.Add(ci.Arg<AutoFocusEngineOptions>()); return SweptOk(captured.Count); });
        var vm = NewStarvedLiveVM(engine, StarvedLoader());
        vm.FocusRecoverySteps = 1;

        await vm.StartAsync(CancellationToken.None);
        Assert.That(captured[0].AutoFocusInitialOffsetSteps, Is.EqualTo(1), "fixture guard: 0 profile offset + 1 recovery");

        vm.FocusRecoverySteps = 5;
        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.That(captured[1].AutoFocusInitialOffsetSteps, Is.EqualTo(1),
            "the re-capture widens by the SNAPSHOT (1), not by the edited box (5)");
    }

    [Test]
    public async Task CaptureNewSweep_FailedCapture_RestoresTheExposureAndLeavesTheSummaryIntact() {
        var fail = false;
        var engine = LiveEngine(_ => fail ? throw new InvalidOperationException("focuser exploded") : SweptOk());
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);
        var vm = NewStarvedLiveVM(engine, StarvedLoader(), profileService: profileService);

        await vm.StartAsync(CancellationToken.None);
        var summaryAfterStart = vm.Summary;
        var exposureRowAfterStart = vm.SweepExposureChangeText;

        fail = true;
        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("focuser exploded"));
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "the previous result is still valid");
            Assert.That(vm.Summary, Is.SameAs(summaryAfterStart), "and untouched");
            Assert.That(vm.LiveExposureSeconds, Is.EqualTo(5.0), "the pending sweep exposure is rolled back");
            // RunLiveAttemptAsync writes capturedLiveExposureSeconds BEFORE the sweep can fail, so restoring only
            // LiveExposureSeconds would leave the intact summary reporting - and Accept writing - an exposure
            // nothing was ever captured at.
            Assert.That(vm.SweepExposureChangeText, Is.EqualTo(exposureRowAfterStart));
            Assert.That(vm.SweepExposureChangeText, Does.Not.Contain("11 s"));
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.True, "a failed extra capture must not block Accept");
            Assert.That(vm.IsBusy, Is.False);
        });

        vm.AcceptCommand.Execute(null);
        focuserSettings.DidNotReceive().AutoFocusExposureTime = 11.0;
        focuserSettings.Received().AutoFocusExposureTime = 5.0;
    }

    [Test]
    public async Task CaptureNewSweep_Cancelled_RestoresTheExposureWithoutAnError() {
        // The cancel path takes a different catch clause than the failure path, and only shares the finally. A
        // restore written into the exception handler instead of the finally would pass the test above and leave the
        // exposure armed here.
        var cancel = false;
        var engine = LiveEngine(_ => cancel ? throw new OperationCanceledException() : SweptOk());
        var vm = NewStarvedLiveVM(engine, StarvedLoader());

        await vm.StartAsync(CancellationToken.None);
        var summaryAfterStart = vm.Summary;

        cancel = true;
        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.Null, "a cancel is a user choice, not a failure");
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.Summary, Is.SameAs(summaryAfterStart));
            Assert.That(vm.LiveExposureSeconds, Is.EqualTo(5.0));
            Assert.That(vm.SweepExposureChangeText, Does.Not.Contain("11 s"));
            Assert.That(vm.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task CaptureNewSweep_SweepProducedNoFrames_KeepsTheUserOnTheSummary() {
        // A failed/partial sweep can still return a SaveFolder; "not succeeded" means no capture. Unlike Start
        // (which drops back to Select Source) this is an OPTIONAL extra capture, so the previous result stands.
        var succeed = true;
        var engine = LiveEngine(_ => succeed ? SweptOk() : new AutoFocusResult { Succeeded = false, SaveFolder = @"C:\live\attempt" });
        var vm = NewStarvedLiveVM(engine, StarvedLoader());

        await vm.StartAsync(CancellationToken.None);
        var summaryAfterStart = vm.Summary;

        succeed = false;
        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Does.Contain("did not produce"));
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.Summary, Is.SameAs(summaryAfterStart));
            Assert.That(vm.LiveExposureSeconds, Is.EqualTo(5.0));
        });
    }

    [Test]
    public async Task CaptureNewSweep_RepointsEveryLaterReloadAtTheFreshFrames() {
        // The snapshot at the end of the capture must carry the FRESH folders. Handing it the previous run's would
        // leave Continue / re-optimize / Review all re-reading the starved frames this action exists to replace -
        // and nothing on screen would say so, since the summary would describe the new capture.
        var captures = 0;
        var engine = LiveEngine(_ => SweptOk(++captures));
        var loader = StarvedLoader();
        var vm = NewStarvedLiveVM(engine, loader);

        await vm.StartAsync(CancellationToken.None);
        Assert.That(loader.Folders, Is.EqualTo(new[] { @"C:\live\attempt1" }), "fixture guard: the Start sweep's folder");

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);
        Assert.That(loader.Folders.Last(), Is.EqualTo(@"C:\live\attempt2"), "fixture guard: the fresh sweep's folder");

        // Continue re-loads from the snapshot, so it is the honest probe for what the snapshot now holds.
        Assert.That(vm.CanContinueOptimization, Is.True, "fixture guard");
        await vm.ContinueOptimizationCommand.ExecuteAsync(null);

        Assert.That(loader.Folders.Last(), Is.EqualTo(@"C:\live\attempt2"),
            "the reload must reach for the frames the capture just took, not the starved ones it replaced");
    }

    [Test]
    public async Task CaptureNewSweep_ResetsTheRoundChainAndDropsTheFeedbackVariant() {
        // Fresh frames at a different exposure are a fresh tuning, not another round: splicing them onto the
        // Continue trajectory would show a per-round path measured across two different sets of exposures, and the
        // feedback variant's labels were drawn on images that no longer describe this result.
        var engine = LiveEngine(_ => SweptOk());
        var vm = NewStarvedLiveVM(engine, StarvedLoader());

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CanContinueOptimization, Is.True, "fixture guard");
        await vm.ContinueOptimizationCommand.ExecuteAsync(null);
        Assert.That(vm.RoundsCompleted, Is.EqualTo(2), "fixture guard: a second round is on the chain");

        // Building a REAL feedback variant needs a full Review round-trip with labels; the reset is what is under
        // test, so the variant is planted directly.
        typeof(StarDetectionOptimizerWizardVM)
            .GetField("feedbackResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(vm, new OptimizationResult { BestParams = new StarDetectorParams(), BestJ = 0.5, SeedJ = 0.5 });
        Assert.That(vm.HasFeedback, Is.True, "fixture guard");

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
            Assert.That(vm.RoundsCompleted, Is.EqualTo(0), "the chain restarts from this capture");
            Assert.That(vm.HasFeedback, Is.False, "the feedback variant was measured on the discarded frames");
            Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Optimized));
            Assert.That(vm.HasRoundsSummary, Is.False, "no multi-round header to splice");
        });
    }

    [Test]
    public async Task CaptureNewSweep_UpdatesTheAcceptWriteBack() {
        // capturedLiveExposureSeconds is updated by RunLiveAttemptAsync, so the summary's exposure row and Accept's
        // profile write follow with no extra plumbing - which is exactly why it needs a test.
        var profileService = Substitute.For<IProfileService>();
        var focuserSettings = Substitute.For<IFocuserSettings>();
        profileService.ActiveProfile.FocuserSettings.Returns(focuserSettings);
        var engine = LiveEngine(_ => SweptOk());
        var vm = NewStarvedLiveVM(engine, StarvedLoader(), profileService: profileService);

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.SweepExposureChangeText, Does.Contain("5 s"), "fixture guard: the Start sweep's exposure");

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);

        Assert.Multiple(() => {
            Assert.That(vm.SweepExposureChangeText, Does.Contain("11 s"),
                "the summary reports what the LATEST capture actually used");
            Assert.That(vm.CanApplyExposureTime, Is.True);
            Assert.That(vm.ApplyRecommendedStepSize, Is.True, "an exposure change alone makes the AF toggle meaningful");
        });

        vm.AcceptCommand.Execute(null);

        focuserSettings.Received().AutoFocusExposureTime = 11.0;
    }

    [Test]
    public async Task CaptureNewSweep_IsBlockedWhileAnotherRunIsInFlight() {
        // The `running` interlock, not IsBusy: the command's CanExecute is bypassed by a direct ExecuteAsync (and
        // by a double-click that lands before the requery), so re-entrancy has to be refused inside the method.
        var gate = new TaskCompletionSource<AutoFocusResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = false;
        var captures = 0;
        var engine = LiveEngineReturningTasks(_ => {
            captures++;
            return block ? gate.Task : Task.FromResult(SweptOk(captures));
        });
        var vm = NewStarvedLiveVM(engine, StarvedLoader());

        await vm.StartAsync(CancellationToken.None);
        Assert.That(captures, Is.EqualTo(1), "fixture guard");

        block = true;
        var inFlight = vm.CaptureNewSweepCommand.ExecuteAsync(null);
        Assert.That(vm.IsBusy, Is.True, "fixture guard: the first capture is parked inside the sweep");
        Assert.That(captures, Is.EqualTo(2), "fixture guard");

        await vm.CaptureNewSweepCommand.ExecuteAsync(null);
        Assert.That(captures, Is.EqualTo(2), "the second click must not start a second sweep");

        gate.SetResult(SweptOk(2));
        await inFlight;
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary), "and the first capture still completes normally");
    }

    [Test]
    public async Task CaptureNewSweep_DisconnectedCamera_LeavesTheRowVisibleButTheButtonDisabled() {
        // The Show/Can split: a device that dropped while the user read the summary is TEMPORARILY unavailable, so
        // the affordance stays on screen (disabled) rather than vanishing out from under the copy that names it.
        var connected = true;
        var engine = LiveEngine(_ => SweptOk());
        var vm = NewVM(StarvedLoader(), isCameraConnected: () => connected, isFocuserConnected: () => true,
            autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 5.0;

        await vm.StartAsync(CancellationToken.None);
        Assert.That(vm.CanCaptureNewSweep, Is.True, "fixture guard");

        connected = false;

        Assert.Multiple(() => {
            Assert.That(vm.ShowCaptureNewSweep, Is.True, "the row stays visible");
            Assert.That(vm.CanCaptureNewSweep, Is.False, "but the action cannot run");
            Assert.That(vm.CaptureNewSweepCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task CaptureNewSweep_Live_UseCurrentMode_IsHiddenNotDisabled_AndTheCopyNamesTheModeInstead() {
        // The state is genuinely reachable: BuildSummaryAsync derives ExposureAdvice regardless of OptimizeMode, so
        // a Live use-current run whose OWN hand-set gate is floored lands on a Summary with a real increase on
        // offer. Use-current is chosen on the Select Source step and is FIXED for the life of this Summary, so a
        // button disabled by it would sit dead for the whole run with nothing on screen saying why — it is hidden,
        // and the body names the mode to switch to. (An earlier revision had it in CanCaptureNewSweep, which is
        // exactly the dead-button outcome; no test covered this combination, which is why nothing caught it.)
        var engine = LiveEngine(_ => SweptOk());
        // The user's OWN gate sits at the floor: use-current never runs the search, so the Current variant's gate
        // is the only one there is, and a healthy one would hide the whole block.
        var loader = StarvedLoader(baselineSensitivity: 0.5);
        var vm = NewVM(loader, isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine);
        vm.SourceMode = SourceMode.Live;
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;
        vm.SaveFolderPath = @"C:\live";
        vm.LiveExposureSeconds = 5.0;

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
        Assert.That(vm.SelectedVariant, Is.EqualTo(OptimizationVariant.Current), "fixture guard: use-current never optimizes");
        Assert.That(vm.HasExposureBlock, Is.True,
            "fixture guard: the user's own gate must be floored, or there is no block to hang the action off");
        Assert.That(vm.Summary.ExposureAdvice.IncreasesExposure, Is.True,
            "fixture guard: there must really be a longer exposure on offer, or the row would be hidden anyway");
        Assert.Multiple(() => {
            Assert.That(vm.ShowCaptureNewSweep, Is.False, "hidden, not disabled - the mode cannot change from here");
            Assert.That(vm.CanCaptureNewSweep, Is.False);
            Assert.That(vm.CaptureNewSweepCommand.CanExecute(null), Is.False);
            Assert.That(vm.ExposureBodyText, Does.Not.Contain("Capture a new sweep at the longer exposure to re-tune"),
                "the copy must not name a button that is not on screen");
            Assert.That(vm.ExposureBodyText, Does.Contain("run this wizard in Optimize mode"),
                "it names the mode to switch to instead - the binning block's use-current branch");
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public async Task CaptureNewSweep_Replay_IsNotOfferedAtAll_AndAcceptStaysEnabled() {
        // Replay has no rig to re-capture from, so the action is mode-inapplicable and the row is HIDDEN (the
        // binning block's use-current branch). The Star signal block is a warning, never a veto: Accept must stay
        // available exactly as it does without this action.
        var vm = NewVM(LoaderReturning(StarvedRun()));
        vm.SourcePaths[0] = @"C:\fake\attempt";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.HasExposureBlock, Is.True, "fixture guard: the block itself is on screen");
        Assert.Multiple(() => {
            Assert.That(vm.ShowCaptureNewSweep, Is.False);
            Assert.That(vm.CanCaptureNewSweep, Is.False);
            Assert.That(vm.CaptureNewSweepCommand.CanExecute(null), Is.False);
            Assert.That(vm.ExposureBodyText, Does.Not.Contain("Capture a new sweep"),
                "and the copy must not describe a control that is not on screen");
            Assert.That(vm.AcceptCommand.CanExecute(null), Is.True);
        });
    }
}
