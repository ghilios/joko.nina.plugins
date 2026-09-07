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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace TestApp.Gpu {

    /// <summary>
    /// GPU feasibility-spike benchmarks (see plans/gpu-early-pipeline-spike-plan.md).
    ///
    /// `TestApp bench-gpu --sanity` — Step 1 stack sanity on Blackwell: device banner, verified saxpy,
    /// one kernel exercising every math primitive the early-pipeline kernels need (the LibDevice-avoidance
    /// check), D2D bandwidth, H2D/D2H pageable + page-locked at the real 61 MP frame size, and kernel
    /// launch latency. Needs no profile and no images. Gate G1: saxpy correct, D2D >= 300 GB/s, pinned
    /// H2D/D2H >= 10 GB/s, no PTX JIT errors.
    /// </summary>
    public static class BenchGpuRunner {
        // CWhiteFocus frame size: 9576x6388 = 61.17 MP, the largest sensor in the bank.
        private const int FramePixels = 9576 * 6388;

        public static async Task Run(string[] args) {
            bool sanity = DiagnosticUtil.HasFlag(args, "--sanity");
            bool early = DiagnosticUtil.HasFlag(args, "--early");
            bool compare = DiagnosticUtil.HasFlag(args, "--compare");
            if (sanity) {
                RunSanity();
                return;
            }
            if (early || compare) {
                await RunEarly(args, compare);
                return;
            }
            Console.Error.WriteLine("Usage: TestApp bench-gpu --sanity | --early --image <path> [--iters N] [--layers L] [--resident-repeats N] | --compare --image <path>");
            Environment.ExitCode = 2;
        }

        private static GpuEarlyParams BenchParams(int layers) => new GpuEarlyParams {
            // Representative of the seed/search shape observed in the Step-0 traces: hotpixel filtering on,
            // structure-side noise reduction only (so the measurement image differs and BOTH K-σ estimates
            // run), adaptive binarization on (profile default).
            HotpixelFiltering = true,
            HotpixelThresholdingEnabled = false,
            HotpixelThreshold = 0.001,
            NoiseReductionRadius = 4,
            StarMeasurementNoiseReductionEnabled = false,
            NoiseClippingMultiplier = 4.0,
            EffectiveStructureLayers = layers,
            StructureLayers = layers,
            LocallyAdaptiveBinarization = true,
            AdaptiveNoiseBlockSize = 128,
        };

        private static async Task RunEarly(string[] args, bool compare) {
            var imagePath = DiagnosticUtil.GetArg(args, "--image");
            if (string.IsNullOrEmpty(imagePath)) {
                Console.Error.WriteLine("--image <path> is required");
                Environment.ExitCode = 2;
                return;
            }
            int iters = int.Parse(DiagnosticUtil.GetArg(args, "--iters") ?? "3", CultureInfo.InvariantCulture);
            int layers = int.Parse(DiagnosticUtil.GetArg(args, "--layers") ?? "4", CultureInfo.InvariantCulture);
            int residentRepeats = int.Parse(DiagnosticUtil.GetArg(args, "--resident-repeats") ?? "0", CultureInfo.InvariantCulture);

            var profileService = new NINA.Profile.ProfileService();
            profileService.TryLoad(DiagnosticUtil.GetArg(args, "--profile-id") ?? string.Empty);
            // Mat-level kernel bench, not a detection-parity surface: the debayered-float route satisfies
            // the parity guard (mono frames are byte-identical; bayered frames arrive CPU-debayered, which
            // is exactly the spike's boundary — CFA/debayer stays on the CPU).
            using var image = await DiagnosticUtil.LoadDebayeredFloatMat(imagePath, profileService);
            Console.WriteLine($"Image: {image.Width}x{image.Height} ({image.Width * (long)image.Height / 1e6:F1} MP), layers={layers}, iters={iters}");
            var p = BenchParams(layers);

            var acc = GpuDevice.TryGet(out var reason);
            if (acc == null) {
                Console.Error.WriteLine($"GPU unavailable: {reason}");
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine(GpuDevice.DescribeBanner(acc));
            using var gpuChain = new GpuEarlyChain(acc) { CollectStageTimings = true };

            // Warmup (JIT + allocation) — reported, excluded from medians.
            var jitSw = Stopwatch.StartNew();
            using (var warm = gpuChain.Run(image, p, hotpixelAlreadyApplied: false)) { }
            Console.WriteLine($"GPU first run (JIT+alloc): {jitSw.ElapsedMilliseconds} ms");

            if (compare) {
                CompareChains(image, p, gpuChain);
                return;
            }

            var cpuTimings = CollectRuns("CPU", iters, () => {
                var r = CpuEarlyChain.Run(image, p, hotpixelAlreadyApplied: false, collectTimings: true);
                var t = r.StageMs;
                r.Dispose();
                return t;
            });
            var gpuTimings = CollectRuns("GPU", iters, () => {
                var r = gpuChain.Run(image, p, hotpixelAlreadyApplied: false);
                var t = r.StageMs;
                r.Dispose();
                return t;
            });
            PrintStageTable(cpuTimings, gpuTimings);

            // CPU "I1" baseline (speedup-results doc future-work): the production histogram is a
            // single-threaded raster scan; a parallel per-partition histogram + merge is bit-identical
            // (integer counts are associative). Measured here so the GPU verdict is "vs best-available
            // CPU", not "vs current CPU".
            using (var i1Result = CpuEarlyChain.Run(image, p, hotpixelAlreadyApplied: false, collectTimings: false)) {
                var sw = Stopwatch.StartNew();
                var i1Median = ParallelHistogramMedian(i1Result.StructureMap);
                sw.Stop();
                Console.WriteLine($"CPU I1 parallel histogram: {sw.Elapsed.TotalMilliseconds:F1} ms, median match: " +
                                  $"{(i1Median == i1Result.StructureMapMedian ? "EXACT" : $"cpu={i1Result.StructureMapMedian:E9} i1={i1Median:E9}")}");
            }

            if (residentRepeats > 0) {
                var totals = new List<double>();
                for (int i = 0; i < residentRepeats; i++) {
                    var sw = Stopwatch.StartNew();
                    using var r = gpuChain.Run(image, p, hotpixelAlreadyApplied: false);
                    totals.Add(sw.Elapsed.TotalMilliseconds);
                }
                totals.Sort();
                Console.WriteLine($"GPU steady-state x{residentRepeats}: median {totals[totals.Count / 2]:F1} ms, min {totals[0]:F1} ms, max {totals[^1]:F1} ms");
            }
        }

        private static List<Dictionary<string, double>> CollectRuns(string label, int iters, Func<Dictionary<string, double>> run) {
            var results = new List<Dictionary<string, double>>();
            for (int i = 0; i < iters; i++) {
                results.Add(run());
            }
            return results;
        }

        private static readonly string[] StageOrder = {
            "H2D", "SrcImagePreparation", "StructureMapPreparation", "KSigma", "WaveletCalculation",
            "PostWaveletConvolution", "BinarizationStatistics", "D2H", "CpuGrids"
        };

        private static void PrintStageTable(List<Dictionary<string, double>> cpu, List<Dictionary<string, double>> gpu) {
            static double Median(List<Dictionary<string, double>> runs, string stage) {
                var vals = runs.Where(r => r.ContainsKey(stage)).Select(r => r[stage]).OrderBy(v => v).ToList();
                return vals.Count == 0 ? 0 : vals[vals.Count / 2];
            }
            Console.WriteLine();
            Console.WriteLine($"{"stage",-26}{"CPU ms",10}{"GPU ms",10}{"speedup",10}");
            double cpuTotal = 0, gpuTotal = 0;
            foreach (var stage in StageOrder) {
                var c = Median(cpu, stage);
                var g = Median(gpu, stage);
                cpuTotal += c;
                gpuTotal += g;
                Console.WriteLine($"{stage,-26}{c,10:F1}{g,10:F1}{(g > 0 ? c / g : 0),10:F1}");
            }
            Console.WriteLine($"{"TOTAL",-26}{cpuTotal,10:F1}{gpuTotal,10:F1}{(gpuTotal > 0 ? cpuTotal / gpuTotal : 0),10:F1}");
        }

        private static void CompareChains(OpenCvSharp.Mat image, GpuEarlyParams p, GpuEarlyChain gpuChain) {
            using var cpu = CpuEarlyChain.Run(image, p, hotpixelAlreadyApplied: false, collectTimings: false);
            using var gpu = gpuChain.Run(image, p, hotpixelAlreadyApplied: false);

            Console.WriteLine();
            Console.WriteLine("== divergence: GPU vs CPU oracle ==");
            Console.WriteLine($"hotpixel count: cpu={cpu.HotpixelCount} gpu={gpu.HotpixelCount} {(cpu.HotpixelCount == gpu.HotpixelCount ? "EXACT" : "DIFF")}");
            Console.WriteLine($"K-sigma structure: sigma rel-delta={RelDelta(cpu.StructureNoise.Sigma, gpu.StructureNoise.Sigma):E2}, " +
                              $"mean rel-delta={RelDelta(cpu.StructureNoise.BackgroundMean, gpu.StructureNoise.BackgroundMean):E2}, " +
                              $"iters cpu={cpu.StructureNoise.NumIterations} gpu={gpu.StructureNoise.NumIterations}");
            Console.WriteLine($"K-sigma measurement: sigma rel-delta={RelDelta(cpu.MeasurementNoise.Sigma, gpu.MeasurementNoise.Sigma):E2}, " +
                              $"iters cpu={cpu.MeasurementNoise.NumIterations} gpu={gpu.MeasurementNoise.NumIterations}");
            Console.WriteLine($"histogram median: cpu={cpu.StructureMapMedian:E9} gpu={gpu.StructureMapMedian:E9} " +
                              $"{(cpu.StructureMapMedian == gpu.StructureMapMedian ? "EXACT" : $"rel-delta={RelDelta(cpu.StructureMapMedian, gpu.StructureMapMedian):E2}")}");

            var (maxAbs, nDiff) = MatDiff(cpu.StructureMap, gpu.StructureMap);
            Console.WriteLine($"structure map: max-abs-diff={maxAbs:E2}, pixels-differing={nDiff} ({100.0 * nDiff / (image.Width * (long)image.Height):F4}%)");
            if (cpu.MeasurementImage != null && gpu.MeasurementImage != null) {
                var (mMaxAbs, mDiff) = MatDiff(cpu.MeasurementImage, gpu.MeasurementImage);
                Console.WriteLine($"measurement image: max-abs-diff={mMaxAbs:E2}, pixels-differing={mDiff}");
            }

            // Binarized flip count at each side's own scalar threshold (median + NC·σ), the downstream decision.
            double thrCpu = cpu.StructureMapMedian + p.NoiseClippingMultiplier * cpu.StructureNoise.Sigma;
            double thrGpu = gpu.StructureMapMedian + p.NoiseClippingMultiplier * gpu.StructureNoise.Sigma;
            long flips = BinarizeFlips(cpu.StructureMap, gpu.StructureMap, thrCpu, thrGpu);
            Console.WriteLine($"binarized flips (thrCpu={thrCpu:E6}, thrGpu={thrGpu:E6}): {flips} ({100.0 * flips / (image.Width * (long)image.Height):F5}%)");

            if (cpu.SigmaGrid != null && gpu.SigmaGrid != null) {
                Console.WriteLine($"sigma grid: max-abs-diff={GridDiff(cpu.SigmaGrid.Sigma, gpu.SigmaGrid.Sigma):E2}; " +
                                  $"median grid (sigma-source): max-abs-diff={GridDiff(cpu.SigmaGrid.Median, gpu.SigmaGrid.Median):E2}");
                Console.WriteLine($"adaptive median grid: median max-abs-diff={GridDiff(cpu.AdaptiveMedianGrid.Median, gpu.AdaptiveMedianGrid.Median):E2}, " +
                                  $"sigma max-abs-diff={GridDiff(cpu.AdaptiveMedianGrid.Sigma, gpu.AdaptiveMedianGrid.Sigma):E2}");
            }
        }

        /// <summary>
        /// Bit-identical parallel version of CalculateStatistics_Histogram's linear-median path:
        /// per-partition 65536-bin histograms merged by integer addition, then the exact same
        /// interpolated cumulative walk.
        /// </summary>
        private static unsafe double ParallelHistogramMedian(OpenCvSharp.Mat image) {
            const int numBuckets = 1 << 16;
            int height = image.Height;
            int width = image.Width;
            var dataPtr = image.DataPointer;
            var merged = new uint[numBuckets];
            var mergeLock = new object();
            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, height),
                () => new uint[numBuckets],
                (range, _, local) => {
                    var p = (float*)dataPtr + (long)range.Item1 * width;
                    long n = (long)(range.Item2 - range.Item1) * width;
                    for (long i = 0; i < n; i++) {
                        int bucket = (int)Math.Floor((double)p[i] * numBuckets);
                        if (bucket < 0) bucket = 0;
                        if (bucket >= numBuckets) bucket = numBuckets - 1;
                        local[bucket]++;
                    }
                    return local;
                },
                local => {
                    lock (mergeLock) {
                        for (int i = 0; i < numBuckets; i++) {
                            merged[i] += local[i];
                        }
                    }
                });

            long numPixels = (long)height * width;
            double target = numPixels / 2.0;
            uint currentCount = 0;
            for (int i = 0; i < numBuckets; i++) {
                currentCount += merged[i];
                if (currentCount >= target) {
                    var interpolationRatio = (currentCount - target) / merged[i];
                    var nextBucket = i < (numBuckets - 1) ? (double)(i + 1) / numBuckets : 1.0d;
                    var thisBucket = (double)i / numBuckets;
                    return thisBucket + (nextBucket - thisBucket) * interpolationRatio;
                }
            }
            return 0;
        }

        private static double RelDelta(double a, double b) {
            if (a == b) return 0;
            var denom = Math.Max(Math.Abs(a), Math.Abs(b));
            return denom == 0 ? 0 : Math.Abs(a - b) / denom;
        }

        private static double GridDiff(float[] a, float[] b) {
            double max = 0;
            for (int i = 0; i < a.Length; i++) {
                max = Math.Max(max, Math.Abs((double)a[i] - b[i]));
            }
            return max;
        }

        private static unsafe (double MaxAbs, long NumDiff) MatDiff(OpenCvSharp.Mat a, OpenCvSharp.Mat b) {
            long n = a.Width * (long)a.Height;
            var pa = (float*)a.DataPointer;
            var pb = (float*)b.DataPointer;
            double maxAbs = 0;
            long numDiff = 0;
            for (long i = 0; i < n; i++) {
                var d = Math.Abs((double)pa[i] - pb[i]);
                if (d > 0) {
                    numDiff++;
                    if (d > maxAbs) maxAbs = d;
                }
            }
            return (maxAbs, numDiff);
        }

        private static unsafe long BinarizeFlips(OpenCvSharp.Mat a, OpenCvSharp.Mat b, double thrA, double thrB) {
            long n = a.Width * (long)a.Height;
            var pa = (float*)a.DataPointer;
            var pb = (float*)b.DataPointer;
            long flips = 0;
            // Cv2.Threshold on CV_32F compares against the float-cast threshold, strict greater.
            float fa = (float)thrA;
            float fb = (float)thrB;
            for (long i = 0; i < n; i++) {
                if ((pa[i] > fa) != (pb[i] > fb)) {
                    flips++;
                }
            }
            return flips;
        }

        private static void RunSanity() {
            var initSw = Stopwatch.StartNew();
            var acc = GpuDevice.TryGet(out var reason);
            initSw.Stop();
            if (acc == null) {
                Console.Error.WriteLine($"GPU unavailable: {reason}");
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine(GpuDevice.DescribeBanner(acc));
            Console.WriteLine($"Context+accelerator init: {initSw.ElapsedMilliseconds} ms");

            var ok = true;
            ok &= SaxpyCheck(acc);
            ok &= MathPrimitivesCheck(acc);
            DeviceBandwidth(acc);
            TransferBandwidth(acc);
            LaunchLatency(acc);

            Console.WriteLine(ok ? "SANITY PASS" : "SANITY FAIL");
            if (!ok) {
                Environment.ExitCode = 1;
            }
        }

        private static void SaxpyKernel(Index1D i, ArrayView<float> x, ArrayView<float> y, float a) {
            y[i] = a * x[i] + y[i];
        }

        // Every math primitive the early-pipeline kernels rely on, in one verifiable kernel:
        // abs/min/max/float sqrt (PTX-native, correctly rounded like MathF.Sqrt) and the histogram's
        // double-precision bucketing (int)floor((double)v * 65536). None of these touch LibDevice.
        private static void MathPrimitivesKernel(Index1D i, ArrayView<float> x, ArrayView<float> sqrtOut, ArrayView<int> bucketOut) {
            var v = x[i];
            var clamped = Math.Min(Math.Max(Math.Abs(v), 0.0f), 1.0f);
            sqrtOut[i] = (float)Math.Sqrt(clamped);
            bucketOut[i] = (int)Math.Floor((double)clamped * 65536.0);
        }

        private static void EmptyKernel(Index1D i, ArrayView<float> unused) {
        }

        private static bool SaxpyCheck(CudaAccelerator acc) {
            const int N = 64 * 1024 * 1024;
            var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float>(SaxpyKernel);
            var hx = new float[N];
            var hy = new float[N];
            var rng = new Random(42);
            for (int i = 0; i < N; i++) {
                hx[i] = (float)rng.NextDouble();
                hy[i] = (float)rng.NextDouble();
            }
            using var dx = acc.Allocate1D<float>(N);
            using var dy = acc.Allocate1D<float>(N);
            dx.CopyFromCPU(hx);
            dy.CopyFromCPU(hy);

            var jitSw = Stopwatch.StartNew();
            kernel(N, dx.View, dy.View, 2.5f);
            acc.Synchronize();
            jitSw.Stop();

            var result = dy.GetAsArray1D();
            int mismatches = 0;
            int fmaContracted = 0;
            for (int i = 0; i < N; i++) {
                var expected = 2.5f * hx[i] + hy[i];
                if (result[i] != expected) {
                    // ptxas contracts mul+add into fma.rn (single rounding) — a correct result that differs
                    // from the CPU's twice-rounded value by <= 1 ulp. Count it separately; only values that
                    // match NEITHER form are real mismatches.
                    var fused = MathF.FusedMultiplyAdd(2.5f, hx[i], hy[i]);
                    if (result[i] == fused) {
                        fmaContracted++;
                    } else {
                        mismatches++;
                    }
                }
            }
            // Time the compute alone (already JITted), 10 reps.
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < 10; r++) {
                kernel(N, dx.View, dy.View, 2.5f);
            }
            acc.Synchronize();
            sw.Stop();
            var gbMoved = 10 * 3.0 * N * sizeof(float) / 1e9;
            Console.WriteLine($"saxpy 64M floats: first-launch(+JIT) {jitSw.ElapsedMilliseconds} ms, steady {sw.Elapsed.TotalMilliseconds / 10:F2} ms/iter " +
                              $"({gbMoved / sw.Elapsed.TotalSeconds:F0} GB/s effective), mismatches={mismatches}, fma-contracted={fmaContracted}");
            return mismatches == 0;
        }

        private static bool MathPrimitivesCheck(CudaAccelerator acc) {
            const int N = 1 << 20;
            var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(MathPrimitivesKernel);
            var hx = new float[N];
            var rng = new Random(1234);
            for (int i = 0; i < N; i++) {
                // Cover negatives, >1 values, denormal-ish smalls, and exact bucket boundaries.
                hx[i] = (i % 7 == 0) ? -(float)rng.NextDouble() : (float)(rng.NextDouble() * 1.5);
                if (i % 97 == 0) hx[i] = i / 65536.0f;
            }
            using var dx = acc.Allocate1D<float>(N);
            using var dsqrt = acc.Allocate1D<float>(N);
            using var dbucket = acc.Allocate1D<int>(N);
            dx.CopyFromCPU(hx);
            kernel(N, dx.View, dsqrt.View, dbucket.View);
            acc.Synchronize();
            var gsqrt = dsqrt.GetAsArray1D();
            var gbucket = dbucket.GetAsArray1D();

            int sqrtMismatches = 0, bucketMismatches = 0;
            for (int i = 0; i < N; i++) {
                var clamped = Math.Min(Math.Max(Math.Abs(hx[i]), 0.0f), 1.0f);
                if (gsqrt[i] != (float)Math.Sqrt(clamped)) sqrtMismatches++;
                if (gbucket[i] != (int)Math.Floor((double)clamped * 65536.0)) bucketMismatches++;
            }
            Console.WriteLine($"math primitives (abs/min/max/sqrt/double-floor-bucket) on 1M values: " +
                              $"sqrt mismatches={sqrtMismatches}, bucket mismatches={bucketMismatches}");
            return sqrtMismatches == 0 && bucketMismatches == 0;
        }

        private static void DeviceBandwidth(CudaAccelerator acc) {
            const int N = 64 * 1024 * 1024; // 256 MB
            using var a = acc.Allocate1D<float>(N);
            using var b = acc.Allocate1D<float>(N);
            a.MemSetToZero();
            acc.Synchronize();
            // Warmup
            a.View.CopyTo(acc.DefaultStream, b.View);
            acc.Synchronize();
            const int reps = 20;
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < reps; r++) {
                a.View.CopyTo(acc.DefaultStream, b.View);
            }
            acc.Synchronize();
            sw.Stop();
            // A D2D copy reads + writes each byte.
            var gb = reps * 2.0 * N * sizeof(float) / 1e9;
            Console.WriteLine($"D2D copy 256 MB x{reps}: {gb / sw.Elapsed.TotalSeconds:F0} GB/s");
        }

        private static void TransferBandwidth(CudaAccelerator acc) {
            var n = FramePixels;
            var mb = n * sizeof(float) / 1e6;
            using var dev = acc.Allocate1D<float>(n);

            var pageable = new float[n];
            const int reps = 5;
            // Warmup both directions.
            dev.CopyFromCPU(pageable);
            dev.CopyToCPU(pageable);

            var sw = Stopwatch.StartNew();
            for (int r = 0; r < reps; r++) dev.CopyFromCPU(pageable);
            sw.Stop();
            var h2dPageable = reps * n * (double)sizeof(float) / 1e9 / sw.Elapsed.TotalSeconds;
            sw.Restart();
            for (int r = 0; r < reps; r++) dev.CopyToCPU(pageable);
            sw.Stop();
            var d2hPageable = reps * n * (double)sizeof(float) / 1e9 / sw.Elapsed.TotalSeconds;

            using var pinned = acc.AllocatePageLocked1D<float>(n);
            dev.View.CopyFromPageLockedAsync(acc.DefaultStream, pinned);
            acc.Synchronize();
            sw.Restart();
            for (int r = 0; r < reps; r++) dev.View.CopyFromPageLockedAsync(acc.DefaultStream, pinned);
            acc.Synchronize();
            sw.Stop();
            var h2dPinned = reps * n * (double)sizeof(float) / 1e9 / sw.Elapsed.TotalSeconds;
            sw.Restart();
            for (int r = 0; r < reps; r++) dev.View.CopyToPageLockedAsync(acc.DefaultStream, pinned);
            acc.Synchronize();
            sw.Stop();
            var d2hPinned = reps * n * (double)sizeof(float) / 1e9 / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"H2D/D2H {mb:F0} MB (61 MP frame): pageable {h2dPageable:F1}/{d2hPageable:F1} GB/s, " +
                              $"page-locked {h2dPinned:F1}/{d2hPinned:F1} GB/s");
        }

        private static void LaunchLatency(CudaAccelerator acc) {
            var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(EmptyKernel);
            using var dummy = acc.Allocate1D<float>(1);
            kernel(1, dummy.View);
            acc.Synchronize();

            const int syncedReps = 200;
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < syncedReps; r++) {
                kernel(1, dummy.View);
                acc.Synchronize();
            }
            sw.Stop();
            var syncedUs = sw.Elapsed.TotalMilliseconds * 1000.0 / syncedReps;

            const int streamReps = 1000;
            sw.Restart();
            for (int r = 0; r < streamReps; r++) {
                kernel(1, dummy.View);
            }
            acc.Synchronize();
            sw.Stop();
            var streamedUs = sw.Elapsed.TotalMilliseconds * 1000.0 / streamReps;
            Console.WriteLine($"kernel launch latency: {syncedUs.ToString("F1", CultureInfo.InvariantCulture)} us synced, " +
                              $"{streamedUs.ToString("F1", CultureInfo.InvariantCulture)} us streamed");
        }
    }
}
