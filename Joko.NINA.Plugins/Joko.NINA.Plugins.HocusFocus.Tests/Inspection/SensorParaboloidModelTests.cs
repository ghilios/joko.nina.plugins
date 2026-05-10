using NINA.Joko.Plugins.HocusFocus.Inspection;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Inspection {

    [TestFixture]
    public class SensorParaboloidModelTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        // SensorParaboloidModel z(x,y) = (x' cosφ + y' sinφ) tanθ + sign(c)c²·(x'² + y'²) + z0
        // where x' = x - x0, y' = y - y0
        [Test]
        public void ValueAt_NoTiltNoCurvature_ReturnsZ0() {
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: 0);
            Assert.That(m.ValueAt(50, 50), Is.EqualTo(100).Within(1e-9));
        }

        [Test]
        public void ValueAt_PureCurvaturePositive_ReturnsZ0PlusCurvature() {
            // c = 0.1 → C2 = 0.01; r² = 100 → ZPrime = 1
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: 0.1);
            Assert.That(m.ValueAt(10, 0), Is.EqualTo(101.0).Within(1e-9));
            Assert.That(m.ValueAt(0, 10), Is.EqualTo(101.0).Within(1e-9));
            Assert.That(m.ValueAt(0, 0), Is.EqualTo(100.0).Within(1e-9));
        }

        [Test]
        public void ValueAt_NegativeCurvature_FlipsSign() {
            var positive = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: 0.1);
            var negative = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: -0.1);

            // r² = 100, so positive curvature contributes +1, negative contributes -1
            Assert.Multiple(() => {
                Assert.That(positive.ValueAt(10, 0), Is.EqualTo(101.0).Within(1e-9));
                Assert.That(negative.ValueAt(10, 0), Is.EqualTo(99.0).Within(1e-9));
            });
        }

        [Test]
        public void ValueAt_TiltAlongXOnly_AppliesLinearSlope() {
            // φ = 0 → tilt direction along x. theta = atan(0.1) → tanθ = 0.1
            var theta = Math.Atan(0.1);
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, theta: theta, phi: 0, c: 0);
            Assert.Multiple(() => {
                Assert.That(m.ValueAt(0, 0), Is.EqualTo(100.0).Within(1e-9));
                Assert.That(m.ValueAt(10, 0), Is.EqualTo(101.0).Within(1e-9));
                Assert.That(m.ValueAt(0, 10), Is.EqualTo(100.0).Within(1e-9)); // sin(0) = 0
            });
        }

        [Test]
        public void TiltAt_IsValueAtMinusZ0AndCurvature() {
            var theta = Math.Atan(0.1);
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, theta: theta, phi: 0, c: 0);
            Assert.That(m.TiltAt(15, 7), Is.EqualTo(1.5).Within(1e-9));
        }

        [Test]
        public void CurvatureAt_PureCurvature_ReturnsCSquaredTimesRSquared() {
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, theta: 0, phi: 0, c: 0.2);
            // r² = 4² + 3² = 25, c² = 0.04 → 1.0
            Assert.That(m.CurvatureAt(4, 3), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void CurvatureAt_NegativeC_ReturnsNegativeSquaredCurvature() {
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, theta: 0, phi: 0, c: -0.2);
            Assert.That(m.CurvatureAt(4, 3), Is.EqualTo(-1.0).Within(1e-9));
        }

        [Test]
        public void ToArray_FromArray_RoundTrip() {
            var original = new SensorParaboloidModel(x0: 1.1, y0: 2.2, z0: 3.3, theta: 0.4, phi: 0.5, c: 0.6);
            var clone = new SensorParaboloidModel();
            clone.FromArray(original.ToArray());

            Assert.Multiple(() => {
                Assert.That(clone.X0, Is.EqualTo(1.1));
                Assert.That(clone.Y0, Is.EqualTo(2.2));
                Assert.That(clone.Z0, Is.EqualTo(3.3));
                Assert.That(clone.Theta, Is.EqualTo(0.4));
                Assert.That(clone.Phi, Is.EqualTo(0.5));
                Assert.That(clone.C, Is.EqualTo(0.6));
            });
        }

        [Test]
        public void FromArray_WrongLength_Throws() {
            var m = new SensorParaboloidModel();
            Assert.Throws<ArgumentException>(() => m.FromArray(new double[] { 1, 2, 3 }));
        }

        [Test]
        public void Solver_OnSyntheticParaboloid_RecoversCurvatureAndTilt() {
            const double trueX0 = 0;
            const double trueY0 = 0;
            const double trueZ0 = 1000;
            const double truePhi = 0;
            var trueTheta = Math.Atan(0.001); // gentle tilt
            const double trueC = 5e-4;

            var truth = new SensorParaboloidModel(
                x0: trueX0, y0: trueY0, z0: trueZ0,
                theta: trueTheta, phi: truePhi, c: trueC);

            // Sample on a 5x5 grid spanning ±1000 microns
            var pts = new List<SensorParaboloidDataPoint>();
            for (var iy = -2; iy <= 2; ++iy) {
                for (var ix = -2; ix <= 2; ++ix) {
                    double x = ix * 500;
                    double y = iy * 500;
                    var z = truth.ValueAt(x, y);
                    pts.Add(new SensorParaboloidDataPoint(x: x, y: y, focuserPosition: z, rSquared: 0.99));
                }
            }

            var solver = new SensorParaboloidSolver(
                dataPoints: pts,
                sensorSizeMicronsX: 4000,
                sensorSizeMicronsY: 4000,
                inFocusMicrons: trueZ0,
                fixedSensorCenter: true) {
                PositiveCurvature = true
            };
            var nlls = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(alglibAPI);
            var result = nlls.Solve(solver, tolerance: 1e-10);

            Assert.Multiple(() => {
                Assert.That(result.Z0, Is.EqualTo(trueZ0).Within(1.0));
                // Curvature recovery — sign-and-magnitude
                Assert.That(Math.Abs(result.C), Is.EqualTo(Math.Abs(trueC)).Within(1e-4));
                Assert.That(Math.Sign(result.C), Is.EqualTo(Math.Sign(trueC)));
            });
        }
    }
}
