using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Builds synthetic fields where each star is a sharp Gaussian core plus a broad shallow halo. The halo
    /// pixels are detected as part of the star's structure footprint (so they inflate starPoints.Count) but lie
    /// below the clip margin (so they are excluded from totalFlux). This makes the clip-survivor fraction small,
    /// which is exactly the regime where the F11 meanFlux denominator fix (divide by survivor count, not
    /// footprint count) moves NormalizedBrightness by a wide margin — used to pin the fix's direction.
    /// </summary>
    internal static class SyntheticHaloStarField {

        public static Mat CreateFlat(int width, int height, float background) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            mat.SetTo(new Scalar(background));
            return mat;
        }

        public static void AddGaussian(Mat mat, double cx, double cy, double sigma, double peak) {
            int rad = (int)Math.Ceiling(5.0 * sigma);
            double inv = 1.0 / (2.0 * sigma * sigma);
            int width = mat.Width, height = mat.Height;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = Math.Max(0, (int)cy - rad); y <= Math.Min(height - 1, (int)cy + rad); ++y) {
                    for (int x = Math.Max(0, (int)cx - rad); x <= Math.Min(width - 1, (int)cx + rad); ++x) {
                        double dx = x - cx, dy = y - cy;
                        data[y * width + x] += (float)(peak * Math.Exp(-(dx * dx + dy * dy) * inv));
                    }
                }
            }
        }

        /// <summary>Adds a core+halo star: a sharp core (small σ, high peak) and a broad shallow halo at the same center.</summary>
        public static void AddCoreHaloStar(Mat mat, double cx, double cy,
            double coreSigma, double corePeak, double haloSigma, double haloPeak) {
            AddGaussian(mat, cx, cy, coreSigma, corePeak);
            AddGaussian(mat, cx, cy, haloSigma, haloPeak);
        }
    }
}
