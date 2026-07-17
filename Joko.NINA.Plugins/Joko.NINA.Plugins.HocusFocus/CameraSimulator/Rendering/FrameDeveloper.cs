#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Develops a per-pixel electron accumulator into an ADU frame in parallel.
    ///
    /// <para>Development is the dominant cost of a frame — at 61 MP it is ~90% of the render (measured ~12 s of
    /// a ~13 s render), because every pixel draws shot noise and read noise. It used to run single-threaded so
    /// that one seeded <see cref="NoiseGenerator"/> produced a reproducible frame. This type keeps that
    /// reproducibility while using every core: the frame is cut into <see cref="StripeCount"/> disjoint index
    /// ranges, and each stripe develops with its <b>own</b> generator seeded from (frame seed, stripe index).
    /// Disjoint ranges mean no shared RNG and no shared writes, so the result is independent of the order the
    /// stripes happen to run in.</para>
    /// </summary>
    public static class FrameDeveloper {

        /// <summary>
        /// The number of disjoint index ranges the frame is cut into.
        ///
        /// <para><b>This is a fixed constant and must never become <see cref="Environment.ProcessorCount"/>.</b>
        /// Each stripe draws from its own RNG stream, so the partition decides which stream lands on which pixel
        /// — the partition is part of the frame's identity, not an execution detail. A core-count-derived
        /// partition would make the same seed render differently on different machines, silently breaking the
        /// "bit-deterministic for a fixed seed regardless of core count" guarantee this plugin already makes for
        /// the stamping stage. The <i>degree of parallelism</i> is free to vary with the machine; the
        /// <i>partition</i> is not.</para>
        ///
        /// <para><b>Why 64 — a floor, not a balancing knob.</b> This is not a tuning dial worth turning:
        /// <see cref="Parallel.For"/> hands stripes out <i>dynamically</i> — a worker takes the next stripe the
        /// moment it finishes one — so there is no wave quantisation to tune away, and 64 already balances ~24
        /// workers to within ~1.5% (one stripe). Measured at 61 MP on 24 cores, 64 stripes beat 256 on the
        /// representative flat field (382 ms vs 408 ms, ~6% faster) and tied on a ragged one; a finer partition
        /// only costs per-stripe setup and locality. The residual gap from linear speedup (a measured ~7.9× of a
        /// theoretical ~24×) is memory bandwidth and all-core clock — not partition granularity, so no stripe count
        /// recovers it. The one real constraint is that a partition can never occupy more cores than it has
        /// stripes, which makes 64 a floor set by expected core counts (NINA typically runs on 4–8 core mini-PCs)
        /// rather than a knob to balance load.</para>
        /// </summary>
        public const int StripeCount = 64;

        /// <summary>Develops the accumulator into a freshly allocated ADU frame. See <see cref="DevelopToAdu(float[], ushort[], SensorDefinition, int, int, int, CancellationToken)"/>.</summary>
        public static ushort[] DevelopToAdu(float[] electronAccumulator, SensorDefinition sensor, int gain, int biasPedestalAdu, int seed, CancellationToken token) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            var output = new ushort[electronAccumulator.Length];
            DevelopToAdu(electronAccumulator, output, sensor, gain, biasPedestalAdu, seed, token);
            return output;
        }

        /// <summary>
        /// Develops the accumulator into a caller-provided ADU buffer of the same length. Deterministic for a
        /// given (<paramref name="seed"/>, length) regardless of core count or scheduling.
        ///
        /// <para>Known and accepted: <see cref="Random"/>'s seeded legacy path folds the seed through
        /// <c>Math.Abs</c>, so seeds <c>s</c> and <c>−s</c> yield identical streams, halving the effective seed
        /// space to 2^31. Two stripes drawing such a pair would render as a duplicated band: with
        /// <see cref="StripeCount"/> = 64 there are C(64,2) ≈ 2k pairs, so the chance is ~1e-6 per frame. Judged
        /// not worth the complexity of masking or a different RNG. Considered, not missed — but note this scales
        /// with <see cref="StripeCount"/>², so revisit it if the partition ever grows much beyond 64.</para>
        /// </summary>
        public static void DevelopToAdu(float[] electronAccumulator, ushort[] output, SensorDefinition sensor, int gain, int biasPedestalAdu, int seed, CancellationToken token) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (output.Length != electronAccumulator.Length) throw new ArgumentException("Output length must match the accumulator length.", nameof(output));

            token.ThrowIfCancellationRequested();
            var length = electronAccumulator.Length;

            // Routed through the shared CPU governor so a render cannot oversubscribe the box against the star
            // detection running alongside it (a render is kicked off at StartExposure, while the previous
            // autofocus point is still being detected). Bounding concurrency cannot change a pixel: the partition
            // below depends only on (stripe, StripeCount, length), never on the scheduler or the thread count.
            var options = ParallelExecution.CreateOptions(0, token);

            Parallel.For(0, StripeCount, options, stripe => {
                // Boundaries come from (stripe, StripeCount, length) alone — never from the thread count.
                var from = (int)((long)stripe * length / StripeCount);
                var to = (int)((long)(stripe + 1) * length / StripeCount);
                if (from >= to) {
                    // Frames shorter than StripeCount round several stripes onto the same index, leaving the
                    // empties at the front and interspersed — never trailing (stripe StripeCount-1 always ends
                    // at length, so it is non-empty for any length >= 1). At length=7, stripes 0..8 are empty
                    // and stripe 9 is the first to own a pixel.
                    return;
                }
                options.CancellationToken.ThrowIfCancellationRequested();
                new NoiseGenerator(SeedMixer.Combine(seed, stripe))
                    .DevelopRange(electronAccumulator, output, from, to, sensor, gain, biasPedestalAdu);
            });
        }
    }
}
