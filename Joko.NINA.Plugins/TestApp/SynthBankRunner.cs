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
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TestApp; // GoldenFrame/GoldenStarBox/GoldenConfidence/GoldenStarSetStore/DiagnosticUtil/MonoFits16Writer — same assembly, top-level TestApp namespace.
using Logger = NINA.Core.Utility.Logger;

namespace TestApp.SynthBank {

    /// <summary>
    /// One sweep frame <see cref="SynthBankRunner.GenerateSweep"/> wrote to disk: the frame path, the golden and
    /// truth sidecar paths (null when the round did not emit them, <see cref="SynthBootstrapParams.EmitGoldens"/>),
    /// and an in-memory copy of the golden/truth data so a caller (the bank generator's per-dataset metadata, or a
    /// future V1 validation round) can summarize without re-reading the sidecars off disk.
    /// </summary>
    public sealed class SweepFrame {
        public int FocuserPosition { get; init; }
        public string FramePath { get; init; }
        public string GoldenPath { get; init; }
        public string TruthPath { get; init; }

        /// <summary>Captured (post-binning) frame width actually written — <c>sensor.Width / afBinning</c>.</summary>
        public int Width { get; init; }

        /// <summary>Captured (post-binning) frame height actually written.</summary>
        public int Height { get; init; }

        /// <summary>Count of <see cref="StarTruth"/> entries the compositor accepted for this frame (0 when
        /// <see cref="SynthBootstrapParams.EmitGoldens"/> was false — truth was never collected).</summary>
        public int TruthStarCount { get; init; }

        /// <summary>The golden sidecar actually written (null when <see cref="SynthBootstrapParams.EmitGoldens"/> is false).</summary>
        public GoldenFrame Golden { get; init; }

        /// <summary>The per-star dispositions actually written to <see cref="TruthPath"/> (null when
        /// <see cref="SynthBootstrapParams.EmitGoldens"/> is false) — kept in-memory so callers needing tier/
        /// saturation/merge counts (e.g. <c>synthetic_meta.json</c>'s <c>catalog</c> block) do not need to re-parse
        /// the sidecar.</summary>
        public IReadOnlyList<SyntheticStarDisposition> Dispositions { get; init; }
    }

    /// <summary>
    /// The output of <see cref="SynthBankRunner.GenerateSweep"/> — every frame written for one sweep round, in
    /// ascending focuser-position order (matching the design's "9 frames … ascending" layout contract). This is
    /// the "Library API used by V1" the design's §G6/G7 calls for: a future <c>SynthValidateRunner</c> calls the
    /// SAME method the bank generator's per-dataset path calls, so the bank and validation rounds cannot diverge.
    /// </summary>
    public sealed class SweepResult {
        public IReadOnlyList<SweepFrame> Frames { get; init; }

        [JsonIgnore] public IReadOnlyList<int> FocuserPositions => Frames.Select(f => f.FocuserPosition).ToList();
        [JsonIgnore] public IReadOnlyList<string> FramePaths => Frames.Select(f => f.FramePath).ToList();
    }

    /// <summary>
    /// Workstream G6 — <c>TestApp synth-bank</c>: renders the checked-in <see cref="SynthBankSpec"/> (G7,
    /// <c>SynthBank/synthetic-bank-spec.json</c>) into the on-disk synthetic AF bank the design's "Bank contract"
    /// section specifies (<c>docs/synthetic-af-bank-design.md</c>). Per dataset: derive the expected-optimal
    /// bootstrap (<see cref="SynthBankDerivations"/>), render its 9-frame sweep through the real simulator
    /// (<see cref="GenerateSweep"/>), and write the golden/truth sidecars plus the two metadata files the bank
    /// contract requires (<c>run_meta.json</c> apriori from truth, <c>synthetic_meta.json</c> at the dataset root).
    ///
    /// <para><see cref="GenerateSweep"/> is deliberately the ONLY place that renders a sweep and writes frame
    /// files — <see cref="Run"/>'s per-dataset path calls it exactly as a future V1 validation driver would (design
    /// §"Library API used by V1"), so there is no second, drifting implementation of "render N frames and write
    /// them out."</para>
    /// </summary>
    public static class SynthBankRunner {

        // ── CLI entry point ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>Canonical exception-catching wrapper (matches <c>OptimizationDiagnosticRunner.Run</c>'s
        /// shape): usage errors set exit code 2 inside <see cref="RunImpl"/> and return normally; anything that
        /// escapes here is a genuine failure and gets exit code 1.</summary>
        public static async Task Run(string[] args) {
            try {
                await RunImpl(args).ConfigureAwait(false);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "synth-bank run failed");
                Environment.ExitCode = 1;
            }
        }

        private static void PrintUsage() {
            Console.Error.WriteLine(
                "Usage: TestApp synth-bank --spec <json> --out <bank-root> " +
                "[--datasets id1,id2] [--overwrite] [--dry-run] [--verify] [--catalog <path>] [--profile-id <guid>] [--step-detect-bound]");
            Console.Error.WriteLine("  --step-detect-bound: F18 -- derive step* as min(W_3x, max(W_detect, floor)) instead of curve geometry alone.");
        }

        private static async Task RunImpl(string[] args) {
            var specPath = DiagnosticUtil.GetArg(args, "--spec");
            var outRoot = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(specPath) || string.IsNullOrWhiteSpace(outRoot)) {
                PrintUsage();
                Environment.ExitCode = 2;
                return;
            }
            if (!File.Exists(specPath)) {
                Console.Error.WriteLine($"--spec file not found: {specPath}");
                Environment.ExitCode = 2;
                return;
            }

            var datasetFilter = DiagnosticUtil.GetArg(args, "--datasets");
            var overwrite = DiagnosticUtil.HasFlag(args, "--overwrite");
            var dryRun = DiagnosticUtil.HasFlag(args, "--dry-run");
            var verify = DiagnosticUtil.HasFlag(args, "--verify");
            var catalogOverride = DiagnosticUtil.GetArg(args, "--catalog");
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            // F18 arm switch. Absent => step* is curve geometry alone, byte-identical to every prior wave, so
            // `--dry-run` with and without it is the derived-parameter diff that scopes the re-render.
            var stepDetectBound = DiagnosticUtil.HasFlag(args, "--step-detect-bound");

            var specBytes = File.ReadAllBytes(specPath);
            var spec = JsonConvert.DeserializeObject<SynthBankSpec>(System.Text.Encoding.UTF8.GetString(specBytes))
                ?? throw new InvalidOperationException($"--spec '{specPath}' did not parse to a SynthBankSpec.");
            var specSha256 = Sha256Hex(specBytes);

            if (!string.IsNullOrWhiteSpace(catalogOverride)) {
                spec.Defaults.AstapCatalogPath = catalogOverride;
            }

            var allIds = spec.Datasets.Select(d => d.Id).ToList();
            var datasetIndexById = allIds
                .Select((id, idx) => (id, idx))
                .ToDictionary(t => t.id, t => t.idx, StringComparer.OrdinalIgnoreCase);

            List<SynthDatasetSpec> selected;
            if (!string.IsNullOrWhiteSpace(datasetFilter)) {
                var wanted = new HashSet<string>(
                    datasetFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
                selected = spec.Datasets.Where(d => wanted.Contains(d.Id)).ToList();
                foreach (var w in wanted) {
                    if (!allIds.Contains(w, StringComparer.OrdinalIgnoreCase)) {
                        Console.Error.WriteLine($"WARNING: --datasets requested unknown dataset id '{w}' (ignored).");
                    }
                }
            } else {
                selected = spec.Datasets.ToList();
            }
            if (selected.Count == 0) {
                Console.Error.WriteLine("No datasets selected (check --datasets against the spec's dataset ids).");
                Environment.ExitCode = 2;
                return;
            }

            Directory.CreateDirectory(outRoot);
            Logger.SetLogLevel(LogLevelEnum.INFO);

            // --verify needs a live NINA profile to reload frame 0 through FITS.Load (DiagnosticUtil.LoadRenderedImage
            // requires one); generation itself (StarFieldCompositor / AstapCatalogReader / MonoFits16Writer) needs
            // none, so the profile is only stood up when actually requested.
            IProfileService profileService = null;
            if (verify) {
                if (Application.Current == null) { new Application(); }
                var ps = new ProfileService();
                ps.TryLoad(profileId ?? string.Empty);
                if (ps.ActiveProfile == null) {
                    throw new InvalidOperationException("--verify requires an active NINA profile. Pass --profile-id.");
                }
                profileService = ps;
            }

            IAstapCatalogReader catalogReader = new AstapCatalogReader(spec.Defaults.AstapCatalogPath);

            Prog($"synth-bank: spec={specPath} (sha256={specSha256.Substring(0, 12)}…) " +
                $"{selected.Count}/{spec.Datasets.Count} dataset(s) out={outRoot} " +
                $"dryRun={dryRun} overwrite={overwrite} verify={verify}");

            // F44 — say it up front, once, for every selected row that is marked known-bad. A dataset whose frames
            // must be re-rendered is indistinguishable from a good one on disk, and the numbers it produces look
            // entirely ordinary, so the warning has to come from the spec rather than from the reader remembering.
            foreach (var suspectRow in selected.Where(d => !string.IsNullOrWhiteSpace(d.Suspect))) {
                Console.Error.WriteLine($"[{suspectRow.Id}] SUSPECT: {suspectRow.Suspect}");
                Logger.Warning($"synth-bank: dataset '{suspectRow.Id}' is marked suspect: {suspectRow.Suspect}");
            }

            int generated = 0, skipped = 0, dryRunCount = 0, failed = 0;
            foreach (var dataset in selected) {
                try {
                    var outcome = await ProcessDatasetAsync(
                        spec, dataset, datasetIndexById[dataset.Id], outRoot, overwrite, dryRun, verify,
                        profileService, catalogReader, specSha256, stepDetectBound, CancellationToken.None).ConfigureAwait(false);
                    switch (outcome) {
                        case DatasetOutcome.Generated: generated++; break;
                        case DatasetOutcome.Skipped: skipped++; break;
                        case DatasetOutcome.DryRun: dryRunCount++; break;
                    }
                } catch (Exception ex) {
                    failed++;
                    Console.Error.WriteLine($"[{dataset.Id}] FAILED: {ex.GetType().Name}: {ex.Message}");
                    Logger.Error(ex, $"synth-bank failed for dataset '{dataset.Id}'");
                }
            }

            if (dryRun) {
                Prog($"synth-bank: dry-run complete, {dryRunCount} dataset(s) examined, {failed} failed to derive.");
            } else {
                Prog($"synth-bank: {generated} generated, {skipped} skipped (already complete), {failed} failed.");
            }
            if (failed > 0) {
                // A per-dataset failure here is a legitimate quality-gate failure (spec error, kernel-cap guard
                // violation, or a --verify hard-assertion mismatch) rather than a process crash — exit code 3,
                // matching the project's "3 = assertion failure" convention. Applies to --dry-run too: a dataset
                // whose expected-optimal derivation itself threw (e.g. a starless pointing) is not a clean pass.
                Environment.ExitCode = 3;
            }
        }

        private enum DatasetOutcome { Generated, Skipped, DryRun }

        // ── Per-dataset orchestration ────────────────────────────────────────────────────────────────────────

        private static async Task<DatasetOutcome> ProcessDatasetAsync(
                SynthBankSpec spec, SynthDatasetSpec dataset, int datasetIndex, string outRoot,
                bool overwrite, bool dryRun, bool verify, IProfileService profileService,
                IAstapCatalogReader catalogReader, string specSha256, bool stepDetectBound, CancellationToken token) {
            var defaults = spec.Defaults;
            var model = SynthBankDerivations.BuildDefocusModel(dataset, defaults);
            // --dry-run DOES pass the catalog reader, so the exposure band is derived and printed like every other
            // parameter. That costs one reference render per dataset (DeriveExposureBand renders the in-focus frame
            // once at 1s and then solves for t in closed form), which is the whole point: the exposure band is the
            // softest derivation in the design, and finding out it is wrong after a 30-60 minute bank generation is
            // exactly the outcome --dry-run exists to prevent. The catalog star count PrintDryRun reports is still a
            // separate, direct Query() -- it must not depend on a render having succeeded.
            var expectedOptimal = SynthBankDerivations.DeriveExpectedOptimal(
                dataset, defaults, catalogReader, out var kernelCapGuard, out var truthModel, token,
                detectBoundedStep: stepDetectBound);

            // Apply the checked-in spec's *Override fields on top of the physics answer (SynthBankDerivations is
            // deliberately override-free — see its class remarks). Any override that actually disagrees with the
            // derived value is printed, not silently absorbed, per the same remarks. A step-size override forces a
            // guard/truth-model recompute so the two never drift out of sync with what actually gets rendered.
            var stepSize = expectedOptimal.StepSizeSteps;
            if (dataset.StepSizeOverride.HasValue && dataset.StepSizeOverride.Value != stepSize) {
                Prog($"[{dataset.Id}] NOTE: stepSizeOverride={dataset.StepSizeOverride.Value} overrides derived step*={stepSize}");
                stepSize = dataset.StepSizeOverride.Value;
                kernelCapGuard = SynthBankDerivations.DeriveKernelCapGuard(model, defaults.OffsetSteps, stepSize);
                truthModel = kernelCapGuard.WithinCap
                    ? SynthBankDerivations.BuildTruthModel(model, dataset, defaults.OffsetSteps, stepSize)
                    : null;
                expectedOptimal.StepSizeSteps = stepSize;
            }
            if (dataset.DetectionBinningOverride.HasValue && dataset.DetectionBinningOverride.Value != expectedOptimal.DetectionBinning) {
                Prog($"[{dataset.Id}] NOTE: detectionBinningOverride={dataset.DetectionBinningOverride.Value} overrides derived detectionBinning={expectedOptimal.DetectionBinning}");
                expectedOptimal.DetectionBinning = dataset.DetectionBinningOverride.Value;
            }
            if (dataset.DonutOverride.HasValue && dataset.DonutOverride.Value != expectedOptimal.DonutDetection) {
                Prog($"[{dataset.Id}] NOTE: donutOverride={dataset.DonutOverride.Value} overrides derived donutDetection={expectedOptimal.DonutDetection}");
                expectedOptimal.DonutDetection = dataset.DonutOverride.Value;
            }
            if (dataset.ExposureSecondsOverride.HasValue && dataset.ExposureSecondsOverride.Value != expectedOptimal.ExposureSeconds) {
                // F19's exposure ladder. The band stays as DERIVED — it describes what the physics asks for, and a
                // rung deliberately sitting outside it is the measurement, not an error to be normalized away.
                Prog($"[{dataset.Id}] NOTE: exposureSecondsOverride={dataset.ExposureSecondsOverride.Value:0.###}s " +
                    $"overrides derived exposure={expectedOptimal.ExposureSeconds:0.###}s " +
                    $"(band {expectedOptimal.ExposureBandLowSeconds:0.###}-{expectedOptimal.ExposureBandHighSeconds:0.###}s, unchanged)");
                expectedOptimal.ExposureSeconds = dataset.ExposureSecondsOverride.Value;
                expectedOptimal.ExposureDefinition = $"PINNED by exposureSecondsOverride to " +
                    $"{dataset.ExposureSecondsOverride.Value.ToString("0.###", CultureInfo.InvariantCulture)}s. " +
                    expectedOptimal.ExposureDefinition;
            }

            if (dryRun) {
                PrintDryRun(dataset, expectedOptimal, kernelCapGuard, catalogReader);
                return DatasetOutcome.DryRun;
            }

            if (!kernelCapGuard.WithinCap) {
                throw new InvalidOperationException(
                    $"kernel-cap guard failed: sweep extreme {kernelCapGuard.SweepExtremeDefocusMicrons:0.#}um exceeds " +
                    $"{SynthBankDerivations.KernelCapGuardUtilizationLimit:P0} of max {kernelCapGuard.MaxAbsDefocusMicrons:0.#}um " +
                    $"(utilization {kernelCapGuard.Utilization:P1}). This is a dataset spec error (step size / offsetSteps too " +
                    "large for this optical train) -- fix synthetic-bank-spec.json, it is not clamped automatically.");
            }
            if (double.IsNaN(expectedOptimal.ExposureSeconds)) {
                throw new InvalidOperationException(
                    $"expected-optimal exposure could not be derived ({expectedOptimal.ExposureDefinition}) -- cannot generate frames.");
            }

            var datasetOutDir = Path.Combine(outRoot, dataset.Id);
            var attemptDir = Path.Combine(datasetOutDir, "attempt01");

            if (!overwrite && IsDatasetComplete(dataset, defaults, stepSize, datasetOutDir, attemptDir)) {
                Prog($"[{dataset.Id}] already complete, skipping (pass --overwrite to regenerate)");
                return DatasetOutcome.Skipped;
            }

            Directory.CreateDirectory(attemptDir);

            var bootstrap = new SynthBootstrapParams {
                CenterPosition = dataset.OptimalFocuserPosition,
                StepSize = stepSize,
                OffsetSteps = defaults.OffsetSteps,
                ExposureSeconds = expectedOptimal.ExposureSeconds,
                AfBinning = dataset.CaptureBinning,
                EmitGoldens = true
            };
            // NoiseSeed = SeedMixer.Combine(bankSeed, datasetIndex, frameIndex) per the design's determinism
            // requirement, but GenerateSweep's signature (shared with the future V1 validation driver, which
            // collapses a different triple -- datasetSeed/scenarioId/round -- into the same single `seed` slot)
            // takes one already-mixed int and folds the frame index in itself. Folding bankSeed+datasetIndex here
            // first, then GenerateSweep folding in frameIndex, gives every frame a value that is exactly as
            // deterministic and well-avalanched as a literal 3-argument Combine, just computed in two mixing
            // steps instead of one -- see GenerateSweep's remarks.
            var seed = SeedMixer.Combine(spec.BankSeed, datasetIndex);

            Prog($"[{dataset.Id}] generating {2 * defaults.OffsetSteps + 1} frames -> {attemptDir} " +
                $"(seed={seed}, step={stepSize}, exposure={bootstrap.ExposureSeconds:0.###}s, afBinning={bootstrap.AfBinning})");
            var sweepResult = GenerateSweep(dataset, defaults, bootstrap, attemptDir, seed, token);

            WriteRunMeta(dataset, expectedOptimal, model, dataset.CaptureBinning, sweepResult, attemptDir);
            WriteSyntheticMeta(spec, dataset, defaults, bootstrap, seed, specSha256, expectedOptimal, kernelCapGuard, truthModel, sweepResult, datasetOutDir);

            if (verify) {
                var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));
                await VerifyFrame0Async(dataset, bootstrap, sensor, sweepResult, profileService, token).ConfigureAwait(false);
            }

            return DatasetOutcome.Generated;
        }

        private static void PrintDryRun(
                SynthDatasetSpec dataset, SynthExpectedOptimal expectedOptimal, SynthKernelCapGuard guard, IAstapCatalogReader catalogReader) {
            var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));
            // Per the task: the dry-run catalog count queries the reader directly against the dataset's diagonal
            // FOV -- it deliberately does NOT reproduce StarFieldCompositor's private FovMarginFactor padding, so
            // this is a lower bound on what a real render would query, not a byte-identical reproduction of it.
            var diagonalFov = StarFieldCompositor.DiagonalFovDegrees(sensor.Width, sensor.Height, sensor.PixelSizeMicrons, dataset.FocalLengthMm);
            int catalogCount;
            try {
                catalogCount = catalogReader.Query(dataset.RaDegrees, dataset.DecDegrees, diagonalFov, dataset.LimitingMagnitude).Count();
            } catch (Exception ex) {
                catalogCount = -1;
                Prog($"[{dataset.Id}] catalog query FAILED: {ex.GetType().Name}: {ex.Message}");
            }

            var exposureText = double.IsNaN(expectedOptimal.ExposureSeconds)
                ? "not derived (--dry-run passes no catalog reader to the exposure-band render; see definition below)"
                : $"{expectedOptimal.ExposureSeconds:0.###}s (band {expectedOptimal.ExposureBandLowSeconds:0.###}-{expectedOptimal.ExposureBandHighSeconds:0.###}s)";
            Prog($"[{dataset.Id}] step*={expectedOptimal.StepSizeSteps} steps  exposure={exposureText}  " +
                $"detectionBinning={expectedOptimal.DetectionBinning}  donut={expectedOptimal.DonutDetection}");
            // F19(b): printed on its OWN line, never appended to the step*/exposure line above -- that line is the
            // derived-parameter control every wave diffs against develop, and a reporting addition must not show up
            // in it as if the physics had moved.
            if (TestApp.SynthBank.ExposureClamp.Saturated(expectedOptimal.ExposureClamp)) {
                Prog($"    exposure SATURATED at the {expectedOptimal.ExposureClamp}: solve asked for "
                    + $"{expectedOptimal.ExposureRawSeconds:0.###}s, reported {expectedOptimal.ExposureSeconds:0.###}s "
                    + "(the clamp, not a derived value)");
            }
            if (double.IsFinite(expectedOptimal.DetectableHalfWidthSteps)) {
                Prog($"    W_detect*={expectedOptimal.DetectableHalfWidthSteps:0} steps");
            }
            if (!string.IsNullOrEmpty(expectedOptimal.StepSizeDefinition)) {
                Prog($"    step rationale: {expectedOptimal.StepSizeDefinition}");
            }
            Prog($"    donut rationale: {expectedOptimal.DonutRationale}");
            Prog($"    exposure definition: {expectedOptimal.ExposureDefinition}");
            Prog($"    kernel-cap guard: maxAbsDefocus={guard.MaxAbsDefocusMicrons:0.#}um sweepExtreme={guard.SweepExtremeDefocusMicrons:0.#}um " +
                $"utilization={guard.Utilization:P1} withinCap={(guard.WithinCap ? "YES" : "NO -- SPEC ERROR, will refuse to render")}");
            Prog($"    catalog: {(catalogCount >= 0 ? catalogCount.ToString(CultureInfo.InvariantCulture) : "ERROR")} star(s) within diagonal FOV " +
                $"{diagonalFov:0.###} deg at RA={dataset.RaDegrees:0.####} Dec={dataset.DecDegrees:0.####} (limiting mag {dataset.LimitingMagnitude:0.#})");
        }

        // ── The shared render+write primitive (also the V1 library API) ─────────────────────────────────────

        /// <summary>
        /// Renders and writes <c>2·bootstrap.OffsetSteps+1</c> sweep frames into <paramref name="outDir"/>, plus
        /// their golden and truth sidecars when <see cref="SynthBootstrapParams.EmitGoldens"/> is set. This is the
        /// SINGLE render+write primitive shared by the bank generator's per-dataset path (<see cref="ProcessDatasetAsync"/>,
        /// bootstrap = the expected-optimal physics answer) and, per the design's "Library API used by V1", a
        /// future <c>SynthValidateRunner</c> (bootstrap = a deliberately-wrong scenario perturbation) — so the bank
        /// and validation rounds can never render two different frame sets for what should be the same inputs.
        ///
        /// <para><b>Frame naming</b> follows the bank contract's load-bearing filename
        /// (<c>OptimizationRunDiscovery.ImageFileRegex</c>): <c>NN_FrameNN_BitDepth16_Bayered0_Focuser&lt;pos&gt;.fits</c>,
        /// <c>NN</c> a 2-digit index from 00, ascending with focuser position (assuming <c>StepSize &gt; 0</c>).
        /// Data is always written as BITPIX=16: sensors under 16 bits (<see cref="SensorDefinition.BitDepth"/>) are
        /// left-shifted by <c>16 − bits</c> AFTER binning (<see cref="HocusFocusSimulatorCamera.BinFrame"/> clips at
        /// the SENSOR's native bit depth, so shifting first would clip wrongly) — see the design's "G3. FITS
        /// writer" section.</para>
        ///
        /// <para><b>Determinism.</b> Each frame's <see cref="RenderRequest.NoiseSeed"/> is
        /// <c>SeedMixer.Combine(seed, frameIndex)</c> — <paramref name="seed"/> is expected to already fold in
        /// whatever identifies this particular round (bank seed + dataset index for G6; dataset seed + scenario +
        /// round for V1), so this method never needs to know how many components went into it.</para>
        ///
        /// <para>Throws (does not clamp) when any computed focuser position would be negative — the design is
        /// explicit that this is a spec error to be reported, not silently absorbed.</para>
        /// </summary>
        public static SweepResult GenerateSweep(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, SynthBootstrapParams bootstrap,
                string outDir, int seed, CancellationToken token) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            if (bootstrap == null) throw new ArgumentNullException(nameof(bootstrap));
            if (string.IsNullOrWhiteSpace(outDir)) throw new ArgumentException("outDir is required.", nameof(outDir));
            if (bootstrap.OffsetSteps < 1) throw new ArgumentOutOfRangeException(nameof(bootstrap), bootstrap.OffsetSteps, "OffsetSteps must be >= 1.");
            if (bootstrap.AfBinning < 1) throw new ArgumentOutOfRangeException(nameof(bootstrap), bootstrap.AfBinning, "AfBinning must be >= 1.");
            if (!(bootstrap.ExposureSeconds > 0.0)) throw new ArgumentOutOfRangeException(nameof(bootstrap), bootstrap.ExposureSeconds, "ExposureSeconds must be positive.");

            Directory.CreateDirectory(outDir);

            var positions = ComputeSweepPositions(bootstrap.CenterPosition, bootstrap.StepSize, bootstrap.OffsetSteps);
            var negative = positions.Where(p => p < 0).ToList();
            if (negative.Count > 0) {
                throw new InvalidOperationException(
                    $"Dataset '{dataset.Id}': sweep would produce negative focuser position(s) [{string.Join(",", negative)}] from " +
                    $"centerPosition={bootstrap.CenterPosition}, stepSize={bootstrap.StepSize}, offsetSteps={bootstrap.OffsetSteps}. " +
                    "This is a spec error (optimalFocuserPosition too small for the step size) -- fix the dataset spec, do not clamp.");
            }

            var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));
            var filter = FilterRegistry.Get(SynthRenderRequestFactory.ParseFilter(dataset.Filter ?? defaults.Filter));
            var gain = dataset.Gain ?? defaults.Gain;
            var model = SynthBankDerivations.BuildDefocusModel(dataset, defaults);
            var catalogReader = new AstapCatalogReader(defaults.AstapCatalogPath);
            var compositor = new StarFieldCompositor(catalogReader);

            GoldenTierThresholds thresholds = null;
            if (bootstrap.EmitGoldens) {
                thresholds = new GoldenTierThresholds {
                    HighSnr = defaults.GoldenHighSnr,
                    MediumSnr = defaults.GoldenMediumSnr,
                    LowSnr = defaults.GoldenLowSnr,
                    UnresolvedSnr = defaults.GoldenUnresolvedSnr
                };
            }

            var shift = 16 - sensor.BitDepth;
            if (shift < 0) {
                throw new InvalidOperationException($"Sensor '{sensor.SensorName}' bit depth {sensor.BitDepth} exceeds 16 -- cannot fit a 16-bit FITS frame.");
            }
            var pixelSizeTimesBinning = sensor.PixelSizeMicrons * bootstrap.AfBinning;
            var binnedWidth = sensor.Width / bootstrap.AfBinning;
            var binnedHeight = sensor.Height / bootstrap.AfBinning;

            var frames = new List<SweepFrame>(positions.Count);
            for (var i = 0; i < positions.Count; i++) {
                token.ThrowIfCancellationRequested();
                var position = positions[i];
                var noiseSeed = SeedMixer.Combine(seed, i);
                var request = SynthRenderRequestFactory.Build(dataset, defaults, position, bootstrap.ExposureSeconds, noiseSeed);

                var truth = bootstrap.EmitGoldens ? new List<StarTruth>() : null;
                var pixels = compositor.Render(request, truth, token);

                if (bootstrap.AfBinning > 1) {
                    pixels = HocusFocusSimulatorCamera.BinFrame(pixels, sensor.Width, sensor.Height, bootstrap.AfBinning, sensor.BitDepth);
                }
                if (shift > 0) {
                    for (var p = 0; p < pixels.Length; p++) {
                        pixels[p] = (ushort)(pixels[p] << shift);
                    }
                }

                var fileName = BuildFrameFileName(i, position);
                var framePath = Path.Combine(outDir, fileName);
                var cards = MonoFits16Writer.StandardCards(
                    binning: bootstrap.AfBinning,
                    pixelSizeMicronsTimesBinning: pixelSizeTimesBinning,
                    focalLengthMm: dataset.FocalLengthMm,
                    exposureSeconds: bootstrap.ExposureSeconds,
                    gain: gain,
                    focuserPosition: position,
                    instrument: $"HocusFocusSynthBank {dataset.Id}");
                MonoFits16Writer.Write(framePath, pixels, binnedWidth, binnedHeight, cards);

                string goldenPath = null, truthPath = null;
                GoldenFrame goldenFrame = null;
                IReadOnlyList<SyntheticStarDisposition> dispositions = null;
                if (bootstrap.EmitGoldens) {
                    var radiometry = RadiometryCalculator.FromRequest(request, sensor, filter);
                    var noiseModel = new GoldenFrameNoiseModel {
                        SkyElectronsPerNativePixel = radiometry.SkyElectronsPerPixel(),
                        DarkElectronsPerNativePixel = radiometry.DarkElectronsPerPixel(),
                        ReadNoiseElectrons = sensor.ReadNoiseElectronsAtGain(gain),
                        DigitalSaturationElectrons = sensor.DigitalSaturationElectronsAtGain(gain),
                        Binning = bootstrap.AfBinning,
                        FrameWidthNative = sensor.Width,
                        FrameHeightNative = sensor.Height,
                        SigmaMinPixels = model.SigmaMinPixels
                    };
                    var goldenResult = GoldenFromTruth.Build(truth, noiseModel, thresholds, fileName, position);
                    goldenFrame = goldenResult.Frame;
                    dispositions = goldenResult.Dispositions;
                    goldenPath = GoldenStarSetStore.SaveForImage(framePath, goldenResult.Frame);
                    truthPath = framePath + ".truth.json";
                    File.WriteAllText(truthPath, JsonConvert.SerializeObject(dispositions, Formatting.Indented));
                }

                frames.Add(new SweepFrame {
                    FocuserPosition = position,
                    FramePath = framePath,
                    GoldenPath = goldenPath,
                    TruthPath = truthPath,
                    Width = binnedWidth,
                    Height = binnedHeight,
                    TruthStarCount = truth?.Count ?? 0,
                    Golden = goldenFrame,
                    Dispositions = dispositions
                });

                Prog($"  [{dataset.Id}] frame {i:00} focuser={position} exp={bootstrap.ExposureSeconds:0.###}s bin={bootstrap.AfBinning} -> {fileName}" +
                    (bootstrap.EmitGoldens ? $" (golden {goldenFrame.Stars.Count}/{goldenFrame.Unresolved?.Count ?? 0} stars/unresolved of {truth.Count} truth)" : ""));
            }

            return new SweepResult { Frames = frames };
        }

        /// <summary>Ascending focuser positions <c>centerPosition + k·stepSize</c> for <c>k = -offsetSteps..offsetSteps</c>
        /// — the single place both <see cref="GenerateSweep"/> (rendering) and <see cref="IsDatasetComplete"/>
        /// (idempotence check) compute the sweep, so they can never disagree on what "the 9 frames" means.</summary>
        private static List<int> ComputeSweepPositions(int centerPosition, int stepSize, int offsetSteps) {
            var positions = new List<int>(2 * offsetSteps + 1);
            for (var k = -offsetSteps; k <= offsetSteps; k++) {
                positions.Add(centerPosition + k * stepSize);
            }
            return positions;
        }

        /// <summary>The load-bearing frame filename (<c>OptimizationRunDiscovery.ImageFileRegex</c>): 2-digit image
        /// index and frame number (identical, both 0-based ascending), always <c>BitDepth16</c> (see
        /// <see cref="GenerateSweep"/>'s remarks on the post-binning left-shift) and <c>Bayered0</c> (the simulator
        /// only ever writes mono).</summary>
        private static string BuildFrameFileName(int frameIndex, int focuserPosition) {
            var idx = frameIndex.ToString("00", CultureInfo.InvariantCulture);
            return $"{idx}_Frame{idx}_BitDepth16_Bayered0_Focuser{focuserPosition}.fits";
        }

        /// <summary>A dataset is complete iff every sweep frame plus its golden and truth sidecars exist, alongside
        /// both metadata files — the idempotence contract the design's "G6/G7" section specifies. A sweep that
        /// would itself be a spec error (negative focuser position) is never reported complete, so the real run
        /// surfaces that error instead of a stale/partial folder masking it.</summary>
        private static bool IsDatasetComplete(SynthDatasetSpec dataset, SynthBankDefaults defaults, int stepSize, string datasetOutDir, string attemptDir) {
            var positions = ComputeSweepPositions(dataset.OptimalFocuserPosition, stepSize, defaults.OffsetSteps);
            if (positions.Any(p => p < 0)) {
                return false;
            }
            for (var i = 0; i < positions.Count; i++) {
                var framePath = Path.Combine(attemptDir, BuildFrameFileName(i, positions[i]));
                if (!File.Exists(framePath)) return false;
                if (!File.Exists(framePath + ".golden.json")) return false;
                if (!File.Exists(framePath + ".truth.json")) return false;
            }
            if (!File.Exists(Path.Combine(attemptDir, "run_meta.json"))) return false;
            if (!File.Exists(Path.Combine(datasetOutDir, "synthetic_meta.json"))) return false;
            return true;
        }

        // ── run_meta.json (apriori from truth) ───────────────────────────────────────────────────────────────

        /// <summary>Matches the shape <c>BankDonutMetaRunner</c> writes for the real bank (plain, unattributed
        /// properties -- Newtonsoft emits them verbatim), so <c>bank-verify</c>'s <c>RunMetaLite</c> (which only
        /// reads <c>donutAware</c>/<c>reason</c>) parses either one identically.</summary>
        private sealed class RunMeta {
            public bool donutAware { get; set; }
            public string reason { get; set; }
            public int[] extremeFocusers { get; set; }
            public DonutSignal donutSignal { get; set; }
            public string detectedAtUtc { get; set; }
        }

        private sealed class DonutSignal {
            public double donutPeakFracMax { get; set; }
            public double extremeFrameMedianHFR { get; set; }
            public double extremeDonutBBoxMedianPx { get; set; }
        }

        /// <summary>
        /// Writes <c>run_meta.json</c> inside <paramref name="attemptDir"/> — apriori from truth (design: "here it
        /// is apriori from truth instead of measuring it"), rather than by running detection on the extreme frames
        /// the way <c>BankDonutMetaRunner</c> does for the real bank.
        /// <see cref="SynthBankDerivations.DeriveDonutExpectation"/>'s inner-radius/HFR/obstruction terms are the
        /// decisive quantities (see <see cref="SynthExpectedOptimal.DonutRationale"/>), so
        /// <see cref="DonutSignal.extremeFrameMedianHFR"/>/<see cref="DonutSignal.extremeDonutBBoxMedianPx"/> record
        /// those SAME quantities rather than independently re-deriving anything. <see cref="DonutSignal.donutPeakFracMax"/>
        /// has no apriori analogue (it is a MEASURED peak/donut ratio from running the real detector on the extreme
        /// frames, which this apriori path never does) and is left at 0 — documented, not silently omitted.
        /// </summary>
        private static void WriteRunMeta(
                SynthDatasetSpec dataset, SynthExpectedOptimal expectedOptimal, DefocusModel model, int captureBinning,
                SweepResult sweepResult, string attemptDir) {
            var positions = sweepResult.FocuserPositions;
            var extremePositions = new[] { positions.Min(), positions.Max() };
            var extremeDefocusMicrons = extremePositions
                .Select(p => Math.Abs((p - model.OptimalFocuserPosition) * model.FocuserStepSizeMicrons))
                .Max();
            var extremeHfrCapturedPx = model.HfrAtDefocusMicrons(extremeDefocusMicrons) / captureBinning;
            var extremeOuterRadiusCapturedPx = model.OuterAnnulusRadiusPixels(extremeDefocusMicrons) / captureBinning;

            var meta = new RunMeta {
                donutAware = expectedOptimal.DonutDetection,
                reason = $"apriori from truth (SynthBankDerivations.DeriveDonutExpectation): {expectedOptimal.DonutRationale}",
                extremeFocusers = extremePositions,
                donutSignal = new DonutSignal {
                    donutPeakFracMax = 0.0, // not measured -- see method remarks.
                    extremeFrameMedianHFR = extremeHfrCapturedPx,
                    extremeDonutBBoxMedianPx = 2.0 * extremeOuterRadiusCapturedPx
                },
                detectedAtUtc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
            };
            File.WriteAllText(Path.Combine(attemptDir, "run_meta.json"), JsonConvert.SerializeObject(meta, Formatting.Indented));
        }

        // ── synthetic_meta.json (dataset root) ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes <c>synthetic_meta.json</c> at <paramref name="datasetOutDir"/> (the dataset root, sibling of
        /// <c>attempt01</c> — deliberately outside both <c>bank-clean</c>'s per-run walk and <c>optimize --per-run</c>'s
        /// write target, per the design). Every block matches the design's "synthetic_meta.json (dataset root)"
        /// section verbatim.
        /// </summary>
        private static void WriteSyntheticMeta(
                SynthBankSpec spec, SynthDatasetSpec dataset, SynthBankDefaults defaults, SynthBootstrapParams bootstrap,
                int seed, string specSha256, SynthExpectedOptimal expectedOptimal, SynthKernelCapGuard kernelCapGuard,
                SynthTruthModel truthModel, SweepResult sweepResult, string datasetOutDir) {
            var sensor = SensorRegistry.Get(SynthRenderRequestFactory.ParseSensorModel(dataset.SensorModel));

            // The in-focus (k=0) frame is index `offsetSteps` in the ascending -offsetSteps..+offsetSteps sweep.
            var centerIndex = bootstrap.OffsetSteps;
            var centerNoiseSeed = SeedMixer.Combine(seed, centerIndex);
            var renderRequestTemplate = SynthRenderRequestFactory.Build(
                dataset, defaults, dataset.OptimalFocuserPosition, bootstrap.ExposureSeconds, centerNoiseSeed);

            var noiseSeeds = Enumerable.Range(0, sweepResult.Frames.Count).Select(i => SeedMixer.Combine(seed, i)).ToList();

            var centerFrame = sweepResult.Frames[centerIndex];
            var centerGolden = centerFrame.Golden;
            var centerDispositions = centerFrame.Dispositions;

            var thresholds = new GoldenTierThresholds {
                HighSnr = defaults.GoldenHighSnr,
                MediumSnr = defaults.GoldenMediumSnr,
                LowSnr = defaults.GoldenLowSnr,
                UnresolvedSnr = defaults.GoldenUnresolvedSnr
            };

            // matchRadiusPx = clamp(12, 0.5*maxHFR_at_extreme_in_captured_px, 32) -- read as "12, clamped into
            // [lo, hi]" per the design (i.e. Math.Clamp(value: 12, min: lo, max: hi)), floored at 6 when the
            // dataset's median golden box is under 4px. For this 17-dataset matrix lo never exceeds 12 (the
            // largest captured extreme HFR across the bank is ~20px, giving lo=10) and the golden policy's own
            // MinBoxHalfWidthPixels=4 floor means a box is never under 8px wide, so this formula evaluates to a
            // flat 12.0 for every dataset here -- implemented generically anyway, since a future wider-range
            // dataset would exercise the clamp's other two edges.
            var extremeHfrCapturedPx = Math.Max(truthModel.PerFrame.First().MeasuredKernelHfrPixels, truthModel.PerFrame.Last().MeasuredKernelHfrPixels) / dataset.CaptureBinning;
            var rawMatchRadius = Math.Clamp(12.0, 0.5 * extremeHfrCapturedPx, 32.0);
            var allBoxWidths = sweepResult.Frames
                .Where(f => f.Golden != null)
                .SelectMany(f => (f.Golden.Stars ?? new List<GoldenStarBox>()).Concat(f.Golden.Unresolved ?? new List<GoldenStarBox>()))
                .Select(b => b.W)
                .ToList();
            var medianBoxWidth = Median(allBoxWidths);
            var matchRadiusPx = medianBoxWidth < 4.0 ? Math.Max(rawMatchRadius, 6.0) : rawMatchRadius;

            var diagonalFov = StarFieldCompositor.DiagonalFovDegrees(sensor.Width, sensor.Height, sensor.PixelSizeMicrons, dataset.FocalLengthMm);

            var meta = new {
                generator = new {
                    tool = "TestApp synth-bank",
                    policyVersion = 1, // this GENERATOR's policy revision (bump on a SynthBankDerivations/GoldenFromTruth policy change).
                    specSha256
                },
                renderRequest = renderRequestTemplate,
                sweep = new {
                    positions = sweepResult.FocuserPositions,
                    noiseSeeds,
                    kernelCapGuard
                },
                truthModel,
                expectedOptimal,
                catalog = new {
                    pointing = new { raDegrees = dataset.RaDegrees, decDegrees = dataset.DecDegrees, diagonalFovDegrees = diagonalFov },
                    countsByTier = new {
                        high = centerGolden?.Stars.Count(s => s.Confidence == GoldenConfidence.High) ?? 0,
                        medium = centerGolden?.Stars.Count(s => s.Confidence == GoldenConfidence.Medium) ?? 0,
                        low = centerGolden?.Stars.Count(s => s.Confidence == GoldenConfidence.Low) ?? 0,
                        unresolved = centerGolden?.Unresolved?.Count ?? 0
                    },
                    saturated = centerDispositions?.Count(d => d.Saturated) ?? 0,
                    merged = centerDispositions?.Count(d => d.Tier == SyntheticTier.MergedInto) ?? 0
                },
                goldenPolicy = new {
                    thresholds = new {
                        highSnr = thresholds.HighSnr,
                        mediumSnr = thresholds.MediumSnr,
                        lowSnr = thresholds.LowSnr,
                        unresolvedSnr = thresholds.UnresolvedSnr,
                        mergeSeparationHfrMultiple = thresholds.MergeSeparationHfrMultiple,
                        blendSeparationHfrMultiple = thresholds.BlendSeparationHfrMultiple,
                        dominanceFluxRatio = thresholds.DominanceFluxRatio,
                        minBoxHalfWidthPixels = thresholds.MinBoxHalfWidthPixels,
                        saturationFraction = thresholds.SaturationFraction
                    }
                },
                matchRadiusPx
            };
            File.WriteAllText(Path.Combine(datasetOutDir, "synthetic_meta.json"), JsonConvert.SerializeObject(meta, Formatting.Indented));
        }

        private static double Median(List<double> xs) {
            if (xs.Count == 0) return 0.0;
            var sorted = xs.OrderBy(x => x).ToList();
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }

        // ── --verify: reload frame 0 through NINA's FITS.Load and hard-assert (design risk R1) ────────────────

        /// <summary>
        /// Reloads sweep frame 0 through <see cref="DiagnosticUtil.LoadRenderedImage"/> (NINA's real <c>FITS.Load</c>
        /// path) and hard-asserts that <c>Camera.PixelSize</c>, <c>Telescope.FocalLength</c>, <c>Camera.BinX</c> and
        /// <c>Image.ExposureTime</c> all come back with the values written — design risk R1 ("NINA's FITS header
        /// parse conventions are unverified from our side"). Throws (rather than warns) on any mismatch; the
        /// caller's per-dataset try/catch turns that into a counted failure and, at the end of the run, exit code 3.
        /// </summary>
        private static async Task VerifyFrame0Async(
                SynthDatasetSpec dataset, SynthBootstrapParams bootstrap, SensorDefinition sensor,
                SweepResult sweepResult, IProfileService profileService, CancellationToken token) {
            var frame0 = sweepResult.Frames[0];
            var image = await DiagnosticUtil.LoadRenderedImage(frame0.FramePath, profileService).ConfigureAwait(false);
            var meta = image.RawImageData?.MetaData;

            // NINA's FITS layer round-trips the pixel size through binning, in BOTH directions:
            //   write: XPIXSZ = Camera.PixelSize * BinX   (FITSHeader.PopulateFromMetaData)
            //   read:  Camera.PixelSize = XPIXSZ / BinX   (FITSHeader.ExtractMetaData)
            // So the card on disk carries the BINNED size (which is what MonoFits16Writer.StandardCards is given,
            // and is correct), while Camera.PixelSize comes back as the PHYSICAL size. Asserting the written card
            // value here would be asserting the wrong side of that divide -- and did, until --verify caught it on
            // the two AF-bin-2 datasets (D11, D12: wrote 5.8, read back 2.9). Downstream this composes correctly:
            // HarnessSettingsStore.PixelScaleForFrame multiplies ArcsecPerPixel(Camera.PixelSize, FocalLength) by
            // Camera.BinX, recovering the binned plate scale. That chain is precisely what design risk R1 asked us
            // to confirm rather than assume, so the assertion is kept -- pointed at the right quantity.
            var expectedPixelSize = sensor.PixelSizeMicrons;
            var mismatches = new List<string>();

            void Check(string name, double expected, double? actual) {
                if (!actual.HasValue || !double.IsFinite(actual.Value) || Math.Abs(actual.Value - expected) > 1e-6 * Math.Max(1.0, Math.Abs(expected))) {
                    var actualText = actual.HasValue ? actual.Value.ToString("0.######", CultureInfo.InvariantCulture) : "null";
                    mismatches.Add($"{name}: wrote {expected.ToString("0.######", CultureInfo.InvariantCulture)}, read back {actualText}");
                }
            }

            Check("Camera.PixelSize", expectedPixelSize, meta?.Camera?.PixelSize);
            Check("Telescope.FocalLength", dataset.FocalLengthMm, meta?.Telescope?.FocalLength);
            Check("Camera.BinX", bootstrap.AfBinning, meta?.Camera?.BinX);
            Check("Image.ExposureTime", bootstrap.ExposureSeconds, meta?.Image?.ExposureTime);

            // The four fields above are only the ingredients. What the harness actually consumes is the plate scale
            // they COMPOSE into, so assert that too -- it is the quantity a wrong binning convention would corrupt
            // while every individual field still looked plausible. This mirrors HarnessSettingsStore.PixelScaleForFrame
            // exactly (ArcsecPerPixel(physical pixel size, focal length) * BinX).
            if (meta?.Camera != null && meta.Telescope != null) {
                var expectedArcsecPerPixel =
                    NINA.Joko.Plugins.HocusFocus.Utility.MathUtility.ArcsecPerPixel(sensor.PixelSizeMicrons, dataset.FocalLengthMm) * bootstrap.AfBinning;
                var actualArcsecPerPixel =
                    NINA.Joko.Plugins.HocusFocus.Utility.MathUtility.ArcsecPerPixel(meta.Camera.PixelSize, meta.Telescope.FocalLength) * Math.Max(meta.Camera.BinX, 1);
                Check("composed arcsec/px", expectedArcsecPerPixel, actualArcsecPerPixel);
            }

            if (mismatches.Count > 0) {
                var detail = string.Join("; ", mismatches);
                Console.Error.WriteLine($"[{dataset.Id}] --verify FAILED (frame {Path.GetFileName(frame0.FramePath)}): {detail}");
                throw new InvalidOperationException($"--verify failed for '{dataset.Id}': {detail}");
            }
            Prog($"[{dataset.Id}] --verify OK: PixelSize={meta.Camera.PixelSize:0.###} FocalLength={meta.Telescope.FocalLength:0.#} " +
                $"BinX={meta.Camera.BinX} ExposureTime={meta.Image.ExposureTime:0.###}");
        }

        // ── progress ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>One flushed stdout line per call. These runs are detached under WSL (which block-buffers
        /// stdout), so an explicit <see cref="Console.Out"/> flush after every frame is the only way progress is
        /// observable while a multi-hour bank generation is in flight.</summary>
        private static void Prog(string message) {
            Console.WriteLine(message);
            Console.Out.Flush();
        }

        // ── misc ──────────────────────────────────────────────────────────────────────────────────────────────

        private static string Sha256Hex(byte[] bytes) {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash) { sb.Append(b.ToString("x2", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }
    }
}
