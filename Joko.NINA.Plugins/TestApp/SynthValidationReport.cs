#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TestApp.SynthBank {

    /// <summary>
    /// Tri-state assertion verdict (design "Assertions A1-A7"): only <see cref="Fail"/> flips the process exit
    /// code. <see cref="Flag"/> is a recorded-but-non-fatal finding (e.g. an F18-attributable step_behavioral vs
    /// step_theory divergence, or an at-floor exposure miss per the "F8 discipline" note).
    /// </summary>
    public enum SynthValidationVerdict { Pass, Flag, Fail }

    /// <summary>One A1-A7 (or scenario-specific) assertion instance, attached either to a round or to a scenario's
    /// terminal block.</summary>
    public sealed class AssertionFinding {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("verdict")] public SynthValidationVerdict Verdict { get; set; }
        [JsonProperty("detail")] public string Detail { get; set; }
    }

    /// <summary>The bootstrap parameters a round's sweep was actually rendered with (design step 1: <c>GenerateSweep</c>).</summary>
    public sealed class BootstrapSnapshot {
        [JsonProperty("centerPosition")] public int CenterPosition { get; set; }
        [JsonProperty("stepSize")] public int StepSize { get; set; }
        [JsonProperty("offsetSteps")] public int OffsetSteps { get; set; }
        [JsonProperty("exposureSeconds")] public double ExposureSeconds { get; set; }
        [JsonProperty("afBinning")] public int AfBinning { get; set; }
        [JsonProperty("detectionBinning")] public int DetectionBinning { get; set; }
        [JsonProperty("donutDetection")] public bool DonutDetection { get; set; }
        [JsonProperty("seed")] public int Seed { get; set; }
        [JsonProperty("inferredStepSize")] public int InferredStepSize { get; set; }
        [JsonProperty("capturedExposureSeconds")] public double CapturedExposureSeconds { get; set; }
        [JsonProperty("pixelScale")] public double PixelScale { get; set; }
        [JsonProperty("pixelScaleSource")] public string PixelScaleSource { get; set; }
    }

    /// <summary>The winning fit (design step 3: <c>StarDetectionOptimizer.OptimizeAsync</c> + a final <c>EvaluateAndFitAsync</c>).</summary>
    public sealed class FitSnapshot {
        [JsonProperty("rSquared")] public double RSquared { get; set; } = double.NaN;
        [JsonProperty("reducedChiSquared")] public double ReducedChiSquared { get; set; } = double.NaN;
        [JsonProperty("sigmaFocus")] public double SigmaFocus { get; set; } = double.NaN;
        [JsonProperty("vertexX")] public double VertexX { get; set; } = double.NaN;
        [JsonProperty("vertexY")] public double VertexY { get; set; } = double.NaN;
        [JsonProperty("bestJ")] public double BestJ { get; set; } = double.NaN;
        [JsonProperty("worstFrameStarCount")] public int WorstFrameStarCount { get; set; }
        [JsonProperty("landedSensitivity")] public double LandedSensitivity { get; set; } = double.NaN;
    }

    /// <summary>design step 4a: <see cref="NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.StepSizeRecommender"/>.</summary>
    public sealed class StepRecommendationSnapshot {
        [JsonProperty("stepSize")] public int StepSize { get; set; }
        [JsonProperty("offsetSteps")] public int OffsetSteps { get; set; }
        [JsonProperty("halfWidth")] public double HalfWidth { get; set; } = double.NaN;
        [JsonProperty("wasCapped")] public bool WasCapped { get; set; }
    }

    /// <summary>design step 4b: <see cref="NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.ExposureRecommender"/>,
    /// gated exactly as <c>OptimizationDiagnosticRunner.BuildAggregateRow</c> gates it (only computed when
    /// <see cref="SensitivityAtFloor"/>).</summary>
    public sealed class ExposureRecommendationSnapshot {
        [JsonProperty("sensitivityAtFloor")] public bool SensitivityAtFloor { get; set; }
        [JsonProperty("computed")] public bool Computed { get; set; } // false when not gated in this round (SensitivityAtFloor == false)
        [JsonProperty("hasRecommendation")] public bool HasRecommendation { get; set; }
        [JsonProperty("currentSeconds")] public double CurrentSeconds { get; set; } = double.NaN;
        [JsonProperty("recommendedSeconds")] public double RecommendedSeconds { get; set; } = double.NaN;
        [JsonProperty("measuredSnr")] public double MeasuredSnr { get; set; } = double.NaN;
        [JsonProperty("increasesExposure")] public bool IncreasesExposure { get; set; }
        [JsonProperty("wasCapped")] public bool WasCapped { get; set; }
        [JsonProperty("cappedByAbsoluteLimit")] public bool CappedByAbsoluteLimit { get; set; }
        [JsonProperty("exposureIsNotTheLimit")] public bool ExposureIsNotTheLimit { get; set; }
        [JsonProperty("starCountIsTheLimit")] public bool StarCountIsTheLimit { get; set; }
        [JsonProperty("starFieldIsExhausted")] public bool StarFieldIsExhausted { get; set; }
    }

    /// <summary>design step 4c: <see cref="NINA.Joko.Plugins.HocusFocus.Utility.DetectionBinningResolver.RecommendFromHfr"/>,
    /// gated on the fit R² &gt;= <c>OptimizationSummary.MinRSquaredForBinningRecommendation</c> — a gate
    /// that lives in the wizard, not the resolver, so the driver applies it itself (design instruction, said here
    /// per that instruction).</summary>
    public sealed class BinningRecommendationSnapshot {
        [JsonProperty("hasMeasurement")] public bool HasMeasurement { get; set; } // R^2 gate passed
        [JsonProperty("vertexHfr")] public double VertexHfr { get; set; } = double.NaN;
        [JsonProperty("recommendedFactor")] public int? RecommendedFactor { get; set; }
    }

    /// <summary>What the update policy actually did with this round's recommendations, and the current
    /// (possibly unchanged) state each carries into the next round.</summary>
    public sealed class AppliedSnapshot {
        [JsonProperty("binningApplied")] public bool BinningApplied { get; set; }
        [JsonProperty("stepApplied")] public bool StepApplied { get; set; }
        [JsonProperty("exposureApplied")] public bool ExposureApplied { get; set; }
        [JsonProperty("recentered")] public bool Recentered { get; set; }
        [JsonProperty("newCenterPosition")] public int NewCenterPosition { get; set; }
        [JsonProperty("reasons")] public List<string> Reasons { get; set; } = new List<string>();

        [JsonIgnore] public bool AppliedAnything => BinningApplied || StepApplied || ExposureApplied;
    }

    /// <summary>One round of the convergence loop: what was rendered, what fit came back, every recommendation
    /// (with its gate state) and what the update policy did with it, plus the per-round assertions (A4-A7).</summary>
    public sealed class RoundValidationReport {
        [JsonProperty("roundIndex")] public int RoundIndex { get; set; }
        [JsonProperty("bootstrap")] public BootstrapSnapshot Bootstrap { get; set; }
        [JsonProperty("kernelCapGuardExceeded")] public bool KernelCapGuardExceeded { get; set; }
        [JsonProperty("fit")] public FitSnapshot Fit { get; set; }
        [JsonProperty("stepRecommendation")] public StepRecommendationSnapshot StepRecommendation { get; set; }
        [JsonProperty("exposureRecommendation")] public ExposureRecommendationSnapshot ExposureRecommendation { get; set; }
        [JsonProperty("binningRecommendation")] public BinningRecommendationSnapshot BinningRecommendation { get; set; }
        [JsonProperty("applied")] public AppliedSnapshot Applied { get; set; }
        [JsonProperty("assertions")] public List<AssertionFinding> Assertions { get; set; } = new List<AssertionFinding>();
    }

    /// <summary>The terminal block for one (dataset, scenario): whether the loop converged, the scenario-level
    /// (cross-round) assertions A1-A3, the deltas versus the dataset's expected-optimal bootstrap, and the
    /// rolled-up flags/verdict.</summary>
    public sealed class ScenarioTerminal {
        [JsonProperty("converged")] public bool Converged { get; set; }
        [JsonProperty("roundsUsed")] public int RoundsUsed { get; set; }
        [JsonProperty("stoppedReason")] public string StoppedReason { get; set; }

        // step_behavioral vs step_theory (design A3's crux — see docs/followups.md F18).
        [JsonProperty("stepTheory")] public double StepTheory { get; set; } = double.NaN;
        [JsonProperty("stepBehavioral")] public double StepBehavioral { get; set; } = double.NaN;
        [JsonProperty("stepBehavioralVsTheoryDeltaFraction")] public double StepBehavioralVsTheoryDeltaFraction { get; set; } = double.NaN;

        [JsonProperty("finalStepSize")] public int FinalStepSize { get; set; }
        [JsonProperty("finalExposureSeconds")] public double FinalExposureSeconds { get; set; }
        [JsonProperty("finalDetectionBinning")] public int FinalDetectionBinning { get; set; }
        [JsonProperty("finalCenterPosition")] public int FinalCenterPosition { get; set; }

        [JsonProperty("expectedStepSize")] public int ExpectedStepSize { get; set; }
        [JsonProperty("expectedExposureSeconds")] public double ExpectedExposureSeconds { get; set; }
        [JsonProperty("expectedDetectionBinning")] public int ExpectedDetectionBinning { get; set; }

        [JsonProperty("deltaStepVsExpected")] public int DeltaStepVsExpected { get; set; }
        [JsonProperty("deltaExposureVsExpectedFraction")] public double DeltaExposureVsExpectedFraction { get; set; } = double.NaN;
        [JsonProperty("deltaBinningVsExpected")] public int DeltaBinningVsExpected { get; set; }

        // S5 only ("NOT convergence -- PASS = the degradation signature appears").
        [JsonProperty("degradationSignaturePresent")] public bool? DegradationSignaturePresent { get; set; }
        [JsonProperty("degradationDetail")] public string DegradationDetail { get; set; }

        [JsonProperty("assertions")] public List<AssertionFinding> Assertions { get; set; } = new List<AssertionFinding>();
        [JsonProperty("overallVerdict")] public SynthValidationVerdict OverallVerdict { get; set; }
    }

    /// <summary>One (dataset, scenario) pair's full round history + terminal verdict.</summary>
    public sealed class ScenarioValidationReport {
        [JsonProperty("scenarioId")] public string ScenarioId { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("applicable")] public bool Applicable { get; set; }
        [JsonProperty("skipReason")] public string SkipReason { get; set; }
        [JsonProperty("rounds")] public List<RoundValidationReport> Rounds { get; set; } = new List<RoundValidationReport>();
        [JsonProperty("terminal")] public ScenarioTerminal Terminal { get; set; }
    }

    /// <summary>One dataset's scenario results.</summary>
    public sealed class DatasetValidationReport {
        [JsonProperty("datasetId")] public string DatasetId { get; set; }
        [JsonProperty("scenarios")] public List<ScenarioValidationReport> Scenarios { get; set; } = new List<ScenarioValidationReport>();
    }

    /// <summary>
    /// Root of the <c>synth-validate</c> report (schema <c>synth-validate/1</c>, design §"Report"). Written as both
    /// JSON (scripted consumption) and Markdown (human triage) by <see cref="SynthValidationReportWriter"/>.
    /// </summary>
    public sealed class SynthValidationReport {
        [JsonProperty("schemaVersion")] public string SchemaVersion { get; set; } = "synth-validate/1";
        [JsonProperty("generatedAtUtc")] public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        [JsonProperty("specPath")] public string SpecPath { get; set; }
        [JsonProperty("specSha256")] public string SpecSha256 { get; set; }
        [JsonProperty("outDir")] public string OutDir { get; set; }
        [JsonProperty("maxRounds")] public int MaxRounds { get; set; }
        [JsonProperty("maxEvals")] public int? MaxEvals { get; set; }
        [JsonProperty("datasets")] public List<DatasetValidationReport> Datasets { get; set; } = new List<DatasetValidationReport>();
    }

    /// <summary>Writes <see cref="SynthValidationReport"/> as JSON and as a scannable Markdown summary.</summary>
    public static class SynthValidationReportWriter {

        public static void WriteJson(string path, SynthValidationReport report) {
            File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
        }

        public static void WriteMarkdown(string path, SynthValidationReport report) {
            var sb = new StringBuilder();
            sb.AppendLine("# Synthetic AF bank convergence validation (`synth-validate/1`)");
            sb.AppendLine();
            sb.AppendLine($"Generated: {report.GeneratedAtUtc:u}");
            sb.AppendLine($"Spec: `{report.SpecPath}` (sha256 `{report.SpecSha256}`)");
            sb.AppendLine($"Out: `{report.OutDir}`");
            sb.AppendLine($"maxRounds={report.MaxRounds} maxEvals={(report.MaxEvals?.ToString(CultureInfo.InvariantCulture) ?? "default")}");
            sb.AppendLine();

            var allTerminals = report.Datasets.SelectMany(d => d.Scenarios.Select(s => (d.DatasetId, s))).ToList();
            var failCount = allTerminals.Count(t => t.s.Terminal?.OverallVerdict == SynthValidationVerdict.Fail);
            var flagCount = allTerminals.Count(t => t.s.Terminal?.OverallVerdict == SynthValidationVerdict.Flag);
            var passCount = allTerminals.Count(t => t.s.Terminal?.OverallVerdict == SynthValidationVerdict.Pass);
            sb.AppendLine($"## Summary: {passCount} PASS, {flagCount} FLAG, {failCount} FAIL (of {allTerminals.Count} applicable dataset x scenario pairs)");
            sb.AppendLine();
            sb.AppendLine("| dataset | scenario | verdict | converged | rounds | step (final/expected) | exposure (final/expected) | binning (final/expected) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var (datasetId, s) in allTerminals) {
                if (!s.Applicable) continue;
                var t = s.Terminal;
                sb.AppendLine($"| {datasetId} | {s.ScenarioId} | {t.OverallVerdict} | {t.Converged} | {t.RoundsUsed} | " +
                    $"{t.FinalStepSize}/{t.ExpectedStepSize} | {t.FinalExposureSeconds:0.###}/{t.ExpectedExposureSeconds:0.###} | " +
                    $"{t.FinalDetectionBinning}/{t.ExpectedDetectionBinning} |");
            }
            sb.AppendLine();

            var skipped = allTerminals.Where(t => !t.s.Applicable).ToList();
            if (skipped.Count > 0) {
                sb.AppendLine("## Not applicable");
                foreach (var (datasetId, s) in skipped) {
                    sb.AppendLine($"- {datasetId} / {s.ScenarioId}: {s.SkipReason}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Flags and failures");
            foreach (var (datasetId, s) in allTerminals) {
                if (!s.Applicable || s.Terminal == null) continue;
                // Both the per-round assertions (A1/A4/A5/A6/A7, one set per round) and the scenario-terminal ones
                // (A2/A3, plus S5's degradation-signature verdict) roll up here -- a per-round FAIL must be just as
                // visible in this summary as a terminal one.
                var flagged = s.Rounds.SelectMany(r => r.Assertions.Select(a => (Round: (int?)r.RoundIndex, Assertion: a)))
                    .Concat(s.Terminal.Assertions.Select(a => (Round: (int?)null, Assertion: a)))
                    .Where(x => x.Assertion.Verdict != SynthValidationVerdict.Pass)
                    .ToList();
                if (flagged.Count == 0) continue;
                sb.AppendLine($"### {datasetId} / {s.ScenarioId}");
                foreach (var (round, a) in flagged) {
                    var where = round.HasValue ? $"round {round.Value}" : "terminal";
                    sb.AppendLine($"- **{a.Verdict}** [{a.Id}] ({where}): {a.Detail}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Per-dataset detail");
            foreach (var d in report.Datasets) {
                sb.AppendLine($"### {d.DatasetId}");
                foreach (var s in d.Scenarios) {
                    sb.AppendLine($"#### {s.ScenarioId} — {s.Description}");
                    if (!s.Applicable) {
                        sb.AppendLine($"Not applicable: {s.SkipReason}");
                        sb.AppendLine();
                        continue;
                    }
                    var t = s.Terminal;
                    sb.AppendLine($"Verdict **{t.OverallVerdict}** — converged={t.Converged}, roundsUsed={t.RoundsUsed}" +
                        (string.IsNullOrEmpty(t.StoppedReason) ? "" : $" ({t.StoppedReason})"));
                    if (double.IsFinite(t.StepBehavioral)) {
                        sb.AppendLine($"step_theory={t.StepTheory:0.##} step_behavioral={t.StepBehavioral:0.##} " +
                            $"(delta {t.StepBehavioralVsTheoryDeltaFraction:P1})");
                    }
                    if (t.DegradationSignaturePresent.HasValue) {
                        sb.AppendLine($"Degradation signature present: {t.DegradationSignaturePresent.Value} — {t.DegradationDetail}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("| round | center | step | exposure | detBin | donut | R² | σ_focus | vertex | step rec | exposure rec | binning rec | applied |");
                    sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
                    foreach (var r in s.Rounds) {
                        var stepRecTxt = r.StepRecommendation == null ? "-" :
                            $"{r.StepRecommendation.StepSize}{(r.StepRecommendation.WasCapped ? " (capped)" : "")}";
                        var expRecTxt = r.ExposureRecommendation == null || !r.ExposureRecommendation.Computed ? "n/a" :
                            (r.ExposureRecommendation.HasRecommendation
                                ? $"{r.ExposureRecommendation.CurrentSeconds:0.##}->{r.ExposureRecommendation.RecommendedSeconds:0.##}"
                                : "no-rec");
                        var binRecTxt = r.BinningRecommendation == null || !r.BinningRecommendation.HasMeasurement ? "n/a" :
                            $"{r.BinningRecommendation.RecommendedFactor}";
                        var appliedTxt = r.Applied == null || r.Applied.Reasons.Count == 0 ? "(none)" : string.Join("; ", r.Applied.Reasons);
                        sb.AppendLine($"| {r.RoundIndex} | {r.Bootstrap.CenterPosition} | {r.Bootstrap.StepSize} | " +
                            $"{r.Bootstrap.ExposureSeconds:0.###} | {r.Bootstrap.DetectionBinning} | {r.Bootstrap.DonutDetection} | " +
                            $"{r.Fit.RSquared:0.###} | {r.Fit.SigmaFocus:0.##} | {r.Fit.VertexX:0.#} | {stepRecTxt} | {expRecTxt} | {binRecTxt} | {appliedTxt} |");
                    }
                    sb.AppendLine();
                }
            }

            File.WriteAllText(path, sb.ToString());
        }
    }
}
