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
/// The SEM criterion's plumbing through <see cref="AlglibHyperbolicFitting.SelectBestModel"/> — the harness knob
/// wave 16 item A is measured with, and the pin that it reaches NOTHING the product runs.
///
/// <para><b>The product's inertness is pinned by a test, not by inspection.</b> An optional parameter that
/// "defaults to off" is a claim about every call site in the plugin, and the way that claim fails is silently:
/// the parameter acquires a non-empty default, or a convenience overload starts forwarding one. Both are caught
/// here, on data where a live spec demonstrably changes the answer.</para>
///
/// <para><b>N* travels as a side map keyed by X</b>, never on <c>ScatterErrorPoint.Tag</c>, so
/// <c>WeightRegularization</c> — a production class on the production path — needs no edit. These tests build
/// the map the same way <c>af-fit</c> does.</para>
///
/// <para><b>Against the pre-change source every test in this file fails to compile</b> (<see cref="SemScaleSpec"/>
/// and the <c>semScale</c>/<c>starCounts</c> parameters do not exist). Each test names the narrower mutant it
/// kills.</para>
/// </summary>
[TestFixture]
public class SemScaleFitPlumbingTests {
    private IAlglibAPI alglibAPI;

    [SetUp]
    public void SetUp() {
        alglibAPI = new AlglibAPI();
    }

    // One gross outlier on an otherwise exact symmetric curve, unanimously flagged by all four candidate models.
    // sigma = 0.1 on every point, so the weighted residual of the outlier is ~30; with N* = 1 its s is ~30 too,
    // which puts it far above the small veto rung and far below the absurd one.
    private static (List<ScatterErrorPoint> Points, double OutlierX, int StepSize) SingleGrossOutlier() {
        var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
            x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 910, xStep: 15, count: 13, errorY: 0.1);
        var outlierX = clean[9].X;
        var withOutlier = clean.Select(p => p.X == outlierX
            ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();
        return (withOutlier, outlierX, 15);
    }

    // The af-fit side map: position -> N*. An X that is not in the map throws rather than defaulting to 1, for
    // the same reason the runner's does — a star count nobody measured must not quietly enter the statistic.
    private static Func<double, double> CountsFor(IEnumerable<ScatterErrorPoint> points, double n) {
        var map = new Dictionary<double, double>();
        foreach (var p in points) {
            map[p.X] = n;
        }
        return x => map.TryGetValue(x, out var v) ? v : throw new InvalidOperationException($"no star count for {x}");
    }

    [Test]
    public void TheProductionOverloadsCarryNoSemCriterion() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: giving the new parameter a non-empty default (e.g.
        // `SemScaleSpec semScale = ...Veto(1.0)`), or having the no-rejection convenience overload forward one.
        // Either would ship a criterion to users under cover of a "harness-only" parameter — the one outcome the
        // wave is explicitly NOT authorised to produce, since the product cannot even supply N* (design §2.3).
        var (points, outlierX, stepSize) = SingleGrossOutlier();
        var counts = CountsFor(points, 1.0);

        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var defaulted);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var explicitlyNone, semScale: SemScaleSpec.None, starCounts: null);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var vetoed, semScale: SemScaleSpec.Veto(1e6), starCounts: counts);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var vetoTooSmall, semScale: SemScaleSpec.Veto(1.0), starCounts: counts);

        Assert.Multiple(() => {
            Assert.That(defaulted.Count, Is.EqualTo(1), "the production call must still reject the gross outlier");
            Assert.That(defaulted[0].X, Is.EqualTo(outlierX));
            Assert.That(explicitlyNone.Count, Is.EqualTo(1), "SemScaleSpec.None must be the same thing as omitting it");
            Assert.That(explicitlyNone[0].X, Is.EqualTo(outlierX));
            Assert.That(vetoed, Is.Empty,
                "and a veto that IS passed must reach the rejection — otherwise this test would pass on a knob wired to nothing");
            Assert.That(vetoTooSmall.Count, Is.EqualTo(1),
                "a veto below the point's s changes nothing: the criterion is not an off-switch");
            Assert.That(vetoTooSmall[0].X, Is.EqualTo(outlierX));
        });
    }

    [Test]
    public void TheNoRejectionOverload_IsUnaffected_BecauseTheGrubbsTestNeverRuns() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: the four-argument overload forwarding something other than
        // None. It cannot change behaviour (maxOutlierRejections is 0 there), which is exactly why a wrong value
        // would go unnoticed until some later wave raised that budget — and it would then need a star map that
        // production has no way to supply.
        var (points, _, stepSize) = SingleGrossOutlier();

        var legacy = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out var legacyFit);
        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 0, rejectionConfidence: 0.95,
            out var modernFit, out var rejects, semScale: SemScaleSpec.Veto(1e6), starCounts: CountsFor(points, 1.0));

        Assert.Multiple(() => {
            Assert.That(rejects, Is.Empty, "budget 0 rejects nothing, criterion or no criterion");
            Assert.That(modernFit.Minimum.X, Is.EqualTo(legacyFit.Minimum.X).Within(1e-9),
                "and the fit is the same one the legacy overload produces");
            Assert.That(legacy, Is.Not.EqualTo(HyperbolicFitModel.Hybrid));
        });
    }

    [Test]
    public void ALiveSpecWithNoStarCounts_ThrowsThroughTheWholeFitPath() {
        // PRE-CHANGE: does not compile. MUTANT KILLED: relying on RejectionTest's guard alone. Pass 1 of
        // SelectBestModel runs each candidate inside `try { ... } catch (Exception ex) { Logger.Trace(...) }`, so
        // a throw raised down there is SWALLOWED into "no model solved" — four empty budget rows that a scorer
        // reads as "the criterion fired on nothing". This is exactly the shape F66 named, so the guard is
        // asserted at every level a caller can enter by.
        var (points, _, stepSize) = SingleGrossOutlier();

        Assert.Multiple(() => {
            Assert.That(() => AlglibHyperbolicFitting.SelectBestModel(
                    alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                    out _, out _, semScale: SemScaleSpec.Veto(1.0), starCounts: null),
                Throws.TypeOf<InvalidOperationException>(), "family V with no counts must not degrade to 'nothing solved'");
            Assert.That(() => AlglibHyperbolicFitting.SelectBestModel(
                    alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                    out _, out _, semScale: SemScaleSpec.Rank(), starCounts: null),
                Throws.TypeOf<InvalidOperationException>(), "family R likewise");
            Assert.That(() => AlglibHyperbolicFitting.FitWithOutlierRejection(
                    alglibAPI, HyperbolicFitModel.Symmetric, points, stepSize, useWeights: true,
                    maxOutlierRejections: 1, rejectionConfidence: 0.95, out _, out _,
                    MadFloorSpec.None, SemScaleSpec.Veto(1.0), null),
                Throws.TypeOf<InvalidOperationException>(), "and so must the per-model loop, entered directly");
        });
    }

    [Test]
    public void FitWithOutlierRejection_PassesTheSpecDownToEveryRound() {
        // The per-MODEL loop is where the consensus sets are actually produced, so a spec that reached
        // SelectBestModel and stopped there would leave every measured set identical to the control while the
        // report named a SEM rung.
        //
        // PRE-CHANGE: does not compile (FitWithOutlierRejection is private and has no semScale parameter).
        // MUTANT KILLED: accepting semScale/starCounts in FitWithOutlierRejection and not forwarding them to
        // RejectionTest — a knob wired to nothing, which every "no change at the default" test above would
        // happily pass.
        var (points, outlierX, stepSize) = SingleGrossOutlier();
        var counts = CountsFor(points, 1.0);

        foreach (var model in new[] { HyperbolicFitModel.Symmetric, HyperbolicFitModel.TiltedHyperbola }) {
            var control = AlglibHyperbolicFitting.FitWithOutlierRejection(
                alglibAPI, model, points, stepSize, useWeights: true, maxOutlierRejections: 1,
                rejectionConfidence: 0.95, out var rejectsControl, out _);
            var vetoed = AlglibHyperbolicFitting.FitWithOutlierRejection(
                alglibAPI, model, points, stepSize, useWeights: true, maxOutlierRejections: 1,
                rejectionConfidence: 0.95, out var rejectsVetoed, out _,
                MadFloorSpec.None, SemScaleSpec.Veto(1e6), counts);

            Assert.Multiple(() => {
                Assert.That(control, Is.Not.Null, $"{model} must solve on this data");
                Assert.That(rejectsControl.Select(p => p.X), Is.EqualTo(new[] { outlierX }), $"{model} flags the gross outlier");
                Assert.That(rejectsVetoed, Is.Empty, $"{model}: the veto reaches this model's own round 1");
                Assert.That(vetoed, Is.Not.Null, $"{model}: and the fit still solves, on the un-pruned set");
            });
        }
    }

    [Test]
    public void FamilyR_WithAConstantStarCount_LeavesTheProductionConsensusExactlyWhereItWas() {
        // Through the real path — four candidate models, each running its own rejection loop, then the consensus
        // intersection. z is scale-invariant, so a constant N* is a theorem-level no-op and any deviation means
        // the rescale landed somewhere it does not belong.
        //
        // PRE-CHANGE: does not compile. MUTANT KILLED: rescaling the residuals but not the median (or the scale),
        // which survives every hand-computed unit test that uses a single family-R rung and only shows up when
        // the same data is run at two different constant counts.
        var (points, outlierX, stepSize) = SingleGrossOutlier();

        AlglibHyperbolicFitting.SelectBestModel(
            alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
            out _, out var control);

        foreach (var n in new[] { 1.0, 25.0, 400.0 }) {
            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out _, out var ranked, semScale: SemScaleSpec.Rank(), starCounts: CountsFor(points, n));

            Assert.Multiple(() => {
                Assert.That(control.Select(p => p.X), Is.EqualTo(new[] { outlierX }));
                Assert.That(ranked.Select(p => p.X), Is.EqualTo(new[] { outlierX }),
                    $"N* = {n} is constant across the round, so family R cannot move anything");
            });
        }
    }
}
