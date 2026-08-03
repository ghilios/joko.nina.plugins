#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.MyMessageBox;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile.Interfaces;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
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

    /// <summary>One row of the summary's changed-parameters table (seed vs optimized for a single knob). For a
    /// single optimization pass this is just current → optimized; after "Continue optimizing" it carries the full
    /// per-round path in <see cref="Stages"/> (current → round1 → round2 → …), rendered as an arrowed
    /// <see cref="Trajectory"/>.</summary>
    public sealed class ChangedParameterRow {
        public string Name { get; set; }
        public double SeedValue { get; set; }
        public double OptimizedValue { get; set; }

        /// <summary>The value of this parameter at each stage: [current, round1, round2, …]. Defaults to the
        /// 2-stage [SeedValue, OptimizedValue] when not explicitly set (single-pass rows / AF step-size rows).</summary>
        public IReadOnlyList<double> Stages { get; set; }

        /// <summary>The full path as an arrowed string, e.g. "0.500 → 0.620 → 0.680 → 0.700". Falls back to
        /// current → optimized when <see cref="Stages"/> was not populated.</summary>
        public string Trajectory =>
            string.Join("  →  ", (Stages ?? new[] { SeedValue, OptimizedValue }).Select(v => v.ToString("F3")));
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

        /// <summary>The software detection binning this run actually analyzed at ("F"). Not necessarily the
        /// persisted setting: an "Optimize again at NxN" pass runs at a factor the user has not committed to.</summary>
        public int RunDetectionBinning { get; set; } = 1;

        /// <summary>The persisted Star Detector setting at the time this summary was built ("P"). When it differs
        /// from <see cref="RunDetectionBinning"/> this summary came from an optimize-again pass, and Accept writes
        /// the factor and the tuned settings together.</summary>
        public int PersistedDetectionBinning { get; set; } = 1;

        /// <summary>The factor the MEASURED in-focus HFR implies ("R"), via the same target the options-page
        /// recommendation uses. Derived from the sweep's own fitted curve rather than an assumed seeing figure, so
        /// it is the better answer whenever a run exists.</summary>
        public int RecommendedDetectionBinning { get; set; } = 1;

        /// <summary>
        /// Fitted minimum (in-focus) HFR for THIS VARIANT's settings, in captured pixels — the optimized settings
        /// on the Optimized/Feedback views, the current ones on the Current view. It must follow the variant,
        /// because the variant is what Accept applies and what the chart above is showing: reading it from the
        /// current-settings curve while displaying the optimized one produced a recommendation that contradicted
        /// the graph (a run whose optimized curve bottomed at 5.1 px reported 4.3 px and asked for no change).
        /// NaN when that variant's fit was degenerate.
        /// </summary>
        public double MeasuredInFocusHfr { get; set; } = double.NaN;

        /// <summary>R² of the focus-curve fit <see cref="MeasuredInFocusHfr"/> came from. NaN when no fit.</summary>
        public double FitRSquared { get; set; } = double.NaN;

        /// <summary>The CURRENT-settings equivalents, carried so the Current variant's summary can be built from
        /// this one (see <c>BuildCurrentSummary</c>) without re-evaluating the runs.</summary>
        public double BaselineMeasuredInFocusHfr { get; set; } = double.NaN;

        public double BaselineFitRSquared { get; set; } = double.NaN;

        /// <summary>
        /// Minimum fit quality before the fitted curve minimum may be used as an in-focus HFR.
        ///
        /// <para>Measured on the simulator at 2800 mm: while the detector still tracked the defocused donuts the
        /// fit sat at R² ≥ 0.998 and its vertex agreed with the optics model to within 5%. Once the sweep ran wide
        /// enough that the outer frames lost the donuts — the detector then reports a couple of compact ~2 px noise
        /// blobs instead — R² went NEGATIVE and the vertex landed at 2.16 px against a 4.87 px truth. No estimator
        /// recovers from that (the smallest measured point and the point nearest best focus were just as wrong), so
        /// the only honest response is to say nothing.</para>
        /// </summary>
        public const double MinRSquaredForBinningRecommendation = 0.9;

        /// <summary>Whether there is a trustworthy measurement to reason about. False hides the block entirely: a
        /// degenerate or badly-fitting curve is already visible on the chart and has nothing to say about binning.</summary>
        public bool HasDetectionBinningMeasurement =>
            double.IsFinite(MeasuredInFocusHfr) && double.IsFinite(FitRSquared) && FitRSquared >= MinRSquaredForBinningRecommendation;

        /// <summary>True when the measured curve calls for a factor this run did NOT analyze at, so there is
        /// something to offer.</summary>
        public bool DetectionBinningDiffers =>
            HasDetectionBinningMeasurement && RecommendedDetectionBinning != RunDetectionBinning;

        /// <summary>True when this summary came from an optimize-again pass: the run analyzed at a factor that is
        /// not yet persisted, so Accept has a factor to write alongside the settings.</summary>
        public bool DetectionBinningPendingApply =>
            HasDetectionBinningMeasurement && RunDetectionBinning != PersistedDetectionBinning;

        /// <summary>The recommended-factor row, e.g. "1x1 -> 2x2 (measured in-focus HFR 6.1 px)".</summary>
        public string DetectionBinningText {
            get {
                if (!HasDetectionBinningMeasurement) {
                    return $"{RunDetectionBinning}x{RunDetectionBinning}";
                }
                if (DetectionBinningPendingApply) {
                    return $"{PersistedDetectionBinning}x{PersistedDetectionBinning} -> {RunDetectionBinning}x{RunDetectionBinning} (applied on Accept; measured in-focus HFR {MeasuredInFocusHfr:F1} px)";
                }
                return DetectionBinningDiffers
                    ? $"{RunDetectionBinning}x{RunDetectionBinning} -> {RecommendedDetectionBinning}x{RecommendedDetectionBinning} (measured in-focus HFR {MeasuredInFocusHfr:F1} px)"
                    : $"{RunDetectionBinning}x{RunDetectionBinning} (unchanged; measured in-focus HFR {MeasuredInFocusHfr:F1} px)";
            }
        }

        /// <summary>
        /// The per-frame exposure time (seconds) the run this summary describes was captured with — a Live sweep's
        /// own exposure, a Replay run's recorded header value, or the profile's auto-focus exposure as a last
        /// resort. <see cref="double.NaN"/> when none of those yielded a positive number, in which case
        /// <see cref="ExposureAdvice"/> carries no derived seconds (a factor scaled off an unknown base is worse
        /// than no recommendation). Read by the body copy to name the base when
        /// <see cref="RunExposureIsAssumed"/>.
        /// </summary>
        public double RunExposureSeconds { get; set; } = double.NaN;

        /// <summary>
        /// True when <see cref="RunExposureSeconds"/> came from the PROFILE's auto-focus exposure rather than from
        /// the run itself — i.e. the saved frames recorded no exposure, so the whole derivation rests on an
        /// assumption about how these frames were captured. The body copy states this outright: a derived number
        /// computed off an assumed input must not read as a measurement of the frames.
        /// </summary>
        public bool RunExposureIsAssumed { get; set; }

        /// <summary>
        /// The Sensitivity (BrightnessSensitivity) gate THIS VARIANT's settings use — the optimized value on the
        /// Optimized/Feedback views, the user's CURRENT value on the Current view. Named for the variant rather
        /// than for the optimizer precisely because of that second case. It must follow the variant for the same
        /// reason <see cref="MeasuredInFocusHfr"/> does: the variant is what Accept applies, so a Current view
        /// whose OWN hand-set gate sits at the floor has to say so rather than report the optimizer's.
        /// </summary>
        public double VariantSensitivity { get; set; } = double.NaN;

        /// <summary>The CURRENT-settings equivalent, carried so the Current variant's summary can be built from
        /// this one (see <c>BuildCurrentSummary</c>) without re-evaluating the runs — exactly as
        /// <see cref="BaselineMeasuredInFocusHfr"/> is.</summary>
        public double BaselineSensitivity { get; set; } = double.NaN;

        /// <summary>The exposure-time recommendation derived from THIS VARIANT's accepted-star SNRs (see
        /// <see cref="ExposureRecommender"/>). Null when the summary was built without one (every pre-feature
        /// construction, and every unit test that does not exercise this block).</summary>
        public ExposureRecommendation ExposureAdvice { get; set; }

        /// <summary>The CURRENT-settings equivalent, carried for <c>BuildCurrentSummary</c> — the baseline
        /// counterpart to <see cref="ExposureAdvice"/>, mirroring the <see cref="BaselineMeasuredInFocusHfr"/>
        /// pair.</summary>
        public ExposureRecommendation BaselineExposureAdvice { get; set; }

        /// <summary>
        /// Whether this variant's detector is running on a floored acceptance gate — the condition the "Star
        /// signal" block exists to report. Named for the FINDING, not for the block's contents: it deliberately
        /// does NOT mean "an exposure recommendation exists" (that is
        /// <see cref="ExposureRecommendation.HasRecommendation"/> on <see cref="ExposureAdvice"/>, which can be
        /// false while this is true).
        ///
        /// <para>This is the SENSITIVITY GATE ALONE — deliberately NOT additionally gated on the derived exposure
        /// factor being large, on the star counts, or on <see cref="ExposureAdvice"/> having produced a number. A
        /// Sensitivity that landed at its search floor means the detector had to admit essentially anything above
        /// the noise to find stars at all, and that is worth saying out loud EVEN WHEN a longer exposure is not the
        /// answer: the sub-states carry that nuance (<see cref="ExposureRecommendation.ExposureIsNotTheLimit"/>
        /// says exposure is not what is limiting the run; a missing recommendation says only the diagnosis can be
        /// given). Gating the whole block on "we have a big number to show" would silently hide the one finding the
        /// user most needs — that the focus result rests on low-confidence detections — in exactly the cases where
        /// nothing can be done about it.</para>
        ///
        /// <para>NaN (the default, i.e. a summary built before this feature or by a test that does not set it)
        /// is false: <c>NaN &lt;= threshold</c> is false, so the block stays hidden rather than firing on
        /// "unknown".</para>
        /// </summary>
        public bool HasLowStarSignal => ExposureRecommender.SensitivityIsAtFloor(VariantSensitivity);

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

        /// <summary>True when the recommended step size was capped because this sweep was too shallow to contain
        /// the band it is measured from, so it is a partial step toward the answer (see
        /// <see cref="StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple"/>).</summary>
        public bool StepSizeWasCapped { get; set; }

        /// <summary>Plain-language step-size readout: "{current} → {recommended}" when changed, else
        /// "{recommended} (unchanged)", with a capped note when the sweep could not support the full move.</summary>
        public string StepSizeText => StepSizeWasCapped
            ? FormatRecommendation(CurrentStepSize, RecommendedStepSize) + " (capped by this sweep's width; re-run auto-focus to refine)"
            : FormatRecommendation(CurrentStepSize, RecommendedStepSize);

        /// <summary>Plain-language offset-steps readout (same before→after / "(unchanged)" convention).</summary>
        public string OffsetStepsText => FormatRecommendation(CurrentOffsetSteps, RecommendedOffsetSteps);

        /// <summary>Minimum strictly-positive J margin for a result to count as beating the baseline it is measured
        /// against. Just above double round-off so a true tie (e.g. the StartFromCurrentSettings path returning
        /// current unchanged) reads as "no improvement".</summary>
        public const double ImprovementEpsilon = 1e-9;

        /// <summary>σ moves smaller than this are "unchanged": they vanish at the F2 precision every σ readout uses.</summary>
        public const double SigmaUnchangedTolerance = 0.005;

        /// <summary>Focus-precision readout: "{seed} → {best}" with a "(~N% tighter)" suffix when σ improved
        /// (σ is the focus-curve sigma — lower is tighter/better).</summary>
        public string SigmaText {
            get {
                // "2.20 (unchanged)" when σ is effectively the same before/after (compared at the F2 precision
                // shown), else "{seed} → {best}" with a "(~N% tighter)" suffix when it improved.
                if (double.IsFinite(SeedSigmaFocus) && double.IsFinite(BestSigmaFocus)
                    && Math.Abs(SeedSigmaFocus - BestSigmaFocus) < SigmaUnchangedTolerance) {
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

        /// <summary>Arrow-joined σ(focus) path "2.20 → 1.85 → 1.52" across the optimization rounds, with a
        /// "(~N% tighter)" suffix when the last σ improved on the first (σ is lower-is-better). Non-finite σ are
        /// dropped; an empty/all-NaN input yields "". Pure — unit-tested.</summary>
        public static string FormatSigmaTrajectory(IReadOnlyList<double> sigmas) {
            var finite = sigmas?.Where(double.IsFinite).ToList() ?? new List<double>();
            if (finite.Count == 0) {
                return string.Empty;
            }
            var path = string.Join(" → ", finite.Select(s => s.ToString("F2")));
            var first = finite[0];
            var last = finite[finite.Count - 1];
            if (first > 1e-9 && last < first) {
                var pct = (first - last) / first * 100.0;
                return $"{path}  (~{pct:F0}% tighter)";
            }
            return path;
        }

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

        /// <summary>
        /// The live, plain-language readout shown under the progress bar while the search runs. It reports the
        /// incumbent's focus precision as the same "{seed:F2} → {best:F2}" σ pair the results page headlines
        /// (<see cref="SigmaText"/>), so the two can never disagree.
        ///
        /// <para>It deliberately does NOT report a percentage of the objective J. J is a saturating score in [0, 1]
        /// that already sits near its ceiling on a decent setup — a real run reads 0.999939 → 0.999919 — so a
        /// J-relative percent is ~1e-5 and always renders as "0%", which contradicted the results page's
        /// "(~N% tighter)".</para>
        ///
        /// <para>The improvement gate is the J comparison, not the σ one: an incumbent may hold a tighter σ while
        /// still scoring below the user's current settings (the saturated plateau the Wtie tie-breaker exists for),
        /// and that run ends on "kept your current settings". Gating on J is what keeps the live line from promising
        /// an improvement the results page then withholds.</para>
        ///
        /// <para><paramref name="gateBaselineJ"/> is therefore the STRICTER of the pass's own baseline and the user's
        /// current-settings J — see <c>StarDetectionOptimizerWizardVM.ProgressGateJ</c>. The σ pair shown is measured
        /// from <paramref name="baselineSigma"/>, which is the pass's own baseline (the current settings on a fresh
        /// Start, the prior round's best on a Continue pass).</para>
        ///
        /// Pure — unit-tested.
        /// </summary>
        public static string LiveImprovementText(double gateBaselineJ, double bestJ, double baselineSigma, double bestSigma) {
            if (!(bestJ > gateBaselineJ + ImprovementEpsilon)) {
                return "Searching for a better fit…";
            }
            if (double.IsFinite(baselineSigma) && double.IsFinite(bestSigma)
                && baselineSigma - bestSigma >= SigmaUnchangedTolerance) {
                return $"Best so far: σ {baselineSigma:F2} → {bestSigma:F2}";
            }
            // J improved without a visible σ gain — more stars under the aberration-inspection reweight, or a
            // degenerate fit with no σ. Say so plainly rather than print a σ pair that reads as a focus win.
            return "Found better settings so far";
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
        // The persisted AF options, used only to seed and persist the Live sweep's save folder (AutoFocusOptions.SavePath).
        // Optional/nullable so the interfaces-only test constructor stays mediator-free; production wires the plugin singleton.
        private readonly IAutoFocusOptions autoFocusOptions;
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
        // (drives OptimizerImprovedOverCurrent / the default-to-Current guard). Shared with the live readout's gate so
        // the progress line and the results page agree on what counts as an improvement.
        private const double ImprovementEpsilon = OptimizationSummary.ImprovementEpsilon;

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

        // Live-sweep collaborators. confirmRoughFocus asks the user to confirm the current position is roughly focused
        // (production shows an OK/Cancel dialog; tests default to confirmed). currentFilterName/currentGain report the
        // actual filter and gain the sweep will expose with (from the filter wheel / camera), for the confirmation panel.
        private readonly Func<bool> confirmRoughFocus;

        // Confirms re-running the search at a different detection binning factor. Takes (from, to) so the dialog can
        // name both. Defaults to "yes" so tests and headless paths are not blocked.
        private readonly Func<int, int, bool> confirmReoptimizeAtBinning;

        // Confirms capturing a WHOLE NEW SWEEP at a longer exposure. Takes (from, to) seconds so the dialog can name
        // both. Same default-to-yes contract as confirmReoptimizeAtBinning: tests and headless paths proceed. This
        // one matters more than its sibling — the sibling re-reads frames already on disk, this one moves the
        // focuser and spends minutes of sky time.
        private readonly Func<double, double, bool> confirmCaptureNewSweep;
        private readonly Func<string> currentFilterName;
        private readonly Func<int?> currentGain;

        // Per-filter star detection collaborators (delegates, so the VM stays store/mediator-free and testable).
        // perFilterEnabled gates the target-filter picker and per-filter routing; the rest resolve profile
        // filters and the per-filter settings store. Defaults keep the feature off for existing callers/tests.
        private readonly Func<bool> perFilterEnabled;
        private readonly Func<bool> isFilterWheelConnected;
        private readonly Func<IReadOnlyList<string>> getFilterNames;
        private readonly Func<string> getCurrentFilterName;
        private readonly Func<string, FilterInfo> resolveFilterByName;
        private readonly Func<string, IStarDetectionOptions> getFilterDetectionOptions;
        private readonly Action<string, OptimizedStarDetectionSettings> applyOptimizedToFilter;
        // Writes the donut master into ONE filter's settings set. Separate from getFilterDetectionOptions because the
        // store's getters return CLONES: mutating what they hand back persists nothing. Production routes this through
        // PerFilterEditBinder.MutateFilterSettings, which picks the store or the edit buffer as appropriate.
        private readonly Action<string, bool> setFilterDonutDetection;

        // The last measured in-focus HFR (captured pixels), for the confirmation panel's recommendation. Supplied by
        // the plugin from InFocusHfrRecord; null in tests, which then show the no-measurement copy.
        private readonly Func<double> getMeasuredInFocusHfr;

        // Publishes a live sweep's measured in-focus HFR back to the shared record, so the options page and the
        // wizard cannot disagree. Null in tests.
        private readonly Action<double> recordMeasuredInFocusHfr;

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

        // The objective the optimizer uses. Rebuilt once per run in ComputeBaselineJAsync: the STANDARD objective by
        // default, or — when OptimizeForAberrationInspection is on — ObjectiveConstants.ForAberrationInspection
        // anchored to the measured current-settings σ. The wizard computes the current-settings baseline J with the
        // SAME constants it hands the optimizer, so the before/after numbers stay comparable to BestJ.
        private ObjectiveConstants objectiveConstants = new ObjectiveConstants();

        // J of the user's CURRENT settings (Baseline), evaluated on the loaded runs. The displayed "before" for the
        // improvement readouts (summary SeedJ + live ProgressSeedJ). Set in StartAsync (and the re-optimize path)
        // before the optimize + summary steps.
        private double currentBaselineJ;

        // σ of the user's CURRENT settings, measured alongside currentBaselineJ in ComputeBaselineJAsync (it already
        // needs the mean σ to anchor the aberration-inspection fit guard). This is the summary's SeedSigmaFocus, and
        // the "before" the live readout counts from — so the σ pair shown mid-run matches the results page.
        private double currentBaselineSigma = double.NaN;

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
            IFilterWheelMediator filterWheelMediator,
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
                frameReviewBuilder: BuildProductionReviewBuilder(profileService, imageDataFactory,
                    cameraSensorType: () => cameraMediator?.GetInfo()?.SensorType),
                // Live pre-flight: probe the camera/focuser mediators for the connection check on Start.
                isCameraConnected: () => cameraMediator?.GetInfo()?.Connected == true,
                isFocuserConnected: () => focuserMediator?.GetInfo()?.Connected == true,
                // The Live sweep saves its frames under this persisted folder; seed + persist the picker from it.
                autoFocusOptions: HocusFocusPlugin.AutoFocusOptions,
                // Confirm rough focus with an OK/Cancel dialog when Start is clicked (the sweep centers on the current position).
                confirmRoughFocus: () => MyMessageBox.Show(
                    "The focus sweep is centered on the current focuser position. Is the telescope in focus?",
                    "Confirm in focus",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK,
                // Live capture readouts: the actual filter and gain the sweep will expose with.
                currentFilterName: () => ResolveSweepFilterName(profileService, filterWheelMediator),
                currentGain: () => ResolveSweepGain(profileService, filterWheelMediator, cameraMediator),
                // Per-filter star detection: resolve the store/binder statics lazily inside the delegates (they
                // are created by the plugin bootstrap after the options singletons).
                perFilterEnabled: () => HocusFocusPlugin.PerFilterStarDetection?.Enabled == true,
                isFilterWheelConnected: () => filterWheelMediator?.GetInfo()?.Connected == true,
                getFilterNames: () => profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Select(f => f.Name).ToList(),
                getCurrentFilterName: () => filterWheelMediator?.GetInfo()?.SelectedFilter?.Name,
                resolveFilterByName: name => profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.FirstOrDefault(f => f.Name == name),
                getFilterDetectionOptions: name => HocusFocusPlugin.PerFilterStarDetection?.GetOrSeedSnapshot(name),
                applyOptimizedToFilter: (name, dto) => {
                    // Point the options-page edit buffer at the target filter, then apply through it: the binder
                    // mirrors the result into the store and the page shows the filter that was just optimized.
                    // A blank name would leave the binder unbound and the buffer edit would never reach the store,
                    // silently discarding the optimization — so refuse loudly instead.
                    if (string.IsNullOrEmpty(name)) {
                        Logger.Error("Cannot apply optimized star-detection settings: no target filter was selected.");
                        return;
                    }
                    HocusFocusPlugin.PerFilterStarDetectionEditBinder.EditedFilterName = name;
                    HocusFocusPlugin.StarDetectionOptions.ApplyOptimizedSettings(dto);
                },
                // The donut master is a per-filter setting: write it into the TARGET filter's set, through the binder
                // so the store-vs-buffer choice (and the clone-return trap) is handled in one place.
                setFilterDonutDetection: (name, value) => {
                    var binder = HocusFocusPlugin.PerFilterStarDetectionEditBinder;
                    if (binder == null) {
                        Logger.Error("Cannot set the defocus-aware donut master: the per-filter edit binder is unavailable.");
                        return;
                    }
                    binder.MutateFilterSettings(name, o => o.DefocusAwareDonutDetection = value);
                },
                getMeasuredInFocusHfr: () => HocusFocusPlugin.InFocusHfr?.HfrPixels ?? double.NaN,
                recordMeasuredInFocusHfr: hfr => HocusFocusPlugin.InFocusHfr?.Record(hfr, DateTime.UtcNow, "optimization wizard live sweep"),
                // The summary already gave the reason and the button named the action, so this only has to cover what
                // the click costs and what it commits to. Confirming at all is worth it because the search re-runs.
                confirmReoptimizeAtBinning: (from, to) => MyMessageBox.Show(
                    DescribeReoptimizeAtBinning(from, to),
                    "Detection Binning",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxResult.Yes) == System.Windows.MessageBoxResult.Yes,
                // Unlike the binning re-run, this one takes new exposures and moves the focuser, so the dialog has to
                // say so — the summary's copy and the button name the action, not its cost.
                confirmCaptureNewSweep: (from, to) => MyMessageBox.Show(
                    DescribeCaptureNewSweep(from, to),
                    "Capture New Sweep",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxResult.Yes) == System.Windows.MessageBoxResult.Yes) {
        }

        /// <summary>
        /// The "optimize again at a different detection binning" confirmation body.
        ///
        /// <para>Every line break is deliberate TYPESETTING, not paragraphing, and this is a named method rather
        /// than an inline string so the line widths can be guarded by a test. It goes to NINA's
        /// <c>MyMessageBox</c>, whose TextBlock has no <c>TextWrapping</c> and no <c>MaxWidth</c> inside a window
        /// that sizes to content: the window ends up exactly as wide as the longest line, and its two buttons
        /// split that width between them. As one flowing paragraph this rendered a 1280px-wide modal with 610px
        /// buttons. Keep every line under <c>MaxDialogLineLength</c>.</para>
        /// </summary>
        internal static string DescribeReoptimizeAtBinning(int from, int to) =>
            $"Detection binning {from}x{from} → {to}x{to}.\n"
            + "\n"
            + $"This re-runs the search at {to}x{to} on the frames already\n"
            + "captured (no new exposures, no focuser movement).\n"
            + "Nothing is saved until you Accept the new result.\n"
            + "\n"
            + "Optimize again now?";

        /// <summary>
        /// The "capture a new sweep at a longer exposure" confirmation body.
        ///
        /// <para>The DELIBERATE INVERSE of <see cref="DescribeReoptimizeAtBinning"/>'s reassurance. That action
        /// re-reads frames already on disk, so its body promises "no new exposures, no focuser movement". This one
        /// does the opposite on every count — it exposes again, it drives the focuser through a full sweep, and at
        /// the longer exposure it costs proportionally more sky time — so the body has to say all three. The
        /// summary's copy said WHY and the button said WHAT; this is the only place the COST appears.</para>
        ///
        /// <para>The cost is stated as a RATIO rather than a wall-clock estimate on purpose: the absolute figure
        /// needs the sweep's point count and frames-per-point, which are profile state this pure function does not
        /// (and should not) reach for, and the confirmation panel's own
        /// <see cref="SweepEstimatedDurationText"/> already carries the absolute number for the sweep the user
        /// configured. "About Nx as long as the last one" is derivable from the two exposures alone and is the
        /// comparison the user is actually making at this moment.</para>
        ///
        /// <para>Line breaks are TYPESETTING, not paragraphing — see <see cref="DescribeReoptimizeAtBinning"/> for
        /// why (MyMessageBox does not wrap, so the longest line sets the dialog's width). Keep every line under
        /// <see cref="MaxDialogLineLength"/>; the widths are guarded by a test that sweeps the interpolated
        /// magnitudes.</para>
        /// </summary>
        internal static string DescribeCaptureNewSweep(double from, double to) {
            var text =
                $"Sweep exposure {from:0.##} s → {to:0.##} s.\n"
                + "\n"
                + $"This captures a NEW sweep at {to:0.##} s per frame: fresh\n"
                + "exposures, and the focuser moves.\n";
            // A non-positive "from" cannot produce a ratio (the sweep exposure is validated positive in the UI, but
            // this is a pure function and an "∞x as long" line would be worse than no line at all).
            if (from > 0.0) {
                text += $"Expect it to take about {to / from:0.#}x as long as the last one.\n";
            }
            return text
                + "Nothing is saved until you Accept the new result.\n"
                + "\n"
                + "Capture and optimize now?";
        }

        /// <summary>The line-width budget for <see cref="MyMessageBox"/> bodies, in characters. At NINA's dialog
        /// face (~8.2 px/char) 60 characters puts the window near 525px and each button near 245px — an ordinary
        /// confirmation dialog. It is also mid-band of readable measure (45-75 characters). Past ~100 the buttons
        /// clear 400px and the dialog reads as broken.</summary>
        internal const int MaxDialogLineLength = 60;

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
            Func<bool> isFocuserConnected = null,
            IAutoFocusOptions autoFocusOptions = null,
            Func<bool> confirmRoughFocus = null,
            Func<int, int, bool> confirmReoptimizeAtBinning = null,
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
            Action<string, bool> setFilterDonutDetection = null,
            Func<double> getMeasuredInFocusHfr = null,
            Action<double> recordMeasuredInFocusHfr = null) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.getMeasuredInFocusHfr = getMeasuredInFocusHfr;
            this.recordMeasuredInFocusHfr = recordMeasuredInFocusHfr;
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
            this.autoFocusOptions = autoFocusOptions;
            // Live-sweep collaborators (delegates so the VM stays mediator-free/testable). Default confirm to true so
            // tests and headless callers proceed without a dialog.
            this.confirmRoughFocus = confirmRoughFocus ?? (() => true);
            this.confirmReoptimizeAtBinning = confirmReoptimizeAtBinning ?? ((from, to) => true);
            this.confirmCaptureNewSweep = confirmCaptureNewSweep ?? ((from, to) => true);
            this.currentFilterName = currentFilterName;
            this.currentGain = currentGain;
            this.perFilterEnabled = perFilterEnabled ?? (() => false);
            this.isFilterWheelConnected = isFilterWheelConnected ?? (() => true);
            this.getFilterNames = getFilterNames ?? (() => Array.Empty<string>());
            this.getCurrentFilterName = getCurrentFilterName;
            this.resolveFilterByName = resolveFilterByName;
            this.getFilterDetectionOptions = getFilterDetectionOptions;
            this.applyOptimizedToFilter = applyOptimizedToFilter;
            this.setFilterDonutDetection = setFilterDonutDetection;
            if (this.perFilterEnabled()) {
                // Default the target to the currently-loaded wheel filter, else the first profile filter.
                var current = this.getCurrentFilterName?.Invoke();
                targetFilterName = !string.IsNullOrEmpty(current) ? current : this.getFilterNames().FirstOrDefault();
            }

            sourcePaths = new ObservableCollection<string> { null };
            applyRecommendedStepSize = true;
            // Seed the Live sweep's editable exposure from the profile's current AF exposure, and its save folder from
            // the persisted AF SavePath (both are just starting points the user can change on the confirmation screen).
            liveExposureSeconds = profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime;
            saveFolderPath = autoFocusOptions?.SavePath;

            BrowseSourceCommand = new RelayCommand<object>(BrowseSource);
            BrowseSaveFolderCommand = new RelayCommand(BrowseSaveFolder);
            StartCommand = new AsyncRelayCommand(() => StartAsync(CancellationToken.None), CanStart);
            CancelCommand = new RelayCommand(Cancel);
            AcceptCommand = new RelayCommand(Accept, CanAccept);
            OptimizeAgainAtRecommendedBinningCommand = new AsyncRelayCommand(() => OptimizeAgainAtRecommendedBinningAsync(CancellationToken.None), () => CanOptimizeAgainAtRecommendedBinning && !IsBusy);
            CaptureNewSweepCommand = new AsyncRelayCommand(() => CaptureNewSweepAsync(CancellationToken.None), () => CanCaptureNewSweep && !IsBusy);
            BackCommand = new RelayCommand(Back, () => CurrentStep == WizardStep.Summary && !IsBusy);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
            ReviewCommand = new AsyncRelayCommand(EnterReviewAsync, CanEnterReview);
            BackToSummaryCommand = new RelayCommand(BackToSummary, () => CurrentStep == WizardStep.Review && !IsBusy);
            ReOptimizeCommand = new AsyncRelayCommand(() => ReOptimizeWithLabelsAsync(CancellationToken.None), () => HasLabels && !IsBusy);
            ContinueOptimizationCommand = new AsyncRelayCommand(() => ContinueOptimizationAsync(CancellationToken.None), () => CanContinueOptimization);

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
                    ContinueOptimizationCommand.NotifyCanExecuteChanged();
                    RaisePropertyChanged(nameof(CanContinueOptimization));
                    RaisePropertyChanged(nameof(RoundsSummaryText));
                    RaisePropertyChanged(nameof(HasRoundsSummary));
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
                    // Both recommendation blocks branch on the mode — the binning block's button visibility and
                    // body, and the Star signal block's capture row and body. The radio buttons live on the Select
                    // Source step, so today the mode cannot change while a Summary is on screen and these are
                    // re-raised on the way back anyway; that is an ordering accident, and an un-notified dependency
                    // on a mutable field is the bug class the variant-switch notification test exists to catch.
                    RaiseDetectionBinningBlockChanged();
                    RaiseExposureBlockChanged();
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

        private int focusRecoverySteps = 1;

        /// <summary>Session-only "Focus recovery = N" knob (default 1, VM-only, resets each launch like
        /// <see cref="StartFromCurrentSettings"/> since the VM is constructed fresh per wizard launch). Only meaningful
        /// in Live mode: it widens the captured focuser sweep by N extra steps PER SIDE beyond the profile's
        /// <see cref="SweepOffsetSteps"/> (see <see cref="SweepEffectiveOffsetSteps"/> / <see cref="ApplyFocusRecovery"/>),
        /// and is snapshotted at Start onto every loaded run's <see cref="RunEvaluationData.RecoveryStepsPerSide"/> so the
        /// evaluator TAGS those outer frames as recovery (down-weighted in the fit, exempt from star-count gates). Inert
        /// for Replay and production autofocus: <see cref="SweepEffectiveOffsetSteps"/> adds 0 when not Live, the snapshot
        /// is 0 unless the run is Live, and <see cref="ApplyFocusRecovery"/> is a no-op at N &lt;= 0.</summary>
        public int FocusRecoverySteps {
            get => focusRecoverySteps;
            set {
                var clamped = Math.Max(0, value);
                if (focusRecoverySteps != clamped) {
                    focusRecoverySteps = clamped;
                    RaisePropertyChanged();
                    RaiseSweepReadoutsChanged();
                }
            }
        }

        /// <summary>Pass-through to the profile-saved <see cref="IStarDetectionOptions.DefocusAwareDonutDetection"/>
        /// master toggle, surfaced on the wizard start page. Default OFF. UNLIKE <see cref="StartFromCurrentSettings"/>
        /// (VM-only, resets each launch) this is a true profile option: the setter persists IMMEDIATELY on click
        /// (NINA auto-saves the active profile), because it changes what the wizard's seed/baseline detection does
        /// for this very run (it gates the early morph-close and unlocks the defocus axes for the optimizer). The
        /// optimizer's tuned numeric values still persist only on Accept (ApplyOptimizedSettings).
        ///
        /// <para>While per-filter star detection is on this reads and writes the TARGET filter's settings set, like
        /// every other input to the run — NOT the options-page edit buffer, which belongs to whichever filter is
        /// selected over there and is routinely a different one.</para></summary>
        public bool DefocusAwareDonutDetection {
            get => EffectiveDetectionOptions.DefocusAwareDonutDetection;
            set {
                if (EffectiveDetectionOptions.DefocusAwareDonutDetection == value) {
                    return;
                }
                if (setFilterDonutDetection != null && UsesTargetFilterSettings) {
                    setFilterDonutDetection(TargetFilterName, value);
                } else {
                    starDetectionOptions.DefocusAwareDonutDetection = value;
                }
                RaisePropertyChanged();
            }
        }

        private bool optimizeForAberrationInspection;

        /// <summary>When true (default OFF), the optimizer uses the aberration-inspection objective
        /// (<see cref="ObjectiveConstants.ForAberrationInspection"/>): reweighted toward star count so it recovers
        /// many more stars across the frame (what a tilt/curvature model needs), with the AF-curve fit bounded by
        /// <see cref="OptimizationObjective.SFitGuard"/> relative to the current settings' σ so focus stays usable.
        /// VM-only (resets each launch, like <see cref="StartFromCurrentSettings"/>): it only shapes the optimizer
        /// search, not runtime detection, so nothing is persisted until Accept. Only meaningful in Optimize mode.</summary>
        public bool OptimizeForAberrationInspection {
            get => optimizeForAberrationInspection;
            set {
                if (optimizeForAberrationInspection != value) {
                    optimizeForAberrationInspection = value;
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
                    RaisePropertyChanged(nameof(IsLive));
                    // Refresh the Live readouts against current equipment/profile state when the panel is shown.
                    RaiseSweepReadoutsChanged();
                    // Live gates Start on a save folder; Saved gates on the per-run folders.
                    StartCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>True for the "Saved Auto-Focus" (replay) source. The Number-of-runs input and the per-run
        /// folder pickers are only meaningful here, so the wizard binds their visibility to this.</summary>
        public bool IsReplay => SourceMode == SourceMode.Replay;

        /// <summary>True for the "Live Auto-Focus" source. The Live sweep does a fixed, non-convergent focuser sweep
        /// from the current rough-focus position (see <see cref="RunLiveAttemptAsync"/>), so the confirmation panel
        /// (rough-focus acknowledgement, editable exposure, save folder, and the read-only capture summary) binds its
        /// visibility to this.</summary>
        public bool IsLive => SourceMode == SourceMode.Live;

        private string targetFilterName;

        /// <summary>The ONE filter this run captures on, seeds its baseline from, and (on Accept) writes its
        /// result to. Only meaningful while per-filter star detection is enabled. Session-only, not persisted.</summary>
        public string TargetFilterName {
            get => targetFilterName;
            set {
                if (targetFilterName != value) {
                    targetFilterName = value;
                    RaisePropertyChanged();
                    // The sweep readouts (filter/gain) reflect the target filter while per-filter is on.
                    RaiseSweepReadoutsChanged();
                    // So does the donut master: it is read from the target filter's set, so re-targeting changes it.
                    RaisePropertyChanged(nameof(DefocusAwareDonutDetection));
                    RaisePropertyChanged(nameof(SummaryFilterName));
                    RaisePropertyChanged(nameof(HasSummaryFilter));
                }
            }
        }

        /// <summary>True while per-filter star detection is enabled: shows the target-filter picker (both
        /// modes) and routes baseline/capture/Accept through the target filter's settings set.</summary>
        public bool IsPerFilterEnabled => perFilterEnabled();

        /// <summary>The active profile's filter names, for the target-filter picker.</summary>
        public IReadOnlyList<string> AvailableFilterNames => getFilterNames();

        #region Live sweep (fixed-sweep capture)

        private double liveExposureSeconds;

        /// <summary>The exposure the Live sweep uses, seeded from the profile's AF exposure but editable here so the
        /// user can lengthen it (e.g. narrowband) to keep stars visible at the defocused wings of the sweep. Applied
        /// per-run via <see cref="AutoFocusEngineOptions.OverrideAutoFocusExposureTime"/>; on Accept it can also be
        /// written back to the profile with the recommended step size (see <see cref="ApplyRecommendedStepSize"/>).</summary>
        public double LiveExposureSeconds {
            get => liveExposureSeconds;
            set {
                if (liveExposureSeconds != value) {
                    liveExposureSeconds = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(SweepEstimatedDurationText));
                }
            }
        }

        private string saveFolderPath;

        /// <summary>Where the Live sweep writes its captured frames (seeded from the persisted AF SavePath). Required
        /// before Start is enabled in Live mode; the Browse button both sets this and persists it back to the AF
        /// options so the choice sticks.</summary>
        public string SaveFolderPath {
            get => saveFolderPath;
            set {
                if (saveFolderPath != value) {
                    saveFolderPath = value;
                    RaisePropertyChanged();
                    StartCommand.NotifyCanExecuteChanged();
                    // The summary's capture action saves to the same folder, so it is gated on this too. Unguarded,
                    // like the StartCommand line above: the constructor seeds the FIELD, so this setter is only
                    // ever reached after both commands exist.
                    RaisePropertyChanged(nameof(CanCaptureNewSweep));
                    CaptureNewSweepCommand.NotifyCanExecuteChanged();
                }
            }
        }

        // Read-only capture summary shown on the Live confirmation panel. All derive from the profile / AF filter and
        // do not change during the wizard, so they need no change notifications beyond the initial bind.
        public int SweepStepSize => profileService.ActiveProfile.FocuserSettings.AutoFocusStepSize;
        public int SweepOffsetSteps => profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps;

        /// <summary>The offset-steps-per-side the Live sweep will actually capture: the profile's
        /// <see cref="SweepOffsetSteps"/> plus the session-only <see cref="FocusRecoverySteps"/> recovery steps. Adds 0
        /// unless the source is Live, so the Replay readouts (and any production path) are byte-identical to before.</summary>
        public int SweepEffectiveOffsetSteps => SweepOffsetSteps + (IsLive ? Math.Max(0, FocusRecoverySteps) : 0);

        public int SweepPointCount => 2 * SweepEffectiveOffsetSteps + 1;
        public int SweepFramesPerPoint => profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint;
        /// <summary>The CAMERA binning the sweep will capture at, from NINA's Auto Focus Binning. Named "capture"
        /// to keep it distinct from <see cref="SweepDetectionBinning"/>, which resamples the captured frame for
        /// detection only. The two multiply.</summary>
        public string SweepCaptureBinning {
            get {
                var b = profileService.ActiveProfile.FocuserSettings.AutoFocusBinning;
                return $"{b}x{b}";
            }
        }

        /// <summary>
        /// The Hocus Focus software binning detection will run at, applied ON TOP of
        /// <see cref="SweepCaptureBinning"/>. Settable here because the whole search is tuned at whatever factor
        /// is in effect when the run starts, so this is the last honest moment to choose it.
        ///
        /// <para>This is the PERSISTED option, edited in place — the same write the Star Detector options page
        /// does, conflict prompt included. A session-only override would let Accept apply settings tuned at a
        /// factor the profile does not have. Binning is global even in per-filter mode: it follows the optics,
        /// not the filter.</para>
        /// </summary>
        public DetectionBinningEnum SweepDetectionBinning {
            get => starDetectionOptions.DetectionBinning;
            set {
                if (starDetectionOptions.DetectionBinning != value) {
                    starDetectionOptions.DetectionBinning = value;
                    RaiseSweepDetectionBinningChanged();
                }
            }
        }

        /// <summary>The same short recommendation the Star Detector options page shows, from the same function and
        /// the same measurement, so the two surfaces can never disagree.</summary>
        public string SweepDetectionBinningRecommendation => DetectionBinningResolver.DescribeRecommendation(
            DetectionBinningResolver.ToFactor(starDetectionOptions.DetectionBinning), MeasuredInFocusHfrPixels);

        /// <summary>The reasoning behind it, shown as the recommendation's tooltip.</summary>
        public string SweepDetectionBinningRecommendationDetail => DetectionBinningResolver.DescribeRecommendationDetail(
            DetectionBinningResolver.ToFactor(starDetectionOptions.DetectionBinning), MeasuredInFocusHfrPixels, null);

        /// <summary>Whether to show the recommendation beside the dropdown at all — only when it asks for
        /// something. Hidden once the setting matches the measurement.</summary>
        public bool SweepDetectionBinningRecommendationVisible => DetectionBinningResolver.ShouldShowRecommendation(
            DetectionBinningResolver.ToFactor(starDetectionOptions.DetectionBinning), MeasuredInFocusHfrPixels);

        /// <summary>The last measured in-focus HFR, in captured pixels. NaN before anything has been measured.</summary>
        private double MeasuredInFocusHfrPixels => getMeasuredInFocusHfr?.Invoke() ?? double.NaN;

        private void RaiseSweepDetectionBinningChanged() {
            RaisePropertyChanged(nameof(SweepDetectionBinning));
            RaisePropertyChanged(nameof(SweepDetectionBinningRecommendation));
            RaisePropertyChanged(nameof(SweepDetectionBinningRecommendationDetail));
            RaisePropertyChanged(nameof(SweepDetectionBinningRecommendationVisible));
        }

        /// <summary>The gain the sweep will actually expose with: the AF filter's gain when filter-wheel offsets
        /// designate one with an explicit gain, otherwise the connected camera's current gain.</summary>
        public string SweepGain {
            get {
                if (IsPerFilterEnabled && !string.IsNullOrEmpty(TargetFilterName)) {
                    var filter = resolveFilterByName?.Invoke(TargetFilterName);
                    if (filter != null && filter.AutoFocusGain > -1) {
                        return filter.AutoFocusGain.ToString();
                    }
                }
                var gain = currentGain?.Invoke();
                return gain.HasValue ? gain.Value.ToString() : "Unavailable";
            }
        }

        /// <summary>The filter the sweep will actually expose through: the designated AF filter when offsets are on,
        /// otherwise the currently loaded filter.</summary>
        public string SweepFilterName {
            get {
                if (IsPerFilterEnabled && !string.IsNullOrEmpty(TargetFilterName)) {
                    // Don't claim a filter the sweep can't actually select: Start refuses an unresolvable target,
                    // so the readout says so up front rather than implying a capture that will be blocked.
                    return resolveFilterByName != null && resolveFilterByName(TargetFilterName) == null
                        ? $"{TargetFilterName} (not in profile)"
                        : TargetFilterName;
                }
                var name = currentFilterName?.Invoke();
                return string.IsNullOrEmpty(name) ? "Unavailable" : name;
            }
        }

        /// <summary>The filter these summary results are for, shown on the summary page. Null when per-filter
        /// detection is off (the run tuned the single global set, so there is no filter to name).</summary>
        public string SummaryFilterName => IsPerFilterEnabled ? TargetFilterName : null;

        /// <summary>Whether to show the summary-page filter readout at all.</summary>
        public bool HasSummaryFilter => IsPerFilterEnabled && !string.IsNullOrEmpty(TargetFilterName);

        public int SweepEstimatedFrames => SweepPointCount * Math.Max(1, SweepFramesPerPoint);

        /// <summary>A rough capture-time estimate (exposure only; excludes download and focuser-move overhead), so the
        /// user knows how long the sweep runs. Recomputed when the exposure changes.</summary>
        public string SweepEstimatedDurationText {
            get {
                var seconds = SweepEstimatedFrames * Math.Max(0.0, LiveExposureSeconds);
                var ts = TimeSpan.FromSeconds(seconds);
                return ts.TotalMinutes >= 1
                    ? $"~{(int)ts.TotalMinutes}m {ts.Seconds}s (exposure only)"
                    : $"~{ts.TotalSeconds:0}s (exposure only)";
            }
        }

        private void RaiseSweepReadoutsChanged() {
            RaisePropertyChanged(nameof(SweepStepSize));
            RaisePropertyChanged(nameof(SweepEffectiveOffsetSteps));
            RaisePropertyChanged(nameof(SweepPointCount));
            RaisePropertyChanged(nameof(SweepCaptureBinning));
            RaiseSweepDetectionBinningChanged();
            RaisePropertyChanged(nameof(SweepFilterName));
            RaisePropertyChanged(nameof(SweepGain));
            RaisePropertyChanged(nameof(SweepEstimatedFrames));
            RaisePropertyChanged(nameof(SweepEstimatedDurationText));
        }

        private string captureContextText;

        /// <summary>Which frame the sweep is on and where the focuser is, shown live during capture (e.g.
        /// "Capturing frame 3 of 9 at focuser position 12345"). Fed by the engine's per-point progress reports.</summary>
        public string CaptureContextText {
            get => captureContextText;
            private set { captureContextText = value; RaisePropertyChanged(); }
        }

        private string captureExposureText;

        /// <summary>The camera's own exposure status for the current frame (e.g. the exposure countdown), shown live
        /// during capture. Fed by the camera's progress reports forwarded through the sweep.</summary>
        public string CaptureExposureText {
            get => captureExposureText;
            private set { captureExposureText = value; RaisePropertyChanged(); }
        }

        #endregion Live sweep (fixed-sweep capture)

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
                    // Drive the elapsed-time clock off the busy state: it runs for the whole busy span the progress
                    // window is shown for (load → analyze → optimize → build review), so the timer and the summary's
                    // duration measure exactly the run the user sees progress for.
                    if (value) { StartRunTimer(); } else { StopRunTimer(); }
                    RaisePropertyChanged();
                    RaiseStepVisibilityChanged();
                    StartCommand.NotifyCanExecuteChanged();
                    AcceptCommand.NotifyCanExecuteChanged();
                    BackCommand.NotifyCanExecuteChanged();
                    ReviewCommand.NotifyCanExecuteChanged();
                    BackToSummaryCommand.NotifyCanExecuteChanged();
                    ReOptimizeCommand.NotifyCanExecuteChanged();
                    ContinueOptimizationCommand.NotifyCanExecuteChanged();
                    // Every command whose CanExecute names IsBusy must be listed here. This one was missed, and the
                    // shape of the resulting bug is worth remembering: the run snapshots its folders (and raises the
                    // binning block) INSIDE the try, while IsBusy is still true, so the button evaluated to disabled
                    // at the only moment it was asked - and then nothing ever asked again. The body copy beside it,
                    // being a plain property, read CanOptimizeAgainAtRecommendedBinning alone and correctly said the
                    // frames were there. A disabled button under copy promising it works.
                    OptimizeAgainAtRecommendedBinningCommand.NotifyCanExecuteChanged();
                    CaptureNewSweepCommand.NotifyCanExecuteChanged();
                    RaisePropertyChanged(nameof(CanContinueOptimization));
                }
            }
        }

        // Elapsed-time clock for the run currently in progress. runStopwatch measures the busy span; elapsedTimer
        // ticks once a second to refresh ElapsedText in the progress window. lastRunDuration is the final elapsed of
        // the most recent completed run, shown on the summary.
        private readonly System.Diagnostics.Stopwatch runStopwatch = new System.Diagnostics.Stopwatch();
        private DispatcherTimer elapsedTimer;
        private TimeSpan lastRunDuration;

        /// <summary>Live "M:SS" elapsed time for the in-progress run, shown inline with the progress count. Updates
        /// each second while busy.</summary>
        public string ElapsedText => FormatDuration(runStopwatch.Elapsed);

        /// <summary>"M:SS" total time the most recent run took, shown on the summary. Empty until a run has completed.</summary>
        public string RunDurationText => lastRunDuration > TimeSpan.Zero ? FormatDuration(lastRunDuration) : string.Empty;

        /// <summary>Whether a completed-run duration is available to show on the summary.</summary>
        public bool HasRunDuration => lastRunDuration > TimeSpan.Zero;

        /// <summary>Formats a duration as minutes:seconds (e.g. "3:07"); pure — unit-tested.</summary>
        public static string FormatDuration(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

        private void StartRunTimer() {
            runStopwatch.Restart();
            RaiseElapsedChanged();
            if (elapsedTimer == null) {
                elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                elapsedTimer.Tick += (_, __) => RaiseElapsedChanged();
            }
            elapsedTimer.Start();
        }

        private void StopRunTimer() {
            elapsedTimer?.Stop();
            runStopwatch.Stop();
            RaiseElapsedChanged();
        }

        /// <summary>Refreshes the elapsed readout and the combined "X / Y (M:SS)" progress line each tick.</summary>
        private void RaiseElapsedChanged() {
            RaisePropertyChanged(nameof(ElapsedText));
            RaisePropertyChanged(nameof(ProgressCountElapsedText));
        }

        /// <summary>Captures the in-progress run's elapsed time as the displayed run duration (called on the success
        /// path, just before landing on the Summary, while the stopwatch is still running).</summary>
        private void RecordRunDuration() {
            lastRunDuration = runStopwatch.Elapsed;
            RaisePropertyChanged(nameof(RunDurationText));
            RaisePropertyChanged(nameof(HasRunDuration));
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

        private string recoveryWidenedNotice;

        /// <summary>
        /// Set when the guard widened the focus-recovery exemption to make the sweep fittable (see
        /// <c>SeedFitIsUsableAsync</c>). This is NOT an error — the run proceeds — but it changes WHICH positions
        /// the curve was fitted from, so it has to be visible rather than silently applied: the user chose the
        /// step size and the recovery count that produced those empty wings, and they are the ones who can fix it.
        /// </summary>
        public string RecoveryWidenedNotice {
            get => recoveryWidenedNotice;
            private set {
                if (recoveryWidenedNotice != value) {
                    recoveryWidenedNotice = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(HasRecoveryWidenedNotice));
                }
            }
        }

        public bool HasRecoveryWidenedNotice => !string.IsNullOrEmpty(RecoveryWidenedNotice);

        #endregion State

        #region Progress

        private int progressCurrent;

        /// <summary>Determinate progress numerator: items done in the current phase (frames loaded / frames
        /// analyzed / combinations tried / frames detected for review).</summary>
        public int ProgressCurrent {
            get => progressCurrent;
            private set { progressCurrent = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressCountText)); RaisePropertyChanged(nameof(ProgressCountElapsedText)); }
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
                RaisePropertyChanged(nameof(ProgressCountElapsedText));
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

        /// <summary>The progress line under the bar: "X / Y (M:SS)" when there is a determinate count, else just the
        /// elapsed clock "(M:SS)" (so the timer stays visible during indeterminate phases). Re-raised each second by
        /// the elapsed timer and whenever the count changes.</summary>
        public string ProgressCountElapsedText =>
            HasProgressCount ? $"{ProgressCountText} ({ElapsedText})" : $"({ElapsedText})";

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

        private double progressSeedSigma = double.NaN;

        /// <summary>The σ the live readout counts from: the current settings' σ for a fresh Start (matching the
        /// summary's "Focus precision" baseline), or the prior round's best σ for a Continue pass.</summary>
        public double ProgressSeedSigma {
            get => progressSeedSigma;
            private set { progressSeedSigma = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressImprovementText)); }
        }

        private double progressBestSigma = double.NaN;

        /// <summary>σ of the search's current incumbent, straight off <see cref="OptimizationProgress.BestSigmaFocus"/>.</summary>
        public double ProgressBestSigma {
            get => progressBestSigma;
            private set { progressBestSigma = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ProgressImprovementText)); }
        }

        /// <summary>The J the live readout must beat before it claims anything: the stricter of this pass's own
        /// baseline (<see cref="ProgressSeedJ"/>) and the user's current settings (currentBaselineJ).
        ///
        /// <para>They coincide on a fresh Start. They do NOT on a "Continue optimizing" pass, whose baseline is the
        /// prior round's best — and the prior round is allowed to have finished BELOW the current settings, since
        /// <see cref="CanContinueOptimization"/> does not require <see cref="OptimizerImprovedOverCurrent"/>. Gating
        /// on the prior round alone would let the live line announce a win over that (losing) round while the results
        /// page, which judges <see cref="OptimizerImprovedOverCurrent"/> against the current settings, still reports
        /// "could not improve on your current settings". Taking the max keeps the live claim at least as strict as
        /// the page's verdict on every path.</para></summary>
        public double ProgressGateJ => Math.Max(progressSeedJ, currentBaselineJ);

        /// <summary>Live, plain-language readout shown during the search (no jargon "J"): the focus precision of the
        /// best result found so far, as the same σ pair the results page headlines. See
        /// <see cref="OptimizationSummary.LiveImprovementText"/> for why it reports σ rather than a percent of J.</summary>
        public string ProgressImprovementText =>
            OptimizationSummary.LiveImprovementText(ProgressGateJ, progressBestJ, progressSeedSigma, progressBestSigma);

        private string phase;

        public string Phase {
            get => phase;
            private set { phase = value; RaisePropertyChanged(); }
        }

        private bool isCapturing;

        /// <summary>True only while the live sweep is capturing frames. The sweep is a plain capture with no star
        /// detection (the optimizer detects the saved frames afterward), so there is no focus curve to chart — the
        /// progress panel instead shows the capture readouts (frame count, focuser position, exposure) gated on this.</summary>
        public bool IsCapturing {
            get => isCapturing;
            private set { isCapturing = value; RaisePropertyChanged(); }
        }

        private double exposureProgressCurrent;

        /// <summary>Elapsed seconds of the current frame's exposure, straight from the camera's progress reports.</summary>
        public double ExposureProgressCurrent {
            get => exposureProgressCurrent;
            private set { exposureProgressCurrent = value; RaisePropertyChanged(); }
        }

        private int exposureProgressMax;

        /// <summary>Total seconds of the current frame's exposure. Zero between exposures, which hides the bar.</summary>
        public int ExposureProgressMax {
            get => exposureProgressMax;
            private set { exposureProgressMax = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(HasExposureProgress)); }
        }

        /// <summary>Whether an exposure is in flight (so the exposure bar is meaningful).</summary>
        public bool HasExposureProgress => exposureProgressMax > 0;

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

        // "Continue optimizing" chain for the Optimized variant: the params at each stage [baseline, round1Best,
        // round2Best, …] and the per-round BestJ. Round 1 is the initial StartAsync optimize; each Continue appends a
        // stage by re-seeding from the prior best. The latest stage IS the Optimized variant (chart + Accept follow
        // it); the chain only drives the multi-stage trajectory shown in the Changed-parameters table. Capped at
        // MaxOptimizationRounds total passes.
        private const int MaxOptimizationRounds = 3;
        private readonly List<StarDetectorParams> optimizedChain = new List<StarDetectorParams>();
        private readonly List<double> optimizedRoundJ = new List<double>();
        // The Optimized variant's curve per round [round1, round2, …] (parallel to the chain's non-baseline stages),
        // retained so the per-frame "Stars per frame" trajectory can show counts across each pass. The latest one is
        // also held in optimizedCurve for the chart; this list is the only place earlier rounds' curves survive.
        private readonly List<OptimizationCurve> optimizedRoundCurves = new List<OptimizationCurve>();

        /// <summary>Number of optimization passes that have produced the current Optimized variant (1 after the
        /// initial optimize; up to <see cref="MaxOptimizationRounds"/> after Continue). 0 when no optimization ran.</summary>
        public int RoundsCompleted => Math.Max(0, optimizedChain.Count - 1);

        /// <summary>True while another "Continue optimizing" pass is allowed (an Optimized variant exists, the cap
        /// hasn't been reached, and nothing is running). Drives the Continue button's enabled state.</summary>
        public bool CanContinueOptimization => !IsBusy && IsSummary && HasOptimized && RoundsCompleted < MaxOptimizationRounds;

        /// <summary>Header shown above the Changed-parameters table once more than one optimization pass has run:
        /// e.g. "3 rounds  •  J: 0.940 → 0.985 → 0.990". Empty for a single round (no chain to summarize).</summary>
        public string RoundsSummaryText {
            get {
                if (selectedVariant != OptimizationVariant.Optimized || optimizedRoundJ.Count <= 1) {
                    return string.Empty;
                }
                var js = string.Join(" → ", optimizedRoundJ.Select(j => j.ToString("F3")));
                return $"{optimizedRoundJ.Count} rounds  •  J: {currentBaselineJ:F3} → {js}";
            }
        }

        public bool HasRoundsSummary => RoundsSummaryText.Length > 0;

        /// <summary>Focus-precision (σ) readout for the summary row. For the Optimized variant after more than one
        /// pass it shows the full per-round σ path "baseline → R1 → R2 → …" (mirroring <see cref="RoundsSummaryText"/>'s
        /// J path and <see cref="StarsPerFrame"/>'s counts, all read off the same retained curves); otherwise it falls
        /// back to the start→finish <see cref="OptimizationSummary.SigmaText"/> (which carries the "(unchanged)" wording
        /// for a single pass). Stage 0 is the current-settings σ (the same baseline RoundsSummaryText uses for J).</summary>
        public string FocusPrecisionText {
            get {
                if (selectedVariant == OptimizationVariant.Optimized && optimizedRoundCurves.Count > 1 && currentCurve != null) {
                    var sigmas = new List<double>(optimizedRoundCurves.Count + 1) { currentCurve.MinimumStdError };
                    sigmas.AddRange(optimizedRoundCurves.Select(c => c.MinimumStdError));
                    return OptimizationSummary.FormatSigmaTrajectory(sigmas);
                }
                return Summary?.SigmaText ?? string.Empty;
            }
        }

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

        /// <summary>Per-frame (per focuser position) accepted-star count and how it moved across the optimization
        /// passes, shown in the main results flow (NOT the feedback panel). For the Optimized variant the stages are
        /// [current, round1, round2, …] (the full Continue path); for the Current variant it is the single current
        /// count; empty for the Feedback variant (its own <see cref="StarCountChanges"/> panel covers that view).</summary>
        public IReadOnlyList<FrameStarCountTrajectory> StarsPerFrame {
            get {
                if (selectedVariant == OptimizationVariant.Optimized && currentCurve != null && optimizedRoundCurves.Count > 0) {
                    var stages = new List<OptimizationCurve> { currentCurve }; // stage 0 = current settings
                    stages.AddRange(optimizedRoundCurves);                      // then each optimization round
                    return BuildStarCountTrajectories(stages);
                }
                if (selectedVariant == OptimizationVariant.Current && currentCurve != null) {
                    return BuildStarCountTrajectories(new[] { currentCurve });  // counts only (single stage)
                }
                return Array.Empty<FrameStarCountTrajectory>();
            }
        }

        public bool HasStarsPerFrame => StarsPerFrame.Count > 0;

        /// <summary>Heading for the Stars-per-frame section: the Optimized variant shows a current→optimized change,
        /// the Current variant just the per-frame counts.</summary>
        public string StarsPerFrameLabel =>
            selectedVariant == OptimizationVariant.Optimized ? "Stars per frame (current → optimized)" : "Stars per frame";

        /// <summary>Builds per-position star-count trajectories across an ordered list of stage curves (each detects
        /// the SAME frames, so positions line up). Each row carries that position's summed accepted-star count at
        /// every stage (missing → 0). Curves that lack per-frame arrays contribute an empty stage.</summary>
        private static IReadOnlyList<FrameStarCountTrajectory> BuildStarCountTrajectories(IReadOnlyList<OptimizationCurve> stageCurves) {
            var byStage = stageCurves
                .Select(c => (c?.FrameStarCounts != null && c.FrameFocuserPositions != null)
                    ? SumStarCountsByPosition(c.FrameFocuserPositions, c.FrameStarCounts)
                    : new Dictionary<int, int>())
                .ToList();
            var positions = byStage.SelectMany(d => d.Keys).Distinct().OrderBy(p => p).ToList();
            var rows = new List<FrameStarCountTrajectory>(positions.Count);
            foreach (var pos in positions) {
                var stages = byStage.Select(d => d.TryGetValue(pos, out var v) ? v : 0).ToList();
                rows.Add(new FrameStarCountTrajectory { FocuserPosition = pos, Stages = stages });
            }
            return rows;
        }

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
            RaiseDetectionBinningBlockChanged();
            RaisePropertyChanged(nameof(CanApplyExposureTime));
            RaisePropertyChanged(nameof(SweepExposureChangeText));
            RaisePropertyChanged(nameof(ChangedParametersDisplay));
            RaisePropertyChanged(nameof(StarCountChanges));
            RaisePropertyChanged(nameof(HasStarCountChanges));
            RaisePropertyChanged(nameof(StarCountChangeBaselineLabel));
            RaisePropertyChanged(nameof(StarsPerFrame));
            RaisePropertyChanged(nameof(HasStarsPerFrame));
            RaisePropertyChanged(nameof(StarsPerFrameLabel));
            RaisePropertyChanged(nameof(ShowReoptimizePrompt));
            RaisePropertyChanged(nameof(ShowFeedbackPanel));
            RaisePropertyChanged(nameof(CanContinueOptimization));
            RaisePropertyChanged(nameof(RoundsCompleted));
            RaisePropertyChanged(nameof(RoundsSummaryText));
            RaisePropertyChanged(nameof(HasRoundsSummary));
            RaisePropertyChanged(nameof(FocusPrecisionText));
            // The Star signal block reads the SELECTED summary's own Sensitivity gate and advice, so it has to be
            // re-raised on every variant switch exactly like the detection-binning block above — a user toggling to
            // Current must see the gate THOSE settings use, not the one the previous variant reported.
            // (This call replaced a second, redundant RaiseDetectionBinningBlockChanged() that sat here.)
            RaiseExposureBlockChanged();
            // The capture box is re-seeded HERE, with the block it belongs to, and not merely once per run: the
            // recommended-exposure ROW follows the selected variant, so a box that did not would disagree with the
            // row printed directly above it. That is reachable and expensive — when the optimizer cannot beat the
            // current settings the page OPENS on Current (whose healthy gate hides the block, leaving the box on
            // its fallback), and the user then clicks Optimized to find the row asking for 11 s beside a box still
            // reading 5 s. Clicking would spend a whole sweep at the exposure that just starved.
            // The trade: a manual edit made BEFORE a variant switch is discarded. That is the correct side to err
            // on — the recommendation the edit was relative to has itself changed.
            SeedRecaptureExposure();
            AcceptCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
            ContinueOptimizationCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Resets all retained variants (called at the top of a fresh Start).</summary>
        private void ResetVariants() {
            currentResult = optimizedResult = feedbackResult = null;
            currentSummary = optimizedSummary = feedbackSummary = null;
            currentCurve = optimizedCurve = feedbackCurve = null;
            optimizedChain.Clear();
            optimizedRoundJ.Clear();
            optimizedRoundCurves.Clear();
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
                    // When Current is selected, this toggle is the only thing that makes Accept meaningful.
                    AcceptCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>Whether the "apply recommended AF step size" toggle is meaningful — true only when the
        /// recommended step size and/or offset steps actually differ from the current profile values. The
        /// checkbox binds its IsEnabled here so an unchanged recommendation can't be "applied".</summary>
        public bool CanApplyRecommendedStepSize => (Summary?.StepSizeOrOffsetChanged ?? false) || ExposureChangedForLiveRun;

        /// <summary>Whether the summary shows the detection-binning block at all. Hidden when the baseline fit
        /// was degenerate: there is no measured HFR to reason from, and the bad curve is already on the chart.</summary>
        public bool HasDetectionBinningBlock => SelectedSummary?.HasDetectionBinningMeasurement ?? false;

        /// <summary>The recommended-factor row's value.</summary>
        public string DetectionBinningRecommendationText => SelectedSummary?.DetectionBinningText ?? string.Empty;

        /// <summary>
        /// Whether the "Optimize again at NxN" button is SHOWN: the measured curve calls for a different factor and
        /// this is an Optimize-mode run (a use-current run has nothing tuned to re-tune).
        ///
        /// <para>Deliberately the same condition the body copy uses to promise the button. They were separate once,
        /// and the copy then described an action whose control was hidden — the worst of both. Whether the action
        /// can actually RUN is <see cref="CanOptimizeAgainAtRecommendedBinning"/>, which the command's CanExecute
        /// uses, so an unavailable action shows as a disabled button rather than as nothing at all.</para>
        /// </summary>
        public bool ShowOptimizeAgainAtRecommendedBinning =>
            (SelectedSummary?.DetectionBinningDiffers ?? false) && !IsUseCurrentMode;

        /// <summary>Whether the re-run can actually proceed: it reloads the run's frames from disk, so it needs the
        /// folders that were loaded. Drives the button's enabled state.</summary>
        public bool CanOptimizeAgainAtRecommendedBinning =>
            ShowOptimizeAgainAtRecommendedBinning
            && reoptimizeRunFolders != null && reoptimizeRunFolders.Count > 0;

        /// <summary>Button label, e.g. "Optimize again at 2x2" — the consequence lives in the label.</summary>
        public string OptimizeAgainAtRecommendedBinningText {
            get {
                var r = SelectedSummary?.RecommendedDetectionBinning ?? 1;
                return $"Optimize again at {r}x{r}";
            }
        }

        /// <summary>
        /// The body paragraph under the recommended-factor row. Three shapes, matching the three things that can
        /// be true; empty when the recommendation simply confirms the run's factor (the row says it all).
        /// </summary>
        public string DetectionBinningBodyText {
            get {
                var summary = SelectedSummary;
                if (summary == null || !summary.HasDetectionBinningMeasurement) {
                    return string.Empty;
                }
                if (summary.DetectionBinningPendingApply) {
                    var f = summary.RunDetectionBinning;
                    var text = $"This result was optimized at {f}x{f}. Accept applies these settings and Detection Binning {f}x{f} together; Close discards both.";
                    if (IsPerFilterEnabled) {
                        var p = summary.PersistedDetectionBinning;
                        text += $" Detection Binning is shared by all filters; re-optimize settings tuned for other filters at {p}x{p} as well.";
                    }
                    return text;
                }
                if (!summary.DetectionBinningDiffers) {
                    return string.Empty;
                }
                var run = summary.RunDetectionBinning;
                var rec = summary.RecommendedDetectionBinning;
                if (IsUseCurrentMode) {
                    return "To change the factor, run this wizard in Optimize mode; settings must be re-tuned for a new factor.";
                }
                // The row above already gives the measurement and the factors, the tooltip carries the 2-4 px
                // background, and the button is directly below. All this has to add is why the button exists.
                // Only promise the re-run when it can actually happen: the frames are reloaded from disk, so a run
                // whose folders are gone can show the recommendation but not act on it.
                return CanOptimizeAgainAtRecommendedBinning
                    ? $"These settings were tuned at {run}x{run}, so using {rec}x{rec} means optimizing again on the frames already captured."
                    : $"These settings were tuned at {run}x{run}, and this run's frames are no longer available to re-optimize. Start a new run with Detection Binning set to {rec}x{rec}.";
            }
        }

        /// <summary>Whether the block has a consequence paragraph to show. False when the recommendation simply
        /// confirms the run's factor — the row already says everything.</summary>
        public bool HasDetectionBinningBody => !string.IsNullOrEmpty(DetectionBinningBodyText);

        /// <summary>Whether the summary shows the "Star signal" block at all — i.e. whether the selected variant's
        /// Sensitivity gate landed at (or near) its search floor. See
        /// <see cref="OptimizationSummary.HasLowStarSignal"/> for why this is the gate ALONE and not
        /// additionally conditioned on there being a number to show.</summary>
        public bool HasExposureBlock => SelectedSummary?.HasLowStarSignal ?? false;

        /// <summary>
        /// The one italic sentence rendered directly above the focus-curve chart when the gate is floored; empty
        /// otherwise.
        ///
        /// <para>It exists because Accept lives in the footer OUTSIDE the ScrollViewer and is always visible, so a
        /// user looking at a plausible-shaped curve can accept the run without ever scrolling to the Star signal
        /// block below the fold. This is the page's only colored element, and it is a warning, not an error: the
        /// result is usable, it is just built on low-confidence detections.</para>
        ///
        /// <para>It states the GATE, not the signal, and states it WITHOUT AGENCY. The block below fires on the
        /// gate alone, and one of its states (<see cref="ExposureRecommendation.ExposureIsNotTheLimit"/>) is a run
        /// whose brightest stars measured well above the default gate — so a note claiming the stars "barely
        /// cleared the noise" would be flatly false exactly there, and would then be contradicted by the block it
        /// points at. "The detector HAD TO drop its gate" is wrong for the same family of reason: the note follows
        /// the selected variant, and on the Current view the gate is the user's own hand-set value, which nothing
        /// compelled.</para>
        /// </summary>
        public string LowSignalChartNote => HasExposureBlock ? LowSignalChartNoteText : string.Empty;

        /// <summary>The chart note's wording, as a constant so a test can pin it without duplicating the string.</summary>
        internal const string LowSignalChartNoteText =
            "This run's star acceptance gate sits at the bottom of its range, so the result below is built from low-confidence detections; see Star signal.";

        /// <summary>The recommended-exposure row's value (see <see cref="StarSignalCopy.DescribeExposureRow"/>).</summary>
        public string RecommendedExposureText => StarSignalCopy.DescribeExposureRow(SelectedSummary);

        /// <summary>Whether the recommended-exposure row has a derived number to show. False when the run could not
        /// support one (too few usable frames, no per-star SNRs, or no known exposure to scale from) — the block
        /// still renders, carrying only its diagnosis, rather than showing a labelled row with nothing beside it.</summary>
        public bool HasRecommendedExposure => !string.IsNullOrEmpty(RecommendedExposureText);

        /// <summary>The body paragraph under the recommended-exposure row.</summary>
        public string ExposureBodyText => StarSignalCopy.DescribeExposureRecommendation(SelectedSummary, lastRunWasLive, IsUseCurrentMode);

        /// <summary>The recommended-exposure row's TOOLTIP: the arithmetic behind the number, and which cap (if
        /// any) trimmed it. Background belongs in a tooltip, not in the paragraph — the sibling detection-binning
        /// block sets the same rule, and the sweep page's own binning row already carries a bound detail tooltip
        /// (<c>SweepDetectionBinningRecommendationDetail</c>) as the precedent for a per-run one.</summary>
        public string ExposureDerivationDetail => StarSignalCopy.DescribeExposureDerivation(SelectedSummary);

        /// <summary>Whether the block has a body paragraph to show. Always true while the block is visible (the
        /// diagnosis sentence is unconditional), but bound anyway so the block degrades to the row alone rather
        /// than to a blank gap if that ever stops holding.</summary>
        public bool HasExposureBody => !string.IsNullOrEmpty(ExposureBodyText);

        private double recaptureExposureSeconds;

        /// <summary>
        /// The exposure the "Capture a new sweep and optimize" action will capture at. Pre-filled from the selected
        /// variant's recommendation each time a run settles (see <c>SeedRecaptureExposure</c>), then EDITABLE — the
        /// recommendation is a derived extrapolation, and the user knows things about their sky that it does not.
        ///
        /// <para>Session-only and deliberately NOT persisted: it exists for the duration of one summary. What DOES
        /// persist is <see cref="LiveExposureSeconds"/> — which this writes on capture, and which Accept can then
        /// write to the profile — so a value that survived the wizard would silently re-arm a later sweep with an
        /// exposure derived from a run the user never accepted.</para>
        /// </summary>
        public double RecaptureExposureSeconds {
            get => recaptureExposureSeconds;
            set {
                if (recaptureExposureSeconds != value) {
                    recaptureExposureSeconds = value;
                    RaisePropertyChanged();
                    // The capture refuses to run at a non-positive exposure, so the button follows this box.
                    RaisePropertyChanged(nameof(CanCaptureNewSweep));
                    CaptureNewSweepCommand.NotifyCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Whether the Star signal block's capture action row is SHOWN: this run was a Live sweep and there is a
        /// longer exposure to capture at.
        ///
        /// <para>Deliberately the same condition the body copy uses to promise the action, exactly as
        /// <see cref="ShowOptimizeAgainAtRecommendedBinning"/> is for its own button — see that property for the
        /// bug that rule exists to prevent. Branches on
        /// <see cref="ExposureRecommendation.IncreasesExposure"/>, never on
        /// <see cref="ExposureRecommendation.HasRecommendation"/>: a run whose current exposure already exceeds the
        /// absolute cap keeps <c>HasRecommendation</c> true while the recommendation collapses onto the current
        /// value, and re-capturing at the exposure you just used is a no-op that costs a sweep.</para>
        ///
        /// <para>The two MODE conditions are here rather than in <see cref="CanCaptureNewSweep"/>, matching
        /// <see cref="ShowOptimizeAgainAtRecommendedBinning"/>. REPLAY has no rig to re-capture from. "Use current
        /// settings" has nothing to re-tune — it skips the search entirely, so a fresh sweep would spend the sky
        /// time and land back on the same current settings. Both are decided on the Select Source step and are
        /// FIXED for the life of this Summary, so a button disabled by either would sit dead for the whole run
        /// with nothing on screen explaining why; the body copy names the mode to switch to instead (see
        /// <see cref="StarSignalCopy.DescribeExposureRecommendation"/>). Only conditions that can CHANGE while the
        /// user reads the summary belong in <c>Can</c>.</para>
        ///
        /// <para><b>One deliberate asymmetry with the copy, which otherwise tracks this property exactly.</b> When
        /// a detection-binning change is ALSO on offer, <c>RemedyFor</c>'s top-ranked remedy displaces the capture
        /// sentence with "change the factor first and let the next run re-measure the exposure" — while this stays
        /// true, so the capture button remains on screen with no sentence naming it. That is intentional, and it is
        /// NOT the failure mode the house rule above guards against (copy promising an action whose control is
        /// hidden — the reverse). Both buttons are visible in that state and the paragraph says which to use first,
        /// so the user keeps the choice; hiding the capture action instead would silently overrule a user who has
        /// reason to re-expose before touching the factor.</para>
        /// </summary>
        public bool ShowCaptureNewSweep =>
            HasExposureBlock
            && lastRunWasLive
            && !IsUseCurrentMode
            && (SelectedSummary?.ExposureAdvice?.IncreasesExposure ?? false);

        /// <summary>
        /// Whether the capture can actually proceed. It drives a real auto-focus sweep, so it needs an engine, a
        /// connected camera and focuser, and somewhere to save the frames — the same pre-flight
        /// <see cref="ValidateSourceBeforeStart"/> applies to a Live Start, re-checked here because any of them can
        /// change while the user reads the summary. That transience is exactly what earns them a place here rather
        /// than in <see cref="ShowCaptureNewSweep"/>: a disabled button that comes back to life when the camera
        /// reconnects is informative, whereas one that can never come back is just a dead control.
        ///
        /// <para>The exposure is checked HERE rather than trusted from the UI, for the same reason
        /// <see cref="DescribeCaptureNewSweep"/> guards its own divisor: the XAML
        /// <c>GreaterThanZeroRule</c> only refuses to push a bad value to the source — it cannot stop
        /// <see cref="RecaptureExposureSeconds"/> being set to zero from anywhere else, and this is the command
        /// that spends real sky time. A zero-second sweep is the one failure this must not merely log.</para>
        /// </summary>
        public bool CanCaptureNewSweep =>
            ShowCaptureNewSweep
            && RecaptureExposureSeconds > 0.0
            && autoFocusEngine != null
            && isCameraConnected()
            && isFocuserConnected()
            && !string.IsNullOrWhiteSpace(SaveFolderPath);

        /// <summary>
        /// Pre-fills <see cref="RecaptureExposureSeconds"/> from the SELECTED variant's recommendation. Called from
        /// <see cref="RaiseSelectedVariantDependents"/>, which is the single point that both every run path and
        /// every variant switch pass through once the selection has settled — so the box tracks the row it sits
        /// under, rather than only the run it was produced by. (Seeding once per run was not enough: see the call
        /// site for the Current-then-Optimized route that left a stale 5 s in the box beside a row asking for 11.)
        ///
        /// <para>Falls back to the exposure the run was captured with when there is no derived number, so the box
        /// never shows a bare 0 in the window between the block appearing and the user typing.</para>
        /// </summary>
        private void SeedRecaptureExposure() {
            var advice = SelectedSummary?.ExposureAdvice;
            var recommended = advice?.RecommendedSeconds ?? double.NaN;
            if (advice != null && advice.HasRecommendation && double.IsFinite(recommended) && recommended > 0.0) {
                RecaptureExposureSeconds = recommended;
                return;
            }
            RecaptureExposureSeconds = capturedLiveExposureSeconds > 0.0 ? capturedLiveExposureSeconds : LiveExposureSeconds;
        }


        private void RaiseExposureBlockChanged() {
            RaisePropertyChanged(nameof(HasExposureBlock));
            RaisePropertyChanged(nameof(LowSignalChartNote));
            RaisePropertyChanged(nameof(RecommendedExposureText));
            RaisePropertyChanged(nameof(HasRecommendedExposure));
            RaisePropertyChanged(nameof(ExposureBodyText));
            RaisePropertyChanged(nameof(HasExposureBody));
            RaisePropertyChanged(nameof(ExposureDerivationDetail));
            // The capture action's visibility and runnability read the same selected-summary state the copy above
            // does, so they are re-raised with it — the binning block's sibling call is right below, and the bug
            // that motivates both is documented on OptimizeAgainAtRecommendedBinningCommand's notification there.
            RaisePropertyChanged(nameof(ShowCaptureNewSweep));
            RaisePropertyChanged(nameof(CanCaptureNewSweep));
            CaptureNewSweepCommand?.NotifyCanExecuteChanged();
        }

        private void RaiseDetectionBinningBlockChanged() {
            RaisePropertyChanged(nameof(HasDetectionBinningBlock));
            RaisePropertyChanged(nameof(HasDetectionBinningBody));
            RaisePropertyChanged(nameof(DetectionBinningRecommendationText));
            RaisePropertyChanged(nameof(DetectionBinningBodyText));
            RaisePropertyChanged(nameof(ShowOptimizeAgainAtRecommendedBinning));
            RaisePropertyChanged(nameof(CanOptimizeAgainAtRecommendedBinning));
            RaisePropertyChanged(nameof(OptimizeAgainAtRecommendedBinningText));
            OptimizeAgainAtRecommendedBinningCommand?.NotifyCanExecuteChanged();
        }

        // The factor an "Optimize again at NxN" pass runs at, stamped onto each reloaded run's seed + baseline.
        // Deliberately NOT persisted: the wizard's contract is that Accept is the only thing that writes settings,
        // and a factor applied without the settings measured at it is an invalid combination. Cleared whenever a
        // run starts from the Select Source page.
        private int? pendingDetectionBinning;

        // Snapshotted when a Live sweep captures: the exposure it used (for the summary write-back) and a flag that a
        // live capture happened (gates the exposure write-back offer). A fresh Replay run clears the flag.
        private double capturedLiveExposureSeconds;
        private bool lastRunWasLive;
        // Snapshotted at Start (Live => Math.Max(0, FocusRecoverySteps), else 0) and stamped onto EVERY loaded run's
        // RunEvaluationData.RecoveryStepsPerSide (acquire AND the re-optimize/feedback reload loop) so both optimize
        // passes tag the same outer frames as recovery even if the UI box is later edited. 0 for Replay => inert.
        private int capturedRecoveryStepsPerSide;

        private bool LastRunWasLive {
            get => lastRunWasLive;
            set {
                if (lastRunWasLive != value) {
                    lastRunWasLive = value;
                    RaiseExposureRowChanged();
                }
            }
        }

        /// <summary>Whether the summary shows the sweep-exposure row — true only after a Live sweep, since a Replay
        /// run never chose a sweep exposure.</summary>
        public bool CanApplyExposureTime => lastRunWasLive;

        /// <summary>The sweep exposure as a "before → after" change against the profile's current AF exposure (or
        /// "N s (unchanged)"), shown in the auto-focus settings block for a Live run. The single Apply toggle writes it
        /// alongside the recommended step size/offset.</summary>
        public string SweepExposureChangeText {
            get {
                var current = profileService?.ActiveProfile?.FocuserSettings?.AutoFocusExposureTime ?? 0.0;
                return Math.Abs(current - capturedLiveExposureSeconds) < 1e-6
                    ? $"{capturedLiveExposureSeconds:0.##} s (unchanged)"
                    : $"{current:0.##} s → {capturedLiveExposureSeconds:0.##} s";
            }
        }

        /// <summary>True when a Live sweep chose an exposure that differs from the profile's current AF exposure, so
        /// the Apply-AF-settings toggle is enabled to write it even when the step size/offset are unchanged.</summary>
        private bool ExposureChangedForLiveRun {
            get {
                if (!lastRunWasLive || capturedLiveExposureSeconds <= 0) {
                    return false;
                }
                var current = profileService?.ActiveProfile?.FocuserSettings?.AutoFocusExposureTime ?? 0.0;
                return Math.Abs(current - capturedLiveExposureSeconds) > 1e-6;
            }
        }

        private void RaiseExposureRowChanged() {
            RaisePropertyChanged(nameof(CanApplyExposureTime));
            RaisePropertyChanged(nameof(CanApplyRecommendedStepSize));
            RaisePropertyChanged(nameof(SweepExposureChangeText));
            // The Star signal BODY is Live-vs-Replay dependent (ExposureBodyText reads lastRunWasLive), and this is
            // the one place that flag changes. Today the summary is always rebuilt afterwards, so the block would
            // be re-raised anyway — but that is an ordering accident, and an un-notified dependency of a mutable
            // field is exactly the bug class the variant-switch notification test exists to catch.
            RaiseExposureBlockChanged();
        }

        /// <summary>The changed-parameters rows shown on the summary: the optimized detector params, plus — only
        /// when <see cref="ApplyRecommendedStepSize"/> is on — the AF step size / offset-steps rows so the user
        /// sees the AF settings that Accept will also write. Recomputed when the toggle or the summary changes.</summary>
        public IReadOnlyList<ChangedParameterRow> ChangedParametersDisplay {
            get {
                if (Summary?.ChangedParameters == null) {
                    return Array.Empty<ChangedParameterRow>();
                }
                // For the Optimized variant after one or more "Continue" passes, show the FULL per-round path
                // (Current → R1 → R2 → …) from the chain; otherwise the plain current → optimized rows.
                var baseRows = (selectedVariant == OptimizationVariant.Optimized && optimizedChain.Count > 2)
                    ? BuildTrajectoryRows()
                    : new List<ChangedParameterRow>(Summary.ChangedParameters);
                var rows = baseRows;
                // Detection binning first: it is the change that gives every other row its units. Shown whenever
                // Accept will write it (an optimize-again result on a non-Current variant), so this table stays the
                // complete manifest of what Accept does.
                if (selectedVariant != OptimizationVariant.Current && (Summary.DetectionBinningPendingApply)) {
                    rows.Insert(0, new ChangedParameterRow {
                        Name = "Detection binning",
                        SeedValue = Summary.PersistedDetectionBinning,
                        OptimizedValue = Summary.RunDetectionBinning
                    });
                }
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

        /// <summary>Builds the multi-stage changed-parameter rows from the Continue chain: one row per curated knob
        /// that changed at ANY stage, carrying its value at every stage [current, round1, round2, …] so the table can
        /// render the full Current → R1 → R2 → … trajectory. Mirrors BuildSummaryAsync's "diff over the curated set"
        /// but across all stages rather than just current → best.</summary>
        private List<ChangedParameterRow> BuildTrajectoryRows() {
            var rows = new List<ChangedParameterRow>();
            var baseline = optimizedChain[0];
            foreach (var v in OptimizerVariable.CreateCuratedSet(baseline)) {
                var stages = optimizedChain.Select(p => v.Read(p)).ToList();
                if (stages.Skip(1).Any(s => Math.Abs(s - stages[0]) > 1e-9)) {
                    rows.Add(new ChangedParameterRow {
                        Name = v.Name,
                        SeedValue = stages[0],
                        OptimizedValue = stages[stages.Count - 1],
                        Stages = stages
                    });
                }
            }
            return rows;
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

        /// <summary>Live confirmation panel: pick (and persist) the folder the sweep saves its frames to.</summary>
        public RelayCommand BrowseSaveFolderCommand { get; }
        public AsyncRelayCommand StartCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand AcceptCommand { get; }

        /// <summary>Summary page: re-run the search at the measured recommended factor, on the frames already on
        /// disk. Writes nothing — see <see cref="OptimizeAgainAtRecommendedBinningAsync"/>.</summary>
        public AsyncRelayCommand OptimizeAgainAtRecommendedBinningCommand { get; }

        /// <summary>Summary page (Live runs only): capture a FRESH sweep at <see cref="RecaptureExposureSeconds"/>
        /// and re-optimize on it. Writes nothing — see <see cref="CaptureNewSweepAsync"/>.</summary>
        public AsyncRelayCommand CaptureNewSweepCommand { get; }

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

        /// <summary>Summary → continue optimizing: re-loads the runs from disk and runs another optimization pass
        /// seeded from the current best (fresh step scale), updating the Optimized variant in place and appending a
        /// stage to the trajectory. Enabled only while <see cref="CanContinueOptimization"/> (≤ 3 total passes).</summary>
        public AsyncRelayCommand ContinueOptimizationCommand { get; }

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
                // The sweep must save its frames somewhere, so Start stays disabled until a save folder is chosen.
                // Rough-focus confirmation is asked as an OK/Cancel dialog when Start is clicked (see StartAsync).
                return !string.IsNullOrWhiteSpace(SaveFolderPath);
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
            if (IsPerFilterEnabled && string.IsNullOrWhiteSpace(TargetFilterName)) {
                ErrorMessage = "Select a target filter.";
                return false;
            }
            if (SourceMode == SourceMode.Live) {
                if (!isCameraConnected()) {
                    ErrorMessage = "Connect a camera before running a live auto-focus optimization.";
                    return false;
                }
                if (!isFocuserConnected()) {
                    ErrorMessage = "Connect a focuser before running a live auto-focus optimization.";
                    return false;
                }
                if (IsPerFilterEnabled && !isFilterWheelConnected()) {
                    ErrorMessage = "Connect a filter wheel before running a live optimization.";
                    return false;
                }
                // The sweep must expose through EXACTLY the target filter. If the name can't be resolved to a
                // profile filter (a common device-name/profile-name mismatch, since the default target is seeded
                // from the wheel's reported filter), the engine would silently fall back to the designated AF
                // filter — or not move the wheel at all — and Accept would then write the result into the target
                // filter's settings set. Refuse to start rather than optimize the wrong filter.
                if (IsPerFilterEnabled && resolveFilterByName?.Invoke(TargetFilterName) == null) {
                    ErrorMessage = $"Filter '{TargetFilterName}' was not found in the profile's filter wheel settings. Choose a filter that exists in the profile.";
                    return false;
                }
                // Belt to CanStart's suspenders (and the engine's own guard): never start a sweep with nowhere to save.
                if (string.IsNullOrWhiteSpace(SaveFolderPath)) {
                    ErrorMessage = "Choose a folder to save the captured frames before running a live sweep.";
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
            // Belongs to the run that raised it, so a fresh Start clears it alongside the error.
            RecoveryWidenedNotice = null;
            // A fresh run always uses the PERSISTED factor: any pending optimize-again factor from a previous
            // summary was abandoned when the user came back here.
            pendingDetectionBinning = null;
            // Pre-flight validation: Live needs the camera + focuser connected; Saved needs a distinct, non-empty
            // folder per run. On failure, surface the error and stay on SelectSource without starting a run.
            if (!ValidateSourceBeforeStart()) {
                Interlocked.Exchange(ref running, 0);
                return;
            }
            // The sweep is centered on the current focuser position, so confirm rough focus before moving anything.
            // Declining leaves the wizard idle with no error (the user chose not to proceed).
            if (SourceMode == SourceMode.Live && !confirmRoughFocus()) {
                Interlocked.Exchange(ref running, 0);
                return;
            }
            // Whether this fresh run is a Live sweep decides whether the Summary offers the exposure write-back.
            // (Continue/Re-optimize don't reset it — they preserve the original run's nature.)
            LastRunWasLive = SourceMode == SourceMode.Live;
            // Snapshot the recovery knob NOW so a later edit of the box can't desync the widened capture from the
            // tagging, nor the first optimize pass from the re-optimize/feedback pass. Replay => 0 (recovery inert).
            capturedRecoveryStepsPerSide = (SourceMode == SourceMode.Live) ? Math.Max(0, FocusRecoverySteps) : 0;
            ResetVariants();
            // Clear stale progress so a re-run after a cancel/complete doesn't briefly show the previous run's
            // evaluation count / phase / improvement before the first progress callback overwrites them.
            SetProgress(null, 0, 0);
            ProgressSeedJ = 0;
            ProgressBestJ = 0;
            ProgressSeedSigma = double.NaN;
            ProgressBestSigma = double.NaN;
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
                ProgressSeedSigma = currentBaselineSigma;

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
                    // Round 1 of the "Continue optimizing" chain: [current settings, this pass's best]. Continue
                    // appends a stage per pass; the trajectory table reads these.
                    optimizedChain.Clear();
                    optimizedChain.Add(loadedRuns[0].Baseline);
                    optimizedChain.Add(optimizeResult.BestParams);
                    optimizedRoundJ.Clear();
                    optimizedRoundJ.Add(optimizeResult.BestJ);
                    optimizedRoundCurves.Clear();
                    optimizedRoundCurves.Add(built.OptimizedCurve); // round-1 curve (stage 0 = currentCurve, the baseline)
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

                RecordRunDuration();
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

        /// <summary>Per-filter runs resolve "current settings" from the TARGET filter's stored set, threaded to
        /// the loader as the baseline optionsOverride (the existing GetStarDetectorParams override seam). Null
        /// when the feature is off — baseline params byte-identical to today.</summary>
        private IStarDetectionOptions ResolveBaselineOptionsOverride() {
            if (!UsesTargetFilterSettings || getFilterDetectionOptions == null) {
                return null;
            }
            return getFilterDetectionOptions(TargetFilterName);
        }

        /// <summary>True when this run's settings come from the target filter's stored set rather than the
        /// options-page edit buffer.</summary>
        private bool UsesTargetFilterSettings => IsPerFilterEnabled && !string.IsNullOrEmpty(TargetFilterName);

        /// <summary>The settings set this RUN reads from: the target filter's stored set while per-filter routing
        /// applies, otherwise the options singleton — so with the feature off every read is byte-identical to
        /// before per-filter existed. Read-only: the store hands back clones, so writes must go through
        /// <see cref="setFilterDonutDetection"/>.</summary>
        private IStarDetectionOptions EffectiveDetectionOptions => ResolveBaselineOptionsOverride() ?? starDetectionOptions;

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
                            ErrorMessage = "The live sweep did not produce a saved set of frames. Check the focuser and try again.";
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
                    // Load + stamp the recovery snapshot together through the single choke-point (see LoadRunStampedAsync).
                    var loaded = await LoadRunStampedAsync(folder, null, loadProgress, token).ConfigureAwait(true);
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

        /// <summary>The SINGLE load-and-stamp choke-point for every run this VM loads. Loads the run through the loader
        /// exactly as the inline call sites did (same region/baseline-override; each caller still forwards its own
        /// <paramref name="frameLabels"/> and <paramref name="progress"/>), then stamps the recovery snapshot onto the
        /// loaded <see cref="RunEvaluationData"/> so the evaluator tags the outer sweep frames as recovery. Every reload
        /// path (acquire, re-optimize, continue) routes through here, so a future reload site can't ship un-stamped and
        /// silently drop the tag. Reads the <see cref="capturedRecoveryStepsPerSide"/> field on purpose: continue /
        /// re-optimize intentionally reuse the originating Start's snapshot. 0 for Replay/non-Live => the stamp is inert
        /// (RunEvaluationData is byte-identical to before, <see cref="RunEvaluationMetrics.FrameIsRecovery"/> stays null).</summary>
        private async Task<LoadedRun> LoadRunStampedAsync(
            string folder, IReadOnlyList<FrameLabels> frameLabels, IProgress<RunLoadProgress> progress, CancellationToken token) {
            var loaded = await loader.LoadSavedRunAsync(folder, region, frameLabels, progress, ResolveBaselineOptionsOverride(), token).ConfigureAwait(true);
            if (loaded?.Data != null) {
                loaded.Data.RecoveryStepsPerSide = capturedRecoveryStepsPerSide;
            }
            // An "Optimize again at NxN" pass analyzes at a factor the user has not committed to, so the loader
            // (which reads the live option) built the seed and baseline at the OLD factor. Re-stamp both here,
            // the single choke point every load goes through, so the search and its "before" comparison agree.
            if (pendingDetectionBinning.HasValue && loaded != null) {
                DetectionBinningResolver.ApplyFactor(loaded.Seed, pendingDetectionBinning.Value);
                DetectionBinningResolver.ApplyFactor(loaded.Baseline, pendingDetectionBinning.Value);
            }
            return loaded;
        }

        /// <summary>
        /// Captures a fixed, non-convergent focuser sweep centered on the current (rough-focus) position, saving its
        /// frames, then returns the saved attempt folder so the run converges onto the same replay path. Unlike a full
        /// auto-focus this does NOT require the current settings to build a focus curve — that is the whole point, so
        /// the wizard can then find settings that do. The sweep runs no star detection (the optimizer detects the saved
        /// frames afterward), so the progress panel shows capture readouts rather than a focus curve. Replay never
        /// reaches here.
        /// </summary>
        private async Task<string> RunLiveAttemptAsync(CancellationToken token) {
            if (autoFocusEngine == null) {
                throw new InvalidOperationException("Live mode requires an auto-focus engine.");
            }
            Phase = "Capturing frames";
            CaptureContextText = null;
            CaptureExposureText = null;
            ExposureProgressMax = 0;
            var options = autoFocusEngine.GetOptions();
            options.Save = true;                        // frames must land on disk so we can load them like a replay
            options.SavePath = SaveFolderPath;          // honor the folder chosen on the confirmation screen
            // Use the exposure the user set on the confirmation screen (they can lengthen it for faint/narrowband stars).
            options.OverrideAutoFocusExposureTime = TimeSpan.FromSeconds(LiveExposureSeconds);
            capturedLiveExposureSeconds = LiveExposureSeconds;   // remember what we captured with, for the Summary write-back
            RaiseExposureRowChanged();

            // Per-filter runs sweep on EXACTLY the chosen target filter: pass it as the imaging filter and set
            // UseExactImagingFilter so the engine skips the designated-AF-filter substitution. The wheel moves
            // as part of the sweep and stays on the target afterward.
            // ValidateSourceBeforeStart already refused an unresolvable target; this is the last line of defense —
            // a null filter here would make the engine fall back to the designated AF filter (or not move at all),
            // so fail loudly rather than sweep the wrong filter and attribute the result to the target.
            FilterInfo sweepFilter = null;
            if (IsPerFilterEnabled) {
                sweepFilter = resolveFilterByName?.Invoke(TargetFilterName);
                if (sweepFilter == null) {
                    throw new InvalidOperationException($"Filter '{TargetFilterName}' was not found in the profile's filter wheel settings.");
                }
                options.UseExactImagingFilter = true;
            }

            // Widen the captured sweep by the snapshotted recovery steps (never the live box) so the widened capture and
            // the tagged evaluation use the identical N, and scale the per-run timeout so the longer sweep doesn't time
            // out. No-op at N <= 0, keeping the Live path byte-identical when recovery is off.
            ApplyFocusRecovery(options, capturedRecoveryStepsPerSide);

            // Route the sweep's progress: the engine's per-point reports (tagged with its source) carry the focuser
            // position + frame count and drive the frame bar; the camera's own reports carry the exposure countdown.
            var captureProgress = new Progress<ApplicationStatus>(HandleCaptureProgress);

            IsCapturing = true;
            try {
                var afResult = await autoFocusEngine.CaptureFixedSweepAsync(options, sweepFilter, token, captureProgress).ConfigureAwait(true);
                // A failed/partial sweep may still have created its timestamped folder; treat "not succeeded" as no
                // usable capture (return null) so AcquireAsync surfaces a clean message rather than a confusing loader
                // error on an empty or too-short attempt folder.
                return afResult?.Succeeded == true ? afResult.SaveFolder : null;
            } finally {
                // Collapse the capture readouts before the "Loading frames" phase takes over the progress panel.
                IsCapturing = false;
                CaptureContextText = null;
                CaptureExposureText = null;
                ExposureProgressMax = 0;
            }
        }

        /// <summary>Widens a Live sweep's per-run options for focus recovery: adds <paramref name="recoverySteps"/> extra
        /// offset steps PER SIDE (widening the sweep RANGE, not refining it — <see cref="AutoFocusEngineOptions.AutoFocusStepSize"/>
        /// is deliberately untouched) and scales ONLY the per-run <see cref="AutoFocusEngineOptions.AutoFocusTimeout"/>
        /// override by the point-count ratio so the longer sweep (the fixed sweep enforces a hard timeout) doesn't time
        /// out. Never touches the persisted profile offset or timeout seconds. A no-op at <paramref name="recoverySteps"/>
        /// &lt;= 0 (or null options), so the Live path stays byte-identical when recovery is off. Internal + static so the
        /// widening is directly unit-testable.</summary>
        internal static void ApplyFocusRecovery(AutoFocusEngineOptions options, int recoverySteps) {
            if (options == null || recoverySteps <= 0) {
                return; // N<=0 keeps the Live path byte-identical
            }
            var oldOffset = options.AutoFocusInitialOffsetSteps;
            var oldPoints = 2 * oldOffset + 1;
            var newOffset = oldOffset + recoverySteps;
            var newPoints = 2 * newOffset + 1;
            options.AutoFocusInitialOffsetSteps = newOffset;             // widen range (do NOT touch AutoFocusStepSize — recovery widens, not refines)
            if (oldPoints > 0 && newPoints > oldPoints) {                // scale ONLY the per-run timeout override (fixed sweep enforces AutoFocusTimeout)
                var oldTicks = options.AutoFocusTimeout.Ticks;
                options.AutoFocusTimeout = TimeSpan.FromTicks((long)(oldTicks * (double)newPoints / oldPoints));
            }
        }

        /// <summary>Routes a sweep progress report to the capture readouts. The engine tags two streams: the frame
        /// context (focuser position + frame count, driving the frame bar) and the per-exposure countdown (elapsed /
        /// total seconds, driving the exposure bar). Any other report is NINA's own status, which goes to NINA's status
        /// bar rather than here, so it is ignored. Internal so the routing is directly unit-testable.</summary>
        internal void HandleCaptureProgress(ApplicationStatus status) {
            if (status == null) {
                return;
            }
            if (status.Source == AutoFocusEngine.LiveSweepProgressSource) {
                CaptureContextText = status.Status;
                if (status.MaxProgress > 0) {
                    SetProgress("Capturing frames", (int)status.Progress, status.MaxProgress);
                }
            } else if (status.Source == AutoFocusEngine.LiveSweepExposureSource) {
                ExposureProgressCurrent = status.Progress;
                ExposureProgressMax = status.MaxProgress;
                var remaining = Math.Max(0, status.MaxProgress - status.Progress);
                CaptureExposureText = $"Exposing, {remaining:0}s remaining";
            }
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
            // Which params to gate on. Replay checks the user's CURRENT settings (Baseline) — the run only exists
            // because those settings could already focus. A LIVE sweep is the opposite case: the current settings may
            // detect nothing (that is why the user is here), so gate on the SEED (default) params the optimizer
            // actually starts its search from — a sweep whose defaults find stars is worth optimizing even when the
            // current settings can't build a curve. Either way this first pass also warms each frame's expensive
            // early-detection context, which the optimizer then reuses.
            Func<LoadedRun, StarDetectorParams> guardParams = r => r.Baseline;
            if (SourceMode == SourceMode.Live) {
                guardParams = r => r.Seed;
            }

            var results = await AnalyzeWithProgressAsync(runs, guardParams, token).ConfigureAwait(true);
            if (AnyRunIsFittable(results, MinPositionsForFit)) {
                return true;
            }

            // Not fittable as configured. Before refusing, try WIDENING the focus-recovery exemption: on a wide
            // sweep the far wings can be too defocused for any settings to find stars, and one starless position
            // contributes a NaN HFR that poisons the fit for the whole run. Exempting the starless wings hands the
            // fit the interior positions, which is usually a clean curve. See
            // RunEvaluationData.RecoveryStepsToExcludeStarlessWings.
            //
            // Deliberately widened only to what the FIT needs, not to what the objective's per-frame hard floor
            // wants. Star counts depend on the detection params, so a position sitting just under the floor at the
            // seed may clear it once the search moves the gate -- that is the search's job, and exempting those
            // positions up front would discard the very evidence it needs. This guard only answers "is a curve
            // determinable at all", which is what it was always for.
            if (SourceMode == SourceMode.Live) {
                var widened = WidenedRecoveryStepsForStarlessWings(results);
                if (widened > capturedRecoveryStepsPerSide) {
                    var previous = capturedRecoveryStepsPerSide;
                    // Field, not local: every reload path stamps runs from it (see LoadRunStampedAsync), so the
                    // optimize pass and any continue/re-optimize see the same exemption this guard validated.
                    capturedRecoveryStepsPerSide = widened;
                    foreach (var run in runs) {
                        if (run?.Data != null) {
                            run.Data.RecoveryStepsPerSide = widened;
                        }
                    }
                    results = await AnalyzeWithProgressAsync(runs, guardParams, token).ConfigureAwait(true);
                    if (AnyRunIsFittable(results, MinPositionsForFit)) {
                        Logger.Info($"Star detection optimizer: focus-recovery exemption widened {previous} -> {widened} " +
                                    "steps/side; the sweep's outermost positions yielded no stars at the seed settings, " +
                                    "and exempting them makes the focus curve fittable.");
                        RecoveryWidenedNotice =
                            $"The sweep's outermost {widened} position(s) per side found no stars, so they were excluded " +
                            "from the focus curve. Optimization continued on the remaining positions. A smaller step size " +
                            "would put those frames to better use.";
                        return true;
                    }
                    capturedRecoveryStepsPerSide = previous; // widening did not help; report against the real setting
                    foreach (var run in runs) {
                        if (run?.Data != null) {
                            run.Data.RecoveryStepsPerSide = previous;
                        }
                    }
                }
            }

            ErrorMessage = SourceMode == SourceMode.Live
                ? "The captured sweep does not produce a usable focus curve at the default detection settings, and " +
                  "excluding its outermost positions does not help. Frames without stars in the middle of the sweep, " +
                  "or too few positions overall, both cause this. Try a smaller step size so more positions land near " +
                  "focus, a longer exposure, or the recommended detection binning."
                : "The selected auto-focus run(s) do not produce a usable focus curve at the current settings: " +
                  "too few focuser positions yielded detectable stars to fit a curve. Pick a run with stars across " +
                  "most frames, or re-acquire with a longer exposure before optimizing.";
            CurrentStep = WizardStep.SelectSource;
            return false;
        }

        /// <summary>
        /// Whether ANY run yields a determinable focus curve: a finite σ(focus) backed by ≥
        /// <paramref name="minPositionsForFit"/> NON-recovery positions. Gating on the non-recovery count matters
        /// because with recovery frames the raw PooledPointCount is trivially ≥ 3, so a starless-near-focus run
        /// could otherwise pass on its recovery positions alone. RecoveryStepsPerSide == 0 ⇒
        /// NonRecoveryPooledPointCount == PooledPointCount, so Replay / N=0 behaviour is unchanged.
        /// </summary>
        private static bool AnyRunIsFittable(IReadOnlyList<RunEvaluationResult> results, int minPositionsForFit) {
            foreach (var eval in results) {
                if (double.IsFinite(eval.Metrics.SigmaFocus) && eval.NonRecoveryPooledPointCount >= minPositionsForFit) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The smallest per-side exemption that clears the starless wings on the run that needs the LEAST widening
        /// — the guard passes when ANY run is fittable, so the cheapest run to rescue is the one to size for.
        /// 0 when no run has starless wings, or when every run's starless positions are interior (unfixable here).
        /// </summary>
        private static int WidenedRecoveryStepsForStarlessWings(IReadOnlyList<RunEvaluationResult> results) {
            var best = 0;
            foreach (var eval in results) {
                var needed = RunEvaluationData.RecoveryStepsToExcludeStarlessWings(
                    eval.Metrics?.FrameFocuserPositions, eval.Metrics?.FrameStarCounts);
                if (needed <= 0) {
                    continue; // -1 unfixable, 0 nothing starless
                }
                if (best == 0 || needed < best) {
                    best = needed;
                }
            }
            return best;
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
            // Evaluate the current settings on each run ONCE: this yields both the σ that anchors the
            // aberration-inspection fit guard and the metrics for the baseline J. σ is averaged over the runs that
            // produced a finite focus σ (matching BuildSummaryAsync's baseline-σ aggregation).
            var metrics = new List<RunEvaluationMetrics>(runs.Count);
            double sigmaSum = 0.0; var sigmaCount = 0;
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var eval = await runs[i].Data.EvaluateAndFitAsync(baseline, token).ConfigureAwait(true);
                metrics.Add(eval.Metrics);
                if (double.IsFinite(eval.Metrics.SigmaFocus)) { sigmaSum += eval.Metrics.SigmaFocus; sigmaCount++; }
            }

            // Finalize the objective for THIS run before scoring: aberration-inspection reweights toward stars and
            // bounds the fit relative to the measured current-settings σ; otherwise the standard objective. The same
            // constants are then handed to the optimizer (OptimizeAsync), so the baseline J and BestJ are comparable.
            var currentSigma = sigmaCount > 0 ? sigmaSum / sigmaCount : double.NaN;
            currentBaselineSigma = currentSigma;
            objectiveConstants = OptimizeForAberrationInspection
                ? ObjectiveConstants.ForAberrationInspection(currentSigma)
                : new ObjectiveConstants();

            var perRunJ = metrics.Select(m => OptimizationObjective.JRun(m, objectiveConstants)).ToList();
            return OptimizationObjective.JTotal(perRunJ, objectiveConstants);
        }

        /// <summary>Runs the pure optimizer off the UI thread; progress posts back via <see cref="IProgress{T}"/>.
        /// When <paramref name="seedOverride"/>/<paramref name="variablesOverride"/> are supplied (the warm-start
        /// "Optimize with feedback" path), the search starts from the analytic recommendation and explores only the
        /// narrowed/curated axes it produced; otherwise it uses the first run's seed and the full curated set.</summary>
        private async Task<OptimizationResult> OptimizeAsync(IReadOnlyList<LoadedRun> runs, CancellationToken token,
            StarDetectorParams seedOverride = null, IReadOnlyList<OptimizerVariable> variablesOverride = null,
            double? progressBaselineJOverride = null, double? progressBaselineSigmaOverride = null) {
            // All runs share the same camera/optics, so the search starts from the first run's seed (or the override).
            // With StartFromCurrentSettings, seed from the current settings (Baseline) to refine them rather than the
            // fully-default params. The warm-start override (feedback path) always wins.
            var seed = seedOverride ?? (StartFromCurrentSettings ? runs[0].Baseline : runs[0].Seed);
            // The master donut toggle is a profile option, NOT an optimizer axis: stamp it onto the seed so the
            // seed's early morph-close runs and CreateCuratedSet includes the defocus axes iff the user enabled it
            // (the default Seed carries master=OFF, so without this the donut feature would never be searched when
            // not starting from current settings).
            // Read once, from the SAME set the baseline came from (the target filter while per-filter is on), so the
            // seed, the budget, and runs[0].Baseline can never disagree about whether this run is hunting donuts.
            var donutMaster = EffectiveDetectionOptions.DefocusAwareDonutDetection;
            seed.DefocusAwareDonutDetection = donutMaster;
            // Donut recovery widens the curated search space (the defocus axes are added only when the master is
            // on), so it needs more iterations to converge: use the larger budget when enabled, else the standard
            // one. Re-applied each call so toggling the donut master between builds takes effect.
            optimizerSettings.MaxEvaluations = donutMaster ? DonutMaxEvaluations : standardMaxEvaluations;
            var variables = variablesOverride ?? OptimizerVariable.CreateCuratedSet(seed);
            var evaluator = RunEvaluationData.CreateEvaluator(runs.Select(r => r.Data).ToList());

            ProgressTotal = optimizerSettings.MaxEvaluations;
            var progress = new Progress<OptimizationProgress>(p => {
                ProgressCurrent = p.Evaluations;
                ProgressTotal = p.MaxEvaluations;
                ProgressBestJ = p.BestJ;
                ProgressBestSigma = p.BestSigmaFocus;
                // The displayed baseline is the user's CURRENT settings (currentBaselineJ / currentBaselineSigma) for a
                // fresh Start/feedback pass — not the optimizer's default seed J (p.SeedJ) — so the live readout reads
                // vs current and matches the summary. A "Continue optimizing" pass overrides both with the PRIOR ROUND's
                // best (the seed for that pass) so the live readout reflects the gain the continued search is making,
                // not the total gain since the initial baseline. J gates whether an improvement is claimed at all; σ is
                // what gets shown.
                ProgressSeedJ = progressBaselineJOverride ?? currentBaselineJ;
                ProgressSeedSigma = progressBaselineSigmaOverride ?? currentBaselineSigma;
                Phase = FriendlyPhase(p.Phase);
            });

            // Hand the optimizer the SAME objective the wizard scored the baseline with (standard, or the
            // aberration-inspection reweight + fit guard) so BestJ and the displayed before/after stay comparable.
            var optimizer = new StarDetectionOptimizer(objectiveConstants);
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
        /// <summary>
        /// Splits an evaluation's pooled scatter points into the NON-recovery (core) subset and the far-from-focus
        /// RECOVERY subset, so the chart can draw the recovery points as a visually-distinct hollow overlay while the
        /// main series draws only the core. A point is a recovery point iff its focuser position (X, rounded) is one of
        /// the DISTINCT positions flagged as recovery in <see cref="RunEvaluationMetrics.FrameIsRecovery"/>.
        ///
        /// <para>When recovery is OFF (<c>FrameIsRecovery</c> null) — or no position is flagged, or the recovery
        /// positions were excluded from <c>eval.Points</c> upstream (the un-weighted-fit case) — this returns
        /// <c>(points, empty)</c>, so <c>CorePoints</c> is content-identical to <c>Points</c> and the overlay is empty:
        /// rendering is byte-identical to before this feature. <c>core ∪ recovery == points</c> with no duplication.</para>
        /// </summary>
        internal static (IReadOnlyList<ScatterErrorPoint> core, IReadOnlyList<ScatterErrorPoint> recovery)
            PartitionRecoveryPoints(RunEvaluationResult eval) {
            var points = eval.Points;
            var metrics = eval.Metrics;
            if (points == null) return (System.Array.Empty<ScatterErrorPoint>(), System.Array.Empty<ScatterErrorPoint>());
            if (metrics?.FrameIsRecovery == null || metrics.FrameFocuserPositions == null) {
                return (points, System.Array.Empty<ScatterErrorPoint>());   // recovery off ⇒ all core, none recovery
            }
            var recoverySet = new HashSet<int>();
            var flags = metrics.FrameIsRecovery;
            var positions = metrics.FrameFocuserPositions;
            var n = System.Math.Min(flags.Count, positions.Count);
            for (var i = 0; i < n; i++) { if (flags[i]) recoverySet.Add(positions[i]); }
            if (recoverySet.Count == 0) return (points, System.Array.Empty<ScatterErrorPoint>());
            var core = new List<ScatterErrorPoint>();
            var recovery = new List<ScatterErrorPoint>();
            foreach (var p in points) {
                if (recoverySet.Contains((int)System.Math.Round(p.X))) recovery.Add(p); else core.Add(p);
            }
            return (core, recovery);
        }

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
            // The CURRENT-settings fit minimum from the representative run: the measured in-focus HFR the detection
            // binning recommendation is derived from. Detection reports HFR in captured pixels regardless of the
            // binning it analyzed at, so this needs no rescaling.
            var measuredInFocusHfr = double.NaN;
            var measuredFitRSquared = double.NaN;
            var baselineInFocusHfr = double.NaN;
            var baselineFitRSquared = double.NaN;
            // The exposure-time recommendation, derived from run 0 ONLY — matching the detection-binning precedent
            // above. With several Replay runs the per-run exposures may legitimately differ, and a single number
            // covering all of them would be ill-defined; the representative run is the one whose curve is plotted.
            var (runExposureSeconds, runExposureIsAssumed) = ResolveRunExposureSeconds(runs[0]);
            ExposureRecommendation exposureAdvice = null, baselineExposureAdvice = null;
            OptimizationCurve currentCurveLocal = null, optimizedCurveLocal = null;
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var baselineEval = await runs[i].Data.EvaluateAndFitAsync(baseline, token).ConfigureAwait(true);
                var bestEval = await runs[i].Data.EvaluateAndFitAsync(res.BestParams, token).ConfigureAwait(true);
                if (double.IsFinite(baselineEval.Metrics.SigmaFocus)) { baselineSigmaSum += baselineEval.Metrics.SigmaFocus; baselineSigmaCount++; }
                if (double.IsFinite(bestEval.Metrics.SigmaFocus)) { bestSigmaSum += bestEval.Metrics.SigmaFocus; bestSigmaCount++; }
                if (i == 0) {
                    representativeBestFit = bestEval.BestFit;
                    // This summary describes the OPTIMIZED variant, so its in-focus HFR comes from the optimized
                    // curve — the one plotted above it and the one Accept puts into service. The current-settings
                    // numbers are carried alongside for BuildCurrentSummary.
                    measuredInFocusHfr = bestEval.BestFit?.Minimum.Y ?? double.NaN;
                    measuredFitRSquared = bestEval.Metrics?.RSquared ?? double.NaN;
                    baselineInFocusHfr = baselineEval.BestFit?.Minimum.Y ?? double.NaN;
                    baselineFitRSquared = baselineEval.Metrics?.RSquared ?? double.NaN;
                    // Both variants' exposure advice, computed HERE because both RunEvaluationResults are already
                    // in hand — the recommender only reads Metrics.FrameStarSnrs, so this costs no extra evaluation
                    // pass. objectiveConstants (not a fresh ObjectiveConstants) so the recommendation inverts the
                    // SAME NTarget star-count knee the search just optimized against; the aberration-inspection
                    // profile moves that knee to 60.
                    // Each variant gets ITS OWN params: the inert-gate bound is PeakResponse x the effective clip
                    // multiplier and both are searched axes, so the optimized and current settings can have
                    // different bounds on the same frames (F28). Same pairing rule as VariantSensitivity /
                    // BaselineSensitivity.
                    exposureAdvice = ExposureRecommender.Recommend(bestEval.Metrics, objectiveConstants, runExposureSeconds, res.BestParams);
                    baselineExposureAdvice = ExposureRecommender.Recommend(baselineEval.Metrics, objectiveConstants, runExposureSeconds, baseline);
                    var (baselineCore, baselineRecovery) = PartitionRecoveryPoints(baselineEval);
                    var (bestCore, bestRecovery) = PartitionRecoveryPoints(bestEval);
                    currentCurveLocal = new OptimizationCurve {
                        Label = "Current", Points = baselineEval.Points, Fit = baselineEval.BestFit,
                        CorePoints = baselineCore, RecoveryPoints = baselineRecovery,
                        FrameStarCounts = baselineEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = baselineEval.Metrics.FrameFocuserPositions
                    };
                    optimizedCurveLocal = new OptimizationCurve {
                        Label = "Optimized", Points = bestEval.Points, Fit = bestEval.BestFit,
                        CorePoints = bestCore, RecoveryPoints = bestRecovery,
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
                StepSizeWasCapped = recommendation.WasCapped,
                CurrentStepSize = currentStepSize,
                CurrentOffsetSteps = currentOffsetSteps,
                ImprovedOverSeed = res.ImprovedOverSeed,
                RunDetectionBinning = Math.Max(1, baseline.DetectionBinning),
                PersistedDetectionBinning = DetectionBinningResolver.ToFactor(starDetectionOptions.DetectionBinning),
                MeasuredInFocusHfr = measuredInFocusHfr,
                FitRSquared = measuredFitRSquared,
                BaselineMeasuredInFocusHfr = baselineInFocusHfr,
                BaselineFitRSquared = baselineFitRSquared,
                RecommendedDetectionBinning = DetectionBinningResolver.RecommendFromHfr(measuredInFocusHfr),
                RunExposureSeconds = runExposureSeconds,
                RunExposureIsAssumed = runExposureIsAssumed,
                // This summary describes the OPTIMIZED variant, so the gate it reports is the optimized one — the
                // gate Accept puts into service. The current-settings gate is carried alongside for
                // BuildCurrentSummary, exactly as the in-focus HFR pair is.
                VariantSensitivity = res.BestParams?.Sensitivity ?? double.NaN,
                BaselineSensitivity = baseline?.Sensitivity ?? double.NaN,
                ExposureAdvice = exposureAdvice,
                BaselineExposureAdvice = baselineExposureAdvice
            };
            return (summary, currentCurveLocal, optimizedCurveLocal);
        }

        /// <summary>
        /// Builds the "Current" variant's summary from the optimized run's summary: nothing changed, so the
        /// changed-parameters table is empty, J/σ collapse to the seed baseline, and the AF recommendation equals the
        /// current profile values (so the apply-step-size toggle is disabled). Used so toggling to "Current" shows a
        /// truthful "no changes" summary alongside the current curve.
        /// </summary>
        internal static OptimizationSummary BuildCurrentSummary(OptimizationSummary optimized) {
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
                ImprovedOverSeed = false,
                RunDetectionBinning = optimized.RunDetectionBinning,
                PersistedDetectionBinning = optimized.PersistedDetectionBinning,
                // The Current view keeps the current detector settings, so its in-focus HFR is the one THOSE
                // settings measure, not the optimized run's.
                MeasuredInFocusHfr = optimized.BaselineMeasuredInFocusHfr,
                FitRSquared = optimized.BaselineFitRSquared,
                BaselineMeasuredInFocusHfr = optimized.BaselineMeasuredInFocusHfr,
                BaselineFitRSquared = optimized.BaselineFitRSquared,
                RecommendedDetectionBinning = DetectionBinningResolver.RecommendFromHfr(optimized.BaselineMeasuredInFocusHfr),
                // Both variants ran on the same frames, so the exposure — and whether it had to be assumed — is the
                // same number on either view.
                RunExposureSeconds = optimized.RunExposureSeconds,
                RunExposureIsAssumed = optimized.RunExposureIsAssumed,
                // The Current view keeps the current detector settings, so it reports THEIR gate and the advice
                // derived from THEIR accepted stars — not the optimizer's. That is what tells a user who hand-set
                // their own Sensitivity to 0 why this block is on screen at all.
                VariantSensitivity = optimized.BaselineSensitivity,
                BaselineSensitivity = optimized.BaselineSensitivity,
                ExposureAdvice = optimized.BaselineExposureAdvice,
                BaselineExposureAdvice = optimized.BaselineExposureAdvice
            };
        }

        /// <summary>
        /// The exposure time (seconds) to scale the exposure recommendation from — <c>t_old</c>. Three sources, in
        /// order of how directly they describe the frames that were actually scored:
        ///
        /// <list type="number">
        /// <item>a Live sweep's own captured exposure (<see cref="capturedLiveExposureSeconds"/>) — exact, and it
        /// is what the wizard itself set on the camera;</item>
        /// <item>a Replay run's <see cref="LoadedRun.CapturedExposureSeconds"/>, read back out of the saved frames'
        /// headers, since nothing in a saved attempt records it otherwise;</item>
        /// <item>the profile's auto-focus exposure — a FALLBACK, not a measurement: it is what an auto-focus run
        /// would use TODAY, which is only the same number if the saved run used the current setting. Called out in
        /// the Star signal tooltip so the derived figure is not read as a property of the frames.</item>
        /// </list>
        ///
        /// <para>Returns <see cref="double.NaN"/> when none of the three yields a positive number, which makes
        /// <see cref="ExposureRecommender.Recommend"/> withhold every derived figure and leaves the block showing
        /// only its diagnosis. Never scale a factor off an unknown base.</para>
        ///
        /// <para><c>IsAssumed</c> distinguishes source 3 from sources 1 and 2, so the copy can say the derivation
        /// rests on an assumption about how the frames were captured rather than on the frames themselves.</para>
        /// </summary>
        private (double Seconds, bool IsAssumed) ResolveRunExposureSeconds(LoadedRun run) {
            if (lastRunWasLive && capturedLiveExposureSeconds > 0.0) {
                return (capturedLiveExposureSeconds, false);
            }
            var recorded = run?.CapturedExposureSeconds ?? double.NaN;
            if (double.IsFinite(recorded) && recorded > 0.0) {
                return (recorded, false);
            }
            var profileExposure = profileService?.ActiveProfile?.FocuserSettings?.AutoFocusExposureTime ?? double.NaN;
            return double.IsFinite(profileExposure) && profileExposure > 0.0
                ? (profileExposure, true)
                : (double.NaN, false);   // nothing at all: not "assumed", just unknown
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
        /// <summary>Accept is enabled on the finished Summary view when the VM is idle and there is something to apply.
        /// A non-Current variant always has its optimized detector settings to apply. The Current variant keeps the
        /// detector settings untouched, so it can be accepted only to write the recommended auto-focus settings — for
        /// a live sweep, the exposure you chose — i.e. when the AF-settings toggle is on and something actually changed.
        /// This lets a live run whose optimizer could not beat the current detector still apply its exposure without
        /// being forced to switch to the Optimized variant.</summary>
        private bool CanAccept() {
            if (IsBusy || !IsSummary || SelectedResult == null || SelectedSummary == null) {
                return false;
            }
            if (selectedVariant != OptimizationVariant.Current) {
                return true;
            }
            return ApplyRecommendedStepSize && CanApplyRecommendedStepSize;
        }

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

            // A non-Current variant applies its optimized detector settings. The Current variant keeps the current
            // detector settings untouched (the optimizer did not beat them) and only writes the recommended auto-focus
            // settings below. Build the snapshot via the shared params->DTO mapping (the single source of truth shared
            // with the headless harness) so the in-app and offline paths can never drift.
            if (selectedVariant != OptimizationVariant.Current) {
                var dto = OptimizedStarDetectionSettings.FromParams(
                    result.BestParams, summary.RunCount, summary.SeedJ, summary.BestJ,
                    summary.RecommendedStepSize, summary.RecommendedOffsetSteps);

                if (IsPerFilterEnabled && applyOptimizedToFilter != null) {
                    // Per-filter: write the result into the TARGET filter's settings set (production routes
                    // through the edit binder, so the options page lands on the filter just optimized).
                    applyOptimizedToFilter(TargetFilterName, dto);
                    Logger.Info($"Applied optimized star-detection settings to filter '{TargetFilterName}' (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");
                } else {
                    starDetectionOptions.ApplyOptimizedSettings(dto);
                    Logger.Info($"Applied optimized star-detection settings (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");
                }

                // The factor and the settings measured at it are written TOGETHER, and only here. Splitting them
                // would apply a combination the optimizer never evaluated. Global even in per-filter mode: binning
                // follows the optics, not the filter.
                if (summary.DetectionBinningPendingApply) {
                    var factor = summary.RunDetectionBinning;
                    starDetectionOptions.DetectionBinning = DetectionBinningResolver.ToSetting(factor);
                    Logger.Info($"Applied detection binning {factor}x{factor} with the settings optimized at that factor (was {summary.PersistedDetectionBinning}x{summary.PersistedDetectionBinning})");
                }
            } else {
                Logger.Info("Keeping current star-detection settings (the optimizer did not beat them); applying the recommended auto-focus settings only.");
            }

            // Publish the ACCEPTED variant's measured in-focus HFR so the options page agrees with what is now in
            // service — only from a LIVE sweep, whose frames were captured on this rig just now (replaying a saved
            // run, possibly from another scope or another night, must not overwrite the rig's measurement), and
            // only on Accept, keeping the wizard's contract that Accept is the only thing that writes.
            if (lastRunWasLive && summary.HasDetectionBinningMeasurement) {
                recordMeasuredInFocusHfr?.Invoke(summary.MeasuredInFocusHfr);
            }

            // The single "Apply these auto-focus settings" toggle writes the recommended step size / offset, and for a
            // Live sweep the exposure it captured with too (the user may have lengthened it to make focus work, so it
            // belongs with the other AF settings). Only write a positive exposure: a non-positive value was never
            // actually used (the sweep fell back to the profile/filter exposure), so writing it would corrupt the profile.
            if (ApplyRecommendedStepSize) {
                var focuserSettings = profileService?.ActiveProfile?.FocuserSettings;
                if (focuserSettings != null) {
                    focuserSettings.AutoFocusStepSize = summary.RecommendedStepSize;
                    focuserSettings.AutoFocusInitialOffsetSteps = summary.RecommendedOffsetSteps;
                    Logger.Info($"Applied recommended AF step size {summary.RecommendedStepSize}, offset steps {summary.RecommendedOffsetSteps}");

                    if (lastRunWasLive && capturedLiveExposureSeconds > 0) {
                        focuserSettings.AutoFocusExposureTime = capturedLiveExposureSeconds;
                        Logger.Info($"Applied AF exposure time {capturedLiveExposureSeconds}s to the active profile");
                    }
                }
            }
        }

        // ---- Review step (optional, post-Summary) ----------------------------------------------------------

        /// <summary>
        /// Installs the result of a pass that RE-TUNED FROM SCRATCH — "Optimize again at NxN" and "Capture a new
        /// sweep" — as the Optimized variant, and selects it.
        ///
        /// <para>Distinct from <see cref="ContinueOptimizationAsync"/>'s adoption, which APPENDS a stage: both
        /// callers here seeded fresh, so the round chain is RESET to a single stage rather than extended. Splicing
        /// either onto the previous trajectory would render a per-round path whose stages were measured in
        /// different units — a different pixel scale for the binning pass, a different exposure for the capture —
        /// and the numbers would look comparable when they are not.</para>
        ///
        /// <para>The feedback variant is dropped for the same reason: it was measured under the superseded
        /// conditions, so it can neither be compared against this result nor accepted alongside it.</para>
        ///
        /// <para><b>What it deliberately does NOT do is touch the review snapshot</b> (<c>capturedLabels</c>,
        /// <see cref="ReviewVM"/>, <c>reviewFrames</c>, <c>reviewParams</c>, <see cref="Recommendation"/>). The two
        /// callers differ there and must: the binning pass re-reads the SAME files, so its RunIds and the labels
        /// keyed by them still line up, and silently discarding a user's labelling work would be a regression. The
        /// capture pass replaces the frames outright and so calls <see cref="DiscardReviewSnapshot"/> itself. Any
        /// third caller has to make that decision explicitly, which is why it is not folded in here.</para>
        /// </summary>
        private void AdoptFreshTuning(
            OptimizationResult optimizeResult,
            (OptimizationSummary Summary, OptimizationCurve CurrentCurve, OptimizationCurve OptimizedCurve) built) {
            optimizedResult = optimizeResult;
            optimizedSummary = built.Summary;
            optimizedCurve = built.OptimizedCurve;
            currentCurve = built.CurrentCurve;
            optimizedChain.Clear();
            optimizedChain.Add(optimizeResult.BestParams);
            optimizedRoundJ.Clear();
            optimizedRoundJ.Add(optimizeResult.BestJ);
            optimizedRoundCurves.Clear();
            optimizedRoundCurves.Add(built.OptimizedCurve);
            feedbackResult = null;
            feedbackSummary = null;
            feedbackCurve = null;
            currentSummary = BuildCurrentSummary(built.Summary);
            OptimizerImprovedOverCurrent = optimizeResult.BestJ > currentBaselineJ + ImprovementEpsilon;
            selectedVariant = OptimizationVariant.Optimized;
            RaiseSelectedVariantDependents();
        }

        /// <summary>
        /// Drops the whole review snapshot — the built <see cref="ReviewVM"/> and its subscription, the in-memory
        /// labels, the detected frames and the params they were detected at, and the label→gate recommendation —
        /// exactly as <see cref="StartAsync"/> does when a fresh run begins, and for exactly the same reason: the
        /// images those labels were drawn on are no longer the images this result describes.
        ///
        /// <para>Called by the capture path only. It is NOT enough to null the feedback variant: the labels
        /// themselves drive <see cref="ShowReoptimizePrompt"/> and re-enable <see cref="ReOptimizeCommand"/>, so
        /// leaving them behind puts a "you labeled stars — re-run optimization" prompt on a summary built from
        /// frames those labels never saw.</para>
        ///
        /// <para><c>reviewDescriptors</c> / <c>reviewLabelsDir</c> / the re-optimize folders are NOT cleared here:
        /// every caller re-establishes them from the new runs via <see cref="SnapshotReviewInputs"/> on the very
        /// next line, and clearing them first would only widen the window in which they are inconsistent.</para>
        ///
        /// <para>This does NOT write anything. A caller that is discarding labels the user actually drew must call
        /// <see cref="PersistReviewLabels"/> FIRST — it is <see cref="ReviewVM"/> that performs the write, and this
        /// nulls it — so their work lands on disk under the run it describes. The capture path does exactly that;
        /// see the call site for why the ordering is load-bearing in both directions.</para>
        /// </summary>
        private void DiscardReviewSnapshot() {
            if (ReviewVM != null) {
                ReviewVM.PropertyChanged -= OnReviewLabelsChanged;
            }
            ReviewVM = null;
            capturedLabels = null;
            reviewFrames = null;
            reviewParams = null;
            Recommendation = null;
            RaisePropertyChanged(nameof(HasLabels));
            RaisePropertyChanged(nameof(ShowReoptimizePrompt));
            RaisePropertyChanged(nameof(ShowFeedbackPanel));
            ReOptimizeCommand.NotifyCanExecuteChanged();
        }

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
            // The detection-binning block reads reoptimizeRunFolders to decide whether the re-run can proceed, and
            // every run path raises its dependents (RaiseSelectedVariantDependents) BEFORE reaching here — so
            // without this the block is computed against the previous run's (empty) folders and never refreshed.
            // The button then sits permanently disabled under copy claiming the frames are gone, while they are on
            // disk exactly where this method just recorded them.
            RaiseDetectionBinningBlockChanged();
            // The Star signal block is refreshed at the same point, for the same structural reason: this is where a
            // run's post-success state finally settles, AFTER every path has already raised the summary's
            // dependents. Its inputs are all fixed by BuildSummaryAsync today, so this is currently belt-and-braces
            // — but the binning block's bug was exactly "a later-settling input was never re-notified", and pairing
            // the two refreshes keeps the next input added here (the Live capture action reads the same
            // run-availability state the binning button does) from re-introducing it.
            RaiseExposureBlockChanged();
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
                ReviewVM = new StarReviewVM(reviews, labelsByRun, labelsDir ?? string.Empty, EffectiveDetectionOptions.MeasurementAverage);
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
            ProgressSeedSigma = double.NaN;
            ProgressBestSigma = double.NaN;
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
                    // CRITICAL: load + stamp through the single choke-point (LoadRunStampedAsync) so the feedback/second
                    // optimize pass reuses the SAME recovery snapshot the first pass used — otherwise it loses the tag and
                    // its runs hard-floor on star-count gates, making its J disagree with pass 1.
                    var loaded = await LoadRunStampedAsync(folder, frameLabels, loadProgress, token).ConfigureAwait(true);
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

                RecordRunDuration();
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
        /// Summary → "Continue optimizing": runs ANOTHER optimization pass seeded from the current Optimized best,
        /// updating the Optimized variant in place and appending a stage to the trajectory (Current → R1 → R2 → …).
        /// Like <see cref="ReOptimizeWithLabelsAsync"/> it RE-LOADS the runs from disk (the first pass disposed the
        /// in-memory data). Re-seeding with a FRESH curated set (built inside <see cref="OptimizeAsync"/> from the new
        /// seed) resets the pattern-search step scale, so the search can make larger moves again — the whole point of
        /// continuing. Never regresses: each pass's never-regress floor is its own seed (the prior best). Capped at
        /// <see cref="MaxOptimizationRounds"/> total passes via <see cref="CanContinueOptimization"/>. No labels are
        /// used (the feedback variant, if any, is left untouched).
        /// </summary>
        private async Task ContinueOptimizationAsync(CancellationToken externalToken) {
            if (!CanContinueOptimization || reoptimizeRunFolders == null || reoptimizeRunFolders.Count == 0) {
                return;
            }
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) {
                return; // a Start/re-optimize/continue is already in flight
            }

            // The seed for this pass is the CURRENT Optimized best (captured before any UI churn). optimizedResult is
            // not cleared by this path, so this stays valid through the reload.
            var seedForContinue = optimizedResult.BestParams;
            // The prior round's best J/σ are the live baseline for THIS pass: the user wants the readout to measure the
            // leg the continued search is adding, relative to where the last round left off, not the total gain since
            // the initial current-settings baseline. That mirrors the summary, whose σ trajectory appends this pass as
            // the next stage of "baseline → R1 → R2 → …".
            var seedForContinueJ = optimizedResult.BestJ;
            var seedForContinueSigma = optimizedSummary?.BestSigmaFocus ?? double.NaN;

            ErrorMessage = null;
            SetProgress(null, 0, 0);
            // Seed the live baseline synchronously (not via the async progress callback) so the readout is correct from
            // the first frame and deterministic for tests; ProgressBestJ stays 0 so it starts at "Searching…".
            ProgressSeedJ = seedForContinueJ;
            ProgressBestJ = 0;
            ProgressSeedSigma = seedForContinueSigma;
            ProgressBestSigma = double.NaN;
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
                    var loadProgress = new Progress<RunLoadProgress>(rp =>
                        SetProgress("Loading frames", rp.Current, rp.Total));
                    // No labels for a plain continue (byte-identical to the no-label load); load + stamp through the single
                    // choke-point (LoadRunStampedAsync) so a continued round keeps the recovery tag and its J stays
                    // comparable with the prior round. 0 for Replay/N=0 => inert.
                    var loaded = await LoadRunStampedAsync(folder, null, loadProgress, token).ConfigureAwait(true);
                    reloaded.Add(loaded);
                }

                // Warm the early contexts the optimizer will start from (the new seed), so the bar moves during the
                // first build (continue skips the seed-guard, like the re-optimize path).
                await AnalyzeWithProgressAsync(reloaded, _ => seedForContinue, token).ConfigureAwait(true);

                // Baseline J (current settings) for the reloaded runs — also (re)builds objectiveConstants for THIS
                // run, so the inspection-vs-standard objective stays consistent across continued passes.
                currentBaselineJ = await ComputeBaselineJAsync(reloaded, token).ConfigureAwait(true);

                // Another optimize pass seeded from the prior best; variablesOverride=null ⇒ OptimizeAsync builds a
                // fresh CreateCuratedSet(seedForContinue) with reset step scale. The progress baseline override makes
                // the live readout measure this pass against the prior round's best rather than the current baseline.
                var optimizeResult = await OptimizeAsync(reloaded, token, seedForContinue,
                    progressBaselineJOverride: seedForContinueJ,
                    progressBaselineSigmaOverride: seedForContinueSigma).ConfigureAwait(true);
                var built = await BuildSummaryAsync(reloaded, optimizeResult, token).ConfigureAwait(true);

                // The latest pass REPLACES the Optimized variant (chart + Accept follow it); append the trajectory stage.
                optimizedResult = optimizeResult;
                optimizedSummary = built.Summary;
                optimizedCurve = built.OptimizedCurve;
                optimizedChain.Add(optimizeResult.BestParams);
                optimizedRoundJ.Add(optimizeResult.BestJ);
                optimizedRoundCurves.Add(built.OptimizedCurve); // retain this round's curve for the per-frame trajectory
                OptimizerImprovedOverCurrent = optimizeResult.BestJ > currentBaselineJ + ImprovementEpsilon;
                selectedVariant = OptimizationVariant.Optimized;
                RaiseSelectedVariantDependents();

                // Re-snapshot so a further Continue (or Review) stays reachable from the freshly-reloaded runs.
                SnapshotReviewInputs(reloaded, reoptimizeRunFolders, reoptimizeRunIds);

                RecordRunDuration();
                CurrentStep = WizardStep.Summary;
            } catch (OperationCanceledException) {
                Logger.Info("Star detection continue-optimization cancelled");
                CurrentStep = WizardStep.Summary;
            } catch (Exception ex) {
                Logger.Error(ex, "Star detection continue-optimization failed");
                ErrorMessage = $"Continue optimization failed: {ex.Message}";
                CurrentStep = WizardStep.Summary;
            } finally {
                DisposeLoadedRuns(reloaded);
                IsBusy = false;
                Interlocked.Exchange(ref running, 0);
            }
        }

        /// <summary>
        /// Re-runs the whole optimization on the frames already on disk, at the factor this run's MEASURED in-focus
        /// HFR calls for. Deliberately persists nothing: the new factor is stamped onto the reloaded runs only, and
        /// is written to the profile — together with the settings tuned at it — when the user Accepts.
        ///
        /// <para>This is the ONLY way the wizard changes the factor, and that is the point. Applying a factor
        /// without the settings measured at it produces a combination that was never evaluated: every pixel-unit
        /// knob shifts meaning and measured HFR moves several percent. A checkbox or a standalone apply button
        /// could produce exactly that, however it was worded, so neither exists.</para>
        ///
        /// <para>Unlike <see cref="ContinueOptimizationAsync"/> this seeds FRESH (no seed override) and resets the
        /// round chain: the previous best was tuned in the old factor's units and is not a meaningful starting
        /// point, nor a meaningful thing to compare against.</para>
        /// </summary>
        private async Task OptimizeAgainAtRecommendedBinningAsync(CancellationToken externalToken) {
            if (!CanOptimizeAgainAtRecommendedBinning) {
                return;
            }
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) {
                return; // a Start/re-optimize/continue is already in flight
            }

            var target = SelectedSummary.RecommendedDetectionBinning;
            if (!confirmReoptimizeAtBinning(SelectedSummary.RunDetectionBinning, target)) {
                Interlocked.Exchange(ref running, 0);
                return;
            }
            var previous = pendingDetectionBinning;
            pendingDetectionBinning = target;

            ErrorMessage = null;
            SetProgress(null, 0, 0);
            ProgressSeedJ = 0;
            ProgressBestJ = 0;
            ProgressSeedSigma = double.NaN;
            ProgressBestSigma = double.NaN;
            IsBusy = true;

            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            List<LoadedRun> reloaded = null;
            var succeeded = false;
            try {
                CurrentStep = WizardStep.Optimize;
                reloaded = new List<LoadedRun>(reoptimizeRunFolders.Count);
                for (var i = 0; i < reoptimizeRunFolders.Count; i++) {
                    token.ThrowIfCancellationRequested();
                    SetProgress($"Re-loading frames for {target}x{target}", 0, 0);
                    var loadProgress = new Progress<RunLoadProgress>(rp =>
                        SetProgress($"Loading frames for {target}x{target}", rp.Current, rp.Total));
                    reloaded.Add(await LoadRunStampedAsync(reoptimizeRunFolders[i], null, loadProgress, token).ConfigureAwait(true));
                }

                await AnalyzeWithProgressAsync(reloaded, r => r.Seed, token).ConfigureAwait(true);
                // The "before" for this pass is the user's current settings measured AT THE NEW FACTOR — the only
                // honest comparison, since a baseline J from the old factor is in different units.
                currentBaselineJ = await ComputeBaselineJAsync(reloaded, token).ConfigureAwait(true);

                var optimizeResult = await OptimizeAsync(reloaded, token).ConfigureAwait(true);
                var built = await BuildSummaryAsync(reloaded, optimizeResult, token).ConfigureAwait(true);

                // A fresh tuning, not another round: reset the chain so the trajectory does not splice together
                // values measured in two different pixel scales. The frames themselves are unchanged, so unlike the
                // capture path this deliberately KEEPS the review snapshot — see AdoptFreshTuning.
                AdoptFreshTuning(optimizeResult, built);

                SnapshotReviewInputs(reloaded, reoptimizeRunFolders, reoptimizeRunIds);
                RecordRunDuration();
                CurrentStep = WizardStep.Summary;
                succeeded = true;
                Logger.Info($"Re-optimized at detection binning {target}x{target} (not yet applied; Accept writes it with the settings)");
            } catch (OperationCanceledException) {
                Logger.Info("Re-optimize at the recommended detection binning was cancelled");
                CurrentStep = WizardStep.Summary;
            } catch (Exception ex) {
                Logger.Error(ex, "Re-optimize at the recommended detection binning failed");
                ErrorMessage = $"Optimizing at {target}x{target} failed: {ex.Message}";
                CurrentStep = WizardStep.Summary;
            } finally {
                if (!succeeded) {
                    // Cancelled or failed: drop the pending factor so nothing downstream believes this run happened
                    // at it. Nothing was persisted, so there is no residue to undo.
                    pendingDetectionBinning = previous;
                }
                DisposeLoadedRuns(reloaded);
                IsBusy = false;
                Interlocked.Exchange(ref running, 0);
            }
        }

        /// <summary>
        /// Summary → "Capture a new sweep and optimize": captures a WHOLE NEW Live sweep at
        /// <see cref="RecaptureExposureSeconds"/> and re-runs the optimization on the fresh frames. The answer to a
        /// signal-starved run whose gate floored: the settings cannot be fixed on frames that never had the signal,
        /// so the frames are replaced rather than re-analyzed.
        ///
        /// <para>Structurally the twin of <see cref="OptimizeAgainAtRecommendedBinningAsync"/> — same interlock,
        /// same progress reset, same chain reset, same <c>succeeded</c>/<c>finally</c> restore — with four
        /// deliberate differences, each of which was a trap:</para>
        ///
        /// <list type="number">
        /// <item>It CAPTURES via <see cref="RunLiveAttemptAsync"/> instead of re-loading
        /// <c>reoptimizeRunFolders</c>. Those folders hold the starved frames; re-reading them is exactly what
        /// cannot help.</item>
        /// <item>It loops over <see cref="RunCount"/>, not over the recorded folders. A user who configured 2 runs
        /// gets 2 sweeps — capturing a single sweep here would silently halve a multi-run configuration, and the
        /// summary would then compare a 1-run result against a 2-run baseline.</item>
        /// <item>The snapshot at the end passes the FRESH folders and ids. The old ones point at the frames this
        /// action exists to replace, so re-optimize/continue/review would all reach for the starved set.</item>
        /// <item>It restores BOTH <see cref="LiveExposureSeconds"/> (which <see cref="RunLiveAttemptAsync"/> reads)
        /// and <c>capturedLiveExposureSeconds</c> (which it WRITES, before the sweep can fail) when the capture
        /// does not succeed. Restoring only the first leaves the intact summary's exposure row — and Accept's
        /// profile write-back — reporting an exposure nothing was ever captured at.</item>
        /// </list>
        ///
        /// <para><c>pendingDetectionBinning</c> is deliberately LEFT ALONE, so a prior "Optimize again at NxN"
        /// still applies: the fresh frames are analyzed at the factor the user is mid-way through evaluating, and
        /// Accept still writes that factor with the settings measured at it.
        /// <c>capturedRecoveryStepsPerSide</c> is likewise reused rather than re-read from
        /// <see cref="FocusRecoverySteps"/> — <see cref="LoadRunStampedAsync"/>'s contract is that every reload
        /// after the originating Start uses that Start's snapshot, so the widened capture and the recovery TAGGING
        /// cannot disagree.</para>
        ///
        /// <para>Persists nothing, like every other path here: Accept remains the only writer.</para>
        /// </summary>
        private async Task CaptureNewSweepAsync(CancellationToken externalToken) {
            if (!CanCaptureNewSweep) {
                return;
            }
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) {
                return; // a Start/re-optimize/continue/capture is already in flight
            }

            var target = RecaptureExposureSeconds;
            // The sweep exposure is what RunLiveAttemptAsync reads, so the confirmation quotes the change it is
            // about to make to it rather than the summary's (identical, but derived) recommendation row.
            var previousExposure = LiveExposureSeconds;
            var previousCapturedExposure = capturedLiveExposureSeconds;
            if (!confirmCaptureNewSweep(previousExposure, target)) {
                Interlocked.Exchange(ref running, 0);
                return;
            }
            LiveExposureSeconds = target;

            ErrorMessage = null;
            SetProgress(null, 0, 0);
            ProgressSeedJ = 0;
            ProgressBestJ = 0;
            ProgressSeedSigma = double.NaN;
            ProgressBestSigma = double.NaN;
            IsBusy = true;

            cts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            List<LoadedRun> captured = null;
            var succeeded = false;
            try {
                CurrentStep = WizardStep.Acquire;
                // The freshly captured folders/ids REPLACE the previous run's, in capture order, exactly as
                // AcquireAsync fills them on a Start — SnapshotReviewInputs below hands them on to every path that
                // re-reads this run from disk.
                //
                // Cleared inside the try with NO rollback, deliberately: nothing reads these two except
                // SnapshotReviewInputs (which only runs on the success path below), and both of their writers —
                // StartAsync and this method — clear before filling. So a capture that fails halfway leaves them
                // half-populated but unread, and the previous run's re-optimize/review inputs are unaffected
                // because those live in reoptimizeRunFolders/Ids, which only SnapshotReviewInputs writes.
                loadedRunFolders.Clear();
                loadedRunIds.Clear();
                captured = new List<LoadedRun>(RunCount);
                for (var i = 0; i < RunCount; i++) {
                    token.ThrowIfCancellationRequested();
                    var folder = await RunLiveAttemptAsync(token).ConfigureAwait(true);
                    if (string.IsNullOrEmpty(folder)) {
                        // Same clean message AcquireAsync gives, but the user stays on the SUMMARY: the previous
                        // run's result is still valid and still acceptable, which is the whole point of this being
                        // an optional extra capture rather than a re-Start.
                        ErrorMessage = "The live sweep did not produce a saved set of frames. Check the focuser and try again.";
                        CurrentStep = WizardStep.Summary;
                        return;
                    }
                    SetProgress("Loading frames", 0, 0);
                    var loadProgress = new Progress<RunLoadProgress>(rp =>
                        SetProgress("Loading frames", rp.Current, rp.Total));
                    // Load + stamp through the single choke-point: it carries the recovery snapshot AND any pending
                    // optimize-again binning factor onto the fresh runs.
                    var loaded = await LoadRunStampedAsync(folder, null, loadProgress, token).ConfigureAwait(true);
                    captured.Add(loaded);
                    loadedRunFolders.Add(folder);
                    loadedRunIds.Add(loaded.Data.RunId);
                }

                CurrentStep = WizardStep.Optimize;
                await AnalyzeWithProgressAsync(captured, r => r.Seed, token).ConfigureAwait(true);
                // The "before" is the user's current settings measured on THESE frames — the only honest
                // comparison, since the previous run's baseline was measured on differently-exposed ones.
                currentBaselineJ = await ComputeBaselineJAsync(captured, token).ConfigureAwait(true);

                var optimizeResult = await OptimizeAsync(captured, token).ConfigureAwait(true);
                var built = await BuildSummaryAsync(captured, optimizeResult, token).ConfigureAwait(true);

                // A fresh tuning on fresh frames, not another round: reset the chain rather than splicing a
                // trajectory across two different sets of exposures.
                AdoptFreshTuning(optimizeResult, built);
                // Flush the labels to disk BEFORE dropping them, and before SnapshotReviewInputs re-points the
                // labels dir at the run that is replacing this one. Order is load-bearing twice over: the discard
                // nulls ReviewVM, which is what actually writes (and what PersistReviewLabels guards on), so a
                // flush after it silently no-ops; and reviewLabelsDir still resolves to the OLD run's folder here,
                // which is where labels drawn on the OLD run's images belong. The principle is the Cancel path's:
                // labels are the user's work product, not a settings mutation, so they survive an action that
                // discards everything else. (Tolerant — a failed write logs and does not block the capture.)
                PersistReviewLabels();
                // …and, UNLIKE the binning path, throw away the review with it. That path re-reads the very same
                // files, so its RunIds — and therefore its labels — still line up. This one replaced the frames, so
                // the captured labels describe images that no longer exist here. Keeping them would leave
                // ShowReoptimizePrompt true on the new summary, and "Optimize with feedback" would then miss on
                // every runId and fall through to ResolveLabelsForRun's POSITIONAL fallback, applying star boxes
                // hand-drawn on the old sweep to the new sweep's frames behind nothing but a Logger.Warning.
                // Success path only: a failed or cancelled capture leaves the previous run — and its review —
                // entirely intact.
                DiscardReviewSnapshot();

                SnapshotReviewInputs(captured, loadedRunFolders, loadedRunIds);
                RecordRunDuration();
                CurrentStep = WizardStep.Summary;
                succeeded = true;
                Logger.Info($"Captured a new sweep at {target}s per frame and re-optimized on it (not yet applied; Accept writes the settings)");
            } catch (OperationCanceledException) {
                Logger.Info("Capture of a new sweep was cancelled");
                CurrentStep = WizardStep.Summary;
            } catch (Exception ex) {
                Logger.Error(ex, "Capturing a new sweep failed");
                ErrorMessage = $"Capturing a new sweep at {target}s failed: {ex.Message}";
                CurrentStep = WizardStep.Summary;
            } finally {
                if (!succeeded) {
                    // Cancelled or failed: put BOTH exposures back. LiveExposureSeconds is what a later sweep would
                    // capture at; capturedLiveExposureSeconds is what the (still-displayed, still-acceptable)
                    // summary reports and what Accept writes to the profile — and RunLiveAttemptAsync has already
                    // overwritten it by the time any sweep can fail. Nothing was persisted, so there is no other
                    // residue to undo.
                    LiveExposureSeconds = previousExposure;
                    capturedLiveExposureSeconds = previousCapturedExposure;
                    RaiseExposureRowChanged();
                }
                DisposeLoadedRuns(captured);
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
        /// real <see cref="StarDetector"/> and the SAME disk-load path the optimizer step itself uses (NINA's
        /// <see cref="IImageDataFactory"/> + <see cref="RenderedImageLoading.ForDetection"/>), so Review detects on
        /// the image the optimizer scored rather than on a differently-loaded copy of it.
        ///
        /// <para>This used to load a bare float Mat with <c>isBayered: false</c>, which for an OSC run meant Review
        /// detected and displayed the raw Bayer MOSAIC while the optimizer step ran on CFA-filtered luminance — and
        /// the labels a user draws here are what "Optimize with feedback" subsequently trusts.</para>
        /// </summary>
        /// <param name="cameraSensorType">The connected camera's sensor type, live's last-resort CFA source (see
        /// <see cref="RenderedImageLoading.ResolveBayerPattern"/>). Probed per call so connecting the camera between
        /// wizard steps is picked up.</param>
        private static Func<IReadOnlyList<FrameReviewDescriptor>, StarDetectorParams, IProgress<RunLoadProgress>, CancellationToken, Task<List<FrameReview>>>
            BuildProductionReviewBuilder(IProfileService profileService, IImageDataFactory imageDataFactory, Func<SensorType?> cameraSensorType) {
            return (descriptors, p, progress, token) => {
                var detector = new StarDetector(HocusFocusPlugin.AlglibAPI);
                // Hop to the threadpool so the WHOLE build (disk load + detection) runs off the captured UI context.
                // FrameReviewBuilder.BuildAsync awaits its work, so without this hop its synchronous prologue (and any
                // continuation that resumes on the UI SynchronizationContext) could stutter the UI thread. Progress
                // is posted via the IProgress the VM created on the UI context, so per-frame reports marshal back.
                return Task.Run(
                    () => FrameReviewBuilder.BuildAsync(
                        descriptors, p, detector,
                        // Detection: Detect(IRenderedImage, …), so the CFA hotpixel filter and the debayer happen
                        // INSIDE detection at the review's params — exactly as the live app does them.
                        path => LoadRenderedImageFromDisk(path, profileService, imageDataFactory, cameraSensorType, token),
                        // Display: the same debayer WITHOUT the CFA filter. A mosaic renders as a visible
                        // checkerboard, which is unreviewable, and the stretch is not a detection input. Straight
                        // off the image data, so a big OSC frame does not also build the Rgb48 debayered source
                        // this path would immediately discard.
                        async path => RenderedImageLoading.ToDebayeredLuminanceMat(
                            await LoadImageDataFromDisk(path, profileService, imageDataFactory, token).ConfigureAwait(false),
                            profileService, cameraSensorType?.Invoke()),
                        token, progress),
                    token);
            };
        }

        /// <summary>
        /// The DETECTION input for one review frame: loaded exactly as the optimizer step loads its run frames,
        /// then handed to <see cref="RenderedImageLoading.ForDetection"/> for the profile-gated debayer, so
        /// <c>Detect</c> — not this loader — decides whether to CFA-filter.
        /// </summary>
        private static async Task<IRenderedImage> LoadRenderedImageFromDisk(
            string path, IProfileService profileService, IImageDataFactory imageDataFactory, Func<SensorType?> cameraSensorType, CancellationToken token) {
            var imageData = await LoadImageDataFromDisk(path, profileService, imageDataFactory, token).ConfigureAwait(false);
            return RenderedImageLoading.ForDetection(imageData, profileService, cameraSensorType?.Invoke());
        }

        /// <summary>
        /// The disk read behind both review loaders: NINA's image-data factory with the frame's REAL
        /// <c>isBayered</c> + bit depth recovered from the AF engine's file name. A file that is not an AF-engine
        /// saved frame falls back to not-bayered at the profile's bit depth — what the previous loader assumed for
        /// every file.
        /// </summary>
        private static Task<IImageData> LoadImageDataFromDisk(
            string path, IProfileService profileService, IImageDataFactory imageDataFactory, CancellationToken token) {
            var saved = SavedAutoFocusImage.TryParseFileName(path);
            var bitDepth = saved?.BitDepth ?? (int)profileService.ActiveProfile.CameraSettings.BitDepth;
            return imageDataFactory.CreateFromFile(
                path, bitDepth, saved?.IsBayered ?? false,
                profileService.ActiveProfile.CameraSettings.RawConverter, token);
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
            elapsedTimer?.Stop();
            elapsedTimer = null;
            runStopwatch.Stop();
            cts?.Dispose();
            cts = null;
        }

        private void Back() {
            if (CurrentStep == WizardStep.Summary && !IsBusy) {
                // Leaving the summary abandons any un-accepted optimize-again factor, so the confirmation panel
                // and a subsequent Start both go back to the persisted value.
                pendingDetectionBinning = null;
                RaiseSweepDetectionBinningChanged();
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

        /// <summary>Live confirmation panel: pick the folder the sweep saves to, and persist it back to the AF options
        /// so the choice sticks across launches. Reuses the same injected folder picker as the replay browser.</summary>
        private void BrowseSaveFolder() {
            var picked = folderPicker();
            if (!string.IsNullOrWhiteSpace(picked)) {
                SaveFolderPath = picked;
                if (autoFocusOptions != null) {
                    autoFocusOptions.SavePath = picked;
                }
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

        /// <summary>The filter the live sweep will expose through, for display: the designated AF filter when
        /// filter-wheel offsets are enabled, otherwise the filter currently loaded in the wheel. Null when unknown.</summary>
        private static string ResolveSweepFilterName(IProfileService profileService, IFilterWheelMediator filterWheelMediator) {
            var focuserSettings = profileService?.ActiveProfile?.FocuserSettings;
            if (focuserSettings != null && focuserSettings.UseFilterWheelOffsets) {
                var af = profileService.ActiveProfile.FilterWheelSettings?.FilterWheelFilters?.FirstOrDefault(f => f.AutoFocusFilter);
                if (af != null) {
                    return af.Name;
                }
            }
            return filterWheelMediator?.GetInfo()?.SelectedFilter?.Name;
        }

        /// <summary>The gain the live sweep will expose with, for display: the designated AF filter's gain when
        /// offsets are enabled and it sets one, otherwise the connected camera's current gain. Null when the camera
        /// isn't connected (the engine has no gain override — see AutoFocusEngine.TakeExposure).</summary>
        private static int? ResolveSweepGain(IProfileService profileService, IFilterWheelMediator filterWheelMediator, ICameraMediator cameraMediator) {
            var focuserSettings = profileService?.ActiveProfile?.FocuserSettings;
            if (focuserSettings != null && focuserSettings.UseFilterWheelOffsets) {
                var af = profileService.ActiveProfile.FilterWheelSettings?.FilterWheelFilters?.FirstOrDefault(f => f.AutoFocusFilter);
                if (af != null && af.AutoFocusGain > -1) {
                    return af.AutoFocusGain;
                }
            }
            var cameraInfo = cameraMediator?.GetInfo();
            return (cameraInfo != null && cameraInfo.Connected) ? cameraInfo.Gain : (int?)null;
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
