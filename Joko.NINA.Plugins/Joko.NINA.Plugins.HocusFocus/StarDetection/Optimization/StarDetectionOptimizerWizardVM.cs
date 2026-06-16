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
using System.ComponentModel;
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

    /// <summary>Where the run frames come from: an already-saved AF attempt (Replay) or a fresh live AF run.
    /// The <see cref="DescriptionAttribute"/> text is what the wizard ComboBox shows (via the enum-description
    /// converter) — plain language, not the raw enum names.</summary>
    public enum SourceMode {
        [Description("Saved Auto-Focus")]
        Replay,

        [Description("Live Auto-Focus")]
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
        public int CurrentOffsetSteps { get; set; }
        public bool ImprovedOverSeed { get; set; }

        /// <summary>True when the recommended AF step size and/or offset steps differ from the current profile
        /// values — i.e. there is actually something to apply. Drives whether the "apply AF step size" toggle is
        /// enabled and whether the AF rows appear in the changed-parameters list.</summary>
        public bool StepSizeOrOffsetChanged =>
            RecommendedStepSize != CurrentStepSize || RecommendedOffsetSteps != CurrentOffsetSteps;

        /// <summary>Plain-language step-size readout: "{current} → {recommended}" when changed, else
        /// "{recommended} (unchanged)".</summary>
        public string StepSizeText => FormatRecommendation(CurrentStepSize, RecommendedStepSize);

        /// <summary>Plain-language offset-steps readout (same before→after / "(unchanged)" convention).</summary>
        public string OffsetStepsText => FormatRecommendation(CurrentOffsetSteps, RecommendedOffsetSteps);

        /// <summary>Focus-precision readout: "{seed} → {best}" with a "(~N% tighter)" suffix when σ improved
        /// (σ is the focus-curve sigma — lower is tighter/better).</summary>
        public string SigmaText {
            get {
                var baseText = $"{SeedSigmaFocus:F2} → {BestSigmaFocus:F2}";
                if (double.IsFinite(SeedSigmaFocus) && double.IsFinite(BestSigmaFocus)
                    && SeedSigmaFocus > 1e-9 && BestSigmaFocus < SeedSigmaFocus) {
                    var pct = (SeedSigmaFocus - BestSigmaFocus) / SeedSigmaFocus * 100.0;
                    return $"{baseText}  (~{pct:F0}% tighter)";
                }
                return baseText;
            }
        }

        /// <summary>How much the objective improved over the seed, as a non-negative percentage (the objective
        /// is a score where higher is better, and best never regresses below the seed).</summary>
        public double ImprovementPercent => PercentBetter(SeedJ, BestJ);

        /// <summary>"{current} → {recommended}" when the two differ, else "{recommended} (unchanged)". Pure
        /// formatter — unit-tested.</summary>
        public static string FormatRecommendation(int current, int recommended) =>
            current == recommended ? $"{recommended} (unchanged)" : $"{current} → {recommended}";

        /// <summary>Percent improvement of <paramref name="improved"/> over <paramref name="baseline"/> for a
        /// higher-is-better score, clamped at 0 and guarded against non-finite / near-zero baselines. Pure —
        /// unit-tested.</summary>
        public static double PercentBetter(double baseline, double improved) {
            if (!double.IsFinite(baseline) || !double.IsFinite(improved) || Math.Abs(baseline) < 1e-12) {
                return 0.0;
            }
            var pct = (improved - baseline) / Math.Abs(baseline) * 100.0;
            return pct > 0.0 ? pct : 0.0;
        }
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

        // The actual source folders loaded during the current run, in load order (Replay = the SourcePaths entry,
        // Live = the saved attempt folder). Captured in AcquireAsync and snapshotted alongside the review descriptors
        // so the re-optimize-with-labels path can RE-LOAD from disk after StartAsync's finally disposed the in-memory
        // data. Reset per run like the other review fields.
        private readonly List<string> loadedRunFolders = new List<string>();
        // The deterministic per-run RunId for each loaded run, captured in load order parallel to loadedRunFolders.
        // RunEvaluationLoader sets RunId = attempt.FolderPath ?? attemptFolderPath (a pure function of the folder), so
        // recording it during the first acquire lets re-optimize match labels by id WITHOUT a probe re-load.
        private readonly List<string> loadedRunIds = new List<string>();
        private IReadOnlyList<string> reoptimizeRunFolders;
        private IReadOnlyList<string> reoptimizeRunIds;

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
            ReOptimizeCommand = new AsyncRelayCommand(() => ReOptimizeWithLabelsAsync(CancellationToken.None), () => HasLabels && !IsBusy);
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
                    RaisePropertyChanged(nameof(IsReplay));
                }
            }
        }

        /// <summary>True for the "Saved Auto-Focus" (replay) source. The Number-of-runs input and the per-run
        /// folder pickers are only meaningful here, so the wizard binds their visibility to this.</summary>
        public bool IsReplay => SourceMode == SourceMode.Replay;

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
                    ReOptimizeCommand.NotifyCanExecuteChanged();
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
            private set { progressSeedJ = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressImprovementText)); }
        }

        private double progressBestJ;

        public double ProgressBestJ {
            get => progressBestJ;
            private set { progressBestJ = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressImprovementText)); }
        }

        /// <summary>Live, plain-language improvement readout shown during the search (no jargon "J"): how much
        /// better the best result found so far is than the user's current settings. Computed from the objective
        /// score but never labeled as such.</summary>
        public string ProgressImprovementText {
            get {
                var pct = OptimizationSummary.PercentBetter(progressSeedJ, progressBestJ);
                return pct > 0.0 ? $"Detection improved ~{pct:F0}% so far" : "Searching for a better fit…";
            }
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
                // A fresh summary: if nothing in the AF recommendation actually changed, there is nothing to
                // apply, so disable + clear the toggle.
                if (!CanApplyRecommendedStepSize) {
                    applyRecommendedStepSize = false;
                }
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ApplyRecommendedStepSize));
                RaisePropertyChanged(nameof(CanApplyRecommendedStepSize));
                RaisePropertyChanged(nameof(ChangedParametersDisplay));
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
                    // The AF rows in the changed-parameters list appear only while this is on.
                    RaisePropertyChanged(nameof(ChangedParametersDisplay));
                }
            }
        }

        /// <summary>Whether the "apply recommended AF step size" toggle is meaningful — true only when the
        /// recommended step size and/or offset steps actually differ from the current profile values. The
        /// checkbox binds its IsEnabled here so an unchanged recommendation can't be "applied".</summary>
        public bool CanApplyRecommendedStepSize => Summary?.StepSizeOrOffsetChanged ?? false;

        /// <summary>The changed-parameters rows shown on the summary: the optimized detector params, plus — only
        /// when <see cref="ApplyRecommendedStepSize"/> is on — the AF step size / offset-steps rows so the user
        /// sees the AF settings that Accept will also write. Recomputed when the toggle or the summary changes.</summary>
        public IReadOnlyList<ChangedParameterRow> ChangedParametersDisplay {
            get {
                if (Summary?.ChangedParameters == null) {
                    return Array.Empty<ChangedParameterRow>();
                }
                var rows = new List<ChangedParameterRow>(Summary.ChangedParameters);
                if (ApplyRecommendedStepSize && CanApplyRecommendedStepSize) {
                    if (Summary.RecommendedStepSize != Summary.CurrentStepSize) {
                        rows.Add(new ChangedParameterRow {
                            Name = "Auto-focus step size",
                            SeedValue = Summary.CurrentStepSize,
                            OptimizedValue = Summary.RecommendedStepSize
                        });
                    }
                    if (Summary.RecommendedOffsetSteps != Summary.CurrentOffsetSteps) {
                        rows.Add(new ChangedParameterRow {
                            Name = "Auto-focus offset steps",
                            SeedValue = Summary.CurrentOffsetSteps,
                            OptimizedValue = Summary.RecommendedOffsetSteps
                        });
                    }
                }
                return rows;
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

        /// <summary>Summary → re-optimize: re-loads the runs from disk (the first pass disposed the in-memory data),
        /// feeds the labels the user made into the optimizer's recall/precision objective term, and returns to an
        /// updated Summary. Enabled only when <see cref="HasLabels"/> is true and the VM is idle.</summary>
        public AsyncRelayCommand ReOptimizeCommand { get; }

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
            if (ReviewVM != null) {
                ReviewVM.PropertyChanged -= OnReviewLabelsChanged;
            }
            ReviewVM = null;
            reviewDescriptors = null;
            reviewLabelsDir = null;
            reoptimizeRunFolders = null;
            reoptimizeRunIds = null;
            loadedRunFolders.Clear();
            loadedRunIds.Clear();
            capturedLabels = null;
            RaisePropertyChanged(nameof(HasLabels));
            ReOptimizeCommand.NotifyCanExecuteChanged();
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
                SnapshotReviewInputs(loadedRuns, loadedRunFolders, loadedRunIds);

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
                    // Record the actual folder loaded (in load order) so the re-optimize path can re-read it from disk
                    // after the source Mats are disposed. Snapshotted in SnapshotReviewInputs on full success.
                    loadedRunFolders.Add(folder);
                    // Also record the deterministic RunId now, so re-optimize can match labels by id without a probe
                    // re-load (RunEvaluationLoader derives RunId purely from the folder).
                    loadedRunIds.Add(loaded.Data.RunId);
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
                Phase = FriendlyPhase(p.Phase);
            });

            var optimizer = new StarDetectionOptimizer();
            return await Task.Run(
                () => optimizer.OptimizeAsync(seed, variables, evaluator, optimizerSettings, progress, token),
                token).ConfigureAwait(true);
        }

        /// <summary>Maps the optimizer's internal phase identifiers ("Seed"/"CoarseGrid"/"PatternSearch", which
        /// stay as-is in logs and tests) to plain language for the progress display.</summary>
        private static string FriendlyPhase(string phase) => phase switch {
            "Seed" => "Checking your current settings",
            "CoarseGrid" => "Searching settings (coarse pass)",
            "PatternSearch" => "Refining settings",
            _ => phase
        };

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
            var currentOffsetSteps = runs[0].AfOptions?.AutoFocusInitialOffsetSteps ?? DefaultCurrentOffsetSteps;
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
                CurrentOffsetSteps = currentOffsetSteps,
                ImprovedOverSeed = res.ImprovedOverSeed
            };
        }

        private const int DefaultCurrentStepSize = 10;
        private const int DefaultCurrentOffsetSteps = 4;

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
        private void SnapshotReviewInputs(IReadOnlyList<LoadedRun> runs, IReadOnlyList<string> sourceFolders, IReadOnlyList<string> sourceRunIds) {
            var descriptors = new List<FrameReviewDescriptor>();
            foreach (var run in runs) {
                descriptors.AddRange(run.Data.GetFrameDescriptors());
            }
            reviewDescriptors = descriptors;
            reviewLabelsDir = DeriveLabelsDir(descriptors);
            // Snapshot the loaded source folders (in load order) so re-optimize can RE-LOAD from disk after the
            // source Mats are disposed in the run's finally. The runs[i] ordering matches sourceFolders[i].
            reoptimizeRunFolders = sourceFolders?.ToList() ?? new List<string>();
            // Snapshot the per-run RunIds (same load order) so re-optimize matches labels by id without a probe load.
            reoptimizeRunIds = sourceRunIds?.ToList() ?? new List<string>();
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
                ReOptimizeCommand.NotifyCanExecuteChanged();

                // Replace any prior review (drop its label-change subscription first to avoid a dangling handler).
                if (ReviewVM != null) {
                    ReviewVM.PropertyChanged -= OnReviewLabelsChanged;
                }
                ReviewVM = new StarReviewVM(reviews, labelsByRun, labelsDir ?? string.Empty);
                // Surface label edits live: the "Optimize with feedback" button enables as soon as the user labels
                // anything (the StarReviewVM raises CountsLabel on every add/remove).
                ReviewVM.PropertyChanged += OnReviewLabelsChanged;
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

        /// <summary>Review → Summary: returns to the Summary step WITHOUT writing labels to disk — labels are
        /// kept in-memory (<see cref="capturedLabels"/>) and only flushed to disk when the user Accepts or
        /// re-optimizes (their work product is preserved either way). Accept/Cancel remain the terminal
        /// decisions, reachable from the Summary step.</summary>
        private void BackToSummary() {
            if (CurrentStep != WizardStep.Review || IsBusy) {
                return;
            }
            CurrentStep = WizardStep.Summary;
        }

        /// <summary>Re-raises the label-dependent UI state when the in-progress review's labels change, so the
        /// "Optimize with feedback" affordance enables/disables live as the user labels.</summary>
        private void OnReviewLabelsChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(StarReviewVM.CountsLabel)) {
                RaisePropertyChanged(nameof(HasLabels));
                ReOptimizeCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// Summary → re-optimize-with-labels: after the user labeled mistakes in Review, re-run the optimization with
        /// those labels feeding the recall/precision objective term, then return to an updated Summary (from which the
        /// user can Review again or Accept). Because the first pass disposed the in-memory <see cref="RunEvaluationData"/>
        /// (freeing the source Mats) in <see cref="StartAsync"/>'s finally, this RE-LOADS each recorded source folder
        /// from disk — it cannot reuse the disposed data. The captured per-run labels are converted in-memory via
        /// <see cref="LabelConverter.ToFrameLabels"/> and threaded into the loader so the optimizer scores recall/precision.
        ///
        /// <para>Matches captured labels to re-loaded runs by the <see cref="RunEvaluationData.RunId"/> recorded
        /// during the FIRST acquire pass (it is a deterministic function of the source folder), so each labeled run
        /// loads exactly ONCE — no probe re-load. If a runId does not line up (e.g. an empty snapshot), it falls back
        /// to POSITIONAL matching across the snapshotted folders (loaded in the same order) and logs a warning — never
        /// crashing or silently dropping labels. Runs with no captured labels load with null labels (byte-identical to
        /// the label-agnostic load: the loader's no-label overload delegates to the labels overload with null).</para>
        /// </summary>
        private async Task ReOptimizeWithLabelsAsync(CancellationToken externalToken) {
            if (!HasLabels || reoptimizeRunFolders == null || reoptimizeRunFolders.Count == 0) {
                return;
            }
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) {
                return; // a Start/re-optimize is already in flight
            }

            // Persist whatever labels the user has made so far (the on-disk <runId>.json mirrors the in-memory model
            // used for scoring) — same flush Accept/BackToSummary do. The in-memory capturedLabels drive the re-run.
            PersistReviewLabels();

            // Match the captured labels (keyed by runId) to the folders we'll re-load. We re-load in folder order, so
            // we also keep a positional fallback (folder index → labels) for the case where the re-loaded runId does
            // not line up with the captured key.
            var labelsByRunId = capturedLabels ?? new Dictionary<string, StarReviewRunLabels>(StringComparer.Ordinal);
            var labelsByFolderIndex = BuildPositionalLabelFallback(reoptimizeRunFolders, labelsByRunId);

            ErrorMessage = null;
            IsBusy = true;

            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            List<LoadedRun> reloaded = null;
            try {
                CurrentStep = WizardStep.Optimize;
                reloaded = new List<LoadedRun>(reoptimizeRunFolders.Count);
                for (var i = 0; i < reoptimizeRunFolders.Count; i++) {
                    token.ThrowIfCancellationRequested();
                    Phase = $"Re-loading run {i + 1} of {reoptimizeRunFolders.Count}";
                    var folder = reoptimizeRunFolders[i];

                    // The RunId was recorded during the first acquire (it is a deterministic function of the folder),
                    // so we resolve labels WITHOUT a probe re-load. Resolve the runId for this index (guarding the
                    // snapshot), match its labels by id (positional fallback otherwise), and load the run exactly ONCE
                    // through the labels overload — at 61 MP a probe load would re-render the full frame set (GBs).
                    var runId = i < reoptimizeRunIds.Count ? reoptimizeRunIds[i] : null;
                    var runLabels = ResolveLabelsForRun(runId, i, labelsByRunId, labelsByFolderIndex);
                    // Passing null frameLabels is byte-identical to the no-label load (the loader's no-label overload
                    // delegates to this one with labels: null), so a label-less run is loaded the same as before.
                    var frameLabels = LabelConverter.ToFrameLabels(runLabels);
                    var loaded = await loader.LoadSavedRunAsync(folder, region, frameLabels, token).ConfigureAwait(true);
                    reloaded.Add(loaded);
                }

                // Re-run the same optimize + summary pipeline over the labeled runs.
                var optimizeResult = await OptimizeAsync(reloaded, token).ConfigureAwait(true);
                Result = optimizeResult;
                Summary = await BuildSummaryAsync(reloaded, optimizeResult, token).ConfigureAwait(true);

                // Re-snapshot the review inputs from the RE-LOADED runs (mirroring StartAsync step 4) so the user can
                // enter Review again before this finally disposes the freshly-loaded source Mats. The same folders we
                // just re-loaded (in order) become the re-optimize source again, so a second re-optimize works too;
                // the RunIds are deterministic from the folders, so the same snapshot carries forward.
                SnapshotReviewInputs(reloaded, reoptimizeRunFolders, reoptimizeRunIds);

                CurrentStep = WizardStep.Summary;
            } catch (OperationCanceledException) {
                Logger.Info("Star detection re-optimization cancelled");
                CurrentStep = WizardStep.Summary;
            } catch (Exception ex) {
                Logger.Error(ex, "Star detection re-optimization failed");
                ErrorMessage = $"Re-optimization failed: {ex.Message}";
                CurrentStep = WizardStep.Summary;
            } finally {
                DisposeLoadedRuns(reloaded);
                IsBusy = false;
                Interlocked.Exchange(ref running, 0);
            }
        }

        /// <summary>
        /// Builds a positional (folder-index → labels) fallback used only when a re-loaded run's id does not match any
        /// captured-labels key. The folders are re-loaded in the same order the captured labels were produced, so the
        /// fallback pairs them by index. Captured-label values are taken in their dictionary's enumeration order.
        /// </summary>
        private static IReadOnlyList<StarReviewRunLabels> BuildPositionalLabelFallback(
            IReadOnlyList<string> folders, IReadOnlyDictionary<string, StarReviewRunLabels> labelsByRunId) {
            var ordered = labelsByRunId.Values.ToList();
            var result = new StarReviewRunLabels[folders.Count];
            for (var i = 0; i < folders.Count && i < ordered.Count; i++) {
                result[i] = ordered[i];
            }
            return result;
        }

        /// <summary>
        /// Resolves the captured labels for a re-loaded run: prefer a runId match; on a miss, fall back to the
        /// positional entry for this folder index (and log a warning so the mismatch is visible). Returns null when
        /// no labels apply to this run.
        /// </summary>
        private static StarReviewRunLabels ResolveLabelsForRun(
            string runId, int folderIndex,
            IReadOnlyDictionary<string, StarReviewRunLabels> labelsByRunId,
            IReadOnlyList<StarReviewRunLabels> labelsByFolderIndex) {
            if (!string.IsNullOrEmpty(runId) && labelsByRunId.TryGetValue(runId, out var byId)) {
                return byId;
            }
            var positional = folderIndex < labelsByFolderIndex.Count ? labelsByFolderIndex[folderIndex] : null;
            if (positional != null) {
                Logger.Warning(
                    $"Re-optimize: re-loaded run id '{runId}' did not match a captured-labels key; " +
                    $"falling back to positional labels for run index {folderIndex} (run id '{positional.RunId}').");
            }
            return positional;
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
                ReOptimizeCommand.NotifyCanExecuteChanged();
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
