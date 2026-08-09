#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

/// <summary>
/// The MAD floor's plumbing through <see cref="AlglibHyperbolicFitting.SelectBestModel"/> — the harness knob
/// F45(b) is measured with, and the pin that it reaches NOTHING the product runs.
///
/// <para><b>The product's inertness is pinned by a test, not by inspection.</b> An optional parameter that
/// "defaults to off" is a claim about every call site in the plugin, and the way that claim fails is silently:
/// the parameter acquires a non-empty default, or a convenience overload starts forwarding one. Both are caught
/// here, on data where a floor demonstrably changes the answer.</para>
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b> (<see cref="MadFloorSpec"/>
/// and the <c>madFloor</c> parameter do not exist). Each test names the narrower mutant it kills.</para>
/// </summary>
[TestFixture]
public class MadFloorFitPlumbingTests {
    private IAlglibAPI alglibAPI;

    [SetUp]
    public void SetUp() {
        alglibAPI = new AlglibAPI();
    }

    // One gross outlier on an otherwise exact symmetric curve, unanimously flagged by all four candidate models
    // (HybridModelSelectionTests pins that unanimity separately). sigma = 0.1 on every point, so the Grubbs
    // residuals are in units of 1/0.1 = 10 and the outlier's standardized residual is ~30.
    private static (List<ScatterErrorPoint> Points, double OutlierX, int StepSize) SingleGrossOutlier() {
        var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
            x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 910, xStep: 15, count: 13, errorY: 0.1);
        var outlierX = clean[9].X;
        var withOutlier = clean.Select(p => p.X == outlierX
            ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();
        return (withOutlier, outlierX, 15);
    }

    private static (List<ScatterErrorPoint> Points, int StepSize) TwoGrossOutliers() {
        var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
            x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 880, xStep: 12, count: 21, errorY: 0.1);
        var x1 = clean[6].X;
        var x2 = clean[14].X;
        var withTwo = clean.Select(p =>
            p.X == x1 || p.X == x2 ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();
        return (withTwo, 12);
    }

    [Test]
    public void TheProductionOverloadsCarryNoFloor() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: giving the new parameter a non-empty default (e.g.
        // `MadFloorSpec madFloor = ...Absolute(0.25)`), or having the no-rejection convenience overload forward
        // one. Either would ship F45(b) to users under cover of a "harness-only" parameter — the one outcome the
        // wave is explicitly NOT authorised to produce. A floor of 1000 in standardized-residual units is far
        // above anything this data can generate, so if any floor leaked in, the rejection below would vanish.
        var (points, outlierX, stepSize) = SingleGrossOutlier();

        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var defaulted);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var explicitlyNone, madFloor: MadFloorSpec.None);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var floored, madFloor: MadFloorSpec.Absolute(1000.0));

        Assert.Multiple(() => {
            Assert.That(defaulted.Count, Is.EqualTo(1), "the production call must still reject the gross outlier");
            Assert.That(defaulted[0].X, Is.EqualTo(outlierX));
            Assert.That(explicitlyNone.Count, Is.EqualTo(1), "MadFloorSpec.None must be the same thing as omitting it");
            Assert.That(explicitlyNone[0].X, Is.EqualTo(outlierX));
            Assert.That(floored, Is.Empty,
                "and a floor that IS passed must reach the rejection — otherwise this test would pass on a knob wired to nothing");
        });
    }

    [Test]
    public void TheNoRejectionOverload_IsUnaffected_BecauseTheGrubbsTestNeverRuns() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: the four-argument overload forwarding something other
        // than None. It cannot change behaviour (maxOutlierRejections is 0 there), which is exactly why a wrong
        // value would go unnoticed until some later wave raised that budget.
        var (points, _, stepSize) = SingleGrossOutlier();

        var legacy = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out var legacyFit);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 0, rejectionConfidence: 0.95,
            out var modernFit, out var rejects, madFloor: MadFloorSpec.Absolute(1000.0));

        Assert.Multiple(() => {
            Assert.That(rejects, Is.Empty, "budget 0 rejects nothing, floor or no floor");
            Assert.That(modernFit.Minimum.X, Is.EqualTo(legacyFit.Minimum.X).Within(1e-9),
                "and the fit is the same one the legacy overload produces");
            Assert.That(legacy, Is.Not.EqualTo(HyperbolicFitModel.Hybrid));
        });
    }

    [Test]
    public void FamilyB_LeavesRoundOneUntouchedThroughTheRealFitLoop() {
        // The round-1 invariance is asserted here through the PRODUCTION path — every candidate model's own
        // FitWithOutlierRejection loop, the consensus intersection, the lot — not just against RejectionTest.
        // alpha = 1e9 is deliberately absurd: if the anchor leaked into round 1 in any form, nothing anywhere
        // could survive it and the rejection would disappear.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: resolving family B's floor against the CURRENT round's
        // scale, or pre-seeding the anchor before the first RejectionTest call. Contrast with the family-A row,
        // which shows a floor of that magnitude really does silence round 1 when it is allowed to apply there.
        var (points, outlierX, stepSize) = SingleGrossOutlier();

        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var familyB, madFloor: MadFloorSpec.RelativeToFirstRound(1e9));
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var familyA, madFloor: MadFloorSpec.Absolute(1e9));

        Assert.Multiple(() => {
            Assert.That(familyB.Count, Is.EqualTo(1), "family B cannot reach round 1 at ANY alpha");
            Assert.That(familyB[0].X, Is.EqualTo(outlierX), "and it is the same point");
            Assert.That(familyA, Is.Empty, "family A at the same magnitude does silence round 1");
        });
    }

    [Test]
    public void FamilyB_StopsTheCascadeAfterRoundOne() {
        // Two injected outliers: at budget 2 the unfloored run removes both (round 1 and round 2). Under family B
        // at an absurd alpha, every model's round 2 is floored into silence, so no model can propose a second
        // point and the consensus intersection cannot contain one.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: threading the spec into SelectBestModel but never passing
        // it down to FitWithOutlierRejection — a knob wired to nothing, which every "no change at the default"
        // test in this file would happily pass.
        var (points, stepSize) = TwoGrossOutliers();

        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.95,
            out _, out var unfloored);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.95,
            out _, out var familyB, madFloor: MadFloorSpec.RelativeToFirstRound(1e9));

        Assert.Multiple(() => {
            Assert.That(unfloored.Count, Is.EqualTo(2), "the control removes both injected outliers");
            Assert.That(familyB.Count, Is.LessThan(2),
                "with round 2 floored into silence no model proposes a second point, so the consensus cannot hold one");
        });
    }

    [Test]
    public void FitWithOutlierRejection_RecordsRoundOnesScale_PerModel() {
        // The anchor is per MODEL, not per selection: each candidate runs its own rejection loop against its own
        // residuals, so a shared anchor would let a well-fitting model's scale float a badly-fitting model's
        // rounds. Driving the loop directly is the only way to see that.
        //
        // PRE-CHANGE: does not compile (FitWithOutlierRejection is private and has no madFloor parameter).
        // MUTANT KILLED: hoisting the anchor to SelectBestModel and sharing it across the Parallel.For — which
        // would additionally be a data race between the four candidate models.
        var (points, outlierX, stepSize) = SingleGrossOutlier();

        foreach (var model in new[] { HyperbolicFitModel.Symmetric, HyperbolicFitModel.TiltedHyperbola }) {
            var unfloored = AlglibHyperbolicFitting.FitWithOutlierRejection(
                alglibAPI, model, points, stepSize, useWeights: true, maxOutlierRejections: 1,
                rejectionConfidence: 0.95, out var rejectsUnfloored, out _);
            var familyB = AlglibHyperbolicFitting.FitWithOutlierRejection(
                alglibAPI, model, points, stepSize, useWeights: true, maxOutlierRejections: 1,
                rejectionConfidence: 0.95, out var rejectsFamilyB, out _, MadFloorSpec.RelativeToFirstRound(1e9));
            var familyA = AlglibHyperbolicFitting.FitWithOutlierRejection(
                alglibAPI, model, points, stepSize, useWeights: true, maxOutlierRejections: 1,
                rejectionConfidence: 0.95, out var rejectsFamilyA, out _, MadFloorSpec.Absolute(1000.0));

            Assert.Multiple(() => {
                Assert.That(unfloored, Is.Not.Null, $"{model} must solve on this data");
                Assert.That(rejectsUnfloored.Select(p => p.X), Is.EqualTo(new[] { outlierX }), $"{model} flags the gross outlier");
                Assert.That(rejectsFamilyB.Select(p => p.X), Is.EqualTo(new[] { outlierX }),
                    $"{model}: family B leaves its own round 1 alone");
                Assert.That(familyB, Is.Not.Null);
                Assert.That(rejectsFamilyA, Is.Empty, $"{model}: family A silences its round 1");
                Assert.That(familyA, Is.Not.Null);
            });
        }
    }
}
