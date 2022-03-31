#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TestApp {

    public interface INonLinearLeastSquaresDataPoint {

        double[] ToInput();

        double ToOutput();
    }

    public interface INonLinearLeastSquaresParameters {

        void FromArray(double[] parameters);

        double[] ToArray();
    }

    public class SensorParaboloidDataPoint : INonLinearLeastSquaresDataPoint {

        public SensorParaboloidDataPoint(double x, double y, double focuserPosition) {
            this.X = x;
            this.Y = y;
            this.FocuserPosition = focuserPosition;
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double FocuserPosition { get; private set; }

        public double[] ToInput() {
            return new double[] { X, Y };
        }

        public double ToOutput() {
            return FocuserPosition;
        }

        public override string ToString() {
            return $"{{{nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(FocuserPosition)}={FocuserPosition.ToString()}}}";
        }
    }

    public class SensorParaboloidModel : INonLinearLeastSquaresParameters {

        public SensorParaboloidModel(double x0, double y0, double z0, double theta, double phi, double c) {
            this.X0 = x0;
            this.Y0 = y0;
            this.Z0 = z0;
            this.Theta = theta;
            this.Phi = phi;
            this.C = c;
        }

        public SensorParaboloidModel() {
        }

        public void FromArray(double[] parameters) {
            if (parameters == null || parameters.Length != 6) {
                throw new ArgumentException($"Expected a 6-element array of parameters");
            }

            this.X0 = parameters[0];
            this.Y0 = parameters[1];
            this.Z0 = parameters[2];
            this.Theta = parameters[3];
            this.Phi = parameters[4];
            this.C = parameters[5];
        }

        public double[] ToArray() {
            return new double[] {
                this.X0,
                this.Y0,
                this.Z0,
                this.Theta,
                this.Phi,
                this.C
            };
        }

        public double X0 { get; private set; }
        public double Y0 { get; private set; }
        public double Z0 { get; private set; }
        public double Theta { get; private set; }
        public double Phi { get; private set; }
        public double C { get; private set; }

        public override string ToString() {
            return $"{{{nameof(X0)}={X0.ToString()}, {nameof(Y0)}={Y0.ToString()}, {nameof(Z0)}={Z0.ToString()}, {nameof(Theta)}={Theta.ToString()}, {nameof(Phi)}={Phi.ToString()}, {nameof(C)}={C.ToString()}}}";
        }
    }

    public abstract class NonLinearLeastSquaresSolverBase<T, U>
        where T : INonLinearLeastSquaresDataPoint
        where U : INonLinearLeastSquaresParameters, new() {

        protected NonLinearLeastSquaresSolverBase(List<T> dataPoints, int numParameters) {
            this.Inputs = dataPoints.Select(p => p.ToInput()).ToArray();
            this.Outputs = dataPoints.Select(p => p.ToOutput()).ToArray();
            this.dataPoints = dataPoints;
            this.NumParameters = numParameters;
        }

        private readonly List<T> dataPoints;

        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        public int NumParameters { get; private set; }

        public virtual bool UseJacobian => false;

        public virtual double NumericIntegrationIntervalSize => 1E-8;

        public abstract double Value(double[] parameters, double[] input);

        public virtual void Gradient(double[] parameters, double[] input, double[] result) {
            throw new NotImplementedException();
        }

        public abstract void SetInitialGuess(double[] initialGuess);

        public abstract void SetBounds(double[] lowerBounds, double[] upperBounds);

        public abstract void SetScale(double[] scales);
    }

    public class SensorParaboloidSolver : NonLinearLeastSquaresSolverBase<SensorParaboloidDataPoint, SensorParaboloidModel> {
        private readonly double inFocusPosition;
        private readonly double sensorSizeX;
        private readonly double sensorSizeY;

        public SensorParaboloidSolver(
            List<SensorParaboloidDataPoint> dataPoints,
            double sensorSizeX,
            double sensorSizeY,
            double inFocusPosition) : base(dataPoints, 6) {
            this.sensorSizeX = sensorSizeX;
            this.sensorSizeY = sensorSizeY;
            this.inFocusPosition = inFocusPosition;
        }

        public override bool UseJacobian => false;

        public override double Value(double[] parameters, double[] input) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0]; // u
            var y0 = parameters[1]; // v
            var z0 = parameters[2]; // w
            var theta = parameters[3]; // t
            var phi = parameters[4]; // p
            var c = parameters[5]; // c

            var XPrime = X - x0;
            var YPrime = Y - y0;
            var XPrime2 = XPrime * XPrime;
            var YPrime2 = YPrime * YPrime;

            var ZPrime = c * (XPrime2 + YPrime2);
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);
            var sinPhi = Math.Sin(phi);
            var cosPhi = Math.Cos(phi);
            var result = XPrime * sinPhi + (-YPrime * sinTheta + ZPrime * cosTheta) * cosPhi + z0;
            // var result = z0 + XPrime * Math.Cos(phi) * Math.Tan(theta) + YPrime * Math.Sin(phi) * Math.Tan(theta) + c * (XPrime2 + YPrime2);
            return result;
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            var X = input[0];
            var Y = input[1];
            var x0 = parameters[0]; // u
            var y0 = parameters[1]; // v
            var z0 = parameters[2]; // w - Not used
            var theta = parameters[3]; // t
            var phi = parameters[4]; // p
            var c = parameters[5]; // c

            var XPrime = X - x0;
            var YPrime = Y - y0;
            var XPrime2 = XPrime * XPrime;
            var YPrime2 = YPrime * YPrime;
            var sinP = Math.Sin(phi);
            var cosP = Math.Cos(phi);
            var cosT = Math.Cos(theta);
            var tanT = Math.Tan(theta);
            var cosT2 = cosT * cosT;
            var secT2 = 1.0 / cosT2;

            // u
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+u
            // -2 c (-u + x) - Cos[p] Tan[t]
            var dz_du = -2 * c * XPrime - cosP * tanT;

            // v
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+v
            //  -2 c (-v + y) - Sin[p] Tan[t]
            var dz_dv = -2 * c * YPrime - sinP * tanT;

            // w
            var dz_dw = 1;

            // t
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+t
            //  Sec[t]^2 ((-u + x) Cos[p] + (-v + y) Sin[p])
            var dz_dt = secT2 * (XPrime * cosP + YPrime * sinP);

            // p
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+p
            //  ((-v + y) Cos[p] - (-u + x) Sin[p]) Tan[t]
            var dz_dp = tanT * (YPrime * cosP - XPrime * sinP);

            // c
            //  https://www.wolframalpha.com/input?i2d=true&i=derivative+of+w+%2B+%5C%2840%29x+-+u%5C%2841%29+*+Cos%5Bp%5D+*+Tan%5Bt%5D+%2B+%5C%2840%29y+-+v%5C%2841%29+*+Sin%5Bp%5D+*+Tan%5Bt%5D+%2B+c+*+%5C%2840%29Power%5B%5C%2840%29x+-+u%5C%2841%29%2C2%5D+%2BPower%5B%5C%2840%29y-v%5C%2841%29%2C2%5D%5C%2841%29+with+respect+to+c
            //  (-u + x)^2 + (-v + y)^2
            var dz_dc = XPrime2 + YPrime2;

            result[0] = dz_du;
            result[1] = dz_dv;
            result[2] = dz_dw;
            result[3] = dz_dt;
            result[4] = dz_dp;
            result[5] = dz_dc;
        }

        public override void SetInitialGuess(double[] initialGuess) {
            initialGuess[0] = sensorSizeX / 2.0;
            initialGuess[1] = sensorSizeY / 2.0;
            initialGuess[2] = inFocusPosition;
            initialGuess[3] = 0.0;
            initialGuess[4] = 0.0;
            initialGuess[5] = 0.0;
        }

        public override void SetBounds(double[] lowerBounds, double[] upperBounds) {
            lowerBounds[0] = 0.0;
            lowerBounds[1] = 0.0;
            lowerBounds[2] = 0;
            lowerBounds[3] = -Math.PI / 2.0;
            lowerBounds[4] = -Math.PI / 2.0;
            lowerBounds[5] = double.NegativeInfinity;

            upperBounds[0] = sensorSizeX;
            upperBounds[1] = sensorSizeY;
            upperBounds[2] = double.PositiveInfinity;
            upperBounds[3] = Math.PI / 2.0;
            upperBounds[4] = Math.PI / 2.0;
            upperBounds[5] = double.PositiveInfinity;
        }

        public override void SetScale(double[] scales) {
            scales[0] = 1.0;
            scales[1] = 1.0;
            scales[2] = 1.0;
            scales[3] = 1.0;
            scales[4] = 1.0;
            scales[5] = 1E-3;
        }
    }

    public class OptGuardException : Exception {

        public OptGuardException(string message) : base(message) {
        }
    }

    public class OptGuardBadGradientException : OptGuardException {

        public OptGuardBadGradientException(double[,] differences) : base("Bad gradients") {
            this.Differences = differences;
        }

        public double[,] Differences { get; private set; }
    }

    public class NonLinearLeastSquaresSolver<S, T, U> : IDisposable
        where S : NonLinearLeastSquaresSolverBase<T, U>
        where T : INonLinearLeastSquaresDataPoint
        where U : class, INonLinearLeastSquaresParameters, new() {
        public bool OptGuardEnabled { get; set; } = false;

        private CancellationToken solverCancellationToken;

        private double[] weights;

        public S Solver { get; private set; }

        public U Solve(S solver, int maxIterations = 0, double tolerance = 1E-8, CancellationToken ct = default(CancellationToken)) {
            var initialGuess = new double[solver.NumParameters];
            solver.SetInitialGuess(initialGuess);
            InitializeWeights(solver);
            return SolveWithInitialGuess(solver, initialGuess, maxIterations, tolerance, ct);
        }

        public U SolveIRLS(
            S solver,
            int maxIterationsIRLS = 0,
            double toleranceIRLS = 1E-8,
            int maxIterationsLS = 0,
            double toleranceLS = 1E-8,
            double minResidual = 1E-6, // set to noise sigma
            CancellationToken ct = default(CancellationToken)) {
            try {
                maxIterationsIRLS = maxIterationsIRLS > 0 ? Math.Min(maxIterationsIRLS, 20) : 20;
                var initialGuess = new double[solver.NumParameters];
                InitializeWeights(solver);

                var iterationsIRLS = 0;
                var sumOfResidualsDelta = double.PositiveInfinity;
                var prevSumOfResiduals = double.PositiveInfinity;
                U lastSolution = null;
                solver.SetInitialGuess(initialGuess);
                while (sumOfResidualsDelta > toleranceIRLS && iterationsIRLS++ < maxIterationsIRLS) {
                    lastSolution = SolveWithInitialGuess(solver, initialGuess, maxIterationsLS, toleranceLS, ct);
                    var iterationSolutionArray = lastSolution.ToArray();

                    var sumOfResiduals = 0.0d;
                    for (int i = 0; i < this.weights.Length; ++i) {
                        var observedValue = solver.Outputs[i];
                        var estimatedValue = solver.Value(iterationSolutionArray, solver.Inputs[i]);
                        var newWeightDenom = Math.Abs(estimatedValue - observedValue);
                        sumOfResiduals += newWeightDenom;
                        newWeightDenom = Math.Max(minResidual, newWeightDenom);
                        var newWeight = 1.0 / newWeightDenom;
                        this.weights[i] = newWeight;
                    }

                    sumOfResidualsDelta = Math.Abs(sumOfResiduals - prevSumOfResiduals);
                    prevSumOfResiduals = sumOfResiduals;
                    initialGuess = iterationSolutionArray; // next starting point is the most recent solution
                }
                return lastSolution;
            } catch (Exception e) {
                throw;
            }
        }

        private U SolveWithInitialGuess(S solver, double[] initialGuess, int maxIterations, double tolerance, CancellationToken ct) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                var lowerBounds = new double[solver.NumParameters];
                var upperBounds = new double[solver.NumParameters];
                var scales = new double[solver.NumParameters];
                solver.SetBounds(lowerBounds, upperBounds);
                solver.SetScale(scales);

                var solution = new double[solver.NumParameters];
                if (solver.UseJacobian) {
                    alglib.minlmcreatevj(solver.Inputs.Length, initialGuess, out state);
                    alglib.minlmsetacctype(state, 1);
                } else {
                    alglib.minlmcreatev(solver.Inputs.Length, initialGuess, solver.NumericIntegrationIntervalSize, out state);
                }
                alglib.minlmsetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                alglib.minlmsetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                alglib.minlmsetscale(state, scales);

                if (OptGuardEnabled) {
                    alglib.minlmoptguardgradient(state, 1E-4);
                }

                // Perform the optimization
                this.Solver = solver;
                this.solverCancellationToken = ct;
                alglib.minlmoptimize(state, this.FitResiduals, this.FitResidualsJacobian, null, null);
                ct.ThrowIfCancellationRequested();

                alglib.minlmresults(state, out solution, out rep);
                if (rep.terminationtype < 0) {
                    // TODO: terminationtype=5 means max iterations were taken
                    string reason;
                    if (rep.terminationtype == -8) {
                        reason = "optimizer detected NAN/INF values either in the function itself, or in its Jacobian";
                    } else if (rep.terminationtype == -3) {
                        reason = "constraints are inconsistent";
                    } else {
                        reason = "unknown";
                    }
                    throw new Exception($"PSF modeling failed with type {rep.terminationtype} and reason: {reason}");
                }

                if (OptGuardEnabled) {
                    alglib.optguardreport ogrep;
                    alglib.minlmoptguardresults(state, out ogrep);
                    try {
                        if (ogrep.badgradsuspected) {
                            var differences = new double[ogrep.badgraduser.GetLength(0), ogrep.badgraduser.GetLength(1)];
                            for (int i = 0; i < ogrep.badgraduser.GetLength(0); ++i) {
                                for (int j = 0; j < ogrep.badgraduser.GetLength(1); ++j) {
                                    differences[i, j] = ogrep.badgradnum[i, j] - ogrep.badgraduser[i, j];
                                }
                            }
                            throw new OptGuardBadGradientException(differences);
                        }
                    } finally {
                        alglib.deallocateimmediately(ref ogrep);
                    }
                }

                var solutionModel = new U();
                solutionModel.FromArray(solution);
                return solutionModel;
            } finally {
                if (state != null) {
                    alglib.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    alglib.deallocateimmediately(ref rep);
                }
            }
        }

        private void InitializeWeights(S solver) {
            this.weights = new double[solver.Inputs.Length];
            for (int i = 0; i < weights.Length; ++i) {
                weights[i] = 1.0;
            }
        }

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            solverCancellationToken.ThrowIfCancellationRequested();

            // x contains the parameters
            // fi will store the residualized result for each observation
            for (int i = 0; i < Solver.Inputs.Length; ++i) {
                var input = Solver.Inputs[i];
                var observedValue = Solver.Outputs[i];
                var estimatedValue = Solver.Value(parameters, input);
                fi[i] = this.weights[i] * (estimatedValue - observedValue);
            }
        }

        public virtual void FitResidualsJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            solverCancellationToken.ThrowIfCancellationRequested();

            // Every observation is a function that returns a value.
            // The optimizer minimizes the sum of the squares of the values, so we use a residual for the value function
            // The Jacobian matrix has 1 row per function/observation, and each column is a partial derivative with respect to each parameter

            FitResiduals(parameters, fi, obj);
            var singleGradient = new double[parameters.Length];
            for (int i = 0; i < Solver.Inputs.Length; ++i) {
                Solver.Gradient(parameters, Solver.Inputs[i], singleGradient);
                for (int j = 0; j < parameters.Length; ++j) {
                    jac[i, j] = singleGradient[j];
                }
            }
        }

        public double GoodnessOfFit(S solver, U model) {
            var parameters = model.ToArray();
            var rss = 0.0d;
            var tss = 0.0d;
            int pixelCount = solver.Inputs.Length;
            var yBar = solver.Outputs.Average();
            for (int i = 0; i < pixelCount; ++i) {
                var input = solver.Inputs[i];
                var observedValue = solver.Outputs[i];
                var estimatedValue = solver.Value(parameters, input);
                var residual = estimatedValue - observedValue;
                var observedDispersion = observedValue - yBar;
                tss += observedDispersion * observedDispersion;
                rss += residual * residual;
            }
            return 1 - rss / tss;
        }

        public void Dispose() {
            throw new NotImplementedException();
        }
    }
}