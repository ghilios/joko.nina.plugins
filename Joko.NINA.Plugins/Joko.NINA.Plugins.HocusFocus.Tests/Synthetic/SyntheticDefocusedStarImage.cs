using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using OpenCvSharp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Renders defocused-star test images (uniform disk, annular donut) whose continuous radial profile is
    /// known, so MeasureStar's flux-weighted-mean-radius output can be compared to analytic ground truth.
    ///
    /// The render primitives now live in the plugin (<see cref="StarStamper"/>); these are thin wrappers that
    /// preserve the existing signatures for the ~8 consumer test files.
    /// </summary>
    internal static class SyntheticDefocusedStarImage {

        public static Mat CreateDisk(int width, int height, double centerX, double centerY,
                double radius, double peak, double background, double edgeBlurSigma = 0.0)
            => StarStamper.CreateDisk(width, height, centerX, centerY, radius, peak, background, edgeBlurSigma);

        public static Mat CreateAnnulus(int width, int height, double centerX, double centerY,
                double innerRadius, double outerRadius, double peak, double background, double edgeBlurSigma = 0.0)
            => StarStamper.CreateAnnulus(width, height, centerX, centerY, innerRadius, outerRadius, peak, background, edgeBlurSigma);

        /// <summary>Adds zero-mean Gaussian noise of the given standard deviation using a seeded RNG (reproducible).</summary>
        public static void AddGaussianNoise(Mat image, double sigma, int seed) => StarStamper.AddGaussianNoise(image, sigma, seed);
    }
}
