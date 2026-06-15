#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class OptimizationObjectiveTests {

    private static ObjectiveConstants C => new ObjectiveConstants();

    // ---- SFocus ----

    [Test]
    public void SFocus_DecreasesMonotonicallyInSigma() {
        var c = C;
        // Smaller sigma (sharper focus relative to step) => higher score.
        var sLow = OptimizationObjective.SFocus(sigmaFocus: 0.10, looStdError: double.NaN, stepSize: 1.0, c);
        var sMid = OptimizationObjective.SFocus(sigmaFocus: 0.25, looStdError: double.NaN, stepSize: 1.0, c);
        var sHigh = OptimizationObjective.SFocus(sigmaFocus: 1.0, looStdError: double.NaN, stepSize: 1.0, c);
        Assert.Multiple(() => {
            Assert.That(sLow, Is.GreaterThan(sMid));
            Assert.That(sMid, Is.GreaterThan(sHigh));
            Assert.That(sLow, Is.LessThanOrEqualTo(1.0));
            Assert.That(sHigh, Is.GreaterThan(0.0));
        });
    }

    [Test]
    public void SFocus_AtRhoRef_IsOneHalf() {
        var c = C; // RhoRef = 0.25
        // rho = sigma/stepSize = RhoRef => 1/(1+1) = 0.5
        var s = OptimizationObjective.SFocus(sigmaFocus: 0.25, looStdError: double.NaN, stepSize: 1.0, c);
        Assert.That(s, Is.EqualTo(0.5).Within(1e-12));
    }

    [Test]
    public void SFocus_FallsBackToLooStdError_WhenSigmaNotFinite() {
        var c = C;
        var fromLoo = OptimizationObjective.SFocus(sigmaFocus: double.NaN, looStdError: 0.25, stepSize: 1.0, c);
        var direct = OptimizationObjective.SFocus(sigmaFocus: 0.25, looStdError: double.NaN, stepSize: 1.0, c);
        Assert.That(fromLoo, Is.EqualTo(direct).Within(1e-12));
    }

    [Test]
    public void SFocus_BothSigmaNonFinite_ReturnsNaN() {
        var c = C;
        var s = OptimizationObjective.SFocus(sigmaFocus: double.NaN, looStdError: double.NaN, stepSize: 1.0, c);
        Assert.That(double.IsNaN(s), Is.True);
    }

    // ---- SStars ----

    [Test]
    public void SStars_IncreasesWithCounts() {
        var c = C;
        var low = OptimizationObjective.SStars(new[] { 2, 3, 4 }, c);
        var high = OptimizationObjective.SStars(new[] { 30, 40, 50 }, c);
        Assert.That(high, Is.GreaterThan(low));
    }

    [Test]
    public void SStars_NMinDominates_PerWeights() {
        var c = C; // 0.6 * min-term + 0.4 * median-term, NFloor=8, NTarget=20
        // Case A: a single low frame drags nMin down even with high median.
        var aMin = 0; // nMin -> 0
        var manyHigh = Enumerable.Repeat(40, 9).ToList();
        var withOneZero = new List<int> { aMin };
        withOneZero.AddRange(manyHigh);
        var scoreWithZero = OptimizationObjective.SStars(withOneZero, c);
        // All-high frames score much higher because the min term is satisfied.
        var allHigh = OptimizationObjective.SStars(manyHigh, c);
        Assert.That(allHigh, Is.GreaterThan(scoreWithZero));
        // The min term carries 0.6 weight: with nMin=0 the score can be at most 0.4 (the median term).
        Assert.That(scoreWithZero, Is.LessThanOrEqualTo(0.4 + 1e-12));
    }

    [Test]
    public void SStars_ClampsAtOne() {
        var c = C;
        var s = OptimizationObjective.SStars(new[] { 1000, 2000, 3000 }, c);
        Assert.That(s, Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void SStars_AtFloorAndTarget_GivesFullSubScores() {
        var c = C; // NFloor=8, NTarget=20
        // nMin = 8 -> min-term clamps to 1.0; median = 20 -> median-term clamps to 1.0 -> total 1.0
        var s = OptimizationObjective.SStars(new[] { 8, 20, 20 }, c);
        Assert.That(s, Is.EqualTo(1.0).Within(1e-12));
    }

    // ---- SFit ----

    [Test]
    public void SFit_PenaltyIsOne_BelowChiTau() {
        var c = C; // ChiTau = 2.0
        var s = OptimizationObjective.SFit(rSquared: 1.0, reducedChiSquared: 1.0, c);
        Assert.That(s, Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void SFit_PenaltyIsOne_WhenChiSquaredNaN() {
        var c = C;
        var s = OptimizationObjective.SFit(rSquared: 0.8, reducedChiSquared: double.NaN, c);
        Assert.That(s, Is.EqualTo(0.8).Within(1e-12));
    }

    [Test]
    public void SFit_PenaltyDecreasesAboveChiTau() {
        var c = C; // penalty = ChiTau / reducedChiSquared above the knee
        var atKnee = OptimizationObjective.SFit(rSquared: 1.0, reducedChiSquared: 2.0, c);
        var above = OptimizationObjective.SFit(rSquared: 1.0, reducedChiSquared: 4.0, c);
        var wayAbove = OptimizationObjective.SFit(rSquared: 1.0, reducedChiSquared: 8.0, c);
        Assert.Multiple(() => {
            Assert.That(atKnee, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(above, Is.LessThan(atKnee));
            Assert.That(wayAbove, Is.LessThan(above));
            // ChiTau/4 = 0.5, ChiTau/8 = 0.25
            Assert.That(above, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(wayAbove, Is.EqualTo(0.25).Within(1e-12));
        });
    }

    [Test]
    public void SFit_MultipliesClampedRSquared() {
        var c = C;
        // R^2 negative -> clamps to 0; high chi -> still 0.
        var sNeg = OptimizationObjective.SFit(rSquared: -3.0, reducedChiSquared: 1.0, c);
        Assert.That(sNeg, Is.EqualTo(0.0).Within(1e-12));
        // R^2 above 1 clamps to 1.
        var sBig = OptimizationObjective.SFit(rSquared: 1.5, reducedChiSquared: 1.0, c);
        Assert.That(sBig, Is.EqualTo(1.0).Within(1e-12));
    }

    // ---- JRun ----

    private static RunEvaluationMetrics GoodRun(double sigmaFocus = 0.05, int frameCount = 10, int starsPerFrame = 40) {
        return new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = 1.0,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(starsPerFrame, frameCount).ToList()
        };
    }

    [Test]
    public void JRun_HardFails_OnNaNFocus() {
        var c = C;
        var m = GoodRun();
        m.SigmaFocus = double.NaN;
        m.LooStdError = double.NaN;
        var j = OptimizationObjective.JRun(m, c);
        Assert.That(j, Is.EqualTo(0.0));
    }

    [Test]
    public void JRun_HardFails_WhenTooManyFramesBelowHardFloor() {
        var c = C; // NHard=3, MaxFramesBelowHardFloor=0
        var m = GoodRun(frameCount: 10, starsPerFrame: 40);
        // Inject one frame below the hard floor (2 < 3).
        var counts = m.FrameStarCounts.ToList();
        counts[0] = 2;
        m.FrameStarCounts = counts;
        var j = OptimizationObjective.JRun(m, c);
        Assert.That(j, Is.EqualTo(0.0));
    }

    [Test]
    public void JRun_DoesNotHardFail_AtHardFloorExactly() {
        var c = C; // NHard=3 => starCount < 3 fails; exactly 3 is OK
        var m = GoodRun(frameCount: 10, starsPerFrame: 40);
        var counts = m.FrameStarCounts.ToList();
        counts[0] = 3;
        m.FrameStarCounts = counts;
        var j = OptimizationObjective.JRun(m, c);
        Assert.That(j, Is.GreaterThan(0.0));
    }

    // A run with every sub-score saturated to exactly 1.0 (sharp focus, star-rich, perfect fit). Used to
    // isolate the weights-normalization invariant: if the weights sum to 1, J must equal 1.0 exactly.
    private static RunEvaluationMetrics PerfectRun() => new RunEvaluationMetrics {
        SigmaFocus = 1e-9,   // SFocus -> 1
        LooStdError = double.NaN,
        StepSize = 1.0,
        RSquared = 1.0,      // SFit -> 1 (chi at knee)
        ReducedChiSquared = 1.0,
        FrameStarCounts = Enumerable.Repeat(100, 10).ToList() // SStars -> 1
    };

    [Test]
    public void JRun_WithoutLabels_UsesThreeWeightsSummingToOne() {
        var c = C;
        // With a perfect run (all sub-scores = 1) J must equal 1 exactly => weights normalized to sum 1.
        var j = OptimizationObjective.JRun(PerfectRun(), c);
        Assert.That(j, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void JRun_WithLabels_RenormalizesFourWeightsToSumToOne() {
        var c = C;
        // Perfect run including a perfect label score => J = 1 exactly.
        var j = OptimizationObjective.JRun(PerfectRun(), c, recall: 1.0, precision: 1.0);
        Assert.That(j, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void JRun_AddingHighLabelScore_RaisesJ_WhenLabelExceedsDisplacedAverage() {
        var c = C;
        // Construct a run whose non-label sub-scores are middling, then add a perfect label term.
        // sigma chosen so SFocus < 1; counts moderate so SStars < 1; chi raised so SFit < 1.
        var m = new RunEvaluationMetrics {
            SigmaFocus = 0.25, // SFocus = 0.5
            LooStdError = double.NaN,
            StepSize = 1.0,
            RSquared = 0.8,
            ReducedChiSquared = 4.0, // penalty 0.5 => SFit = 0.4
            FrameStarCounts = Enumerable.Repeat(8, 10).ToList() // SStars: min-term 1.0, median-term 0.4*... => < 1
        };
        var withoutLabels = OptimizationObjective.JRun(m, c);
        var withPerfectLabel = OptimizationObjective.JRun(m, c, recall: 1.0, precision: 1.0);
        Assert.That(withPerfectLabel, Is.GreaterThan(withoutLabels));
    }

    [Test]
    public void JRun_IsClampedToUnitInterval() {
        var c = C;
        var m = GoodRun(sigmaFocus: 1e-12, frameCount: 20, starsPerFrame: 10000);
        var j = OptimizationObjective.JRun(m, c, recall: 1.0, precision: 1.0);
        Assert.That(j, Is.LessThanOrEqualTo(1.0));
        Assert.That(j, Is.GreaterThanOrEqualTo(0.0));
    }

    // ---- JTotal ----

    [Test]
    public void JTotal_SingleRun_IsIdentity() {
        var c = C;
        var j = OptimizationObjective.JTotal(new[] { 0.42 }, c);
        Assert.That(j, Is.EqualTo(0.42).Within(1e-12));
    }

    [Test]
    public void JTotal_TwoRuns_BlendsMeanAndMin_WithBetaOneHalf() {
        var c = new ObjectiveConstants { Beta = 0.5 };
        var runs = new[] { 0.8, 0.4 };
        var mean = 0.6;
        var min = 0.4;
        var expected = 0.5 * mean + 0.5 * min; // 0.5
        var j = OptimizationObjective.JTotal(runs, c);
        Assert.That(j, Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void JTotal_BetaOne_IsMin() {
        var c = new ObjectiveConstants { Beta = 1.0 };
        var j = OptimizationObjective.JTotal(new[] { 0.9, 0.3, 0.7 }, c);
        Assert.That(j, Is.EqualTo(0.3).Within(1e-12));
    }

    [Test]
    public void JTotal_BetaZero_IsMean() {
        var c = new ObjectiveConstants { Beta = 0.0 };
        var j = OptimizationObjective.JTotal(new[] { 0.9, 0.3, 0.6 }, c);
        Assert.That(j, Is.EqualTo(0.6).Within(1e-12));
    }

    // ---- ComputeLabelScores (box-containment: point-in-box of accepted centers) ----

    // A small box CENTERED on (cx,cy) with the given half-extent, so the existing point-style test intents
    // (an accepted center "inside" / "outside" a label region) map cleanly onto boxes.
    private static LabelBox Box(double cx, double cy, double half = 5.0) =>
        new LabelBox(cx - half, cy - half, 2 * half, 2 * half);

    [Test]
    public void ComputeLabelScores_RecallOne_WhenAcceptedCenterInsideMissedBox() {
        var accepted = new[] { (X: 100.0, Y: 100.0) };
        var labeledMissed = new[] { Box(101.0, 101.0) }; // contains the accepted center
        var shouldReject = Array.Empty<LabelBox>();
        var (recall, precision) = OptimizationObjective.ComputeLabelScores(accepted, labeledMissed, shouldReject);
        Assert.Multiple(() => {
            Assert.That(recall, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(precision, Is.EqualTo(1.0).Within(1e-12)); // empty shouldReject => 1.0
        });
    }

    [Test]
    public void ComputeLabelScores_RecallZero_WhenNoAcceptedCenterInsideMissedBox() {
        var accepted = new[] { (X: 100.0, Y: 100.0) };
        var labeledMissed = new[] { Box(200.0, 200.0) }; // no accepted center inside
        var shouldReject = Array.Empty<LabelBox>();
        var (recall, _) = OptimizationObjective.ComputeLabelScores(accepted, labeledMissed, shouldReject);
        Assert.That(recall, Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void ComputeLabelScores_PrecisionZero_WhenAcceptedCenterInsideShouldRejectBox() {
        var accepted = new[] { (X: 50.0, Y: 50.0) };
        var labeledMissed = Array.Empty<LabelBox>();
        var shouldReject = new[] { Box(51.0, 50.0) }; // accepted center inside => NOT correctly excluded
        var (recall, precision) = OptimizationObjective.ComputeLabelScores(accepted, labeledMissed, shouldReject);
        Assert.Multiple(() => {
            Assert.That(recall, Is.EqualTo(1.0).Within(1e-12)); // empty missed => 1.0
            Assert.That(precision, Is.EqualTo(0.0).Within(1e-12));
        });
    }

    [Test]
    public void ComputeLabelScores_PrecisionOne_WhenNoAcceptedCenterInsideShouldRejectBox() {
        var accepted = new[] { (X: 50.0, Y: 50.0) };
        var labeledMissed = Array.Empty<LabelBox>();
        var shouldReject = new[] { Box(500.0, 500.0) }; // no accepted center inside => correctly excluded
        var (_, precision) = OptimizationObjective.ComputeLabelScores(accepted, labeledMissed, shouldReject);
        Assert.That(precision, Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void ComputeLabelScores_BothEmpty_GiveOnes() {
        var (recall, precision) = OptimizationObjective.ComputeLabelScores(
            Array.Empty<(double X, double Y)>(),
            Array.Empty<LabelBox>(),
            Array.Empty<LabelBox>());
        Assert.Multiple(() => {
            Assert.That(recall, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(precision, Is.EqualTo(1.0).Within(1e-12));
        });
    }

    [Test]
    public void ComputeLabelScores_FractionalRecallAndPrecision() {
        var accepted = new[] { (X: 0.0, Y: 0.0), (X: 100.0, Y: 0.0) };
        // 2 missed boxes: one contains an accepted center, one does not => recall 0.5
        var labeledMissed = new[] { Box(1.0, 0.0), Box(999.0, 999.0) };
        // 2 should-reject boxes: one contains an accepted center (bad), one does not (good) => precision 0.5
        var shouldReject = new[] { Box(100.5, 0.0), Box(-999.0, -999.0) };
        var (recall, precision) = OptimizationObjective.ComputeLabelScores(accepted, labeledMissed, shouldReject);
        Assert.Multiple(() => {
            Assert.That(recall, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(precision, Is.EqualTo(0.5).Within(1e-12));
        });
    }

    // ---- LabelScore ----

    [Test]
    public void LabelScore_IsHalfRecallPlusHalfPrecision() {
        var s = OptimizationObjective.LabelScore(recall: 0.6, precision: 0.4);
        Assert.That(s, Is.EqualTo(0.5).Within(1e-12));
    }

    // ---- Clamp01 ----

    [Test]
    public void Clamp01_ClampsToUnitInterval() {
        Assert.Multiple(() => {
            Assert.That(OptimizationObjective.Clamp01(-1.0), Is.EqualTo(0.0));
            Assert.That(OptimizationObjective.Clamp01(0.5), Is.EqualTo(0.5));
            Assert.That(OptimizationObjective.Clamp01(2.0), Is.EqualTo(1.0));
        });
    }
}
