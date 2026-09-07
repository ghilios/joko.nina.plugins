#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILGPU.Runtime.Cuda;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Gpu {

    /// <summary>
    /// The heuristic deciding whether the star-detection OPTIMIZATION path uses the GPU for a run
    /// (initial logic, expected to be tuned with field experience):
    ///
    /// 1. The user's GpuAccelerationEnabled option must be on (checked by callers who own the options).
    /// 2. A CUDA accelerator must initialize (cached probe; NVIDIA-only by construction — ILGPU's Cuda
    ///    backend — so integrated/AMD GPUs never qualify).
    /// 3. The frame must be at least <see cref="MinPixels"/>: below ~2 MP a CPU early build is only a few
    ///    tens of ms and the PCIe transfers + launch overhead erase the win.
    /// 4. The device must have VRAM headroom for the working set: ~5 frame-sized float buffers per chain,
    ///    2 pooled chains, plus 1 GB slack for the rest of the system.
    ///
    /// A separate runtime latch lives in <see cref="GpuEarlyPipeline"/>: repeated per-build failures stop
    /// further GPU attempts for the process. Scope note: only the optimization wizard/harness consult this
    /// policy — autofocus, sensor modeling, and single-frame detection never do.
    /// </summary>
    public static class GpuAccelerationPolicy {
        public const long MinPixels = 2_000_000;
        private const long WorkingSetBuffersPerChain = 5;
        private const int PooledChains = 2;
        private const long VramSlackBytes = 1L << 30;

        /// <summary>
        /// Option + device check only (no frame-size/VRAM gate) — for the offline harness, which stamps its
        /// params before any frames are loaded and whose operator has already chosen the machine.
        /// </summary>
        public static bool ShouldUseForOptimization(bool optionEnabled, out string reason) {
            if (!optionEnabled) {
                reason = "GPU acceleration disabled";
                return false;
            }
            var accelerator = GpuDevice.TryGet(out var unavailableReason);
            if (accelerator == null) {
                reason = $"no usable CUDA device ({unavailableReason})";
                return false;
            }
            reason = $"using {accelerator.Name} ({accelerator.MemorySize / (1 << 30)} GB)";
            return true;
        }

        /// <summary>
        /// Decides GPU use for an optimization run over frames of the given dimensions. Returns false with a
        /// human-readable reason (for the log/UI) when the GPU should not be used.
        /// </summary>
        public static bool ShouldUseForOptimization(bool optionEnabled, int width, int height, out string reason) {
            if (!optionEnabled) {
                reason = "GPU acceleration disabled in star detection options";
                return false;
            }
            long pixels = (long)width * height;
            if (pixels < MinPixels) {
                reason = $"frame too small ({pixels / 1e6:F1} MP < {MinPixels / 1e6:F0} MP) — CPU is faster once transfer overhead is paid";
                return false;
            }
            var accelerator = GpuDevice.TryGet(out var unavailableReason);
            if (accelerator == null) {
                reason = $"no usable CUDA device ({unavailableReason})";
                return false;
            }
            long workingSet = WorkingSetBuffersPerChain * PooledChains * pixels * sizeof(float);
            if (accelerator.MemorySize < workingSet + VramSlackBytes) {
                reason = $"insufficient VRAM ({accelerator.MemorySize / (1 << 20)} MB < {(workingSet + VramSlackBytes) / (1 << 20)} MB needed for {pixels / 1e6:F0} MP frames)";
                return false;
            }
            reason = $"using {accelerator.Name} ({accelerator.MemorySize / (1 << 30)} GB)";
            return true;
        }
    }
}
