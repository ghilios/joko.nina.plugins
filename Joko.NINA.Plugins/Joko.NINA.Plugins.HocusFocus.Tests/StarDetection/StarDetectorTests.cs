using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using System.Collections.Generic;
using System;

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

        // -----------------------------------------------------------------------
        // MeasureStar circular aperture tests
        // -----------------------------------------------------------------------

        /// <summary>
        /// Verifies that the circular aperture removes the rectangular bias: corner pixels at
        /// distance ≈ √2 × half-box are excluded so HFR is lower than a naive rectangular sum.
        /// Uses a wider Gaussian (sigma=8) so that corner pixels are meaningful (~5% of peak at 3.5σ),
        /// resulting in an observable HFR difference of ~0.2+ pixels.
        /// </summary>
        [Test]
        public void MeasureStar_CircularAperture_HfrLowerThanRectangularBias() {
            // Create a symmetric Gaussian star centred at (20, 20) with sigma=8, peak=0.8, background=0.05.
            // The star is embedded in a 41×41 image so there are plenty of corner pixels well outside a
            // circle inscribed in the bounding box.  With sigma=8, corner pixels at distance ≈28px (≈3.5σ)
            // have meaningful amplitude (~5% of peak), making the circular vs rectangular HFR difference observable.
            const int imageSize = 41;
            const double cx = 20.0;
            const double cy = 20.0;
            const double sigma = 8.0;
            const double peak = 0.8;
            const double background = 0.05;

            using (var image = SyntheticGaussianStarImage.Create(
                width: imageSize, height: imageSize,
                centerX: cx, centerY: cy,
                sigmaX: sigma, sigmaY: sigma,
                peak: peak, background: background)) {

                var detector = new StarDetector(new AlglibAPI());

                // Bounding box covers the entire image (square), so the inscribed circle has
                // radius = imageSize / 2 = 20.5.  Corner pixels are at distance √2 × 20 ≈ 28.3
                // from the centre — well outside the circle.
                var boundingBox = new Rect(0, 0, imageSize, imageSize);
                var p = new StarDetectorParams {
                    AnalysisSamplingSize = 1.0f,
                    StarClippingMultiplier = 0.0   // no noise clipping for this synthetic test
                };

                // Circular aperture HFR
                var circularStar = new Star {
                    Center = new Point2d(cx, cy),
                    StarBoundingBox = boundingBox,
                    Background = background
                };
                var circularSuccess = detector.MeasureStar(image, circularStar, p, noiseSigma: 0.0);
                Assert.That(circularSuccess, Is.True, "MeasureStar should succeed for a well-formed Gaussian");

                // Rectangular HFR: manually compute by summing ALL pixels in the box (no aperture filter).
                double rectTotalBrightness = 0.0;
                double rectTotalWeightedDistance = 0.0;
                unsafe {
                    var data = (float*)image.DataPointer;
                    for (int y = 0; y < imageSize; ++y) {
                        for (int x = 0; x < imageSize; ++x) {
                            var value = data[y * imageSize + x] - background;
                            if (value > 0.0) {
                                var dx = x - cx;
                                var dy = y - cy;
                                var dist = Math.Sqrt(dx * dx + dy * dy);
                                rectTotalWeightedDistance += value * dist;
                                rectTotalBrightness += value;
                            }
                        }
                    }
                }
                var rectangularHFR = rectTotalBrightness > 0 ? rectTotalWeightedDistance / rectTotalBrightness : 0.0;

                // The circular HFR must be strictly less than the rectangular HFR because corner
                // pixels (far from centre) are excluded, lowering the flux-weighted mean radius.
                // With sigma=8, the difference should be meaningfully large (>0.05 pixels).
                Assert.Multiple(() => {
                    Assert.That(circularStar.HFR, Is.LessThan(rectangularHFR),
                        $"Circular HFR ({circularStar.HFR:F4}) should be lower than rectangular HFR ({rectangularHFR:F4})");
                    Assert.That(rectangularHFR - circularStar.HFR, Is.GreaterThan(0.05),
                        $"Rectangular bias should be at least 0.05 HFR pixels, but got {rectangularHFR - circularStar.HFR:F4}");
                });
            }
        }

        // -----------------------------------------------------------------------
        // CheckBackgroundContamination tests
        // -----------------------------------------------------------------------

        private static PSFModel MakePSF(double background, double rSquared = 0.99) {
            return new PSFModel(
                psfType: StarDetectorPSFFitType.Gaussian,
                offsetX: 0.0,
                offsetY: 0.0,
                peak: 0.5,
                background: background,
                sigmaX: 2.0,
                sigmaY: 2.0,
                fwhmX: 4.7,
                fwhmY: 4.7,
                thetaRadians: 0.0,
                rSquared: rSquared,
                pixelScale: 1.0);
        }

        [Test]
        public void CheckBackgroundContamination_AnnulusVsPSFDiffExceedsTwoSigma_FlagsAndReturnsTrue() {
            // annulus_bg = 0.1, psf_bg = 0.3, noiseSigma = 0.05
            // |0.1 - 0.3| = 0.2 > 2 * 0.05 = 0.1  → contamination suspected
            const double annulusBg = 0.1;
            const double psfBg = 0.3;
            const double noiseSigma = 0.05;

            var star = new Star {
                Center = new Point2d(100.0, 200.0),
                Background = annulusBg,
                StarBoundingBox = new Rect(90, 190, 20, 20)
            };
            var psf = MakePSF(background: psfBg);
            var p = new StarDetectorParams { StarClippingMultiplier = 1.0 };

            var result = StarDetector.CheckBackgroundContamination(star, psf, p, noiseSigma);

            Assert.Multiple(() => {
                Assert.That(result, Is.True, "Should return true when annulus and PSF backgrounds differ by 4σ");
                Assert.That(star.StarContaminationSuspected, Is.True, "Star.StarContaminationSuspected should be set");
            });
        }

        [Test]
        public void CheckBackgroundContamination_AnnulusVsPSFDiffExactlyTwoSigma_DoesNotFlag() {
            // |0.1 - 0.2| = 0.1 == 2 * 0.05 = 0.1  → NOT > twoSigma, so not flagged
            const double annulusBg = 0.1;
            const double psfBg = 0.2;
            const double noiseSigma = 0.05;

            var star = new Star {
                Center = new Point2d(100.0, 200.0),
                Background = annulusBg,
                StarBoundingBox = new Rect(90, 190, 20, 20)
            };
            var psf = MakePSF(background: psfBg);
            var p = new StarDetectorParams { StarClippingMultiplier = 1.0 };

            var result = StarDetector.CheckBackgroundContamination(star, psf, p, noiseSigma);

            Assert.Multiple(() => {
                Assert.That(result, Is.False, "Should not flag when difference equals exactly 2σ (not strictly greater)");
                Assert.That(star.StarContaminationSuspected, Is.False, "Star.StarContaminationSuspected should remain false");
            });
        }

        [Test]
        public void CheckBackgroundContamination_AnnulusVsPSFAgreement_DoesNotFlag() {
            // Backgrounds agree: |0.1 - 0.12| = 0.02 < 2 * 0.05 = 0.1
            const double annulusBg = 0.1;
            const double psfBg = 0.12;
            const double noiseSigma = 0.05;

            var star = new Star {
                Center = new Point2d(50.0, 50.0),
                Background = annulusBg,
                StarBoundingBox = new Rect(40, 40, 20, 20)
            };
            var psf = MakePSF(background: psfBg);
            var p = new StarDetectorParams { StarClippingMultiplier = 1.0 };

            var result = StarDetector.CheckBackgroundContamination(star, psf, p, noiseSigma);

            Assert.Multiple(() => {
                Assert.That(result, Is.False, "Should not flag when backgrounds agree within 2σ");
                Assert.That(star.StarContaminationSuspected, Is.False, "StarContaminationSuspected should remain false");
            });
        }

        [Test]
        public void CheckBackgroundContamination_MetricsCounterIncremented_WhenFlagged() {
            // Verify that the caller (ModelPSF) would correctly count the flagged star.
            // We simulate the counter increment that ModelPSF performs.
            var metrics = new StarDetectorMetrics();
            var star = new Star {
                Center = new Point2d(10.0, 20.0),
                Background = 0.1,
                StarBoundingBox = new Rect(5, 15, 20, 20)
            };
            var psf = MakePSF(background: 0.3);  // Differs by 4σ from annulus
            var p = new StarDetectorParams { StarClippingMultiplier = 1.0 };

            if (StarDetector.CheckBackgroundContamination(star, psf, p, noiseSigma: 0.05)) {
                ++metrics.ContaminationSuspected;
            }

            Assert.That(metrics.ContaminationSuspected, Is.EqualTo(1));
        }

        [Test]
        public unsafe void ComputeIterativeCentroid_SecondPassEmptyAperture_ReturnsSinglePassEstimate() {
            // Pass 1 succeeds and estimates centroid at (2.5, 2.5).
            // Pass 2 uses a very small aperture radius that excludes all pixels,
            // so it should break early and return the pass-1 estimate.
            const int side = 7;
            const float background = 0.0f;
            const float threshold = 0.5f;
            var (pixels, points) = MakeImage(side, side, background);

            // Place two bright pixels at (2, 2) and (3, 3) symmetrically
            // Pass 1 centroid will be (2.5, 2.5)
            pixels[2 * side + 2] = 0.8f;
            pixels[3 * side + 3] = 0.8f;

            double backgroundThreshold = threshold;
            // Aperture radius 0.3: pixels at (2,2) and (3,3) are both at distance sqrt(0.5^2 + 0.5^2) = 0.707
            // from (2.5, 2.5), which exceeds 0.3, so the aperture will exclude all pixels in pass 2.
            double apertureRadius = 0.3;

            fixed (float* imageData = pixels) {
                var center = StarDetector.ComputeIterativeCentroid(
                    imageData: imageData,
                    imageWidth: side,
                    starPoints: points,
                    backgroundMedian: background,
                    backgroundThreshold: backgroundThreshold,
                    apertureRadius: apertureRadius,
                    numPasses: 3);

                // Should return pass-1 estimate of (2.5, 2.5)
                Assert.Multiple(() => {
                    Assert.That(center.X, Is.EqualTo(2.5).Within(1e-12),
                        "Should return pass-1 centroid when pass 2 aperture excludes all pixels");
                    Assert.That(center.Y, Is.EqualTo(2.5).Within(1e-12),
                        "Should return pass-1 centroid when pass 2 aperture excludes all pixels");
                });
            }
        }
    }
}
