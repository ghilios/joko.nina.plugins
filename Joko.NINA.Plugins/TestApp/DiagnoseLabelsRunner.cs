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
using TestApp.StarReview;
using Logger = NINA.Core.Utility.Logger;
using Rect = OpenCvSharp.Rect;

namespace TestApp {

    /// <summary>
    /// Headless diagnostic: classify EACH human-labeled review box (missed / shouldReject / wronglyRejected)
    /// against a fresh detection of the same frame, to determine which detector outcome applies to it:
    ///
    /// <list type="bullet">
    /// <item><b>ACCEPTED</b> — the label box overlaps an accepted star's <c>StarBoundingBox</c> (the detector
    ///   already keeps it).</item>
    /// <item><b>REJECTED:&lt;reason&gt;</b> — the label box overlaps a rejected candidate from one of the
    ///   <c>StarDetectorMetrics.*Bounds</c> lists (TooDistorted / Degenerate / Saturated / LowSensitivity /
    ///   NotCentered / TooFlat / Contaminated); reports WHICH gate killed it.</item>
    /// <item><b>NO CANDIDATE (structure gap)</b> — the label box overlaps no accepted star AND no rejected
    ///   candidate, so the detector never even formed a candidate there: a true structure-detection gap, only
    ///   fixable by detector-algorithm work (not by loosening a gate).</item>
    /// </list>
    ///
    /// <para>This is purely a READ-ONLY diagnostic: it reuses <see cref="StarReviewRunner"/>'s param construction
    /// (<c>--params current|optimized</c> + <c>--opt-results</c>/run-folder resolution) and rejected-candidate
    /// extraction so it sees exactly what the review overlay / optimizer see, never mutating the profile or
    /// detection logic. It tells us whether the wrongly-rejected donut stars are killed by ONE loosenable gate
    /// versus being structure gaps.</para>
    ///
    /// <para>CLI: <c>TestApp diagnose-labels --runs &lt;folder&gt; --labels &lt;dir&gt; [--params current|optimized]
    /// [--opt-results &lt;dir&gt;] [--profile-id &lt;guid&gt;] [--out &lt;dir&gt;]</c>.</para>
    /// </summary>
    internal static class DiagnoseLabelsRunner {

        // The four classification outcomes for a labeled box.
        private const string ResultAccepted = "ACCEPTED";
        private const string ResultNoCandidate = "NO CANDIDATE";

        public static void Run(string[] args) {
            try {
                RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "diagnose-labels run failed");
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

            // Default labels dir alongside the runs (mirrors `review`/`optimize`).
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

            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                outDir = Path.Combine(Path.GetTempPath(), "hf-diagnose-labels", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);

            Logger.SetLogLevel(LogLevelEnum.TRACE);

            // The real ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a WPF
            // Application must exist before the profile loads (mirrors the review / contamination runners).
            if (System.Windows.Application.Current == null) {
                new System.Windows.Application();
            }

            var sb = new StringBuilder();
            void Line(string s = "") {
                Console.WriteLine(s);
                sb.AppendLine(s);
            }

            Line($"Runs root: {runsDir}");
            Line($"Labels dir: {labelsDir}");
            Line($"Params: {paramsArg}");
            Line($"Out dir: {outDir}");

            // Load the user's real profile + star detection options (single source of truth for params + PixelScale).
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile;
            if (activeProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA at least once to create a profile.");
            }
            Line($"Profile: {activeProfile.Name} ({activeProfile.Id})");

            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new InvalidOperationException("Could not resolve the HocusFocus plugin assembly GUID");
            }
            var accessor = new PluginOptionsAccessor(profileService, guid.Value);
            var starDetectionOptions = new StarDetectionOptions(profileService, accessor);

            var discovered = DiscoverRuns(runsDir);
            if (discovered.Count == 0) {
                throw new InvalidOperationException(
                    $"No AF frames matched the saved-run filename pattern under {runsDir}. Expected files like " +
                    "'0_Frame1_BitDepth16_Bayered0_Focuser5000.fits' in the folder or an immediate subfolder.");
            }
            Line($"Discovered {discovered.Count} run(s):");
            foreach (var d in discovered) {
                Line($"  {d.RunId}: {d.Frames.Count} frames");
            }

            // Build the detection params (current vs optimized) via StarReviewRunner's single source of truth so
            // this diagnostic sees exactly what the review overlay / optimizer see.
            var firstFramePath = discovered[0].Frames.FirstOrDefault()?.Path;
            var firstRunFolder = string.IsNullOrEmpty(firstFramePath) ? null : Path.GetDirectoryName(firstFramePath);
            var detectionParams = StarReviewRunner.BuildDetectionParams(starDetectionOptions, activeProfile, useOptimized, optResultsDir, firstRunFolder);

            // Enable the per-rejected-candidate diagnostics so the gate that killed each labeled star — INCLUDING
            // the counter-only gates (TooLowHFR/TooSmall/HFRAnalysisFailed) that have no metrics *Bounds list and
            // therefore used to mis-classify as NO CANDIDATE — is attributable from result.RejectedCandidates.
            detectionParams.CollectRejectedCandidateDiagnostics = true;

            // Opt-in defocus-aware distortion test switch. Flips DefocusAwareDistortion ON on the freshly built
            // params (least-invasive: does not touch the profile). Optional --defocus-size-ref / --defocus-min-factor
            // override the two tuning knobs (otherwise the StarDetectorParams class defaults 20.0 px / 0.25 apply).
            // This lets a single `--params current` baseline run be re-run with the gate ON to measure the delta.
            if (DiagnosticUtil.HasFlag(args, "--defocus-distortion")) {
                detectionParams.DefocusAwareDistortion = true;
                var sizeRefArg = DiagnosticUtil.GetArg(args, "--defocus-size-ref");
                if (!string.IsNullOrWhiteSpace(sizeRefArg) && double.TryParse(sizeRefArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizeRef)) {
                    detectionParams.DefocusDistortionSizeReference = sizeRef;
                }
                var minFactorArg = DiagnosticUtil.GetArg(args, "--defocus-min-factor");
                if (!string.IsNullOrWhiteSpace(minFactorArg) && double.TryParse(minFactorArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var minFactor)) {
                    detectionParams.DefocusDistortionMinFactor = minFactor;
                }
            }

            // Opt-in defocus-aware CENTERING test switch (companion to --defocus-distortion). Flips
            // DefocusAwareCentering ON on the freshly built params; optional --defocus-center-factor overrides the
            // max centering-tolerance multiplier (otherwise the StarDetectorParams class default 2.0 applies). The
            // size reference is SHARED with the distortion gate (DefocusDistortionSizeReference), so --defocus-size-ref
            // affects both gates. Enable both flags to recover ring-unstable donut centroids the distortion gate admits.
            if (DiagnosticUtil.HasFlag(args, "--defocus-centering")) {
                detectionParams.DefocusAwareCentering = true;
                var sizeRefArg = DiagnosticUtil.GetArg(args, "--defocus-size-ref");
                if (!string.IsNullOrWhiteSpace(sizeRefArg) && double.TryParse(sizeRefArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var sizeRef)) {
                    detectionParams.DefocusDistortionSizeReference = sizeRef;
                }
                var centerFactorArg = DiagnosticUtil.GetArg(args, "--defocus-center-factor");
                if (!string.IsNullOrWhiteSpace(centerFactorArg) && double.TryParse(centerFactorArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var centerFactor)) {
                    detectionParams.DefocusCenteringToleranceFactor = centerFactor;
                }
            }

            // Opt-in defocus-aware STRUCTURE test switch (the EARLY-stage candidate-formation relaxation, distinct
            // from the two LATE gates above). Flips DefocusAwareStructure ON and sets StructureLayerBoost so the
            // wavelet residual that removes large-scale structure is computed at StructureLayers + boost EXTRA
            // layers (coarser), letting large/donut defocused stars survive and form candidates instead of being
            // erased entirely (the NO-CANDIDATE structure gap). --structure-boost defaults to 2 when the flag is
            // present (0 ⇒ no change even with the flag on, useful as an explicit bit-identical control).
            if (DiagnosticUtil.HasFlag(args, "--defocus-structure")) {
                detectionParams.DefocusAwareStructure = true;
                var boost = 2;
                var boostArg = DiagnosticUtil.GetArg(args, "--structure-boost");
                if (!string.IsNullOrWhiteSpace(boostArg) && int.TryParse(boostArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) && b >= 0) {
                    boost = b;
                }
                detectionParams.StructureLayerBoost = boost;
            }

            // Opt-in ROUNDNESS-rescue test switch (companion to --defocus-distortion). Flips DefocusRoundnessAdmission
            // ON so a large low-fill candidate the (relaxed) distortion gate would reject is admitted iff it is round
            // (a complete donut ring). --defocus-max-elongation overrides the elongation cap (default 2.0).
            if (DiagnosticUtil.HasFlag(args, "--defocus-roundness")) {
                detectionParams.DefocusRoundnessAdmission = true;
                var elongArg = DiagnosticUtil.GetArg(args, "--defocus-max-elongation");
                if (!string.IsNullOrWhiteSpace(elongArg) && double.TryParse(elongArg, NumberStyles.Float, CultureInfo.InvariantCulture, out var elong)) {
                    detectionParams.DefocusMaxElongation = elong;
                }
            }

            // Optional StructureLayers override (diagnostic only): pins the nominal StructureLayers independent of
            // the profile so the structure-boost mechanism can be A/B-tested against a controlled baseline (e.g.
            // the factory default 4 where the donuts are NO-CANDIDATE) rather than against a drifted/optimized
            // profile. Read-only on the profile, like the defocus switches.
            var structureLayersArg = DiagnosticUtil.GetArg(args, "--structure-layers");
            if (!string.IsNullOrWhiteSpace(structureLayersArg) && int.TryParse(structureLayersArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sl) && sl >= 1) {
                detectionParams.StructureLayers = sl;
            }

            Line($"Detection params: Sensitivity={detectionParams.Sensitivity.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"StructureLayers={detectionParams.StructureLayers}, MaxDistortion={detectionParams.MaxDistortion.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"PixelScale={detectionParams.PixelScale.ToString("G6", CultureInfo.InvariantCulture)}, RejectContaminated={detectionParams.RejectContaminatedStars}");
            Line($"DefocusAwareDistortion={detectionParams.DefocusAwareDistortion}" +
                (detectionParams.DefocusAwareDistortion
                    ? $" (SizeReference={detectionParams.DefocusDistortionSizeReference.ToString("G6", CultureInfo.InvariantCulture)} px, " +
                      $"MinFactor={detectionParams.DefocusDistortionMinFactor.ToString("G6", CultureInfo.InvariantCulture)})"
                    : ""));
            Line($"DefocusAwareCentering={detectionParams.DefocusAwareCentering}" +
                (detectionParams.DefocusAwareCentering
                    ? $" (SizeReference={detectionParams.DefocusDistortionSizeReference.ToString("G6", CultureInfo.InvariantCulture)} px, " +
                      $"ToleranceFactor={detectionParams.DefocusCenteringToleranceFactor.ToString("G6", CultureInfo.InvariantCulture)})"
                    : ""));
            Line($"DefocusAwareStructure={detectionParams.DefocusAwareStructure}" +
                (detectionParams.DefocusAwareStructure ? $" (StructureLayerBoost={detectionParams.StructureLayerBoost}, effective layers={detectionParams.StructureLayers + detectionParams.StructureLayerBoost})" : ""));
            Line();

            var detector = new StarDetector(new AlglibAPI());

            // Tally: category -> (result-or-reason -> count). Result key is "ACCEPTED", "NO CANDIDATE", or
            // "REJECTED:<reason>".
            var tally = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            void Tally(string category, string result) {
                if (!tally.TryGetValue(category, out var byResult)) {
                    byResult = new Dictionary<string, int>(StringComparer.Ordinal);
                    tally[category] = byResult;
                }
                byResult[result] = byResult.TryGetValue(result, out var c) ? c + 1 : 1;
            }

            int totalBoxes = 0;
            foreach (var d in discovered) {
                // Load labels for this run (reuse the same loader/model the review tool writes + optimizer reads).
                var labels = StarReviewLabelStore.Load(labelsDir, d.RunId, out var loadError);
                if (loadError != null) {
                    Line($"WARNING: {loadError}");
                    Logger.Warning(loadError);
                }
                if (labels?.Positions == null || labels.Positions.Count == 0) {
                    continue;
                }

                // Index frames by focuser position so we only detect/process positions that appear in the labels.
                var framesByPos = d.Frames
                    .GroupBy(f => f.FocuserPosition)
                    .ToDictionary(g => g.Key, g => g.First().Path);

                foreach (var pos in labels.Positions.OrderBy(p => p.FocuserPosition)) {
                    var missed = pos.Missed ?? new List<StarReviewLabelBox>();
                    var shouldReject = pos.ShouldReject ?? new List<StarReviewLabelBox>();
                    var wronglyRejected = pos.WronglyRejected ?? new List<StarReviewLabelBox>();
                    if (missed.Count == 0 && shouldReject.Count == 0 && wronglyRejected.Count == 0) {
                        continue;
                    }

                    if (!framesByPos.TryGetValue(pos.FocuserPosition, out var framePath)) {
                        Line($"WARNING: run '{d.RunId}' label focuser {pos.FocuserPosition} has no matching frame; skipping " +
                            $"{missed.Count + shouldReject.Count + wronglyRejected.Count} box(es).");
                        Logger.Warning($"diagnose-labels: no frame for run '{d.RunId}' focuser {pos.FocuserPosition}");
                        continue;
                    }

                    // Fresh detection of this frame (Detect mutates its input; clone the loaded Mat). Uses the rich
                    // per-rejected-candidate records (gate + measured value), so the counter-only gates are attributed.
                    List<Star> accepted;
                    IReadOnlyList<RejectedCandidateRecord> rejectedRecords;
                    using (var mat = DiagnosticUtil.LoadFloatMat(framePath, profileService).GetAwaiter().GetResult())
                    using (var clone = mat.Clone()) {
                        var result = detector.Detect(clone, detectionParams, null, CancellationToken.None).GetAwaiter().GetResult();
                        accepted = result.DetectedStars ?? new List<Star>();
                        rejectedRecords = result.RejectedCandidates ?? new List<RejectedCandidateRecord>();
                    }
                    var acceptedBounds = accepted.Select(s => s.StarBoundingBox).ToList();

                    Line($"=== run '{d.RunId}' @ focuser {pos.FocuserPosition} ({Path.GetFileName(framePath)}) ===");
                    Line($"    detection: {accepted.Count} accepted, {rejectedRecords.Count} rejected candidate(s)");
                    Line($"    {"category",-16} {"box(x,y,w,h)",-28} {"result",-26} matched candidate");

                    void Classify(string category, List<StarReviewLabelBox> boxes) {
                        foreach (var box in boxes) {
                            totalBoxes++;
                            var (result, matchDesc) = ClassifyLabelBox(box, acceptedBounds, rejectedRecords);
                            Tally(category, result);
                            Line($"    {category,-16} {FormatBox(box),-28} {result,-26} {matchDesc}");
                        }
                    }

                    Classify("missed", missed);
                    Classify("shouldReject", shouldReject);
                    Classify("wronglyRejected", wronglyRejected);
                    Line();
                }
            }

            // ---- Summary tally ----
            Line("================ SUMMARY TALLY ================");
            Line($"Total labeled boxes classified: {totalBoxes}");
            if (tally.Count == 0) {
                Line("(no labeled boxes found in the labels dir for any discovered run)");
            }
            foreach (var category in new[] { "missed", "shouldReject", "wronglyRejected" }) {
                if (!tally.TryGetValue(category, out var byResult)) {
                    continue;
                }
                var total = byResult.Values.Sum();
                Line($"{category}: {total} box(es)");
                foreach (var kv in byResult.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)) {
                    Line($"    {kv.Value,4}  {kv.Key}");
                }
            }
            // Compact one-line-per-category form (matches the task's example phrasing).
            Line();
            foreach (var category in new[] { "missed", "shouldReject", "wronglyRejected" }) {
                if (!tally.TryGetValue(category, out var byResult)) {
                    continue;
                }
                var parts = byResult.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Value} {kv.Key}");
                Line($"{category}: {string.Join(", ", parts)}");
            }

            var outFile = Path.Combine(outDir, "diagnose_labels.txt");
            File.WriteAllText(outFile, sb.ToString());
            Console.WriteLine();
            Console.WriteLine($"Wrote {outFile}");
        }

        /// <summary>
        /// Classifies one label box via the shared <see cref="BoxMatcher"/> (so this harness, the in-wizard
        /// analyzer, and the recommender agree by construction), then formats the human-readable result line. The
        /// rejected pool is the rich per-candidate records, so the gate AND the measured value it failed are
        /// reported, and the counter-only gates (TooLowHFR/TooSmall/HFRAnalysisFailed) are no longer mis-reported
        /// as NO CANDIDATE.
        /// </summary>
        private static (string result, string matchDesc) ClassifyLabelBox(
            StarReviewLabelBox box, IReadOnlyList<Rect> acceptedBounds, IReadOnlyList<RejectedCandidateRecord> rejected) {
            var m = BoxMatcher.Classify(ToRectD(box), acceptedBounds, rejected);
            switch (m.Kind) {
                case BoxClassification.Accepted: {
                        var tieNote = m.Rejected != null && m.RejectedIoU > 0.0
                            ? $" [also overlaps REJECTED:{m.Gate} IoU={m.RejectedIoU:F3}]" : "";
                        return (ResultAccepted, $"accepted box {FormatRect(m.AcceptedBounds)} IoU={m.AcceptedIoU:F3}{tieNote}");
                    }
                case BoxClassification.Rejected: {
                        var measured = double.IsNaN(m.Rejected.MeasuredValue)
                            ? ""
                            : $" measured={m.Rejected.MeasuredValue.ToString("G4", CultureInfo.InvariantCulture)} thr={m.Rejected.ThresholdValue.ToString("G4", CultureInfo.InvariantCulture)}";
                        var acceptedNote = m.AcceptedIoU > 0.0 ? $" [also overlaps ACCEPTED IoU={m.AcceptedIoU:F3}]" : "";
                        var ambiguity = m.RejectedTieCount > 1 ? $" [AMBIGUOUS: {m.RejectedTieCount} reasons tie at IoU={m.RejectedIoU:F3}]" : "";
                        return ($"REJECTED:{m.Gate}", $"rejected box {FormatRect(m.Rejected.Bounds)} IoU={m.RejectedIoU:F3}{measured}{acceptedNote}{ambiguity}");
                    }
                default:
                    return (ResultNoCandidate, "(no accepted star, no rejected candidate)");
            }
        }

        // ---- Geometry helpers ------------------------------------------------------------------------------
        // RectD + IoU + the BEST-overlap classification now live in the shared plugin BoxMatcher.

        private static RectD ToRectD(StarReviewLabelBox b) => new RectD(b.X, b.Y, b.W ?? 0.0, b.H ?? 0.0);

        private static string FormatBox(StarReviewLabelBox b) =>
            $"({b.X.ToString("F0", CultureInfo.InvariantCulture)},{b.Y.ToString("F0", CultureInfo.InvariantCulture)}," +
            $"{(b.W ?? 0).ToString("F0", CultureInfo.InvariantCulture)},{(b.H ?? 0).ToString("F0", CultureInfo.InvariantCulture)})";

        private static string FormatRect(Rect r) => $"({r.X},{r.Y},{r.Width},{r.Height})";

        private static void PrintUsage() {
            Console.Error.WriteLine("Usage: TestApp diagnose-labels --runs <folder> --labels <dir> [--params current|optimized] [--opt-results <dir>] [--profile-id <guid>] [--out <dir>]");
            Console.Error.WriteLine("  Classifies each human-labeled review box against a fresh detection of the same frame:");
            Console.Error.WriteLine("    ACCEPTED          - overlaps an accepted star's bounding box");
            Console.Error.WriteLine("    REJECTED:<reason> - overlaps a rejected candidate (TooSmall/OnBorder/TooDistorted/Degenerate/LowSensitivity/NotCentered/TooFlat/HFRAnalysisFailed/TooLowHFR/Contaminated), with the measured value vs threshold");
            Console.Error.WriteLine("    NO CANDIDATE      - overlaps neither (a structure-detection gap; only fixable by detector-algorithm work)");
            Console.Error.WriteLine("  --runs        (required) folder of saved AF runs (same discovery as `review`/`optimize`).");
            Console.Error.WriteLine("  --labels      (default <runs>/labels) folder of label JSON files written by `review`.");
            Console.Error.WriteLine("  --params      (default current) which detector params to use: 'current' (profile seed) or 'optimized' (snapshot).");
            Console.Error.WriteLine("  --opt-results (optional) folder holding optimized_settings.json (the optimizer's --out subdir).");
            Console.Error.WriteLine("  --profile-id  (default active) NINA profile id to load.");
            Console.Error.WriteLine("  --out         (default a temp dir) where diagnose_labels.txt is written.");
            Console.Error.WriteLine("  --defocus-distortion        (opt-in test switch) flips DefocusAwareDistortion ON on the built params.");
            Console.Error.WriteLine("  --defocus-centering         (opt-in test switch) flips DefocusAwareCentering ON (relaxes NotCentered for large/defocused candidates).");
            Console.Error.WriteLine("  --defocus-size-ref <px>     (with either defocus switch) override the strict-below size reference, shared by both gates (default 30).");
            Console.Error.WriteLine("  --defocus-min-factor <0..1> (with --defocus-distortion) override the floor multiplier on MaxDistortion (default 0.25).");
            Console.Error.WriteLine("  --defocus-center-factor <>=1> (with --defocus-centering) override the max multiplier on StarCenterTolerance (default 2.0).");
            Console.Error.WriteLine("  --defocus-structure         (opt-in test switch) flips DefocusAwareStructure ON (EARLY-stage: coarser large-structure removal so donut/defocused stars form candidates).");
            Console.Error.WriteLine("  --structure-boost <0..6>    (with --defocus-structure) extra wavelet layers for large-structure removal (default 2; 0 ⇒ bit-identical control).");
            Console.Error.WriteLine("  --structure-layers <n>      (diagnostic) override nominal StructureLayers (e.g. 4 for the factory baseline), independent of the profile.");
        }

        // ---- Run / frame discovery (mirrors StarReviewRunner / OptimizationDiagnosticRunner) ----------------

        private sealed class FrameRef {
            public string Path;
            public int FocuserPosition;
        }

        private sealed class DiscoveredRun {
            public string RunId;
            public List<FrameRef> Frames;
        }

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
                var match = OptimizationRunDiscovery.ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
                if (!match.Success) {
                    continue;
                }
                if (!int.TryParse(match.Groups["FOCUSER"].Value, out var pos)) {
                    continue;
                }
                frames.Add(new FrameRef { Path = file.FullName, FocuserPosition = pos });
            }
            return frames;
        }
    }
}
