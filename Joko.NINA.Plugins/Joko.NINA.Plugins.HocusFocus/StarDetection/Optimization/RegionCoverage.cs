#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Inspection;
using System.Collections.Generic;
using System.Drawing;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// Spatial coverage of accepted-star centers over an equal <c>rows × cols</c> tiling of the sensor. Used by the
    /// optimizer's region-coverage reward (<see cref="OptimizationObjective.SCoverage"/>) to discourage configs that
    /// cluster their accepted stars in one part of the frame. The tiling is the same one the inspector uses
    /// (<see cref="SensorAberrationCalculator.CreateFullRegionSet"/>), so the geometry has a single source of truth.
    /// </summary>
    internal static class RegionCoverage {

        /// <summary>
        /// Fraction of the <paramref name="rows"/> × <paramref name="cols"/> sensor tiles that contain at least one
        /// of the given accepted-star <paramref name="centers"/> (full-frame pixel coords). Returns <c>NaN</c> when
        /// the geometry is unusable (no centers, or a non-positive image/grid dimension) so the caller can skip the
        /// frame. A star maps to exactly one tile (the first containing it); stars outside [0, width)×[0, height)
        /// fall in no tile and simply do not raise occupancy.
        /// </summary>
        public static double Occupancy(IReadOnlyList<(double X, double Y)> centers, int imageWidth, int imageHeight, int rows, int cols) {
            if (centers == null || centers.Count == 0 || imageWidth <= 0 || imageHeight <= 0 || rows <= 0 || cols <= 0) {
                return double.NaN;
            }

            var regions = SensorAberrationCalculator.CreateFullRegionSet(new Size(imageWidth, imageHeight), rows, cols);
            var occupied = new bool[regions.Count];
            var occupiedCount = 0;
            foreach (var (cx, cy) in centers) {
                var rx = cx / imageWidth;
                var ry = cy / imageHeight;
                for (var r = 0; r < regions.Count; r++) {
                    var b = regions[r].OuterBoundary;
                    // Direct ratio comparison (not RatioRect.Contains, whose ctor rejects edge rects): a half-open
                    // [Start, EndExclusive) test that exactly tiles the unit square.
                    if (rx >= b.StartX && rx < b.EndExclusiveX() && ry >= b.StartY && ry < b.EndExclusiveY()) {
                        if (!occupied[r]) {
                            occupied[r] = true;
                            occupiedCount++;
                        }
                        break;
                    }
                }
            }
            return (double)occupiedCount / regions.Count;
        }
    }
}
