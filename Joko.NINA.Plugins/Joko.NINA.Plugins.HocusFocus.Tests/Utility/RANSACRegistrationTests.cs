using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class RANSACRegistrationTests {

        [Test]
        public void SimilarityTransform_IdentityTransform_ReturnsSamePoint() {
            var t = new SimilarityTransform(scale: 1.0, rotation: 0.0, tx: 0.0, ty: 0.0);
            var p = new Point2D(3.0, 4.0);
            var transformed = t.Transform(p);
            Assert.Multiple(() => {
                Assert.That(transformed.X, Is.EqualTo(3.0).Within(1e-9));
                Assert.That(transformed.Y, Is.EqualTo(4.0).Within(1e-9));
            });
        }

        [Test]
        public void SimilarityTransform_TranslationOnly_AppliesOffset() {
            var t = new SimilarityTransform(scale: 1.0, rotation: 0.0, tx: 5.0, ty: -3.0);
            var transformed = t.Transform(new Point2D(1.0, 2.0));
            Assert.Multiple(() => {
                Assert.That(transformed.X, Is.EqualTo(6.0).Within(1e-9));
                Assert.That(transformed.Y, Is.EqualTo(-1.0).Within(1e-9));
            });
        }

        [Test]
        public void SimilarityTransform_NinetyDegRotation_RotatesPoint() {
            var t = new SimilarityTransform(scale: 1.0, rotation: Math.PI / 2.0, tx: 0.0, ty: 0.0);
            var transformed = t.Transform(new Point2D(1.0, 0.0));
            Assert.Multiple(() => {
                Assert.That(transformed.X, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(transformed.Y, Is.EqualTo(1.0).Within(1e-9));
            });
        }

        [Test]
        public void SimilarityTransform_Scaling_AppliesScale() {
            var t = new SimilarityTransform(scale: 2.5, rotation: 0.0, tx: 0.0, ty: 0.0);
            var transformed = t.Transform(new Point2D(2.0, 3.0));
            Assert.Multiple(() => {
                Assert.That(transformed.X, Is.EqualTo(5.0).Within(1e-9));
                Assert.That(transformed.Y, Is.EqualTo(7.5).Within(1e-9));
            });
        }

        [Test]
        public void SimilarityTransform_ToMatrix3x2_ProducesIdenticalPointMapping() {
            var t = new SimilarityTransform(scale: 1.5, rotation: 0.3, tx: 10.0, ty: -4.0);
            var m = t.ToMatrix3x2();
            foreach (var p in new[] { new Point2D(7.0, 11.0), new Point2D(-3.0, 2.0), new Point2D(0.0, 0.0) }) {
                var viaT = t.Transform(p);
                var viaM = m.Transform(p);
                Assert.Multiple(() => {
                    Assert.That(viaM.X, Is.EqualTo(viaT.X).Within(1e-9));
                    Assert.That(viaM.Y, Is.EqualTo(viaT.Y).Within(1e-9));
                });
            }
        }

        [Test]
        public void Matrix3x2_Identity_LeavesPointsUnchanged() {
            var m = Matrix3x2.Identity;
            var p = new Point2D(2.5, -3.5);
            var t = m.Transform(p);
            Assert.Multiple(() => {
                Assert.That(t.X, Is.EqualTo(2.5));
                Assert.That(t.Y, Is.EqualTo(-3.5));
            });
        }

        [Test]
        public void Matrix3x2_Determinant_KnownValue() {
            var m = new Matrix3x2(2, 0, 0, 3, 100, 200);
            Assert.That(m.GetDeterminant(), Is.EqualTo(6.0).Within(1e-9));
        }

        [Test]
        public void Matrix3x2_Invert_RoundTripIsIdentity() {
            var m = new Matrix3x2(2, 0, 0, 3, 100, 200);
            Assert.That(m.Invert(out var inv), Is.True);

            var p = new Point2D(7.0, 11.0);
            var roundTrip = inv.Transform(m.Transform(p));
            Assert.Multiple(() => {
                Assert.That(roundTrip.X, Is.EqualTo(7.0).Within(1e-9));
                Assert.That(roundTrip.Y, Is.EqualTo(11.0).Within(1e-9));
            });
        }

        [Test]
        public void Matrix3x2_InvertSingular_ReturnsFalse() {
            var m = new Matrix3x2(0, 0, 0, 0, 5, 5);
            Assert.That(m.Invert(out _), Is.False);
        }

        [Test]
        public void FitAffineTransform_RecoversTranslation() {
            var src = new List<Point2D> {
                new(0, 0), new(1, 0), new(0, 1), new(1, 1),
            };
            var dst = src.Select(p => new Point2D(p.X + 10, p.Y - 5)).ToList();

            var m = RANSACRegistration.FitAffineTransform(src, dst);
            var t = m.Transform(new Point2D(2, 3));
            Assert.Multiple(() => {
                Assert.That(t.X, Is.EqualTo(12.0).Within(1e-6));
                Assert.That(t.Y, Is.EqualTo(-2.0).Within(1e-6));
            });
        }

        [Test]
        public void FitAffineTransform_RecoversScaling() {
            var src = new List<Point2D> { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };
            var dst = src.Select(p => new Point2D(p.X * 2.0, p.Y * 3.0)).ToList();

            var m = RANSACRegistration.FitAffineTransform(src, dst);
            var t = m.Transform(new Point2D(4, 5));
            Assert.Multiple(() => {
                Assert.That(t.X, Is.EqualTo(8.0).Within(1e-6));
                Assert.That(t.Y, Is.EqualTo(15.0).Within(1e-6));
            });
        }

        [Test]
        public void FitAffineTransform_TooFewPoints_Throws() {
            var src = new List<Point2D> { new(0, 0), new(1, 1) };
            var dst = new List<Point2D> { new(0, 0), new(1, 1) };
            Assert.Throws<ArgumentException>(() => RANSACRegistration.FitAffineTransform(src, dst));
        }

        [Test]
        public void FitAffineTransform_MismatchedCount_Throws() {
            var src = new List<Point2D> { new(0, 0), new(1, 0), new(0, 1) };
            var dst = new List<Point2D> { new(0, 0), new(1, 0) };
            Assert.Throws<ArgumentException>(() => RANSACRegistration.FitAffineTransform(src, dst));
        }

        [Test]
        public void GeneratePutativeMatchesUsingNN_FindsExactMatches() {
            var img = new List<Point2D> { new(10, 10, 0.5), new(20, 20, 0.5), new(30, 30, 0.5) };
            var refStars = new List<Point2D> { new(10, 10, 0.5), new(21, 21, 0.5), new(50, 50, 0.5) };

            var (src, dst) = RANSACRegistration.GeneratePutativeMatchesUsingNN(
                img, refStars, maxDistance: 5.0, relativeBrightnessDiff: 0.1, status: null);

            Assert.Multiple(() => {
                Assert.That(src.Count, Is.EqualTo(2));
                Assert.That(src[0].X, Is.EqualTo(10));
                Assert.That(dst[0].X, Is.EqualTo(10));
                Assert.That(src[1].X, Is.EqualTo(20));
                Assert.That(dst[1].X, Is.EqualTo(21));
            });
        }

        [Test]
        public void GeneratePutativeMatchesUsingNN_BrightnessDifferenceFiltersOut() {
            var img = new List<Point2D> { new(10, 10, 0.5) };
            var refStars = new List<Point2D> { new(10, 10, 1.0) }; // brightness difference too large

            var (src, dst) = RANSACRegistration.GeneratePutativeMatchesUsingNN(
                img, refStars, maxDistance: 5.0, relativeBrightnessDiff: 0.1, status: null);

            Assert.Multiple(() => {
                Assert.That(src.Count, Is.EqualTo(0));
                Assert.That(dst.Count, Is.EqualTo(0));
            });
        }

        [Test]
        public void AngleBetweenPoints_RightAngle_ReturnsPiOver2() {
            var a = new Point2D(1.0, 0.0);
            var b = new Point2D(0.0, 0.0);
            var c = new Point2D(0.0, 1.0);
            var theta = RANSACRegistration.AngleBetweenPoints(a, b, c);
            Assert.That(theta, Is.EqualTo(Math.PI / 2.0).Within(1e-9));
        }

        [Test]
        public void AngleFromHorizontal_AlongPositiveX_IsZero() {
            var theta = RANSACRegistration.AngleFromHorizontal(new Point2D(0, 0), new Point2D(1, 0));
            Assert.That(theta, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void AngleFromHorizontal_AlongPositiveY_IsPiOver2() {
            var theta = RANSACRegistration.AngleFromHorizontal(new Point2D(0, 0), new Point2D(0, 1));
            Assert.That(theta, Is.EqualTo(Math.PI / 2.0).Within(1e-9));
        }

        [Test]
        public void Point2D_AsPointF_RoundsCoordinates() {
            var p = new Point2D(3.7, 4.2, 0.5);
            var pf = p.AsPointF();
            Assert.Multiple(() => {
                Assert.That(pf.X, Is.EqualTo(3.7f).Within(1e-3f));
                Assert.That(pf.Y, Is.EqualTo(4.2f).Within(1e-3f));
            });
        }

        [Test]
        public void Point2D_AsPoint_TruncatesToInt() {
            var p = new Point2D(3.7, 4.2);
            var sysPoint = p.AsPoint();
            Assert.That(sysPoint.X, Is.EqualTo(3).Or.EqualTo(4));
            Assert.That(sysPoint.Y, Is.EqualTo(4).Or.EqualTo(5));
        }

        [Test]
        public void Point2D_ConstructorFromAccordPoint_CopiesXY() {
            var ap = new Accord.Point(5.5f, 6.5f);
            var p = new Point2D(ap);
            Assert.Multiple(() => {
                Assert.That(p.X, Is.EqualTo(5.5).Within(1e-6));
                Assert.That(p.Y, Is.EqualTo(6.5).Within(1e-6));
            });
        }

        [Test]
        public void SimilarityTransform_RotationPlusScalePlusTranslation_Composes() {
            // 90-degree rotation, scale 2, then translate (10, 20)
            var t = new SimilarityTransform(scale: 2.0, rotation: Math.PI / 2.0, tx: 10.0, ty: 20.0);
            var transformed = t.Transform(new Point2D(1.0, 0.0));
            // Rotated then scaled: (0, 1) * 2 = (0, 2). Then translated: (10, 22).
            Assert.Multiple(() => {
                Assert.That(transformed.X, Is.EqualTo(10.0).Within(1e-9));
                Assert.That(transformed.Y, Is.EqualTo(22.0).Within(1e-9));
            });
        }

        [Test]
        public void SimilarityTransform_ToString_ContainsKeyFields() {
            var t = new SimilarityTransform(scale: 1.5, rotation: 0.25, tx: 1, ty: 2);
            var s = t.ToString();
            // Format: "S:1.5, R:0.25, Tx:1, Ty:2"
            Assert.Multiple(() => {
                Assert.That(s, Does.Contain("S:"));
                Assert.That(s, Does.Contain("R:"));
                Assert.That(s, Does.Contain("Tx:"));
                Assert.That(s, Does.Contain("Ty:"));
            });
        }

        [Test]
        public void StarTriangle_Construction_OrdersByAngle() {
            var imageSize = new System.Drawing.Size(1000, 1000);
            var p1 = new Point2D(100, 100, 0.5);
            var p2 = new Point2D(200, 100, 0.5);
            var p3 = new Point2D(150, 200, 0.5);
            var tri = new RANSACRegistration.StarTriangle(
                imageSize, minBrightness: 0.5, maxBrightness: 0.5,
                p1: p1, p2: p2, p3: p3, isReference: true, referenceID: 42);

            Assert.Multiple(() => {
                Assert.That(tri.P1, Is.EqualTo(p1));
                Assert.That(tri.NormalizedLengths.Count, Is.EqualTo(3));
                Assert.That(tri.NormalizedBrightnesses.Count, Is.EqualTo(3));
                Assert.That(tri.AsPositionMatrix().Length, Is.EqualTo(6));
                Assert.That(tri.AsShapeMatrix().Length, Is.EqualTo(3));
                Assert.That(tri.AsBrightnessMatrix().Length, Is.EqualTo(3));
                Assert.That(tri.AsShapeAndBrightnessMatrix().Length, Is.EqualTo(6));
                Assert.That(tri.IsReference, Is.True);
                Assert.That(tri.Matched, Is.False);
                Assert.That(tri.ReferenceID, Is.EqualTo(42));
            });
        }

        [Test]
        public void StarTriangle_NormalizedLengths_StartAtOne() {
            var p1 = new Point2D(100, 100, 0.5);
            var p2 = new Point2D(200, 100, 0.5);
            var p3 = new Point2D(150, 200, 0.5);
            var tri = new RANSACRegistration.StarTriangle(
                new System.Drawing.Size(1000, 1000), 0.5, 0.5, p1, p2, p3, false, 0);
            // Shortest side becomes 1.0 after normalization
            Assert.That(tri.NormalizedLengths.Min(), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void StarTriangle_SameTriangle_ReturnsTrueForIdenticalPoints() {
            var p1 = new Point2D(100, 100, 0.5);
            var p2 = new Point2D(200, 100, 0.5);
            var p3 = new Point2D(150, 200, 0.5);
            var size = new System.Drawing.Size(1000, 1000);
            var t1 = new RANSACRegistration.StarTriangle(size, 0.5, 0.5, p1, p2, p3, false, 0);
            var t2 = new RANSACRegistration.StarTriangle(size, 0.5, 0.5, p1, p2, p3, false, 1);
            Assert.That(t1.SameTriangle(t2), Is.True);
        }

        [Test]
        public void StarTriangle_MarkAsMatched_FlipsMatchedFlag() {
            var p1 = new Point2D(100, 100, 0.5);
            var p2 = new Point2D(200, 100, 0.5);
            var p3 = new Point2D(150, 200, 0.5);
            var tri = new RANSACRegistration.StarTriangle(
                new System.Drawing.Size(1000, 1000), 0.5, 0.5, p1, p2, p3, false, 0);
            tri.MarkAsMatched(referenceID: 99, matchScore: 0.5);
            Assert.Multiple(() => {
                Assert.That(tri.Matched, Is.True);
                Assert.That(tri.ReferenceID, Is.EqualTo(99));
                Assert.That(tri.MatchString, Does.Contain("99"));
            });
        }

        [Test]
        public void AngleBetweenPoints_RejectsCoincidentPoints() {
            var p = new Point2D(1, 1);
            Assert.Throws<ArgumentException>(() =>
                RANSACRegistration.AngleBetweenPoints(p, p, new Point2D(2, 2)));
        }

        [Test]
        public void GeneratePutativeMatchesUsingNN_BeyondMaxDistance_FiltersOut() {
            var img = new List<Point2D> { new(10, 10, 0.5) };
            var refStars = new List<Point2D> { new(50, 50, 0.5) }; // far away
            var (src, dst) = RANSACRegistration.GeneratePutativeMatchesUsingNN(
                img, refStars, maxDistance: 5.0, relativeBrightnessDiff: 0.1, status: null);
            Assert.That(src.Count, Is.EqualTo(0));
            Assert.That(dst.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Builds a deterministic set of correspondences: <paramref name="inlierCount"/> points mapped by a
        /// known similarity transform with sub-pixel noise (well within the inlier threshold), plus a few
        /// gross outliers. The test RNG is fixed-seed so the data itself is identical every run.
        /// </summary>
        private static (List<Point2D> src, List<Point2D> dst) BuildNoisyCorrespondences(
            double scale, double rotation, double tx, double ty, int inlierCount = 30) {
            var rng = new Random(20240526);
            var truth = new SimilarityTransform(scale, rotation, tx, ty);
            var src = new List<Point2D>();
            var dst = new List<Point2D>();
            for (int i = 0; i < inlierCount; i++) {
                var p = new Point2D(rng.NextDouble() * 1000.0, rng.NextDouble() * 1000.0);
                var t = truth.Transform(p);
                // Sub-pixel noise (|d| < 0.25px) keeps every point inside the 3px inlier threshold.
                src.Add(p);
                dst.Add(new Point2D(t.X + (rng.NextDouble() - 0.5) * 0.5, t.Y + (rng.NextDouble() - 0.5) * 0.5));
            }
            // Gross outliers RANSAC must reject.
            src.Add(new Point2D(500, 500)); dst.Add(new Point2D(5, 990));
            src.Add(new Point2D(100, 900)); dst.Add(new Point2D(950, 30));
            return (src, dst);
        }

        [Test]
        public void EstimateSimilarityTransform_RecoversKnownTransform() {
            double scale = 1.0, rotation = 0.05, tx = 12.0, ty = -7.0;
            var (src, dst) = BuildNoisyCorrespondences(scale, rotation, tx, ty);

            var result = RANSACRegistration.EstimateSimilarityTransform(src, dst, status: null, progress: null);

            Assert.Multiple(() => {
                Assert.That(result.Scale, Is.EqualTo(scale).Within(0.01));
                Assert.That(result.Rotation, Is.EqualTo(rotation).Within(0.01));
                Assert.That(result.Tx, Is.EqualTo(tx).Within(1.0));
                Assert.That(result.Ty, Is.EqualTo(ty).Within(1.0));
            });
        }

        [Test]
        public void EstimateSimilarityTransform_FixedSeed_IsBitForBitDeterministic() {
            var (src, dst) = BuildNoisyCorrespondences(1.0, 0.05, 12.0, -7.0);

            // Small maxIterations forces the seeded subsampling path (fewer samples than candidate pairs),
            // which is exactly where the old shared unseeded RNG produced run-to-run variation.
            var a = RANSACRegistration.EstimateSimilarityTransform(src, dst, null, null, maxIterations: 8);
            var b = RANSACRegistration.EstimateSimilarityTransform(src, dst, null, null, maxIterations: 8);

            Assert.Multiple(() => {
                Assert.That(a.Scale, Is.EqualTo(b.Scale));
                Assert.That(a.Rotation, Is.EqualTo(b.Rotation));
                Assert.That(a.Tx, Is.EqualTo(b.Tx));
                Assert.That(a.Ty, Is.EqualTo(b.Ty));
            });
        }

        [Test]
        public void EstimateAffineTransform_FixedSeed_IsBitForBitDeterministic() {
            var (src, dst) = BuildNoisyCorrespondences(1.0, 0.05, 12.0, -7.0);

            var a = RANSACRegistration.EstimateAffineTransform(src, dst, null, null, maxIterations: 8);
            var b = RANSACRegistration.EstimateAffineTransform(src, dst, null, null, maxIterations: 8);

            Assert.Multiple(() => {
                Assert.That(a.M11, Is.EqualTo(b.M11));
                Assert.That(a.M12, Is.EqualTo(b.M12));
                Assert.That(a.M21, Is.EqualTo(b.M21));
                Assert.That(a.M22, Is.EqualTo(b.M22));
                Assert.That(a.M31, Is.EqualTo(b.M31));
                Assert.That(a.M32, Is.EqualTo(b.M32));
            });
        }

        [Test]
        public void BuildStarTriangles_OnePerPoint_BuildsAtMostOneTrianglePerPoint() {
            var imageSize = new System.Drawing.Size(1000, 1000);
            var pts = new List<Point2D> {
                new(100, 100, 0.5),
                new(200, 100, 0.6),
                new(150, 200, 0.7),
                new(800, 800, 0.5),
                new(700, 800, 0.6),
                new(750, 900, 0.7),
            };
            var triangles = RANSACRegistration.BuildStarTriangles(
                imageSize, pts,
                searchSquareSide: 250, onePerPoint: true, isReference: true);
            // We have two well-separated clusters of three; we expect at most 2 triangles.
            Assert.That(triangles.Count, Is.LessThanOrEqualTo(2));
        }
    }
}
