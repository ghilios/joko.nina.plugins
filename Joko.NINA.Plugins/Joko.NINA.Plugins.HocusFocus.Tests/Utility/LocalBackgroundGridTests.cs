#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    /// <summary>
    /// Unit tests for the pure coarse-grid local-background helper that backs spatially-adaptive binarization.
    /// It computes, per block, the robust background (block median) and noise (1.4826·MAD, floored), matching
    /// tools/golden/snr_ref.py:coarse_bg semantics. The upsample (Cv2.Resize bilinear) is intentionally NOT tested
    /// here — only the grid reduction, which is the unit under test.
    /// </summary>
    [TestFixture]
    public class LocalBackgroundGridTests {

        private static Mat MakeF32(int width, int height, Func<int, int, float> valueAt) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            unsafe {
                var p = (float*)mat.DataPointer;
                for (var y = 0; y < height; ++y) {
                    for (var x = 0; x < width; ++x) {
                        p[y * width + x] = valueAt(x, y);
                    }
                }
            }
            return mat;
        }

        [Test]
        public void RejectsNonF32() {
            using var mat = new Mat(new Size(8, 8), MatType.CV_8UC1);
            Assert.Throws<ArgumentException>(() => CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 4, sigmaFloor: 0f));
        }

        [Test]
        public void RejectsNonPositiveBlockSize() {
            using var mat = MakeF32(8, 8, (x, y) => 0.5f);
            Assert.Throws<ArgumentException>(() => CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 0, sigmaFloor: 0f));
        }

        [Test]
        public void UniformImage_FlatSurface_MedianEqualsValue_SigmaIsZero() {
            using var mat = MakeF32(16, 16, (x, y) => 0.5f);
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 8, sigmaFloor: 0f);
            Assert.Multiple(() => {
                Assert.That(grid.GridRows, Is.EqualTo(2));
                Assert.That(grid.GridCols, Is.EqualTo(2));
                for (var r = 0; r < grid.GridRows; ++r) {
                    for (var c = 0; c < grid.GridCols; ++c) {
                        Assert.That(grid.MedianAt(r, c), Is.EqualTo(0.5f).Within(1e-6), $"median[{r},{c}]");
                        Assert.That(grid.SigmaAt(r, c), Is.EqualTo(0.0f).Within(1e-6), $"sigma[{r},{c}]");
                    }
                }
            });
        }

        [Test]
        public void KnownBlock_UpperMiddleMedian_And_ScaledMad() {
            // 2x2 block of {1,2,3,4}. Upper-middle median (length>>1) = sorted[2] = 3 (matches CalculateStatistics).
            // |x-3| = {2,1,0,1} -> sorted {0,1,1,2} -> MAD = sorted[2] = 1 -> sigma = 1*1.4826.
            using var mat = MakeF32(2, 2, (x, y) => 1f + x + 2 * y); // (0,0)=1 (1,0)=2 (0,1)=3 (1,1)=4
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 2, sigmaFloor: 0f);
            Assert.Multiple(() => {
                Assert.That(grid.GridRows, Is.EqualTo(1));
                Assert.That(grid.GridCols, Is.EqualTo(1));
                Assert.That(grid.MedianAt(0, 0), Is.EqualTo(3f).Within(1e-6));
                Assert.That(grid.SigmaAt(0, 0), Is.EqualTo(1.4826f).Within(1e-4));
            });
        }

        [Test]
        public void NoisyRegion_HasLargerSigmaThanCleanRegion() {
            // Left half (cols 0..127) uniform -> sigma ~ 0; right half (cols 128..255) checkerboard 0.4/0.6
            // -> MAD = 0.2 -> sigma = 0.2*1.4826 ~ 0.2965. Grid is 1 row x 2 cols at blockSize 128.
            using var mat = MakeF32(256, 128, (x, y) => x < 128 ? 0.5f : (((x + y) % 2 == 0) ? 0.6f : 0.4f));
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 128, sigmaFloor: 0f);
            Assert.Multiple(() => {
                Assert.That(grid.GridRows, Is.EqualTo(1));
                Assert.That(grid.GridCols, Is.EqualTo(2));
                Assert.That(grid.SigmaAt(0, 0), Is.EqualTo(0.0f).Within(1e-6), "clean block sigma");
                Assert.That(grid.SigmaAt(0, 1), Is.GreaterThan(0.25f), "noisy block sigma");
                Assert.That(grid.SigmaAt(0, 1), Is.GreaterThan(grid.SigmaAt(0, 0)));
            });
        }

        [Test]
        public void SigmaFloor_IsApplied_WhenMadIsZero() {
            using var mat = MakeF32(8, 8, (x, y) => 0.3f);
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 8, sigmaFloor: 0.05f);
            Assert.That(grid.SigmaAt(0, 0), Is.EqualTo(0.05f).Within(1e-6));
        }

        [Test]
        public void BlockSizeLargerThanImage_ProducesSingleGlobalCell() {
            using var mat = MakeF32(8, 8, (x, y) => x < 4 ? 0.2f : 0.8f);
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 64, sigmaFloor: 0f);
            Assert.Multiple(() => {
                Assert.That(grid.GridRows, Is.EqualTo(1));
                Assert.That(grid.GridCols, Is.EqualTo(1));
                // 32 px at 0.2 and 32 px at 0.8; upper-middle median (index 32) = 0.8; |x-0.8| -> MAD = 0.6.
                Assert.That(grid.MedianAt(0, 0), Is.EqualTo(0.8f).Within(1e-6));
                Assert.That(grid.SigmaAt(0, 0), Is.EqualTo(0.6f * 1.4826f).Within(1e-4));
            });
        }

        [Test]
        public void NonMultipleSize_CeilGrid_PartialEdgeBlocksComputedFromTheirOwnPixels() {
            // 5x5, blockSize 4 -> ceil grid 2x2. The bottom-right cell is a single pixel (4,4): median = that pixel,
            // MAD of a 1-element block = 0 -> sigma = floor.
            using var mat = MakeF32(5, 5, (x, y) => 0.1f * (x + y)); // (4,4) = 0.8
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 4, sigmaFloor: 0f);
            Assert.Multiple(() => {
                Assert.That(grid.GridRows, Is.EqualTo(2));
                Assert.That(grid.GridCols, Is.EqualTo(2));
                Assert.That(grid.MedianAt(1, 1), Is.EqualTo(0.8f).Within(1e-6), "single-pixel corner median");
                Assert.That(grid.SigmaAt(1, 1), Is.EqualTo(0.0f).Within(1e-6), "single-pixel corner sigma");
            });
        }

        [Test]
        public void RowMajorIndexerMatchesBackingArray() {
            using var mat = MakeF32(24, 16, (x, y) => 0.01f * (x % 7) + 0.02f * (y % 5));
            var grid = CvImageUtility.ComputeLocalBackgroundGrid(mat, blockSize: 8, sigmaFloor: 0f);
            for (var r = 0; r < grid.GridRows; ++r) {
                for (var c = 0; c < grid.GridCols; ++c) {
                    Assert.That(grid.MedianAt(r, c), Is.EqualTo(grid.Median[r * grid.GridCols + c]));
                    Assert.That(grid.SigmaAt(r, c), Is.EqualTo(grid.Sigma[r * grid.GridCols + c]));
                }
            }
        }

        [Test]
        public void BinarizeAdaptive_PerPixelThreshold_StrictGreater() {
            using var src = MakeF32(2, 2, (x, y) => new[] { 0.1f, 0.4f, 0.6f, 0.9f }[y * 2 + x]);
            using var surface = MakeF32(2, 2, (x, y) => new[] { 0.2f, 0.4f, 0.5f, 0.95f }[y * 2 + x]);
            using var dst = new Mat();
            CvImageUtility.BinarizeAdaptive(src, dst, surface);
            unsafe {
                var p = (float*)dst.DataPointer;
                // 0.1>0.2 no; 0.4>0.4 no (strict); 0.6>0.5 yes; 0.9>0.95 no
                Assert.Multiple(() => {
                    Assert.That(p[0], Is.EqualTo(0.0f));
                    Assert.That(p[1], Is.EqualTo(0.0f));
                    Assert.That(p[2], Is.EqualTo(1.0f));
                    Assert.That(p[3], Is.EqualTo(0.0f));
                });
            }
        }

        [Test]
        public void BinarizeAdaptive_ConstantSurface_MatchesScalarBinarize() {
            const float threshold = 0.5f;
            using var src = MakeF32(8, 8, (x, y) => 0.05f * (x + y)); // 0.0 .. 0.7
            using var surface = MakeF32(8, 8, (x, y) => threshold);
            using var adaptive = new Mat();
            using var scalar = new Mat();
            CvImageUtility.BinarizeAdaptive(src, adaptive, surface);
            CvImageUtility.Binarize(src, scalar, threshold);
            unsafe {
                var a = (float*)adaptive.DataPointer;
                var s = (float*)scalar.DataPointer;
                for (var i = 0; i < 64; ++i) {
                    Assert.That(a[i], Is.EqualTo(s[i]), $"pixel {i}");
                }
            }
        }

        [Test]
        public void BinarizeAdaptive_RejectsSizeMismatch() {
            using var src = MakeF32(4, 4, (x, y) => 0.5f);
            using var surface = MakeF32(2, 2, (x, y) => 0.5f);
            using var dst = new Mat();
            Assert.Throws<ArgumentException>(() => CvImageUtility.BinarizeAdaptive(src, dst, surface));
        }
    }
}
