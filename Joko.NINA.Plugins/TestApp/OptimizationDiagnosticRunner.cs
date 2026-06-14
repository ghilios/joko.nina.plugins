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
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
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
using System.Text.RegularExpressions;
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

        // Mirrors AutoFocusEngine.IMAGE_FILE_REGEX (private) so the harness stays decoupled from the engine, and
        // matches FocusSweepDiagnosticRunner's copy. Frames the AF engine writes look like
        // "0_Frame1_BitDepth16_Bayered0_Focuser5000.fits" (the trailing _HFRxxx is optional).
        private static readonly Regex ImageFileRegex = new Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

            var labelsDir = DiagnosticUtil.GetArg(args, "--labels");

            // Verbose logging to the NINA log file for offline inspection.
            Logger.SetLogLevel(LogLevelEnum.TRACE);

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

            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new InvalidOperationException("Could not resolve the HocusFocus plugin assembly GUID");
            }
            var accessor = new PluginOptionsAccessor(profileService, guid.Value);
            var starDetectionOptions = new StarDetectionOptions(profileService, accessor);
            var afOptions = new AutoFocusOptions(profileService);

            // PixelScale = arcsec/pixel from the profile (pixel size / focal length) × binning, mirroring
            // HocusFocusStarDetection.GetStarDetectorParams. Production reads binning from per-frame metadata
            // (image.RawImageData.MetaData.Camera.BinX, which defaults to 1 when unset); the harness loads raw
            // float Mats with no NINA metadata, so binning = 1 is used (the same value an unbinned/unset frame
            // yields in production). PixelScale only feeds PixelScale-dependent detection gates, not the curve fit.
            const int binning = 1;
            var pixelScale = MathUtility.ArcsecPerPixel(activeProfile.CameraSettings.PixelSize, activeProfile.TelescopeSettings.FocalLength) * binning;
            if (double.IsNaN(pixelScale)) {
                Console.WriteLine("WARNING: PixelScale is NaN (pixel size / focal length not set in the profile). Detection will still run; PixelScale-dependent gates use NaN.");
                Logger.Warning("PixelScale is NaN; pixel size / focal length not set in the profile");
            }

            // Seed params = the SAME mapping NINA uses (BuildStarDetectorParams), with the AF overrides
            // GetStarDetectorParams(..., isAutoFocus:true) applies: ModelPSF=false, no intermediate files, plus
            // PixelScale and Region=Full. This is exactly the bundle the optimizer starts from and tunes.
            var seed = HocusFocusStarDetection.BuildStarDetectorParams(starDetectionOptions);
            seed.PixelScale = pixelScale;
            seed.Region = StarDetectionRegion.Full;
            seed.ModelPSF = false;
            seed.SaveIntermediateFilesPath = string.Empty;
            // The optimizer scores the full accepted set, so keep contaminated stars only if the profile says to.
            // Leave RejectContaminatedStars exactly as the profile resolved it (matches production AF).

            Console.WriteLine($"Seed params: Sensitivity={F(seed.Sensitivity)}, StarClippingMultiplier={F(seed.StarClippingMultiplier)}, " +
                $"NoiseClippingMultiplier={F(seed.NoiseClippingMultiplier)}, StructureLayers={seed.StructureLayers}, PixelScale={F(seed.PixelScale)}, " +
                $"MeasurementAverage={starDetectionOptions.MeasurementAverage}");

            // The fixed AF-detection sigma rejections the wizard loader uses (HocusFocusDetectionParams defaults):
            // high = 4.0, low = 3.0. These feed the MeanOutliers HFR aggregation only.
            const double highSigmaOutlierRejection = 4.0;
            const double lowSigmaOutlierRejection = 3.0;

            // Discover runs: each immediate subfolder that contains AF frames is one run; if the --runs folder
            // itself contains AF frames it is treated as a single run (so a single attempt folder works directly).
            var discovered = DiscoverRuns(runsDir);
            if (discovered.Count == 0) {
                throw new InvalidOperationException(
                    $"No AF frames matched the saved-run filename pattern under {runsDir}. Expected files like " +
                    "'0_Frame1_BitDepth16_Bayered0_Focuser5000.fits' in the folder or an immediate subfolder.");
            }
            Console.WriteLine($"Discovered {discovered.Count} run(s):");
            foreach (var d in discovered) {
                Console.WriteLine($"  {d.RunId}: {d.Frames.Count} frames, {d.Frames.Select(f => f.FocuserPosition).Distinct().Count()} positions");
            }

            var labelsByRun = LoadLabels(labelsDir, discovered);

            var alglibAPI = new AlglibAPI();
            var detector = new StarDetector(alglibAPI);

            // Build a RunEvaluationData per discovered run: load every frame once as a float Mat, wire the harness
            // detection delegate (StarDetector.Detect(Mat) -> FrameDetectionResult), and infer the fit config.
            var loadedRuns = new List<LoadedHarnessRun>(discovered.Count);
            foreach (var d in discovered) {
                var frames = new List<RunFrame>(d.Frames.Count);
                foreach (var frame in d.Frames) {
                    // LoadFloatMat returns a fresh Mat each call; the optimizer detect delegate clones before
                    // detection (Detect mutates its input), so the cached Mat here is never mutated.
                    var mat = await DiagnosticUtil.LoadFloatMat(frame.Path, profileService).ConfigureAwait(false);
                    frames.Add(new RunFrame {
                        FrameId = frame.Path,
                        FocuserPosition = frame.FocuserPosition,
                        Image = mat
                    });
                }

                var stepSize = InferStepSize(d.Frames);
                var fitConfig = new RunFitConfig {
                    StepSize = stepSize,
                    UseWeights = afOptions.WeightedHyperbolicFitEnabled,
                    MaxOutlierRejections = afOptions.MaxOutlierRejections,
                    RejectionConfidence = afOptions.OutlierRejectionConfidence,
                    PreferredModel = null
                };

                var measurementAverage = starDetectionOptions.MeasurementAverage;
                Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect =
                    async (image, candidateParams, ct) => {
                        // Detect mutates its input in place, so each detection gets a clone of the cached Mat.
                        using var clone = ((Mat)image).Clone();
                        var result = await detector.Detect(clone, candidateParams, null, ct).ConfigureAwait(false);
                        return ToFrameDetectionResult(result, measurementAverage, highSigmaOutlierRejection, lowSigmaOutlierRejection);
                    };

                var labels = labelsByRun.TryGetValue(d.RunId, out var ls) ? ls : null;
                var data = new RunEvaluationData(d.RunId, frames, detect, alglibAPI, fitConfig, labels);
                loadedRuns.Add(new LoadedHarnessRun { Discovered = d, Data = data, StepSize = stepSize, Frames = frames });
                Console.WriteLine($"  {d.RunId}: inferred step size {stepSize}" + (labels != null ? $", {labels.Count} labeled position(s)" : ", unlabeled"));
            }

            var settings = new OptimizerSettings();
            if (maxEvals.HasValue) {
                settings.MaxEvaluations = maxEvals.Value;
            }

            var variables = OptimizerVariable.CreateCuratedSet();
            var optimizer = new StarDetectionOptimizer();

            // Run the optimizer over ALL discovered runs at once (single-run reduces to N=1; N>1 is the balanced
            // min/mean blend via JTotal). This is the configuration the wizard ships.
            var dataList = loadedRuns.Select(r => r.Data).ToList();
            var evaluator = RunEvaluationData.CreateEvaluator(dataList);

            var progress = new Progress<OptimizationProgress>(op =>
                Console.WriteLine($"  [{op.Phase}] evals={op.Evaluations}/{op.MaxEvaluations} bestJ={F(op.BestJ)} seedJ={F(op.SeedJ)}"));

            Console.WriteLine($"Optimizing over {dataList.Count} run(s) (MaxEvaluations={settings.MaxEvaluations}, {(labelsDir != null ? "labeled" : "unlabeled")})...");
            var result = await optimizer.OptimizeAsync(seed, variables, evaluator, settings, progress, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Optimization complete: seedJ={F(result.SeedJ)} -> bestJ={F(result.BestJ)} ({(result.ImprovedOverSeed ? "improved" : "no improvement")}), evals={result.Evaluations}");

            // Evaluate the seed and the winning params against each run, capturing the fit + per-frame counts.
            var perRunSeed = new List<RunEvaluationResult>(loadedRuns.Count);
            var perRunBest = new List<RunEvaluationResult>(loadedRuns.Count);
            foreach (var r in loadedRuns) {
                perRunSeed.Add(await r.Data.EvaluateAndFitAsync(seed, CancellationToken.None).ConfigureAwait(false));
                perRunBest.Add(await r.Data.EvaluateAndFitAsync(result.BestParams, CancellationToken.None).ConfigureAwait(false));
            }

            // Hard-floor assertion: every frame (incl. defocused extremes) must keep >= NHard accepted stars under
            // the winning params. This is the optimizer's hard constraint; a FAIL means the winner starves a frame.
            var objectiveConstants = new ObjectiveConstants();
            var (passed, worstFrameCount, worstRunId) = AssertHardFloor(loadedRuns, perRunBest, objectiveConstants);
            Console.WriteLine(passed
                ? $"PASS: every frame has >= {objectiveConstants.NHard} stars under optimized params (min observed = {worstFrameCount})"
                : $"FAIL: at least one frame has < {objectiveConstants.NHard} stars under optimized params (min observed = {worstFrameCount} in run {worstRunId})");
            if (!passed) {
                Environment.ExitCode = 3;
            }

            // Each run's step-size recommendation (from its own winning fit) is written to the summary. The
            // focuser's max step is unavailable headless, so it is left null — StepSizeRecommender clamps to >= 1.
            var focuserMaxStep = (int?)null;

            WriteSummary(Path.Combine(outDir, "optimize_summary.txt"), runsDir, seed, result, variables,
                loadedRuns, perRunSeed, perRunBest, objectiveConstants, focuserMaxStep, labelsDir, settings, passed, worstFrameCount, worstRunId);
            WriteCsv(Path.Combine(outDir, "optimize_result.csv"), loadedRuns, perRunSeed, perRunBest);

            // Annotated PNGs (per --annotate) using the OPTIMIZED params, so a real star with no marker reads as
            // "missed entirely". Annotate the min/max focuser frame per run by default, or every frame for "all".
            await WriteAnnotatedFrames(outDir, loadedRuns, result.BestParams, detector,
                starDetectionOptions.MeasurementAverage, highSigmaOutlierRejection, lowSigmaOutlierRejection, annotateAll).ConfigureAwait(false);

            // Release the cached frame Mats (each RunFrame.Image is a Mat the harness loaded once).
            foreach (var r in loadedRuns) {
                foreach (var rf in r.Frames) {
                    (rf.Image as Mat)?.Dispose();
                }
            }

            Console.WriteLine($"Wrote optimize_summary.txt, optimize_result.csv, and annotated PNG(s) to {outDir}");
        }

        private static void PrintUsage() {
            Console.Error.WriteLine("Usage: TestApp optimize --runs <folder> [--profile-id <guid>] [--out <dir>] [--max-evals <int>] [--annotate extremes|all] [--labels <dir>]");
            Console.Error.WriteLine("  --runs       (required) folder of saved AF runs. Each immediate subfolder with AF frames is one run; or the folder itself is a single run.");
            Console.Error.WriteLine("  --profile-id (default active) NINA profile id to load (settings + PixelScale).");
            Console.Error.WriteLine("  --out        (default %LOCALAPPDATA%\\NINA\\Logs\\hf-diag\\optimize\\<timestamp>) output directory.");
            Console.Error.WriteLine("  --max-evals  (optional) override the optimizer's MaxEvaluations budget.");
            Console.Error.WriteLine("  --annotate   (default extremes) annotate only min/max-focuser frames, or 'all' frames.");
            Console.Error.WriteLine("  --labels     (optional) folder of label JSON files; activates the recall/precision objective term.");
        }

        // ---- Run / frame discovery -------------------------------------------------------------------------

        private sealed class FrameRef {
            public string Path;
            public int FocuserPosition;
        }

        private sealed class DiscoveredRun {
            public string RunId;
            public List<FrameRef> Frames;
        }

        private sealed class LoadedHarnessRun {
            public DiscoveredRun Discovered;
            public RunEvaluationData Data;
            public int StepSize;
            public List<RunFrame> Frames; // the loaded-once frame Mats, retained for disposal
        }

        /// <summary>
        /// Discovers runs under <paramref name="runsDir"/>. If the folder itself contains AF frames it is a single
        /// run; otherwise every immediate subfolder that contains AF frames is treated as a separate run. RunId is
        /// the folder name (or the runs-dir name for the single-run case). Deterministic (ordinal) order.
        /// </summary>
        private static List<DiscoveredRun> DiscoverRuns(string runsDir) {
            var root = new DirectoryInfo(runsDir);
            var runs = new List<DiscoveredRun>();

            var rootFrames = MatchFrames(root);
            if (rootFrames.Count > 0) {
                runs.Add(new DiscoveredRun { RunId = root.Name, Frames = rootFrames });
                return runs;
            }

            foreach (var sub in root.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.Ordinal)) {
                var frames = MatchFrames(sub);
                if (frames.Count == 0) {
                    // Also accept a parent that wraps a single attempt* subfolder (the saved AF layout).
                    var attempt = sub.EnumerateDirectories("attempt*").FirstOrDefault();
                    if (attempt != null) {
                        frames = MatchFrames(attempt);
                    }
                }
                if (frames.Count > 0) {
                    runs.Add(new DiscoveredRun { RunId = sub.Name, Frames = frames });
                }
            }
            return runs;
        }

        private static List<FrameRef> MatchFrames(DirectoryInfo dir) {
            var frames = new List<FrameRef>();
            if (!dir.Exists) {
                return frames;
            }
            foreach (var file in dir.GetFiles().OrderBy(f => f.Name, StringComparer.Ordinal)) {
                var m = ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
                if (!m.Success) {
                    continue;
                }
                if (!int.TryParse(m.Groups["FOCUSER"].Value, out var pos)) {
                    continue;
                }
                frames.Add(new FrameRef { Path = file.FullName, FocuserPosition = pos });
            }
            return frames;
        }

        /// <summary>
        /// Infers the AF step size from the frames exactly as <c>AutoFocusEngine.LoadSavedAttemptImpl</c> does:
        /// the absolute difference of the two smallest DISTINCT focuser positions. 0 when fewer than 2 positions.
        /// </summary>
        private static int InferStepSize(List<FrameRef> frames) {
            var positions = frames.Select(f => f.FocuserPosition).Distinct().OrderBy(x => x).Take(2).ToList();
            return positions.Count > 1 ? Math.Abs(positions[0] - positions[1]) : 0;
        }

        // ---- Production-matching HFR aggregation ------------------------------------------------------------

        /// <summary>
        /// Adapts a <see cref="HocusFocusStarDetectorResult"/> (from <see cref="StarDetector.Detect(Mat, StarDetectorParams, IProgress{NINA.Core.Model.ApplicationStatus}, CancellationToken)"/>)
        /// into the <see cref="FrameDetectionResult"/> the optimizer consumes, REPLICATING the production HFR
        /// aggregation in <c>HocusFocusStarDetection.Detect</c> so the harness's per-frame HFR/σ match the live AF
        /// path exactly. The matched production code:
        ///   - MeanOutliers: first re-filter the star list to HFR ∈ [median − low·MAD, median + high·MAD]
        ///     (HocusFocusDetection.Detect lines ~389-399, sigmas 4.0/3.0 from HocusFocusDetectionParams), then
        ///     AverageHFR = mean, HFRStdDev = sample std-dev (n-1) (lines ~440-442).
        ///   - Median (default): AverageHFR = median, HFRStdDev = MAD (lines ~446-448).
        ///   - Both only computed when the surviving list has &gt; 1 star; otherwise AverageHFR/HFRStdDev stay 0.
        /// StarCount and StarCenters are taken from the SURVIVING accepted set (post outlier-filter for
        /// MeanOutliers) to match what production counts and centroids.
        /// </summary>
        private static FrameDetectionResult ToFrameDetectionResult(
            HocusFocusStarDetectorResult result, MeasurementAverageEnum measurementAverage,
            double highSigmaOutlierRejection, double lowSigmaOutlierRejection) {
            var stars = result.DetectedStars ?? new List<Star>();

            // Production MeanOutliers re-filters the list before averaging (only when count > 1).
            if (stars.Count > 1 && measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                var (median, mad) = stars.Select(s => s.HFR).MedianMAD();
                var hi = median + highSigmaOutlierRejection * mad;
                var lo = median - lowSigmaOutlierRejection * mad;
                stars = stars.Where(s => s.HFR <= hi && s.HFR >= lo).ToList();
            }

            double averageHfr = 0.0;
            double hfrStdDev = 0.0;
            if (stars.Count > 1) {
                if (measurementAverage == MeasurementAverageEnum.MeanOutliers) {
                    averageHfr = stars.Average(s => s.HFR);
                    var variance = stars.Sum(s => (s.HFR - averageHfr) * (s.HFR - averageHfr)) / (stars.Count - 1);
                    hfrStdDev = Math.Sqrt(variance);
                } else {
                    var (hfrMedian, hfrMAD) = stars.Select(s => s.HFR).MedianMAD();
                    averageHfr = hfrMedian;
                    hfrStdDev = hfrMAD;
                }
            }

            var centers = stars.Select(s => (s.Center.X, s.Center.Y)).ToList();
            return new FrameDetectionResult {
                AverageHFR = averageHfr,
                HFRStdDev = hfrStdDev,
                StarCount = stars.Count,
                StarCenters = centers
            };
        }

        // ---- Labels ----------------------------------------------------------------------------------------

        /// <summary>
        /// Forward-compatible label JSON shape (T7 writes these; T6 reads them). One file per run, named
        /// "&lt;runId&gt;.json" (the discovered RunId == the run's folder name), or any *.json whose embedded RunId
        /// matches a discovered run. Shape:
        /// <code>
        /// {
        ///   "runId": "attempt01",
        ///   "radiusPx": 6.0,                       // default radius for positions that omit one
        ///   "positions": [
        ///     {
        ///       "focuserPosition": 5000,
        ///       "radiusPx": 6.0,                    // optional per-position override
        ///       "missed":       [ { "x": 123.4, "y": 567.8 }, ... ],   // false negatives to recover (recall)
        ///       "shouldReject": [ { "x": 12.0,  "y": 34.0  }, ... ]    // false positives to exclude (precision)
        ///     }
        ///   ]
        /// }
        /// </code>
        /// </summary>
        private sealed class LabelPoint {
            [JsonProperty("x")] public double X { get; set; }
            [JsonProperty("y")] public double Y { get; set; }
        }

        private sealed class LabelPosition {
            [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
            [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
            [JsonProperty("missed")] public List<LabelPoint> Missed { get; set; }
            [JsonProperty("shouldReject")] public List<LabelPoint> ShouldReject { get; set; }
        }

        private sealed class RunLabelFile {
            [JsonProperty("runId")] public string RunId { get; set; }
            [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
            [JsonProperty("positions")] public List<LabelPosition> Positions { get; set; }
        }

        private const double DefaultLabelRadiusPx = 6.0;

        private static Dictionary<string, IReadOnlyList<FrameLabels>> LoadLabels(string labelsDir, List<DiscoveredRun> runs) {
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
                if (!byRunId.TryGetValue(run.RunId, out var lf) || lf.Positions == null) {
                    continue;
                }
                var defaultRadius = lf.RadiusPx ?? DefaultLabelRadiusPx;
                var frameLabels = new List<FrameLabels>(lf.Positions.Count);
                foreach (var pos in lf.Positions) {
                    frameLabels.Add(new FrameLabels {
                        FocuserPosition = pos.FocuserPosition,
                        RadiusPx = pos.RadiusPx ?? defaultRadius,
                        Missed = (pos.Missed ?? new List<LabelPoint>()).Select(p => (p.X, p.Y)).ToList(),
                        ShouldReject = (pos.ShouldReject ?? new List<LabelPoint>()).Select(p => (p.X, p.Y)).ToList()
                    });
                }
                result[run.RunId] = frameLabels;
            }
            return result;
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
            string path, string runsDir, StarDetectorParams seed, OptimizationResult result,
            IReadOnlyList<OptimizerVariable> variables, List<LoadedHarnessRun> runs,
            List<RunEvaluationResult> perRunSeed, List<RunEvaluationResult> perRunBest,
            ObjectiveConstants c, int? focuserMaxStep, string labelsDir, OptimizerSettings settings,
            bool passed, int worstFrameCount, string worstRunId) {
            var sb = new StringBuilder();
            sb.AppendLine("=== Star Detection Optimizer — headless harness ===");
            sb.AppendLine($"Runs root: {runsDir}");
            sb.AppendLine($"Runs: {runs.Count} ({string.Join(", ", runs.Select(r => r.Discovered.RunId))})");
            sb.AppendLine($"Labels: {(string.IsNullOrWhiteSpace(labelsDir) ? "(none — unlabeled)" : labelsDir)}");
            sb.AppendLine($"MaxEvaluations: {settings.MaxEvaluations}; evaluator calls: {result.Evaluations}");
            sb.AppendLine();

            sb.AppendLine("--- Objective ---");
            sb.AppendLine($"Seed J : {F(result.SeedJ)}");
            sb.AppendLine($"Best J : {F(result.BestJ)}  ({(result.ImprovedOverSeed ? "improved over seed" : "no improvement over seed")})");
            sb.AppendLine();

            sb.AppendLine("--- Per-run σ_focus (seed -> optimized) and recommended step size ---");
            for (int i = 0; i < runs.Count; i++) {
                var run = runs[i];
                var seedM = perRunSeed[i].Metrics;
                var bestM = perRunBest[i].Metrics;
                var rec = StepSizeRecommender.Recommend(perRunBest[i].BestFit, run.StepSize, focuserMaxStep);
                sb.AppendLine($"  {run.Discovered.RunId}:");
                sb.AppendLine($"    σ_focus       : {F(seedM.SigmaFocus)} -> {F(bestM.SigmaFocus)}");
                sb.AppendLine($"    R²            : {F(seedM.RSquared)} -> {F(bestM.RSquared)}");
                sb.AppendLine($"    reducedχ²     : {F(seedM.ReducedChiSquared)} -> {F(bestM.ReducedChiSquared)}");
                sb.AppendLine($"    current step  : {run.StepSize}");
                sb.AppendLine($"    recommended   : step {rec.StepSize}, offset {rec.OffsetSteps} per side (half-width {F(rec.HalfWidth)})");
                if (bestM.Recall.HasValue || bestM.Precision.HasValue) {
                    sb.AppendLine($"    recall/prec   : {F(bestM.Recall ?? double.NaN)} / {F(bestM.Precision ?? double.NaN)}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("--- Curated params (seed -> optimized) ---");
            // Map changed variables for quick lookup.
            var changed = result.ChangedVariables?.ToDictionary(cv => cv.Name, cv => cv) ?? new Dictionary<string, (string, double, double)>();
            foreach (var v in variables) {
                var seedVal = v.Read(seed);
                var bestVal = v.Read(result.BestParams);
                var marker = changed.ContainsKey(v.Name) ? "  *" : "";
                sb.AppendLine($"  {v.Name,-30} {F(seedVal),14} -> {F(bestVal),-14}{marker}");
            }
            sb.AppendLine("  (* = changed by the optimizer)");
            sb.AppendLine();

            sb.AppendLine($"--- Hard-floor check (every frame must keep >= {c.NHard} stars) ---");
            sb.AppendLine(passed
                ? $"  PASS (min observed = {worstFrameCount})"
                : $"  FAIL (min observed = {worstFrameCount} in run {worstRunId})");
            sb.AppendLine();

            sb.AppendLine("--- Per-frame star counts (per run, per focuser position: seed -> optimized) ---");
            for (int i = 0; i < runs.Count; i++) {
                var run = runs[i];
                sb.AppendLine($"  {run.Discovered.RunId}:");
                // Group frames by focuser position; report seed/optimized counts for each.
                var byPos = BuildPerPositionCounts(run, perRunSeed[i], perRunBest[i]);
                sb.AppendLine($"    {"focuser",-10} {"seed",6} {"opt",6}");
                foreach (var kvp in byPos) {
                    sb.AppendLine($"    {kvp.Key,-10} {kvp.Value.seed,6} {kvp.Value.opt,6}");
                }
            }

            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Builds a focuser-position -> (seed count, optimized count) map for one run. FrameStarCounts is in the
        /// SAME order RunEvaluationData iterates its frames (the order they were added), so it aligns 1:1 with the
        /// run's frame list; multiple frames at one position are summed (matches how the V-curve pools positions).
        /// </summary>
        private static SortedDictionary<int, (int seed, int opt)> BuildPerPositionCounts(
            LoadedHarnessRun run, RunEvaluationResult seedResult, RunEvaluationResult bestResult) {
            var byPos = new SortedDictionary<int, (int seed, int opt)>();
            var frames = run.Discovered.Frames;
            var seedCounts = seedResult.Metrics.FrameStarCounts;
            var bestCounts = bestResult.Metrics.FrameStarCounts;
            for (int i = 0; i < frames.Count; i++) {
                var pos = frames[i].FocuserPosition;
                var s = (seedCounts != null && i < seedCounts.Count) ? seedCounts[i] : 0;
                var o = (bestCounts != null && i < bestCounts.Count) ? bestCounts[i] : 0;
                if (byPos.TryGetValue(pos, out var cur)) {
                    byPos[pos] = (cur.seed + s, cur.opt + o);
                } else {
                    byPos[pos] = (s, o);
                }
            }
            return byPos;
        }

        private static void WriteCsv(
            string path, List<LoadedHarnessRun> runs, List<RunEvaluationResult> perRunSeed, List<RunEvaluationResult> perRunBest) {
            var sb = new StringBuilder();
            sb.AppendLine("run,focuserPosition,seedStarCount,optimizedStarCount,frameIndex,framePath");
            for (int i = 0; i < runs.Count; i++) {
                var run = runs[i];
                var frames = run.Discovered.Frames;
                var seedCounts = perRunSeed[i].Metrics.FrameStarCounts;
                var bestCounts = perRunBest[i].Metrics.FrameStarCounts;
                for (int j = 0; j < frames.Count; j++) {
                    var s = (seedCounts != null && j < seedCounts.Count) ? seedCounts[j] : 0;
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
                    // Reuse the already-loaded float Mat (it is never mutated — detection always clones it). This
                    // also sidesteps re-loading .xisf/.fits, which would need the profile again.
                    var srcFloat = (Mat)frame.Image;
                    using var detectClone = srcFloat.Clone();
                    var result = await detector.Detect(detectClone, optimizedParams, null, CancellationToken.None).ConfigureAwait(false);

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

        private static string SanitizeFileName(string name) {
            foreach (var c in Path.GetInvalidFileNameChars()) {
                name = name.Replace(c, '_');
            }
            return name;
        }

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
