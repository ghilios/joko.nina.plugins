#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OpenCvSharp;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class MoffatPSFType : PSFModelTypeBase {
        public double Beta { get; private set; }

        public MoffatPSFType(double beta, double[][] inputs, double[] outputs, double centroidBrightness, Rect starBoundingBox, double pixelScale) :
            base(centroidBrightness: centroidBrightness, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.Beta = beta;
        }

        public override bool UseJacobian => false;

        // G(x,y; sigx,sigy,theta)
        // Background level is normalized already to 0
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Moffate elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
        public override double Value(double[] parameters, double[] input) {
            var x = input[0];
            var y = input[1];
            var U = parameters[0];
            var V = parameters[1];
            var T = parameters[2];
            // U = sigmaX
            // V = sigmaY
            // T = theta

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

            //         X^2   Y^2
            // D = 1 + --- + ---
            //         U^2   V^2
            var D = 1 + X2 / U2 + Y2 / V2;
            var A = this.CentroidBrightness;
            var B = this.Beta;

            // O = A / D^B
            return A / Math.Pow(D, B);
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            // TODO: Consider implementing this to evaluate whether using a jacobian can speed up modeling
            throw new NotImplementedException();
        }
    }
}