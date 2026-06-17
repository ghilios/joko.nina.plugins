using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectorTests {

        [Test]
        public void ComputeMedian_OddLength_ReturnsCenterElement() {
            // 5 elements: sorted [1, 2, 3, 4, 5] — median is index 2 = 3.0
            var pixels = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(3.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_AveragesTwoMiddleElements() {
            // 10 elements: [1,2,3,4,5,6,7,8,9,10]
            // Correct middle indices: 4 and 5 (values 5 and 6), median = 5.5
            // Buggy formula used Length >> (1+1) = Length / 4 = index 2,
            // producing (pixels[3] + pixels[2]) / 2 = (4 + 3) / 2 = 3.5 — a different result.
            var pixels = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(5.5).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_TwoElements_AveragesBoth() {
            var pixels = new double[] { 3.0, 7.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(5.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_FourElements_AveragesTwoMiddleElements() {
            // [10, 20, 30, 40] — middle indices 1 and 2 → (20 + 30) / 2 = 25.0
            var pixels = new double[] { 10.0, 20.0, 30.0, 40.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(25.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_SingleElement_ReturnsThatElement() {
            var pixels = new double[] { 42.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(42.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EmptyArray_ThrowsArgumentException() {
            var pixels = new double[] { };
            Assert.Throws<ArgumentException>(() => StarDetector.ComputeMedian(pixels));
        }

        // -----------------------------------------------------------------------
        // ComputeEffectiveMaxDistortion (defocus-aware distortion gate) tests
        // -----------------------------------------------------------------------

        private static StarDetectorParams DistortionParams(bool defocusAware, double maxDistortion = 0.5,
            double sizeReference = 20.0, double minFactor = 0.25) => new StarDetectorParams {
            MaxDistortion = maxDistortion,
            DefocusAwareDistortion = defocusAware,
            DefocusDistortionSizeReference = sizeReference,
            DefocusDistortionMinFactor = minFactor,
        };

        [Test]
        public void EffectiveMaxDistortion_FlagOff_AlwaysReturnsMaxDistortion_BitIdentical() {
            // With the flag OFF the gate must be byte-for-byte the legacy gate: MaxDistortion exactly, for every
            // candidate size, regardless of the (ignored) tuning knobs.
            var p = DistortionParams(defocusAware: false, maxDistortion: 0.5);
            Assert.Multiple(() => {
                foreach (var size in new[] { 1.0, 5.0, 20.0, 50.0, 200.0, 1000.0 }) {
                    Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, size), Is.EqualTo(0.5),
                        $"flag OFF must return MaxDistortion verbatim at size {size}");
                }
            });
        }

        [Test]
        public void EffectiveMaxDistortion_FlagOn_SmallCandidate_StaysStrict() {
            // Candidates at or below the size reference keep the strict threshold (factor clamped to 1.0).
            var p = DistortionParams(defocusAware: true, maxDistortion: 0.5, sizeReference: 20.0, minFactor: 0.25);
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, 5.0), Is.EqualTo(0.5).Within(1e-12),
                    "well below the reference ⇒ strict");
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, 20.0), Is.EqualTo(0.5).Within(1e-12),
                    "exactly at the reference ⇒ strict (factor = 1.0)");
            });
        }

        [Test]
        public void EffectiveMaxDistortion_FlagOn_LargeCandidate_RelaxesTowardFloor() {
            // Larger candidates get a more permissive threshold = MaxDistortion · (sizeReference / candidateSize),
            // floored at MinFactor · MaxDistortion.
            var p = DistortionParams(defocusAware: true, maxDistortion: 0.5, sizeReference: 20.0, minFactor: 0.25);
            Assert.Multiple(() => {
                // size 40 ⇒ factor 0.5 ⇒ 0.25
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, 40.0), Is.EqualTo(0.25).Within(1e-12));
                // size 25 ⇒ factor 0.8 ⇒ 0.4
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, 25.0), Is.EqualTo(0.4).Within(1e-12));
                // size 1000 ⇒ raw factor 0.02, clamped up to minFactor 0.25 ⇒ 0.125
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, 1000.0), Is.EqualTo(0.5 * 0.25).Within(1e-12),
                    "very large candidates floor at MinFactor · MaxDistortion");
            });
        }

        [Test]
        public void EffectiveMaxDistortion_LargeLowFill_PassesOnButRejectsOff_SmallLowFillRejectsBoth() {
            // The core behavioral contract the production gate uses (fillRatio < effectiveMaxDistortion ⇒ reject):
            //  - a LARGE low-fill candidate (donut) passes when ON, rejects when OFF;
            //  - a SMALL low-fill candidate rejects in BOTH cases (strict at small size).
            const double maxDistortion = 0.5;
            var on = DistortionParams(defocusAware: true, maxDistortion: maxDistortion, sizeReference: 20.0, minFactor: 0.25);
            var off = DistortionParams(defocusAware: false, maxDistortion: maxDistortion);

            // Donut: bbox max-dim 60px, annulus fills ~30% of the bbox² ⇒ fillRatio 0.30.
            const double largeSize = 60.0;
            const double largeFillRatio = 0.30;
            bool LargeRejected(StarDetectorParams pp) => largeFillRatio < StarDetector.ComputeEffectiveMaxDistortion(pp, largeSize);

            // Small junk: bbox max-dim 8px, same low fill ⇒ should stay rejected even with the relaxed gate.
            const double smallSize = 8.0;
            const double smallFillRatio = 0.30;
            bool SmallRejected(StarDetectorParams pp) => smallFillRatio < StarDetector.ComputeEffectiveMaxDistortion(pp, smallSize);

            Assert.Multiple(() => {
                Assert.That(LargeRejected(off), Is.True, "large low-fill donut is rejected with the flag OFF (legacy)");
                Assert.That(LargeRejected(on), Is.False, "large low-fill donut survives with the flag ON");
                Assert.That(SmallRejected(off), Is.True, "small low-fill junk is rejected OFF");
                Assert.That(SmallRejected(on), Is.True, "small low-fill junk is STILL rejected ON (strict at small size)");
            });
        }

        [Test]
        public void EffectiveMaxDistortion_DegenerateSizes_FallBackToStrict() {
            var p = DistortionParams(defocusAware: true, maxDistortion: 0.5, sizeReference: 20.0, minFactor: 0.25);
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, 0.0), Is.EqualTo(0.5),
                    "zero candidate size ⇒ strict fallback (no relaxation)");
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(p, -3.0), Is.EqualTo(0.5),
                    "negative candidate size ⇒ strict fallback");
                var pZeroRef = DistortionParams(defocusAware: true, sizeReference: 0.0);
                Assert.That(StarDetector.ComputeEffectiveMaxDistortion(pZeroRef, 100.0), Is.EqualTo(0.5),
                    "zero size reference ⇒ strict fallback");
            });
        }

        // -----------------------------------------------------------------------
        // ComputeCandidateElongation (roundness-rescue discriminator) tests
        // -----------------------------------------------------------------------

        // Build a filled rectangle footprint (w x h) of integer pixel coordinates.
        private static List<CvPoint> FilledRect(int w, int h) {
            var pts = new List<CvPoint>(w * h);
            for (int y = 0; y < h; ++y) {
                for (int x = 0; x < w; ++x) {
                    pts.Add(new CvPoint(x, y));
                }
            }
            return pts;
        }

        [Test]
        public void Elongation_FilledSquare_IsNearOne() {
            // A symmetric (square) footprint has equal coordinate variances ⇒ elongation ≈ 1.
            var e = StarDetector.ComputeCandidateElongation(FilledRect(21, 21));
            Assert.That(e, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void Elongation_Streak_MatchesAspectRatio() {
            // For a filled w x h rectangle the coordinate-covariance eigenvalues are the per-axis variances,
            // so elongation = sqrt(varMajor / varMinor) = (longer side / shorter side) for uniform fills.
            // A 60x20 streak ⇒ 3:1. Allow a small tolerance for the discrete uniform variance ratio.
            var e = StarDetector.ComputeCandidateElongation(FilledRect(60, 20));
            Assert.That(e, Is.EqualTo(3.0).Within(0.05));
        }

        [Test]
        public void Elongation_DonutRing_IsNearOne_AndPassesTypicalCap() {
            // A hollow ring (donut) of outer radius ~20 px, inner radius ~9 px is radially symmetric ⇒ elongation ≈ 1,
            // so it clears the default DefocusMaxElongation = 2.0 cap (admitted), unlike an elongated streak.
            var pts = new List<CvPoint>();
            const double rOut = 20.0, rIn = 9.0;
            for (int y = -22; y <= 22; ++y) {
                for (int x = -22; x <= 22; ++x) {
                    var r = Math.Sqrt(x * x + y * y);
                    if (r >= rIn && r <= rOut) {
                        pts.Add(new CvPoint(x + 30, y + 30));
                    }
                }
            }
            var e = StarDetector.ComputeCandidateElongation(pts);
            Assert.Multiple(() => {
                Assert.That(e, Is.EqualTo(1.0).Within(0.05), "a symmetric ring is round");
                Assert.That(e, Is.LessThanOrEqualTo(2.0), "round donut clears the default elongation cap");
            });
        }

        [Test]
        public void Elongation_DegenerateFootprints_ReturnInfinity() {
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeCandidateElongation(null), Is.EqualTo(double.PositiveInfinity));
                Assert.That(StarDetector.ComputeCandidateElongation(new List<CvPoint>()), Is.EqualTo(double.PositiveInfinity));
                Assert.That(StarDetector.ComputeCandidateElongation(new List<CvPoint> { new CvPoint(5, 5) }),
                    Is.EqualTo(double.PositiveInfinity), "single pixel ⇒ infinite (never rescued)");
                // A perfectly collinear set has zero minor-axis variance ⇒ infinite elongation (a line is not round).
                var line = new List<CvPoint>();
                for (int x = 0; x < 10; ++x) {
                    line.Add(new CvPoint(x, 3));
                }
                Assert.That(StarDetector.ComputeCandidateElongation(line), Is.EqualTo(double.PositiveInfinity));
            });
        }

        // -----------------------------------------------------------------------
        // ComputeEffectiveStarCenterTolerance (defocus-aware NotCentered gate) tests
        // -----------------------------------------------------------------------

        [Test]
        public void EffectiveStarCenterTolerance_FlagOff_AlwaysReturnsBaseTolerance_BitIdentical() {
            // The production gate only calls this helper when DefocusAwareCentering is true; with the flag OFF the
            // gate uses p.StarCenterTolerance verbatim. To prove the helper itself never tightens/relaxes when its
            // own preconditions are not met, maxFactor <= 1.0 (which a flag-off path effectively means: no relax)
            // must return the base tolerance byte-for-byte at every size.
            Assert.Multiple(() => {
                foreach (var size in new[] { 1.0, 5.0, 30.0, 60.0, 200.0, 1000.0 }) {
                    Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, size, sizeReference: 30.0, maxFactor: 1.0),
                        Is.EqualTo(0.3), $"maxFactor 1.0 (no-op) must return base tolerance verbatim at size {size}");
                }
            });
        }

        [Test]
        public void EffectiveStarCenterTolerance_SmallCandidate_StaysStrict() {
            // Candidates at or below the size reference keep the strict tolerance (factor clamped to 1.0).
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 5.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.3).Within(1e-12), "well below the reference ⇒ strict");
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 30.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.3).Within(1e-12), "exactly at the reference ⇒ strict (factor = 1.0)");
            });
        }

        [Test]
        public void EffectiveStarCenterTolerance_LargeCandidate_RelaxesTowardCeiling() {
            // Larger candidates get a more permissive (larger) tolerance = base · (candidateSize / sizeReference),
            // capped at base · maxFactor, then clamped to <= 1.0.
            Assert.Multiple(() => {
                // size 45 ⇒ factor 1.5 ⇒ 0.3 * 1.5 = 0.45
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 45.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.45).Within(1e-12));
                // size 60 ⇒ raw factor 2.0 = maxFactor ⇒ 0.3 * 2.0 = 0.6
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 60.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.6).Within(1e-12));
                // size 1000 ⇒ raw factor 33.3, clamped DOWN to maxFactor 2.0 ⇒ 0.6 (the ceiling)
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 1000.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.6).Within(1e-12), "very large candidates cap at base · maxFactor");
            });
        }

        [Test]
        public void EffectiveStarCenterTolerance_NeverExceedsOne_ClampedToValidRange() {
            // The tolerance is a ratio of the bbox; the gate's valid max is 1.0 (sub-box == whole bbox). Even with a
            // large base tolerance and a large multiplier the effective value must never exceed 1.0.
            Assert.Multiple(() => {
                // base 0.8 * factor 2.0 = 1.6 ⇒ clamped to 1.0
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.8, 100.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(1.0).Within(1e-12), "1.6 clamped down to the 1.0 ceiling");
                // base 0.6 * factor 2.0 = 1.2 ⇒ clamped to 1.0
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.6, 60.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(1.0).Within(1e-12), "1.2 clamped down to the 1.0 ceiling");
                // base 0.4 * factor 2.0 = 0.8 ⇒ within range, unchanged
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.4, 60.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.8).Within(1e-12), "0.8 is in range, not clamped");
            });
        }

        [Test]
        public void EffectiveStarCenterTolerance_DegenerateInputs_FallBackToStrict() {
            Assert.Multiple(() => {
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 0.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.3), "zero candidate size ⇒ strict fallback (no relaxation)");
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, -3.0, sizeReference: 30.0, maxFactor: 2.0),
                    Is.EqualTo(0.3), "negative candidate size ⇒ strict fallback");
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 100.0, sizeReference: 0.0, maxFactor: 2.0),
                    Is.EqualTo(0.3), "zero size reference ⇒ strict fallback");
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 100.0, sizeReference: 30.0, maxFactor: 1.0),
                    Is.EqualTo(0.3), "maxFactor 1.0 ⇒ no-op (strict)");
                Assert.That(StarDetector.ComputeEffectiveStarCenterTolerance(0.3, 100.0, sizeReference: 30.0, maxFactor: 0.5),
                    Is.EqualTo(0.3), "maxFactor < 1.0 must NOT tighten the gate ⇒ strict fallback");
            });
        }

        [Test]
        public void EffectiveStarCenterTolerance_LargeOffCenter_PassesOnButFailsOff_SmallOffCenterFailsBoth() {
            // Replicates the production NotCentered decision (StarDetector.IsStarCentered): a centroid passes iff it
            // lies inside the centered acceptance sub-box of size (tolerance · bbox) about the bbox center. Equivalent
            // 1-D condition for a coordinate at fractional offset f from the bbox-center (f in [0, 0.5], where 0.5 is
            // the bbox edge): the candidate is centered iff f <= tolerance / 2.
            //
            //  - a LARGE off-center centroid (defocused donut: big bbox, centroid pushed toward the ring) passes when
            //    the relaxed (ON) tolerance is used, fails under the strict (OFF) tolerance;
            //  - a SMALL off-center centroid (junk) fails under BOTH (strict at small size, no relaxation).
            const double baseTolerance = 0.3;     // strict acceptance sub-box half-extent = 0.15 of the bbox
            const double sizeReference = 30.0;
            const double maxFactor = 2.0;         // ON ceiling: effective tolerance up to 0.6 (half-extent 0.30)

            // A donut centroid offset by 0.20 of the bbox from center: outside the strict 0.15 band, inside the
            // relaxed 0.30 band (at the large size the factor hits the 2.0 ceiling ⇒ tolerance 0.6 ⇒ band 0.30).
            const double largeSize = 60.0;
            const double centroidOffsetFraction = 0.20;
            bool LargeCentered(bool defocusAware) {
                var tol = defocusAware
                    ? StarDetector.ComputeEffectiveStarCenterTolerance(baseTolerance, largeSize, sizeReference, maxFactor)
                    : baseTolerance;
                return centroidOffsetFraction <= tol / 2.0;
            }

            // Small junk at the SAME fractional offset: at/below the size reference the tolerance never relaxes, so it
            // stays rejected even with the flag ON.
            const double smallSize = 8.0;
            bool SmallCentered(bool defocusAware) {
                var tol = defocusAware
                    ? StarDetector.ComputeEffectiveStarCenterTolerance(baseTolerance, smallSize, sizeReference, maxFactor)
                    : baseTolerance;
                return centroidOffsetFraction <= tol / 2.0;
            }

            Assert.Multiple(() => {
                Assert.That(LargeCentered(defocusAware: false), Is.False, "large off-center donut is NotCentered with the flag OFF (legacy)");
                Assert.That(LargeCentered(defocusAware: true), Is.True, "large off-center donut is admitted with the flag ON");
                Assert.That(SmallCentered(defocusAware: false), Is.False, "small off-center junk is NotCentered OFF");
                Assert.That(SmallCentered(defocusAware: true), Is.False, "small off-center junk is STILL NotCentered ON (strict at small size)");
            });
        }

        // -----------------------------------------------------------------------
        // RelaxationAdmitted flag + RelaxationAdmittedCount metric (F3) — end-to-end detection
        // -----------------------------------------------------------------------

        // Params that open every gate EXCEPT the defocus-relaxable distortion/centering gates, so a large hollow
        // donut is accepted iff the defocus-aware gates (flipped together, as the optimizer's combined
        // DefocusAwareGates switch does) admit it. A real defocused donut needs BOTH relaxations: its big-but-low-
        // fill bbox trips the distortion gate, and its ring-destabilized centroid trips the centering gate.
        // Contamination rejection off (a lone donut on a flat field is clean anyway). ModelPSF off for speed.
        private static StarDetectorParams DonutDetectParams(bool defocusAware) => new StarDetectorParams {
            ModelPSF = false,
            RejectContaminatedStars = false,
            Sensitivity = 0.1,                 // very faint allowed
            MinHFR = 0.1,                      // tiny HFR allowed
            PeakResponse = 0.99,               // flatness gate effectively off (donut median ≪ peak anyway)
            StarCenterTolerance = 0.3,         // default
            MinimumStarBoundingBoxSize = 5,
            MaxDistortion = 0.5,               // default strict threshold
            DefocusAwareDistortion = defocusAware,
            DefocusAwareCentering = defocusAware,
            DefocusDistortionSizeReference = 30.0,
            DefocusDistortionMinFactor = 0.25,
            DefocusCenteringToleranceFactor = 2.0,
        };

        // A single large hollow donut whose annulus fill-ratio sits BELOW the strict MaxDistortion (0.5) but ABOVE
        // the defocus-relaxed threshold, so it is rejected when the gates are OFF and admitted (and flagged
        // RelaxationAdmitted) when they are ON. inner 18 / outer 26 ⇒ bbox d ≈ 52, fill ≈ π(26²−18²)/52² ≈ 0.41.
        private static Mat BuildLargeDonutField() {
            const int w = 128, h = 128;
            var mat = SyntheticDefocusedStarImage.CreateAnnulus(
                w, h, centerX: 64, centerY: 64, innerRadius: 18.0, outerRadius: 26.0,
                peak: 0.7, background: 0.05, edgeBlurSigma: 0.8);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, sigma: 0.005, seed: 4242);
            return mat;
        }

        [Test]
        public async Task RelaxationAdmittedCount_NearFocusSmallStars_ZeroAndCountsIdenticalOnVsOff() {
            // CRITICAL bit-identity: for a NEAR-FOCUS field of small, well-formed stars the defocus relaxation
            // never lowers any gate (small candidates stay strict), so turning the gate ON must NOT change the
            // detected set — the signature is byte-identical — and RelaxationAdmittedCount is 0 in BOTH cases.
            var off = StarDetectorEquivalence.StandardParams();
            off.DefocusAwareDistortion = false;
            off.DefocusAwareCentering = false;

            var on = StarDetectorEquivalence.StandardParams();
            on.DefocusAwareDistortion = true;
            on.DefocusAwareCentering = true;

            using var fieldOff = StarDetectorEquivalence.BuildSmallField();
            using var fieldOn = StarDetectorEquivalence.BuildSmallField();
            var resultOff = await StarDetectorEquivalence.RunDetect(fieldOff, off);
            var resultOn = await StarDetectorEquivalence.RunDetect(fieldOn, on);

            Assert.Multiple(() => {
                Assert.That(StarDetectorEquivalence.Signature(resultOn), Is.EqualTo(StarDetectorEquivalence.Signature(resultOff)),
                    "small near-focus stars: detection must be byte-identical with the defocus gates ON (the flag is informational)");
                Assert.That(resultOff.Metrics.RelaxationAdmittedCount, Is.EqualTo(0), "gate OFF ⇒ no relaxation-admitted stars");
                Assert.That(resultOn.Metrics.RelaxationAdmittedCount, Is.EqualTo(0), "small stars never need relaxation ⇒ still 0 with the gate ON");
                Assert.That(resultOff.DetectedStars.TrueForAll(s => !s.RelaxationAdmitted), Is.True);
                Assert.That(resultOn.DetectedStars.TrueForAll(s => !s.RelaxationAdmitted), Is.True);
            });
        }

        [Test]
        public async Task RelaxationAdmitted_LargeDonut_RejectedWhenGateOff_FlaggedAndAcceptedWhenGateOn() {
            using var fieldOff = BuildLargeDonutField();
            using var fieldOn = BuildLargeDonutField();

            var resultOff = await StarDetectorEquivalence.RunDetect(fieldOff, DonutDetectParams(defocusAware: false));
            var resultOn = await StarDetectorEquivalence.RunDetect(fieldOn, DonutDetectParams(defocusAware: true));

            Assert.Multiple(() => {
                // OFF: the strict distortion gate rejects the hollow donut, and nothing is relaxation-admitted.
                Assert.That(resultOff.DetectedStars.Count, Is.EqualTo(0), "strict gate (OFF) rejects the large hollow donut");
                Assert.That(resultOff.Metrics.RelaxationAdmittedCount, Is.EqualTo(0), "gate OFF ⇒ RelaxationAdmittedCount == 0");

                // ON: the relaxed distortion gate admits the donut, and it is flagged + tallied.
                Assert.That(resultOn.DetectedStars.Count, Is.GreaterThanOrEqualTo(1), "relaxed gate (ON) admits the large donut");
                Assert.That(resultOn.Metrics.RelaxationAdmittedCount, Is.EqualTo(resultOn.DetectedStars.Count),
                    "every donut admitted only by relaxation is flagged + counted");
                Assert.That(resultOn.DetectedStars.TrueForAll(s => s.RelaxationAdmitted), Is.True,
                    "the admitted donut(s) carry the RelaxationAdmitted flag");
            });
        }

        // -----------------------------------------------------------------------
        // ComputeIterativeCentroid tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Helper: create a flat float pixel array for a small image and return the
        /// star points list as "all pixels in the image" for simplicity.
        /// </summary>
        private static (float[] pixels, List<CvPoint> points) MakeImage(int width, int height, float background) {
            var pixels = new float[width * height];
            for (int i = 0; i < pixels.Length; ++i) pixels[i] = background;
            var points = new List<CvPoint>();
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width; ++x)
                    points.Add(new CvPoint(x, y));
            return (pixels, points);
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_SymmetricGaussian_ReturnsCenter() {
            // A symmetric 11x11 Gaussian centered exactly at (5, 5).
            const int side = 11;
            const float background = 0.05f;
            const float peak = 0.8f;
            const double sigma = 2.0;
            var pixels = new float[side * side];
            var points = new List<CvPoint>();
            for (int y = 0; y < side; ++y) {
                for (int x = 0; x < side; ++x) {
                    var dx = x - 5.0;
                    var dy = y - 5.0;
                    pixels[y * side + x] = (float)(background + peak * System.Math.Exp(-(dx * dx + dy * dy) / (2.0 * sigma * sigma)));
                    points.Add(new CvPoint(x, y));
                }
            }

            double backgroundThreshold = background + 0.01;
            double apertureRadius = side / 2.0;

            fixed (float* imageData = pixels) {
                var center = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundPlane: LocalBackgroundPlane.Flat(0, 0, background),
                    clipMargin: backgroundThreshold - background,
                    apertureRadius: apertureRadius,
                    numPasses: 3);

                Assert.Multiple(() => {
                    Assert.That(center.X, Is.EqualTo(5.0).Within(0.05),
                        "Symmetric Gaussian: iterative centroid X should be near true centre");
                    Assert.That(center.Y, Is.EqualTo(5.0).Within(0.05),
                        "Symmetric Gaussian: iterative centroid Y should be near true centre");
                });
            }
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_AsymmetricStar_ConvergesCloserThanSinglePass() {
            // 11×11 grid, background = 0.05.
            // True star centre is at (5, 5) but we add a bright outlier pixel at (9, 9) that
            // is above threshold and well outside the circular aperture.  A single-pass centroid
            // is pulled toward that outlier; the iterative centroid should settle closer to
            // the true stellar peak.
            const int side = 11;
            const float background = 0.05f;
            const float peak = 0.8f;
            const double sigma = 1.5;

            var pixels = new float[side * side];
            var points = new List<CvPoint>();

            // Place a symmetric Gaussian at (5, 5)
            for (int y = 0; y < side; ++y) {
                for (int x = 0; x < side; ++x) {
                    var dx = x - 5.0;
                    var dy = y - 5.0;
                    pixels[y * side + x] = (float)(background + peak * System.Math.Exp(-(dx * dx + dy * dy) / (2.0 * sigma * sigma)));
                    points.Add(new CvPoint(x, y));
                }
            }

            // Add an outlier bright pixel far from centre that is above threshold
            pixels[9 * side + 9] = 0.6f;

            double backgroundThreshold = background + 0.01;
            // Aperture radius ~5 px: the outlier at (9,9) is sqrt(32)≈5.66 px away from (5,5),
            // just outside the aperture, so it will be excluded from passes 2 and 3.
            double apertureRadius = 5.0;

            fixed (float* imageData = pixels) {
                // Single-pass centroid (numPasses = 1)
                var single = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundPlane: LocalBackgroundPlane.Flat(0, 0, background),
                    clipMargin: backgroundThreshold - background,
                    apertureRadius: apertureRadius,
                    numPasses: 1);

                // Iterative centroid (numPasses = 3)
                var iterative = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundPlane: LocalBackgroundPlane.Flat(0, 0, background),
                    clipMargin: backgroundThreshold - background,
                    apertureRadius: apertureRadius,
                    numPasses: 3);

                // Both should be above threshold; verify the outlier actually biases single-pass
                var singleError = System.Math.Sqrt(
                    (single.X - 5.0) * (single.X - 5.0) +
                    (single.Y - 5.0) * (single.Y - 5.0));
                var iterativeError = System.Math.Sqrt(
                    (iterative.X - 5.0) * (iterative.X - 5.0) +
                    (iterative.Y - 5.0) * (iterative.Y - 5.0));

                Assert.Multiple(() => {
                    // The outlier must actually bias the single-pass result away from centre
                    Assert.That(singleError, Is.GreaterThan(0.05),
                        "Single-pass centroid should be pulled off-centre by the outlier");
                    // Iterative centroid must be materially closer to the true centre
                    Assert.That(iterativeError, Is.LessThan(singleError),
                        "Iterative centroid should converge closer to true centre than single-pass");
                    // And it must be within a tight bound
                    Assert.That(iterativeError, Is.LessThan(0.1),
                        "Iterative centroid should be within 0.1 px of true centre (5, 5)");
                });
            }
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_NoPixelsAboveThreshold_ReturnsBoundingBoxCentre() {
            // All pixels are at or below threshold — the fallback centre-of-points path.
            const int side = 5;
            const float background = 0.1f;
            var (pixels, points) = MakeImage(side, side, background);
            // threshold higher than all pixels
            double backgroundThreshold = 0.5;

            fixed (float* imageData = pixels) {
                var center = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundPlane: LocalBackgroundPlane.Flat(0, 0, background),
                    clipMargin: backgroundThreshold - background,
                    apertureRadius: 3.0,
                    numPasses: 3);

                // The unweighted geometric centre of a 5×5 grid is (2, 2)
                Assert.Multiple(() => {
                    Assert.That(center.X, Is.EqualTo(2.0).Within(0.01));
                    Assert.That(center.Y, Is.EqualTo(2.0).Within(0.01));
                });
            }
        }

        // -----------------------------------------------------------------------
        // MeasureStar circular aperture tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that the circular aperture removes the rectangular bias: corner pixels at
        /// distance ≈ √2 × half-box are excluded so HFR is lower than a naive rectangular sum.
        /// Uses a wider Gaussian (sigma=8) so that corner pixels are meaningful (~5% of peak at 3.5σ),
        /// resulting in an observable HFR difference of ~0.2+ pixels.
        /// </summary>
        [Test]
        public void MeasureStar_CircularAperture_HfrLowerThanRectangularBias() {
            // Create a symmetric Gaussian star centred at (20, 20) with sigma=8, peak=0.8, background=0.05.
            // The star is embedded in a 41×41 image so there are plenty of corner pixels well outside a
            // circle inscribed in the bounding box.  With sigma=8, corner pixels at distance ≈28px (≈3.5σ)
            // have meaningful amplitude (~5% of peak), making the circular vs rectangular HFR difference observable.
            const int imageSize = 41;
            const double cx = 20.0;
            const double cy = 20.0;
            const double sigma = 8.0;
            const double peak = 0.8;
            const double background = 0.05;

            using (var image = SyntheticGaussianStarImage.Create(
                width: imageSize, height: imageSize,
                centerX: cx, centerY: cy,
                sigmaX: sigma, sigmaY: sigma,
                peak: peak, background: background)) {

                var detector = new StarDetector(new AlglibAPI());

                // Bounding box covers the entire image (square), so the inscribed circle has
                // radius = imageSize / 2 = 20.5.  Corner pixels are at distance √2 × 20 ≈ 28.3
                // from the centre — well outside the circle.
                var boundingBox = new Rect(0, 0, imageSize, imageSize);
                var p = new StarDetectorParams {
                    AnalysisSamplingSize = 1.0f,
                    StarClippingMultiplier = 0.0   // no noise clipping for this synthetic test
                };

                // Circular aperture HFR
                var circularStar = new Star {
                    Center = new Point2d(cx, cy),
                    StarBoundingBox = boundingBox,
                    Background = background
                };
                var circularSuccess = detector.MeasureStar(image, circularStar, p, noiseSigma: 0.0);
                Assert.That(circularSuccess, Is.True, "MeasureStar should succeed for a well-formed Gaussian");

                // Rectangular HFR: manually compute by summing ALL pixels in the box (no aperture filter).
                double rectTotalBrightness = 0.0;
                double rectTotalWeightedDistance = 0.0;
                unsafe {
                    var data = (float*)image.DataPointer;
                    for (int y = 0; y < imageSize; ++y) {
                        for (int x = 0; x < imageSize; ++x) {
                            var value = data[y * imageSize + x] - background;
                            if (value > 0.0) {
                                var dx = x - cx;
                                var dy = y - cy;
                                var dist = Math.Sqrt(dx * dx + dy * dy);
                                rectTotalWeightedDistance += value * dist;
                                rectTotalBrightness += value;
                            }
                        }
                    }
                }
                var rectangularHFR = rectTotalBrightness > 0 ? rectTotalWeightedDistance / rectTotalBrightness : 0.0;

                // The circular HFR must be strictly less than the rectangular HFR because corner
                // pixels (far from centre) are excluded, lowering the flux-weighted mean radius.
                // With sigma=8, the difference should be meaningfully large (>0.05 pixels).
                Assert.Multiple(() => {
                    Assert.That(circularStar.HFR, Is.LessThan(rectangularHFR),
                        $"Circular HFR ({circularStar.HFR:F4}) should be lower than rectangular HFR ({rectangularHFR:F4})");
                    Assert.That(rectangularHFR - circularStar.HFR, Is.GreaterThan(0.05),
                        $"Rectangular bias should be at least 0.05 HFR pixels, but got {rectangularHFR - circularStar.HFR:F4}");
                });
            }
        }

        // -----------------------------------------------------------------------
        // CheckBackgroundContamination tests
        // -----------------------------------------------------------------------

        private static PSFModel MakePSF(double background, double rSquared = 0.99) {
            return new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0.0,
                offsetY: 0.0,
                peak: 0.5,
                background: background,
                sigmaX: 2.0,
                sigmaY: 2.0,
                fwhmX: 4.7,
                fwhmY: 4.7,
                thetaRadians: 0.0,
                rSquared: rSquared,
                pixelScale: 1.0);
        }

        // -----------------------------------------------------------------------
        // Gradient-robust contamination test + local background plane
        // -----------------------------------------------------------------------

        /// <summary>
        /// Builds a synthetic background annulus: a ring of pixels (inner..outer radius) around the origin,
        /// each valued by a planar gradient plus an optional localized positive bump in one octant.
        /// </summary>
        private static (float[] dx, float[] dy, float[] val, int n) MakeAnnulus(
                double b0, double b1, double b2, int bumpOctant = -1, double bumpValue = 0.0,
                int innerRadius = 4, int outerRadius = 8) {
            var dxs = new List<float>();
            var dys = new List<float>();
            var vals = new List<float>();
            for (int y = -outerRadius; y <= outerRadius; ++y) {
                for (int x = -outerRadius; x <= outerRadius; ++x) {
                    var r = Math.Sqrt(x * x + y * y);
                    if (r < innerRadius || r > outerRadius) {
                        continue;
                    }
                    double v = b0 + b1 * x + b2 * y;
                    if (bumpOctant >= 0 && StarDetector.OctantOf(x, y) == bumpOctant) {
                        v += bumpValue;
                    }
                    dxs.Add(x); dys.Add(y); vals.Add((float)v);
                }
            }
            return (dxs.ToArray(), dys.ToArray(), vals.ToArray(), dxs.Count);
        }

        [Test]
        public void LocalBackgroundPlane_ValueAt_EvaluatesPlaneRelativeToOrigin() {
            var plane = new LocalBackgroundPlane(originX: 100, originY: 50, b0: 5.0, b1: 0.1, b2: -0.2, isFlat: false);
            Assert.Multiple(() => {
                Assert.That(plane.ValueAt(100, 50), Is.EqualTo(5.0).Within(1e-9), "At origin → b0");
                Assert.That(plane.ValueAt(110, 50), Is.EqualTo(5.0 + 0.1 * 10).Within(1e-9));
                Assert.That(plane.ValueAt(100, 60), Is.EqualTo(5.0 + -0.2 * 10).Within(1e-9));
                var flat = LocalBackgroundPlane.Flat(0, 0, 3.0);
                Assert.That(flat.IsFlat, Is.True);
                Assert.That(flat.ValueAt(123, -45), Is.EqualTo(3.0).Within(1e-12), "Flat plane is constant everywhere");
            });
        }

        [Test]
        public void ComputeGradientContamination_SmoothGradientNoContaminant_NotSuspectedButFitsPlane() {
            // A pure one-sided gradient (galaxy/nebula slope) with no localized source must NOT be flagged,
            // and the fitted plane should recover the gradient coefficients.
            var (dx, dy, val, n) = MakeAnnulus(b0: 10.0, b1: 0.5, b2: -0.25);

            var r = StarDetector.ComputeGradientContamination(dx, dy, val, n,
                fallbackSigma: 1.0, sensitivity: 5.0, minSectorPixels: 8, fillSectors: true);

            Assert.Multiple(() => {
                Assert.That(r.PlaneValid, Is.True);
                Assert.That(r.B0, Is.EqualTo(10.0).Within(1e-3));
                Assert.That(r.B1, Is.EqualTo(0.5).Within(1e-3));
                Assert.That(r.B2, Is.EqualTo(-0.25).Within(1e-3));
                Assert.That(r.Suspected, Is.False, "A smooth gradient is removed by the plane fit → not contamination");
            });
        }

        [Test]
        public void ComputeGradientContamination_LocalizedBrightOctantOnGradient_IsSuspected() {
            // The same gradient plus a strong localized positive bump in one octant must be flagged, because the
            // bump survives as a one-sided positive residual after the gradient is removed.
            var (dx, dy, val, n) = MakeAnnulus(b0: 10.0, b1: 0.5, b2: -0.25, bumpOctant: 0, bumpValue: 50.0);

            var r = StarDetector.ComputeGradientContamination(dx, dy, val, n,
                fallbackSigma: 1.0, sensitivity: 5.0, minSectorPixels: 8, fillSectors: true);

            Assert.Multiple(() => {
                Assert.That(r.PlaneValid, Is.True);
                Assert.That(r.Suspected, Is.True, "A localized bright octant is real contamination → flagged");
                Assert.That(r.TrippingSector, Is.EqualTo(0));
            });
        }

        [Test]
        public void ComputeGradientContamination_OneSidedDeficit_NotSuspected() {
            // An edge-clipped annulus has one side reading far BELOW the plane (a deficit). The test is
            // one-sided (positive excess only), so a deficit must not be flagged as contamination.
            var (dx, dy, val, n) = MakeAnnulus(b0: 10.0, b1: 0.0, b2: 0.0, bumpOctant: 2, bumpValue: -9.0);

            var r = StarDetector.ComputeGradientContamination(dx, dy, val, n,
                fallbackSigma: 1.0, sensitivity: 5.0, minSectorPixels: 8, fillSectors: false);

            Assert.That(r.Suspected, Is.False, "A one-sided deficit (edge clip) is not a contaminant");
        }

        [Test]
        public void ComputeGradientContamination_SensitivityZero_FitsPlaneButNeverSuspected() {
            // Sensitivity 0 disables the contamination decision, but the background plane must still be fit so
            // it can serve as the local background for HFR/PSF — even in the presence of a localized contaminant.
            var (dx, dy, val, n) = MakeAnnulus(b0: 10.0, b1: 0.5, b2: -0.25, bumpOctant: 0, bumpValue: 50.0);

            var r = StarDetector.ComputeGradientContamination(dx, dy, val, n,
                fallbackSigma: 1.0, sensitivity: 0.0, minSectorPixels: 8, fillSectors: false);

            Assert.Multiple(() => {
                Assert.That(r.Suspected, Is.False, "Sensitivity 0 disables the decision");
                Assert.That(r.PlaneValid, Is.True, "Plane is still fit when the contamination test is disabled");
            });
        }

        [Test]
        public void ComputeGradientContamination_TooFewPoints_NoPlaneNotSuspected() {
            var dx = new float[] { -1, 0, 1 };
            var dy = new float[] { 0, 1, 0 };
            var val = new float[] { 1, 1, 1 };

            var r = StarDetector.ComputeGradientContamination(dx, dy, val, dx.Length,
                fallbackSigma: 1.0, sensitivity: 5.0, minSectorPixels: 8, fillSectors: false);

            Assert.Multiple(() => {
                Assert.That(r.PlaneValid, Is.False);
                Assert.That(r.Suspected, Is.False);
            });
        }

        // -----------------------------------------------------------------------
        // ComputeLocalBackgroundSigma + local-sigma contamination behavior
        // -----------------------------------------------------------------------

        [Test]
        public void ComputeLocalBackgroundSigma_SampleTooSmall_ReturnsZero() {
            // Fewer than the minimum sample size cannot give a trustworthy MAD, so the helper
            // returns 0 to signal the caller to fall back to the global noise sigma.
            var pixels = new float[] { 1f, 2f, 3f, 4f, 5f }; // 5 < 8
            Array.Sort(pixels);
            var median = pixels[pixels.Length >> 1];
            var sigma = StarDetector.ComputeLocalBackgroundSigma(pixels, pixels.Length, median);
            Assert.That(sigma, Is.EqualTo(0.0));
        }

        [Test]
        public void ComputeLocalBackgroundSigma_FlatSample_ReturnsZero() {
            // A perfectly flat annulus has MAD = 0; the helper returns 0 (fall back to global sigma)
            // rather than a zero scale that would flag every star.
            var pixels = new float[10];
            for (int i = 0; i < pixels.Length; ++i) pixels[i] = 7.0f;
            var sigma = StarDetector.ComputeLocalBackgroundSigma(pixels, pixels.Length, 7.0);
            Assert.That(sigma, Is.EqualTo(0.0));
        }

        [Test]
        public void ComputeLocalBackgroundSigma_KnownScatter_ScalesByMadConstant() {
            // Symmetric sample [-4..4] about median 0. Absolute deviations are
            // {0,1,1,2,2,3,3,4,4}; their median is 2, so sigma = 1.4826 * 2 = 2.9652.
            var pixels = new float[] { -4f, -3f, -2f, -1f, 0f, 1f, 2f, 3f, 4f };
            Array.Sort(pixels);
            var median = pixels[pixels.Length >> 1]; // 0
            var sigma = StarDetector.ComputeLocalBackgroundSigma(pixels, pixels.Length, median);
            Assert.That(sigma, Is.EqualTo(1.4826 * 2.0).Within(1e-9));
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_SecondPassEmptyAperture_ReturnsSinglePassEstimate() {
            // Pass 1 succeeds and estimates centroid at (2.5, 2.5).
            // Pass 2 uses a very small aperture radius that excludes all pixels,
            // so it should break early and return the pass-1 estimate.
            const int side = 7;
            const float background = 0.0f;
            const float threshold = 0.5f;
            var (pixels, points) = MakeImage(side, side, background);

            // Place two bright pixels at (2, 2) and (3, 3) symmetrically
            // Pass 1 centroid will be (2.5, 2.5)
            pixels[2 * side + 2] = 0.8f;
            pixels[3 * side + 3] = 0.8f;

            double backgroundThreshold = threshold;
            // Aperture radius 0.3: pixels at (2,2) and (3,3) are both at distance sqrt(0.5^2 + 0.5^2) = 0.707
            // from (2.5, 2.5), which exceeds 0.3, so the aperture will exclude all pixels in pass 2.
            double apertureRadius = 0.3;

            fixed (float* imageData = pixels) {
                var center = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundPlane: LocalBackgroundPlane.Flat(0, 0, background),
                    clipMargin: backgroundThreshold - background,
                    apertureRadius: apertureRadius,
                    numPasses: 3);

                // Should return pass-1 estimate of (2.5, 2.5)
                Assert.Multiple(() => {
                    Assert.That(center.X, Is.EqualTo(2.5).Within(1e-12),
                        "Should return pass-1 centroid when pass 2 aperture excludes all pixels");
                    Assert.That(center.Y, Is.EqualTo(2.5).Within(1e-12),
                        "Should return pass-1 centroid when pass 2 aperture excludes all pixels");
                });
            }
        }
    }
}
