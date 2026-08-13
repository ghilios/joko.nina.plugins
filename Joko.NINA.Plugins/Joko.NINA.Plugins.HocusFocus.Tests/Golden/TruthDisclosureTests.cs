#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using TestApp;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Golden {

    /// <summary>
    /// W27 (A). <c>bank-verify</c> carries four truth-related honesty disclosures — <c>scoringMode</c> +
    /// <c>protectedStars</c>, <c>truthViolations</c>, <c>scoredFraction</c> and <c>precisionNull</c>.
    /// <c>golden eval</c>, the instrument that produced the published results table, carried NONE of them, so
    /// every stored report was written by a truth-protected instrument that could not say it was truth-protected,
    /// could not say what chance alone would have scored, could not say whether any false positive sat on a real
    /// star, and could not say what fraction of the detections it judged.
    ///
    /// <para>These pin the ported computation. Each of the three named mutants must turn one of them red:
    /// <c>M-S1</c> deletes the <c>protectedStars</c> accumulation, <c>M-S2</c> hard-codes
    /// <c>scoringMode = "golden"</c>, <c>M-S3</c> returns the unshifted detections from the null path.</para>
    /// </summary>
    [TestFixture]
    public class TruthDisclosureTests {

        private const double MatchRadius = 12.0;
        private const double Tau = 0.3;
        private const int FrameWidth = 4000;
        private const int FrameHeight = 3000;

        private static SyntheticStarDisposition Disp(string tier, double x, double y) =>
            new SyntheticStarDisposition { Tier = tier, BinnedCenterX = x, BinnedCenterY = y, BinnedHfrPixels = 2.0 };

        private static DetBox Det(double cx, double cy) =>
            new DetBox(new RectD(cx - 6, cy - 6, 12, 12), cx, cy);

        /// <summary>
        /// One frame carrying every case the disclosure has to separate, scored through the REAL path
        /// (<see cref="GoldenMatch.Match"/> then <see cref="GoldenMatch.ExcludeUnresolved"/> then
        /// <see cref="TruthProtection.ExcludeProtected"/>) so <see cref="TruthDisclosure.Measure"/> is fed exactly
        /// what <c>GoldenEvalRunner</c> feeds it:
        /// <list type="bullet">
        /// <item>a detection on a required golden star  -> true positive</item>
        /// <item>a detection on an <c>omitted</c> truth star -> protection EXERCISED</item>
        /// <item>a detection on nothing -> a genuine false positive that must survive</item>
        /// <item>a detection on the golden's own <c>unresolved</c> box -> excluded before protection is consulted</item>
        /// </list>
        /// </summary>
        private static TruthDisclosureFrame MeasureScenario(bool withTruth) {
            var truth = withTruth
                ? new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 2000, 2000) }
                : null;
            var goldenRects = new List<RectD> { new RectD(88, 88, 24, 24) };            // centre (100,100)
            var unresolvedRects = new List<RectD> { new RectD(1488, 1488, 24, 24) };    // centre (1500,1500)
            var det = new List<DetBox> {
                Det(100, 100),      // TP
                Det(2000, 2000),    // on the omitted truth star
                Det(3000, 3000),    // on nothing -- a real false positive
                Det(1500, 1500)     // on the golden's unresolved box
            };

            var match = GoldenMatch.Match(goldenRects, det, GoldenMatchMode.Centroid, Tau, MatchRadius);
            var beforeProtection = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, unresolvedRects);
            var scoredFps = TruthProtection.ExcludeProtected(
                beforeProtection, det, TruthProtection.ProtectionCenters(truth), MatchRadius);

            return TruthDisclosure.Measure(truth, goldenRects, unresolvedRects, det,
                beforeProtection, scoredFps, match.Pairs.Count,
                GoldenMatchMode.Centroid, Tau, MatchRadius, FrameWidth, FrameHeight);
        }

        // ---- M-S1: the protectedStars accumulation ------------------------------------------------------------

        /// <summary>
        /// <b>M-S1 kills this.</b> <c>protectedStars</c> is protection AVAILABLE — how many real-but-unboxed truth
        /// stars a detection could have been excused by. Deleting the accumulation reads 0, which is
        /// indistinguishable from "the real bank, no sidecar" and is exactly the silence being repaired.
        /// </summary>
        [Test]
        public void ProtectedStars_CountsEveryRealButUnboxedTruthStar_AndSumsOverFrames() {
            var truth = new List<SyntheticStarDisposition> {
                Disp(SyntheticTier.Omitted, 100, 100),
                Disp(SyntheticTier.MergedInto, 200, 200),
                Disp(SyntheticTier.Unresolved, 300, 300),    // already boxed in the golden -- not protection
                Disp(GoldenConfidence.High, 400, 400)        // a required find -- never protected
            };
            var frame = TruthDisclosure.Measure(truth, new List<RectD>(), new List<RectD>(), new List<DetBox>(),
                new List<int>(), new List<int>(), 0, GoldenMatchMode.Centroid, Tau, MatchRadius, FrameWidth, FrameHeight);

            Assert.Multiple(() => {
                Assert.That(frame.ProtectedStars, Is.EqualTo(2),
                    "omitted + merged-into are the real-but-unboxed population; unresolved and the tiered stars are not");
                Assert.That(TruthDisclosure.Aggregate(new[] { frame, frame }).ProtectedStars, Is.EqualTo(4),
                    "the run total is the sum over frames, exactly as bank-verify accumulates it");
            });
        }

        /// <summary>Availability is not exercise, and the report has to be able to tell them apart: this frame
        /// carries one protected star and exercises it exactly once.</summary>
        [Test]
        public void ProtectedDetections_CountsProtectionExercised_NotProtectionAvailable() {
            var frame = MeasureScenario(withTruth: true);
            Assert.Multiple(() => {
                Assert.That(frame.ProtectedStars, Is.EqualTo(1), "one omitted truth star was available to protect");
                Assert.That(frame.ProtectedDetections, Is.EqualTo(1),
                    "exactly one detection was removed from the false-positive count by ExcludeProtected");
                Assert.That(frame.ScoredFp, Is.EqualTo(1),
                    "the detection on nothing is still a false positive -- protection must not launder junk");
            });
        }

        // ---- M-S2: the scoring mode ---------------------------------------------------------------------------

        /// <summary>
        /// <b>M-S2 kills this.</b> Hard-coding <c>"golden"</c> makes a truth-protected report claim it was scored
        /// against the golden alone. Reports differing on this are NOT comparable on precision, so a wrong label
        /// here silently misattributes every stored report — which is the whole of the defect (A) repairs.
        /// </summary>
        [Test]
        public void ScoringMode_SaysTruthProtected_IffAFrameCarriedATruthSidecar() {
            Assert.Multiple(() => {
                Assert.That(TruthDisclosure.ScoringMode(0), Is.EqualTo("golden"));
                Assert.That(TruthDisclosure.ScoringMode(1), Is.EqualTo("golden+truth-protected"));
                Assert.That(TruthDisclosure.ScoringMode(9), Is.EqualTo("golden+truth-protected"));
            });
        }

        /// <summary>The console line is bank-verify's, word for word, so a reader (or a grep across nine wave
        /// roots) cannot tell the two harnesses apart.</summary>
        [Test]
        public void ScoringLine_IsBankVerifysWording_Verbatim_InBothDirections() {
            Assert.Multiple(() => {
                Assert.That(TruthDisclosure.ScoringLine(9, 231), Is.EqualTo(
                    "  scoring: golden+truth-protected (231 real-but-unboxed truth stars protected from FP scoring across 9 frames)"));
                Assert.That(TruthDisclosure.ScoringLine(0, 0), Is.EqualTo(
                    "  scoring: golden (no truth sidecars; false positives scored against the golden alone)"));
            });
        }

        [Test]
        public void ReportLines_CarryAllFourDisclosures_PlusTheExercisedCount() {
            var total = TruthDisclosure.Aggregate(new[] { MeasureScenario(withTruth: true) });
            var text = string.Join("\n", TruthDisclosure.ReportLines("D09", total, framesWithTruth: 1));

            Assert.Multiple(() => {
                Assert.That(text, Does.Contain("scoringMode: golden+truth-protected"));
                Assert.That(text, Does.Contain("protectedStars: 1"));
                Assert.That(text, Does.Contain("protectedDetections: 1"));
                Assert.That(text, Does.Contain("truthViolations: 0"));
                Assert.That(text, Does.Contain("scoredFraction: 0.500"));
                Assert.That(text, Does.Contain("precisionNull: 0.000"));
                Assert.That(text.All(c => c < 128), Is.True, "TestApp output must be ASCII (TestAppOutputAsciiTests)");
            });
        }

        [Test]
        public void ReportLines_SayTheModeIsGoldenAlone_WhenNoSidecarExists() {
            var total = TruthDisclosure.Aggregate(new[] { MeasureScenario(withTruth: false) });
            var text = string.Join("\n", TruthDisclosure.ReportLines("mccomiskey", total, framesWithTruth: 0));

            Assert.Multiple(() => {
                Assert.That(text, Does.Contain("scoringMode: golden (no truth sidecars;"));
                Assert.That(text, Does.Contain("precisionNull: NaN"),
                    "no truth sidecar means no synthetic null is defined -- NaN, never a 0 a scorer would average in");
                Assert.That(text, Does.Not.Contain("!!"), "no violation line when there are no violations");
            });
        }

        // ---- M-S3: the null control ---------------------------------------------------------------------------

        /// <summary>
        /// <b>M-S3 kills this.</b> Returning the unshifted detections makes the "null" re-score the LIVE
        /// correspondence, so it reports the live precision and a saturated metric passes the check that exists to
        /// fail it. The first cut of the F31 repair read 1.000 on all 17 datasets for exactly that reason, and the
        /// precision column alone could not show it.
        /// </summary>
        [Test]
        public void PrecisionNull_IsWhatChanceAloneScores_NotTheLivePrecision() {
            var frame = MeasureScenario(withTruth: true);
            var livePrecision = (double)frame.ScoredTp / (frame.ScoredTp + frame.ScoredFp);

            Assert.Multiple(() => {
                Assert.That(livePrecision, Is.EqualTo(0.5).Within(1e-9), "precondition: the live metric is measuring");
                Assert.That(frame.NullTp, Is.Zero,
                    "shifted by (317,211) with wraparound, not one detection still lands on its golden star");
                Assert.That(frame.NullFp, Is.EqualTo(4), "every shifted detection is unmatched");
                Assert.That(frame.PrecisionNull, Is.EqualTo(0.0).Within(1e-9),
                    "chance alone earns nothing here -- if this equalled the live precision the null would be measuring "
                    + "the live correspondence, which is M-S3");
            });
        }

        [Test]
        public void PrecisionNull_IsNaN_WithNoTruthSidecar_SoTheRealBankReportsNoNull() {
            var frame = MeasureScenario(withTruth: false);
            Assert.Multiple(() => {
                Assert.That(frame.HasTruth, Is.False);
                Assert.That(frame.PrecisionNull, Is.NaN);
                Assert.That(frame.NullTp + frame.NullFp, Is.Zero, "the null must not run without a truth sidecar");
            });
        }

        /// <summary>
        /// REPORT-ONLY has to be structural, not a promise: <c>S27-4</c> is byte-level over 360 detection
        /// artifacts. The null works on a copy, so no ordering, membership or coordinate of the live detection
        /// list can move.
        /// </summary>
        [Test]
        public void Measure_LeavesTheLiveDetectionListExactlyAsItFoundIt() {
            var truth = new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 2000, 2000) };
            var goldenRects = new List<RectD> { new RectD(88, 88, 24, 24) };
            var det = new List<DetBox> { Det(100, 100), Det(2000, 2000), Det(3000, 3000) };
            var before = det.Select(d => (d.Cx, d.Cy, d.Box.X, d.Box.Y, d.Box.W, d.Box.H)).ToList();

            TruthDisclosure.Measure(truth, goldenRects, new List<RectD>(), det,
                new List<int> { 1, 2 }, new List<int> { 2 }, 1,
                GoldenMatchMode.Centroid, Tau, MatchRadius, FrameWidth, FrameHeight);

            Assert.That(det.Select(d => (d.Cx, d.Cy, d.Box.X, d.Box.Y, d.Box.W, d.Box.H)), Is.EqualTo(before),
                "the null control must never touch the list the scoring path scored");
        }

        // ---- truthViolations: has an exact answer, and the answer must be 0 -----------------------------------

        [Test]
        public void TruthViolations_CountsAScoredFalsePositiveSittingOnARealStar_AndIsZeroUnderTheRepair() {
            // Truth is complete by construction, so "is there a real star here?" has an exact answer. Charging a
            // detection of one as a false positive is the F31 signature itself.
            var truth = new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500) };
            var det = new List<DetBox> { Det(500, 500), Det(3000, 3000) };
            var match = GoldenMatch.Match(new List<RectD>(), det, GoldenMatchMode.Centroid, Tau, MatchRadius);
            var beforeProtection = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, new List<RectD>());
            var scored = TruthProtection.ExcludeProtected(
                beforeProtection, det, TruthProtection.ProtectionCenters(truth), MatchRadius);

            var repaired = TruthDisclosure.Measure(truth, new List<RectD>(), new List<RectD>(), det,
                beforeProtection, scored, match.Pairs.Count,
                GoldenMatchMode.Centroid, Tau, MatchRadius, FrameWidth, FrameHeight);
            // The pre-repair path: protection never consulted, so the detection on the real star is charged.
            var unrepaired = TruthDisclosure.Measure(truth, new List<RectD>(), new List<RectD>(), det,
                beforeProtection, beforeProtection, match.Pairs.Count,
                GoldenMatchMode.Centroid, Tau, MatchRadius, FrameWidth, FrameHeight);

            Assert.Multiple(() => {
                Assert.That(unrepaired.TruthViolations, Is.EqualTo(1), "precondition: the /3 metric charged this real star as junk");
                Assert.That(repaired.TruthViolations, Is.Zero, "under the repair this has an exact answer and it must be 0");
                Assert.That(string.Join("\n", TruthDisclosure.ReportLines("D09", unrepaired, 1)),
                    Does.Contain("!! [D09] 1 scored false positive(s) sit on a REAL rendered star"),
                    "non-zero must be loud, exactly as bank-verify is");
            });
        }

        // ---- scoredFraction, and the aggregation rule ---------------------------------------------------------

        [Test]
        public void ScoredFraction_IsHowMuchOfTheDetectionSetThePrecisionRatioJudged() {
            var frame = MeasureScenario(withTruth: true);
            Assert.Multiple(() => {
                Assert.That(frame.Detections, Is.EqualTo(4));
                Assert.That(frame.ScoredTp + frame.ScoredFp, Is.EqualTo(2),
                    "one detection was protected and one landed on an unresolved box; neither is judgeable");
                Assert.That(frame.ScoredFraction, Is.EqualTo(0.5).Within(1e-9));
            });
        }

        [Test]
        public void Aggregate_SumsTheCounters_AndRecomputesTheRatiosFromTheSums() {
            // A mean of per-frame ratios would weight a 2-detection frame like a 900-detection one. The wing
            // frames of a defocus sweep are exactly where the detection counts diverge most, so this matters.
            var small = new TruthDisclosureFrame { HasTruth = true, Detections = 2, ScoredTp = 1, ScoredFp = 1, NullTp = 1, NullFp = 0 };
            var large = new TruthDisclosureFrame { HasTruth = true, Detections = 98, ScoredTp = 0, ScoredFp = 0, NullTp = 0, NullFp = 99 };
            var total = TruthDisclosure.Aggregate(new[] { small, large });

            Assert.Multiple(() => {
                Assert.That(total.Detections, Is.EqualTo(100));
                Assert.That(total.ScoredFraction, Is.EqualTo(0.02).Within(1e-9),
                    "2 of 100 detections were judged -- not the 0.5 a mean of the two frame ratios would report");
                Assert.That(total.PrecisionNull, Is.EqualTo(1.0 / 100.0).Within(1e-9));
                Assert.That(total.HasTruth, Is.True);
            });
        }

        [Test]
        public void Aggregate_OfNothing_IsAllZerosAndNaNRatios_RatherThanThrowing() {
            var total = TruthDisclosure.Aggregate(null);
            Assert.Multiple(() => {
                Assert.That(total.ProtectedStars, Is.Zero);
                Assert.That(total.ScoredFraction, Is.NaN);
                Assert.That(total.PrecisionNull, Is.NaN);
                Assert.That(TruthDisclosure.FramesWithTruth(null), Is.Zero);
            });
        }

        // ---- the CSV contract ---------------------------------------------------------------------------------

        [Test]
        public void CsvHeaderAndRow_AgreeOnFieldCountAndOrder() {
            var frame = MeasureScenario(withTruth: true);
            var header = TruthDisclosure.CsvHeaderSuffix.Split(',');
            var row = TruthDisclosure.CsvRowSuffix(frame).Split(',');

            Assert.Multiple(() => {
                Assert.That(header, Is.EqualTo(new[] { "protectedStars", "protectedDetections", "scoredFraction", "precisionNull" }));
                Assert.That(row, Has.Length.EqualTo(header.Length), "a header/row length mismatch silently shifts every cell");
                Assert.That(row[0], Is.EqualTo("1"));
                Assert.That(row[1], Is.EqualTo("1"));
                Assert.That(row[2], Is.EqualTo("0.500"));
                Assert.That(row[3], Is.EqualTo("0.000"));
            });
        }

        [Test]
        public void CsvRow_WritesNaN_RatherThanAnEmptyCellAScorerWouldReadAsZero() {
            var row = TruthDisclosure.CsvRowSuffix(MeasureScenario(withTruth: false)).Split(',');
            Assert.That(row[3], Is.EqualTo("NaN"));
        }
    }

    /// <summary>
    /// The disclosures are only worth anything if the RUNNERS actually emit them, and <c>GoldenEvalRunner</c>
    /// cannot be linked into this project (it loads NINA rendered images), so the wiring is asserted against its
    /// SOURCE — the same mechanism <see cref="Harness.TestAppOutputAsciiTests"/> uses, and for the same reason.
    /// This is what makes <c>M-S2</c> reachable at its real change site: hard-coding the label in the runner
    /// instead of deriving it turns these red.
    /// </summary>
    [TestFixture]
    public class TruthDisclosureWiringTests {

        /// <summary>The pre-W27 header, character for character. The disclosure columns are APPENDED after it;
        /// if a future edit inserts one instead, this literal stops matching and the test says so — the
        /// score_*.py scorers across nine wave roots index this file BY POSITION.</summary>
        private const string LegacyFramesCsvHeader =
            "focuser,params,golden,accepted,TP,FP,FN,precision,recall,f1,recallHigh,recallHighMed,recallAll,"
            + "fnAcceptedElsewhere,fnNoCandidate,fnRejected";

        [Test]
        public void GoldenEvalRunner_DerivesTheScoringMode_AndNeverHardcodesTheLabel() {
            var source = ReadTestAppSource("GoldenEvalRunner.cs");
            var code = StripComments(source);

            Assert.Multiple(() => {
                Assert.That(code, Does.Contain("TruthDisclosure.ScoringLine("),
                    "the console must report the mode ACTUALLY used, via the shared helper");
                Assert.That(code, Does.Contain("TruthDisclosure.ReportLines("),
                    "golden_eval.txt must carry the disclosure block");
                Assert.That(code, Does.Not.Contain("\"golden+truth-protected\""),
                    "a hardcoded label here silently misattributes every stored report (GoldenEvalRunner.cs house rule)");
                Assert.That(code, Does.Not.Contain("scoringMode = \"golden\""),
                    "M-S2: the mode must be derived from how many frames carried a sidecar, never assigned a literal");
            });
        }

        [Test]
        public void GoldenEvalRunner_MeasuresEveryScoredFrame_AndPassesTheTruthToTheReport() {
            var code = StripComments(ReadTestAppSource("GoldenEvalRunner.cs"));
            Assert.Multiple(() => {
                Assert.That(code, Does.Contain("TruthDisclosure.Measure("),
                    "every scored frame must be measured, or the run total is silently partial");
                Assert.That(code, Does.Contain("fe.Truth ="),
                    "WriteReports is reached through the frame list, which is how it is now passed the truth dispositions");
                Assert.That(code, Does.Contain("TruthDisclosure.ViolationLine("),
                    "a non-zero truthViolations must be loud on the console, exactly as bank-verify is");
            });
        }

        [Test]
        public void GoldenEvalFramesCsv_AppendsTheDisclosureColumns_NeverInsertsThem() {
            var code = StripComments(ReadTestAppSource("GoldenEvalRunner.cs"));
            var headerAt = code.IndexOf(LegacyFramesCsvHeader, System.StringComparison.Ordinal);

            Assert.Multiple(() => {
                Assert.That(headerAt, Is.GreaterThanOrEqualTo(0),
                    "the pre-W27 column list must survive intact -- an inserted column re-labels every number to its right");
                Assert.That(code.IndexOf("TruthDisclosure.CsvHeaderSuffix", System.StringComparison.Ordinal),
                    Is.GreaterThan(headerAt), "the new columns come AFTER the legacy ones");
                Assert.That(code, Does.Contain("TruthDisclosure.CsvRowSuffix("),
                    "and the per-frame row must carry their values");
            });
        }

        [Test]
        public void BankVerifyRunner_SharesTheWording_SoTheTwoHarnessesCannotDriftApart() {
            var code = StripComments(ReadTestAppSource("BankVerifyRunner.cs"));
            Assert.Multiple(() => {
                Assert.That(code, Does.Contain("TruthDisclosure.ScoringLine("));
                Assert.That(code, Does.Contain("TruthDisclosure.ScoringMode("));
                Assert.That(code, Does.Contain("TruthDisclosure.ViolationLine("));
                Assert.That(code, Does.Contain("protectedDetections"),
                    "protection EXERCISED is the number a reader needs, and bank-verify reported only availability");
                Assert.That(code, Does.Not.Contain("\"golden+truth-protected\""),
                    "the label lives in TruthDisclosure now; two copies is how the two harnesses came to disagree");
            });
        }

        // ---- reading the source: FAIL, never skip -------------------------------------------------------------

        private static string ReadTestAppSource(string fileName, [CallerFilePath] string thisFile = null) {
            // .../Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Golden/TruthDisclosureTests.cs -> .../TestApp
            var goldenDir = Path.GetDirectoryName(thisFile);
            var solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(goldenDir));
            var path = Path.Combine(solutionDir ?? string.Empty, "TestApp", fileName);
            Assert.That(File.Exists(path), Is.True,
                $"could not look: {path} does not exist (resolved from {thisFile}); a test that cannot look must not pass");
            var source = File.ReadAllText(path);
            Assert.That(source, Has.Length.GreaterThan(2000), $"could not look: {path} is suspiciously short");
            return source;
        }

        /// <summary>Blanks out <c>//</c> and <c>/* */</c> comments so a doc comment quoting a label is not mistaken
        /// for code emitting it — the same exemption, and the same reason, as the ASCII guard's comment mask.</summary>
        private static string StripComments(string source) {
            var sb = new System.Text.StringBuilder(source.Length);
            var i = 0;
            while (i < source.Length) {
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/') {
                    while (i < source.Length && source[i] != '\n') { sb.Append(' '); i++; }
                    continue;
                }
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*') {
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')) {
                        sb.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    sb.Append("  ");
                    i = System.Math.Min(i + 2, source.Length);
                    continue;
                }
                sb.Append(source[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
