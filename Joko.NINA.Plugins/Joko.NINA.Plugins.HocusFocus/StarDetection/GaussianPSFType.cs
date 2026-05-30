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

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public static class GaussianPSFConstants {
        public static readonly double SIGMA_TO_FWHM_FACTOR = 2.0d * Math.Sqrt(2.0d * Math.Log(2.0d));
        public static readonly double SQRT2 = Math.Sqrt(2.0d);
    }

    public class GaussianPSFAlglibType : PSFModelTypeAlglibBase {
        private readonly bool pixelIntegration;

        public GaussianPSFAlglibType(IAlglibAPI alglibAPI, double[][] inputs, double[] outputs, double centroidBrightness, double starDetectionBackground, Rect starBoundingBox, double pixelScale, bool pixelIntegration = false) :
            base(alglibAPI: alglibAPI, centroidBrightness: centroidBrightness, starDetectionBackground: starDetectionBackground, pixelScale: pixelScale, starBoundingBox: starBoundingBox, inputs: inputs, outputs: outputs) {
            this.pixelIntegration = pixelIntegration;
        }

        public override StarDetectorPSFFitType PSFType => StarDetectorPSFFitType.Gaussian;

        public override bool UseJacobian => !pixelIntegration;

        /// <summary>
        /// Computes the integral of a 1-D Gaussian exp(-t²/(2σ²)) over [lo, hi].
        /// Returns σ√(2π) · [Φ((hi-μ)/σ) − Φ((lo-μ)/σ)] where Φ is the standard-normal CDF,
        /// but we factor out the σ√(2π) and the amplitude A separately in Value(), so here we
        /// return the dimensionless fractional integral (divided by σ√(2π)) ∈ [0, 1].
        /// </summary>
        private static double GaussianCdfDiff(double lo, double hi, double sigma) {
            // Φ(z) = (1 + erf(z/√2)) / 2
            // ΔΦ = Φ((hi)/σ) − Φ((lo)/σ) = [erf(hi/(σ√2)) − erf(lo/(σ√2))] / 2
            var loZ = lo / (sigma * GaussianPSFConstants.SQRT2);
            var hiZ = hi / (sigma * GaussianPSFConstants.SQRT2);
            return (MathNet.Numerics.SpecialFunctions.Erf(hiZ) - MathNet.Numerics.SpecialFunctions.Erf(loZ)) * 0.5;
        }

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

            if (pixelIntegration) {
                // Pixel-area integration: integrate the 2-D Gaussian over [x-0.5, x+0.5] × [y-0.5, y+0.5].
                //
                // The 2-D rotated elliptical Gaussian is separable in the rotated frame (X', Y').
                // The pixel corners in image space are (x ± 0.5, y ± 0.5). We map each corner pair to
                // the rotated frame and compute the 1-D integral along each axis.
                //
                // For a pixel at (x, y) with centre-of-pixel at input coords (dx, dy) = (x - x0, y - y0):
                //   The pixel spans [dx-0.5, dx+0.5] × [dy-0.5, dy+0.5] in image space.
                //
                // In the rotated frame, the pixel maps to a parallelogram, NOT a rectangle. However,
                // since the pixel is 1×1, we use the standard approximation of integrating over the
                // rotated-frame projections of the pixel extents. For small pixels, this is accurate.
                //
                // X'_center = (dx)*cosT + (dy)*sinT
                // Y'_center = -(dx)*sinT + (dy)*cosT
                //
                // The half-extents in the rotated frame from a 1×1 pixel:
                //   ΔX' = |cosT| * 0.5 + |sinT| * 0.5  (projection of pixel extents)
                //   ΔY' = |sinT| * 0.5 + |cosT| * 0.5
                //
                // But for a cleanly separable integral, we integrate the X' marginal over X'±ΔX' and
                // Y' marginal over Y'±ΔY'. This is the "rotated pixel" integral approximation.
                //
                // Normalisation: the 1-D integral of exp(-t²/(2σ²)) over (-∞,∞) = σ√(2π).
                // So the integral of the full 2-D Gaussian over all space = A * σX√(2π) * σY√(2π) / (normalization).
                // Since our model is A*exp(-X²/(2U²) - Y²/(2V²)), the peak value at (x0,y0) is A.
                // The pixel integral relative to A equals:
                //   (σX√(2π)) * ΔΦ_X(x0) * (σY√(2π)) * ΔΦ_Y(y0) / (σX√(2π) * σY√(2π))
                //   = ΔΦ_X * ΔΦ_Y   — dimensionless fractions in [0,1]
                // So pixelValue = A * ΔΦ_X * ΔΦ_Y  where each ΔΦ is the fractional CDF interval.
                //
                // The Gaussian over the pixel [i-½, i+½]×[j-½, j+½] in rotated frame is:
                //   ∫∫ exp(-X'²/(2U²)) exp(-Y'²/(2V²)) dX' dY'
                //   / (∫∫ exp(-X'²/(2U²)) exp(-Y'²/(2V²)) dX' dY' over all space)
                //   × A
                //
                // ∫_{lo_X}^{hi_X} exp(-X'²/(2U²)) dX' = U√(2π) * [Φ(hi_X/U) - Φ(lo_X/U)]
                // Similarly for Y'.
                //
                // So pixelValue ≈ A * [U√(2π)*ΔΦ_X] * [V√(2π)*ΔΦ_Y] / (U√(2π) * V√(2π))
                //               = A * ΔΦ_X * ΔΦ_Y
                //
                // where ΔΦ_X = Φ((xCentre+0.5-x0)/U) - Φ((xCentre-0.5-x0)/U)  (in rotated frame)
                //
                // NOTE: the pixel boundaries in the rotated frame depend on the orientation.
                // We use the half-extent projection as an approximation.

                var dx = x - x0;
                var dy = y - y0;
                var Xc = dx * cosT + dy * sinT;
                var Yc = -dx * sinT + dy * cosT;

                // Half-extents of the 1×1 pixel projected onto the principal axes
                var halfExtX = Math.Abs(cosT) * 0.5 + Math.Abs(sinT) * 0.5;
                var halfExtY = Math.Abs(sinT) * 0.5 + Math.Abs(cosT) * 0.5;

                var deltaPhiX = GaussianCdfDiff(Xc - halfExtX, Xc + halfExtX, U);
                var deltaPhiY = GaussianCdfDiff(Yc - halfExtY, Yc + halfExtY, V);

                // The normalization factor: when the pixel is very large compared to sigma, the
                // integral approaches the continuous analytic integral. We normalise by the
                // full analytic 1-pixel-wide CDF at the centre (treating the pixel as half-extents):
                // This gives the integrated value scaled so that the peak pixel (at x=x0, y=y0)
                // produces a value ≈ A when the pixel is much larger than sigma.
                // Since we want the output to have the same scale as the point-sampled model
                // (value = A at centroid pixel), we need to normalize by the CDF at the centroid
                // rather than the total-integral 1. We do this by dividing by ΔΦ_centroid (the
                // CDF diff at offset 0), which equals erf(halfExt/(σ√2)).
                var normX = MathNet.Numerics.SpecialFunctions.Erf(halfExtX / (U * GaussianPSFConstants.SQRT2));
                var normY = MathNet.Numerics.SpecialFunctions.Erf(halfExtY / (V * GaussianPSFConstants.SQRT2));

                double gaussianPart;
                if (normX > 0.0 && normY > 0.0) {
                    gaussianPart = A * (deltaPhiX / normX) * (deltaPhiY / normY);
                } else {
                    // Fallback to point sample if sigma is extremely small
                    var X = Xc;
                    var Y = Yc;
                    gaussianPart = A * Math.Exp(-(X * X) / (2 * U * U) - (Y * Y) / (2 * V * V));
                }
                return B + gaussianPart;
            }

            var Xpt = (x - x0) * cosT + (y - y0) * sinT;
            var Ypt = -(x - x0) * sinT + (y - y0) * cosT;
            // X = xPrime = x * cos(T) + y * sin(T)
            // Y = yPrime = -x * sin(T) + y * cos(T)

            var X2 = Xpt * Xpt;
            var Y2 = Ypt * Ypt;
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
