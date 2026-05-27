using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectorTests {

        [Test]
        public void ComputeMedian_OddLength_ReturnsCenterElement() {
            // 5 elements: sorted [1, 2, 3, 4, 5] — median is index 2 = 3.0
            var pixels = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(3.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_AveragesTwoMiddleElements() {
            // 10 elements: [1,2,3,4,5,6,7,8,9,10]
            // Correct middle indices: 4 and 5 (values 5 and 6), median = 5.5
            // Buggy formula used Length >> (1+1) = Length / 4 = index 2,
            // producing (pixels[3] + pixels[2]) / 2 = (4 + 3) / 2 = 3.5 — a different result.
            var pixels = new double[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(5.5).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_TwoElements_AveragesBoth() {
            var pixels = new double[] { 3.0, 7.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(5.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EvenLength_FourElements_AveragesTwoMiddleElements() {
            // [10, 20, 30, 40] — middle indices 1 and 2 → (20 + 30) / 2 = 25.0
            var pixels = new double[] { 10.0, 20.0, 30.0, 40.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(25.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_SingleElement_ReturnsThatElement() {
            var pixels = new double[] { 42.0 };
            var median = StarDetector.ComputeMedian(pixels);
            Assert.That(median, Is.EqualTo(42.0).Within(1e-12));
        }

        [Test]
        public void ComputeMedian_EmptyArray_ThrowsArgumentException() {
            var pixels = new double[] { };
            Assert.Throws<ArgumentException>(() => StarDetector.ComputeMedian(pixels));
        }

        // -----------------------------------------------------------------------
        // ComputeIterativeCentroid tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Helper: create a flat float pixel array for a small image and return the
        /// star points list as "all pixels in the image" for simplicity.
        /// </summary>
        private static (float[] pixels, List<CvPoint> points) MakeImage(int width, int height, float background) {
            var pixels = new float[width * height];
            for (int i = 0; i < pixels.Length; ++i) pixels[i] = background;
            var points = new List<CvPoint>();
            for (int y = 0; y < height; ++y)
                for (int x = 0; x < width; ++x)
                    points.Add(new CvPoint(x, y));
            return (pixels, points);
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_SymmetricGaussian_ReturnsCenter() {
            // A symmetric 11x11 Gaussian centered exactly at (5, 5).
            const int side = 11;
            const float background = 0.05f;
            const float peak = 0.8f;
            const double sigma = 2.0;
            var pixels = new float[side * side];
            var points = new List<CvPoint>();
            for (int y = 0; y < side; ++y) {
                for (int x = 0; x < side; ++x) {
                    var dx = x - 5.0;
                    var dy = y - 5.0;
                    pixels[y * side + x] = (float)(background + peak * System.Math.Exp(-(dx * dx + dy * dy) / (2.0 * sigma * sigma)));
                    points.Add(new CvPoint(x, y));
                }
            }

            double backgroundThreshold = background + 0.01;
            double apertureRadius = side / 2.0;

            fixed (float* imageData = pixels) {
                var center = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundMedian: background,
                    backgroundThreshold: backgroundThreshold,
                    apertureRadius: apertureRadius,
                    numPasses: 3);

                Assert.Multiple(() => {
                    Assert.That(center.X, Is.EqualTo(5.0).Within(0.05),
                        "Symmetric Gaussian: iterative centroid X should be near true centre");
                    Assert.That(center.Y, Is.EqualTo(5.0).Within(0.05),
                        "Symmetric Gaussian: iterative centroid Y should be near true centre");
                });
            }
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_AsymmetricStar_ConvergesCloserThanSinglePass() {
            // 11×11 grid, background = 0.05.
            // True star centre is at (5, 5) but we add a bright outlier pixel at (9, 9) that
            // is above threshold and well outside the circular aperture.  A single-pass centroid
            // is pulled toward that outlier; the iterative centroid should settle closer to
            // the true stellar peak.
            const int side = 11;
            const float background = 0.05f;
            const float peak = 0.8f;
            const double sigma = 1.5;

            var pixels = new float[side * side];
            var points = new List<CvPoint>();

            // Place a symmetric Gaussian at (5, 5)
            for (int y = 0; y < side; ++y) {
                for (int x = 0; x < side; ++x) {
                    var dx = x - 5.0;
                    var dy = y - 5.0;
                    pixels[y * side + x] = (float)(background + peak * System.Math.Exp(-(dx * dx + dy * dy) / (2.0 * sigma * sigma)));
                    points.Add(new CvPoint(x, y));
                }
            }

            // Add an outlier bright pixel far from centre that is above threshold
            pixels[9 * side + 9] = 0.6f;

            double backgroundThreshold = background + 0.01;
            // Aperture radius ~5 px: the outlier at (9,9) is sqrt(32)≈5.66 px away from (5,5),
            // just outside the aperture, so it will be excluded from passes 2 and 3.
            double apertureRadius = 5.0;

            fixed (float* imageData = pixels) {
                // Single-pass centroid (numPasses = 1)
                var single = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundMedian: background,
                    backgroundThreshold: backgroundThreshold,
                    apertureRadius: apertureRadius,
                    numPasses: 1);

                // Iterative centroid (numPasses = 3)
                var iterative = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundMedian: background,
                    backgroundThreshold: backgroundThreshold,
                    apertureRadius: apertureRadius,
                    numPasses: 3);

                // Both should be above threshold; verify the outlier actually biases single-pass
                var singleError = System.Math.Sqrt(
                    (single.X - 5.0) * (single.X - 5.0) +
                    (single.Y - 5.0) * (single.Y - 5.0));
                var iterativeError = System.Math.Sqrt(
                    (iterative.X - 5.0) * (iterative.X - 5.0) +
                    (iterative.Y - 5.0) * (iterative.Y - 5.0));

                Assert.Multiple(() => {
                    // The outlier must actually bias the single-pass result away from centre
                    Assert.That(singleError, Is.GreaterThan(0.05),
                        "Single-pass centroid should be pulled off-centre by the outlier");
                    // Iterative centroid must be materially closer to the true centre
                    Assert.That(iterativeError, Is.LessThan(singleError),
                        "Iterative centroid should converge closer to true centre than single-pass");
                    // And it must be within a tight bound
                    Assert.That(iterativeError, Is.LessThan(0.1),
                        "Iterative centroid should be within 0.1 px of true centre (5, 5)");
                });
            }
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_NoPixelsAboveThreshold_ReturnsBoundingBoxCentre() {
            // All pixels are at or below threshold — the fallback centre-of-points path.
            const int side = 5;
            const float background = 0.1f;
            var (pixels, points) = MakeImage(side, side, background);
            // threshold higher than all pixels
            double backgroundThreshold = 0.5;

            fixed (float* imageData = pixels) {
                var center = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundMedian: background,
                    backgroundThreshold: backgroundThreshold,
                    apertureRadius: 3.0,
                    numPasses: 3);

                // The unweighted geometric centre of a 5×5 grid is (2, 2)
                Assert.Multiple(() => {
                    Assert.That(center.X, Is.EqualTo(2.0).Within(0.01));
                    Assert.That(center.Y, Is.EqualTo(2.0).Within(0.01));
                });
            }
        }
    }
}
