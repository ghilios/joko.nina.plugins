#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Fast à trous B3-spline dyadic wavelet cascade, replacing the Cv2.SepFilter2D path in
    /// <see cref="CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer"/>. That path convolves layer i
    /// with a DENSE zero-padded separable kernel of 2^(i+2)+1 taps, of which only 5 are ever non-zero — OpenCV
    /// multiplies mostly zeros, so per-layer cost doubles with each layer (the measured ~2x-wall-per-StructureLayer
    /// scaling). This implementation applies exactly the 5 non-zero taps (offsets ±2^i and ±2^(i+1)) per
    /// direction, so per-layer work is constant, and it is SIMD-vectorized and multithreaded through
    /// <see cref="ParallelExecution"/>.
    ///
    /// Border handling matches the legacy call's BorderTypes.Reflect (OpenCV BORDER_REFLECT: the edge pixel
    /// repeats — index -1 maps to 0, -2 to 1, ...), including multi-bounce reflection when a tap offset exceeds
    /// the image dimension. Results are deterministic (independent of thread partitioning and SIMD width)
    /// because every output pixel is computed independently with a fixed operation order shared by the scalar
    /// and vector paths. Numerically the cascade agrees with the legacy path to float rounding (the
    /// tap-summation order differs), but it is NOT bit-identical to it.
    /// </summary>
    public static class AtrousWaveletFast {
        private const float W0 = 0.375f;
        private const float W1 = 0.25f;
        private const float W2 = 0.0625f;

        /// <summary>
        /// Computes the residual (low-pass) layer after <paramref name="numLayers"/> à trous B3-spline smoothing
        /// passes. Drop-in equivalent of <see cref="CvImageUtility.ComputeResidualAtrousB3SplineDyadicWaveletLayer"/>
        /// (within float rounding). The caller owns the returned Mat; <paramref name="src"/> is not modified.
        /// </summary>
        /// <param name="parallelismKnob">See <see cref="ParallelExecution.ResolveDegreeOfParallelism"/>.</param>
        public static Mat ComputeResidual(Mat src, int numLayers, int parallelismKnob = 0, CancellationToken ct = default) {
            if (src.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }
            if (numLayers <= 0) {
                // The legacy path would return src itself here (an aliasing hazard for the caller's using block);
                // unreachable in production since EffectiveStructureLayers >= 1. Return a copy to stay safe.
                return src.Clone();
            }

            var options = ParallelExecution.CreateOptions(parallelismKnob, ct);
            var horizontal = new Mat(src.Size(), MatType.CV_32F);
            var result = new Mat(src.Size(), MatType.CV_32F);
            try {
                var current = src;
                for (int layer = 0; layer < numLayers; ++layer) {
                    int scale = 1 << layer;
                    HorizontalPass(current, horizontal, scale, options);
                    VerticalPass(horizontal, result, scale, options, subtractFrom: null);
                    current = result;
                }
                return result;
            } catch {
                result.Dispose();
                throw;
            } finally {
                horizontal.Dispose();
            }
        }

        /// <summary>
        /// Fused form of the production wavelet step: computes the <paramref name="numLayers"/>-layer residual and
        /// replaces <paramref name="srcAndDst"/> with clamp(srcAndDst - residual, 0, 1) in a single final pass —
        /// equivalent to <c>ComputeResidual</c> followed by <see cref="CvImageUtility.SubtractInPlace"/>, without
        /// materializing the residual or re-reading the image for the subtract.
        /// </summary>
        public static void ComputeResidualAndSubtractInPlace(Mat srcAndDst, int numLayers, int parallelismKnob = 0, CancellationToken ct = default) {
            if (srcAndDst.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }
            if (numLayers <= 0) {
                throw new ArgumentException("numLayers must be positive", nameof(numLayers));
            }

            var options = ParallelExecution.CreateOptions(parallelismKnob, ct);
            var horizontal = new Mat(srcAndDst.Size(), MatType.CV_32F);
            var smoothed = new Mat(srcAndDst.Size(), MatType.CV_32F);
            try {
                var current = srcAndDst;
                for (int layer = 0; layer < numLayers; ++layer) {
                    int scale = 1 << layer;
                    HorizontalPass(current, horizontal, scale, options);
                    bool last = layer == numLayers - 1;
                    // On the last layer the vertical pass reads only the horizontal buffer (rows y±s, y±2s) plus
                    // srcAndDst's own row y, and writes srcAndDst row y — no cross-row hazard, so writing the
                    // subtracted result straight into the source is safe.
                    VerticalPass(horizontal, last ? srcAndDst : smoothed, scale, options, subtractFrom: last ? srcAndDst : null);
                    current = smoothed;
                }
            } finally {
                horizontal.Dispose();
                smoothed.Dispose();
            }
        }

        private static unsafe void HorizontalPass(Mat src, Mat dst, int scale, ParallelOptions options) {
            int width = src.Width;
            int height = src.Height;
            long srcStep = src.Step() / sizeof(float);
            long dstStep = dst.Step() / sizeof(float);
            var srcBase = (float*)src.DataPointer;
            var dstBase = (float*)dst.DataPointer;
            int s = scale;
            int s2 = 2 * scale;
            int vecCount = Vector<float>.Count;
            var w0 = new Vector<float>(W0);
            var w1 = new Vector<float>(W1);
            var w2 = new Vector<float>(W2);

            Parallel.ForEach(Partitioner.Create(0, height), options, range => {
                for (int y = range.Item1; y < range.Item2; ++y) {
                    float* srow = srcBase + y * srcStep;
                    float* drow = dstBase + y * dstStep;
                    // Interior = columns where all 5 taps are in-bounds without reflection
                    int interiorStart = Math.Min(s2, width);
                    int interiorEnd = Math.Max(width - s2, interiorStart); // exclusive

                    for (int x = 0; x < interiorStart; ++x) {
                        drow[x] = ReflectedTap(srow, x, s, s2, width);
                    }
                    int xi = interiorStart;
                    if (Vector.IsHardwareAccelerated) {
                        for (; xi <= interiorEnd - vecCount; xi += vecCount) {
                            var m2 = Unsafe.ReadUnaligned<Vector<float>>(srow + xi - s2);
                            var m1 = Unsafe.ReadUnaligned<Vector<float>>(srow + xi - s);
                            var c0 = Unsafe.ReadUnaligned<Vector<float>>(srow + xi);
                            var p1 = Unsafe.ReadUnaligned<Vector<float>>(srow + xi + s);
                            var p2 = Unsafe.ReadUnaligned<Vector<float>>(srow + xi + s2);
                            var r = w2 * (m2 + p2) + w1 * (m1 + p1) + w0 * c0;
                            Unsafe.WriteUnaligned(drow + xi, r);
                        }
                    }
                    for (; xi < interiorEnd; ++xi) {
                        drow[xi] = W2 * (srow[xi - s2] + srow[xi + s2]) + W1 * (srow[xi - s] + srow[xi + s]) + W0 * srow[xi];
                    }
                    for (int x = interiorEnd; x < width; ++x) {
                        drow[x] = ReflectedTap(srow, x, s, s2, width);
                    }
                }
            });
        }

        /// <summary>
        /// Vertical 5-tap à trous pass from <paramref name="src"/> into <paramref name="dst"/>. When
        /// <paramref name="subtractFrom"/> is non-null, writes clamp(subtractFrom - filtered, 0, 1) instead of the
        /// filtered value (the fused final subtract; <paramref name="dst"/> may BE <paramref name="subtractFrom"/>).
        /// </summary>
        private static unsafe void VerticalPass(Mat src, Mat dst, int scale, ParallelOptions options, Mat subtractFrom) {
            int width = src.Width;
            int height = src.Height;
            long srcStep = src.Step() / sizeof(float);
            long dstStep = dst.Step() / sizeof(float);
            var srcBase = (float*)src.DataPointer;
            var dstBase = (float*)dst.DataPointer;
            var subBase = subtractFrom != null ? (float*)subtractFrom.DataPointer : null;
            long subStep = subtractFrom != null ? subtractFrom.Step() / sizeof(float) : 0;
            int s = scale;
            int s2 = 2 * scale;
            int vecCount = Vector<float>.Count;
            var w0 = new Vector<float>(W0);
            var w1 = new Vector<float>(W1);
            var w2 = new Vector<float>(W2);
            var zero = Vector<float>.Zero;
            var one = new Vector<float>(1.0f);

            Parallel.ForEach(Partitioner.Create(0, height), options, range => {
                for (int y = range.Item1; y < range.Item2; ++y) {
                    float* rm2 = srcBase + (long)Reflect(y - s2, height) * srcStep;
                    float* rm1 = srcBase + (long)Reflect(y - s, height) * srcStep;
                    float* rc0 = srcBase + (long)y * srcStep;
                    float* rp1 = srcBase + (long)Reflect(y + s, height) * srcStep;
                    float* rp2 = srcBase + (long)Reflect(y + s2, height) * srcStep;
                    float* drow = dstBase + (long)y * dstStep;
                    float* srow = subBase != null ? subBase + (long)y * subStep : null;
                    int x = 0;
                    if (Vector.IsHardwareAccelerated) {
                        for (; x <= width - vecCount; x += vecCount) {
                            var m2 = Unsafe.ReadUnaligned<Vector<float>>(rm2 + x);
                            var m1 = Unsafe.ReadUnaligned<Vector<float>>(rm1 + x);
                            var c0 = Unsafe.ReadUnaligned<Vector<float>>(rc0 + x);
                            var p1 = Unsafe.ReadUnaligned<Vector<float>>(rp1 + x);
                            var p2 = Unsafe.ReadUnaligned<Vector<float>>(rp2 + x);
                            var r = w2 * (m2 + p2) + w1 * (m1 + p1) + w0 * c0;
                            if (srow != null) {
                                var orig = Unsafe.ReadUnaligned<Vector<float>>(srow + x);
                                r = Vector.Min(Vector.Max(orig - r, zero), one);
                            }
                            Unsafe.WriteUnaligned(drow + x, r);
                        }
                    }
                    for (; x < width; ++x) {
                        float r = W2 * (rm2[x] + rp2[x]) + W1 * (rm1[x] + rp1[x]) + W0 * rc0[x];
                        if (srow != null) {
                            // Comparison-based clamp (not Math.Min/Max) so a NaN maps to 0 exactly like the
                            // vector path's Vector.Max/Min (which select the non-NaN operand) and the legacy
                            // Cv2.Max/Min clamp — keeps the scalar tail bit-identical to the SIMD lanes.
                            float d = srow[x] - r;
                            d = d > 0.0f ? d : 0.0f;
                            r = d < 1.0f ? d : 1.0f;
                        }
                        drow[x] = r;
                    }
                }
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float ReflectedTap(float* row, int x, int s, int s2, int width) {
            return W2 * (row[Reflect(x - s2, width)] + row[Reflect(x + s2, width)])
                 + W1 * (row[Reflect(x - s, width)] + row[Reflect(x + s, width)])
                 + W0 * row[x];
        }

        // OpenCV BORDER_REFLECT (fedcba|abcdefgh|hgfedcb): -1 -> 0, -2 -> 1, ...; n -> n-1, n+1 -> n-2, ...
        // Multi-bounce so tap offsets larger than the image dimension still resolve (tiny images).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Reflect(int i, int n) {
            if ((uint)i < (uint)n) {
                return i;
            }
            if (n == 1) {
                return 0;
            }
            do {
                i = i < 0 ? -i - 1 : 2 * n - 1 - i;
            } while ((uint)i >= (uint)n);
            return i;
        }
    }
}
