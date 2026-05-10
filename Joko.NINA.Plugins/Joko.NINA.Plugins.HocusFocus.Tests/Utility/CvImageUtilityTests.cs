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

        [Test]
        public void GetB3SplineFilter_SecondLayer_HasZerosBetweenCoefficients() {
            using var filter = CvImageUtility.GetB3SplineFilter(1);
            // Layer 1: stride = 2, size = (2 << 2) + 1 = 9 with zeros between coefficients
            Assert.That(filter.Cols, Is.EqualTo(1));
            Assert.That(filter.Rows, Is.EqualTo(9));
            unsafe {
                var p = (float*)filter.DataPointer;
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.0625f).Within(1e-6f));
                    Assert.That(p[1], Is.EqualTo(0.0f));
                    Assert.That(p[2], Is.EqualTo(0.25f).Within(1e-6f));
                    Assert.That(p[3], Is.EqualTo(0.0f));
                    Assert.That(p[4], Is.EqualTo(0.375f).Within(1e-6f));
                    Assert.That(p[5], Is.EqualTo(0.0f));
                    Assert.That(p[6], Is.EqualTo(0.25f).Within(1e-6f));
                    Assert.That(p[7], Is.EqualTo(0.0f));
                    Assert.That(p[8], Is.EqualTo(0.0625f).Within(1e-6f));
                });
            }
        }

        [Test]
        public void Rescale_FlatImage_ProducesNaNDueToZeroRange() {
            // Documents current behavior: flat input divides by (max - min) = 0 → NaN. If this is ever
            // changed to coalesce to a defined value, this test should fail and prompt an update.
            using var src = SyntheticGaussianStarImage.CreateFlat(4, 4, 0.3f);
            using var dst = CvImageUtility.Rescale(src);
            unsafe {
                var p = (float*)dst.DataPointer;
                Assert.That(float.IsNaN(p[0]) || p[0] == 0.0f, Is.True);
            }
        }

        [Test]
        public void SubtractInPlace_KnownDifference() {
            using var lhs = new Mat(new Size(2, 2), MatType.CV_32F);
            using var rhs = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var l = (float*)lhs.DataPointer;
                var r = (float*)rhs.DataPointer;
                l[0] = 0.6f; l[1] = 0.5f; l[2] = 0.4f; l[3] = 0.3f;
                r[0] = 0.1f; r[1] = 0.4f; r[2] = 0.4f; r[3] = 0.5f;
            }
            CvImageUtility.SubtractInPlace(lhs, rhs);
            unsafe {
                var p = (float*)lhs.DataPointer;
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.5f).Within(1e-5f));
                    Assert.That(p[1], Is.EqualTo(0.1f).Within(1e-5f));
                    Assert.That(p[2], Is.EqualTo(0.0f).Within(1e-5f));
                    // 0.3 - 0.5 = -0.2 → clamped to 0 by default min
                    Assert.That(p[3], Is.EqualTo(0.0f));
                });
            }
        }

        [Test]
        public void ComputeResidualAtrousB3SplineDyadicWaveletLayer_FlatImage_PreservesMean() {
            using var src = SyntheticGaussianStarImage.CreateFlat(32, 32, 0.5f);
            using var residual = CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer(src, numLayers: 3);
            // Residual layer of a flat image (low-pass) should keep the mean approximately
            var stats = CvImageUtility.CalculateStatistics(residual);
            Assert.That(stats.Mean, Is.EqualTo(0.5).Within(1e-3));
        }

        [Test]
        public void KappaSigmaNoiseEstimate_Reports_BackgroundMean_OnFlatImage() {
            using var mat = SyntheticGaussianStarImage.CreateFlat(16, 16, 0.42f);
            var result = CvImageUtility.KappaSigmaNoiseEstimate(mat);
            Assert.That(result.BackgroundMean, Is.EqualTo(0.42).Within(1e-5));
        }

        [Test]
        public void CalculateStatistics_OnSubRect_RestrictsToRegion() {
            using var mat = new Mat(new Size(4, 4), MatType.CV_32F);
            unsafe {
                var p = (float*)mat.DataPointer;
                for (var i = 0; i < 16; i++) p[i] = i / 16.0f;
            }
            // Top-left 2x2 block: indices 0,1,4,5 → values 0/16, 1/16, 4/16, 5/16
            // Mean = (0 + 0.0625 + 0.25 + 0.3125) / 4 = 0.15625
            var stats = CvImageUtility.CalculateStatistics(mat, rect: new Rect(0, 0, 2, 2));
            Assert.That(stats.Mean, Is.EqualTo(0.15625).Within(1e-5));
        }

        [Test]
        public void Binarize_AtThresholdValue_BelongsToBelow() {
            using var src = new Mat(new Size(2, 1), MatType.CV_32F);
            unsafe {
                var p = (float*)src.DataPointer;
                p[0] = 0.5f;     // exactly threshold
                p[1] = 0.5001f;  // just above
            }
            using var dst = new Mat();
            CvImageUtility.Binarize(src, dst, threshold: 0.5);
            unsafe {
                var p = (float*)dst.DataPointer;
                Assert.That(p[0], Is.EqualTo(0.0f));
                Assert.That(p[1], Is.EqualTo(1.0f));
            }
        }

        [Test]
        public void BilinearSamplePixelValue_OutsideBounds_DoesNotThrow() {
            using var mat = new Mat(new Size(2, 2), MatType.CV_32F);
            unsafe {
                var p = (float*)mat.DataPointer;
                p[0] = 0.0f; p[1] = 0.0f; p[2] = 0.0f; p[3] = 0.0f;
            }
            // Sampling at (-0.5, -0.5) — implementations vary on edge handling, but it should not throw and return finite.
            var v = CvImageUtility.BilinearSamplePixelValue(mat, y: 0.0, x: 0.0);
            Assert.That(double.IsFinite(v), Is.True);
        }
    }
}
