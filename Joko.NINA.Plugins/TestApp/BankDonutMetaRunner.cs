#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Utility;
using Newtonsoft.Json;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Step 3 of the AF-bank verification: per run, decide whether donut-aware detection is warranted and persist
    /// <c>run_meta.json</c> beside the frames. The decision is the pure-logic, unit-tested
    /// <see cref="DonutHeuristic.Decide"/> (require BOTH a high donut/peak fraction AND heavy extreme-frame
    /// defocus — the cwhite over-flag fix). The signals are measured by running real HocusFocus detection (donut
    /// detection ON) on the two EXTREME (min/max focuser) frames:
    ///   - extremeFrameMedianHFR     = median HFR of accepted stars on the extreme frames (heavy-defocus signal);
    ///   - donutPeakFracMax          = max over the extreme frames of (large/bloated stars)/(compact stars)
    ///     (a donut-domination ratio that exceeds 1 when defocused disks dominate compact cores);
    ///   - extremeDonutBBoxMedianPx  = median bounding size (≈2·HFR) of the large stars (donut disk size).
    /// Idempotent: skips runs whose run_meta.json exists unless <c>--refresh</c>. Read-only on the profile.
    /// </summary>
    public static class BankDonutMetaRunner {

        /// <summary>HFR (px) above which an accepted star is treated as "large/bloated" (a defocus proxy).</summary>
        private const double LargeStarHFR = 6.0;

        private class RunMeta {
            public bool donutAware { get; set; }
            public string reason { get; set; }
            public int[] extremeFocusers { get; set; }
            public DonutSignal donutSignal { get; set; }
            public string detectedAtUtc { get; set; }
        }

        private class DonutSignal {
            public double donutPeakFracMax { get; set; }
            public double extremeFrameMedianHFR { get; set; }
            public double extremeDonutBBoxMedianPx { get; set; }
        }

        public static async Task Run(string[] args) {
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runs) || !Directory.Exists(runs)) {
                Console.Error.WriteLine("Usage: TestApp bank-donut-meta --runs <bank-root> [--profile-id <guid>] [--refresh]");
                Environment.ExitCode = 2;
                return;
            }
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var refresh = DiagnosticUtil.HasFlag(args, "--refresh");
            Logger.SetLogLevel(LogLevelEnum.INFO);
            if (Application.Current == null) {
                new Application();
            }
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            if (profileService.ActiveProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");
            }
            // Detector settings come from the harness's LOCAL settings file, not the NINA profile: a
            // profile-sourced value is mutable machine state nothing records, and the ACTIVE profile can
            // even be a different telescope between runs. See HarnessSettingsStore.
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, profileService.ActiveProfile);
            var starDetectionOptions = new StarDetectionOptions(profileService, harnessSettings.Accessor);

            // Donut detection ON so the extreme frames' bloated/donut stars are actually measured.
            var detectorParams = HocusFocusStarDetection.BuildDefaultStarDetectorParams();
            detectorParams.ModelPSF = false;
            detectorParams.DefocusAwareDonutDetection = true;
            detectorParams.DefocusAwareDistortion = true;
            detectorParams.DefocusAwareCentering = true;
            var detector = new StarDetector(new AlglibAPI());

            var discovery = OptimizationRunDiscovery.Discover(runs);
            Console.WriteLine($"bank-donut-meta: {discovery.Runs.Count} run(s) under {runs}  ({(refresh ? "refresh" : "skip-existing")})");
            int flagged = 0, processed = 0, skipped = 0;
            foreach (var run in discovery.Runs) {
                var runFolder = Path.GetDirectoryName(run.Frames[0].Path);
                var metaPath = Path.Combine(runFolder, "run_meta.json");
                if (!refresh && File.Exists(metaPath)) {
                    skipped++;
                    var existing = TryRead(metaPath);
                    Console.WriteLine($"  [{run.RunId}] cached: donutAware={existing?.donutAware}");
                    if (existing?.donutAware == true) flagged++;
                    continue;
                }

                var ordered = run.Frames.OrderBy(f => f.FocuserPosition).ToList();
                var extremes = new[] { ordered.First(), ordered.Last() }.Distinct().ToList();
                double fracMax = 0, hfrAccum = 0; int hfrFrames = 0;
                var largeBboxes = new List<double>();
                foreach (var frame in extremes) {
                    try {
                        // IRenderedImage route: the donut heuristic is measured on the image the live app detects on.
                        var rendered = await DiagnosticUtil.LoadRenderedImage(frame.Path, profileService);
                        var result = await detector.Detect(rendered, detectorParams, progress: null, CancellationToken.None);
                        var hfrs = result.DetectedStars.Select(s => HocusFocusStarDetection.ToDetectedStar(s).HFR)
                            .Where(h => h > 0 && double.IsFinite(h)).ToList();
                        if (hfrs.Count == 0) { continue; }
                        var large = hfrs.Count(h => h >= LargeStarHFR);
                        var compact = Math.Max(1, hfrs.Count - large);
                        fracMax = Math.Max(fracMax, (double)large / compact);
                        hfrAccum += Median(hfrs); hfrFrames++;
                        largeBboxes.AddRange(hfrs.Where(h => h >= LargeStarHFR).Select(h => 2.0 * h));
                    } catch (Exception ex) {
                        Console.WriteLine($"      Focuser {frame.FocuserPosition}: detect FAILED ({ex.GetType().Name}: {ex.Message})");
                    }
                }
                var signal = new DonutSignal {
                    donutPeakFracMax = fracMax,
                    extremeFrameMedianHFR = hfrFrames > 0 ? hfrAccum / hfrFrames : 0,
                    extremeDonutBBoxMedianPx = largeBboxes.Count > 0 ? Median(largeBboxes) : 0
                };
                var decision = DonutHeuristic.Decide(new DonutHeuristic.Signals {
                    DonutPeakFracMax = signal.donutPeakFracMax,
                    ExtremeFrameMedianHFR = signal.extremeFrameMedianHFR,
                    ExtremeDonutBBoxMedianPx = signal.extremeDonutBBoxMedianPx
                });
                var meta = new RunMeta {
                    donutAware = decision.DonutAware,
                    reason = decision.Reason,
                    extremeFocusers = extremes.Select(e => e.FocuserPosition).ToArray(),
                    donutSignal = signal,
                    detectedAtUtc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture)
                };
                File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, Formatting.Indented));
                processed++;
                if (decision.DonutAware) flagged++;
                Console.WriteLine($"  [{run.RunId}] donutAware={decision.DonutAware}  frac={signal.donutPeakFracMax:F2} " +
                    $"extremeHFR={signal.extremeFrameMedianHFR:F1} bbox={signal.extremeDonutBBoxMedianPx:F0}px  ({decision.Reason})");
            }
            Console.WriteLine($"bank-donut-meta: {processed} processed, {skipped} cached, {flagged} donutAware.");
        }

        private static RunMeta TryRead(string path) {
            try { return JsonConvert.DeserializeObject<RunMeta>(File.ReadAllText(path)); } catch { return null; }
        }

        private static double Median(List<double> xs) {
            if (xs.Count == 0) return 0;
            var s = xs.OrderBy(x => x).ToList();
            int m = s.Count / 2;
            return s.Count % 2 == 1 ? s[m] : 0.5 * (s[m - 1] + s[m]);
        }
    }
}
