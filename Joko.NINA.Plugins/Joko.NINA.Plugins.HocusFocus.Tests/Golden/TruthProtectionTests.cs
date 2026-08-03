using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using TestApp;
using TestApp.SynthBank;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Golden {

    /// <summary>
    /// F31: the golden policy drops sub-3.5-SNR truth stars to <c>omitted</c>, which produces no box, so a
    /// detection there is scored a FALSE POSITIVE — while the 3.5–5 band gets an <c>unresolved</c> box and is
    /// excluded. Measured on the real bank of synthetic runs, 96% of reported false positives were real stars.
    /// These pin the repair: real-but-unboxed stars are protected from FP scoring and never become required finds.
    /// </summary>
    [TestFixture]
    public class TruthProtectionTests {

        private static SyntheticStarDisposition Disp(string tier, double x, double y, double hfr = 2.0) =>
            new SyntheticStarDisposition {
                Tier = tier,
                BinnedCenterX = x,
                BinnedCenterY = y,
                BinnedHfrPixels = hfr
            };

        [Test]
        public void BuildProtectionBoxes_NullOrEmpty_ReturnsEmpty_SoTheRealBankIsUnaffected() {
            Assert.Multiple(() => {
                Assert.That(TruthProtection.BuildProtectionBoxes(null, 12.0), Is.Empty);
                Assert.That(TruthProtection.BuildProtectionBoxes(new List<SyntheticStarDisposition>(), 12.0), Is.Empty);
            });
        }

        [Test]
        public void BuildProtectionBoxes_CoversOmittedAndMergedInto_Only() {
            var d = new List<SyntheticStarDisposition> {
                Disp(SyntheticTier.Omitted, 100, 100),
                Disp(SyntheticTier.MergedInto, 200, 200),
                Disp(SyntheticTier.Unresolved, 300, 300),   // already boxed in the golden
                Disp(GoldenConfidence.High, 400, 400),      // a required find — must never be protected
                Disp(GoldenConfidence.Medium, 500, 500),
                Disp(GoldenConfidence.Low, 600, 600)
            };
            var boxes = TruthProtection.BuildProtectionBoxes(d, 12.0);
            Assert.That(boxes.Select(b => b.Confidence),
                Is.EquivalentTo(new[] { SyntheticTier.Omitted, SyntheticTier.MergedInto }));
        }

        [Test]
        public void BuildProtectionBoxes_IsCenteredOnTheTruthStar() {
            var boxes = TruthProtection.BuildProtectionBoxes(
                new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 1000.0, 750.0) }, 12.0);
            var b = boxes.Single();
            Assert.Multiple(() => {
                Assert.That(b.CenterX, Is.EqualTo(1000.0).Within(1e-9));
                Assert.That(b.CenterY, Is.EqualTo(750.0).Within(1e-9));
                Assert.That(b.W, Is.EqualTo(b.H), "protection boxes are square");
            });
        }

        [Test]
        public void BuildProtectionBoxes_IsNeverTighterThanTheMatchRadius() {
            // THE load-bearing invariant. A detection close enough to a real star to be MATCHED must also be
            // close enough not to be called a false positive; otherwise the two halves of the metric disagree.
            // A tiny-HFR star is the case that exposes it (2*HFR = 1 px would give a 1 px box).
            const double radius = 12.0;
            var boxes = TruthProtection.BuildProtectionBoxes(
                new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 0.5) }, radius);
            var b = boxes.Single();
            Assert.That(b.W / 2.0, Is.EqualTo(radius).Within(1e-9),
                "half-width IS the match radius — protection exactly as generous as matching");
            // and a detection exactly at the radius is inside the box
            Assert.That(b.X, Is.LessThanOrEqualTo(500 - radius));
            Assert.That(b.X + b.W, Is.GreaterThanOrEqualTo(500 + radius));
        }

        [Test]
        public void BuildProtectionBoxes_DoesNotWidenWithHfr_SoTheMetricStaysDiscriminating() {
            // A wing donut (HFR 20 px) gets the SAME protection as a compact star: the match radius, NOT the
            // star's light footprint. Sizing by 2·HFR saturated the metric — precision read 1.000 on all 17
            // datasets for all three F23 arms, because a ~40 px box launders genuine noise blobs along with the
            // real star. A metric that cannot separate configurations is as useless as a biased one.
            var compact = TruthProtection.BuildProtectionBoxes(
                new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 1.0) }, 12.0).Single();
            var donut = TruthProtection.BuildProtectionBoxes(
                new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 20.0) }, 12.0).Single();
            Assert.Multiple(() => {
                Assert.That(donut.W, Is.EqualTo(compact.W), "protection must not scale with HFR");
                Assert.That(donut.W / 2.0, Is.EqualTo(12.0).Within(1e-9), "half-width is the match radius");
            });
        }

        [Test]
        public void BuildProtectionBoxes_SkipsNonFinitePositions() {
            var d = new List<SyntheticStarDisposition> {
                Disp(SyntheticTier.Omitted, double.NaN, 100),
                Disp(SyntheticTier.Omitted, 100, double.NaN)
            };
            Assert.That(TruthProtection.BuildProtectionBoxes(d, 12.0), Is.Empty);
        }

        [Test]
        public void ExcludeUnresolved_WithProtection_DropsTheFalsePositiveOnARealStar() {
            // End-to-end over the real scorer: one detection sitting on an `omitted` truth star. Without
            // protection GoldenMatch calls it a false positive; with protection it is excluded.
            var truth = new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 3.0) };
            var det = new List<DetBox> { new DetBox(new RectD(494, 494, 12, 12), 500, 500) };
            var golden = new List<RectD>(); // the golden holds no box here — that is the defect

            var match = GoldenMatch.Match(golden, det, GoldenMatchMode.Centroid, 0.3, 12.0);
            Assert.That(match.FalsePositives, Has.Count.EqualTo(1), "precondition: scored as an FP today");

            var rects = TruthProtection.BuildExclusionRects(goldenUnresolved: null, dispositions: truth, matchRadius: 12.0);
            var kept = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, rects);
            Assert.That(kept, Is.Empty, "a detection of a real star must not be a false positive");
        }

        [Test]
        public void ExcludeUnresolved_WithProtection_KeepsAGenuineFalsePositive() {
            // The other side of the contract: protection must not launder junk. A detection far from any truth
            // star stays a false positive.
            var truth = new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 3.0) };
            var det = new List<DetBox> { new DetBox(new RectD(2994, 2994, 12, 12), 3000, 3000) };

            var match = GoldenMatch.Match(new List<RectD>(), det, GoldenMatchMode.Centroid, 0.3, 12.0);
            var rects = TruthProtection.BuildExclusionRects(null, truth, 12.0);
            var kept = GoldenMatch.ExcludeUnresolved(match.FalsePositives, det, rects);
            Assert.That(kept, Has.Count.EqualTo(1), "a detection with no real star under it is still an FP");
        }

        [Test]
        public void BuildExclusionRects_KeepsTheGoldensOwnUnresolvedBoxes() {
            var goldenUnresolved = new List<GoldenStarBox> { new GoldenStarBox { X = 0, Y = 0, W = 10, H = 10 } };
            var rects = TruthProtection.BuildExclusionRects(
                goldenUnresolved, new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500) }, 12.0);
            Assert.That(rects, Has.Count.EqualTo(2), "golden unresolved + truth protection");
        }

        [Test]
        public void BuildExclusionRects_NoTruth_IsExactlyTheGoldensUnresolvedList() {
            // The real bank has no truth sidecar: scoring must be byte-identical to before this change.
            var goldenUnresolved = new List<GoldenStarBox> {
                new GoldenStarBox { X = 1, Y = 2, W = 3, H = 4 },
                new GoldenStarBox { X = 5, Y = 6, W = 7, H = 8 }
            };
            var rects = TruthProtection.BuildExclusionRects(goldenUnresolved, null, 12.0);
            Assert.Multiple(() => {
                Assert.That(rects, Has.Count.EqualTo(2));
                Assert.That(rects[0].X, Is.EqualTo(1.0));
                Assert.That(rects[1].W, Is.EqualTo(7.0));
            });
        }

        // ---- The symmetric protection predicate (afbank-verify/5) --------------------------------------------

        [Test]
        public void ExcludeProtected_UsesTheSamePredicateAsMatching_NotTheDetectionsBoundingBox() {
            // THE reason /5 exists. GoldenMatch.Covers excludes on `centre-in-box OR IoU(box, det.bbox) > 0`, so
            // a WIDE detection is protected by its own size: a 60 px donut bbox overlaps a protection box whose
            // centre is 30+ px away, far outside the 12 px match radius. Measured on the saved wave-1 detection
            // dumps that reads up to 0.011 high, always flattering. ExcludeProtected compares centroids only.
            var truth = new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 3.0) };
            // A wide detection centred 35 px away — well outside the match radius, so NOT a protected detection,
            // but its 60 px box still overlaps the 24 px protection box.
            var det = new List<DetBox> { new DetBox(new RectD(505, 470, 60, 60), 535, 500) };
            var fps = new List<int> { 0 };

            var viaCovers = GoldenMatch.ExcludeUnresolved(
                fps, det, TruthProtection.BuildExclusionRects(null, truth, 12.0));
            var viaCentroid = TruthProtection.ExcludeProtected(
                fps, det, TruthProtection.ProtectionCenters(truth), 12.0);

            Assert.Multiple(() => {
                Assert.That(viaCovers, Is.Empty, "precondition: the bbox-dilated predicate launders this one");
                Assert.That(viaCentroid, Has.Count.EqualTo(1),
                    "35 px from the nearest real star is outside the 12 px match radius, so it stays a false positive");
            });
        }

        [Test]
        public void ExcludeProtected_DropsADetectionInsideTheMatchRadius() {
            var truth = new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500) };
            var det = new List<DetBox> { new DetBox(new RectD(503, 503, 8, 8), 507, 507) }; // ~9.9 px away
            Assert.That(TruthProtection.ExcludeProtected(new List<int> { 0 }, det,
                TruthProtection.ProtectionCenters(truth), 12.0), Is.Empty);
        }

        [Test]
        public void ExcludeProtected_NoTruth_IsANoOp_SoTheRealBankIsUnaffected() {
            var det = new List<DetBox> { new DetBox(new RectD(0, 0, 4, 4), 2, 2) };
            Assert.Multiple(() => {
                Assert.That(TruthProtection.ExcludeProtected(new List<int> { 0 }, det,
                    TruthProtection.ProtectionCenters(null), 12.0), Has.Count.EqualTo(1));
                Assert.That(TruthProtection.ProtectionCenters(null), Is.Empty);
            });
        }

        // ---- The regression guard: a detection on a real star is never a false positive ----------------------

        [Test]
        public void CountWithinRadius_IsTheF31Signature_AndMustBeZeroUnderTheRepair() {
            // Synthetic truth is COMPLETE by construction, so "is there a real star here?" has an exact answer.
            // A scored false positive within the match radius of one is the F31 bug, whatever tier the golden
            // policy assigned. This is the invariant bank-verify now reports as `truthViolations`.
            var truth = new List<SyntheticStarDisposition> {
                Disp(SyntheticTier.Omitted, 500, 500),
                Disp(GoldenConfidence.High, 900, 900)
            };
            var det = new List<DetBox> {
                new DetBox(new RectD(496, 496, 8, 8), 500, 500),   // on the omitted star — the F31 case
                new DetBox(new RectD(2996, 2996, 8, 8), 3000, 3000) // on nothing — a genuine false positive
            };
            var golden = new List<RectD>();
            var match = GoldenMatch.Match(golden, det, GoldenMatchMode.Centroid, 0.3, 12.0);
            var all = TruthProtection.AllCenters(truth);

            var unrepaired = TruthProtection.CountWithinRadius(match.FalsePositives, det, all, 12.0);
            var repaired = TruthProtection.CountWithinRadius(
                TruthProtection.ExcludeProtected(match.FalsePositives, det,
                    TruthProtection.ProtectionCenters(truth), 12.0),
                det, all, 12.0);

            Assert.Multiple(() => {
                Assert.That(unrepaired, Is.EqualTo(1), "the /3 metric charged this real star as junk");
                Assert.That(repaired, Is.Zero, "under the repair, no scored false positive sits on a real star");
            });
        }

        [Test]
        public void AllCenters_CoversEveryTier_NotJustTheProtectedOnes() {
            var truth = new List<SyntheticStarDisposition> {
                Disp(SyntheticTier.Omitted, 1, 1),
                Disp(SyntheticTier.MergedInto, 2, 2),
                Disp(SyntheticTier.Unresolved, 3, 3),
                Disp(GoldenConfidence.High, 4, 4)
            };
            Assert.Multiple(() => {
                Assert.That(TruthProtection.AllCenters(truth), Has.Count.EqualTo(4));
                Assert.That(TruthProtection.ProtectionCenters(truth), Has.Count.EqualTo(2));
                Assert.That(TruthProtection.AllCenters(null), Is.Empty);
            });
        }

        // ---- The null control -------------------------------------------------------------------------------

        [Test]
        public void ShiftForNullControl_MovesEveryDetectionOffItsStar_AndWrapsInsideTheFrame() {
            // The null control answers "what does chance alone score?", which is the only way to tell a real
            // 1.000 from a saturated metric. The first cut of the F31 repair read 1.000 on all 17 datasets
            // BECAUSE protection was 2*HFR wide, and the precision column alone could not show that.
            var det = new List<DetBox> {
                new DetBox(new RectD(96, 96, 8, 8), 100, 100),
                new DetBox(new RectD(3990, 2990, 8, 8), 3994, 2994)   // wraps on both axes
            };
            var shifted = TruthProtection.ShiftForNullControl(det, 4000, 3000);
            Assert.Multiple(() => {
                Assert.That(shifted, Has.Count.EqualTo(2));
                Assert.That(shifted[0].Cx, Is.EqualTo(100 + TruthProtection.NullShiftX).Within(1e-9));
                Assert.That(shifted[0].Cy, Is.EqualTo(100 + TruthProtection.NullShiftY).Within(1e-9));
                Assert.That(shifted[1].Cx, Is.EqualTo((3994.0 + TruthProtection.NullShiftX) % 4000).Within(1e-9));
                Assert.That(shifted[1].Cy, Is.EqualTo((2994.0 + TruthProtection.NullShiftY) % 3000).Within(1e-9));
                Assert.That(shifted.All(s => s.Cx >= 0 && s.Cx < 4000 && s.Cy >= 0 && s.Cy < 3000),
                    "every shifted detection stays inside the frame");
                Assert.That(shifted[0].Box.W, Is.EqualTo(8.0), "size is preserved — only position moves");
            });
        }

        [Test]
        public void ShiftForNullControl_ShiftIsLargerThanAnyMatchRadiusInTheBank() {
            // If the shift were comparable to the match radius the null would still see the real stars, and a
            // saturated metric would pass the check it exists to fail.
            Assert.Multiple(() => {
                Assert.That(TruthProtection.NullShiftX, Is.GreaterThan(100));
                Assert.That(TruthProtection.NullShiftY, Is.GreaterThan(100));
            });
        }

        [Test]
        public void ShiftForNullControl_DegenerateFrame_ReturnsEmpty_RatherThanDividingByZero() {
            var det = new List<DetBox> { new DetBox(new RectD(0, 0, 4, 4), 2, 2) };
            Assert.Multiple(() => {
                Assert.That(TruthProtection.ShiftForNullControl(det, 0, 100), Is.Empty);
                Assert.That(TruthProtection.ShiftForNullControl(null, 100, 100), Is.Empty);
            });
        }
    }
}
