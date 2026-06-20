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

    /// <summary>What the Start button does: run the optimization pass (default), or skip it and jump straight to a
    /// Summary built from the CURRENT settings so the user can validate (and label against) today's detection without
    /// an optimization run. Either way the Review → "Optimize with feedback" loop stays available.</summary>
    public enum WizardOptimizeMode {
        Optimize,
        UseCurrentSettings
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

        /// <summary>For the feedback variant only: σ(focus) of the optimized-WITHOUT-feedback result, so the
        /// results header can show how much the feedback round tightened focus relative to the plain optimization.
        /// Null for the current/optimized summaries.</summary>
        public double? PriorSigmaFocus { get; set; }

        /// <summary>For the feedback variant only: the optimized-without-feedback objective (J) baseline.</summary>
        public double? PriorBestJ { get; set; }

        /// <summary>True when this summary carries an optimized-without-feedback baseline to compare against
        /// (i.e. it is the feedback summary produced after an initial optimization).</summary>
        public bool HasFeedbackComparison => PriorSigmaFocus.HasValue;

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
                // "2.20 (unchanged)" when σ is effectively the same before/after (compared at the F2 precision
                // shown), else "{seed} → {best}" with a "(~N% tighter)" suffix when it improved.
                if (double.IsFinite(SeedSigmaFocus) && double.IsFinite(BestSigmaFocus)
                    && Math.Abs(SeedSigmaFocus - BestSigmaFocus) < 0.005) {
                    return $"{BestSigmaFocus:F2} (unchanged)";
                }
                var baseText = $"{SeedSigmaFocus:F2} → {BestSigmaFocus:F2}";
                if (double.IsFinite(SeedSigmaFocus) && double.IsFinite(BestSigmaFocus)
                    && SeedSigmaFocus > 1e-9 && BestSigmaFocus < SeedSigmaFocus) {
                    var pct = (SeedSigmaFocus - BestSigmaFocus) / SeedSigmaFocus * 100.0;
                    return $"{baseText}  (~{pct:F0}% tighter)";
                }
                return baseText;
            }
        }

        /// <summary>Feedback-vs-optimized focus-precision readout for the results header: "{optimized} → {feedback}"
        /// with a "(~N% tighter)" suffix when the feedback round improved σ over the plain optimization. Empty when
        /// there is no optimized-without-feedback baseline (so the row is hidden for non-feedback variants).</summary>
        public string FeedbackVsOptimizedText {
            get {
                if (!PriorSigmaFocus.HasValue) {
                    return string.Empty;
                }
                var prior = PriorSigmaFocus.Value;
                if (double.IsFinite(prior) && double.IsFinite(BestSigmaFocus)
                    && Math.Abs(prior - BestSigmaFocus) < 0.005) {
                    return $"{BestSigmaFocus:F2} (unchanged)";
                }
                var baseText = $"{prior:F2} → {BestSigmaFocus:F2}";
                if (double.IsFinite(prior) && double.IsFinite(BestSigmaFocus)
                    && prior > 1e-9 && BestSigmaFocus < prior) {
                    var pct = (prior - BestSigmaFocus) / prior * 100.0;
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
        // Standard optimizer evaluation budget, captured from optimizerSettings ONCE at construction so the
        // donut-off path always restores it (OptimizeAsync mutates optimizerSettings.MaxEvaluations per run).
        private readonly int standardMaxEvaluations;
        // Larger budget used when "Recover out-of-focus donut stars" is enabled: turning the donut master on adds
        // the defocus axes to the curated search set, enlarging the space the optimizer must explore, so it gets
        // the pre-cut 400-eval budget instead of the standard one.
        private const int DonutMaxEvaluations = 400;

        // Minimum strictly-positive J margin for the optimized result to count as beating the user's current settings
        // (drives OptimizerImprovedOverCurrent / the default-to-Current guard). Just above double round-off so a true
        // tie (e.g. the StartFromCurrentSettings path returning current unchanged) reads as "no improvement".
        private const double ImprovementEpsilon = 1e-9;

        // The review-build seam: given the snapshotted frame descriptors + the params to detect with, produces the
        // per-frame FrameReviews the StarReviewVM renders. Production wires FrameReviewBuilder over a real
        // StarDetector + a profile-aware disk loader; the unit tests inject a fake so the step-flow can be exercised
        // without real images.
        private readonly Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>> frameReviewBuilder;

        // Connection probes for the Live source's pre-flight check (Start verifies the camera + focuser are
        // connected before triggering a live auto-focus). Injected as delegates so the VM stays mediator-free and
        // unit-testable; production wires them to the camera/focuser mediators, tests pass a stub. Default to
        // "connected" when not supplied so the existing tests are unaffected.
        private readonly Func<bool> isCameraConnected;
        private readonly Func<bool> isFocuserConnected;

        // Snapshotted at the end of a successful run (BEFORE loadedRuns is disposed): the Mat-free per-frame
        // descriptors for every loaded run, the labels dir each run's labels persist to, and the in-memory label
        // models the review edits. These survive disposal so the Review step can detect from disk afterward.
        private IReadOnlyList<FrameReviewDescriptor> reviewDescriptors;
        private string reviewLabelsDir;

        // The frames detected for the LAST Review build + the params they were detected at. The "Optimize with
        // feedback" analyzer reuses these (plus the live capturedLabels) to attribute labels to gates — no re-detect.
        private IReadOnlyList<FrameReview> reviewFrames;
        private StarDetectorParams reviewParams;

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

        // The objective the optimizer uses by default (StarDetectionOptimizer ctor defaults to new ObjectiveConstants()).
        // The wizard computes the current-settings baseline J with the SAME constants so it is comparable to BestJ.
        private readonly ObjectiveConstants objectiveConstants = new ObjectiveConstants();

        // J of the user's CURRENT settings (Baseline), evaluated on the loaded runs. The displayed "before" for the
        // improvement readouts (summary SeedJ + live ProgressSeedJ). Set in StartAsync (and the re-optimize path)
        // before the optimize + summary steps.
        private double currentBaselineJ;

        /// <summary>
        /// MEF/T5 convenience constructor: wires the real collaborators from the plugin singletons + mediators.
        /// The wizard is not MEF-exported (it is created on demand by T5's launch command), so this just supplies
        /// production dependencies to the primary constructor.
        /// </summary>
        public StarDetectionOptimizerWizardVM(
            IProfileService profileService,
            IImageDataFactory imageDataFactory,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IFocuserMediator focuserMediator,
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
                frameReviewBuilder: BuildProductionReviewBuilder(profileService, imageDataFactory),
                // Live pre-flight: probe the camera/focuser mediators for the connection check on Start.
                isCameraConnected: () => cameraMediator?.GetInfo()?.Connected == true,
                isFocuserConnected: () => focuserMediator?.GetInfo()?.Connected == true) {
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
            Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>> frameReviewBuilder = null,
            Func<bool> isCameraConnected = null,
            Func<bool> isFocuserConnected = null) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.starDetectionOptions = starDetectionOptions ?? throw new ArgumentNullException(nameof(starDetectionOptions));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.autoFocusEngine = autoFocusEngine;
            this.folderPicker = folderPicker ?? (() => null);
            this.region = region ?? StarDetectionRegion.Full;
            this.optimizerSettings = optimizerSettings ?? new OptimizerSettings();
            this.standardMaxEvaluations = this.optimizerSettings.MaxEvaluations;
            this.frameReviewBuilder = frameReviewBuilder;
            this.isCameraConnected = isCameraConnected ?? (() => true);
            this.isFocuserConnected = isFocuserConnected ?? (() => true);

            sourcePaths = new ObservableCollection<string> { null };
            applyRecommendedStepSize = true;

            BrowseSourceCommand = new RelayCommand<object>(BrowseSource);
            StartCommand = new AsyncRelayCommand(() => StartAsync(CancellationToken.None), CanStart);
            CancelCommand = new RelayCommand(Cancel);
            AcceptCommand = new RelayCommand(Accept, CanAccept);
            BackCommand = new RelayCommand(Back, () => CurrentStep == WizardStep.Summary && !IsBusy);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
            ReviewCommand = new AsyncRelayCommand(EnterReviewAsync, CanEnterReview);
            BackToSummaryCommand = new RelayCommand(BackToSummary, () => CurrentStep == WizardStep.Review && !IsBusy);
            ReOptimizeCommand = new AsyncRelayCommand(() => ReOptimizeWithLabelsAsync(CancellationToken.None), () => HasLabels && !IsBusy);

            // Start enables/disables as the per-run paths are filled in (Saved Auto-Focus), so re-evaluate its
            // CanExecute whenever a path is set or the run count changes.
            sourcePaths.CollectionChanged += (_, __) => StartCommand.NotifyCanExecuteChanged();
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
                    RaiseStepVisibilityChanged();
                    BackCommand.NotifyCanExecuteChanged();
                    ReviewCommand.NotifyCanExecuteChanged();
                    BackToSummaryCommand.NotifyCanExecuteChanged();
                    AcceptCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsSelectSource => CurrentStep == WizardStep.SelectSource;
        public bool IsAcquire => CurrentStep == WizardStep.Acquire;
        public bool IsOptimize => CurrentStep == WizardStep.Optimize;
        public bool IsSummary => CurrentStep == WizardStep.Summary;
        public bool IsReview => CurrentStep == WizardStep.Review;

        // Body-panel visibility: exactly one panel shows. While the wizard is busy (loading / analyzing / optimizing
        // / building the review) the determinate progress panel takes over, so the content panels hide on !IsBusy.
        public bool ShowSelectSource => IsSelectSource && !IsBusy;
        public bool ShowProgress => IsBusy;
        public bool ShowSummary => IsSummary && !IsBusy;
        public bool ShowReview => IsReview && !IsBusy;

        private void RaiseStepVisibilityChanged() {
            RaisePropertyChanged(nameof(ShowSelectSource));
            RaisePropertyChanged(nameof(ShowProgress));
            RaisePropertyChanged(nameof(ShowSummary));
            RaisePropertyChanged(nameof(ShowReview));
        }

        private WizardOptimizeMode optimizeMode = WizardOptimizeMode.Optimize;

        /// <summary>Optimize (run the search) vs. Use current settings (skip optimization, go straight to a Summary
        /// built from today's params). Bound by two radio buttons on the SelectSource step.</summary>
        public WizardOptimizeMode OptimizeMode {
            get => optimizeMode;
            set {
                if (optimizeMode != value) {
                    optimizeMode = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsOptimizeMode));
                    RaisePropertyChanged(nameof(IsUseCurrentMode));
                }
            }
        }

        public bool IsOptimizeMode {
            get => OptimizeMode == WizardOptimizeMode.Optimize;
            set { if (value) { OptimizeMode = WizardOptimizeMode.Optimize; } }
        }

        public bool IsUseCurrentMode {
            get => OptimizeMode == WizardOptimizeMode.UseCurrentSettings;
            set { if (value) { OptimizeMode = WizardOptimizeMode.UseCurrentSettings; } }
        }

        private bool startFromCurrentSettings;

        /// <summary>When true (default OFF), the optimization SEED is the user's current settings (the run's
        /// Baseline) instead of the fully-default params, so the search refines the current setup rather than
        /// re-deriving from scratch. Only meaningful in Optimize mode. Improvement is still reported vs the current
        /// settings, and the never-regress floor becomes the current settings' J.</summary>
        public bool StartFromCurrentSettings {
            get => startFromCurrentSettings;
            set {
                if (startFromCurrentSettings != value) {
                    startFromCurrentSettings = value;
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>Pass-through to the profile-saved <see cref="IStarDetectionOptions.DefocusAwareDonutDetection"/>
        /// master toggle, surfaced on the wizard start page. Default OFF. UNLIKE <see cref="StartFromCurrentSettings"/>
        /// (VM-only, resets each launch) this is a true profile option: the setter persists IMMEDIATELY on click
        /// (NINA auto-saves the active profile), because it changes what the wizard's seed/baseline detection does
        /// for this very run (it gates the early morph-close and unlocks the defocus axes for the optimizer). The
        /// optimizer's tuned numeric values still persist only on Accept (ApplyOptimizedSettings).</summary>
        public bool DefocusAwareDonutDetection {
            get => starDetectionOptions.DefocusAwareDonutDetection;
            set {
                if (starDetectionOptions.DefocusAwareDonutDetection != value) {
                    starDetectionOptions.DefocusAwareDonutDetection = value;
                    RaisePropertyChanged();
                }
            }
        }

        private SourceMode sourceMode = SourceMode.Replay;

        public SourceMode SourceMode {
            get => sourceMode;
            set {
                if (sourceMode != value) {
                    sourceMode = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsReplay));
                    // Live needs no paths (Start enabled); Saved disables Start until paths are filled.
                    StartCommand.NotifyCanExecuteChanged();
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
                    RaiseStepVisibilityChanged();
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

        private int progressCurrent;

        /// <summary>Determinate progress numerator: items done in the current phase (frames loaded / frames
        /// analyzed / combinations tried / frames detected for review).</summary>
        public int ProgressCurrent {
            get => progressCurrent;
            private set { progressCurrent = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressCountText)); }
        }

        private int progressTotal;

        /// <summary>Determinate progress denominator: total items in the current phase. 0 = no determinate count
        /// (an indeterminate phase), which hides the count text.</summary>
        public int ProgressTotal {
            get => progressTotal;
            private set {
                progressTotal = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ProgressCountText));
                RaisePropertyChanged(nameof(HasProgressCount));
                RaisePropertyChanged(nameof(IsProgressIndeterminate));
            }
        }

        public bool HasProgressCount => progressTotal > 0;

        /// <summary>True when the current phase has no determinate count yet (e.g. the live AF run, or before the
        /// frame count is known) — the progress bar shows its marching animation instead of a static empty bar.</summary>
        public bool IsProgressIndeterminate => progressTotal <= 0;

        /// <summary>The "N / M" count line shown under the progress bar; empty when there is no determinate total.</summary>
        public string ProgressCountText => progressTotal > 0 ? $"{progressCurrent} / {progressTotal}" : string.Empty;

        private bool isOptimizing;

        /// <summary>True only while the parameter search is running, so the live improvement readout (which is
        /// meaningless during loading/analysis) is shown only then.</summary>
        public bool IsOptimizing {
            get => isOptimizing;
            private set { isOptimizing = value; RaisePropertyChanged(); }
        }

        /// <summary>Sets the descriptive phase heading + the determinate count in one shot (raising once each).</summary>
        private void SetProgress(string phase, int current, int total) {
            Phase = phase;
            ProgressCurrent = current;
            ProgressTotal = total;
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

        // Retained per-variant state. Each variant has its own result + summary + curve so the user can toggle the
        // chart/summary between them and Accept either the first optimization or the latest feedback round. "Current"
        // is the seed (shown for comparison, never Accepted). "Optimized" is the first optimization pass and is
        // PRESERVED across re-optimizes. "Feedback" is the latest re-optimization that used review labels and is
        // REPLACED each round (so at most three variants ever exist).
        private OptimizationResult currentResult, optimizedResult, feedbackResult;
        private OptimizationSummary currentSummary, optimizedSummary, feedbackSummary;
        private OptimizationCurve currentCurve, optimizedCurve, feedbackCurve;
        private OptimizationVariant selectedVariant = OptimizationVariant.Current;

        private OptimizationResult SelectedResult => selectedVariant switch {
            OptimizationVariant.Feedback => feedbackResult,
            OptimizationVariant.Optimized => optimizedResult,
            _ => currentResult
        };

        private OptimizationSummary SelectedSummary => selectedVariant switch {
            OptimizationVariant.Feedback => feedbackSummary,
            OptimizationVariant.Optimized => optimizedSummary,
            _ => currentSummary
        };

        /// <summary>The optimization result for the selected variant. A computed pass-through so existing bindings,
        /// <see cref="Apply"/>, and tests that read <c>Result</c> follow the variant toggle automatically.</summary>
        public OptimizationResult Result => SelectedResult;

        /// <summary>The summary for the selected variant (pass-through; drives the changed-params table + header).</summary>
        public OptimizationSummary Summary => SelectedSummary;

        /// <summary>Which settings variant the results page shows / would Accept. Selecting an available variant
        /// refreshes the chart, the summary text, and the Accept affordance.</summary>
        public OptimizationVariant SelectedVariant {
            get => selectedVariant;
            set {
                if (selectedVariant != value && IsVariantAvailable(value)) {
                    selectedVariant = value;
                    RaiseSelectedVariantDependents();
                }
            }
        }

        /// <summary>The auto-focus curve plotted for the selected variant.</summary>
        public OptimizationCurve SelectedCurve => selectedVariant switch {
            OptimizationVariant.Feedback => feedbackCurve,
            OptimizationVariant.Optimized => optimizedCurve,
            _ => currentCurve
        };

        public bool HasSelectedCurve => SelectedCurve?.HasFit == true;
        public string SelectedVariantLabel => SelectedCurve?.Label;

        public bool HasCurrent => currentCurve != null;
        public bool HasOptimized => optimizedResult != null;
        public bool HasFeedback => feedbackResult != null;

        private bool optimizerImprovedOverCurrent;

        /// <summary>True when an optimization pass produced a result that STRICTLY beats the user's current settings
        /// (BestJ &gt; current-settings J). The optimizer seeds from the fully-default params and only guarantees
        /// "&ge; the seed", NOT "&ge; current" — on an easy/saturated run it can converge to a local optimum that is
        /// worse than a well-tuned current setup. When this is false the results page defaults to the Current variant
        /// (so the default Accept keeps current); the Optimized variant is still available to inspect. The UI binds
        /// this to show a "could not improve on your current settings" note.</summary>
        public bool OptimizerImprovedOverCurrent {
            get => optimizerImprovedOverCurrent;
            private set {
                if (optimizerImprovedOverCurrent != value) {
                    optimizerImprovedOverCurrent = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(OptimizerNoImprovementNote));
                    RaisePropertyChanged(nameof(ShowNoImprovementNote));
                }
            }
        }

        /// <summary>True when an optimization pass ran but could not beat the current settings — drives the
        /// visibility of the "kept Current" note on the results page.</summary>
        public bool ShowNoImprovementNote => HasOptimized && !OptimizerImprovedOverCurrent;

        /// <summary>A short note shown when an optimization pass ran but could not beat the current settings; empty
        /// otherwise. Lets the user understand why the page defaulted to Current rather than the optimized variant.</summary>
        public string OptimizerNoImprovementNote =>
            ShowNoImprovementNote
                ? "The optimizer could not improve on your current settings — keeping Current. " +
                  "Tip: enable \"Start from my current settings\" to refine them, or inspect the Optimized variant below."
                : string.Empty;

        private bool IsVariantAvailable(OptimizationVariant v) => v switch {
            OptimizationVariant.Feedback => HasFeedback,
            OptimizationVariant.Optimized => HasOptimized,
            _ => HasCurrent
        };

        /// <summary>The accepted-star-count change per focuser position between the baseline (optimized, or current
        /// when no optimization pass ran) and the feedback variant. Non-empty ONLY while the feedback variant is
        /// selected (i.e. when the user is comparing optimized vs optimized-with-feedback). Summed per position so it
        /// lines up with the curve points.</summary>
        public IReadOnlyList<FrameStarCountChange> StarCountChanges {
            get {
                if (selectedVariant != OptimizationVariant.Feedback) {
                    return Array.Empty<FrameStarCountChange>();
                }
                return BuildStarCountChanges(optimizedCurve ?? currentCurve, feedbackCurve);
            }
        }

        public bool HasStarCountChanges => StarCountChanges.Count > 0;

        /// <summary>Whether the star-count comparison baseline is the optimized run ("optimized") or — in the
        /// review-only → feedback path — the current settings ("current"). Drives the section heading.</summary>
        public string StarCountChangeBaselineLabel => HasOptimized ? "optimized" : "current";

        /// <summary>Shows the "you labeled stars — re-run optimization" prompt: only when the user has labels that
        /// have not yet been turned into a feedback result. Once a feedback variant exists, the prompt is replaced by
        /// the star-count comparison (re-optimize stays reachable from the Review page).</summary>
        public bool ShowReoptimizePrompt => HasLabels && !HasFeedback;

        /// <summary>Whether the feedback panel (the bordered box on the results page) has anything to show: the
        /// re-run prompt, the star-count comparison, or the label→gate breakdown.</summary>
        public bool ShowFeedbackPanel => ShowReoptimizePrompt || HasStarCountChanges || HasRecommendation;

        /// <summary>Builds the per-position accepted-star-count change between two variants' representative-run frames
        /// (positions match because both detect the SAME frames). Returns empty when either side lacks counts.</summary>
        private static IReadOnlyList<FrameStarCountChange> BuildStarCountChanges(OptimizationCurve baseline, OptimizationCurve feedback) {
            if (baseline?.FrameStarCounts == null || baseline.FrameFocuserPositions == null
                || feedback?.FrameStarCounts == null || feedback.FrameFocuserPositions == null) {
                return Array.Empty<FrameStarCountChange>();
            }
            var baseByPos = SumStarCountsByPosition(baseline.FrameFocuserPositions, baseline.FrameStarCounts);
            var fbByPos = SumStarCountsByPosition(feedback.FrameFocuserPositions, feedback.FrameStarCounts);
            var result = new List<FrameStarCountChange>();
            foreach (var pos in baseByPos.Keys.Union(fbByPos.Keys).OrderBy(p => p)) {
                result.Add(new FrameStarCountChange {
                    FocuserPosition = pos,
                    BaselineCount = baseByPos.TryGetValue(pos, out var b) ? b : 0,
                    FeedbackCount = fbByPos.TryGetValue(pos, out var f) ? f : 0
                });
            }
            return result;
        }

        private static Dictionary<int, int> SumStarCountsByPosition(IReadOnlyList<int> positions, IReadOnlyList<int> counts) {
            var d = new Dictionary<int, int>();
            var n = Math.Min(positions.Count, counts.Count);
            for (var i = 0; i < n; i++) {
                d[positions[i]] = (d.TryGetValue(positions[i], out var c) ? c : 0) + counts[i];
            }
            return d;
        }

        // Variant toggle radio-button helpers (mirror IsOptimizeMode/IsUseCurrentMode).
        public bool IsCurrentVariant {
            get => selectedVariant == OptimizationVariant.Current;
            set { if (value) { SelectedVariant = OptimizationVariant.Current; } }
        }

        public bool IsOptimizedVariant {
            get => selectedVariant == OptimizationVariant.Optimized;
            set { if (value) { SelectedVariant = OptimizationVariant.Optimized; } }
        }

        public bool IsFeedbackVariant {
            get => selectedVariant == OptimizationVariant.Feedback;
            set { if (value) { SelectedVariant = OptimizationVariant.Feedback; } }
        }

        /// <summary>
        /// Re-raises every property that depends on which variant is selected (and re-derives the AF step-size toggle
        /// default from the selected summary). Called after the variants are (re)populated and whenever the selection
        /// changes — it is where the old <c>Summary</c> setter's batch of notifications now lives.
        /// </summary>
        private void RaiseSelectedVariantDependents() {
            // Default the AF step-size toggle to match whether the SELECTED summary actually changes the AF settings
            // (opt-out when there is something to apply; off + disabled when nothing changed).
            applyRecommendedStepSize = CanApplyRecommendedStepSize;
            RaisePropertyChanged(nameof(SelectedVariant));
            RaisePropertyChanged(nameof(IsCurrentVariant));
            RaisePropertyChanged(nameof(IsOptimizedVariant));
            RaisePropertyChanged(nameof(IsFeedbackVariant));
            RaisePropertyChanged(nameof(Result));
            RaisePropertyChanged(nameof(Summary));
            RaisePropertyChanged(nameof(SelectedCurve));
            RaisePropertyChanged(nameof(HasSelectedCurve));
            RaisePropertyChanged(nameof(SelectedVariantLabel));
            RaisePropertyChanged(nameof(HasCurrent));
            RaisePropertyChanged(nameof(HasOptimized));
            RaisePropertyChanged(nameof(HasFeedback));
            RaisePropertyChanged(nameof(ApplyRecommendedStepSize));
            RaisePropertyChanged(nameof(CanApplyRecommendedStepSize));
            RaisePropertyChanged(nameof(ChangedParametersDisplay));
            RaisePropertyChanged(nameof(StarCountChanges));
            RaisePropertyChanged(nameof(HasStarCountChanges));
            RaisePropertyChanged(nameof(StarCountChangeBaselineLabel));
            RaisePropertyChanged(nameof(ShowReoptimizePrompt));
            RaisePropertyChanged(nameof(ShowFeedbackPanel));
            AcceptCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Resets all retained variants (called at the top of a fresh Start).</summary>
        private void ResetVariants() {
            currentResult = optimizedResult = feedbackResult = null;
            currentSummary = optimizedSummary = feedbackSummary = null;
            currentCurve = optimizedCurve = feedbackCurve = null;
            selectedVariant = OptimizationVariant.Current;
            OptimizerImprovedOverCurrent = false;
            RaiseSelectedVariantDependents();
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
        /// CanExecute for Start. Live is always startable (the camera/focuser connection is checked on Start with a
        /// clear error). Saved Auto-Focus stays disabled until EVERY run has a (non-empty) folder picked — the
        /// distinctness check happens on Start so the user gets an explanatory error rather than a silently-disabled
        /// button. Always disabled while a run is in flight.
        /// </summary>
        private bool CanStart() {
            if (IsBusy) {
                return false;
            }
            if (SourceMode == SourceMode.Live) {
                return true;
            }
            for (var i = 0; i < RunCount; i++) {
                if (string.IsNullOrWhiteSpace(i < SourcePaths.Count ? SourcePaths[i] : null)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Pre-flight source validation run when Start is clicked. Live: the camera AND focuser must be connected.
        /// Saved Auto-Focus: every run needs a folder (Start is already disabled until then; re-checked here as a
        /// safety net) and the folders must be DISTINCT (optimizing the same run twice is almost certainly a
        /// mistake). Sets <see cref="ErrorMessage"/> and returns false on failure.
        /// </summary>
        private bool ValidateSourceBeforeStart() {
            if (SourceMode == SourceMode.Live) {
                if (!isCameraConnected()) {
                    ErrorMessage = "Connect a camera before running a live auto-focus optimization.";
                    return false;
                }
                if (!isFocuserConnected()) {
                    ErrorMessage = "Connect a focuser before running a live auto-focus optimization.";
                    return false;
                }
                return true;
            }

            var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < RunCount; i++) {
                var path = i < SourcePaths.Count ? SourcePaths[i] : null;
                if (string.IsNullOrWhiteSpace(path)) {
                    ErrorMessage = "Select a saved auto-focus folder.";
                    return false;
                }
                if (!normalized.Add(NormalizePath(path))) {
                    ErrorMessage = "Each selected auto-focus folder must be different.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Normalizes a folder path for distinctness comparison (full path, trailing separators trimmed);
        /// falls back to the trimmed input when the path can't be expanded.</summary>
        private static string NormalizePath(string path) {
            try {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            } catch {
                return path?.Trim() ?? string.Empty;
            }
        }

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
            // Pre-flight validation: Live needs the camera + focuser connected; Saved needs a distinct, non-empty
            // folder per run. On failure, surface the error and stay on SelectSource without starting a run.
            if (!ValidateSourceBeforeStart()) {
                Interlocked.Exchange(ref running, 0);
                return;
            }
            ResetVariants();
            // Clear stale progress so a re-run after a cancel/complete doesn't briefly show the previous run's
            // evaluation count / phase / improvement before the first progress callback overwrites them.
            SetProgress(null, 0, 0);
            ProgressSeedJ = 0;
            ProgressBestJ = 0;
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
            reviewFrames = null;
            reviewParams = null;
            Recommendation = null;
            RaisePropertyChanged(nameof(HasLabels));
            RaisePropertyChanged(nameof(ShowReoptimizePrompt));
            RaisePropertyChanged(nameof(ShowFeedbackPanel));
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

                // 2. Seed guard — evaluate the current settings once; bail with a clear error on a degenerate run.
                if (!await SeedFitIsUsableAsync(loadedRuns, token).ConfigureAwait(true)) {
                    return; // ErrorMessage set by the guard
                }

                // 2b. Baseline J = the user's CURRENT settings' objective, the displayed "before" for improvement.
                //     Computed once here (cheap: the guard just warmed the current-settings contexts).
                currentBaselineJ = await ComputeBaselineJAsync(loadedRuns, token).ConfigureAwait(true);
                ProgressSeedJ = currentBaselineJ;

                // 3. Optimize — off the UI thread, progress marshaled back. OptimizeAsync always returns a
                // non-null result; it signals cancellation by throwing OperationCanceledException (caught below).
                // In "Use current settings" mode the optimization pass is skipped entirely: BestParams = the seed
                // (current settings), so the Summary shows no changes and the user can go straight to Review.
                OptimizationResult optimizeResult;
                bool optimized;
                if (OptimizeMode == WizardOptimizeMode.UseCurrentSettings) {
                    SetProgress("Using current settings (no optimization)", 0, 0);
                    optimizeResult = new OptimizationResult {
                        BestParams = loadedRuns[0].Baseline,
                        SeedJ = currentBaselineJ,
                        BestJ = currentBaselineJ,
                        Evaluations = 0,
                        ImprovedOverSeed = false,
                        ChangedVariables = new List<(string Name, double SeedValue, double BestValue)>()
                    };
                    optimized = false;
                } else {
                    CurrentStep = WizardStep.Optimize;
                    // Warm the early contexts the optimizer will start from (default seed, or the current settings
                    // when StartFromCurrentSettings is on) so the bar moves during the first build instead of
                    // sitting on the optimizer's seed evaluation.
                    await AnalyzeWithProgressAsync(loadedRuns,
                        r => StartFromCurrentSettings ? r.Baseline : r.Seed, token).ConfigureAwait(true);
                    optimizeResult = await OptimizeAsync(loadedRuns, token).ConfigureAwait(true);
                    optimized = true;
                }

                // 4. Summary + curves — re-evaluate seed vs best for σ(focus), recommend a step size, and capture the
                //    AF curves to plot (seed = "Current", best = "Optimized"). Done here while loadedRuns is alive,
                //    before the finally disposes the source Mats; the captured curves hold only Mat-free POCOs.
                var built = await BuildSummaryAsync(loadedRuns, optimizeResult, token).ConfigureAwait(true);

                // The Current (seed) variant always exists — for the comparison curve and the baseline summary.
                currentResult = new OptimizationResult {
                    BestParams = loadedRuns[0].Baseline,
                    SeedJ = currentBaselineJ,
                    BestJ = currentBaselineJ,
                    Evaluations = 0,
                    ImprovedOverSeed = false,
                    ChangedVariables = new List<(string Name, double SeedValue, double BestValue)>()
                };
                currentSummary = BuildCurrentSummary(built.Summary);
                currentCurve = built.CurrentCurve;

                if (optimized) {
                    optimizedResult = optimizeResult;
                    optimizedSummary = built.Summary;
                    optimizedCurve = built.OptimizedCurve;
                    // Only PRESENT the optimized result as the recommendation when it strictly beats the user's
                    // current settings. The search seeds from the default params and guarantees ">= seed", NOT
                    // ">= current"; on a saturated/easy run it can converge to a local optimum worse than a well-tuned
                    // current setup (validated on the mufti run: BestJ 0.999919 < current 0.999939). In that case
                    // default to Current so the default Accept keeps current; the Optimized variant stays available.
                    OptimizerImprovedOverCurrent = optimizeResult.BestJ > currentBaselineJ + ImprovementEpsilon;
                    selectedVariant = OptimizerImprovedOverCurrent
                        ? OptimizationVariant.Optimized // default to showing the genuine improvement
                        : OptimizationVariant.Current;  // optimizer couldn't beat current => keep current by default
                } else {
                    OptimizerImprovedOverCurrent = false;
                    selectedVariant = OptimizationVariant.Current; // review-only: only the current curve exists
                }
                RaiseSelectedVariantDependents();

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
                            ErrorMessage = "Select a saved auto-focus folder.";
                            CurrentStep = WizardStep.SelectSource;
                            DisposeLoadedRuns(runs);
                            return null;
                        }
                    }

                    SetProgress("Loading frames", 0, 0);
                    var loadProgress = new Progress<RunLoadProgress>(rp =>
                        SetProgress("Loading frames", rp.Current, rp.Total));
                    var loaded = await loader.LoadSavedRunAsync(folder, region, null, loadProgress, token).ConfigureAwait(true);
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
            // Evaluate the seed on every run WITH determinate "Analyzing frames" progress. This first pass also builds
            // each frame's expensive early-detection context (the long initial wait), so reporting it here is what
            // makes the bar advance instead of sitting on a static count; the optimizer then reuses the warmed contexts.
            var results = await AnalyzeWithProgressAsync(runs, r => r.Baseline, token).ConfigureAwait(true);
            var anyUsable = false;
            foreach (var eval in results) {
                if (double.IsFinite(eval.Metrics.SigmaFocus) && eval.PooledPointCount >= MinPositionsForFit) {
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

        /// <summary>
        /// Evaluates <paramref name="paramsSelector"/>'s params on each run while reporting determinate
        /// "Analyzing frames" progress as each frame's detection completes. Used to surface — and warm —
        /// the expensive first-evaluation early-context build so the progress bar moves during the long initial wait.
        /// Returns the per-run results (the seed-guard reads them; the re-optimize warm-up discards them, the call
        /// having only primed the cache).
        /// </summary>
        private async Task<List<RunEvaluationResult>> AnalyzeWithProgressAsync(
            IReadOnlyList<LoadedRun> runs, Func<LoadedRun, StarDetectorParams> paramsSelector, CancellationToken token) {
            var results = new List<RunEvaluationResult>(runs.Count);
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var run = runs[i];
                var label = "Analyzing frames";
                SetProgress(label, 0, run.Data.FrameCount);
                var frameProgress = new Progress<RunLoadProgress>(rp => SetProgress(label, rp.Current, rp.Total));
                results.Add(await run.Data.EvaluateAndFitAsync(paramsSelector(run), frameProgress, token).ConfigureAwait(true));
            }
            return results;
        }

        /// <summary>
        /// Computes the objective J of the user's CURRENT settings (<see cref="LoadedRun.Baseline"/>) on the loaded
        /// runs, using the same <see cref="ObjectiveConstants"/> and JRun/JTotal aggregation the optimizer uses, so the
        /// value is directly comparable to <see cref="OptimizationResult.BestJ"/>. This is the displayed "before"
        /// baseline for the improvement readouts. Cheap after the seed guard warmed the early-detection contexts (the
        /// late stage re-runs against the warmed cache).
        /// </summary>
        private async Task<double> ComputeBaselineJAsync(IReadOnlyList<LoadedRun> runs, CancellationToken token) {
            // Use a SINGLE current-settings bundle (runs[0].Baseline) across every run, exactly as the optimizer's
            // evaluator applies one StarDetectorParams across all runs — so this J is directly comparable to BestJ.
            var baseline = runs[0].Baseline;
            var perRunJ = new List<double>(runs.Count);
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var eval = await runs[i].Data.EvaluateAndFitAsync(baseline, token).ConfigureAwait(true);
                perRunJ.Add(OptimizationObjective.JRun(eval.Metrics, objectiveConstants));
            }
            return OptimizationObjective.JTotal(perRunJ, objectiveConstants);
        }

        /// <summary>Runs the pure optimizer off the UI thread; progress posts back via <see cref="IProgress{T}"/>.
        /// When <paramref name="seedOverride"/>/<paramref name="variablesOverride"/> are supplied (the warm-start
        /// "Optimize with feedback" path), the search starts from the analytic recommendation and explores only the
        /// narrowed/curated axes it produced; otherwise it uses the first run's seed and the full curated set.</summary>
        private async Task<OptimizationResult> OptimizeAsync(IReadOnlyList<LoadedRun> runs, CancellationToken token,
            StarDetectorParams seedOverride = null, IReadOnlyList<OptimizerVariable> variablesOverride = null) {
            // All runs share the same camera/optics, so the search starts from the first run's seed (or the override).
            // With StartFromCurrentSettings, seed from the current settings (Baseline) to refine them rather than the
            // fully-default params. The warm-start override (feedback path) always wins.
            var seed = seedOverride ?? (StartFromCurrentSettings ? runs[0].Baseline : runs[0].Seed);
            // The master donut toggle is a profile option, NOT an optimizer axis: stamp it onto the seed so the
            // seed's early morph-close runs and CreateCuratedSet includes the defocus axes iff the user enabled it
            // (the default Seed carries master=OFF, so without this the donut feature would never be searched when
            // not starting from current settings).
            seed.DefocusAwareDonutDetection = starDetectionOptions.DefocusAwareDonutDetection;
            // Donut recovery widens the curated search space (the defocus axes are added only when the master is
            // on), so it needs more iterations to converge: use the larger budget when enabled, else the standard
            // one. Re-applied each call so toggling the donut master between builds takes effect.
            optimizerSettings.MaxEvaluations = starDetectionOptions.DefocusAwareDonutDetection ? DonutMaxEvaluations : standardMaxEvaluations;
            var variables = variablesOverride ?? OptimizerVariable.CreateCuratedSet(seed);
            var evaluator = RunEvaluationData.CreateEvaluator(runs.Select(r => r.Data).ToList());

            ProgressTotal = optimizerSettings.MaxEvaluations;
            var progress = new Progress<OptimizationProgress>(p => {
                ProgressCurrent = p.Evaluations;
                ProgressTotal = p.MaxEvaluations;
                ProgressBestJ = p.BestJ;
                // The displayed baseline is the user's CURRENT settings (currentBaselineJ), not the optimizer's
                // default seed J (p.SeedJ), so the live "improved ~X%" reads vs current and matches the summary.
                ProgressSeedJ = currentBaselineJ;
                Phase = FriendlyPhase(p.Phase);
            });

            var optimizer = new StarDetectionOptimizer();
            IsOptimizing = true;
            try {
                return await Task.Run(
                    () => optimizer.OptimizeAsync(seed, variables, evaluator, optimizerSettings, progress, token),
                    token).ConfigureAwait(true);
            } finally {
                IsOptimizing = false;
            }
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
        /// Builds the summary: the changed-parameters table, before/after J + σ(focus) (re-evaluated at the current
        /// settings (baseline) and best, averaged across runs), and the recommended step size from the representative (first) run's best
        /// fit, clamped to the focuser's max increment when known.
        /// </summary>
        private async Task<(OptimizationSummary Summary, OptimizationCurve CurrentCurve, OptimizationCurve OptimizedCurve)>
            BuildSummaryAsync(IReadOnlyList<LoadedRun> runs, OptimizationResult res, CancellationToken token) {
            // The displayed "before" is the user's CURRENT settings (Baseline), NOT the optimizer's default seed.
            var baseline = runs[0].Baseline;

            // Changed-parameters table = current (Baseline) -> optimized (BestParams) over the curated knobs, so it is
            // exactly the diff the user would accept onto their live settings (consistent with the σ/J improvement,
            // which is also measured vs current). This intentionally replaces res.ChangedVariables (default -> best).
            var changed = new List<ChangedParameterRow>();
            // Iterate the SAME axis set the search used: baseline (current settings) carries the master toggle, so
            // the defocus axes appear in the diff iff the user enabled donut detection.
            foreach (var v in OptimizerVariable.CreateCuratedSet(baseline)) {
                var before = v.Read(baseline);
                var after = v.Read(res.BestParams);
                if (Math.Abs(before - after) > 1e-9) {
                    changed.Add(new ChangedParameterRow { Name = v.Name, SeedValue = before, OptimizedValue = after });
                }
            }

            // σ(focus) before/after, averaged over the runs (the first run also yields the representative best fit
            // AND the curves we plot — baseline = "Current", best = "Optimized"; both carry the scatter points + fit).
            double baselineSigmaSum = 0.0, bestSigmaSum = 0.0;
            var baselineSigmaCount = 0; var bestSigmaCount = 0;
            AlglibHyperbolicFitting representativeBestFit = null;
            OptimizationCurve currentCurveLocal = null, optimizedCurveLocal = null;
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var baselineEval = await runs[i].Data.EvaluateAndFitAsync(baseline, token).ConfigureAwait(true);
                var bestEval = await runs[i].Data.EvaluateAndFitAsync(res.BestParams, token).ConfigureAwait(true);
                if (double.IsFinite(baselineEval.Metrics.SigmaFocus)) { baselineSigmaSum += baselineEval.Metrics.SigmaFocus; baselineSigmaCount++; }
                if (double.IsFinite(bestEval.Metrics.SigmaFocus)) { bestSigmaSum += bestEval.Metrics.SigmaFocus; bestSigmaCount++; }
                if (i == 0) {
                    representativeBestFit = bestEval.BestFit;
                    currentCurveLocal = new OptimizationCurve {
                        Label = "Current", Points = baselineEval.Points, Fit = baselineEval.BestFit,
                        FrameStarCounts = baselineEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = baselineEval.Metrics.FrameFocuserPositions
                    };
                    optimizedCurveLocal = new OptimizationCurve {
                        Label = "Optimized", Points = bestEval.Points, Fit = bestEval.BestFit,
                        FrameStarCounts = bestEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = bestEval.Metrics.FrameFocuserPositions
                    };
                }
            }

            var currentStepSize = runs[0].AfOptions?.AutoFocusStepSize ?? DefaultCurrentStepSize;
            var currentOffsetSteps = runs[0].AfOptions?.AutoFocusInitialOffsetSteps ?? DefaultCurrentOffsetSteps;
            var recommendation = StepSizeRecommender.Recommend(representativeBestFit, currentStepSize, GetFocuserMaxStep());

            var summary = new OptimizationSummary {
                ChangedParameters = changed,
                SeedJ = currentBaselineJ,
                BestJ = res.BestJ,
                SeedSigmaFocus = baselineSigmaCount > 0 ? baselineSigmaSum / baselineSigmaCount : double.NaN,
                BestSigmaFocus = bestSigmaCount > 0 ? bestSigmaSum / bestSigmaCount : double.NaN,
                RunCount = runs.Count,
                RecommendedStepSize = recommendation.StepSize,
                RecommendedOffsetSteps = recommendation.OffsetSteps,
                CurrentStepSize = currentStepSize,
                CurrentOffsetSteps = currentOffsetSteps,
                ImprovedOverSeed = res.ImprovedOverSeed
            };
            return (summary, currentCurveLocal, optimizedCurveLocal);
        }

        /// <summary>
        /// Builds the "Current" variant's summary from the optimized run's summary: nothing changed, so the
        /// changed-parameters table is empty, J/σ collapse to the seed baseline, and the AF recommendation equals the
        /// current profile values (so the apply-step-size toggle is disabled). Used so toggling to "Current" shows a
        /// truthful "no changes" summary alongside the current curve.
        /// </summary>
        private static OptimizationSummary BuildCurrentSummary(OptimizationSummary optimized) {
            return new OptimizationSummary {
                ChangedParameters = Array.Empty<ChangedParameterRow>(),
                SeedJ = optimized.SeedJ,
                BestJ = optimized.SeedJ,
                SeedSigmaFocus = optimized.SeedSigmaFocus,
                BestSigmaFocus = optimized.SeedSigmaFocus,
                RunCount = optimized.RunCount,
                RecommendedStepSize = optimized.CurrentStepSize,
                RecommendedOffsetSteps = optimized.CurrentOffsetSteps,
                CurrentStepSize = optimized.CurrentStepSize,
                CurrentOffsetSteps = optimized.CurrentOffsetSteps,
                ImprovedOverSeed = false
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
        /// <summary>Accept is enabled only on the finished Summary view, when the VM is idle, and when a non-Current
        /// variant (something there is actually to apply) is selected. This keeps Accept disabled while the Start /
        /// SelectSource view or a run-in-progress is showing, and in review-only (Current selected).</summary>
        private bool CanAccept() =>
            !IsBusy && IsSummary && selectedVariant != OptimizationVariant.Current
            && SelectedResult != null && SelectedSummary != null;

        private void Accept() {
            PersistReviewLabels();
            Apply(SelectedResult, SelectedSummary);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Applies the result. Builds an <see cref="OptimizedStarDetectionSettings"/> from
        /// <see cref="OptimizationResult.BestParams"/> + run metadata and hands it to
        /// <see cref="IStarDetectionOptions.ApplyOptimizedSettings"/>. When <see cref="ApplyRecommendedStepSize"/>
        /// is set, also writes the recommended AF step size/offset to the active profile. Only ever called via
        /// <see cref="Accept"/> when the user accepts the summary — never during the search.
        /// </summary>
        private void Apply(OptimizationResult result, OptimizationSummary summary) {
            if (result == null || summary == null) {
                return;
            }

            // Build the snapshot via the shared params->DTO mapping (the single source of truth shared with the
            // headless harness) so the in-app and offline paths can never drift. CreatedAtUtc is stamped inside.
            // Record the displayed (vs current-settings) before/after J in the snapshot + log, so the persisted record
            // matches the improvement the user saw. summary.SeedJ is the current-settings baseline J; summary.BestJ is
            // the optimizer's best (== result.BestJ).
            var dto = OptimizedStarDetectionSettings.FromParams(
                result.BestParams, summary.RunCount, summary.SeedJ, summary.BestJ,
                summary.RecommendedStepSize, summary.RecommendedOffsetSteps);

            starDetectionOptions.ApplyOptimizedSettings(dto);
            Logger.Info($"Applied optimized star-detection settings (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");

            if (ApplyRecommendedStepSize) {
                var focuserSettings = profileService?.ActiveProfile?.FocuserSettings;
                if (focuserSettings != null) {
                    focuserSettings.AutoFocusStepSize = summary.RecommendedStepSize;
                    focuserSettings.AutoFocusInitialOffsetSteps = summary.RecommendedOffsetSteps;
                    Logger.Info($"Applied recommended AF step size {summary.RecommendedStepSize}, offset steps {summary.RecommendedOffsetSteps}");
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
            SetProgress("Detecting frames for review", 0, descriptors.Count);
            try {
                // Detect every frame at the optimized params, off the UI thread. The production builder seam wraps the
                // shared FrameReviewBuilder in a Task.Run, so the whole build (disk load + detection) runs on the
                // threadpool and never stutters the UI thread; we just await the result here. The resulting
                // FrameReviews carry frozen overlays + a disk-backed image provider.
                var reviewProgress = new Progress<RunLoadProgress>(rp => SetProgress("Detecting frames for review", rp.Current, rp.Total));
                var reviews = await frameReviewBuilder(descriptors, bestParams, reviewProgress, cts?.Token ?? CancellationToken.None).ConfigureAwait(true);
                if (reviews == null || reviews.Count == 0) {
                    ErrorMessage = "No frames could be prepared for review.";
                    return;
                }
                // Keep the detected frames + the params they were detected at for the feedback analyzer.
                reviewFrames = reviews;
                reviewParams = bestParams;

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
                RaisePropertyChanged(nameof(ShowReoptimizePrompt));
                RaisePropertyChanged(nameof(ShowFeedbackPanel));
                ReOptimizeCommand.NotifyCanExecuteChanged();

                // Replace any prior review (drop its label-change subscription first to avoid a dangling handler).
                if (ReviewVM != null) {
                    ReviewVM.PropertyChanged -= OnReviewLabelsChanged;
                }
                ReviewVM = new StarReviewVM(reviews, labelsByRun, labelsDir ?? string.Empty, starDetectionOptions.MeasurementAverage,
                    normalizedHfrActive: starDetectionOptions.DefocusAwareDonutDetection && starDetectionOptions.UseNormalizedHFR);
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
                RaisePropertyChanged(nameof(ShowReoptimizePrompt));
                RaisePropertyChanged(nameof(ShowFeedbackPanel));
                ReOptimizeCommand.NotifyCanExecuteChanged();
            }
        }

        private GateRecommendation recommendation;

        /// <summary>The transparent label→gate analysis produced by the last "Optimize with feedback": which gate
        /// rejected each labeled star, and the recommended threshold changes (precision-bounded). Bound by the
        /// Summary's feedback breakdown. Null until the user runs feedback.</summary>
        public GateRecommendation Recommendation {
            get => recommendation;
            private set {
                recommendation = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasRecommendation));
                RaisePropertyChanged(nameof(RecommendationRows));
                RaisePropertyChanged(nameof(ShowFeedbackPanel));
            }
        }

        public bool HasRecommendation => recommendation != null && recommendation.Rows.Count > 0;

        public IReadOnlyList<RecommendationRow> RecommendationRows => recommendation?.Rows ?? new List<RecommendationRow>();

        /// <summary>
        /// Builds the direct label→gate analysis from the LAST review's detected frames (which carry the rich
        /// rejected-candidate records) and the live captured labels, then runs the recommender. Returns null when no
        /// review/labels are available (the caller then falls back to the plain label objective). No re-detection —
        /// it reuses what the Review step already computed at the reviewed params.
        /// </summary>
        private GateRecommendation ComputeFeedbackRecommendation() {
            if (reviewFrames == null || reviewParams == null || capturedLabels == null) {
                return null;
            }

            RectD ToRectD(StarReviewLabelBox b) => new RectD(b.X, b.Y, b.W ?? 0.0, b.H ?? 0.0);

            var frames = new List<FrameDetectionForAnalysis>(reviewFrames.Count);
            foreach (var fr in reviewFrames) {
                StarReviewPositionLabels pos = null;
                if (fr.RunId != null && capturedLabels.TryGetValue(fr.RunId, out var runLabels)) {
                    pos = runLabels?.Positions?.FirstOrDefault(p => p.FocuserPosition == fr.FocuserPosition);
                }
                var recall = new List<RectD>();
                var shouldReject = new List<RectD>();
                if (pos != null) {
                    foreach (var b in (pos.Missed ?? new List<StarReviewLabelBox>()).Concat(pos.WronglyRejected ?? new List<StarReviewLabelBox>())) {
                        recall.Add(ToRectD(b));
                    }
                    foreach (var b in pos.ShouldReject ?? new List<StarReviewLabelBox>()) {
                        shouldReject.Add(ToRectD(b));
                    }
                }
                frames.Add(new FrameDetectionForAnalysis {
                    FocuserPosition = fr.FocuserPosition,
                    AcceptedBounds = fr.Accepted.Select(a => a.Bounds).ToList(),
                    AcceptedCenters = fr.Accepted.Select(a => (a.CX, a.CY)).ToList(),
                    Rejected = fr.RejectedCandidates ?? new List<RejectedCandidateRecord>(),
                    RecallBoxes = recall,
                    ShouldRejectBoxes = shouldReject
                });
            }

            var analysis = LabelGateAnalyzer.Analyze(frames);
            return GateRecommender.Recommend(analysis, reviewParams, new RecommenderConfig());
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
            // Clear stale progress so the re-optimize doesn't briefly show the prior run's numbers.
            SetProgress(null, 0, 0);
            ProgressSeedJ = 0;
            ProgressBestJ = 0;
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
                    SetProgress("Re-loading frames", 0, 0);
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
                    var loadProgress = new Progress<RunLoadProgress>(rp =>
                        SetProgress("Loading frames", rp.Current, rp.Total));
                    var loaded = await loader.LoadSavedRunAsync(folder, region, frameLabels, loadProgress, token).ConfigureAwait(true);
                    reloaded.Add(loaded);
                }

                // Direct label→gate analysis (the transparent step): attribute each labeled star to the gate that
                // killed it and recommend precision-bounded threshold changes. Then WARM-START the optimizer from
                // that recommendation (seed + narrowed/curated axes) so it balances the analytic point against
                // focus-curve quality. If no review/labels are available, recommendation is null and the optimizer
                // runs as before (the plain label objective term still applies).
                var feedback = ComputeFeedbackRecommendation();
                Recommendation = feedback;
                StarDetectorParams seedOverride = null;
                IReadOnlyList<OptimizerVariable> variablesOverride = null;
                if (feedback != null) {
                    seedOverride = feedback.Recommended;
                    variablesOverride = OptimizerVariable.CreateWarmStartSet(
                        OptimizerVariable.CreateCuratedSet(reviewParams), reviewParams, feedback.Recommended);
                    // The recommendation may only move axes that aren't searchable (e.g. it recommends the defocus
                    // gates while the master donut toggle is OFF, so they're excluded from the curated set). That
                    // would leave an EMPTY warm-start set, which the optimizer can't run. Fall back to the full
                    // (master-gated) curated set in OptimizeAsync so the feedback round still refines the base axes.
                    if (variablesOverride.Count == 0) {
                        variablesOverride = null;
                    }
                }

                // Warm + report the per-frame early contexts for the params the optimizer will start from (re-optimize
                // skips the seed-guard, so this is the parity warm-up that makes the bar move during the initial build).
                await AnalyzeWithProgressAsync(reloaded, _ => seedOverride ?? reloaded[0].Seed, token).ConfigureAwait(true);

                // Baseline J for the reloaded runs (current settings) — the displayed "before" for the feedback summary,
                // keeping the improvement measured vs the user's current settings on this path too.
                currentBaselineJ = await ComputeBaselineJAsync(reloaded, token).ConfigureAwait(true);

                // Re-run the optimize + summary pipeline over the labeled runs (warm-started when we have a recommendation).
                var optimizeResult = await OptimizeAsync(reloaded, token, seedOverride, variablesOverride).ConfigureAwait(true);
                var built = await BuildSummaryAsync(reloaded, optimizeResult, token).ConfigureAwait(true);

                // Retain as the FEEDBACK variant. This REPLACES any prior feedback and leaves the first Optimized
                // variant (and Current) untouched, so only the latest feedback persists while the original optimization
                // stays available to compare/accept. Carry the optimized-without-feedback σ/J baseline so the header
                // can show how much the feedback round improved over the plain optimization.
                feedbackResult = optimizeResult;
                feedbackSummary = built.Summary;
                feedbackSummary.PriorSigmaFocus = optimizedSummary?.BestSigmaFocus;
                feedbackSummary.PriorBestJ = optimizedResult?.BestJ;
                feedbackCurve = built.OptimizedCurve;
                if (feedbackCurve != null) {
                    feedbackCurve.Label = "Optimized (with feedback)";
                }
                selectedVariant = OptimizationVariant.Feedback;
                RaiseSelectedVariantDependents();

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
                RaisePropertyChanged(nameof(ShowReoptimizePrompt));
                RaisePropertyChanged(nameof(ShowFeedbackPanel));
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
        private static Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>>
            BuildProductionReviewBuilder(IProfileService profileService, IImageDataFactory imageDataFactory) {
            return (descriptors, p, progress, token) => {
                var detector = new StarDetector(HocusFocusPlugin.AlglibAPI);
                // Hop to the threadpool so the WHOLE build (disk load + detection) runs off the captured UI context.
                // FrameReviewBuilder.BuildAsync awaits its work, so without this hop its synchronous prologue (and any
                // continuation that resumes on the UI SynchronizationContext) could stutter the UI thread. Progress
                // is posted via the IProgress the VM created on the UI context, so per-frame reports marshal back.
                return Task.Run(
                    () => FrameReviewBuilder.BuildAsync(
                        descriptors, p, detector,
                        path => LoadFloatMatFromDisk(path, profileService, imageDataFactory),
                        token, progress),
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
