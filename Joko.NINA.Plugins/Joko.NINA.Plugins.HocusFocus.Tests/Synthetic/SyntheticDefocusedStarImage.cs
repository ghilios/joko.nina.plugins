using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Renders defocused-star test images (uniform disk, annular donut) whose continuous radial profile is
    /// known, so MeasureStar's flux-weighted-mean-radius output can be compared to analytic ground truth.
    /// Each pixel is supersampled so the rendered value equals the analytic mean over the pixel area, keeping
    /// the rim faithful (otherwise discretization at the edge dominates the error budget).
    /// </summary>
    internal static class SyntheticDefocusedStarImage {
        private const int SuperSample = 4; // NxN subsamples per pixel

        public static Mat CreateDisk(int width, int height, double centerX, double centerY,
                double radius, double peak, double background, double edgeBlurSigma = 0.0) {
            return Render(width, height, centerX, centerY, background,
                r => DiskIntensity(r, radius, peak, edgeBlurSigma));
        }

        public static Mat CreateAnnulus(int width, int height, double centerX, double centerY,
                double innerRadius, double outerRadius, double peak, double background, double edgeBlurSigma = 0.0) {
            return Render(width, height, centerX, centerY, background,
                r => AnnulusIntensity(r, innerRadius, outerRadius, peak, edgeBlurSigma));
        }

        /// <summary>Adds zero-mean Gaussian noise of the given standard deviation using a seeded RNG (reproducible).</summary>
        public static void AddGaussianNoise(Mat image, double sigma, int seed) {
            var rng = new Random(seed);
            int n = image.Width * image.Height;
            unsafe {
                var data = (float*)image.DataPointer;
                for (int i = 0; i < n; ++i) {
                    double u1 = 1.0 - rng.NextDouble();
                    double u2 = 1.0 - rng.NextDouble();
                    double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2); // Box-Muller
                    data[i] += (float)(sigma * z);
                }
            }
        }

        private static Mat Render(int width, int height, double cx, double cy, double background,
                Func<double, double> radialIntensity) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            double inv = 1.0 / SuperSample;
            int sub = SuperSample * SuperSample;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = 0; y < height; ++y) {
                    for (int x = 0; x < width; ++x) {
                        double sum = 0.0;
                        for (int sy = 0; sy < SuperSample; ++sy) {
                            for (int sx = 0; sx < SuperSample; ++sx) {
                                double px = x - 0.5 + (sx + 0.5) * inv;
                                double py = y - 0.5 + (sy + 0.5) * inv;
                                double dx = px - cx, dy = py - cy;
                                sum += radialIntensity(Math.Sqrt(dx * dx + dy * dy));
                            }
                        }
                        data[y * width + x] = (float)(background + sum / sub);
                    }
                }
            }
            return mat;
        }

        private static double DiskIntensity(double r, double radius, double peak, double edgeBlurSigma) {
            if (edgeBlurSigma <= 0.0) return r <= radius ? peak : 0.0;
            return peak * 0.5 * Erfc((r - radius) / (edgeBlurSigma * Math.Sqrt(2.0)));
        }

        private static double AnnulusIntensity(double r, double inner, double outer, double peak, double edgeBlurSigma) {
            if (edgeBlurSigma <= 0.0) return (r >= inner && r <= outer) ? peak : 0.0;
            double rise = 0.5 * Erfc((inner - r) / (edgeBlurSigma * Math.Sqrt(2.0)));
            double fall = 0.5 * Erfc((r - outer) / (edgeBlurSigma * Math.Sqrt(2.0)));
            return peak * rise * fall;
        }

        // Abramowitz & Stegun 7.1.26 erfc approximation (max error ~1.2e-7).
        private static double Erfc(double x) {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }
    }
}
