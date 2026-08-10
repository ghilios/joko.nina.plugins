#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using MathNet.Numerics.Distributions;
using Newtonsoft.Json.Linq;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using OpenCvSharp;
using OxyPlot.Series;
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

namespace TestApp {

    /// <summary>
    /// Headless diagnostic that reproduces the AutoFocus curve fit + outlier rejection on a saved AF run, to
    /// explain exactly which measured points the Grubbs test rejects and why. It re-detects each frame with the
    /// ORIGINAL run's detector params (deserialized from the run's saved _star_detection_result.json, so the
    /// ROI/knobs match the run rather than the possibly-drifted current profile), computes the same per-point
    /// (Y, σ) the engine uses in Median mode — Y = median HFR, σ = 1.483·MAD(HFR) over the detected stars — then
    /// runs the production Hybrid <see cref="AlglibHyperbolicFitting.SelectBestModel"/> and the production
    /// weighted <see cref="MathUtility.RejectionTest"/> exactly as <c>AutoFocusEngine</c> does, printing each
    /// point's absolute residual, standardized residual (resid/σ), Grubbs z-score, and the Grubbs limit.
    /// </summary>
    internal static class AfFitDiagnosticRunner {

        // Mirrors AutoFocusEngine.IMAGE_FILE_REGEX / FocusSweepDiagnosticRunner.
        private static readonly Regex ImageFileRegex = new Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private sealed class Frame {
            public string Path;
            public int FocuserPosition;
        }

        private sealed class PointRow {
            public int Position;
            public int Stars;
            public double MedianHFR;   // engine Y (Median mode)
            public double Sigma;       // engine σ = 1.483·MAD(HFR)
            public double RegSigma;    // σ after WeightRegularization
        }

        public static async Task Run(string[] args) {
            try {
                await RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "af-fit diagnostic run failed");
                Environment.ExitCode = 1;
            }
        }

        private static async Task RunImpl(string[] args) {
            var afRun = DiagnosticUtil.GetArg(args, "--af-run");
            if (string.IsNullOrWhiteSpace(afRun)) {
                Console.Error.WriteLine("Usage: TestApp af-fit --af-run <dir> [--profile-id <guid>] [--out <dir>]");
                Console.Error.WriteLine("                      [--max-rejections <n>=show 0..3] [--confidence <c>=0.95]");
                Console.Error.WriteLine("                      [--weighted true|false] [--step-size <n>]");
                Console.Error.WriteLine("                      [--mad-floor <f>] | [--mad-floor-rel <alpha>]   (mutually exclusive; F45(b))");
                Console.Error.WriteLine("                      [--sem-veto <t>] | [--sem-rank]   (mutually exclusive with each other AND");
                Console.Error.WriteLine("                                                         with the mad-floor flags; wave 16 item A)");
                Environment.ExitCode = 2;
                return;
            }
            if (!Directory.Exists(afRun)) {
                throw new DirectoryNotFoundException($"AF-run folder not found: {afRun}");
            }

            // The MAD-floor rung (F45(b)). Parsed BEFORE anything expensive runs: passing both families is a
            // usage error, and an arm that is going to be rejected for naming two rungs should be rejected
            // before it spends eleven minutes detecting stars.
            MadFloorSpec madFloor;
            SemScaleSpec semScale;
            try {
                madFloor = MadFloorArgs.Parse(args);
                // Same discipline, one wave later: SemScaleArgs also rejects an invocation that names a SEM rung
                // AND a MAD-floor rung, so "two mechanisms in one arm" fails here rather than in the write-up.
                semScale = SemScaleArgs.Parse(args);
            } catch (ArgumentException ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Environment.ExitCode = 2;
                return;
            }

            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "af-fit");
            }
            Directory.CreateDirectory(outDir);
            Logger.SetLogLevel(LogLevelEnum.INFO);

            double confidence = ParseDouble(DiagnosticUtil.GetArg(args, "--confidence"), 0.95);
            bool weighted = ParseBool(DiagnosticUtil.GetArg(args, "--weighted"), true);

            var frames = ParseAfRun(afRun);
            if (frames.Count == 0) {
                throw new InvalidOperationException($"No AF frames matched the filename pattern in {afRun}");
            }

            // A NINA profile is required to load XISF/FITS frames (DiagnosticUtil.LoadFloatMat). Also lets us read
            // the original AutoFocusStepSize if --step-size is not given.
            if (Application.Current == null) {
                new Application();
            }
            var profileService = new ProfileService();
            profileService.TryLoad(DiagnosticUtil.GetArg(args, "--profile-id") ?? string.Empty);
            if (profileService.ActiveProfile == null) {
                throw new InvalidOperationException("No active NINA profile. Pass --profile-id or run NINA once.");
            }
            Console.WriteLine($"Profile: {profileService.ActiveProfile.Name} ({profileService.ActiveProfile.Id})");

            // Faithful params: prefer the run's OWN saved detection params (ROI + knobs as they were when the run
            // executed) over the current profile, which may have drifted since. --params-from pins a specific saved
            // detection JSON (e.g. the initial-HFR frame, whose region may differ from the final-validation frame).
            var paramsFrom = DiagnosticUtil.GetArg(args, "--params-from");
            var (baseParams, paramsSource) = LoadOriginalDetectorParams(afRun, paramsFrom);
            if (baseParams == null) {
                // Detector settings come from the harness's LOCAL settings file, not the NINA profile: a
                // profile-sourced value is mutable machine state nothing records, and the ACTIVE profile can
                // even be a different telescope between runs. See HarnessSettingsStore.
                var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, profileService.ActiveProfile);
                var accessor = harnessSettings.Accessor;
                var options = new StarDetectionOptions(profileService, accessor);
                baseParams = HocusFocusStarDetection.BuildStarDetectorParams(options);
                // Name the FILE, not "the current profile". This line used to say "current profile", which was
                // false the moment HarnessSettingsStore took over the fallback — and a provenance line that
                // names the wrong source is worse than none, because it is quotable. F57/F58 are what a
                // profile-sourced input costs; a log that claims one when the file was used hides exactly that.
                paramsSource = $"harness settings file (no saved detection JSON found in run): {harnessSettings.Path ?? "code defaults"}";
            }
            baseParams.ModelPSF = false; // AutoFocus never models PSF; HFR is the measured quantity.
            Console.WriteLine($"Detector params from: {paramsSource}");
            Console.WriteLine($"Region: {baseParams.Region}");
            // RULE P16 (F67): the FULL params surface, printed by the same formatter `optimize` uses, so the two
            // entry points' bundles can be diffed field by field instead of guessed at from five fields. This is
            // baseParams in its final state — nothing below mutates it before StarDetector.Detect receives it.
            //
            // TO THE CONSOLE, AND NOT THROUGH `Emit`. Emit appends to `sb`, which becomes af_fit_summary.txt, and
            // clause W1 is a BYTE comparison of that file against wave 14's control rung. Diagnostic provenance
            // belongs in the log; one extra line in the report would spend the only clause that can catch a
            // wave-16 change that is not inert at its default.
            ParamsDump.Write(Console.WriteLine, ParamsDump.AfFitDetector, baseParams);

            int stepSize = (int)ParseDouble(DiagnosticUtil.GetArg(args, "--step-size"),
                EstimateStepSize(frames, profileService));
            Console.WriteLine($"Frames: {frames.Count}; step size for fit seed: {stepSize}; weighted={weighted}; confidence={confidence}");
            // Always logged, at every rung including the control — the LOG is where the rung is recorded
            // unconditionally, because af_fit_summary.txt must stay byte-for-byte the previous wave's file when
            // no floor applies (that identity is the control rung's whole evidentiary value).
            Console.WriteLine($"MAD floor: {MadFloorArgs.Describe(madFloor)}");
            Console.WriteLine($"SEM scale: {SemScaleArgs.Describe(semScale)}");

            // ---- Detect each frame and build the engine's (Y, σ) measurement point (Median mode). ----
            var detector = new StarDetector(new AlglibAPI());
            var rows = new List<PointRow>();
            foreach (var frame in frames.OrderByDescending(f => f.FocuserPosition)) {
                // Detect on the IRenderedImage: the CFA hotpixel filter + debayer run inside Detect at these params,
                // so the per-frame HFR this fit is built from is the one the live AF engine would measure.
                // DetectionSource keeps the .tif carve-out (this runner's frame match is extension-agnostic, so a
                // focus-sweep --synthesize folder of .tif frames reaches it).
                using var img = await DetectionSource.LoadAsync(frame.Path, profileService);
                var result = await img.DetectAsync(detector, baseParams, CancellationToken.None);
                var hfrs = result.DetectedStars.Select(s => s.HFR).ToList();
                if (hfrs.Count <= 1) {
                    Console.WriteLine($"  pos {frame.FocuserPosition}: {hfrs.Count} stars (skipped — Y would be 0)");
                    continue;
                }
                var (median, mad) = hfrs.MedianMAD();
                rows.Add(new PointRow { Position = frame.FocuserPosition, Stars = hfrs.Count, MedianHFR = median, Sigma = mad });
                Console.WriteLine($"  pos {frame.FocuserPosition}: {hfrs.Count} stars, medianHFR={median:F4}, sigma(1.483*MAD)={mad:F4}");
            }
            if (rows.Count < 3) {
                throw new InvalidOperationException($"Only {rows.Count} usable measurement points; need >= 3 to fit.");
            }

            // Raw points (X=focuser, Y=median HFR, ErrorY=σ) then WeightRegularization exactly as the engine does
            // before any weighted fit.
            var rawPoints = rows.Select(r => new ScatterErrorPoint(r.Position, r.MedianHFR, 0.0, r.Sigma)).ToList();
            var regular = WeightRegularization.Regularize(rawPoints);
            for (int i = 0; i < rows.Count; ++i) {
                rows[i].RegSigma = regular[i].ErrorY;
            }

            // N* per point, as a side map keyed by X — the shape BuildResidualWeights already uses for the 1/sigma
            // weights, so nothing about ScatterErrorPoint or WeightRegularization (both production) has to change.
            // X passes through Regularize and Clone unchanged, so every lookup resolves; an X that does NOT resolve
            // throws, because a silent 1 would be a star count nobody measured quietly entering the statistic.
            var starsByPosition = new Dictionary<double, double>();
            foreach (var r in rows) {
                starsByPosition[r.Position] = r.Stars;
            }
            Func<double, double> starCounts = x => starsByPosition.TryGetValue(x, out var n)
                ? n
                : throw new InvalidOperationException($"No star count recorded for focuser position {x.ToString("R", CultureInfo.InvariantCulture)}");
            // Handed to the fit ONLY at a live rung. At the control rung the calls below are LITERALLY the calls
            // the previous wave made — same overload resolution, same arguments — which is what clause W1's byte
            // comparison is measuring, so it is not weakened by a side map that happens to be wired up.
            var semCounts = semScale.IsNone ? null : starCounts;

            var alglib = new AlglibAPI();
            var sb = new StringBuilder();
            void Emit(string line) { Console.WriteLine(line); sb.AppendLine(line); }

            Emit("");
            Emit("================ AUTOFOCUS FIT + OUTLIER REJECTION (faithful reproduction) ================");
            Emit($"AF run        : {afRun}");
            Emit($"Params source : {paramsSource}");
            Emit($"Region        : {baseParams.Region}");
            Emit($"Measurement   : Median mode  ->  Y = median(HFR), sigma = 1.483*MAD(HFR) over detected stars");
            Emit($"Weighted fit  : {weighted}   (weight = 1/sigma; Grubbs uses standardized residual (Y-f)/sigma)");
            Emit($"Confidence    : {confidence}");
            // Named in the header only when a floor actually applies. With no floor this file is the control
            // rung, and the control rung's claim is that it reproduces the previous wave's file exactly — an
            // extra header line would break the comparison it exists to pass. The console log above names the
            // rung either way, so "which rung produced this" is never unrecorded.
            if (!madFloor.IsNone) {
                Emit($"MAD floor     : {MadFloorArgs.Describe(madFloor)}");
            }
            // Same rule, same reason: at rung N this file must be byte-for-byte wave 14's control-rung file, and
            // clause W1 is the ONLY clause that can catch a SemScaleSpec that is not inert at its default. One
            // extra header line would spend that clause on the report format.
            if (!semScale.IsNone) {
                Emit($"SEM scale     : {SemScaleArgs.Describe(semScale)}");
            }
            Emit("");

            var floorTag = MadFloorArgs.CaptionTag(madFloor);

            // ---- Production result: Hybrid SelectBestModel at each rejection budget 0..3. ----
            Emit($"---- Production Hybrid model selection, per outlier-rejection budget{floorTag} ----");
            Emit("budget | winner model      | #rej | rejected positions      | sigma(focus) | redChi^2 | R^2     | minPos");
            for (int budget = 0; budget <= 3; ++budget) {
                var pts = regular.Select(Clone).ToList();
                var model = AlglibHyperbolicFitting.SelectBestModel(
                    alglib, pts, stepSize, weighted, budget, confidence,
                    out var bestFit, out var rejected, madFloor: madFloor, semScale: semScale, starCounts: semCounts);
                var rejList = rejected == null || rejected.Count == 0
                    ? "(none)"
                    : string.Join(" ", rejected.Select(p => ((int)Math.Round(p.X)).ToString()));
                Emit($"  {budget}    | {model,-17} | {(rejected?.Count ?? 0),3}  | {rejList,-23} | " +
                    $"{F(bestFit?.MinimumStdError),11} | {F(bestFit?.ReducedChiSquared),7} | {F(bestFit?.RSquared),6} | {F(bestFit?.Minimum.X)}");
            }
            Emit("");

            // ---- Model-fairness analysis: per-model fit on the FULL data + each model's would-be outliers, then
            // a simulated CONSENSUS rejection (intersection of all models' outliers) and who wins after it. This
            // tests whether the symmetric outcome is driven by rejection (cured by consensus) or by the
            // MinimumStdError selection criterion itself (independent of rejection). ----
            int analysisBudget = (int)ParseDouble(DiagnosticUtil.GetArg(args, "--max-rejections"), 2);
            var candidates = new[] {
                HyperbolicFitModel.Symmetric, HyperbolicFitModel.UnevenBlend,
                HyperbolicFitModel.TiltedHyperbola, HyperbolicFitModel.SmoothBlend
            };
            Emit($"---- Per-model fit on the FULL (un-pruned) data + each model's would-be outliers (budget {analysisBudget}){floorTag} ----");
            Emit("model            | params | sigma(focus) | redChi^2 | R^2     | minPos  | would-reject");
            var perModelOutliers = new Dictionary<HyperbolicFitModel, List<int>>();
            foreach (var m in candidates) {
                var full = regular.Select(Clone).ToList();
                var fit = AlglibHyperbolicFitting.Create(alglib, m, full, stepSize, weighted);
                bool ok = fit.Solve();
                var outliers = ComputeModelOutliers(alglib, m, regular, analysisBudget, confidence, weighted, stepSize, madFloor, semScale, semCounts);
                perModelOutliers[m] = outliers.Select(p => (int)Math.Round(p.X)).ToList();
                var rejStr = perModelOutliers[m].Count == 0 ? "(none)" : string.Join(" ", perModelOutliers[m]);
                Emit($"{m,-16} | {ParamCount(m),5}  | {(ok ? F(fit.MinimumStdError) : "FAILED"),11} | " +
                    $"{F(fit.ReducedChiSquared),7} | {F(fit.RSquared),6} | {F(fit.Minimum.X),7} | {rejStr}");
            }
            // Consensus = points flagged by EVERY candidate model.
            var consensus = perModelOutliers.Values.Aggregate((IEnumerable<int>)perModelOutliers.Values.First(),
                (acc, s) => acc.Intersect(s)).Distinct().ToList();
            Emit("");
            Emit($"Consensus outliers (rejected by ALL {candidates.Length} models) = {(consensus.Count == 0 ? "(none)" : string.Join(" ", consensus))}");
            Emit("  -> A point only some models reject is model-misfit (tilt signal), not a true outlier; consensus keeps it.");
            // Who wins after removing only the consensus set, ranked by variance alone — i.e. the consensus step
            // WITHOUT the F-test significance gate. Contrast with the production table above (which applies the gate).
            var cleaned = regular.Where(p => !consensus.Contains((int)Math.Round(p.X))).Select(Clone).ToList();
            HyperbolicFitModel? fairWinner = null; double bestErr = double.PositiveInfinity; double minX = rows.Min(r => (double)r.Position), maxX = rows.Max(r => (double)r.Position);
            foreach (var m in candidates) {
                var pts = cleaned.Select(Clone).ToList();
                var fit = AlglibHyperbolicFitting.Create(alglib, m, pts, stepSize, weighted);
                if (!fit.Solve()) continue;
                var mx = fit.Minimum.X;
                if (double.IsNaN(fit.MinimumStdError) || double.IsNaN(mx) || mx < minX || mx > maxX) continue;
                if (fit.MinimumStdError < bestErr) { bestErr = fit.MinimumStdError; fairWinner = m; }
            }
            Emit($"Pre-gate variance ranking (consensus-cleaned, by sigma(focus) only) = {(fairWinner?.ToString() ?? "n/a")} (sigma(focus)={F(bestErr)})");
            Emit("  -> This is the variance-only ranking BEFORE the F-test gate. The production winner (top table)");
            Emit("     additionally applies the nested F-test vs Symmetric, which unlocks the asymmetric models when");
            Emit("     they significantly improve the fit — so a genuine tilt is reported instead of lower-variance Symmetric.");
            Emit("");

            // ---- Detailed Grubbs breakdown: iteratively reject against the chosen model's own fit. ----
            // Mirror FitWithOutlierRejection: pick the model that wins at budget 0 (the unrejected best fit), then
            // fit -> RejectionTest(weighted) -> remove -> refit, printing the per-point internals each round.
            var seedPts = regular.Select(Clone).ToList();
            var winnerModel = AlglibHyperbolicFitting.SelectBestModel(alglib, seedPts, stepSize, weighted, 0, confidence, out _, out _,
                madFloor: madFloor, semScale: semScale, starCounts: semCounts);
            Emit($"---- Per-round Grubbs detail for the winning model ({winnerModel}){floorTag} ----");
            Emit("(A point is rejected when its z = |r - median(r)| / MAD(r) exceeds the Grubbs limit, where");
            Emit(" r = standardized residual = (Y - f(x)) / sigma. Absolute residual |Y - f| can be tiny and the");
            Emit(" point still trip the test, because the test ranks points RELATIVE to how well the others fit.)");
            Emit("");

            var working = regular.Select(Clone).ToList();
            // Family B's anchor for the printed trace, kept exactly as FitWithOutlierRejection keeps it: NaN
            // until round 1 has produced a scale, so round 1 is unfloored by construction here too.
            var firstRoundScale = double.NaN;
            for (int round = 1; round <= 3 && working.Count > 3; ++round) {
                var fit = AlglibHyperbolicFitting.Create(alglib, winnerModel, working, stepSize, weighted);
                if (!fit.Solve()) {
                    Emit($"Round {round}: fit failed to solve; stopping.");
                    break;
                }
                var weightsFn = AlglibHyperbolicFitting.BuildResidualWeights(working, weighted);

                // Replicate MathUtility.RejectionTest internals for transparency (and cross-check its verdict).
                var stdResid = working.Select(p => {
                    var resid = p.Y - fit.Fitting(p.X);
                    return weightsFn != null ? weightsFn(p.X) * resid : resid;
                }).ToArray();
                // Family R only, in the SAME place RejectionTest applies it — BEFORE the median/MAD. The printed
                // `stdResid r` column below stays UNSCALED so it means the same thing at every rung (and so the
                // `s = |r|*sqrt(N*)` tie on the SEM line is checkable against it); the sqrt(N*) factor a reader
                // needs to recompute z from it is the `Stars` column of af_fit_points.csv, at the same position.
                var rankResid = stdResid;
                if (semScale.IsRank) {
                    rankResid = new double[stdResid.Length];
                    for (int i = 0; i < stdResid.Length; ++i) {
                        rankResid[i] = stdResid[i] * Math.Sqrt(Math.Max(starCounts(working[i].X), 1.0));
                    }
                }
                var (rMedian, rMad) = rankResid.MedianMAD();
                double scale = rMad;
                if (scale <= 0.0 || double.IsNaN(scale)) {
                    var (_, sd) = MathNet.Numerics.Statistics.Statistics.MeanStandardDeviation(rankResid);
                    scale = sd;
                }
                // The SAME floor RejectionTest is about to apply, in the SAME place — after both degenerate-scale
                // guards. If this replication skipped it, the printed z would stop agreeing with the printed
                // verdict, and that agreement is this file's only internal cross-check.
                var roundFloor = madFloor.ResolveFloor(round == 1 ? double.NaN : firstRoundScale);
                if (roundFloor > 0.0 && scale > 0.0 && !double.IsNaN(scale)) {
                    scale = Math.Max(scale, roundFloor);
                }
                int n = working.Count;
                double pTail = (1.0 - confidence) / (2 * n);
                double t = StudentT.InvCDF(0.0, 1.0, n - 2, pTail);
                double t2 = t * t;
                double grubbLimit = (double)(n - 1) / Math.Sqrt(n) * Math.Sqrt(t2 / (t2 + n - 2));

                var rejected = MathUtility.RejectionTest(working, fit.Fitting, confidence, weightsFn, roundFloor,
                    semScale, semCounts, out var scaleUsed, out var semOfSelected);
                if (round == 1) {
                    firstRoundScale = scaleUsed;
                }

                // The point the test SELECTED (the argmax of z), which is the point the SEM criterion judges —
                // rejected or not. First-of-ties, exactly as RejectionTest's MaxBy resolves them.
                var zs = new double[working.Count];
                int selIndex = -1;
                double maxZ = double.NaN;
                for (int i = 0; i < working.Count; ++i) {
                    zs[i] = scale > 0 ? Math.Abs(rankResid[i] - rMedian) / scale : double.NaN;
                    if (scale > 0 && (selIndex < 0 || zs[i] > maxZ)) {
                        selIndex = i;
                        maxZ = zs[i];
                    }
                }

                Emit($"Round {round}: N={n}, fit minPos={F(fit.Minimum.X)}, redChi^2={F(fit.ReducedChiSquared)}, " +
                    $"Grubbs limit={grubbLimit:F3}, median(r)={rMedian:F4}, MAD(r)={scale:F4}");
                // Full precision, ALWAYS -- including at the control rung. The `MAD(r)={scale:F4}` field above is
                // printed AFTER the stdDev fallback and at 4 dp, so it reads 0.0000 on 8 of wave 13's 72 rounds
                // and `caboose` round 1's true scale of 1.48e-5 is unrecoverable from it. Wave 14 spent a scorer
                // on reconstructing every scale as |r - median| / z; this line is what stops the next wave paying
                // that again. It is SAFE at the control rung because RULE F14's V1 is a FIELD comparison over the
                // four budget rows (`score_affit_w14.py`'s RX_BUDGET), not a whole-file diff -- verified before
                // this line was made unconditional, because "it probably won't break the gate" is not a control.
                Emit($"  effective scale (full precision) = {scaleUsed.ToString("G17", CultureInfo.InvariantCulture)}" +
                    $"   [floor applied this round = {roundFloor.ToString("G17", CultureInfo.InvariantCulture)}]");
                // ASCII ONLY, and only when a SEM rung is live. Not style: a Unicode arrow printed elsewhere in
                // this harness reaches a REDIRECTED log as the single byte 0x1A, and a scorer written against the
                // source string then reports "could not look" on every log in the population. Nothing wave 16
                // prints for a scorer to read leaves ASCII.
                //
                // At rung N nothing is printed at all, which is what lets clause W1 be a BYTE comparison against
                // wave 14's control rung rather than only a field comparison.
                if (!semScale.IsNone) {
                    string absRSel = "NaN", sSel = "NaN", verdict = "N/A";
                    int nStarSel = -1; // no point could be selected (degenerate scale); NOT a star count of 0
                    if (selIndex >= 0) {
                        nStarSel = (int)Math.Round(starCounts(working[selIndex].X));
                        absRSel = Math.Abs(stdResid[selIndex]).ToString("G17", CultureInfo.InvariantCulture);
                        sSel = semOfSelected.ToString("G17", CultureInfo.InvariantCulture);
                        // KEPT: the rejection stood. SUPPRESSED: the Grubbs test cleared the limit and the veto
                        // took it back. N/A: the test found nothing, so there was nothing for a veto to do.
                        verdict = rejected != null ? "KEPT" : (maxZ >= grubbLimit ? "SUPPRESSED" : "N/A");
                    }
                    // Three independently produced quantities on one line — N* from the side map, |r| from this
                    // file's own replication, s from RejectionTest itself — so `s == |r| * sqrt(N*)` is a real
                    // cross-check and not the same number printed three times.
                    Emit($"  SEM detail   : spec={SemScaleArgs.Tag(semScale)}  N*(sel)={nStarSel}  " +
                        $"|r|(sel)={absRSel}  s={sSel}  verdict={verdict}");
                    if (semScale.IsRank) {
                        double spanMin = double.PositiveInfinity, spanMax = double.NegativeInfinity;
                        foreach (var p in working) {
                            var f = Math.Sqrt(Math.Max(starCounts(p.X), 1.0));
                            spanMin = Math.Min(spanMin, f);
                            spanMax = Math.Max(spanMax, f);
                        }
                        // A span of exactly 1.000 makes family R a no-op on this round, and the diagnostic that
                        // explains R's effect should not have to be reconstructed by a later wave.
                        Emit($"  SEM rank     : sqrt(N*) span this round = {spanMin.ToString("G17", CultureInfo.InvariantCulture)}" +
                            $"..{spanMax.ToString("G17", CultureInfo.InvariantCulture)}");
                    }
                }
                Emit("  pos    | medHFR  | sigma  | f(x)    | |Y-f|abs | stdResid r | z=|r-med|/MAD | over-limit?");
                for (int i = 0; i < working.Count; ++i) {
                    var p = working[i];
                    var f = fit.Fitting(p.X);
                    var absR = Math.Abs(p.Y - f);
                    var r = stdResid[i];
                    var z = zs[i];
                    var mark = z >= grubbLimit ? "  *** YES" : "";
                    Emit($"  {((int)Math.Round(p.X)),-6} | {p.Y,6:F3} | {p.ErrorY,6:F3} | {f,6:F3}  | {absR,7:F4}  | {r,10:F4} | {z,12:F3}  |{mark}");
                }
                if (rejected == null) {
                    Emit("  -> RejectionTest: NO outlier above the Grubbs limit. Stable; stopping.");
                    Emit("");
                    break;
                }
                Emit($"  -> RejectionTest rejects pos {(int)Math.Round(rejected.X)} " +
                    $"(medHFR={rejected.Y:F3}, |Y-f|={Math.Abs(rejected.Y - fit.Fitting(rejected.X)):F4} HFR). Removing and refitting.");
                Emit("");
                working.Remove(rejected);
            }

            var csvPath = Path.Combine(outDir, "af_fit_points.csv");
            WritePointsCsv(csvPath, rows);
            var sumPath = Path.Combine(outDir, "af_fit_summary.txt");
            File.WriteAllText(sumPath, sb.ToString());
            Console.WriteLine($"Wrote af_fit_points.csv, af_fit_summary.txt to {outDir}");
        }

        // Deserialize the original run's StarDetectorParams from any saved per-region detection-result JSON in the
        // run tree (initial/ or final/ or attempt*/). When paramsFrom is given, only JSONs whose path contains that
        // substring are considered (e.g. "initial" to pin the initial-HFR frame's region). Returns (null, reason).
        private static (StarDetectorParams, string) LoadOriginalDetectorParams(string afRun, string paramsFrom) {
            try {
                var jsons = Directory.EnumerateFiles(afRun, "*_star_detection_result.json", SearchOption.AllDirectories)
                    .Where(p => string.IsNullOrWhiteSpace(paramsFrom) || p.IndexOf(paramsFrom, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(p => p).ToList();
                foreach (var json in jsons) {
                    try {
                        var root = JObject.Parse(File.ReadAllText(json));
                        var dp = root["DetectorParams"];
                        if (dp == null) continue;
                        var parsed = dp.ToObject<StarDetectorParams>();
                        if (parsed != null) {
                            // StarDetectionRegion has no parameterless ctor + private setters, so ToObject silently
                            // leaves the default Full region. Rebuild it from the raw JSON so the ROI is honored.
                            var region = BuildRegion(dp["Region"]);
                            if (region != null) {
                                parsed.Region = region;
                            }
                            return (parsed, $"saved run params: {Path.GetFileName(json)}");
                        }
                    } catch (Exception ex) {
                        Logger.Warning($"Could not parse DetectorParams from {json}: {ex.Message}");
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"Searching for saved detection JSON failed: {ex.Message}");
            }
            return (null, null);
        }

        // Rebuilds a StarDetectionRegion from the saved JSON's "Region" token (OuterBoundary + optional
        // InnerCropBoundary, each {StartX,StartY,Width,Height}). Returns null if the token is absent/unparseable
        // or already describes the full frame.
        private static StarDetectionRegion BuildRegion(JToken regionToken) {
            if (regionToken == null || regionToken.Type != JTokenType.Object) return null;
            var outer = BuildRatioRect(regionToken["OuterBoundary"]);
            if (outer == null) return null;
            var inner = BuildRatioRect(regionToken["InnerCropBoundary"]);
            int index = regionToken["Index"]?.Value<int>() ?? 0;
            return inner == null ? new StarDetectionRegion(outer, index) : new StarDetectionRegion(outer, inner, index);
        }

        private static RatioRect BuildRatioRect(JToken t) {
            if (t == null || t.Type != JTokenType.Object) return null;
            double sx = t["StartX"]?.Value<double>() ?? 0.0;
            double sy = t["StartY"]?.Value<double>() ?? 0.0;
            double w = t["Width"]?.Value<double>() ?? 1.0;
            double h = t["Height"]?.Value<double>() ?? 1.0;
            return new RatioRect(sx, sy, w, h);
        }

        private static int EstimateStepSize(List<Frame> frames, IProfileService profileService) {
            var positions = frames.Select(f => f.FocuserPosition).Distinct().OrderBy(p => p).ToList();
            if (positions.Count >= 2) {
                var diffs = new List<int>();
                for (int i = 1; i < positions.Count; ++i) diffs.Add(positions[i] - positions[i - 1]);
                diffs.Sort();
                return Math.Max(1, diffs[diffs.Count / 2]); // median spacing
            }
            return 1;
        }

        private static ScatterErrorPoint Clone(ScatterErrorPoint p) => new ScatterErrorPoint(p.X, p.Y, p.ErrorX, p.ErrorY);

        // Symmetric is the 4-parameter base; the asymmetric models each add one parameter (all nest Symmetric).
        private static int ParamCount(HyperbolicFitModel m) => m == HyperbolicFitModel.Symmetric ? 4 : 5;

        // Replicates the production per-model iterative rejection (fit -> weighted Grubbs -> remove -> refit), up to
        // maxRej points, returning the points THIS model would reject. Mirrors AlglibHyperbolicFitting's private
        // FitWithOutlierRejection so the diagnostic can show each model's set independently.
        private static List<ScatterErrorPoint> ComputeModelOutliers(
                AlglibAPI alglib, HyperbolicFitModel model, List<ScatterErrorPoint> points,
                int maxRej, double confidence, bool weighted, int stepSize, MadFloorSpec madFloor,
                SemScaleSpec semScale, Func<double, double> starCounts) {
            var working = points.Select(Clone).ToList();
            var rejected = new List<ScatterErrorPoint>();
            var firstRoundScale = double.NaN; // family B anchors per MODEL, exactly as FitWithOutlierRejection does
            for (int i = 0; i < maxRej && working.Count > 3; ++i) {
                var fit = AlglibHyperbolicFitting.Create(alglib, model, working, stepSize, weighted);
                if (!fit.Solve()) break;
                var w = AlglibHyperbolicFitting.BuildResidualWeights(working, weighted);
                var roundFloor = madFloor.ResolveFloor(i == 0 ? double.NaN : firstRoundScale);
                // The per-model view runs at the SAME rung as everything else in the report. A rung that applied
                // to the production table but not to this one would put two mechanisms in one file.
                var r = MathUtility.RejectionTest(working, fit.Fitting, confidence, w, roundFloor,
                    semScale, starCounts, out var scaleUsed, out _);
                if (i == 0) {
                    firstRoundScale = scaleUsed;
                }
                if (r == null) break;
                rejected.Add(r);
                working.Remove(r);
            }
            return rejected;
        }

        private static List<Frame> ParseAfRun(string dir) {
            var root = new DirectoryInfo(dir);
            var search = root;
            if (!root.GetFiles().Any(f => ImageFileRegex.IsMatch(Path.GetFileNameWithoutExtension(f.Name)))) {
                var attempt = root.EnumerateDirectories("attempt*").FirstOrDefault();
                if (attempt != null) search = attempt;
            }
            var frames = new List<Frame>();
            foreach (var file in search.GetFiles()) {
                var m = ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups["FOCUSER"].Value, out var pos)) continue;
                frames.Add(new Frame { Path = file.FullName, FocuserPosition = pos });
            }
            return frames;
        }

        private static void WritePointsCsv(string path, List<PointRow> rows) {
            var sb = new StringBuilder();
            sb.AppendLine("FocuserPosition,Stars,MedianHFR,Sigma_1483MAD,RegularizedSigma");
            foreach (var r in rows.OrderBy(r => r.Position)) {
                sb.AppendLine(string.Join(",", r.Position, r.Stars,
                    r.MedianHFR.ToString("G9", CultureInfo.InvariantCulture),
                    r.Sigma.ToString("G9", CultureInfo.InvariantCulture),
                    r.RegSigma.ToString("G9", CultureInfo.InvariantCulture)));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static double ParseDouble(string s, double dflt)
            => string.IsNullOrWhiteSpace(s) ? dflt : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static bool ParseBool(string s, bool dflt)
            => string.IsNullOrWhiteSpace(s) ? dflt : bool.Parse(s);

        private static string F(double? v) {
            if (v == null || double.IsNaN(v.Value)) return "NaN";
            return v.Value.ToString("G6", CultureInfo.InvariantCulture);
        }
    }
}
