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

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Catalog {

    /// <summary>
    /// Which ASTAP/HNSKY sky partitioning a database uses. The extension of the cell files (<c>.290</c> or
    /// <c>.1476</c>) equals the number of cells the sky is divided into.
    /// </summary>
    public enum AstapPartitioning {

        /// <summary>Older 290-cell partitioning (~9.5° cells). Cell files end in <c>.290</c>.</summary>
        Astap290,

        /// <summary>Current 1476-cell partitioning (~5.14° cells). Cell files end in <c>.1476</c>.</summary>
        Astap1476
    }

    /// <summary>
    /// One intersecting cell returned by <see cref="AstapCellGeometry.FindAreas"/>.
    /// </summary>
    public readonly struct AstapAreaSelection {

        public AstapAreaSelection(int area, double fraction, string cellFileName) {
            Area = area;
            Fraction = fraction;
            CellFileName = cellFileName;
        }

        /// <summary>1-based cell/area number (1..290 or 1..1476).</summary>
        public int Area { get; }

        /// <summary>Fraction of the (square) field of view covered by this cell, in (0, 1].</summary>
        public double Fraction { get; }

        /// <summary>The bare cell file name, e.g. <c>0101.1476</c> (without the database prefix).</summary>
        public string CellFileName { get; }

        public override string ToString() => $"Area {Area} ({CellFileName}) frac={Fraction:F3}";
    }

    /// <summary>
    /// Pure (no I/O) port of the ASTAP/HNSKY sky-cell geometry from the MPL-2.0 reference reader
    /// <c>unit_star_database.pas</c> (han-k59/astap): <c>find_areas</c>, <c>area_and_boundaries</c> (.290),
    /// <c>area_and_boundaries1476</c> (.1476), and the cell-file-name mapping.
    ///
    /// The sky is divided into equal-area cells by rings of constant declination; each ring is split into a
    /// ring-specific number of RA cells. A cell (area) number is a flat 1-based index that walks the rings
    /// from the south pole (area 1) to the north pole (area 290 or 1476). The cell file name encodes the
    /// (band, cell-in-band) as <c>BBCC.&lt;ext&gt;</c> (e.g. <c>0301.1476</c> = ring/band 3, cell 1).
    ///
    /// <para>All angles are in <b>radians</b> (matching the reference). RA is expected in [0, 2π); Dec in
    /// [−π/2, π/2] (values beyond the poles resolve to the pole cell, as in the reference).</para>
    /// </summary>
    public static class AstapCellGeometry {

        private const double TwoPi = 2.0 * Math.PI;
        private const double Deg = Math.PI / 180.0;

        // ---- .290 partitioning ---------------------------------------------------------------------------

        // dec_boundaries[0..18], radians. arcsin-derived equal-area declination rings (design + reference).
        private static readonly double[] DecBoundaries290 = {
            -90.0 * Deg,
            -85.23224404 * Deg,
            -75.66348756 * Deg,
            -65.99286637 * Deg,
            -56.14497387 * Deg,
            -46.03163067 * Deg,
            -35.54307745 * Deg,
            -24.53348115 * Deg,
            -12.79440589 * Deg,
            0.0,
            12.79440589 * Deg,
            24.53348115 * Deg,
            35.54307745 * Deg,
            46.03163067 * Deg,
            56.14497387 * Deg,
            65.99286637 * Deg,
            75.66348756 * Deg,
            85.23224404 * Deg,
            90.0 * Deg
        };

        // RA cell counts per band (band 1 .. band 18). Sums to 290.
        private static readonly int[] CellsPerBand290 = {
            1, 4, 8, 12, 16, 20, 24, 28, 32, 32, 28, 24, 20, 16, 12, 8, 4, 1
        };

        // ---- .1476 partitioning --------------------------------------------------------------------------

        // dec_boundaries1476[0..36], radians. Rings are 90/17.5 = 5.142857° apart, half-steps at the poles.
        private static readonly double[] DecBoundaries1476 = {
            -90.0 * Deg,
            -87.42857143 * Deg,
            -82.28571429 * Deg,
            -77.14285714 * Deg,
            -72.0 * Deg,
            -66.85714286 * Deg,
            -61.71428571 * Deg,
            -56.57142857 * Deg,
            -51.42857143 * Deg,
            -46.28571429 * Deg,
            -41.14285714 * Deg,
            -36.0 * Deg,
            -30.85714286 * Deg,
            -25.71428571 * Deg,
            -20.57142857 * Deg,
            -15.42857143 * Deg,
            -10.28571429 * Deg,
            -5.142857143 * Deg,
            0.0,
            5.142857143 * Deg,
            10.28571429 * Deg,
            15.42857143 * Deg,
            20.57142857 * Deg,
            25.71428571 * Deg,
            30.85714286 * Deg,
            36.0 * Deg,
            41.14285714 * Deg,
            46.28571429 * Deg,
            51.42857143 * Deg,
            56.57142857 * Deg,
            61.71428571 * Deg,
            66.85714286 * Deg,
            72.0 * Deg,
            77.14285714 * Deg,
            82.28571429 * Deg,
            87.42857143 * Deg,
            90.0 * Deg
        };

        // RA cell counts per band (band 1 .. band 36). Sums to 1476.
        private static readonly int[] CellsPerBand1476 = {
            1, 3, 9, 15, 21, 27, 33, 38, 43, 48, 52, 56, 60, 63, 65, 67, 68, 69,
            69, 68, 67, 65, 63, 60, 56, 52, 48, 43, 38, 33, 27, 21, 15, 9, 3, 1
        };

        // Cumulative cell count before each band: CumBefore[b] = sum of cells in bands 1..b-1 (1-based band).
        // Index 0 is unused; CumBefore[1] = 0. Length nBands+2 so CumBefore[nBands+1] == total (== area count).
        private static readonly int[] CumBefore290 = BuildCumBefore(CellsPerBand290);
        private static readonly int[] CumBefore1476 = BuildCumBefore(CellsPerBand1476);

        private static int[] BuildCumBefore(int[] cellsPerBand) {
            var cum = new int[cellsPerBand.Length + 2];
            cum[1] = 0;
            for (var b = 1; b <= cellsPerBand.Length; b++) {
                cum[b + 1] = cum[b] + cellsPerBand[b - 1];
            }
            return cum;
        }

        /// <summary>Total number of cells for a partitioning (290 or 1476).</summary>
        public static int AreaCount(AstapPartitioning partitioning) =>
            partitioning == AstapPartitioning.Astap1476 ? 1476 : 290;

        /// <summary>Cell-file extension for a partitioning ("290" or "1476").</summary>
        public static string Extension(AstapPartitioning partitioning) =>
            partitioning == AstapPartitioning.Astap1476 ? "1476" : "290";

        /// <summary>
        /// Maximum FOV (radians) a single query may span before <c>find_areas</c> would risk missing a tile:
        /// one cell dimension (9.53° for .290, 5.142857° for .1476). Matches the reference crop.
        /// </summary>
        public static double MaxFovRadians(AstapPartitioning partitioning) =>
            (partitioning == AstapPartitioning.Astap1476 ? 5.142857 : 9.53) * Deg;

        /// <summary>
        /// Port of <c>area_and_boundaries</c> / <c>area_and_boundaries1476</c>: the cell (area) number that
        /// contains the given (ra, dec) [radians].
        /// </summary>
        public static int AreaForCoordinates(double raRadians, double decRadians, AstapPartitioning partitioning) {
            AreaAndBoundaries(raRadians, decRadians, partitioning, out var area, out _, out _, out _, out _);
            return area;
        }

        /// <summary>
        /// The bare cell file name for a 1-based area number, e.g. <c>0301.1476</c>. Mirrors the reference
        /// <c>filenames290</c>/<c>filenames1476</c> arrays (generated from the per-band cell counts; verified
        /// against the explicit arrays in the unit tests).
        /// </summary>
        public static string CellFileName(int area, AstapPartitioning partitioning) {
            var count = AreaCount(partitioning);
            if (area < 1 || area > count) {
                throw new ArgumentOutOfRangeException(nameof(area), area, $"Area must be in [1, {count}] for {partitioning}.");
            }
            var cellsPerBand = partitioning == AstapPartitioning.Astap1476 ? CellsPerBand1476 : CellsPerBand290;
            var cumBefore = partitioning == AstapPartitioning.Astap1476 ? CumBefore1476 : CumBefore290;
            // Find the band b (1-based) such that cumBefore[b] < area <= cumBefore[b] + cellsPerBand[b-1].
            for (var b = 1; b <= cellsPerBand.Length; b++) {
                var cellInBand = area - cumBefore[b];
                if (cellInBand >= 1 && cellInBand <= cellsPerBand[b - 1]) {
                    return $"{b:D2}{cellInBand:D2}.{Extension(partitioning)}";
                }
            }
            // Unreachable given the range check above.
            throw new ArgumentOutOfRangeException(nameof(area), area, "Could not map area to a cell file.");
        }

        /// <summary>
        /// EVERY cell intersecting a cone of angular radius <c>fovRadians / 2</c> about (ra, dec) — the wide-field
        /// counterpart to <see cref="FindAreas"/>.
        ///
        /// <para><b>Why this exists (F44).</b> <see cref="FindAreas"/> is a faithful port of ASTAP's
        /// <c>find_areas</c>, which clamps the field to ONE CELL (<see cref="MaxFovRadians"/>) and then samples
        /// only the four CORNERS of that clamped box. That is exactly right for plate-solving, where fields are a
        /// few degrees and four corner cells genuinely cover them. It is wrong for rendering: a 40 mm lens on a
        /// full-frame sensor spans ~57°, the clamp cuts the query to 5.14°, and the simulator rendered stars over
        /// a ~10° patch of a 48.5° × 33.4° frame with the rest of the sensor empty. Nothing warned, because the
        /// query returned plenty of stars — just not the right ones.</para>
        ///
        /// <para><b>Deliberately over-inclusive.</b> The RA span per band is padded by one whole cell on each
        /// side, and a cone touching a pole takes every cell of every band it reaches. Extra cells cost a little
        /// I/O and are then trimmed by the caller's per-star angular cut and by the projection's frame bounds;
        /// missing cells are the defect. Correctness first.</para>
        ///
        /// <para><see cref="AstapAreaSelection.Fraction"/> is reported as an even split. The reference's
        /// per-corner coverage arithmetic does not generalize to an arbitrary cell count, and nothing reads the
        /// value — it exists for diagnostics.</para>
        /// </summary>
        /// <param name="raRadians">Field-center RA [radians].</param>
        /// <param name="decRadians">Field-center Dec [radians].</param>
        /// <param name="fovRadians">Full field-of-view angle [radians]; NOT clamped.</param>
        /// <param name="partitioning">Which cell partitioning the target database uses.</param>
        public static IReadOnlyList<AstapAreaSelection> FindAreasCovering(
            double raRadians, double decRadians, double fovRadians, AstapPartitioning partitioning) {
            if (fovRadians <= 0.0) {
                throw new ArgumentOutOfRangeException(nameof(fovRadians), fovRadians, "FOV must be positive.");
            }

            var decB = partitioning == AstapPartitioning.Astap1476 ? DecBoundaries1476 : DecBoundaries290;
            var cellsPerBand = partitioning == AstapPartitioning.Astap1476 ? CellsPerBand1476 : CellsPerBand290;
            var cumBefore = partitioning == AstapPartitioning.Astap1476 ? CumBefore1476 : CumBefore290;
            var nBands = cellsPerBand.Length;

            var ra = NormalizeRa(raRadians);
            var radius = Math.Min(fovRadians / 2.0, Math.PI);
            var decLo = decRadians - radius;
            var decHi = decRadians + radius;

            var areas = new SortedSet<int>();
            for (var b = 1; b <= nBands; b++) {
                var bandLo = decB[b - 1];
                var bandHi = decB[b];
                if (bandHi < decLo || bandLo > decHi) {
                    continue; // band lies entirely outside the cone's declination range
                }

                var cellsInBand = cellsPerBand[b - 1];
                if (cellsInBand <= 1) {
                    areas.Add(cumBefore[b] + 1); // pole cell: one cell spans all RA
                    continue;
                }

                var cellWidth = TwoPi / cellsInBand;
                // No special case for a cone containing a pole: MaxRaHalfSpan already returns pi there, both via
                // its cos(dec) guard and because the law-of-cosines term drops below -1 (at dec0 = 89 deg with
                // r = 15 deg it evaluates to -41.5). A separate touchesPole branch WAS written here, measured
                // against the tests, found to change nothing, and removed rather than left as an untested path.
                var halfSpan = MaxRaHalfSpan(decRadians, radius, Math.Max(bandLo, decLo), Math.Min(bandHi, decHi));
                if (double.IsNaN(halfSpan)) {
                    continue; // the cone does not actually reach into this band
                }

                if (halfSpan + cellWidth >= Math.PI) {
                    for (var i = 0; i < cellsInBand; i++) {
                        areas.Add(cumBefore[b] + 1 + i);
                    }
                    continue;
                }

                // Pad by a whole cell each side, then walk the (possibly wrapping) index range.
                var first = (int)Math.Floor((ra - halfSpan - cellWidth) / cellWidth);
                var last = (int)Math.Floor((ra + halfSpan + cellWidth) / cellWidth);
                for (var i = first; i <= last; i++) {
                    var idx = ((i % cellsInBand) + cellsInBand) % cellsInBand;
                    areas.Add(cumBefore[b] + 1 + idx);
                }
            }

            // CellFileName, NOT the bare area number: the on-disk names are band/index encoded ("0101.1476"),
            // and building them by hand here would produce files that do not exist.
            var fraction = areas.Count > 0 ? 1.0 / areas.Count : 0.0;
            var result = new List<AstapAreaSelection>(areas.Count);
            foreach (var area in areas) {
                result.Add(new AstapAreaSelection(area, fraction, CellFileName(area, partitioning)));
            }
            return result;
        }

        /// <summary>
        /// The largest half-width in RA [radians] that a cone of <paramref name="radius"/> about
        /// (<paramref name="centerDec"/>, implicit RA) subtends anywhere in the declination range
        /// [<paramref name="decFrom"/>, <paramref name="decTo"/>]. NaN when the cone does not reach that range.
        ///
        /// <para>From the spherical law of cosines: <c>cos Δra = (cos r − sin d₀ sin d) / (cos d₀ cos d)</c>.
        /// Evaluated at both ends of the range and at the cone centre's own declination when that falls inside it,
        /// because the extremum can sit at either an endpoint or the centre.</para>
        /// </summary>
        private static double MaxRaHalfSpan(double centerDec, double radius, double decFrom, double decTo) {
            var best = double.NaN;
            var samples = decFrom <= centerDec && centerDec <= decTo
                ? new[] { decFrom, decTo, centerDec }
                : new[] { decFrom, decTo };
            foreach (var d in samples) {
                var cosD = Math.Cos(d);
                var cosD0 = Math.Cos(centerDec);
                if (cosD <= 1e-12 || cosD0 <= 1e-12) {
                    return Math.PI; // at/near a pole every meridian is within reach
                }
                var cosDelta = (Math.Cos(radius) - (Math.Sin(centerDec) * Math.Sin(d))) / (cosD0 * cosD);
                if (cosDelta <= -1.0) {
                    return Math.PI;
                }
                if (cosDelta >= 1.0) {
                    continue; // cone does not reach this declination
                }
                var delta = Math.Acos(cosDelta);
                if (double.IsNaN(best) || delta > best) {
                    best = delta;
                }
            }
            return best;
        }

        /// <summary>
        /// Port of <c>find_areas</c>: the 1–4 cells whose union covers the square field of view centered on
        /// (ra, dec). Returns each intersecting cell once with the fraction of the FOV it covers (in (0, 1]),
        /// dropping duplicates and cells covering &lt; 1% of the frame — exactly as the reference does.
        /// The first entry corresponds to the reference's primary corner (area1).
        /// </summary>
        /// <param name="raRadians">Field-center RA [radians], expected in [0, 2π).</param>
        /// <param name="decRadians">Field-center Dec [radians].</param>
        /// <param name="fovRadians">Field-of-view side length [radians]; internally capped at
        /// <see cref="MaxFovRadians"/> (reference behavior).</param>
        /// <param name="partitioning">Which cell partitioning the target database uses.</param>
        public static IReadOnlyList<AstapAreaSelection> FindAreas(
            double raRadians, double decRadians, double fovRadians, AstapPartitioning partitioning) {
            if (fovRadians <= 0.0) {
                throw new ArgumentOutOfRangeException(nameof(fovRadians), fovRadians, "FOV must be positive.");
            }

            var fov = Math.Min(fovRadians, MaxFovRadians(partitioning));
            var fovHalf = fov / 2.0;

            // Corners of the square field. dec beyond ±90° is fine — it resolves to the pole cell.
            var decCornerN = decRadians + fovHalf;
            var decCornerS = decRadians - fovHalf;

            var raCornerWN = raRadians - fovHalf / Math.Cos(decCornerN);
            if (raCornerWN < 0.0) raCornerWN += TwoPi; // west: RA decreases
            var raCornerEN = raRadians + fovHalf / Math.Cos(decCornerN);
            if (raCornerEN >= TwoPi) raCornerEN -= TwoPi;
            var raCornerWS = raRadians - fovHalf / Math.Cos(decCornerS);
            if (raCornerWS < 0.0) raCornerWS += TwoPi;
            var raCornerES = raRadians + fovHalf / Math.Cos(decCornerS);
            if (raCornerES >= TwoPi) raCornerES -= TwoPi;

            var fov2 = fov * fov;

            // Corner 1 (E, N)
            AreaAndBoundaries(raCornerEN, decCornerN, partitioning, out var area1, out _, out var spaceW1, out _, out var spaceS1);
            var frac1 = Math.Min(spaceW1, fov) * Math.Min(spaceS1, fov) / fov2;

            // Corner 2 (W, N)
            AreaAndBoundaries(raCornerWN, decCornerN, partitioning, out var area2, out var spaceE2, out _, out _, out var spaceS2);
            var frac2 = Math.Min(spaceE2, fov) * Math.Min(spaceS2, fov) / fov2;

            // Corner 3 (E, S)
            AreaAndBoundaries(raCornerES, decCornerS, partitioning, out var area3, out _, out var spaceW3, out var spaceN3, out _);
            var frac3 = Math.Min(spaceW3, fov) * Math.Min(spaceN3, fov) / fov2;

            // Corner 4 (W, S)
            AreaAndBoundaries(raCornerWS, decCornerS, partitioning, out var area4, out var spaceE4, out _, out var spaceN4, out _);
            var frac4 = Math.Min(spaceE4, fov) * Math.Min(spaceN4, fov) / fov2;

            // De-duplicate (reference order). Area numbers are ≥ 1, so a zeroed slot never spuriously matches.
            if (area2 == area1) { area2 = 0; frac2 = 0; }
            if (area3 == area1) { area3 = 0; frac3 = 0; }
            if (area4 == area1) { area4 = 0; frac4 = 0; }
            if (area3 == area2) { area3 = 0; frac3 = 0; }
            if (area4 == area2) { area4 = 0; frac4 = 0; }
            if (area4 == area3) { area4 = 0; frac4 = 0; }

            // Drop negligible slivers.
            if (frac1 < 0.01) { area1 = 0; frac1 = 0; }
            if (frac2 < 0.01) { area2 = 0; frac2 = 0; }
            if (frac3 < 0.01) { area3 = 0; frac3 = 0; }
            if (frac4 < 0.01) { area4 = 0; frac4 = 0; }

            var result = new List<AstapAreaSelection>(4);
            AddIfPresent(result, area1, frac1, partitioning);
            AddIfPresent(result, area2, frac2, partitioning);
            AddIfPresent(result, area3, frac3, partitioning);
            AddIfPresent(result, area4, frac4, partitioning);
            return result;
        }

        private static void AddIfPresent(List<AstapAreaSelection> result, int area, double frac, AstapPartitioning p) {
            if (area != 0) {
                result.Add(new AstapAreaSelection(area, frac, CellFileName(area, p)));
            }
        }

        /// <summary>
        /// Port of <c>area_and_boundaries</c> (.290) / <c>area_and_boundaries1476</c> (.1476): for a (ra, dec)
        /// find the cell number plus the angular distances to the cell's East/West/North/South boundaries.
        /// spaceW/spaceE are RA-boundary distances already scaled by cos(dec) (great-circle), matching the
        /// reference; spaceN/spaceS are Dec distances. Poles report spaceW = spaceE = 2π.
        /// </summary>
        private static void AreaAndBoundaries(
            double ra, double dec, AstapPartitioning partitioning,
            out int areaNr, out double spaceE, out double spaceW, out double spaceN, out double spaceS) {

            var decB = partitioning == AstapPartitioning.Astap1476 ? DecBoundaries1476 : DecBoundaries290;
            var cellsPerBand = partitioning == AstapPartitioning.Astap1476 ? CellsPerBand1476 : CellsPerBand290;
            var cumBefore = partitioning == AstapPartitioning.Astap1476 ? CumBefore1476 : CumBefore290;
            var nBands = cellsPerBand.Length; // 18 or 36
            var cosDec = Math.Cos(dec);

            // Normalize RA to [0, 2π). The reference assumes this precondition; normalizing here hardens the
            // public API so a negative or out-of-range RA can't mis-cell at a boundary. (No-op for RA already
            // in range, so it does not alter the byte-accurate port for valid inputs. Poles ignore RA.)
            ra = NormalizeRa(ra);

            // North pole cell.
            if (dec > decB[nBands - 1]) {
                areaNr = AreaCount(partitioning);
                spaceS = dec - decB[nBands - 1];
                spaceN = decB[nBands] - decB[nBands - 1]; // minimum; could go past the pole
                spaceW = TwoPi;
                spaceE = TwoPi;
                return;
            }

            // Middle bands: find the largest boundary index k in [1, nBands-2] with dec > decB[k]; band = k+1.
            for (var k = nBands - 2; k >= 1; k--) {
                if (dec > decB[k]) {
                    var b = k + 1; // 1-based band number
                    var cellsInBand = cellsPerBand[b - 1];
                    var rot = ra * cellsInBand / TwoPi;
                    var fracRot = rot - Math.Truncate(rot);
                    areaNr = cumBefore[b] + 1 + (int)Math.Floor(rot); // == Pascal trunc(rot) for rot ≥ 0

                    spaceS = dec - decB[k];
                    // Northern boundary of the band. Reference quirk: for the .1476 sub-equatorial band (band
                    // 18, dec ∈ (−5.14°, 0°]) area_and_boundaries1476 uses dec_boundaries1476[19] here rather
                    // than [18]=0. Replicated faithfully (reference source wins over the design doc).
                    var northBoundary = (partitioning == AstapPartitioning.Astap1476 && b == 18)
                        ? decB[19]
                        : decB[b];
                    spaceN = northBoundary - dec;

                    spaceW = TwoPi / cellsInBand * fracRot * cosDec;      // RA decreases toward west
                    spaceE = TwoPi / cellsInBand * (1.0 - fracRot) * cosDec;
                    return;
                }
            }

            // South pole cell.
            areaNr = 1;
            spaceS = decB[1] - decB[0]; // minimum; could go past the pole
            spaceN = decB[1] - dec;
            spaceW = TwoPi;
            spaceE = TwoPi;
        }

        private static double NormalizeRa(double ra) {
            var r = ra % TwoPi;
            if (r < 0.0) {
                r += TwoPi;
            }
            return r;
        }
    }
}
