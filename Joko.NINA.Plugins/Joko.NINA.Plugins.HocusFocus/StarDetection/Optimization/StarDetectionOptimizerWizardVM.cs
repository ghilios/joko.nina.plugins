#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.FileFormat.FITS;
using NINA.Image.FileFormat.XISF;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;
using Logger = NINA.Core.Utility.Logger;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>The wizard's linear steps. The window renders a single panel keyed off <see cref="CurrentStep"/>.</summary>
    public enum WizardStep {
        SelectSource,
        Acquire,
        Optimize,
        Summary,

        /// <summary>Optional post-Summary step: review the optimized detector boxes over ALL loaded frames and label
        /// missed / should-reject / wrongly-rejected stars. Reachable from Summary; returns to Summary or Accepts.</summary>
        Review
    }

    /// <summary>Where the run frames come from: an already-saved AF attempt (Replay) or a fresh live AF run.</summary>
    public enum SourceMode {
        Replay,
        Live
    }

    /// <summary>One row of the summary's changed-parameters table (seed vs optimized for a single knob).</summary>
    public sealed class ChangedParameterRow {
        public string Name { get; set; }
        public double SeedValue { get; set; }
        public double OptimizedValue { get; set; }
    }

    /// <summary>
    /// The fully-computed summary shown after a successful optimization: the changed-parameters table, the
    /// before/after objective and σ(focus), and the recommended AF step size/offset.
    /// </summary>
    public sealed class OptimizationSummary {
        public IReadOnlyList<ChangedParameterRow> ChangedParameters { get; set; }
        public double SeedJ { get; set; }
        public double BestJ { get; set; }
        public double SeedSigmaFocus { get; set; }
        public double BestSigmaFocus { get; set; }
        public int RunCount { get; set; }
        public int RecommendedStepSize { get; set; }
        public int RecommendedOffsetSteps { get; set; }
        public int CurrentStepSize { get; set; }
        public bool ImprovedOverSeed { get; set; }
    }

    /// <summary>
    /// ViewModel for the Star Detection Optimization Wizard. It is a plain <see cref="BaseINPC"/> (not a
    /// DockableVM): T5 instantiates it on demand and shows it in a window via <c>IWindowServiceFactory</c>,
    /// resolving the content from the keyed DataTemplate. The VM owns the four-step state machine, loads one or
    /// more saved AF runs through an injected <see cref="IRunEvaluationLoader"/>, validates the seed fit (the
    /// STARHFR/seed guard), runs the pure <see cref="StarDetectionOptimizer"/> off the UI thread, builds the
    /// summary, and applies the result to <see cref="IStarDetectionOptions"/> (and optionally the profile's AF
    /// step size) only when the user clicks Apply.
    ///
    /// <para>All optimizer/loader work runs off the captured UI context (via <see cref="Task.Run(Func{Task})"/>);
    /// progress is marshaled back through <see cref="IProgress{T}"/>, which posts to the captured
    /// <see cref="SynchronizationContext"/>.</para>
    /// </summary>
    public class StarDetectionOptimizerWizardVM : BaseINPC, IDisposable {
        private readonly IProfileService profileService;
        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IRunEvaluationLoader loader;
        private readonly IAutoFocusEngine autoFocusEngine;
        private readonly Func<string> folderPicker;
        private readonly StarDetectionRegion region;
        private readonly OptimizerSettings optimizerSettings;

        // The review-build seam: given the snapshotted frame descriptors + the params to detect with, produces the
        // per-frame FrameReviews the StarReviewVM renders. Production wires FrameReviewBuilder over a real
        // StarDetector + a profile-aware disk loader; the unit tests inject a fake so the step-flow can be exercised
        // without real images.
        private readonly Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, CancellationToken, Task<List<FrameReview>>> frameReviewBuilder;

        // Snapshotted at the end of a successful run (BEFORE loadedRuns is disposed): the Mat-free per-frame
        // descriptors for every loaded run, the labels dir each run's labels persist to, and the in-memory label
        // models the review edits. These survive disposal so the Review step can detect from disk afterward.
        private IReadOnlyList<FrameReviewDescriptor> reviewDescriptors;
        private string reviewLabelsDir;

        private CancellationTokenSource cts;
        private int running; // 0 = idle, 1 = a Start is in flight (guards against double-start)
        private bool disposed;

        /// <summary>
        /// MEF/T5 convenience constructor: wires the real collaborators from the plugin singletons + mediators.
        /// The wizard is not MEF-exported (it is created on demand by T5's launch command), so this just supplies
        /// production dependencies to the primary constructor.
        /// </summary>
        public StarDetectionOptimizerWizardVM(
            IProfileService profileService,
            IImageDataFactory imageDataFactory,
            IImagingMediator imagingMediator,
            IAutoFocusEngine autoFocusEngine,
            IHocusFocusStarDetection detection)
            : this(
                profileService,
                HocusFocusPlugin.StarDetectionOptions,
                new RunEvaluationLoader(profileService, imageDataFactory, imagingMediator, autoFocusEngine, detection),
                autoFocusEngine,
                folderPicker: PickFolderViaDialog,
                // The optimizer's detection mirrors AF detection on the full image; the loader's own default is
                // StarDetectionRegion.Full as well. See the type doc for why Full is used.
                region: StarDetectionRegion.Full,
                optimizerSettings: new OptimizerSettings(),
                // Production review builder: the SHARED FrameReviewBuilder over a real StarDetector + a profile-aware
                // disk loader (mirrors the TestApp `review` path; single source of truth). Built lazily so the
                // detector/loader only allocate when the user actually enters Review.
                frameReviewBuilder: BuildProductionReviewBuilder(profileService, imageDataFactory)) {
        }

        /// <summary>
        /// Primary constructor (interfaces only) used by both the convenience constructor and the unit tests.
        /// </summary>
        public StarDetectionOptimizerWizardVM(
            IProfileService profileService,
            IStarDetectionOptions starDetectionOptions,
            IRunEvaluationLoader loader,
            IAutoFocusEngine autoFocusEngine,
            Func<string> folderPicker,
            StarDetectionRegion region,
            OptimizerSettings optimizerSettings,
            Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, CancellationToken, Task<List<FrameReview>>> frameReviewBuilder = null) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.starDetectionOptions = starDetectionOptions ?? throw new ArgumentNullException(nameof(starDetectionOptions));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.autoFocusEngine = autoFocusEngine;
            this.folderPicker = folderPicker ?? (() => null);
            this.region = region ?? StarDetectionRegion.Full;
            this.optimizerSettings = optimizerSettings ?? new OptimizerSettings();
            this.frameReviewBuilder = frameReviewBuilder;

            sourcePaths = new ObservableCollection<string> { null };
            applyRecommendedStepSize = true;

            BrowseSourceCommand = new RelayCommand<object>(BrowseSource);
            StartCommand = new AsyncRelayCommand(() => StartAsync(CancellationToken.None));
            CancelCommand = new RelayCommand(Cancel);
            AcceptCommand = new RelayCommand(Accept, () => Summary != null && !IsBusy);
            BackCommand = new RelayCommand(Back, () => CurrentStep == WizardStep.Summary && !IsBusy);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
            ReviewCommand = new AsyncRelayCommand(EnterReviewAsync, CanEnterReview);
            BackToSummaryCommand = new RelayCommand(BackToSummary, () => CurrentStep == WizardStep.Review && !IsBusy);
        }

        /// <summary>Raised when the user clicks Close so the host window (T5) can dismiss the dialog.</summary>
        public event EventHandler RequestClose;

        #region State

        private WizardStep currentStep = WizardStep.SelectSource;

        public WizardStep CurrentStep {
            get => currentStep;
            private set {
                if (currentStep != value) {
                    currentStep = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsSelectSource));
                    RaisePropertyChanged(nameof(IsAcquire));
                    RaisePropertyChanged(nameof(IsOptimize));
                    RaisePropertyChanged(nameof(IsSummary));
                    RaisePropertyChanged(nameof(IsReview));
                    BackCommand.NotifyCanExecuteChanged();
                    ReviewCommand.NotifyCanExecuteChanged();
                    BackToSummaryCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsSelectSource => CurrentStep == WizardStep.SelectSource;
        public bool IsAcquire => CurrentStep == WizardStep.Acquire;
        public bool IsOptimize => CurrentStep == WizardStep.Optimize;
        public bool IsSummary => CurrentStep == WizardStep.Summary;
        public bool IsReview => CurrentStep == WizardStep.Review;

        private SourceMode sourceMode = SourceMode.Replay;

        public SourceMode SourceMode {
            get => sourceMode;
            set {
                if (sourceMode != value) {
                    sourceMode = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int runCount = 1;

        public int RunCount {
            get => runCount;
            set {
                var clamped = Math.Max(1, value);
                if (runCount != clamped) {
                    runCount = clamped;
                    ResizeSourcePaths();
                    RaisePropertyChanged();
                }
            }
        }

        private readonly ObservableCollection<string> sourcePaths;

        /// <summary>One source folder per run (sized to <see cref="RunCount"/>).</summary>
        public ObservableCollection<string> SourcePaths => sourcePaths;

        private bool isBusy;

        public bool IsBusy {
            get => isBusy;
            private set {
                if (isBusy != value) {
                    isBusy = value;
                    RaisePropertyChanged();
                    StartCommand.NotifyCanExecuteChanged();
                    AcceptCommand.NotifyCanExecuteChanged();
                    BackCommand.NotifyCanExecuteChanged();
                    ReviewCommand.NotifyCanExecuteChanged();
                    BackToSummaryCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private string errorMessage;

        public string ErrorMessage {
            get => errorMessage;
            private set {
                if (errorMessage != value) {
                    errorMessage = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(HasError));
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        #endregion State

        #region Progress

        private int evaluations;

        public int Evaluations {
            get => evaluations;
            private set { evaluations = value; RaisePropertyChanged(); }
        }

        private int maxEvaluations;

        public int MaxEvaluations {
            get => maxEvaluations;
            private set { maxEvaluations = value; RaisePropertyChanged(); }
        }

        private double progressSeedJ;

        public double ProgressSeedJ {
            get => progressSeedJ;
            private set { progressSeedJ = value; RaisePropertyChanged(); }
        }

        private double progressBestJ;

        public double ProgressBestJ {
            get => progressBestJ;
            private set { progressBestJ = value; RaisePropertyChanged(); }
        }

        private string phase;

        public string Phase {
            get => phase;
            private set { phase = value; RaisePropertyChanged(); }
        }

        #endregion Progress

        #region Results

        private OptimizationResult result;

        public OptimizationResult Result {
            get => result;
            private set { result = value; RaisePropertyChanged(); }
        }

        private OptimizationSummary summary;

        public OptimizationSummary Summary {
            get => summary;
            private set {
                summary = value;
                RaisePropertyChanged();
                AcceptCommand.NotifyCanExecuteChanged();
                BackCommand.NotifyCanExecuteChanged();
            }
        }

        private bool applyRecommendedStepSize;

        public bool ApplyRecommendedStepSize {
            get => applyRecommendedStepSize;
            set {
                if (applyRecommendedStepSize != value) {
                    applyRecommendedStepSize = value;
                    RaisePropertyChanged();
                }
            }
        }

        private StarReviewVM reviewVM;

        /// <summary>The interactive review over the optimized result's per-frame detector boxes, built lazily when
        /// the user enters the Review step (null until then). The Review-step panel binds its DataContext to this.</summary>
        public StarReviewVM ReviewVM {
            get => reviewVM;
            private set { reviewVM = value; RaisePropertyChanged(); }
        }

        // The in-memory label models the review edits, keyed by runId, kept on the VM so the NEXT task (re-optimize
        // with labels) can consume them without re-reading disk. Populated when the review is built (loaded from any
        // existing on-disk labels) and mutated in place by the StarReviewVM as the user labels.
        private Dictionary<string, StarReviewRunLabels> capturedLabels;

        /// <summary>The captured per-run labels from the review (empty until the user enters Review). Exposed so the
        /// re-optimize task can pick them up in-memory; the seam <see cref="HasLabels"/> reports whether any exist.</summary>
        public IReadOnlyDictionary<string, StarReviewRunLabels> CapturedLabels => capturedLabels;

        /// <summary>True once the user has applied at least one label in the review. The clean seam the re-optimize
        /// task keys off (and the UI can surface "labels captured — re-optimize?").</summary>
        public bool HasLabels {
            get {
                if (capturedLabels == null) {
                    return false;
                }
                foreach (var run in capturedLabels.Values) {
                    var (missed, reject, wrongly) = StarReviewLabelStore.Counts(run);
                    if (missed + reject + wrongly > 0) {
                        return true;
                    }
                }
                return false;
            }
        }

        #endregion Results

        #region Commands

        public RelayCommand<object> BrowseSourceCommand { get; }
        public AsyncRelayCommand StartCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand AcceptCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand CloseCommand { get; }

        /// <summary>Summary → Review: builds the review over ALL loaded frames at the optimized BestParams.</summary>
        public AsyncRelayCommand ReviewCommand { get; }

        /// <summary>Review → Summary: persists labels and returns to the Summary step.</summary>
        public RelayCommand BackToSummaryCommand { get; }

        #endregion Commands

        /// <summary>
        /// Runs the full pipeline: acquire (load each run) → seed guard → optimize → summary. Guards against a
        /// double-start, marshals progress via <see cref="IProgress{T}"/>, and never throws on cancellation —
        /// a cancelled run leaves the VM idle and re-runnable.
        /// </summary>
        public async Task StartAsync(CancellationToken externalToken) {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) {
                return; // already running
            }

            ErrorMessage = null;
            Summary = null;
            Result = null;
            // A fresh run invalidates any prior review snapshot/labels (they belonged to the previous result).
            ReviewVM = null;
            reviewDescriptors = null;
            reviewLabelsDir = null;
            capturedLabels = null;
            RaisePropertyChanged(nameof(HasLabels));
            IsBusy = true;

            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            // Each loaded run owns a disposable RunEvaluationData (the cached early-detection contexts pin the
            // per-frame source Mats — multiple GB at 61 MP). They are consumed through seed-guard → optimize →
            // summary, then released in the finally so the Mats are freed on success, cancel, AND error (and never
            // accumulate across re-runs). The summary is built inside the try (step 4) BEFORE this finally runs, so
            // nothing the summary needs is disposed early.
            List<LoadedRun> loadedRuns = null;
            try {
                // 1. Acquire — load every source into a LoadedRun. AcquireAsync disposes its own partial list on an
                //    early-out/exception, so a null return has already cleaned up.
                CurrentStep = WizardStep.Acquire;
                loadedRuns = await AcquireAsync(token).ConfigureAwait(true);
                if (loadedRuns == null) {
                    return; // ErrorMessage already set, or cancelled
                }

                // 2. Seed guard — evaluate the seed once; bail with a clear error on a degenerate run.
                if (!await SeedFitIsUsableAsync(loadedRuns, token).ConfigureAwait(true)) {
                    return; // ErrorMessage set by the guard
                }

                // 3. Optimize — off the UI thread, progress marshaled back. OptimizeAsync always returns a
                // non-null result; it signals cancellation by throwing OperationCanceledException (caught below).
                CurrentStep = WizardStep.Optimize;
                var optimizeResult = await OptimizeAsync(loadedRuns, token).ConfigureAwait(true);
                Result = optimizeResult;

                // 4. Summary — re-evaluate seed vs best for σ(focus) and recommend a step size.
                Summary = await BuildSummaryAsync(loadedRuns, optimizeResult, token).ConfigureAwait(true);

                // Snapshot the Mat-free frame descriptors + the labels dir for the optional Review step NOW, while
                // loadedRuns is still alive — the finally below disposes the in-memory source Mats, so the Review
                // step must detect from DISK afterward using only these paths/positions.
                SnapshotReviewInputs(loadedRuns);

                CurrentStep = WizardStep.Summary;
            } catch (OperationCanceledException) {
                Logger.Info("Star detection optimization cancelled");
                CurrentStep = WizardStep.SelectSource;
            } catch (Exception ex) {
                Logger.Error(ex, "Star detection optimization failed");
                ErrorMessage = $"Optimization failed: {ex.Message}";
                CurrentStep = WizardStep.SelectSource;
            } finally {
                // Release the cached source Mats. Safe even when AcquireAsync already disposed a partial list
                // (RunEvaluationData.Dispose is idempotent); harmless when loadedRuns is null.
                DisposeLoadedRuns(loadedRuns);
                IsBusy = false;
                Interlocked.Exchange(ref running, 0);
            }
        }

        /// <summary>Loads each configured source folder into a <see cref="LoadedRun"/>. For Live, runs a fresh AF
        /// attempt first and then loads its saved folder. Sets <see cref="ErrorMessage"/> and returns null on a
        /// missing/invalid source. Each loaded run owns a disposable <see cref="RunEvaluationData"/> (cached source
        /// Mats); on any early-out (error/cancel) or exception this disposes whatever was loaded so a partial
        /// acquisition never leaks. On full success the caller (<see cref="StartAsync"/>) owns disposing the list.</summary>
        private async Task<List<LoadedRun>> AcquireAsync(CancellationToken token) {
            var runs = new List<LoadedRun>(RunCount);
            try {
                for (var i = 0; i < RunCount; i++) {
                    token.ThrowIfCancellationRequested();
                    string folder;
                    if (SourceMode == SourceMode.Live) {
                        folder = await RunLiveAttemptAsync(token).ConfigureAwait(true);
                        if (string.IsNullOrEmpty(folder)) {
                            ErrorMessage = "The live auto-focus run did not produce a saved attempt folder.";
                            CurrentStep = WizardStep.SelectSource;
                            DisposeLoadedRuns(runs);
                            return null;
                        }
                    } else {
                        folder = i < SourcePaths.Count ? SourcePaths[i] : null;
                        if (string.IsNullOrEmpty(folder)) {
                            ErrorMessage = $"Select a saved auto-focus folder for run {i + 1}.";
                            CurrentStep = WizardStep.SelectSource;
                            DisposeLoadedRuns(runs);
                            return null;
                        }
                    }

                    Phase = $"Loading run {i + 1} of {RunCount}";
                    var loaded = await loader.LoadSavedRunAsync(folder, region, token).ConfigureAwait(true);
                    runs.Add(loaded);
                }
                return runs;
            } catch {
                // Cancellation / loader failure midway: free the runs loaded so far before propagating.
                DisposeLoadedRuns(runs);
                throw;
            }
        }

        /// <summary>Disposes each loaded run's <see cref="RunEvaluationData"/> (the cached early-detection contexts
        /// pinning the source Mats). Null-safe and idempotent — <see cref="RunEvaluationData.Dispose"/> guards
        /// double-dispose, so calling this twice on the same list is harmless.</summary>
        private static void DisposeLoadedRuns(IReadOnlyList<LoadedRun> runs) {
            if (runs == null) {
                return;
            }
            foreach (var run in runs) {
                run?.Data?.Dispose();
            }
        }

        /// <summary>
        /// Triggers a live auto-focus run that saves its frames, then returns the saved attempt folder so the run
        /// converges onto the same replay path. Kept minimal — hardware-dependent, manually verified later (T6).
        /// </summary>
        private async Task<string> RunLiveAttemptAsync(CancellationToken token) {
            if (autoFocusEngine == null) {
                throw new InvalidOperationException("Live mode requires an auto-focus engine.");
            }
            Phase = "Running live auto-focus";
            var options = autoFocusEngine.GetOptions();
            options.Save = true; // ensure frames land on disk so we can load them like a replay
            var afResult = await autoFocusEngine.Run(options, null, token, null).ConfigureAwait(true);
            return afResult?.SaveFolder;
        }

        /// <summary>
        /// The STARHFR/seed guard. STARHFR is NINA's "HFR-on-detected-stars" auto-focus method (AFMethodEnum.
        /// STARHFR), where focus is found by fitting an HFR-vs-position curve — so a usable run needs enough
        /// frames that actually yielded stars to determine that fit. We evaluate the SEED params once per run and
        /// require, on at least one run, a finite σ(focus) backed by ≥ 3 distinct focuser positions (the minimum
        /// a hyperbola can determine; mirrors the AF engine's "< 3 valid points => no fit" rule). When every run
        /// is degenerate (NaN σ and too few usable positions, i.e. most frames had no stars) we surface a clear
        /// error and refuse to optimize, so the user is not handed a meaningless result.
        /// </summary>
        private async Task<bool> SeedFitIsUsableAsync(IReadOnlyList<LoadedRun> runs, CancellationToken token) {
            const int MinPositionsForFit = 3;
            var anyUsable = false;
            foreach (var run in runs) {
                token.ThrowIfCancellationRequested();
                var eval = await run.Data.EvaluateAndFitAsync(run.Seed, token).ConfigureAwait(true);
                var sigmaFinite = double.IsFinite(eval.Metrics.SigmaFocus);
                if (sigmaFinite && eval.PooledPointCount >= MinPositionsForFit) {
                    anyUsable = true;
                    break;
                }
            }

            if (!anyUsable) {
                ErrorMessage =
                    "The selected auto-focus run(s) do not produce a usable focus curve at the current settings: " +
                    "too few focuser positions yielded detectable stars to fit a curve. Pick a run with stars across " +
                    "most frames, or re-acquire with a longer exposure before optimizing.";
                CurrentStep = WizardStep.SelectSource;
                return false;
            }
            return true;
        }

        /// <summary>Runs the pure optimizer off the UI thread; progress posts back via <see cref="IProgress{T}"/>.</summary>
        private async Task<OptimizationResult> OptimizeAsync(IReadOnlyList<LoadedRun> runs, CancellationToken token) {
            // All runs share the same camera/optics, so the search starts from the first run's seed.
            var seed = runs[0].Seed;
            var variables = OptimizerVariable.CreateCuratedSet();
            var evaluator = RunEvaluationData.CreateEvaluator(runs.Select(r => r.Data).ToList());

            MaxEvaluations = optimizerSettings.MaxEvaluations;
            var progress = new Progress<OptimizationProgress>(p => {
                Evaluations = p.Evaluations;
                MaxEvaluations = p.MaxEvaluations;
                ProgressBestJ = p.BestJ;
                ProgressSeedJ = p.SeedJ;
                Phase = p.Phase;
            });

            var optimizer = new StarDetectionOptimizer();
            return await Task.Run(
                () => optimizer.OptimizeAsync(seed, variables, evaluator, optimizerSettings, progress, token),
                token).ConfigureAwait(true);
        }

        /// <summary>
        /// Builds the summary: the changed-parameters table, before/after J + σ(focus) (re-evaluated at seed and
        /// best, averaged across runs), and the recommended step size from the representative (first) run's best
        /// fit, clamped to the focuser's max increment when known.
        /// </summary>
        private async Task<OptimizationSummary> BuildSummaryAsync(IReadOnlyList<LoadedRun> runs, OptimizationResult res, CancellationToken token) {
            var seed = runs[0].Seed;
            var changed = res.ChangedVariables
                .Select(c => new ChangedParameterRow { Name = c.Name, SeedValue = c.SeedValue, OptimizedValue = c.BestValue })
                .ToList();

            // σ(focus) before/after, averaged over the runs (the first run also yields the representative best fit).
            double seedSigmaSum = 0.0, bestSigmaSum = 0.0;
            var seedSigmaCount = 0; var bestSigmaCount = 0;
            AlglibHyperbolicFitting representativeBestFit = null;
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var seedEval = await runs[i].Data.EvaluateAndFitAsync(seed, token).ConfigureAwait(true);
                var bestEval = await runs[i].Data.EvaluateAndFitAsync(res.BestParams, token).ConfigureAwait(true);
                if (double.IsFinite(seedEval.Metrics.SigmaFocus)) { seedSigmaSum += seedEval.Metrics.SigmaFocus; seedSigmaCount++; }
                if (double.IsFinite(bestEval.Metrics.SigmaFocus)) { bestSigmaSum += bestEval.Metrics.SigmaFocus; bestSigmaCount++; }
                if (i == 0) {
                    representativeBestFit = bestEval.BestFit;
                }
            }

            var currentStepSize = runs[0].AfOptions?.AutoFocusStepSize ?? DefaultCurrentStepSize;
            var recommendation = StepSizeRecommender.Recommend(representativeBestFit, currentStepSize, GetFocuserMaxStep());

            return new OptimizationSummary {
                ChangedParameters = changed,
                SeedJ = res.SeedJ,
                BestJ = res.BestJ,
                SeedSigmaFocus = seedSigmaCount > 0 ? seedSigmaSum / seedSigmaCount : double.NaN,
                BestSigmaFocus = bestSigmaCount > 0 ? bestSigmaSum / bestSigmaCount : double.NaN,
                RunCount = runs.Count,
                RecommendedStepSize = recommendation.StepSize,
                RecommendedOffsetSteps = recommendation.OffsetSteps,
                CurrentStepSize = currentStepSize,
                ImprovedOverSeed = res.ImprovedOverSeed
            };
        }

        private const int DefaultCurrentStepSize = 10;

        /// <summary>
        /// The focuser's max single-move increment, when available, to clamp the recommendation. No reliable
        /// max-step source is exposed to the wizard, so this returns null (the recommender treats null as
        /// "no clamp"), keeping the recommendation purely curve-driven.
        /// </summary>
        private int? GetFocuserMaxStep() => null;

        /// <summary>
        /// The terminal "accept" decision: applies the optimized settings (which sets
        /// <c>UseOptimizedSettings = true</c>, selecting them as the active Simple-Mode source) via <see cref="Apply"/>,
        /// persists any review labels, then closes the wizard. One action = apply + select + close. Works whether or
        /// not the user visited Review (no labels = nothing to persist). The sibling Cancel path closes WITHOUT
        /// applying (it never mutates options) — though it still flushes labels, since those are user work product,
        /// not a settings mutation. Reachable from Summary AND Review.
        /// </summary>
        private void Accept() {
            PersistReviewLabels();
            Apply();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Applies the result. Builds an <see cref="OptimizedStarDetectionSettings"/> from
        /// <see cref="OptimizationResult.BestParams"/> + run metadata and hands it to
        /// <see cref="IStarDetectionOptions.ApplyOptimizedSettings"/>. When <see cref="ApplyRecommendedStepSize"/>
        /// is set, also writes the recommended AF step size/offset to the active profile. Only ever called via
        /// <see cref="Accept"/> when the user accepts the summary — never during the search.
        /// </summary>
        private void Apply() {
            if (Result == null || Summary == null) {
                return;
            }

            // Build the snapshot via the shared params->DTO mapping (the single source of truth shared with the
            // headless harness) so the in-app and offline paths can never drift. CreatedAtUtc is stamped inside.
            var dto = OptimizedStarDetectionSettings.FromParams(
                Result.BestParams, Summary.RunCount, Result.SeedJ, Result.BestJ,
                Summary.RecommendedStepSize, Summary.RecommendedOffsetSteps);

            starDetectionOptions.ApplyOptimizedSettings(dto);
            Logger.Info($"Applied optimized star-detection settings (J {Result.SeedJ:F3} -> {Result.BestJ:F3}, {Summary.RunCount} run(s))");

            if (ApplyRecommendedStepSize) {
                var focuserSettings = profileService?.ActiveProfile?.FocuserSettings;
                if (focuserSettings != null) {
                    focuserSettings.AutoFocusStepSize = Summary.RecommendedStepSize;
                    focuserSettings.AutoFocusInitialOffsetSteps = Summary.RecommendedOffsetSteps;
                    Logger.Info($"Applied recommended AF step size {Summary.RecommendedStepSize}, offset steps {Summary.RecommendedOffsetSteps}");
                }
            }
        }

        // ---- Review step (optional, post-Summary) ----------------------------------------------------------

        /// <summary>
        /// Captures the Mat-free inputs the optional Review step needs, snapshotted while <paramref name="runs"/> is
        /// still alive (StartAsync disposes the source Mats right after). For each run we keep its per-frame
        /// descriptors (paths + positions + runId); the labels dir is derived from the FIRST run's source folder
        /// (<c>&lt;sourceFolder&gt;/labels</c>), matching where <c>optimize --labels</c> /
        /// <c>RunEvaluationData.ApplyLabelScores</c> expect the per-run <c>&lt;runId&gt;.json</c> files.
        /// </summary>
        private void SnapshotReviewInputs(IReadOnlyList<LoadedRun> runs) {
            var descriptors = new List<FrameReviewDescriptor>();
            foreach (var run in runs) {
                descriptors.AddRange(run.Data.GetFrameDescriptors());
            }
            reviewDescriptors = descriptors;
            reviewLabelsDir = DeriveLabelsDir(descriptors);
            ReviewCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Derives the labels directory from the run frames' source folder: <c>&lt;sourceFolder&gt;/labels</c>
        /// (the source folder being the directory holding a frame). Returns null when no usable frame path exists,
        /// in which case the review still works but labels can't be persisted to disk.
        /// </summary>
        private static string DeriveLabelsDir(IReadOnlyList<FrameReviewDescriptor> descriptors) {
            var firstPath = descriptors.FirstOrDefault(d => !string.IsNullOrEmpty(d.FramePath)).FramePath;
            if (string.IsNullOrEmpty(firstPath)) {
                return null;
            }
            var folder = Path.GetDirectoryName(firstPath);
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "labels");
        }

        /// <summary>The Review step is enterable only after a successful run produced frame descriptors and the VM is
        /// idle (the StarReviewVM assumes a non-empty queue, so an empty snapshot blocks entry).</summary>
        private bool CanEnterReview() =>
            CurrentStep == WizardStep.Summary && !IsBusy
            && reviewDescriptors != null && reviewDescriptors.Count > 0
            && frameReviewBuilder != null;

        /// <summary>
        /// Summary → Review: builds a <see cref="FrameReview"/> for EVERY loaded frame at the optimized
        /// <see cref="OptimizationResult.BestParams"/> (off the UI thread — detection is I/O + compute), loads any
        /// existing on-disk labels for the involved runs, constructs the <see cref="StarReviewVM"/> over them, and
        /// advances to the Review step. The busy/phase indicators are reused so the build shows progress.
        /// </summary>
        private async Task EnterReviewAsync() {
            if (!CanEnterReview() || Result == null) {
                return;
            }

            var descriptors = reviewDescriptors;
            var bestParams = Result.BestParams;
            var labelsDir = reviewLabelsDir;

            ErrorMessage = null;
            IsBusy = true;
            Phase = "Detecting frames for review";
            try {
                // Detect every frame at the optimized params, off the UI thread. The production builder seam wraps the
                // shared FrameReviewBuilder in a Task.Run, so the whole build (disk load + detection) runs on the
                // threadpool and never stutters the UI thread; we just await the result here. The resulting
                // FrameReviews carry frozen overlays + a disk-backed image provider.
                var reviews = await frameReviewBuilder(descriptors, bestParams, cts?.Token ?? CancellationToken.None).ConfigureAwait(true);
                if (reviews == null || reviews.Count == 0) {
                    ErrorMessage = "No frames could be prepared for review.";
                    return;
                }

                // Load any existing labels for each involved run so a re-review merges/extends prior work, exactly as
                // the TestApp `review` tool does. Kept in-memory on the VM (capturedLabels) for the re-optimize seam.
                var labelsByRun = new Dictionary<string, StarReviewRunLabels>(StringComparer.Ordinal);
                foreach (var runId in reviews.Select(r => r.RunId).Distinct(StringComparer.Ordinal)) {
                    var labels = StarReviewLabelStore.Load(labelsDir, runId, out var loadError);
                    if (loadError != null) {
                        Logger.Warning(loadError);
                    }
                    labelsByRun[runId] = labels;
                }
                capturedLabels = labelsByRun;
                RaisePropertyChanged(nameof(HasLabels));

                ReviewVM = new StarReviewVM(reviews, labelsByRun, labelsDir ?? string.Empty);
                CurrentStep = WizardStep.Review;
            } catch (OperationCanceledException) {
                Logger.Info("Star detection review build cancelled");
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to build the star-detection review");
                ErrorMessage = $"Could not build the review: {ex.Message}";
            } finally {
                IsBusy = false;
            }
        }

        /// <summary>Review → Summary: persists labels and returns to the Summary step (Accept/Cancel remain the
        /// terminal decisions and are reachable from either step).</summary>
        private void BackToSummary() {
            if (CurrentStep != WizardStep.Review || IsBusy) {
                return;
            }
            PersistReviewLabels();
            CurrentStep = WizardStep.Summary;
        }

        /// <summary>
        /// Flushes the captured per-run labels to disk under <see cref="reviewLabelsDir"/> as the
        /// <c>&lt;runId&gt;.json</c> files that <c>optimize --labels</c> / <see cref="RunEvaluationData"/> consume.
        /// No-op when the user never reviewed (no labels), when the StarReviewVM hasn't been built, or when no labels
        /// dir could be derived. Tolerant — a failed save logs and does not block Accept/navigation.
        /// </summary>
        private void PersistReviewLabels() {
            if (ReviewVM == null || string.IsNullOrEmpty(reviewLabelsDir)) {
                return;
            }
            try {
                // The StarReviewVM owns the save (it normalizes + prunes empties + writes one file per run into the
                // labels dir it was constructed with). SaveAll also refreshes its in-memory counts.
                ReviewVM.SaveAll();
                RaisePropertyChanged(nameof(HasLabels));
            } catch (Exception ex) {
                Logger.Error(ex, "Failed to persist review labels");
            }
        }

        /// <summary>
        /// Builds the production review-builder seam: the SHARED <see cref="FrameReviewBuilder.BuildAsync"/> over a
        /// real <see cref="StarDetector"/> and a profile-aware disk loader (.xisf/.fits via NINA's loaders →
        /// normalized float Mat; .tif direct). Mirrors the TestApp <c>review</c> path so detection + overlay
        /// extraction + the MTF-stretch image provider are byte-identical between the offline tool and the wizard.
        /// </summary>
        private static Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, CancellationToken, Task<List<FrameReview>>>
            BuildProductionReviewBuilder(IProfileService profileService, IImageDataFactory imageDataFactory) {
            return (descriptors, p, token) => {
                var detector = new StarDetector(HocusFocusPlugin.AlglibAPI);
                // Hop to the threadpool so the WHOLE build (disk load + detection) runs off the captured UI context.
                // FrameReviewBuilder.BuildAsync awaits its work, so without this hop its synchronous prologue (and any
                // continuation that resumes on the UI SynchronizationContext) could stutter the UI thread.
                return Task.Run(
                    () => FrameReviewBuilder.BuildAsync(
                        descriptors, p, detector,
                        path => LoadFloatMatFromDisk(path, profileService, imageDataFactory),
                        token),
                    token);
            };
        }

        /// <summary>
        /// Loads an image file as a CV_32F Mat normalized to [0,1] for the review detection input. .tif/.tiff are
        /// read directly; .xisf/.fits/.fit go through NINA's loaders (which need the active profile). Mirrors the
        /// TestApp <c>DiagnosticUtil.LoadFloatMat</c> body so the wizard and the offline tool share one float-Mat
        /// load path — but lives in the plugin (no TestApp dependency).
        /// </summary>
        private static async Task<Mat> LoadFloatMatFromDisk(string path, IProfileService profileService, IImageDataFactory imageDataFactory) {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff") {
                // Normalize by ushort.MaxValue, matching DiagnosticUtil.LoadFloatMat for 16-bit TIFFs (the real case
                // — AF/review frames are 16-bit). For non-16-bit TIFFs the scale factor would differ, but those are
                // not produced by this pipeline.
                using var src = new Mat(path, ImreadModes.Unchanged);
                var dst = new Mat();
                src.ConvertTo(dst, MatType.CV_32F, 1.0 / ushort.MaxValue);
                return dst;
            }
            if (ext == ".xisf" || ext == ".fits" || ext == ".fit") {
                var uri = new Uri(Path.GetFullPath(path));
                IImageData imageData = ext == ".xisf"
                    ? await XISF.Load(uri, false, imageDataFactory, CancellationToken.None).ConfigureAwait(false)
                    : await FITS.Load(uri, false, imageDataFactory, CancellationToken.None).ConfigureAwait(false);
                return CvImageUtility.ToOpenCVMat(imageData);
            }
            throw new NotSupportedException($"Unsupported image extension '{ext}'. Supported: .tif/.tiff, .xisf, .fits/.fit");
        }

        private void Cancel() {
            try {
                cts?.Cancel();
            } catch (ObjectDisposedException) {
            }
        }

        /// <summary>
        /// Disposes the current <see cref="CancellationTokenSource"/>. T5's launch command should call this when
        /// the wizard window closes. Idempotent and null-safe; does not change run-time behavior otherwise.
        /// </summary>
        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            cts?.Dispose();
            cts = null;
        }

        private void Back() {
            if (CurrentStep == WizardStep.Summary && !IsBusy) {
                CurrentStep = WizardStep.SelectSource;
            }
        }

        private void BrowseSource(object parameter) {
            var index = 0;
            if (parameter != null && int.TryParse(parameter.ToString(), out var parsed)) {
                index = parsed;
            }
            var picked = folderPicker();
            if (!string.IsNullOrEmpty(picked) && index >= 0 && index < SourcePaths.Count) {
                SourcePaths[index] = picked;
            }
        }

        private void ResizeSourcePaths() {
            while (SourcePaths.Count < runCount) {
                SourcePaths.Add(null);
            }
            while (SourcePaths.Count > runCount) {
                SourcePaths.RemoveAt(SourcePaths.Count - 1);
            }
        }

        /// <summary>The production folder picker (WinForms), mirroring InspectorVM's replay picker.</summary>
        private static string PickFolderViaDialog() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                    return null;
                }
                return dialog.SelectedPath;
            }
        }
    }
}
