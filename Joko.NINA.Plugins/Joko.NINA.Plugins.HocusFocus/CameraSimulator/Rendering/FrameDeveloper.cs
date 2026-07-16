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
        /// <para><b>Why 256 — headroom, not speed.</b> It is <i>not</i> faster than 64 today: measured at 61 MP on
        /// 24 cores, 64 stripes runs a flat field in 382 ms against 256's 408 ms (64 ahead by ~6%), and the two tie
        /// on a ragged one. There is no "last wave partly idle" effect to recover, because <see cref="Parallel.For"/>
        /// hands stripes out <i>dynamically</i> — a worker takes the next stripe the moment it finishes one — so 64
        /// stripes already balance ~24 workers to within a stripe. (The shortfall from the ideal ~24× to a measured
        /// ~7.9× is memory bandwidth and all-core clock, which no partition can fix.) 256 is chosen anyway because
        /// this constant is a <b>one-way door</b>: it is part of frame identity, so raising it later would
        /// invalidate every stored reference frame. A partition can never occupy more cores than it has stripes, so
        /// 64 would cap a 96-core box at ~2/3 and waste anything beyond 64 cores outright. ~6% on today's hardware
        /// is a cheap premium for headroom that cannot be bought back later. The overhead is bounded: a seeded
        /// <see cref="Random"/> is ~232 bytes, so 256 of them is ~60 KB per frame, and empty stripes return before
        /// allocating one.</para>
        /// </summary>
        public const int StripeCount = 256;

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
        /// <see cref="StripeCount"/> = 256 there are C(256,2) ≈ 32.6k pairs, so the chance is ~1.5e-5 per frame
        /// (~1 in 66k). Judged not worth the complexity of masking or a different RNG. Considered, not missed —
        /// but note this scales with <see cref="StripeCount"/>², so revisit it if the partition ever grows much
        /// beyond 256.</para>
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
                    // at length, so it is non-empty for any length >= 1). At length=7, stripes 0..35 are empty
                    // and stripe 36 is the first to own a pixel.
                    return;
                }
                options.CancellationToken.ThrowIfCancellationRequested();
                new NoiseGenerator(SeedMixer.Combine(seed, stripe))
                    .DevelopRange(electronAccumulator, output, from, to, sensor, gain, biasPedestalAdu);
            });
        }
    }
}
