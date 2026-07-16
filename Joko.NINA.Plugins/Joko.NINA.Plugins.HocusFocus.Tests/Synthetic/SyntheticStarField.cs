using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using OpenCvSharp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Builds multi-star synthetic fields for full-pipeline StarDetector.Detect tests. Stars are additive
    /// Gaussians on a flat background; noise is seeded (deterministic). Peak values are chosen by callers to
    /// sit clearly above or below the gates under test, so small σ-estimate differences cannot flip outcomes.
    ///
    /// The render primitives now live in the plugin (<see cref="StarStamper"/>); these are thin wrappers that
    /// preserve the existing signatures for the ~8 consumer test files.
    /// </summary>
    internal static class SyntheticStarField {

        public static Mat CreateFlat(int width, int height, float background) => StarStamper.CreateFlat(width, height, background);

        /// <summary>Adds a Gaussian star (peak above background) at (cx, cy) with the given σ in pixels.</summary>
        public static void AddStar(Mat mat, double cx, double cy, double sigma, double peak) => StarStamper.AddGaussianStar(mat, cx, cy, sigma, peak);
    }
}
