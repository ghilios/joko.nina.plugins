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

namespace NINA.Joko.Plugins.HocusFocus.Gpu {

    /// <summary>
    /// Lazy process-wide CUDA context/accelerator for the GPU feasibility spike (bench-gpu and the
    /// optimize --gpu early pipeline). Initialization failures are captured, never thrown: callers get
    /// null plus a reason so every GPU path can fall back to CPU gracefully. Disposed on process exit.
    ///
    /// Blackwell note: ILGPU 1.5.3 is the minimum version that JITs on RTX 50-series (sm_120); kernels
    /// must avoid XMath/LibDevice transcendentals (broken on compute_100+ until ILGPU 1.6) — the early
    /// pipeline only needs add/mul/compare/abs/min/max/sqrt/floor, which are PTX-native.
    /// </summary>
    public static class GpuDevice {
        private static readonly object initLock = new object();
        private static bool initAttempted;
        private static Context context;
        private static CudaAccelerator accelerator;
        private static string unavailableReason;

        /// <summary>
        /// Returns the process-wide CUDA accelerator, or null with a human-readable reason. Thread-safe;
        /// the first caller pays the JIT/context cost.
        /// </summary>
        public static CudaAccelerator TryGet(out string reason) {
            lock (initLock) {
                if (!initAttempted) {
                    initAttempted = true;
                    try {
                        context = Context.Create(builder => builder.Cuda());
                        var device = context.GetCudaDevice(0);
                        accelerator = device.CreateCudaAccelerator(context);
                        AppDomain.CurrentDomain.ProcessExit += (_, __) => DisposeDevice();
                        unavailableReason = null;
                    } catch (Exception e) {
                        DisposeDevice();
                        unavailableReason = $"{e.GetType().Name}: {e.Message}";
                    }
                }
                reason = unavailableReason;
                return accelerator;
            }
        }

        public static string DescribeBanner(CudaAccelerator acc) {
            var device = acc.Device as CudaDevice;
            return $"GPU: {acc.Name}, CC {device?.Architecture.ToString() ?? "?"}, " +
                   $"{acc.MemorySize / (1024.0 * 1024 * 1024):F1} GB, " +
                   $"warp={acc.WarpSize}, maxThreads/group={acc.MaxNumThreadsPerGroup}, " +
                   $"multiprocessors={acc.NumMultiprocessors}, ILGPU {typeof(Context).Assembly.GetName().Version}";
        }

        private static void DisposeDevice() {
            lock (initLock) {
                try { accelerator?.Dispose(); } catch { }
                try { context?.Dispose(); } catch { }
                accelerator = null;
                context = null;
            }
        }
    }
}
