#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestApp {

    /// <summary>
    /// Pure aggregation for the AF-bank verification report: bank-level medians and the NoiseClippingMultiplier
    /// recommendation that answers "was the shipped 4→2 default right, or should NC be derived adaptively from the
    /// live noise?". STUB until the GREEN implementation lands.
    /// </summary>
    internal static class BankVerifyAggregate {

        /// <summary>Median over the finite values (NaN/Inf skipped); NaN if none.</summary>
        public static double Median(IEnumerable<double> values) {
            var xs = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (xs.Count == 0) return double.NaN;
            int m = xs.Count / 2;
            return xs.Count % 2 == 1 ? xs[m] : 0.5 * (xs[m - 1] + xs[m]);
        }

        public struct NcPoint {
            public double Nc;
            public double MedianRecallHigh;
            public double MedianPrecision;
        }

        public struct NcRecommendation {
            public double Nc;
            public string Rationale;
            public bool ConsiderAdaptive;
        }

        /// <summary>
        /// Picks the recommended default NoiseClippingMultiplier from the swept points. Recall rises as NC falls,
        /// so among the NC values whose bank-median precision clears <paramref name="precisionFloor"/> we take the
        /// LOWEST NC. If the lowest swept NC itself clears the floor, recall is still climbing at the bottom of the
        /// range → flag that an even lower / per-frame-adaptive NC may be warranted. If none clear the floor, fall
        /// back to the highest-precision NC and flag adaptive.
        /// </summary>
        public static NcRecommendation RecommendNoiseClip(IReadOnlyList<NcPoint> points, double precisionFloor) {
            if (points == null || points.Count == 0) {
                return new NcRecommendation { Nc = double.NaN, Rationale = "no NC sweep points", ConsiderAdaptive = false };
            }
            var byNc = points.OrderBy(p => p.Nc).ToList();
            var passing = byNc.Where(p => p.MedianPrecision >= precisionFloor).ToList();
            if (passing.Count == 0) {
                var best = byNc.OrderByDescending(p => p.MedianPrecision).First();
                return new NcRecommendation {
                    Nc = best.Nc,
                    Rationale = $"no swept NC cleared the precision floor {precisionFloor:F2}; highest-precision NC={best.Nc:F0} " +
                        $"(precision {best.MedianPrecision:F2}, recall@high {best.MedianRecallHigh:F2}) — precision-limited across the bank",
                    ConsiderAdaptive = true
                };
            }
            // Recall rises as NC falls → lowest NC clearing the floor maximizes recall.
            var chosen = passing.First();
            var lowestSwept = byNc.First();
            var atRangeFloor = chosen.Nc <= lowestSwept.Nc + 1e-9;
            var rationale = atRangeFloor
                ? $"NC={chosen.Nc:F0} (recall@high {chosen.MedianRecallHigh:F2}, precision {chosen.MedianPrecision:F2}) is the lowest swept value and still " +
                  $"clears the precision floor {precisionFloor:F2} — recall is still rising at the bottom of the range, so a lower / per-frame-adaptive NC may help"
                : $"NC={chosen.Nc:F0} (recall@high {chosen.MedianRecallHigh:F2}, precision {chosen.MedianPrecision:F2}) is the lowest NC clearing the precision " +
                  $"floor {precisionFloor:F2}; lower NC over-recalled into noise (precision dropped below the floor)";
            return new NcRecommendation { Nc = chosen.Nc, Rationale = rationale, ConsiderAdaptive = atRangeFloor };
        }
    }

    /// <summary>
    /// Pure-logic helpers for the AF-bank verification orchestrators (bank-clean / bank-donut-meta / bank-verify),
    /// kept free of NINA / image-load coupling so they are unit-testable against in-memory inputs (linked into the
    /// test project the same way <see cref="OptimizationRunDiscovery"/> is).
    /// </summary>
    internal static class BankCleanup {

        public readonly struct Decision {
            public Decision(bool keep, string reason) { Keep = keep; Reason = reason; }
            public bool Keep { get; }
            public string Reason { get; }
        }

        private static readonly Regex AutoFocusReportRegex =
            new Regex(@"^autofocus_report_Region\d+\.json$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Classifies one file (path relative to its run folder, forward-slashed) as KEEP or DELETE. The classifier
        /// is CONSERVATIVE: it deletes only files matching a known stale-artifact denylist (all regenerated per
        /// config by optimize / golden eval / inspect-align / contamination) and keeps everything it does not
        /// recognize. Protected inputs (AF sweep frames, autofocus_report_Region*.json, the labels/ subtree, our
        /// *.golden.json reference sidecars, run_meta.json, *.linear.fits export sidecars) are kept first.
        /// </summary>
        public static Decision Classify(string relativePathForwardSlashed) {
            var rel = (relativePathForwardSlashed ?? string.Empty).Replace('\\', '/');
            var name = rel.Contains('/') ? rel.Substring(rel.LastIndexOf('/') + 1) : rel;
            var lower = name.ToLowerInvariant();

            // 1) Protected: anything under a labels/ directory (human review labels + their crops).
            if (Regex.IsMatch(rel, @"(^|/)labels/", RegexOptions.IgnoreCase)) {
                return new Decision(true, "labels");
            }
            // 2) Protected: our reference golden sidecars and per-run donut metadata.
            if (lower.EndsWith(".golden.json", StringComparison.Ordinal)) return new Decision(true, "golden sidecar");
            if (lower == "run_meta.json") return new Decision(true, "run metadata");
            // Synthetic-bank per-DATASET metadata (matchRadiusPx, expected-optimal bootstrap params, etc.). It lives
            // at the dataset root, one level above the run folder this classifier walks, so this entry is pure
            // insurance in case a future caller ever runs the walk from the dataset root instead.
            if (lower == "synthetic_meta.json") return new Decision(true, "synthetic dataset metadata");
            // 3) Protected: AF report JSON (run config + region geometry + step size — needed by golden eval).
            if (AutoFocusReportRegex.IsMatch(name)) return new Decision(true, "autofocus report");
            // 4) Protected: AF sweep frames + our linear-export sidecars.
            if (lower.EndsWith(".linear.fits", StringComparison.Ordinal)) return new Decision(true, "linear export");
            var ext = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            if ((ext.Equals(".fits", StringComparison.OrdinalIgnoreCase) || ext.Equals(".fit", StringComparison.OrdinalIgnoreCase)
                 || ext.Equals(".xisf", StringComparison.OrdinalIgnoreCase)) && OptimizationRunDiscovery.ImageFileRegex.IsMatch(stem)) {
                return new Decision(true, "AF sweep frame");
            }

            // 5) Denylist: stale per-config artifacts regenerated by the harnesses.
            if (lower.Contains("_star_detection_result")) return new Decision(false, "stale star_detection_result");
            if (lower == "optimized_settings.json") return new Decision(false, "stale optimized_settings handoff");
            // Its NINA-loadable twin, written by the same optimize pass. Both must be cleaned together: leaving this
            // one behind while deleting its sibling would let a stale landing be imported into the app long after the
            // arm that produced it was cleaned away.
            if (lower == "hocusfocus_star_detection.json") return new Decision(false, "stale star-detection handoff");
            if (lower.EndsWith(".png", StringComparison.Ordinal)) return new Decision(false, "diagnostic PNG");
            if (lower.StartsWith("optimize_") || lower == "aggregate_summary.txt") return new Decision(false, "stale optimize output");
            if (lower.StartsWith("golden_eval") || (lower.StartsWith("detected_f") && lower.EndsWith(".csv"))) return new Decision(false, "stale golden eval output");
            if (lower.StartsWith("contamination_") || lower == "gr_sweep.csv" || lower == "sweep.csv") return new Decision(false, "stale contamination output");
            if (lower == "inspect_align.txt") return new Decision(false, "stale inspect-align output");
            if (lower == "diagnose_labels.txt") return new Decision(false, "stale diagnose-labels output");

            // 6) Anything else is kept — bank-clean must never delete data it does not recognize.
            return new Decision(true, "unrecognized (kept)");
        }
    }

    /// <summary>
    /// Refined donut-aware decision (plan Step 3). The donut/peak local-maxima fraction alone over-flags
    /// mildly-defocused runs (cwhite frac≈1.53 yet donuts were marginal and donut-aware loosened its sensor fit),
    /// so we require BOTH a high fraction AND heavy defocus on the extreme (most out-of-focus) frames — a high
    /// extreme-frame median HFR OR a large donut bounding box. STUB until the GREEN implementation lands.
    /// </summary>
    internal static class DonutHeuristic {

        /// <summary>Min donut/peak local-maxima fraction (the original over-eager rule's only gate).</summary>
        public const double FracThreshold = 1.0;

        /// <summary>Extreme-frame median HFR (px) marking heavy defocus.</summary>
        public const double HeavyDefocusHFR = 9.0;

        /// <summary>Median donut bounding-box size (px) on the extreme frames marking large defocused disks.</summary>
        public const double LargeDonutBBoxPx = 24.0;

        public struct Signals {
            public double DonutPeakFracMax;
            public double ExtremeFrameMedianHFR;
            public double ExtremeDonutBBoxMedianPx;
        }

        public readonly struct DonutDecision {
            public DonutDecision(bool donutAware, string reason) { DonutAware = donutAware; Reason = reason; }
            public bool DonutAware { get; }
            public string Reason { get; }
        }

        public static DonutDecision Decide(Signals s) {
            var highFrac = s.DonutPeakFracMax >= FracThreshold;
            var heavyHfr = s.ExtremeFrameMedianHFR >= HeavyDefocusHFR;
            var largeBbox = s.ExtremeDonutBBoxMedianPx >= LargeDonutBBoxPx;
            var heavyDefocus = heavyHfr || largeBbox;
            var donutAware = highFrac && heavyDefocus;
            var reason = donutAware
                ? $"donut/peak frac {s.DonutPeakFracMax:F2}>={FracThreshold:F1} AND heavy defocus (" +
                  $"extreme HFR {s.ExtremeFrameMedianHFR:F1}{(heavyHfr ? ">=" : "<")}{HeavyDefocusHFR:F1}" +
                  $"{(largeBbox ? $", donut bbox {s.ExtremeDonutBBoxMedianPx:F0}>={LargeDonutBBoxPx:F0}px" : "")})"
                : !highFrac
                    ? $"donut/peak frac {s.DonutPeakFracMax:F2}<{FracThreshold:F1} (not donut-dominated)"
                    : $"high frac {s.DonutPeakFracMax:F2} but mild defocus (extreme HFR {s.ExtremeFrameMedianHFR:F1}<{HeavyDefocusHFR:F1}, " +
                      $"donut bbox {s.ExtremeDonutBBoxMedianPx:F0}<{LargeDonutBBoxPx:F0}px) — cwhite-style over-flag avoided";
            return new DonutDecision(donutAware, reason);
        }
    }

    /// <summary>
    /// V-P2: reads the synthetic bank's per-DATASET <c>synthetic_meta.json</c> for knobs the generator derived from
    /// ground truth — today just <c>matchRadiusPx</c> (see docs/synthetic-af-bank-design.md's "V-P2 / V-P3"). Pure
    /// file I/O + JSON, no NINA coupling, so it lives here alongside <see cref="BankCleanup"/> and is unit-testable
    /// the same way.
    /// </summary>
    internal static class SyntheticDatasetMeta {

        /// <summary>File name, per <c>TestApp synth-bank</c>'s writer (SynthBankRunner.WriteSyntheticMeta).</summary>
        public const string FileName = "synthetic_meta.json";

        /// <summary>
        /// Looks for <c>synthetic_meta.json</c> starting at <paramref name="frameDir"/> (a run's frame folder,
        /// e.g. <c>&lt;bank&gt;/&lt;datasetId&gt;/attempt01/</c>) and walking UPWARD one directory at a time. The
        /// generator writes the file at the DATASET ROOT — one level above <c>attempt01</c> — deliberately outside
        /// the per-run walk both <c>bank-clean</c> and <c>optimize --per-run</c> use, so a single parent-directory
        /// check covers the current layout; the walk continues (bounded by, and including, <paramref
        /// name="bankRoot"/>) so a future nested-attempt layout would still be found without a code change here.
        /// The real bank has no such file at any level, so this is a silent no-op there — the entire point of
        /// keeping the synthetic-only knob out of the real bank's code path.
        /// </summary>
        /// <param name="frameDir">Directory holding the run's frames (NOT the frame file itself).</param>
        /// <param name="bankRoot">The bank root passed to <c>--runs</c>; the walk never looks above it.</param>
        /// <returns>The parsed <c>matchRadiusPx</c>, or null when no meta file was found or it had no such field.</returns>
        public static double? TryReadMatchRadiusPx(string frameDir, string bankRoot) {
            if (string.IsNullOrWhiteSpace(frameDir)) {
                return null;
            }
            var rootFull = string.IsNullOrWhiteSpace(bankRoot)
                ? null
                : Path.GetFullPath(bankRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dir = Path.GetFullPath(frameDir);
            while (dir != null) {
                var candidate = Path.Combine(dir, FileName);
                if (File.Exists(candidate)) {
                    try {
                        var o = JObject.Parse(File.ReadAllText(candidate));
                        var v = o["matchRadiusPx"];
                        return v != null && v.Type != JTokenType.Null ? (double?)v : null;
                    } catch {
                        // Malformed sidecar: treat exactly like "absent" rather than aborting the run over a
                        // knob that has a well-defined fallback (CLI --match-radius / the 12px default).
                        return null;
                    }
                }
                var trimmed = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (rootFull != null && string.Equals(trimmed, rootFull, StringComparison.OrdinalIgnoreCase)) {
                    break; // reached the bank root without finding one — do not search above the bank
                }
                var parent = Path.GetDirectoryName(dir);
                if (parent == dir) {
                    break; // filesystem root
                }
                dir = parent;
            }
            return null;
        }
    }
}
