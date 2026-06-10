using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Ground-truth flux-weighted mean radius (the quantity MeasureStar approximates) for the synthetic
    /// shapes, with aperture R >= the star's support:  HFR = ∫0^R I(r) r^2 dr / ∫0^R I(r) r dr.
    /// </summary>
    internal static class HfrGroundTruth {
        /// <summary>Uniform filled disk of radius a:  HFR = 2a/3.</summary>
        public static double Disk(double radius) => 2.0 * radius / 3.0;

        /// <summary>Uniform annulus [inner, outer]:  HFR = (2/3)(a^3 - b^3)/(a^2 - b^2).</summary>
        public static double Annulus(double inner, double outer) =>
            (2.0 / 3.0) * (Math.Pow(outer, 3) - Math.Pow(inner, 3)) / (Math.Pow(outer, 2) - Math.Pow(inner, 2));

        /// <summary>Gaussian exp(-r^2/2σ^2), large aperture:  HFR = σ·sqrt(π/2) ≈ 1.2533σ.</summary>
        public static double Gaussian(double sigma) => sigma * Math.Sqrt(Math.PI / 2.0);

        /// <summary>Numerical flux-weighted mean radius of a radial profile over [0, R] (validates the closed forms).</summary>
        public static double Numerical(Func<double, double> intensity, double apertureRadius, double step = 0.001) {
            double num = 0.0, den = 0.0;
            for (double r = 0.0; r <= apertureRadius; r += step) {
                double i = intensity(r);
                num += i * r * r * step;
                den += i * r * step;
            }
            return den > 0 ? num / den : 0.0;
        }
    }
}
