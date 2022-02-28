#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using OpenCvSharp;
using System;
using static ILNumerics.Globals;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public static class MoffatShared {

        public static double SigmaToFWHM(double beta, double sigma) {
            return sigma * 2 * Math.Sqrt(Math.Pow(2, 1 / beta) - 1);
        }
    }

    public class MoffatPSFAlglibType : PSFModelTypeAlglibBase {
        public double Beta { get; private set; }

        public MoffatPSFAlglibType(double beta, double[][] inputs, double[] outputs, double centroidBrightness, Rect starBoundingBox, double pixelScale) :
            base(centroidBrightness: centroidBrightness, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.Beta = beta;
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Moffat_40;

        public override bool UseJacobian => false;

        // G(x,y; x0,y0,sigx,sigy,theta)
        // Background level is normalized already to 0
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Moffate elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
        public override double Value(double[] parameters, double[] input) {
            var A = parameters[0];
            var x = input[0];
            var y = input[1];
            var x0 = parameters[1];
            var y0 = parameters[2];
            var U = parameters[3];
            var V = parameters[4];
            var T = parameters[5];
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
            var B = this.Beta;

            // O = A / D^B
            return A / Math.Pow(D, B);
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
            // TODO: Consider implementing this to evaluate whether using a jacobian can speed up modeling
            throw new NotImplementedException();
        }

        public override double SigmaToFWHM(double sigma) {
            return MoffatShared.SigmaToFWHM(this.Beta, sigma);
        }
    }

    public class MoffatPSFILNumericsType : PSFModelTypeILNBase {
        public double Beta { get; private set; }

        public MoffatPSFILNumericsType(double beta, double[,] inputs, double[] outputs, double centroidBrightness, Rect starBoundingBox, double pixelScale) :
            base(centroidBrightness: centroidBrightness, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.Beta = beta;
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Moffat_40;

        public override RetArray<double> Residuals(InArray<double> parameters) {
            using (Scope.Enter(parameters)) {
                Array<double> ilnInputs = this.Inputs;
                Array<double> actualValue = this.Outputs;
                Array<double> A = parameters.GetValue<double>(0);
                Array<double> x0 = parameters.GetValue<double>(1);
                Array<double> y0 = parameters.GetValue<double>(2);
                Array<double> U = parameters.GetValue<double>(3);
                Array<double> V = parameters.GetValue<double>(4);
                Array<double> T = parameters.GetValue<double>(5);
                Array<double> x = ilnInputs[0, full];
                Array<double> y = ilnInputs[1, full];

                Array<double> cosT = ILMath.cos(T);
                Array<double> sinT = ILMath.sin(T);
                Array<double> X = (x - x0) * cosT + (y - y0) * sinT;
                Array<double> Y = -(x - x0) * sinT + (y - y0) * cosT;
                Array<double> X2 = X * X;
                Array<double> Y2 = Y * Y;
                Array<double> U2 = U * U;
                Array<double> V2 = V * V;
                Array<double> D = 1 + X2 / U2 + Y2 / V2;
                Array<double> B = this.Beta;
                Array<double> psfValue = A / ILMath.pow(D, B);
                return psfValue - actualValue.T;
            }
        }

        public override double SigmaToFWHM(double sigma) {
            return MoffatShared.SigmaToFWHM(this.Beta, sigma);
        }
    }
}