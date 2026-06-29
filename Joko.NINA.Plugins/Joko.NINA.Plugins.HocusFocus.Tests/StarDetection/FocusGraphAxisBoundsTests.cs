using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;
using OxyPlot;
using OxyPlot.Series;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class FocusGraphAxisBoundsTests {

        private static List<ScatterErrorPoint> Pts(params (double x, double y)[] xy) {
            var list = new List<ScatterErrorPoint>();
            foreach (var (x, y) in xy) {
                list.Add(new ScatterErrorPoint(x, y, 0.0, 0.0));
            }
            return list;
        }

        [Test]
        public void Compute_VertexBelowPoints_ExtendsYDownToVertex() {
            // The bug case: a steep curve whose minimum HFR (2.0) sits below every sampled point (>= 5).
            var pts = Pts((100, 10), (300, 6), (500, 5), (800, 7), (1000, 9));
            var bounds = FocusGraphAxisBounds.Compute(pts, new DataPoint(600, 2.0));

            Assert.Multiple(() => {
                Assert.That(bounds.YMin, Is.LessThanOrEqualTo(2.0)); // vertex now visible at the bottom
                Assert.That(bounds.YMax, Is.GreaterThanOrEqualTo(10.0)); // tallest point still inside
            });
        }

        [Test]
        public void Compute_VertexXOutsidePointSpan_ExtendsXToVertex() {
            // All points sampled left of focus; the minimum is to the right of the last sampled point.
            var pts = Pts((100, 10), (200, 8), (300, 7));
            var bounds = FocusGraphAxisBounds.Compute(pts, new DataPoint(600, 4.0));

            Assert.Multiple(() => {
                Assert.That(bounds.XMax, Is.GreaterThanOrEqualTo(600.0)); // vertex X inside the range
                Assert.That(bounds.XMin, Is.LessThanOrEqualTo(100.0));
            });
        }

        [Test]
        public void Compute_VertexWithinPoints_EqualsPaddedPointExtent() {
            var pts = Pts((100, 10), (1000, 9));
            // Vertex inside the point span on both axes -> the points alone determine the (padded) extent.
            var bounds = FocusGraphAxisBounds.Compute(pts, new DataPoint(550, 9.5), pad: 0.1);

            Assert.Multiple(() => {
                Assert.That(bounds.XMin, Is.EqualTo(100 - 0.1 * 900).Within(1e-9)); // 10
                Assert.That(bounds.XMax, Is.EqualTo(1000 + 0.1 * 900).Within(1e-9)); // 1090
                Assert.That(bounds.YMin, Is.EqualTo(9 - 0.1 * 1).Within(1e-9));      // 8.9
                Assert.That(bounds.YMax, Is.EqualTo(10 + 0.1 * 1).Within(1e-9));     // 10.1
            });
        }

        [Test]
        public void Compute_NullVertex_UsesPointsOnly() {
            var pts = Pts((100, 5), (200, 7));
            var bounds = FocusGraphAxisBounds.Compute(pts, null, pad: 0.1);

            Assert.Multiple(() => {
                Assert.That(bounds.XMin, Is.EqualTo(100 - 0.1 * 100).Within(1e-9)); // 90
                Assert.That(bounds.XMax, Is.EqualTo(200 + 0.1 * 100).Within(1e-9)); // 210
                Assert.That(bounds.YMin, Is.EqualTo(5 - 0.1 * 2).Within(1e-9));      // 4.8
                Assert.That(bounds.YMax, Is.EqualTo(7 + 0.1 * 2).Within(1e-9));      // 7.2
            });
        }

        [Test]
        public void Compute_NonFiniteVertex_IsIgnored() {
            var pts = Pts((100, 5), (200, 7));
            var bounds = FocusGraphAxisBounds.Compute(pts, new DataPoint(double.NaN, double.NaN));

            Assert.Multiple(() => {
                Assert.That(bounds.XMin, Is.EqualTo(90).Within(1e-9)); // same as points-only
                Assert.That(bounds.XMax, Is.EqualTo(210).Within(1e-9));
            });
        }

        [Test]
        public void Compute_SinglePointZeroWidth_ExpandsAroundValue() {
            var pts = Pts((500, 5));
            var bounds = FocusGraphAxisBounds.Compute(pts, null);

            Assert.Multiple(() => {
                Assert.That(bounds.XMin, Is.LessThan(500));
                Assert.That(bounds.XMax, Is.GreaterThan(500));
                Assert.That(bounds.YMin, Is.LessThan(5));
                Assert.That(bounds.YMax, Is.GreaterThan(5));
            });
        }

        [Test]
        public void Compute_NoFiniteData_ReturnsBenignNonDegenerateRange() {
            var bounds = FocusGraphAxisBounds.Compute(new List<ScatterErrorPoint>(), null);

            Assert.Multiple(() => {
                Assert.That(bounds.XMax, Is.GreaterThan(bounds.XMin));
                Assert.That(bounds.YMax, Is.GreaterThan(bounds.YMin));
            });
        }
    }
}
