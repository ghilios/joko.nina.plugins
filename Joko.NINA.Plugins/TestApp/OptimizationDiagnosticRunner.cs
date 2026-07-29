#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;
using Rect = OpenCvSharp.Rect;

namespace TestApp {

    /// <summary>
    /// Headless dev harness for the Star Detection Optimization wizard's optimizer (T6). Loads one or more saved
    /// auto-focus runs from disk as float Mats, drives the SAME <see cref="StarDetectionOptimizer"/> the live
    /// wizard uses (over an injected <see cref="StarDetector.Detect(Mat, StarDetectorParams, IProgress{NINA.Core.Model.ApplicationStatus}, CancellationToken)"/>
    /// delegate), and writes a summary + CSV + stretched annotated PNGs so the optimizer can be verified offline
    /// without launching NINA. Mirrors <see cref="ContaminationDiagnosticRunner"/> in structure and conventions.
    ///
    /// <para>This is image-source-agnostic by design: the optimizer's <see cref="RunEvaluationData"/> takes an
    /// opaque image payload and an injected detection delegate, so the offline harness feeds it float Mats while
    /// the live wizard feeds it <c>IRenderedImage</c>s — the optimizer code is identical in both.</para>
    /// </summary>
    internal static class OptimizationDiagnosticRunner {

        // Run discovery (attempt-anchored, >=3-position guard) lives in the pure, unit-testable
        // OptimizationRunDiscovery helper. Frame matching uses its ImageFileRegex (kept as the single copy).

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Optimization diagnostic run failed");
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunImpl(string[] args) {
            var runsDir = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runsDir)) {
                PrintUsage();
                Environment.ExitCode = 2;
                return;
            }
            if (!Directory.Exists(runsDir)) {
                throw new DirectoryNotFoundException($"--runs folder not found: {runsDir}");
            }

            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "optimize",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            int? maxEvals = null;
            var maxEvalsArg = DiagnosticUtil.GetArg(args, "--max-evals");
            if (!string.IsNullOrWhiteSpace(maxEvalsArg)) {
                if (!int.TryParse(maxEvalsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var me) || me <= 0) {
                    throw new ArgumentException($"--max-evals: '{maxEvalsArg}' is not a positive integer");
                }
                maxEvals = me;
            }

            var annotateArg = DiagnosticUtil.GetArg(args, "--annotate");
            bool annotateAll = false;
            if (!string.IsNullOrWhiteSpace(annotateArg)) {
                if (annotateArg.Equals("all", StringComparison.OrdinalIgnoreCase)) {
                    annotateAll = true;
                } else if (annotateArg.Equals("extremes", StringComparison.OrdinalIgnoreCase)) {
                    annotateAll = false;
                } else {
                    throw new ArgumentException($"--annotate: '{annotateArg}' must be 'extremes' or 'all'");
                }
            }

            // --donut forces the DefocusAwareDonutDetection MASTER on for this headless run (the profile option is
            // read-only here), so the optimizer explores the donut-recovery + spike-suppression axes. Without it the
            // master follows the profile (default OFF) and no defocus axis is searched — for A/B before/after.
            bool forceDonut = DiagnosticUtil.HasFlag(args, "--donut");

            // --inspection mirrors the wizard's "Optimize for aberration inspection" toggle: swap in the
            // star-favoring objective (ObjectiveConstants.ForAberrationInspection) bounded relative to the current
            // settings' σ. Off ⇒ the standard objective (bit-identical to before).
            bool inspection = DiagnosticUtil.HasFlag(args, "--inspection");

            // --legacy-objective disables BOTH new behaviors for an A/B "before" pass from one binary: it zeroes the
            // extreme-HFR outlier penalty (HfrOutlierStrength=0) and the region-coverage reward (Wcov=0) in the
            // objective, AND forces the detector's saturated-star HFR exclusion off on the seed/baseline params. The
            // result is the pre-change objective + detection. Default (no flag) = the new behavior (the "after" pass).
            bool legacyObjective = DiagnosticUtil.HasFlag(args, "--legacy-objective");

            // --continue-rounds <0-2> mirrors the wizard's "Continue optimizing" button: after the first optimize,
            // re-seed from the prior best and run again (fresh curated set / step scale) up to this many more times
            // (3 passes total). Validates the chaining headlessly; per-round J is reported.
            int continueRounds = 0;
            var continueRoundsArg = DiagnosticUtil.GetArg(args, "--continue-rounds");
            if (!string.IsNullOrWhiteSpace(continueRoundsArg)) {
                if (!int.TryParse(continueRoundsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out continueRounds) || continueRounds < 0 || continueRounds > 2) {
                    throw new ArgumentException($"--continue-rounds: '{continueRoundsArg}' must be an integer in [0, 2]");
                }
            }

            var labelsDir = DiagnosticUtil.GetArg(args, "--labels");

            // --per-run is a valueless flag: optimize each discovered run INDEPENDENTLY (one optimization, one
            // evaluator, one output subfolder, one hard-floor assertion per run) instead of jointly. Use this
            // when --runs points at a bank of DIFFERENT optical setups, where a joint (N>1 balanced) objective
            // across incompatible cameras/scopes is meaningless.
            bool perRun = args.Any(a => string.Equals(a, "--per-run", StringComparison.OrdinalIgnoreCase));

            // --verbose is a valueless flag that restores TRACE logging for offline inspection. By default the
            // optimize harness runs at INFO so the detector's thousands of per-detection Logger.Trace stage-timing
            // lines (LoadImage/SrcImagePreparation/WaveletCalculation/...) short-circuit instead of serializing to
            // disk every detection — pure wall-clock overhead during a run of thousands of detections. This is a
            // logging-only change: detection inputs, params, gates, and results are untouched.
            bool verbose = args.Any(a => string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase));
            Logger.SetLogLevel(verbose ? LogLevelEnum.TRACE : LogLevelEnum.INFO);
            if (verbose) {
                Console.WriteLine("Verbose logging enabled (TRACE).");
            }

            // The real ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a
            // (non-running) WPF Application must exist or it NREs (mirrors ContaminationDiagnosticRunner).
            if (Application.Current == null) {
                new Application();
            }

            Console.WriteLine($"Runs root: {runsDir}");
            Console.WriteLine($"Output: {outDir}");

            // Load the user's real profile + star detection settings (single source of truth for params + PixelScale).
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile;
            if (activeProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA at least once to create a profile.");
            }
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");
            Logger.Info($"Loaded profile {activeProfile.Name} ({activeProfile.Id})");

            // Detector settings come from the harness's LOCAL settings file, never the NINA profile: a
            // profile-sourced seed is mutable machine state nothing records, and TryLoad("") picks whichever
            // profile is ACTIVE -- two runs of the same data minutes apart were seeded from different telescopes.
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, activeProfile);
            var accessor = harnessSettings.Accessor;
            var starDetectionOptions = new StarDetectionOptions(profileService, accessor);
            var afOptions = new AutoFocusOptions(profileService);

            // PixelScale = arcsec/pixel from the profile (pixel size / focal length) × binning, mirroring
            // HocusFocusStarDetection.GetStarDetectorParams. Production reads binning from per-frame metadata
            // (image.RawImageData.MetaData.Camera.BinX, which defaults to 1 when unset); the harness loads raw
            // float Mats with no NINA metadata, so binning = 1 is used (the same value an unbinned/unset frame
            // yields in production). PixelScale only feeds PixelScale-dependent detection gates, not the curve fit.
            // PixelScale is resolved PER RUN from each run's own frame headers (see PrepareRunAsync). The bank is
            // other people's data -- true scales span 0.277-5.966 arcsec/px -- so one harness-wide value taken from
            // the local profile was wrong for nearly every run. This is only the fallback for callers that never
            // load a frame; every detecting path overwrites it from the frame header.
            const int binning = 1;
            var pixelScale = MathUtility.ArcsecPerPixel(harnessSettings.PixelSizeMicrons, harnessSettings.FocalLengthMm) * binning;

            // Mirror the wizard's seed/baseline split (optimizer default seed + current-settings baseline):
            //  - Seed = fully-DEFAULT params (BuildDefaultStarDetectorParams) — the bundle the optimizer STARTS from,
            //    exactly like the wizard's LoadedRun.Seed, so the headless search reproduces the live result.
            //  - Baseline = the user's CURRENT settings (BuildStarDetectorParams) — the "before" that improvement is
            //    measured against (σ, J, curated-param deltas), exactly like the wizard's LoadedRun.Baseline.
            // Both carry the same AF image-context overrides GetStarDetectorParams / GetDefaultStarDetectorParams apply
            // (PixelScale, Region=Full, ModelPSF=false, no intermediate files). Neither writes to the profile.
            StarDetectorParams ApplyAfContext(StarDetectorParams p) {
                p.PixelScale = pixelScale;
                p.Region = StarDetectionRegion.Full;
                p.ModelPSF = false;
                p.SaveIntermediateFilesPath = string.Empty;
                // Mirrors RunEvaluationLoader: every detection here is an OPTIMIZER evaluation (thousands per run),
                // and the plugin's BuildStarDetectionResult logs a per-region "Average HFR" INFO line for each. The
                // flag is output-neutral and excluded from the detection cache key, so results stay byte-identical;
                // it only stops one optimization from emitting ~10k+ INFO lines. Candidates inherit it via Clone.
                p.SuppressInfoLogging = true;
                return p;
            }
            var baseline = ApplyAfContext(HocusFocusStarDetection.BuildStarDetectorParams(starDetectionOptions));
            // --start-from-current mirrors the wizard's "Start from my current settings" toggle: the optimizer SEED
            // becomes the user's current settings (Baseline) instead of the fully-default params, so the never-regress
            // floor is the current J and the search refines from the current basin (it can only improve on current).
            bool startFromCurrent = DiagnosticUtil.HasFlag(args, "--start-from-current");
            var seed = startFromCurrent
                ? ApplyAfContext(HocusFocusStarDetection.BuildStarDetectorParams(starDetectionOptions))
                : ApplyAfContext(HocusFocusStarDetection.BuildDefaultStarDetectorParams());
            if (forceDonut) {
                seed.DefocusAwareDonutDetection = true;
                baseline.DefocusAwareDonutDetection = true;
                Console.WriteLine("--donut: DefocusAwareDonutDetection forced ON (optimizer will explore the donut/spike axes)");
            }
            if (startFromCurrent) {
                Console.WriteLine("--start-from-current: optimizer seed = current settings (never regresses below current)");
            }
            if (legacyObjective) {
                // Force the detector-side saturated-star HFR exclusion OFF on both seed and baseline so the harness
                // aggregates HFR exactly as before. The objective-side terms are zeroed where objectiveConstants is built.
                seed.ExcludeSaturatedStarsFromHFR = false;
                baseline.ExcludeSaturatedStarsFromHFR = false;
                Console.WriteLine("--legacy-objective: HFR-outlier penalty + coverage reward OFF, saturated-HFR exclusion OFF (pre-change A/B 'before')");
            }

            Console.WriteLine($"Default seed params: Sensitivity={F(seed.Sensitivity)}, StarClippingMultiplier={F(seed.StarClippingMultiplier)}, " +
                $"NoiseClippingMultiplier={F(seed.NoiseClippingMultiplier)}, StructureLayers={seed.StructureLayers}, PixelScale={F(seed.PixelScale)}");
            Console.WriteLine($"Current (baseline) params: Sensitivity={F(baseline.Sensitivity)}, StarClippingMultiplier={F(baseline.StarClippingMultiplier)}, " +
                $"NoiseClippingMultiplier={F(baseline.NoiseClippingMultiplier)}, StructureLayers={baseline.StructureLayers}, " +
                $"MeasurementAverage={starDetectionOptions.MeasurementAverage}");

            // The fixed AF-detection sigma rejections the wizard's RunEvaluationLoader uses (the
            // HocusFocusDetectionParams class defaults, NOT the live AF path): high = 4.0, low = 3.0. These feed
            // the MeanOutliers HFR aggregation only.
            const double highSigmaOutlierRejection = 4.0;
            const double lowSigmaOutlierRejection = 3.0;

            // Discover runs: attempt-anchored recursive search with the >=3-position guard (excludes the
            // single-frame final/initial validation captures), with back-compat (--runs is itself an attempt
            // folder) and a fallback (no attempt* folders anywhere). Pure logic lives in OptimizationRunDiscovery.
            var discovery = OptimizationRunDiscovery.Discover(runsDir);
            foreach (var d in discovery.Runs) {
                Console.WriteLine($"  run '{d.RunId}': {d.Frames.Count} frames, {d.DistinctPositions} focuser position(s)");
                Logger.Info($"Discovered run '{d.RunId}': {d.Frames.Count} frames, {d.DistinctPositions} focuser position(s)");
            }
            foreach (var s in discovery.Skipped) {
                Console.WriteLine($"  skipped '{s.RelativePath}': {s.Reason}");
                Logger.Info($"Skipped '{s.RelativePath}': {s.Reason}");
            }
            Console.WriteLine($"Discovered {discovery.Runs.Count} runs (skipped {discovery.Skipped.Count} folders)");
            Logger.Info($"Discovered {discovery.Runs.Count} runs (skipped {discovery.Skipped.Count} folders)");
            if (discovery.Runs.Count == 0) {
                throw new InvalidOperationException(
                    $"No usable AF runs (>= {OptimizationRunDiscovery.MinPositionsForFit} focuser positions) were found under {runsDir}. " +
                    "Expected files like '0_Frame1_BitDepth16_Bayered0_Focuser5000.fits' in an 'attempt*' folder (or directly in --runs).");
            }

            var labelsByRun = LoadLabels(labelsDir, discovery.Runs);

            var alglibAPI = new AlglibAPI();
            var detector = new StarDetector(alglibAPI);
            // The plugin's OWN detection facade, so the optimizer drives the same RunEvaluationLoader split detector
            // the wizard does instead of a harness mirror of its FrameDetectionResult mapping. Only GetInfo() and the
            // inner StarDetector are exercised on the split path; the remaining dependencies are headless stubs (see
            // StubFocuserMediator / StubPerFilterStarDetectionStore), and imageStatisticsVM is never touched.
            var detection = new HocusFocusStarDetection(
                imageStatisticsVM: null,
                profileService: profileService,
                focuserMediator: new StubFocuserMediator(),
                starDetectionOptions: starDetectionOptions,
                alglibAPI: alglibAPI,
                perFilterStore: new StubPerFilterStarDetectionStore(accessor));
            var ctx = new RunDetectionContext {
                ProfileService = profileService,
                HarnessSettings = harnessSettings,
                AfOptions = afOptions,
                AlglibAPI = alglibAPI,
                Detector = detector,
                Detection = detection,
                MeasurementAverage = starDetectionOptions.MeasurementAverage,
                HighSigmaOutlierRejection = highSigmaOutlierRejection,
                LowSigmaOutlierRejection = lowSigmaOutlierRejection,
                Seed = seed,
                Baseline = baseline,
                Variables = OptimizerVariable.CreateCuratedSet(seed),
                MaxEvals = maxEvals,
                LabelsDir = labelsDir,
                AnnotateAll = annotateAll,
                Inspection = inspection,
                LegacyObjective = legacyObjective,
                ContinueRounds = continueRounds
            };
            if (inspection) {
                Console.WriteLine("--inspection: aberration-inspection objective (favor more stars, fit bounded vs current σ)");
            }
            if (continueRounds > 0) {
                Console.WriteLine($"--continue-rounds: {continueRounds} extra pass(es) after the first ({continueRounds + 1} total)");
            }

            if (perRun) {
                await RunPerRun(ctx, runsDir, outDir, discovery.Runs, labelsByRun).ConfigureAwait(false);
            } else {
                await RunJoint(ctx, runsDir, outDir, discovery.Runs, labelsByRun).ConfigureAwait(false);
            }
        }

        /// <summary>Shared, run-independent dependencies + tuning settings threaded through the optimize helpers.</summary>
        private sealed class RunDetectionContext {
            public ProfileService ProfileService;
            public AutoFocusOptions AfOptions;
            public AlglibAPI AlglibAPI;
            public StarDetector Detector;

            /// <summary>The plugin's detection facade — the optimizer's split detector is the wizard's own
            /// <c>HocusFocusSplitFrameDetector</c> driven through this, so the two can never drift.</summary>
            public IHocusFocusStarDetection Detection;

            public MeasurementAverageEnum MeasurementAverage;
            public double HighSigmaOutlierRejection;
            public double LowSigmaOutlierRejection;
            public StarDetectorParams Seed;       // optimizer start = fully-default params (wizard's LoadedRun.Seed)
            public StarDetectorParams Baseline;    // current settings = displayed "before" (wizard's LoadedRun.Baseline)
            public HarnessSettingsStore.Resolved HarnessSettings;  // local settings file; fallback for PixelScale
            public IReadOnlyList<OptimizerVariable> Variables;
            public int? MaxEvals;
            public string LabelsDir;
            public bool AnnotateAll;
            public bool Inspection;     // --inspection: use the aberration-inspection objective
            public bool LegacyObjective; // --legacy-objective: zero the HFR-outlier penalty + coverage reward (A/B "before")
            public int ContinueRounds;  // --continue-rounds: extra chained passes after the first (0-2)
        }

        /// <summary>Outcome of optimizing one set of runs (joint or a single per-run), for the aggregate report.</summary>
        private sealed class RunSetOutcome {
            public bool HardFloorPassed;
            public int WorstFrameCount;
            public string WorstRunId;
            public OptimizationResult Result;
            public double BaselineJ = double.NaN;   // J of the user's CURRENT settings (the displayed "before")
            public List<RunEvaluationResult> PerRunBaseline;   // per-run eval of CURRENT settings (the "before" columns)
            public List<RunEvaluationResult> PerRunBest;
            public List<LoadedHarnessRun> LoadedRuns;
            public ObjectiveConstants ObjectiveConstants; // the SAME constants the runs were scored with (ExposureRecommender needs NTarget)
        }

        // ---- Joint mode (default): optimize ALL discovered runs together (N=1 reduces; N>1 is the balanced blend).

        private static async Task RunJoint(
            RunDetectionContext ctx, string runsDir, string outDir,
            List<OptimizationRunDiscovery.DiscoveredRun> discovered,
            Dictionary<string, IReadOnlyList<FrameLabels>> labelsByRun) {
            var loadedRuns = new List<LoadedHarnessRun>(discovered.Count);
            try {
                foreach (var d in discovered) {
                    loadedRuns.Add(await PrepareRunAsync(ctx, d, labelsByRun).ConfigureAwait(false));
                }

                Console.WriteLine($"Optimizing JOINTLY over {loadedRuns.Count} run(s) (MaxEvaluations=" +
                    $"{(ctx.MaxEvals?.ToString(CultureInfo.InvariantCulture) ?? "default")}, {(ctx.LabelsDir != null ? "labeled" : "unlabeled")})...");
                var outcome = await OptimizeRunSetAsync(ctx, runsDir, outDir, loadedRuns).ConfigureAwait(false);
                if (!outcome.HardFloorPassed) {
                    Environment.ExitCode = 3;
                }
                Console.WriteLine($"Wrote optimize_summary.txt, optimize_result.csv, and annotated PNG(s) to {outDir}");
            } finally {
                DisposeRuns(loadedRuns);
            }
        }

        // ---- Per-run mode: optimize each discovered run INDEPENDENTLY, one output subfolder each, plus an
        //      aggregate_summary.txt at the top level. One bad run is logged and recorded as failed; the batch
        //      continues.

        private static async Task RunPerRun(
            RunDetectionContext ctx, string runsDir, string outDir,
            List<OptimizationRunDiscovery.DiscoveredRun> discovered,
            Dictionary<string, IReadOnlyList<FrameLabels>> labelsByRun) {
            var aggregate = new List<AggregateRow>(discovered.Count);
            bool anyFailure = false;
            for (int idx = 0; idx < discovered.Count; idx++) {
                var d = discovered[idx];
                Console.WriteLine($"[{idx + 1}/{discovered.Count}] optimizing {d.RunId} ...");
                var subDir = Path.Combine(outDir, OptimizationRunDiscovery.SanitizeForFileName(d.RunId));
                var loadedRuns = new List<LoadedHarnessRun>(1);
                try {
                    var loaded = await PrepareRunAsync(ctx, d, labelsByRun).ConfigureAwait(false);
                    loadedRuns.Add(loaded);
                    // PixelScale from THIS run's frames. Per-run mode optimizes each run independently, so each
                    // carries the scale its data actually has. Joint mode cannot express this (one bundle, many scales).
                    if (double.IsFinite(loaded.PixelScale)) {
                        ctx.Seed.PixelScale = loaded.PixelScale;
                        ctx.Baseline.PixelScale = loaded.PixelScale;
                    }
                    Console.WriteLine($"  {d.RunId}: PixelScale {F(ctx.Seed.PixelScale)} arcsec/px ({loaded.PixelScaleSource})");
                    // Per-dataset settings, derived from THIS run and recorded beside it.
                    var runFolder = Path.GetDirectoryName(d.Frames.First().Path);
                    HarnessSettingsStore.ResolveForRun(
                        runFolder, ctx.HarnessSettings, loaded.FirstFrameMeta,
                        HarnessSettingsStore.ReadInFocusHfr(runFolder));
                    Directory.CreateDirectory(subDir);
                    var outcome = await OptimizeRunSetAsync(ctx, runsDir, subDir, loadedRuns).ConfigureAwait(false);
                    if (!outcome.HardFloorPassed) {
                        anyFailure = true;
                    }
                    var row = BuildAggregateRow(d.RunId, ctx, outcome);
                    aggregate.Add(row);
                    Console.WriteLine($"  -> {d.RunId}: currentJ={F(outcome.BaselineJ)} bestJ={F(outcome.Result.BestJ)} " +
                        $"hard-floor {(outcome.HardFloorPassed ? "PASS" : "FAIL")}; outputs in {subDir}");
                    Console.WriteLine(row.SensitivityIsAtFloor && row.ExposureRecommendation?.HasRecommendation == true
                        ? $"     sensitivity={F(row.BrightnessSensitivity)} (AT FLOOR): exposure rec {F(row.ExposureRecommendation.CurrentSeconds)}s -> {F(row.ExposureRecommendation.RecommendedSeconds)}s"
                        : row.SensitivityIsAtFloor
                            ? $"     sensitivity={F(row.BrightnessSensitivity)} (AT FLOOR): no exposure recommendation (insufficient data)"
                            : $"     sensitivity={F(row.BrightnessSensitivity)} (not at floor)");
                } catch (Exception ex) {
                    anyFailure = true;
                    Console.Error.WriteLine($"  -> {d.RunId}: FAILED to optimize ({ex.Message}); continuing to next run");
                    Logger.Error(ex, $"Per-run optimization failed for '{d.RunId}'");
                    aggregate.Add(new AggregateRow {
                        RunId = d.RunId,
                        LoadOk = false,
                        Error = ex.Message
                    });
                } finally {
                    DisposeRuns(loadedRuns);
                }
            }

            WriteAggregateSummary(Path.Combine(outDir, "aggregate_summary.txt"), runsDir, ctx, aggregate);
            // JSON twin of aggregate_summary.txt (same AggregateRow data, including the exposure recommendation) for
            // scripted consumption -- e.g. checking ExposureRecommendation.RecommendedSeconds across a bank without
            // parsing the text report.
            File.WriteAllText(Path.Combine(outDir, "aggregate_summary.json"), JsonConvert.SerializeObject(aggregate, Formatting.Indented));
            Console.WriteLine($"Per-run batch complete: {aggregate.Count(r => r.LoadOk)} optimized, " +
                $"{aggregate.Count(r => !r.LoadOk)} failed. Wrote aggregate_summary.txt and aggregate_summary.json to {outDir}");
            if (anyFailure) {
                Environment.ExitCode = 3;
            }
        }

        /// <summary>
        /// Loads a discovered run's frames as IRenderedImages once, wires the plugin's split detector, infers the
        /// fit config, and builds its <see cref="RunEvaluationData"/>. Caller owns disposing the returned run via
        /// <see cref="DisposeRuns"/>, which frees BOTH the <see cref="RunEvaluationData"/>'s cached early-detection
        /// contexts and the loaded-once frame images.
        /// </summary>


        private static async Task<LoadedHarnessRun> PrepareRunAsync(
            RunDetectionContext ctx, OptimizationRunDiscovery.DiscoveredRun d,
            Dictionary<string, IReadOnlyList<FrameLabels>> labelsByRun) {
            var frames = new List<RunFrame>(d.Frames.Count);
            var runPixelScale = double.NaN;
            var pixelScaleSource = "unset";
            NINA.Image.ImageData.ImageMetaData firstFrameMeta = null;
            // Captured exposure, in seconds, that the run's frames were actually shot with — read off the FIRST
            // frame's header only (an AF sweep exposes every point identically, mirroring
            // RunEvaluationLoader.LoadedRun.CapturedExposureSeconds's "first frame only" convention). NaN when the
            // header carries no exposure keyword; ExposureRecommender.Recommend treats a non-positive/NaN exposure
            // as "no recommendation", never a guess. Taken off the rendered image this loop already loads, so the
            // exposure costs no extra read and comes from the same header the detector's frame does.
            var capturedExposureSeconds = double.NaN;
            for (var i = 0; i < d.Frames.Count; i++) {
                var frame = d.Frames[i];
                // The IRenderedImage the LIVE app detects on (see DiagnosticUtil.LoadRenderedImage). Detection reads
                // it without mutating it — Detect/BuildDetectionContext build their own source Mat per call — so one
                // load per frame serves every candidate evaluation. NOTHING is CFA-filtered here: the filter and the
                // debayer belong inside Detect, at each candidate's own HotpixelThreshold/HotpixelThresholdingEnabled,
                // which is what keeps those two searched axes meaningful.
                var rendered = await DiagnosticUtil.LoadRenderedImage(frame.Path, ctx.ProfileService).ConfigureAwait(false);
                if (i == 0) {
                    firstFrameMeta = rendered.RawImageData?.MetaData;
                    runPixelScale = HarnessSettingsStore.PixelScaleForFrame(firstFrameMeta, ctx.HarnessSettings, out pixelScaleSource);
                    capturedExposureSeconds = firstFrameMeta?.Image?.ExposureTime ?? double.NaN;
                }
                frames.Add(new RunFrame {
                    FrameId = frame.Path,
                    FocuserPosition = frame.FocuserPosition,
                    Image = rendered
                });
            }

            var stepSize = InferStepSize(d.Frames);
            var fitConfig = new RunFitConfig {
                StepSize = stepSize,
                UseWeights = ctx.AfOptions.WeightedHyperbolicFitEnabled,
                MaxOutlierRejections = ctx.AfOptions.MaxOutlierRejections,
                RejectionConfidence = ctx.AfOptions.OutlierRejectionConfidence,
                // Was hard-coded null while the wizard's loader passes afOptions.HyperbolicFitModel. Currently
                // informational (the evaluator always runs the Hybrid best-fit selection), but a silent divergence
                // from the wizard is exactly what this work exists to remove.
                PreferredModel = ctx.AfOptions.HyperbolicFitModel
            };

            // Split detector: the WIZARD'S OWN HocusFocusSplitFrameDetector (RunEvaluationLoader), not a harness
            // mirror of it. It caches the expensive early detection per (frame, early key) and reuses it across the
            // many candidate evaluations that change only late-stage gate params (the ~2× speedup), and its
            // FrameDetectionResult mapping is by definition the one the wizard uses. hocusParams mirrors the loader's:
            // IsAutoFocus with NumberOfAFStars = 0, so every accepted star is scored (no brightest-N trim) and the
            // sigma rejections stay at the HocusFocusDetectionParams class defaults (high 4.0 / low 3.0).
            var splitDetector = new RunEvaluationLoader.HocusFocusSplitFrameDetector(
                ctx.Detection, new HocusFocusDetectionParams { IsAutoFocus = true, NumberOfAFStars = 0 });

            var labels = labelsByRun.TryGetValue(d.RunId, out var ls) ? ls : null;
            var data = new RunEvaluationData(d.RunId, frames, splitDetector, ctx.AlglibAPI, fitConfig, labels);
            var exposureForLog = double.IsFinite(capturedExposureSeconds) ? $"{F(capturedExposureSeconds)}s" : "unrecorded";
            Console.WriteLine($"  {d.RunId}: inferred step size {stepSize}, exposure {exposureForLog}" + (labels != null ? $", {labels.Count} labeled position(s)" : ", unlabeled"));
            return new LoadedHarnessRun { Discovered = d, Data = data, StepSize = stepSize, Frames = frames,
                PixelScale = runPixelScale, PixelScaleSource = pixelScaleSource, FirstFrameMeta = firstFrameMeta,
                CapturedExposureSeconds = capturedExposureSeconds };
        }

        /// <summary>
        /// Optimizes a SET of loaded runs together (1 run = N=1; several = the balanced N>1 blend), evaluates the
        /// seed + winner against each run, runs the hard-floor assertion, and writes the per-set summary, CSV, and
        /// annotated PNGs into <paramref name="targetDir"/>. Returns the outcome for the aggregate report.
        /// </summary>
        private static async Task<RunSetOutcome> OptimizeRunSetAsync(
            RunDetectionContext ctx, string runsDir, string targetDir, List<LoadedHarnessRun> loadedRuns) {
            var settings = new OptimizerSettings();
            if (ctx.MaxEvals.HasValue) {
                settings.MaxEvaluations = ctx.MaxEvals.Value;
            }

            var variables = ctx.Variables;

            var dataList = loadedRuns.Select(r => r.Data).ToList();
            var evaluator = RunEvaluationData.CreateEvaluator(dataList);

            // "Before" = the user's CURRENT settings (ctx.Baseline) — the wizard's displayed baseline, NOT the default
            // seed the optimizer started from. Evaluate it FIRST so it yields BOTH the current-settings σ (the
            // aberration-inspection fit-guard anchor) and the baselineJ (the SAME JTotal the optimizer uses, over the
            // single baseline bundle across runs), keeping every console/report number "vs current" — consistent with
            // the final summary and the live wizard.
            var perRunBaseline = new List<RunEvaluationResult>(loadedRuns.Count);
            foreach (var r in loadedRuns) {
                perRunBaseline.Add(await r.Data.EvaluateAndFitAsync(ctx.Baseline, CancellationToken.None).ConfigureAwait(false));
            }

            // Finalize the objective: aberration-inspection (reweighted toward stars, fit bounded relative to the
            // current-settings σ) when --inspection, else standard. The wizard builds these exact constants in
            // ComputeBaselineJAsync; mirroring that here keeps the headless harness authoritative.
            var baselineSigmas = perRunBaseline.Select(pr => pr.Metrics.SigmaFocus).Where(s => double.IsFinite(s)).ToList();
            var currentSigma = baselineSigmas.Count > 0 ? baselineSigmas.Average() : double.NaN;
            var objectiveConstants = ctx.Inspection
                ? ObjectiveConstants.ForAberrationInspection(currentSigma)
                : new ObjectiveConstants();
            if (ctx.LegacyObjective) {
                // A/B "before": disable the extreme-HFR outlier penalty and the region-coverage reward so the
                // objective is exactly the pre-change one (HfrOutlierStrength=0 => SHfrOutlier==1; Wcov=0 => coverage
                // excluded from the weighted sum).
                objectiveConstants.HfrOutlierStrength = 0.0;
                objectiveConstants.Wcov = 0.0;
            }
            var optimizer = new StarDetectionOptimizer(objectiveConstants);

            var baselineJ = OptimizationObjective.JTotal(
                perRunBaseline.Select(pr => OptimizationObjective.JRun(pr.Metrics, objectiveConstants)).ToList(), objectiveConstants);
            Console.WriteLine($"Current settings J: {F(baselineJ)}");
            if (ctx.Inspection) {
                Console.WriteLine($"  inspection objective: reference σ = {F(currentSigma)} (current settings), margin = {F(objectiveConstants.FitGuardMarginFraction)}");
            }

            // Trajectory capture (eval-budget analysis): the optimizer reports on the seed + every accepted move, so
            // these rows are the bestJ-vs-evals staircase. Written to optimize_trajectory.csv; analyzed offline to
            // find the smallest eval count reaching finalBestJ·(1−relTol) — the data backing an eval-budget cut.
            var trajectory = new List<(int Evaluations, double BestJ, string Phase)>();
            var progress = new Progress<OptimizationProgress>(op => {
                trajectory.Add((op.Evaluations, op.BestJ, op.Phase));
                Console.WriteLine($"  [{op.Phase}] evals={op.Evaluations}/{op.MaxEvaluations} bestJ={F(op.BestJ)} currentJ={F(baselineJ)}");
            });

            // Snapshot the cache counters so the readout below measures the OPTIMIZE phase only (the baseline eval above
            // already warmed some early contexts; counting its builds would understate the optimizer's own reuse).
            var buildsBefore = dataList.Sum(d => d.ContextBuilds);
            var reusesBefore = dataList.Sum(d => d.ContextReuses);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await optimizer.OptimizeAsync(ctx.Seed, variables, evaluator, settings, progress, CancellationToken.None).ConfigureAwait(false);

            // --continue-rounds: chain additional passes, each re-seeded from the prior best with a FRESH curated set
            // (resets the pattern-search step scale, so it can make larger moves again — the point of "Continue").
            // Mirrors the wizard's Continue button (latest replaces Optimized); per-round J reported. Never regresses
            // (each pass's never-regress floor is its own seed = the prior best).
            var roundBestJ = new List<double> { result.BestJ };
            for (var roundIdx = 0; roundIdx < ctx.ContinueRounds; roundIdx++) {
                var seedN = result.BestParams;
                var variablesN = OptimizerVariable.CreateCuratedSet(seedN);
                var next = await optimizer.OptimizeAsync(seedN, variablesN, evaluator, settings, progress, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"Continue round {roundIdx + 1}: bestJ {F(result.BestJ)} -> {F(next.BestJ)}");
                result = next;
                roundBestJ.Add(next.BestJ);
            }
            sw.Stop();
            if (ctx.ContinueRounds > 0) {
                Console.WriteLine($"Per-round J: {string.Join(" -> ", roundBestJ.Select(F))}");
            }
            Console.WriteLine($"Optimization complete: currentJ={F(baselineJ)} -> bestJ={F(result.BestJ)} ({(result.BestJ > baselineJ ? "improved over current" : "no improvement over current")}), evals={result.Evaluations}");

            // Cache-health + wall-clock readout (the early-context build:reuse ratio is the direct measure of how much
            // per-frame early work the staged search / context cache avoids; see the performance-design doc).
            var builds = dataList.Sum(d => d.ContextBuilds) - buildsBefore;
            var reuses = dataList.Sum(d => d.ContextReuses) - reusesBefore;
            var totalDetections = builds + reuses;
            var reusePct = totalDetections > 0 ? 100.0 * reuses / totalDetections : 0.0;
            Console.WriteLine($"Optimize phase: {sw.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} s wall, " +
                $"early-context builds={builds}, reuses={reuses} ({reusePct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}% reuse of {totalDetections} frame detections)");

            // Persist the bestJ-vs-evals trajectory (eval-budget analysis). finalBestJ is result.BestJ; the rows let
            // us read off how few evals reach within a small tolerance of it.
            try {
                var trajLines = new List<string> { "eval,bestJ,phase,wall_s_total,final_bestJ" };
                foreach (var t in trajectory) {
                    trajLines.Add($"{t.Evaluations},{t.BestJ.ToString("R", CultureInfo.InvariantCulture)},{t.Phase}," +
                        $"{sw.Elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture)},{result.BestJ.ToString("R", CultureInfo.InvariantCulture)}");
                }
                File.WriteAllLines(Path.Combine(targetDir, "optimize_trajectory.csv"), trajLines);
            } catch (Exception ex) {
                Logger.Warning($"Failed to write optimize_trajectory.csv: {ex.Message}");
            }

            // Evaluate the winner per run (the current-settings baseline was already evaluated above).
            var perRunBest = new List<RunEvaluationResult>(loadedRuns.Count);
            foreach (var r in loadedRuns) {
                perRunBest.Add(await r.Data.EvaluateAndFitAsync(result.BestParams, CancellationToken.None).ConfigureAwait(false));
            }
            var (passed, worstFrameCount, worstRunId) = AssertHardFloor(loadedRuns, perRunBest, objectiveConstants);
            Console.WriteLine(passed
                ? $"PASS: every frame has >= {objectiveConstants.NHard} stars under optimized params (min observed = {worstFrameCount})"
                : $"FAIL: at least one frame has < {objectiveConstants.NHard} stars under optimized params (min observed = {worstFrameCount} in run {worstRunId})");

            // The focuser's max step is unavailable headless, so it is left null — StepSizeRecommender clamps to >= 1.
            var focuserMaxStep = (int?)null;

            WriteSummary(Path.Combine(targetDir, "optimize_summary.txt"), runsDir, ctx.Baseline, baselineJ, result, variables,
                loadedRuns, perRunBaseline, perRunBest, objectiveConstants, focuserMaxStep, ctx.LabelsDir, settings, passed, worstFrameCount, worstRunId);
            WriteCsv(Path.Combine(targetDir, "optimize_result.csv"), loadedRuns, perRunBaseline, perRunBest);

            // Optimized-settings handoff: write the winning snapshot as optimized_settings.json into each focus run's
            // own source folder (so `review --runs <same>` auto-discovers it) plus a single copy in the --out dir.
            // Uses the SAME params->DTO mapping the wizard's Apply() uses (OptimizedStarDetectionSettings.FromParams)
            // so the headless and in-app handoffs can never drift. The source-folder copies each carry that run's OWN
            // recommended step (StepSizeRecommender, exactly as BuildAggregateRow computes it); the single --out copy
            // uses the representative (first) run's step in joint mode (see WriteOptimizedSettings).
            WriteOptimizedSettings(targetDir, loadedRuns, perRunBest, result, baselineJ, focuserMaxStep);

            await WriteAnnotatedFrames(targetDir, loadedRuns, result.BestParams, ctx.Detector,
                ctx.MeasurementAverage, ctx.HighSigmaOutlierRejection, ctx.LowSigmaOutlierRejection, ctx.AnnotateAll).ConfigureAwait(false);

            return new RunSetOutcome {
                HardFloorPassed = passed,
                WorstFrameCount = worstFrameCount,
                WorstRunId = worstRunId,
                Result = result,
                BaselineJ = baselineJ,
                PerRunBaseline = perRunBaseline,
                PerRunBest = perRunBest,
                LoadedRuns = loadedRuns,
                ObjectiveConstants = objectiveConstants
            };
        }

        /// <summary>
        /// Writes <c>optimized_settings.json</c> (the wizard's <see cref="OptimizedStarDetectionSettings"/> snapshot
        /// serialized with <see cref="JsonConvert"/>): one per-run copy into each focus run's own source folder (the
        /// directory holding its sweep frames — so <c>review --runs &lt;same&gt;</c> auto-discovers it), carrying THAT
        /// run's recommended AF step from its OWN best fit; plus a single copy into <paramref name="targetDir"/> (the
        /// <c>--out</c> dir). The curated knob values are always the optimizer's combined winner
        /// (<see cref="OptimizationResult.BestParams"/>). In joint mode the <c>--out</c> copy uses the
        /// REPRESENTATIVE (first) run's recommended step — matching the wizard's <c>BuildSummaryAsync</c>, which uses
        /// <c>runs[0]</c> for <see cref="StepSizeRecommender"/> — so the surviving <c>--out</c> copy is order-independent
        /// rather than whatever the last loop iteration happened to write. (In per-run mode <paramref name="targetDir"/>
        /// is that single run's own subfolder, so "first run" == that run; behavior is unchanged.) A failed write for
        /// one run is logged and skipped — it never aborts the batch.
        /// </summary>
        private static void WriteOptimizedSettings(
            string targetDir, List<LoadedHarnessRun> loadedRuns, List<RunEvaluationResult> perRunBest,
            OptimizationResult result, double baselineJ, int? focuserMaxStep) {
            // Per-run source-folder copies: each run's frame directory gets the winner snapshot with its OWN step.
            for (int i = 0; i < loadedRuns.Count; i++) {
                var run = loadedRuns[i];
                try {
                    var rec = StepSizeRecommender.Recommend(perRunBest[i].BestFit, run.StepSize, focuserMaxStep);
                    var dto = OptimizedStarDetectionSettings.FromParams(
                        result.BestParams, loadedRuns.Count, baselineJ, result.BestJ, rec.StepSize, rec.OffsetSteps);
                    var json = JsonConvert.SerializeObject(dto);

                    // The run's source folder is the directory holding its frames (each run's frames live together).
                    var firstFramePath = run.Discovered.Frames.FirstOrDefault()?.Path;
                    var runFolder = string.IsNullOrEmpty(firstFramePath) ? null : Path.GetDirectoryName(firstFramePath);
                    if (!string.IsNullOrEmpty(runFolder)) {
                        var runPath = Path.Combine(runFolder, "optimized_settings.json");
                        File.WriteAllText(runPath, json);
                        Console.WriteLine($"  wrote optimized_settings.json to {runPath}");
                    } else {
                        Console.Error.WriteLine($"  WARNING: could not resolve source folder for run '{run.Discovered.RunId}'; skipped the in-folder optimized_settings.json");
                    }
                } catch (Exception ex) {
                    Console.Error.WriteLine($"  WARNING: failed to write optimized_settings.json for run '{run.Discovered.RunId}': {ex.Message}");
                    Logger.Error(ex, $"Failed to write optimized_settings.json for run '{run.Discovered.RunId}'");
                }
            }

            // Single --out copy, written ONCE (outside the per-run loop) so it is independent of loop order. In joint
            // mode (loadedRuns.Count > 1) the recommended step is the representative (first) run's; the per-run
            // source-folder copies above carry each run's own step.
            if (loadedRuns.Count > 0) {
                var representative = loadedRuns[0];
                try {
                    var rec = StepSizeRecommender.Recommend(perRunBest[0].BestFit, representative.StepSize, focuserMaxStep);
                    var dto = OptimizedStarDetectionSettings.FromParams(
                        result.BestParams, loadedRuns.Count, baselineJ, result.BestJ, rec.StepSize, rec.OffsetSteps);
                    var json = JsonConvert.SerializeObject(dto);
                    var outPath = Path.Combine(targetDir, "optimized_settings.json");
                    File.WriteAllText(outPath, json);
                } catch (Exception ex) {
                    Console.Error.WriteLine($"  WARNING: failed to write optimized_settings.json to --out dir '{targetDir}': {ex.Message}");
                    Logger.Error(ex, $"Failed to write --out optimized_settings.json to '{targetDir}'");
                }
            }
        }

        private static void DisposeRuns(List<LoadedHarnessRun> loadedRuns) {
            foreach (var r in loadedRuns) {
                // Release the per-frame cached early-detection contexts the RunEvaluationData pinned (each holds a
                // ~244 MB source Mat at 61 MP); a long --per-run batch accumulates them otherwise. Idempotent.
                r.Data?.Dispose();
                // Drop the cached frames (each RunFrame.Image is an IRenderedImage the harness loaded once, holding
                // the raw ushort[] plus its rendered BitmapSources). Nothing here is IDisposable, so releasing the
                // references is what frees them; a long --per-run batch would otherwise pin every run's frame set.
                foreach (var rf in r.Frames) {
                    rf.Image = null;
                }
            }
        }

        // ---- Per-run aggregate report ----------------------------------------------------------------------

        /// <summary>One scannable row of the cross-setup aggregate report (the --per-run verification deliverable).</summary>
        private sealed class AggregateRow {
            public string RunId;
            public bool LoadOk = true;              // false => the run failed to load/optimize (Error set)
            public string Error;                    // populated only when LoadOk == false
            public bool HardFloorPassed;
            public int WorstFrameCount;
            public double BaselineJ = double.NaN;       // J of the user's CURRENT settings (the "before")
            public double BestJ = double.NaN;
            public double BaselineSigmaFocus = double.NaN;   // σ_focus of the CURRENT settings (the "before")
            public double BestSigmaFocus = double.NaN;
            public int RecommendedStep;
            public string ChangedParams = string.Empty;

            // Exposure-time recommendation (T8): the winning run's landed Sensitivity gate, whether it is at the
            // optimizer's search floor (signal-starved frames, see ExposureRecommender), and — only when it is —
            // the derived recommendation itself. Left null when SensitivityIsAtFloor is false: the offline report
            // does not compute or print a recommendation for a healthy run.
            public double BrightnessSensitivity = double.NaN;
            public bool SensitivityIsAtFloor;
            public ExposureRecommendation ExposureRecommendation;
        }

        /// <summary>Builds an aggregate row from a per-run outcome (exactly one run in the set in --per-run mode).
        /// J / σ_focus / changed-params are reported vs the user's CURRENT settings (ctx.Baseline), mirroring the
        /// wizard — NOT vs the default seed the optimizer started from.</summary>
        private static AggregateRow BuildAggregateRow(string runId, RunDetectionContext ctx, RunSetOutcome outcome) {
            var run = outcome.LoadedRuns[0];
            var baselineM = outcome.PerRunBaseline[0].Metrics;
            var bestM = outcome.PerRunBest[0].Metrics;
            var rec = StepSizeRecommender.Recommend(outcome.PerRunBest[0].BestFit, run.StepSize, null);
            // Changed curated params = current (Baseline) -> optimized (BestParams), exactly the diff the wizard shows
            // (and would apply), not the optimizer's default-seed -> optimized deltas.
            var changed = ctx.Variables
                .Select(v => (v.Name, Cur: v.Read(ctx.Baseline), Best: v.Read(outcome.Result.BestParams)))
                .Where(t => Math.Abs(t.Cur - t.Best) > 1e-9)
                .ToList();

            // Exposure-time recommendation (T8): only computed when the WINNING run's landed Sensitivity gate is at
            // the optimizer's search floor (signal-starved frames) — a healthy run gets a one-line "not at floor"
            // instead (see WriteAggregateSummary). Same metrics/constants the run was scored with: bestM is this
            // run's own RunEvaluationMetrics, outcome.ObjectiveConstants is the objective the optimizer actually
            // searched against (NTarget included), and run.CapturedExposureSeconds is the exposure its frames were
            // shot with (NaN when the header carried none -- ExposureRecommender then reports no recommendation).
            var landedSensitivity = outcome.Result.BestParams.Sensitivity;
            var sensitivityAtFloor = ExposureRecommender.SensitivityIsAtFloor(landedSensitivity);
            var exposureRecommendation = sensitivityAtFloor
                ? ExposureRecommender.Recommend(bestM, outcome.ObjectiveConstants, run.CapturedExposureSeconds)
                : null;

            return new AggregateRow {
                RunId = runId,
                LoadOk = true,
                HardFloorPassed = outcome.HardFloorPassed,
                WorstFrameCount = outcome.WorstFrameCount,
                BaselineJ = outcome.BaselineJ,
                BestJ = outcome.Result.BestJ,
                BaselineSigmaFocus = baselineM.SigmaFocus,
                BestSigmaFocus = bestM.SigmaFocus,
                RecommendedStep = rec.StepSize,
                ChangedParams = changed.Count == 0
                    ? "(none)"
                    : string.Join("; ", changed.Select(t => $"{t.Name}: {F(t.Cur)}->{F(t.Best)}")),
                BrightnessSensitivity = landedSensitivity,
                SensitivityIsAtFloor = sensitivityAtFloor,
                ExposureRecommendation = exposureRecommendation
            };
        }

        /// <summary>
        /// Writes the top-level aggregate_summary.txt for --per-run mode: one row per run, easy to scan across
        /// setups — RunId, load OK/failed, hard-floor PASS/FAIL (with the min star count), seed J -> best J,
        /// σ_focus seed -> optimized, recommended step, and which curated params changed.
        /// </summary>
        private static void WriteAggregateSummary(string path, string runsDir, RunDetectionContext ctx, List<AggregateRow> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("=== Star Detection Optimizer — per-run aggregate ===");
            sb.AppendLine($"Runs root: {runsDir}");
            sb.AppendLine($"Mode: --per-run (each run optimized independently)");
            sb.AppendLine($"Labels: {(string.IsNullOrWhiteSpace(ctx.LabelsDir) ? "(none — unlabeled)" : ctx.LabelsDir)}");
            sb.AppendLine($"MaxEvaluations: {(ctx.MaxEvals?.ToString(CultureInfo.InvariantCulture) ?? "default")}");
            sb.AppendLine($"Runs: {rows.Count} ({rows.Count(r => r.LoadOk)} optimized, {rows.Count(r => !r.LoadOk)} failed)");
            sb.AppendLine();

            foreach (var r in rows) {
                sb.AppendLine($"--- {r.RunId} ---");
                if (!r.LoadOk) {
                    sb.AppendLine($"  load        : FAILED ({r.Error})");
                    sb.AppendLine();
                    continue;
                }
                sb.AppendLine($"  load        : OK");
                sb.AppendLine($"  hard-floor  : {(r.HardFloorPassed ? "PASS" : "FAIL")} (min stars = {r.WorstFrameCount})");
                sb.AppendLine($"  J           : {F(r.BaselineJ)} -> {F(r.BestJ)}  (current -> optimized)");
                sb.AppendLine($"  sigma_focus : {F(r.BaselineSigmaFocus)} -> {F(r.BestSigmaFocus)}  (current -> optimized)");
                sb.AppendLine($"  rec. step   : {r.RecommendedStep}");
                sb.AppendLine($"  changed     : {r.ChangedParams}");
                AppendExposureRecommendationLines(sb, "  ", r.BrightnessSensitivity, r.SensitivityIsAtFloor, r.ExposureRecommendation);
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Formats the exposure-time recommendation (T8) for one run into <paramref name="sb"/>, shared by the
        /// joint-mode optimize_summary.txt and the --per-run aggregate_summary.txt so the two never drift. When
        /// <paramref name="sensitivityIsAtFloor"/> is false, a single line says so — no recommendation is computed
        /// or printed for a healthy run. When it fired but <see cref="ExposureRecommendation.HasRecommendation"/> is
        /// false (thin data or an unrecorded exposure), that is reported explicitly rather than silently omitted.
        /// </summary>
        private static void AppendExposureRecommendationLines(StringBuilder sb, string indent, double brightnessSensitivity, bool sensitivityIsAtFloor, ExposureRecommendation rec) {
            sb.AppendLine($"{indent}sensitivity : {F(brightnessSensitivity)}{(sensitivityIsAtFloor ? "  (AT SEARCH FLOOR -- frames may be signal-starved)" : "")}");
            if (!sensitivityIsAtFloor) {
                sb.AppendLine($"{indent}exposure rec: n/a (Sensitivity is not at the search floor)");
                return;
            }
            if (rec == null || !rec.HasRecommendation) {
                sb.AppendLine($"{indent}exposure rec: NO RECOMMENDATION (usable frames={rec?.UsableFrameCount ?? 0}, short frames={rec?.ShortFrameCount ?? 0}, " +
                    $"current exposure={(rec != null && double.IsFinite(rec.CurrentSeconds) ? $"{F(rec.CurrentSeconds)}s" : "unrecorded")})");
                return;
            }
            sb.AppendLine($"{indent}exposure rec: {F(rec.CurrentSeconds)}s -> {F(rec.RecommendedSeconds)}s " +
                $"(increases={rec.IncreasesExposure}, raw={F(rec.RawSeconds)}s, S_now={F(rec.MeasuredSnr)}, " +
                $"capped={rec.WasCapped}{(rec.WasCapped ? $" [byAbsoluteLimit={rec.CappedByAbsoluteLimit}]" : "")}, " +
                $"usable frames={rec.UsableFrameCount}, short frames={rec.ShortFrameCount})");
        }

        private static void PrintUsage() {
            Console.Error.WriteLine("Usage: TestApp optimize --runs <folder> [--per-run] [--profile-id <guid>] [--out <dir>] [--max-evals <int>] [--annotate extremes|all] [--labels <dir>] [--inspection] [--donut] [--start-from-current] [--continue-rounds <0-2>] [--verbose]");
            Console.Error.WriteLine("  --runs       (required) folder of saved AF runs. Runs are 'attempt*' folders (recursively, <=4 deep) with >=3 focuser positions; or --runs itself.");
            Console.Error.WriteLine("  --per-run    (optional) optimize each discovered run INDEPENDENTLY into its own subfolder + an aggregate_summary.txt (use for a multi-setup bank).");
            Console.Error.WriteLine("  --profile-id (default active) NINA profile id to load (settings + PixelScale).");
            Console.Error.WriteLine("  --out        (default %LOCALAPPDATA%\\NINA\\Logs\\hf-diag\\optimize\\<timestamp>) output directory.");
            Console.Error.WriteLine("  --max-evals  (optional) override the optimizer's MaxEvaluations budget.");
            Console.Error.WriteLine("  --annotate   (default extremes) annotate only min/max-focuser frames, or 'all' frames.");
            Console.Error.WriteLine("  --labels     (optional) folder of label JSON files; activates the recall/precision objective term.");
            Console.Error.WriteLine("  --inspection (optional) use the aberration-inspection objective (favor more stars; fit bounded relative to current σ).");
            Console.Error.WriteLine("  --donut      (optional) force the DefocusAwareDonutDetection MASTER on so the optimizer explores the donut/spike recovery axes.");
            Console.Error.WriteLine("  --start-from-current (optional) seed the optimizer from the current settings instead of the defaults (never regresses below current J).");
            Console.Error.WriteLine("  --legacy-objective (optional) disable the HFR-outlier penalty + region-coverage reward + saturated-HFR exclusion (the pre-change 'before' for an A/B).");
            Console.Error.WriteLine("  --continue-rounds (optional, 0-2) extra chained passes after the first, each re-seeded from the prior best (3 total).");
            Console.Error.WriteLine("  --verbose    (optional) restore TRACE logging (default INFO). Slower: serializes per-detection stage timings to the NINA log.");
        }

        // ---- Run / frame discovery -------------------------------------------------------------------------
        // Attempt-anchored discovery (with the >=3-position guard, back-compat, and fallback) lives in the pure,
        // unit-testable OptimizationRunDiscovery helper. The runner consumes its DiscoveredRun/FrameRef types.

        private sealed class LoadedHarnessRun {
            public OptimizationRunDiscovery.DiscoveredRun Discovered;
            public RunEvaluationData Data;
            public int StepSize;
            public List<RunFrame> Frames; // the loaded-once frame images, retained for release
            // PixelScale (arcsec/binned-px) from this run's OWN first frame header, and that frame's metadata for
            // the per-dataset settings derivation. NaN when neither frame nor settings file supplies one.
            public double PixelScale = double.NaN;
            public string PixelScaleSource = "unset";
            public NINA.Image.ImageData.ImageMetaData FirstFrameMeta;

            // The exposure, in seconds, the run's frames were captured with (first frame's header; NaN when
            // unrecorded) — see PrepareRunAsync. Feeds ExposureRecommender.Recommend's currentExposureSeconds.
            public double CapturedExposureSeconds = double.NaN;
        }

        /// <summary>
        /// Infers the AF step size from the frames exactly as <c>AutoFocusEngine.LoadSavedAttemptImpl</c> does:
        /// the absolute difference of the two smallest DISTINCT focuser positions. 0 when fewer than 2 positions.
        /// </summary>
        private static int InferStepSize(List<OptimizationRunDiscovery.FrameRef> frames) {
            var positions = frames.Select(f => f.FocuserPosition).Distinct().OrderBy(x => x).Take(2).ToList();
            return positions.Count > 1 ? Math.Abs(positions[0] - positions[1]) : 0;
        }

        // ---- Detection + HFR aggregation live in the PLUGIN: RunEvaluationLoader.HocusFocusSplitFrameDetector
        //      (TestApp/HarnessSplitDetector.cs), shared with the tilt-calibration harness so the two can never drift.

        // ---- Labels ----------------------------------------------------------------------------------------

        /// <summary>
        /// Forward-compatible label JSON shape (T7 writes these; T6 reads them). One file per run, named
        /// "&lt;runId&gt;.json" (the discovered RunId == the run's folder name), or any *.json whose embedded RunId
        /// matches a discovered run. Each label is a BOX (top-left x,y + w,h); recall/precision are scored by
        /// box-containment of an accepted center. Older point-only files (x,y, no w/h) still load: a missing w/h
        /// defaults to a 2·radiusPx box centered on the point. Shape:
        /// <code>
        /// {
        ///   "runId": "attempt01",
        ///   "radiusPx": 6.0,                       // default radius (legacy point-load box fallback)
        ///   "positions": [
        ///     {
        ///       "focuserPosition": 5000,
        ///       "radiusPx": 6.0,                    // optional per-position override
        ///       "missed":          [ { "x": 123.4, "y": 567.8, "w": 12.0, "h": 12.0 }, ... ],  // false negatives to recover (recall)
        ///       "shouldReject":    [ { "x": 12.0,  "y": 34.0,  "w": 9.0,  "h": 9.0  }, ... ],  // false positives to exclude (precision)
        ///       "wronglyRejected": [ { "x": 88.0,  "y": 90.0,  "w": 10.0, "h": 8.0  }, ... ]   // detected-but-gated candidates to KEEP (folds into recall)
        ///     }
        ///   ]
        /// }
        /// </code>
        /// </summary>
        private sealed class LabelPoint {
            [JsonProperty("x")] public double X { get; set; }
            [JsonProperty("y")] public double Y { get; set; }

            // Nullable so a legacy point-only file (x,y, no w/h) deserializes; back-filled to a 2·radiusPx box below.
            [JsonProperty("w")] public double? W { get; set; }
            [JsonProperty("h")] public double? H { get; set; }
        }

        private sealed class LabelPosition {
            [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
            [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
            [JsonProperty("missed")] public List<LabelPoint> Missed { get; set; }
            [JsonProperty("shouldReject")] public List<LabelPoint> ShouldReject { get; set; }
            [JsonProperty("wronglyRejected")] public List<LabelPoint> WronglyRejected { get; set; }
        }

        private sealed class RunLabelFile {
            [JsonProperty("runId")] public string RunId { get; set; }
            [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
            [JsonProperty("positions")] public List<LabelPosition> Positions { get; set; }
        }

        private const double DefaultLabelRadiusPx = 6.0;

        private static Dictionary<string, IReadOnlyList<FrameLabels>> LoadLabels(string labelsDir, List<OptimizationRunDiscovery.DiscoveredRun> runs) {
            var result = new Dictionary<string, IReadOnlyList<FrameLabels>>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(labelsDir)) {
                return result;
            }
            if (!Directory.Exists(labelsDir)) {
                throw new DirectoryNotFoundException($"--labels folder not found: {labelsDir}");
            }

            var byRunId = new Dictionary<string, RunLabelFile>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(labelsDir, "*.json").OrderBy(f => f, StringComparer.Ordinal)) {
                RunLabelFile parsed;
                try {
                    parsed = JsonConvert.DeserializeObject<RunLabelFile>(File.ReadAllText(file));
                } catch (Exception ex) {
                    Console.WriteLine($"WARNING: could not parse label file '{file}': {ex.Message}");
                    Logger.Warning($"Could not parse label file '{file}': {ex.Message}");
                    continue;
                }
                if (parsed == null) {
                    continue;
                }
                // Prefer the embedded runId; fall back to the filename (without extension).
                var runId = !string.IsNullOrWhiteSpace(parsed.RunId) ? parsed.RunId : Path.GetFileNameWithoutExtension(file);
                byRunId[runId] = parsed;
            }

            foreach (var run in runs) {
                if (!byRunId.TryGetValue(run.RunId, out var lf)) {
                    // G3 trailing-segment fallback: wizard label files embed the ABSOLUTE run path as runId, while
                    // discovery keys a run by its relative/leaf path — match on the last path segment so wizard
                    // labels load against a path-relocated copy of the run.
                    var leaf = StarReviewLabelStore.LastSegment(run.RunId);
                    if (!string.IsNullOrEmpty(leaf)) {
                        lf = byRunId.FirstOrDefault(kv => string.Equals(StarReviewLabelStore.LastSegment(kv.Key), leaf, StringComparison.OrdinalIgnoreCase)).Value;
                    }
                }
                if (lf?.Positions == null) {
                    continue;
                }
                var defaultRadius = lf.RadiusPx ?? DefaultLabelRadiusPx;
                var frameLabels = new List<FrameLabels>(lf.Positions.Count);
                foreach (var pos in lf.Positions) {
                    var radius = pos.RadiusPx ?? defaultRadius;
                    frameLabels.Add(new FrameLabels {
                        FocuserPosition = pos.FocuserPosition,
                        RadiusPx = radius,
                        Missed = ToBoxes(pos.Missed, radius),
                        ShouldReject = ToBoxes(pos.ShouldReject, radius),
                        WronglyRejected = ToBoxes(pos.WronglyRejected, radius)
                    });
                }
                result[run.RunId] = frameLabels;
            }
            return result;
        }

        /// <summary>
        /// Maps parsed label JSON points into the objective's <see cref="LabelBox"/> list. A label with explicit w/h
        /// is taken as-is (top-left x,y + w,h). A legacy point-only label (no w/h) is widened to a 2·radiusPx box
        /// CENTERED on its (x,y), matching the T7 writer's legacy back-fill so the two tools agree on old files.
        /// </summary>
        private static IReadOnlyList<LabelBox> ToBoxes(List<LabelPoint> points, double radiusPx) {
            if (points == null || points.Count == 0) {
                return new List<LabelBox>();
            }
            var side = Math.Max(1e-9, 2.0 * radiusPx);
            var boxes = new List<LabelBox>(points.Count);
            foreach (var p in points) {
                if (p.W.HasValue && p.H.HasValue) {
                    boxes.Add(new LabelBox(p.X, p.Y, p.W.Value, p.H.Value));
                } else {
                    boxes.Add(new LabelBox(p.X - side / 2.0, p.Y - side / 2.0, side, side));
                }
            }
            return boxes;
        }

        // ---- Hard-floor assertion --------------------------------------------------------------------------

        private static (bool passed, int worstCount, string worstRunId) AssertHardFloor(
            List<LoadedHarnessRun> runs, List<RunEvaluationResult> perRunBest, ObjectiveConstants c) {
            bool passed = true;
            int worst = int.MaxValue;
            string worstRunId = null;
            for (int i = 0; i < runs.Count; i++) {
                var counts = perRunBest[i].Metrics.FrameStarCounts;
                if (counts == null || counts.Count == 0) {
                    continue;
                }
                var min = counts.Min();
                if (min < worst) {
                    worst = min;
                    worstRunId = runs[i].Discovered.RunId;
                }
                if (min < c.NHard) {
                    passed = false;
                }
            }
            if (worst == int.MaxValue) {
                worst = 0;
            }
            return (passed, worst, worstRunId);
        }

        // ---- Output: summary + CSV -------------------------------------------------------------------------

        private static void WriteSummary(
            string path, string runsDir, StarDetectorParams baseline, double baselineJ, OptimizationResult result,
            IReadOnlyList<OptimizerVariable> variables, List<LoadedHarnessRun> runs,
            List<RunEvaluationResult> perRunBaseline, List<RunEvaluationResult> perRunBest,
            ObjectiveConstants c, int? focuserMaxStep, string labelsDir, OptimizerSettings settings,
            bool passed, int worstFrameCount, string worstRunId) {
            var sb = new StringBuilder();
            sb.AppendLine("=== Star Detection Optimizer — headless harness ===");
            sb.AppendLine($"Runs root: {runsDir}");
            sb.AppendLine($"Runs: {runs.Count} ({string.Join(", ", runs.Select(r => r.Discovered.RunId))})");
            sb.AppendLine($"Labels: {(string.IsNullOrWhiteSpace(labelsDir) ? "(none — unlabeled)" : labelsDir)}");
            sb.AppendLine($"MaxEvaluations: {settings.MaxEvaluations}; evaluator calls: {result.Evaluations}");
            sb.AppendLine("Optimizer seed: fully-default params; improvement is measured vs the user's CURRENT settings (matches the wizard).");
            sb.AppendLine();

            sb.AppendLine("--- Objective (current settings -> optimized) ---");
            sb.AppendLine($"Current J : {F(baselineJ)}");
            sb.AppendLine($"Best J    : {F(result.BestJ)}  ({(result.BestJ > baselineJ ? "improved over current" : "no improvement over current")})");
            sb.AppendLine();

            sb.AppendLine("--- Per-run σ_focus (current -> optimized) and recommended step size ---");
            for (int i = 0; i < runs.Count; i++) {
                var run = runs[i];
                var baselineM = perRunBaseline[i].Metrics;
                var bestM = perRunBest[i].Metrics;
                var rec = StepSizeRecommender.Recommend(perRunBest[i].BestFit, run.StepSize, focuserMaxStep);
                sb.AppendLine($"  {run.Discovered.RunId}:");
                sb.AppendLine($"    σ_focus       : {F(baselineM.SigmaFocus)} -> {F(bestM.SigmaFocus)}");
                sb.AppendLine($"    R²            : {F(baselineM.RSquared)} -> {F(bestM.RSquared)}");
                sb.AppendLine($"    reducedχ²     : {F(baselineM.ReducedChiSquared)} -> {F(bestM.ReducedChiSquared)}");
                sb.AppendLine($"    current step  : {run.StepSize}");
                sb.AppendLine($"    recommended   : step {rec.StepSize}, offset {rec.OffsetSteps} per side (half-width {F(rec.HalfWidth)})");
                if (bestM.Recall.HasValue || bestM.Precision.HasValue) {
                    sb.AppendLine($"    recall/prec   : {F(bestM.Recall ?? double.NaN)} / {F(bestM.Precision ?? double.NaN)}");
                }
                // Exposure-time recommendation (T8): result.BestParams is the JOINT winner shared by every run in
                // this set, so its Sensitivity is the same landed value for each row; bestM/run.CapturedExposureSeconds
                // are this run's own metrics/exposure, and c is the same ObjectiveConstants the runs were scored with.
                var sensitivityIsAtFloor = ExposureRecommender.SensitivityIsAtFloor(result.BestParams.Sensitivity);
                var exposureRec = sensitivityIsAtFloor ? ExposureRecommender.Recommend(bestM, c, run.CapturedExposureSeconds) : null;
                AppendExposureRecommendationLines(sb, "    ", result.BestParams.Sensitivity, sensitivityIsAtFloor, exposureRec);
            }
            sb.AppendLine();

            sb.AppendLine("--- Curated params (current -> optimized) ---");
            foreach (var v in variables) {
                var currentVal = v.Read(baseline);
                var bestVal = v.Read(result.BestParams);
                var marker = Math.Abs(currentVal - bestVal) > 1e-9 ? "  *" : "";
                sb.AppendLine($"  {v.Name,-30} {F(currentVal),14} -> {F(bestVal),-14}{marker}");
            }
            sb.AppendLine("  (* = differs from your current settings)");
            sb.AppendLine();

            sb.AppendLine($"--- Hard-floor check (every frame must keep >= {c.NHard} stars) ---");
            sb.AppendLine(passed
                ? $"  PASS (min observed = {worstFrameCount})"
                : $"  FAIL (min observed = {worstFrameCount} in run {worstRunId})");
            sb.AppendLine();

            sb.AppendLine("--- Per-frame star counts (per run, per focuser position: current -> optimized) ---");
            for (int i = 0; i < runs.Count; i++) {
                var run = runs[i];
                sb.AppendLine($"  {run.Discovered.RunId}:");
                // Group frames by focuser position; report current/optimized counts for each.
                var byPos = BuildPerPositionCounts(run, perRunBaseline[i], perRunBest[i]);
                sb.AppendLine($"    {"focuser",-10} {"current",7} {"opt",6}");
                foreach (var kvp in byPos) {
                    sb.AppendLine($"    {kvp.Key,-10} {kvp.Value.current,7} {kvp.Value.opt,6}");
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Builds a focuser-position -> (current count, optimized count) map for one run. FrameStarCounts is in the
        /// SAME order RunEvaluationData iterates its frames (the order they were added), so it aligns 1:1 with the
        /// run's frame list; multiple frames at one position are summed (matches how the V-curve pools positions).
        /// </summary>
        private static SortedDictionary<int, (int current, int opt)> BuildPerPositionCounts(
            LoadedHarnessRun run, RunEvaluationResult baselineResult, RunEvaluationResult bestResult) {
            var byPos = new SortedDictionary<int, (int current, int opt)>();
            var frames = run.Discovered.Frames;
            var baselineCounts = baselineResult.Metrics.FrameStarCounts;
            var bestCounts = bestResult.Metrics.FrameStarCounts;
            for (int i = 0; i < frames.Count; i++) {
                var pos = frames[i].FocuserPosition;
                var s = (baselineCounts != null && i < baselineCounts.Count) ? baselineCounts[i] : 0;
                var o = (bestCounts != null && i < bestCounts.Count) ? bestCounts[i] : 0;
                if (byPos.TryGetValue(pos, out var cur)) {
                    byPos[pos] = (cur.current + s, cur.opt + o);
                } else {
                    byPos[pos] = (s, o);
                }
            }
            return byPos;
        }

        private static void WriteCsv(
            string path, List<LoadedHarnessRun> runs, List<RunEvaluationResult> perRunBaseline, List<RunEvaluationResult> perRunBest) {
            var sb = new StringBuilder();
            sb.AppendLine("run,focuserPosition,currentStarCount,optimizedStarCount,frameIndex,framePath");
            for (int i = 0; i < runs.Count; i++) {
                var run = runs[i];
                var frames = run.Discovered.Frames;
                var baselineCounts = perRunBaseline[i].Metrics.FrameStarCounts;
                var bestCounts = perRunBest[i].Metrics.FrameStarCounts;
                for (int j = 0; j < frames.Count; j++) {
                    var s = (baselineCounts != null && j < baselineCounts.Count) ? baselineCounts[j] : 0;
                    var o = (bestCounts != null && j < bestCounts.Count) ? bestCounts[j] : 0;
                    sb.AppendLine(string.Join(",",
                        Csv(run.Discovered.RunId), frames[j].FocuserPosition, s, o, j, Csv(frames[j].Path)));
                }
            }
            File.WriteAllText(path, sb.ToString());
        }

        // ---- Output: annotated PNGs ------------------------------------------------------------------------

        /// <summary>
        /// Color key for rejected candidates (BGR for OpenCV). Accepted stars are GREEN with their HFR; a real
        /// star with NO marker at all is "missed entirely" — the whole point of drawing every rejection reason.
        /// </summary>
        private static readonly Scalar AcceptedColor = new Scalar(0, 255, 0);          // green
        private static readonly (string Reason, Scalar Color)[] RejectionLegend = {
            ("TooDistorted",   new Scalar(0, 255, 255)),   // yellow
            ("Degenerate",     new Scalar(0, 165, 255)),   // orange
            ("Saturated",      new Scalar(0, 0, 255)),     // red
            ("LowSensitivity", new Scalar(255, 255, 0)),   // cyan
            ("NotCentered",    new Scalar(255, 0, 255)),   // magenta
            ("TooFlat",        new Scalar(255, 0, 0)),     // blue
            ("Contaminated",   new Scalar(128, 0, 128)),   // purple
        };

        private static async Task WriteAnnotatedFrames(
            string outDir, List<LoadedHarnessRun> runs, StarDetectorParams optimizedParams, StarDetector detector,
            MeasurementAverageEnum measurementAverage, double highSigma, double lowSigma, bool annotateAll) {
            foreach (var run in runs) {
                var frames = run.Frames;
                if (frames.Count == 0) {
                    continue;
                }
                List<RunFrame> toAnnotate;
                if (annotateAll) {
                    toAnnotate = frames;
                } else {
                    // The two most-defocused extremes = min and max focuser position.
                    var minPos = frames.OrderBy(f => f.FocuserPosition).First();
                    var maxPos = frames.OrderByDescending(f => f.FocuserPosition).First();
                    toAnnotate = ReferenceEquals(minPos, maxPos)
                        ? new List<RunFrame> { minPos }
                        : new List<RunFrame> { minPos, maxPos };
                }

                foreach (var frame in toAnnotate) {
                    // Reuse the already-loaded rendered image (detection never mutates it). This also sidesteps
                    // re-loading .xisf/.fits, which would need the profile again. The PNG background is a
                    // display-only Mat (debayered luminance for an OSC frame, never CFA-filtered); star boxes are
                    // full-frame pixel coordinates, which the debayer preserves.
                    var rendered = (IRenderedImage)frame.Image;
                    var result = await detector.Detect(rendered, optimizedParams, null, CancellationToken.None).ConfigureAwait(false);
                    using var srcFloat = RenderedImageLoading.ToDebayeredLuminanceMat(rendered);

                    var fileName = $"{SanitizeFileName(run.Discovered.RunId)}_Focuser{frame.FocuserPosition}_optimized.png";
                    var outPath = Path.Combine(outDir, fileName);
                    WriteAnnotated(outPath, srcFloat, result, measurementAverage, highSigma, lowSigma);
                    Console.WriteLine($"  annotated {outPath} ({result.DetectedStars.Count} accepted)");
                }
            }
        }

        private static void WriteAnnotated(
            string path, Mat srcFloat, HocusFocusStarDetectorResult result,
            MeasurementAverageEnum measurementAverage, double highSigma, double lowSigma) {
            using var bgr = BuildStretchedBgr(srcFloat);

            // Rejected candidates first (so accepted markers draw on top): one color per reason.
            var metrics = result.Metrics;
            DrawRects(bgr, metrics?.TooDistortedBounds, RejectionLegend[0].Color);
            DrawRects(bgr, metrics?.DegenerateBounds, RejectionLegend[1].Color);
            DrawRects(bgr, metrics?.SaturatedBounds, RejectionLegend[2].Color);
            DrawRects(bgr, metrics?.LowSensitivityBounds, RejectionLegend[3].Color);
            DrawRects(bgr, metrics?.NotCenteredBounds, RejectionLegend[4].Color);
            DrawRects(bgr, metrics?.TooFlatBounds, RejectionLegend[5].Color);
            DrawRects(bgr, metrics?.ContaminatedBounds, RejectionLegend[6].Color);

            // Accepted stars: green circle + HFR text. We mirror the production accepted set (post outlier-filter
            // for MeanOutliers) so the markers match the counts the optimizer scored.
            var accepted = (result.DetectedStars ?? new List<Star>());
            if (accepted.Count > 1 && measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                var (median, mad) = accepted.Select(s => s.HFR).MedianMAD();
                var hi = median + highSigma * mad;
                var lo = median - lowSigma * mad;
                accepted = accepted.Where(s => s.HFR <= hi && s.HFR >= lo).ToList();
            }
            foreach (var s in accepted) {
                var center = new OpenCvSharp.Point((int)Math.Round(s.Center.X), (int)Math.Round(s.Center.Y));
                Cv2.Circle(bgr, center, 8, AcceptedColor, 1, LineTypes.AntiAlias);
                Cv2.PutText(bgr, s.HFR.ToString("0.0", CultureInfo.InvariantCulture),
                    new OpenCvSharp.Point(center.X + 9, center.Y - 9), HersheyFonts.HersheySimplex, 0.35, AcceptedColor, 1, LineTypes.AntiAlias);
            }

            DrawLegend(bgr, accepted.Count);
            Cv2.ImWrite(path, bgr);
        }

        private static void DrawRects(Mat bgr, List<Rect> rects, Scalar color) {
            if (rects == null) {
                return;
            }
            foreach (var r in rects) {
                Cv2.Rectangle(bgr, r, color, 1, LineTypes.AntiAlias);
            }
        }

        /// <summary>Draws a small color key (top-left) so the rejection reasons are readable on the image itself.</summary>
        private static void DrawLegend(Mat bgr, int acceptedCount) {
            int x = 8, y = 18, dy = 16;
            Cv2.PutText(bgr, $"accepted ({acceptedCount})", new OpenCvSharp.Point(x, y), HersheyFonts.HersheySimplex, 0.4, AcceptedColor, 1, LineTypes.AntiAlias);
            y += dy;
            foreach (var (reason, color) in RejectionLegend) {
                Cv2.PutText(bgr, reason, new OpenCvSharp.Point(x, y), HersheyFonts.HersheySimplex, 0.4, color, 1, LineTypes.AntiAlias);
                y += dy;
            }
        }

        // MTF-stretched BGR background for the annotated image (copied from ContaminationDiagnosticRunner so the
        // two harnesses stay self-contained and identical in appearance).
        private static Mat BuildStretchedBgr(Mat srcFloat) {
            using var src16 = new Mat();
            srcFloat.ConvertTo(src16, MatType.CV_16U, ushort.MaxValue);
            using var stretched16 = new Mat();
            try {
                var stats = CvImageUtility.CalculateStatistics_Histogram(src16);
                using var lut = CvImageUtility.CreateMTFLookup(stats);
                CvImageUtility.ApplyLUT(src16, lut, stretched16);
            } catch (Exception ex) {
                Logger.Warning($"MTF stretch failed ({ex.Message}); falling back to linear normalization for the annotated image");
                Cv2.Normalize(src16, stretched16, 0, ushort.MaxValue, NormTypes.MinMax);
            }
            using var stretched8 = new Mat();
            stretched16.ConvertTo(stretched8, MatType.CV_8U, 1.0 / 256.0);
            var bgr = new Mat();
            Cv2.CvtColor(stretched8, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }

        // ---- Small helpers ---------------------------------------------------------------------------------

        // RunIds can be nested paths (e.g. "toml999/attempt01"), so delegate to the helper's slash-aware sanitizer.
        private static string SanitizeFileName(string name) => OptimizationRunDiscovery.SanitizeForFileName(name);

        private static string Csv(string s) {
            if (string.IsNullOrEmpty(s)) {
                return string.Empty;
            }
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n')) {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private static string F(double v) {
            if (double.IsNaN(v)) {
                return "NaN";
            }
            return v.ToString("G6", CultureInfo.InvariantCulture);
        }
    }
}
