#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public static class MoffatShared {

        public static double SigmaToFWHM(double beta, double sigma) {
            return sigma * 2 * Math.Sqrt(Math.Pow(2, 1 / beta) - 1);
        }
    }

    public class MoffatPSFAlglibType : PSFModelTypeAlglibBase {
        public double Beta { get; protected set; }
        private readonly bool pixelIntegration;

        public MoffatPSFAlglibType(IAlglibAPI alglibAPI, double beta, double[][] inputs, double[] outputs, double centroidBrightness, double starDetectionBackground, Rect starBoundingBox, double pixelScale, bool pixelIntegration = false) :
            base(alglibAPI: alglibAPI, centroidBrightness: centroidBrightness, starDetectionBackground: starDetectionBackground, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.Beta = beta;
            this.pixelIntegration = pixelIntegration;
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Moffat_40;

        public override bool UseJacobian => !pixelIntegration;

        /// <summary>
        /// Evaluates the Moffat profile at a single (possibly sub-pixel) location.
        /// </summary>
        private double MoffatPoint(double[] parameters, double px, double py) {
            var A = parameters[0];
            var x0 = parameters[2];
            var y0 = parameters[3];
            var U = parameters[4];
            var V = parameters[5];
            var T = parameters[6];

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            var X = (px - x0) * cosT + (py - y0) * sinT;
            var Y = -(px - x0) * sinT + (py - y0) * cosT;
            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var V2 = V * V;
            var D = 1 + X2 / U2 + Y2 / V2;
            return A / Math.Pow(D, this.Beta);
        }

        // G(x,y; A,B,x0,y0,sigx,sigy,theta)
        // Background level is normalized already to 0
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Moffate elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
        public override double Value(double[] parameters, double[] input) {
            var B = parameters[1];
            var x = input[0];
            var y = input[1];

            if (pixelIntegration) {
                // 2×2 sub-pixel sampling: sample at offsets ±0.25 from the pixel centre.
                // Average of 4 samples approximates the pixel-area integral.
                const double off = 0.25;
                var sum =
                    MoffatPoint(parameters, x - off, y - off) +
                    MoffatPoint(parameters, x + off, y - off) +
                    MoffatPoint(parameters, x - off, y + off) +
                    MoffatPoint(parameters, x + off, y + off);
                return B + sum * 0.25;
            }

            var x0 = parameters[2];
            var y0 = parameters[3];
            var U = parameters[4];
            var V = parameters[5];
            var T = parameters[6];
            // x0 = X0 (X offset)
            // y0 = Y0 (Y offset)
            // U = sigmaX
            // V = sigmaY
            // T = theta

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            var X = (x - x0) * cosT + (y - y0) * sinT;
            var Y = -(x - x0) * sinT + (y - y0) * cosT;
            // X = xPrime = x * cos(T) + y * sin(T)
            // Y = yPrime = -x * sin(T) + y * cos(T)

            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var V2 = V * V;

            //         X^2   Y^2
            // D = 1 + --- + ---
            //         U^2   V^2
            var D = 1 + X2 / U2 + Y2 / V2;
            var Beta = this.Beta;

            // O = B + A / D^Beta
            return B + parameters[0] / Math.Pow(D, Beta);
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            // Wolfram-Alpha solutions for the partial derivatives
            //  a = A
            //  b = Beta
            //  c = U = sigmaX
            //  d = V = sigmaY
            //  f = x0
            //  g = y0
            //  h = b
            //  t = theta
            var A = parameters[0];
            var B = parameters[1];
            var x = input[0];
            var y = input[1];
            var x0 = parameters[2];
            var y0 = parameters[3];
            var U = parameters[4];
            var V = parameters[5];
            var T = parameters[6];

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            var X = (x - x0) * cosT + (y - y0) * sinT;
            var Y = -(x - x0) * sinT + (y - y0) * cosT;
            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var V2 = V * V;
            var U3 = U2 * U;
            var V3 = V2 * V;

            // d/da
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+Divide%5Ba%2CPower%5B1+%2B+Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bd%2C2%5D%5D%2Cb%5D%5D+with+respect+to+a
            var den_common = X2 / U2 + Y2 / V2 + 1.0;
            var d_da = Math.Pow(den_common, -this.Beta);

            // d/df
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+Divide%5Ba%2CPower%5B1+%2B+Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bd%2C2%5D%5D%2Cb%5D%5D+with+respect+to+f
            var d_df_part1 = -A * this.Beta * ((2.0 * sinT * Y / V2) - (2.0 * cosT * X / U2));
            var d_df_part2 = Math.Pow(den_common, -this.Beta - 1);
            var d_df = d_df_part1 * d_df_part2;

            // d/dg
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+Divide%5Ba%2CPower%5B1+%2B+Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bd%2C2%5D%5D%2Cb%5D%5D+with+respect+to+g
            var d_dg_part1 = -A * this.Beta * ((-2.0 * sinT * X / U2) - (2.0 * cosT * Y / V2));
            var d_dg_part2 = d_df_part2;
            var d_dg = d_dg_part1 * d_dg_part2;

            // d/dt
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+Divide%5Ba%2CPower%5B1+%2B+Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bd%2C2%5D%5D%2Cb%5D%5D+with+respect+to+t
            var YX_2 = 2.0 * Y * X;
            var d_dt_part1 = -A * this.Beta * ((YX_2 / U2) - (YX_2 / V2));
            var d_dt_part2 = d_df_part2;
            var d_dt = d_dt_part1 * d_dt_part2;

            // d/dc
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+Divide%5Ba%2CPower%5B1+%2B+Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bd%2C2%5D%5D%2Cb%5D%5D+with+respect+to+c
            var AB_2 = 2.0 * A * this.Beta;
            var d_dc_part1 = (AB_2 / U3) * X2;
            var d_dc_part2 = d_df_part2;
            var d_dc = d_dc_part1 * d_dc_part2;

            // d/dd
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+Divide%5Ba%2CPower%5B1+%2B+Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2CPower%5Bd%2C2%5D%5D%2Cb%5D%5D+with+respect+to+d
            var d_dd_part1 = (AB_2 / V3) * Y2;
            var d_dd_part2 = d_df_part2;
            var d_dd = d_dd_part1 * d_dd_part2;

            result[0] = d_da;
            result[1] = 1;
            result[2] = d_df;
            result[3] = d_dg;
            result[4] = d_dc;
            result[5] = d_dd;
            result[6] = d_dt;
        }

        public override double SigmaToFWHM(double sigma) {
            return MoffatShared.SigmaToFWHM(this.Beta, sigma);
        }
    }

    /// <summary>
    /// Moffat PSF where β is a free Levenberg-Marquardt parameter (8th parameter, index 7).
    /// Parameter layout: { A, B, x0, y0, σx, σy, θ, β }
    /// Bounds for β: [1.0, 10.0], initial = 4.0.
    /// Jacobian is computed numerically (UseJacobian = false) because the analytic derivative
    /// with respect to β involves log(D) · D^β which adds complexity and is not worth maintaining.
    /// After Solve/SolveIRLS, <see cref="MoffatPSFAlglibType.Beta"/> reflects the fitted β.
    /// </summary>
    public class FittableMoffatPSFAlglibType : MoffatPSFAlglibType {

        public FittableMoffatPSFAlglibType(IAlglibAPI alglibAPI, double[][] inputs, double[] outputs, double centroidBrightness, double starDetectionBackground, Rect starBoundingBox, double pixelScale) :
            base(alglibAPI: alglibAPI, beta: 4.0, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starDetectionBackground: starDetectionBackground, pixelScale: pixelScale, starBoundingBox: starBoundingBox, pixelIntegration: false) {
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Moffat_40;

        // Always use finite differences — analytic Jacobian for β is not implemented
        public override bool UseJacobian => false;

        /// <summary>
        /// Value function for the 8-parameter fittable-β case.
        /// parameters[7] is β.
        /// </summary>
        public override double Value(double[] parameters, double[] input) {
            var A = parameters[0];
            var B = parameters[1];
            var x = input[0];
            var y = input[1];
            var x0 = parameters[2];
            var y0 = parameters[3];
            var U = parameters[4];
            var V = parameters[5];
            var T = parameters[6];
            var betaParam = parameters[7];

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            var X = (x - x0) * cosT + (y - y0) * sinT;
            var Y = -(x - x0) * sinT + (y - y0) * cosT;
            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var V2 = V * V;
            var D = 1 + X2 / U2 + Y2 / V2;
            return B + A / Math.Pow(D, betaParam);
        }

        /// <summary>
        /// Gradient is never called by alglib when the state was created with minlmcreatev
        /// (finite-differences mode). This is a no-op implementation to satisfy the base-class
        /// contract; the base FitResidualsJacobian is passed to minlmoptimize so that the
        /// alglib wrapper does not receive a null callback, but alglib never invokes it.
        /// </summary>
        public override void Gradient(double[] parameters, double[] input, double[] result) {
            // No-op: alglib will not call this in finite-differences mode (minlmcreatev)
        }

        // Override ComputeRSS to include the current β at index 7
        public (double rss, double tss) ComputeRSS8(double A, double B, double x0, double y0, double sigmaX, double sigmaY, double theta, double beta) {
            var parameters = new double[] { A, B, x0, y0, sigmaX, sigmaY, theta, beta };
            var rss = 0.0d;
            var tss = 0.0d;
            int pixelCount = this.Inputs.Length;
            var yBar = this.Outputs.Average();
            for (int i = 0; i < pixelCount; ++i) {
                var input = this.Inputs[i];
                var observedValue = this.Outputs[i];
                var estimatedValue = Value(parameters, input);
                var residual = estimatedValue - observedValue;
                var observedDispersion = observedValue - yBar;
                tss += observedDispersion * observedDispersion;
                rss += residual * residual;
            }
            return (rss, tss);
        }

        /// <summary>
        /// Builds the 8-element arrays needed for the LM optimizer and runs it.
        /// Returns a <see cref="PSFModelSolution"/> with the 7 standard fields; the fitted β is stored
        /// in <see cref="MoffatPSFAlglibType.Beta"/> so that <see cref="MoffatPSFAlglibType.SigmaToFWHM"/>
        /// uses the correct value.
        /// </summary>
        public override PSFModelSolution Solve(int maxIterations, double tolerance, CancellationToken ct) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
                var centroidBrightnessAboveBackground = Math.Max(0.0d, this.CentroidBrightness - this.StarDetectionBackground);
                var (initSigmaX, initSigmaY) = ComputeSecondMomentSigmas();
                if (initSigmaX < initSigmaY) {
                    (initSigmaX, initSigmaY) = (initSigmaY, initSigmaX);
                }
                initSigmaX *= 1.001;
                var dxLimit = this.StarBoundingBox.Width / 2.0d;
                var dyLimit = this.StarBoundingBox.Height / 2.0d;
                var initialGuess = new double[] { centroidBrightnessAboveBackground, this.StarDetectionBackground, 0.0, 0.0, initSigmaX, initSigmaY, 0.0d, 4.0d };
                var lowerBounds = new double[] { 0.0d, 0.0d, -dxLimit, -dyLimit, 0, 0, -Math.PI / 2.0d, 1.0d };
                var upperBounds = new double[] { 2.0d, 1.0d, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d, 10.0d };
                var scale = new double[] { 0.01, 0.01, 0.1, 0.1, 1, 1, 1, 1 };
                var solution = new double[8];

                const double deltaForNumericIntegration = 1E-4;
                this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                this.alglibAPI.minlmsetbc(state, lowerBounds, upperBounds);
                this.alglibAPI.minlmsetcond(state, tolerance, maxIterations);
                this.alglibAPI.minlmsetscale(state, scale);
                // Pass FitResidualsJacobian even though alglib won't invoke it in finite-diff mode (V mode).
                // Passing null causes an alglib exception — V-mode requires the callback to be non-null.
                this.alglibAPI.minlmoptimize(state, this.FitResiduals, this.FitResidualsJacobian, (a, f, o) => ct.ThrowIfCancellationRequested(), null);
                ct.ThrowIfCancellationRequested();

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
                    throw new Exception($"PSF modeling failed with type {rep.terminationtype} and reason: {reason}");
                }

                // Store the fitted β so SigmaToFWHM uses the correct value
                this.Beta = solution[7];

                return new PSFModelSolution() {
                    A = solution[0],
                    B = solution[1],
                    X0 = solution[2],
                    Y0 = solution[3],
                    SigmaX = solution[4],
                    SigmaY = solution[5],
                    Theta = solution[6]
                };
            } finally {
                if (state != null) {
                    this.alglibAPI.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
            }
        }

        public override PSFModelSolution SolveIRLS(
            int maxIterationsIRLS,
            double toleranceIRLS,
            int maxIterationsLM,
            double toleranceLM,
            double noiseSigma,
            CancellationToken ct) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
            var (initSigmaX, initSigmaY) = ComputeSecondMomentSigmas();
            if (initSigmaX < initSigmaY) {
                (initSigmaX, initSigmaY) = (initSigmaY, initSigmaX);
            }
            initSigmaX *= 1.001;
            var dxLimit = this.StarBoundingBox.Width / 2.0d;
            var dyLimit = this.StarBoundingBox.Height / 2.0d;
            var initialGuess = new double[] { Math.Max(0.0d, this.CentroidBrightness - this.StarDetectionBackground), this.StarDetectionBackground, 0.0, 0.0, initSigmaX, initSigmaY, 0.0d, 4.0d };
            var lowerBounds = new double[] { 0.0d, 0.0d, -dxLimit, -dyLimit, 0, 0, -Math.PI / 2.0d, 1.0d };
            var upperBounds = new double[] { 2.0d, 1.0d, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d, 10.0d };
            var scale = new double[] { 0.01, 0.01, 0.1, 0.1, 1, 1, 1, 1 };
            try {
                var solution = new double[8];

                maxIterationsLM = maxIterationsLM > 0 ? Math.Min(maxIterationsLM, 20) : 20;
                var iterations = 0;
                var sumOfResidualsDelta = double.PositiveInfinity;
                var prevSumOfResiduals = double.PositiveInfinity;
                while (sumOfResidualsDelta > toleranceIRLS && iterations++ < maxIterationsLM) {
                    const double deltaForNumericIntegration = 1E-4;
                    this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                    this.alglibAPI.minlmsetbc(state, lowerBounds, upperBounds);
                    this.alglibAPI.minlmsetcond(state, toleranceLM, maxIterationsLM);
                    this.alglibAPI.minlmsetscale(state, scale);
                    this.alglibAPI.minlmoptimize(state, this.FitResidualsWeighted, this.FitResidualsJacobianWeighted, null, null);
                    ct.ThrowIfCancellationRequested();

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
                        throw new Exception($"PSF modeling failed with type {rep.terminationtype} and reason: {reason}");
                    }

                    var sumOfResiduals = 0.0d;
                    var huberDelta = HuberThresholdMultiplier * noiseSigma;
                    for (int i = 0; i < this.weights.Length; ++i) {
                        var observedValue = this.Outputs[i];
                        var estimatedValue = Value(solution, this.Inputs[i]);
                        var absResidual = Math.Abs(estimatedValue - observedValue);
                        sumOfResiduals += absResidual;
                        var newWeight = absResidual <= huberDelta ? 1.0 : huberDelta / absResidual;
                        this.weights[i] = newWeight;
                    }

                    sumOfResidualsDelta = Math.Abs(sumOfResiduals - prevSumOfResiduals);
                    prevSumOfResiduals = sumOfResiduals;
                    initialGuess = solution;
                }

                // Store the fitted β so SigmaToFWHM uses the correct value
                this.Beta = solution[7];

                return new PSFModelSolution() {
                    A = solution[0],
                    B = solution[1],
                    X0 = solution[2],
                    Y0 = solution[3],
                    SigmaX = solution[4],
                    SigmaY = solution[5],
                    Theta = solution[6]
                };
            } finally {
                if (state != null) {
                    this.alglibAPI.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
            }
        }
    }
}
