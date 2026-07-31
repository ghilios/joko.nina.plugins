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
using System.Linq;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Golden {

    [TestFixture]
    public class GoldenGeometryTests {

        // ---- TileLayout ----------------------------------------------------------------------------------

        [Test]
        public void Positions_SingleTile_WhenExtentAtMostTile() {
            Assert.That(TileLayout.Positions(0, 1024, 1024, 128), Is.EqualTo(new[] { 0 }));
            Assert.That(TileLayout.Positions(7, 500, 1024, 128), Is.EqualTo(new[] { 7 }));
        }

        [Test]
        public void Positions_StridesAndFlushesLastTileToEdge() {
            // tile=1024, overlap=128 => stride=896. Last tile flush to end-tile so it stays full size.
            var p = TileLayout.Positions(0, 2000, 1024, 128);
            Assert.That(p, Is.EqualTo(new[] { 0, 896, 976 }));
            // Every tile stays within [0, 2000].
            Assert.That(p.All(x => x >= 0 && x + 1024 <= 2000), Is.True);
            // Coverage reaches the far edge.
            Assert.That(p.Max() + 1024, Is.EqualTo(2000));
        }

        [Test]
        public void Compute_FullFrame_CoversEdgesAndStaysInBounds() {
            const int fullW = 9576, fullH = 6388;
            var tiles = TileLayout.Compute(fullW, fullH, new IntRect(0, 0, fullW, fullH), 1024, 128);
            Assert.That(tiles, Is.Not.Empty);
            Assert.That(tiles.All(t => t.X >= 0 && t.Y >= 0 && t.Right <= fullW && t.Bottom <= fullH), Is.True);
            Assert.That(tiles[0].X, Is.EqualTo(0));
            Assert.That(tiles[0].Y, Is.EqualTo(0));
            Assert.That(tiles[0].W, Is.EqualTo(1024));
            // Some tile touches the right and bottom edges.
            Assert.That(tiles.Any(t => t.Right == fullW), Is.True);
            Assert.That(tiles.Any(t => t.Bottom == fullH), Is.True);
        }

        [Test]
        public void Compute_RespectsRegionOffset_ForInnerCrop() {
            var region = new IntRect(478, 319, 8620, 5749); // ~5%-95% inner crop
            var tiles = TileLayout.Compute(9576, 6388, region, 1024, 128);
            Assert.That(tiles[0].X, Is.EqualTo(478));
            Assert.That(tiles[0].Y, Is.EqualTo(319));
            Assert.That(tiles.All(t => t.X >= 478 && t.Y >= 319 && t.Right <= 478 + 8620 && t.Bottom <= 319 + 5749), Is.True);
        }

        [Test]
        public void TileLocalToFullFrame_Transform_IsRectOriginPlusLocal() {
            // The manifest contract: full = tile.Origin + local. Verify on a computed interior tile.
            var tiles = TileLayout.Compute(9576, 6388, new IntRect(0, 0, 9576, 6388), 1024, 128);
            var t = tiles.First(x => x.X == 896 && x.Y == 0);
            // A star the LLM marks at tile-local (10, 20) is at full-frame (906, 20).
            Assert.That(t.X + 10, Is.EqualTo(906));
            Assert.That(t.Y + 20, Is.EqualTo(20));
        }

        // ---- GoldenMatch ---------------------------------------------------------------------------------

        private static RectD G(double x, double y, double w, double h) => new RectD(x, y, w, h);
        private static DetBox D(double x, double y, double w, double h) => new DetBox(new RectD(x, y, w, h), x + w / 2.0, y + h / 2.0);

        [Test]
        public void Match_Center_StarCenterInsideGoldenBox_IsTruePositive() {
            var golden = new List<RectD> { G(100, 100, 20, 20) };
            var det = new List<DetBox> { D(105, 105, 8, 8) }; // center (109,109) inside golden
            var r = GoldenMatch.Match(golden, det, GoldenMatchMode.Center, 0.3);
            Assert.Multiple(() => {
                Assert.That(r.Pairs, Has.Count.EqualTo(1));
                Assert.That(r.FalseNegatives, Is.Empty);
                Assert.That(r.FalsePositives, Is.Empty);
            });
        }

        [Test]
        public void Match_TwoGoldenContendForOneStar_OneTP_OneFN() {
            // Both golden boxes contain the detected center; greedy gives the higher-IoU one the TP.
            var golden = new List<RectD> { G(100, 100, 40, 40), G(108, 108, 12, 12) };
            var det = new List<DetBox> { D(110, 110, 8, 8) }; // center (114,114) inside both
            var r = GoldenMatch.Match(golden, det, GoldenMatchMode.Center, 0.3);
            Assert.Multiple(() => {
                Assert.That(r.Pairs, Has.Count.EqualTo(1));
                Assert.That(r.FalseNegatives, Has.Count.EqualTo(1));
                Assert.That(r.FalsePositives, Is.Empty);
                // The tighter box (higher IoU) wins.
                Assert.That(r.Pairs[0].Golden, Is.EqualTo(1));
            });
        }

        [Test]
        public void Match_TwoStarsInOneGoldenBox_OneTP_OneFP() {
            var golden = new List<RectD> { G(100, 100, 40, 40) };
            var det = new List<DetBox> { D(104, 104, 10, 10), D(120, 120, 8, 8) };
            var r = GoldenMatch.Match(golden, det, GoldenMatchMode.Center, 0.3);
            Assert.Multiple(() => {
                Assert.That(r.Pairs, Has.Count.EqualTo(1));
                Assert.That(r.FalseNegatives, Is.Empty);
                Assert.That(r.FalsePositives, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void Match_Iou_RespectsThreshold() {
            var golden = new List<RectD> { G(0, 0, 10, 10) };
            // IoU of (5,5,10,10) vs (0,0,10,10) = 25 / 175 ≈ 0.143.
            var lowOverlap = new List<DetBox> { D(5, 5, 10, 10) };
            Assert.That(GoldenMatch.Match(golden, lowOverlap, GoldenMatchMode.Iou, 0.3).Pairs, Is.Empty);
            Assert.That(GoldenMatch.Match(golden, lowOverlap, GoldenMatchMode.Iou, 0.1).Pairs, Has.Count.EqualTo(1));
        }

        [Test]
        public void Match_CenterVsIou_DifferWhenCenterInButLowIoU() {
            var golden = new List<RectD> { G(5, 5, 10, 10) };
            var det = new List<DetBox> { D(0, 0, 10, 10) }; // center (5,5) inside golden, but IoU ≈ 0.143
            Assert.That(GoldenMatch.Match(golden, det, GoldenMatchMode.Center, 0.3).Pairs, Has.Count.EqualTo(1));
            Assert.That(GoldenMatch.Match(golden, det, GoldenMatchMode.Iou, 0.3).Pairs, Is.Empty);
            Assert.That(GoldenMatch.Match(golden, det, GoldenMatchMode.Both, 0.3).Pairs, Has.Count.EqualTo(1));
        }

        [Test]
        public void Match_EmptyInputs_AreHandled() {
            Assert.That(GoldenMatch.Match(new List<RectD>(), new List<DetBox> { D(0, 0, 5, 5) }, GoldenMatchMode.Both, 0.3).FalsePositives, Has.Count.EqualTo(1));
            Assert.That(GoldenMatch.Match(new List<RectD> { G(0, 0, 5, 5) }, new List<DetBox>(), GoldenMatchMode.Both, 0.3).FalseNegatives, Has.Count.EqualTo(1));
            var empty = GoldenMatch.Match(new List<RectD>(), new List<DetBox>(), GoldenMatchMode.Both, 0.3);
            Assert.That(empty.Pairs, Is.Empty);
        }

        [Test]
        public void CenterInRect_BoundaryInclusive() {
            var r = G(10, 10, 10, 10);
            Assert.That(GoldenMatch.CenterInRect(10, 10, r), Is.True);
            Assert.That(GoldenMatch.CenterInRect(20, 20, r), Is.True);
            Assert.That(GoldenMatch.CenterInRect(20.001, 15, r), Is.False);
        }

        // ---- PrecisionRecall -----------------------------------------------------------------------------

        [Test]
        public void PrecisionRecall_Compute_BasicCases() {
            var perfect = PrecisionRecall.Compute(5, 0, 0);
            Assert.Multiple(() => {
                Assert.That(perfect.Precision, Is.EqualTo(1.0));
                Assert.That(perfect.Recall, Is.EqualTo(1.0));
                Assert.That(perfect.F1, Is.EqualTo(1.0));
            });

            var half = PrecisionRecall.Compute(5, 5, 0);
            Assert.That(half.Precision, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(half.Recall, Is.EqualTo(1.0));

            var none = PrecisionRecall.Compute(0, 0, 0);
            Assert.That(double.IsNaN(none.Precision), Is.True);
            Assert.That(double.IsNaN(none.Recall), Is.True);
            Assert.That(double.IsNaN(none.F1), Is.True);

            var allFp = PrecisionRecall.Compute(0, 3, 0);
            Assert.That(allFp.Precision, Is.EqualTo(0.0));
            Assert.That(double.IsNaN(allFp.Recall), Is.True);
        }

        [Test]
        public void DetectionOnUnresolvedBox_IsNotAFalsePositive() {
            var unresolved = new List<RectD> { new RectD(100, 100, 20, 20) };
            var det = new List<DetBox> {
                new DetBox(new RectD(102, 102, 16, 16), 110, 110),  // inside an unresolved box
                new DetBox(new RectD(500, 500, 10, 10), 505, 505)   // genuinely spurious
            };
            var kept = GoldenMatch.ExcludeUnresolved(new List<int> { 0, 1 }, det, unresolved);
            Assert.That(kept, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void ExcludeUnresolved_WithNoUnresolvedBoxes_KeepsEveryFalsePositive() {
            var det = new List<DetBox> { new DetBox(new RectD(0, 0, 10, 10), 5, 5) };
            Assert.That(GoldenMatch.ExcludeUnresolved(new List<int> { 0 }, det, null),
                        Is.EqualTo(new List<int> { 0 }));
        }

        [Test]
        public void ExcludeUnresolved_MatchesOnOverlapNotOnlyCentre() {
            // A detection whose centre is outside the unresolved box but which overlaps it is still unjudged.
            var unresolved = new List<RectD> { new RectD(100, 100, 20, 20) };
            var det = new List<DetBox> { new DetBox(new RectD(115, 115, 20, 20), 125, 125) };
            Assert.That(GoldenMatch.ExcludeUnresolved(new List<int> { 0 }, det, unresolved), Is.Empty);
        }
    }
}
