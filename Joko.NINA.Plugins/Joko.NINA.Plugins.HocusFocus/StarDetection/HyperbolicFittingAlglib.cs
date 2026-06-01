#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Symmetric hyperbolic auto-focus fit: y = (a/b)·√((x−x0)² + b²) + y0. Four parameters
    /// {x0, y0, a, b}; the minimum (best focus) is at x0 with value a + y0.
    /// </summary>
    public class HyperbolicFittingAlglib : AlglibHyperbolicFitting {

        private HyperbolicFittingAlglib(IAlglibAPI alglibAPI, double[][] inputs, double[] inputStdDevs, double[] outputs) {
            this.alglibAPI = alglibAPI;
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.Weights = inputStdDevs.Select(sd => 1.0d / Math.Max(Math.Abs(sd), 1e-6)).ToArray();
        }

        public static HyperbolicFittingAlglib Create(IAlglibAPI alglibAPI, ICollection<ScatterErrorPoint> points, bool useWeights) {
            var nonzeroPoints = points.Where((dp) => dp.Y >= 0.1).ToList();
            var inputs = nonzeroPoints.Select(dp => new double[] { dp.X }).ToArray();
            double[] inputStdDevs;
            if (useWeights) {
                inputStdDevs = nonzeroPoints.Select(dp => dp.ErrorY).ToArray();
            } else {
                inputStdDevs = new double[nonzeroPoints.Count];
                Array.Fill(inputStdDevs, 1.0d);
            }
            var outputs = nonzeroPoints.Select(dp => dp.Y).ToArray();
            return new HyperbolicFittingAlglib(alglibAPI, inputs, inputStdDevs, outputs);
        }

        protected override int ParameterCount => 4;

        protected override double ModelValue(double[] p, double x) {
            var x0 = p[0];
            var y0 = p[1];
            var a = p[2];
            var b = p[3];
            return a / b * Math.Sqrt((x - x0) * (x - x0) + b * b) + y0;
        }

        protected override void ModelGradient(double[] p, double x, double[] grad) {
            var x0 = p[0];
            var a = p[2];
            var b = p[3];
            var XPrime = x - x0;
            var XPrime2 = XPrime * XPrime;
            var B2 = b * b;
            var sqrtTerm = Math.Sqrt(B2 + XPrime2);

            grad[0] = -a * XPrime / (b * sqrtTerm);     // ∂/∂x0
            grad[1] = 1.0d;                             // ∂/∂y0
            grad[2] = sqrtTerm / b;                     // ∂/∂a
            grad[3] = -a * XPrime2 / (B2 * sqrtTerm);   // ∂/∂b
        }

        protected override DataPoint ComputeMinimum(double[] p) {
            return new DataPoint(p[0], p[2] + p[1]);
        }

        protected override string FormatExpression(double[] p) {
            FormattableString expression = $"y = {p[2]:0.###}/{p[3]:0.###} * √((x - {p[0]:0.###})² + {p[3]:0.###}²) + {p[1]:0.###}";
            return expression.ToString(CultureInfo.InvariantCulture);
        }

        protected override bool TryComputeInitialState(out double[] initialGuess, out double[] lowerBounds, out double[] upperBounds, out double[] scale) {
            initialGuess = lowerBounds = upperBounds = scale = null;

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
                return false;
            }

            initialGuess = new double[] { initialX0, initialY0, initialA, initialB };
            lowerBounds = new double[] { lowestInputX, -lowestOutput, 0.001d, 0.001d };
            upperBounds = new double[] { highestInputX, lowestOutput, lowestOutput * 2, double.PositiveInfinity };
            var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
            scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1 };
            return true;
        }
    }
}
