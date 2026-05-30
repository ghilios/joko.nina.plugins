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
    /// Asymmetric "tilted" hyperbolic fit: a single smooth (C∞) hyperbola plus a linear skew term:
    ///   y = y0 + (a/b)·√((x − x0)² + b²) + s·(x − x0)
    /// Five parameters {x0, y0, a, b, s}. The skew s tilts the two asymptotes to slopes a/b ± s (|s| &lt; a/b),
    /// so the curve is steeper on one side than the other; s = 0 recovers the symmetric hyperbola. Unlike the
    /// legacy uneven blend this is one differentiable equation with an analytic Jacobian, and the best-focus
    /// position has a closed form (it is shifted off x0 by the skew).
    /// </summary>
    public class TiltedHyperbolicFittingAlglib : AlglibHyperbolicFitting {

        // When a/b and |s| are nearly equal the closed-form minimum blows up (√(k²−s²) → 0). Floor the radical
        // at this fraction of k so the model, its minimum, and its derivatives stay finite if the optimizer
        // transiently pushes |s| toward a/b (the static box on s only uses the initial a/b).
        private const double SkewRadicalFloorFraction = 0.05;

        private TiltedHyperbolicFittingAlglib(IAlglibAPI alglibAPI, double[][] inputs, double[] inputStdDevs, double[] outputs) {
            this.alglibAPI = alglibAPI;
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.Weights = inputStdDevs.Select(sd => 1.0d / Math.Max(Math.Abs(sd), 1e-6)).ToArray();
        }

        public static TiltedHyperbolicFittingAlglib Create(IAlglibAPI alglibAPI, ICollection<ScatterErrorPoint> points, bool useWeights) {
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
            return new TiltedHyperbolicFittingAlglib(alglibAPI, inputs, inputStdDevs, outputs);
        }

        protected override int ParameterCount => 5;

        protected override double ModelValue(double[] p, double x) {
            var x0 = p[0];
            var y0 = p[1];
            var a = p[2];
            var b = p[3];
            var s = p[4];
            var u = x - x0;
            return y0 + a / b * Math.Sqrt(u * u + b * b) + s * u;
        }

        protected override void ModelGradient(double[] p, double x, double[] grad) {
            var x0 = p[0];
            var a = p[2];
            var b = p[3];
            var s = p[4];
            var u = x - x0;
            var u2 = u * u;
            var b2 = b * b;
            var r = Math.Sqrt(u2 + b2);
            var k = a / b;

            grad[0] = -(k * u / r + s);          // ∂/∂x0
            grad[1] = 1.0d;                       // ∂/∂y0
            grad[2] = r / b;                       // ∂/∂a
            grad[3] = -a * u2 / (b2 * r);          // ∂/∂b
            grad[4] = u;                           // ∂/∂s
        }

        /// <summary>Best-focus offset u* = x_min − x0 = −s·b / √(k²−s²), k = a/b, with the radical floored for
        /// numerical safety. Also returns k and the (floored) radical for reuse by the covariance gradient.</summary>
        private static double MinimumOffset(double[] p, out double k, out double d) {
            var a = p[2];
            var b = p[3];
            var s = p[4];
            k = a / b;
            var floor = SkewRadicalFloorFraction * Math.Abs(k);
            var radicand = Math.Max(k * k - s * s, floor * floor);
            d = Math.Sqrt(radicand);
            return -s * b / d;
        }

        protected override DataPoint ComputeMinimum(double[] p) {
            var uMin = MinimumOffset(p, out _, out _);
            var xMin = p[0] + uMin;
            return new DataPoint(xMin, ModelValue(p, xMin));
        }

        protected override void MinimumPositionGradient(double[] p, double[] grad) {
            // x_min = x0 − s·b / D, D = √(k²−s²), k = a/b. Propagate var(x_min) by the delta method.
            MinimumOffset(p, out var k, out var d);
            var b = p[3];
            var s = p[4];
            var d3 = d * d * d;
            grad[0] = 1.0;                              // ∂x_min/∂x0
            grad[1] = 0.0;                              // ∂x_min/∂y0
            grad[2] = s * k / d3;                       // ∂x_min/∂a
            grad[3] = -s * (2.0 * k * k - s * s) / d3;  // ∂x_min/∂b
            grad[4] = -b * k * k / d3;                  // ∂x_min/∂s
        }

        protected override string FormatExpression(double[] p) {
            FormattableString expression = $"y = {p[2]:0.###}/{p[3]:0.###} * √((x - {p[0]:0.###})² + {p[3]:0.###}²) + {p[4]:0.#####}·(x - {p[0]:0.###}) + {p[1]:0.###}";
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
            }

            var initialA = lowestOutput;
            var initialA2 = initialA * initialA;
            var highestOutput2 = highestOutput * highestOutput;
            var inputDelta = highestInput - lowestInput;
            var initialB = Math.Sqrt(inputDelta * inputDelta * initialA2 / (highestOutput2 - initialA2));
            var initialX0 = lowestInput;
            var initialY0 = 0.0d;

            if (double.IsNaN(initialA) || double.IsNaN(initialB) || initialA == 0 || initialB == 0 || inputDelta == 0) {
                return false;
            }

            // Asymptotic slope of the seed hyperbola; the skew |s| must stay below it for the curve to keep a
            // single minimum. Bound s to 0.9·k0 and start unskewed.
            var k0 = initialA / initialB;
            var sBound = 0.9 * k0;

            initialGuess = new double[] { initialX0, initialY0, initialA, initialB, 0.0d };
            lowerBounds = new double[] { lowestInputX, -lowestOutput, 0.001d, 0.001d, -sBound };
            upperBounds = new double[] { highestInputX, lowestOutput, lowestOutput * 2, double.PositiveInfinity, sBound };
            var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
            scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1, Math.Max(k0, 1e-6) };
            return true;
        }
    }
}
