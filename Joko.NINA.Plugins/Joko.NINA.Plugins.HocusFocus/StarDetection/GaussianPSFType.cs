#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using OpenCvSharp;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class GaussianPSFType : PSFModelTypeBase {
        private static readonly double SIGMA_TO_FWHM_FACTOR = 2.0d * Math.Sqrt(2.0d * Math.Log(2.0d));

        public GaussianPSFType(double[][] inputs, double[] outputs, double centroidBrightness, Rect starBoundingBox, double pixelScale, bool calculateCenter) :
            base(centroidBrightness: centroidBrightness, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs, calculateCenter: calculateCenter) {
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Gaussian;

        public override bool UseJacobian => false;

        // G(x,y; sigx,sigy,theta)
        // Background level is normalized to already 0
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Gaussian elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
        public override double Value(double[] parameters, double[] input) {
            var x = input[0];
            var y = input[1];
            var x0 = parameters[0];
            var y0 = parameters[1];
            var U = parameters[2];
            var V = parameters[3];
            var T = parameters[4];
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

            //     X^2    Y^2
            // E = ---- + ----
            //     2U^2   2V^2
            var E = X2 / (2 * U2) + Y2 / (2 * V2);
            var A = this.CentroidBrightness;

            // O = A * e^(-E)
            return A * Math.Exp(-E);
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
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
            var A = this.CentroidBrightness;
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

        public override double SigmaToFWHM(double sigma) {
            return sigma * SIGMA_TO_FWHM_FACTOR;
        }
    }
}