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
    /// Legacy asymmetric ("uneven") hyperbolic fit. Blends a left and right hyperbola with a hard linear
    /// ramp t = clamp((x0 − x)/StepSize, 0, 1):
    ///   y = t·(a/b)·√((x−x0)² + b²) + (1−t)·(a/c)·√((x−x0)² + c²) + y0
    /// Five parameters {x0, y0, a, b, c}. The blend is only C⁰ (kinked at x0 and x0−StepSize), so this model
    /// uses numerical differentiation and does not estimate a best-focus standard error. Retained for
    /// backward compatibility; prefer <see cref="TiltedHyperbolicFittingAlglib"/> or
    /// <see cref="SmoothBlendHyperbolicFittingAlglib"/>.
    /// </summary>
    public class HyperbolicUnevenFittingAlglib : AlglibHyperbolicFitting {

        private HyperbolicUnevenFittingAlglib(IAlglibAPI alglibAPI, double[][] inputs, double[] inputStdDevs, double[] outputs, int stepSize) {
            if (stepSize == 0) {
                throw new ArgumentException("StepSize cannot be zero");
            }

            this.alglibAPI = alglibAPI;
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.StepSize = stepSize;
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
                Array.Fill(inputStdDevs, 1.0d);
            }
            var outputs = nonzeroPoints.Select(dp => dp.Y).ToArray();
            return new HyperbolicUnevenFittingAlglib(alglibAPI, inputs, inputStdDevs, outputs, stepSize);
        }

        protected override int ParameterCount => 5;

        // C⁰ blend: keep numerical differentiation (kinks make an analytic Jacobian unreliable) and skip the
        // covariance/standard-error estimate, exactly as this model behaved before the base refactor.
        protected override bool UseJacobian => false;

        protected override bool SupportsCovariance => false;

        protected override double ModelValue(double[] p, double x) {
            var x0 = p[0];
            var y0 = p[1];
            var a = p[2];
            var b = p[3];
            var c = p[4];
            var t = Math.Clamp((x0 - x) / this.StepSize, 0.0d, 1.0d);
            var leftSide = t * a / b * Math.Sqrt((x - x0) * (x - x0) + b * b);
            var rightSide = (1.0d - t) * a / c * Math.Sqrt((x - x0) * (x - x0) + c * c);
            return leftSide + rightSide + y0;
        }

        protected override DataPoint ComputeMinimum(double[] p) {
            return new DataPoint(p[0], p[2] + p[1]);
        }

        protected override string FormatExpression(double[] p) {
            var x0 = p[0];
            var y0 = p[1];
            var a = p[2];
            var b = p[3];
            var c = p[4];
            FormattableString expression = $"y = min(max(0, ({x0:0.###} - x) / {StepSize}), 1) * {a:0.###}/{b:0.###} * √((x - {x0:0.###})² + {b:0.###}²) + (1 - min(max(0, ({x0:0.###} - x) / {StepSize}), 1)) * {a:0.###}/{c:0.###} * √((x - {x0:0.###})² + {c:0.###}²) + {y0:0.###}";
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

            initialGuess = new double[] { initialX0, initialY0, initialA, initialB, initialB };
            lowerBounds = new double[] { lowestInputX, -lowestOutput, 0.001d, 0.001d, 0.001d };
            upperBounds = new double[] { highestInputX, lowestOutput, lowestOutput * 2, double.PositiveInfinity, double.PositiveInfinity };
            var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
            scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1, 1 };
            return true;
        }
    }
}
