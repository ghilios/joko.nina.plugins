using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    // y = a + b*x
    internal class LinearDataPoint : INonLinearLeastSquaresDataPoint {
        public double X { get; set; }
        public double Y { get; set; }
        public double Sigma { get; set; } = 1.0;

        public double[] ToInput() => new double[] { X };

        public double ToOutput() => Y;

        public double ToOutputStdDev() => Sigma;
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
        public void SolveWinsorizedResiduals_SkewedContamination_MedianCenteredClipRecoversTruth() {
            // A one-sided contaminant cluster offsets the seed fit, so the residual distribution has a
            // nonzero median. The clip must be centered on that median (in weighted units): a zero-centered
            // cut prunes the good bulk one-sidedly and walks the fit away from the data.
            const double trueA = 5.0;
            const double trueB = 2.0;
            var pts = new List<LinearDataPoint>();
            for (int i = 0; i < 44; ++i) {
                var noise = (i % 2 == 0) ? 0.3 : -0.3;
                pts.Add(new LinearDataPoint { X = i, Y = trueA + trueB * i + noise });
            }
            foreach (var x in new[] { 10.5, 15.5, 25.5, 30.5 }) {
                pts.Add(new LinearDataPoint { X = x, Y = trueA + trueB * x + 60.0 });
            }

            var solver = new LinearSolver(pts);
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI) {
                WinsorizedDiagnosticsEnabled = true
            };

            var result = nlls.SolveWinsorizedResiduals(solver);

            var firstIteration = nlls.WinsorizedDiagnostics.First();
            Assert.Multiple(() => {
                Assert.That(result.A, Is.EqualTo(trueA).Within(0.2));
                Assert.That(result.B, Is.EqualTo(trueB).Within(0.02));
                Assert.That(nlls.InputEnabledCount, Is.EqualTo(44), "only the 4 contaminants should be pruned");
                Assert.That(firstIteration.UpperBound - firstIteration.ResidualMedian,
                    Is.EqualTo(2.5 * firstIteration.ResidualMAD).Within(1e-9), "clip must be centered on the residual median");
                Assert.That(firstIteration.ResidualMedian - firstIteration.LowerBound,
                    Is.EqualTo(2.5 * firstIteration.ResidualMAD).Within(1e-9), "clip must be centered on the residual median");
            });
        }

        [Test]
        public void SolveWinsorizedResiduals_HonestLargeSigmaPoints_AreNotPruned() {
            // Points with honestly-large σ and errors consistent with it (~1σ) are not outliers; a point
            // with small σ and a ~20σ error is. Judged on unweighted residuals both look alike, so the clip
            // must operate on weighted residuals (r/σ).
            const double trueA = 5.0;
            const double trueB = 2.0;
            var pts = new List<LinearDataPoint>();
            for (int i = 0; i < 30; ++i) {
                var noise = (i % 2 == 0) ? 0.4 : -0.4;
                pts.Add(new LinearDataPoint { X = i, Y = trueA + trueB * i + noise, Sigma = 0.5 });
            }
            for (int i = 30; i < 42; ++i) {
                var noise = (i % 2 == 0) ? 9.0 : -12.0;
                pts.Add(new LinearDataPoint { X = i, Y = trueA + trueB * i + noise, Sigma = 10.0 });
            }
            pts.Add(new LinearDataPoint { X = 21.3, Y = trueA + trueB * 21.3 + 10.0, Sigma = 0.5 });

            var solver = new LinearSolver(pts);
            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);

            var result = nlls.SolveWinsorizedResiduals(solver);

            Assert.Multiple(() => {
                Assert.That(nlls.InputEnabledCount, Is.EqualTo(42), "only the 20σ contaminant should be pruned");
                Assert.That(result.A, Is.EqualTo(trueA).Within(0.4));
                Assert.That(result.B, Is.EqualTo(trueB).Within(0.04));
            });
        }

        [Test]
        public void SolveWinsorizedResiduals_RemovalBudget_CapsTotalPruning() {
            // 10 of 50 points are contaminated. The default 10% removal budget must stop pruning at 5 points
            // (worst-first) and still terminate; raising the budget allows the full cluster to be removed.
            const double trueA = 5.0;
            const double trueB = 2.0;
            List<LinearDataPoint> MakePoints() {
                var pts = new List<LinearDataPoint>();
                for (int i = 0; i < 40; ++i) {
                    var noise = (i % 2 == 0) ? 0.3 : -0.3;
                    pts.Add(new LinearDataPoint { X = i, Y = trueA + trueB * i + noise });
                }
                for (int i = 0; i < 10; ++i) {
                    var x = 5.5 + 3.0 * i;
                    pts.Add(new LinearDataPoint { X = x, Y = trueA + trueB * x + 60.0 });
                }
                return pts;
            }

            var nlls = new NonLinearLeastSquaresSolver<LinearSolver, LinearDataPoint, LinearParameters>(alglibAPI);
            nlls.SolveWinsorizedResiduals(new LinearSolver(MakePoints()));
            var cappedEnabled = nlls.InputEnabledCount;
            var cappedIterations = nlls.SolutionIterations;

            var uncapped = nlls.SolveWinsorizedResiduals(new LinearSolver(MakePoints()), maxRemovalFraction: 0.25);

            Assert.Multiple(() => {
                Assert.That(cappedEnabled, Is.EqualTo(45), "default 10% budget must cap removals at 5 of 50");
                Assert.That(cappedIterations, Is.LessThanOrEqualTo(10), "budget exhaustion must still terminate");
                Assert.That(nlls.InputEnabledCount, Is.EqualTo(40), "raised budget must remove the full cluster");
                Assert.That(uncapped.A, Is.EqualTo(trueA).Within(0.2));
                Assert.That(uncapped.B, Is.EqualTo(trueB).Within(0.02));
            });
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
