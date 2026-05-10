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
    }
}
