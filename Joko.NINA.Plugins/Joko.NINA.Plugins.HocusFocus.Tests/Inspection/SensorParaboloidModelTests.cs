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

        // SensorParaboloidModel z(x,y) = Gx·x' + Gy·y' + K·(x'² + y'²) + z0, where x' = x - x0, y' = y - y0.
        // Tilt/curvature display parameters: tanθ = hypot(Gx,Gy), φ = atan2(Gy,Gx), K = sign(c)·c².
        [Test]
        public void ValueAt_NoTiltNoCurvature_ReturnsZ0() {
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, gx: 0, gy: 0, k: 0);
            Assert.That(m.ValueAt(50, 50), Is.EqualTo(100).Within(1e-9));
        }

        [Test]
        public void ValueAt_PureCurvaturePositive_ReturnsZ0PlusCurvature() {
            // K = 0.01; r² = 100 → curvature = 1
            var m = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: 0.1);
            Assert.That(m.ValueAt(10, 0), Is.EqualTo(101.0).Within(1e-9));
            Assert.That(m.ValueAt(0, 10), Is.EqualTo(101.0).Within(1e-9));
            Assert.That(m.ValueAt(0, 0), Is.EqualTo(100.0).Within(1e-9));
        }

        [Test]
        public void ValueAt_NegativeCurvature_FlipsSign() {
            var positive = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: 0.1);
            var negative = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 100, theta: 0, phi: 0, c: -0.1);

            Assert.Multiple(() => {
                Assert.That(positive.ValueAt(10, 0), Is.EqualTo(101.0).Within(1e-9));
                Assert.That(negative.ValueAt(10, 0), Is.EqualTo(99.0).Within(1e-9));
            });
        }

        [Test]
        public void ValueAt_TiltAlongXOnly_AppliesLinearSlope() {
            // Gx = 0.1, Gy = 0
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, gx: 0.1, gy: 0, k: 0);
            Assert.Multiple(() => {
                Assert.That(m.ValueAt(0, 0), Is.EqualTo(100.0).Within(1e-9));
                Assert.That(m.ValueAt(10, 0), Is.EqualTo(101.0).Within(1e-9));
                Assert.That(m.ValueAt(0, 10), Is.EqualTo(100.0).Within(1e-9));
            });
        }

        [Test]
        public void TiltAt_LinearInGradients() {
            var m = new SensorParaboloidModel(x0: 0, y0: 0, z0: 100, gx: 0.1, gy: 0, k: 0);
            Assert.That(m.TiltAt(15, 7), Is.EqualTo(1.5).Within(1e-9));
        }

        [Test]
        public void DerivedThetaPhi_RecoverTiltAngleAndAzimuth() {
            var m = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 0.2, phi: 0.6, c: 0.0);
            Assert.Multiple(() => {
                Assert.That(m.Theta, Is.EqualTo(0.2).Within(1e-9));
                Assert.That(m.Phi, Is.EqualTo(0.6).Within(1e-9));
            });
        }

        [Test]
        public void DerivedC_RecoversSignedCurvature() {
            var pos = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 1e-6, phi: 0, c: 0.05);
            var neg = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 1e-6, phi: 0, c: -0.05);
            Assert.Multiple(() => {
                Assert.That(pos.C, Is.EqualTo(0.05).Within(1e-9));
                Assert.That(neg.C, Is.EqualTo(-0.05).Within(1e-9));
            });
        }

        [Test]
        public void CurvatureAt_PureCurvature_ReturnsKTimesRSquared() {
            var m = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 0, phi: 0, c: 0.2);
            // K = 0.04, r² = 25 → 1.0
            Assert.That(m.CurvatureAt(4, 3), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void CurvatureAt_NegativeC_ReturnsNegativeSquaredCurvature() {
            var m = SensorParaboloidModel.FromTiltCurvature(x0: 0, y0: 0, z0: 0, theta: 0, phi: 0, c: -0.2);
            Assert.That(m.CurvatureAt(4, 3), Is.EqualTo(-1.0).Within(1e-9));
        }

        [Test]
        public void ToArray_FromArray_RoundTripsCanonicalParameters() {
            var original = new SensorParaboloidModel(x0: 1.1, y0: 2.2, z0: 3.3, gx: 0.01, gy: -0.02, k: 5e-4);
            var clone = new SensorParaboloidModel();
            clone.FromArray(original.ToArray());

            Assert.Multiple(() => {
                Assert.That(clone.X0, Is.EqualTo(1.1));
                Assert.That(clone.Y0, Is.EqualTo(2.2));
                Assert.That(clone.Z0, Is.EqualTo(3.3));
                Assert.That(clone.Gx, Is.EqualTo(0.01));
                Assert.That(clone.Gy, Is.EqualTo(-0.02));
                Assert.That(clone.K, Is.EqualTo(5e-4));
            });
        }

        [Test]
        public void FromArray_WrongLength_Throws() {
            var m = new SensorParaboloidModel();
            Assert.Throws<ArgumentException>(() => m.FromArray(new double[] { 1, 2, 3 }));
        }

        private static List<SensorParaboloidDataPoint> SampleGrid(SensorParaboloidModel truth, int half = 2, double step = 500, double span = 1000, Func<double, double> perturb = null) {
            var pts = new List<SensorParaboloidDataPoint>();
            for (var iy = -half; iy <= half; ++iy) {
                for (var ix = -half; ix <= half; ++ix) {
                    double x = ix * step;
                    double y = iy * step;
                    var z = truth.ValueAt(x, y);
                    if (perturb != null) {
                        z += perturb(x + y);
                    }
                    pts.Add(new SensorParaboloidDataPoint(x: x, y: y, focuserPosition: z, rSquared: 0.99));
                }
            }
            return pts;
        }

        private NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel> SolveModel(
            List<SensorParaboloidDataPoint> pts, out SensorParaboloidModel result, double sensorHalf = 4000) {
            var solver = new SensorParaboloidSolver(
                dataPoints: pts,
                sensorSizeMicronsX: sensorHalf * 2,
                sensorSizeMicronsY: sensorHalf * 2,
                inFocusMicrons: 1000,
                fixedSensorCenter: true);
            var nlls = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(alglibAPI);
            result = nlls.Solve(solver, tolerance: 1e-12);
            return nlls;
        }

        private NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel> SolveModelAstigmatic(
            List<SensorParaboloidDataPoint> pts, out SensorParaboloidModel result, double sensorHalf = 4000) {
            var solver = new SensorParaboloidSolver(
                dataPoints: pts,
                sensorSizeMicronsX: sensorHalf * 2,
                sensorSizeMicronsY: sensorHalf * 2,
                inFocusMicrons: 1000,
                fixedSensorCenter: true,
                astigmatic: true);
            var nlls = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(alglibAPI);
            result = nlls.Solve(solver, tolerance: 1e-12);
            return nlls;
        }

        [Test]
        public void Solver_PositiveCurvature_RecoversCurvatureAndTilt() {
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.001, gy: 0.0, k: 5e-7);
            var pts = SampleGrid(truth);

            SolveModel(pts, out var result);

            Assert.Multiple(() => {
                Assert.That(result.Z0, Is.EqualTo(1000).Within(1.0));
                Assert.That(result.Gx, Is.EqualTo(0.001).Within(1e-5));
                Assert.That(result.K, Is.EqualTo(5e-7).Within(1e-8));
            });
        }

        [Test]
        public void Solver_NegativeCurvature_RecoversSignWithoutDoubleSolve() {
            // The single signed-K solve must recover negative curvature directly.
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.0, gy: 0.002, k: -8e-7);
            var pts = SampleGrid(truth);

            SolveModel(pts, out var result);

            Assert.Multiple(() => {
                Assert.That(result.Gy, Is.EqualTo(0.002).Within(1e-5));
                Assert.That(result.K, Is.EqualTo(-8e-7).Within(1e-8));
                Assert.That(Math.Sign(result.K), Is.EqualTo(-1));
            });
        }

        [Test]
        public void Solver_FlatField_HandlesZeroCurvatureWithoutSingularity() {
            // k ≈ 0 was the discontinuity case for the old sign(c)·c² parameterization. The signed-K
            // single solve must handle a flat field cleanly and return near-zero curvature/tilt.
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.0, gy: 0.0, k: 0.0);
            var pts = SampleGrid(truth);

            SolveModel(pts, out var result);

            Assert.Multiple(() => {
                Assert.That(result.Z0, Is.EqualTo(1000).Within(1e-3));
                Assert.That(result.K, Is.EqualTo(0.0).Within(1e-9));
                Assert.That(result.Gx, Is.EqualTo(0.0).Within(1e-6));
                Assert.That(result.Gy, Is.EqualTo(0.0).Within(1e-6));
            });
        }

        [Test]
        public void Solver_WeightedFit_AnalyticJacobianMatchesWeightedResiduals() {
            // Regression: the analytic Jacobian must carry the same per-point 1/σ weight as the residuals.
            // With non-uniform σ, an unweighted Jacobian is inconsistent with the weighted residual function;
            // OptGuard (analytic-vs-numerical gradient check) throws OptGuardBadGradientException when that
            // happens. Uniform-σ fits would not expose the bug, so the σ here are deliberately varied.
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.001, gy: -0.0005, k: 3e-7);
            var pts = new List<SensorParaboloidDataPoint>();
            int idx = 0;
            for (var iy = -2; iy <= 2; ++iy) {
                for (var ix = -2; ix <= 2; ++ix) {
                    double x = ix * 500, y = iy * 500;
                    double sigma = 1.0 + (idx % 5) * 10.0;
                    pts.Add(new SensorParaboloidDataPoint(x: x, y: y, focuserPosition: truth.ValueAt(x, y), rSquared: 0.99, focuserPositionStdDev: sigma));
                    ++idx;
                }
            }

            var solver = new SensorParaboloidSolver(
                dataPoints: pts, sensorSizeMicronsX: 8000, sensorSizeMicronsY: 8000,
                inFocusMicrons: 1000, fixedSensorCenter: true);
            var nlls = new NonLinearLeastSquaresSolver<SensorParaboloidSolver, SensorParaboloidDataPoint, SensorParaboloidModel>(alglibAPI) {
                OptGuardEnabled = true
            };

            SensorParaboloidModel result = null;
            Assert.DoesNotThrow(() => result = nlls.Solve(solver, tolerance: 1e-12));
            Assert.Multiple(() => {
                Assert.That(result.Gx, Is.EqualTo(0.001).Within(1e-5));
                Assert.That(result.Gy, Is.EqualTo(-0.0005).Within(1e-5));
                Assert.That(result.K, Is.EqualTo(3e-7).Within(1e-8));
            });
        }

        [Test]
        public void Astigmatic_Solver_RecoversIndependentKxKy() {
            // A saddle-shaped field (Kx and Ky of opposite sign) cannot be represented by the isotropic
            // single-K model; the astigmatic 7-parameter solve must recover both coefficients.
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.001, gy: -0.0005, kx: 6e-7, ky: -4e-7);
            var pts = SampleGrid(truth);

            SolveModelAstigmatic(pts, out var result);

            Assert.Multiple(() => {
                Assert.That(result.Astigmatic, Is.True);
                Assert.That(result.Z0, Is.EqualTo(1000).Within(1.0));
                Assert.That(result.Gx, Is.EqualTo(0.001).Within(1e-5));
                Assert.That(result.Gy, Is.EqualTo(-0.0005).Within(1e-5));
                Assert.That(result.Kx, Is.EqualTo(6e-7).Within(1e-8));
                Assert.That(result.Ky, Is.EqualTo(-4e-7).Within(1e-8));
            });
        }

        [Test]
        public void Astigmatic_IsotropicData_RecoversEqualKxKy() {
            // Fed isotropic data, the astigmatic solver must still converge with Kx ≈ Ky (no spurious split).
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.0, gy: 0.0, k: 5e-7);
            var pts = SampleGrid(truth);

            SolveModelAstigmatic(pts, out var result);

            Assert.Multiple(() => {
                Assert.That(result.Kx, Is.EqualTo(5e-7).Within(1e-8));
                Assert.That(result.Ky, Is.EqualTo(5e-7).Within(1e-8));
                Assert.That(result.Kx, Is.EqualTo(result.Ky).Within(1e-8));
            });
        }

        [Test]
        public void Astigmatic_ToArrayFromArray_RoundTripsSevenParameters() {
            var model = new SensorParaboloidModel(x0: 10, y0: -20, z0: 1234, gx: 0.003, gy: -0.002, kx: 7e-7, ky: -3e-7);
            var array = model.ToArray();

            Assert.That(array.Length, Is.EqualTo(7));

            var roundTripped = new SensorParaboloidModel();
            roundTripped.FromArray(array);

            Assert.Multiple(() => {
                Assert.That(roundTripped.Astigmatic, Is.True);
                Assert.That(roundTripped.Kx, Is.EqualTo(7e-7));
                Assert.That(roundTripped.Ky, Is.EqualTo(-3e-7));
                Assert.That(roundTripped.K, Is.EqualTo(0.5 * (7e-7 + -3e-7)));
            });
        }

        [Test]
        public void Isotropic_ToArray_RemainsSixParameters() {
            // Guards the default path: isotropic models must continue to emit a 6-element array so the
            // 6-parameter solve and all existing behaviour are unchanged.
            var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.001, gy: 0.0, k: 5e-7);

            Assert.That(model.ToArray().Length, Is.EqualTo(6));
            Assert.That(model.Astigmatic, Is.False);
            Assert.That(model.Kx, Is.EqualTo(model.Ky));
        }

        [Test]
        public void CurvatureAt_Astigmatic_UsesSeparateKxKy() {
            var model = new SensorParaboloidModel(x0: 0, y0: 0, z0: 0, gx: 0.0, gy: 0.0, kx: 2e-6, ky: 5e-6);

            // Kx·x² + Ky·y²
            Assert.That(model.CurvatureAt(100.0, 200.0), Is.EqualTo(2e-6 * 100.0 * 100.0 + 5e-6 * 200.0 * 200.0).Within(1e-12));
        }

        [Test]
        public void Volume_Astigmatic_MatchesNumericalIntegral() {
            var model = new SensorParaboloidModel(x0: 1500.0, y0: -900.0, z0: 25000.0, gx: 0.004, gy: -0.002, kx: 6e-7, ky: -3e-7);
            double w = 23000.0, h = 16000.0;

            var analytic = model.Volume(w, h);
            var numeric = SimpsonIntegrate2D((x, y) => model.ValueAt(x, y), w, h);

            Assert.That(analytic, Is.EqualTo(numeric).Within(Math.Abs(numeric) * 1e-9 + 1e-3));
        }

        // ---- χ² acceptance statistic ----

        private static Func<double, double> GaussianNoise(int seed, double sigma) {
            var rng = new Random(seed);
            return _ => {
                // Box-Muller
                double u1 = 1.0 - rng.NextDouble();
                double u2 = 1.0 - rng.NextDouble();
                return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            };
        }

        private List<SensorParaboloidDataPoint> NoisyGrid(SensorParaboloidModel truth, double sigma, double declaredSigma, int seed, int half = 7, double step = 700) {
            var noise = GaussianNoise(seed, sigma);
            var pts = new List<SensorParaboloidDataPoint>();
            for (var iy = -half; iy <= half; ++iy) {
                for (var ix = -half; ix <= half; ++ix) {
                    double x = ix * step;
                    double y = iy * step;
                    var z = truth.ValueAt(x, y) + noise(0);
                    pts.Add(new SensorParaboloidDataPoint(x: x, y: y, focuserPosition: z, rSquared: 0.99, focuserPositionStdDev: declaredSigma));
                }
            }
            return pts;
        }

        [Test]
        public void ReducedChiSquared_WithCorrectlyDeclaredSigma_IsApproximatelyOne() {
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.003, gy: -0.001, k: 1e-7);
            const double sigma = 2.0;
            var pts = NoisyGrid(truth, sigma: sigma, declaredSigma: sigma, seed: 4242);

            var nlls = SolveModel(pts, out var result, sensorHalf: 6000);
            var reducedChiSq = nlls.ReducedChiSquared(nlls.Solver, result);

            // dof ≈ 225 - 6, so the standard deviation of reduced χ² ≈ sqrt(2/dof) ≈ 0.10; ±0.4 is generous.
            Assert.That(reducedChiSq, Is.EqualTo(1.0).Within(0.4));
        }

        [Test]
        public void ReducedChiSquared_ScalesQuadraticallyWithMisstatedSigma() {
            // Declaring 2x the true sigma must divide reduced χ² by 4 (uniform sigma ⇒ identical fit/residuals).
            var truth = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 0.003, gy: -0.001, k: 1e-7);
            const double sigma = 2.0;

            var ptsCorrect = NoisyGrid(truth, sigma: sigma, declaredSigma: sigma, seed: 99);
            var ptsDoubled = NoisyGrid(truth, sigma: sigma, declaredSigma: 2.0 * sigma, seed: 99);

            // Fit once (correct σ), then evaluate that SAME model under both weightings so the residuals
            // are identical and only the 1/σ weighting differs — isolating the quadratic σ dependence.
            var nllsCorrect = SolveModel(ptsCorrect, out var model, sensorHalf: 6000);
            var nllsDoubled = SolveModel(ptsDoubled, out _, sensorHalf: 6000); // initializes the 1/(2σ) weights

            var chiCorrect = nllsCorrect.ChiSquared(nllsCorrect.Solver, model);
            var chiDoubled = nllsDoubled.ChiSquared(nllsDoubled.Solver, model);
            Assert.That(chiCorrect / chiDoubled, Is.EqualTo(4.0).Within(1e-9));
        }

        [Test]
        public void ReducedChiSquared_IsMagnitudeIndependentWhereR2IsNot() {
            // Same residual noise, two very different tilt magnitudes. Reduced χ² depends only on residuals
            // vs σ (so both ≈ 1), but R² inflates with signal magnitude — the exact reason R² is a poor,
            // non-repeatable acceptance metric and reduced χ² replaces it.
            const double sigma = 2.0;
            var nearlyFlat = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 1e-5, gy: 0, k: 0);
            var stronglyTilted = new SensorParaboloidModel(x0: 0, y0: 0, z0: 1000, gx: 2e-2, gy: 0, k: 0);

            var flatPts = NoisyGrid(nearlyFlat, sigma: sigma, declaredSigma: sigma, seed: 7);
            var tiltedPts = NoisyGrid(stronglyTilted, sigma: sigma, declaredSigma: sigma, seed: 7);

            var nllsFlat = SolveModel(flatPts, out var flatResult, sensorHalf: 6000);
            var nllsTilted = SolveModel(tiltedPts, out var tiltedResult, sensorHalf: 6000);

            var flatReduced = nllsFlat.ReducedChiSquared(nllsFlat.Solver, flatResult);
            var tiltedReduced = nllsTilted.ReducedChiSquared(nllsTilted.Solver, tiltedResult);
            // Solve() does not populate the model's display stats, so query R² via the solver.
            var flatR2 = nllsFlat.GoodnessOfFit(nllsFlat.Solver, flatResult);
            var tiltedR2 = nllsTilted.GoodnessOfFit(nllsTilted.Solver, tiltedResult);

            Assert.Multiple(() => {
                // Reduced χ² ~ 1 for both, regardless of signal magnitude.
                Assert.That(flatReduced, Is.EqualTo(1.0).Within(0.4));
                Assert.That(tiltedReduced, Is.EqualTo(1.0).Within(0.4));
                Assert.That(Math.Abs(flatReduced - tiltedReduced), Is.LessThan(0.4));
                // R² collapses on the near-flat field but is near 1 for the strongly tilted one.
                Assert.That(flatR2, Is.LessThan(0.5));
                Assert.That(tiltedR2, Is.GreaterThan(0.95));
            });
        }

        /// <summary>
        /// Composite Simpson 2D integration of an arbitrary surface over [-w/2,w/2] x [-h/2,h/2].
        /// Simpson is exact for polynomials up to degree 3 per axis, and the paraboloid surface is
        /// degree 2 in x and y, so this is an exact, parameterization-independent oracle for Volume().
        /// </summary>
        private static double SimpsonIntegrate2D(Func<double, double, double> f, double w, double h, int n = 8) {
            if (n % 2 != 0) {
                throw new ArgumentException("n must be even");
            }
            double ax = -w / 2.0, ay = -h / 2.0;
            double dx = w / n, dy = h / n;

            double SimpsonWeight(int i) => (i == 0 || i == n) ? 1.0 : (i % 2 == 1 ? 4.0 : 2.0);

            double total = 0.0;
            for (int i = 0; i <= n; i++) {
                double x = ax + i * dx;
                double wx = SimpsonWeight(i);
                for (int j = 0; j <= n; j++) {
                    double y = ay + j * dy;
                    double wy = SimpsonWeight(j);
                    total += wx * wy * f(x, y);
                }
            }
            return total * dx * dy / 9.0;
        }

        [Test]
        public void Volume_MatchesNumericalIntegralOfValueAt_WithOffsetCenter() {
            // Nonzero Y0 (and X0) with nonzero curvature is exactly the case the Y0+Y0 vs Y0*Y0 bug
            // got wrong: it only manifests when the curvature center is offset in Y.
            var model = SensorParaboloidModel.FromTiltCurvature(x0: 1500.0, y0: -900.0, z0: 25000.0, theta: 0.01, phi: 0.7, c: 0.05);
            double w = 23000.0, h = 16000.0;

            var analytic = model.Volume(w, h);
            var numeric = SimpsonIntegrate2D((x, y) => model.ValueAt(x, y), w, h);

            Assert.That(analytic, Is.EqualTo(numeric).Within(Math.Abs(numeric) * 1e-9 + 1e-3));
        }

        [Test]
        public void Volume_MatchesNumericalIntegralOfValueAt_FlatTiltedField() {
            var model = SensorParaboloidModel.FromTiltCurvature(x0: 0.0, y0: 0.0, z0: 10000.0, theta: 0.02, phi: 1.2, c: 1e-6);
            double w = 20000.0, h = 13000.0;

            var analytic = model.Volume(w, h);
            var numeric = SimpsonIntegrate2D((x, y) => model.ValueAt(x, y), w, h);

            Assert.That(analytic, Is.EqualTo(numeric).Within(Math.Abs(numeric) * 1e-9 + 1e-3));
        }

        [Test]
        public void Volume_OffsetCenterCurvature_IsSymmetricInY0() {
            // Regression guard for the specific bug: with X0 = 0 and no tilt, the only Y-center contribution
            // to the curvature volume is Y0², so the volume must be symmetric about Y0 = 0.
            var pos = SensorParaboloidModel.FromTiltCurvature(x0: 0.0, y0: 800.0, z0: 0.0, theta: 1e-6, phi: 0.0, c: 0.05);
            var neg = SensorParaboloidModel.FromTiltCurvature(x0: 0.0, y0: -800.0, z0: 0.0, theta: 1e-6, phi: 0.0, c: 0.05);
            double w = 10000.0, h = 10000.0;

            Assert.That(pos.Volume(w, h), Is.EqualTo(neg.Volume(w, h)).Within(1e-6));
        }
    }
}
