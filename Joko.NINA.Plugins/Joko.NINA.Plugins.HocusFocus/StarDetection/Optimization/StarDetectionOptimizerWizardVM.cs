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
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        Summary
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
                optimizerSettings: new OptimizerSettings()) {
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
            OptimizerSettings optimizerSettings) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.starDetectionOptions = starDetectionOptions ?? throw new ArgumentNullException(nameof(starDetectionOptions));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.autoFocusEngine = autoFocusEngine;
            this.folderPicker = folderPicker ?? (() => null);
            this.region = region ?? StarDetectionRegion.Full;
            this.optimizerSettings = optimizerSettings ?? new OptimizerSettings();

            sourcePaths = new ObservableCollection<string> { null };
            applyRecommendedStepSize = true;

            BrowseSourceCommand = new RelayCommand<object>(BrowseSource);
            StartCommand = new AsyncRelayCommand(() => StartAsync(CancellationToken.None));
            CancelCommand = new RelayCommand(Cancel);
            ApplyCommand = new RelayCommand(Apply, () => Summary != null);
            BackCommand = new RelayCommand(Back, () => CurrentStep == WizardStep.Summary && !IsBusy);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
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
                    BackCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsSelectSource => CurrentStep == WizardStep.SelectSource;
        public bool IsAcquire => CurrentStep == WizardStep.Acquire;
        public bool IsOptimize => CurrentStep == WizardStep.Optimize;
        public bool IsSummary => CurrentStep == WizardStep.Summary;

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
                    BackCommand.NotifyCanExecuteChanged();
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
                ApplyCommand.NotifyCanExecuteChanged();
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

        #endregion Results

        #region Commands

        public RelayCommand<object> BrowseSourceCommand { get; }
        public AsyncRelayCommand StartCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand ApplyCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand CloseCommand { get; }

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
            IsBusy = true;

            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            try {
                // 1. Acquire — load every source into a LoadedRun.
                CurrentStep = WizardStep.Acquire;
                var loadedRuns = await AcquireAsync(token).ConfigureAwait(true);
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
                CurrentStep = WizardStep.Summary;
            } catch (OperationCanceledException) {
                Logger.Info("Star detection optimization cancelled");
                CurrentStep = WizardStep.SelectSource;
            } catch (Exception ex) {
                Logger.Error(ex, "Star detection optimization failed");
                ErrorMessage = $"Optimization failed: {ex.Message}";
                CurrentStep = WizardStep.SelectSource;
            } finally {
                IsBusy = false;
                Interlocked.Exchange(ref running, 0);
            }
        }

        /// <summary>Loads each configured source folder into a <see cref="LoadedRun"/>. For Live, runs a fresh AF
        /// attempt first and then loads its saved folder. Sets <see cref="ErrorMessage"/> and returns null on a
        /// missing/invalid source.</summary>
        private async Task<List<LoadedRun>> AcquireAsync(CancellationToken token) {
            var runs = new List<LoadedRun>(RunCount);
            for (var i = 0; i < RunCount; i++) {
                token.ThrowIfCancellationRequested();
                string folder;
                if (SourceMode == SourceMode.Live) {
                    folder = await RunLiveAttemptAsync(token).ConfigureAwait(true);
                    if (string.IsNullOrEmpty(folder)) {
                        ErrorMessage = "The live auto-focus run did not produce a saved attempt folder.";
                        CurrentStep = WizardStep.SelectSource;
                        return null;
                    }
                } else {
                    folder = i < SourcePaths.Count ? SourcePaths[i] : null;
                    if (string.IsNullOrEmpty(folder)) {
                        ErrorMessage = $"Select a saved auto-focus folder for run {i + 1}.";
                        CurrentStep = WizardStep.SelectSource;
                        return null;
                    }
                }

                Phase = $"Loading run {i + 1} of {RunCount}";
                var loaded = await loader.LoadSavedRunAsync(folder, region, token).ConfigureAwait(true);
                runs.Add(loaded);
            }
            return runs;
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
        /// Applies the result. Builds an <see cref="OptimizedStarDetectionSettings"/> from
        /// <see cref="OptimizationResult.BestParams"/> + run metadata and hands it to
        /// <see cref="IStarDetectionOptions.ApplyOptimizedSettings"/>. When <see cref="ApplyRecommendedStepSize"/>
        /// is set, also writes the recommended AF step size/offset to the active profile. Only ever called by the
        /// user clicking Apply — never during the search.
        /// </summary>
        private void Apply() {
            if (Result == null || Summary == null) {
                return;
            }

            var best = Result.BestParams;
            var dto = new OptimizedStarDetectionSettings {
                // Curated knobs — note the DTO renames a few (Sensitivity->BrightnessSensitivity etc.).
                BrightnessSensitivity = best.Sensitivity,
                StarClippingMultiplier = best.StarClippingMultiplier,
                NoiseClippingMultiplier = best.NoiseClippingMultiplier,
                StarPeakResponse = best.PeakResponse,
                MaxDistortion = best.MaxDistortion,
                MinHFR = best.MinHFR,
                StarCenterTolerance = best.StarCenterTolerance,
                StructureLayers = best.StructureLayers,
                NoiseReductionRadius = best.NoiseReductionRadius,
                MinStarBoundingBoxSize = best.MinimumStarBoundingBoxSize,
                HotpixelThresholdingEnabled = best.HotpixelThresholdingEnabled,
                HotpixelThreshold = best.HotpixelThreshold,

                // Metadata (DateTime is fine here — this runs in-app, not in the headless harness).
                CreatedAtUtc = DateTime.UtcNow,
                RunCount = Summary.RunCount,
                BaselineJ = Result.SeedJ,
                FinalJ = Result.BestJ,
                RecommendedStepSize = Summary.RecommendedStepSize,
                RecommendedOffsetSteps = Summary.RecommendedOffsetSteps
            };

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
