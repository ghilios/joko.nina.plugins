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
}
