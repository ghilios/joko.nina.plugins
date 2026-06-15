#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestApp {

    /// <summary>
    /// Pure (no NINA / no image-load coupling) run discovery for the optimize harness. Operates purely on
    /// directory structure + AF-frame filenames so it is unit-testable against a temp directory tree.
    ///
    /// <para>Real AF save folders look like <c>&lt;setup&gt;/attempt01/&lt;sweep frames&gt;</c> with sibling
    /// single-frame validation captures (<c>&lt;setup&gt;/final/&lt;1 frame&gt;</c>,
    /// <c>&lt;setup&gt;/initial/&lt;1 frame&gt;</c>) that are NOT runs (1 focuser position each → can't fit →
    /// σ_focus NaN → corrupts the joint objective). Runs can also be nested 1–3 levels under the root, and some
    /// <c>attempt*</c> folders contain ZERO sweep frames (only annotation / result-JSON artifacts) and must be
    /// skipped. The >= 3 distinct-focuser-positions guard (mirroring
    /// <c>RunEvaluationData.MinPositionsForFit = 3</c>) is the key filter that excludes final/initial.</para>
    /// </summary>
    internal static class OptimizationRunDiscovery {

        /// <summary>Mirror of <c>RunEvaluationData.MinPositionsForFit</c>: the minimum distinct focuser
        /// positions a folder needs before it can be fit (and therefore counted as a usable run).</summary>
        public const int MinPositionsForFit = 3;

        /// <summary>How deep below the runs root to search for <c>attempt*</c> folders.</summary>
        public const int DefaultMaxDepth = 4;

        /// <summary>An AF sweep frame the AF engine writes, e.g.
        /// "0_Frame1_BitDepth16_Bayered0_Focuser5000.fits" (the trailing _HFRxxx is optional).</summary>
        public static readonly Regex ImageFileRegex = new Regex(
            @"^(?<IMAGE_INDEX>\d+)_Frame(?<FRAME_NUMBER>\d+)_BitDepth(?<BITDEPTH>\d+)_Bayered(?<BAYERED>\d)_Focuser(?<FOCUSER>\d+)(_HFR(?<HFR>(\d+)(\.\d+)?))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AttemptDirRegex = new Regex(@"^attempt\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> FrameExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".fits", ".fit", ".xisf"
        };

        public sealed class FrameRef {
            public string Path;
            public int FocuserPosition;
        }

        public sealed class DiscoveredRun {
            /// <summary>Readable path relative to the runs root (e.g. "toml999/attempt01"), forward-slashed.</summary>
            public string RunId;

            public List<FrameRef> Frames;

            public int DistinctPositions => Frames.Select(f => f.FocuserPosition).Distinct().Count();
        }

        public sealed class SkippedFolder {
            /// <summary>Readable path relative to the runs root, forward-slashed.</summary>
            public string RelativePath;

            public string Reason;
        }

        public sealed class DiscoveryResult {
            public List<DiscoveredRun> Runs = new List<DiscoveredRun>();
            public List<SkippedFolder> Skipped = new List<SkippedFolder>();
        }

        /// <summary>
        /// Discovers usable runs under <paramref name="runsDir"/>. Algorithm:
        /// <list type="number">
        /// <item>Back-compat: if the runs root itself directly holds AF sweep frames (>= 3 distinct positions),
        ///   the root is a single run (so pointing directly at an attempt folder works). If it has frames but
        ///   too few positions, it is recorded as skipped (not silently dropped).</item>
        /// <item>Otherwise, recursively search to <paramref name="maxDepth"/> for directories whose name matches
        ///   <c>attempt\d+</c> (case-insensitive). Each is a candidate: it becomes a run iff it has >= 3 distinct
        ///   focuser positions, else it is skipped with a reason (no sweep frames / too few positions).</item>
        /// <item>Fallback: if NO <c>attempt*</c> folders are found anywhere, treat each immediate subfolder with
        ///   >= 3 distinct positions as a run (unusual layouts); subfolders with frames but too few positions are
        ///   skipped.</item>
        /// </list>
        /// Runs are returned in ordinal RunId order (deterministic). RunId / RelativePath are forward-slashed
        /// paths relative to the runs root.
        /// </summary>
        public static DiscoveryResult Discover(string runsDir, int minPositions = MinPositionsForFit, int maxDepth = DefaultMaxDepth) {
            var result = new DiscoveryResult();
            var root = new DirectoryInfo(runsDir);
            if (!root.Exists) {
                return result;
            }

            // 1) Back-compat: the runs root itself directly holds AF sweep frames.
            var rootFrames = MatchFrames(root);
            if (rootFrames.Count > 0) {
                if (DistinctPositions(rootFrames) >= minPositions) {
                    result.Runs.Add(new DiscoveredRun { RunId = root.Name, Frames = rootFrames });
                } else {
                    result.Skipped.Add(new SkippedFolder {
                        RelativePath = root.Name,
                        Reason = $"only {DistinctPositions(rootFrames)} focuser position(s) (need >={minPositions})"
                    });
                }
                Finalize(result);
                return result;
            }

            // 2) Recursive attempt* search.
            var attemptDirs = new List<DirectoryInfo>();
            CollectAttemptDirs(root, root, 0, maxDepth, attemptDirs);
            if (attemptDirs.Count > 0) {
                foreach (var dir in attemptDirs) {
                    var rel = RelativePath(root, dir);
                    var frames = MatchFrames(dir);
                    if (frames.Count == 0) {
                        result.Skipped.Add(new SkippedFolder { RelativePath = rel, Reason = "no sweep frames" });
                        continue;
                    }
                    var positions = DistinctPositions(frames);
                    if (positions < minPositions) {
                        result.Skipped.Add(new SkippedFolder {
                            RelativePath = rel,
                            Reason = $"only {positions} focuser position(s) (need >={minPositions})"
                        });
                        continue;
                    }
                    result.Runs.Add(new DiscoveredRun { RunId = rel, Frames = frames });
                }
                Finalize(result);
                return result;
            }

            // 3) Fallback: no attempt* folders anywhere — treat each immediate subfolder with enough positions
            //    as a run.
            foreach (var sub in root.EnumerateDirectories()) {
                var rel = RelativePath(root, sub);
                var frames = MatchFrames(sub);
                if (frames.Count == 0) {
                    continue; // no frames at all is not a notable skip in fallback mode
                }
                var positions = DistinctPositions(frames);
                if (positions < minPositions) {
                    result.Skipped.Add(new SkippedFolder {
                        RelativePath = rel,
                        Reason = $"only {positions} focuser position(s) (need >={minPositions})"
                    });
                    continue;
                }
                result.Runs.Add(new DiscoveredRun { RunId = rel, Frames = frames });
            }
            Finalize(result);
            return result;
        }

        private static void Finalize(DiscoveryResult result) {
            result.Runs = result.Runs.OrderBy(r => r.RunId, StringComparer.Ordinal).ToList();
            result.Skipped = result.Skipped.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ToList();
        }

        /// <summary>Recursively collects directories named "attempt\d+" up to <paramref name="maxDepth"/> levels
        /// below the root. Depth 0 == the root's immediate children.</summary>
        private static void CollectAttemptDirs(DirectoryInfo root, DirectoryInfo current, int depth, int maxDepth, List<DirectoryInfo> sink) {
            if (depth > maxDepth) {
                return;
            }
            IEnumerable<DirectoryInfo> children;
            try {
                children = current.EnumerateDirectories();
            } catch {
                return; // unreadable directory — skip the subtree
            }
            foreach (var child in children) {
                if (AttemptDirRegex.IsMatch(child.Name)) {
                    sink.Add(child);
                    // Do not descend into an attempt folder — its contents are frames, not nested runs.
                    continue;
                }
                CollectAttemptDirs(root, child, depth + 1, maxDepth, sink);
            }
        }

        private static List<FrameRef> MatchFrames(DirectoryInfo dir) {
            var frames = new List<FrameRef>();
            if (!dir.Exists) {
                return frames;
            }
            FileInfo[] files;
            try {
                files = dir.GetFiles();
            } catch {
                return frames;
            }
            foreach (var file in files.OrderBy(f => f.Name, StringComparer.Ordinal)) {
                if (!FrameExtensions.Contains(file.Extension)) {
                    continue;
                }
                var m = ImageFileRegex.Match(Path.GetFileNameWithoutExtension(file.Name));
                if (!m.Success) {
                    continue;
                }
                if (!int.TryParse(m.Groups["FOCUSER"].Value, out var pos)) {
                    continue;
                }
                frames.Add(new FrameRef { Path = file.FullName, FocuserPosition = pos });
            }
            return frames;
        }

        private static int DistinctPositions(List<FrameRef> frames) => frames.Select(f => f.FocuserPosition).Distinct().Count();

        /// <summary>Forward-slashed path of <paramref name="dir"/> relative to <paramref name="root"/>.</summary>
        private static string RelativePath(DirectoryInfo root, DirectoryInfo dir) {
            var rel = Path.GetRelativePath(root.FullName, dir.FullName);
            if (string.IsNullOrEmpty(rel) || rel == ".") {
                return dir.Name;
            }
            return rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        /// <summary>Sanitizes a RunId for use as a filename / folder-name prefix (slashes → underscores too).</summary>
        public static string SanitizeForFileName(string name) {
            if (string.IsNullOrEmpty(name)) {
                return name;
            }
            name = name.Replace('/', '_').Replace('\\', '_');
            foreach (var c in Path.GetInvalidFileNameChars()) {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
