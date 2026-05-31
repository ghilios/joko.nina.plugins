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
    ///   y = y0 + (a/b)·√((x − x0)² + b²) + s·(x − x0),  with s = σ·(a/b)
    /// Five parameters {x0, y0, a, b, σ}. The skew is parametrized RELATIVE to the asymptotic slope k = a/b:
    /// the asymptotes have slopes k·(1 ± σ), so the curve is steeper on one side than the other and σ = 0
    /// recovers the symmetric hyperbola. Because σ is boxed to |σ| ≤ <see cref="MaxRelativeSkew"/> &lt; 1, the
    /// condition |s| &lt; k that a single finite minimum requires holds STRUCTURALLY for any a, b the optimizer
    /// picks — unlike an absolute skew s whose static box (built from the initial a/b) can be violated when the
    /// optimizer inflates b on a shallow/near-linear curve, sending the closed-form minimum off to ±1e14.
    /// Unlike the legacy uneven blend this is one differentiable equation with an analytic Jacobian, and the
    /// best-focus position has a closed form (shifted off x0 by the skew).
    /// </summary>
    public class TiltedHyperbolicFittingAlglib : AlglibHyperbolicFitting {

        // Relative skew bound: |σ| ≤ 0.9 keeps the radical √(1−σ²) ≥ 0.436 (a single, finite minimum) while still
        // allowing a strongly asymmetric curve (asymptote slope ratio up to 19:1).
        private const double MaxRelativeSkew = 0.9d;

        // Cap on b (the hyperbola's focus-depth scale) as a multiple of the sampled x-span. A hyperbola with
        // b ≫ span is linear across the data — the degenerate near-linear regime that let the skewed minimum
        // run away. Capping b also bounds the minimum offset |u*| = |σ|·b/√(1−σ²) ≤ ~2.06·b to a few spans.
        private const double MaxBToSpanRatio = 2.0d;

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
            var sigma = p[4];
            var u = x - x0;
            var k = a / b;
            return y0 + k * (Math.Sqrt(u * u + b * b) + sigma * u);
        }

        protected override void ModelGradient(double[] p, double x, double[] grad) {
            var x0 = p[0];
            var a = p[2];
            var b = p[3];
            var sigma = p[4];
            var u = x - x0;
            var u2 = u * u;
            var b2 = b * b;
            var r = Math.Sqrt(u2 + b2);
            var k = a / b;

            grad[0] = -k * (u / r + sigma);             // ∂/∂x0
            grad[1] = 1.0d;                              // ∂/∂y0
            grad[2] = (r + sigma * u) / b;               // ∂/∂a
            grad[3] = -a / b2 * (u2 / r + sigma * u);    // ∂/∂b
            grad[4] = k * u;                             // ∂/∂σ
        }

        /// <summary>Best-focus offset u* = x_min − x0 = −σ·b / √(1−σ²). Bounded by the |σ| box, so no radical
        /// floor is needed (√(1−σ²) ≥ √(1−0.9²) = 0.436). Also returns √(1−σ²) for reuse by the covariance gradient.</summary>
        private static double MinimumOffset(double[] p, out double d) {
            var b = p[3];
            var sigma = p[4];
            d = Math.Sqrt(Math.Max(1.0 - sigma * sigma, 1e-12));
            return -sigma * b / d;
        }

        protected override DataPoint ComputeMinimum(double[] p) {
            var uMin = MinimumOffset(p, out _);
            var xMin = p[0] + uMin;
            return new DataPoint(xMin, ModelValue(p, xMin));
        }

        protected override void MinimumPositionGradient(double[] p, double[] grad) {
            // x_min = x0 − σ·b / D, D = √(1−σ²). Propagate var(x_min) by the delta method. Note x_min no longer
            // depends on a, which decouples the best-focus uncertainty from the curve's depth parameter.
            MinimumOffset(p, out var d);
            var b = p[3];
            var sigma = p[4];
            var d3 = d * d * d;
            grad[0] = 1.0;                  // ∂x_min/∂x0
            grad[1] = 0.0;                  // ∂x_min/∂y0
            grad[2] = 0.0;                  // ∂x_min/∂a
            grad[3] = -sigma / d;           // ∂x_min/∂b
            grad[4] = -b / d3;              // ∂x_min/∂σ  (d/dσ[σ/√(1−σ²)] = 1/(1−σ²)^{3/2})
        }

        protected override string FormatExpression(double[] p) {
            // Report the equivalent absolute skew s = σ·(a/b) so the printed slope term matches the curve.
            var s = p[4] * p[2] / p[3];
            FormattableString expression = $"y = {p[2]:0.###}/{p[3]:0.###} * √((x - {p[0]:0.###})² + {p[3]:0.###}²) + {s:0.#####}·(x - {p[0]:0.###}) + {p[1]:0.###}";
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

            // Cap b at a multiple of the sampled x-span so the hyperbola cannot degenerate into a line across the
            // data (which, combined with skew, is what let the minimum run away). Clamp the seed into the box.
            var xSpan = highestInputX - lowestInputX;
            var bUpperBound = MaxBToSpanRatio * xSpan;
            if (!(bUpperBound > 0.001d)) {
                return false;
            }
            initialB = Math.Min(Math.Max(initialB, 0.001d), bUpperBound);

            // The skew is now relative to the asymptotic slope (s = σ·a/b), so its box is a slope-INDEPENDENT
            // constant in [−0.9, 0.9] that holds for any a, b the optimizer reaches. Start unskewed (σ = 0).
            initialGuess = new double[] { initialX0, initialY0, initialA, initialB, 0.0d };
            lowerBounds = new double[] { lowestInputX, -lowestOutput, 0.001d, 0.001d, -MaxRelativeSkew };
            upperBounds = new double[] { highestInputX, lowestOutput, lowestOutput * 2, bUpperBound, MaxRelativeSkew };
            var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
            scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1, 1 };
            return true;
        }
    }
}
