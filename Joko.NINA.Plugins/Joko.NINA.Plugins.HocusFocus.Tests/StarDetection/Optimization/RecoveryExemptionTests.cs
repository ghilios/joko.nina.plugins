#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Collections.Generic;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// <see cref="RunEvaluationData.RecoveryStepsToExcludeStarlessWings"/> — the widening that lets a wide sweep be
/// optimized instead of refused. A starless position contributes a NaN HFR that poisons the hyperbolic fit for the
/// whole run, so one empty wing frame makes σ(focus) NaN no matter how clean the interior curve is.
/// </summary>
[TestFixture]
public class RecoveryExemptionTests {

    /// <summary>One frame per position, ascending, at a fixed step — the shape a wizard sweep produces.</summary>
    private static (List<int> Positions, List<int> Counts) Sweep(int firstPosition, int step, params int[] starCounts) {
        var positions = Enumerable.Range(0, starCounts.Length).Select(i => firstPosition + (i * step)).ToList();
        return (positions, starCounts.ToList());
    }

    [Test]
    public void TheReportedSweep_NeedsTwoStepsPerSide() {
        // The measured 3800mm case: an 11-point sweep at step 1770. Its interior is a textbook V (HFR 15.2 / 9.9 /
        // 6.3 / 5.3 / 7.0 / 11.6 / 17.1, minimum dead centre) but the four outermost frames are empty, so the fit
        // returned NaN and the wizard refused the run outright. Two per side clears exactly those four.
        var (positions, counts) = Sweep(16365, 1770, 0, 0, 2, 4, 10, 14, 10, 3, 1, 0, 0);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(2));
    }

    [Test]
    public void NoStarlessPositions_NeedsNoWidening() {
        var (positions, counts) = Sweep(1000, 100, 3, 8, 20, 30, 20, 8, 3);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(0));
    }

    [Test]
    public void AsymmetricWings_SizeToTheLargerSide() {
        // perSide is symmetric (ComputeRecoveryPositions takes k from each end), so a 1-empty/3-empty sweep needs 3
        // — and that necessarily discards two populated positions on the short side. Sizing to the SMALLER side
        // would leave a starless position in the fit and change nothing.
        var (positions, counts) = Sweep(1000, 100, 0, 5, 12, 30, 20, 12, 8, 5, 0, 0, 0);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(3));
    }

    [Test]
    public void AnInteriorGap_IsNotAWing_AndIsReportedUnfixable() {
        // A starless position BETWEEN populated ones is a data gap (cloud, satellite, tracking glitch), not a
        // too-defocused wing. Exempting inward from the ends to reach it would silently discard good positions on
        // both sides, so this must refuse rather than paper over it.
        var (positions, counts) = Sweep(1000, 100, 4, 9, 0, 25, 9, 4);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(-1));
    }

    [Test]
    public void WidenedExactlyToTheCap_IsAllowed_WhenThreePositionsSurvive() {
        // The cap is (d - 3) / 2, so 9 positions permit exactly 3 per side, leaving precisely the three the fit
        // needs to determine a hyperbola. The boundary is INCLUSIVE: refusing here would discard a fittable run.
        var (positions, counts) = Sweep(1000, 100, 0, 0, 0, 12, 30, 12, 0, 0, 0);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(3));
    }

    [Test]
    public void WingsSoWideThatTooFewPositionsSurvive_IsUnfixable() {
        // 7 positions cap perSide at 2, but the wings need 3. Widening past the cap would leave fewer than 3
        // fittable positions, which buys nothing.
        var (positions, counts) = Sweep(1000, 100, 0, 0, 0, 30, 0, 0, 0);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(-1));
    }

    [Test]
    public void EveryPositionStarless_IsUnfixable() {
        var (positions, counts) = Sweep(1000, 100, 0, 0, 0, 0, 0);
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(-1));
    }

    [Test]
    public void APositionIsStarlessOnlyIfEveryFrameThereIsEmpty() {
        // Multiple frames can share a focuser position; the fit pools them, so one populated frame gives the
        // position a finite HFR. Treating the position as starless would discard real data.
        var positions = new List<int> { 1000, 1000, 1100, 1200, 1300, 1300 };
        var counts = new List<int> { 0, 6, 15, 15, 6, 0 };
        Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(positions, counts), Is.EqualTo(0));
    }

    [Test]
    public void MalformedInput_IsUnfixable_RatherThanThrowing() {
        Assert.Multiple(() => {
            Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(null, new List<int> { 1 }), Is.EqualTo(-1));
            Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(new List<int> { 1 }, null), Is.EqualTo(-1));
            Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(new List<int> { 1, 2 }, new List<int> { 1 }), Is.EqualTo(-1),
                "mismatched lengths");
            Assert.That(RunEvaluationData.RecoveryStepsToExcludeStarlessWings(new List<int>(), new List<int>()), Is.EqualTo(-1));
        });
    }
}
