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
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TestApp; // DiagnosticUtil/HarnessSettingsStore/OptimizationRunDiscovery/StubFocuserMediator/StubPerFilterStarDetectionStore/OptimizationDiagnosticRunner — same assembly, top-level TestApp namespace.
using Logger = NINA.Core.Utility.Logger;

namespace TestApp.SynthBank {

    /// <summary>
    /// Workstream V1 — <c>TestApp synth-validate</c>: the convergence driver (design
    /// <c>docs/synthetic-af-bank-design.md</c> §"V1. The convergence driver"). Per (dataset, scenario), starts the
    /// optimizer's bootstrap deliberately wrong (or, for S0, exactly right) and measures whether the resulting
    /// recommendations (step size, exposure, detection binning) walk it back toward the dataset's physics-derived
    /// expected-optimal bootstrap — the question the real AF bank can never answer, because it has no known-correct
    /// answer to converge toward.
    ///
    /// <para>Runs on a dedicated STA thread with a pumped <see cref="DispatcherSynchronizationContext"/>, copied
    /// verbatim from <see cref="BankVerifyRunner"/>: <see cref="RunEvaluationData.EvaluateAndFitAsync"/> deadlocks
    /// on a non-pumping captured context, and the thread-affine sensor-model fit hangs on a thread-pool thread.
    /// No <c>ConfigureAwait(false)</c> anywhere in that path (see <see cref="RunCore"/> onward).</para>
    ///
    /// <para>Every round's frames are rendered through <see cref="SynthBankRunner.GenerateSweep"/> — the SAME
    /// render+write primitive the bank generator uses — so a scenario round can never render a different frame set
    /// than the bank would for the same inputs (design: "Library API used by V1").</para>
    /// </summary>
    public static class SynthValidateRunner {

        // ── CLI entry point / STA threading (copied from BankVerifyRunner.Run) ──────────────────────────────────

        private const string DefaultOutRoot = @"D:\SyntheticAutofocusBank-validation";

        /// <summary>The synthetic bank's own default <c>--out</c> (<c>synth-bank</c>'s usage/design). A
        /// <c>synth-validate --out</c> nested inside this would let <c>OptimizationRunDiscovery</c> pick up
        /// scenario round folders (each a valid-looking <c>attempt01</c> run) as EXTRA bank runs and silently
        /// corrupt the V2 precision/recall baseline — see <see cref="RunCore"/>'s guard.</summary>
        private const string KnownBankRoot = @"D:\SyntheticAutofocusBank";

        /// <summary>
        /// Runs the whole validation on a dedicated STA thread with a PUMPED dispatcher (the proven
        /// <see cref="BankVerifyRunner"/> pattern, copied verbatim). Required because the work mixes the
        /// optimizer's <see cref="RunEvaluationData.EvaluateAndFitAsync"/> (deadlocks on a non-pumping captured
        /// context) with the thread-affine sensor/hyperbolic fit machinery (hangs on a thread-pool thread).
        /// </summary>
        public static Task Run(string[] args) {
            Exception err = null;
            var thread = new Thread(() => {
                try {
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

        private static void PrintUsage() {
            Console.Error.WriteLine(
                "Usage: TestApp synth-validate --spec <json> --out <dir> [--datasets id1,id2] [--scenarios S0,S1,...] " +
                "[--max-rounds 4] [--max-evals N] [--catalog <path>] [--profile-id <guid>] " +
                "[--step-detect-bound] [--step-size-for-executed-sweep] [--step-recovery-steps N]");
            Console.Error.WriteLine($"  --out defaults to {DefaultOutRoot} and MUST be outside the bank root ({KnownBankRoot}).");
            Console.Error.WriteLine("  F18 arms: --step-detect-bound bounds the step half-width by the outermost frame that still");
            Console.Error.WriteLine("            detected NHard stars; --step-size-for-executed-sweep additionally sizes the step for");
            Console.Error.WriteLine("            offset+recovery points per side (--step-recovery-steps, default 1). Absent => the");
            Console.Error.WriteLine("            driver is bit-identical to the pre-F18 one, so one binary is both arms.");
        }

        private static async Task RunCore(string[] args) {
            var specPath = DiagnosticUtil.GetArg(args, "--spec");
            var outRoot = DiagnosticUtil.GetArg(args, "--out") ?? DefaultOutRoot;
            if (string.IsNullOrWhiteSpace(specPath)) {
                PrintUsage();
                Environment.ExitCode = 2;
                return;
            }
            if (!File.Exists(specPath)) {
                Console.Error.WriteLine($"--spec file not found: {specPath}");
                Environment.ExitCode = 2;
                return;
            }
            if (IsInsideOrEqual(outRoot, KnownBankRoot)) {
                Console.Error.WriteLine(
                    $"--out '{outRoot}' is inside the synthetic bank root '{KnownBankRoot}'. Refusing: " +
                    "OptimizationRunDiscovery would pick up the scenario round folders as EXTRA bank runs (each looks " +
                    "like a valid 'attempt01' run) and silently corrupt the P/R baseline. Point --out elsewhere " +
                    $"(default: {DefaultOutRoot}).");
                Environment.ExitCode = 2;
                return;
            }

            var datasetFilter = DiagnosticUtil.GetArg(args, "--datasets");
            var scenarioFilter = DiagnosticUtil.GetArg(args, "--scenarios");

            var maxRounds = 4;
            var maxRoundsArg = DiagnosticUtil.GetArg(args, "--max-rounds");
            if (!string.IsNullOrWhiteSpace(maxRoundsArg)) {
                if (!int.TryParse(maxRoundsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxRounds) || maxRounds < 1) {
                    throw new ArgumentException($"--max-rounds: '{maxRoundsArg}' is not a positive integer");
                }
            }
            int? maxEvals = null;
            var maxEvalsArg = DiagnosticUtil.GetArg(args, "--max-evals");
            if (!string.IsNullOrWhiteSpace(maxEvalsArg)) {
                if (!int.TryParse(maxEvalsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var me) || me <= 0) {
                    throw new ArgumentException($"--max-evals: '{maxEvalsArg}' is not a positive integer");
                }
                maxEvals = me;
            }
            // F23: lets the V1 matrix be re-scored with the marginal-SNR false-positive term ENABLED, so the
            // design spec's prediction -- that fixing F23 dissolves F22 and F26 -- can actually be tested. The
            // shipping default is 0 (the term is measured-but-not-enabled), which reproduces HEAD exactly.
            double? marginalSnrStrength = null;
            var msArg = DiagnosticUtil.GetArg(args, "--marginal-snr-strength");
            if (!string.IsNullOrWhiteSpace(msArg)) {
                if (!double.TryParse(msArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || !double.IsFinite(ms) || ms < 0.0) {
                    throw new ArgumentException($"--marginal-snr-strength: '{msArg}' must be a finite number >= 0");
                }
                marginalSnrStrength = ms;
            }
            // F18 arms. Absent, the driver is bit-identical to the pre-F18 one, so this binary is its own control
            // (F41). --step-recovery-steps only means anything alongside --step-size-for-executed-sweep.
            var stepDetectBound = DiagnosticUtil.HasFlag(args, "--step-detect-bound");
            var stepSizeForExecutedSweep = DiagnosticUtil.HasFlag(args, "--step-size-for-executed-sweep");
            var stepRecoverySteps = 1;
            var srsArg = DiagnosticUtil.GetArg(args, "--step-recovery-steps");
            if (!string.IsNullOrWhiteSpace(srsArg)) {
                if (!int.TryParse(srsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out stepRecoverySteps) || stepRecoverySteps < 0) {
                    throw new ArgumentException($"--step-recovery-steps: '{srsArg}' must be an integer >= 0");
                }
            }
            if (stepDetectBound || stepSizeForExecutedSweep) {
                Console.WriteLine($"F18 arm: detectBound={stepDetectBound} sizeForExecutedSweep={stepSizeForExecutedSweep}"
                    + (stepSizeForExecutedSweep ? $" recoveryStepsPerSide={stepRecoverySteps}" : string.Empty));
            }

            var catalogOverride = DiagnosticUtil.GetArg(args, "--catalog");
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");

            var specBytes = File.ReadAllBytes(specPath);
            var spec = JsonConvert.DeserializeObject<SynthBankSpec>(System.Text.Encoding.UTF8.GetString(specBytes))
                ?? throw new InvalidOperationException($"--spec '{specPath}' did not parse to a SynthBankSpec.");
            var specSha256 = Sha256Hex(specBytes);
            if (!string.IsNullOrWhiteSpace(catalogOverride)) {
                spec.Defaults.AstapCatalogPath = catalogOverride;
            }

            var allIds = spec.Datasets.Select(d => d.Id).ToList();
            List<SynthDatasetSpec> selectedDatasets;
            if (!string.IsNullOrWhiteSpace(datasetFilter)) {
                var wanted = new HashSet<string>(
                    datasetFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
                selectedDatasets = spec.Datasets.Where(d => wanted.Contains(d.Id)).ToList();
                foreach (var w in wanted) {
                    if (!allIds.Contains(w, StringComparer.OrdinalIgnoreCase)) {
                        Console.Error.WriteLine($"WARNING: --datasets requested unknown dataset id '{w}' (ignored).");
                    }
                }
            } else {
                selectedDatasets = spec.Datasets.ToList();
            }
            if (selectedDatasets.Count == 0) {
                Console.Error.WriteLine("No datasets selected (check --datasets against the spec's dataset ids).");
                Environment.ExitCode = 2;
                return;
            }

            List<SynthValidationScenario> selectedScenarios;
            if (!string.IsNullOrWhiteSpace(scenarioFilter)) {
                var wanted = new HashSet<string>(
                    scenarioFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
                selectedScenarios = SynthValidationScenarios.All.Where(s => wanted.Contains(s.Id)).ToList();
                foreach (var w in wanted) {
                    if (SynthValidationScenarios.ById(w) == null) {
                        Console.Error.WriteLine($"WARNING: --scenarios requested unknown scenario id '{w}' (ignored).");
                    }
                }
            } else {
                selectedScenarios = SynthValidationScenarios.All.ToList();
            }
            if (selectedScenarios.Count == 0) {
                Console.Error.WriteLine("No scenarios selected (check --scenarios against S0-S6).");
                Environment.ExitCode = 2;
                return;
            }

            Directory.CreateDirectory(outRoot);
            Logger.SetLogLevel(LogLevelEnum.INFO);

            // Application/profile setup mirrors BankVerifyRunner.RunCore: Run() already created the WPF Application
            // and installed the dispatcher SynchronizationContext on this STA thread.
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile
                ?? throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");

            // Detector settings come from the harness's LOCAL settings file, never the NINA profile (see
            // HarnessSettingsStore's remarks) -- the profile is only used for image loading (FITS.Load needs one).
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, activeProfile);
            var starDetectionOptions = new StarDetectionOptions(profileService, harnessSettings.Accessor);
            // F58(d): the pinned settings FILE, not the active profile -- the detector was pinned one line above
            // and the FIT was not, which is the asymmetry F58 named in `optimize`.
            var afOptions = HarnessSettingsStore.BuildFitOptions(profileService, harnessSettings);
            var alglibAPI = new AlglibAPI();
            var detection = new HocusFocusStarDetection(
                imageStatisticsVM: null,
                profileService: profileService,
                focuserMediator: new StubFocuserMediator(),
                starDetectionOptions: starDetectionOptions,
                alglibAPI: alglibAPI,
                perFilterStore: new StubPerFilterStarDetectionStore(harnessSettings.Accessor));

            var ctx = new SharedContext {
                MarginalSnrStrength = marginalSnrStrength,
                StepDetectBound = stepDetectBound,
                StepSizeForExecutedSweep = stepSizeForExecutedSweep,
                StepRecoveryStepsPerSide = stepRecoverySteps,
                ProfileService = profileService,
                HarnessSettings = harnessSettings,
                AfOptions = afOptions,
                AlglibAPI = alglibAPI,
                Detection = detection
            };

            IAstapCatalogReader catalogReader = new AstapCatalogReader(spec.Defaults.AstapCatalogPath);

            Prog($"synth-validate: spec={specPath} (sha256={specSha256.Substring(0, 12)}...) " +
                $"datasets={selectedDatasets.Count}/{spec.Datasets.Count} scenarios=[{string.Join(",", selectedScenarios.Select(s => s.Id))}] " +
                $"maxRounds={maxRounds} maxEvals={(maxEvals?.ToString(CultureInfo.InvariantCulture) ?? "default")} out={outRoot}");
            // F58(d): VALUES, not a hash — a hash says something moved, these say which one.
            var fitInputs = HarnessFitInputs.From(afOptions).ToString();
            Prog($"synth-validate: Profile: {activeProfile.Name} ({activeProfile.Id})  FitInputs: {fitInputs}");

            var report = new SynthValidationReport {
                SpecPath = specPath,
                SpecSha256 = specSha256,
                OutDir = outRoot,
                MaxRounds = maxRounds,
                MaxEvals = maxEvals,
                FitInputs = fitInputs,
                ProfileId = $"{activeProfile.Name} ({activeProfile.Id})"
            };
            var jsonPath = Path.Combine(outRoot, "synth_validate_report.json");
            var mdPath = Path.Combine(outRoot, "synth_validate_report.md");

            var anyFail = false;
            foreach (var dataset in selectedDatasets) {
                try {
                    var datasetReport = await ProcessDatasetAsync(
                        spec, dataset, catalogReader, outRoot, maxRounds, maxEvals, selectedScenarios, ctx, CancellationToken.None);
                    report.Datasets.Add(datasetReport);
                    if (datasetReport.Scenarios.Any(s => s.Terminal?.OverallVerdict == SynthValidationVerdict.Fail)) {
                        anyFail = true;
                    }
                } catch (Exception ex) {
                    anyFail = true;
                    Console.Error.WriteLine($"[{dataset.Id}] FAILED: {ex.GetType().Name}: {ex.Message}");
                    Logger.Error(ex, $"synth-validate failed for dataset '{dataset.Id}'");
                    report.Datasets.Add(new DatasetValidationReport {
                        DatasetId = dataset.Id,
                        Scenarios = new List<ScenarioValidationReport> {
                            new ScenarioValidationReport {
                                ScenarioId = "(dataset)",
                                Description = "dataset-level failure (expected-optimal derivation or kernel-cap guard)",
                                Applicable = true,
                                Terminal = new ScenarioTerminal {
                                    OverallVerdict = SynthValidationVerdict.Fail,
                                    StoppedReason = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}",
                                    Assertions = new List<AssertionFinding> { Fail("RUNTIME", ex.ToString()) }
                                }
                            }
                        }
                    });
                }

                // Flushed, incremental report write -- these runs are hours long and detached under WSL (which
                // block-buffers stdout), so a partial JSON/MD after every dataset is the only artifact an operator
                // can inspect mid-run (design: "a flushed progress log ... these runs are hours long").
                SynthValidationReportWriter.WriteJson(jsonPath, report);
                SynthValidationReportWriter.WriteMarkdown(mdPath, report);
                Console.Out.Flush();
            }

            Prog($"synth-validate: complete. Wrote {jsonPath} and {mdPath}.");
            if (anyFail) {
                Environment.ExitCode = 1;
            }
        }

        // ── Shared, dataset/scenario/round-independent dependencies ─────────────────────────────────────────────

        private sealed class SharedContext {
            public IProfileService ProfileService;
            public HarnessSettingsStore.Resolved HarnessSettings;
            public AutoFocusOptions AfOptions;
            public AlglibAPI AlglibAPI;
            public IHocusFocusStarDetection Detection;

            /// <summary>F23: override ObjectiveConstants.MarginalSnrStrength for this pass; null ⇒ shipping default
            /// (0, i.e. the term is off and J is bit-identical to the pre-F23 objective).</summary>
            public double? MarginalSnrStrength;

            /// <summary>F18 arm D — <c>--step-detect-bound</c>: bound the step recommendation's half-width by the
            /// outermost frame that still detected NHard stars. False ⇒ bit-identical to the pre-F18 driver.</summary>
            public bool StepDetectBound;

            /// <summary>F18 arm S — <c>--step-size-for-executed-sweep</c>: additionally size the step for the points
            /// the run actually VISITS (offset + recovery per side), not the offset steps alone.</summary>
            public bool StepSizeForExecutedSweep;

            /// <summary>Focus-recovery steps per side to assume for arm S. Synthetic sweeps carry none, so the arm
            /// has to be told what the LIVE engine would add; 1 is the shipped default.</summary>
            public int StepRecoveryStepsPerSide = 1;
        }

        /// <summary>Mutable round-to-round state for one (dataset, scenario) run: the bootstrap the NEXT round will
        /// render with, after the current round's update policy has been applied.</summary>
        private sealed class ScenarioRunState {
            public int CenterPosition;
            public int StepSize;
            public double ExposureSeconds;
            public int DetectionBinningFactor;
            public bool DonutOn;
        }

        // ── Per-dataset orchestration ─────────────────────────────────────────────────────────────────────────

        private static async Task<DatasetValidationReport> ProcessDatasetAsync(
                SynthBankSpec spec, SynthDatasetSpec dataset, IAstapCatalogReader catalogReader, string outRoot,
                int maxRounds, int? maxEvals, IReadOnlyList<SynthValidationScenario> scenarios,
                SharedContext ctx, CancellationToken token) {
            var defaults = spec.Defaults;
            var report = new DatasetValidationReport { DatasetId = dataset.Id };

            Prog($"[{dataset.Id}] deriving expected-optimal bootstrap...");
            var model = SynthBankDerivations.BuildDefocusModel(dataset, defaults);
            // F18: the arm flag drives the bank's TARGET as well as the runtime recommender, so each arm is
            // internally consistent (see DeriveExpectedOptimal's detectBoundedStep remarks).
            var expected = SynthBankDerivations.DeriveExpectedOptimal(dataset, defaults, catalogReader, out var kernelCapGuard, out _, token,
                detectBoundedStep: ctx.StepDetectBound);

            // Apply the checked-in spec's *Override fields on top of the physics answer, exactly as
            // SynthBankRunner.ProcessDatasetAsync does for the bank generator -- S0's bootstrap must match what is
            // ACTUALLY in the bank (the checked-in, possibly-overridden value), not the raw derived one. That
            // method is private and tightly coupled to the generator's own dry-run/verify control flow, and
            // SynthBankRunner.cs is not in this task's edit list, so the (small, override-application-only) logic
            // is duplicated here rather than the derivation itself.
            if (dataset.StepSizeOverride.HasValue && dataset.StepSizeOverride.Value != expected.StepSizeSteps) {
                expected.StepSizeSteps = dataset.StepSizeOverride.Value;
                kernelCapGuard = SynthBankDerivations.DeriveKernelCapGuard(model, defaults.OffsetSteps, expected.StepSizeSteps);
            }
            if (dataset.DetectionBinningOverride.HasValue) {
                expected.DetectionBinning = dataset.DetectionBinningOverride.Value;
            }
            if (dataset.DonutOverride.HasValue) {
                expected.DonutDetection = dataset.DonutOverride.Value;
            }

            if (!kernelCapGuard.WithinCap) {
                throw new InvalidOperationException(
                    $"kernel-cap guard failed for the EXPECTED-OPTIMAL bootstrap itself (utilization {kernelCapGuard.Utilization:P1}) " +
                    "-- cannot validate this dataset (this is a dataset spec error, not a scenario finding).");
            }
            if (double.IsNaN(expected.ExposureSeconds)) {
                throw new InvalidOperationException($"expected-optimal exposure could not be derived ({expected.ExposureDefinition}).");
            }

            var stepTheory = (double)expected.StepSizeSteps;
            var stepBehavioral = ComputeStepBehavioral(model, defaults.OffsetSteps, expected.StepSizeSteps, ctx.AlglibAPI,
                ctx.StepDetectBound ? expected.DetectableHalfWidthSteps : double.NaN,
                ctx.StepSizeForExecutedSweep ? ctx.StepRecoveryStepsPerSide : 0, ctx.StepSizeForExecutedSweep);
            var deltaPct = stepTheory > 0 && double.IsFinite(stepBehavioral) ? (stepBehavioral - stepTheory) / stepTheory : double.NaN;
            Prog($"[{dataset.Id}] step_theory={stepTheory:0.##} step_behavioral={stepBehavioral:0.##} (delta {deltaPct:P1})");

            var datasetOutDir = Path.Combine(outRoot, dataset.Id);
            FitSnapshot s0Round0Fit = null;

            foreach (var scenario in scenarios) {
                var (applicable, reason) = scenario.IsApplicable(dataset, expected);
                if (!applicable) {
                    report.Scenarios.Add(new ScenarioValidationReport {
                        ScenarioId = scenario.Id,
                        Description = scenario.Description,
                        Applicable = false,
                        SkipReason = reason
                    });
                    Prog($"[{dataset.Id}/{scenario.Id}] not applicable: {reason}");
                    continue;
                }

                Prog($"[{dataset.Id}/{scenario.Id}] starting...");
                try {
                    var scenarioReport = await RunScenarioAsync(
                        scenario, dataset, defaults, expected, model, stepTheory, stepBehavioral,
                        datasetOutDir, maxRounds, maxEvals, ctx, s0Round0Fit, token);
                    report.Scenarios.Add(scenarioReport);
                    if (string.Equals(scenario.Id, "S0", StringComparison.OrdinalIgnoreCase) && scenarioReport.Rounds.Count > 0) {
                        s0Round0Fit = scenarioReport.Rounds[0].Fit;
                    }
                    Prog($"[{dataset.Id}/{scenario.Id}] done: verdict={scenarioReport.Terminal.OverallVerdict} " +
                        $"converged={scenarioReport.Terminal.Converged} roundsUsed={scenarioReport.Terminal.RoundsUsed}");
                } catch (Exception ex) {
                    Console.Error.WriteLine($"[{dataset.Id}/{scenario.Id}] FAILED: {ex.GetType().Name}: {ex.Message}");
                    Logger.Error(ex, $"synth-validate scenario failed for '{dataset.Id}/{scenario.Id}'");
                    report.Scenarios.Add(new ScenarioValidationReport {
                        ScenarioId = scenario.Id,
                        Description = scenario.Description,
                        Applicable = true,
                        Terminal = new ScenarioTerminal {
                            OverallVerdict = SynthValidationVerdict.Fail,
                            StoppedReason = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}",
                            Assertions = new List<AssertionFinding> { Fail("RUNTIME", ex.ToString()) }
                        }
                    });
                }
                Console.Out.Flush();
            }

            return report;
        }

        // ── Per-scenario round loop ──────────────────────────────────────────────────────────────────────────

        private static async Task<ScenarioValidationReport> RunScenarioAsync(
                SynthValidationScenario scenario, SynthDatasetSpec dataset, SynthBankDefaults defaults,
                SynthExpectedOptimal expected, DefocusModel model, double stepTheory, double stepBehavioral,
                string datasetOutDir, int maxRounds, int? maxEvals, SharedContext ctx,
                FitSnapshot s0Round0Fit, CancellationToken token) {
            var report = new ScenarioValidationReport {
                ScenarioId = scenario.Id,
                Description = scenario.Description,
                Applicable = true
            };
            var isConvergenceScenario = scenario.Expectation == SynthValidationExpectation.Convergence;

            // S5 ("donut off ... NOT convergence") is a single-observation degradation probe -- design: "Donut is
            // never toggled by the loop", so nothing changes round over round and further rounds would only
            // re-render the same experiment under fresh noise. Not stated explicitly in the design; this
            // implementation's resolution of that gap.
            var effectiveMaxRounds = isConvergenceScenario ? maxRounds : 1;

            var state = new ScenarioRunState {
                CenterPosition = dataset.OptimalFocuserPosition,
                StepSize = Math.Max(1, (int)Math.Round(expected.StepSizeSteps * scenario.StepFactor, MidpointRounding.AwayFromZero)),
                ExposureSeconds = expected.ExposureSeconds * scenario.ExposureFactor,
                DetectionBinningFactor = scenario.ForceDetectionBinningTo1 ? 1 : expected.DetectionBinning,
                DonutOn = scenario.ForceDonutOff ? false : expected.DonutDetection
            };

            var scenarioDir = Path.Combine(datasetOutDir, scenario.Id);
            string stoppedReason = null;
            var converged = false;

            // ONE definition of the tolerance band a convergence CLAIM is judged against, shared by both stop
            // branches below. They used to disagree: a round that applied nothing declared convergence at ANY
            // step, while assertion A3 scored that same step against a band and FAILED it — `D05_tec140_1000mm`
            // S2 reported `converged: true` with `finalStepSize` 140 against `stepBehavioral` 35 and an A3 band
            // of [21,56], in the same JSON object as the assertion rejecting it. That inflates convergence counts
            // on precisely the runs that are most broken, and it is the fourth bug found in this family.
            //
            // The band is the loop's own (±max(0.5, tolerance·|step_behavioral|), i.e. [0.6,1.4]× at the default
            // tolerance 0.4), which is a strict SUBSET of A3's [0.6,1.6]× and shares its lower bound — so a run
            // this now calls converged cannot be one A3 fails. Gated only on `stepBehavioral` being a finite
            // fixed point, exactly like A3, rather than on `isConvergenceScenario`: whether stopping EARLY is
            // desirable depends on the scenario, but whether a stop deserves to be CALLED convergence does not.
            var stepBand = ConvergenceBand.HalfWidth(stepBehavioral, expected.StepSizeTolerance);
            var bandKnown = double.IsFinite(stepBand);
            bool WithinStepBand(int step) => ConvergenceBand.Contains(step, stepBehavioral, stepBand);

            for (var roundIndex = 0; roundIndex < effectiveMaxRounds; roundIndex++) {
                token.ThrowIfCancellationRequested();
                var roundDir = Path.Combine(scenarioDir, $"round_{roundIndex}");
                var attemptDir = Path.Combine(roundDir, "attempt01");
                var seed = SeedMixer.Combine(dataset.DatasetSeed, ScenarioIdHash(scenario.Id), roundIndex);

                Prog($"  [{dataset.Id}/{scenario.Id}] round {roundIndex}: center={state.CenterPosition} step={state.StepSize} " +
                    $"exposure={state.ExposureSeconds:0.###}s detBin={state.DetectionBinningFactor} donut={state.DonutOn}");

                var (roundReport, stop, reason) = await RunRoundAsync(
                    dataset, defaults, model, state, roundIndex, attemptDir, seed, maxEvals, ctx,
                    expected, stepBehavioral, isConvergenceScenario, token);

                report.Rounds.Add(roundReport);
                Console.Out.Flush();

                if (stop) {
                    stoppedReason = reason;
                    break;
                }
                if (!roundReport.Applied.AppliedAnything) {
                    // A no-op round always STOPS the loop — the recommender is not going to move on its own — but
                    // it is only CONVERGENCE if it stopped somewhere the scenario would accept. A degenerate fit
                    // produces a byte-identical no-op: `StepSizeRecommender.Degenerate` holds the current step
                    // when there is no `Fitting`, when the vertex or minimum HFR is non-finite, or when the 3x
                    // band is never crossed. So "nothing changed" is exactly as consistent with a broken fit as
                    // with a converged one, and the step VALUE is the only thing that separates them.
                    if (!bandKnown || WithinStepBand(state.StepSize)) {
                        converged = true;
                        stoppedReason = "converged (round applied nothing)";
                    } else {
                        stoppedReason = $"stalled (round applied nothing, but step {state.StepSize} is outside the "
                            + $"{stepBand:0.###} tolerance band of step_behavioral {stepBehavioral:0.###}) -- "
                            + "a no-op recommendation from a degenerate fit, not convergence";
                    }
                    break;
                }
                // Converged ALSO means "inside the tolerance band", not only "applied literally nothing". The
                // recommender never emits a byte-identical answer on consecutive rounds -- each round is a fresh
                // noise realization, so it keeps nudging by a percent or two forever. Waiting for an exact no-op
                // therefore runs every scenario to --max-rounds and reports RoundsUsed = max, which then fails
                // scenarios whose whole expectation is "converges quickly": D13's S2 reached 131 against a target
                // of 127 in ONE round from a 4x-too-coarse start of 508, then jittered 132/129/124, and was
                // flagged "expected convergence within <= 2 round(s), used 4". Stopping at the band makes
                // RoundsUsed mean "rounds to reach the target", which is what the scenario expectations are about.
                if (isConvergenceScenario && WithinStepBand(state.StepSize)) {
                    converged = true;
                    stoppedReason = $"converged (step {state.StepSize} within the {stepBand:0.###} tolerance band of step_behavioral {stepBehavioral:0.###})";
                    break;
                }
            }

            // `converged` is now set explicitly at each stop site rather than derived by string-matching
            // `stoppedReason` for a "converged" prefix, so the flag and the human-readable reason cannot drift
            // apart — which is how a stall came to be labelled as convergence in the first place.
            var terminal = new ScenarioTerminal {
                Converged = converged,
                StepToleranceBand = stepBand,
                RoundsUsed = report.Rounds.Count,
                StoppedReason = stoppedReason ?? $"reached --max-rounds ({effectiveMaxRounds})",
                StepTheory = stepTheory,
                StepBehavioral = stepBehavioral,
                StepBehavioralVsTheoryDeltaFraction = stepTheory > 0 && double.IsFinite(stepBehavioral) ? (stepBehavioral - stepTheory) / stepTheory : double.NaN,
                FinalStepSize = state.StepSize,
                FinalExposureSeconds = state.ExposureSeconds,
                FinalDetectionBinning = state.DetectionBinningFactor,
                FinalCenterPosition = state.CenterPosition,
                ExpectedStepSize = expected.StepSizeSteps,
                ExpectedExposureSeconds = expected.ExposureSeconds,
                ExpectedDetectionBinning = expected.DetectionBinning,
                DeltaStepVsExpected = state.StepSize - expected.StepSizeSteps,
                DeltaExposureVsExpectedFraction = expected.ExposureSeconds > 0 ? (state.ExposureSeconds - expected.ExposureSeconds) / expected.ExposureSeconds : double.NaN,
                DeltaBinningVsExpected = state.DetectionBinningFactor - expected.DetectionBinning
            };

            List<AssertionFinding> terminalFindings;
            if (isConvergenceScenario) {
                terminalFindings = EvaluateTerminalAssertions(report, expected, stepTheory, stepBehavioral, terminal);
                // S2's "converges within <= 2 rounds" -- soft expectation, a FLAG (not a FAIL) when exceeded, since
                // the driver's convergence-RATE expectation is softer than the A1-A7 hard invariants.
                if (scenario.ExpectedMaxRoundsForConvergence.HasValue && terminal.RoundsUsed > scenario.ExpectedMaxRoundsForConvergence.Value) {
                    terminalFindings.Add(Flag(scenario.Id,
                        $"expected convergence within <= {scenario.ExpectedMaxRoundsForConvergence.Value} round(s), used {terminal.RoundsUsed}"));
                }
            } else {
                terminalFindings = new List<AssertionFinding>();
                var round0Fit = report.Rounds.Count > 0 ? report.Rounds[0].Fit : null;
                var (present, detail) = DetectDegradation(s0Round0Fit, round0Fit);
                terminal.DegradationSignaturePresent = present;
                terminal.DegradationDetail = detail;
                terminalFindings.Add(present
                    ? Pass(scenario.Id, $"degradation signature present: {detail}")
                    : Flag(scenario.Id, $"degradation signature NOT observed: {detail}"));
            }

            terminal.Assertions = terminalFindings;
            var allFindings = report.Rounds.SelectMany(r => r.Assertions).Concat(terminalFindings).ToList();
            terminal.OverallVerdict = Worst(allFindings);
            report.Terminal = terminal;
            return report;
        }

        // ── One round: render, load, optimize, recommend, apply, assert ─────────────────────────────────────────

        private static async Task<(RoundValidationReport Round, bool Stop, string Reason)> RunRoundAsync(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, DefocusModel model, ScenarioRunState state,
                int roundIndex, string attemptDir, int seed, int? maxEvals, SharedContext ctx,
                SynthExpectedOptimal expected, double stepBehavioral, bool isConvergenceScenario, CancellationToken token) {
            var round = new RoundValidationReport { RoundIndex = roundIndex };
            var bootstrapSnapshotBase = new BootstrapSnapshot {
                CenterPosition = state.CenterPosition,
                StepSize = state.StepSize,
                OffsetSteps = defaults.OffsetSteps,
                ExposureSeconds = state.ExposureSeconds,
                AfBinning = dataset.CaptureBinning,
                DetectionBinning = state.DetectionBinningFactor,
                DonutDetection = state.DonutOn,
                Seed = seed
            };

            // Kernel-cap guard -- BEFORE rendering (design §G4). An aggressive scenario perturbation (S2's 4x step,
            // or several rounds of ~1.7x-per-round capped growth) can in principle push the sweep's extreme defocus
            // past what PsfKernelGenerator can safely render; stopping here with a recorded reason beats letting
            // StarFieldCompositor.Render throw and aborting the whole multi-hour batch.
            var guard = SynthBankDerivations.DeriveKernelCapGuard(model, defaults.OffsetSteps, state.StepSize);
            if (!guard.WithinCap) {
                round.KernelCapGuardExceeded = true;
                round.Bootstrap = bootstrapSnapshotBase;
                round.Fit = new FitSnapshot();
                round.Applied = new AppliedSnapshot { NewCenterPosition = state.CenterPosition };
                return (round, true, $"kernel-cap guard exceeded at round {roundIndex} (utilization {guard.Utilization:P1}, " +
                    $"step={state.StepSize}) -- too large for this optical train at offsetSteps={defaults.OffsetSteps}");
            }

            var bootstrap = new SynthBootstrapParams {
                CenterPosition = state.CenterPosition,
                StepSize = state.StepSize,
                OffsetSteps = defaults.OffsetSteps,
                ExposureSeconds = state.ExposureSeconds,
                AfBinning = dataset.CaptureBinning,
                EmitGoldens = false // this loop measures fit/recommendations, not P/R (design instruction)
            };

            SweepResult sweepResult;
            try {
                sweepResult = SynthBankRunner.GenerateSweep(dataset, defaults, bootstrap, attemptDir, seed, token);
            } catch (InvalidOperationException ex) {
                round.Bootstrap = bootstrapSnapshotBase;
                round.Fit = new FitSnapshot();
                round.Applied = new AppliedSnapshot { NewCenterPosition = state.CenterPosition };
                return (round, true, $"GenerateSweep refused round {roundIndex}: {ex.Message}");
            }

            // Load EXACTLY as `optimize` does (design step 2): OptimizationRunDiscovery over the attempt folder,
            // DiagnosticUtil.LoadRenderedImage per frame, first frame's ImageMetaData -> PixelScaleForFrame /
            // CapturedExposureSeconds, step INFERRED from the positions (never the nominal bootstrap value) --
            // OptimizationDiagnosticRunner.InferStepSize, widened internal for this reuse.
            var discovery = OptimizationRunDiscovery.Discover(attemptDir);
            if (discovery.Runs.Count != 1) {
                throw new InvalidOperationException(
                    $"Expected exactly one discovered run at '{attemptDir}' ({sweepResult.Frames.Count} frames written), found {discovery.Runs.Count}.");
            }
            var discovered = discovery.Runs[0];

            var frames = new List<RunFrame>(discovered.Frames.Count);
            NINA.Image.ImageData.ImageMetaData firstFrameMeta = null;
            var pixelScale = double.NaN;
            var pixelScaleSource = "unset";
            var capturedExposureSeconds = double.NaN;
            for (var i = 0; i < discovered.Frames.Count; i++) {
                var frame = discovered.Frames[i];
                var rendered = await DiagnosticUtil.LoadRenderedImage(frame.Path, ctx.ProfileService);
                if (i == 0) {
                    firstFrameMeta = rendered.RawImageData?.MetaData;
                    pixelScale = HarnessSettingsStore.PixelScaleForFrame(firstFrameMeta, ctx.HarnessSettings, out pixelScaleSource);
                    capturedExposureSeconds = firstFrameMeta?.Image?.ExposureTime ?? double.NaN;
                }
                frames.Add(new RunFrame { FrameId = frame.Path, FocuserPosition = frame.FocuserPosition, Image = rendered });
            }
            var inferredStepSize = OptimizationDiagnosticRunner.InferStepSize(discovered.Frames);

            round.Bootstrap = new BootstrapSnapshot {
                CenterPosition = state.CenterPosition,
                StepSize = state.StepSize,
                OffsetSteps = defaults.OffsetSteps,
                ExposureSeconds = state.ExposureSeconds,
                AfBinning = dataset.CaptureBinning,
                DetectionBinning = state.DetectionBinningFactor,
                DonutDetection = state.DonutOn,
                Seed = seed,
                InferredStepSize = inferredStepSize,
                CapturedExposureSeconds = capturedExposureSeconds,
                PixelScale = pixelScale,
                PixelScaleSource = pixelScaleSource
            };

            var fitConfig = new RunFitConfig {
                StepSize = inferredStepSize,
                UseWeights = ctx.AfOptions.WeightedHyperbolicFitEnabled,
                MaxOutlierRejections = ctx.AfOptions.MaxOutlierRejections,
                RejectionConfidence = ctx.AfOptions.OutlierRejectionConfidence,
                PreferredModel = ctx.AfOptions.HyperbolicFitModel
            };

            var seedParams = HocusFocusStarDetection.BuildDefaultStarDetectorParams();
            if (double.IsFinite(pixelScale)) {
                seedParams.PixelScale = pixelScale;
            }
            seedParams.Region = StarDetectionRegion.Full;
            seedParams.ModelPSF = false;
            seedParams.SaveIntermediateFilesPath = string.Empty;
            seedParams.SuppressInfoLogging = true;
            seedParams.DefocusAwareDonutDetection = state.DonutOn;
            // Detection binning is a bootstrap INPUT here (design: apply-detection-binning-first policy), applied
            // to the seed exactly as DetectionBinningResolver.ApplyFactor's own doc prescribes for "a factor the
            // user has not committed to yet" -- it also keeps PixelScale consistent with the factor.
            DetectionBinningResolver.ApplyFactor(seedParams, state.DetectionBinningFactor);

            var variables = OptimizerVariable.CreateCuratedSet(seedParams);
            var splitDetector = new RunEvaluationLoader.HocusFocusSplitFrameDetector(
                ctx.Detection, new HocusFocusDetectionParams { IsAutoFocus = true, NumberOfAFStars = 0 });
            var data = new RunEvaluationData(discovered.RunId, frames, splitDetector, ctx.AlglibAPI, fitConfig, labels: null);

            OptimizationResult result;
            RunEvaluationResult bestEval;
            var objectiveConstants = new ObjectiveConstants();
            if (ctx.MarginalSnrStrength.HasValue) {
                objectiveConstants.MarginalSnrStrength = ctx.MarginalSnrStrength.Value;
            }
            try {
                var evaluator = RunEvaluationData.CreateEvaluator(new List<RunEvaluationData> { data });
                var settings = new OptimizerSettings();
                if (maxEvals.HasValue) {
                    settings.MaxEvaluations = maxEvals.Value;
                }
                var optimizer = new StarDetectionOptimizer(objectiveConstants);
                result = await optimizer.OptimizeAsync(seedParams, variables, evaluator, settings, progress: null, token);
                bestEval = await data.EvaluateAndFitAsync(result.BestParams, token);
            } finally {
                data.Dispose();
                foreach (var rf in frames) { rf.Image = null; }
            }

            var bestFit = bestEval.BestFit;
            var metrics = bestEval.Metrics;

            round.Fit = new FitSnapshot {
                RSquared = metrics?.RSquared ?? double.NaN,
                ReducedChiSquared = metrics?.ReducedChiSquared ?? double.NaN,
                SigmaFocus = metrics?.SigmaFocus ?? double.NaN,
                VertexX = bestFit != null ? bestFit.Minimum.X : double.NaN,
                VertexY = bestFit != null ? bestFit.Minimum.Y : double.NaN,
                BestJ = result.BestJ,
                WorstFrameStarCount = metrics?.FrameStarCounts != null && metrics.FrameStarCounts.Count > 0 ? metrics.FrameStarCounts.Min() : 0,
                LandedSensitivity = result.BestParams.Sensitivity
            };

            // ---- Recommendations (design step 4) ----

            // F18: hand the recommender what this round actually DETECTED, so the half-width can be bounded by
            // detectability as well as by curve geometry. Null unless an arm asked for it, so the default driver
            // is bit-identical.
            var stepDetectability = (ctx.StepDetectBound || ctx.StepSizeForExecutedSweep)
                ? new SweepDetectability {
                    FrameStarCounts = ctx.StepDetectBound ? metrics?.FrameStarCounts : null,
                    FrameFocuserPositions = ctx.StepDetectBound ? metrics?.FrameFocuserPositions : null,
                    FrameIsRecovery = metrics?.FrameIsRecovery,
                    HardFloorStarCount = objectiveConstants.NHard,
                    RecoveryStepsPerSide = ctx.StepRecoveryStepsPerSide
                }
                : null;
            var stepRec = StepSizeRecommender.Recommend(bestFit, inferredStepSize, focuserMaxStep: null,
                detectability: stepDetectability, sizeForExecutedSweep: ctx.StepSizeForExecutedSweep);
            round.StepRecommendation = new StepRecommendationSnapshot {
                StepSize = stepRec.StepSize, OffsetSteps = stepRec.OffsetSteps, HalfWidth = stepRec.HalfWidth, WasCapped = stepRec.WasCapped,
                DetectHalfWidth = stepRec.DetectHalfWidth, MaxUsefulHalfSpan = stepRec.MaxUsefulHalfSpan,
                WasDetectBounded = stepRec.WasDetectBounded,
                SampledHfrRange = stepRec.SampledHfrRange, CappedGrowthRatio = stepRec.CappedGrowthRatio
            };

            // Gated EXACTLY as OptimizationDiagnosticRunner.BuildAggregateRow gates it: only when the landed
            // Sensitivity is at the optimizer's search floor. Never computed ungated (design instruction).
            var sensitivityAtFloor = ExposureRecommender.SensitivityIsAtFloor(result.BestParams.Sensitivity);
            var exposureRec = sensitivityAtFloor
                ? ExposureRecommender.Recommend(metrics, objectiveConstants, capturedExposureSeconds, result.BestParams)
                : null;
            round.ExposureRecommendation = new ExposureRecommendationSnapshot {
                SensitivityAtFloor = sensitivityAtFloor,
                Computed = sensitivityAtFloor,
                HasRecommendation = exposureRec?.HasRecommendation ?? false,
                CurrentSeconds = exposureRec?.CurrentSeconds ?? double.NaN,
                RecommendedSeconds = exposureRec?.RecommendedSeconds ?? double.NaN,
                MeasuredSnr = exposureRec?.MeasuredSnr ?? double.NaN,
                IncreasesExposure = exposureRec?.IncreasesExposure ?? false,
                WasCapped = exposureRec?.WasCapped ?? false,
                CappedByAbsoluteLimit = exposureRec?.CappedByAbsoluteLimit ?? false,
                ExposureIsNotTheLimit = exposureRec?.ExposureIsNotTheLimit ?? false,
                StarCountIsTheLimit = exposureRec?.StarCountIsTheLimit ?? false,
                StarFieldIsExhausted = exposureRec?.StarFieldIsExhausted ?? false
            };

            // Gated on fit R^2 >= MinRSquaredForBinningRecommendation -- that gate lives in the wizard
            // (OptimizationSummary, the wizard's per-variant summary type), not in DetectionBinningResolver itself,
            // so the driver applies it here (design instruction, noted per that instruction).
            var hasBinningMeasurement = bestFit != null && double.IsFinite(bestFit.Minimum.Y) && double.IsFinite(bestFit.RSquared)
                && bestFit.RSquared >= OptimizationSummary.MinRSquaredForBinningRecommendation;
            int? binningRecFactor = hasBinningMeasurement ? DetectionBinningResolver.RecommendFromHfr(bestFit.Minimum.Y) : (int?)null;
            round.BinningRecommendation = new BinningRecommendationSnapshot {
                HasMeasurement = hasBinningMeasurement,
                VertexHfr = bestFit != null ? bestFit.Minimum.Y : double.NaN,
                RecommendedFactor = binningRecFactor
            };

            // ---- Update policy (design step 5) ----

            var applied = new AppliedSnapshot();
            var binningDiffers = hasBinningMeasurement && binningRecFactor.HasValue && binningRecFactor.Value != state.DetectionBinningFactor;
            if (binningDiffers) {
                // Binning first: apply ONLY the binning change this round and defer exposure (and step) by one
                // round -- SNRs are per-binned-pixel, so changing binning invalidates the exposure measurement
                // (and the HFR-derived step geometry) this round just took.
                applied.BinningApplied = true;
                applied.Reasons.Add($"binning {state.DetectionBinningFactor} -> {binningRecFactor.Value} (deferring exposure/step this round)");
                state.DetectionBinningFactor = binningRecFactor.Value;
            } else {
                var exposureApplies = exposureRec != null && exposureRec.HasRecommendation && sensitivityAtFloor && exposureRec.IncreasesExposure;
                if (exposureApplies) {
                    applied.ExposureApplied = true;
                    applied.Reasons.Add($"exposure {state.ExposureSeconds:0.###}s -> {exposureRec.RecommendedSeconds:0.###}s");
                    state.ExposureSeconds = exposureRec.RecommendedSeconds;
                }
                if (stepRec.StepSize != state.StepSize) {
                    applied.StepApplied = true;
                    applied.Reasons.Add($"step {state.StepSize} -> {stepRec.StepSize}");
                    state.StepSize = stepRec.StepSize;
                }
            }

            // Re-centre on the fitted vertex every round, regardless of what else was applied.
            var newCenter = state.CenterPosition;
            if (bestFit != null && double.IsFinite(bestFit.Minimum.X)) {
                newCenter = (int)Math.Round(bestFit.Minimum.X, MidpointRounding.AwayFromZero);
            }
            applied.Recentered = newCenter != state.CenterPosition;
            if (applied.Recentered) {
                applied.Reasons.Add($"re-centred {state.CenterPosition} -> {newCenter}");
            }
            applied.NewCenterPosition = newCenter;
            state.CenterPosition = newCenter;
            round.Applied = applied;

            round.Assertions = EvaluateRoundAssertions(dataset, defaults, model, round, expected, stepBehavioral, isConvergenceScenario);
            return (round, false, null);
        }

        // ── Assertions A1-A7 (tri-state PASS/FLAG/FAIL) ──────────────────────────────────────────────────────

        private static List<AssertionFinding> EvaluateRoundAssertions(
                SynthDatasetSpec dataset, SynthBankDefaults defaults, DefocusModel model, RoundValidationReport round,
                SynthExpectedOptimal expected, double stepBehavioral, bool isConvergenceScenario) {
            var findings = new List<AssertionFinding>();

            // A1 -- direction: each recommendation moves toward its target. Skipped for a non-convergence scenario
            // (S5), whose bootstrap is not being walked toward anything.
            if (isConvergenceScenario) {
                // Two tolerances, two jobs -- see CheckDirection's remarks for why conflating them silently
                // inverts the verdict. The at-target BAND is relative (the design's StepSizeTolerance, 0.4),
                // because a control that starts on target must not fail for drifting a few percent. The
                // PROGRESS threshold is small, because a round that closes a third of the remaining gap is
                // converging and has to be scored as such.
                var stepAtTargetEps = Math.Max(0.5, expected.StepSizeTolerance * Math.Abs(stepBehavioral));
                var stepProgressEps = Math.Max(0.5, StepProgressFraction * Math.Abs(stepBehavioral));
                findings.Add(CheckDirection("A1", "step", round.Bootstrap.StepSize, round.StepRecommendation.StepSize,
                    stepBehavioral, stepAtTargetEps, stepProgressEps));
                if (round.ExposureRecommendation.Computed && round.ExposureRecommendation.HasRecommendation) {
                    // The at-target band is relative for the same reason; the progress threshold is floored at
                    // 0.5s because ExposureRecommender.RoundExposureSeconds quantizes onto a 0.5/1/5s ladder, so
                    // anything finer than one ladder rung is below the recommender's own output resolution.
                    var expAtTargetEps = Math.Max(0.5, 0.4 * expected.ExposureSeconds);
                    var expProgressEps = Math.Max(0.5, StepProgressFraction * expected.ExposureSeconds);
                    findings.Add(CheckDirection("A1", "exposure", round.Bootstrap.ExposureSeconds,
                        round.ExposureRecommendation.RecommendedSeconds, expected.ExposureSeconds, expAtTargetEps, expProgressEps));
                }
                if (round.BinningRecommendation.HasMeasurement && round.BinningRecommendation.RecommendedFactor.HasValue) {
                    findings.Add(CheckDirection("A1", "detectionBinning", round.Bootstrap.DetectionBinning, round.BinningRecommendation.RecommendedFactor.Value,
                        expected.DetectionBinning, atTargetEps: 0.001, progressEps: 0.001)); // integral factor -- exact match or nothing
                }
            }

            // A4 -- cap semantics: WasCapped iff truth half-width > MaxHalfWidthSampledHalfSpanMultiple(1.5) x
            // sampled half-span (the exact invariant InFocusHfrDiagnosticTests pins) -- computed against the
            // ANALYTIC truth curve (noiseless), then compared to what the REAL (noisy) fit's recommendation did.
            // Near the boundary a noisy fit can legitimately land on the other side, so a mismatch there is a FLAG,
            // not a FAIL; far from the boundary it is a genuine invariant violation.
            {
                var truthHalfWidth = Math.Sqrt(8.0) * model.HfrMinPixels / model.KappaPixelsPerStep;
                var sampledHalfSpan = defaults.OffsetSteps * round.Bootstrap.StepSize;
                var capBoundary = StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple * sampledHalfSpan;
                var predictedCapped = capBoundary > 0 && truthHalfWidth > capBoundary;
                var actualCapped = round.StepRecommendation.WasCapped;
                var ratio = capBoundary > 0 ? truthHalfWidth / capBoundary : double.PositiveInfinity;
                if (predictedCapped == actualCapped) {
                    findings.Add(Pass("A4", $"WasCapped={actualCapped} matches truth (halfWidth={truthHalfWidth:0.#} vs cap boundary {capBoundary:0.#}, ratio {ratio:0.00})"));
                } else if (Math.Abs(ratio - 1.0) < 0.15) {
                    findings.Add(Flag("A4", $"WasCapped={actualCapped} but truth predicts {predictedCapped} -- near the cap boundary (ratio {ratio:0.00}), noise-sensitive on a real fit"));
                } else {
                    findings.Add(Fail("A4", $"WasCapped={actualCapped} but truth predicts {predictedCapped} (halfWidth={truthHalfWidth:0.#} vs cap boundary {capBoundary:0.#}, ratio {ratio:0.00})"));
                }
            }

            // A5 -- state sanity: StarFieldIsExhausted only where plausible (sparse datasets, identified by the
            // "sparse" token the design's own dataset ids use for D06/D10); nothing applied when its own gate was
            // not satisfied (a driver self-consistency check); OffsetSteps always 4.
            {
                var looksSparse = dataset.Id.IndexOf("sparse", StringComparison.OrdinalIgnoreCase) >= 0;
                if (round.ExposureRecommendation.Computed && round.ExposureRecommendation.StarFieldIsExhausted && !looksSparse) {
                    findings.Add(Flag("A5", $"StarFieldIsExhausted=true on a non-sparse-looking dataset ('{dataset.Id}') -- plausible only for the sparse-class datasets"));
                } else {
                    findings.Add(Pass("A5", "StarFieldIsExhausted plausibility OK"));
                }

                var exposureAppliedOk = !round.Applied.ExposureApplied ||
                    (round.ExposureRecommendation.Computed && round.ExposureRecommendation.HasRecommendation &&
                     round.ExposureRecommendation.SensitivityAtFloor && round.ExposureRecommendation.IncreasesExposure);
                var binningAppliedOk = !round.Applied.BinningApplied ||
                    (round.BinningRecommendation.HasMeasurement && round.BinningRecommendation.RecommendedFactor.HasValue);
                findings.Add(exposureAppliedOk && binningAppliedOk
                    ? Pass("A5", "nothing applied whose own gate was not satisfied")
                    : Fail("A5", "the update policy applied a recommendation whose own gate was not satisfied -- driver bug"));

                const int ExpectedRecommendedOffsetSteps = 4; // StepSizeRecommender.DefaultOffsetSteps (private)
                findings.Add(round.StepRecommendation.OffsetSteps == ExpectedRecommendedOffsetSteps
                    ? Pass("A5", $"OffsetSteps == {ExpectedRecommendedOffsetSteps}")
                    : Fail("A5", $"OffsetSteps == {round.StepRecommendation.OffsetSteps}, expected {ExpectedRecommendedOffsetSteps}"));
            }

            // A6 -- fit gate: R^2 >= 0.95 per round (design). Graded: a fit below 0.80 is treated as clearly
            // broken (FAIL); [0.80, 0.95) is a FLAG (the design states only the 0.95 target, not a lower FAIL
            // floor -- this driver introduces one so genuinely degenerate fits still surface as failures).
            {
                const double FitGatePass = 0.95, FitGateFail = 0.80;
                var r2 = round.Fit.RSquared;
                if (!double.IsFinite(r2)) {
                    findings.Add(Fail("A6", "fit R^2 is non-finite (degenerate fit)"));
                } else if (r2 >= FitGatePass) {
                    findings.Add(Pass("A6", $"R^2={r2:0.####} >= {FitGatePass}"));
                } else if (r2 >= FitGateFail) {
                    findings.Add(Flag("A6", $"R^2={r2:0.####} below the {FitGatePass} target but above {FitGateFail}"));
                } else {
                    findings.Add(Fail("A6", $"R^2={r2:0.####} below {FitGateFail}"));
                }
            }

            // A7 -- vertex tracking: |vertex - centerPosition| <= max(2*step, 0.02*W_3x), where W_3x is the FULL
            // width (2x the half-width) of the truth curve's HFR-reaches-3x-minimum band -- the design does not
            // define W_3x explicitly; this is this implementation's reading of it (a curve-geometry-scaled
            // tolerance, consistent with the same 3x band StepSizeRecommender itself sizes steps from).
            {
                var vertexX = round.Fit.VertexX;
                if (!double.IsFinite(vertexX)) {
                    findings.Add(Flag("A7", "no finite fitted vertex to check vertex tracking against"));
                } else {
                    var w3x = 2.0 * Math.Sqrt(8.0) * model.HfrMinPixels / model.KappaPixelsPerStep;
                    var tolerance = Math.Max(2.0 * round.Bootstrap.StepSize, 0.02 * w3x);
                    var delta = Math.Abs(vertexX - round.Bootstrap.CenterPosition);
                    if (delta <= tolerance) {
                        findings.Add(Pass("A7", $"|vertex-center|={delta:0.##} <= tolerance {tolerance:0.##}"));
                    } else if (delta <= 2.0 * tolerance) {
                        findings.Add(Flag("A7", $"|vertex-center|={delta:0.##} exceeds tolerance {tolerance:0.##} (within 2x)"));
                    } else {
                        findings.Add(Fail("A7", $"|vertex-center|={delta:0.##} exceeds tolerance {tolerance:0.##} by more than 2x"));
                    }
                }
            }

            return findings;
        }

        /// <summary>A2 (monotone approach, no oscillation) and A3 (terminal band vs step_behavioral, plus the F18
        /// divergence flag), evaluated once over the whole round sequence.</summary>
        private static List<AssertionFinding> EvaluateTerminalAssertions(
                ScenarioValidationReport report, SynthExpectedOptimal expected,
                double stepTheory, double stepBehavioral, ScenarioTerminal terminal) {
            var findings = new List<AssertionFinding>();

            findings.Add(CheckMonotone("A2", "step",
                report.Rounds.Select(r => (double)r.Bootstrap.StepSize).Append((double)terminal.FinalStepSize).ToList(),
                stepBehavioral, Math.Max(0.5, expected.StepSizeTolerance * Math.Abs(stepBehavioral))));
            findings.Add(CheckMonotone("A2", "exposure",
                report.Rounds.Select(r => r.Bootstrap.ExposureSeconds).Append(terminal.FinalExposureSeconds).ToList(),
                expected.ExposureSeconds, Math.Max(0.5, 0.4 * expected.ExposureSeconds)));

            // A3 -- terminal band [0.6, 1.6] x step_behavioral. Deliberately asserted against step_behavioral (the
            // recommender's OWN fixed point on the truth curve), never against step_theory -- see the design's
            // crux (F18: StepSizeRecommender sizes by curve geometry alone, so its fixed point legitimately
            // differs from the theoretical step*; asserting against theory would re-report that as a new failure).
            if (double.IsFinite(stepBehavioral) && stepBehavioral > 0) {
                var lo = 0.6 * stepBehavioral;
                var hi = 1.6 * stepBehavioral;
                findings.Add(terminal.FinalStepSize >= lo && terminal.FinalStepSize <= hi
                    ? Pass("A3", $"final step {terminal.FinalStepSize} within [{lo:0.##},{hi:0.##}] = [0.6,1.6]x step_behavioral ({stepBehavioral:0.##})")
                    : Fail("A3", $"final step {terminal.FinalStepSize} outside [{lo:0.##},{hi:0.##}] = [0.6,1.6]x step_behavioral ({stepBehavioral:0.##})"));
            } else {
                findings.Add(Flag("A3", "step_behavioral did not converge to a finite fixed point -- cannot evaluate the terminal band"));
            }

            if (double.IsFinite(stepTheory) && stepTheory > 0 && double.IsFinite(stepBehavioral)) {
                var deltaFrac = Math.Abs(stepBehavioral - stepTheory) / stepTheory;
                if (deltaFrac > 0.25) {
                    findings.Add(Flag("A3", $"step_behavioral ({stepBehavioral:0.##}) vs step_theory ({stepTheory:0.##}) differ by {deltaFrac:P0} (> 25%) -- " +
                        "see docs/followups.md F18 (StepSizeRecommender sizes by curve geometry alone with no detectability term, so its fixed point legitimately differs from theory)"));
                }
            }

            return findings;
        }

        /// <summary>
        /// A1 -- direction. Takes TWO tolerances, because the question is really two questions and they want
        /// different scales:
        /// <list type="bullet">
        /// <item><paramref name="atTargetEps"/> is a BAND AROUND THE TARGET (relative — the design's
        /// <c>StepSizeTolerance</c>, 0.4). "Am I already close enough that any further motion is noise?"</item>
        /// <item><paramref name="progressEps"/> is a MINIMUM PER-ROUND IMPROVEMENT (small, near-absolute).
        /// "Did this round actually close some of the gap?"</item>
        /// </list>
        ///
        /// <para>Using one value for both is wrong in a way that silently inverts the result, and did: with a
        /// single relative eps of <c>0.4 × 64 = 25.6</c>, a round had to close 25.6 steps of gap to count as
        /// progress, so D15's S1 sequence <c>16 → 27 → 46 → 63 → 64</c> against a target of 64 — error shrinking
        /// 48 → 37 → 18 → 1, textbook geometric convergence — was scored "made no meaningful progress" twice.
        /// The first full S1–S6 matrix returned 42 FLAGs largely for this reason. A harness that reports clean
        /// convergence as a flag is as useless as one that reports failure as a pass.</para>
        ///
        /// <para>The at-target branch also checks where the recommendation LANDS, not just where the round
        /// started. Otherwise a control scenario (S0, which by construction starts on target) would pass
        /// unconditionally no matter how wild the recommendation was — which is exactly D17's
        /// <c>60 → 3</c> half-width collapse (F21), and it must not be scored PASS.</para>
        /// </summary>
        /// <summary>
        /// Minimum fraction of the TARGET a round must close to count as progress (A1). Deliberately small and
        /// unrelated to the at-target band: convergence here is geometric -- a round that closes a third of the
        /// remaining gap is healthy -- so the bar is "moved measurably", not "arrived".
        /// </summary>
        private const double StepProgressFraction = 0.02;

        private static AssertionFinding CheckDirection(
                string id, string label, double current, double recommended, double target,
                double atTargetEps, double progressEps) {
            if (!double.IsFinite(target)) {
                return Flag(id, $"{label}: no finite target to check direction against");
            }
            var distBefore = Math.Abs(current - target);
            var distAfter = Math.Abs(recommended - target);

            if (distBefore <= atTargetEps) {
                return distAfter <= atTargetEps
                    ? Pass(id, $"{label}: already at target ({current:0.###} vs target {target:0.###}); recommendation {recommended:0.###} stays within the {atTargetEps:0.###} band")
                    : Flag(id, $"{label}: started at target ({current:0.###} vs {target:0.###}) but recommended {recommended:0.###}, which leaves the {atTargetEps:0.###} band (|delta| {distBefore:0.###} -> {distAfter:0.###})");
            }
            if (distAfter < distBefore - progressEps) {
                return Pass(id, $"{label}: {current:0.###} -> {recommended:0.###} moves toward target {target:0.###} (|delta| {distBefore:0.###} -> {distAfter:0.###})");
            }
            if (distAfter <= distBefore + progressEps) {
                return Flag(id, $"{label}: {current:0.###} -> {recommended:0.###} made no meaningful progress toward target {target:0.###} (|delta| {distBefore:0.###} -> {distAfter:0.###})");
            }
            return Fail(id, $"{label}: {current:0.###} -> {recommended:0.###} moved AWAY from target {target:0.###} (|delta| {distBefore:0.###} -> {distAfter:0.###})");
        }

        /// <summary>
        /// A2 -- no oscillation: at most one sign change of <c>(value − target)</c> across the round sequence (a
        /// single crossing on the way to convergence is normal; more than one is back-and-forth).
        ///
        /// <para><b>Values already inside the tolerance band are excluded from the sign sequence.</b> Once a
        /// recommendation is within tolerance it keeps jittering by a few percent, because every round is a fresh
        /// noise realization and the fit shifts slightly — the recommender never emits a byte-identical answer
        /// twice. Counting those crossings scores healthy settling as oscillation: D15's S0 control sits at
        /// 64 → 61 → 62 → 65 → 61 against a target of 64, i.e. ±5% jitter around the right answer, and was scored
        /// FAIL for "2 sign changes". Oscillation means failing to close on the target, so only excursions
        /// OUTSIDE the band can evidence it.</para>
        /// </summary>
        private static AssertionFinding CheckMonotone(string id, string label, List<double> sequence, double target, double bandEps) {
            if (sequence.Count < 3 || !double.IsFinite(target)) {
                return Pass(id, $"{label}: too few rounds to assess monotone approach");
            }
            var signs = new List<int>();
            var insideBand = 0;
            foreach (var v in sequence) {
                var dev = v - target;
                if (Math.Abs(dev) <= Math.Max(bandEps, 1e-9)) {
                    insideBand++;
                    continue; // settled within tolerance -- sub-tolerance jitter is not oscillation
                }
                signs.Add(Math.Sign(dev));
            }
            var signChanges = 0;
            for (var i = 1; i < signs.Count; i++) {
                if (signs[i] != signs[i - 1]) { signChanges++; }
            }
            var inside = insideBand > 0 ? $" ({insideBand} of {sequence.Count} round(s) already within the {bandEps:0.###} band, excluded)" : string.Empty;
            return signChanges <= 1
                ? Pass(id, $"{label}: {signChanges} sign change(s) outside the tolerance band (<=1 allowed){inside}")
                : Fail(id, $"{label}: {signChanges} sign changes outside the tolerance band -- oscillating rather than converging{inside}");
        }

        /// <summary>S5's "degradation signature" check: compares this scenario's round-0 fit against the same
        /// dataset's S0 round-0 fit (a healthy baseline) for a measurable worsening.</summary>
        private static (bool Present, string Detail) DetectDegradation(FitSnapshot baseline, FitSnapshot underTest) {
            if (baseline == null || underTest == null) {
                return (false, "no S0 baseline available for this dataset (S0 was not run) -- cannot assess degradation");
            }
            const double RSquaredDropThreshold = 0.02;
            const double SigmaWorsenFraction = 0.20;
            const double StarCountDropFraction = 0.20;
            var reasons = new List<string>();

            if (double.IsFinite(baseline.RSquared) && !double.IsFinite(underTest.RSquared)) {
                reasons.Add("fit became degenerate (R^2 non-finite) with donut detection off");
            } else if (double.IsFinite(baseline.RSquared) && double.IsFinite(underTest.RSquared)
                    && baseline.RSquared - underTest.RSquared > RSquaredDropThreshold) {
                reasons.Add($"R^2 dropped {baseline.RSquared:0.###} -> {underTest.RSquared:0.###}");
            }
            if (double.IsFinite(baseline.SigmaFocus) && double.IsFinite(underTest.SigmaFocus) && baseline.SigmaFocus > 1e-9
                    && (underTest.SigmaFocus - baseline.SigmaFocus) / baseline.SigmaFocus > SigmaWorsenFraction) {
                reasons.Add($"sigma_focus worsened {baseline.SigmaFocus:0.##} -> {underTest.SigmaFocus:0.##}");
            }
            if (baseline.WorstFrameStarCount > 0
                    && (baseline.WorstFrameStarCount - underTest.WorstFrameStarCount) / (double)baseline.WorstFrameStarCount > StarCountDropFraction) {
                reasons.Add($"worst-frame star count dropped {baseline.WorstFrameStarCount} -> {underTest.WorstFrameStarCount}");
            }

            return reasons.Count > 0
                ? (true, string.Join("; ", reasons))
                : (false, "no measurable degradation vs the S0 baseline (R^2, sigma_focus, worst-frame star count all held) -- " +
                    "cross-links docs/followups.md F1 (donut heuristic misses small/borderline donuts): a standing FLAG that no " +
                    "product signal recommends donut detection here either");
        }

        private static AssertionFinding Pass(string id, string detail) => new AssertionFinding { Id = id, Verdict = SynthValidationVerdict.Pass, Detail = detail };
        private static AssertionFinding Flag(string id, string detail) => new AssertionFinding { Id = id, Verdict = SynthValidationVerdict.Flag, Detail = detail };
        private static AssertionFinding Fail(string id, string detail) => new AssertionFinding { Id = id, Verdict = SynthValidationVerdict.Fail, Detail = detail };

        private static SynthValidationVerdict Worst(IEnumerable<AssertionFinding> findings) {
            var v = SynthValidationVerdict.Pass;
            foreach (var f in findings) {
                if (f.Verdict == SynthValidationVerdict.Fail) { return SynthValidationVerdict.Fail; }
                if (f.Verdict == SynthValidationVerdict.Flag) { v = SynthValidationVerdict.Flag; }
            }
            return v;
        }

        // ── step_behavioral: StepSizeRecommender's own fixed point on the analytic truth curve ─────────────────

        /// <summary>
        /// Iterates <see cref="StepSizeRecommender.Recommend"/> to its fixed point on the dataset's EXACT (noiseless)
        /// analytic hyperbola <c>HFR(x) = sqrt(HfrMin^2 + (kappa*(x-x0))^2)</c> (<see cref="DefocusModel.HfrAtFocuserPosition"/>):
        /// fit a <see cref="HyperbolicFittingAlglib"/> (Symmetric model -- the same functional form as the truth
        /// curve, so the fit recovers it essentially exactly) to <c>2*offsetSteps+1</c> points sampled at the
        /// CURRENT step, recommend a new step from that fit, and repeat until the recommendation stops changing.
        /// This is the design's "recommender's own fixed point computed on the truth curve" (§V1, assertion A3) --
        /// deliberately NOT the same number as <see cref="SynthBankDerivations.DeriveStepSize"/>'s closed-form
        /// step_theory, which additionally floors HFR_min at <see cref="SynthBankDerivations.PixelizationFloorPixels"/>
        /// (R2) and never accounts for <see cref="StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple"/>'s cap.
        /// </summary>
        /// <param name="detectableHalfWidthSteps">
        /// F18 — the analytic detectable half-width (<c>SynthBankDerivations.DeriveDetectableHalfWidth</c>), or NaN
        /// for the control arm. The truth curve carries no star counts, so the fixed point cannot observe
        /// detectability directly; instead each sampled position is given <c>NHardStars</c> if it lies within this
        /// half-width and 0 if it does not. That is exactly the discretization the runtime rule performs on a real
        /// sweep — it takes the outermost SAMPLED position still clearing the floor — so the fixed point A3
        /// compares against is produced by the same rule as the landing it scores.
        /// </param>
        private static double ComputeStepBehavioral(DefocusModel model, int offsetSteps, int seedStep, IAlglibAPI alglibAPI,
                double detectableHalfWidthSteps = double.NaN, int recoveryStepsPerSide = 0, bool sizeForExecutedSweep = false) {
            var step = Math.Max(1, seedStep);
            var visited = new HashSet<int>();
            const int MaxIterations = 50;
            for (var iter = 0; iter < MaxIterations; iter++) {
                if (!visited.Add(step)) {
                    // A cycle (should not happen for this map in practice -- capped growth is a fixed multiplicative
                    // factor toward the answer) -- return the current value rather than loop forever.
                    return step;
                }
                var points = new List<ScatterErrorPoint>(2 * offsetSteps + 1);
                for (var k = -offsetSteps; k <= offsetSteps; k++) {
                    var x = model.OptimalFocuserPosition + k * step;
                    // Floor at the pixelization limit. The recommender never sees the OPTICAL curve -- it sees the
                    // curve the detector MEASURES, and a sub-pixel PSF cannot report an HFR below roughly
                    // SynthBankDerivations.PixelizationFloorPixels however sharp the optics get. Fitting the
                    // unfloored curve on a severely oversampled dataset produces a fixed point that the real loop
                    // never approaches: D01 (HFR_min ~0.24px optical) gives step_behavioral=3 unfloored, while the
                    // actual round loop -- fitting real detected HFRs -- lands at 10, right next to step_theory's 9.
                    // Assertion A3 would then flag a perfectly healthy convergence. Flooring here makes
                    // step_behavioral the recommender's fixed point on the curve it can actually observe, which is
                    // what the design asks A3 to compare against; the residual step_behavioral-vs-step_theory gap
                    // then means what it is supposed to mean (F18), instead of restating our own R2 floor choice.
                    var hfr = Math.Max(model.HfrAtFocuserPosition(x), SynthBankDerivations.PixelizationFloorPixels);
                    points.Add(new ScatterErrorPoint(x, hfr, 0, 0));
                }
                var fit = HyperbolicFittingAlglib.Create(alglibAPI, points, useWeights: false);
                if (!fit.Solve()) {
                    return double.NaN;
                }
                SweepDetectability detectability = null;
                if (double.IsFinite(detectableHalfWidthSteps) || recoveryStepsPerSide > 0) {
                    var counts = new int[2 * offsetSteps + 1];
                    var positions = new int[2 * offsetSteps + 1];
                    for (var k = -offsetSteps; k <= offsetSteps; k++) {
                        var idx = k + offsetSteps;
                        positions[idx] = model.OptimalFocuserPosition + k * step;
                        counts[idx] = !double.IsFinite(detectableHalfWidthSteps)
                            ? SynthBankDerivations.NHardStars   // no bound measured: every position counts as usable
                            : (Math.Abs((double)k * step) <= detectableHalfWidthSteps ? SynthBankDerivations.NHardStars : 0);
                    }
                    detectability = new SweepDetectability {
                        FrameStarCounts = counts,
                        FrameFocuserPositions = positions,
                        HardFloorStarCount = SynthBankDerivations.NHardStars,
                        RecoveryStepsPerSide = recoveryStepsPerSide
                    };
                }
                var rec = StepSizeRecommender.Recommend(fit, step, focuserMaxStep: null,
                    detectability: detectability, sizeForExecutedSweep: sizeForExecutedSweep);
                if (rec.StepSize == step) {
                    return step;
                }
                step = rec.StepSize;
            }
            return step; // did not settle within the iteration budget; last value is still a reasonable estimate.
        }

        // ── misc ──────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Deterministic FNV-1a over the scenario id's chars -- NOT <c>string.GetHashCode()</c>, which is
        /// randomized per process in modern .NET and would make <c>SeedMixer.Combine</c>'s seed non-reproducible
        /// across runs (the whole point of folding the scenario id into the seed).</summary>
        private static int ScenarioIdHash(string id) {
            unchecked {
                var hash = 2166136261u;
                foreach (var ch in id) {
                    hash = (hash ^ ch) * 16777619u;
                }
                return (int)hash;
            }
        }

        private static bool IsInsideOrEqual(string candidate, string root) {
            var a = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var b = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string Sha256Hex(byte[] bytes) {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash) { sb.Append(b.ToString("x2", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        /// <summary>One flushed stdout line per call -- these runs are detached under WSL (which block-buffers
        /// stdout), so an explicit flush is the only way progress is observable while a multi-hour run is in flight.</summary>
        private static void Prog(string message) {
            Console.WriteLine(message);
            Console.Out.Flush();
        }
    }
}
