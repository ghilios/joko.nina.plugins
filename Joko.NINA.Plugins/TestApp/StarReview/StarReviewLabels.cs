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

namespace TestApp.StarReview {

    /// <summary>
    /// The on-disk label JSON model, shaped EXACTLY as the T6 optimizer harness
    /// (<c>OptimizationDiagnosticRunner</c>) reads it via <c>--labels</c>: one file per run, named
    /// "&lt;runId&gt;.json", with the property names/casing below (matching T6's <c>[JsonProperty]</c> names).
    /// T7 (the interactive review tool) WRITES these; T6 READS them back to activate the recall/precision
    /// objective term — so the two property-name sets must stay identical. The round-trip is covered by a unit
    /// test that deserializes a T7-written file through a POCO carrying T6's exact attributes.
    /// <code>
    /// {
    ///   "runId": "attempt01",
    ///   "radiusPx": 6.0,                          // default radius for positions that omit one
    ///   "positions": [
    ///     {
    ///       "focuserPosition": 5000,
    ///       "radiusPx": 6.0,                       // optional per-position override
    ///       "missed":          [ { "x": 123.4, "y": 567.8 } ],  // false negatives to recover (recall)
    ///       "shouldReject":    [ { "x": 12.0,  "y": 34.0  } ],  // false positives to exclude (precision)
    ///       "wronglyRejected": [ { "x": 88.0,  "y": 90.0  } ]   // detected-but-gated candidates that should be KEPT (folds into recall)
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public sealed class StarReviewLabelPoint {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }

        public StarReviewLabelPoint() { }

        public StarReviewLabelPoint(double x, double y) {
            X = x;
            Y = y;
        }
    }

    public sealed class StarReviewPositionLabels {
        [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
        [JsonProperty("radiusPx")] public double? RadiusPx { get; set; }
        [JsonProperty("missed")] public List<StarReviewLabelPoint> Missed { get; set; } = new List<StarReviewLabelPoint>();
        [JsonProperty("shouldReject")] public List<StarReviewLabelPoint> ShouldReject { get; set; } = new List<StarReviewLabelPoint>();

        /// <summary>Detected-but-gate-rejected candidates the user judges should have been KEPT. Folds into recall
        /// (union with <see cref="Missed"/>) on the optimizer side — every wrongly-rejected point is a recall target.</summary>
        [JsonProperty("wronglyRejected")] public List<StarReviewLabelPoint> WronglyRejected { get; set; } = new List<StarReviewLabelPoint>();
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

        // Two label points are "the same" (so a toggle removes rather than re-adds) when within this many pixels.
        // Independent of the scoring radius; this is purely a UI hit-test for the undo-on-click behavior.
        public const double SamePointTolerancePx = 4.0;

        // T6 reads with default Newtonsoft settings (JsonConvert.DeserializeObject). Serialize indented and skip
        // nulls so an empty per-position radius override is simply absent (T6 falls back to the file default).
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string FileNameFor(string runId) => SanitizeFileName(runId) + ".json";

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
        /// Toggles a label point at (x,y): removes an existing point in <paramref name="points"/> within
        /// <see cref="SamePointTolerancePx"/>, otherwise adds a new one. Returns true if a point was ADDED, false
        /// if one was removed. Pure list mutation — used for the click-to-add / click-again-to-undo UX.
        /// </summary>
        public static bool TogglePoint(List<StarReviewLabelPoint> points, double x, double y) {
            var idx = NearestPointIndexWithin(points, x, y, SamePointTolerancePx);
            if (idx >= 0) {
                points.RemoveAt(idx);
                return false;
            }
            points.Add(new StarReviewLabelPoint(x, y));
            return true;
        }

        /// <summary>
        /// Index of the point in <paramref name="points"/> nearest to (x,y) within <paramref name="tolerancePx"/>,
        /// or -1 if none. Pure helper (used by the toggle hit-test and unit-tested directly).
        /// </summary>
        public static int NearestPointIndexWithin(List<StarReviewLabelPoint> points, double x, double y, double tolerancePx) {
            if (points == null || points.Count == 0) {
                return -1;
            }
            var tol2 = tolerancePx * tolerancePx;
            var bestIdx = -1;
            var bestD2 = double.MaxValue;
            for (var i = 0; i < points.Count; i++) {
                var dx = points[i].X - x;
                var dy = points[i].Y - y;
                var d2 = dx * dx + dy * dy;
                if (d2 <= tol2 && d2 < bestD2) {
                    bestD2 = d2;
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
            foreach (var p in labels.Positions) {
                p.Missed ??= new List<StarReviewLabelPoint>();
                p.ShouldReject ??= new List<StarReviewLabelPoint>();
                p.WronglyRejected ??= new List<StarReviewLabelPoint>();
            }
            labels.Positions.Sort((a, b) => a.FocuserPosition.CompareTo(b.FocuserPosition));
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
