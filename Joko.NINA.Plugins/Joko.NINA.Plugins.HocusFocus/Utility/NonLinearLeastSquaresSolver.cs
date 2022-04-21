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

namespace NINA.Joko.Plugins.HocusFocus.Utility {

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

    public class NonLinearLeastSquaresSolver<S, T, U>
        where S : NonLinearLeastSquaresSolverBase<T, U>
        where T : INonLinearLeastSquaresDataPoint
        where U : class, INonLinearLeastSquaresParameters, new() {
        public bool OptGuardEnabled { get; set; } = false;
        public int SolutionIterations { get; private set; } = 0;

        private CancellationToken solverCancellationToken;

        private double[] weights;
        private bool[] inputEnabled;

        public int InputEnabledCount {
            get => inputEnabled.Count(i => i);
        }

        public S Solver { get; private set; }

        public U Solve(S solver, int maxIterations = 0, double tolerance = 1E-8, CancellationToken ct = default(CancellationToken)) {
            var initialGuess = new double[solver.NumParameters];
            solver.SetInitialGuess(initialGuess);
            InitializeWeights(solver);
            SolutionIterations = 0;
            var result = SolveWithInitialGuess(solver, initialGuess, maxIterations, tolerance, ct);
            SolutionIterations = 1;
            return result;
        }

        public U SolveWinsorizedResiduals(
            S solver,
            int maxWinsorizedIterations = 0,
            double winsorizationSigma = 4.0d,
            int maxIterationsLS = 0,
            double toleranceLS = 1E-8,
            CancellationToken ct = default(CancellationToken)) {
            maxWinsorizedIterations = maxWinsorizedIterations > 0 ? Math.Min(maxWinsorizedIterations, 10) : 10;
            var initialGuess = new double[solver.NumParameters];
            InitializeWeights(solver);

            var winsorizedIterations = 0;
            U lastSolution = null;
            solver.SetInitialGuess(initialGuess);
            int disabledCount = int.MaxValue;
            SolutionIterations = 0;
            while (disabledCount > 0 && winsorizedIterations++ < maxWinsorizedIterations) {
                lastSolution = SolveWithInitialGuess(solver, initialGuess, maxIterationsLS, toleranceLS, ct);
                var iterationSolutionArray = lastSolution.ToArray();

                var sumOfResiduals = 0.0d;
                var residuals = new List<(int, double)>();
                for (int i = 0; i < this.weights.Length; ++i) {
                    if (!inputEnabled[i]) {
                        continue;
                    }

                    var observedValue = solver.Outputs[i];
                    var estimatedValue = solver.Value(iterationSolutionArray, solver.Inputs[i]);
                    var residual = Math.Abs(estimatedValue - observedValue);
                    sumOfResiduals += residual;
                    residuals.Add((i, residual));
                }

                var (median, mad) = residuals.Select(p => p.Item2).MedianMAD();
                var upperBound = median + mad * winsorizationSigma;
                disabledCount = 0;
                for (int i = 0; i < residuals.Count; ++i) {
                    var residual = residuals[i].Item2;
                    if (residual > upperBound) {
                        var residualIdx = residuals[i].Item1;
                        inputEnabled[residualIdx] = false;
                        ++disabledCount;
                    }
                }

                initialGuess = iterationSolutionArray; // next starting point is the most recent solution
            }
            SolutionIterations = winsorizedIterations;
            return lastSolution;
        }

        public U SolveIRLS(
            S solver,
            int maxIterationsIRLS = 0,
            double toleranceIRLS = 1E-8,
            int maxIterationsLS = 0,
            double toleranceLS = 1E-8,
            double minResidual = 1E-6, // set to noise sigma
            CancellationToken ct = default(CancellationToken)) {
            maxIterationsIRLS = maxIterationsIRLS > 0 ? Math.Min(maxIterationsIRLS, 20) : 20;
            var initialGuess = new double[solver.NumParameters];
            InitializeWeights(solver);

            var iterationsIRLS = 0;
            var sumOfResidualsDelta = double.PositiveInfinity;
            var prevSumOfResiduals = double.PositiveInfinity;
            U lastSolution = null;
            solver.SetInitialGuess(initialGuess);
            SolutionIterations = 0;
            while (sumOfResidualsDelta > toleranceIRLS && iterationsIRLS++ < maxIterationsIRLS) {
                lastSolution = SolveWithInitialGuess(solver, initialGuess, maxIterationsLS, toleranceLS, ct);
                var iterationSolutionArray = lastSolution.ToArray();

                var sumOfResiduals = 0.0d;
                for (int i = 0; i < this.weights.Length; ++i) {
                    if (!this.inputEnabled[i]) {
                        this.weights[i] = 0.0d;
                        continue;
                    }

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
            SolutionIterations = iterationsIRLS;
            return lastSolution;
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
                    throw new Exception($"Modeling failed with type {rep.terminationtype} and reason: {reason}");
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
            this.inputEnabled = new bool[solver.Inputs.Length];
            for (int i = 0; i < weights.Length; ++i) {
                weights[i] = 1.0;
                inputEnabled[i] = true;
            }
        }

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            solverCancellationToken.ThrowIfCancellationRequested();

            // x contains the parameters
            // fi will store the residualized result for each observation
            for (int i = 0; i < Solver.Inputs.Length; ++i) {
                if (!inputEnabled[i]) {
                    fi[i] = 0.0;
                    continue;
                }

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
                if (!inputEnabled[i]) {
                    for (int j = 0; j < parameters.Length; ++j) {
                        jac[i, j] = 0.0d;
                    }
                    continue;
                }

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
            var yBar = solver.Outputs.Where((o, idx) => inputEnabled[idx]).Average();
            for (int i = 0; i < pixelCount; ++i) {
                if (!inputEnabled[i]) {
                    continue;
                }

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

        public double RMSError(S solver, U model) {
            var parameters = model.ToArray();
            var rss = 0.0d;
            int pixelCount = 0;
            for (int i = 0; i < solver.Inputs.Length; ++i) {
                if (!inputEnabled[i]) {
                    continue;
                }

                var input = solver.Inputs[i];
                var observedValue = solver.Outputs[i];
                var estimatedValue = solver.Value(parameters, input);
                var residual = estimatedValue - observedValue;
                rss += residual * residual;
                ++pixelCount;
            }
            return Math.Sqrt(rss / pixelCount);
        }
    }
}