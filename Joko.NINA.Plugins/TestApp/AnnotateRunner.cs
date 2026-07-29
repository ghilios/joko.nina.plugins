#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

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

namespace TestApp {

    /// <summary>
    /// Minimal CSV-driven annotator: overlays an EXTERNALLY supplied star list onto frames rendered with the
    /// real plugin MTF stretch (the same stretch the optimize/review tools use), so a hand-built golden catalog
    /// can be visualized on the production stretch. Read-only on the profile.
    ///
    /// Usage:
    ///   TestApp annotate --image &lt;frame&gt; --stars &lt;list.csv&gt; [--out &lt;dir&gt;] [--profile-id &lt;guid&gt;]
    ///   TestApp annotate --runs  &lt;dir&gt;   --stars &lt;csvDir&gt; [--out &lt;dir&gt;] [--profile-id &lt;guid&gt;]
    ///
    /// CSV: one star per line "x,y[,w,h[,color]]" in image-space pixels. color ∈ {green,red,orange,yellow,
    /// cyan,blue} (default green); w,h default to 12. Lines starting with '#' are ignored. With --runs, the
    /// per-frame CSV is &lt;csvDir&gt;/&lt;frameStem&gt;.csv. Omit --stars to export a plain stretched PNG (FITS/XISF→PNG).
    /// </summary>
    internal static class AnnotateRunner {
        private static readonly string[] ImageExts = { ".fits", ".fit", ".xisf", ".tif", ".tiff" };

        public static async Task Run(string[] args) {
            // ImageDataFactory writes to Application.Current.Resources during FITS/XISF load.
            if (Application.Current == null) {
                new Application();
            }

            var image = DiagnosticUtil.GetArg(args, "--image");
            var runs = DiagnosticUtil.GetArg(args, "--runs");
            var stars = DiagnosticUtil.GetArg(args, "--stars");
            var profileId = DiagnosticUtil.GetArg(args, "--profile-id");
            var outDir = DiagnosticUtil.GetArg(args, "--out")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Logs", "hf-diag", "annotate", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (image == null && runs == null) {
                Console.WriteLine("Usage: TestApp annotate --image <frame> --stars <csv> [--out <dir>] [--profile-id <guid>]");
                Console.WriteLine("       TestApp annotate --runs <dir> --stars <csvDir> [--out <dir>] [--profile-id <guid>]");
                return;
            }
            Directory.CreateDirectory(outDir);

            var profileService = new ProfileService();
            profileService.TryLoad(profileId ?? string.Empty);
            if (profileService.ActiveProfile == null) {
                throw new InvalidOperationException("No active NINA profile could be loaded. Pass --profile-id, or run NINA once to create a profile.");
            }
            Console.WriteLine($"Profile: {profileService.ActiveProfile.Name} ({profileService.ActiveProfile.Id})");
            Console.WriteLine($"Output: {outDir}");

            var frames = new List<string>();
            if (image != null) {
                frames.Add(image);
            } else {
                frames.AddRange(Directory.EnumerateFiles(runs)
                    .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.Ordinal));
            }
            if (frames.Count == 0) {
                Console.WriteLine("No frames found.");
                return;
            }

            foreach (var frame in frames) {
                var stem = Path.GetFileNameWithoutExtension(frame);
                string csvPath = null;
                if (stars != null) {
                    csvPath = image != null ? stars : Path.Combine(stars, stem + ".csv");
                }
                var boxes = (csvPath != null && File.Exists(csvPath)) ? ParseCsv(csvPath) : new List<Box>();

                // Overlay background only (never detection): a bayered frame is debayered to luminance so the
                // annotated PNG is legible, but is NEVER CFA hotpixel filtered.
                using var floatMat = await DiagnosticUtil.LoadDisplayFloatMat(frame, profileService);
                using var bgr = BuildStretchedBgr(floatMat);
                foreach (var b in boxes) {
                    var rect = new OpenCvSharp.Rect((int)Math.Round(b.X - b.W / 2.0), (int)Math.Round(b.Y - b.H / 2.0),
                        Math.Max(1, (int)Math.Round(b.W)), Math.Max(1, (int)Math.Round(b.H)));
                    Cv2.Rectangle(bgr, rect, b.Color, 2, LineTypes.AntiAlias);
                }
                var outPath = Path.Combine(outDir, stem + "_annotated.png");
                Cv2.ImWrite(outPath, bgr);
                Console.WriteLine($"{stem}: {boxes.Count} boxes -> {Path.GetFileName(outPath)}");
            }
            Console.WriteLine("Done.");
        }

        private struct Box { public double X, Y, W, H; public Scalar Color; }

        private static List<Box> ParseCsv(string path) {
            var result = new List<Box>();
            foreach (var raw in File.ReadAllLines(path)) {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) {
                    continue;
                }
                var p = line.Split(',');
                if (p.Length < 2) {
                    continue;
                }
                if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) {
                    continue;
                }
                double w = 12, h = 12;
                if (p.Length >= 4) {
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out w);
                    double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out h);
                }
                var color = p.Length >= 5 ? ColorOf(p[4].Trim()) : ColorOf("green");
                result.Add(new Box { X = x, Y = y, W = w, H = h, Color = color });
            }
            return result;
        }

        // BGR scalars (OpenCV channel order).
        private static Scalar ColorOf(string c) {
            switch ((c ?? "green").ToLowerInvariant()) {
                case "red": return new Scalar(0, 0, 255);
                case "orange": return new Scalar(0, 150, 255);
                case "yellow": return new Scalar(0, 255, 255);
                case "cyan": return new Scalar(255, 255, 0);
                case "blue": return new Scalar(255, 0, 0);
                case "magenta": return new Scalar(255, 0, 255);
                default: return new Scalar(0, 255, 0); // green
            }
        }

        // Same MTF-stretch path as OptimizationDiagnosticRunner.BuildStretchedBgr (the labeler/optimize render).
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
