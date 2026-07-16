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
using System.Diagnostics;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// The physical photon→electron→ADU sampling stage (design §"Noise model &amp; sampling"). Given a
    /// per-pixel <b>electron</b> accumulator whose value is the mean electron budget
    /// <c>λ_e = N_star + N_sky + N_dark</c> (already summed by the compositor), it draws shot noise,
    /// clips to the full well, adds read noise, converts to ADU with a bias pedestal, and applies the digital
    /// clip:
    /// <code>
    /// ne  = λ_e &lt; 40 ? Poisson(λ_e) : round(Normal(λ_e, √λ_e))   // Knuth exact vs CLT approximation
    /// ne  = min(ne, FullWell)
    /// e   = ne + Normal(0, σ_read(gain))
    /// adu = round(e / g_e(gain)) + pedestal
    /// ADU = clamp(adu, 0, 2^bits − 1)
    /// </code>
    /// The RNG is seeded so a frame is reproducible; the Gaussian primitive (<see cref="NextGaussian"/>) is
    /// public so test helpers can reuse the exact Box-Muller draw.
    ///
    /// <b>Thread-safety:</b> this type wraps a single <see cref="System.Random"/> and is <b>not</b> thread-safe.
    /// If frame development is ever parallelized, use one <see cref="NoiseGenerator"/> instance per thread/tile
    /// (each with its own seed) rather than sharing one across threads.
    /// </summary>
    public sealed class NoiseGenerator {

        /// <summary>
        /// λ_e threshold below which shot noise is drawn exactly with Knuth's Poisson algorithm; at or above it
        /// the CLT Gaussian approximation N(λ, √λ) is used. The design quoted 1000 (conservative), but Knuth
        /// costs ~λ <see cref="Math.Log"/> calls per pixel — ruinous at 61 MP where most pixels sit at
        /// λ≈50–800 (sky/faint stars). The Gaussian's mean/variance are exact and its distribution error is
        /// &lt;1% by λ≈30–40 (and negative draws are negligibly rare here: P(Z&lt;−√40)≈1e-10), so 40 keeps the
        /// physics honest while making a full frame fast.
        /// </summary>
        public const double PoissonToGaussianThreshold = 40.0;

        private readonly Random rng;

        /// <summary>Creates a deterministic generator seeded with <paramref name="seed"/>.</summary>
        public NoiseGenerator(int seed) {
            rng = new Random(seed);
        }

        /// <summary>
        /// One Box-Muller normal draw (cosine branch), consuming two uniforms — the same primitive the seeded
        /// test noise helper delegates to, so identical seeds reproduce identical sequences.
        /// </summary>
        public double NextGaussian(double mean, double standardDeviation) {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + standardDeviation * z;
        }

        /// <summary>
        /// One Poisson draw for mean <paramref name="lambda"/> using Knuth's multiplicative algorithm: one
        /// <see cref="Math.Exp"/> up front, then one multiply per iteration. Returns 0 for λ ≤ 0.
        /// </summary>
        /// <remarks>
        /// <para>This deliberately does <b>not</b> work in log space. Log space costs a <see cref="Math.Log"/>
        /// per iteration — about λ of them per pixel, which at 61 MP is the single dominant cost of a frame — to
        /// guard against <c>e^(−λ)</c> underflowing to zero. That cannot happen here: this branch only runs below
        /// <see cref="PoissonToGaussianThreshold"/> (40), <c>e^(−40)</c> ≈ 4.2e−18, and a double does not
        /// underflow until λ > 745. The guard was buying nothing and costing ~2× (measured).</para>
        ///
        /// <para>The two forms are equivalent, not merely similar: <c>log</c> is monotonic, so
        /// <c>Σ log(uᵢ) &gt; −λ</c> and <c>Π uᵢ &gt; e^(−λ)</c> are the same predicate over the same uniforms.
        /// Pinned draw-for-draw against the previous implementation by
        /// <c>PoissonDraw_MatchesLegacyLogSpaceFormExactly</c>.</para>
        /// </remarks>
        private long NextPoisson(double lambda) {
            if (lambda <= 0.0) return 0L;
            Debug.Assert(lambda < PoissonToGaussianThreshold,
                $"NextPoisson is only valid below the Gaussian threshold: e^(-lambda) underflows to 0 above ~745, " +
                $"degenerating the loop guard and silently saturating the draw. lambda={lambda}");
            var threshold = Math.Exp(-lambda); // > 0 below the Gaussian threshold; see remarks
            var product = 1.0;
            var k = 0L;
            do {
                ++k;
                product *= 1.0 - rng.NextDouble(); // uniform in (0,1]
            } while (product > threshold);
            return k - 1;
        }

        /// <summary>Test seam for <see cref="NextPoisson"/> — the shot-noise draw is otherwise only reachable
        /// through a whole frame, which cannot pin the draw sequence itself.</summary>
        internal long NextPoissonForTest(double lambda) => NextPoisson(lambda);

        /// <summary>
        /// Develops a per-pixel electron accumulator into a row-major ADU frame. The accumulator holds the mean
        /// electron budget λ_e per pixel; it is read (not written). <paramref name="gain"/> is the sensor's
        /// 0.1 dB-unit gain slider and <paramref name="biasPedestalAdu"/> the bias offset in ADU.
        /// </summary>
        public ushort[] DevelopToAdu(float[] electronAccumulator, SensorDefinition sensor, int gain, int biasPedestalAdu) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            var output = new ushort[electronAccumulator.Length];
            DevelopToAdu(electronAccumulator, output, sensor, gain, biasPedestalAdu);
            return output;
        }

        /// <summary>
        /// Develops the electron accumulator into a caller-provided ADU buffer (same length), avoiding an
        /// allocation. See <see cref="DevelopToAdu(float[], SensorDefinition, int, int)"/>.
        /// </summary>
        public void DevelopToAdu(float[] electronAccumulator, ushort[] output, SensorDefinition sensor, int gain, int biasPedestalAdu) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (output.Length != electronAccumulator.Length) throw new ArgumentException("Output length must match the accumulator length.", nameof(output));

            var fullWell = sensor.FullWellElectrons;
            var readNoise = sensor.ReadNoiseElectronsAtGain(gain);
            var electronsPerAdu = sensor.ElectronsPerAduAtGain(gain);
            var maxAdu = sensor.MaxAdu;

            for (var i = 0; i < electronAccumulator.Length; ++i) {
                double lambda = electronAccumulator[i];
                double ne = lambda < PoissonToGaussianThreshold
                    ? NextPoisson(lambda)
                    : Math.Round(NextGaussian(lambda, Math.Sqrt(lambda)));
                if (ne < 0.0) ne = 0.0;
                if (ne > fullWell) ne = fullWell;

                var electrons = ne + NextGaussian(0.0, readNoise);
                var adu = (long)Math.Round(electrons / electronsPerAdu) + biasPedestalAdu;
                if (adu < 0) adu = 0;
                else if (adu > maxAdu) adu = maxAdu;
                output[i] = (ushort)adu;
            }
        }
    }
}
