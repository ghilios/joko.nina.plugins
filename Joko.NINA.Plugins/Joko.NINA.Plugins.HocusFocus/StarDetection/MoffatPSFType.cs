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
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Threading;
using static ILNumerics.Globals;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public static class MoffatShared {

        public static double SigmaToFWHM(double beta, double sigma) {
            return sigma * 2 * Math.Sqrt(Math.Pow(2, 1 / beta) - 1);
        }
    }

    public class MoffatPSFAlglibType : PSFModelTypeAlglibBase {
        public double Beta { get; private set; }

        public MoffatPSFAlglibType(IAlglibAPI alglibAPI, double beta, double[][] inputs, double[] outputs, double centroidBrightness, double starDetectionBackground, Rect starBoundingBox, double pixelScale) :
            base(alglibAPI: alglibAPI, centroidBrightness: centroidBrightness, starDetectionBackground: starDetectionBackground, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.Beta = beta;
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Moffat_40;

        public override bool UseJacobian => true;

        // G(x,y; A,B,x0,y0,sigx,sigy,theta)
        // Background level is normalized already to 0
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Moffate elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
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
            return B + A / Math.Pow(D, Beta);
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

    public class MoffatPSFILNumericsType : PSFModelTypeILNBase {
        public double Beta { get; private set; }

        public MoffatPSFILNumericsType(double beta, double[,] inputs, double[] outputs, double centroidBrightness, double starDetectionBackground, Rect starBoundingBox, double pixelScale) :
            base(centroidBrightness: centroidBrightness, starDetectionBackground: starDetectionBackground, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.Beta = beta;
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Moffat_40;

        public override RetArray<double> Residuals(InArray<double> parameters) {
            using (Scope.Enter(parameters)) {
                Array<double> ilnInputs = this.Inputs;
                Array<double> actualValue = this.Outputs;
                Array<double> A = parameters.GetValue<double>(0);
                Array<double> B = parameters.GetValue<double>(1);
                Array<double> x0 = parameters.GetValue<double>(2);
                Array<double> y0 = parameters.GetValue<double>(3);
                Array<double> U = parameters.GetValue<double>(4);
                Array<double> V = parameters.GetValue<double>(5);
                Array<double> T = parameters.GetValue<double>(6);
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
                Array<double> Beta = this.Beta;
                Array<double> psfValue = B + A / ILMath.pow(D, Beta);
                return psfValue - actualValue.T;
            }
        }

        public override double SigmaToFWHM(double sigma) {
            return MoffatShared.SigmaToFWHM(this.Beta, sigma);
        }
    }
}