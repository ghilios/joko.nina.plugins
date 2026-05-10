using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class CvImageUtilityTests {

        [Test]
        public void CalculateStatistics_FlatImage_ReturnsZeroStdDev() {
            using var mat = SyntheticGaussianStarImage.CreateFlat(8, 8, 0.5f);
            var stats = CvImageUtility.CalculateStatistics(mat);
            Assert.Multiple(() => {
                Assert.That(stats.Mean, Is.EqualTo(0.5).Within(1e-6));
                Assert.That(stats.StdDev, Is.EqualTo(0.0).Within(1e-6));
                Assert.That(stats.Median, Is.EqualTo(0.5).Within(1e-6));
                Assert.That(stats.MAD, Is.EqualTo(0.0).Within(1e-6));
            });
        }

        [Test]
        public void CalculateStatistics_RejectsNonF32() {
            using var mat = new Mat(new Size(4, 4), MatType.CV_8UC1);
            Assert.Throws<ArgumentException>(() => CvImageUtility.CalculateStatistics(mat));
        }

        [Test]
        public void CalculateStatistics_KnownValues_MeanIsCorrect() {
            using var mat = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var p = (float*)mat.DataPointer;
                p[0] = 0.1f; p[1] = 0.2f; p[2] = 0.3f; p[3] = 0.4f;
            }
            var stats = CvImageUtility.CalculateStatistics(mat);
            Assert.That(stats.Mean, Is.EqualTo(0.25).Within(1e-6));
        }

        [Test]
        public void CalculateStatistics_OnlyMeanFlag_ComputesMeanOnly() {
            using var mat = SyntheticGaussianStarImage.CreateFlat(4, 4, 0.7f);
            var stats = CvImageUtility.CalculateStatistics(mat, flags: CvImageStatisticsFlags.Mean);
            Assert.Multiple(() => {
                Assert.That(stats.Mean, Is.EqualTo(0.7).Within(1e-6));
                Assert.That(stats.Median, Is.EqualTo(0.0));   // never set
                Assert.That(stats.MAD, Is.EqualTo(0.0));      // never set
                Assert.That(stats.StdDev, Is.EqualTo(0.0));   // never set
            });
        }

        [Test]
        public void ConvolveGaussian_RejectsEvenKernel() {
            using var src = SyntheticGaussianStarImage.CreateFlat(8, 8, 0.5f);
            using var dst = new Mat();
            Assert.Throws<ArgumentException>(() => CvImageUtility.ConvolveGaussian(src, dst, kernelSize: 4));
        }

        [Test]
        public void ConvolveGaussian_RejectsKernelSmallerThanThree() {
            using var src = SyntheticGaussianStarImage.CreateFlat(8, 8, 0.5f);
            using var dst = new Mat();
            Assert.Throws<ArgumentException>(() => CvImageUtility.ConvolveGaussian(src, dst, kernelSize: 1));
        }

        [Test]
        public void ConvolveGaussian_ConstantImage_StaysConstant() {
            using var src = SyntheticGaussianStarImage.CreateFlat(16, 16, 0.42f);
            using var dst = new Mat();
            CvImageUtility.ConvolveGaussian(src, dst, kernelSize: 5);

            unsafe {
                var data = (float*)dst.DataPointer;
                for (var i = 0; i < 16 * 16; ++i) {
                    Assert.That(data[i], Is.EqualTo(0.42f).Within(1e-5));
                }
            }
        }

        [Test]
        public void Binarize_AboveThresholdMapsToOne_BelowMapsToZero() {
            using var src = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var p = (float*)src.DataPointer;
                p[0] = 0.1f; p[1] = 0.4f; p[2] = 0.6f; p[3] = 0.9f;
            }
            using var dst = new Mat();
            CvImageUtility.Binarize(src, dst, threshold: 0.5);

            unsafe {
                var p = (float*)dst.DataPointer;
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.0f));
                    Assert.That(p[1], Is.EqualTo(0.0f));
                    Assert.That(p[2], Is.EqualTo(1.0f));
                    Assert.That(p[3], Is.EqualTo(1.0f));
                });
            }
        }

        [Test]
        public void ClampInPlace_ClipsBelowMinAndAboveMax() {
            using var src = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var p = (float*)src.DataPointer;
                p[0] = -0.5f; p[1] = 0.3f; p[2] = 0.5f; p[3] = 1.5f;
            }
            CvImageUtility.ClampInPlace(src, min: 0.0f, max: 1.0f);
            unsafe {
                var p = (float*)src.DataPointer;
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.0f));
                    Assert.That(p[1], Is.EqualTo(0.3f).Within(1e-6f));
                    Assert.That(p[2], Is.EqualTo(0.5f).Within(1e-6f));
                    Assert.That(p[3], Is.EqualTo(1.0f));
                });
            }
        }

        [Test]
        public void Rescale_MapsMinToZeroAndMaxToOne() {
            using var src = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var p = (float*)src.DataPointer;
                p[0] = 100f; p[1] = 200f; p[2] = 300f; p[3] = 400f;
            }
            using var dst = CvImageUtility.Rescale(src);
            unsafe {
                var p = (float*)dst.DataPointer;
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.0).Within(1e-5));
                    Assert.That(p[3], Is.EqualTo(1.0).Within(1e-5));
                });
            }
        }

        [Test]
        public void BilinearSamplePixelValue_OnPixelCenter_ReturnsExactPixelValue() {
            using var mat = new Mat(new Size(4, 4), MatType.CV_32F);
            unsafe {
                var p = (float*)mat.DataPointer;
                for (var i = 0; i < 16; ++i) p[i] = i * 0.05f;
            }
            // Pixel (1,2) → row 2, col 1 → flat index 2*4+1 = 9 → 0.45
            var value = CvImageUtility.BilinearSamplePixelValue(mat, y: 2.0, x: 1.0);
            Assert.That(value, Is.EqualTo(0.45f).Within(1e-5));
        }

        [Test]
        public void BilinearSamplePixelValue_HalfwayBetweenPixels_AveragesNeighbors() {
            using var mat = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var p = (float*)mat.DataPointer;
                p[0] = 0; p[1] = 1; p[2] = 1; p[3] = 0;
            }
            // y=0.5, x=0.5 should be the average of all four (each weight 0.25) → 0.5
            var value = CvImageUtility.BilinearSamplePixelValue(mat, y: 0.5, x: 0.5);
            Assert.That(value, Is.EqualTo(0.5).Within(1e-6));
        }

        [Test]
        public void KappaSigmaNoiseEstimate_FlatImage_GivesZeroSigma() {
            using var mat = SyntheticGaussianStarImage.CreateFlat(32, 32, 0.5f);
            var result = CvImageUtility.KappaSigmaNoiseEstimate(mat);
            Assert.Multiple(() => {
                Assert.That(result.Sigma, Is.EqualTo(0.0).Within(1e-6));
                Assert.That(result.NumIterations, Is.GreaterThan(0));
            });
        }

        [Test]
        public void KappaSigmaNoiseEstimate_RejectsNonF32() {
            using var mat = new Mat(new Size(4, 4), MatType.CV_8UC1);
            Assert.Throws<ArgumentException>(() => CvImageUtility.KappaSigmaNoiseEstimate(mat));
        }

        [Test]
        public void GetB3SplineFilter_FirstLayer_HasExpectedSeparatedCoefficients() {
            using var filter = CvImageUtility.GetB3SplineFilter(0);
            // Layer 0: size = (1<<2)+1 = 5
            Assert.That(filter.Cols, Is.EqualTo(1));
            Assert.That(filter.Rows, Is.EqualTo(5));
            unsafe {
                var p = (float*)filter.DataPointer;
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.0625f).Within(1e-6f));
                    Assert.That(p[1], Is.EqualTo(0.25f).Within(1e-6f));
                    Assert.That(p[2], Is.EqualTo(0.375f).Within(1e-6f));
                    Assert.That(p[3], Is.EqualTo(0.25f).Within(1e-6f));
                    Assert.That(p[4], Is.EqualTo(0.0625f).Within(1e-6f));
                });
            }
        }
    }
}
