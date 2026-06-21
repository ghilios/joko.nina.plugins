#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;
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

        public RecordingLoader(double optSensitivity = 10.0, int seedSensitivity = 2) {
            this.optSensitivity = optSensitivity;
            this.seedSensitivity = seedSensitivity;
        }

        // The wizard sets RunId = attempt.FolderPath; here we mirror that by stamping the run id from the folder, so
        // the runId→labels match in ReOptimizeWithLabelsAsync resolves by id (not the positional fallback).
        private static string RunIdFor(string folder) => "run::" + folder;

        private LoadedRun Make(string folder, IReadOnlyList<FrameLabels> labels) {
            var data = new RunEvaluationData(RunIdFor(folder), NineFrames(), OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig(), labels);
            return new LoadedRun {
                Data = data,
                Seed = new StarDetectorParams { Sensitivity = seedSensitivity, StarClippingMultiplier = 2.0 },
                AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
            };
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
    }

    private static StarDetectionOptimizerWizardVM NewVM(
        IRunEvaluationLoader loader,
        IStarDetectionOptions options = null,
        IProfileService profileService = null,
        Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>> frameReviewBuilder = null,
        Func<bool> isCameraConnected = null,
        Func<bool> isFocuserConnected = null) {
        options ??= Substitute.For<IStarDetectionOptions>();
        profileService ??= Substitute.For<IProfileService>();
        return new StarDetectionOptimizerWizardVM(
            profileService,
            options,
            loader,
            autoFocusEngine: Substitute.For<IAutoFocusEngine>(),
            folderPicker: () => @"C:\fake\attempt",
            region: StarDetectionRegion.Full,
            optimizerSettings: new OptimizerSettings { MaxEvaluations = 200, CoarseGridLevels = 4, StepFloorFraction = 0.125 },
            frameReviewBuilder: frameReviewBuilder,
            isCameraConnected: isCameraConnected,
            isFocuserConnected: isFocuserConnected);
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
            loader.Received(2).LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<CancellationToken>());
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
    public void CanStart_LiveMode_EnabledEvenWithoutPaths() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        vm.SourcePaths[0] = null;
        vm.SourceMode = SourceMode.Live;
        Assert.That(vm.StartCommand.CanExecute(null), Is.True, "Live needs no source paths");
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
}
