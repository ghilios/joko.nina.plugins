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
using ILGPU.Runtime.Cuda;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TestApp.Gpu {

    /// <summary>Subset of StarDetectorParams the EARLY GPU span depends on.</summary>
    public sealed class GpuEarlyParams {
        public bool HotpixelFiltering;
        public bool HotpixelThresholdingEnabled;
        public double HotpixelThreshold;
        public int NoiseReductionRadius;
        public bool StarMeasurementNoiseReductionEnabled;
        public double NoiseClippingMultiplier;
        public int EffectiveStructureLayers;
        public int StructureLayers;
        public bool LocallyAdaptiveBinarization;
        public int AdaptiveNoiseBlockSize;
    }

    public sealed class GpuEarlyResult : IDisposable {
        /// <summary>Post-PostWaveletConvolution structure map (caller owns).</summary>
        public Mat StructureMap;

        /// <summary>The prepared measurement image (hotpixel/noise-reduction applied), or null when the
        /// span did not mutate it (caller keeps using its input Mat).</summary>
        public Mat MeasurementImage;

        public CvImageUtility.KappaSigmaNoiseEstimateResult StructureNoise;
        public CvImageUtility.KappaSigmaNoiseEstimateResult MeasurementNoise;
        public CvImageUtility.LocalBackgroundGrid SigmaGrid;
        public CvImageUtility.LocalBackgroundGrid AdaptiveMedianGrid;
        public double StructureMapMedian;
        public long HotpixelCount;
        public Dictionary<string, double> StageMs;

        public void Dispose() {
            StructureMap?.Dispose();
            MeasurementImage?.Dispose();
            StructureMap = null;
            MeasurementImage = null;
        }
    }

    /// <summary>
    /// Executes the EARLY pipeline span (StarDetector.BuildDetectionContextInternal steps 1-5b) on the GPU:
    /// hotpixel filter, Gaussians, structure-source copies, K-σ noise estimates, à-trous wavelet residual
    /// subtract, post-wavelet Gaussian, the 65536-bin histogram median, and the adaptive-binarization grids
    /// (exact per-block radix-select). Each instance owns its own CudaStream, so pooled instances overlap
    /// transfers and compute; Mat↔device copies go directly through the Mat's memory (pageable — measured
    /// within 3% of page-locked on this PCIe Gen3 box, and it saves a staging memcpy each way). Not
    /// thread-safe: one instance = one working set; callers serialize or pool instances.
    /// </summary>
    public sealed class GpuEarlyChain : IDisposable {
        // Mirrors StarDetector.AdaptiveBinarizationSigmaFloor (private const there).
        private const float AdaptiveBinarizationSigmaFloor = 1e-6f;

        // Grid-stride thread count for the reduction/histogram kernels: enough blocks to saturate the
        // memory system, few enough that per-thread atomics stay cheap.
        private const int ReductionThreads = 128 * 1024;

        // Enough grid cells for a 61 MP frame at the minimum practical block size we bench (64 px).
        private const int MaxGridCells = 32 * 1024;

        private readonly CudaAccelerator acc;
        private readonly AcceleratorStream stream;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<float>, int, int> median3x3;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, float, ArrayView<long>> hotpixelSelect;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int> convolveRow;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int> convolveCol;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<float>, int, int, int> atrousH;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int, int> atrousV;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<double>, float, float, int, long> maskedMoments;
        private readonly Action<AcceleratorStream, Index1D, ArrayView<float>, ArrayView<uint>, int, int, long> histogram;
        private readonly Action<AcceleratorStream, KernelConfig, ArrayView<float>, int, int, int, int, ArrayView<float>, ArrayView<float>, float> localGrid;

        private int width;
        private int height;
        private long length;
        private MemoryBuffer1D<float, Stride1D.Dense> dMeas;
        private MemoryBuffer1D<float, Stride1D.Dense> dNoiseReduced;
        private MemoryBuffer1D<float, Stride1D.Dense> dStructure;
        private MemoryBuffer1D<float, Stride1D.Dense> dSmooth;
        private MemoryBuffer1D<float, Stride1D.Dense> dTmp;
        private MemoryBuffer1D<double, Stride1D.Dense> dMoments;
        private MemoryBuffer1D<uint, Stride1D.Dense> dHist;
        private MemoryBuffer1D<long, Stride1D.Dense> dHotCount;
        private MemoryBuffer1D<float, Stride1D.Dense> dTaps;
        private MemoryBuffer1D<float, Stride1D.Dense> dGridOut;

        public bool CollectStageTimings { get; set; }

        public GpuEarlyChain(CudaAccelerator accelerator) {
            acc = accelerator;
            stream = acc.CreateStream();
            median3x3 = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int>(GpuEarlyKernels.Median3x3Kernel);
            hotpixelSelect = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, float, ArrayView<long>>(GpuEarlyKernels.HotpixelSelectKernel);
            convolveRow = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int>(GpuEarlyKernels.ConvolveRowKernel);
            convolveCol = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int>(GpuEarlyKernels.ConvolveColKernel);
            atrousH = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, int>(GpuEarlyKernels.AtrousHorizontalKernel);
            atrousV = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int, int, int, int>(GpuEarlyKernels.AtrousVerticalKernel);
            maskedMoments = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<double>, float, float, int, long>(GpuEarlyKernels.MaskedMomentsKernel);
            histogram = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<uint>, int, int, long>(GpuEarlyKernels.HistogramKernel);
            localGrid = acc.LoadKernel<ArrayView<float>, int, int, int, int, ArrayView<float>, ArrayView<float>, float>(GpuEarlyKernels.LocalBackgroundGridKernel);
        }

        private void EnsureCapacity(int w, int h) {
            if (w == width && h == height && dMeas != null) {
                return;
            }
            DisposeBuffers();
            width = w;
            height = h;
            length = (long)w * h;
            dMeas = acc.Allocate1D<float>(length);
            dNoiseReduced = acc.Allocate1D<float>(length);
            dStructure = acc.Allocate1D<float>(length);
            dSmooth = acc.Allocate1D<float>(length);
            dTmp = acc.Allocate1D<float>(length);
            dMoments = acc.Allocate1D<double>(3);
            dHist = acc.Allocate1D<uint>(256);
            dHotCount = acc.Allocate1D<long>(1);
            dTaps = acc.Allocate1D<float>(64);
            dGridOut = acc.Allocate1D<float>(2L * MaxGridCells);
        }

        /// <summary>
        /// Runs the EARLY span on <paramref name="srcImage"/> (CV_32F, continuous; NOT modified). Mirrors
        /// StarDetector.BuildDetectionContextInternal steps 1-5b exactly, including the branch structure
        /// around hotpixelAlreadyApplied / noise-reduction placement.
        /// </summary>
        public GpuEarlyResult Run(Mat srcImage, GpuEarlyParams p, bool hotpixelAlreadyApplied) {
            if (srcImage.Type() != MatType.CV_32F) {
                throw new ArgumentException("Only CV_32F supported");
            }
            if (!srcImage.IsContinuous()) {
                throw new ArgumentException("srcImage must be continuous");
            }
            EnsureCapacity(srcImage.Width, srcImage.Height);

            var result = new GpuEarlyResult { StageMs = CollectStageTimings ? new Dictionary<string, double>() : null };
            var sw = Stopwatch.StartNew();
            void Record(string stage) {
                if (result.StageMs != null) {
                    stream.Synchronize();
                    result.StageMs[stage] = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                }
            }

            Upload(srcImage, dMeas);
            Record("H2D");

            long hotpixelCount = 0;
            bool measurementMutated = false;
            bool hotpixelFilteringApplied = hotpixelAlreadyApplied;

            // Step 1: hotpixel filter + optional measurement noise reduction (StarDetector.cs:520-537).
            if (p.HotpixelFiltering || (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled)) {
                if (!hotpixelAlreadyApplied) {
                    hotpixelCount = ApplyHotpixelFilter(dMeas, p);
                    measurementMutated = true;
                }
                hotpixelFilteringApplied = true;
            }
            bool noiseReductionApplied = false;
            if (p.NoiseReductionRadius > 0 && p.StarMeasurementNoiseReductionEnabled) {
                Gaussian(dMeas, dMeas, p.NoiseReductionRadius * 2 + 1);
                noiseReductionApplied = true;
                measurementMutated = true;
            }
            Record("SrcImagePreparation");

            // Steps 2-3: structure-source copy (+ hotpixel there when the measurement image skipped it),
            // then the structure-side noise reduction (StarDetector.cs:545-557).
            if (hotpixelFilteringApplied || noiseReductionApplied || p.NoiseReductionRadius <= 0) {
                dMeas.View.CopyTo(stream, dNoiseReduced.View);
            } else {
                dMeas.View.CopyTo(stream, dNoiseReduced.View);
                hotpixelCount = ApplyHotpixelFilter(dNoiseReduced, p);
            }
            if (p.NoiseReductionRadius > 0 && !noiseReductionApplied) {
                Gaussian(dNoiseReduced, dNoiseReduced, p.NoiseReductionRadius * 2 + 1);
            }
            dNoiseReduced.View.CopyTo(stream, dStructure.View);
            Record("StructureMapPreparation");

            // K-σ noise estimates (concurrent tasks on CPU; sequential launches here — same math).
            result.StructureNoise = KappaSigma(dNoiseReduced, p.NoiseClippingMultiplier);
            var measurementImageDiffers = p.NoiseReductionRadius > 0 && !noiseReductionApplied;
            result.MeasurementNoise = measurementImageDiffers ? KappaSigma(dMeas, p.NoiseClippingMultiplier) : result.StructureNoise;
            Record("KSigma");

            // Step 4: à-trous wavelet residual, fused subtract + clamp on the last layer
            // (AtrousWaveletFast.ComputeResidualAndSubtractInPlace).
            var currentView = dStructure.View;
            for (int layer = 0; layer < p.EffectiveStructureLayers; layer++) {
                int scale = 1 << layer;
                atrousH(stream, (int)length, currentView, dTmp.View, scale, width, height);
                bool last = layer == p.EffectiveStructureLayers - 1;
                if (last) {
                    atrousV(stream, (int)length, dTmp.View, dStructure.View, dStructure.View, 1, scale, width, height);
                } else {
                    atrousV(stream, (int)length, dTmp.View, dSmooth.View, dSmooth.View, 0, scale, width, height);
                    currentView = dSmooth.View;
                }
            }
            Record("WaveletCalculation");

            // Step 5: post-wavelet Gaussian (kernel radius from StructureLayers, NOT effective layers).
            Gaussian(dStructure, dStructure, p.StructureLayers * 2 + 1);
            Record("PostWaveletConvolution");

            // Step 5b: histogram median (two-pass exact reconstruction of the 65536-bin walk).
            result.StructureMapMedian = HistogramMedian(dStructure);
            Record("BinarizationStatistics");

            // Adaptive-binarization grids on GPU: exact per-block radix-selected order statistics, matching
            // ComputeLocalBackgroundGrid (StarDetector.cs:570-573 and :642-645 semantics).
            if (p.LocallyAdaptiveBinarization) {
                int blockSize = Math.Max(1, p.AdaptiveNoiseBlockSize);
                result.SigmaGrid = ComputeGridOnGpu(dNoiseReduced, blockSize);
                result.AdaptiveMedianGrid = ComputeGridOnGpu(dStructure, blockSize);
            }
            Record("CpuGrids");

            // Downloads: structure map always; measurement image only when mutated. (The noise-reduced
            // source never leaves the GPU — its only consumer was the sigma grid.)
            result.StructureMap = Download(dStructure);
            if (measurementMutated) {
                result.MeasurementImage = Download(dMeas);
            }
            Record("D2H");

            result.HotpixelCount = hotpixelCount;
            return result;
        }

        private CvImageUtility.LocalBackgroundGrid ComputeGridOnGpu(MemoryBuffer1D<float, Stride1D.Dense> image, int blockSize) {
            int gridCols = (width + blockSize - 1) / blockSize;
            int gridRows = (height + blockSize - 1) / blockSize;
            int cells = gridRows * gridCols;
            if (cells > MaxGridCells) {
                throw new NotSupportedException($"Grid of {cells} cells exceeds the {MaxGridCells} buffer");
            }
            var medianView = dGridOut.View.SubView(0, cells);
            var sigmaView = dGridOut.View.SubView(MaxGridCells, cells);
            localGrid(stream, new KernelConfig(cells, 256), image.View, width, height, blockSize, gridCols, medianView, sigmaView, AdaptiveBinarizationSigmaFloor);
            var median = new float[cells];
            var sigma = new float[cells];
            medianView.CopyToCPU(stream, median);
            sigmaView.CopyToCPU(stream, sigma);
            stream.Synchronize();
            return new CvImageUtility.LocalBackgroundGrid(gridRows, gridCols, blockSize, median, sigma);
        }

        private long ApplyHotpixelFilter(MemoryBuffer1D<float, Stride1D.Dense> image, GpuEarlyParams p) {
            median3x3(stream, (int)length, image.View, dTmp.View, width, height);
            if (p.HotpixelThresholdingEnabled) {
                dHotCount.MemSetToZero(stream);
                hotpixelSelect(stream, (int)length, image.View, dTmp.View, image.View, (float)p.HotpixelThreshold, dHotCount.View);
                var count = new long[1];
                dHotCount.View.CopyToCPU(stream, count);
                stream.Synchronize();
                return count[0];
            }
            dTmp.View.CopyTo(stream, image.View);
            return 0;
        }

        private void Gaussian(MemoryBuffer1D<float, Stride1D.Dense> src, MemoryBuffer1D<float, Stride1D.Dense> dst, int kernelSize) {
            var taps = GpuEarlyKernels.GaussianTaps(kernelSize, -1.0);
            var tapsView = dTaps.View.SubView(0, taps.Length);
            tapsView.CopyFromCPU(stream, taps);
            int radius = kernelSize / 2;
            convolveRow(stream, (int)length, src.View, dTmp.View, tapsView, radius, width, height);
            convolveCol(stream, (int)length, dTmp.View, dst.View, tapsView, radius, width, height);
        }

        // Mirrors CvImageUtility.KappaSigmaNoiseEstimate: masked (InRange-inclusive) mean/σ per iteration,
        // float-cast threshold update, absolute σ-convergence check.
        private CvImageUtility.KappaSigmaNoiseEstimateResult KappaSigma(MemoryBuffer1D<float, Stride1D.Dense> image, double clippingMultiplier, double allowedError = 0.00001, int maxIterations = 5) {
            var threshold = float.MaxValue;
            var lastSigma = 1.0d;
            var lastBackgroundMean = 1.0d;
            int numIterations = 0;
            var moments = new double[3];

            while (numIterations < maxIterations) {
                dMoments.MemSetToZero(stream);
                maskedMoments(stream, ReductionThreads, image.View, dMoments.View, float.Epsilon, threshold - float.Epsilon, ReductionThreads, length);
                dMoments.View.CopyToCPU(stream, moments);
                stream.Synchronize();
                var n = moments[0];
                var mean = n > 0 ? moments[1] / n : 0.0;
                var variance = n > 0 ? moments[2] / n - mean * mean : 0.0;
                var sigma = Math.Sqrt(Math.Max(0.0, variance));
                if (++numIterations > 1) {
                    if (Math.Abs(sigma - lastSigma) <= allowedError) {
                        lastSigma = sigma;
                        break;
                    }
                }
                threshold = (float)(mean + clippingMultiplier * sigma);
                lastSigma = sigma;
                lastBackgroundMean = mean;
            }

            return new CvImageUtility.KappaSigmaNoiseEstimateResult {
                Sigma = lastSigma,
                BackgroundMean = lastBackgroundMean,
                NumIterations = numIterations
            };
        }

        private double HistogramMedian(MemoryBuffer1D<float, Stride1D.Dense> image) {
            var coarse = new uint[256];
            var fine = new uint[256];
            dHist.MemSetToZero(stream);
            histogram(stream, ReductionThreads, image.View, dHist.View, -1, ReductionThreads, length);
            dHist.View.CopyToCPU(stream, coarse);
            stream.Synchronize();
            int coarseBucket = GpuEarlyKernels.MedianCoarseBucket(coarse, length);
            dHist.MemSetToZero(stream);
            histogram(stream, ReductionThreads, image.View, dHist.View, coarseBucket, ReductionThreads, length);
            dHist.View.CopyToCPU(stream, fine);
            stream.Synchronize();
            return GpuEarlyKernels.MedianFromTwoPass(coarse, fine, coarseBucket, length);
        }

        private void Upload(Mat src, MemoryBuffer1D<float, Stride1D.Dense> dst) {
            unsafe {
                var span = new ReadOnlySpan<float>((void*)src.DataPointer, checked((int)length));
                dst.View.CopyFromCPU(stream, span);
            }
            stream.Synchronize();
        }

        private Mat Download(MemoryBuffer1D<float, Stride1D.Dense> src) {
            var mat = new Mat(height, width, MatType.CV_32F);
            unsafe {
                var span = new Span<float>((void*)mat.DataPointer, checked((int)length));
                src.View.CopyToCPU(stream, span);
            }
            stream.Synchronize();
            return mat;
        }

        private void DisposeBuffers() {
            dMeas?.Dispose();
            dNoiseReduced?.Dispose();
            dStructure?.Dispose();
            dSmooth?.Dispose();
            dTmp?.Dispose();
            dMoments?.Dispose();
            dHist?.Dispose();
            dHotCount?.Dispose();
            dTaps?.Dispose();
            dGridOut?.Dispose();
            dMeas = null;
            dNoiseReduced = null;
            dStructure = null;
            dSmooth = null;
            dTmp = null;
            dMoments = null;
            dHist = null;
            dHotCount = null;
            dTaps = null;
            dGridOut = null;
            width = 0;
            height = 0;
        }

        public void Dispose() {
            DisposeBuffers();
            stream.Dispose();
        }
    }
}
