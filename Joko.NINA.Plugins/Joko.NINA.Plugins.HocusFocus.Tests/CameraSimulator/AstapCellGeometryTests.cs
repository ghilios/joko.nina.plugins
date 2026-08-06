using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

// Port-fidelity + invariant tests for the pure ASTAP/HNSKY cell geometry (find_areas / area_and_boundaries).
// Absolute cell-number ground truth would require running ASTAP itself; the bar here is faithful port of the
// reference plus the structural invariants the reference guarantees.
[TestFixture]
public class AstapCellGeometryTests {
    private const double Deg = Math.PI / 180.0;

    // ---- CellFileName maps area -> file name, verified against the reference filenames arrays ----------

    private static IEnumerable<TestCaseData> CellFileNameSamples() {
        // (area, partitioning, expected) — expected values read directly from filenames290 / filenames1476.
        yield return new TestCaseData(1, AstapPartitioning.Astap290, "0101.290");
        yield return new TestCaseData(2, AstapPartitioning.Astap290, "0201.290");
        yield return new TestCaseData(5, AstapPartitioning.Astap290, "0204.290");
        yield return new TestCaseData(6, AstapPartitioning.Astap290, "0301.290");
        yield return new TestCaseData(13, AstapPartitioning.Astap290, "0308.290");
        yield return new TestCaseData(14, AstapPartitioning.Astap290, "0401.290");
        yield return new TestCaseData(113, AstapPartitioning.Astap290, "0828.290");
        yield return new TestCaseData(114, AstapPartitioning.Astap290, "0901.290");
        yield return new TestCaseData(145, AstapPartitioning.Astap290, "0932.290");
        yield return new TestCaseData(146, AstapPartitioning.Astap290, "1001.290");
        yield return new TestCaseData(290, AstapPartitioning.Astap290, "1801.290");

        yield return new TestCaseData(1, AstapPartitioning.Astap1476, "0101.1476");
        yield return new TestCaseData(2, AstapPartitioning.Astap1476, "0201.1476");
        yield return new TestCaseData(4, AstapPartitioning.Astap1476, "0203.1476");
        yield return new TestCaseData(5, AstapPartitioning.Astap1476, "0301.1476");
        yield return new TestCaseData(13, AstapPartitioning.Astap1476, "0309.1476");
        yield return new TestCaseData(14, AstapPartitioning.Astap1476, "0401.1476");
        yield return new TestCaseData(29, AstapPartitioning.Astap1476, "0501.1476");
        yield return new TestCaseData(670, AstapPartitioning.Astap1476, "1801.1476");
        yield return new TestCaseData(738, AstapPartitioning.Astap1476, "1869.1476");
        yield return new TestCaseData(1476, AstapPartitioning.Astap1476, "3601.1476");
    }

    [TestCaseSource(nameof(CellFileNameSamples))]
    public void CellFileName_MatchesReferenceArrays(int area, AstapPartitioning partitioning, string expected) {
        Assert.That(AstapCellGeometry.CellFileName(area, partitioning), Is.EqualTo(expected));
    }

    [Test]
    public void CellFileName_EnumeratesEveryAreaExactlyOnce_ForBothPartitionings() {
        foreach (var partitioning in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            var count = AstapCellGeometry.AreaCount(partitioning);
            var names = new HashSet<string>();
            for (var area = 1; area <= count; area++) {
                var name = AstapCellGeometry.CellFileName(area, partitioning);
                Assert.That(name.EndsWith("." + AstapCellGeometry.Extension(partitioning)), Is.True, name);
                Assert.That(names.Add(name), Is.True, $"duplicate cell file name {name} for area {area}");
            }
            Assert.That(names.Count, Is.EqualTo(count), $"{partitioning} should map to {count} distinct files");
        }
    }

    [Test]
    public void CellFileName_OutOfRange_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(() => AstapCellGeometry.CellFileName(0, AstapPartitioning.Astap290));
        Assert.Throws<ArgumentOutOfRangeException>(() => AstapCellGeometry.CellFileName(291, AstapPartitioning.Astap290));
        Assert.Throws<ArgumentOutOfRangeException>(() => AstapCellGeometry.CellFileName(1477, AstapPartitioning.Astap1476));
    }

    // ---- AreaForCoordinates invariants -----------------------------------------------------------------

    [Test]
    public void AreaForCoordinates_AlwaysInRange_OverSkyGrid() {
        foreach (var partitioning in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            var count = AstapCellGeometry.AreaCount(partitioning);
            for (var decDeg = -89.0; decDeg <= 89.0; decDeg += 3.0) {
                for (var raDeg = 0.0; raDeg < 360.0; raDeg += 7.0) {
                    var area = AstapCellGeometry.AreaForCoordinates(raDeg * Deg, decDeg * Deg, partitioning);
                    Assert.That(area, Is.InRange(1, count), $"ra={raDeg} dec={decDeg} {partitioning}");
                }
            }
            // Poles resolve to the pole cells.
            Assert.That(AstapCellGeometry.AreaForCoordinates(1.2, -89.99 * Deg, partitioning), Is.EqualTo(1));
            Assert.That(AstapCellGeometry.AreaForCoordinates(1.2, 89.99 * Deg, partitioning), Is.EqualTo(count));
        }
    }

    // ---- FindAreas: 1 area for a small FOV well inside a cell -------------------------------------------

    private static IEnumerable<TestCaseData> InteriorCenters() {
        // Points chosen to sit comfortably inside a cell (away from ring/RA boundaries).
        yield return new TestCaseData(30.0, 40.0);
        yield return new TestCaseData(120.0, -20.0);
        yield return new TestCaseData(200.0, 5.0);
        yield return new TestCaseData(300.0, -55.0);
        yield return new TestCaseData(15.0, 62.0);
    }

    [TestCaseSource(nameof(InteriorCenters))]
    public void FindAreas_SmallFovInsideCell_ReturnsExactlyOneArea(double raDeg, double decDeg) {
        foreach (var partitioning in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            var count = AstapCellGeometry.AreaCount(partitioning);
            var result = AstapCellGeometry.FindAreas(raDeg * Deg, decDeg * Deg, 0.2 * Deg, partitioning);
            Assert.That(result.Count, Is.EqualTo(1), $"{partitioning} ra={raDeg} dec={decDeg} -> {result.Count} areas");

            var only = result[0];
            Assert.That(only.Area, Is.InRange(1, count));
            Assert.That(only.Fraction, Is.GreaterThan(0.0).And.LessThanOrEqualTo(1.0));
            Assert.That(only.CellFileName, Is.EqualTo(AstapCellGeometry.CellFileName(only.Area, partitioning)));

            // The primary area (find_areas' area1) equals area_and_boundaries(center) for an interior point.
            var centerArea = AstapCellGeometry.AreaForCoordinates(raDeg * Deg, decDeg * Deg, partitioning);
            Assert.That(only.Area, Is.EqualTo(centerArea));
        }
    }

    // ---- FindAreas: straddling a boundary returns 2..4 areas --------------------------------------------

    [Test]
    public void FindAreas_StraddlingRaBoundary_ReturnsMultipleAreas() {
        // Near the equator a .1476 ring has 69 RA cells; the boundary between cells 0 and 1 sits at
        // RA = 360/69 ≈ 5.2174°. A ~2° FOV straddling it must select more than one cell.
        const double decDeg = 2.0;
        var raBoundaryDeg = 360.0 / 69.0;
        var result = AstapCellGeometry.FindAreas(raBoundaryDeg * Deg, decDeg * Deg, 2.0 * Deg, AstapPartitioning.Astap1476);
        Assert.That(result.Count, Is.InRange(2, 4), $"got {result.Count}");
        AssertValidSelections(result, AstapPartitioning.Astap1476);
    }

    [Test]
    public void FindAreas_StraddlingDecRing_ReturnsMultipleAreas() {
        // A .1476 declination ring boundary sits at 5.142857°. A FOV centered on it spans two rings.
        const double raDeg = 45.0;
        var decRingDeg = 5.142857;
        var result = AstapCellGeometry.FindAreas(raDeg * Deg, decRingDeg * Deg, 2.0 * Deg, AstapPartitioning.Astap1476);
        Assert.That(result.Count, Is.InRange(2, 4), $"got {result.Count}");
        AssertValidSelections(result, AstapPartitioning.Astap1476);
    }

    // ---- FindAreas: general invariants over many (ra, dec, fov) -----------------------------------------

    [Test]
    public void FindAreas_Invariants_OverManyFields() {
        var rng = new Random(12345);
        foreach (var partitioning in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            for (var i = 0; i < 500; i++) {
                var raDeg = rng.NextDouble() * 360.0;
                var decDeg = (rng.NextDouble() * 178.0) - 89.0;
                var fovDeg = 0.1 + (rng.NextDouble() * 3.0);
                var result = AstapCellGeometry.FindAreas(raDeg * Deg, decDeg * Deg, fovDeg * Deg, partitioning);

                Assert.That(result.Count, Is.InRange(1, 4), $"{partitioning} ra={raDeg} dec={decDeg} fov={fovDeg}");
                AssertValidSelections(result, partitioning);

                // Distinct area numbers (find_areas de-duplicates).
                var distinct = result.Select(r => r.Area).Distinct().Count();
                Assert.That(distinct, Is.EqualTo(result.Count), "areas must be distinct");
            }
        }
    }

    [Test]
    public void FindAreas_NonPositiveFov_Throws() {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AstapCellGeometry.FindAreas(1.0, 0.5, 0.0, AstapPartitioning.Astap1476));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AstapCellGeometry.FindAreas(1.0, 0.5, -0.1, AstapPartitioning.Astap1476));
    }

    private static void AssertValidSelections(IReadOnlyList<AstapAreaSelection> result, AstapPartitioning partitioning) {
        var count = AstapCellGeometry.AreaCount(partitioning);
        foreach (var sel in result) {
            Assert.That(sel.Area, Is.InRange(1, count));
            Assert.That(sel.Fraction, Is.GreaterThan(0.0).And.LessThanOrEqualTo(1.0), $"fraction {sel.Fraction}");
            Assert.That(sel.CellFileName, Is.EqualTo(AstapCellGeometry.CellFileName(sel.Area, partitioning)));
        }
    }

    // ---- F44: wide-field cell enumeration ------------------------------------------------------------------
    //
    // find_areas clamps the field to ONE CELL and samples only its four corners. Correct for plate-solving,
    // catastrophic for rendering: a 40 mm full-frame field is ~57°, the clamp cut the catalog query to 5.14°,
    // and the simulator drew stars over a ~10° patch of a 48.5° x 33.4° frame with the rest of the sensor empty.

    /// <summary>The reference brute force: every cell whose CENTRE lies within the cone, found by sampling the
    /// sphere densely. Independent of the routine under test, so it can disagree with it.</summary>
    private static HashSet<int> CellsByDenseSampling(double raRad, double decRad, double radiusRad, AstapPartitioning p) {
        var hit = new HashSet<int>();
        const int NDec = 900, NRa = 1800;
        for (var i = 0; i <= NDec; i++) {
            var d = -Math.PI / 2 + (Math.PI * i / NDec);
            for (var j = 0; j < NRa; j++) {
                var r = 2 * Math.PI * j / NRa;
                var cosSep = (Math.Sin(decRad) * Math.Sin(d)) + (Math.Cos(decRad) * Math.Cos(d) * Math.Cos(r - raRad));
                if (cosSep >= Math.Cos(radiusRad)) {
                    hit.Add(AstapCellGeometry.AreaForCoordinates(r, d, p));
                }
            }
        }
        return hit;
    }

    [TestCase(305.5571, 40.2567, 59.67)]   // the reporting rig: 40 mm full-frame
    [TestCase(305.5571, 40.2567, 12.55)]   // D02_rich_135mm
    [TestCase(10.0, 0.0, 40.0)]            // equator, crossing RA = 0
    [TestCase(350.0, -20.0, 50.0)]         // southern, wrapping RA
    [TestCase(0.0, 89.0, 30.0)]            // cone containing the north pole
    [TestCase(180.0, -88.0, 25.0)]         // cone containing the south pole
    public void FindAreasCovering_MissesNoCellThatTheConeActuallyTouches(double raDeg, double decDeg, double fovDeg) {
        foreach (var part in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            var ra = raDeg * Deg;
            var dec = decDeg * Deg;
            var covering = AstapCellGeometry.FindAreasCovering(ra, dec, fovDeg * Deg, part)
                .Select(a => a.Area).ToHashSet();
            var expected = CellsByDenseSampling(ra, dec, fovDeg * Deg / 2.0, part);

            var missed = expected.Except(covering).OrderBy(x => x).ToList();
            Assert.That(missed, Is.Empty,
                $"{part} at ({raDeg},{decDeg}) fov {fovDeg}: cells inside the cone that were never queried: " +
                string.Join(",", missed));
        }
    }

    [Test]
    public void FindAreasCovering_IsVastlyWiderThanFindAreas_OnA40mmField() {
        // The defect, as an inequality. find_areas clamps 59.67 deg to one cell and returns at most 4.
        const double Ra = 305.5571 * (Math.PI / 180.0), Dec = 40.2567 * (Math.PI / 180.0), Fov = 59.67 * (Math.PI / 180.0);
        var reference = AstapCellGeometry.FindAreas(Ra, Dec, Fov, AstapPartitioning.Astap1476);
        var covering = AstapCellGeometry.FindAreasCovering(Ra, Dec, Fov, AstapPartitioning.Astap1476);

        Assert.Multiple(() => {
            Assert.That(reference.Count, Is.LessThanOrEqualTo(4), "find_areas returns at most 4 cells by construction");
            Assert.That(covering.Count, Is.GreaterThan(50), "a 59.67 deg field spans far more than 4 of the 5.14 deg cells");
            Assert.That(covering.Select(c => c.Area).ToHashSet().IsSupersetOf(reference.Select(c => c.Area)),
                Is.True, "the wide sweep must still include everything the reference found");
        });
    }

    [Test]
    public void FindAreasCovering_ReturnsRealCellFileNamesAndDistinctAreas() {
        // Building "<area>.1476" by hand would name files that do not exist on disk; the encoding is band/index.
        var cells = AstapCellGeometry.FindAreasCovering(
            305.5571 * Deg, 40.2567 * Deg, 59.67 * Deg, AstapPartitioning.Astap1476);

        Assert.Multiple(() => {
            Assert.That(cells.Select(c => c.Area).Distinct().Count(), Is.EqualTo(cells.Count), "no duplicate cells");
            foreach (var c in cells) {
                Assert.That(c.Area, Is.InRange(1, AstapCellGeometry.AreaCount(AstapPartitioning.Astap1476)));
                Assert.That(c.CellFileName, Is.EqualTo(AstapCellGeometry.CellFileName(c.Area, AstapPartitioning.Astap1476)));
            }
        });
    }

    [TestCase(0.0, 89.0, 30.0)]
    [TestCase(123.0, -88.5, 20.0)]
    [TestCase(200.0, 90.0, 44.0)]
    public void FindAreasCovering_AConeContainingAPoleTakesEVERYMeridianOfEveryBandItReaches(
        double raDeg, double decDeg, double fovDeg) {
        // A cone that swallows a pole has no bounded RA range: every meridian is inside it. Computing one from
        // the law of cosines leaves a narrow wrap-around sliver unqueried (halfSpan lands just under pi, so the
        // "covers the circle" branch does not fire and the padded index walk stops a few degrees short). The
        // sliver is easy to miss with a centre-sampling oracle, so assert the structural property directly.
        foreach (var part in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            var ra = raDeg * Deg;
            var dec = decDeg * Deg;
            var radius = fovDeg * Deg / 2.0;
            var returned = AstapCellGeometry.FindAreasCovering(ra, dec, fovDeg * Deg, part)
                .Select(a => a.Area).ToHashSet();

            // Any band the cone reaches must be present in FULL.
            var reached = returned.Select(a => BandOf(a, part)).Distinct().ToList();
            Assert.That(reached, Is.Not.Empty);
            foreach (var band in reached) {
                foreach (var area in AreasInBand(band, part)) {
                    Assert.That(returned, Does.Contain(area),
                        $"{part} at ({raDeg},{decDeg}) fov {fovDeg}: band {band} is only partly queried " +
                        $"(missing area {area}) — a pole-containing cone covers every meridian");
                }
            }
        }
    }

    // Band arithmetic mirrored from the geometry's own cumulative table, via the public area->name encoding:
    // the file name's first four digits are "BBII" (band, index within band), so the band is recoverable.
    private static int BandOf(int area, AstapPartitioning p) =>
        int.Parse(AstapCellGeometry.CellFileName(area, p).Substring(0, 2));

    private static IEnumerable<int> AreasInBand(int band, AstapPartitioning p) {
        for (var a = 1; a <= AstapCellGeometry.AreaCount(p); a++) {
            if (BandOf(a, p) == band) {
                yield return a;
            }
        }
    }

    [Test]
    public void FindAreasCovering_AWholeSkyConeReturnsEveryCell() {
        foreach (var part in new[] { AstapPartitioning.Astap290, AstapPartitioning.Astap1476 }) {
            var cells = AstapCellGeometry.FindAreasCovering(0.0, 0.0, 360.0 * Deg, part);
            Assert.That(cells.Count, Is.EqualTo(AstapCellGeometry.AreaCount(part)), $"{part}");
        }
    }
}
