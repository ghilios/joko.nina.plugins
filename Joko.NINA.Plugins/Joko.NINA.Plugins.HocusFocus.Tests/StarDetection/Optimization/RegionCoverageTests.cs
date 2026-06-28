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
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class RegionCoverageTests {

    [Test]
    public void Occupancy_NoCenters_ReturnsNaN() {
        Assert.That(double.IsNaN(RegionCoverage.Occupancy(Array.Empty<(double X, double Y)>(), 900, 900, 3, 3)), Is.True);
    }

    [Test]
    public void Occupancy_NonPositiveImageSize_ReturnsNaN() {
        var centers = new[] { (10.0, 10.0) };
        Assert.Multiple(() => {
            Assert.That(double.IsNaN(RegionCoverage.Occupancy(centers, 0, 900, 3, 3)), Is.True);
            Assert.That(double.IsNaN(RegionCoverage.Occupancy(centers, 900, 0, 3, 3)), Is.True);
        });
    }

    [Test]
    public void Occupancy_AllInOneCell_IsOneOverGridCount() {
        // 3×3 grid over 900×900 ⇒ each cell 300 px. All centers in the top-left cell (0..300, 0..300) ⇒ 1/9.
        var centers = new[] { (10.0, 10.0), (50.0, 200.0), (290.0, 100.0) };
        Assert.That(RegionCoverage.Occupancy(centers, 900, 900, 3, 3), Is.EqualTo(1.0 / 9).Within(1e-12));
    }

    [Test]
    public void Occupancy_OneStarPerCell_IsOne() {
        // A center in the middle of each of the 9 cells (cell size 300 ⇒ centers at 150, 450, 750).
        var pts = new List<(double, double)>();
        foreach (var cy in new[] { 150.0, 450.0, 750.0 }) {
            foreach (var cx in new[] { 150.0, 450.0, 750.0 }) {
                pts.Add((cx, cy));
            }
        }
        Assert.That(RegionCoverage.Occupancy(pts, 900, 900, 3, 3), Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void Occupancy_RisesWithSpread() {
        var clustered = new[] { (10.0, 10.0), (20.0, 20.0), (30.0, 30.0) };      // all one cell ⇒ 1/9
        var spread = new[] { (150.0, 150.0), (450.0, 450.0), (750.0, 750.0) };   // 3 distinct cells ⇒ 3/9
        Assert.That(RegionCoverage.Occupancy(spread, 900, 900, 3, 3),
            Is.GreaterThan(RegionCoverage.Occupancy(clustered, 900, 900, 3, 3)));
    }
}
