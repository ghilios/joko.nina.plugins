#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestApp {

    /// <summary>An integer axis-aligned rectangle (full-frame pixels). JSON-friendly POCO used by the tile manifest
    /// and region geometry — kept free of OpenCV/NINA types so this whole file is pure and unit-testable.</summary>
    public sealed class IntRect {
        [JsonProperty("x")] public int X { get; set; }
        [JsonProperty("y")] public int Y { get; set; }
        [JsonProperty("w")] public int W { get; set; }
        [JsonProperty("h")] public int H { get; set; }

        public IntRect() { }

        public IntRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }

        [JsonIgnore] public int Right => X + W;
        [JsonIgnore] public int Bottom => Y + H;

        public RectD ToRectD() => new RectD(X, Y, W, H);

        public override string ToString() => $"{{X={X},Y={Y},W={W},H={H}}}";
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tile layout (pure): split a region of a full frame into native-resolution tiles for visual inspection.
    // ------------------------------------------------------------------------------------------------------------

    public static class TileLayout {

        /// <summary>
        /// Computes the 1-D tile start positions covering <paramref name="extent"/> px starting at
        /// <paramref name="start"/>, with <paramref name="tile"/>-px tiles and stride <c>tile - overlap</c>. Tiles are
        /// kept FULL size; the LAST tile is flushed back to the region edge (<c>start + extent - tile</c>) so it stays
        /// full-size and simply overlaps its neighbor more — uniform tile sizes are easier to inspect than a ragged
        /// clamped strip. When the region is no larger than a tile, a single (clamped) tile covers it.
        /// </summary>
        public static List<int> Positions(int start, int extent, int tile, int overlap) {
            var result = new List<int>();
            if (extent <= 0) {
                return result;
            }
            if (extent <= tile) {
                result.Add(start);
                return result;
            }
            var stride = Math.Max(1, tile - overlap);
            var end = start + extent;
            var p = start;
            while (true) {
                if (p + tile >= end) {
                    result.Add(end - tile);
                    break;
                }
                result.Add(p);
                p += stride;
            }
            return result.Distinct().OrderBy(v => v).ToList();
        }

        /// <summary>
        /// Tiles <paramref name="region"/> (clamped to the full frame) into native-resolution tiles. Tile width/height
        /// is <c>min(tile, regionExtent)</c> so tiles never exceed the region; positions come from <see cref="Positions"/>.
        /// </summary>
        public static List<IntRect> Compute(int fullW, int fullH, IntRect region, int tile, int overlap) {
            var rx = Math.Max(0, region.X);
            var ry = Math.Max(0, region.Y);
            var rw = Math.Min(region.W, fullW - rx);
            var rh = Math.Min(region.H, fullH - ry);
            var tileW = Math.Min(tile, rw);
            var tileH = Math.Min(tile, rh);

            var xs = Positions(rx, rw, tile, overlap);
            var ys = Positions(ry, rh, tile, overlap);
            var tiles = new List<IntRect>();
            foreach (var y in ys) {
                foreach (var x in xs) {
                    tiles.Add(new IntRect(x, y, tileW, tileH));
                }
            }
            return tiles;
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Tile manifest (pure POCOs) — maps each rendered tile PNG back to its full-frame rectangle.
    // ------------------------------------------------------------------------------------------------------------

    public sealed class ManifestTile {
        [JsonProperty("file")] public string File { get; set; }
        [JsonProperty("rect")] public IntRect Rect { get; set; }
    }

    public sealed class ManifestFrame {
        [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
        [JsonProperty("fullWidth")] public int FullWidth { get; set; }
        [JsonProperty("fullHeight")] public int FullHeight { get; set; }
        [JsonProperty("cropRect")] public IntRect CropRect { get; set; }
        [JsonProperty("overview")] public string Overview { get; set; }
        [JsonProperty("tiles")] public List<ManifestTile> Tiles { get; set; } = new List<ManifestTile>();
    }

    public sealed class TilesManifest {
        [JsonProperty("runId")] public string RunId { get; set; }
        [JsonProperty("region")] public string Region { get; set; }
        [JsonProperty("tileSize")] public int TileSize { get; set; }
        [JsonProperty("overlap")] public int Overlap { get; set; }
        /// <summary>Display upscale applied to each tile PNG (native tile px × Upscale = PNG px). Coordinates a
        /// labeler returns in the PNG space must be divided by Upscale to get native tile-local px.</summary>
        [JsonProperty("upscale")] public double Upscale { get; set; } = 1.0;
        [JsonProperty("frames")] public List<ManifestFrame> Frames { get; set; } = new List<ManifestFrame>();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Matching (pure): golden boxes <-> detected accepted stars.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>A detected accepted star reduced to what matching needs: its bounding box + its center.</summary>
    public struct DetBox {
        public RectD Box;
        public double Cx;
        public double Cy;

        public DetBox(RectD box, double cx, double cy) { Box = box; Cx = cx; Cy = cy; }
    }

    public enum GoldenMatchMode {
        /// <summary>A golden box matches a star when the star's center lies inside the box (same predicate the
        /// optimizer's recall/precision term uses). Default — robust to bbox-size disagreement on donuts.</summary>
        Center,

        /// <summary>A golden box matches a star when IoU(box, star bbox) &gt;= tau.</summary>
        Iou,

        /// <summary>Match when EITHER center-in-box OR IoU&gt;=tau holds (the union).</summary>
        Both,

        /// <summary>A golden box matches a star when their centers are within <c>radius</c> px. Robust to the
        /// ~10px coordinate scatter of visual labeling.</summary>
        Centroid
    }

    public sealed class GoldenMatchResult {
        /// <summary>(goldenIndex, detectedIndex) pairs — true positives.</summary>
        public List<(int Golden, int Detected)> Pairs { get; } = new List<(int, int)>();

        /// <summary>Indices into the golden list with no matched star — false negatives.</summary>
        public List<int> FalseNegatives { get; } = new List<int>();

        /// <summary>Indices into the detected list with no matched golden — false positives.</summary>
        public List<int> FalsePositives { get; } = new List<int>();
    }

    public static class GoldenMatch {

        public static bool CenterInRect(double cx, double cy, RectD r) =>
            cx >= r.X && cx <= r.X + r.W && cy >= r.Y && cy <= r.Y + r.H;

        /// <summary>Drops false positives that land on an UNRESOLVED golden box. Those candidates were
        /// never examined by QA, so a detection there is unjudged rather than wrong — counting it as a
        /// false positive is what turned montage-budget truncation into thousands of fake FPs (F11).
        /// A null/empty unresolved list is the schema-v1 case and must be a no-op.</summary>
        public static List<int> ExcludeUnresolved(
                IReadOnlyList<int> falsePositives, IReadOnlyList<DetBox> detected, IReadOnlyList<RectD> unresolved) {
            if (unresolved == null || unresolved.Count == 0) {
                return falsePositives.ToList();
            }
            return falsePositives.Where(di => unresolved.All(u => !Covers(u, detected[di]))).ToList();
        }

        private static bool Covers(RectD box, DetBox det) =>
            CenterInRect(det.Cx, det.Cy, box) || BoxMatcher.IoU(box, det.Box) > 0.0;

        private struct Cand {
            public int G;
            public int D;
            public double IoU;
            public double Dist;
        }

        /// <summary>
        /// Greedy 1:1 matching of golden boxes to detected accepted stars. Qualifying pairs (per <paramref name="mode"/>)
        /// are ranked by IoU desc, then center-distance asc, and assigned greedily so each golden box and each star is
        /// used at most once. Unmatched golden ⇒ false negatives; unmatched stars ⇒ false positives.
        /// </summary>
        public static GoldenMatchResult Match(
            IReadOnlyList<RectD> golden, IReadOnlyList<DetBox> detected, GoldenMatchMode mode, double tau, double radius = 0.0) {

            var result = new GoldenMatchResult();
            var nG = golden?.Count ?? 0;
            var nD = detected?.Count ?? 0;

            var cands = new List<Cand>();
            for (int g = 0; g < nG; g++) {
                for (int d = 0; d < nD; d++) {
                    var iou = BoxMatcher.IoU(golden[g], detected[d].Box);
                    var centerIn = CenterInRect(detected[d].Cx, detected[d].Cy, golden[g]);
                    var dx = detected[d].Cx - (golden[g].X + golden[g].W / 2.0);
                    var dy = detected[d].Cy - (golden[g].Y + golden[g].H / 2.0);
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    var withinRadius = radius > 0.0 && dist <= radius;
                    bool qualifies;
                    switch (mode) {
                        case GoldenMatchMode.Center: qualifies = centerIn || withinRadius; break;
                        case GoldenMatchMode.Iou: qualifies = iou >= tau || withinRadius; break;
                        case GoldenMatchMode.Centroid: qualifies = withinRadius; break;
                        default: qualifies = centerIn || iou >= tau || withinRadius; break;
                    }
                    if (!qualifies) {
                        continue;
                    }
                    cands.Add(new Cand { G = g, D = d, IoU = iou, Dist = dist });
                }
            }

            cands.Sort((a, b) => {
                var c = b.IoU.CompareTo(a.IoU);
                return c != 0 ? c : a.Dist.CompareTo(b.Dist);
            });

            var gUsed = new bool[nG];
            var dUsed = new bool[nD];
            foreach (var c in cands) {
                if (gUsed[c.G] || dUsed[c.D]) {
                    continue;
                }
                gUsed[c.G] = true;
                dUsed[c.D] = true;
                result.Pairs.Add((c.G, c.D));
            }
            for (int g = 0; g < nG; g++) {
                if (!gUsed[g]) {
                    result.FalseNegatives.Add(g);
                }
            }
            for (int d = 0; d < nD; d++) {
                if (!dUsed[d]) {
                    result.FalsePositives.Add(d);
                }
            }
            return result;
        }
    }

    /// <summary>Precision/recall/F1 from confusion counts. Denominator 0 ⇒ NaN (no golden ⇒ recall N/A; no
    /// predictions ⇒ precision N/A) so empty regions are reported as N/A rather than a misleading 0 or 1.</summary>
    public struct PrecisionRecall {
        public int TP;
        public int FP;
        public int FN;
        public double Precision;
        public double Recall;
        public double F1;

        public static PrecisionRecall Compute(int tp, int fp, int fn) {
            var precision = (tp + fp) == 0 ? double.NaN : (double)tp / (tp + fp);
            var recall = (tp + fn) == 0 ? double.NaN : (double)tp / (tp + fn);
            double f1;
            if (double.IsNaN(precision) || double.IsNaN(recall) || (precision + recall) == 0.0) {
                f1 = double.NaN;
            } else {
                f1 = 2.0 * precision * recall / (precision + recall);
            }
            return new PrecisionRecall { TP = tp, FP = fp, FN = fn, Precision = precision, Recall = recall, F1 = f1 };
        }
    }
}
