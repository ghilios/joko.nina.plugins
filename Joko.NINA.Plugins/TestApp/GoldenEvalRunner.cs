#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
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
using Size = OpenCvSharp.Size;

namespace TestApp {

    /// <summary>
    /// <c>golden eval</c>: runs the REAL star detector over a saved AF sweep with a chosen settings bundle and scores
    /// precision/recall against the visual GOLDEN set (per-image <c>&lt;image&gt;.golden.json</c> sidecars). Reports overall,
    /// per-region (the 6 AF regions from the saved reports), per-frame, per-confidence-cut and per-defocus metrics,
    /// attributes every false negative to a gate (ACCEPTED-elsewhere / REJECTED:&lt;gate&gt; / NO CANDIDATE structure
    /// gap), and lists false positives. The golden set is detector-independent ground truth; this only consumes it.
    ///
    /// Usage:
    ///   TestApp golden eval --runs &lt;dir&gt; [--golden &lt;dir&gt;] [--params current|default|optimized] [--opt-results &lt;dir&gt;]
    ///       [--match center|iou|both] [--iou 0.3] [--match-radius 0] [--pixel-scale header|profile]
    ///       [--label &lt;name&gt;] [--annotate] [--run &lt;runId&gt;] [--profile-id &lt;guid&gt;]
    ///       [--defocus-donut] [--defocus-gates] [--defocus-structure] [--structure-layer-boost N]
    ///       [--defocus-size-ref V] [--defocus-min-factor V] [--defocus-center-factor V]
    ///       [--donut-morph-close N] [--donut-hole-fraction V] [--donut-streak-ecc V] [--donut-bloom-radius V]
    ///
    /// <c>--pixel-scale</c> (V-P1) and the dataset-provided <c>matchRadiusPx</c> (V-P2) are documented on
    /// <see cref="BankVerifyRunner"/>'s identical seam; both default to reading the run's OWN frame header /
    /// dataset metadata rather than one bank-wide value from the active profile / CLI default.
    /// </summary>
    internal static class GoldenEvalRunner {

        private sealed class RegionReportLite {
            public StarDetectionRegion Region { get; set; }
        }

        private sealed class NamedRegion {
            public int Index;
            public string Name;
            public RectD Rect;
        }

        private sealed class FrameEval {
            public int FocuserPosition;
            public string ParamsLabel;
            public int GoldenCount;
            public int Accepted;
            public int TP;
            public int FP;
            public int FN;
            public int FnAcceptedElsewhere;
            public int FnNoCandidate;
            public Dictionary<string, int> FnByGate = new Dictionary<string, int>(StringComparer.Ordinal);
            public int MatchedHigh, TotalHigh, MatchedHighMed, TotalHighMed, MatchedAll, TotalAll;
            public Dictionary<int, PrecisionRecall> PerRegion = new Dictionary<int, PrecisionRecall>();
            public double DefocusOffset; // |focuser - best| in steps (filled later)
        }

        public static async Task Run(string[] args) {
            if (Application.Current == null) {
                new Application();
            }

            var runsDir = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runsDir)) {
                Console.WriteLine("Usage: TestApp golden eval --runs <dir> [--golden <dir>] [--params current|default|optimized] [--opt-results <dir>] [--match center|iou|both] [--iou 0.3] [--match-radius 0] [--pixel-scale header|profile] [--label <name>] [--annotate] ...");
                return;
            }
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var goldenDirArg = DiagnosticUtil.GetArg(args, "--golden");
            var mode = (DiagnosticUtil.GetArg(args, "--params") ?? "current").Trim().ToLowerInvariant();
            var optResults = DiagnosticUtil.GetArg(args, "--opt-results");
            var matchArg = (DiagnosticUtil.GetArg(args, "--match") ?? "both").Trim().ToLowerInvariant();
            var tau = ParseDouble(DiagnosticUtil.GetArg(args, "--iou"), 0.3);
            // CLI default stays 0.0 (unchanged behavior) — see EvalRun for why 0 is dangerous in centroid mode and
            // how a synthetic dataset's own synthetic_meta.json overrides this per run (V-P2).
            var matchRadius = ParseDouble(DiagnosticUtil.GetArg(args, "--match-radius"), 0.0);
            // header (default): PixelScaleForFrame reads each run's OWN frame header — see BankVerifyRunner's
            // identical flag for the full rationale (V-P1). profile: the exact pre-V-P1 behavior, kept as an
            // escape hatch.
            var pixelScaleMode = string.Equals(DiagnosticUtil.GetArg(args, "--pixel-scale"), "profile", StringComparison.OrdinalIgnoreCase)
                ? "profile" : "header";
            var label = DiagnosticUtil.GetArg(args, "--label");
            var annotate = DiagnosticUtil.HasFlag(args, "--annotate");
            var runFilter = DiagnosticUtil.GetArg(args, "--run");
            var outDir = DiagnosticUtil.GetArg(args, "--out")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Logs", "hf-diag", "golden-eval", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var matchMode = matchArg == "center" ? GoldenMatchMode.Center
                : matchArg == "iou" ? GoldenMatchMode.Iou
                : matchArg == "centroid" ? GoldenMatchMode.Centroid
                : GoldenMatchMode.Both;

            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile;
            if (activeProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA once to create a profile.");
            }
            // Detector settings come from the harness's LOCAL settings file, not the NINA profile: a
            // profile-sourced value is mutable machine state nothing records, and the ACTIVE profile can
            // even be a different telescope between runs. See HarnessSettingsStore.
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, activeProfile);
            var accessor = harnessSettings.Accessor;
            var options = new StarDetectionOptions(profileService, accessor);

            // V-P1: the pre-change bank-wide value — "profile" mode's value AND "header" mode's fallback when a
            // run's own frame header carries no usable pixel size / focal length. See BankVerifyRunner for the
            // identical computation and rationale.
            const int binning = 1;
            var profilePixelScale = MathUtility.ArcsecPerPixel(activeProfile.CameraSettings.PixelSize, activeProfile.TelescopeSettings.FocalLength) * binning;

            Directory.CreateDirectory(outDir);
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");
            Console.WriteLine($"Output: {outDir}  (params={mode}, match={matchMode}, iou={tau}, pixelScale={pixelScaleMode})");

            var discovery = OptimizationRunDiscovery.Discover(runsDir);
            if (discovery.Runs.Count == 0) {
                throw new InvalidOperationException($"No usable AF runs found under {runsDir}.");
            }

            var detector = new StarDetector(new AlglibAPI());

            foreach (var run in discovery.Runs) {
                if (!string.IsNullOrWhiteSpace(runFilter) && !string.Equals(run.RunId, runFilter, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                await EvalRun(run, runsDir, goldenDirArg, mode, optResults, matchMode, tau, matchRadius, label, annotate, outDir,
                    options, activeProfile, profileService, detector, args, harnessSettings, profilePixelScale, pixelScaleMode);
            }
            Console.WriteLine("Done.");
        }

        private static async Task EvalRun(OptimizationRunDiscovery.DiscoveredRun run, string runsRoot, string goldenDirArg, string mode,
            string optResults, GoldenMatchMode matchMode, double tau, double matchRadius, string label, bool annotate, string outRoot,
            StarDetectionOptions options, IProfile activeProfile, ProfileService profileService, StarDetector detector, string[] args,
            HarnessSettingsStore.Resolved harnessSettings, double profilePixelScale, string pixelScaleMode) {

            var runFolder = Path.GetDirectoryName(run.Frames.First().Path);
            var p = BuildEvalParams(options, mode, optResults, runFolder, args, out var sourceLabel);
            var paramsLabel = string.IsNullOrWhiteSpace(label) ? sourceLabel : label;
            var bestFocuser = ReadBestFocuser(runFolder);
            var stepSize = InferStep(run);

            var runOut = Path.Combine(outRoot, OptimizationRunDiscovery.SanitizeForFileName(run.RunId));
            Directory.CreateDirectory(runOut);

            // V-P2: match radius FOR THIS RUN — the synthetic bank's own matchRadiusPx (dataset root, one level
            // above this run's frame folder) wins over the CLI/default when present; see SyntheticDatasetMeta and
            // BankVerifyRunner's identical seam. The CLI default here is 0.0 (unchanged by this work), which
            // silently matches NOTHING in centroid mode (GoldenGeometry requires radius > 0), so that combination
            // gets a loud warning below rather than a quietly empty report.
            var syntheticMatchRadius = SyntheticDatasetMeta.TryReadMatchRadiusPx(runFolder, runsRoot);
            var effectiveMatchRadius = syntheticMatchRadius ?? matchRadius;
            var matchRadiusSource = syntheticMatchRadius.HasValue ? "synthetic_meta.json" : "CLI/default";
            if (matchMode == GoldenMatchMode.Centroid && effectiveMatchRadius <= 0.0) {
                Console.WriteLine($"  WARNING: centroid match mode with matchRadius={effectiveMatchRadius} matches NOTHING " +
                    "(GoldenGeometry requires radius > 0) — every golden star will be a false negative. Pass --match-radius, " +
                    "or use a synthetic dataset whose synthetic_meta.json carries matchRadiusPx.");
            }
            Console.WriteLine($"  matchRadius: {Fmt(effectiveMatchRadius)}px ({matchRadiusSource})");

            // Optional focuser-position filter (sweep on a single representative frame quickly).
            var framesArg = DiagnosticUtil.GetArg(args, "--frames");
            HashSet<int> framesFilter = null;
            if (!string.IsNullOrWhiteSpace(framesArg)) {
                framesFilter = new HashSet<int>();
                foreach (var part in framesArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                    if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) {
                        framesFilter.Add(v);
                    }
                }
            }

            // V-P1: pixel scale FOR THIS RUN, resolved once from the first frame this loop actually processes (the
            // first with a golden sidecar, honoring --frames when given) — see BuildEvalParams and BankVerifyRunner
            // for the identical seam/rationale. p.PixelScale is set the first time through the loop, before the
            // first Detect call, so every frame in this run is detected at the same value.
            var pixelScaleResolved = false;
            var effectivePixelScale = double.NaN;
            string pixelScaleSource = null;

            var frameEvals = new List<FrameEval>();
            foreach (var frame in run.Frames.OrderBy(f => f.FocuserPosition)) {
                if (framesFilter != null && !framesFilter.Contains(frame.FocuserPosition)) {
                    continue;
                }
                // Per-image golden sidecar: beside the image, or in --golden dir by image filename.
                var goldenPath = string.IsNullOrWhiteSpace(goldenDirArg)
                    ? frame.Path
                    : Path.Combine(goldenDirArg, Path.GetFileName(frame.Path));
                var gf = GoldenStarSetStore.LoadForImage(goldenPath);
                if (gf == null) {
                    Console.WriteLine($"  f{frame.FocuserPosition}: no golden sidecar ({GoldenStarSetStore.PathForImage(goldenPath)}); skipped.");
                    continue;
                }

                // Detect on the IRenderedImage, exactly as the live app does: the CFA hotpixel filter and the
                // debayer then run INSIDE Detect at these params (see DiagnosticUtil.LoadRenderedImage). Recall and
                // precision are only meaningful when the harness scores the image the app actually detects on.
                var rendered = await DiagnosticUtil.LoadRenderedImage(frame.Path, profileService);

                if (!pixelScaleResolved) {
                    pixelScaleResolved = true;
                    if (pixelScaleMode == "profile") {
                        effectivePixelScale = profilePixelScale;
                        pixelScaleSource = "profile (forced via --pixel-scale profile)";
                    } else {
                        var firstFrameMeta = rendered.RawImageData?.MetaData;
                        var headerScale = HarnessSettingsStore.PixelScaleForFrame(firstFrameMeta, harnessSettings, out var headerSource);
                        if (double.IsFinite(headerScale)) {
                            effectivePixelScale = headerScale;
                            pixelScaleSource = headerSource;
                        } else {
                            effectivePixelScale = profilePixelScale;
                            pixelScaleSource = $"profile (fallback: {headerSource})";
                        }
                    }
                    p.PixelScale = effectivePixelScale;
                    Console.WriteLine($"  pixelScale: {Fmt(effectivePixelScale)} arcsec/px ({pixelScaleSource})");
                }

                var fullW = rendered.RawImageData.Properties.Width;
                var fullH = rendered.RawImageData.Properties.Height;
                var regions = ReadRegions(runFolder, fullW, fullH);

                // Detect(IRenderedImage) builds its own source Mat per call, so the caller's image is never mutated.
                var result = await detector.Detect(rendered, p, null, CancellationToken.None);

                var stars = result.DetectedStars ?? new List<Star>();
                var det = stars.Select(s => new DetBox(RectD.FromRect(s.StarBoundingBox), s.Center.X, s.Center.Y)).ToList();
                var acceptedBounds = stars.Select(s => s.StarBoundingBox).ToList();
                var rejected = result.RejectedCandidates ?? new List<RejectedCandidateRecord>();

                var goldenRects = gf.Stars.Select(b => new RectD(b.X, b.Y, b.W, b.H)).ToList();
                // Unresolved candidates are neither stars nor confirmed non-stars, so they are absent from
                // goldenRects (no effect on recall) and are subtracted from the false positives below.
                var unresolvedRects = (gf.Unresolved ?? new List<GoldenStarBox>())
                    .Select(b => new RectD(b.X, b.Y, b.W, b.H)).ToList();
                var match = GoldenMatch.Match(goldenRects, det, matchMode, tau, effectiveMatchRadius);
                var falsePositives = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, unresolvedRects);

                var fe = new FrameEval {
                    FocuserPosition = frame.FocuserPosition,
                    ParamsLabel = paramsLabel,
                    GoldenCount = gf.Stars.Count,
                    Accepted = stars.Count,
                    TP = match.Pairs.Count,
                    FP = falsePositives.Count,
                    FN = match.FalseNegatives.Count,
                    DefocusOffset = bestFocuser.HasValue && stepSize > 0
                        ? Math.Abs(frame.FocuserPosition - bestFocuser.Value) / (double)stepSize
                        : double.NaN
                };

                // Per-confidence recall (cuts: high; high+med; all). A golden box is "matched" iff it is in a TP pair.
                var matchedGolden = new HashSet<int>(match.Pairs.Select(x => x.Golden));
                for (int gi = 0; gi < gf.Stars.Count; gi++) {
                    var rank = GoldenConfidence.Rank(gf.Stars[gi].Confidence);
                    var matched = matchedGolden.Contains(gi);
                    fe.TotalAll++; if (matched) fe.MatchedAll++;
                    if (rank >= 2) { fe.TotalHighMed++; if (matched) fe.MatchedHighMed++; }
                    if (rank >= 3) { fe.TotalHigh++; if (matched) fe.MatchedHigh++; }
                }

                // FN attribution.
                foreach (var gi in match.FalseNegatives) {
                    var cls = BoxMatcher.Classify(goldenRects[gi], acceptedBounds, rejected);
                    if (cls.Kind == BoxClassification.Accepted) {
                        fe.FnAcceptedElsewhere++;
                    } else if (cls.Kind == BoxClassification.Rejected) {
                        var gate = cls.Gate ?? "Unknown";
                        fe.FnByGate.TryGetValue(gate, out var c);
                        fe.FnByGate[gate] = c + 1;
                    } else {
                        fe.FnNoCandidate++;
                    }
                }

                // Per-region (assign golden & detected to regions by center).
                foreach (var region in regions) {
                    var gIdx = Enumerable.Range(0, goldenRects.Count)
                        .Where(i => GoldenMatch.CenterInRect(goldenRects[i].X + goldenRects[i].W / 2.0, goldenRects[i].Y + goldenRects[i].H / 2.0, region.Rect))
                        .ToHashSet();
                    var dIdx = Enumerable.Range(0, det.Count)
                        .Where(i => GoldenMatch.CenterInRect(det[i].Cx, det[i].Cy, region.Rect))
                        .ToHashSet();
                    var tp = match.Pairs.Count(pr => gIdx.Contains(pr.Golden));
                    var fn = match.FalseNegatives.Count(gi => gIdx.Contains(gi));
                    var fp = falsePositives.Count(di => dIdx.Contains(di));
                    fe.PerRegion[region.Index] = PrecisionRecall.Compute(tp, fp, fn);
                }

                // Dump detected accepted stars (full-frame center+bbox+HFR) so the golden can be validated against
                // what the detector actually accepted.
                var dcsv = new StringBuilder("cx,cy,x,y,w,h,hfr\n");
                foreach (var s in stars) {
                    dcsv.AppendLine($"{s.Center.X.ToString("F1", CultureInfo.InvariantCulture)},{s.Center.Y.ToString("F1", CultureInfo.InvariantCulture)}," +
                        $"{s.StarBoundingBox.X},{s.StarBoundingBox.Y},{s.StarBoundingBox.Width},{s.StarBoundingBox.Height},{s.HFR.ToString("F2", CultureInfo.InvariantCulture)}");
                }
                File.WriteAllText(Path.Combine(runOut, $"detected_f{frame.FocuserPosition}.csv"), dcsv.ToString());

                frameEvals.Add(fe);
                Console.WriteLine($"  f{frame.FocuserPosition}: golden={fe.GoldenCount} accepted={fe.Accepted} TP={fe.TP} FP={fe.FP} FN={fe.FN} " +
                    $"P={Fmt(PrecisionRecall.Compute(fe.TP, fe.FP, fe.FN).Precision)} R={Fmt(PrecisionRecall.Compute(fe.TP, fe.FP, fe.FN).Recall)}");

                if (annotate) {
                    // Display only (debayered luminance for an OSC frame, never CFA-filtered) — the overlay boxes
                    // are full-frame pixel coordinates, which the debayer preserves.
                    using var displayMat = RenderedImageLoading.ToDebayeredLuminanceMat(rendered);
                    WriteAnnotated(displayMat, gf, stars, match, runOut, frame.FocuserPosition);
                }
            }

            if (frameEvals.Count == 0) {
                Console.WriteLine($"  run '{run.RunId}': no per-image golden sidecars found; nothing scored.");
                return;
            }
            WriteReports(runOut, run.RunId, paramsLabel, sourceLabel, p, frameEvals, matchMode, tau, effectiveMatchRadius,
                effectivePixelScale, pixelScaleSource, matchRadiusSource);
        }

        // ---- Params bundle ---------------------------------------------------------------------------------

        private static StarDetectorParams BuildEvalParams(StarDetectionOptions options, string mode,
            string optResults, string runFolder, string[] args, out string sourceLabel) {

            var p = mode == "default"
                ? HocusFocusStarDetection.BuildDefaultStarDetectorParams()
                : HocusFocusStarDetection.BuildStarDetectorParams(options);

            // V-P1: PixelScale is NOT set here — it depends on the run's first frame header (or the profile
            // fallback) and is resolved once the caller (EvalRun) has actually loaded that frame. Setting it here
            // would mean either loading a frame early just for this, or reproducing the pre-V-P1 bug of one
            // profile-wide value for every run. See EvalRun's "V-P1: pixel scale FOR THIS RUN" block.
            p.Region = StarDetectionRegion.Full;
            p.ModelPSF = false;
            p.SaveIntermediateFilesPath = string.Empty;
            sourceLabel = mode;

            if (mode == "optimized") {
                var snapshot = ResolveSnapshot(options, optResults, runFolder, out var src);
                if (snapshot != null) {
                    OverlayAll(p, snapshot);
                    sourceLabel = $"optimized ({src})";
                } else {
                    Console.WriteLine("WARNING: --params optimized but no snapshot found; using current.");
                    sourceLabel = "optimized(missing->current)";
                }
            }

            ApplyDefocusOverrides(p, args, ref sourceLabel);
            ApplyParamOverrides(p, args, ref sourceLabel);
            p.CollectRejectedCandidateDiagnostics = true;
            return p;
        }

        /// <summary>Generic detector-param overrides for sweeping the candidate-formation vs late-gate frontier
        /// (e.g. --noise-clip / --structure-layers / --min-box drive candidate FORMATION; --sensitivity etc. are
        /// late gates). Applied last, so a flag wins over the mode/snapshot.</summary>
        private static void ApplyParamOverrides(StarDetectorParams p, string[] args, ref string sourceLabel) {
            var notes = new List<string>();
            void Dbl(string name, Action<double> set, string tag) {
                var s = DiagnosticUtil.GetArg(args, name);
                if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) { set(v); notes.Add($"{tag}={v.ToString(CultureInfo.InvariantCulture)}"); }
            }
            void Int(string name, Action<int> set, string tag) {
                var s = DiagnosticUtil.GetArg(args, name);
                if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) { set(v); notes.Add($"{tag}={v}"); }
            }
            Dbl("--noise-clip", v => p.NoiseClippingMultiplier = v, "noiseClip");      // structure-map binarize (candidate formation)
            Int("--structure-layers", v => p.StructureLayers = v, "structLayers");      // wavelet depth (candidate formation)
            Int("--noise-reduction-radius", v => p.NoiseReductionRadius = v, "nrRadius");
            Int("--min-box", v => p.MinimumStarBoundingBoxSize = v, "minBox");          // candidate size filter
            Dbl("--sensitivity", v => p.Sensitivity = v, "sens");                       // late brightness gate
            Dbl("--star-clip", v => p.StarClippingMultiplier = v, "starClip");
            Dbl("--peak-response", v => p.PeakResponse = v, "peak");
            Dbl("--max-distortion", v => p.MaxDistortion = v, "maxDist");
            Dbl("--min-hfr", v => p.MinHFR = v, "minHFR");
            Dbl("--star-center-tolerance", v => p.StarCenterTolerance = v, "centerTol");
            Int("--adaptive-block", v => p.AdaptiveNoiseBlockSize = v, "adaptiveBlock");  // adaptive-binarization grid (candidate formation)
            if (DiagnosticUtil.HasFlag(args, "--adaptive-binarize")) { p.LocallyAdaptiveBinarization = true; notes.Add("adaptiveBinarize"); }
            if (notes.Count > 0) {
                sourceLabel += " +params[" + string.Join(",", notes) + "]";
            }
        }

        /// <summary>Overlays ALL optimized snapshot fields including the v2 defocus-aware ones (the in-tree TestApp
        /// OverlaySnapshot only copies the 12 curated knobs). AND-gates defocus flags by the master exactly as
        /// <c>HocusFocusStarDetection.BuildStarDetectorParams</c> does, so optimized+defocus is faithful.</summary>
        private static void OverlayAll(StarDetectorParams p, OptimizedStarDetectionSettings s) {
            p.Sensitivity = s.BrightnessSensitivity;
            p.StarClippingMultiplier = s.StarClippingMultiplier;
            p.NoiseClippingMultiplier = s.NoiseClippingMultiplier;
            p.PeakResponse = s.StarPeakResponse;
            p.MaxDistortion = s.MaxDistortion;
            p.MinHFR = s.MinHFR;
            p.StarCenterTolerance = s.StarCenterTolerance;
            p.StructureLayers = s.StructureLayers;
            p.NoiseReductionRadius = s.NoiseReductionRadius;
            p.MinimumStarBoundingBoxSize = s.MinStarBoundingBoxSize;
            p.HotpixelThresholdingEnabled = s.HotpixelThresholdingEnabled;
            p.HotpixelThreshold = s.HotpixelThreshold;
            p.LocallyAdaptiveBinarization = s.LocallyAdaptiveBinarization;
            p.AdaptiveNoiseBlockSize = s.AdaptiveNoiseBlockSize;

            var master = s.DefocusAwareDonutDetection;
            p.DefocusAwareDonutDetection = master;
            p.DefocusAwareDistortion = s.DefocusAwareGates && master;
            p.DefocusAwareCentering = s.DefocusAwareGates && master;
            p.DefocusAwareStructure = s.DefocusAwareStructure && master;
            p.StructureLayerBoost = s.StructureLayerBoost;
            p.DefocusDistortionSizeReference = s.DefocusDistortionSizeReference;
            p.DefocusDistortionMinFactor = s.DefocusDistortionMinFactor;
            p.DefocusCenteringToleranceFactor = s.DefocusCenteringToleranceFactor;
            p.DonutMorphCloseSize = s.DonutMorphCloseSize;
            p.DonutMinAnnularityHoleFraction = s.DonutMinAnnularityHoleFraction;
            p.DonutMaxStreakEccentricity = s.DonutMaxStreakEccentricity;
            p.DonutSaturationBloomRadius = s.DonutSaturationBloomRadius;
        }

        /// <summary>Explicit defocus overrides win last (force gates ON for diagnosis regardless of snapshot/mode).</summary>
        private static void ApplyDefocusOverrides(StarDetectorParams p, string[] args, ref string sourceLabel) {
            var notes = new List<string>();
            if (DiagnosticUtil.HasFlag(args, "--defocus-donut")) { p.DefocusAwareDonutDetection = true; notes.Add("donut"); }
            if (DiagnosticUtil.HasFlag(args, "--defocus-gates")) { p.DefocusAwareDistortion = true; p.DefocusAwareCentering = true; notes.Add("gates"); }
            if (DiagnosticUtil.HasFlag(args, "--defocus-structure")) {
                p.DefocusAwareStructure = true;
                if (p.StructureLayerBoost <= 0) { p.StructureLayerBoost = 2; }
                notes.Add("structure");
            }
            var slb = DiagnosticUtil.GetArg(args, "--structure-layer-boost");
            if (slb != null && int.TryParse(slb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slbV)) { p.StructureLayerBoost = slbV; notes.Add($"boost={slbV}"); }
            ApplyDoubleArg(args, "--defocus-size-ref", v => p.DefocusDistortionSizeReference = v, notes, "sizeRef");
            ApplyDoubleArg(args, "--defocus-min-factor", v => p.DefocusDistortionMinFactor = v, notes, "minFactor");
            ApplyDoubleArg(args, "--defocus-center-factor", v => p.DefocusCenteringToleranceFactor = v, notes, "centerFactor");
            ApplyDoubleArg(args, "--donut-hole-fraction", v => p.DonutMinAnnularityHoleFraction = v, notes, "hole");
            ApplyDoubleArg(args, "--donut-streak-ecc", v => p.DonutMaxStreakEccentricity = v, notes, "streakEcc");
            ApplyDoubleArg(args, "--donut-bloom-radius", v => p.DonutSaturationBloomRadius = v, notes, "bloom");
            var dmc = DiagnosticUtil.GetArg(args, "--donut-morph-close");
            if (dmc != null && int.TryParse(dmc, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dmcV)) { p.DonutMorphCloseSize = dmcV; notes.Add($"morph={dmcV}"); }
            if (notes.Count > 0) {
                sourceLabel += " +overrides[" + string.Join(",", notes) + "]";
            }
        }

        private static void ApplyDoubleArg(string[] args, string name, Action<double> set, List<string> notes, string tag) {
            var s = DiagnosticUtil.GetArg(args, name);
            if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) {
                set(v);
                notes.Add($"{tag}={v.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private static OptimizedStarDetectionSettings ResolveSnapshot(StarDetectionOptions options, string optResults, string runFolder, out string source) {
            foreach (var candidate in new[] {
                string.IsNullOrWhiteSpace(optResults) ? null : Path.Combine(optResults, "optimized_settings.json"),
                string.IsNullOrWhiteSpace(runFolder) ? null : Path.Combine(runFolder, "optimized_settings.json")
            }) {
                if (candidate != null && File.Exists(candidate)) {
                    try {
                        source = candidate;
                        return JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(File.ReadAllText(candidate));
                    } catch (Exception ex) {
                        Console.WriteLine($"WARNING: could not parse '{candidate}': {ex.Message}");
                    }
                }
            }
            var profileSnapshot = options.GetOptimizedSettings();
            if (profileSnapshot != null) {
                source = "profile";
                return profileSnapshot;
            }
            source = "(none)";
            return null;
        }

        // ---- Region geometry -------------------------------------------------------------------------------

        private static readonly string[] RegionNames = { "Global", "Center", "Corner-TL", "Corner-TR", "Corner-BL", "Corner-BR", "OuterROI" };

        private static List<NamedRegion> ReadRegions(string runFolder, int fullW, int fullH, bool namesOnly = false) {
            var list = new List<NamedRegion>();
            for (int i = 0; i < 7; i++) {
                var path = Path.Combine(runFolder ?? string.Empty, $"autofocus_report_Region{i}.json");
                if (!File.Exists(path)) {
                    continue;
                }
                if (namesOnly) {
                    list.Add(new NamedRegion { Index = i, Name = NameFor(i), Rect = default });
                    continue;
                }
                try {
                    var lite = JsonConvert.DeserializeObject<RegionReportLite>(File.ReadAllText(path));
                    var outer = lite?.Region?.OuterBoundary;
                    if (outer != null) {
                        var r = outer.ToRectangle(new System.Drawing.Size(fullW, fullH));
                        list.Add(new NamedRegion { Index = i, Name = NameFor(i), Rect = new RectD(r.X, r.Y, r.Width, r.Height) });
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"  WARNING: could not read {path}: {ex.Message}");
                }
            }
            if (list.Count == 0 && !namesOnly) {
                Console.WriteLine("  WARNING: no autofocus_report_Region*.json found; per-region metrics unavailable.");
            }
            return list;
        }

        private static string NameFor(int i) => i >= 0 && i < RegionNames.Length ? RegionNames[i] : $"Region{i}";

        private static int? ReadBestFocuser(string runFolder) {
            var path = Path.Combine(runFolder ?? string.Empty, "autofocus_report_Region0.json");
            if (!File.Exists(path)) {
                return null;
            }
            try {
                dynamic d = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(path));
                var pos = d?.CalculatedFocusPoint?.Position;
                if (pos != null) {
                    return (int)Math.Round((double)pos);
                }
            } catch { }
            return null;
        }

        private static int InferStep(OptimizationRunDiscovery.DiscoveredRun run) {
            var positions = run.Frames.Select(f => f.FocuserPosition).Distinct().OrderBy(x => x).ToList();
            if (positions.Count < 2) {
                return 0;
            }
            var diffs = new List<int>();
            for (int i = 1; i < positions.Count; i++) {
                diffs.Add(positions[i] - positions[i - 1]);
            }
            return diffs.OrderBy(x => x).ElementAt(diffs.Count / 2); // median gap
        }

        // ---- Reporting -------------------------------------------------------------------------------------

        private static void WriteReports(string runOut, string runId, string paramsLabel, string sourceLabel,
            StarDetectorParams p, List<FrameEval> frames, GoldenMatchMode matchMode, double tau, double matchRadius,
            double pixelScale, string pixelScaleSource, string matchRadiusSource) {

            // CSV (per-frame).
            var csv = new StringBuilder();
            csv.AppendLine("focuser,params,golden,accepted,TP,FP,FN,precision,recall,f1,recallHigh,recallHighMed,recallAll,fnAcceptedElsewhere,fnNoCandidate,fnRejected");
            foreach (var f in frames.OrderBy(f => f.FocuserPosition)) {
                var pr = PrecisionRecall.Compute(f.TP, f.FP, f.FN);
                var fnRej = f.FnByGate.Values.Sum();
                csv.AppendLine(string.Join(",",
                    f.FocuserPosition, Csv(paramsLabel), f.GoldenCount, f.Accepted, f.TP, f.FP, f.FN,
                    Fmt(pr.Precision), Fmt(pr.Recall), Fmt(pr.F1),
                    Ratio(f.MatchedHigh, f.TotalHigh), Ratio(f.MatchedHighMed, f.TotalHighMed), Ratio(f.MatchedAll, f.TotalAll),
                    f.FnAcceptedElsewhere, f.FnNoCandidate, fnRej));
            }
            File.WriteAllText(Path.Combine(runOut, "golden_eval_frames.csv"), csv.ToString());

            // Per-region CSV.
            var rcsv = new StringBuilder();
            rcsv.AppendLine("focuser,params,regionIndex,regionName,TP,FP,FN,precision,recall,f1");
            foreach (var f in frames.OrderBy(f => f.FocuserPosition)) {
                foreach (var kv in f.PerRegion.OrderBy(k => k.Key)) {
                    var pr = kv.Value;
                    rcsv.AppendLine(string.Join(",", f.FocuserPosition, Csv(paramsLabel), kv.Key, NameFor(kv.Key),
                        pr.TP, pr.FP, pr.FN, Fmt(pr.Precision), Fmt(pr.Recall), Fmt(pr.F1)));
                }
            }
            File.WriteAllText(Path.Combine(runOut, "golden_eval_regions.csv"), rcsv.ToString());

            // Text summary.
            var sb = new StringBuilder();
            sb.AppendLine($"GOLDEN EVAL — run '{runId}'");
            sb.AppendLine($"params: {paramsLabel}   (source: {sourceLabel})");
            // V-P1/V-P2: record which source won for the two per-run knobs this report was scored with, so a
            // reader comparing two reports can tell whether a delta came from the detector or from a resolved
            // input changing underneath it.
            sb.AppendLine($"pixelScale: {Fmt(pixelScale)} arcsec/px ({pixelScaleSource})   matchRadius: {Fmt(matchRadius)}px ({matchRadiusSource})");
            // Report the mode actually used — a hardcoded label here silently misattributes every stored report.
            var matchLabel = matchMode switch {
                GoldenMatchMode.Center => "center-in-box",
                GoldenMatchMode.Iou => $"IoU>={tau}",
                GoldenMatchMode.Centroid => $"centroid within {matchRadius}px",
                _ => $"center-in-box OR IoU>={tau}"
            };
            sb.AppendLine($"match: {matchLabel}; key detector knobs: Sensitivity={p.Sensitivity}, StarClip={p.StarClippingMultiplier}, " +
                $"NoiseClip={p.NoiseClippingMultiplier}, PeakResponse={p.PeakResponse}, MaxDistortion={p.MaxDistortion}, StarCenterTol={p.StarCenterTolerance}, " +
                $"StructureLayers={p.StructureLayers}, MinHFR={p.MinHFR}, MinBox={p.MinimumStarBoundingBoxSize}");
            sb.AppendLine($"defocus: Donut={p.DefocusAwareDonutDetection}, Distortion={p.DefocusAwareDistortion}, Centering={p.DefocusAwareCentering}, " +
                $"Structure={p.DefocusAwareStructure}, LayerBoost={p.StructureLayerBoost}, SizeRef={p.DefocusDistortionSizeReference}, " +
                $"MinFactor={p.DefocusDistortionMinFactor}, CenterFactor={p.DefocusCenteringToleranceFactor}, MorphClose={p.DonutMorphCloseSize}");
            sb.AppendLine();

            var tTP = frames.Sum(f => f.TP);
            var tFP = frames.Sum(f => f.FP);
            var tFN = frames.Sum(f => f.FN);
            var overall = PrecisionRecall.Compute(tTP, tFP, tFN);
            sb.AppendLine($"OVERALL: TP={tTP} FP={tFP} FN={tFN}  precision={Fmt(overall.Precision)} recall={Fmt(overall.Recall)} f1={Fmt(overall.F1)}");
            sb.AppendLine($"  recall@high={Ratio(frames.Sum(f => f.MatchedHigh), frames.Sum(f => f.TotalHigh))}  " +
                $"recall@high+med={Ratio(frames.Sum(f => f.MatchedHighMed), frames.Sum(f => f.TotalHighMed))}  " +
                $"recall@all={Ratio(frames.Sum(f => f.MatchedAll), frames.Sum(f => f.TotalAll))}");
            sb.AppendLine();

            sb.AppendLine("PER-FRAME (sorted by focuser):");
            sb.AppendLine("  focuser  Δsteps  golden  acc   TP   FP   FN   prec   recall   f1");
            foreach (var f in frames.OrderBy(f => f.FocuserPosition)) {
                var pr = PrecisionRecall.Compute(f.TP, f.FP, f.FN);
                sb.AppendLine($"  {f.FocuserPosition,7}  {(double.IsNaN(f.DefocusOffset) ? "  -  " : f.DefocusOffset.ToString("F1")),6}  {f.GoldenCount,6}  {f.Accepted,4}  {f.TP,4} {f.FP,4} {f.FN,4}  {Fmt(pr.Precision),6} {Fmt(pr.Recall),7} {Fmt(pr.F1),6}");
            }
            sb.AppendLine();

            sb.AppendLine("PER-REGION (aggregated over frames):");
            sb.AppendLine("  idx  name        TP   FP   FN   prec   recall   f1");
            var regionIndices = frames.SelectMany(f => f.PerRegion.Keys).Distinct().OrderBy(x => x).ToList();
            foreach (var idx in regionIndices) {
                var tp = frames.Where(f => f.PerRegion.ContainsKey(idx)).Sum(f => f.PerRegion[idx].TP);
                var fp = frames.Where(f => f.PerRegion.ContainsKey(idx)).Sum(f => f.PerRegion[idx].FP);
                var fn = frames.Where(f => f.PerRegion.ContainsKey(idx)).Sum(f => f.PerRegion[idx].FN);
                var pr = PrecisionRecall.Compute(tp, fp, fn);
                sb.AppendLine($"  {idx,3}  {NameFor(idx),-10}  {tp,4} {fp,4} {fn,4}  {Fmt(pr.Precision),6} {Fmt(pr.Recall),7} {Fmt(pr.F1),6}");
            }
            sb.AppendLine();

            sb.AppendLine("FALSE-NEGATIVE ATTRIBUTION (where did the missed golden stars go?):");
            sb.AppendLine($"  ACCEPTED-elsewhere: {frames.Sum(f => f.FnAcceptedElsewhere)}");
            sb.AppendLine($"  NO CANDIDATE (structure gap): {frames.Sum(f => f.FnNoCandidate)}");
            var allGates = frames.SelectMany(f => f.FnByGate.Keys).Distinct().OrderBy(x => x);
            foreach (var gate in allGates) {
                sb.AppendLine($"  REJECTED:{gate}: {frames.Sum(f => f.FnByGate.TryGetValue(gate, out var c) ? c : 0)}");
            }

            File.WriteAllText(Path.Combine(runOut, "golden_eval.txt"), sb.ToString());
            Console.WriteLine();
            Console.WriteLine(sb.ToString());
            Console.WriteLine($"  reports -> {runOut}");
        }

        // ---- Annotation ------------------------------------------------------------------------------------

        private static void WriteAnnotated(Mat floatMat, GoldenFrame gf, List<Star> stars, GoldenMatchResult match, string runOut, int focuser) {
            using var bgr = BuildStretchedBgr(floatMat);
            var matchedGolden = new HashSet<int>(match.Pairs.Select(x => x.Golden));
            var matchedDet = new HashSet<int>(match.Pairs.Select(x => x.Detected));

            // Golden boxes: TP green, FN yellow.
            for (int gi = 0; gi < gf.Stars.Count; gi++) {
                var b = gf.Stars[gi];
                var rect = new Rect((int)Math.Round(b.X), (int)Math.Round(b.Y), Math.Max(1, (int)Math.Round(b.W)), Math.Max(1, (int)Math.Round(b.H)));
                var color = matchedGolden.Contains(gi) ? new Scalar(0, 255, 0) : new Scalar(0, 255, 255);
                Cv2.Rectangle(bgr, rect, color, 3, LineTypes.AntiAlias);
            }
            // FP accepted stars: red.
            for (int di = 0; di < stars.Count; di++) {
                if (matchedDet.Contains(di)) {
                    continue;
                }
                var bb = stars[di].StarBoundingBox;
                Cv2.Rectangle(bgr, bb, new Scalar(0, 0, 255), 2, LineTypes.AntiAlias);
            }

            // Downscale to a viewable overview (boxes already drawn at full scale).
            var scale = Math.Min(1.0, 3000.0 / Math.Max(bgr.Cols, bgr.Rows));
            using var outImg = new Mat();
            Cv2.Resize(bgr, outImg, new Size(Math.Max(1, (int)(bgr.Cols * scale)), Math.Max(1, (int)(bgr.Rows * scale))), interpolation: InterpolationFlags.Area);
            Cv2.ImWrite(Path.Combine(runOut, $"f{focuser}_eval.png"), outImg);
        }

        // ---- helpers ---------------------------------------------------------------------------------------

        private static string Fmt(double v) => double.IsNaN(v) ? "NaN" : v.ToString("F3", CultureInfo.InvariantCulture);
        private static string Ratio(int num, int den) => den == 0 ? "NaN" : ((double)num / den).ToString("F3", CultureInfo.InvariantCulture) + $"({num}/{den})";
        private static string Csv(string s) => s == null ? "" : "\"" + s.Replace("\"", "'") + "\"";
        private static double ParseDouble(string s, double fallback) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static Mat BuildStretchedBgr(Mat srcFloat) {
            using var src16 = new Mat();
            srcFloat.ConvertTo(src16, MatType.CV_16U, ushort.MaxValue);
            using var stretched16 = new Mat();
            try {
                var stats = CvImageUtility.CalculateStatistics_Histogram(src16);
                using var lut = CvImageUtility.CreateMTFLookup(stats);
                CvImageUtility.ApplyLUT(src16, lut, stretched16);
            } catch (Exception ex) {
                Logger.Warning($"MTF stretch failed ({ex.Message}); falling back to linear normalization");
                Cv2.Normalize(src16, stretched16, 0, ushort.MaxValue, NormTypes.MinMax);
            }
            using var stretched8 = new Mat();
            stretched16.ConvertTo(stretched8, MatType.CV_8U, 1.0 / 256.0);
            var bgr = new Mat();
            Cv2.CvtColor(stretched8, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }
    }
}
