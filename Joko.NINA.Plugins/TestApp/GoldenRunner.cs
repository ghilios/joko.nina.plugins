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
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Logger = NINA.Core.Utility.Logger;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace TestApp {

    /// <summary>
    /// <c>golden tiles</c>: renders each frame of a saved AF focus sweep into MTF-stretched, native-resolution PNG
    /// TILES (plus a downsampled overview) for VISUAL golden-set authoring. It runs NO star detection — it only
    /// stretches + crops + tiles, and writes a <c>tiles_manifest.json</c> mapping every tile back to its full-frame
    /// rectangle so visually-marked tile-local coordinates can be transformed to full-frame coordinates and merged.
    ///
    /// <para>The stretch is computed on the WHOLE frame (global statistics) BEFORE tiling, so every tile shares the
    /// same contrast and faint stars read consistently — this is the same MTF path the optimize/review/annotate
    /// tools use.</para>
    ///
    /// Usage:
    ///   TestApp golden tiles --runs &lt;dir&gt; [--out &lt;dir&gt;] [--region full|inner] [--tile 1024] [--overlap 128]
    ///                        [--overview-max 2000] [--run &lt;runId&gt;] [--profile-id &lt;guid&gt;]
    /// </summary>
    internal static class GoldenRunner {

        private sealed class RegionReportLite {
            public StarDetectionRegion Region { get; set; }
        }

        public static async Task Run(string[] args) {
            // ImageDataFactory writes to Application.Current.Resources during FITS/XISF load.
            if (Application.Current == null) {
                new Application();
            }

            var runsDir = DiagnosticUtil.GetArg(args, "--runs");
            if (string.IsNullOrWhiteSpace(runsDir)) {
                Console.WriteLine("Usage: TestApp golden tiles --runs <dir> [--out <dir>] [--region full|inner] [--tile 1024] [--overlap 128] [--overview-max 2000] [--run <runId>] [--profile-id <guid>]");
                return;
            }
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var region = (DiagnosticUtil.GetArg(args, "--region") ?? "full").Trim().ToLowerInvariant();
            var tile = ParseInt(DiagnosticUtil.GetArg(args, "--tile"), 1024);
            var overlap = ParseInt(DiagnosticUtil.GetArg(args, "--overlap"), 128);
            var overviewMax = ParseInt(DiagnosticUtil.GetArg(args, "--overview-max"), 2000);
            var runFilter = DiagnosticUtil.GetArg(args, "--run");
            // Stretch controls for visual star authoring (stronger than the production MTF defaults): black-clip is
            // the MTF shadowsClipping (LESS negative = clip more background to black), midtone is the MTF target
            // median (higher = brighter), gamma < 1 brightens faint stars (applied after the MTF).
            var blackClip = ParseDouble(DiagnosticUtil.GetArg(args, "--black-clip"), -1.25);
            var midtone = ParseDouble(DiagnosticUtil.GetArg(args, "--midtone"), 0.25);
            var gamma = ParseDouble(DiagnosticUtil.GetArg(args, "--gamma"), 0.6);
            // Display upscale: each native tile PNG is enlarged by this factor so small stars span enough
            // vision-encoder patches to be localized precisely (1024px tiles are too zoomed-out — a star is sub-patch).
            var upscale = ParseDouble(DiagnosticUtil.GetArg(args, "--upscale"), 1.0);
            var framesFilter = ParseFrames(DiagnosticUtil.GetArg(args, "--frames"));
            var outDir = DiagnosticUtil.GetArg(args, "--out")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Logs", "hf-diag", "golden-tiles", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (tile <= 0) {
                throw new ArgumentException("--tile must be positive");
            }
            if (overlap < 0 || overlap >= tile) {
                throw new ArgumentException("--overlap must be in [0, tile)");
            }

            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            if (profileService.ActiveProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA once to create a profile.");
            }
            Console.WriteLine($"Profile: {profileService.ActiveProfile.Name} ({profileService.ActiveProfile.Id})");
            Console.WriteLine($"Output: {outDir}  (region={region}, tile={tile}, overlap={overlap}, upscale={upscale}, blackClip={blackClip}, midtone={midtone}, gamma={gamma})");

            var discovery = OptimizationRunDiscovery.Discover(runsDir);
            if (discovery.Runs.Count == 0) {
                throw new InvalidOperationException($"No usable AF runs (>= {OptimizationRunDiscovery.MinPositionsForFit} positions) found under {runsDir}.");
            }
            foreach (var s in discovery.Skipped) {
                Console.WriteLine($"  skipped '{s.RelativePath}': {s.Reason}");
            }

            foreach (var run in discovery.Runs) {
                if (!string.IsNullOrWhiteSpace(runFilter) && !string.Equals(run.RunId, runFilter, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                await RenderRun(run, region, tile, overlap, overviewMax, blackClip, midtone, gamma, upscale, framesFilter, outDir, profileService);
            }
            Console.WriteLine("Done.");
        }

        private static async Task RenderRun(OptimizationRunDiscovery.DiscoveredRun run, string region, int tile, int overlap,
            int overviewMax, double blackClip, double midtone, double gamma, double upscale, HashSet<int> framesFilter, string outRoot, ProfileService profileService) {

            var runOut = Path.Combine(outRoot, OptimizationRunDiscovery.SanitizeForFileName(run.RunId));
            Directory.CreateDirectory(runOut);
            var manifest = new TilesManifest { RunId = run.RunId, Region = region, TileSize = tile, Overlap = overlap, Upscale = upscale };

            foreach (var frame in run.Frames.OrderBy(f => f.FocuserPosition)) {
                if (framesFilter != null && !framesFilter.Contains(frame.FocuserPosition)) {
                    continue;
                }
                // Tiles are the human/LLM authoring surface, so a bayered frame is DEBAYERED to luminance (a raw
                // mosaic renders as a visible checkerboard). No CFA hotpixel filter: this renders, it never detects.
                using var floatMat = await DiagnosticUtil.LoadDebayeredFloatMat(frame.Path, profileService);
                var fullW = floatMat.Cols;
                var fullH = floatMat.Rows;
                var cropRect = ResolveRegion(region, frame.Path, fullW, fullH);

                using var bgr = BuildStretchedBgr(floatMat, blackClip, midtone, gamma);

                var frameDir = Path.Combine(runOut, $"f{frame.FocuserPosition}");
                Directory.CreateDirectory(frameDir);
                var mf = new ManifestFrame {
                    FocuserPosition = frame.FocuserPosition,
                    FullWidth = fullW,
                    FullHeight = fullH,
                    CropRect = cropRect
                };

                // Overview: the region crop downsampled to overviewMax long edge.
                using (var crop = bgr.SubMat(new Rect(cropRect.X, cropRect.Y, cropRect.W, cropRect.H))) {
                    var scale = Math.Min(1.0, (double)overviewMax / Math.Max(cropRect.W, cropRect.H));
                    var ow = Math.Max(1, (int)Math.Round(cropRect.W * scale));
                    var oh = Math.Max(1, (int)Math.Round(cropRect.H * scale));
                    using var overview = new Mat();
                    Cv2.Resize(crop, overview, new Size(ow, oh), interpolation: InterpolationFlags.Area);
                    var overviewName = $"f{frame.FocuserPosition}_overview.png";
                    Cv2.ImWrite(Path.Combine(frameDir, overviewName), overview);
                    mf.Overview = $"f{frame.FocuserPosition}/{overviewName}";
                }

                var tiles = TileLayout.Compute(fullW, fullH, cropRect, tile, overlap);
                foreach (var t in tiles) {
                    using var sub = bgr.SubMat(new Rect(t.X, t.Y, t.W, t.H)).Clone();
                    var name = $"f{frame.FocuserPosition}_x{t.X}_y{t.Y}_w{t.W}_h{t.H}.png";
                    if (Math.Abs(upscale - 1.0) > 1e-6 && upscale > 0) {
                        using var up = new Mat();
                        Cv2.Resize(sub, up, new Size((int)Math.Round(t.W * upscale), (int)Math.Round(t.H * upscale)), interpolation: InterpolationFlags.Lanczos4);
                        Cv2.ImWrite(Path.Combine(frameDir, name), up);
                    } else {
                        Cv2.ImWrite(Path.Combine(frameDir, name), sub);
                    }
                    mf.Tiles.Add(new ManifestTile { File = $"f{frame.FocuserPosition}/{name}", Rect = t });
                }

                manifest.Frames.Add(mf);
                Console.WriteLine($"  f{frame.FocuserPosition}: {fullW}x{fullH}, crop {cropRect}, {tiles.Count} tiles");
            }

            var manifestPath = Path.Combine(runOut, "tiles_manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));
            Console.WriteLine($"  manifest -> {manifestPath}");
        }

        /// <summary>Full frame for "full"; the AF report's Region0 outer boundary (the AF inner crop) for "inner".</summary>
        private static IntRect ResolveRegion(string region, string framePath, int fullW, int fullH) {
            if (region != "inner") {
                return new IntRect(0, 0, fullW, fullH);
            }
            var folder = Path.GetDirectoryName(framePath);
            var reportPath = Path.Combine(folder ?? string.Empty, "autofocus_report_Region0.json");
            if (File.Exists(reportPath)) {
                try {
                    var lite = JsonConvert.DeserializeObject<RegionReportLite>(File.ReadAllText(reportPath));
                    var outer = lite?.Region?.OuterBoundary;
                    if (outer != null) {
                        var r = outer.ToRectangle(new System.Drawing.Size(fullW, fullH));
                        return ClampToFrame(new IntRect(r.X, r.Y, r.Width, r.Height), fullW, fullH);
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"  WARNING: could not read region report '{reportPath}' ({ex.Message}); using full frame.");
                }
            } else {
                Console.WriteLine($"  WARNING: no autofocus_report_Region0.json beside frames; using full frame for --region inner.");
            }
            return new IntRect(0, 0, fullW, fullH);
        }

        private static IntRect ClampToFrame(IntRect r, int fullW, int fullH) {
            var x = Math.Max(0, Math.Min(r.X, fullW - 1));
            var y = Math.Max(0, Math.Min(r.Y, fullH - 1));
            var w = Math.Max(1, Math.Min(r.W, fullW - x));
            var h = Math.Max(1, Math.Min(r.H, fullH - y));
            return new IntRect(x, y, w, h);
        }

        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static double ParseDouble(string s, double fallback) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static HashSet<int> ParseFrames(string s) {
            if (string.IsNullOrWhiteSpace(s)) {
                return null;
            }
            var set = new HashSet<int>();
            foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) {
                    set.Add(v);
                }
            }
            return set.Count > 0 ? set : null;
        }

        // MTF stretch (same path as AnnotateRunner) with explicit knobs tuned STRONGER than production for VISUAL
        // star authoring: blackClip = MTF shadowsClipping (less negative ⇒ more background clipped to black),
        // midtone = MTF target median (brighter), then an optional gamma (&lt;1 brightens faint stars). Computed on
        // the whole frame so contrast is uniform across tiles.
        private static Mat BuildStretchedBgr(Mat srcFloat, double blackClip, double midtone, double gamma) {
            using var src16 = new Mat();
            srcFloat.ConvertTo(src16, MatType.CV_16U, ushort.MaxValue);
            using var stretched16 = new Mat();
            try {
                var stats = CvImageUtility.CalculateStatistics_Histogram(src16);
                using var lut = CvImageUtility.CreateMTFLookup(stats, blackClip, midtone);
                CvImageUtility.ApplyLUT(src16, lut, stretched16);
            } catch (Exception ex) {
                Logger.Warning($"MTF stretch failed ({ex.Message}); falling back to linear normalization");
                Cv2.Normalize(src16, stretched16, 0, ushort.MaxValue, NormTypes.MinMax);
            }
            using var stretched8 = new Mat();
            stretched16.ConvertTo(stretched8, MatType.CV_8U, 1.0 / 256.0);
            if (Math.Abs(gamma - 1.0) > 1e-6 && gamma > 0) {
                using var gammaLut = new Mat(1, 256, MatType.CV_8U);
                unsafe {
                    var p = (byte*)gammaLut.DataPointer;
                    for (int i = 0; i < 256; i++) {
                        p[i] = (byte)Math.Round(Math.Min(255.0, 255.0 * Math.Pow(i / 255.0, gamma)));
                    }
                }
                Cv2.LUT(stretched8, gammaLut, stretched8);
            }
            var bgr = new Mat();
            Cv2.CvtColor(stretched8, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }
    }
}
