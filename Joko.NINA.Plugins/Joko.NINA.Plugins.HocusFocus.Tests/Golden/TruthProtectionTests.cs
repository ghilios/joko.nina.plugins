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
            Assert.That(b.W / 2.0, Is.GreaterThanOrEqualTo(radius),
                "half-width must cover the match radius");
            // and a detection exactly at the radius is inside the box
            Assert.That(b.X, Is.LessThanOrEqualTo(500 - radius));
            Assert.That(b.X + b.W, Is.GreaterThanOrEqualTo(500 + radius));
        }

        [Test]
        public void BuildProtectionBoxes_LargeHfrWins_SoDefocusedDonutsAreCovered() {
            // A wing donut: HFR 20 px => half 40 px, well past the 12 px radius floor. This is the population
            // F31 is about — defocus destroys peak SNR, so these are exactly the stars the golden drops.
            var b = TruthProtection.BuildProtectionBoxes(
                new List<SyntheticStarDisposition> { Disp(SyntheticTier.Omitted, 500, 500, hfr: 20.0) }, 12.0).Single();
            Assert.That(b.W / 2.0, Is.EqualTo(40.0).Within(1.0));
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
    }
}
