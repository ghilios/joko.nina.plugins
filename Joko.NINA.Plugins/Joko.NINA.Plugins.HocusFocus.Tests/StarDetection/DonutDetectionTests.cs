using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Defocus-aware DONUT detection (master DefocusAwareDonutDetection + morph-close / hole-count / streak /
    /// bloom). Covers the bit-identical-when-OFF contract, the EARLY/LATE param classification, the pure
    /// geometry helpers (CountEnclosedHole, ComputePointCloudEccentricity), donut recovery, and the
    /// detection-only guarantee (hole-fill never changes HFR).
    /// </summary>
    [TestFixture]
    public class DonutDetectionTests {

        // ---- Pure helper: CountEnclosedHole ----------------------------------------------------------------

        private static List<Point> RingPoints(int size, double rIn, double rOut) {
            var pts = new List<Point>();
            double c = (size - 1) / 2.0;
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x) {
                    double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d >= rIn && d <= rOut) pts.Add(new Point(x, y));
                }
            return pts;
        }

        [Test]
        public void CountEnclosedHole_DonutRing_ReturnsEnclosedHole() {
            // Ring fills the bbox tightly (outer radius 8 → 17px bbox), mirroring a real tight candidate bbox.
            int size = 17;
            var ring = RingPoints(size, 5.0, 8.0);
            var bounds = new Rect(0, 0, size, size);
            int hole = StarDetector.CountEnclosedHole(ring, bounds, 0.15);
            Assert.That(hole, Is.GreaterThan(0), "a hollow ring must report its enclosed interior hole");
            // (ring + hole) / d^2 must rise above the strict MaxDistortion (0.5) so the donut is accepted.
            double d = size;
            Assert.That((ring.Count + hole) / (d * d), Is.GreaterThan(0.5));
        }

        [Test]
        public void CountEnclosedHole_SolidDisk_ReturnsZero() {
            int size = 21;
            var disk = RingPoints(size, 0.0, 8.0); // filled disk: no enclosed background
            int hole = StarDetector.CountEnclosedHole(disk, new Rect(0, 0, size, size), 0.15);
            Assert.That(hole, Is.EqualTo(0));
        }

        [Test]
        public void CountEnclosedHole_StraightLine_ReturnsZero() {
            var line = new List<Point>();
            for (int x = 0; x < 21; ++x) line.Add(new Point(x, 10));
            int hole = StarDetector.CountEnclosedHole(line, new Rect(0, 0, 21, 21), 0.15);
            Assert.That(hole, Is.EqualTo(0));
        }

        [Test]
        public void CountEnclosedHole_ArcOpenToBorder_ReturnsZero() {
            // A ring with a wedge removed (open to the border) — the interior is reachable, so no enclosed hole.
            int size = 21;
            double c = 10.0;
            var arc = new List<Point>();
            for (int y = 0; y < size; ++y)
                for (int x = 0; x < size; ++x) {
                    double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    if (d >= 6.0 && d <= 8.0 && !(x >= 10 && y <= 10)) arc.Add(new Point(x, y)); // remove a quadrant
                }
            int hole = StarDetector.CountEnclosedHole(arc, new Rect(0, 0, size, size), 0.05);
            Assert.That(hole, Is.EqualTo(0));
        }

        // ---- Pure helper: ComputePointCloudEccentricity ----------------------------------------------------

        [Test]
        public void Eccentricity_StraightLine_NearOne() {
            var line = new List<Point>();
            for (int x = 0; x < 40; ++x) line.Add(new Point(x, 5));
            Assert.That(StarDetector.ComputePointCloudEccentricity(line), Is.GreaterThan(0.99));
        }

        [Test]
        public void Eccentricity_RoundRing_Low() {
            var ring = RingPoints(31, 9.0, 12.0);
            Assert.That(StarDetector.ComputePointCloudEccentricity(ring), Is.LessThan(0.5));
        }

        [Test]
        public void Eccentricity_ModeratelyEllipticalRing_BelowStreakThreshold() {
            // A 2:1 elliptical ring (astigmatism) must stay below the 0.95 streak threshold (never falsely rejected).
            var pts = new List<Point>();
            double c = 20.0;
            for (int y = 0; y < 41; ++y)
                for (int x = 0; x < 41; ++x) {
                    double ex = (x - c) / 16.0, ey = (y - c) / 8.0; // 2:1 ellipse
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    if (d >= 0.7 && d <= 1.0) pts.Add(new Point(x, y));
                }
            Assert.That(StarDetector.ComputePointCloudEccentricity(pts), Is.LessThan(0.95));
        }

        // ---- EARLY/LATE classification -------------------------------------------------------------------

        [Test]
        public void ParamClassification_MasterAndMorphClose_AreEarly_RestAreLate() {
            Assert.Multiple(() => {
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DefocusAwareDonutDetection)), Is.True);
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DonutMorphCloseSize)), Is.True);
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DonutMinAnnularityHoleFraction)), Is.False);
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DonutMaxStreakEccentricity)), Is.False);
                Assert.That(StarDetector.IsEarlyCacheKeyParameter(nameof(StarDetectorParams.DonutSaturationBloomRadius)), Is.False);
            });
        }

        // ---- Bit-identical when the master is OFF --------------------------------------------------------

        [Test]
        public async Task MasterOff_AggressiveKnobs_BitIdentical() {
            var legacy = StarDetectorEquivalence.StandardParams();

            var withKnobs = StarDetectorEquivalence.StandardParams();
            // Master OFF, but every donut/spike knob set to an aggressive non-default value: all must be inert
            // (morph-close, hole-fill/integrated-sensitivity, streak, bloom, and the master-default distortion/
            // centering relaxations are ALL gated by the master).
            withKnobs.DonutMorphCloseSize = 9;
            withKnobs.DonutMinAnnularityHoleFraction = 0.4;
            withKnobs.DonutMaxStreakEccentricity = 0.85;
            withKnobs.DonutSaturationBloomRadius = 40.0;

            using var f1 = StarDetectorEquivalence.BuildSmallField();
            var r1 = await StarDetectorEquivalence.RunDetect(f1, legacy);
            using var f2 = StarDetectorEquivalence.BuildSmallField();
            var r2 = await StarDetectorEquivalence.RunDetect(f2, withKnobs);

            Assert.That(StarDetectorEquivalence.Signature(r2), Is.EqualTo(StarDetectorEquivalence.Signature(r1)),
                "With DefocusAwareDonutDetection OFF, no donut/spike knob may change detection.");
        }

        // ---- Donut recovery -------------------------------------------------------------------------------

        private static bool DetectedNear(HocusFocusStarDetectorResult result, double x, double y, double tol) =>
            (result.DetectedStars ?? new List<Star>()).Any(s =>
                Math.Abs(s.Center.X - x) <= tol && Math.Abs(s.Center.Y - y) <= tol);

        [Test]
        public async Task DonutRecovery_MasterOn_RecoversRingThatLegacyRejects() {
            // A thin hollow ring: low fill-ratio ⇒ legacy rejects as TooDistorted; the annularity hole-count
            // lets it pass when the master is on. PeakResponse high so the uniform-ish ring isn't TooFlat.
            using var img = SyntheticDefocusedStarImage.CreateAnnulus(256, 256, 128, 128,
                innerRadius: 13, outerRadius: 20, peak: 0.7, background: 0.05, edgeBlurSigma: 1.5);
            SyntheticDefocusedStarImage.AddGaussianNoise(img, 0.008, 4242);

            var pOff = StarDetectorEquivalence.StandardParams();
            pOff.PeakResponse = 0.98;
            var off = await StarDetectorEquivalence.RunDetect(img, pOff);

            using var img2 = SyntheticDefocusedStarImage.CreateAnnulus(256, 256, 128, 128,
                innerRadius: 13, outerRadius: 20, peak: 0.7, background: 0.05, edgeBlurSigma: 1.5);
            SyntheticDefocusedStarImage.AddGaussianNoise(img2, 0.008, 4242);
            var pOn = StarDetectorEquivalence.StandardParams();
            pOn.PeakResponse = 0.98;
            pOn.DefocusAwareDonutDetection = true; // morph-close (5) + hole-fill (0.15) active by default
            // A 40px donut is largely removed by the structure wavelet at the default layer count, so keep it via
            // the defocus-aware structure boost (an EARLY axis the optimizer enables the same way when the master
            // is on). With the ring kept, the annularity hole-count then lets it pass the distortion gate.
            pOn.DefocusAwareStructure = true;
            pOn.StructureLayerBoost = 3;
            var on = await StarDetectorEquivalence.RunDetect(img2, pOn);

            Assert.Multiple(() => {
                Assert.That(DetectedNear(off, 128, 128, 6), Is.False, "legacy detection must reject the hollow ring");
                Assert.That(DetectedNear(on, 128, 128, 6), Is.True, "donut mode must recover the hollow ring");
            });
        }

        [Test]
        public async Task DonutAwareSensitivity_RecoversFaintExtendedDonut_PeakPathRejects() {
            // A LARGE, FAINT donut: per-pixel peak is low (fails the peak-based LowSensitivity gate) but its
            // INTEGRATED ring flux is a strong detection. Structure-boost + distortion relaxation are on in BOTH
            // runs (so the donut forms and clears the distortion gate); the ONLY difference is the master, which
            // enables the integrated-flux sensitivity path.
            Mat Make() {
                // Faint: ring only ~0.012 above background, with low noise so it survives clipping but its
                // per-pixel SNR is small while the integrated ring SNR is large.
                var m = SyntheticDefocusedStarImage.CreateAnnulus(200, 200, 100, 100,
                    innerRadius: 14, outerRadius: 22, peak: 0.062, background: 0.05, edgeBlurSigma: 1.0);
                SyntheticDefocusedStarImage.AddGaussianNoise(m, 0.0015, 321);
                return m;
            }
            StarDetectorParams Base() {
                var p = StarDetectorEquivalence.StandardParams();
                p.PeakResponse = 0.98;
                p.DefocusAwareStructure = true; p.StructureLayerBoost = 3; // keep the big donut alive
                p.DefocusAwareDistortion = true; p.DefocusAwareCentering = true; // clear the distortion gate
                p.Sensitivity = 100.0; // high: the faint donut's per-pixel peak SNR is well below this
                return p;
            }
            var pPeakOnly = Base();                                       // master OFF -> no integrated path
            var pIntegrated = Base(); pIntegrated.DefocusAwareDonutDetection = true; // master ON -> integrated path

            using var i1 = Make(); var r1 = await StarDetectorEquivalence.RunDetect(i1, pPeakOnly);
            using var i2 = Make(); var r2 = await StarDetectorEquivalence.RunDetect(i2, pIntegrated);
            Assert.Multiple(() => {
                Assert.That(DetectedNear(r1, 100, 100, 8), Is.False, "faint donut must fail the peak-based sensitivity gate");
                Assert.That(DetectedNear(r2, 100, 100, 8), Is.True, "donut-aware integrated-flux sensitivity must recover it");
            });
        }

        [Test]
        public async Task HoleFill_DoesNotChangeHFR_DetectionOnly() {
            // A THICK ring is accepted by the strict distortion gate either way (fill-ratio > MaxDistortion), so we
            // can compare the SAME accepted star with hole-fill OFF vs ON. HFR/Center/Peak must be byte-identical:
            // hole-counting feeds only the gate decision, never the measurement.
            Mat Make() {
                var m = SyntheticDefocusedStarImage.CreateAnnulus(256, 256, 128, 128,
                    innerRadius: 5, outerRadius: 22, peak: 0.7, background: 0.05, edgeBlurSigma: 1.5);
                SyntheticDefocusedStarImage.AddGaussianNoise(m, 0.008, 909);
                return m;
            }

            // Both keep the large ring alive via the same EARLY structure boost (identical early stage), so the
            // ONLY difference is the LATE hole-fill — isolating its (zero) effect on measurement.
            var pNoHole = StarDetectorEquivalence.StandardParams();
            pNoHole.PeakResponse = 0.98;
            pNoHole.DefocusAwareDonutDetection = true;
            pNoHole.DefocusAwareStructure = true;
            pNoHole.StructureLayerBoost = 3;
            pNoHole.DonutMorphCloseSize = 1;               // no early change
            pNoHole.DonutMinAnnularityHoleFraction = 0.0;  // hole-fill OFF

            var pHole = StarDetectorEquivalence.StandardParams();
            pHole.PeakResponse = 0.98;
            pHole.DefocusAwareDonutDetection = true;
            pHole.DefocusAwareStructure = true;
            pHole.StructureLayerBoost = 3;
            pHole.DonutMorphCloseSize = 1;                 // no early change (isolate hole-fill)
            pHole.DonutMinAnnularityHoleFraction = 0.15;   // hole-fill ON

            using var i1 = Make();
            var r1 = await StarDetectorEquivalence.RunDetect(i1, pNoHole);
            using var i2 = Make();
            var r2 = await StarDetectorEquivalence.RunDetect(i2, pHole);

            var s1 = (r1.DetectedStars ?? new List<Star>()).Where(s => Math.Abs(s.Center.X - 128) <= 8 && Math.Abs(s.Center.Y - 128) <= 8).ToList();
            var s2 = (r2.DetectedStars ?? new List<Star>()).Where(s => Math.Abs(s.Center.X - 128) <= 8 && Math.Abs(s.Center.Y - 128) <= 8).ToList();
            Assert.That(s1.Count, Is.EqualTo(1), "thick ring should be accepted with hole-fill off");
            Assert.That(s2.Count, Is.EqualTo(1), "thick ring should be accepted with hole-fill on");
            Assert.Multiple(() => {
                Assert.That(s2[0].HFR, Is.EqualTo(s1[0].HFR).Within(1e-9), "hole-fill must not change HFR");
                Assert.That(s2[0].Center.X, Is.EqualTo(s1[0].Center.X).Within(1e-9));
                Assert.That(s2[0].Center.Y, Is.EqualTo(s1[0].Center.Y).Within(1e-9));
                Assert.That(s2[0].PeakBrightness, Is.EqualTo(s1[0].PeakBrightness).Within(1e-9));
            });
        }
    }
}
