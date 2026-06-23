#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;
using NUnit.Framework;
using TestApp;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Bank;

/// <summary>
/// Unit tests for the AF-bank verification aggregation: the bank-level median (NaN-skipping) and the
/// NoiseClippingMultiplier recommendation rule that answers "was the shipped 4→2 default right, or should NC be
/// derived adaptively?". The rule: among swept NC values whose bank-median precision clears a floor, pick the
/// LOWEST NC (recall rises as NC falls); if the lowest swept NC still clears the floor, recall is still rising at
/// the bottom of the range, so flag that a lower or per-frame-adaptive NC may be warranted.
/// </summary>
[TestFixture]
public class BankVerifyAggregateTests {

    [Test]
    public void Median_OddLength_SkipsNaN() {
        var m = BankVerifyAggregate.Median(new[] { 3.0, 1.0, double.NaN, 2.0 });
        Assert.That(m, Is.EqualTo(2.0).Within(1e-12));
    }

    [Test]
    public void Median_EmptyOrAllNaN_IsNaN() {
        Assert.That(double.IsNaN(BankVerifyAggregate.Median(new double[0])), Is.True);
        Assert.That(double.IsNaN(BankVerifyAggregate.Median(new[] { double.NaN, double.NaN })), Is.True);
    }

    private static BankVerifyAggregate.NcPoint P(double nc, double recallHigh, double precision) =>
        new BankVerifyAggregate.NcPoint { Nc = nc, MedianRecallHigh = recallHigh, MedianPrecision = precision };

    [Test]
    public void Recommend_AllPassFloor_PicksLowestNc_AndFlagsAdaptive() {
        // Recall rises as NC falls and all clear the precision floor → NC=2 wins, but it's the range floor, so
        // recall is still climbing — recommend considering an even lower / adaptive NC. This is "4→2 confirmed".
        var pts = new List<BankVerifyAggregate.NcPoint> { P(2, 0.46, 0.80), P(3, 0.30, 0.88), P(4, 0.18, 0.92) };
        var rec = BankVerifyAggregate.RecommendNoiseClip(pts, precisionFloor: 0.60);
        Assert.Multiple(() => {
            Assert.That(rec.Nc, Is.EqualTo(2.0));
            Assert.That(rec.ConsiderAdaptive, Is.True);
        });
    }

    [Test]
    public void Recommend_LowestNcFailsFloor_PicksNextNcThatPasses() {
        // NC=2 over-recalls into noise (precision below floor); NC=3 is the sweet spot → "4→2 overshot, 3 is better".
        var pts = new List<BankVerifyAggregate.NcPoint> { P(2, 0.50, 0.45), P(3, 0.34, 0.72), P(4, 0.20, 0.90) };
        var rec = BankVerifyAggregate.RecommendNoiseClip(pts, precisionFloor: 0.60);
        Assert.Multiple(() => {
            Assert.That(rec.Nc, Is.EqualTo(3.0));
            Assert.That(rec.ConsiderAdaptive, Is.False);
        });
    }

    [Test]
    public void Recommend_NonePassFloor_PicksHighestPrecision_AndFlagsAdaptive() {
        var pts = new List<BankVerifyAggregate.NcPoint> { P(2, 0.50, 0.30), P(3, 0.34, 0.40), P(4, 0.20, 0.55) };
        var rec = BankVerifyAggregate.RecommendNoiseClip(pts, precisionFloor: 0.60);
        Assert.Multiple(() => {
            Assert.That(rec.Nc, Is.EqualTo(4.0));
            Assert.That(rec.ConsiderAdaptive, Is.True);
        });
    }
}
