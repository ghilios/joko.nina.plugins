#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TestApp.SynthBank {

    /// <summary>
    /// One scored frame's truth-related disclosure numbers. Pure counters plus the two ratios derived from them,
    /// so a run total is the element-wise sum of its frames and the ratios are recomputed from the summed
    /// counters rather than averaged (see <see cref="TruthDisclosure.Aggregate"/>).
    /// </summary>
    public sealed class TruthDisclosureFrame {

        /// <summary>True iff a per-frame truth sidecar was found for this frame. Drives
        /// <see cref="TruthDisclosure.ScoringMode"/> and gates the null control, exactly as
        /// <c>BankVerifyRunner</c>'s <c>td != null</c> does.</summary>
        public bool HasTruth { get; init; }

        /// <summary>Protection AVAILABLE: <c>TruthProtection.BuildProtectionBoxes(...).Count</c> — the
        /// real-but-unboxed truth stars (<c>omitted</c> / <c>merged-into</c>) a detection COULD have been
        /// protected by. Says how large the correction could be, not how large it was.</summary>
        public int ProtectedStars { get; init; }

        /// <summary>Protection EXERCISED: how many detections <c>TruthProtection.ExcludeProtected</c> actually
        /// removed from the false-positive count. <c>bank-verify</c> reports only <see cref="ProtectedStars"/>,
        /// which is the availability, and a reader weighing a precision figure needs this one instead: a run can
        /// carry hundreds of protected stars and exercise none of them.</summary>
        public int ProtectedDetections { get; init; }

        /// <summary>Scored false positives sitting within the match radius of a REAL rendered truth star. Truth
        /// is complete by construction, so this has an exact answer and the answer must be 0; non-zero is the F31
        /// signature and the precision from that run is not trustworthy.</summary>
        public int TruthViolations { get; init; }

        /// <summary>Accepted stars this frame's detector run produced — the denominator
        /// <see cref="ScoredFraction"/> is a fraction of.</summary>
        public int Detections { get; init; }

        /// <summary>True positives that entered the precision ratio.</summary>
        public int ScoredTp { get; init; }

        /// <summary>False positives that entered the precision ratio, i.e. AFTER unresolved+truth exclusion.</summary>
        public int ScoredFp { get; init; }

        /// <summary>Null-control true positives (the shifted detections re-scored against the unshifted
        /// reference). 0 when no truth sidecar exists, in which case no null is defined.</summary>
        public int NullTp { get; init; }

        /// <summary>Null-control false positives — see <see cref="NullTp"/>.</summary>
        public int NullFp { get; init; }

        /// <summary>Fraction of <see cref="Detections"/> that entered the precision ratio (the rest landed on
        /// protected or unresolved reference objects and are unjudgeable). Not a bias — protection removes a
        /// detection from both numerator and denominator — but precision rests on a smaller sample as this
        /// falls, and at Sensitivity 0 it reaches ~0.57.</summary>
        public double ScoredFraction => Detections > 0 ? (double)(ScoredTp + ScoredFp) / Detections : double.NaN;

        /// <summary>NULL CONTROL: precision after translating every detection with wraparound, destroying
        /// correspondence with the frame while preserving count and clustering. This is the score chance alone
        /// earns, and therefore the floor the metric cannot read below. A precision of 1.000 means something only
        /// when this is near 0 — the first cut of the F31 repair read 1.000 everywhere BECAUSE it was saturated,
        /// which the precision column alone cannot distinguish. NaN on the real bank (no truth sidecar, so no
        /// synthetic null is defined).</summary>
        public double PrecisionNull => (NullTp + NullFp) > 0 ? (double)NullTp / (NullTp + NullFp) : double.NaN;
    }

    /// <summary>
    /// The four truth-related honesty disclosures a golden-scored report owes its reader, computed once and
    /// worded once so the two harnesses that score against the golden cannot drift apart.
    ///
    /// <para><b>Why this exists.</b> <c>bank-verify</c> reports <c>scoringMode</c> + <c>protectedStars</c>,
    /// <c>truthViolations</c>, <c>scoredFraction</c> and <c>precisionNull</c>. <c>golden eval</c> — the instrument
    /// that produced the published results table — reported NONE of them, so every stored
    /// <c>golden_eval.txt</c> was written by a truth-protected instrument that could not say it was
    /// truth-protected, could not say what chance alone would have scored, could not say whether any false
    /// positive sat on a real star, and could not say what fraction of the detections it judged. The wording
    /// below is <c>BankVerifyRunner</c>'s, verbatim, for exactly that reason.</para>
    ///
    /// <para><b>REPORT-ONLY, structurally.</b> <see cref="Measure"/> is handed the live scoring path's ALREADY
    /// COMPUTED results (the match, the false positives before and after protection) and never re-runs or
    /// re-orders them, so it cannot move a scored number. The null control runs on a defensive COPY of the
    /// detection list for the same reason.</para>
    /// </summary>
    public static class TruthDisclosure {

        /// <summary>Scoring mode when at least one per-frame truth sidecar was found and its real-but-unboxed
        /// stars were excluded from false-positive scoring. Reports differing here are NOT comparable on
        /// precision.</summary>
        public const string ModeTruthProtected = "golden+truth-protected";

        /// <summary>Scoring mode with no truth sidecar (the real bank, and every report written before F31).</summary>
        public const string ModeGolden = "golden";

        /// <summary>
        /// The mode ACTUALLY used, derived from how many frames carried a truth sidecar. Never hardcode a label
        /// at a call site: a hardcoded one silently misattributes every stored report, which is the whole defect
        /// this class repairs.
        /// </summary>
        public static string ScoringMode(int framesWithTruth) =>
            framesWithTruth > 0 ? ModeTruthProtected : ModeGolden;

        /// <summary>The parenthetical that follows the mode. Verbatim from <c>BankVerifyRunner</c> so a reader
        /// (or a grep) cannot tell the two harnesses apart.</summary>
        public static string ScoringSuffix(int framesWithTruth, int protectedStars) =>
            framesWithTruth > 0
                ? $" ({protectedStars} real-but-unboxed truth stars protected from FP scoring across {framesWithTruth} frames)"
                : " (no truth sidecars; false positives scored against the golden alone)";

        /// <summary>The console line, verbatim from <c>BankVerifyRunner</c>'s "scoring:" line.</summary>
        public static string ScoringLine(int framesWithTruth, int protectedStars) =>
            $"  scoring: {ScoringMode(framesWithTruth)}{ScoringSuffix(framesWithTruth, protectedStars)}";

        /// <summary>Protection EXERCISED, on its own line so <see cref="ScoringLine"/> stays verbatim. Availability
        /// (<c>protectedStars</c>) without exercise is not readable as a correction size.</summary>
        public static string ProtectedDetectionsLine(int protectedDetections, int protectedStars) =>
            $"  protectedDetections: {protectedDetections} (detections excluded from false-positive scoring; "
            + $"protection EXERCISED, against {protectedStars} available)";

        /// <summary>Loud on purpose, and verbatim from <c>BankVerifyRunner</c>. This is the exact shape of F31,
        /// and four harness-calibration bugs have now been found by someone happening to look rather than by
        /// anything failing.</summary>
        public static string ViolationLine(string label, int truthViolations) =>
            $"    !! [{label}] {truthViolations} scored false positive(s) sit on a REAL rendered star "
            + "-- the precision metric is charging for correct detections again (F31). Precision from this run is not trustworthy.";

        /// <summary>
        /// Measures one frame's disclosure. Everything the live scoring path already produced is passed IN —
        /// nothing here recomputes a scored number.
        /// </summary>
        /// <param name="truthDispositions">The frame's truth sidecar, or null when none exists (the real bank).</param>
        /// <param name="goldenRects">The golden's required-find boxes, exactly as the live path built them.</param>
        /// <param name="unresolvedRects">The golden's own <c>unresolved</c> boxes, exactly as the live path built them.</param>
        /// <param name="detected">The live detection list. Treated as read-only; the null runs on a copy.</param>
        /// <param name="falsePositivesBeforeProtection">False positives after <c>ExcludeUnresolved</c> but before
        /// <c>ExcludeProtected</c> — the difference against <paramref name="falsePositivesScored"/> IS
        /// <c>protectedDetections</c>.</param>
        /// <param name="falsePositivesScored">The false positives that were actually charged.</param>
        /// <param name="truePositives">The matched pairs that were actually credited.</param>
        /// <param name="matchMode">The run's OWN match mode — the null must be scored the way the live path was,
        /// or it is a null for a different metric.</param>
        public static TruthDisclosureFrame Measure(
                IReadOnlyList<SyntheticStarDisposition> truthDispositions,
                IReadOnlyList<RectD> goldenRects,
                IReadOnlyList<RectD> unresolvedRects,
                IReadOnlyList<DetBox> detected,
                IReadOnlyList<int> falsePositivesBeforeProtection,
                IReadOnlyList<int> falsePositivesScored,
                int truePositives,
                GoldenMatchMode matchMode,
                double tau,
                double matchRadius,
                int frameWidth,
                int frameHeight) {

            // Protection AVAILABLE. Same call, same radius, as the exclusion itself uses.
            var protectedStars = TruthProtection.BuildProtectionBoxes(truthDispositions, matchRadius).Count;

            // Protection EXERCISED. ExcludeProtected only ever removes, so this is a count, not a difference of
            // two unrelated quantities; Max(0, ..) is belt-and-braces against a caller passing them swapped.
            var beforeCount = falsePositivesBeforeProtection?.Count ?? 0;
            var scoredFp = falsePositivesScored?.Count ?? 0;
            var protectedDetections = Math.Max(0, beforeCount - scoredFp);

            // The F31 signature, counted rather than reasoned about: a scored false positive that sits on a REAL
            // rendered star. Complete by construction, so this has an exact answer and the answer must be 0.
            var truthViolations = TruthProtection.CountWithinRadius(
                falsePositivesScored, detected, TruthProtection.AllCenters(truthDispositions), matchRadius);

            // The NULL CONTROL. Same detections, translated with wraparound: whatever precision survives is
            // chance coincidence. A metric reading 1.000 with a null near 0 is measuring; one reading 1.000 with a
            // high null is saturated, which is how the first cut of the F31 repair failed.
            //
            // The shift runs on a COPY of the live detection list. ShiftForNullControl already allocates its own
            // output, but this is a report-only path bolted onto a scoring loop whose bytes are under a
            // do-no-harm gate: the copy makes "the null cannot touch the live list" a property of the call site
            // rather than a fact about the callee that a future edit could quietly break.
            int nullTp = 0, nullFp = 0;
            if (truthDispositions != null) {
                var liveCopy = detected == null ? new List<DetBox>() : new List<DetBox>(detected);
                var shifted = TruthProtection.ShiftForNullControl(liveCopy, frameWidth, frameHeight);
                var nullMatch = GoldenMatch.Match(goldenRects, shifted, matchMode, tau, matchRadius);
                var nullFalsePositives = TruthProtection.ExcludeProtected(
                    GoldenMatch.ExcludeUnresolved(nullMatch.FalsePositives, shifted, unresolvedRects),
                    shifted, TruthProtection.ProtectionCenters(truthDispositions), matchRadius);
                nullTp = nullMatch.Pairs.Count;
                nullFp = nullFalsePositives.Count;
            }

            return new TruthDisclosureFrame {
                HasTruth = truthDispositions != null,
                ProtectedStars = protectedStars,
                ProtectedDetections = protectedDetections,
                TruthViolations = truthViolations,
                Detections = detected?.Count ?? 0,
                ScoredTp = truePositives,
                ScoredFp = scoredFp,
                NullTp = nullTp,
                NullFp = nullFp
            };
        }

        /// <summary>The run total: counters summed element-wise, ratios recomputed from the summed counters (a
        /// mean of per-frame ratios would weight a 3-detection frame like a 900-detection one).</summary>
        public static TruthDisclosureFrame Aggregate(IEnumerable<TruthDisclosureFrame> frames) {
            var list = frames?.Where(f => f != null).ToList() ?? new List<TruthDisclosureFrame>();
            return new TruthDisclosureFrame {
                HasTruth = list.Any(f => f.HasTruth),
                ProtectedStars = list.Sum(f => f.ProtectedStars),
                ProtectedDetections = list.Sum(f => f.ProtectedDetections),
                TruthViolations = list.Sum(f => f.TruthViolations),
                Detections = list.Sum(f => f.Detections),
                ScoredTp = list.Sum(f => f.ScoredTp),
                ScoredFp = list.Sum(f => f.ScoredFp),
                NullTp = list.Sum(f => f.NullTp),
                NullFp = list.Sum(f => f.NullFp)
            };
        }

        /// <summary>How many of the scored frames carried a truth sidecar — the input to
        /// <see cref="ScoringMode"/> and to the "across N frames" clause.</summary>
        public static int FramesWithTruth(IEnumerable<TruthDisclosureFrame> frames) =>
            frames?.Count(f => f != null && f.HasTruth) ?? 0;

        /// <summary>The header of the appended <c>golden_eval_frames.csv</c> columns. APPENDED at the end, never
        /// inserted: the scorers across nine wave roots index this file BY POSITION, so an inserted column
        /// silently re-labels every number to their right.</summary>
        public const string CsvHeaderSuffix = "protectedStars,protectedDetections,scoredFraction,precisionNull";

        /// <summary>One frame's appended CSV cells, in <see cref="CsvHeaderSuffix"/> order.</summary>
        public static string CsvRowSuffix(TruthDisclosureFrame f) =>
            $"{f.ProtectedStars},{f.ProtectedDetections},{Fmt(f.ScoredFraction)},{Fmt(f.PrecisionNull)}";

        /// <summary>The stored-report block. Every one of the four disclosures, plus the exercised-protection
        /// count bank-verify omits, so a reader of a stored <c>golden_eval.txt</c> can weigh its precision
        /// figure without re-deriving anything or knowing this class exists.</summary>
        public static IReadOnlyList<string> ReportLines(string label, TruthDisclosureFrame total, int framesWithTruth) {
            var lines = new List<string> {
                "TRUTH PROTECTION (F31) -- what the precision above was scored under:",
                $"  scoringMode: {ScoringMode(framesWithTruth)}{ScoringSuffix(framesWithTruth, total.ProtectedStars)}",
                $"  protectedStars: {total.ProtectedStars} (protection AVAILABLE: real-but-unboxed truth stars, summed over frames)",
                ProtectedDetectionsLine(total.ProtectedDetections, total.ProtectedStars),
                $"  truthViolations: {total.TruthViolations} (scored false positives sitting on a REAL rendered star -- must be 0)",
                $"  scoredFraction: {Fmt(total.ScoredFraction)} ({total.ScoredTp + total.ScoredFp} of {total.Detections} detections entered the precision ratio)",
                $"  precisionNull: {Fmt(total.PrecisionNull)} (precision chance alone earns: the same detections shifted by "
                    + $"({TruthProtection.NullShiftX},{TruthProtection.NullShiftY}) px with wraparound, re-scored against the UNSHIFTED reference)"
            };
            if (total.TruthViolations > 0) {
                lines.Add(ViolationLine(label, total.TruthViolations));
            }
            return lines;
        }

        /// <summary>Same formatting as the surrounding reports: 3 decimals, invariant, and a literal "NaN" for an
        /// undefined ratio rather than an empty cell a scorer would read as 0.</summary>
        private static string Fmt(double v) => double.IsNaN(v) ? "NaN" : v.ToString("F3", CultureInfo.InvariantCulture);
    }
}
