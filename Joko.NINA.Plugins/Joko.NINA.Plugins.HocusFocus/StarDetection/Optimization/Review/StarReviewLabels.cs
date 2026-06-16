#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>
    /// The on-disk label JSON model, shaped EXACTLY as the T6 optimizer harness
    /// (<c>OptimizationDiagnosticRunner</c>) reads it via <c>--labels</c>: one file per run, named
    /// "&lt;runId&gt;.json", with the property names/casing below (matching T6's <c>[JsonProperty]</c> names).
    /// T7 (the interactive review tool) WRITES these; T6 READS them back to activate the recall/precision
    /// objective term — so the two property-name sets must stay identical. The round-trip is covered by a unit
    /// test that deserializes a T7-written file through a POCO carrying T6's exact attributes.
    ///
    /// <para>Each label is a BOX in image-space (top-left <c>x</c>,<c>y</c> + <c>w</c>,<c>h</c>): missed is a
    /// rubber-band box the user drags; should-reject / wrongly-rejected record the actual detector bounding box of
    /// the clicked star. Recall/precision are scored by box-containment (an accepted center inside the box). Older
    /// point-only files (with just <c>x</c>,<c>y</c>) still load: a missing <c>w</c>/<c>h</c> defaults to a small
    /// 2·radiusPx box centered on the point (see <see cref="StarReviewLabelStore.Normalize"/>).</para>
    /// <code>
    /// {
    ///   "runId": "attempt01",
    ///   "radiusPx": 6.0,                          // default radius (legacy point-load box fallback)
    ///   "positions": [
    ///     {
    ///       "focuserPosition": 5000,
    ///       "radiusPx": 6.0,                       // optional per-position override
    ///       "missed":          [ { "x": 123.4, "y": 567.8, "w": 12.0, "h": 12.0 } ],  // false negatives to recover (recall)
    ///       "shouldReject":    [ { "x": 12.0,  "y": 34.0,  "w": 9.0,  "h": 9.0  } ],  // false positives to exclude (precision)
    ///       "wronglyRejected": [ { "x": 88.0,  "y": 90.0,  "w": 10.0, "h": 8.0  } ]   // detected-but-gated candidates that should be KEPT (folds into recall)
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public sealed class StarReviewLabelBox {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }

        // Nullable so a legacy point-only file (x,y but no w,h) round-trips through deserialization and can be
        // back-filled with a default box in Normalize. Once filled they always serialize.
        [JsonProperty("w")] public double? W { get; set; }
        [JsonProperty("h")] public double? H { get; set; }

        public StarReviewLabelBox() { }

        public StarReviewLabelBox(double x, double y, double w, double h) {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        /// <summary>True when this label already carries both dimensions (i.e. is a real box, not a legacy point).</summary>
        [JsonIgnore]
        public bool HasSize => W.HasValue && H.HasValue;

        /// <summary>Center of the box (used by the precision/recall containment helpers and the toggle hit-test).</summary>
        [JsonIgnore]
        public double CenterX => X + (W ?? 0.0) / 2.0;

        [JsonIgnore]
        public double CenterY => Y + (H ?? 0.0) / 2.0;
    }

    public sealed class StarReviewPositionLabels {
        [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
        [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
        [JsonProperty("missed")] public List<StarReviewLabelBox> Missed { get; set; } = new List<StarReviewLabelBox>();
        [JsonProperty("shouldReject")] public List<StarReviewLabelBox> ShouldReject { get; set; } = new List<StarReviewLabelBox>();

        /// <summary>Detected-but-gate-rejected candidates the user judges should have been KEPT. Folds into recall
        /// (union with <see cref="Missed"/>) on the optimizer side — every wrongly-rejected box is a recall target.</summary>
        [JsonProperty("wronglyRejected")] public List<StarReviewLabelBox> WronglyRejected { get; set; } = new List<StarReviewLabelBox>();
    }

    public sealed class StarReviewRunLabels {
        [JsonProperty("runId")] public string RunId { get; set; }
        [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
        [JsonProperty("positions")] public List<StarReviewPositionLabels> Positions { get; set; } = new List<StarReviewPositionLabels>();
    }

    /// <summary>
    /// Load/merge/save helpers for the per-run label files. All file shaping (one file per run, ordinal-sorted,
    /// indented JSON, T6-compatible serializer settings) and all merge/dedupe logic live here so they are pure
    /// (no UI/IO mixed with decisions) and unit-testable.
    /// </summary>
    public static class StarReviewLabelStore {
        public const double DefaultRadiusPx = 6.0;

        // Two label boxes are "the same" (so a toggle removes rather than re-adds) when their CENTERS are within this
        // many pixels. Independent of the scoring radius; this is purely a UI hit-test for the undo-on-click behavior.
        public const double SamePointTolerancePx = 4.0;

        // T6 reads with default Newtonsoft settings (JsonConvert.DeserializeObject). Serialize indented and skip
        // nulls so an empty per-position radius override is simply absent (T6 falls back to the file default).
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string FileNameFor(string runId) => CleanRunFileStem(runId) + ".json";

        /// <summary>
        /// Derives a clean, human-readable, collision-resistant file stem from a <paramref name="runId"/> that is
        /// typically an absolute run-folder path: the last one or two path segments joined with '_' (e.g.
        /// "sensitivity_example1_attempt01"), sanitized of invalid filename characters. Falls back to the whole
        /// sanitized id, then to "run". The embedded <c>runId</c> inside the file remains authoritative for
        /// matching (both the plugin's <see cref="Load"/> embedded-scan and the offline harness match on it), so
        /// this is purely cosmetic — old sanitized-absolute-path files still load via the embedded scan.
        /// </summary>
        public static string CleanRunFileStem(string runId) {
            if (string.IsNullOrWhiteSpace(runId)) {
                return "run";
            }
            var segments = runId.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string stem;
            if (segments.Length >= 2) {
                stem = segments[segments.Length - 2] + "_" + segments[segments.Length - 1];
            } else if (segments.Length == 1) {
                stem = segments[0];
            } else {
                stem = runId;
            }
            return SanitizeFileName(stem);
        }

        /// <summary>
        /// Loads the label file for <paramref name="runId"/> from <paramref name="labelsDir"/> if it exists,
        /// otherwise returns an empty <see cref="StarReviewRunLabels"/> with the default radius. The lookup
        /// prefers the "&lt;runId&gt;.json" filename, then falls back to any *.json whose embedded runId matches
        /// (mirroring T6's resolution order). Never throws on a missing dir/file; a corrupt file logs to the
        /// caller via the returned <paramref name="error"/>.
        /// </summary>
        public static StarReviewRunLabels Load(string labelsDir, string runId, out string error) {
            error = null;
            var fresh = new StarReviewRunLabels { RunId = runId, RadiusPx = DefaultRadiusPx };
            if (string.IsNullOrWhiteSpace(labelsDir) || !Directory.Exists(labelsDir)) {
                return fresh;
            }

            // 1. Exact filename match.
            var direct = Path.Combine(labelsDir, FileNameFor(runId));
            if (File.Exists(direct)) {
                var parsed = TryParse(direct, out error);
                if (parsed != null) {
                    parsed.RunId = runId; // canonicalize
                    Normalize(parsed);
                    return parsed;
                }
                return fresh;
            }

            // 2. Embedded-runId match across all *.json (ordinal order for determinism).
            foreach (var file in Directory.GetFiles(labelsDir, "*.json").OrderBy(f => f, StringComparer.Ordinal)) {
                var parsed = TryParse(file, out var perFileError);
                if (parsed == null) {
                    if (error == null) {
                        error = perFileError;
                    }
                    continue;
                }
                var embedded = !string.IsNullOrWhiteSpace(parsed.RunId) ? parsed.RunId : Path.GetFileNameWithoutExtension(file);
                if (string.Equals(embedded, runId, StringComparison.Ordinal)) {
                    parsed.RunId = runId;
                    Normalize(parsed);
                    return parsed;
                }
            }
            return fresh;
        }

        /// <summary>Saves <paramref name="labels"/> to "&lt;runId&gt;.json" in <paramref name="labelsDir"/>
        /// (created if needed), ordinal-sorted by focuser position, indented, T6-compatible.</summary>
        public static string Save(string labelsDir, StarReviewRunLabels labels) {
            if (labels == null) {
                throw new ArgumentNullException(nameof(labels));
            }
            // No-op when no labels dir could be derived (the wizard passes string.Empty in that case). Mirrors the
            // guard Load already has — without it Directory.CreateDirectory("") throws ArgumentException and every
            // Save/Next/Prev would log an error. Returns null to signal nothing was written.
            if (string.IsNullOrWhiteSpace(labelsDir)) {
                return null;
            }
            Directory.CreateDirectory(labelsDir);
            Normalize(labels);
            var path = Path.Combine(labelsDir, FileNameFor(labels.RunId));
            File.WriteAllText(path, Serialize(labels));
            return path;
        }

        /// <summary>Pure serializer (no IO) — exposed so a unit test can assert the produced JSON shape.</summary>
        public static string Serialize(StarReviewRunLabels labels) {
            Normalize(labels);
            return JsonConvert.SerializeObject(labels, SerializerSettings);
        }

        /// <summary>
        /// Returns the per-position label bucket for <paramref name="focuserPosition"/>, creating it (with the
        /// run's default radius) and appending it in sorted order when absent. Mutates and returns from
        /// <paramref name="labels"/> so the caller's edits land in the persisted model.
        /// </summary>
        public static StarReviewPositionLabels GetOrAddPosition(StarReviewRunLabels labels, int focuserPosition) {
            var existing = labels.Positions.FirstOrDefault(p => p.FocuserPosition == focuserPosition);
            if (existing != null) {
                return existing;
            }
            var added = new StarReviewPositionLabels {
                FocuserPosition = focuserPosition,
                RadiusPx = labels.RadiusPx ?? DefaultRadiusPx
            };
            labels.Positions.Add(added);
            labels.Positions.Sort((a, b) => a.FocuserPosition.CompareTo(b.FocuserPosition));
            return added;
        }

        /// <summary>
        /// Toggles a label box: removes an existing box in <paramref name="boxes"/> whose CENTER is within
        /// <see cref="SamePointTolerancePx"/> of (<paramref name="x"/>,<paramref name="y"/>,<paramref name="w"/>,
        /// <paramref name="h"/>)'s center, otherwise adds <paramref name="x"/>,<paramref name="y"/>,<paramref name="w"/>,
        /// <paramref name="h"/> as a new box. Returns true if a box was ADDED, false if one was removed. Pure list
        /// mutation — used for the click/drag-to-add / click-again-to-undo UX.
        /// </summary>
        public static bool ToggleBox(List<StarReviewLabelBox> boxes, double x, double y, double w, double h) {
            var cx = x + w / 2.0;
            var cy = y + h / 2.0;
            var idx = NearestBoxIndexWithin(boxes, cx, cy, SamePointTolerancePx);
            if (idx >= 0) {
                boxes.RemoveAt(idx);
                return false;
            }
            boxes.Add(new StarReviewLabelBox(x, y, w, h));
            return true;
        }

        /// <summary>
        /// Index of the box in <paramref name="boxes"/> whose CENTER is nearest to (cx,cy) within
        /// <paramref name="tolerancePx"/>, or -1 if none. Pure helper (used by the toggle hit-test and unit-tested directly).
        /// </summary>
        public static int NearestBoxIndexWithin(List<StarReviewLabelBox> boxes, double cx, double cy, double tolerancePx) {
            if (boxes == null || boxes.Count == 0) {
                return -1;
            }
            var tol2 = tolerancePx * tolerancePx;
            var bestIdx = -1;
            var bestD2 = double.MaxValue;
            for (var i = 0; i < boxes.Count; i++) {
                var dx = boxes[i].CenterX - cx;
                var dy = boxes[i].CenterY - cy;
                var d2 = dx * dx + dy * dy;
                if (d2 <= tol2 && d2 < bestD2) {
                    bestD2 = d2;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// Index of the SMALLEST-area box in <paramref name="boxes"/> that CONTAINS (x,y) (AABB containment), or -1 if
        /// none. Pure helper — used by the should-reject/wrongly-rejected undo hit-test where a click lands inside an
        /// already-labeled box rather than near its center.
        /// </summary>
        public static int SmallestContainingBoxIndex(List<StarReviewLabelBox> boxes, double x, double y) {
            if (boxes == null || boxes.Count == 0) {
                return -1;
            }
            var bestIdx = -1;
            var bestArea = double.MaxValue;
            for (var i = 0; i < boxes.Count; i++) {
                var b = boxes[i];
                var w = b.W ?? 0.0;
                var h = b.H ?? 0.0;
                if (x < b.X || y < b.Y || x > b.X + w || y > b.Y + h) {
                    continue;
                }
                var area = w * h;
                if (area < bestArea) {
                    bestArea = area;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>Total missed + should-reject + wrongly-rejected counts across all positions, for a summary line.</summary>
        public static (int missed, int shouldReject, int wronglyRejected) Counts(StarReviewRunLabels labels) {
            if (labels?.Positions == null) {
                return (0, 0, 0);
            }
            var missed = labels.Positions.Sum(p => p.Missed?.Count ?? 0);
            var reject = labels.Positions.Sum(p => p.ShouldReject?.Count ?? 0);
            var wronglyRejected = labels.Positions.Sum(p => p.WronglyRejected?.Count ?? 0);
            return (missed, reject, wronglyRejected);
        }

        /// <summary>
        /// Drops positions that carry no labels at all, so re-saving after an inadvertent navigate-through does not
        /// litter the file with empty buckets. Keeps the file minimal and stable across re-runs.
        /// </summary>
        public static void PruneEmptyPositions(StarReviewRunLabels labels) {
            if (labels?.Positions == null) {
                return;
            }
            labels.Positions.RemoveAll(p => (p.Missed == null || p.Missed.Count == 0)
                                            && (p.ShouldReject == null || p.ShouldReject.Count == 0)
                                            && (p.WronglyRejected == null || p.WronglyRejected.Count == 0));
        }

        private static StarReviewRunLabels TryParse(string path, out string error) {
            error = null;
            try {
                var parsed = JsonConvert.DeserializeObject<StarReviewRunLabels>(File.ReadAllText(path));
                if (parsed == null) {
                    return new StarReviewRunLabels { RunId = Path.GetFileNameWithoutExtension(path), RadiusPx = DefaultRadiusPx };
                }
                return parsed;
            } catch (Exception ex) {
                error = $"Could not parse label file '{path}': {ex.Message}";
                return null;
            }
        }

        private static void Normalize(StarReviewRunLabels labels) {
            labels.Positions ??= new List<StarReviewPositionLabels>();
            var fileRadius = labels.RadiusPx ?? DefaultRadiusPx;
            foreach (var p in labels.Positions) {
                p.Missed ??= new List<StarReviewLabelBox>();
                p.ShouldReject ??= new List<StarReviewLabelBox>();
                p.WronglyRejected ??= new List<StarReviewLabelBox>();

                // Legacy tolerance: a label loaded from an older point-only file carries x,y but no w,h. Back-fill it
                // with a small box of side 2·radiusPx CENTERED on the original point (so x,y becomes the box top-left)
                // — old files keep loading and immediately participate in box-containment scoring.
                var radius = p.RadiusPx ?? fileRadius;
                BackfillLegacyBoxes(p.Missed, radius);
                BackfillLegacyBoxes(p.ShouldReject, radius);
                BackfillLegacyBoxes(p.WronglyRejected, radius);
            }
            labels.Positions.Sort((a, b) => a.FocuserPosition.CompareTo(b.FocuserPosition));
        }

        /// <summary>Back-fills any legacy point-only label (no w/h) in <paramref name="boxes"/> with a 2·radiusPx box
        /// centered on its (x,y), rewriting x,y to the box top-left so it serializes as a real box thereafter.</summary>
        private static void BackfillLegacyBoxes(List<StarReviewLabelBox> boxes, double radiusPx) {
            if (boxes == null) {
                return;
            }
            var side = Math.Max(1e-9, 2.0 * radiusPx);
            foreach (var b in boxes) {
                if (b.HasSize) {
                    continue;
                }
                // The legacy x,y is the point center; convert to top-left + size.
                b.X -= side / 2.0;
                b.Y -= side / 2.0;
                b.W = side;
                b.H = side;
            }
        }

        private static string SanitizeFileName(string name) {
            if (string.IsNullOrEmpty(name)) {
                return "run";
            }
            foreach (var c in Path.GetInvalidFileNameChars()) {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
