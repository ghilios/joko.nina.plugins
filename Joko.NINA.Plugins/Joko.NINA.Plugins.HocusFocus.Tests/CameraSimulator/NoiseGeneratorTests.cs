#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator {

    [TestFixture]
    public class NoiseGeneratorTests {

        private const int Gain = 0;
        private const int Pedestal = 500;

        private static SensorDefinition Sensor => SensorRegistry.Get(SonySensorModel.IMX455);

        private static float[] Flat(int count, double lambdaElectrons) {
            var a = new float[count];
            for (var i = 0; i < count; ++i) a[i] = (float)lambdaElectrons;
            return a;
        }

        private static (double mean, double variance) MeanVariance(ushort[] frame) {
            double sum = 0.0;
            foreach (var v in frame) sum += v;
            var mean = sum / frame.Length;
            double sq = 0.0;
            foreach (var v in frame) {
                var d = v - mean;
                sq += d * d;
            }
            return (mean, sq / frame.Length);
        }

        [TestCase(20.0)]    // Poisson regime (λ < threshold)
        [TestCase(500.0)]   // Gaussian regime (λ ≥ threshold)
        public void MeanAndVariance_MatchPhysicalModel(double lambda) {
            var sensor = Sensor;
            var gE = sensor.ElectronsPerAduAtGain(Gain);
            var readNoise = sensor.ReadNoiseElectronsAtGain(Gain);

            var frame = new NoiseGenerator(1234).DevelopToAdu(Flat(300 * 300, lambda), sensor, Gain, Pedestal);
            var (mean, variance) = MeanVariance(frame);

            var expectedMean = lambda / gE + Pedestal;
            var expectedVar = (lambda + readNoise * readNoise) / (gE * gE);
            Assert.Multiple(() => {
                Assert.That(mean, Is.EqualTo(expectedMean).Within(0.005 * expectedMean), "mean ≈ λ/g_e + pedestal");
                Assert.That(variance, Is.EqualTo(expectedVar).Within(0.05 * expectedVar), "variance ≈ (λ + σ_read²)/g_e²");
            });
        }

        [Test]
        public void PhotonTransfer_SlopeIsInverseGain_InterceptIsReadNoiseSquared() {
            var sensor = Sensor;
            var gE = sensor.ElectronsPerAduAtGain(Gain);
            var readNoise = sensor.ReadNoiseElectronsAtGain(Gain);
            // Span both the Poisson (< threshold) and Gaussian (≥ threshold) branches.
            var lambdas = new[] { 15.0, 30.0, 60.0, 120.0, 240.0, 480.0, 960.0, 1920.0 };

            // Fit variance vs (mean − pedestal): slope 1/g_e, intercept (σ_read/g_e)².
            double n = lambdas.Length, sx = 0, sy = 0, sxx = 0, sxy = 0;
            var seed = 900;
            foreach (var lambda in lambdas) {
                var frame = new NoiseGenerator(seed++).DevelopToAdu(Flat(300 * 300, lambda), sensor, Gain, Pedestal);
                var (mean, variance) = MeanVariance(frame);
                var x = mean - Pedestal;
                sx += x; sy += variance; sxx += x * x; sxy += x * variance;
            }
            var slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
            var intercept = (sy - slope * sx) / n;

            var expectedSlope = 1.0 / gE;
            var expectedIntercept = (readNoise / gE) * (readNoise / gE);
            Assert.Multiple(() => {
                Assert.That(slope, Is.EqualTo(expectedSlope).Within(0.03 * expectedSlope), "PTC slope = 1/g_e");
                Assert.That(intercept, Is.EqualTo(expectedIntercept).Within(0.2 * expectedIntercept + 3.0), "PTC intercept = (σ_read/g_e)²");
            });
        }

        [Test]
        public void SameSeed_ProducesIdenticalFrame() {
            var sensor = Sensor;
            var electrons = Flat(128 * 128, 300.0);
            var a = new NoiseGenerator(777).DevelopToAdu(electrons, sensor, Gain, Pedestal);
            var b = new NoiseGenerator(777).DevelopToAdu(electrons, sensor, Gain, Pedestal);
            Assert.That(a, Is.EqualTo(b), "identical seed ⇒ identical frame");
        }

        [Test]
        public void DifferentSeed_ProducesDifferentFrame() {
            var sensor = Sensor;
            var electrons = Flat(128 * 128, 300.0);
            var a = new NoiseGenerator(1).DevelopToAdu(electrons, sensor, Gain, Pedestal);
            var b = new NoiseGenerator(2).DevelopToAdu(electrons, sensor, Gain, Pedestal);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void FullWellAndDigitalClamp_AreEnforced() {
            var sensor = Sensor;
            // λ far above the full well: shot-clipped to FullWell, then digital-clamped to MaxAdu.
            var frame = new NoiseGenerator(55).DevelopToAdu(Flat(64 * 64, 5.0 * sensor.FullWellElectrons), sensor, Gain, Pedestal);
            foreach (var v in frame) {
                Assert.That(v, Is.EqualTo((ushort)sensor.MaxAdu));
            }
        }

        [Test]
        public void NextGaussian_MatchesLegacyBoxMuller() {
            // The seeded Box-Muller the relocated test noise helper delegates to must reproduce the exact draw.
            const int seed = 4242;
            const double sigma = 0.7;
            var gen = new NoiseGenerator(seed);
            var reference = new Random(seed);
            for (var i = 0; i < 1000; ++i) {
                var u1 = 1.0 - reference.NextDouble();
                var u2 = 1.0 - reference.NextDouble();
                var expected = sigma * (Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
                Assert.That(gen.NextGaussian(0.0, sigma), Is.EqualTo(expected).Within(1e-12));
            }
        }

        // Reference log-space implementation — the form NoiseGenerator shipped before the multiplicative rewrite.
        // Kept here so the rewrite is pinned against the exact algorithm it replaced, not against a re-derivation.
        private static long ReferencePoissonLogSpace(Random rng, double lambda) {
            if (lambda <= 0.0) return 0L;
            var target = -lambda;
            var logProduct = 0.0;
            var k = 0L;
            do {
                ++k;
                logProduct += Math.Log(1.0 - rng.NextDouble());
            } while (logProduct > target);
            return k - 1;
        }

        [Test]
        [TestCase(0.5)]
        [TestCase(5.0)]
        [TestCase(14.62)]
        [TestCase(39.9)]
        public void PoissonDraw_MatchesLegacyLogSpaceFormExactly(double lambda) {
            // Same seed => same uniform sequence => the two forms must agree draw for draw, because
            // sum(log(u)) > -lambda and prod(u) > exp(-lambda) are the same predicate.
            const int seed = 20260716;
            const int draws = 50_000;

            var reference = new Random(seed);
            var expected = new long[draws];
            for (var i = 0; i < draws; ++i) {
                expected[i] = ReferencePoissonLogSpace(reference, lambda);
            }

            var generator = new NoiseGenerator(seed);
            var actual = new long[draws];
            for (var i = 0; i < draws; ++i) {
                actual[i] = generator.NextPoissonForTest(lambda);
            }

            Assert.That(actual, Is.EqualTo(expected), $"multiplicative form must reproduce the log-space draws at lambda={lambda}");
        }

        [Test]
        public void PoissonToGaussianThreshold_LeavesHugeUnderflowHeadroom() {
            // The multiplicative form is only safe because exp(-lambda) cannot reach zero on this branch.
            // Doubles underflow to exactly 0 at lambda > 745, so the threshold must stay far below that.
            Assert.Multiple(() => {
                Assert.That(Math.Exp(-NoiseGenerator.PoissonToGaussianThreshold), Is.GreaterThan(0.0),
                    "exp(-threshold) must not underflow, or the loop guard degenerates to `product > 0`");
                Assert.That(NoiseGenerator.PoissonToGaussianThreshold, Is.LessThan(745.0),
                    "745 is where exp(-lambda) reaches 0 in double precision");
            });
        }
    }
}
