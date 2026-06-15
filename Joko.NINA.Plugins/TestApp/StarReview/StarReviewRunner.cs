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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Logger = NINA.Core.Utility.Logger;
using Rect = OpenCvSharp.Rect;

namespace TestApp.StarReview {

    /// <summary>
    /// CLI + setup for the interactive two-pass star "review" labeling tool (T7). Mirrors the headless T6
    /// <c>OptimizationDiagnosticRunner</c> for run/frame discovery, profile + seed-param construction, and the
    /// float-Mat load path, but instead of optimizing it launches a WPF window where a human marks false
    /// negatives ("missed") and false positives ("should-reject"), then writes labels in the EXACT JSON shape
    /// T6's <c>--labels</c> reader consumes. The loop is: <c>optimize</c> → <c>review --labels L</c> →
    /// <c>optimize --labels L</c>; this tool only produces labels.
    /// </summary>
    internal static class StarReviewRunner {

        // Same saved-frame filename pattern as T6 / the AF engine: "0_Frame1_BitDepth16_Bayered0_Focuser5000.fits".
        private static readonly Regex ImageFileRegex = new Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static void Run(string[] args) {
            try {
                RunImpl(args);
            } catch (Exception ex) {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                Logger.Error(ex, "Star review run failed");
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

            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");

            // Default labels dir alongside the runs so the same path feeds `optimize --labels`.
            var labelsDir = DiagnosticUtil.GetArg(args, "--labels");
            if (string.IsNullOrWhiteSpace(labelsDir)) {
                labelsDir = Path.Combine(runsDir, "labels");
            }

            var paramsArg = (DiagnosticUtil.GetArg(args, "--params") ?? "current").Trim().ToLowerInvariant();
            bool useOptimized;
            switch (paramsArg) {
                case "current": useOptimized = false; break;
                case "optimized": useOptimized = true; break;
                default: throw new ArgumentException($"--params: '{paramsArg}' must be 'current' or 'optimized'");
            }

            // Optional explicit folder holding an optimized_settings.json (the optimizer's --out subdir). When
            // omitted, --params optimized auto-discovers <runFolder>/optimized_settings.json, then the profile.
            var optResultsDir = DiagnosticUtil.GetArg(args, "--opt-results");

            var mode = StarReviewQueue.ParseMode(DiagnosticUtil.GetArg(args, "--review"));

            Logger.SetLogLevel(LogLevelEnum.TRACE);

            // The real ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a WPF
            // Application must exist before the profile loads (mirrors T6 / the contamination runner).
            if (Application.Current == null) {
                new Application();
            }

            Console.WriteLine($"Runs root: {runsDir}");
            Console.WriteLine($"Labels dir: {labelsDir}");
            Console.WriteLine($"Params: {paramsArg}; review queue: {mode}");

            // Load the user's real profile + star detection options (single source of truth for params + PixelScale).
            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile;
            if (activeProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA at least once to create a profile.");
            }
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");

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
            Console.WriteLine($"Discovered {discovered.Count} run(s):");
            foreach (var d in discovered) {
                Console.WriteLine($"  {d.RunId}: {d.Frames.Count} frames");
            }

            // The first discovered run's source folder (the directory holding its frames) is where the optimizer
            // writes optimized_settings.json — used by the --params optimized auto-discovery path.
            var firstFramePath = discovered[0].Frames.FirstOrDefault()?.Path;
            var firstRunFolder = string.IsNullOrEmpty(firstFramePath) ? null : Path.GetDirectoryName(firstFramePath);

            var detectionParams = BuildDetectionParams(starDetectionOptions, activeProfile, useOptimized, optResultsDir, firstRunFolder);
            Console.WriteLine($"Seed params: Sensitivity={detectionParams.Sensitivity.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"StructureLayers={detectionParams.StructureLayers}, PixelScale={detectionParams.PixelScale.ToString("G6", CultureInfo.InvariantCulture)}");

            // Detect every frame once (off the UI thread — this is still the CLI thread) to get accepted counts +
            // the accepted/rejected overlay, then assemble the review queue. Detection is heavy; doing it up front
            // keeps the window responsive and lets the queue selection use real counts.
            var detector = new StarDetector(new AlglibAPI());
            var allFrames = new List<StarReviewFrame>();
            var detections = new Dictionary<(string runId, int focuser), FrameReview>();

            foreach (var d in discovered) {
                foreach (var frame in d.Frames) {
                    using var mat = LoadFloatMatSync(frame.Path, profileService);
                    using var clone = mat.Clone(); // Detect mutates its input in place.
                    var result = detector.Detect(clone, detectionParams, null, CancellationToken.None).GetAwaiter().GetResult();
                    var accepted = result.DetectedStars ?? new List<Star>();
                    var review = new FrameReview {
                        RunId = d.RunId,
                        FocuserPosition = frame.FocuserPosition,
                        FramePath = frame.Path,
                        // Capture each accepted star's REAL StarBoundingBox so the overlay draws actual-size boxes and
                        // the should-reject click records the actual bounds.
                        Accepted = accepted.Select(s => (s.Center.X, s.Center.Y, s.HFR, s.StarBoundingBox)).ToList(),
                        Rejected = ExtractRejected(result),
                    };
                    detections[(d.RunId, frame.FocuserPosition)] = review;
                    allFrames.Add(new StarReviewFrame {
                        RunId = d.RunId,
                        FocuserPosition = frame.FocuserPosition,
                        FramePath = frame.Path,
                        AcceptedCount = accepted.Count
                    });
                    Console.WriteLine($"  {d.RunId} @ {frame.FocuserPosition}: {accepted.Count} accepted");
                }
            }

            StarReviewQueue.MarkExtremes(allFrames);
            var queue = StarReviewQueue.Build(allFrames, mode);
            if (queue.Count == 0) {
                Console.WriteLine($"Review queue is empty for mode '{mode}' (N_review = {StarReviewQueue.NReview}). Try --review all.");
                return;
            }
            Console.WriteLine($"Review queue: {queue.Count} frame(s) (N_review = {StarReviewQueue.NReview})");

            // Load existing labels per run (incrementally — re-running merges/extends prior labels).
            var labelsByRun = new Dictionary<string, StarReviewRunLabels>(StringComparer.Ordinal);
            foreach (var runId in queue.Select(q => q.RunId).Distinct(StringComparer.Ordinal)) {
                var labels = StarReviewLabelStore.Load(labelsDir, runId, out var loadError);
                if (loadError != null) {
                    Console.WriteLine($"WARNING: {loadError}");
                    Logger.Warning(loadError);
                }
                labelsByRun[runId] = labels;
            }

            // Build the per-frame review payloads in queue order, joining each to its detection + run labels. All
            // of the above (discovery, Mat loads, detection) is plain off-UI work; only the WPF VM + window below
            // require an STA thread, so they are constructed and shown on a dedicated one (see ShowReviewWindowSta).
            var reviewFrames = queue.Select(q => detections[(q.RunId, q.FocuserPosition)]).ToList();

            Console.WriteLine("Opening review window. Mark Missed (false negatives) and Should-Reject (false positives), then Save.");
            ShowReviewWindowSta(reviewFrames, labelsByRun, labelsDir, profileService);

            Console.WriteLine($"Labels written to {labelsDir}");
            Console.WriteLine($"Next: TestApp optimize --runs \"{runsDir}\" --labels \"{labelsDir}\"");
        }

        /// <summary>
        /// Constructs the WPF <see cref="StarReviewVM"/> + <see cref="StarReviewWindow"/> and runs the modal dialog
        /// on a dedicated STA thread, then joins it. This is required because <see cref="RunImpl"/> performs async
        /// loading/detection (via blocking <c>GetAwaiter().GetResult()</c> calls and an async <c>Main</c>) before
        /// reaching the UI; after any await the runner is no longer guaranteed to be on the process's [STAThread]
        /// main thread, and a WPF <see cref="Window"/> can only be created on an STA thread. Creating a fresh STA
        /// thread here makes window construction correct regardless of the caller's apartment.
        ///
        /// <para>Every WPF object (VM, window, the property-changed notifications, and the BitmapSource the VM builds
        /// off-thread and then marshals back) is created/touched on THIS thread. A
        /// <see cref="DispatcherSynchronizationContext"/> is installed first so the VM's <c>ConfigureAwait(true)</c>
        /// image-load continuation posts back onto this thread's dispatcher (pumped by <c>ShowDialog</c>) rather than
        /// onto an arbitrary thread-pool thread. Any exception is captured and rethrown on the calling thread so the
        /// runner's catch/logging (and the process exit code) still report failures.</para>
        /// </summary>
        private static void ShowReviewWindowSta(
            List<FrameReview> reviewFrames,
            Dictionary<string, StarReviewRunLabels> labelsByRun,
            string labelsDir,
            IProfileService profileService) {
            Exception uiError = null;
            var thread = new Thread(() => {
                try {
                    // A WPF Application already exists (created on the runner thread so the profile load could write
                    // to Application.Current.Resources); only one Application may exist per process, so do NOT make a
                    // second one. Instead install a dispatcher SynchronizationContext on THIS STA thread so awaited
                    // continuations (the VM's image-load) resume here and are pumped by ShowDialog.
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                    var vm = new StarReviewVM(reviewFrames, labelsByRun, labelsDir, profileService);
                    var window = new StarReviewWindow();
                    window.DataContext = vm;
                    vm.AttachWindow(window);

                    window.ShowDialog();

                    // The VM saves on navigate + on close; do a final flush for safety (on this UI thread).
                    vm.SaveAll();
                } catch (Exception ex) {
                    uiError = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = false;
            thread.Start();
            thread.Join();

            if (uiError != null) {
                // Surface to RunImpl's caller (Run) so existing error logging + exit code 1 still apply.
                throw uiError;
            }
        }

        private static void PrintUsage() {
            Console.Error.WriteLine("Usage: TestApp review --runs <folder> [--labels <dir>] [--params current|optimized] [--opt-results <dir>] [--review low|uncertain|all] [--profile-id <guid>]");
            Console.Error.WriteLine("  --runs        (required) folder of saved AF runs (same discovery as `optimize`).");
            Console.Error.WriteLine("  --labels      (default <runs>/labels) where label JSON is read/written; feeds `optimize --labels`.");
            Console.Error.WriteLine("  --params      (default current) which detector params drive the overlay: 'current' = profile/preset seed, 'optimized' = an optimized snapshot.");
            Console.Error.WriteLine("  --opt-results (optional) folder holding optimized_settings.json (the optimizer's --out subdir). With --params optimized,");
            Console.Error.WriteLine("                resolution order is: this folder, then <runFolder>/optimized_settings.json, then the profile snapshot, then current.");
            Console.Error.WriteLine("  --review      (default low) which frames to queue: 'low' (< N_review accepted), 'uncertain' (low + defocused extremes), or 'all'.");
            Console.Error.WriteLine("  --profile-id  (default active) NINA profile id to load.");
        }

        // ---- Params construction (current vs optimized) ----------------------------------------------------

        /// <summary>
        /// Builds the <see cref="StarDetectorParams"/> for the overlay. <paramref name="useOptimized"/> selects
        /// between the profile-as-configured seed ("current") and the saved optimized snapshot ("optimized").
        /// Mirrors T6's seed assembly (BuildStarDetectorParams + PixelScale + Region.Full + ModelPSF=false).
        ///
        /// <para>This is STRICTLY READ-ONLY with respect to the live <paramref name="options"/>: like the headless
        /// T6 optimizer, the review tool never calls a setter on the persisted <see cref="StarDetectionOptions"/>
        /// (and never touches the underlying options accessor). That matters because the review window is
        /// long-lived and NINA auto-saves the active profile on a debounced timer — mutating the options here (as
        /// the old <c>ApplyOptimizedSettings(...)</c> / <c>UseOptimizedSettings = false</c> path did) would
        /// silently rewrite the user's on-disk settings. So both branches READ from the options and build the
        /// params locally instead.</para>
        /// </summary>
        internal static StarDetectorParams BuildDetectionParams(
            StarDetectionOptions options, IProfile activeProfile, bool useOptimized, string optResultsDir, string runFolder) {
            // "current" = the detector exactly as the profile is currently configured (read-only and honest:
            // whatever UseOptimizedSettings / Simple / Advanced state the profile is already in, we report it
            // unchanged rather than toggling anything).
            var p = BuildBaseParams(options, activeProfile);
            if (!useOptimized) {
                return p;
            }

            // Resolve the optimized snapshot in priority order:
            //   (a) explicit --opt-results <dir>/optimized_settings.json,
            //   (b) auto: <runFolder>/optimized_settings.json (the focus run's own folder, where the optimizer wrote it),
            //   (c) the profile's saved snapshot (options.GetOptimizedSettings()),
            //   (d) fall back to "current" with a warning.
            var snapshot = ResolveOptimizedSnapshot(options, optResultsDir, runFolder, out var source);
            if (snapshot == null) {
                Console.WriteLine("WARNING: --params optimized requested but no optimized snapshot was found " +
                    "(--opt-results, <runFolder>/optimized_settings.json, or the profile); falling back to current.");
                Logger.Warning("--params optimized requested but no optimized snapshot exists; using current params");
                return p;
            }
            Console.WriteLine($"Optimized snapshot source: {source}");

            OverlaySnapshot(p, snapshot);
            return p;
        }

        /// <summary>
        /// Resolves the optimized snapshot to overlay for --params optimized, in priority order: an explicit
        /// --opt-results folder, then the focus run's own folder (auto-discovery), then the profile snapshot. Returns
        /// null when none is found (caller falls back to "current"). File loads are tolerant — a missing/corrupt file
        /// logs a warning and the resolution continues to the next source. STRICTLY READ-ONLY on the options.
        /// </summary>
        private static OptimizedStarDetectionSettings ResolveOptimizedSnapshot(
            StarDetectionOptions options, string optResultsDir, string runFolder, out string source) {
            // (a) Explicit --opt-results directory.
            if (!string.IsNullOrWhiteSpace(optResultsDir)) {
                var explicitPath = Path.Combine(optResultsDir, "optimized_settings.json");
                var loaded = TryLoadSnapshot(explicitPath);
                if (loaded != null) {
                    source = $"--opt-results ({explicitPath})";
                    return loaded;
                }
                Console.WriteLine($"WARNING: --opt-results given but no usable optimized_settings.json at {explicitPath}; trying other sources.");
            }

            // (b) Auto-discovery in the focus run's own folder.
            if (!string.IsNullOrWhiteSpace(runFolder)) {
                var autoPath = Path.Combine(runFolder, "optimized_settings.json");
                var loaded = TryLoadSnapshot(autoPath);
                if (loaded != null) {
                    source = $"auto ({autoPath})";
                    return loaded;
                }
            }

            // (c) The profile's saved snapshot.
            var profileSnapshot = options.GetOptimizedSettings();
            if (profileSnapshot != null) {
                source = "profile (GetOptimizedSettings)";
                return profileSnapshot;
            }

            source = "(none)";
            return null;
        }

        /// <summary>Loads an <see cref="OptimizedStarDetectionSettings"/> from a JSON file, or null if it is missing
        /// or unparseable (a corrupt file logs a warning and is treated as absent).</summary>
        private static OptimizedStarDetectionSettings TryLoadSnapshot(string path) {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return null;
            }
            try {
                return JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(File.ReadAllText(path));
            } catch (Exception ex) {
                Console.WriteLine($"WARNING: could not parse optimized snapshot '{path}': {ex.Message}");
                Logger.Warning($"Could not parse optimized snapshot '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Overlays the 12 curated snapshot knobs onto <paramref name="p"/> LOCALLY (no ApplyOptimizedSettings, which
        /// would persist through the options accessor). Shared by the file (--opt-results / auto) and profile paths so
        /// the mapping lives in one place. Mapping mirrors HocusFocusStarDetection.BuildStarDetectorParams (snapshot
        /// field names match the matching StarDetectionOptions property names) and OptimizedStarDetectionSettings.FromParams.
        /// </summary>
        private static void OverlaySnapshot(StarDetectorParams p, OptimizedStarDetectionSettings snapshot) {
            p.Sensitivity = snapshot.BrightnessSensitivity;
            p.StarClippingMultiplier = snapshot.StarClippingMultiplier;
            p.NoiseClippingMultiplier = snapshot.NoiseClippingMultiplier;
            p.PeakResponse = snapshot.StarPeakResponse;
            p.MaxDistortion = snapshot.MaxDistortion;
            p.MinHFR = snapshot.MinHFR;
            p.StarCenterTolerance = snapshot.StarCenterTolerance;
            p.StructureLayers = snapshot.StructureLayers;
            p.NoiseReductionRadius = snapshot.NoiseReductionRadius;
            p.MinimumStarBoundingBoxSize = snapshot.MinStarBoundingBoxSize;
            p.HotpixelThresholdingEnabled = snapshot.HotpixelThresholdingEnabled;
            p.HotpixelThreshold = snapshot.HotpixelThreshold;
        }

        /// <summary>
        /// Builds the base detection params from the live options (READ-ONLY) plus the AF overrides T6's seed
        /// applies (PixelScale from the profile × binning=1 for raw Mats, Region=Full, ModelPSF=false, no
        /// intermediate-save path), so "current" here matches T6's seed exactly. Never writes to the options.
        /// </summary>
        private static StarDetectorParams BuildBaseParams(StarDetectionOptions options, IProfile activeProfile) {
            var p = HocusFocusStarDetection.BuildStarDetectorParams(options);

            const int binning = 1;
            var pixelScale = MathUtility.ArcsecPerPixel(activeProfile.CameraSettings.PixelSize, activeProfile.TelescopeSettings.FocalLength) * binning;
            p.PixelScale = pixelScale;
            p.Region = StarDetectionRegion.Full;
            p.ModelPSF = false;
            p.SaveIntermediateFilesPath = string.Empty;
            return p;
        }

        // ---- Rejected-candidate overlay extraction ---------------------------------------------------------

        /// <summary>
        /// Flattens the per-reason rejection bounds from <see cref="HocusFocusStarDetectorResult.Metrics"/> into a
        /// flat list of (reason, rect) tuples, using the SAME reason set as T6's annotated PNG so the colors line
        /// up between the two tools.
        /// </summary>
        internal static List<(string Reason, Rect Bounds)> ExtractRejected(HocusFocusStarDetectorResult result) {
            var list = new List<(string, Rect)>();
            var m = result.Metrics;
            if (m == null) {
                return list;
            }
            void Add(string reason, List<Rect> rects) {
                if (rects == null) {
                    return;
                }
                foreach (var r in rects) {
                    list.Add((reason, r));
                }
            }
            Add("TooDistorted", m.TooDistortedBounds);
            Add("Degenerate", m.DegenerateBounds);
            Add("Saturated", m.SaturatedBounds);
            Add("LowSensitivity", m.LowSensitivityBounds);
            Add("NotCentered", m.NotCenteredBounds);
            Add("TooFlat", m.TooFlatBounds);
            Add("Contaminated", m.ContaminatedBounds);
            return list;
        }

        private static Mat LoadFloatMatSync(string path, IProfileService profileService) {
            return DiagnosticUtil.LoadFloatMat(path, profileService).GetAwaiter().GetResult();
        }

        // ---- Run / frame discovery (mirrors T6 OptimizationDiagnosticRunner) -------------------------------

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
                var match = ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
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
