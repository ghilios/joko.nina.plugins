#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
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
        /// <i>partition</i> is not. 64 sits above any core count we expect (so every core stays fed even when the
        /// stripes finish unevenly) and is small enough that per-stripe generator setup is noise.</para>
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
        /// <c>Math.Abs</c>, so seeds <c>s</c> and <c>−s</c> yield identical streams. Two stripes drawing such a
        /// pair would render as a duplicated band — at 64 stripes that is a ~1e-6 per-frame chance, judged not
        /// worth the complexity of masking or a different RNG. Considered, not missed.</para>
        /// </summary>
        public static void DevelopToAdu(float[] electronAccumulator, ushort[] output, SensorDefinition sensor, int gain, int biasPedestalAdu, int seed, CancellationToken token) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (output.Length != electronAccumulator.Length) throw new ArgumentException("Output length must match the accumulator length.", nameof(output));

            token.ThrowIfCancellationRequested();
            var length = electronAccumulator.Length;
            var options = new ParallelOptions { CancellationToken = token };

            Parallel.For(0, StripeCount, options, stripe => {
                // Boundaries come from (stripe, StripeCount, length) alone — never from the thread count.
                var from = (int)((long)stripe * length / StripeCount);
                var to = (int)((long)(stripe + 1) * length / StripeCount);
                if (from >= to) {
                    return; // frames shorter than StripeCount leave trailing stripes empty
                }
                options.CancellationToken.ThrowIfCancellationRequested();
                new NoiseGenerator(SeedMixer.Combine(seed, stripe))
                    .DevelopRange(electronAccumulator, output, from, to, sensor, gain, biasPedestalAdu);
            });
        }
    }
}
