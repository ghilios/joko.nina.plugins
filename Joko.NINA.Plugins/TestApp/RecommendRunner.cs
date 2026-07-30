#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using TestApp.StarReview;
using Logger = NINA.Core.Utility.Logger;
using Rect = OpenCvSharp.Rect;

namespace TestApp {

    /// <summary>
    /// Headless driver for the label-driven gate RECOMMENDER (the "Optimize with feedback" direct analysis). Runs
    /// the same <see cref="LabelGateAnalyzer"/> + <see cref="GateRecommender"/> the in-wizard flow uses: it detects
    /// each labeled frame (with per-rejected-candidate diagnostics on), attributes every labeled wrongly-rejected
    /// star to the exact gate that killed it, then recommends the threshold changes that recover the most labels
    /// within a precision budget — so the example can be validated without launching NINA.
    ///
    /// <para>CLI: <c>TestApp recommend --runs &lt;folder&gt; [--labels &lt;dir&gt;] [--params current|optimized]
    /// [--opt-results &lt;dir&gt;] [--profile-id &lt;guid&gt;] [--out &lt;dir&gt;] [--precision-budget &lt;n&gt;]</c></para>
    /// </summary>
    internal static class RecommendRunner {

        public static void Run(string[] args) {
            try {
                RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "recommend run failed");
                Environment.ExitCode = 1;
            }
        }

        private static void RunImpl(string[] args) {
            var runsDir = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runsDir)) {
                PrintUsage();
                Environment.ExitCode = 2;
                return;
            }
            if (!Directory.Exists(runsDir)) {
                throw new DirectoryNotFoundException($"--runs folder not found: {runsDir}");
            }

            var labelsDir = DiagnosticUtil.GetArg(args, "--labels");
            if (string.IsNullOrWhiteSpace(labelsDir)) {
                labelsDir = Path.Combine(runsDir, "labels");
            }
            if (!Directory.Exists(labelsDir)) {
                throw new DirectoryNotFoundException($"--labels folder not found: {labelsDir}");
            }

            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var paramsArg = (DiagnosticUtil.GetArg(args, "--params") ?? "current").Trim().ToLowerInvariant();
            bool useOptimized;
            switch (paramsArg) {
                case "current": useOptimized = false; break;
                case "optimized": useOptimized = true; break;
                default: throw new ArgumentException($"--params: '{paramsArg}' must be 'current' or 'optimized'");
            }
            var optResultsDir = DiagnosticUtil.GetArg(args, "--opt-results");

            var budget = 2.0;
            var budgetArg = DiagnosticUtil.GetArg(args, "--precision-budget");
            if (!string.IsNullOrWhiteSpace(budgetArg) && double.TryParse(budgetArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) {
                budget = b;
            }

            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                outDir = Path.Combine(Path.GetTempPath(), "hf-recommend", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            Logger.SetLogLevel(LogLevelEnum.INFO);
            if (System.Windows.Application.Current == null) {
                new System.Windows.Application();
            }

            var sb = new System.Text.StringBuilder();
            void Line(string s = "") {
                Console.WriteLine(s);
                sb.AppendLine(s);
            }

            Line($"Runs root: {runsDir}");
            Line($"Labels dir: {labelsDir}");
            Line($"Params: {paramsArg};  precision budget (max unlabeled per recovered): {budget.ToString("G3", CultureInfo.InvariantCulture)}");

            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile;
            if (activeProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA at least once.");
            }
            Line($"Profile: {activeProfile.Name} ({activeProfile.Id})");

            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new InvalidOperationException("Could not resolve the HocusFocus plugin assembly GUID");
            }
            var accessor = new PluginOptionsAccessor(profileService, guid.Value);
            var starDetectionOptions = new StarDetectionOptions(profileService, accessor);

            var discovery = OptimizationRunDiscovery.Discover(runsDir);
            if (discovery.Runs.Count == 0) {
                throw new InvalidOperationException($"No AF runs (≥3 focuser positions) discovered under {runsDir}.");
            }

            var firstFramePath = discovery.Runs[0].Frames.FirstOrDefault()?.Path;
            var firstRunFolder = string.IsNullOrEmpty(firstFramePath) ? null : Path.GetDirectoryName(firstFramePath);
            var detectionParams = StarReviewRunner.BuildDetectionParams(starDetectionOptions, activeProfile, useOptimized, optResultsDir, firstRunFolder);
            detectionParams.CollectRejectedCandidateDiagnostics = true;
            Line($"Detection params: Sensitivity={detectionParams.Sensitivity.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"MinHFR={detectionParams.MinHFR.ToString("G4", CultureInfo.InvariantCulture)}, " +
                $"MaxDistortion={detectionParams.MaxDistortion.ToString("G4", CultureInfo.InvariantCulture)}, " +
                $"StructureLayers={detectionParams.StructureLayers}, MinBBox={detectionParams.MinimumStarBoundingBoxSize}");
            Line();

            var detector = new StarDetector(new AlglibAPI());
            var frames = new List<FrameDetectionForAnalysis>();

            foreach (var run in discovery.Runs) {
                var labels = StarReviewLabelStore.Load(labelsDir, run.RunId, out var loadError);
                if (loadError != null) {
                    Line($"WARNING: {loadError}");
                }
                if (labels?.Positions == null || labels.Positions.Count == 0) {
                    Line($"(run '{run.RunId}': no labels matched — skipping)");
                    continue;
                }

                var framesByPos = run.Frames
                    .GroupBy(f => f.FocuserPosition)
                    .ToDictionary(g => g.Key, g => g.First().Path);

                foreach (var pos in labels.Positions.OrderBy(p => p.FocuserPosition)) {
                    var recall = (pos.Missed ?? new List<StarReviewLabelBox>())
                        .Concat(pos.WronglyRejected ?? new List<StarReviewLabelBox>()).ToList();
                    var shouldReject = pos.ShouldReject ?? new List<StarReviewLabelBox>();
                    if (recall.Count == 0 && shouldReject.Count == 0) {
                        continue;
                    }
                    if (!framesByPos.TryGetValue(pos.FocuserPosition, out var framePath)) {
                        Line($"WARNING: run '{run.RunId}' focuser {pos.FocuserPosition} has no frame; skipping its labels.");
                        continue;
                    }

                    // Detect on the IRenderedImage so the gate analysis sees the SAME image the live app detects on
                    // (CFA hotpixel filter + debayer happen inside Detect at these params). Detect(IRenderedImage)
                    // builds its own source Mat, so no clone is needed.
                    List<Star> accepted;
                    IReadOnlyList<RejectedCandidateRecord> rejected;
                    {
                        var rendered = DiagnosticUtil.LoadRenderedImage(framePath, profileService).GetAwaiter().GetResult();
                        var result = detector.Detect(rendered, detectionParams, null, CancellationToken.None).GetAwaiter().GetResult();
                        accepted = result.DetectedStars ?? new List<Star>();
                        rejected = result.RejectedCandidates ?? new List<RejectedCandidateRecord>();
                    }

                    frames.Add(new FrameDetectionForAnalysis {
                        FocuserPosition = pos.FocuserPosition,
                        AcceptedBounds = accepted.Select(s => s.StarBoundingBox).ToList(),
                        AcceptedCenters = accepted.Select(s => ((double)s.Center.X, (double)s.Center.Y)).ToList(),
                        Rejected = rejected,
                        RecallBoxes = recall.Select(ToRectD).ToList(),
                        ShouldRejectBoxes = shouldReject.Select(ToRectD).ToList()
                    });
                }
            }

            if (frames.Count == 0) {
                Line("No labeled frames could be detected — nothing to recommend.");
                File.WriteAllText(Path.Combine(outDir, "recommend.txt"), sb.ToString());
                return;
            }

            var analysis = LabelGateAnalyzer.Analyze(frames);
            var config = new RecommenderConfig { MaxUnlabeledPerRecovered = budget };
            var rec = GateRecommender.Recommend(analysis, detectionParams, config);

            // ---- Report ----
            Line("================ LABEL → GATE ATTRIBUTION ================");
            Line($"Labeled frames: {frames.Count};  recall targets: {analysis.TotalRecallTargets} " +
                $"(already accepted: {analysis.AlreadyRecovered}, NO-CANDIDATE: {analysis.NoCandidateCount})");
            foreach (var kv in analysis.RecallTargetCountByGate.OrderByDescending(kv => kv.Value)) {
                Line($"    {kv.Value,5}  {kv.Key}");
            }
            if (analysis.ShouldReject.Count > 0) {
                Line($"    should-reject labels: {analysis.ShouldReject.Count} (currently violated: {analysis.ShouldReject.Count(s => s.CurrentlyViolated)})");
            }
            Line();

            Line("================ RECOMMENDED SETTINGS ================");
            foreach (var row in rec.Rows) {
                Line($"    {row}");
            }
            Line();
            Line($"Total recovered: {rec.TotalRecovered}/{rec.TotalRecallTargets};  " +
                $"estimated weighted precision cost: {rec.EstimatedWeightedPrecisionCost.ToString("G3", CultureInfo.InvariantCulture)}");
            if (rec.RecommendDefocusAwareGates) {
                Line("    → recommends ENABLING Defocus-Aware Gates (distortion/centering relaxation).");
            }
            if (rec.RecommendStructureRecovery) {
                Line("    → recommends ENABLING Defocus-Aware Structure Detection (NO-CANDIDATE donuts).");
            }
            Line();

            Line("================ PARAMS (current → recommended) ================");
            void P(string name, double cur, double next, string fmt = "G4") {
                var marker = Math.Abs(cur - next) > 1e-9 ? " *" : "";
                Line($"    {name,-26} {cur.ToString(fmt, CultureInfo.InvariantCulture),12} → {next.ToString(fmt, CultureInfo.InvariantCulture),-12}{marker}");
            }
            P("BrightnessSensitivity", detectionParams.Sensitivity, rec.Recommended.Sensitivity);
            P("MinHFR", detectionParams.MinHFR, rec.Recommended.MinHFR);
            P("MaxDistortion", detectionParams.MaxDistortion, rec.Recommended.MaxDistortion);
            P("StarPeakResponse", detectionParams.PeakResponse, rec.Recommended.PeakResponse);
            P("StarCenterTolerance", detectionParams.StarCenterTolerance, rec.Recommended.StarCenterTolerance);
            P("MinStarBoundingBoxSize", detectionParams.MinimumStarBoundingBoxSize, rec.Recommended.MinimumStarBoundingBoxSize, "G3");
            Line($"    {"DefocusAwareGates",-26} {detectionParams.DefocusAwareDistortion,12} → {rec.Recommended.DefocusAwareDistortion,-12}");

            var outFile = Path.Combine(outDir, "recommend.txt");
            File.WriteAllText(outFile, sb.ToString());
            Console.WriteLine();
            Console.WriteLine($"Wrote {outFile}");
        }

        private static RectD ToRectD(StarReviewLabelBox b) => new RectD(b.X, b.Y, b.W ?? 0.0, b.H ?? 0.0);

        private static void PrintUsage() {
            Console.Error.WriteLine("Usage: TestApp recommend --runs <folder> [--labels <dir>] [--params current|optimized] [--opt-results <dir>] [--profile-id <guid>] [--out <dir>] [--precision-budget <n>]");
            Console.Error.WriteLine("  Attributes each labeled wrongly-rejected star to the gate that killed it, then recommends the");
            Console.Error.WriteLine("  threshold changes that recover the most labels within a precision budget (default 2.0 unlabeled/recovered).");
        }
    }
}
