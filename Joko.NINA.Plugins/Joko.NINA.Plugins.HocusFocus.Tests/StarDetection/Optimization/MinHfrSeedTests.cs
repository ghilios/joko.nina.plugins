#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Linq;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// F35 — the MinHFR seeding rule. The fitted vertex TRIGGERS the seed; it never SIZES it, because every HFR
/// statistic available before the search is biased upward (the wing fit over-predicts the vertex 2.3x, and the
/// surviving-star medians are left-censored at MinHFR itself). The size is a constant in binned detection
/// pixels, justified by sampling and measured against synthetic truth.
/// </summary>
[TestFixture]
public class MinHfrSeedTests {

    private const double DefaultGate = 1.2;   // HocusFocusStarDetection.BuildDefaultStarDetectorParams

    /// <summary>
    /// D01_ultrawide_40mm. The wing-only hyperbola fit reads 0.548 px against a truth vertex of 0.238 — it is
    /// 2.3x high and STILL sits below the 1.2 gate, which is exactly why the trigger works despite the bias.
    /// </summary>
    [Test]
    public void Resolve_SeedsBeneathTheGate_WhenTheFittedVertexIsBelowIt() {
        Assert.That(MinHfrSeed.Resolve(0.548, DefaultGate), Is.EqualTo(MinHfrSeed.SeedFloor));
    }

    /// <summary>
    /// D05_tec140_1000mm — the control that makes a global rule defensible. Truth vertex 1.804 px, well clear of
    /// the gate, recall flat at 0.985 across the whole MinHFR sweep. A rig that does not need the seed must not
    /// receive it.
    /// </summary>
    [Test]
    public void Resolve_LeavesAWellSampledRigAlone() {
        Assert.That(MinHfrSeed.Resolve(1.804, DefaultGate), Is.Null);
    }

    /// <summary>
    /// The gate is <c>star.HFR &lt;= p.MinHFR</c> (INCLUSIVE, StarDetector.cs:1873), so a vertex sitting exactly
    /// AT the gate is already being rejected wholesale. That is the collapse F20 describes, not a boundary case
    /// to leave alone.
    /// </summary>
    [Test]
    public void Resolve_SeedsWhenTheVertexSitsExactlyOnTheInclusiveGate() {
        Assert.That(MinHfrSeed.Resolve(DefaultGate, DefaultGate), Is.EqualTo(MinHfrSeed.SeedFloor));
    }

    /// <summary>
    /// A run with no determinable fit yields NaN (RunEvaluationResult.BestFit is null =&gt; Minimum.Y is NaN).
    /// Re-gating a detector off a statistic that does not exist is the circularity F35 was filed to remove.
    /// </summary>
    [Test]
    public void Resolve_DoesNothingWithoutAUsableFit([Values(double.NaN, double.PositiveInfinity, double.NegativeInfinity)] double vertex) {
        Assert.That(MinHfrSeed.Resolve(vertex, DefaultGate), Is.Null);
    }

    /// <summary>
    /// The rule only ever LOWERS the gate. A user (or a prior landing) who already set MinHFR beneath the floor
    /// must not have it raised — the objective rewards star count, so a raised gate is a knob the search has no
    /// gradient to climb back down.
    /// </summary>
    [Test]
    public void Resolve_NeverRaisesAGateThatIsAlreadyLower() {
        Assert.That(MinHfrSeed.Resolve(0.05, 0.2), Is.Null);
    }

    /// <summary>Equal to the floor is not lower than the floor — no write, so the landing stays comparable.</summary>
    [Test]
    public void Resolve_DoesNothingWhenTheGateAlreadyEqualsTheFloor() {
        Assert.That(MinHfrSeed.Resolve(0.1, MinHfrSeed.SeedFloor), Is.Null);
    }

    /// <summary>
    /// The seed must survive <see cref="OptimizerVariable"/>'s bounds. A value below the axis Lower is silently
    /// raised at theta0 (StarDetectionOptimizer.cs:115), which would apply the fix and change nothing — the exact
    /// failure mode F35 warns about for the "0.8 x predicted" rule.
    /// </summary>
    [Test]
    public void SeedFloor_IsReachableOnTheMinHfrAxis() {
        var axis = OptimizerVariable.CreateCuratedSet().Single(v => v.Name == nameof(StarDetectorParams.MinHFR));
        Assert.Multiple(() => {
            Assert.That(MinHfrSeed.SeedFloor, Is.GreaterThanOrEqualTo(axis.Lower));
            Assert.That(axis.Quantize(MinHfrSeed.SeedFloor), Is.EqualTo(MinHfrSeed.SeedFloor));
        });
    }

    /// <summary>
    /// The measured safe band is 0.25-0.35 px: FP = 0 in all 32 configurations of the D01/D02/D03/D05 sweep, and
    /// the recall gain saturates by 0.5. Pinned so a later retune has to argue with the measurement.
    /// </summary>
    [Test]
    public void SeedFloor_SitsInTheMeasuredSafeBand() {
        Assert.That(MinHfrSeed.SeedFloor, Is.InRange(0.25, 0.35));
    }
}
