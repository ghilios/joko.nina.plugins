#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using OpenCvSharp;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DrawingSize = System.Drawing.Size;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Headless validator for the Tilt Adapter Wizard's calibration. Given a dataset folder containing the 6
    /// saved AF runs the wizard consumes in sequence (Baseline → AllInward → ReBaseline1 → Screw1 → ReBaseline2 →
    /// Screw2) — or the 4 runs of a wizard flow saved without the curvature-direction steps (Baseline → Screw1 →
    /// ReBaseline2 → Screw2) — it measures each
    /// run's tilt plane, runs the SAME pure <see cref="TiltCalibrationCalculator"/> the live wizard uses, and
    /// reports the computed per-screw position angles + recovered hardware (thread pitch / step size) against
    /// ground-truth metadata stored in a per-dataset JSON file. It can also run (and persist) the star-detection
    /// optimizer first so detection is consistent run-to-run.
    ///
    /// Read-only on the NINA profile: the optimized detector params are applied only to a transient in-memory
    /// <see cref="StarDetectorParams"/>, never to the profile. The optimization result is persisted only to the
    /// dataset's own <c>&lt;name&gt;.tilt.json</c> metadata file (which this tool owns).
    ///
    /// NOTE on detection: like the headless <c>optimize</c> harness, detection runs on the loaded float Mats and
    /// (for bayered frames) is not debayered through NINA's render pipeline — the two harnesses therefore use an
    /// identical detection representation. Tilt is a relative cross-region measure of the focus-curve minimum, so
    /// a uniform detection bias largely cancels in the tilt plane.
    /// </summary>
    internal static class TiltCalibrationRunner {

        // The 6 wizard measurement steps, in capture order. Same for 3- and 4-screw adapters. Shared with the
        // live wizard via TiltCalibrationMetadata so a wizard-saved run replays both in-app and headlessly.
        private static readonly string[] StepOrder = TiltCalibrationMetadata.StepOrder;

        // Sigma rejections matching the wizard's RunEvaluationLoader / the optimize harness (HocusFocusDetectionParams defaults).
        private const double HighSigmaOutlierRejection = 4.0;
        private const double LowSigmaOutlierRejection = 3.0;

        private static readonly JsonSerializerSettings MetadataJsonSettings = TiltCalibrationMetadata.JsonSettings;

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Tilt calibration run failed");
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunImpl(string[] args) {
            var datasetDir = DiagnosticUtil.GetArg(args, "--dataset");
            if (string.IsNullOrWhiteSpace(datasetDir)) {
                PrintUsage();
                Environment.ExitCode = 2;
                return;
            }
            datasetDir = Path.GetFullPath(datasetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!Directory.Exists(datasetDir)) {
                throw new DirectoryNotFoundException($"--dataset folder not found: {datasetDir}");
            }

            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            bool reoptimize = DiagnosticUtil.HasFlag(args, "--reoptimize");
            // Match the live app: when the profile debayers (ImageSettings.DebayerImage), detection runs on the
            // debayered luminance, not the raw Bayer mosaic. The wizard's per-star sensor-model tilt is sensitive
            // to this, so replaying a bayered run faithfully requires it.
            bool debayer = DiagnosticUtil.HasFlag(args, "--debayer");
            int? maxEvals = null;
            var maxEvalsArg = DiagnosticUtil.GetArg(args, "--max-evals");
            if (!string.IsNullOrWhiteSpace(maxEvalsArg)) {
                if (!int.TryParse(maxEvalsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var me) || me <= 0) {
                    throw new ArgumentException($"--max-evals: '{maxEvalsArg}' is not a positive integer");
                }
                maxEvals = me;
            }

            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "tilt",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);
            Logger.SetLogLevel(LogLevelEnum.INFO);

            // The real ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a
            // (non-running) WPF Application must exist or it NREs (mirrors the other runners).
            if (Application.Current == null) {
                new Application();
            }

            var datasetName = new DirectoryInfo(datasetDir).Name;
            var parentDir = Directory.GetParent(datasetDir)?.FullName ?? datasetDir;
            // A live wizard run drops its metadata.json into the run root; prefer it so a saved run replays
            // directly. Otherwise fall back to the validation-dataset convention (<parent>/<name>.tilt.json).
            var wizardMetadataPath = Path.Combine(datasetDir, "metadata.json");
            var metadataPath = File.Exists(wizardMetadataPath)
                ? wizardMetadataPath
                : Path.Combine(parentDir, datasetName + ".tilt.json");

            Console.WriteLine($"Dataset: {datasetDir}");
            Console.WriteLine($"Metadata: {metadataPath}");
            Console.WriteLine($"Output: {outDir}");

            // Discover the ordered run folders (immediate subdirectories, name-ascending = chronological).
            var runFolders = Directory.GetDirectories(datasetDir)
                .OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal)
                .ToList();

            if (!File.Exists(metadataPath)) {
                WriteMetadataTemplate(metadataPath, outDir, runFolders);
                Console.Error.WriteLine($"No metadata file found. A template was written to {Path.Combine(outDir, "tilt_metadata_template.json")}.");
                Console.Error.WriteLine($"Fill it in and save it as {metadataPath}, then re-run.");
                Environment.ExitCode = 2;
                return;
            }

            var metadata = JsonConvert.DeserializeObject<TiltCalibrationMetadata>(File.ReadAllText(metadataPath), MetadataJsonSettings)
                ?? throw new InvalidOperationException($"Could not parse metadata file {metadataPath}");
            metadata.Validate();

            // Load the real profile + star detection settings (source of truth for default/baseline params + pixel scale).
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile
                ?? throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA at least once.");
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");

            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions))
                ?? throw new InvalidOperationException("Could not resolve the HocusFocus plugin assembly GUID");
            var accessor = new PluginOptionsAccessor(profileService, guid);
            var starDetectionOptions = new StarDetectionOptions(profileService, accessor);
            var afOptions = new AutoFocusOptions(profileService);
            var inspectorOptions = new InspectorOptions(profileService);
            var alglibAPI = new AlglibAPI();

            // Map the run folders to the wizard steps (explicit override in metadata, else folder-name order).
            var orderedRuns = MapRunsToSteps(metadata, runFolders, datasetDir);
            foreach (var r in orderedRuns) {
                Console.WriteLine($"  {r.Step,-10} -> {Path.GetFileName(r.Folder)} ({r.Frames.Count} frames, {r.Frames.Select(f => f.Focuser).Distinct().Count()} positions)");
            }

            // PixelScale = arcsec/pixel from the dataset pixel size + the profile's focal length (binning 1).
            var pixelScale = MathUtility.ArcsecPerPixel(metadata.PixelSizeMicrons, activeProfile.TelescopeSettings.FocalLength);

            // Resolve the detection params: stored optimized settings (default), a fresh optimization run, or the
            // optimizer is forced via --reoptimize. The result is applied only to this transient params object.
            var (detectionParams, optimizationSource) = await ResolveDetectionParamsAsync(
                metadata, metadataPath, outDir, reoptimize, maxEvals, orderedRuns,
                profileService, starDetectionOptions, afOptions, alglibAPI, pixelScale, debayer).ConfigureAwait(false);
            Console.WriteLine($"Detection params: source={optimizationSource}, Sensitivity={F(detectionParams.Sensitivity)}, " +
                $"StarClippingMultiplier={F(detectionParams.StarClippingMultiplier)}, StructureLayers={detectionParams.StructureLayers}, " +
                $"DefocusAwareDonutDetection={detectionParams.DefocusAwareDonutDetection}");
            Console.WriteLine($"Debayer bayered frames to luminance (match live app): {debayer}");

            // Measure the tilt plane for each run.
            var detector = new StarDetector(alglibAPI);
            var regions = BuildTiltRegions(inspectorOptions);
            double fRatio = activeProfile.TelescopeSettings.FocalRatio;

            var perStep = new List<StepResult>(orderedRuns.Count);
            foreach (var run in orderedRuns) {
                Console.WriteLine($"Measuring 4-corner tilt for {run.Step} ({Path.GetFileName(run.Folder)}, {run.Frames.Count} frames × 5 regions) ...");
                var sw4c = System.Diagnostics.Stopwatch.StartNew();
                var stepResult = await MeasureTiltAsync(run, detector, detectionParams, regions, fRatio,
                    metadata.FocuserStepSizeMicrons, metadata.PixelSizeMicrons, starDetectionOptions.MeasurementAverage,
                    profileService, alglibAPI, afOptions.HyperbolicFitModel, debayer).ConfigureAwait(false);
                perStep.Add(stepResult);
                Console.WriteLine($"    [4-corner {run.Step}] done in {sw4c.ElapsedMilliseconds} ms");
                Console.WriteLine($"    A={F(stepResult.Gradient.A)}, B={F(stepResult.Gradient.B)}, " +
                    $"direction={F(NormalizeAngle(Math.Atan2(stepResult.Gradient.A, -stepResult.Gradient.B) * 180.0 / Math.PI))}°, " +
                    $"tilt={F(stepResult.TiltAngleDeg)}°, mean={F(stepResult.Gradient.MeanFocuserPosition)}");
            }

            // Calibrate using the pure shared calculator.
            var byStep = perStep.ToDictionary(s => s.Step, StringComparer.OrdinalIgnoreCase);
            var imageSize = perStep[0].ImageSize;
            // A 4-step run (wizard flow without the curvature-direction steps) has no AllInward/ReBaseline1: its
            // Baseline reading is the screw-1 reference (ReBaseline1 slot), and the curvature sign falls back to
            // whatever the wizard carried in the metadata (0 = unknown).
            bool hasCurvatureSteps = byStep.ContainsKey("AllInward");
            var inputs = new TiltCalibrationInputs {
                ScrewCount = metadata.NumberOfScrews,
                Baseline = hasCurvatureSteps ? byStep["Baseline"].Gradient : default,
                AllInward = hasCurvatureSteps ? byStep["AllInward"].Gradient : default,
                ReBaseline1 = hasCurvatureSteps ? byStep["ReBaseline1"].Gradient : byStep["Baseline"].Gradient,
                Screw1 = byStep["Screw1"].Gradient,
                ReBaseline2 = byStep["ReBaseline2"].Gradient,
                Screw2 = byStep["Screw2"].Gradient,
                HasCurvatureMeasurement = hasCurvatureSteps,
                FallbackCurvatureSign = metadata.Calibration?.CurvatureSign ?? 0,
                ImageWidthPixels = imageSize.Width,
                ImageHeightPixels = imageSize.Height,
                PixelSizeMicrons = metadata.PixelSizeMicrons,
                FocuserStepMicrons = metadata.FocuserStepSizeMicrons,
                ScrewRadiusMillimeters = metadata.ScrewRadiusMillimeters,
                CalibrationAppliedAmount = metadata.CalibrationAppliedAmount,
                IsStepperAdjustment = string.Equals(metadata.AdjustmentType, "StepperMotors", StringComparison.OrdinalIgnoreCase)
            };
            var calibration = TiltCalibrationCalculator.Calibrate(inputs);

            // Alternative estimator: measure each step's tilt from the robust per-star paraboloid (Gx/Gy) and run the
            // SAME calibration on it, to see how the "use the sensor-model tilt" rewire behaves on this exact data.
            var paraboloidSteps = new List<ParaboloidStepResult>(orderedRuns.Count);
            foreach (var run in orderedRuns) {
                Console.WriteLine($"Paraboloid (per-star) tilt for {run.Step} ...");
                var mean = byStep[run.Step].Gradient.MeanFocuserPosition;
                var ps = await MeasureTiltViaParaboloidAsync(run, detector, detectionParams, metadata.FocuserStepSizeMicrons,
                    metadata.PixelSizeMicrons, mean, profileService, inspectorOptions, afOptions, alglibAPI, debayer,
                    diagDir: Path.Combine(outDir, "diag")).ConfigureAwait(false);
                paraboloidSteps.Add(ps);
                Console.WriteLine(ps.Fitted
                    ? $"    A={F(ps.Gradient.A)}, B={F(ps.Gradient.B)}, stars={ps.StarsInModel}, R²={F(ps.RSquared)}"
                    : $"    paraboloid fit FAILED: {ps.Status}");
            }
            TiltCalibrationResult paraboloidCalibration = null;
            if (paraboloidSteps.All(p => p.Fitted)) {
                var pbyStep = paraboloidSteps.ToDictionary(s => s.Step, StringComparer.OrdinalIgnoreCase);
                var pinputs = new TiltCalibrationInputs {
                    ScrewCount = metadata.NumberOfScrews,
                    Baseline = hasCurvatureSteps ? pbyStep["Baseline"].Gradient : default,
                    AllInward = hasCurvatureSteps ? pbyStep["AllInward"].Gradient : default,
                    ReBaseline1 = hasCurvatureSteps ? pbyStep["ReBaseline1"].Gradient : pbyStep["Baseline"].Gradient,
                    Screw1 = pbyStep["Screw1"].Gradient,
                    ReBaseline2 = pbyStep["ReBaseline2"].Gradient,
                    Screw2 = pbyStep["Screw2"].Gradient,
                    HasCurvatureMeasurement = hasCurvatureSteps,
                    FallbackCurvatureSign = metadata.Calibration?.CurvatureSign ?? 0,
                    ImageWidthPixels = imageSize.Width,
                    ImageHeightPixels = imageSize.Height,
                    PixelSizeMicrons = metadata.PixelSizeMicrons,
                    FocuserStepMicrons = metadata.FocuserStepSizeMicrons,
                    ScrewRadiusMillimeters = metadata.ScrewRadiusMillimeters,
                    CalibrationAppliedAmount = metadata.CalibrationAppliedAmount,
                    IsStepperAdjustment = inputs.IsStepperAdjustment
                };
                paraboloidCalibration = TiltCalibrationCalculator.Calibrate(pinputs);
            }

            WriteReport(outDir, metadata, optimizationSource, perStep, inputs, calibration, paraboloidSteps, paraboloidCalibration);
            Console.WriteLine($"Wrote tilt_summary.txt and tilt_summary.json to {outDir}");
        }

        // ---- Run -> step mapping ---------------------------------------------------------------------------

        private sealed class RunStep {
            public string Step;
            public string Folder;
            public List<(int Focuser, string Path)> Frames;
        }

        private static List<RunStep> MapRunsToSteps(TiltCalibrationMetadata metadata, List<string> runFolders, string datasetDir) {
            // Runs saved by the 4-step wizard flow have no AllInward/ReBaseline1 steps. Pick the expected step set
            // from the explicit mapping when present, else from the discovered folder count.
            string[] expectedSteps;
            if (metadata.RunStepMapping != null && metadata.RunStepMapping.Count > 0) {
                bool hasAllInward = metadata.RunStepMapping.Any(m => string.Equals(m.Step, "AllInward", StringComparison.OrdinalIgnoreCase));
                expectedSteps = hasAllInward ? StepOrder : TiltCalibrationMetadata.StepOrderWithoutCurvature;
                if (!hasAllInward) {
                    // A mapping without an "AllInward" entry is treated as a 4-step run, which silently
                    // ignores any measured curvature-direction data. Entries that are not 4-step names
                    // (a typo like "AllInwards", or a stray "ReBaseline1" without "AllInward") suggest
                    // the author intended the 6-step flow — warn instead of guessing.
                    var anomalous = metadata.RunStepMapping
                        .Select(m => m.Step)
                        .Where(s => !TiltCalibrationMetadata.StepOrderWithoutCurvature.Any(k => string.Equals(k, s, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    if (anomalous.Count > 0) {
                        Console.Error.WriteLine(
                            $"WARNING: runStepMapping has no 'AllInward' entry, so this dataset is treated as a 4-step run " +
                            $"({string.Join(", ", TiltCalibrationMetadata.StepOrderWithoutCurvature)}) and curvature-direction data is ignored. " +
                            $"Unrecognized 4-step entries: {string.Join(", ", anomalous.Select(s => $"'{s}'"))}. " +
                            $"If this was meant to be a 6-step run, check the step names in runStepMapping ({string.Join(", ", StepOrder)}).");
                    }
                }
            } else if (runFolders.Count == StepOrder.Length) {
                expectedSteps = StepOrder;
            } else if (runFolders.Count == TiltCalibrationMetadata.StepOrderWithoutCurvature.Length) {
                expectedSteps = TiltCalibrationMetadata.StepOrderWithoutCurvature;
            } else {
                throw new InvalidOperationException(
                    $"Expected {StepOrder.Length} run folders ({string.Join(", ", StepOrder)}) or " +
                    $"{TiltCalibrationMetadata.StepOrderWithoutCurvature.Length} ({string.Join(", ", TiltCalibrationMetadata.StepOrderWithoutCurvature)}) under {datasetDir}, " +
                    $"but found {runFolders.Count}. Provide an explicit 'runStepMapping' in the metadata to disambiguate.");
            }

            // Build the ordered (step, folder) list.
            List<(string Step, string Folder)> ordered;
            if (metadata.RunStepMapping != null && metadata.RunStepMapping.Count > 0) {
                ordered = metadata.RunStepMapping
                    .Select(m => (m.Step, Folder: ResolveFolder(m.Folder, datasetDir)))
                    .ToList();
            } else {
                ordered = runFolders.Select((f, i) => (expectedSteps[i], f)).ToList();
            }

            // Validate every expected step is present.
            var steps = ordered.Select(o => o.Step).ToList();
            foreach (var required in expectedSteps) {
                if (!steps.Any(s => string.Equals(s, required, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidOperationException($"runStepMapping is missing the '{required}' step.");
                }
            }

            // Discover each run's sweep frames (attempt-anchored; excludes single-frame final/initial captures).
            var result = new List<RunStep>(ordered.Count);
            foreach (var (step, folder) in ordered) {
                if (!Directory.Exists(folder)) {
                    throw new DirectoryNotFoundException($"Run folder for step '{step}' not found: {folder}");
                }
                var discovery = OptimizationRunDiscovery.Discover(folder);
                if (discovery.Runs.Count == 0) {
                    throw new InvalidOperationException(
                        $"No usable AF sweep (>= {OptimizationRunDiscovery.MinPositionsForFit} focuser positions) found for step '{step}' under {folder}.");
                }
                // A single AF run folder yields exactly one discovered run; if more, take the one with the most frames.
                var run = discovery.Runs.OrderByDescending(r => r.Frames.Count).First();
                result.Add(new RunStep {
                    Step = step,
                    Folder = folder,
                    Frames = run.Frames.Select(fr => (fr.FocuserPosition, fr.Path)).ToList()
                });
            }
            return result;
        }

        // Shared with the in-app wizard replay so offline and in-app resolution agree (relative entries rebase onto
        // the dataset dir; legacy absolute entries are honored as-is).
        private static string ResolveFolder(string folder, string datasetDir) =>
            TiltCalibrationMetadata.ResolveStepFolder(datasetDir, folder);

        // ---- Optimization (resolve detection params) ------------------------------------------------------

        private static async Task<(StarDetectorParams Params, string Source)> ResolveDetectionParamsAsync(
            TiltCalibrationMetadata metadata, string metadataPath, string outDir, bool reoptimize, int? maxEvals,
            List<RunStep> orderedRuns, ProfileService profileService, StarDetectionOptions starDetectionOptions,
            AutoFocusOptions afOptions, AlglibAPI alglibAPI, double pixelScale, bool debayer) {

            StarDetectorParams ApplyAfContext(StarDetectorParams p) {
                p.PixelScale = pixelScale;
                p.Region = StarDetectionRegion.Full;
                p.ModelPSF = false;
                p.SaveIntermediateFilesPath = string.Empty;
                return p;
            }

            if (metadata.OptimizedStarDetectionSettings != null && !reoptimize) {
                var p = ApplyAfContext(HocusFocusStarDetection.BuildDefaultStarDetectorParams());
                ApplyDtoToParams(p, metadata.OptimizedStarDetectionSettings);
                return (p, "stored");
            }

            // Run the optimizer over all runs jointly (same path the optimize harness drives).
            Console.WriteLine(reoptimize
                ? "Re-optimizing star detection (--reoptimize) ..."
                : "No stored optimized settings; running star detection optimization ...");

            var seed = ApplyAfContext(HocusFocusStarDetection.BuildDefaultStarDetectorParams());
            var baseline = ApplyAfContext(HocusFocusStarDetection.BuildStarDetectorParams(starDetectionOptions));
            if (metadata.DefocusAwareDetectionNeeded) {
                // Mirror the optimize harness's --donut: force the donut master ON so the optimizer explores the
                // defocus/donut recovery axes (needed for reflectors/SCTs).
                seed.DefocusAwareDonutDetection = true;
                baseline.DefocusAwareDonutDetection = true;
            }

            var detector = new StarDetector(alglibAPI);
            var measurementAverage = starDetectionOptions.MeasurementAverage;
            var objectiveConstants = new ObjectiveConstants();

            var loaded = new List<(RunStep Run, List<(int Focuser, Mat Mat)> Frames, RunEvaluationData Data)>();
            try {
                foreach (var run in orderedRuns) {
                    var mats = await LoadRunMatsAsync(run, profileService, debayer).ConfigureAwait(false);
                    var frames = mats.Select(m => new RunFrame { FrameId = m.Path, FocuserPosition = m.Focuser, Image = m.Mat }).ToList();
                    var stepSize = InferStepSize(run.Frames.Select(f => f.Focuser));
                    var fitConfig = new RunFitConfig {
                        StepSize = stepSize,
                        UseWeights = afOptions.WeightedHyperbolicFitEnabled,
                        MaxOutlierRejections = afOptions.MaxOutlierRejections,
                        RejectionConfidence = afOptions.OutlierRejectionConfidence,
                        PreferredModel = null
                    };
                    var splitDetector = new MatSplitFrameDetector(detector, measurementAverage, HighSigmaOutlierRejection, LowSigmaOutlierRejection);
                    var data = new RunEvaluationData(run.Folder, frames, splitDetector, alglibAPI, fitConfig, null);
                    loaded.Add((run, mats.Select(m => (m.Focuser, m.Mat)).ToList(), data));
                }

                var settings = new OptimizerSettings();
                if (maxEvals.HasValue) {
                    settings.MaxEvaluations = maxEvals.Value;
                }
                var variables = OptimizerVariable.CreateCuratedSet(seed);
                var evaluator = RunEvaluationData.CreateEvaluator(loaded.Select(l => l.Data).ToList());

                var perRunBaseline = new List<RunEvaluationResult>(loaded.Count);
                foreach (var l in loaded) {
                    perRunBaseline.Add(await l.Data.EvaluateAndFitAsync(baseline, CancellationToken.None).ConfigureAwait(false));
                }
                var baselineJ = OptimizationObjective.JTotal(
                    perRunBaseline.Select(pr => OptimizationObjective.JRun(pr.Metrics, objectiveConstants)).ToList(), objectiveConstants);

                var progress = new Progress<OptimizationProgress>(op =>
                    Console.WriteLine($"  [{op.Phase}] evals={op.Evaluations}/{op.MaxEvaluations} bestJ={F(op.BestJ)} currentJ={F(baselineJ)}"));

                var optimizer = new StarDetectionOptimizer();
                var result = await optimizer.OptimizeAsync(seed, variables, evaluator, settings, progress, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"Optimization complete: currentJ={F(baselineJ)} -> bestJ={F(result.BestJ)}, evals={result.Evaluations}");

                var bestFitFirst = (await loaded[0].Data.EvaluateAndFitAsync(result.BestParams, CancellationToken.None).ConfigureAwait(false)).BestFit;
                var rec = StepSizeRecommender.Recommend(bestFitFirst, InferStepSize(orderedRuns[0].Frames.Select(f => f.Focuser)), null);

                var dto = OptimizedStarDetectionSettings.FromParams(
                    result.BestParams, loaded.Count, baselineJ, result.BestJ, rec.StepSize, rec.OffsetSteps);

                // Persist: write the DTO back into the metadata file (source of truth) + a copy in the out dir.
                metadata.OptimizedStarDetectionSettings = dto;
                File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, MetadataJsonSettings));
                File.WriteAllText(Path.Combine(outDir, "optimized_settings.json"), JsonConvert.SerializeObject(dto, MetadataJsonSettings));
                Console.WriteLine($"Persisted optimized settings to {metadataPath}");

                var detectionParams = ApplyAfContext(CloneParamsViaDefault(result.BestParams));
                return (detectionParams, reoptimize ? "reoptimized" : "optimized");
            } finally {
                foreach (var l in loaded) {
                    l.Data?.Dispose();
                    foreach (var (_, mat) in l.Frames) {
                        mat?.Dispose();
                    }
                }
            }
        }

        /// <summary>Copies the optimizer's winning curated knobs onto a fresh default params bundle (so the returned
        /// params is independent of the RunEvaluationData lifetime), mirroring the FromParams/ApplyDto round-trip.</summary>
        private static StarDetectorParams CloneParamsViaDefault(StarDetectorParams best) {
            var p = HocusFocusStarDetection.BuildDefaultStarDetectorParams();
            ApplyDtoToParams(p, OptimizedStarDetectionSettings.FromParams(best, 1, 0, 0, 1, 0));
            return p;
        }

        /// <summary>Overlays a stored OptimizedStarDetectionSettings snapshot onto a params bundle (inverse of
        /// <see cref="OptimizedStarDetectionSettings.FromParams"/>).</summary>
        private static void ApplyDtoToParams(StarDetectorParams p, OptimizedStarDetectionSettings dto) {
            p.Sensitivity = dto.BrightnessSensitivity;
            p.StarClippingMultiplier = dto.StarClippingMultiplier;
            p.NoiseClippingMultiplier = dto.NoiseClippingMultiplier;
            p.PeakResponse = dto.StarPeakResponse;
            p.MaxDistortion = dto.MaxDistortion;
            p.MinHFR = dto.MinHFR;
            p.StarCenterTolerance = dto.StarCenterTolerance;
            p.StructureLayers = dto.StructureLayers;
            p.NoiseReductionRadius = dto.NoiseReductionRadius;
            p.MinimumStarBoundingBoxSize = dto.MinStarBoundingBoxSize;
            p.HotpixelThresholdingEnabled = dto.HotpixelThresholdingEnabled;
            p.HotpixelThreshold = dto.HotpixelThreshold;
            p.DefocusAwareDistortion = dto.DefocusAwareGates;
            p.DefocusAwareCentering = dto.DefocusAwareGates;
            p.DefocusDistortionSizeReference = dto.DefocusDistortionSizeReference;
            p.DefocusDistortionMinFactor = dto.DefocusDistortionMinFactor;
            p.DefocusCenteringToleranceFactor = dto.DefocusCenteringToleranceFactor;
            p.DefocusAwareStructure = dto.DefocusAwareStructure;
            p.StructureLayerBoost = dto.StructureLayerBoost;
            p.DefocusAwareDonutDetection = dto.DefocusAwareDonutDetection;
            p.DonutMorphCloseSize = dto.DonutMorphCloseSize;
            p.DonutMinAnnularityHoleFraction = dto.DonutMinAnnularityHoleFraction;
            p.DonutMaxStreakEccentricity = dto.DonutMaxStreakEccentricity;
            p.DonutSaturationBloomRadius = dto.DonutSaturationBloomRadius;
        }

        // ---- Tilt measurement ------------------------------------------------------------------------------

        private sealed class StepResult {
            public string Step;
            public string Folder;
            public TiltGradient Gradient;
            public double TiltAngleDeg;
            public DrawingSize ImageSize;
            public double[] RegionRSquared; // index 1..5 (center, TL, TR, BL, BR); [0] unused
            public int[] RegionPositions;
        }

        private static async Task<StepResult> MeasureTiltAsync(
            RunStep run, StarDetector detector, StarDetectorParams baseParams, List<StarDetectionRegion> regions,
            double fRatio, double focuserStepMicrons, double pixelSizeMicrons, MeasurementAverageEnum measurementAverage,
            ProfileService profileService, IAlglibAPI alglibAPI, HyperbolicFitModel hyperbolicModel, bool debayer) {

            var mats = await LoadRunMatsAsync(run, profileService, debayer).ConfigureAwait(false);
            try {
                var imageSize = new DrawingSize(mats[0].Mat.Width, mats[0].Mat.Height);

                // Per region (1..5): collect (focuser, HFR, sigma) then fit the focus curve -> minimum.
                var regionFinal = new double[6];
                var regionR2 = new double[6];
                for (int ri = 1; ri <= 5; ri++) {
                    var region = regions[ri];
                    var points = new List<ScatterErrorPoint>(mats.Count);
                    foreach (var (focuser, _, mat) in mats) {
                        baseParams.Region = region;
                        using var frameCopy = mat.Clone(); // Detect mutates its input
                        var result = await detector.Detect(frameCopy, baseParams, progress: null, CancellationToken.None).ConfigureAwait(false);
                        var agg = HarnessDetection.ToFrameDetectionResult(result, measurementAverage, HighSigmaOutlierRejection, LowSigmaOutlierRejection, imageSize,
                            baseParams.ExcludeSaturatedStarsFromHFR, baseParams.SaturationThreshold);
                        if (agg.AverageHFR > 0 && agg.StarCount > 1) {
                            var sigma = agg.HFRStdDev > 0 ? agg.HFRStdDev : 1.0;
                            points.Add(new ScatterErrorPoint(focuser, agg.AverageHFR, 0, sigma));
                        }
                    }

                    var (finalFocus, rSquared) = FitFinalFocus(points, run, alglibAPI, hyperbolicModel);
                    regionFinal[ri] = finalFocus;
                    regionR2[ri] = rSquared;
                }

                var model = TiltPlaneModel.Create(
                    imageSize: imageSize, fRatio: fRatio, focuserStepSizeMicrons: focuserStepMicrons,
                    centerFocuser: regionFinal[1], topLeftFocuser: regionFinal[2], topRightFocuser: regionFinal[3],
                    bottomLeftFocuser: regionFinal[4], bottomRightFocuser: regionFinal[5]);

                double tiltAngleDeg = TiltAngleDegrees(model.A, model.B, imageSize, focuserStepMicrons, pixelSizeMicrons);

                return new StepResult {
                    Step = run.Step,
                    Folder = run.Folder,
                    Gradient = new TiltGradient(model.A, model.B, model.MeanFocuserPosition),
                    TiltAngleDeg = tiltAngleDeg,
                    ImageSize = imageSize,
                    RegionRSquared = regionR2,
                    RegionPositions = regionFinal.Select(d => (int)Math.Round(d)).ToArray()
                };
            } finally {
                foreach (var (_, _, mat) in mats) {
                    mat?.Dispose();
                }
            }
        }

        // Per-step result of the alternative estimator: the robust per-star paraboloid tilt (Gx/Gy), converted to
        // the same (A, B) plane units as the 4-corner result so the same screw-calibration math can consume it.
        private sealed class ParaboloidStepResult {
            public string Step;
            public TiltGradient Gradient;   // A/B from the paraboloid Gx/Gy; Mean carried over from the 4-corner step
            public int StarsInModel;
            public double RSquared;
            public double TiltAngleDeg;
            public bool Fitted;
            public string Status;           // why the fit failed, when !Fitted
        }

        /// <summary>
        /// Measures one step's tilt via the production per-star sensor-model paraboloid (the robust, outlier-rejected,
        /// curvature-aware estimator), instead of the 4-corner region plane. Detects each frame full-sensor, drives
        /// <see cref="SensorModel.RegisterStarsAndFit"/> (image: null, exactly like bank-verify), then converts the
        /// fitted tilt gradient Gx/Gy to plane (A, B) via <see cref="TiltScrewGeometry.PhysicalGradientToPlane"/>.
        /// The piston (Mean focuser position) is carried over from <paramref name="fourCornerMean"/> since it is
        /// estimator-independent. Returns a non-fitted result (with a reason) when the field is too star-poor.
        /// </summary>
        private static async Task<ParaboloidStepResult> MeasureTiltViaParaboloidAsync(
            RunStep run, StarDetector detector, StarDetectorParams baseParams, double focuserStepMicrons,
            double pixelSizeMicrons, double fourCornerMean, ProfileService profileService,
            InspectorOptions inspectorOptions, AutoFocusOptions afOptions, IAlglibAPI alglibAPI, bool debayer,
            string diagDir = null) {

            var mats = await LoadRunMatsAsync(run, profileService, debayer).ConfigureAwait(false);
            try {
                // Opt-in observation-only diagnostics: per-star/point/iteration CSV dumps of this step's
                // sensor-model fit, named after the step (e.g. Screw1_points.csv). Cleared in the finally below.
                if (!string.IsNullOrEmpty(diagDir)) {
                    SensorModel.DiagnosticsDirectory = diagDir;
                    SensorModel.DiagnosticsLabel = run.Step;
                }
                var imageSize = new DrawingSize(mats[0].Mat.Width, mats[0].Mat.Height);
                var sensorFrames = new List<SensorDetectedStars>(mats.Count);
                baseParams.Region = StarDetectionRegion.Full;
                int fi = 0;
                foreach (var (focuser, _, mat) in mats) {
                    var swDet = System.Diagnostics.Stopwatch.StartNew();
                    using var frameCopy = mat.Clone();
                    var result = await detector.Detect(frameCopy, baseParams, progress: null, CancellationToken.None).ConfigureAwait(false);
                    var stars = result.DetectedStars ?? new List<Star>();
                    var starList = stars.Select(HocusFocusStarDetection.ToDetectedStar)
                        .OrderBy(s => s.Position.Y * (long)imageSize.Width + s.Position.X).ToList();
                    sensorFrames.Add(new SensorDetectedStars(
                        focuser, new HocusFocusStarDetectionResult { StarList = starList, ImageSize = imageSize }, image: null));
                    Console.WriteLine($"    [paraboloid {run.Step}] full-sensor detect {++fi}/{mats.Count} focuser {focuser}: {starList.Count} stars ({swDet.ElapsedMilliseconds} ms)");
                }

                int stepSize = InferStepSize(run.Frames.Select(f => f.Focuser));
                var sortedFoc = run.Frames.Select(f => (double)f.Focuser).OrderBy(x => x).ToList();
                double finalFocus = sortedFoc[sortedFoc.Count / 2];

                var sensorModel = new SensorModel(profileService, inspectorOptions, afOptions, alglibAPI);
                // Headless: route registration/fit messages to the logger instead of the UI-bound
                // RegistrationAndFitReport collection. Without this, Report() marshals to the WPF dispatcher with a
                // blocking Send, which deadlocks against TestApp's main thread (it blocks on the async command), and
                // it only fires on the incomplete-RANSAC-alignment path — i.e. exactly the donut-heavy tilt steps.
                sensorModel.RegistrationReportSink = m => Logger.Info($"[paraboloid {run.Step}] {m}");
                try {
                    Console.WriteLine($"    [paraboloid {run.Step}] registering + fitting {sensorFrames.Sum(f => f.StarDetectionResult.StarList.Count)} stars across {sensorFrames.Count} frames (RANSAC={inspectorOptions.UseRANSAC}) ...");
                    var swFit = System.Diagnostics.Stopwatch.StartNew();
                    // The headless fit can hang on degenerate (donut-heavy, RANSAC-failed) frames, so bound it: run on a
                    // worker with a cancellation token and a hard WhenAny cutoff; report a timeout rather than blocking.
                    // Cutoff is env-configurable (TILT_FIT_TIMEOUT_SEC) so a deadlock investigation can extend it and
                    // capture a managed stack dump of the hung worker before it is abandoned.
                    int fitTimeoutSec = int.TryParse(Environment.GetEnvironmentVariable("TILT_FIT_TIMEOUT_SEC"), out var t) && t > 0 ? t : 45;
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(fitTimeoutSec));
                    var fitTask = Task.Run(() => sensorModel.RegisterStarsAndFit(
                        sensorFrames, imageSize, focuserSizeMicrons: focuserStepMicrons, finalFocusPosition: finalFocus,
                        pixelSize: pixelSizeMicrons, progress: new Progress<ApplicationStatus>(), stepSize: stepSize,
                        ct: cts.Token));
                    if (await Task.WhenAny(fitTask, Task.Delay(TimeSpan.FromSeconds(fitTimeoutSec + 5))).ConfigureAwait(false) != fitTask) {
                        Console.WriteLine($"    [paraboloid {run.Step}] fit TIMED OUT (>{fitTimeoutSec}s) — abandoning and moving on");
                        return new ParaboloidStepResult { Step = run.Step, Fitted = false, Status = $"fit timed out (>{fitTimeoutSec}s)" };
                    }
                    var (fit, _) = await fitTask.ConfigureAwait(false);
                    Console.WriteLine($"    [paraboloid {run.Step}] fit done in {swFit.ElapsedMilliseconds} ms (stars in model: {fit?.StarsInModel ?? 0})");
                    if (fit == null) {
                        return new ParaboloidStepResult { Step = run.Step, Fitted = false, Status = "fit returned null" };
                    }
                    var sensorW = imageSize.Width * pixelSizeMicrons;
                    var sensorH = imageSize.Height * pixelSizeMicrons;
                    var (a, b) = TiltScrewGeometry.PhysicalGradientToPlane(fit.Gx, fit.Gy, focuserStepMicrons, sensorW, sensorH);
                    return new ParaboloidStepResult {
                        Step = run.Step,
                        Gradient = new TiltGradient(a, b, fourCornerMean),
                        StarsInModel = fit.StarsInModel,
                        RSquared = fit.GoodnessOfFit,
                        TiltAngleDeg = fit.Theta * 180.0 / Math.PI,
                        Fitted = true
                    };
                } catch (Exception ex) {
                    return new ParaboloidStepResult { Step = run.Step, Fitted = false, Status = ex.Message };
                }
            } finally {
                SensorModel.DiagnosticsDirectory = null;
                SensorModel.DiagnosticsLabel = null;
                foreach (var (_, _, mat) in mats) {
                    mat?.Dispose();
                }
            }
        }

        /// <summary>Fits a hyperbolic focus curve to the per-region (focuser, HFR) points and returns the curve
        /// minimum (final focus position) + R². Uses the profile's hyperbolic fit model; unweighted for robustness
        /// headless. Returns (NaN, NaN) on too few points or a failed solve.</summary>
        private static (double finalFocus, double rSquared) FitFinalFocus(
            List<ScatterErrorPoint> points, RunStep run, IAlglibAPI alglibAPI, HyperbolicFitModel model) {
            if (points.Count < OptimizationRunDiscovery.MinPositionsForFit) {
                return (double.NaN, double.NaN);
            }
            var stepSize = InferStepSize(run.Frames.Select(f => f.Focuser));
            try {
                var fit = AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights: false);
                if (!fit.Solve() || double.IsNaN(fit.Minimum.X) || double.IsInfinity(fit.Minimum.X)) {
                    return (double.NaN, double.NaN);
                }
                return (fit.Minimum.X, fit.RSquared);
            } catch (Exception ex) {
                Logger.Warning($"Hyperbolic fit failed: {ex.Message}");
                return (double.NaN, double.NaN);
            }
        }

        private static double TiltAngleDegrees(double a, double b, DrawingSize imageSize, double focuserStepMicrons, double pixelSizeMicrons) {
            // Mirror the wizard's ComputeTiltAngleDeg exactly: gx = A*fStep/(W*pixel), gy = B*fStep/(H*pixel).
            var gx = a * focuserStepMicrons / (imageSize.Width * pixelSizeMicrons);
            var gy = b * focuserStepMicrons / (imageSize.Height * pixelSizeMicrons);
            return Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * 180.0 / Math.PI;
        }

        // ---- Image loading + regions ----------------------------------------------------------------------

        private static async Task<List<(int Focuser, string Path, Mat Mat)>> LoadRunMatsAsync(RunStep run, ProfileService profileService, bool debayer) {
            var result = new List<(int, string, Mat)>(run.Frames.Count);
            foreach (var (focuser, path) in run.Frames.OrderBy(f => f.Focuser)) {
                var mat = await DiagnosticUtil.LoadFloatMat(path, profileService, debayer).ConfigureAwait(false);
                result.Add((focuser, path, mat));
            }
            return result;
        }

        /// <summary>Replicates InspectorVM.GetStarDetectionRegions indices 1..5 (center + 4 corners). Index 0 is a
        /// placeholder (unused for the tilt plane).</summary>
        private static List<StarDetectionRegion> BuildTiltRegions(InspectorOptions inspectorOptions) {
            var oneThird = 1.0 / 3.0;
            var width = oneThird * inspectorOptions.CornersROI * inspectorOptions.SensorROI;
            var distanceFromBoundary = (1.0 - inspectorOptions.SensorROI) / 2.0;
            var innerStart = distanceFromBoundary;
            var outerStart = 1.0 - distanceFromBoundary - width;
            return new List<StarDetectionRegion> {
                StarDetectionRegion.Full, // index 0 (unused)
                new StarDetectionRegion(new RatioRect(oneThird, oneThird, oneThird, oneThird), index: 1), // center
                new StarDetectionRegion(new RatioRect(innerStart, innerStart, width, width), index: 2),    // TL
                new StarDetectionRegion(new RatioRect(outerStart, innerStart, width, width), index: 3),    // TR
                new StarDetectionRegion(new RatioRect(innerStart, outerStart, width, width), index: 4),    // BL
                new StarDetectionRegion(new RatioRect(outerStart, outerStart, width, width), index: 5),    // BR
            };
        }

        private static int InferStepSize(IEnumerable<int> focusers) {
            var positions = focusers.Distinct().OrderBy(x => x).Take(2).ToList();
            return positions.Count > 1 ? Math.Abs(positions[0] - positions[1]) : 0;
        }

        // ---- Reporting -------------------------------------------------------------------------------------

        private static void WriteReport(
            string outDir, TiltCalibrationMetadata metadata, string optimizationSource, List<StepResult> perStep,
            TiltCalibrationInputs inputs, TiltCalibrationResult calibration,
            List<ParaboloidStepResult> paraboloidSteps, TiltCalibrationResult paraboloidCalibration) {

            bool isStepper = inputs.IsStepperAdjustment;
            double groundTruthHardware = isStepper ? metadata.StepperStepSizeMicrons : metadata.ScrewThreadPitchMicrons;
            double measuredHardware = calibration.MeasuredHardwareMicrons;
            double hardwarePctDelta = (groundTruthHardware > 0 && !double.IsNaN(measuredHardware))
                ? 100.0 * Math.Abs(measuredHardware - groundTruthHardware) / groundTruthHardware : double.NaN;

            double screw1Deviation = AngleDifference(calibration.Screw1AngleDegrees, metadata.ExpectedPositionAngleScrew1Deg);

            var sb = new StringBuilder();
            void Line(string s = "") { sb.AppendLine(s); Console.WriteLine(s); }

            Line("================ TILT CALIBRATION VALIDATION ================");
            Line($"Screws: {metadata.NumberOfScrews}   Adjustment: {(isStepper ? "StepperMotors" : "Screws")}");
            Line($"Ground truth: radius={F(metadata.ScrewRadiusMillimeters)} mm, " +
                $"{(isStepper ? "step size" : "thread pitch")}={F(groundTruthHardware)} µm/{(isStepper ? "step" : "turn")}, " +
                $"pixel={F(metadata.PixelSizeMicrons)} µm, focuser={F(metadata.FocuserStepSizeMicrons)} µm/step");
            Line($"Applied per screw step: {F(metadata.CalibrationAppliedAmount)} {(isStepper ? "steps" : "turns")}");
            Line($"Expected Screw 1 angle: {F(metadata.ExpectedPositionAngleScrew1Deg)}°   Defocus-aware: {metadata.DefocusAwareDetectionNeeded}");
            Line($"Detection settings source: {optimizationSource}");
            Line();

            Line("Per-run tilt plane:");
            Line($"  {"Step",-10} {"A",10} {"B",10} {"dir°",8} {"tilt°",8} {"mean",10}  R²(C/TL/TR/BL/BR)");
            foreach (var s in perStep) {
                double dir = NormalizeAngle(Math.Atan2(s.Gradient.A, -s.Gradient.B) * 180.0 / Math.PI);
                var r2 = string.Join("/", Enumerable.Range(1, 5).Select(i => s.RegionRSquared[i].ToString("F2", CultureInfo.InvariantCulture)));
                Line($"  {s.Step,-10} {F(s.Gradient.A),10} {F(s.Gradient.B),10} {F(dir),8} {F(s.TiltAngleDeg),8} {F(s.Gradient.MeanFocuserPosition),10}  {r2}");
            }
            Line();

            Line("Calibrated screw angles (image-space, ° CW from top):");
            Line($"  Screw 1: {F(calibration.Screw1AngleDegrees)}°  (expected {F(metadata.ExpectedPositionAngleScrew1Deg)}°, deviation {F(screw1Deviation)}°)");
            Line($"  Screw 2: {F(calibration.Screw2AngleDegrees)}°");
            Line($"  Screw 3: {F(calibration.Screw3AngleDegrees)}°");
            if (metadata.NumberOfScrews == 4) {
                Line($"  Screw 4: {F(calibration.Screw4AngleDegrees)}°");
            }
            Line($"  Raw measured Screw1->Screw2 gap: {F(calibration.RawAngleDiffDegrees)}° (ideal {(metadata.NumberOfScrews == 3 ? "120" : "90")}°)");
            Line($"  Screw move directions: Screw1={F(calibration.Screw1DirectionDegrees)}°, Screw2={F(calibration.Screw2DirectionDegrees)}°");
            Line();

            Line($"Recovered {(isStepper ? "stepper step size" : "thread pitch")}: " +
                $"{F(measuredHardware)} µm/{(isStepper ? "step" : "turn")}  " +
                $"(ground truth {F(groundTruthHardware)}, Δ {(double.IsNaN(hardwarePctDelta) ? "n/a" : F(hardwarePctDelta) + "%")})");
            Line($"Screw move magnitude ratio (larger/smaller): {F(calibration.MoveMagnitudeRatio)}× " +
                "(should be ~1× for two equal calibration turns)");
            // A measured sign is always ±1; sign 0 is only reachable for a 4-step run whose metadata carried no
            // fallback sign (and a two-section format would misprint it as "+0"), so spell out the unmeasured cases.
            string curvatureSignText;
            if (inputs.HasCurvatureMeasurement) {
                curvatureSignText = calibration.CurvatureSign.ToString("+0;-0", CultureInfo.InvariantCulture);
            } else if (calibration.CurvatureSign == 0) {
                curvatureSignText = "0 (unknown — not measured, no fallback in metadata)";
            } else {
                curvatureSignText = calibration.CurvatureSign.ToString("+0;-0", CultureInfo.InvariantCulture) +
                    " (not measured; carried from metadata)";
            }
            Line($"Curvature (backfocus) inward sign: {curvatureSignText}");
            Line();

            var conf = calibration.Confidence;
            Line("Calibration confidence (signal vs noise from the per-step tilt vectors):");
            Line($"  Screw-move signal: {F(conf.ScrewMoveSignal)}   Noise floor: {F(conf.NoiseEstimate)}   SNR: {F(conf.SignalToNoise)}");
            if (inputs.HasCurvatureMeasurement) {
                Line($"  Noise probes (should be « the signal) — AllInward piston residual: {F(conf.AllInwardTiltResidual)}, " +
                    $"re-baseline drift 1/2: {F(conf.Rebaseline1Drift)}/{F(conf.Rebaseline2Drift)}");
            } else {
                // 4-step run: no curvature-direction steps were captured, so the AllInward residual and first
                // re-baseline drift do not exist; the single re-baseline drift is the only noise probe.
                Line($"  Noise probe (should be « the signal) — single re-baseline drift: {F(conf.Rebaseline2Drift)} (4-step run)");
            }
            Line($"  Predicted screw-direction uncertainty: ±{F(conf.PredictedAngleUncertaintyDeg)}°");
            if (!conf.IsReliable) {
                Line($"  NOTE: SNR {F(conf.SignalToNoise)} is below {F(TiltCalibrationCalculator.MinReliableSignalToNoise)} — the calibration is " +
                    "noise-dominated (insufficient or unstable signal). Re-capture on a star-rich field with a finer step; " +
                    "the recovered screw geometry from this run should not be applied.");
            }
            Line();

            // Alternative estimator: the per-star paraboloid tilt (the "calibrate from the sensor-model tilt" rewire).
            Line("Per-star paraboloid tilt (alternative estimator — the robust sensor-model Gx/Gy):");
            Line($"  {"Step",-12} {"A",10} {"B",10} {"stars",6} {"R²",7}  status");
            foreach (var ps in paraboloidSteps) {
                Line(ps.Fitted
                    ? $"  {ps.Step,-12} {F(ps.Gradient.A),10} {F(ps.Gradient.B),10} {ps.StarsInModel,6} {F(ps.RSquared),7}  ok"
                    : $"  {ps.Step,-12} {"—",10} {"—",10} {"—",6} {"—",7}  FAILED: {ps.Status}");
            }
            if (paraboloidCalibration?.Confidence != null) {
                var pc = paraboloidCalibration.Confidence;
                Line($"  Paraboloid calibration: SNR {F(pc.SignalToNoise)} (vs 4-corner {F(conf.SignalToNoise)}), " +
                    $"screw gap {F(paraboloidCalibration.RawAngleDiffDegrees)}° (ideal {(metadata.NumberOfScrews == 3 ? "120" : "90")}°), " +
                    $"move ratio {F(paraboloidCalibration.MoveMagnitudeRatio)}×, reliable={pc.IsReliable}");
            } else {
                int fitted = paraboloidSteps.Count(p => p.Fitted);
                Line($"  Paraboloid calibration not computed — only {fitted}/{paraboloidSteps.Count} steps fitted. " +
                    "The per-star model needs ≥9 stars matched across ≥5 frames; this field is too star-poor for it.");
            }
            Line();

            // Verdicts. The screw-1 angle and hardware checks need ground truth that a wizard-written metadata.json
            // does not carry; when absent they are reported "n/a" and do not fail the run.
            bool angleProvided = !double.IsNaN(metadata.ExpectedPositionAngleScrew1Deg);
            bool hardwareProvided = groundTruthHardware > 0;
            bool angleOk = !angleProvided || (!double.IsNaN(screw1Deviation) && screw1Deviation <= 30.0);
            bool gapOk = Math.Abs(FoldGap(calibration.RawAngleDiffDegrees) - (metadata.NumberOfScrews == 3 ? 120.0 : 90.0)) <= 30.0;
            bool hardwareOk = !hardwareProvided || (!double.IsNaN(hardwarePctDelta) && hardwarePctDelta <= 25.0);
            bool magnitudeOk = !double.IsNaN(calibration.MoveMagnitudeRatio) && calibration.MoveMagnitudeRatio <= 1.5;
            if (!magnitudeOk) {
                Line("  NOTE: the two screw turns produced very unequal tilt changes — the recovered hardware/angles " +
                    "are unreliable. Re-capture turning each screw the same amount.");
            }
            bool confidenceOk = conf.IsReliable;
            string V(bool ok, bool provided) => !provided ? "n/a" : (ok ? "PASS" : "FAIL");
            Line($"VERDICT: Screw1 angle {V(angleOk, angleProvided)}, angle separation {(gapOk ? "PASS" : "FAIL")}, " +
                $"hardware {V(hardwareOk, hardwareProvided)}, move balance {(magnitudeOk ? "PASS" : "FAIL")}, " +
                $"confidence {(confidenceOk ? "PASS" : "FAIL")}");

            File.WriteAllText(Path.Combine(outDir, "tilt_summary.txt"), sb.ToString());

            var json = new {
                metadata,
                optimizationSource,
                perRun = perStep.Select(s => new {
                    s.Step,
                    folder = Path.GetFileName(s.Folder),
                    s.Gradient.A,
                    s.Gradient.B,
                    meanFocuserPosition = s.Gradient.MeanFocuserPosition,
                    directionDeg = NormalizeAngle(Math.Atan2(s.Gradient.A, -s.Gradient.B) * 180.0 / Math.PI),
                    s.TiltAngleDeg,
                    regionRSquared = s.RegionRSquared,
                    regionPositions = s.RegionPositions
                }),
                calibration = new {
                    calibration.Screw1AngleDegrees,
                    calibration.Screw2AngleDegrees,
                    calibration.Screw3AngleDegrees,
                    calibration.Screw4AngleDegrees,
                    calibration.RawAngleDiffDegrees,
                    calibration.CurvatureSign,
                    // False for a 4-step run: CurvatureSign was not measured, it is the metadata fallback (0 = unknown).
                    curvatureSignMeasured = inputs.HasCurvatureMeasurement,
                    calibration.MeasuredHardwareMicrons,
                    calibration.MoveMagnitudeRatio,
                    screw1DeviationDeg = screw1Deviation,
                    groundTruthHardwareMicrons = groundTruthHardware,
                    hardwarePctDelta
                },
                confidence = conf,
                paraboloid = new {
                    perStep = paraboloidSteps.Select(p => new {
                        p.Step, p.Fitted, p.Status, p.StarsInModel, p.RSquared,
                        A = p.Gradient.A, B = p.Gradient.B, p.TiltAngleDeg
                    }),
                    calibration = paraboloidCalibration
                },
                verdict = new { angleOk, gapOk, hardwareOk, magnitudeOk, confidenceOk }
            };
            File.WriteAllText(Path.Combine(outDir, "tilt_summary.json"), JsonConvert.SerializeObject(json, MetadataJsonSettings));

            if (!angleOk || !gapOk || !hardwareOk || !magnitudeOk || !confidenceOk) {
                Environment.ExitCode = 3;
            }
        }

        private static double AngleDifference(double a, double b) {
            double d = Math.Abs(NormalizeAngle(a) - NormalizeAngle(b)) % 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        private static double FoldGap(double rawDiff) => rawDiff <= 180.0 ? rawDiff : 360.0 - rawDiff;

        private static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;

        // ---- Metadata --------------------------------------------------------------------------------------

        private static void WriteMetadataTemplate(string metadataPath, string outDir, List<string> runFolders) {
            // Seed the template mapping with the step order matching the discovered folder count (4 = the wizard
            // flow without the curvature-direction steps); the user corrects it if the guess is wrong.
            var templateStepOrder = runFolders.Count == TiltCalibrationMetadata.StepOrderWithoutCurvature.Length
                ? TiltCalibrationMetadata.StepOrderWithoutCurvature
                : StepOrder;
            var template = new TiltCalibrationMetadata {
                NumberOfScrews = 3,
                ScrewThreadPitchMicrons = 0,
                ScrewRadiusMillimeters = 0,
                PixelSizeMicrons = 0,
                FocuserStepSizeMicrons = 0,
                ExpectedPositionAngleScrew1Deg = 0,
                DefocusAwareDetectionNeeded = false,
                CalibrationAppliedAmount = 1.0,
                AdjustmentType = "Screws",
                StepperStepSizeMicrons = -1,
                RunStepMapping = runFolders.Select((f, i) => new TiltRunStepMapping {
                    Step = i < templateStepOrder.Length ? templateStepOrder[i] : $"Extra{i}",
                    Folder = Path.GetFileName(f)
                }).ToList(),
                OptimizedStarDetectionSettings = null
            };
            File.WriteAllText(Path.Combine(outDir, "tilt_metadata_template.json"), JsonConvert.SerializeObject(template, MetadataJsonSettings));
            Console.WriteLine($"Discovered {runFolders.Count} run folder(s): {string.Join(", ", runFolders.Select(Path.GetFileName))}");
        }

        private static void PrintUsage() {
            Console.Error.WriteLine("Usage: TestApp tilt --dataset <folder> [--profile-id <guid>] [--out <dir>] [--reoptimize] [--max-evals <int>]");
            Console.Error.WriteLine("  --dataset    (required) folder containing the 6 AF runs (Baseline, AllInward, ReBaseline1, Screw1, ReBaseline2, Screw2) — or 4 (Baseline, Screw1, ReBaseline2, Screw2) for a run saved without the curvature-direction steps.");
            Console.Error.WriteLine("  --profile-id (default active) NINA profile id (settings + focal length).");
            Console.Error.WriteLine("  --out        (default %LOCALAPPDATA%\\NINA\\Logs\\hf-diag\\tilt\\<timestamp>) output directory.");
            Console.Error.WriteLine("  --reoptimize force re-running star-detection optimization and overwrite the stored settings in metadata.");
            Console.Error.WriteLine("  --debayer    debayer bayered frames to luminance before detection (matches the live app when the profile debayers); required to reproduce the wizard's per-star sensor-model tilt on a bayered run.");
            Console.Error.WriteLine("  --max-evals  (optional) override the optimizer's MaxEvaluations budget.");
            Console.Error.WriteLine("Metadata lives at <parent>/<datasetName>.tilt.json; a template is written if it is missing.");
        }

        private static string F(double d) => double.IsNaN(d) ? "NaN" : d.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
