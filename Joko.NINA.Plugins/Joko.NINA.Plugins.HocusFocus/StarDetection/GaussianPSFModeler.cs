#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Math.Optimization;
using NINA.Astrometry;
using System;
using System.Linq;
using System.Threading;
using Star = NINA.Joko.Plugins.HocusFocus.Interfaces.Star;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class GaussianPSFModeler {
        private static readonly double SIGMA_TO_FWHM_FACTOR = 2.0d * Math.Sqrt(2.0d * Math.Log(2.0d));

        private readonly Star detectedStar;
        private readonly double A;
        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        public GaussianPSFModeler(Star detectedStar) {
            this.detectedStar = detectedStar;
            this.A = detectedStar.CentroidBrightness;

            double[][] inputs = new double[detectedStar.SampledPixelsAboveBackground.Count][];
            double[] outputs = new double[detectedStar.SampledPixelsAboveBackground.Count];
            int index = 0;
            foreach (var pixelAndValue in detectedStar.SampledPixelsAboveBackground) {
                inputs[index] = new double[] { pixelAndValue.Item1.X, pixelAndValue.Item1.Y };
                outputs[index++] = pixelAndValue.Item2;
            }

            this.Inputs = inputs;
            this.Outputs = outputs;
        }

        public static PSFModel Model(Star detectedStar, double pixelScale, int maxIterations = 20, double tolerance = 1E-8, CancellationToken ct = default) {
            var modeler = new GaussianPSFModeler(detectedStar);
            return modeler.Solve(pixelScale, maxIterations, tolerance, ct);
        }

        // G(x,y; sigx,sigy,theta)
        // Background level is normalized to already 0
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Gaussian elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
        public double Value(double[] parameters, double[] input) {
            var x = input[0];
            var y = input[1];
            var U = parameters[0];
            var V = parameters[1];
            var T = parameters[2];
            // T = theta
            // U = sigmaX
            // V = sigmaY

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            var X = x * cosT + y * sinT;
            var Y = -x * sinT + y * cosT;
            // X = xPrime = x * cos(T) + y * sin(T)
            // Y = yPrime = -x * sin(T) + y * cos(T)

            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var V2 = V * V;

            //     X^2    Y^2
            // E = ---- + ----
            //     2U^2   2V^2
            var E = X2 / (2 * U2) + Y2 / (2 * V2);

            // O = A * e^(-E)
            return A * Math.Exp(-E);
        }

        public void Gradient(double[] parameters, double[] input, double[] result) {
            var x = input[0];
            var y = input[1];
            var U = parameters[0]; // sigmaX
            var V = parameters[1]; // sigmaY
            var T = parameters[2]; // theta

            var cosT = Math.Cos(T);
            var sinT = Math.Sin(T);
            var X = x * cosT + y * sinT; // xPrime
            var Y = -x * sinT + y * cosT; // yPrime
            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var U3 = U2 * U;
            var V2 = V * V;
            var V3 = V2 * V;
            var E = X2 / (2 * U2) + Y2 / (2 * V2);
            var O = A * Math.Exp(-E);

            // dX
            // -- = 2XY
            // dT

            // dY
            // -- = -2XY
            // dT

            // dE        X^2
            // -- = -1 * ---
            // dU        U^3

            // dE        Y^2
            // -- = -1 * ---
            // dV        V^3

            //          dX       dY
            // dE   2*X*--   2*Y*--
            //          dT       dT   2 * X^2 * Y - 2 * X * Y^2
            // -- = ------ + ------ = -----------   -----------
            //                            U^2           V^2
            // dT   2*U^2    2*V^2
            //

            // dO             dE       X^2
            // -- = O * -1 *  -- = O * ---
            // dU             dU       U^3

            // dO             dE       Y^2
            // -- = O * -1 *  -- = O * ---
            // dV             dV       V^3

            // dO            dE         2 * X^2 * Y - 2 * X * Y^2
            // -- = O * -1 * -- = -O * (-----------   -----------)
            // dT            dT             U^2           V^2
            var dO_dU = O * (X2 / U3);
            var dO_dV = O * (Y2 / V3);
            var dO_dT = -O * ((2 * X2 * Y) / U2 - (2 * X * Y2) / V2);

            result[0] = dO_dU;
            result[1] = dO_dV;
            result[2] = dO_dT;
        }

        private double GoodnessOfFit(double sigmaX, double sigmaY, double theta) {
            var parameters = new double[] { sigmaX, sigmaY, theta };
            var rss = 0.0d;
            var tss = 0.0d;
            var estimatedSum = 0.0d;
            var yBar = detectedStar.SampledPixelsAboveBackground.Select(x => x.Item2).Average();
            foreach (var (point, observedValue) in detectedStar.SampledPixelsAboveBackground) {
                var input = new double[] { point.X, point.Y };
                var estimatedValue = Value(parameters, input);
                estimatedSum += estimatedValue;
                var residual = estimatedValue - observedValue;
                var observedDispersion = observedValue - yBar;
                tss += observedDispersion * observedDispersion;
                rss += residual * residual;
            }
            return 1 - rss / tss;
        }

        public PSFModel Solve(double pixelScale, int maxIterations = 20, double tolerance = 1E-8, CancellationToken ct = default) {
            var gn = new LevenbergMarquardt(parameters: 3) {
                Function = this.Value,
                Gradient = this.Gradient,
                Solution = new[] { detectedStar.StarBoundingBox.Width / 3.0, detectedStar.StarBoundingBox.Height / 3.0, 0.0d },
                MaxIterations = maxIterations,
                Tolerance = tolerance,
                Token = ct
            };

            gn.Minimize(this.Inputs, this.Outputs);
            ct.ThrowIfCancellationRequested();

            if (!gn.HasConverged) {
                return null;
            }

            var predict = gn.Solution;
            var sigX = predict[0];
            var sigY = predict[1];
            var theta = predict[2];

            var fwhmX = sigX * SIGMA_TO_FWHM_FACTOR;
            var fwhmY = sigY * SIGMA_TO_FWHM_FACTOR;
            var rSquared = GoodnessOfFit(sigX, sigY, theta);
            return new PSFModel(fwhmX: fwhmX, fwhmY: fwhmY, thetaRadians: theta, rSquared: rSquared, pixelScale: pixelScale);
        }
    }
}