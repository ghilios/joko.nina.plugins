#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Diagnostics;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Per-phase wall-clock and cardinality measurements for one <see cref="StarFieldCompositor.Render"/>
    /// call, filled in by the <c>internal</c> render overload when a non-null instance is passed. Exists for
    /// the <c>bench-simrender</c> harness in TestApp; production callers pass null and pay nothing.
    ///
    /// <para><b>Why the compositor is instrumented at all</b> — rather than timing two whole renders and
    /// differencing them. <see cref="NoiseGenerator"/> branches at
    /// <see cref="NoiseGenerator.PoissonToGaussianThreshold"/>: below it a Poisson draw costs ~λ uniform
    /// samples, at or above it a fixed four. Development cost therefore swings by ~10× with λ, and <b>stars
    /// change λ</b>. A "dense frame minus starless frame" difference silently books that development delta
    /// as PSF cost, which is exactly the quantity the harness is trying to isolate. The phases have to be
    /// measured where they happen.</para>
    ///
    /// <para><b>Contract:</b> passing null must render byte-identically to the public overloads — every
    /// timing call site is guarded, so a production render does not even read the clock. This mirrors the
    /// <c>truthSink</c> overload's contract, and is asserted by
    /// <c>StarFieldCompositorTests.Render_NullTimings_IsByteIdenticalToPublicOverload</c>.</para>
    /// </summary>
    internal sealed class RenderPhaseTimings {

        /// <summary>Catalog query: resolving the reader and enumerating its stars into a list.</summary>
        public double CatalogQueryMs { get; set; }

        /// <summary>
        /// Stamp-job build: projection, per-star defocus, quantization, kernel resolution and flux.
        /// <b>Includes</b> <see cref="KernelGenerateMs"/>.
        /// </summary>
        public double StampJobBuildMs { get; set; }

        /// <summary>PSF kernel generation alone — the cache misses. The one phase that is pure added cost.</summary>
        public double KernelGenerateMs { get; set; }

        /// <summary>Stamping every job into the accumulator plus folding in the uniform background.</summary>
        public double StampMs { get; set; }

        /// <summary>Development to ADU: Poisson/read noise and the electron→ADU conversion.</summary>
        public double DevelopMs { get; set; }

        /// <summary>Catalog rows returned for the pointing, before projection rejects any.</summary>
        public int StarsQueried { get; set; }

        /// <summary>Accepted stamp jobs — on-frame stars plus off-frame stars whose wings spill onto the sensor.</summary>
        public int StampJobs { get; set; }

        /// <summary>
        /// Distinct PSF kernels built for this frame. The headline cardinality number: one with aberrations
        /// off, and the quantity that grows when the kernel cache key gains axes.
        /// </summary>
        public int DistinctKernels { get; set; }

        /// <summary>Approximate bytes held by the kernel cache at the end of the job build.</summary>
        public long KernelCacheBytes { get; set; }

        /// <summary>Largest <see cref="PsfKernel.Radius"/> in the cache, in pixels. Kernel cost and size both go as R².</summary>
        public int MaxKernelRadius { get; set; }

        /// <summary>Sum of the measured phases, in ms. Not the same as the caller's stopwatch — it excludes setup.</summary>
        public double TotalPhaseMs => CatalogQueryMs + StampJobBuildMs + StampMs + DevelopMs;

        /// <summary>Elapsed milliseconds since a <see cref="Stopwatch.GetTimestamp"/> reading.</summary>
        public static double ElapsedMs(long startTimestamp) {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }
    }
}
