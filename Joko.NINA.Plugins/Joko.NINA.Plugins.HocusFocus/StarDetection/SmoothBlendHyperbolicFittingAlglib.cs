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
    /// Asymmetric focus-curve model that blends a left and a right hyperbola with a smooth (C∞) logistic
    /// transition centred on the vertex:
    ///   y = y0 + t·(a/b)·√((x−x0)² + b²) + (1−t)·(a/c)·√((x−x0)² + c²),   t = 1 / (1 + exp((x−x0)/w))
    /// Five parameters {x0, y0, a, b, c}. Unlike the Uneven Blend fit's hard clamp this transition is
    /// differentiable everywhere (so it has an analytic Jacobian and no kink artifacts), and the transition
    /// width w is derived from the data spacing rather than fitted — fitting w is weakly identifiable and
    /// destabilizes the best-focus estimate. Reduces to the symmetric hyperbola when b = c.
    /// </summary>
    public class SmoothBlendHyperbolicFittingAlglib : AlglibHyperbolicFitting {

        // Logistic half-width as a fraction of the median sample spacing. The transition occupies roughly a
        // couple of samples around the vertex, matching the (former) one-step ramp of the Uneven Blend model.
        private const double WidthSpacingFraction = 0.25;

        private double blendWidth;
        private double minSearchLo;
        private double minSearchHi;

        private SmoothBlendHyperbolicFittingAlglib(IAlglibAPI alglibAPI, double[][] inputs, double[] inputStdDevs, double[] outputs) {
            this.alglibAPI = alglibAPI;
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.Weights = inputStdDevs.Select(sd => 1.0d / Math.Max(Math.Abs(sd), 1e-6)).ToArray();
        }

        public static SmoothBlendHyperbolicFittingAlglib Create(IAlglibAPI alglibAPI, ICollection<ScatterErrorPoint> points, bool useWeights) {
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
            return new SmoothBlendHyperbolicFittingAlglib(alglibAPI, inputs, inputStdDevs, outputs);
        }

        protected override int ParameterCount => 5;

        private static double BlendT(double u, double w) {
            var z = u / w;
            if (z > 40.0) return 0.0;
            if (z < -40.0) return 1.0;
            return 1.0 / (1.0 + Math.Exp(z));
        }

        protected override double ModelValue(double[] p, double x) {
            var x0 = p[0];
            var y0 = p[1];
            var a = p[2];
            var b = p[3];
            var c = p[4];
            var u = x - x0;
            var t = BlendT(u, blendWidth);
            var left = t * a / b * Math.Sqrt(u * u + b * b);
            var right = (1.0 - t) * a / c * Math.Sqrt(u * u + c * c);
            return y0 + left + right;
        }

        protected override void ModelGradient(double[] p, double x, double[] grad) {
            var x0 = p[0];
            var a = p[2];
            var b = p[3];
            var c = p[4];
            var u = x - x0;
            var u2 = u * u;
            var rb = Math.Sqrt(u2 + b * b);
            var rc = Math.Sqrt(u2 + c * c);
            var t = BlendT(u, blendWidth);
            var oneMinusT = 1.0 - t;
            var gb = a / b * rb;
            var gc = a / c * rc;
            var dtdu = -t * oneMinusT / blendWidth;

            // ∂f/∂u, then ∂f/∂x0 = −∂f/∂u (f depends on x0 only through u)
            var dfdu = t * (a / b) * (u / rb) + oneMinusT * (a / c) * (u / rc) + dtdu * (gb - gc);
            grad[0] = -dfdu;                                            // ∂/∂x0
            grad[1] = 1.0d;                                            // ∂/∂y0
            grad[2] = t * rb / b + oneMinusT * rc / c;                 // ∂/∂a
            grad[3] = t * (-a * u2 / (b * b * rb));                    // ∂/∂b
            grad[4] = oneMinusT * (-a * u2 / (c * c * rc));            // ∂/∂c
        }

        /// <summary>Deterministic golden-section search for the curve minimum over the sampled x range. The
        /// blended curve has a single trough on real focus data, so a bracketed 1-D minimization is robust and
        /// needs no derivative.</summary>
        private double MinimumX(double[] p) {
            const double invPhi = 0.6180339887498949; // (√5 − 1)/2
            double a = minSearchLo, b = minSearchHi;
            double c = b - invPhi * (b - a);
            double d = a + invPhi * (b - a);
            double fc = ModelValue(p, c);
            double fd = ModelValue(p, d);
            for (int i = 0; i < 100 && (b - a) > 1e-7 * Math.Max(1.0, Math.Abs(b)); ++i) {
                if (fc < fd) {
                    b = d;
                    d = c;
                    fd = fc;
                    c = b - invPhi * (b - a);
                    fc = ModelValue(p, c);
                } else {
                    a = c;
                    c = d;
                    fc = fd;
                    d = a + invPhi * (b - a);
                    fd = ModelValue(p, d);
                }
            }
            return 0.5 * (a + b);
        }

        protected override DataPoint ComputeMinimum(double[] p) {
            var xMin = MinimumX(p);
            return new DataPoint(xMin, ModelValue(p, xMin));
        }

        protected override void MinimumPositionGradient(double[] p, double[] grad) {
            // No closed form for the blended minimum; propagate var(x_min) via central finite differences of
            // the golden-section minimizer w.r.t. each parameter (delta method). Guarded by the base.
            var baseParams = (double[])p.Clone();
            for (int j = 0; j < ParameterCount; ++j) {
                var h = Math.Max(1e-3 * Math.Abs(baseParams[j]), 1e-4);
                baseParams[j] = p[j] + h;
                var plus = MinimumX(baseParams);
                baseParams[j] = p[j] - h;
                var minus = MinimumX(baseParams);
                baseParams[j] = p[j];
                grad[j] = (plus - minus) / (2.0 * h);
            }
        }

        protected override string FormatExpression(double[] p) {
            var x0 = p[0];
            var y0 = p[1];
            var a = p[2];
            var b = p[3];
            var c = p[4];
            FormattableString expression = $"y = σ(({x0:0.###} - x)/{blendWidth:0.###}) * {a:0.###}/{b:0.###} * √((x - {x0:0.###})² + {b:0.###}²) + (1 - σ(({x0:0.###} - x)/{blendWidth:0.###})) * {a:0.###}/{c:0.###} * √((x - {x0:0.###})² + {c:0.###}²) + {y0:0.###}";
            return expression.ToString(CultureInfo.InvariantCulture);
        }

        protected override bool TryComputeInitialState(out double[] initialGuess, out double[] lowerBounds, out double[] upperBounds, out double[] scale) {
            initialGuess = lowerBounds = upperBounds = scale = null;

            var xs = Inputs.Select(i => i[0]).OrderBy(v => v).ToArray();
            minSearchLo = xs[0];
            minSearchHi = xs[xs.Length - 1];

            // Median consecutive spacing → logistic half-width. Keeps the transition ~a couple of samples wide.
            var diffs = new List<double>();
            for (int i = 1; i < xs.Length; ++i) {
                var d = xs[i] - xs[i - 1];
                if (d > 0) {
                    diffs.Add(d);
                }
            }
            var spacing = diffs.Count > 0 ? diffs.OrderBy(v => v).ElementAt(diffs.Count / 2) : Math.Max(minSearchHi - minSearchLo, 1.0);
            blendWidth = Math.Max(WidthSpacingFraction * spacing, 1e-6);

            var lowestInputX = minSearchLo;
            var highestInputX = minSearchHi;
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

            initialGuess = new double[] { initialX0, initialY0, initialA, initialB, initialB };
            lowerBounds = new double[] { lowestInputX, -lowestOutput, 0.001d, 0.001d, 0.001d };
            upperBounds = new double[] { highestInputX, lowestOutput, lowestOutput * 2, double.PositiveInfinity, double.PositiveInfinity };
            var positionScale = lowestOutput > 0 ? lowestInput / lowestOutput : lowestInput;
            scale = new double[] { Math.Max(1.0, positionScale), 1, 1, 1, 1 };
            return true;
        }
    }
}
