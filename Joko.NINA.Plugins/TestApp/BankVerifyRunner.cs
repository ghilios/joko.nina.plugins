#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using Newtonsoft.Json;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
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
using System.Windows.Threading;
using DrawingSize = System.Drawing.Size;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Step 4-5 of the AF-bank verification (plans/autofocus-bank-verification-plan.md). Per discovered run, computes
    /// three configs — C0 as-default (shipped defaults; the honest recall reference, SWEPT over
    /// NoiseClippingMultiplier), A optimized donut-OFF, B optimized donut-ON — and for each reports the SHARED
    /// precision/recall vs the golden set, the autofocus fit (σ_focus / R² / reduced-χ²), and the sensor-model
    /// paraboloid fit (R² / RMS µm / reduced-χ² / stars / tilt θ / frames aligned). Aggregates all runs into a
    /// timestamped <c>verification_&lt;UTC&gt;.{json,md}</c> at the bank root (schema <c>afbank-verify/2</c>), with the
    /// NC-sweep summary + recommendation answering "was the shipped 4→2 default right, or is an adaptive NC
    /// warranted?". A/B read each run's optimized_settings.json from prior <c>optimize</c> runs (<c>--opt-a</c> /
    /// <c>--opt-b</c>); when absent only the C0 sweep is reported. The AF fit reuses the validated
    /// <see cref="RunEvaluationData.EvaluateAndFitAsync"/>, golden P/R reuses <see cref="GoldenMatch"/>, and the
    /// sensor fit reuses <see cref="SensorModel.RegisterStarsAndFit"/> — the same code paths as the standalone
    /// harnesses, so the numbers reproduce the cwhite dry-run. Read-only on the profile.
    /// </summary>
    public static class BankVerifyRunner {

        private const double MatchRadiusDefault = 12.0;

        // Flushed file-based progress trace (stdout is block-buffered through WSL interop, so a file log is the only
        // way to observe where a long run is). Each line is appended + flushed immediately.
        private static string _progressPath;
        private static void Prog(string msg) {
            try { if (_progressPath != null) File.AppendAllText(_progressPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}"); } catch { }
        }

        /// <summary>
        /// Runs the whole verification on a dedicated STA thread with a PUMPED dispatcher (the proven
        /// StarReviewRunner pattern). This is required because the work mixes two thread-sensitive primitives in one
        /// process: the optimizer's <see cref="RunEvaluationData.EvaluateAndFitAsync"/> (whose awaited continuations
        /// would deadlock on a non-pumping captured context) and the thread-affine <see cref="SensorModel"/> fit
        /// (which hangs on a thread-pool thread). Installing a <see cref="DispatcherSynchronizationContext"/> and
        /// pumping it keeps every awaited continuation AND the sensor fit on one consistent STA thread that owns the
        /// WPF <see cref="Application"/> — exactly the environment in which the standalone harnesses work.
        /// </summary>
        public static Task Run(string[] args) {
            Exception err = null;
            var thread = new Thread(() => {
                try {
                    // One Application per process; create it here so the profile load can write to its Resources, and
                    // install a dispatcher SynchronizationContext so awaited continuations resume on (and are pumped
                    // by) THIS STA thread.
                    if (Application.Current == null) { new Application(); }
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    dispatcher.InvokeAsync(async () => {
                        try { await RunCore(args); } catch (Exception ex) { err = ex; } finally { dispatcher.InvokeShutdown(); }
                    });
                    Dispatcher.Run();
                } catch (Exception ex) { err = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = false;
            thread.Start();
            thread.Join();
            if (err != null) { throw err; }
            return Task.CompletedTask;
        }

        private static async Task RunCore(string[] args) {
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runs) || !Directory.Exists(runs)) {
                Console.Error.WriteLine("Usage: TestApp bank-verify --runs <bank-root> [--out <dir>] [--nc-sweep 2,3,4] " +
                    "[--opt-a <dir>] [--opt-b <dir>] [--golden <dir>] [--match-radius 12] [--commit <hash>] [--profile-id <guid>] " +
                    "[--adaptive-binarize] [--adaptive-block 128]");
                Environment.ExitCode = 2;
                return;
            }
            var outDir = DiagnosticUtil.GetArg(args, "--out") ?? runs;
            Directory.CreateDirectory(outDir);
            _progressPath = Path.Combine(outDir, "bank_verify_progress.log");
            try { File.WriteAllText(_progressPath, $"{DateTime.Now:HH:mm:ss.fff} bank-verify start{Environment.NewLine}"); } catch { }
            var optA = DiagnosticUtil.GetArg(args, "--opt-a");
            var optB = DiagnosticUtil.GetArg(args, "--opt-b");
            var goldenDir = DiagnosticUtil.GetArg(args, "--golden");
            var matchRadius = ParseDouble(DiagnosticUtil.GetArg(args, "--match-radius"), MatchRadiusDefault);
            var commit = DiagnosticUtil.GetArg(args, "--commit") ?? "unknown";
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var ncSweep = ParseNcSweep(DiagnosticUtil.GetArg(args, "--nc-sweep") ?? "2,3,4");
            // Spatially-adaptive binarization override for the C0 (as-default) configs — the flag-on-vs-off A/B that
            // the adaptive-noiseclip feature is validated by (docs/adaptive-noiseclip-design.md). Default OFF mirrors
            // the shipped default. The optimized A/B configs instead receive these via OverlayOptimized.
            var adaptiveBinarize = DiagnosticUtil.HasFlag(args, "--adaptive-binarize");
            var adaptiveBlock = DiagnosticUtil.GetArg(args, "--adaptive-block");
            int adaptiveBlockSize = (adaptiveBlock != null && int.TryParse(adaptiveBlock, NumberStyles.Integer, CultureInfo.InvariantCulture, out var abv)) ? abv : 128;

            Logger.SetLogLevel(LogLevelEnum.INFO);
            // The WPF Application + dispatcher SynchronizationContext are set up by Run() on this STA thread.
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile
                ?? throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");
            var starDetectionOptions = new StarDetectionOptions(profileService);
            var inspectorOptions = new InspectorOptions(profileService);
            var autoFocusOptions = new AutoFocusOptions(profileService);
            const int binning = 1;
            var pixelScale = MathUtility.ArcsecPerPixel(activeProfile.CameraSettings.PixelSize, activeProfile.TelescopeSettings.FocalLength) * binning;
            var alglib = new AlglibAPI();
            var detector = new StarDetector(alglib);
            // The plugin's OWN detection facade, so the AF-fit path drives the wizard's HocusFocusSplitFrameDetector
            // rather than a harness mirror of its FrameDetectionResult mapping. Only GetInfo() and the inner
            // StarDetector are exercised on the split path; the rest are headless stubs.
            var detection = new HocusFocusStarDetection(
                imageStatisticsVM: null,
                profileService: profileService,
                focuserMediator: new StubFocuserMediator(),
                starDetectionOptions: starDetectionOptions,
                alglibAPI: alglib,
                perFilterStore: new StubPerFilterStarDetectionStore());

            var discovery = OptimizationRunDiscovery.Discover(runs);
            Console.WriteLine($"bank-verify: {discovery.Runs.Count} run(s) under {runs}; NC sweep [{string.Join(",", ncSweep.Select(x => x.ToString(CultureInfo.InvariantCulture)))}]; " +
                $"A={(optA ?? "(none)")} B={(optB ?? "(none)")}; matchRadius={matchRadius}; adaptiveBinarize={adaptiveBinarize}" + (adaptiveBinarize ? $"(block={adaptiveBlockSize})" : ""));

            var runResults = new List<RunResult>();
            int idx = 0;
            foreach (var run in discovery.Runs) {
                idx++;
                Console.WriteLine($"[{idx}/{discovery.Runs.Count}] {run.RunId}");
                try {
                    var rr = await VerifyRunAsync(run, runs, outDir, ncSweep, optA, optB, goldenDir, matchRadius,
                        profileService, activeProfile, starDetectionOptions, inspectorOptions, autoFocusOptions, detector, detection, alglib, pixelScale,
                        adaptiveBinarize, adaptiveBlockSize);
                    runResults.Add(rr);
                } catch (Exception ex) {
                    Console.Error.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
                    Logger.Error(ex, $"bank-verify failed for {run.RunId}");
                    runResults.Add(new RunResult { runId = run.RunId, error = ex.Message });
                }
            }

            var utc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            WriteReport(outDir, utc, commit, ncSweep, runResults);
            Console.WriteLine($"bank-verify: wrote verification_{utc}.{{json,md}} to {outDir} ({runResults.Count(r => r.error == null)} ok, {runResults.Count(r => r.error != null)} failed).");
        }

        // ---- per-run verification --------------------------------------------------------------------------------

        private static async Task<RunResult> VerifyRunAsync(
            OptimizationRunDiscovery.DiscoveredRun run, string runsRoot, string outDir, double[] ncSweep, string optA, string optB,
            string goldenDir, double matchRadius, ProfileService profileService, NINA.Profile.Interfaces.IProfile activeProfile,
            StarDetectionOptions sdOptions, InspectorOptions inspectorOptions, AutoFocusOptions afOptions,
            StarDetector detector, IHocusFocusStarDetection detection, AlglibAPI alglib, double pixelScale, bool adaptiveBinarize, int adaptiveBlockSize) {

            var runFolder = Path.GetDirectoryName(run.Frames.First().Path);
            var ordered = run.Frames.OrderBy(f => f.FocuserPosition).ToList();

            // Load frames once (shared by AF eval + golden + sensor across all configs) as the IRenderedImage the
            // LIVE app detects on: the CFA hotpixel filter and the debayer then run inside Detect at each config's
            // own params. Verifying recall/precision against an image the app never sees measures the wrong thing.
            Prog($"{run.RunId}: loading {ordered.Count} frames");
            var loaded = new List<(int focuser, string path, IRenderedImage image)>();
            foreach (var f in ordered) {
                loaded.Add((f.FocuserPosition, f.Path, await DiagnosticUtil.LoadRenderedImage(f.Path, profileService)));
                Prog($"  loaded focuser {f.FocuserPosition}");
            }
            var firstProps = loaded[0].image.RawImageData.Properties;
            var imageSize = new DrawingSize(firstProps.Width, firstProps.Height);

            // Golden sidecars per frame (shared across configs — detector-independent).
            var goldenByFocuser = new Dictionary<int, GoldenFrame>();
            int goldenStars = 0, goldenHigh = 0;
            foreach (var (focuser, path, _) in loaded) {
                var gp = string.IsNullOrWhiteSpace(goldenDir) ? path : Path.Combine(goldenDir, Path.GetFileName(path));
                var gf = GoldenStarSetStore.LoadForImage(gp);
                if (gf != null) {
                    goldenByFocuser[focuser] = gf;
                    goldenStars += gf.Stars.Count;
                    goldenHigh += gf.Stars.Count(s => GoldenConfidence.Rank(s.Confidence) >= 3);
                }
            }

            // RunEvaluationData for the AF fit (mirrors OptimizationDiagnosticRunner.PrepareRunAsync).
            var stepSize = InferStepSize(ordered.Select(f => (double)f.FocuserPosition).ToList());
            var runFrames = loaded.Select(l => new RunFrame { FrameId = l.path, FocuserPosition = l.focuser, Image = l.image }).ToList();
            var fitConfig = new RunFitConfig {
                StepSize = stepSize,
                UseWeights = afOptions.WeightedHyperbolicFitEnabled,
                MaxOutlierRejections = afOptions.MaxOutlierRejections,
                RejectionConfidence = afOptions.OutlierRejectionConfidence,
                // Was hard-coded null while the wizard's loader passes afOptions.HyperbolicFitModel. Currently
                // informational (the evaluator always runs the Hybrid best-fit selection), but the divergence itself
                // is what this work exists to remove.
                PreferredModel = afOptions.HyperbolicFitModel
            };
            // The WIZARD'S OWN split detector, driven through the plugin's detection facade — not a harness mirror of
            // its FrameDetectionResult mapping. hocusParams mirrors RunEvaluationLoader's: IsAutoFocus with
            // NumberOfAFStars = 0, so every accepted star is scored and the sigma rejections stay at the
            // HocusFocusDetectionParams class defaults (high 4.0 / low 3.0).
            var splitDetector = new RunEvaluationLoader.HocusFocusSplitFrameDetector(
                detection, new HocusFocusDetectionParams { IsAutoFocus = true, NumberOfAFStars = 0 });
            using var evalData = new RunEvaluationData(run.RunId, runFrames, splitDetector, alglib, fitConfig, null);

            var rr = new RunResult {
                runId = run.RunId,
                camera = $"{imageSize.Width}x{imageSize.Height}",
                goldenStars = goldenStars,
                goldenSNRge12 = goldenHigh,
                framesTotal = loaded.Count,
                configs = new List<ConfigMetrics>()
            };
            var meta = TryReadRunMeta(Path.Combine(runFolder, "run_meta.json"));
            rr.donutAware = meta?.donutAware ?? false;
            rr.donutHeuristic = meta?.reason;

            StarDetectorParams BaseDefault() {
                var p = HocusFocusStarDetection.BuildDefaultStarDetectorParams();
                p.PixelScale = pixelScale; p.Region = StarDetectionRegion.Full; p.ModelPSF = false; p.SaveIntermediateFilesPath = string.Empty;
                // Output-neutral and excluded from the detection cache key (see RunEvaluationLoader): the AF-fit path
                // re-detects every frame per config, and the plugin logs a per-region "Average HFR" INFO line each
                // time. Suppress it so a bank sweep does not flood the log.
                p.SuppressInfoLogging = true;
                return p;
            }

            // C0 — as-default, swept over NoiseClippingMultiplier.
            foreach (var nc in ncSweep) {
                var p = BaseDefault();
                p.NoiseClippingMultiplier = nc;
                p.LocallyAdaptiveBinarization = adaptiveBinarize;
                p.AdaptiveNoiseBlockSize = adaptiveBlockSize;
                var cm = await ScoreConfigAsync($"C0@nc{nc:0.#}", nc, false, p, evalData, loaded, goldenByFocuser, matchRadius,
                    inspectorOptions, alglib, profileService, activeProfile, stepSize, detector);
                rr.configs.Add(cm);
                Console.WriteLine($"    {cm.config}: recall@high={Fmt(cm.recallHigh)} prec={Fmt(cm.precision)} σ={Fmt(cm.sigmaFocus)} sR²={Fmt(cm.sR2)} aligned={cm.framesAligned}/{loaded.Count}");
            }

            // A — optimized, donut OFF.
            var aSettings = LoadOptimized(optA, run.RunId);
            if (aSettings != null) {
                var p = BaseDefault();
                OverlayOptimized(p, aSettings, forceDonutMaster: false);
                var cm = await ScoreConfigAsync("A", p.NoiseClippingMultiplier, p.DefocusAwareDonutDetection, p, evalData, loaded, goldenByFocuser, matchRadius,
                    inspectorOptions, alglib, profileService, activeProfile, stepSize, detector);
                cm.sensitivity = p.Sensitivity;
                rr.configs.Add(cm);
                Console.WriteLine($"    A (opt donutOFF, NC→{Fmt(cm.nc)}): recall@high={Fmt(cm.recallHigh)} prec={Fmt(cm.precision)} σ={Fmt(cm.sigmaFocus)} sR²={Fmt(cm.sR2)}");
            }

            // B — optimized, donut ON.
            var bSettings = LoadOptimized(optB, run.RunId);
            if (bSettings != null) {
                var p = BaseDefault();
                OverlayOptimized(p, bSettings, forceDonutMaster: true);
                var cm = await ScoreConfigAsync("B", p.NoiseClippingMultiplier, true, p, evalData, loaded, goldenByFocuser, matchRadius,
                    inspectorOptions, alglib, profileService, activeProfile, stepSize, detector);
                cm.sensitivity = p.Sensitivity;
                rr.configs.Add(cm);
                Console.WriteLine($"    B (opt donutON, NC→{Fmt(cm.nc)}): recall@high={Fmt(cm.recallHigh)} prec={Fmt(cm.precision)} σ={Fmt(cm.sigmaFocus)} sR²={Fmt(cm.sR2)}");
            }

            // Nothing to dispose: an IRenderedImage is not IDisposable — dropping the list releases the frames.
            loaded.Clear();

            // Per-run sidecar JSON.
            var runOut = Path.Combine(outDir, "bank_verify", OptimizationRunDiscovery.SanitizeForFileName(run.RunId));
            Directory.CreateDirectory(runOut);
            File.WriteAllText(Path.Combine(runOut, "verify.json"), JsonConvert.SerializeObject(rr, Formatting.Indented));
            return rr;
        }

        /// <summary>Detects every frame ONCE at <paramref name="p"/> and computes the shared golden P/R + the
        /// sensor-model fit; the AF fit comes from the validated <see cref="RunEvaluationData.EvaluateAndFitAsync"/>.</summary>
        private static async Task<ConfigMetrics> ScoreConfigAsync(
            string label, double nc, bool donut, StarDetectorParams p, RunEvaluationData evalData,
            List<(int focuser, string path, IRenderedImage image)> loaded, Dictionary<int, GoldenFrame> goldenByFocuser, double matchRadius,
            InspectorOptions inspectorOptions, AlglibAPI alglib, ProfileService profileService, NINA.Profile.Interfaces.IProfile activeProfile, int stepSize,
            StarDetector detector) {

            var cm = new ConfigMetrics { config = label, nc = nc, donut = donut, sensitivity = p.Sensitivity };

            // AF fit (reuses the optimizer's evaluation path → reproduces the dry-run σ_focus).
            Prog($"  [{label}] EvaluateAndFitAsync start (NC={nc}, donut={donut})");
            var af = await evalData.EvaluateAndFitAsync(p, CancellationToken.None);
            cm.sigmaFocus = af.Metrics.SigmaFocus; cm.afR2 = af.Metrics.RSquared; cm.afChi = af.Metrics.ReducedChiSquared;
            Prog($"  [{label}] EvaluateAndFitAsync done σ={cm.sigmaFocus:F3}; detect loop start");

            // Detect each frame once → golden P/R + sensor-model star lists.
            int tp = 0, fp = 0, fn = 0, matchedHigh = 0, totalHigh = 0, matchedAll = 0, totalAll = 0;
            var sensorFrames = new List<SensorDetectedStars>();
            DrawingSize imageSize = DrawingSize.Empty;
            foreach (var (focuser, path, image) in loaded) {
                var props = image.RawImageData.Properties;
                imageSize = new DrawingSize(props.Width, props.Height);
                // Detect(IRenderedImage) builds its own source Mat per call, so no clone is needed.
                var result = await detector.Detect(image, p, null, CancellationToken.None);
                var stars = result.DetectedStars ?? new List<Star>();
                Prog($"    [{label}] detected focuser {focuser}: {stars.Count} stars");

                if (goldenByFocuser.TryGetValue(focuser, out var gf)) {
                    var det = stars.Select(s => new DetBox(RectD.FromRect(s.StarBoundingBox), s.Center.X, s.Center.Y)).ToList();
                    var goldenRects = gf.Stars.Select(b => new RectD(b.X, b.Y, b.W, b.H)).ToList();
                    var match = GoldenMatch.Match(goldenRects, det, GoldenMatchMode.Centroid, 0.3, matchRadius);
                    tp += match.Pairs.Count; fp += match.FalsePositives.Count; fn += match.FalseNegatives.Count;
                    var matchedGolden = new HashSet<int>(match.Pairs.Select(x => x.Golden));
                    for (int gi = 0; gi < gf.Stars.Count; gi++) {
                        var matched = matchedGolden.Contains(gi);
                        totalAll++; if (matched) matchedAll++;
                        if (GoldenConfidence.Rank(gf.Stars[gi].Confidence) >= 3) { totalHigh++; if (matched) matchedHigh++; }
                    }
                }

                // Sensor model: same raster ordering as production (BuildStarDetectionResult).
                var starList = stars.Select(HocusFocusStarDetection.ToDetectedStar)
                    .OrderBy(s => s.Position.Y * (long)props.Width + s.Position.X).ToList();
                sensorFrames.Add(new SensorDetectedStars(focuser, new HocusFocusStarDetectionResult { StarList = starList, ImageSize = imageSize }, image: null));
            }

            var prAll = PrecisionRecall.Compute(tp, fp, fn);
            cm.precision = prAll.Precision;
            cm.recallAll = totalAll > 0 ? (double)matchedAll / totalAll : double.NaN;
            cm.recallHigh = totalHigh > 0 ? (double)matchedHigh / totalHigh : double.NaN;

            // Sensor-model paraboloid fit (reuses SensorModel.RegisterStarsAndFit, like inspect-align).
            Prog($"  [{label}] golden done (P={cm.precision:F3} R@hi={cm.recallHigh:F3}); sensor fit start");
            try {
                var focuserSizeMicrons = inspectorOptions.MicronsPerFocuserStep > 0 ? inspectorOptions.MicronsPerFocuserStep : 1.0;
                var pixelSize = activeProfile.CameraSettings.PixelSize > 0 ? activeProfile.CameraSettings.PixelSize : 3.76;
                var sortedFoc = loaded.Select(l => (double)l.focuser).OrderBy(x => x).ToList();
                var finalFocus = sortedFoc[sortedFoc.Count / 2];
                var sensorModel = new SensorModel(profileService, inspectorOptions, new AutoFocusOptions(profileService), new AlglibAPI());
                SensorParaboloidModel fit = null;
                try {
                    (fit, _) = sensorModel.RegisterStarsAndFit(sensorFrames, imageSize, focuserSizeMicrons, finalFocus, pixelSize,
                        progress: new Progress<ApplicationStatus>(), stepSize: stepSize, ct: CancellationToken.None);
                } catch (Exception ex) {
                    Logger.Warning($"bank-verify {label}: sensor fit threw: {ex.Message}");
                }
                cm.framesAligned = sensorFrames.Count(f => f.HasBeenAligned);
                if (fit != null) {
                    cm.sStars = fit.StarsInModel; cm.sR2 = fit.GoodnessOfFit; cm.sRMS = fit.RMSErrorMicrons;
                    cm.sChi = fit.ReducedChiSquared; cm.sTheta = fit.Theta * 180.0 / Math.PI;
                }
            } catch (Exception ex) {
                Logger.Warning($"bank-verify {label}: sensor stage failed: {ex.Message}");
            }
            Prog($"  [{label}] DONE sStars={cm.sStars} sR2={cm.sR2:F4} aligned={cm.framesAligned}");
            return cm;
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private static int InferStepSize(List<double> focusers) {
            var sorted = focusers.OrderBy(x => x).ToList();
            if (sorted.Count < 2) return 100;
            var diffs = new List<double>();
            for (int i = 1; i < sorted.Count; i++) { var d = Math.Abs(sorted[i] - sorted[i - 1]); if (d > 0) diffs.Add(d); }
            if (diffs.Count == 0) return 100;
            diffs.Sort();
            return (int)Math.Round(diffs[diffs.Count / 2]);
        }

        private static OptimizedStarDetectionSettings LoadOptimized(string optRoot, string runId) {
            if (string.IsNullOrWhiteSpace(optRoot)) return null;
            foreach (var candidate in new[] {
                Path.Combine(optRoot, OptimizationRunDiscovery.SanitizeForFileName(runId), "optimized_settings.json"),
                Path.Combine(optRoot, "optimized_settings.json")
            }) {
                if (File.Exists(candidate)) {
                    try { return JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(File.ReadAllText(candidate)); } catch { }
                }
            }
            return null;
        }

        /// <summary>Overlays an optimized_settings.json onto default params — the SAME field set inspect-align /
        /// golden eval overlay, AND-gating the donut sub-flags by the master (forced ON for config B).</summary>
        private static void OverlayOptimized(StarDetectorParams p, OptimizedStarDetectionSettings s, bool forceDonutMaster) {
            p.Sensitivity = s.BrightnessSensitivity; p.StarClippingMultiplier = s.StarClippingMultiplier;
            p.NoiseClippingMultiplier = s.NoiseClippingMultiplier; p.PeakResponse = s.StarPeakResponse;
            p.MaxDistortion = s.MaxDistortion; p.MinHFR = s.MinHFR; p.StarCenterTolerance = s.StarCenterTolerance;
            p.StructureLayers = s.StructureLayers; p.NoiseReductionRadius = s.NoiseReductionRadius;
            p.MinimumStarBoundingBoxSize = s.MinStarBoundingBoxSize; p.HotpixelThresholdingEnabled = s.HotpixelThresholdingEnabled;
            p.HotpixelThreshold = s.HotpixelThreshold;
            p.LocallyAdaptiveBinarization = s.LocallyAdaptiveBinarization; p.AdaptiveNoiseBlockSize = s.AdaptiveNoiseBlockSize;
            var master = forceDonutMaster || s.DefocusAwareDonutDetection;
            p.DefocusAwareDonutDetection = master;
            p.DefocusAwareDistortion = s.DefocusAwareGates && master; p.DefocusAwareCentering = s.DefocusAwareGates && master;
            p.DefocusAwareStructure = s.DefocusAwareStructure && master; p.StructureLayerBoost = s.StructureLayerBoost;
            p.DefocusDistortionSizeReference = s.DefocusDistortionSizeReference; p.DefocusDistortionMinFactor = s.DefocusDistortionMinFactor;
            p.DefocusCenteringToleranceFactor = s.DefocusCenteringToleranceFactor; p.DonutMorphCloseSize = s.DonutMorphCloseSize;
            p.DonutMinAnnularityHoleFraction = s.DonutMinAnnularityHoleFraction; p.DonutMaxStreakEccentricity = s.DonutMaxStreakEccentricity;
            p.DonutSaturationBloomRadius = s.DonutSaturationBloomRadius;
        }

        private static double[] ParseNcSweep(string s) =>
            s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
             .Where(v => !double.IsNaN(v)).ToArray();

        private static double ParseDouble(string s, double dflt) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : dflt;

        private static RunMetaLite TryReadRunMeta(string path) {
            try { return File.Exists(path) ? JsonConvert.DeserializeObject<RunMetaLite>(File.ReadAllText(path)) : null; } catch { return null; }
        }

        private static string Fmt(double v) => double.IsNaN(v) ? "NaN" : v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---- report ----------------------------------------------------------------------------------------------

        private static void WriteReport(string outDir, string utc, string commit, double[] ncSweep, List<RunResult> runs) {
            var ok = runs.Where(r => r.error == null && r.configs != null && r.configs.Count > 0).ToList();

            // NC-sweep aggregate (C0 only).
            var ncPoints = new List<BankVerifyAggregate.NcPoint>();
            var ncRows = new List<object>();
            foreach (var nc in ncSweep) {
                var c0 = ok.Select(r => r.configs.FirstOrDefault(c => c.config == $"C0@nc{nc:0.#}")).Where(c => c != null).ToList();
                var medRecallHigh = BankVerifyAggregate.Median(c0.Select(c => c.recallHigh));
                var medRecallAll = BankVerifyAggregate.Median(c0.Select(c => c.recallAll));
                var medPrec = BankVerifyAggregate.Median(c0.Select(c => c.precision));
                var medSigma = BankVerifyAggregate.Median(c0.Select(c => c.sigmaFocus));
                var medSR2 = BankVerifyAggregate.Median(c0.Select(c => c.sR2));
                ncPoints.Add(new BankVerifyAggregate.NcPoint { Nc = nc, MedianRecallHigh = medRecallHigh, MedianPrecision = medPrec });
                ncRows.Add(new { nc, medianRecallHigh = medRecallHigh, medianRecallAll = medRecallAll, medianPrecision = medPrec, medianSigmaFocus = medSigma, medianSensorR2 = medSR2, runs = c0.Count });
            }
            var rec = BankVerifyAggregate.RecommendNoiseClip(ncPoints, precisionFloor: 0.60);

            // A/B donut effect.
            int donutHelpedAF = 0, donutHurtSensor = 0, abRuns = 0;
            foreach (var r in ok) {
                var a = r.configs.FirstOrDefault(c => c.config == "A");
                var b = r.configs.FirstOrDefault(c => c.config == "B");
                if (a != null && b != null) {
                    abRuns++;
                    if (b.sigmaFocus < a.sigmaFocus) donutHelpedAF++;
                    if (b.sR2 < a.sR2) donutHurtSensor++;
                }
            }

            var json = new {
                schema = "afbank-verify/2",
                generatedUtc = utc,
                detectorCommit = commit,
                noiseClipDefault = 2.0,
                ncSweep,
                runCount = ok.Count,
                failed = runs.Count(r => r.error != null),
                ncSweepSummary = ncRows,
                ncRecommendation = new { rec.Nc, rec.Rationale, rec.ConsiderAdaptive },
                donutEffect = new { abRuns, donutHelpedAF, donutHurtSensor },
                aggregates = new {
                    medianRecallHighC0nc2 = BankVerifyAggregate.Median(ok.Select(r => r.configs.FirstOrDefault(c => c.config == "C0@nc2")?.recallHigh ?? double.NaN)),
                    medianAfSigmaA = BankVerifyAggregate.Median(ok.Select(r => r.configs.FirstOrDefault(c => c.config == "A")?.sigmaFocus ?? double.NaN)),
                    medianSensorR2A = BankVerifyAggregate.Median(ok.Select(r => r.configs.FirstOrDefault(c => c.config == "A")?.sR2 ?? double.NaN)),
                    donutAwareCount = ok.Count(r => r.donutAware)
                },
                runs
            };
            File.WriteAllText(Path.Combine(outDir, $"verification_{utc}.json"), JsonConvert.SerializeObject(json, Formatting.Indented));

            // Markdown.
            var sb = new StringBuilder();
            sb.AppendLine($"# AF-bank verification — {ok.Count} run(s)");
            sb.AppendLine();
            sb.AppendLine($"generated: {utc}  |  detector commit: {commit}  |  NoiseClip default = 2.0  |  NC sweep: {string.Join(", ", ncSweep.Select(x => x.ToString("0.#", CultureInfo.InvariantCulture)))}");
            sb.AppendLine();
            sb.AppendLine("## NoiseClippingMultiplier sweep (C0 as-default — the honest recall reference)");
            sb.AppendLine();
            sb.AppendLine("| NC | median recall@SNR≥12 | median recall@all | median precision | median AF σ_focus | median sensor R² | runs |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (dynamic row in ncRows) {
                sb.AppendLine($"| {((double)row.nc).ToString("0.#", CultureInfo.InvariantCulture)} | {Fmt((double)row.medianRecallHigh)} | {Fmt((double)row.medianRecallAll)} | {Fmt((double)row.medianPrecision)} | {Fmt((double)row.medianSigmaFocus)} | {Fmt((double)row.medianSensorR2)} | {row.runs} |");
            }
            sb.AppendLine();
            sb.AppendLine($"**Recommended default NoiseClippingMultiplier: {rec.Nc.ToString("0.#", CultureInfo.InvariantCulture)}** — {rec.Rationale}");
            if (rec.ConsiderAdaptive) {
                sb.AppendLine();
                sb.AppendLine("> Recall is still rising at the bottom of the swept range with acceptable precision, so a **per-frame adaptive** NoiseClippingMultiplier (derived from each frame's measured noise floor during optimization) may beat any single global default. See the per-run precision spread below.");
            }
            sb.AppendLine();
            sb.AppendLine($"Donut effect (A vs B over {abRuns} run(s) with both optimized configs): donut-aware tightened AF σ in **{donutHelpedAF}** and loosened the sensor fit in **{donutHurtSensor}**.");
            sb.AppendLine();
            sb.AppendLine("## Per-run (config rows)");
            sb.AppendLine();
            sb.AppendLine("| run | camera | golden (≥12) | donutAware | config | NC | recall@≥12 | recall@all | precision | AF σ | AF R² | sensor R² | RMS µm | sChi | tilt° | stars | aligned |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in runs) {
                if (r.error != null) { sb.AppendLine($"| {r.runId} | — | — | — | ERROR | | | | | | | | | | | | {r.error} |"); continue; }
                foreach (var c in r.configs) {
                    sb.AppendLine($"| {r.runId} | {r.camera} | {r.goldenStars} ({r.goldenSNRge12}) | {r.donutAware} | {c.config} | {Fmt(c.nc)} | {Fmt(c.recallHigh)} | {Fmt(c.recallAll)} | {Fmt(c.precision)} | {Fmt(c.sigmaFocus)} | {Fmt(c.afR2)} | {Fmt(c.sR2)} | {Fmt(c.sRMS)} | {Fmt(c.sChi)} | {Fmt(c.sTheta)} | {c.sStars} | {c.framesAligned}/{r.framesTotal} |");
                }
            }
            File.WriteAllText(Path.Combine(outDir, $"verification_{utc}.md"), sb.ToString());
        }

        // ---- DTOs ------------------------------------------------------------------------------------------------

        private class RunResult {
            public string runId { get; set; }
            public string camera { get; set; }
            public int goldenStars { get; set; }
            public int goldenSNRge12 { get; set; }
            public int framesTotal { get; set; }
            public bool donutAware { get; set; }
            public string donutHeuristic { get; set; }
            public string error { get; set; }
            public List<ConfigMetrics> configs { get; set; }
        }

        private class ConfigMetrics {
            public string config { get; set; }
            public double nc { get; set; }
            public double sensitivity { get; set; }
            public bool donut { get; set; }
            public double recallHigh { get; set; } = double.NaN;
            public double recallAll { get; set; } = double.NaN;
            public double precision { get; set; } = double.NaN;
            public double sigmaFocus { get; set; } = double.NaN;
            public double afR2 { get; set; } = double.NaN;
            public double afChi { get; set; } = double.NaN;
            public int sStars { get; set; }
            public double sR2 { get; set; } = double.NaN;
            public double sRMS { get; set; } = double.NaN;
            public double sChi { get; set; } = double.NaN;
            public double sTheta { get; set; } = double.NaN;
            public int framesAligned { get; set; }
        }

        private class RunMetaLite {
            public bool donutAware { get; set; }
            public string reason { get; set; }
        }
    }
}
