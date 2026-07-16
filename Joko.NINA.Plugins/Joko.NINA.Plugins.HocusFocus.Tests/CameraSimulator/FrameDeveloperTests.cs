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
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class FrameDeveloperTests {

    private static SensorDefinition Sensor => SensorRegistry.Get(SonySensorModel.IMX533);

    private static float[] Flat(int n, float lambda) {
        var a = new float[n];
        Array.Fill(a, lambda);
        return a;
    }

    [Test]
    public void SameSeed_ProducesIdenticalFrame() {
        var acc = Flat(200_000, 14.62f);
        var a = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 4242, CancellationToken.None);
        var b = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 4242, CancellationToken.None);
        Assert.That(a, Is.EqualTo(b), "identical seed => identical frame");
    }

    [Test]
    public void DifferentSeed_ProducesDifferentFrame() {
        var acc = Flat(200_000, 14.62f);
        var a = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 1, CancellationToken.None);
        var b = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 2, CancellationToken.None);
        Assert.That(a, Is.Not.EqualTo(b));
    }

    // THE load-bearing test for this task. The stripe partition selects which RNG stream lands on which
    // pixel, so it is part of the frame's identity. If StripeCount ever becomes ProcessorCount-derived, the
    // same seed renders differently on different machines and the plugin's determinism claim silently breaks.
    [Test]
    public void StripePartition_DoesNotDependOnProcessorCount() {
        Assert.That(FrameDeveloper.StripeCount, Is.EqualTo(64),
            "the partition must be a fixed constant, never Environment.ProcessorCount");
    }

    [Test]
    public void Mean_MatchesPoissonModel_AcrossStripeBoundaries() {
        // Every stripe must actually develop its slice: an off-by-one in the partition would leave a band
        // of zeros, which a mean check over the whole frame catches.
        const float lambda = 14.62f;
        var acc = Flat(1_000_000, lambda);
        var frame = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 99, CancellationToken.None);
        var electronsPerAdu = Sensor.ElectronsPerAduAtGain(100);
        var mean = frame.Select(v => (v - 500) * electronsPerAdu).Average();
        Assert.That(mean, Is.EqualTo(lambda).Within(0.05 * lambda));
        Assert.That(frame.Count(v => v == 0), Is.Zero, "a zero band would mean a stripe was skipped");
    }

    [Test]
    public void ShorterThanStripeCount_DevelopsEveryPixel() {
        // len < StripeCount makes some stripes empty; they must be skipped, not throw or corrupt.
        var acc = Flat(7, 200.0f);
        var frame = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 5, CancellationToken.None);
        Assert.That(frame.Length, Is.EqualTo(7));
        Assert.That(frame.All(v => v > 500), "every pixel should carry signal above the pedestal");
    }

    [Test]
    public void Cancellation_IsObserved() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            FrameDeveloper.DevelopToAdu(Flat(200_000, 14.62f), Sensor, 100, 500, 1, cts.Token));
    }
}
