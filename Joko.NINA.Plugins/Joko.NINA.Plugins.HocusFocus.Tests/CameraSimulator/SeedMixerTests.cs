using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class SeedMixerTests {

    [Test]
    public void Combine_IsDeterministic() {
        Assert.That(SeedMixer.Combine(42, 7, 3), Is.EqualTo(SeedMixer.Combine(42, 7, 3)));
    }

    [Test]
    public void Combine_IsOrderSensitive() {
        Assert.That(SeedMixer.Combine(1, 2), Is.Not.EqualTo(SeedMixer.Combine(2, 1)));
    }

    [Test]
    public void Combine_HasNoCollisionsAcrossARealisticExposureGrid() {
        // Every (focuser position, exposure counter) an autofocus run can produce.
        var seeds = new HashSet<int>();
        for (var focuser = 24_900; focuser <= 25_100; ++focuser) {
            for (var exposure = 0; exposure < 50; ++exposure) {
                seeds.Add(SeedMixer.Combine(42, focuser, exposure));
            }
        }
        Assert.That(seeds.Count, Is.EqualTo(201 * 50), "distinct inputs must not collide");
    }

    [Test]
    public void Combine_AvalanchesAdjacentInputs() {
        // Adjacent inputs (stripe i vs i+1) must differ in ~half their bits, not one.
        // Without avalanche a +1 input yields a +1 output and neighbouring stripes correlate.
        var distances = new List<int>();
        for (var i = 0; i < 64; ++i) {
            var a = (uint)SeedMixer.Combine(42, i);
            var b = (uint)SeedMixer.Combine(42, i + 1);
            distances.Add(System.Numerics.BitOperations.PopCount(a ^ b));
        }
        Assert.That(distances.Average(), Is.EqualTo(16.0).Within(5.0),
            "adjacent seeds should differ in roughly half of their 32 bits");
        Assert.That(distances.Min(), Is.GreaterThan(4), "no adjacent pair may be nearly identical");
    }
}
