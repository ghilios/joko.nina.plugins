using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    /// <summary>
    /// AtrousWaveletFast (the production wavelet) must be numerically equivalent (within float rounding) to the
    /// retained reference oracle CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer (dense
    /// zero-padded Cv2.SepFilter2D), including BORDER_REFLECT handling, and deterministic regardless of
    /// parallelism. Detection-level pinning lives in the StarDetectorEquivalenceTests golden signatures, which
    /// run through this implementation.
    /// </summary>
    [TestFixture]
    public class AtrousWaveletFastTests {

        // Worst-case float rounding drift for a [0,1] image over numLayers × 2 separable passes with a
        // different tap-summation order than OpenCV. A real border/indexing bug produces errors orders of
        // magnitude above this (the taps carry 6-38% of the value each).
        private const double EquivalenceTolerance = 5e-6;

        private static Mat CreateRandomMat(int width, int height, int seed) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            var random = new Random(seed);
            unsafe {
                var p = (float*)mat.DataPointer;
                long step = mat.Step() / sizeof(float);
                for (int y = 0; y < height; ++y) {
                    for (int x = 0; x < width; ++x) {
                        p[y * step + x] = (float)random.NextDouble();
                    }
                }
            }
            return mat;
        }

        private static unsafe double MaxAbsDiff(Mat a, Mat b) {
            Assert.That(a.Size(), Is.EqualTo(b.Size()));
            var pa = (float*)a.DataPointer;
            var pb = (float*)b.DataPointer;
            long stepA = a.Step() / sizeof(float);
            long stepB = b.Step() / sizeof(float);
            double maxDiff = 0.0;
            for (int y = 0; y < a.Height; ++y) {
                for (int x = 0; x < a.Width; ++x) {
                    var diff = Math.Abs((double)pa[y * stepA + x] - pb[y * stepB + x]);
                    if (diff > maxDiff) {
                        maxDiff = diff;
                    }
                }
            }
            return maxDiff;
        }

        [Test]
        public void Reflect_MatchesOpenCvBorderReflect() {
            // Cross-check the multi-bounce reflection against OpenCV's own borderInterpolate for every
            // length/offset combination the à trous taps can produce (offsets up to ±2·2^i).
            for (int n = 1; n <= 12; ++n) {
                for (int i = -3 * n; i < 4 * n; ++i) {
                    Assert.That(AtrousWaveletFast.Reflect(i, n),
                        Is.EqualTo(Cv2.BorderInterpolate(i, n, BorderTypes.Reflect)),
                        $"Reflect({i}, {n})");
                }
            }
        }

        // Sizes chosen to exercise: SIMD interior + scalar tail (odd widths), pure-border columns
        // (width < 4·2^i at high layers), multi-bounce reflection (tiny images), and non-square shapes
        // that would catch a transposed-axis bug.
        [TestCase(64, 64, 1)]
        [TestCase(64, 64, 4)]
        [TestCase(129, 97, 4)]
        [TestCase(257, 193, 6)]
        [TestCase(61, 47, 5)]
        [TestCase(40, 30, 4)]
        [TestCase(33, 9, 3)]
        public void ComputeResidual_MatchesLegacyImplementation(int width, int height, int numLayers) {
            using var src = CreateRandomMat(width, height, seed: width * 1000 + height * 10 + numLayers);
            using var legacy = CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer(src, numLayers);
            using var fast = AtrousWaveletFast.ComputeResidual(src, numLayers);
            var maxDiff = MaxAbsDiff(legacy, fast);
            Assert.That(maxDiff, Is.LessThanOrEqualTo(EquivalenceTolerance),
                $"fast à trous residual must match the legacy SepFilter2D result (maxAbsDiff={maxDiff:E3})");
        }

        [Test]
        public void ComputeResidual_FlatImage_PreservesMean() {
            using var src = SyntheticGaussianStarImage.CreateFlat(32, 32, 0.5f);
            using var residual = AtrousWaveletFast.ComputeResidual(src, numLayers: 3);
            var stats = CvImageUtility.CalculateStatistics(residual);
            Assert.That(stats.Mean, Is.EqualTo(0.5).Within(1e-3));
        }

        [Test]
        public void ComputeResidual_DoesNotModifySource() {
            using var src = CreateRandomMat(96, 80, seed: 12345);
            using var copy = src.Clone();
            using var residual = AtrousWaveletFast.ComputeResidual(src, numLayers: 4);
            Assert.That(MaxAbsDiff(src, copy), Is.EqualTo(0.0), "source image must not be modified");
        }

        [Test]
        public void ComputeResidual_DeterministicAcrossRunsAndParallelism() {
            using var src = CreateRandomMat(200, 150, seed: 424242);
            using var parallelA = AtrousWaveletFast.ComputeResidual(src, numLayers: 5);
            using var parallelB = AtrousWaveletFast.ComputeResidual(src, numLayers: 5);
            using var sequential = AtrousWaveletFast.ComputeResidual(src, numLayers: 5, parallelismKnob: 1);
            Assert.Multiple(() => {
                Assert.That(MaxAbsDiff(parallelA, parallelB), Is.EqualTo(0.0), "repeat runs must be bit-identical");
                Assert.That(MaxAbsDiff(parallelA, sequential), Is.EqualTo(0.0), "parallelism must not change results");
            });
        }

        [Test]
        public void ComputeResidualAndSubtractInPlace_BitIdenticalToComputeThenSubtract() {
            using var fused = CreateRandomMat(150, 110, seed: 777);
            using var unfused = fused.Clone();

            AtrousWaveletFast.ComputeResidualAndSubtractInPlace(fused, numLayers: 4);
            using (var residual = AtrousWaveletFast.ComputeResidual(unfused, numLayers: 4)) {
                CvImageUtility.SubtractInPlace(unfused, residual);
            }

            Assert.That(MaxAbsDiff(fused, unfused), Is.EqualTo(0.0),
                "the fused subtract must be bit-identical to compute-then-SubtractInPlace");
        }

        [Test]
        public void ComputeResidualAndSubtractInPlace_NaNPixel_ClampsToZeroInVectorAndScalarColumns() {
            // A NaN source pixel must clamp to 0 identically in SIMD-covered columns and the scalar tail
            // (and thereby match the legacy Cv2.Max/Min clamp, which also maps NaN to 0) — the output must
            // not depend on column position or vector width.
            using var mat = CreateRandomMat(150, 110, seed: 999);
            unsafe {
                var p = (float*)mat.DataPointer;
                long step = mat.Step() / sizeof(float);
                p[50 * step + 100] = float.NaN; // interior of any vector loop
                p[50 * step + 147] = float.NaN; // scalar tail for Vector<float>.Count of 8 (width 150)
            }
            AtrousWaveletFast.ComputeResidualAndSubtractInPlace(mat, numLayers: 1);
            unsafe {
                var p = (float*)mat.DataPointer;
                long step = mat.Step() / sizeof(float);
                Assert.Multiple(() => {
                    Assert.That(p[50 * step + 100], Is.EqualTo(0.0f), "vector-path NaN must clamp to 0");
                    Assert.That(p[50 * step + 147], Is.EqualTo(0.0f), "scalar-tail NaN must clamp to 0");
                });
            }
        }

    }
}
