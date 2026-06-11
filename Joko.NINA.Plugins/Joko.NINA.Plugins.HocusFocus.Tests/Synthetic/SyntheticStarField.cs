using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Builds multi-star synthetic fields for full-pipeline StarDetector.Detect tests. Stars are additive
    /// Gaussians on a flat background; noise is seeded (deterministic). Peak values are chosen by callers to
    /// sit clearly above or below the gates under test, so small σ-estimate differences cannot flip outcomes.
    /// </summary>
    internal static class SyntheticStarField {

        public static Mat CreateFlat(int width, int height, float background) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            mat.SetTo(new Scalar(background));
            return mat;
        }

        /// <summary>Adds a Gaussian star (peak above background) at (cx, cy) with the given σ in pixels.</summary>
        public static void AddStar(Mat mat, double cx, double cy, double sigma, double peak) {
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
    }
}
