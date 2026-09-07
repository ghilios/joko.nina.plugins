#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILGPU;
using ILGPU.Runtime;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Gpu {

    /// <summary>
    /// GPU kernels for the star detector's EARLY pipeline span (StarDetector.BuildDetectionContextInternal
    /// steps 1-5b: hotpixel filter, Gaussians, à-trous wavelet, K-σ noise estimate, histogram median).
    /// Each kernel replicates its CPU oracle's exact semantics:
    ///
    /// - median3x3: order statistic of the 3x3 neighborhood, BORDER_REPLICATE (Cv2.MedianBlur on CV_32F).
    /// - hotpixel thresholding: |src - median| &gt; threshold (strict, float-cast threshold) selects the
    ///   median, and the hot count matches Cv2.Threshold(THRESH_BINARY) + CountNonZero (HotpixelFiltering.cs).
    /// - separable Gaussian: host-computed taps identical to Cv2.GetGaussianKernel (double, normalized),
    ///   accumulated in float in ascending-tap order like OpenCV's row filter, BORDER_REFLECT.
    /// - à-trous B3: the 5-tap expression in AtrousWaveletFast, same op order
    ///   (W2*(m2+p2) + W1*(m1+p1) + W0*c0), same Reflect() multi-bounce border, same comparison-based
    ///   NaN-to-0 clamp on the fused final subtract.
    /// - K-σ: per-iteration masked (InRange-inclusive) count/Σ/Σ² reduction in double, σ = sqrt(Σ²/n - mean²),
    ///   matching Cv2.MeanStdDev's masked semantics (CvImageUtility.KappaSigmaNoiseEstimate drives the loop).
    /// - histogram median: bucket = clamp((int)floor((double)v * 65536)) exactly as
    ///   CalculateStatistics_Histogram's linear path; two passes (coarse 256, fine 256 within the median's
    ///   coarse bucket) reconstruct the exact 65536-bin cumulative walk on the host.
    ///
    /// All device math is PTX-native (add/mul/compare/abs/min/max/floor); no XMath/LibDevice (broken on
    /// Blackwell until ILGPU 1.6).
    /// </summary>
    public static class GpuEarlyKernels {

        // ---- median 3x3 (BORDER_REPLICATE) --------------------------------------------------------------

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private static void Swap(ref float a, ref float b) {
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            a = lo;
            b = hi;
        }

        /// <summary>Median of 9 via a sorting network; exact order statistic like OpenCV's sort-net.</summary>
        private static float Median9(float p0, float p1, float p2, float p3, float p4, float p5, float p6, float p7, float p8) {
            Swap(ref p1, ref p2); Swap(ref p4, ref p5); Swap(ref p7, ref p8);
            Swap(ref p0, ref p1); Swap(ref p3, ref p4); Swap(ref p6, ref p7);
            Swap(ref p1, ref p2); Swap(ref p4, ref p5); Swap(ref p7, ref p8);
            Swap(ref p0, ref p3); Swap(ref p5, ref p8); Swap(ref p4, ref p7);
            Swap(ref p3, ref p6); Swap(ref p1, ref p4); Swap(ref p2, ref p5);
            Swap(ref p4, ref p7); Swap(ref p4, ref p2); Swap(ref p6, ref p4);
            Swap(ref p4, ref p2);
            return p4;
        }

        public static void Median3x3Kernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, int width, int height) {
            int y = i / width;
            int x = i - y * width;
            int xm = Clamp(x - 1, 0, width - 1);
            int xp = Clamp(x + 1, 0, width - 1);
            int ym = Clamp(y - 1, 0, height - 1);
            int yp = Clamp(y + 1, 0, height - 1);
            dst[i] = Median9(
                src[ym * width + xm], src[ym * width + x], src[ym * width + xp],
                src[y * width + xm], src[y * width + x], src[y * width + xp],
                src[yp * width + xm], src[yp * width + x], src[yp * width + xp]);
        }

        /// <summary>
        /// Fused hotpixel thresholding: where |src - median| &gt; threshold (strict), take the median and count
        /// it; else keep src. Writes into dst; hotCount accumulates the number of replaced pixels.
        /// </summary>
        public static void HotpixelSelectKernel(Index1D i, ArrayView<float> src, ArrayView<float> median, ArrayView<float> dst, float threshold, ArrayView<long> hotCount) {
            var s = src[i];
            var m = median[i];
            var isHot = Math.Abs(s - m) > threshold;
            dst[i] = isHot ? m : s;
            if (isHot) {
                Atomic.Add(ref hotCount[0], 1L);
            }
        }

        // ---- separable convolution (BORDER_REFLECT), generic taps ---------------------------------------

        // OpenCV BORDER_REFLECT (fedcba|abcdefgh|hgfedcb): -1 -> 0, -2 -> 1, ...; n -> n-1, ...; multi-bounce.
        private static int Reflect(int i, int n) {
            if (i >= 0 & i < n) {
                return i;
            }
            if (n == 1) {
                return 0;
            }
            while (true) {
                i = i < 0 ? -i - 1 : 2 * n - 1 - i;
                if (i >= 0 & i < n) {
                    return i;
                }
            }
        }

        /// <summary>Row convolution: dst(y,x) = Σ_k taps[k] · src(y, reflect(x + k - radius)).</summary>
        public static void ConvolveRowKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, ArrayView<float> taps, int radius, int width, int height) {
            int y = i / width;
            int x = i - y * width;
            long row = (long)y * width;
            float sum = 0.0f;
            int n = 2 * radius + 1;
            for (int k = 0; k < n; k++) {
                sum += taps[k] * src[row + Reflect(x + k - radius, width)];
            }
            dst[i] = sum;
        }

        /// <summary>Column convolution: dst(y,x) = Σ_k taps[k] · src(reflect(y + k - radius), x).</summary>
        public static void ConvolveColKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, ArrayView<float> taps, int radius, int width, int height) {
            int y = i / width;
            int x = i - y * width;
            float sum = 0.0f;
            int n = 2 * radius + 1;
            for (int k = 0; k < n; k++) {
                sum += taps[k] * src[(long)Reflect(y + k - radius, height) * width + x];
            }
            dst[i] = sum;
        }

        // ---- à-trous B3 wavelet (AtrousWaveletFast port) ------------------------------------------------

        private const float W0 = 0.375f;
        private const float W1 = 0.25f;
        private const float W2 = 0.0625f;

        public static void AtrousHorizontalKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, int scale, int width, int height) {
            int y = i / width;
            int x = i - y * width;
            long row = (long)y * width;
            int s = scale;
            int s2 = 2 * scale;
            // Same expression + op order as AtrousWaveletFast (interior and border alike; Reflect is identity in-bounds).
            float r = W2 * (src[row + Reflect(x - s2, width)] + src[row + Reflect(x + s2, width)])
                    + W1 * (src[row + Reflect(x - s, width)] + src[row + Reflect(x + s, width)])
                    + W0 * src[row + x];
            dst[i] = r;
        }

        /// <summary>
        /// Vertical pass. When subtractFinal != 0, writes clamp(subtractFrom - filtered, 0, 1) with the same
        /// comparison-based NaN-to-0 clamp as AtrousWaveletFast.VerticalPass (dst may alias subtractFrom).
        /// </summary>
        public static void AtrousVerticalKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, ArrayView<float> subtractFrom, int subtractFinal, int scale, int width, int height) {
            int y = i / width;
            int x = i - y * width;
            int s = scale;
            int s2 = 2 * scale;
            float r = W2 * (src[(long)Reflect(y - s2, height) * width + x] + src[(long)Reflect(y + s2, height) * width + x])
                    + W1 * (src[(long)Reflect(y - s, height) * width + x] + src[(long)Reflect(y + s, height) * width + x])
                    + W0 * src[(long)y * width + x];
            if (subtractFinal != 0) {
                float d = subtractFrom[i] - r;
                d = d > 0.0f ? d : 0.0f;
                r = d < 1.0f ? d : 1.0f;
            }
            dst[i] = r;
        }

        // ---- K-σ masked reduction -----------------------------------------------------------------------

        /// <summary>
        /// One K-σ iteration's reduction: over pixels with lower &lt;= v &lt;= upper (InRange-inclusive),
        /// accumulate count into out[0], Σ into out[1], Σ² into out[2] (doubles). Grid-stride; one atomic
        /// triple per thread.
        /// </summary>
        public static void MaskedMomentsKernel(Index1D i, ArrayView<float> src, ArrayView<double> outMoments, float lower, float upper, int gridStride, long length) {
            long count = 0;
            double sum = 0.0;
            double sumSq = 0.0;
            for (long idx = i; idx < length; idx += gridStride) {
                var v = src[idx];
                if (v >= lower & v <= upper) {
                    count++;
                    double d = v;
                    sum += d;
                    sumSq += d * d;
                }
            }
            if (count > 0) {
                Atomic.Add(ref outMoments[0], (double)count);
                Atomic.Add(ref outMoments[1], sum);
                Atomic.Add(ref outMoments[2], sumSq);
            }
        }

        // ---- 65536-bin histogram median, two-pass (coarse 256 then fine 256) ----------------------------

        /// <summary>
        /// Histogram pass. Bucket index is computed exactly like CalculateStatistics_Histogram's linear path:
        /// clamp((int)floor((double)v * 65536), 0, 65535). With coarseSelect &lt; 0 the 256-bin COARSE histogram
        /// (bucket &gt;&gt; 8) is accumulated; otherwise only pixels whose coarse bucket equals coarseSelect
        /// accumulate their FINE index (bucket &amp; 255). Caller must zero outHist first.
        /// </summary>
        public static void HistogramKernel(Index1D i, ArrayView<float> src, ArrayView<uint> outHist, int coarseSelect, int gridStride, long length) {
            for (long idx = i; idx < length; idx += gridStride) {
                double v = src[idx];
                int bucket = (int)Math.Floor(v * 65536.0);
                if (bucket < 0) bucket = 0;
                if (bucket > 65535) bucket = 65535;
                if (coarseSelect < 0) {
                    Atomic.Add(ref outHist[bucket >> 8], 1u);
                } else if ((bucket >> 8) == coarseSelect) {
                    Atomic.Add(ref outHist[bucket & 255], 1u);
                }
            }
        }

        // ---- local-background grid: exact per-block median/MAD via radix-select -------------------------

        /// <summary>Order-preserving map from float to uint (standard sign-flip transform).</summary>
        private static uint MapFloat(float v) {
            uint b = Interop.FloatAsInt(v);
            return (b & 0x80000000u) != 0 ? ~b : (b | 0x80000000u);
        }

        private static float UnmapFloat(uint k) {
            uint b = (k & 0x80000000u) != 0 ? (k & 0x7FFFFFFFu) : ~k;
            return Interop.IntAsFloat(b);
        }

        /// <summary>
        /// Explicitly-grouped kernel: one group per grid block, computing the block's EXACT median and
        /// 1.4826·MAD (floored) with the same order statistics as CvImageUtility.ComputeLocalBackgroundGrid
        /// (median = element at index count&gt;&gt;1; MAD likewise over |v - median|; σ = (float)(mad · 1.4826)
        /// floored at sigmaFloor). Selection is a 4-pass MSB-first radix over order-preserving float keys —
        /// exact, so the grids are bit-identical to the CPU's for NaN-free inputs.
        /// </summary>
        public static void LocalBackgroundGridKernel(
            ArrayView<float> image, int width, int height, int blockSize, int gridCols,
            ArrayView<float> outMedian, ArrayView<float> outSigma, float sigmaFloor) {
            var hist = SharedMemory.Allocate<uint>(256);
            var shared = SharedMemory.Allocate<uint>(2); // [0] = prefix (high bits), [1] = remaining k

            int groupIdx = Grid.IdxX;
            int blockRow = groupIdx / gridCols;
            int blockCol = groupIdx - blockRow * gridCols;
            int y0 = blockRow * blockSize;
            int y1 = Math.Min(y0 + blockSize, height);
            int x0 = blockCol * blockSize;
            int x1 = Math.Min(x0 + blockSize, width);
            int bw = x1 - x0;
            int count = (y1 - y0) * bw;
            uint kTarget = (uint)(count >> 1);

            float median = 0f;
            for (int phase = 0; phase < 2; phase++) {
                uint prefix = 0;
                int prefixBits = 0;
                if (Group.IdxX == 0) {
                    shared[1] = kTarget;
                }
                Group.Barrier();
                for (int pass = 0; pass < 4; pass++) {
                    for (int t = Group.IdxX; t < 256; t += Group.DimX) {
                        hist[t] = 0;
                    }
                    Group.Barrier();
                    int shift = 24 - 8 * pass;
                    for (int idx = Group.IdxX; idx < count; idx += Group.DimX) {
                        int yy = y0 + idx / bw;
                        int xx = x0 + idx - (idx / bw) * bw;
                        float v = image[(long)yy * width + xx];
                        if (phase == 1) {
                            v = Math.Abs(v - median);
                        }
                        uint key = MapFloat(v);
                        bool match = prefixBits == 0 || (key >> (32 - prefixBits)) == prefix;
                        if (match) {
                            Atomic.Add(ref hist[(int)((key >> shift) & 255u)], 1u);
                        }
                    }
                    Group.Barrier();
                    if (Group.IdxX == 0) {
                        uint kRem = shared[1];
                        uint cumulative = 0;
                        int digit = 255;
                        for (int d = 0; d < 256; d++) {
                            uint c = hist[d];
                            if (cumulative + c > kRem) {
                                digit = d;
                                break;
                            }
                            cumulative += c;
                        }
                        shared[1] = kRem - cumulative;
                        shared[0] = (prefix << 8) | (uint)digit;
                    }
                    Group.Barrier();
                    prefix = shared[0];
                    prefixBits += 8;
                    Group.Barrier();
                }
                float selected = UnmapFloat(prefix);
                if (phase == 0) {
                    median = selected;
                } else if (Group.IdxX == 0) {
                    int gi = blockRow * gridCols + blockCol;
                    outMedian[gi] = median;
                    float sig = (float)((double)selected * 1.4826);
                    if (sig < sigmaFloor) {
                        sig = sigmaFloor;
                    }
                    outSigma[gi] = sig;
                }
                Group.Barrier();
            }
        }

        // ---- host-side helpers --------------------------------------------------------------------------

        /// <summary>
        /// Gaussian taps identical to Cv2.GetGaussianKernel(kernelSize, sigma, CV_64F): exp(-x²/(2σ²)) at
        /// x = i - (n-1)/2 in double, normalized to sum 1, then narrowed to float exactly as OpenCV's
        /// FilterEngine does for CV_32F images.
        /// </summary>
        public static float[] GaussianTaps(int kernelSize, double sigma) {
            if (sigma <= 0.0) {
                sigma = 0.159758 * kernelSize;
            }
            var taps = new double[kernelSize];
            double scale2X = -0.5 / (sigma * sigma);
            double sum = 0.0;
            for (int i = 0; i < kernelSize; i++) {
                double x = i - (kernelSize - 1) * 0.5;
                taps[i] = Math.Exp(scale2X * x * x);
                sum += taps[i];
            }
            var result = new float[kernelSize];
            for (int i = 0; i < kernelSize; i++) {
                result[i] = (float)(taps[i] / sum);
            }
            return result;
        }

        /// <summary>
        /// Reconstructs CalculateStatistics_Histogram's exact median-interpolation walk from the two-pass
        /// histograms: cumulativeBeforeCoarse = Σ coarse[0..C-1], then walk fine[] until
        /// cumulative &gt;= numPixels/2, interpolating within the winning 1/65536-wide bucket.
        /// </summary>
        public static double MedianFromTwoPass(uint[] coarse, uint[] fine, int coarseBucket, long numPixels) {
            double target = numPixels / 2.0;
            ulong cumulative = 0;
            for (int i = 0; i < coarseBucket; i++) {
                cumulative += coarse[i];
            }
            for (int f = 0; f < 256; f++) {
                cumulative += fine[f];
                if (cumulative >= target) {
                    int bucket = (coarseBucket << 8) | f;
                    var interpolationRatio = (cumulative - target) / fine[f];
                    double thisBucket = (double)bucket / 65536.0;
                    double nextBucket = bucket < 65535 ? (double)(bucket + 1) / 65536.0 : 1.0;
                    return thisBucket + (nextBucket - thisBucket) * interpolationRatio;
                }
            }
            // Unreachable when coarseBucket was chosen from the same data.
            return (double)coarseBucket / 256.0;
        }

        /// <summary>Finds the coarse bucket whose cumulative count first reaches numPixels/2.</summary>
        public static int MedianCoarseBucket(uint[] coarse, long numPixels) {
            double target = numPixels / 2.0;
            ulong cumulative = 0;
            for (int i = 0; i < 256; i++) {
                cumulative += coarse[i];
                if (cumulative >= target) {
                    return i;
                }
            }
            return 255;
        }
    }
}
