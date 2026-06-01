using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    // y = a + b*x
    internal class LinearDataPoint : INonLinearLeastSquaresDataPoint {
        public double X { get; set; }
        public double Y { get; set; }

        public double[] ToInput() => new double[] { X };

        public double ToOutput() => Y;
    }

    internal class LinearParameters : INonLinearLeastSquaresParameters {
        public double A { get; set; }
        public double B { get; set; }

        public void FromArray(double[] parameters) {
            A = parameters[0];
            B = parameters[1];
        }

        public double[] ToArray() => new[] { A, B };
    }

    internal class LinearSolver : NonLinearLeastSquaresSolverBase<LinearDataPoint, LinearParameters> {

        public LinearSolver(List<LinearDataPoint> dataPoints) : base(dataPoints, numParameters: 2) {
        }

        public override double Value(double[] parameters, double[] input) {
            return parameters[0] + parameters[1] * input[0];
        }

        // d/dA = 1, d/dB = x. Supplied so ParameterCovariance (which forms JᵀWJ from Gradient) works.
        public override bool UseJacobian => true;

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            result[0] = 1.0;
            result[1] = input[0];
        }

        public override void SetInitialGuess(double[] initialGuess) {
            initialGuess[0] = 0.0;
            initialGuess[1] = 0.0;
        }

        public override void SetBounds(double[] lowerBounds, double[] upperBounds) {
            lowerBounds[0] = double.NegativeInfinity;
            lowerBounds[1] = double.NegativeInfinity;
            upperBounds[0] = double.PositiveInfinity;
            upperBounds[1] = double.PositiveInfinity;
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
        }
    }

    [TestFixture]
    public class NonLinearLeastSquaresSolverTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() {
            alglibAPI = new AlglibAPI();
        }

        private static List<LinearDataPoint> NoiselessLinearPoints(double a, double b, int count = 11) {
            return Enumerable.Range(0, count)
                .Select(i => new LinearDataPoint { X = i, Y = a + b * i })
                .ToList();
        }

        [Test]
        public void Solve_NoiselessLinearData_RecoversCoefficients() {
            const double trueA = 3.5;
            const double trueB = -1.25;
            var solver = new LinearSolver(NoiselessLinearPoints(trueA, trueB));
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);

            var result = nlls.Solve(solver, tolerance: 1e-10);

            Assert.Multiple(() => {
                Assert.That(result.A, Is.EqualTo(trueA).Within(1e-4));
                Assert.That(result.B, Is.EqualTo(trueB).Within(1e-4));
            });
        }

        [Test]
        public void GoodnessOfFit_NoiselessFit_IsApproximatelyOne() {
            var solver = new LinearSolver(NoiselessLinearPoints(2.0, 0.5));
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);
            var result = nlls.Solve(solver, tolerance: 1e-10);

            var rSquared = nlls.GoodnessOfFit(solver, result);
            Assert.That(rSquared, Is.EqualTo(1.0).Within(1e-6));
        }

        [Test]
        public void RMSError_NoiselessFit_IsApproximatelyZero() {
            var solver = new LinearSolver(NoiselessLinearPoints(1.0, 1.0));
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);
            var result = nlls.Solve(solver, tolerance: 1e-10);

            var rms = nlls.RMSError(solver, result);
            Assert.That(rms, Is.LessThan(1e-4));
        }

        [Test]
        public void ParameterCovariance_UnweightedLinearFit_MatchesOlsClosedForm() {
            // For an unweighted (σ = 1) straight-line fit, the asymptotic covariance s²·(JᵀWJ)⁻¹ reduces to
            // the textbook OLS result s²·(XᵀX)⁻¹ with X = [1, x]. Compute that independently and compare.
            var rng = new Random(1234);
            var xs = Enumerable.Range(0, 15).Select(i => (double)i).ToArray();
            var pts = xs.Select(x => new LinearDataPoint { X = x, Y = 2.0 + 0.5 * x + (rng.NextDouble() - 0.5) }).ToList();
            var solver = new LinearSolver(pts);
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);
            var result = nlls.Solve(solver, tolerance: 1e-12);

            var cov = nlls.ParameterCovariance(solver, result);
            Assert.That(cov, Is.Not.Null);

            int n = pts.Count;
            double sx = xs.Sum();
            double sxx = xs.Sum(x => x * x);
            double det = n * sxx - sx * sx;
            double rss = pts.Sum(p => {
                var r = result.A + result.B * p.X - p.Y;
                return r * r;
            });
            double s2 = rss / (n - 2);
            double expVarA = s2 * sxx / det;
            double expVarB = s2 * n / det;
            double expCovAB = -s2 * sx / det;

            Assert.Multiple(() => {
                Assert.That(cov[0, 0], Is.EqualTo(expVarA).Within(1e-4).Percent);
                Assert.That(cov[1, 1], Is.EqualTo(expVarB).Within(1e-4).Percent);
                Assert.That(cov[0, 1], Is.EqualTo(expCovAB).Within(1e-4).Percent);
                Assert.That(cov[1, 0], Is.EqualTo(cov[0, 1]).Within(1e-12)); // symmetric
            });
        }

        [Test]
        public void ParameterCovariance_InsufficientData_ReturnsNull() {
            // n = p (2 points, 2 parameters) ⇒ zero degrees of freedom ⇒ covariance is undetermined.
            var pts = new List<LinearDataPoint> {
                new LinearDataPoint { X = 0, Y = 1 },
                new LinearDataPoint { X = 1, Y = 2 }
            };
            var solver = new LinearSolver(pts);
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);
            var result = nlls.Solve(solver, tolerance: 1e-10);

            Assert.That(nlls.ParameterCovariance(solver, result), Is.Null);
        }

        [Test]
        public void SolveWinsorizedResiduals_OutlierCorrupted_ConvergesNearTruth() {
            const double trueA = 5.0;
            const double trueB = 2.0;
            var pts = NoiselessLinearPoints(trueA, trueB, count: 21);
            // Drop in two clear outliers
            pts[3] = new LinearDataPoint { X = pts[3].X, Y = pts[3].Y + 50.0 };
            pts[15] = new LinearDataPoint { X = pts[15].X, Y = pts[15].Y - 50.0 };

            var solver = new LinearSolver(pts);
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);

            var result = nlls.SolveWinsorizedResiduals(solver, maxWinsorizedIterations: 5, winsorizationSigma: 1.5);

            Assert.Multiple(() => {
                Assert.That(result.A, Is.EqualTo(trueA).Within(0.5));
                Assert.That(result.B, Is.EqualTo(trueB).Within(0.5));
            });
        }
    }
}
