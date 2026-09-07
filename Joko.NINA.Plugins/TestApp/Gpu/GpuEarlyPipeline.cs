#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILGPU.Runtime.Cuda;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace TestApp.Gpu {

    /// <summary>
    /// GPU implementation of the StarDetector EARLY-span hook (the spike's `optimize --gpu` path). Pools
    /// two GpuEarlyChain working sets behind a semaphore so up to two of the harness's parallel per-frame
    /// builds are in flight on the device; excess builders wait (a GPU build clears in well under the CPU
    /// build time). Any failure logs a warning, counts a fallback, and returns false so the unchanged CPU
    /// span runs.
    /// </summary>
    internal sealed class GpuEarlyPipeline : IEarlyPipelineAccelerator, IDisposable {
        // Measured on the E2E matrix: pool=4 bought nothing over pool=2 (Panos 60.7 vs 59.2 s, CWhite 288
        // vs 280 s) — the harness's per-build GpuEarlySpan wall (1.33 s vs 0.21 s isolated) is queue time
        // that overlaps CPU work that bounds the burst anyway (flood-fill tail + late stage). Keep 2:
        // ~2.5 GB VRAM at 61 MP.
        private const int PoolSize = 2;

        private readonly CudaAccelerator acc;
        private readonly ConcurrentBag<GpuEarlyChain> pool = new ConcurrentBag<GpuEarlyChain>();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(PoolSize);
        private int runs;
        private int fallbacks;

        public int Runs => runs;
        public int Fallbacks => fallbacks;

        public GpuEarlyPipeline(CudaAccelerator accelerator) {
            acc = accelerator;
        }

        public bool TryRunEarlySpan(Mat srcImage, StarDetectorParams p, int effectiveStructureLayers, bool hotpixelAlreadyApplied, out EarlySpanOutput output) {
            output = null;
            if (srcImage.Type() != MatType.CV_32F || !srcImage.IsContinuous()) {
                Interlocked.Increment(ref fallbacks);
                return false;
            }

            gate.Wait();
            GpuEarlyChain chain = null;
            try {
                if (!pool.TryTake(out chain)) {
                    chain = new GpuEarlyChain(acc);
                }
                var gpuParams = new GpuEarlyParams {
                    HotpixelFiltering = p.HotpixelFiltering,
                    HotpixelThresholdingEnabled = p.HotpixelThresholdingEnabled,
                    HotpixelThreshold = p.HotpixelThreshold,
                    NoiseReductionRadius = p.NoiseReductionRadius,
                    StarMeasurementNoiseReductionEnabled = p.StarMeasurementNoiseReductionEnabled,
                    NoiseClippingMultiplier = p.NoiseClippingMultiplier,
                    EffectiveStructureLayers = effectiveStructureLayers,
                    StructureLayers = p.StructureLayers,
                    LocallyAdaptiveBinarization = p.LocallyAdaptiveBinarization,
                    AdaptiveNoiseBlockSize = p.AdaptiveNoiseBlockSize,
                };
                var result = chain.Run(srcImage, gpuParams, hotpixelAlreadyApplied);
                output = new EarlySpanOutput {
                    StructureMap = result.StructureMap,
                    MeasurementImage = result.MeasurementImage,
                    StructureNoise = result.StructureNoise,
                    MeasurementNoise = result.MeasurementNoise,
                    SigmaGrid = result.SigmaGrid,
                    AdaptiveMedianGrid = result.AdaptiveMedianGrid,
                    StructureMapMedian = result.StructureMapMedian,
                    HotpixelCount = result.HotpixelCount,
                };
                // Ownership of the Mats moved to the output; don't dispose them with the result wrapper.
                result.StructureMap = null;
                result.MeasurementImage = null;
                Interlocked.Increment(ref runs);
                return true;
            } catch (Exception e) {
                Logger.Warning($"GPU early span failed, falling back to CPU: {e.Message}");
                Interlocked.Increment(ref fallbacks);
                // The chain may hold buffers in an unknown state; drop it rather than repooling.
                try { chain?.Dispose(); } catch { }
                chain = null;
                output = null;
                return false;
            } finally {
                if (chain != null) {
                    pool.Add(chain);
                }
                gate.Release();
            }
        }

        public void Dispose() {
            while (pool.TryTake(out var chain)) {
                try { chain.Dispose(); } catch { }
            }
            gate.Dispose();
        }
    }
}
