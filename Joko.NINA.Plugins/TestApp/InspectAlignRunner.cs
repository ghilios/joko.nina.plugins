#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.Interfaces;
using Newtonsoft.Json;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
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
using DrawingSize = System.Drawing.Size;
using Logger = NINA.Core.Utility.Logger;

namespace TestApp {

    /// <summary>
    /// Headless reproducer for the Aberration Inspector's RANSAC frame alignment. Detects stars in each focus-run
    /// frame using the inspector's region-6 path (the user's real profile params, ModelPSF off like the AF engine),
    /// then drives the SAME SensorModel.RegisterStarsAndFit seam the inspector uses and reports, per frame: detected
    /// star counts, which frame becomes the alignment reference (= max stars), the reference vs non-reference
    /// triangle counts, how many frames aligned, and every registration warning the inspector would surface. Lets
    /// the "too few star triangles" / "N frames failed to align" warnings be reproduced and a fix verified offline,
    /// without launching NINA. Read-only on the profile.
    /// </summary>
    public static class InspectAlignRunner {

        public static async Task Run(string[] args) {
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runs) || !Directory.Exists(runs)) {
                Console.Error.WriteLine("Usage: TestApp inspect-align --runs <folder-with-Focuser-frames> [--params current|default] [--profile-id <guid>] [--opt-results <dir>] [--noise-clip <v>] [--out <dir>]");
                Environment.ExitCode = 2;
                return;
            }
            // --params default evaluates the sensor model at the SHIPPED detector defaults
            // (BuildDefaultStarDetectorParams, incl. NoiseClippingMultiplier=2.0), independent of the live profile —
            // the C0 "as-default" reference in the AF-bank verification. Mirrors golden eval's --params selector.
            var paramsMode = (DiagnosticUtil.GetArg(args, "--params") ?? "current").Trim().ToLowerInvariant();
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var outDir = DiagnosticUtil.GetArg(args, "--out");
            if (string.IsNullOrWhiteSpace(outDir)) {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                outDir = Path.Combine(localAppData, "NINA", "Logs", "hf-diag", "inspect-align", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }
            Directory.CreateDirectory(outDir);
            Logger.SetLogLevel(LogLevelEnum.INFO);

            // ProfileService.ActiveProfile setter writes to Application.Current.Resources, so a WPF Application
            // must exist or it NREs.
            if (Application.Current == null) {
                new Application();
            }

            // Discover the focus-run frames: any *.fits/*.xisf with a Focuser<NNNN> token, recursively.
            var frameFiles = Directory.EnumerateFiles(runs, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".fits", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xisf", StringComparison.OrdinalIgnoreCase))
                            && Regex.IsMatch(Path.GetFileName(f), "Focuser\\d+", RegexOptions.IgnoreCase))
                .Select(f => (path: f, focuser: int.Parse(Regex.Match(Path.GetFileName(f), "Focuser(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture)))
                .OrderBy(x => x.focuser)
                .ToList();
            if (frameFiles.Count < 3) {
                Console.Error.WriteLine($"Found only {frameFiles.Count} Focuser frames under {runs}; need >= 3.");
                Environment.ExitCode = 2;
                return;
            }
            Console.WriteLine($"Frames: {frameFiles.Count} ({string.Join(", ", frameFiles.Select(f => f.focuser))})");

            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            var activeProfile = profileService.ActiveProfile
                ?? throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id.");
            Console.WriteLine($"Profile: {activeProfile.Name} ({activeProfile.Id})");

            // Detector settings come from the harness's LOCAL settings file, not the NINA profile: a
            // profile-sourced value is mutable machine state nothing records, and the ACTIVE profile can
            // even be a different telescope between runs. See HarnessSettingsStore.
            var harnessSettings = HarnessSettingsStore.Resolve(args, profileService, activeProfile);
            var starDetectionOptions = new StarDetectionOptions(profileService, harnessSettings.Accessor);
            var inspectorOptions = new InspectorOptions(profileService);
            // F58(d): the pinned settings FILE, not the active profile -- the detector was pinned one line above
            // and the FIT was not, which is the asymmetry F58 named in `optimize`.
            var autoFocusOptions = HarnessSettingsStore.BuildFitOptions(profileService, harnessSettings);
            Console.WriteLine($"FitInputs: {HarnessFitInputs.From(autoFocusOptions)}");

            // Detection params = the inspector's region-6 path: the single options->params source of truth, full
            // sensor (region 6 at SensorROI=1.0), ModelPSF OFF (the AF engine sets isAutoFocus=true). NumberOfAFStars
            // trim and contamination rejection follow the profile exactly (StarDetector.Detect applies them). PixelScale
            // is irrelevant to star positions/counts (it only feeds PSF), so leave the default.
            var detectorParams = paramsMode == "default"
                ? HocusFocusStarDetection.BuildDefaultStarDetectorParams()
                : HocusFocusStarDetection.BuildStarDetectorParams(starDetectionOptions);
            detectorParams.ModelPSF = false;
            // Optional settings injection so the sensor model can be evaluated at a chosen config (e.g. optimized
            // settings from `optimize`) instead of the live profile — mirrors golden eval's overlay.
            ApplySettingsOverrides(detectorParams, args);
            Console.WriteLine($"Detection: params={paramsMode}, NoiseClip={detectorParams.NoiseClippingMultiplier.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"Sensitivity={detectorParams.Sensitivity.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"StarClippingMultiplier={detectorParams.StarClippingMultiplier.ToString("G6", CultureInfo.InvariantCulture)}, " +
                $"DefocusAwareDonutDetection={starDetectionOptions.DefocusAwareDonutDetection}, " +
                $"RejectContaminatedStars={detectorParams.RejectContaminatedStars}, MaxStarsPerRegion={inspectorOptions.MaxStarsPerRegion}");

            var detector = new StarDetector(new AlglibAPI());
            var frames = new List<SensorDetectedStars>();
            DrawingSize imageSize = DrawingSize.Empty;
            foreach (var (path, focuser) in frameFiles) {
                // IRenderedImage route: the sensor model is fit from the star positions the LIVE detector would
                // produce (the CFA hotpixel filter + debayer run inside Detect at these params). On a bayered rig
                // the mosaic's checkerboard sampling shifts per-star measurements, which is exactly the bias the
                // tilt wizard calibrates from.
                var rendered = await DiagnosticUtil.LoadRenderedImage(path, profileService);
                var props = rendered.RawImageData.Properties;
                imageSize = new DrawingSize(props.Width, props.Height);
                var result = await detector.Detect(rendered, detectorParams, progress: null, CancellationToken.None);
                // Order stars by raster position EXACTLY as BuildStarDetectionResult does (HocusFocusStarDetection
                // line 612). The reference triangles are built onePerPoint=true (order-dependent greedy
                // consumption), so matching the production star-list order is required to reproduce the live
                // alignment outcome faithfully.
                var starList = result.DetectedStars.Select(HocusFocusStarDetection.ToDetectedStar)
                    .OrderBy(s => s.Position.Y * (long)props.Width + s.Position.X).ToList();
                var hfResult = new HocusFocusStarDetectionResult { StarList = starList, ImageSize = imageSize };
                frames.Add(new SensorDetectedStars(focuser, hfResult, image: null));
                Console.WriteLine($"  Focuser {focuser}: {starList.Count} stars");
            }

            var sortedFocusers = frameFiles.Select(f => (double)f.focuser).OrderBy(x => x).ToList();
            var finalFocusPosition = sortedFocusers[sortedFocusers.Count / 2]; // median; alignment is independent of this
            var stepSize = sortedFocusers.Count > 1 ? (int)Math.Round(sortedFocusers[1] - sortedFocusers[0]) : 100;
            var focuserSizeMicrons = inspectorOptions.EffectiveMicronsPerFocuserStep > 0 ? inspectorOptions.EffectiveMicronsPerFocuserStep : 1.0;
            var pixelSize = activeProfile.CameraSettings.PixelSize > 0 ? activeProfile.CameraSettings.PixelSize : 3.76;

            var messages = new List<string>();
            var sensorModel = new SensorModel(profileService, inspectorOptions, autoFocusOptions, new AlglibAPI()) {
                RegistrationReportSink = m => { messages.Add(m); Console.WriteLine($"  [report] {m}"); }
            };

            Console.WriteLine("Running RegisterStarsAndFit (RANSAC alignment + fit) ...");
            SensorParaboloidModel sensorFit = null;
            try {
                (sensorFit, _) = sensorModel.RegisterStarsAndFit(frames, imageSize, focuserSizeMicrons, finalFocusPosition, pixelSize,
                    progress: new Progress<ApplicationStatus>(), stepSize: stepSize, ct: CancellationToken.None);
            } catch (Exception ex) {
                // The paraboloid fit (after alignment) can throw on degenerate synthetic inputs; the alignment
                // report messages we care about are emitted earlier, so surface the error and keep reporting.
                Console.WriteLine($"  (RegisterStarsAndFit threw after alignment: {ex.GetType().Name}: {ex.Message})");
            }

            int aligned = frames.Count(f => f.HasBeenAligned);
            var refIdx = sensorModel.ReferenceImage;
            var tri = sensorModel.TrianglesByImage;

            var sb = new System.Text.StringBuilder();
            void Line(string s = "") { sb.AppendLine(s); Console.WriteLine(s); }
            Line();
            Line("================ ALIGNMENT SUMMARY ================");
            Line($"Reference frame index: {refIdx}" + (refIdx >= 0 && refIdx < frames.Count ? $" (Focuser {frames[refIdx].FocuserPosition}, {frames[refIdx].StarDetectionResult.StarList.Count} stars)" : ""));
            Line($"Frames aligned: {aligned} / {frames.Count}");
            Line("Per-frame: focuser, stars, triangles, aligned");
            for (int i = 0; i < frames.Count; ++i) {
                var triCount = (tri != null && tri.TryGetValue(i, out var t)) ? t.Count : -1;
                Line($"  [{i}] Focuser {frames[i].FocuserPosition,-6} stars={frames[i].StarDetectionResult.StarList.Count,-5} triangles={(triCount >= 0 ? triCount.ToString() : "-"),-6} aligned={frames[i].HasBeenAligned}{(i == refIdx ? "   <-- REFERENCE (onePerPoint=true)" : "")}");
            }
            Line();
            Line("Registration messages:");
            if (messages.Count == 0) Line("  (none -- all frames aligned cleanly)");
            foreach (var m in messages) Line($"  - {m}");

            Line();
            Line("================ SENSOR MODEL FIT ================");
            if (sensorFit != null) {
                Line($"StarsInModel:     {sensorFit.StarsInModel}");
                Line($"GoodnessOfFit R^2:{sensorFit.GoodnessOfFit:F4}");
                Line($"RMSError (um):    {sensorFit.RMSErrorMicrons:F3}");
                Line($"ReducedChiSquared:{sensorFit.ReducedChiSquared:F3}");
                Line($"Tilt theta (deg):  {sensorFit.Theta * 180.0 / Math.PI:F3}");
                Line($"Curvature K:      {sensorFit.K:E3}");
            } else {
                Line("  (no fit -- RegisterStarsAndFit did not produce a model)");
            }

            var outPath = Path.Combine(outDir, "inspect_align.txt");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"\nWrote {outPath}");
        }

        /// <summary>Optional detector-param overrides so the sensor model can be evaluated at a chosen settings
        /// config (mirrors golden eval): --opt-results &lt;dir&gt; overlays an optimized_settings.json (all fields incl.
        /// the v2 defocus-aware ones, AND-gated by the master); plus generic --noise-clip / --defocus-* toggles.</summary>
        private static void ApplySettingsOverrides(StarDetectorParams p, string[] args) {
            var optResults = DiagnosticUtil.GetArg(args, "--opt-results");
            if (!string.IsNullOrWhiteSpace(optResults)) {
                var path = Path.Combine(optResults, "optimized_settings.json");
                if (File.Exists(path)) {
                    var s = JsonConvert.DeserializeObject<OptimizedStarDetectionSettings>(File.ReadAllText(path));
                    p.Sensitivity = s.BrightnessSensitivity; p.StarClippingMultiplier = s.StarClippingMultiplier;
                    p.NoiseClippingMultiplier = s.NoiseClippingMultiplier; p.PeakResponse = s.StarPeakResponse;
                    p.MaxDistortion = s.MaxDistortion; p.MinHFR = s.MinHFR; p.StarCenterTolerance = s.StarCenterTolerance;
                    p.StructureLayers = s.StructureLayers; p.NoiseReductionRadius = s.NoiseReductionRadius;
                    p.MinimumStarBoundingBoxSize = s.MinStarBoundingBoxSize; p.HotpixelThresholdingEnabled = s.HotpixelThresholdingEnabled;
                    p.HotpixelThreshold = s.HotpixelThreshold;
                    p.LocallyAdaptiveBinarization = s.LocallyAdaptiveBinarization; p.AdaptiveNoiseBlockSize = s.AdaptiveNoiseBlockSize;
                    var master = s.DefocusAwareDonutDetection;
                    p.DefocusAwareDonutDetection = master;
                    p.DefocusAwareDistortion = s.DefocusAwareGates && master; p.DefocusAwareCentering = s.DefocusAwareGates && master;
                    p.DefocusAwareStructure = s.DefocusAwareStructure && master; p.StructureLayerBoost = s.StructureLayerBoost;
                    p.DefocusDistortionSizeReference = s.DefocusDistortionSizeReference; p.DefocusDistortionMinFactor = s.DefocusDistortionMinFactor;
                    p.DefocusCenteringToleranceFactor = s.DefocusCenteringToleranceFactor; p.DonutMorphCloseSize = s.DonutMorphCloseSize;
                    p.DonutMinAnnularityHoleFraction = s.DonutMinAnnularityHoleFraction; p.DonutMaxStreakEccentricity = s.DonutMaxStreakEccentricity;
                    p.DonutSaturationBloomRadius = s.DonutSaturationBloomRadius;
                    Console.WriteLine($"Applied optimized settings from {path}");
                } else {
                    Console.WriteLine($"WARNING: --opt-results given but {path} not found; using profile settings.");
                }
            }
            var nc = DiagnosticUtil.GetArg(args, "--noise-clip");
            if (nc != null && double.TryParse(nc, NumberStyles.Float, CultureInfo.InvariantCulture, out var ncv)) { p.NoiseClippingMultiplier = ncv; }
            var adaptiveBlock = DiagnosticUtil.GetArg(args, "--adaptive-block");
            if (adaptiveBlock != null && int.TryParse(adaptiveBlock, NumberStyles.Integer, CultureInfo.InvariantCulture, out var abv)) { p.AdaptiveNoiseBlockSize = abv; }
            if (DiagnosticUtil.HasFlag(args, "--adaptive-binarize")) { p.LocallyAdaptiveBinarization = true; }
            if (DiagnosticUtil.HasFlag(args, "--defocus-donut")) { p.DefocusAwareDonutDetection = true; }
            if (DiagnosticUtil.HasFlag(args, "--defocus-gates")) { p.DefocusAwareDistortion = true; p.DefocusAwareCentering = true; }
            if (DiagnosticUtil.HasFlag(args, "--defocus-structure")) { p.DefocusAwareStructure = true; if (p.StructureLayerBoost <= 0) p.StructureLayerBoost = 2; }
        }
    }
}
