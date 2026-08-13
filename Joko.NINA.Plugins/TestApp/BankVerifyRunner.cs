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
using TestApp.SynthBank;
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

        /// <summary>The LocallyAdaptiveBinarization C0 actually runs with: the shipped default unless forced.</summary>
        private static bool EffectiveAdaptiveBinarize(bool? overrideValue)
            => overrideValue ?? HocusFocusStarDetection.BuildDefaultStarDetectorParams().LocallyAdaptiveBinarization;

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
                    "[--adaptive-binarize|--no-adaptive-binarize] [--adaptive-block 128] [--pixel-scale header|profile]");
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
            // header (default): PixelScaleForFrame reads each run's OWN frame header, since the bank spans wildly
            // different optics (real bank 0.74-5.97 arcsec/px; the synthetic bank's 40-3800mm range makes a single
            // profile-wide value meaningless). profile: the exact pre-V-P1 behavior (one value for the whole bank,
            // from the active NINA profile) — kept as an escape hatch and as the anchor guard for this change.
            var pixelScaleMode = string.Equals(DiagnosticUtil.GetArg(args, "--pixel-scale"), "profile", StringComparison.OrdinalIgnoreCase)
                ? "profile" : "header";
            var ncSweep = ParseNcSweep(DiagnosticUtil.GetArg(args, "--nc-sweep") ?? "2,3,4");
            // Spatially-adaptive binarization override for the C0 (as-default) configs — the flag-on-vs-off A/B that
            // the adaptive-noiseclip feature is validated by (docs/adaptive-noiseclip-design.md). The optimized A/B
            // configs instead receive these via OverlayOptimized.
            //
            // NEITHER flag given ⇒ C0 keeps whatever BuildDefaultStarDetectorParams() ships, so "as-default" cannot
            // drift from the product again. It did once: 9a80324 introduced this override when the shipped default was
            // OFF, c59a4b1 flipped the default ON the same day and did not update here, so every C0 row between then
            // and this fix ran with the feature OFF while the product shipped it ON — and the report recorded neither
            // fact. Pass --adaptive-binarize / --no-adaptive-binarize to force a side for the validation A/B.
            bool? adaptiveBinarizeOverride =
                DiagnosticUtil.HasFlag(args, "--adaptive-binarize") ? true
                : DiagnosticUtil.HasFlag(args, "--no-adaptive-binarize") ? false
                : (bool?)null;
            var adaptiveBlock = DiagnosticUtil.GetArg(args, "--adaptive-block");
            int adaptiveBlockSize = (adaptiveBlock != null && int.TryParse(adaptiveBlock, NumberStyles.Integer, CultureInfo.InvariantCulture, out var abv)) ? abv : 128;

            Logger.SetLogLevel(LogLevelEnum.INFO);
            // The WPF Application + dispatcher SynchronizationContext are set up by Run() on this STA thread.
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile
                ?? throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");
            // Detector settings come from the harness's LOCAL settings file, not the NINA profile: a
            // profile-sourced value is mutable machine state nothing records, and the ACTIVE profile can
            // even be a different telescope between runs. See HarnessSettingsStore.
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, activeProfile);
            var starDetectionOptions = new StarDetectionOptions(profileService, harnessSettings.Accessor);
            var accessor = harnessSettings.Accessor;
            var inspectorOptions = new InspectorOptions(profileService);
            // F58(d): the pinned settings FILE, not the active profile. `bank-verify` fits an AF curve per run and
            // its recall/precision numbers underpin the golden audits, so two results measured under different
            // profiles were never comparable -- MaxOutlierRejections alone partitions this machine's profiles 2/7,
            // and concurrent harness processes each silently acquire a different one.
            var autoFocusOptions = HarnessSettingsStore.BuildFitOptions(profileService, harnessSettings);
            var fitInputs = HarnessFitInputs.From(autoFocusOptions);
            const int binning = 1;
            // The pre-V-P1 bank-wide value: one profile-derived scale for every run. Still computed unconditionally
            // because it is both the "profile" mode's value AND the fallback "header" mode falls back to when a
            // run's own frame header carries no usable pixel size / focal length.
            var profilePixelScale = MathUtility.ArcsecPerPixel(activeProfile.CameraSettings.PixelSize, activeProfile.TelescopeSettings.FocalLength) * binning;
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
                perFilterStore: new StubPerFilterStarDetectionStore(accessor));

            var discovery = OptimizationRunDiscovery.Discover(runs);
            Console.WriteLine($"bank-verify: {discovery.Runs.Count} run(s) under {runs}; NC sweep [{string.Join(",", ncSweep.Select(x => x.ToString(CultureInfo.InvariantCulture)))}]; " +
                $"A={(optA ?? "(none)")} B={(optB ?? "(none)")}; matchRadius={matchRadius} (default, per-run may override from synthetic_meta.json); "
                + $"pixelScale={pixelScaleMode} (profile fallback={Fmt(profilePixelScale)} arcsec/px); "
                + $"adaptiveBinarize={EffectiveAdaptiveBinarize(adaptiveBinarizeOverride)}"
                + (adaptiveBinarizeOverride.HasValue ? " (forced)" : " (shipped default)")
                + $"(block={adaptiveBlockSize})");
            // F58(d): VALUES, not a hash. A hash says something moved; these say which one. Two bank-verify
            // results measured under different fit inputs are not comparable, and until this line existed there
            // was no way to tell from the output that they differed.
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");
            Console.WriteLine($"FitInputs: {fitInputs}");

            var runResults = new List<RunResult>();
            int idx = 0;
            foreach (var run in discovery.Runs) {
                idx++;
                Console.WriteLine($"[{idx}/{discovery.Runs.Count}] {run.RunId}");
                try {
                    var rr = await VerifyRunAsync(run, runs, outDir, ncSweep, optA, optB, goldenDir, matchRadius,
                        profileService, activeProfile, starDetectionOptions, inspectorOptions, autoFocusOptions, detector, detection, alglib,
                        profilePixelScale, pixelScaleMode, harnessSettings,
                        adaptiveBinarizeOverride, adaptiveBlockSize);
                    runResults.Add(rr);
                } catch (Exception ex) {
                    Console.Error.WriteLine($"  FAILED: {ex.GetType().Name}: {ex.Message}");
                    Logger.Error(ex, $"bank-verify failed for {run.RunId}");
                    runResults.Add(new RunResult { runId = run.RunId, error = ex.Message });
                }
            }

            var utc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            WriteReport(outDir, utc, commit, ncSweep, runResults,
                EffectiveAdaptiveBinarize(adaptiveBinarizeOverride), adaptiveBinarizeOverride.HasValue, adaptiveBlockSize, pixelScaleMode,
                fitInputs.ToString(), $"{activeProfile.Name} ({activeProfile.Id})");
            Console.WriteLine($"bank-verify: wrote verification_{utc}.{{json,md}} to {outDir} ({runResults.Count(r => r.error == null)} ok, {runResults.Count(r => r.error != null)} failed).");
        }

        // ---- per-run verification --------------------------------------------------------------------------------

        private static async Task<RunResult> VerifyRunAsync(
            OptimizationRunDiscovery.DiscoveredRun run, string runsRoot, string outDir, double[] ncSweep, string optA, string optB,
            string goldenDir, double matchRadius, ProfileService profileService, NINA.Profile.Interfaces.IProfile activeProfile,
            StarDetectionOptions sdOptions, InspectorOptions inspectorOptions, AutoFocusOptions afOptions,
            StarDetector detector, IHocusFocusStarDetection detection, AlglibAPI alglib,
            double profilePixelScale, string pixelScaleMode, HarnessSettingsStore.Resolved harnessSettings,
            bool? adaptiveBinarizeOverride, int adaptiveBlockSize) {

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

            // V-P1: pixel scale FOR THIS RUN, from the first loaded frame's own header (FITS XPIXSZ/FOCALLEN and
            // XISF equivalents) rather than one bank-wide value from the active profile — see
            // HarnessSettingsStore.PixelScaleForFrame's doc comment for why the profile is meaningless here (the
            // synthetic bank alone spans 40-3800mm focal length across four sensors). "profile" mode is the exact
            // pre-V-P1 behavior, kept as an escape hatch and as this change's anchor guard.
            double effectivePixelScale;
            string pixelScaleSource;
            if (pixelScaleMode == "profile") {
                effectivePixelScale = profilePixelScale;
                pixelScaleSource = "profile (forced via --pixel-scale profile)";
            } else {
                var firstFrameMeta = loaded[0].image.RawImageData?.MetaData;
                var headerScale = HarnessSettingsStore.PixelScaleForFrame(firstFrameMeta, harnessSettings, out var headerSource);
                if (double.IsFinite(headerScale)) {
                    effectivePixelScale = headerScale;
                    pixelScaleSource = headerSource;
                } else {
                    effectivePixelScale = profilePixelScale;
                    pixelScaleSource = $"profile (fallback: {headerSource})";
                }
            }
            Console.WriteLine($"  pixelScale: {Fmt(effectivePixelScale)} arcsec/px ({pixelScaleSource})");

            // V-P2: match radius FOR THIS RUN. The synthetic bank's generator computes matchRadiusPx from that
            // dataset's own defocus geometry and records it at the dataset root (synthetic_meta.json, one level
            // above the run's frame folder); when present it wins over the CLI/default value, which was tuned for
            // the real bank's typical star sizes and has no relationship to a synthetic dataset's geometry. Absent
            // (the entire real bank, and any synthetic dataset predating this field) the CLI/default value applies
            // unchanged.
            var syntheticMatchRadius = SyntheticDatasetMeta.TryReadMatchRadiusPx(runFolder, runsRoot);
            var effectiveMatchRadius = syntheticMatchRadius ?? matchRadius;
            var matchRadiusSource = syntheticMatchRadius.HasValue ? "synthetic_meta.json" : "CLI/default";
            Console.WriteLine($"  matchRadius: {Fmt(effectiveMatchRadius)}px ({matchRadiusSource})");

            // Golden sidecars per frame (shared across configs — detector-independent).
            var goldenByFocuser = new Dictionary<int, GoldenFrame>();
            // F31: the per-frame truth sidecar, when one exists (synthetic bank only). Its `omitted` /
            // `merged-into` entries are REAL stars the golden policy dropped, and detections of them were being
            // charged as false positives. Read once here and shared across configs, like the goldens.
            var truthByFocuser = new Dictionary<int, IReadOnlyList<SyntheticStarDisposition>>();
            int goldenStars = 0, goldenHigh = 0, protectedStars = 0;
            foreach (var (focuser, path, _) in loaded) {
                var gp = string.IsNullOrWhiteSpace(goldenDir) ? path : Path.Combine(goldenDir, Path.GetFileName(path));
                var gf = GoldenStarSetStore.LoadForImage(gp);
                if (gf != null) {
                    goldenByFocuser[focuser] = gf;
                    goldenStars += gf.Stars.Count;
                    goldenHigh += gf.Stars.Count(s => GoldenConfidence.Rank(s.Confidence) >= 3);
                }
                var td = TruthProtection.LoadForImage(path);
                if (td != null) {
                    truthByFocuser[focuser] = td;
                    protectedStars += TruthProtection.BuildProtectionBoxes(td, effectiveMatchRadius).Count;
                }
            }
            // W27 (A): the mode and its wording now come from TruthDisclosure, which `golden eval` also uses. The
            // emitted string is unchanged -- what changes is that the two harnesses can no longer word the same
            // disclosure differently, which is how they came to disagree about reporting it at all.
            var scoringMode = TruthDisclosure.ScoringMode(truthByFocuser.Count);
            Console.WriteLine(TruthDisclosure.ScoringLine(truthByFocuser.Count, protectedStars));

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
            rr.pixelScale = effectivePixelScale;
            rr.pixelScaleSource = pixelScaleSource;
            rr.matchRadius = effectiveMatchRadius;
            rr.matchRadiusSource = matchRadiusSource;
            rr.scoringMode = scoringMode;
            rr.protectedStars = protectedStars;

            StarDetectorParams BaseDefault() {
                var p = HocusFocusStarDetection.BuildDefaultStarDetectorParams();
                p.PixelScale = effectivePixelScale; p.Region = StarDetectionRegion.Full; p.ModelPSF = false; p.SaveIntermediateFilesPath = string.Empty;
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
                if (adaptiveBinarizeOverride.HasValue) {
                    p.LocallyAdaptiveBinarization = adaptiveBinarizeOverride.Value;
                }
                p.AdaptiveNoiseBlockSize = adaptiveBlockSize;
                var cm = await ScoreConfigAsync($"C0@nc{nc:0.#}", nc, false, p, evalData, loaded, goldenByFocuser, truthByFocuser, effectiveMatchRadius,
                    inspectorOptions, alglib, profileService, activeProfile, stepSize, detector, afOptions);
                rr.configs.Add(cm);
                Console.WriteLine($"    {cm.config}: recall@high={Fmt(cm.recallHigh)} prec={Fmt(cm.precision)} sigma={Fmt(cm.sigmaFocus)} sR^2={Fmt(cm.sR2)} aligned={cm.framesAligned}/{loaded.Count}");
            }

            // A — optimized, donut OFF.
            var aSettings = LoadOptimized(optA, run.RunId);
            if (aSettings != null) {
                var p = BaseDefault();
                OverlayOptimized(p, aSettings, forceDonutMaster: false);
                var cm = await ScoreConfigAsync("A", p.NoiseClippingMultiplier, p.DefocusAwareDonutDetection, p, evalData, loaded, goldenByFocuser, truthByFocuser, effectiveMatchRadius,
                    inspectorOptions, alglib, profileService, activeProfile, stepSize, detector, afOptions);
                cm.sensitivity = p.Sensitivity;
                rr.configs.Add(cm);
                Console.WriteLine($"    A (opt donutOFF, NC->{Fmt(cm.nc)}): recall@high={Fmt(cm.recallHigh)} prec={Fmt(cm.precision)} sigma={Fmt(cm.sigmaFocus)} sR^2={Fmt(cm.sR2)}");
            }

            // B — optimized, donut ON.
            var bSettings = LoadOptimized(optB, run.RunId);
            if (bSettings != null) {
                var p = BaseDefault();
                OverlayOptimized(p, bSettings, forceDonutMaster: true);
                var cm = await ScoreConfigAsync("B", p.NoiseClippingMultiplier, true, p, evalData, loaded, goldenByFocuser, truthByFocuser, effectiveMatchRadius,
                    inspectorOptions, alglib, profileService, activeProfile, stepSize, detector, afOptions);
                cm.sensitivity = p.Sensitivity;
                rr.configs.Add(cm);
                Console.WriteLine($"    B (opt donutON, NC->{Fmt(cm.nc)}): recall@high={Fmt(cm.recallHigh)} prec={Fmt(cm.precision)} sigma={Fmt(cm.sigmaFocus)} sR^2={Fmt(cm.sR2)}");
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
            List<(int focuser, string path, IRenderedImage image)> loaded, Dictionary<int, GoldenFrame> goldenByFocuser,
            Dictionary<int, IReadOnlyList<SyntheticStarDisposition>> truthByFocuser, double matchRadius,
            InspectorOptions inspectorOptions, AlglibAPI alglib, ProfileService profileService, NINA.Profile.Interfaces.IProfile activeProfile, int stepSize,
            StarDetector detector, AutoFocusOptions afOptions) {

            // effectiveSensitivity is set HERE, from the same params the config is scored with, so C0/A/B all get it
            // from one place (the A/B call sites re-stamp `sensitivity` afterwards; the effective gate must not
            // acquire a second, drift-prone assignment).
            var cm = new ConfigMetrics {
                config = label, nc = nc, donut = donut, sensitivity = p.Sensitivity,
                effectiveSensitivity = StarDetector.EffectiveSensitivityGate(p)
            };

            // AF fit (reuses the optimizer's evaluation path → reproduces the dry-run σ_focus).
            Prog($"  [{label}] EvaluateAndFitAsync start (NC={nc}, donut={donut})");
            var af = await evalData.EvaluateAndFitAsync(p, CancellationToken.None);
            cm.sigmaFocus = af.Metrics.SigmaFocus; cm.afR2 = af.Metrics.RSquared; cm.afChi = af.Metrics.ReducedChiSquared;
            Prog($"  [{label}] EvaluateAndFitAsync done sigma={cm.sigmaFocus:F3}; detect loop start");

            // Detect each frame once → golden P/R + sensor-model star lists.
            int tp = 0, fp = 0, fn = 0, matchedHigh = 0, totalHigh = 0, matchedAll = 0, totalAll = 0;
            // /5 instrument self-checks — see the header comment on the schema field.
            int detections = 0, truthViolations = 0, nullTp = 0, nullFp = 0;
            // W27 (A): protection EXERCISED. `protectedStars` on the run is protection AVAILABLE, and this runner
            // reported only that -- a run can carry hundreds of protected stars and exercise none of them, so
            // availability alone cannot be read as the size of the correction.
            int protectedDetections = 0;
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
                    // F31: subtract detections that land on something the reference cannot judge — the golden's
                    // own `unresolved`, PLUS the real-but-unboxed truth stars (`omitted` / `merged-into`) that the
                    // golden policy dropped. This runner previously did neither, so every detection of a
                    // sub-3.5-SNR real star was charged as a false positive; measured, 96% of the bank's reported
                    // false positives were real stars. Both are no-ops on the real bank (no truth sidecar, and a
                    // pre-v2 golden carries no `unresolved`).
                    //
                    // The two exclusions use DIFFERENT predicates, deliberately. The golden's `unresolved` boxes
                    // keep `GoldenMatch.ExcludeUnresolved`'s `Covers` semantics because that is the real bank's
                    // scoring path and moving it would break comparability with every published real-bank number.
                    // The truth protection uses the CENTROID predicate instead — the same one matching uses — so
                    // a detection is protected iff it would have been MATCHED had the star carried a golden box.
                    // `Covers` dilates each rect by the DETECTION'S OWN bounding box, which on a wing donut
                    // reaches far past the match radius; measured on the saved wave-1 dumps that read up to 0.011
                    // high, always in the flattering direction.
                    truthByFocuser.TryGetValue(focuser, out var td);
                    var unresolvedRects = (gf.Unresolved ?? new List<GoldenStarBox>())
                        .Select(b => new RectD(b.X, b.Y, b.W, b.H)).ToList();
                    var falsePositivesBeforeProtection = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, unresolvedRects);
                    var falsePositives = TruthProtection.ExcludeProtected(
                        falsePositivesBeforeProtection,
                        det, TruthProtection.ProtectionCenters(td), matchRadius);
                    tp += match.Pairs.Count; fp += falsePositives.Count; fn += match.FalseNegatives.Count;
                    detections += det.Count;
                    protectedDetections += falsePositivesBeforeProtection.Count - falsePositives.Count;

                    // The F31 signature, counted rather than reasoned about: a scored false positive that sits on
                    // a REAL rendered star. Complete by construction, so this has an exact answer and the answer
                    // must be 0. It was 52/318 on D09 and 79/337 on D17 under the /3 metric.
                    truthViolations += TruthProtection.CountWithinRadius(
                        falsePositives, det, TruthProtection.AllCenters(td), matchRadius);

                    // The NULL CONTROL. Same detections, translated with wraparound: whatever precision survives
                    // is chance coincidence. A metric reading 1.000 with a null near 0 is measuring; one reading
                    // 1.000 with a high null is saturated, which is how the first cut of the F31 repair failed.
                    if (td != null) {
                        var shifted = TruthProtection.ShiftForNullControl(det, props.Width, props.Height);
                        var nullMatch = GoldenMatch.Match(goldenRects, shifted, GoldenMatchMode.Centroid, 0.3, matchRadius);
                        var nullFalsePositives = TruthProtection.ExcludeProtected(
                            GoldenMatch.ExcludeUnresolved(nullMatch.FalsePositives, shifted, unresolvedRects),
                            shifted, TruthProtection.ProtectionCenters(td), matchRadius);
                        nullTp += nullMatch.Pairs.Count;
                        nullFp += nullFalsePositives.Count;
                    }
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
            cm.detections = detections;
            cm.truthViolations = truthViolations;
            cm.protectedDetections = protectedDetections;
            // How much of the detection set the precision ratio actually saw. Protection removes a detection from
            // BOTH sides rather than crediting it, so a low value does not bias precision — but it does mean the
            // number rests on a smaller sample, and at Sensitivity 0 that reaches ~57% on D17. Reported so a reader
            // can weigh a precision figure instead of assuming every detection was judged.
            cm.scoredFraction = detections > 0 ? (double)(tp + fp) / detections : double.NaN;
            cm.precisionNull = (nullTp + nullFp) > 0 ? (double)nullTp / (nullTp + nullFp) : double.NaN;

            // Sensor-model paraboloid fit (reuses SensorModel.RegisterStarsAndFit, like inspect-align).
            Prog($"  [{label}] golden done (P={cm.precision:F3} null={cm.precisionNull:F3} viol={cm.truthViolations} protDet={cm.protectedDetections} R@hi={cm.recallHigh:F3}); sensor fit start");
            if (cm.truthViolations > 0) {
                // Loud on purpose. This is the exact shape of F31, and four harness-calibration bugs have now
                // been found by someone happening to look rather than by anything failing. The wording is shared
                // with `golden eval` (TruthDisclosure) so the same defect reads the same way in both harnesses.
                Console.WriteLine(TruthDisclosure.ViolationLine(label, cm.truthViolations));
                Logger.Warning($"bank-verify {label}: {cm.truthViolations} scored false positives land within the match radius of a truth star (F31 regression)");
            }
            try {
                var focuserSizeMicrons = inspectorOptions.EffectiveMicronsPerFocuserStep > 0 ? inspectorOptions.EffectiveMicronsPerFocuserStep : 1.0;
                var pixelSize = activeProfile.CameraSettings.PixelSize > 0 ? activeProfile.CameraSettings.PixelSize : 3.76;
                var sortedFoc = loaded.Select(l => (double)l.focuser).OrderBy(x => x).ToList();
                var finalFocus = sortedFoc[sortedFoc.Count / 2];
                // F58(d): the run's own pinned fit options, not a second profile-backed instance. This was the
                // SECOND construction site in this file, and it fed the SENSOR model -- so the tilt fit was taking
                // its outlier-rejection budget from whichever profile happened to be active.
                var sensorModel = new SensorModel(profileService, inspectorOptions, afOptions, new AlglibAPI());
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

        private static void WriteReport(string outDir, string utc, string commit, double[] ncSweep, List<RunResult> runs,
            bool adaptiveBinarizeEffective, bool adaptiveBinarizeForced, int adaptiveBlockSize, string pixelScaleMode,
            string fitInputs, string profileId) {
            var ok = runs.Where(r => r.error == null && r.configs != null && r.configs.Count > 0).ToList();
            // Read from the product rather than hardcoded — see the json block below for why (9a80324/c59a4b1).
            var noiseClipDefault = HocusFocusStarDetection.BuildDefaultStarDetectorParams().NoiseClippingMultiplier;

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
                // V-P1/V-P2 (per-run pixel scale from the frame header, per-run match radius from synthetic_meta.json)
                // changed the C0/A/B numbers on a shared code path the real bank also runs through, so the schema
                // bumps to mark reports built before/after this change as not directly comparable.
                // F31 bumps this to /4: false positives are no longer charged for detections of real stars the
                // golden policy dropped, so precision from a /4 report is NOT comparable to a /3 one. Recall is
                // unchanged by construction (the golden's `stars` list still defines what must be found).
                // /5 makes the metric self-checking rather than merely repaired. Three additions:
                //   - truth protection now uses the CENTROID predicate matching itself uses, instead of
                //     GoldenMatch.Covers, which dilated every protection box by the detection's own bounding box
                //     and read up to 0.011 high on donut frames;
                //   - `precisionNull` reports what chance alone scores, so a 1.000 can be told from a saturated
                //     metric without re-deriving anything (the first cut of the F31 repair was saturated);
                //   - `truthViolations` counts scored false positives sitting on real rendered stars, which is
                //     the F31 signature itself and must be 0.
                // Precision from /5 is comparable to /4 to within the protection-predicate change above.
                schema = "afbank-verify/5",
                generatedUtc = utc,
                detectorCommit = commit,
                // The bank-wide --pixel-scale mode ("header" default, "profile" the pre-V-P1 escape hatch). The
                // per-run pixelScale/pixelScaleSource below is what actually applied — this is just the mode.
                pixelScaleMode,
                // Aggregate of the per-run scoringMode: "golden+truth-protected" iff every scored run had a
                // truth sidecar, "golden" iff none did, "mixed" otherwise (a bank holding both kinds).
                scoringMode = (runs.Count(r => r.scoringMode == TruthDisclosure.ModeTruthProtected),
                               runs.Count(r => r.scoringMode == TruthDisclosure.ModeGolden)) switch {
                    (> 0, 0) => TruthDisclosure.ModeTruthProtected,
                    (0, > 0) => TruthDisclosure.ModeGolden,
                    (0, 0) => "none",
                    _ => "mixed"
                },
                // Read from the product rather than hardcoded: a literal here said 2.0 while the shipped default was
                // 4.0, so the report misdescribed the very baseline it was measuring.
                noiseClipDefault,
                // C0's adaptive-binarization state is part of what "as-default" MEANT for this report. Recording it
                // is what would have made the 9a80324/c59a4b1 drift visible instead of silent.
                c0AdaptiveBinarization = adaptiveBinarizeEffective,
                c0AdaptiveBinarizationForced = adaptiveBinarizeForced,
                c0AdaptiveNoiseBlockSize = adaptiveBlockSize,
                // F58(d): the four values that reach the AF fit, and the profile the run loaded. Recorded as
                // VALUES so a reader can diff a field rather than infer which machine state produced a number --
                // the whole class of defect F58 named. `fitInputs` now comes from the pinned settings FILE, so
                // two reports with the same string are comparable regardless of which profile was active.
                fitInputs,
                profileId,
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
            sb.AppendLine($"# AF-bank verification -- {ok.Count} run(s)");
            sb.AppendLine();
            sb.AppendLine($"generated: {utc}  |  detector commit: {commit}  |  NoiseClip default = {noiseClipDefault.ToString("0.#", CultureInfo.InvariantCulture)}  |  " +
                $"pixel-scale mode: {pixelScaleMode}  |  NC sweep: {string.Join(", ", ncSweep.Select(x => x.ToString("0.#", CultureInfo.InvariantCulture)))}");
            sb.AppendLine();
            sb.AppendLine("## NoiseClippingMultiplier sweep (C0 as-default -- the honest recall reference)");
            sb.AppendLine();
            sb.AppendLine("| NC | median recall@SNR>=12 | median recall@all | median precision | median AF sigma_focus | median sensor R^2 | runs |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (dynamic row in ncRows) {
                sb.AppendLine($"| {((double)row.nc).ToString("0.#", CultureInfo.InvariantCulture)} | {Fmt((double)row.medianRecallHigh)} | {Fmt((double)row.medianRecallAll)} | {Fmt((double)row.medianPrecision)} | {Fmt((double)row.medianSigmaFocus)} | {Fmt((double)row.medianSensorR2)} | {row.runs} |");
            }
            sb.AppendLine();
            sb.AppendLine($"**Recommended default NoiseClippingMultiplier: {rec.Nc.ToString("0.#", CultureInfo.InvariantCulture)}** -- {rec.Rationale}");
            if (rec.ConsiderAdaptive) {
                sb.AppendLine();
                sb.AppendLine("> Recall is still rising at the bottom of the swept range with acceptable precision, so a **per-frame adaptive** NoiseClippingMultiplier (derived from each frame's measured noise floor during optimization) may beat any single global default. See the per-run precision spread below.");
            }
            sb.AppendLine();
            sb.AppendLine($"Donut effect (A vs B over {abRuns} run(s) with both optimized configs): donut-aware tightened AF sigma in **{donutHelpedAF}** and loosened the sensor fit in **{donutHurtSensor}**.");
            sb.AppendLine();
            sb.AppendLine("## Per-run pixel scale & match radius (V-P1 / V-P2)");
            sb.AppendLine();
            sb.AppendLine("| run | pixelScale (arcsec/px) | source | matchRadius (px) | source |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in runs) {
                if (r.error != null) { continue; }
                sb.AppendLine($"| {r.runId} | {Fmt(r.pixelScale)} | {r.pixelScaleSource} | {Fmt(r.matchRadius)} | {r.matchRadiusSource} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Per-run (config rows)");
            sb.AppendLine();
            sb.AppendLine("| run | camera | golden (>=12) | donutAware | config | NC | recall@>=12 | recall@all | precision | null | scored | viol | AF sigma | AF R^2 | sensor R^2 | RMS um | sChi | tilt deg | stars | aligned |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in runs) {
                if (r.error != null) { sb.AppendLine($"| {r.runId} | -- | -- | -- | ERROR | | | | | | | | | | | | | | | {r.error} |"); continue; }
                foreach (var c in r.configs) {
                    sb.AppendLine($"| {r.runId} | {r.camera} | {r.goldenStars} ({r.goldenSNRge12}) | {r.donutAware} | {c.config} | {Fmt(c.nc)} | {Fmt(c.recallHigh)} | {Fmt(c.recallAll)} | {Fmt(c.precision)} | {Fmt(c.precisionNull)} | {Fmt(c.scoredFraction)} | {c.truthViolations} | {Fmt(c.sigmaFocus)} | {Fmt(c.afR2)} | {Fmt(c.sR2)} | {Fmt(c.sRMS)} | {Fmt(c.sChi)} | {Fmt(c.sTheta)} | {c.sStars} | {c.framesAligned}/{r.framesTotal} |");
                }
            }
            sb.AppendLine();
            sb.AppendLine("**null** is the precision the same detections earn after being translated with wraparound -- chance alone. "
                + "It is the floor this metric can read; a precision of 1.000 means the detector found no false positives only when null is near 0. "
                + "**scored** is the fraction of detections that entered the precision ratio at all (the rest sit on reference objects that cannot be judged). "
                + "**viol** counts scored false positives sitting on a real rendered star and MUST be 0 -- see F31.");
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
            // V-P1: the effective PixelScale (arcsec/binned-px) this run's configs were detected at, and which of
            // "frame header" / "settings file (...)" / "profile (...)" produced it — see VerifyRunAsync.
            public double pixelScale { get; set; } = double.NaN;
            public string pixelScaleSource { get; set; }
            // V-P2: the effective centroid match radius (px) this run's golden P/R was scored at, and whether it
            // came from the dataset's own synthetic_meta.json or the CLI/default.
            public double matchRadius { get; set; } = double.NaN;
            public string matchRadiusSource { get; set; }
            // F31: "golden+truth-protected" when a per-frame truth sidecar was found and its real-but-unboxed
            // stars (omitted / merged-into) were excluded from false-positive scoring; "golden" otherwise (the
            // real bank, and every report written before this fix). Reports differing here are NOT comparable
            // on precision.
            public string scoringMode { get; set; }
            public int protectedStars { get; set; }
            public string error { get; set; }
            public List<ConfigMetrics> configs { get; set; }
        }

        private class ConfigMetrics {
            public string config { get; set; }
            public double nc { get; set; }
            public double sensitivity { get; set; }

            /// <summary>
            /// F33 — <c>max(sensitivity, PeakResponse x effective StarClip)</c>: the gate this config actually
            /// enforces. Read this, not <see cref="sensitivity"/>, when asking how hard a config is culling.
            /// A config can record <c>sensitivity 0.0</c> — the synthetic bank's "drove the gate to its floor"
            /// signature — while enforcing several times the shipped default; 2 of the 6 synthetic-bank
            /// Sensitivity-0.0 landings (D11 at 2.36, D12 at 2.10) are exactly that, as is the real bank's
            /// mccomiskey at 9.81.
            ///
            /// <para>ADDITIVE and derived, so the <c>afbank-verify</c> schema is deliberately NOT bumped: no
            /// number changes and every prior report stays comparable. The /4 and /5 bumps were for changes that
            /// made numbers non-comparable, which this is not.</para>
            /// </summary>
            public double effectiveSensitivity { get; set; } = double.NaN;

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
            /// <summary>Accepted stars this config detected across the run's scored frames — the denominator
            /// <see cref="scoredFraction"/> is a fraction of.</summary>
            public int detections { get; set; }
            /// <summary>Scored false positives that sit within the match radius of a REAL rendered truth star.
            /// MUST be 0: synthetic truth is complete by construction, so a detection on a rendered star is
            /// correct whatever tier the golden policy gave it. Non-zero means F31 has regressed and this run's
            /// precision is not trustworthy. Always 0 on the real bank, which has no truth sidecar.</summary>
            public int truthViolations { get; set; }
            /// <summary>Protection EXERCISED: how many detections <c>TruthProtection.ExcludeProtected</c> actually
            /// removed from this config's false-positive count. The run-level <c>protectedStars</c> is protection
            /// AVAILABLE and was, until W27, the only one of the pair reported — a run can carry hundreds of
            /// protected stars and exercise none of them, so availability alone does not tell a reader how big
            /// the correction to precision was. ADDITIVE and derived, so the <c>afbank-verify</c> schema is
            /// deliberately NOT bumped: no number changes and every prior report stays comparable.</summary>
            public int protectedDetections { get; set; }
            /// <summary>Fraction of <see cref="detections"/> that entered the precision ratio (the rest landed on
            /// protected or unresolved reference objects and are unjudgeable). Not a bias — protection removes a
            /// detection from both numerator and denominator — but precision rests on a smaller sample as this
            /// falls, and at Sensitivity 0 it reaches ~0.57.</summary>
            public double scoredFraction { get; set; } = double.NaN;
            /// <summary>NULL CONTROL: precision after translating every detection with wraparound, destroying
            /// correspondence with the frame while preserving count and clustering. This is the score chance
            /// alone earns, and therefore the floor the metric cannot read below. A precision of 1.000 means
            /// something only when this is near 0 — the first cut of the F31 repair read 1.000 everywhere
            /// BECAUSE it was saturated, which the precision column alone cannot distinguish. NaN on the real
            /// bank (no truth sidecar, so no synthetic null is defined).</summary>
            public double precisionNull { get; set; } = double.NaN;
            public int framesAligned { get; set; }
        }

        private class RunMetaLite {
            public bool donutAware { get; set; }
            public string reason { get; set; }
        }
    }
}
