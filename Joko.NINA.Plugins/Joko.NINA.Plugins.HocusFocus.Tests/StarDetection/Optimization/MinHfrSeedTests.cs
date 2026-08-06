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

    /// <summary>StarDetectorParams.DetectionBinning's default -- captured and binned pixels coincide.</summary>
    private const int DetectionBinningOff = 1;

    /// <summary>
    /// D01_ultrawide_40mm. The wing-only hyperbola fit reads 0.548 px against a truth vertex of 0.238 — it is
    /// 2.3x high and STILL sits below the 1.2 gate, which is exactly why the trigger works despite the bias.
    /// </summary>
    [Test]
    public void Resolve_SeedsBeneathTheGate_WhenTheFittedVertexIsBelowIt() {
        Assert.That(MinHfrSeed.Resolve(0.548, DefaultGate, DetectionBinningOff), Is.EqualTo(MinHfrSeed.SeedFloor));
    }

    /// <summary>
    /// D05_tec140_1000mm — the control that makes a global rule defensible. Truth vertex 1.804 px, well clear of
    /// the gate, recall flat at 0.985 across the whole MinHFR sweep. A rig that does not need the seed must not
    /// receive it.
    /// </summary>
    [Test]
    public void Resolve_LeavesAWellSampledRigAlone() {
        Assert.That(MinHfrSeed.Resolve(1.804, DefaultGate, DetectionBinningOff), Is.Null);
    }

    /// <summary>
    /// The gate is <c>star.HFR &lt;= p.MinHFR</c> (INCLUSIVE, StarDetector.cs:1873), so a vertex sitting exactly
    /// AT the gate is already being rejected wholesale. That is the collapse F20 describes, not a boundary case
    /// to leave alone.
    /// </summary>
    [Test]
    public void Resolve_SeedsWhenTheVertexSitsExactlyOnTheInclusiveGate() {
        Assert.That(MinHfrSeed.Resolve(DefaultGate, DefaultGate, DetectionBinningOff), Is.EqualTo(MinHfrSeed.SeedFloor));
    }

    /// <summary>
    /// A run with no determinable fit yields NaN (RunEvaluationResult.BestFit is null =&gt; Minimum.Y is NaN).
    /// Re-gating a detector off a statistic that does not exist is the circularity F35 was filed to remove.
    /// </summary>
    [Test]
    public void Resolve_DoesNothingWithoutAUsableFit([Values(double.NaN, double.PositiveInfinity, double.NegativeInfinity)] double vertex) {
        Assert.That(MinHfrSeed.Resolve(vertex, DefaultGate, DetectionBinningOff), Is.Null);
    }

    /// <summary>
    /// The rule only ever LOWERS the gate. A user (or a prior landing) who already set MinHFR beneath the floor
    /// must not have it raised — the objective rewards star count, so a raised gate is a knob the search has no
    /// gradient to climb back down.
    /// </summary>
    [Test]
    public void Resolve_NeverRaisesAGateThatIsAlreadyLower() {
        Assert.That(MinHfrSeed.Resolve(0.05, 0.2, DetectionBinningOff), Is.Null);
    }

    /// <summary>Equal to the floor is not lower than the floor — no write, so the landing stays comparable.</summary>
    [Test]
    public void Resolve_DoesNothingWhenTheGateAlreadyEqualsTheFloor() {
        Assert.That(MinHfrSeed.Resolve(0.1, MinHfrSeed.SeedFloor, DetectionBinningOff), Is.Null);
    }

    /// <summary>
    /// F38 — the two operands live in different pixel spaces. The gate fires inside the binned raster
    /// (StarDetector.cs:1894) while <c>BestFit.Minimum.Y</c> has already been scaled back to source pixels
    /// (StarDetector.cs:805-808), so a captured vertex must be divided by the factor before it is compared.
    ///
    /// <para>The regression case: at binning 2 a captured vertex of 2.0 px IS 1.0 binned px, comfortably under
    /// the 1.2 gate — the rig's near-focus frames really are being emptied by <c>TooLowHFR</c>. Before the fix
    /// the rule compared 2.0 against 1.2, took the "clears the gate" branch, and declined to seed. Reverting the
    /// division makes this fail, which is the only reason it is worth having.</para>
    /// </summary>
    [Test]
    public void Resolve_SeedsABinnedRigWhoseVertexIsBelowTheGateInBinnedSpace() {
        Assert.That(MinHfrSeed.Resolve(2.0, DefaultGate, 2), Is.EqualTo(MinHfrSeed.SeedFloor));
    }

    /// <summary>
    /// The other side of F38: the conversion must not manufacture seeds either. At binning 2 a captured vertex of
    /// 3.0 px is 1.5 binned px, genuinely above the gate, so the D05 branch still has to hold.
    /// </summary>
    [Test]
    public void Resolve_LeavesABinnedRigAloneWhenItClearsTheGateInBinnedSpace() {
        Assert.That(MinHfrSeed.Resolve(3.0, DefaultGate, 2), Is.Null);
    }

    /// <summary>
    /// The silent-failure window itself, swept across the factor. A captured vertex of 2.2 px is ABOVE the 1.2
    /// gate unbinned — so at 1x1 the rule correctly declines — but at 2x2 it is 1.1 binned px and the near-focus
    /// frames really are being emptied. Every factor >= 2 must therefore seed.
    ///
    /// <para>This is the discriminating case, and deliberately so: the first version of this fixture's binning
    /// coverage was five tests of which only ONE failed when the division was reverted. The other four asserted
    /// behaviour that is identical with and without the fix, which is worth nothing as a regression test.</para>
    /// </summary>
    [Test]
    public void Resolve_SeedsAcrossTheWholeSilentWindow([Values(2, 3, 4)] int binning) {
        Assert.That(MinHfrSeed.Resolve(2.2, DefaultGate, binning), Is.EqualTo(MinHfrSeed.SeedFloor));
    }

    /// <summary>The same vertex at 1x1 is genuinely above the gate and must still be left alone.</summary>
    [Test]
    public void Resolve_LeavesTheSameVertexAloneWhenUnbinned() {
        Assert.That(MinHfrSeed.Resolve(2.2, DefaultGate, DetectionBinningOff), Is.Null);
    }

    /// <summary>
    /// GUARD, not a regression test — it passes with or without the F38 division. A caller that has not populated
    /// DetectionBinning (0, or a nonsense negative) must behave as unbinned rather than divide by zero and seed
    /// every rig it sees.
    /// </summary>
    [Test]
    public void Resolve_TreatsAnUnsetBinningFactorAsUnbinned([Values(0, -1)] int binning) {
        Assert.That(MinHfrSeed.Resolve(1.804, DefaultGate, binning), Is.Null);
    }

    /// <summary>
    /// The seeded VALUE is unaffected by binning — <see cref="MinHfrSeed.SeedFloor"/> is already in binned pixels
    /// and is written to MinHFR, which is also binned. Only the trigger ever needed converting.
    /// </summary>
    [Test]
    public void Resolve_SeedsTheSameFloorRegardlessOfBinning([Values(1, 2, 4)] int binning) {
        Assert.That(MinHfrSeed.Resolve(0.1, DefaultGate, binning), Is.EqualTo(MinHfrSeed.SeedFloor));
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

    // ---- The bank population this fix has never been able to exercise (F39(b), wave 7) --------------------
    //
    // F38's silent window is a CAPTURED vertex in (gate, N x gate]: the gate fires inside the binned raster while
    // every reported HFR has been scaled back to source pixels, so at factor N a run whose captured vertex sits
    // anywhere up to N x the gate is really being emptied by TooLowHFR while Resolve, comparing the two spaces
    // directly, sees "the rig's stars clear the gate" and stays silent.
    //
    // Until wave 7 no harness run had DetectionBinning > 1 -- optimize discarded the resolved value (F39) -- so
    // this window had no test that used the bank's own numbers. `optimize --apply-run-detection-binning` now runs
    // the seven detectionBinning = 2 datasets at their factor, and these pin the boundary with those datasets'
    // real in-focus HFRs.

    /// <summary>
    /// The whole silent window at factor 2, walked with the shipped gate. A captured vertex of 1.3-2.4 px is
    /// BELOW the effective gate (binned 0.65-1.20 px vs a gate of 1.2) and must seed; the same vertex read
    /// naively against 1.2 would not. 2.5 px is genuinely above it and must not.
    /// </summary>
    [TestCase(1.3, true)]
    [TestCase(2.0, true)]
    [TestCase(2.4, true)]   // the exact top of the window: 2.4 / 2 = 1.2 == the gate, and the compare is <=
    [TestCase(2.5, false)]  // just past it: 1.25 > 1.2
    public void Resolve_AtBinning2_CoversF38sSilentWindowEndToEnd(double capturedVertexHfr, bool shouldSeed) {
        var seeded = MinHfrSeed.Resolve(capturedVertexHfr, DefaultGate, detectionBinning: 2);
        Assert.Multiple(() => {
            Assert.That(seeded.HasValue, Is.EqualTo(shouldSeed),
                $"captured {capturedVertexHfr} px is {(shouldSeed ? "inside" : "outside")} the (1.2, 2.4] window at factor 2");
            // ...and the SAME vertex at factor 1 must answer differently inside the window. That contrast is the
            // defect: one rig, one measurement, two verdicts, decided by a factor Resolve used to never be told.
            if (shouldSeed) {
                Assert.That(MinHfrSeed.Resolve(capturedVertexHfr, DefaultGate, DetectionBinningOff), Is.Null,
                    "at factor 1 the same captured vertex clears the gate — which is why the factor has to be passed");
            }
        });
    }

    /// <summary>
    /// The bank's own seven, at the factor wave 7 finally ran them at. Each dataset's measured in-focus HFR is in
    /// CAPTURED px (the detector rescales before reporting), so the expectation is whether HFR/2 sits at or below
    /// the shipped 1.2 px gate. None of these is near the window at the shipped gate — which is the point: this
    /// pins that honouring the factor did NOT quietly start seeding the whole binning-2 population, and a future
    /// change that made it do so would have to argue with a real number.
    /// </summary>
    [TestCase(4.86)]   // D08_c11_2800mm
    [TestCase(5.30)]   // D09_c14_3800mm / D14 / D17 class
    [TestCase(5.07)]   // D12_c14_585_afbin2
    [TestCase(5.90)]   // D15_cdk20_3454mm_e47
    public void Resolve_AtBinning2_DoesNotSeedTheBanksBinning2Datasets(double capturedInFocusHfr) {
        Assert.That(MinHfrSeed.Resolve(capturedInFocusHfr, DefaultGate, detectionBinning: 2), Is.Null,
            "these rigs' stars clear the gate even in binned space (HFR/2 >= 2.4 px), so the seed must stay silent");
    }
}
