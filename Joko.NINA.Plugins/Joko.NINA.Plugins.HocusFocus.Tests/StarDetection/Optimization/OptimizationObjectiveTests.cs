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
        var c = new ObjectiveConstants { Wtie = 0.0 }; // isolate the weight-normalization invariant from the tie-breaker
        // With a perfect run (all sub-scores = 1) J must equal 1 exactly => weights normalized to sum 1.
        var j = OptimizationObjective.JRun(PerfectRun(), c);
        Assert.That(j, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void JRun_WithLabels_RenormalizesFourWeightsToSumToOne() {
        var c = new ObjectiveConstants { Wtie = 0.0 }; // isolate the weight-normalization invariant from the tie-breaker
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

    // ---- SDefocusPrecision (F3 label-free precision penalty) ----

    // GoodRun augmented with the per-frame plumbing the near-focus signal needs: a focuser-position axis centered
    // on bestFocus, and a parallel relaxed-count list. All zeros relaxed by default (the gate-OFF baseline).
    private static RunEvaluationMetrics RunWithPositions(
            int frameCount = 9, int starsPerFrame = 40, int stepSize = 100, int bestFocus = 5000,
            int[] relaxed = null, double sigmaFocus = 0.05) {
        var positions = new int[frameCount];
        var counts = new int[frameCount];
        // Symmetric sweep around bestFocus in steps of stepSize: ..., -1, 0, +1, ... × stepSize.
        var half = frameCount / 2;
        for (var i = 0; i < frameCount; i++) {
            positions[i] = bestFocus + (i - half) * stepSize;
            counts[i] = starsPerFrame;
        }
        return new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = counts,
            FrameFocuserPositions = positions,
            FrameRelaxationAdmittedCounts = relaxed ?? new int[frameCount],
            BestFocusPosition = bestFocus
        };
    }

    [Test]
    public void SDefocusPrecision_NoData_ReturnsExactlyOne() {
        var c = C;
        // null counts (caller didn't populate the F3 plumbing) => no penalty.
        var m = GoodRun(); // FrameRelaxationAdmittedCounts == null
        Assert.That(OptimizationObjective.SDefocusPrecision(m, c), Is.EqualTo(1.0));
    }

    [Test]
    public void SDefocusPrecision_AllZeroRelaxed_ReturnsExactlyOne() {
        var c = C;
        var m = RunWithPositions(); // all-zero relaxed (gate-OFF baseline)
        Assert.That(OptimizationObjective.SDefocusPrecision(m, c), Is.EqualTo(1.0));
    }

    [Test]
    public void SDefocusPrecision_RelaxedOnlyOnDefocusedExtremes_NotPenalized() {
        var c = C; // NearFocusWindowSteps = 1.5 => window = 150 around bestFocus 5000
        // 9 frames at 4600..5400 (step 100). Put all relaxation on the FAR extremes (legitimate donut recovery):
        // indices 0 (4600) and 8 (5400) are well outside ±150 of the minimum.
        var relaxed = new[] { 30, 0, 0, 0, 0, 0, 0, 0, 30 };
        var m = RunWithPositions(relaxed: relaxed);
        Assert.That(OptimizationObjective.SDefocusPrecision(m, c), Is.EqualTo(1.0),
            "relaxation confined to the defocused extremes is legitimate donut recovery, never penalized");
    }

    [Test]
    public void SDefocusPrecision_NearFocusJunk_DropsBelowOne() {
        var c = C;
        // Relaxation concentrated on the NEAR-FOCUS frames (within ±150 of bestFocus): indices 3,4,5 → 4900,5000,5100.
        // A near-focus relaxed star is a large/low-fill junk blob by the gate's size-scaling. nearAccepted=120,
        // nearRelaxed=60 => frac 0.5; excess over 0.20 threshold = 0.30; penalty = 1 - 0.5*0.30 = 0.85.
        var relaxed = new[] { 0, 0, 0, 20, 20, 20, 0, 0, 0 };
        var m = RunWithPositions(relaxed: relaxed);
        var penalty = OptimizationObjective.SDefocusPrecision(m, c);
        Assert.Multiple(() => {
            Assert.That(penalty, Is.LessThan(1.0));
            Assert.That(penalty, Is.EqualTo(0.85).Within(1e-12));
        });
    }

    [Test]
    public void SDefocusPrecision_NearFocusBelowThreshold_NoPenalty() {
        var c = C; // threshold 0.20
        // Small near-focus relaxed fraction (under the threshold): nearAccepted=120, nearRelaxed=12 => frac 0.10.
        var relaxed = new[] { 0, 0, 0, 4, 4, 4, 0, 0, 0 };
        var m = RunWithPositions(relaxed: relaxed);
        Assert.That(OptimizationObjective.SDefocusPrecision(m, c), Is.EqualTo(1.0),
            "below the precision threshold => no penalty (a couple of borderline admissions are tolerated)");
    }

    [Test]
    public void SDefocusPrecision_PenaltyFlooredAtMinFactor() {
        var c = C; // strength 0.5, floor 0.5
        // Drive the near-focus relaxed fraction to 1.0 (every near-focus accepted star relaxed): penalty would be
        // 1 - 0.5*(1.0-0.20) = 0.60, still above the floor; push strength up to confirm the floor clamps.
        var cHard = new ObjectiveConstants { DefocusPrecisionStrength = 5.0 };
        var relaxed = new[] { 0, 0, 0, 40, 40, 40, 0, 0, 0 }; // nearRelaxed=120 == nearAccepted=120 => frac 1.0
        var m = RunWithPositions(relaxed: relaxed);
        var penalty = OptimizationObjective.SDefocusPrecision(m, cHard);
        Assert.That(penalty, Is.EqualTo(cHard.DefocusPrecisionMinFactor).Within(1e-12),
            "the precision term can never drive J below its floor");
    }

    [Test]
    public void SDefocusPrecision_Fallback_RunLevelFraction_WhenNoPositions() {
        var c = C;
        // No focuser positions / no fitted minimum => fall back to the run-level relaxed fraction.
        // total accepted = 9*40 = 360, total relaxed = 180 => frac 0.5 => penalty 1 - 0.5*(0.5-0.2) = 0.85.
        var m = GoodRun(frameCount: 9, starsPerFrame: 40);
        m.FrameRelaxationAdmittedCounts = Enumerable.Repeat(20, 9).ToList();
        // FrameFocuserPositions stays null and BestFocusPosition stays NaN => fallback path.
        var penalty = OptimizationObjective.SDefocusPrecision(m, c);
        Assert.That(penalty, Is.EqualTo(0.85).Within(1e-12));
    }

    [Test]
    public void SDefocusPrecision_Fallback_TooFewFrames_NoPenalty() {
        var c = C; // MinFramesForPenalty = 3
        var m = GoodRun(frameCount: 2, starsPerFrame: 40);
        m.FrameStarCounts = new[] { 40, 40 };
        m.FrameRelaxationAdmittedCounts = new[] { 40, 40 }; // would be frac 1.0, but too few frames
        Assert.That(OptimizationObjective.SDefocusPrecision(m, c), Is.EqualTo(1.0));
    }

    // ---- JRun bit-identity: multiplying by SDefocusPrecision must not change J when relaxed counts are 0 ----

    // The pre-F3 weighted-sum formula, recomputed independently here, so the test pins JRun to the exact value it
    // produced BEFORE the multiplicative penalty existed (×1.0 leaves it unchanged).
    private static double LegacyJRun(RunEvaluationMetrics m, ObjectiveConstants c, double? recall, double? precision) {
        var effRecall = recall ?? m.Recall;
        var effPrecision = precision ?? m.Precision;
        if (m.FrameStarCounts != null && m.FrameStarCounts.Count(n => n < c.NHard) > c.MaxFramesBelowHardFloor) {
            return 0.0;
        }
        var sFocus = OptimizationObjective.SFocus(m.SigmaFocus, m.LooStdError, m.StepSize, c);
        if (double.IsNaN(sFocus)) {
            return 0.0;
        }
        var sStars = OptimizationObjective.SStars(m.FrameStarCounts, c);
        var sFit = OptimizationObjective.SFit(m.RSquared, m.ReducedChiSquared, c);
        double j;
        if (effRecall.HasValue && effPrecision.HasValue) {
            var sLabel = OptimizationObjective.LabelScore(effRecall.Value, effPrecision.Value);
            var wSum = c.Wf + c.Ws + c.Wc + c.Wl;
            j = (c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit + c.Wl * sLabel) / wSum;
        } else {
            var wSum = c.Wf + c.Ws + c.Wc;
            j = (c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit) / wSum;
        }
        return OptimizationObjective.Clamp01(j);
    }

    [Test]
    public void JRun_BitIdentical_Unlabeled_WhenRelaxedCountsAllZero() {
        var c = new ObjectiveConstants { Wtie = 0.0 }; // bit-identity is the F3-penalty guarantee; isolate from the tie-breaker
        var m = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18); // all-zero relaxed
        var actual = OptimizationObjective.JRun(m, c);
        var expected = LegacyJRun(m, c, null, null);
        Assert.That(actual, Is.EqualTo(expected),
            "with all relaxed counts 0, SDefocusPrecision == 1.0 so J must be byte-identical to the pre-F3 formula");
    }

    [Test]
    public void JRun_BitIdentical_Labeled_WhenRelaxedCountsAllZero() {
        var c = new ObjectiveConstants { Wtie = 0.0 }; // bit-identity is the F3-penalty guarantee; isolate from the tie-breaker
        var m = RunWithPositions(frameCount: 9, starsPerFrame: 12, sigmaFocus: 0.22); // all-zero relaxed
        var actual = OptimizationObjective.JRun(m, c, recall: 0.7, precision: 0.6);
        var expected = LegacyJRun(m, c, 0.7, 0.6);
        Assert.That(actual, Is.EqualTo(expected),
            "labeled case is also byte-identical when there is no relaxation");
    }

    [Test]
    public void JRun_BitIdentical_WhenNoRelaxationPlumbingAtAll() {
        var c = new ObjectiveConstants { Wtie = 0.0 }; // bit-identity is the F3-penalty guarantee; isolate from the tie-breaker
        // GoodRun has null FrameRelaxationAdmittedCounts/positions — the legacy callers' shape. Must be unchanged.
        var m = GoodRun(sigmaFocus: 0.2, frameCount: 10, starsPerFrame: 25);
        Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(LegacyJRun(m, c, null, null)));
        Assert.That(OptimizationObjective.JRun(m, c, recall: 0.9, precision: 0.8),
            Is.EqualTo(LegacyJRun(m, c, 0.9, 0.8)));
    }

    [Test]
    public void JRun_NearFocusJunk_LowersJ_BelowTheUnpenalizedValue() {
        var c = C;
        var clean = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18); // all-zero relaxed
        var junk = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18,
            relaxed: new[] { 0, 0, 0, 20, 20, 20, 0, 0, 0 }); // near-focus junk
        var jClean = OptimizationObjective.JRun(clean, c);
        var jJunk = OptimizationObjective.JRun(junk, c);
        Assert.That(jJunk, Is.LessThan(jClean),
            "near-focus relaxation junk multiplicatively lowers J");
    }

    [Test]
    public void JRun_DefocusedExtremeRelaxation_DoesNotLowerJ() {
        var c = C;
        var clean = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18); // all-zero relaxed
        var donut = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18,
            relaxed: new[] { 30, 0, 0, 0, 0, 0, 0, 0, 30 }); // legitimate donut recovery on the extremes
        var jClean = OptimizationObjective.JRun(clean, c);
        var jDonut = OptimizationObjective.JRun(donut, c);
        Assert.That(jDonut, Is.EqualTo(jClean),
            "legitimate donut recovery on the defocused extremes must not be penalized");
    }

    // ---- TieBreakerScore (plateau tie-breaker) + JRun blend ----

    // A run whose PRIMARY sub-scores all saturate to 1.0 (sharp focus, perfect fit, counts past both knees),
    // so J_primary == 1.0 regardless of star count — the saturated-plateau case the tie-breaker targets.
    private static RunEvaluationMetrics PlateauRun(int starsPerFrame, double sigmaFocus = 1e-6) => new RunEvaluationMetrics {
        SigmaFocus = sigmaFocus,
        LooStdError = double.NaN,
        StepSize = 100.0,
        RSquared = 1.0,
        ReducedChiSquared = 1.0,
        FrameStarCounts = Enumerable.Repeat(starsPerFrame, 9).ToList()
    };

    [Test]
    public void TieBreakerScore_IncreasesWithStarCount() {
        var c = C;
        var fewer = OptimizationObjective.TieBreakerScore(PlateauRun(100), c);
        var more = OptimizationObjective.TieBreakerScore(PlateauRun(300), c);
        Assert.That(more, Is.GreaterThan(fewer));
    }

    [Test]
    public void TieBreakerScore_DecreasesWithSigma() {
        var c = C;
        var sharp = OptimizationObjective.TieBreakerScore(PlateauRun(100, sigmaFocus: 0.1), c);
        var soft = OptimizationObjective.TieBreakerScore(PlateauRun(100, sigmaFocus: 100.0), c);
        Assert.That(sharp, Is.GreaterThan(soft));
    }

    [Test]
    public void TieBreakerScore_NeverSaturates_StillRewardsBeyondKnees() {
        var c = C; // NFloor=8, NTarget=20 — primary S_stars CLAMPS here; the tie-breaker must NOT.
        var atKnees = OptimizationObjective.TieBreakerScore(PlateauRun(20), c);
        var farAbove = OptimizationObjective.TieBreakerScore(PlateauRun(2000), c);
        Assert.Multiple(() => {
            Assert.That(farAbove, Is.GreaterThan(atKnees), "more stars beyond the S_stars knee still scores higher");
            Assert.That(farAbove, Is.LessThan(1.0), "asymptotic: never reaches 1.0");
        });
    }

    [Test]
    public void TieBreakerScore_RewardsTotalRichness_NotJustTheWorstFrame() {
        var c = C;
        // Regression for the observed gaming: on the mufti plateau the optimizer raised ONLY the worst frame
        // (82->91) while dropping ~27% of the total stars (1247->904). The tie-breaker must reward overall
        // richness, so the star-richer set must win even though its MINIMUM frame is lower.
        var richOverall = new RunEvaluationMetrics { // total 1247, min 82
            SigmaFocus = 1e-6, LooStdError = double.NaN, StepSize = 100.0, RSquared = 1.0, ReducedChiSquared = 1.0,
            FrameStarCounts = new[] { 97, 140, 226, 334, 219, 149, 82 }
        };
        var leanButFlatter = new RunEvaluationMetrics { // total 904, min 91
            SigmaFocus = 1e-6, LooStdError = double.NaN, StepSize = 100.0, RSquared = 1.0, ReducedChiSquared = 1.0,
            FrameStarCounts = new[] { 103, 141, 125, 150, 144, 150, 91 }
        };
        Assert.That(OptimizationObjective.TieBreakerScore(richOverall, c),
            Is.GreaterThan(OptimizationObjective.TieBreakerScore(leanButFlatter, c)),
            "tie-breaker must reward total star richness, not be gamed by raising only the worst frame");
    }

    [Test]
    public void JRun_OnSaturatedPlateau_PrefersMoreStars() {
        var c = C; // default Wtie > 0
        var c0 = new ObjectiveConstants { Wtie = 0.0 };
        Assert.Multiple(() => {
            // Both runs saturate the PRIMARY objective (J_primary == 1.0) ...
            Assert.That(OptimizationObjective.JRun(PlateauRun(300), c0), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(OptimizationObjective.JRun(PlateauRun(120), c0), Is.EqualTo(1.0).Within(1e-9));
            // ... yet WITH the tie-breaker the star-richer run is preferred.
            Assert.That(OptimizationObjective.JRun(PlateauRun(300), c),
                Is.GreaterThan(OptimizationObjective.JRun(PlateauRun(120), c)));
        });
    }

    [Test]
    public void JRun_TieBreaker_DisabledWhenWtieZero() {
        var c0 = new ObjectiveConstants { Wtie = 0.0 };
        var rich = OptimizationObjective.JRun(PlateauRun(300), c0);
        var lean = OptimizationObjective.JRun(PlateauRun(120), c0);
        Assert.That(rich, Is.EqualTo(lean), "Wtie=0 => pure primary objective => plateau ties stay tied");
    }

    [Test]
    public void JRun_TieBreaker_DoesNotOverrideRealPrimaryDifference() {
        var c = C; // the tiny tie-breaker must NOT flip a genuine focus-quality gap
        // A: sharp focus (better primary) but FEWER stars. B: soft focus (worse primary) but MORE stars.
        var sharpFewer = OptimizationObjective.JRun(PlateauRun(50, sigmaFocus: 10.0), c);   // rho=0.1
        var softMore = OptimizationObjective.JRun(PlateauRun(400, sigmaFocus: 30.0), c);    // rho=0.3
        Assert.That(sharpFewer, Is.GreaterThan(softMore), "a real primary-J advantage dominates the tie-breaker");
    }

    [Test]
    public void JRun_TieBreaker_DoesNotRescueHardFail() {
        var c = C;
        var starved = PlateauRun(300);
        var counts = starved.FrameStarCounts.ToList();
        counts[0] = 1; // below NHard=3 => hard fail
        starved.FrameStarCounts = counts;
        Assert.That(OptimizationObjective.JRun(starved, c), Is.EqualTo(0.0),
            "the tie-breaker is applied only on the feasible path; it never rescues an infeasible run");
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
