#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Modified Bessel functions of the first kind, I₀ and I₁, in their <b>exponentially-scaled</b> form
    /// <c>Ĩ_n(x) = e^(−|x|)·I_n(x)</c> (Abramowitz &amp; Stegun 9.8.1–9.8.4 polynomial approximations,
    /// max error ≲2e-7). The scaled form is exactly what both the annulus⊛Gaussian radial profile and the
    /// Rice-distribution HFR closed form need: the profile carries a factor <c>e^(−rR/σ²)·I₀(rR/σ²)</c> and
    /// the Rice mean carries <c>e^(−t/2)·I₀(t/2)</c> / <c>e^(−t/2)·I₁(t/2)</c>, whose plain I₀/I₁ overflow for
    /// large arguments while the scaled product stays bounded (≈1/√(2πx) for large x). MathNet.Numerics 5.0.0
    /// does not expose modified Bessel functions, so these are implemented here.
    /// </summary>
    internal static class ModifiedBessel {

        /// <summary>Exponentially-scaled I₀: returns <c>e^(−|x|)·I₀(x)</c>.</summary>
        public static double ScaledI0(double x) {
            var ax = Math.Abs(x);
            if (ax < 3.75) {
                var t = x / 3.75;
                t *= t;
                var i0 = 1.0 + t * (3.5156229 + t * (3.0899424 + t * (1.2067492
                    + t * (0.2659732 + t * (0.0360768 + t * 0.0045813)))));
                return Math.Exp(-ax) * i0;
            } else {
                var t = 3.75 / ax;
                var p = 0.39894228 + t * (0.01328592 + t * (0.00225319 + t * (-0.00157565
                    + t * (0.00916281 + t * (-0.02057706 + t * (0.02635537 + t * (-0.01647633
                    + t * 0.00392377)))))));
                return p / Math.Sqrt(ax);
            }
        }

        /// <summary>Exponentially-scaled I₁: returns <c>e^(−|x|)·I₁(x)</c> (odd in x).</summary>
        public static double ScaledI1(double x) {
            var ax = Math.Abs(x);
            double result;
            if (ax < 3.75) {
                var t = x / 3.75;
                t *= t;
                var i1OverX = 0.5 + t * (0.87890594 + t * (0.51498869 + t * (0.15084934
                    + t * (0.02658733 + t * (0.00301532 + t * 0.00032411)))));
                result = Math.Exp(-ax) * ax * i1OverX;
            } else {
                var t = 3.75 / ax;
                var p = 0.39894228 + t * (-0.03988024 + t * (-0.00362018 + t * (0.00163801
                    + t * (-0.01031555 + t * (0.02282967 + t * (-0.02895312 + t * (0.01787654
                    + t * -0.00420059)))))));
                result = p / Math.Sqrt(ax);
            }
            return x < 0.0 ? -result : result;
        }
    }
}
