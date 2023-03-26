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
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class HyperbolicUnevenFittingAlglib : AlglibHyperbolicFitting {
        private readonly IAlglibAPI alglibAPI;

        private HyperbolicUnevenFittingAlglib(IAlglibAPI alglibAPI, double[][] inputs, double[] inputStdDevs, double[] outputs, int stepSize) {
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.StepSize = stepSize;
            this.alglibAPI = alglibAPI;
            this.Weights = inputStdDevs.Select(sd => 1.0d / Math.Max(Math.Abs(sd), 1e-6)).ToArray();
        }

        public static HyperbolicUnevenFittingAlglib Create(IAlglibAPI alglibAPI, ICollection<ScatterErrorPoint> points, int stepSize, bool useWeights) {
            var nonzeroPoints = points.Where((dp) => dp.Y >= 0.1).ToList();
            var inputs = nonzeroPoints.Select(dp => new double[] { dp.X }).ToArray();
            double[] inputStdDevs;
            if (useWeights) {
                inputStdDevs = nonzeroPoints.Select(dp => dp.ErrorY).ToArray();
            } else {
                inputStdDevs = new double[nonzeroPoints.Count];
                for (int i = 0; i < inputStdDevs.Length; ++i) {
                    inputStdDevs[i] = 1.0d;
                }
            }
            var outputs = nonzeroPoints.Select(dp => dp.Y).ToArray();
            return new HyperbolicUnevenFittingAlglib(alglibAPI, inputs, inputStdDevs, outputs, stepSize);
        }

        private Func<double, double> GetFittingForParameters(double[] parameters) {
            var x0 = parameters[0];
            var y0 = parameters[1];
            var a = parameters[2];
            var b = parameters[3];
            var c = parameters[4];
            return x => {
                var t = (x0 - x) / this.StepSize;
                t = Math.Max(0.0d, Math.Min(1.0d, t));
                var leftSide = t * a / b * Math.Sqrt((x - x0) * (x - x0) + b * b);
                var rightSide = (1.0d - t) * a / c * Math.Sqrt((x - x0) * (x - x0) + c * c);
                return leftSide + rightSide + y0;
            };
        }

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            var fitting = GetFittingForParameters(parameters);
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var input = this.Inputs[i][0];
                var observedValue = this.Outputs[i];
                var estimatedValue = fitting(input);
                var weight = this.Weights[i];
                fi[i] = weight * (estimatedValue - observedValue);
            }
        }

        public virtual void FitResidualsJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            throw new NotImplementedException();
        }

        public override bool Solve() {
            if (Inputs.Length == 0) {
                return false;
            }

            var lowestInputX = Inputs.Min(i => i[0]);
            var highestInputX = Inputs.Max(i => i[0]);

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
                var initialGuess = new double[] { initialX0, initialY0, initialA, initialB, initialB };
                var lowerBounds = new double[] { lowestInputX, -lowestOutput, 0.001d, 0.001d, 0.001d };
                var upperBounds = new double[] { highestInputX, lowestOutput, lowestOutput * 2, double.PositiveInfinity, double.PositiveInfinity };
                var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
                var scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1, 1 };
                var solution = new double[6];
                var tolerance = 1E-6;
                var maxIterations = 1000;
                const double deltaForNumericIntegration = 1E-6;
                this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);

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

                if (rep.terminationtype < 0 && rep.terminationtype != -5) {
                    string reason;
                    if (rep.terminationtype == -8) {
                        reason = "optimizer detected NAN/INF values in the function itself";
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
                var c = solution[4];

                FormattableString expression = $"y = {{ 0, ({x0:0.###} - x) / {StepSize}, 1 }} * {a:0.###}/{b:0.###} * √((x - {x0:0.###})² + {b:0.###}²) + {{ 0, (x - {x0:0.###}) / {StepSize}, 1 }} * {a:0.###}/{c:0.###} * √((x - {x0:0.###})² + {c:0.###}²) + {y0:0.###}";
                Expression = expression.ToString(CultureInfo.InvariantCulture);
                Fitting = GetFittingForParameters(solution);
                Minimum = new DataPoint(x0, a + y0);

                var transformed = new double[Inputs.Length];
                var rSquared = new RSquaredLoss(Inputs.Length, Outputs);
                rSquared.Weights = Weights;
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