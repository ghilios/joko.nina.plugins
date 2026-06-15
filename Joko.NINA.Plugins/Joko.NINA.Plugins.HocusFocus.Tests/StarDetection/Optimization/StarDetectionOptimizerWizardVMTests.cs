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
        return loader;
    }

    private static StarDetectionOptimizerWizardVM NewVM(
        IRunEvaluationLoader loader,
        IStarDetectionOptions options = null,
        IProfileService profileService = null) {
        options ??= Substitute.For<IStarDetectionOptions>();
        profileService ??= Substitute.For<IProfileService>();
        return new StarDetectionOptimizerWizardVM(
            profileService,
            options,
            loader,
            autoFocusEngine: Substitute.For<IAutoFocusEngine>(),
            folderPicker: () => @"C:\fake\attempt",
            region: StarDetectionRegion.Full,
            optimizerSettings: new OptimizerSettings { MaxEvaluations = 200, CoarseGridLevels = 4, StepFloorFraction = 0.125 });
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
    public async Task Apply_CallsApplyOptimizedSettingsWithCuratedValuesAndMetadata() {
        var options = Substitute.For<IStarDetectionOptions>();
        var vm = NewVM(LoaderReturning(GoodRun()), options);
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.ApplyCommand.Execute(null);

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

        vm.ApplyCommand.Execute(null);

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

        vm.ApplyCommand.Execute(null);

        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusStepSize = default;
        focuserSettings.DidNotReceiveWithAnyArgs().AutoFocusInitialOffsetSteps = default;
        // The optimized settings DTO must still be applied even when the step size is declined.
        options.Received(1).ApplyOptimizedSettings(Arg.Any<OptimizedStarDetectionSettings>());
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
            loader.Received(2).LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<CancellationToken>());
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
}
