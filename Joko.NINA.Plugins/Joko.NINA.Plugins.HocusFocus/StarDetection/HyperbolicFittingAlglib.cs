#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Math.Optimization.Losses;
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

        private HyperbolicFittingAlglib(double[][] inputs, double[] outputs) {
            this.Inputs = inputs;
            this.Outputs = outputs;
        }

        public static HyperbolicFittingAlglib Create(ICollection<ScatterErrorPoint> points) {
            var nonzeroPoints = points.Where((dp) => dp.Y >= 0.1).ToList();
            var inputs = nonzeroPoints.Select(dp => new double[] { dp.X }).ToArray();
            var outputs = nonzeroPoints.Select(dp => dp.Y).ToArray();
            return new HyperbolicFittingAlglib(inputs, outputs);
        }

        private Func<double, double> GetFittingForParameters(double[] parameters) {
            var x0 = parameters[0];
            var y0 = parameters[1];
            var a = parameters[2];
            var b = parameters[3];
            return x => a / b * Math.Sqrt((x - x0) * (x - x0) + b * b) + y0;
        }

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            var fitting = GetFittingForParameters(parameters);
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var input = this.Inputs[i][0];
                var observedValue = this.Outputs[i];
                var estimatedValue = fitting(input);
                fi[i] = observedValue - estimatedValue;
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
                var initialGuess = new double[] { initialX0, initialY0, initialA, initialB };
                var lowerBounds = new double[] { 0.0d, double.NegativeInfinity, 0.001d, 0.001d };
                var upperBounds = new double[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
                var scale = new double[] { lowestInput / lowestOutput, 1, 1, 1 };
                var solution = new double[4];
                var tolerance = 1E-6;
                var maxIterations = 0; // Keep going until the solution is found
                const double deltaForNumericIntegration = 1E-4;
                alglib.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);

                alglib.minlmsetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                alglib.minlmsetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                alglib.minlmsetscale(state, scale);

                // Perform the optimization
                alglib.minlmoptimize(state, this.FitResiduals, null, null);

                alglib.minlmresults(state, out solution, out rep);
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

                var x0 = solution[0];
                var y0 = solution[1];
                var a = solution[2];
                var b = solution[3];

                FormattableString expression = $"y = {a:0.###}/{b:0.###} * sqrt((x - {x0:0.###})² + {b:0.###}²) + {y0:0.###}";
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
                    alglib.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    alglib.deallocateimmediately(ref rep);
                }
            }
        }

        public override string ToString() {
            return $"{Expression}";
        }
    }
}