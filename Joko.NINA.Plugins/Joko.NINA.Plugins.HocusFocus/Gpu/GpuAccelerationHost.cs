#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Gpu {

    /// <summary>
    /// Process-wide holder of the shared <see cref="GpuEarlyPipeline"/> consulted by
    /// <c>StarDetector.BuildDetectionContextInternal</c> when a params bundle carries
    /// <c>AllowGpuAcceleration</c> (set ONLY by the optimization wizard/harness — never by autofocus,
    /// sensor modeling, or single-frame detection). Created lazily on first use; a null return means
    /// "run the CPU span" (no device, init failed, or the pipeline latched itself off after repeated
    /// per-build failures).
    /// </summary>
    public static class GpuAccelerationHost {
        private static readonly object initLock = new object();
        private static bool initAttempted;
        private static GpuEarlyPipeline pipeline;

        internal static IEarlyPipelineAccelerator TryGetForBuild() {
            var p = TryGetPipeline();
            return p != null && !p.LatchedOff ? p : null;
        }

        /// <summary>The pipeline if one was already created — never triggers device initialization (for
        /// exit-time stats lines that must not probe CUDA on a run that never used it).</summary>
        internal static GpuEarlyPipeline PeekPipeline() {
            lock (initLock) {
                return pipeline;
            }
        }

        /// <summary>Shared pipeline instance for stats/reporting, or null when the GPU never initialized.</summary>
        internal static GpuEarlyPipeline TryGetPipeline() {
            lock (initLock) {
                if (!initAttempted) {
                    initAttempted = true;
                    var accelerator = GpuDevice.TryGet(out _);
                    if (accelerator != null) {
                        pipeline = new GpuEarlyPipeline(accelerator);
                        AppDomain.CurrentDomain.ProcessExit += (_, __) => {
                            try { pipeline?.Dispose(); } catch { }
                        };
                    }
                }
                return pipeline;
            }
        }
    }
}
