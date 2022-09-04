#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Math.Optimization.Losses;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.WPF.Base.Utility.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class HyperbolicFittingAlglib : HyperbolicFitting {
        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }
        public bool UseJacobian { get; set; } = false;
        public bool OptGuardEnabled { get; set; } = false;
        private bool allowRotation;
        private readonly IAlglibAPI alglibAPI;

        private HyperbolicFittingAlglib(IAlglibAPI alglibAPI, double[][] inputs, double[] outputs, bool allowRotation) {
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.allowRotation = allowRotation;
            this.alglibAPI = alglibAPI;
        }

        public static HyperbolicFittingAlglib Create(IAlglibAPI alglibAPI, ICollection<ScatterErrorPoint> points, bool allowRotation) {
            var nonzeroPoints = points.Where((dp) => dp.Y >= 0.1).ToList();
            var inputs = nonzeroPoints.Select(dp => new double[] { dp.X }).ToArray();
            var outputs = nonzeroPoints.Select(dp => dp.Y).ToArray();
            return new HyperbolicFittingAlglib(alglibAPI, inputs, outputs, allowRotation);
        }

        private Func<double, double> GetFittingForParameters(double[] parameters) {
            var x0 = parameters[0];
            var y0 = parameters[1];
            var a = parameters[2];
            var b = parameters[3];
            var T = parameters[4];
            return x => a / b * Math.Sqrt((x - x0) * (x - x0) + b * b) + y0;

            /*
            if (!allowRotation) {
                return x => a / b * Math.Sqrt((x - x0) * (x - x0) + b * b) + y0;
            }

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            // x = a * cosh(t) * cos(T) - b * sinh(t) * sin(T)
            // y = a * cosh(t) * sin(T) + b * sinh(t) * cos(T)

            // Use Newton-Raphson to solve for t
            var term1Constant = a * cosT;
            var term2Constant = b * sinT;
            const double tErrorTolerance = 1.0e-11;
            const int MAX_TSOLVE_ITERATIONS = 2000;
            return x => {
                double estimate = Math.Log(x);
                double estimateError = double.PositiveInfinity;
                int iterations = 0;
                while (Math.Abs(estimateError) > tErrorTolerance && iterations++ < MAX_TSOLVE_ITERATIONS) {
                    estimateError = term1Constant * Math.Cosh(estimate) - term2Constant * Math.Sinh(estimate) - x;
                    var d_dx = term1Constant * Math.Sinh(estimate) + term2Constant * Math.Cosh(estimate);
                    estimate -= estimate / d_dx;
                }

                if (iterations >= MAX_TSOLVE_ITERATIONS) {
                    throw new Exception($"Solution for rotated hyperbola parameter did not converge");
                }
                return a * Math.Cosh(estimate) * sinT + b * Math.Sinh(estimate) * cosT;
            };
            */
        }

        private Action<double, double[]> GetGradientForParameters(double[] parameters) {
            var x0 = parameters[0];
            var y0 = parameters[1];
            var a = parameters[2];
            var b = parameters[3];
            return (x, fi) => {
                var XPrime = x - x0;
                var XPrime2 = XPrime * XPrime;
                var B2 = b * b;
                var SQRT_TERM = Math.Sqrt(B2 + XPrime2);

                var da = SQRT_TERM / b;

                var db_part1 = -a * XPrime2;
                var db_part2 = B2 * SQRT_TERM;
                var db = db_part1 / db_part2;

                var dx0_part1 = -a * XPrime;
                var dx0_part2 = b * SQRT_TERM;
                var dx0 = dx0_part1 / dx0_part2;

                var dy0 = 1.0d;
                fi[0] = dx0;
                fi[1] = dy0;
                fi[2] = da;
                fi[3] = db;
            };
        }

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            var fitting = GetFittingForParameters(parameters);
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var input = this.Inputs[i][0];
                var observedValue = this.Outputs[i];
                var estimatedValue = fitting(input);
                fi[i] = estimatedValue - observedValue;
            }
        }

        public virtual void FitResidualsJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            // Every observation is a function that returns a value.
            // The optimizer minimizes the sum of the squares of the values, so we use a residual for the value function
            // The Jacobian matrix has 1 row per function/observation, and each column is a partial derivative with respect to each parameter

            FitResiduals(parameters, fi, obj);
            var singleGradient = new double[parameters.Length];
            var gradient = GetGradientForParameters(parameters);
            for (int i = 0; i < this.Inputs.Length; ++i) {
                gradient(this.Inputs[i][0], singleGradient);
                for (int j = 0; j < parameters.Length; ++j) {
                    jac[i, j] = singleGradient[j];
                }
            }
        }

        public bool Solve() {
            if (Inputs.Length == 0) {
                return false;
            }

            var lowestOutputIndex = Outputs.Select((o, i) => (o, i)).Aggregate((l, r) => l.o < r.o ? l : r).i;
            var highestOutputIndex = Outputs.Select((o, i) => (o, i)).Aggregate((l, r) => l.o > r.o ? l : r).i;
            var lowestOutput = Outputs[lowestOutputIndex];
            var highestOutput = Outputs[highestOutputIndex];
            var lowestInput = Inputs[lowestOutputIndex][0];
            var highestInput = Inputs[highestOutputIndex][0];
            if (highestInput < lowestInput) {
                highestInput = 2 * lowestInput - highestInput;
            } // Always go up

            var initialA = lowestOutput;
            var initialA2 = initialA * initialA;
            var highestOutput2 = highestOutput * highestOutput;
            var inputDelta = highestInput - lowestInput;

            //Alternative hyperbola formula: sqr(y)/sqr(a)-sqr(x)/sqr(b)=1 ==>  sqr(b)=sqr(x)*sqr(a)/(sqr(y)-sqr(a)
            var initialB = Math.Sqrt(inputDelta * inputDelta * initialA2 / (highestOutput2 - initialA2));

            var initialX0 = lowestInput;
            var initialY0 = 0.0d;

            if (double.IsNaN(initialA) || double.IsNaN(initialB) || initialA == 0 || initialB == 0 || inputDelta == 0) {
                //Not enough valid data points to fit a curve
                return false;
            }

            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                var rotationRadiansBound = allowRotation ? Math.PI / 8 : 0.0d;
                var initialGuess = new double[] { initialX0, initialY0, initialA, initialB, 0.0d };
                var lowerBounds = new double[] { 0.0d, -lowestOutput, 0.001d, 0.001d, -rotationRadiansBound };
                var upperBounds = new double[] { double.PositiveInfinity, lowestOutput, double.PositiveInfinity, double.PositiveInfinity, rotationRadiansBound };
                var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
                var scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1, 0.1 };
                var solution = new double[4];
                var tolerance = 1E-6;
                var maxIterations = 0; // Keep going until the solution is found
                const double deltaForNumericIntegration = 1E-6;
                if (UseJacobian) {
                    this.alglibAPI.minlmcreatevj(this.Inputs.Length, initialGuess, out state);
                    this.alglibAPI.minlmsetacctype(state, 1);
                } else {
                    this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                }

                this.alglibAPI.minlmsetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                this.alglibAPI.minlmsetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                this.alglibAPI.minlmsetscale(state, scale);

                if (OptGuardEnabled) {
                    this.alglibAPI.minlmoptguardgradient(state, deltaForNumericIntegration);
                }

                // Perform the optimization
                this.alglibAPI.minlmoptimize(state, this.FitResiduals, this.FitResidualsJacobian, null, null);

                this.alglibAPI.minlmresults(state, out solution, out rep);
                if (rep.terminationtype < 0) {
                    string reason;
                    if (rep.terminationtype == -8) {
                        reason = "optimizer detected NAN/INF values either in the function itself, or in its Jacobian";
                    } else if (rep.terminationtype == -3) {
                        reason = "constraints are inconsistent";
                    } else {
                        reason = "unknown";
                    }
                    throw new Exception($"Hyperbolic modeling failed with type {rep.terminationtype} and reason: {reason}");
                }

                if (OptGuardEnabled) {
                    alglib.optguardreport ogrep;
                    this.alglibAPI.minlmoptguardresults(state, out ogrep);
                    try {
                        if (ogrep.badgradsuspected) {
                            var differences = new double[ogrep.badgraduser.GetLength(0), ogrep.badgraduser.GetLength(1)];
                            for (int i = 0; i < ogrep.badgraduser.GetLength(0); ++i) {
                                for (int j = 0; j < ogrep.badgraduser.GetLength(1); ++j) {
                                    differences[i, j] = ogrep.badgradnum[i, j] - ogrep.badgraduser[i, j];
                                }
                            }
                            // throw new OptGuardBadGradientException(differences);
                            throw new Exception();
                        }
                    } finally {
                        this.alglibAPI.deallocateimmediately(ref ogrep);
                    }
                }

                var x0 = solution[0];
                var y0 = solution[1];
                var a = solution[2];
                var b = solution[3];

                FormattableString expression = $"y = {a:0.###}/{b:0.###} * √((x - {x0:0.###})² + {b:0.###}²) + {y0:0.###}";
                Expression = expression.ToString(CultureInfo.InvariantCulture);
                Fitting = GetFittingForParameters(solution);
                Minimum = new DataPoint(x0, a + y0);

                var transformed = new double[Inputs.Length];
                var rSquared = new RSquaredLoss(Inputs.Length, Outputs);
                for (var i = 0; i < Inputs.Length; i++) {
                    transformed[i] = Fitting(Inputs[i][0]);
                }
                RSquared = rSquared.Loss(transformed);
                return true;
            } finally {
                if (state != null) {
                    this.alglibAPI.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
            }
        }

        public override string ToString() {
            return $"{Expression}";
        }
    }
}