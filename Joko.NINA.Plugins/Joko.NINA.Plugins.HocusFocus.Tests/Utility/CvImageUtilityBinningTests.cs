using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class CvImageUtilityBinningTests {

        private static Mat Ramp(int width, int height, Func<int, int, float> value) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            for (var y = 0; y < height; ++y) {
                for (var x = 0; x < width; ++x) {
                    mat.Set(y, x, value(x, y));
                }
            }
            return mat;
        }

        [Test]
        public void BinMean_FactorOfOne_ReturnsAnIndependentCopy() {
            using var src = Ramp(4, 4, (x, y) => x + y);
            using var binned = CvImageUtility.BinMean(src, 1);

            Assert.That(binned.Size(), Is.EqualTo(src.Size()));
            Assert.That(binned.At<float>(2, 3), Is.EqualTo(5.0f));

            // Independent storage: mutating the source must not touch the result.
            src.Set(2, 3, 99.0f);
            Assert.That(binned.At<float>(2, 3), Is.EqualTo(5.0f));
        }

        [Test]
        public void BinMean_ReplacesEachBlockWithItsExactMean() {
            // Value = x + 10*y, so every 2x2 block's mean is analytically known.
            using var src = Ramp(4, 4, (x, y) => x + 10 * y);
            using var binned = CvImageUtility.BinMean(src, 2);

            Assert.That(binned.Size(), Is.EqualTo(new Size(2, 2)));
            // Block (0,0) covers values {0, 1, 10, 11} -> 5.5
            Assert.That(binned.At<float>(0, 0), Is.EqualTo(5.5f).Within(1e-5));
            // Block (0,1) covers {2, 3, 12, 13} -> 7.5
            Assert.That(binned.At<float>(0, 1), Is.EqualTo(7.5f).Within(1e-5));
            // Block (1,0) covers {20, 21, 30, 31} -> 25.5
            Assert.That(binned.At<float>(1, 0), Is.EqualTo(25.5f).Within(1e-5));
            // Block (1,1) covers {22, 23, 32, 33} -> 27.5
            Assert.That(binned.At<float>(1, 1), Is.EqualTo(27.5f).Within(1e-5));
        }

        [Test]
        public void BinMean_FactorOfThree_AveragesNinePixels() {
            using var src = Ramp(3, 3, (x, y) => 3 * y + x);   // 0..8, mean 4
            using var binned = CvImageUtility.BinMean(src, 3);

            Assert.That(binned.Size(), Is.EqualTo(new Size(1, 1)));
            Assert.That(binned.At<float>(0, 0), Is.EqualTo(4.0f).Within(1e-5));
        }

        [Test]
        public void BinMean_CropsTrailingRowsAndColumnsThatDoNotFillABlock() {
            // 5x5 at factor 2 -> 2x2; the last row and column are dropped rather than blended in as partial blocks.
            using var src = Ramp(5, 5, (x, y) => (x == 4 || y == 4) ? 1000.0f : x + 10 * y);
            using var binned = CvImageUtility.BinMean(src, 2);

            Assert.That(binned.Size(), Is.EqualTo(new Size(2, 2)));
            Assert.Multiple(() => {
                Assert.That(binned.At<float>(0, 0), Is.EqualTo(5.5f).Within(1e-5));
                Assert.That(binned.At<float>(0, 1), Is.EqualTo(7.5f).Within(1e-5));
                Assert.That(binned.At<float>(1, 0), Is.EqualTo(25.5f).Within(1e-5));
                Assert.That(binned.At<float>(1, 1), Is.EqualTo(27.5f).Within(1e-5));
            });
        }

        [Test]
        public void BinMean_PreservesTheOverallLevel() {
            // Mean binning (not summing) is what keeps the pipeline's [0,1] normalization and the absolute
            // saturation threshold meaningful.
            using var src = CvImageUtility.ToOpenCVMat(new ushort[64 * 64], bpp: 16, width: 64, height: 64);
            src.SetTo(new Scalar(0.25));
            using var binned = CvImageUtility.BinMean(src, 4);

            Assert.That(binned.Size(), Is.EqualTo(new Size(16, 16)));
            Assert.That(binned.At<float>(7, 9), Is.EqualTo(0.25f).Within(1e-6));
        }

        [Test]
        public void BinMean_FactorLargerThanTheImage_Throws() {
            using var src = Ramp(3, 3, (x, y) => 1.0f);
            Assert.That(() => CvImageUtility.BinMean(src, 4), Throws.ArgumentException);
        }
    }
}
