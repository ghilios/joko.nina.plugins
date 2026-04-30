#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public static class GaussianPSFConstants {
        public static readonly double SIGMA_TO_FWHM_FACTOR = 2.0d * Math.Sqrt(2.0d * Math.Log(2.0d));
    }

    public class GaussianPSFAlglibType : PSFModelTypeAlglibBase {

        public GaussianPSFAlglibType(IAlglibAPI alglibAPI, double[][] inputs, double[] outputs, double centroidBrightness, double starDetectionBackground, Rect starBoundingBox, double pixelScale) :
            base(alglibAPI: alglibAPI, centroidBrightness: centroidBrightness, starDetectionBackground: starDetectionBackground, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Gaussian;

        public override bool UseJacobian => true;

        // G(x,y; A,B,X0,Y0,sigx,sigy,theta)
        // A is the value at the centroid
        // x0,y0 is the origin, so all x,y are relative to the centroid within the star bounding boxes
        // See Gaussian elliptical definition here: https://pixinsight.com/doc/tools/DynamicPSF/DynamicPSF.html
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

            //     X^2    Y^2
            // E = ---- + ----
            //     2U^2   2V^2
            var E = X2 / (2 * U2) + Y2 / (2 * V2);

            // O = A * e^(-E)
            return B + A * Math.Exp(-E);
        }

        public override void Gradient(double[] parameters, double[] input, double[] result) {
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
            var X = (x - x0) * cosT + (y - y0) * sinT; // xPrime
            var Y = -(x - x0) * sinT + (y - y0) * cosT; // yPrime
            var X2 = X * X;
            var Y2 = Y * Y;
            var U2 = U * U;
            var U3 = U2 * U;
            var V2 = V * V;
            var V3 = V2 * V;
            var E = X2 / (2 * U2) + Y2 / (2 * V2);
            var E_E = Math.Exp(-E);

            // d/da
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+h+%2B+a+*+Exp%5B-%5C%2840%29Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bd%2C2%5D%5D%5C%2841%29%5D+with+respect+to+a
            var d_da = E_E;

            // d/dh
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+h+%2B+a+*+Exp%5B-%5C%2840%29Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bd%2C2%5D%5D%5C%2841%29%5D+with+respect+to+h
            var d_dh = 1.0d;

            // d/dc
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+h+%2B+a+*+Exp%5B-%5C%2840%29Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bd%2C2%5D%5D%5C%2841%29%5D+with+respect+to+c
            var d_dc_part1 = 1.0 / U3 * A * X2;
            var d_dc_part2 = E_E;
            var d_dc = d_dc_part1 * d_dc_part2;

            // d/dd
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+h+%2B+a+*+Exp%5B-%5C%2840%29Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bd%2C2%5D%5D%5C%2841%29%5D+with+respect+to+d
            var d_dd_part1 = 1.0 / V3 * A * Y2;
            var d_dd_part2 = E_E;
            var d_dd = d_dd_part1 * d_dd_part2;

            // d/df
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+h+%2B+a+*+Exp%5B-%5C%2840%29Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bd%2C2%5D%5D%5C%2841%29%5D+with+respect+to+f
            var d_df_part1 = A;
            var d_df_part2 = cosT * X / U2 - sinT * Y / V2;
            var d_df_part3 = E_E;
            var d_df = d_df_part1 * d_df_part2 * d_df_part3;

            // d/dg
            var d_dg_part1 = A;
            var d_dg_part2 = sinT * X / U2 + cosT * Y / V2;
            var d_dg_part3 = E_E;
            var d_dg = d_dg_part1 * d_dg_part2 * d_dg_part3;

            // d/dt
            //  https://www.wolframalpha.com/input?i2d=true&i=differentiate+h+%2B+a+*+Exp%5B-%5C%2840%29Divide%5BPower%5B%5C%2840%29%5C%2840%29x+-+f%5C%2841%29*Cos%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Sin%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bc%2C2%5D%5D+%2B+Divide%5BPower%5B%5C%2840%29-%5C%2840%29x+-+f%5C%2841%29*Sin%5Bt%5D%2B%5C%2840%29y+-+g%5C%2841%29*Cos%5Bt%5D%5C%2841%29%2C2%5D%2C2*Power%5Bd%2C2%5D%5D%5C%2841%29%5D+with+respect+to+t
            var d_dt_part1 = A;
            var XY = X * Y;
            var d_dt_part2 = XY / V2 - XY / U2;
            var d_dt_part3 = E_E;
            var d_dt = d_dt_part1 * d_dt_part2 * d_dt_part3;

            result[0] = d_da;
            result[1] = d_dh;
            result[2] = d_df;
            result[3] = d_dg;
            result[4] = d_dc;
            result[5] = d_dd;
            result[6] = d_dt;
        }

        public override double SigmaToFWHM(double sigma) {
            return sigma * GaussianPSFConstants.SIGMA_TO_FWHM_FACTOR;
        }
    }
}
