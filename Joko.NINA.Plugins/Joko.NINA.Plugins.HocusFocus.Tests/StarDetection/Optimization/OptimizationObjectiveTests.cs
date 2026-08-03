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

    [Test]
    public void JRun_TieBreaker_RejectsStarSheddingOnNearPlateau() {
        // Regression for the sensitivity/star-clip bistability (docs/optimizer-sensitivity-pinning-design.md):
        // on a (near-)saturated plateau a star-SHEDDING config can hold a vanishing σ-focus edge over a
        // star-RICH one (both J_primary ≈ 1.0), and a too-weak tie-breaker lets that σ-wiggle pick the
        // star-shedding corner — the bobp_m101 failure (recall 0.13 vs 0.87 at the same J). The shipped
        // tie-breaker strength must out-vote the wiggle in favour of keeping stars.
        RunEvaluationMetrics Plateau(int starsPerFrame, double sigmaFocus) => new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus, LooStdError = double.NaN, StepSize = 24.0, RSquared = 1.0,
            ReducedChiSquared = 1.0, FrameStarCounts = Enumerable.Repeat(starsPerFrame, 10).ToList()
        };
        var shedding = Plateau(25, 0.20);   // few stars, marginally sharper σ → a tiny primary-J edge
        var starRich = Plateau(200, 0.30);  // many stars, marginally softer σ (still on the plateau)

        // Pre-fix baseline: the original tiny Wtie lets the σ-wiggle pick the star-shedding corner (the bug).
        var cWeak = new ObjectiveConstants { Wtie = 1e-3 };
        Assert.That(OptimizationObjective.JRun(shedding, cWeak),
            Is.GreaterThan(OptimizationObjective.JRun(starRich, cWeak)),
            "the weak tie-breaker wrongly prefers the star-shedding corner");

        // Shipped strength: the strengthened tie-breaker keeps the star-rich corner.
        var c = C;
        Assert.That(OptimizationObjective.JRun(starRich, c),
            Is.GreaterThan(OptimizationObjective.JRun(shedding, c)),
            "the shipped tie-breaker keeps the star-rich corner on the plateau");
    }

    // ---- Focus recovery: exempt tagged recovery frames from the star-count gates ----

    // A run with explicit per-frame counts and a parallel FrameIsRecovery tag list (null => baseline / no recovery).
    // All other sub-scores are held healthy so the star-count gates are the only thing under test.
    private static RunEvaluationMetrics RecoveryRun(int[] counts, bool[] isRecovery, double sigmaFocus = 0.05) =>
        new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = 1.0,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = counts,
            FrameIsRecovery = isRecovery
        };

    [Test]
    public void JRun_HardFloorExemption_TaggedRecoveryFramesDoNotZeroJ() {
        var c = C; // NHard=3, MaxFramesBelowHardFloor=0
        // Two starved wing frames (0 and 1 stars) + 7 healthy frames. This is the core failure the feature fixes:
        // recovery frames routinely detect < NHard stars, and the hard floor would force J = 0 for every candidate.
        var counts = new[] { 0, 1, 40, 40, 40, 40, 40, 40, 40 };

        // Untagged (baseline): the starved frames must STILL hard-fail (proves the null path keeps gating).
        var untagged = RecoveryRun(counts, isRecovery: null);
        Assert.That(OptimizationObjective.JRun(untagged, c), Is.EqualTo(0.0),
            "with FrameIsRecovery null the starved frames must still force the hard floor => J == 0");

        // Same counts, but the two starved frames are tagged recovery => exempt from the hard floor.
        var tagged = RecoveryRun(counts,
            isRecovery: new[] { true, true, false, false, false, false, false, false, false });
        Assert.That(OptimizationObjective.JRun(tagged, c), Is.GreaterThan(0.0),
            "starved frames tagged as recovery are exempt from the hard floor => J > 0");
    }

    [Test]
    public void JRun_HardFloorExemption_NonRecoveryStarvedFrameStillHardFails() {
        var c = C;
        // Frame index 2 is starved (2 < 3) but NOT tagged recovery => it must still hard-fail even though the
        // outer wings are tagged. Exemption applies ONLY to tagged frames.
        var counts = new[] { 0, 1, 2, 40, 40, 40, 40, 40, 40 };
        var tagged = RecoveryRun(counts,
            isRecovery: new[] { true, true, false, false, false, false, false, false, false });
        Assert.That(OptimizationObjective.JRun(tagged, c), Is.EqualTo(0.0),
            "a starved NON-recovery frame is not exempt => the run still hard-fails");
    }

    [Test]
    public void JRun_SStars_IgnoresTaggedRecoveryFrames() {
        var c = C;
        // Healthy counts chosen so S_stars is BELOW 1.0 (min-term 1, median-term 0.5) and therefore sensitive to
        // any starved frame that leaks into nMin/nMedian.
        var healthy = Enumerable.Repeat(10, 7).ToArray();

        // Baseline: only the 7 healthy frames, no recovery tagging.
        var noRecovery = RecoveryRun(healthy, isRecovery: null, sigmaFocus: 0.2);

        // Same 7 healthy frames PLUS two starved (count 2) frames tagged as recovery. If those leaked into S_stars,
        // nMin would collapse to 2 and J would drop; the exemption must make J equal to the run without them.
        var withRecovery = RecoveryRun(
            new[] { 2, 2 }.Concat(healthy).ToArray(),
            isRecovery: new[] { true, true }.Concat(Enumerable.Repeat(false, 7)).ToArray(),
            sigmaFocus: 0.2);

        Assert.That(OptimizationObjective.JRun(withRecovery, c),
            Is.EqualTo(OptimizationObjective.JRun(noRecovery, c)).Within(1e-12),
            "starved recovery frames must not drag nMin/nMedian => J matches the run without them");
    }

    [Test]
    public void TieBreakerScore_IgnoresTaggedRecoveryFrames() {
        var c = C;
        // Baseline plateau: 9 frames @ 100 stars, no recovery.
        var baseline = PlateauRun(100);

        // Same 9 healthy frames @ 100 plus two recovery frames whose counts (0 and 5000) would swing the mean
        // dramatically if counted. Tagged recovery => excluded from the tie-breaker mean.
        var withRecovery = new RunEvaluationMetrics {
            SigmaFocus = 1e-6, LooStdError = double.NaN, StepSize = 100.0, RSquared = 1.0, ReducedChiSquared = 1.0,
            FrameStarCounts = new[] { 0, 5000 }.Concat(Enumerable.Repeat(100, 9)).ToArray(),
            FrameIsRecovery = new[] { true, true }.Concat(Enumerable.Repeat(false, 9)).ToArray()
        };

        Assert.That(OptimizationObjective.TieBreakerScore(withRecovery, c),
            Is.EqualTo(OptimizationObjective.TieBreakerScore(baseline, c)).Within(1e-12),
            "recovery frames (even wild outlier counts) must not move the tie-breaker mean");
    }

    [Test]
    [TestCase(0.05, 40)]
    [TestCase(0.18, 25)]
    [TestCase(0.30, 10)]
    [TestCase(0.50, 8)]
    public void JRun_BitIdentical_WhenFrameIsRecoveryNull(double sigmaFocus, int starsPerFrame) {
        // Regression net for THE OVERRIDING INVARIANT: when FrameIsRecovery is null (every legacy caller / Replay /
        // production autofocus), the recovery code path must be provably inert — JRun must equal the pre-feature
        // weighted-sum formula. Wtie = 0 isolates the primary objective (LegacyJRun models only the weighted sum).
        var c = new ObjectiveConstants { Wtie = 0.0 };
        var m = GoodRun(sigmaFocus: sigmaFocus, frameCount: 10, starsPerFrame: starsPerFrame);
        Assert.That(m.FrameIsRecovery, Is.Null, "baseline: recovery tagging absent");
        Assert.Multiple(() => {
            Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(LegacyJRun(m, c, null, null)),
                "recovery code path must be inert when FrameIsRecovery is null (unlabeled)");
            Assert.That(OptimizationObjective.JRun(m, c, recall: 0.8, precision: 0.7),
                Is.EqualTo(LegacyJRun(m, c, 0.8, 0.7)),
                "recovery code path must be inert when FrameIsRecovery is null (labeled)");
        });
    }

    // A run with per-frame positions/counts/relaxed and an optional recovery tag list, for SDefocusPrecision tests.
    // bestFocus = NaN forces the run-level FALLBACK path; a finite bestFocus uses the near-focus PRIMARY path.
    private static RunEvaluationMetrics PrecisionRun(
            int[] positions, int[] counts, int[] relaxed, bool[] isRecovery,
            int stepSize = 100, double bestFocus = 5000, double sigmaFocus = 0.05) =>
        new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = counts,
            FrameFocuserPositions = positions,
            FrameRelaxationAdmittedCounts = relaxed,
            BestFocusPosition = bestFocus,
            FrameIsRecovery = isRecovery
        };

    [Test]
    public void SDefocusPrecision_PrimaryWindow_ExemptsRecoveryFrameInsideWindow() {
        var c = C; // near-focus window = 1.5 * 100 = 150 around bestFocus 5000
        // Genuine near-focus frames carry a real relaxed fraction (30/120 = 0.25 => penalty 0.975).
        var positions = new[] { 4900, 5000, 5100 };
        var counts = new[] { 40, 40, 40 };
        var relaxed = new[] { 10, 10, 10 };
        var basePenalty = OptimizationObjective.SDefocusPrecision(
            PrecisionRun(positions, counts, relaxed, isRecovery: null), c);

        // A recovery frame lands INSIDE the window (position 5000, the poor-start scenario) with heavy relaxation.
        var posRec = new[] { 4900, 5000, 5100, 5000 };
        var countsRec = new[] { 40, 40, 40, 40 };
        var relaxedRec = new[] { 10, 10, 10, 40 };
        var withTagged = OptimizationObjective.SDefocusPrecision(
            PrecisionRun(posRec, countsRec, relaxedRec, new[] { false, false, false, true }), c);
        var withUntagged = OptimizationObjective.SDefocusPrecision(
            PrecisionRun(posRec, countsRec, relaxedRec, isRecovery: null), c);

        Assert.Multiple(() => {
            Assert.That(withTagged, Is.EqualTo(basePenalty).Within(1e-12),
                "a recovery frame inside the near-focus window must not create/inflate the precision penalty");
            Assert.That(withUntagged, Is.LessThan(basePenalty),
                "the SAME frame untagged DOES inflate the penalty — proving the recovery exemption is what suppresses it");
        });
    }

    [Test]
    public void SDefocusPrecision_Fallback_ExemptsRecoveryFrame() {
        var c = C; // bestFocus = NaN => run-level fallback
        var positions = new[] { 4900, 5000, 5100 };
        var basePenalty = OptimizationObjective.SDefocusPrecision(
            PrecisionRun(positions, new[] { 40, 40, 40 }, new[] { 20, 20, 20 }, isRecovery: null, bestFocus: double.NaN), c);

        var posRec = new[] { 4900, 5000, 5100, 4600 };
        var countsRec = new[] { 40, 40, 40, 40 };
        var relaxedRec = new[] { 20, 20, 20, 40 };
        var withTagged = OptimizationObjective.SDefocusPrecision(
            PrecisionRun(posRec, countsRec, relaxedRec, new[] { false, false, false, true }, bestFocus: double.NaN), c);
        var withUntagged = OptimizationObjective.SDefocusPrecision(
            PrecisionRun(posRec, countsRec, relaxedRec, isRecovery: null, bestFocus: double.NaN), c);

        Assert.Multiple(() => {
            Assert.That(withTagged, Is.EqualTo(basePenalty).Within(1e-12),
                "fallback penalty must be unchanged by a tagged recovery frame (numerator AND denominator both skip it)");
            Assert.That(withTagged, Is.GreaterThanOrEqualTo(c.DefocusPrecisionMinFactor),
                "the recovery-exempt fraction stays consistent (never > 1) => penalty stays within [MinFactor, 1]");
            Assert.That(withUntagged, Is.LessThan(basePenalty),
                "the same frame untagged DOES lower the fallback penalty");
        });
    }

    // A fallback-path HFR run (positions null / bestFocus NaN => pool every eligible frame) with a recovery tag list.
    private static RunEvaluationMetrics HfrFallbackRun(IReadOnlyList<double>[] frameHfrs, bool[] isRecovery) =>
        new RunEvaluationMetrics {
            SigmaFocus = 0.05,
            LooStdError = double.NaN,
            StepSize = 100,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = frameHfrs.Select(f => f.Count).ToArray(),
            FrameStarHFRs = frameHfrs,
            FrameIsRecovery = isRecovery
        };

    [Test]
    public void SHfrOutlier_Fallback_ExemptsRecoveryFrame() {
        var c = C;
        var baseFrames = new IReadOnlyList<double>[] { NormalsPlusBlob(9), NormalsPlusBlob(9), NormalsPlusBlob(9) };
        var basePenalty = OptimizationObjective.SHfrOutlier(HfrFallbackRun(baseFrames, isRecovery: null), c);

        var recoveryFrame = (IReadOnlyList<double>)Normals(10, hfr: 6.0); // an all-bloated recovery frame
        var withFrames = new[] { baseFrames[0], baseFrames[1], baseFrames[2], recoveryFrame };
        var withTagged = OptimizationObjective.SHfrOutlier(HfrFallbackRun(withFrames, new[] { false, false, false, true }), c);
        var withUntagged = OptimizationObjective.SHfrOutlier(HfrFallbackRun(withFrames, isRecovery: null), c);

        Assert.Multiple(() => {
            Assert.That(withTagged, Is.EqualTo(basePenalty).Within(1e-12),
                "a tagged recovery frame must not change the pooled outlier fraction");
            Assert.That(withUntagged, Is.Not.EqualTo(basePenalty).Within(1e-12),
                "the same frame untagged DOES change the pooled HFR sample");
        });
    }

    // A fallback-path coverage run (positions null / bestFocus NaN => average every finite frame) with a recovery tag list.
    private static RunEvaluationMetrics CoverageFallbackRun(double[] occupancy, bool[] isRecovery) =>
        new RunEvaluationMetrics {
            SigmaFocus = 0.05,
            LooStdError = double.NaN,
            StepSize = 100,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(40, occupancy.Length).ToArray(),
            FrameRegionOccupancy = occupancy,
            FrameIsRecovery = isRecovery
        };

    [Test]
    public void SCoverage_Fallback_ExemptsRecoveryFrame() {
        var c = C;
        var baseCov = OptimizationObjective.SCoverage(CoverageFallbackRun(new[] { 0.2, 0.4, 0.6 }, isRecovery: null), c);
        var withTagged = OptimizationObjective.SCoverage(
            CoverageFallbackRun(new[] { 0.2, 0.4, 0.6, 1.0 }, new[] { false, false, false, true }), c);
        var withUntagged = OptimizationObjective.SCoverage(
            CoverageFallbackRun(new[] { 0.2, 0.4, 0.6, 1.0 }, isRecovery: null), c);
        Assert.Multiple(() => {
            Assert.That(withTagged, Is.EqualTo(baseCov).Within(1e-12),
                "a tagged recovery frame with skewed occupancy must not move the coverage mean");
            Assert.That(withUntagged, Is.GreaterThan(baseCov),
                "the same frame untagged DOES move the coverage mean");
        });
    }

    [Test]
    public void JRun_AllFramesRecovery_DoesNotThrow() {
        var c = C;
        // Degenerate (can't happen in practice — the evaluator always leaves >= 3 non-recovery frames): EVERY frame is
        // tagged recovery and below the hard floor. JRun (via SStars) and TieBreakerScore both route the counts through
        // the internal NonRecoveryStarCounts, whose empty filtered sequence must fall back to the original counts so
        // Min/Median/Average never throw; the hard floor must also treat every frame as exempt (no divide-by-empty).
        var m = RecoveryRun(new[] { 0, 1, 2, 0 }, isRecovery: new[] { true, true, true, true });
        double j = double.NaN;
        Assert.Multiple(() => {
            Assert.That(() => j = OptimizationObjective.JRun(m, c), Throws.Nothing);
            Assert.That(() => OptimizationObjective.TieBreakerScore(m, c), Throws.Nothing);
        });
        Assert.That(j, Is.InRange(0.0, 1.0), "JRun still returns a valid score with every frame exempt");
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

    // ---- SFitGuard (aberration-inspection fit bound) ----

    private static RunEvaluationMetrics SigmaRun(double sigmaFocus) => new RunEvaluationMetrics {
        SigmaFocus = sigmaFocus,
        LooStdError = double.NaN,
        StepSize = 1.0,
        RSquared = 0.99,
        ReducedChiSquared = 1.0,
        FrameStarCounts = Enumerable.Repeat(40, 10).ToList()
    };

    [Test]
    public void SFitGuard_ReturnsOne_ForStandardConstants() {
        // Default constants leave ReferenceSigmaFocus = NaN ⇒ guard inert (J bit-identical to pre-guard).
        var c = C;
        Assert.That(OptimizationObjective.SFitGuard(SigmaRun(5.0), c), Is.EqualTo(1.0));
    }

    [Test]
    public void SFitGuard_ReturnsOne_WithinMargin() {
        // refσ = 0.10, margin 0.5 ⇒ no penalty up to σ = 0.15.
        var c = ObjectiveConstants.ForAberrationInspection(referenceSigmaFocus: 0.10);
        Assert.Multiple(() => {
            Assert.That(OptimizationObjective.SFitGuard(SigmaRun(0.10), c), Is.EqualTo(1.0)); // at reference
            Assert.That(OptimizationObjective.SFitGuard(SigmaRun(0.14), c), Is.EqualTo(1.0)); // within margin
            Assert.That(OptimizationObjective.SFitGuard(SigmaRun(0.15), c), Is.EqualTo(1.0).Within(1e-12)); // exactly at the knee
        });
    }

    [Test]
    public void SFitGuard_DecreasesMonotonically_AndClampsToFloor_BeyondMargin() {
        var c = ObjectiveConstants.ForAberrationInspection(referenceSigmaFocus: 0.10);
        var pJust = OptimizationObjective.SFitGuard(SigmaRun(0.16), c);  // just past the knee
        var pMore = OptimizationObjective.SFitGuard(SigmaRun(0.20), c);  // further
        var pFar = OptimizationObjective.SFitGuard(SigmaRun(1.00), c);   // far past ⇒ clamped to floor
        Assert.Multiple(() => {
            Assert.That(pJust, Is.LessThan(1.0));
            Assert.That(pMore, Is.LessThan(pJust));
            Assert.That(pFar, Is.EqualTo(c.FitGuardMinFactor).Within(1e-12));
            Assert.That(pMore, Is.GreaterThanOrEqualTo(c.FitGuardMinFactor));
        });
    }

    [Test]
    public void JRun_BitIdentical_WithDefaultConstants_RegardlessOfSigma() {
        // The fit guard must not perturb the standard objective: a default-constants JRun is unchanged whether σ is
        // tiny or large (SFitGuard ≡ 1.0). Compare against an explicit recompute of the weighted sub-scores.
        var c = C;
        var m = GoodRun(sigmaFocus: 2.5, frameCount: 10, starsPerFrame: 40); // a soft fit
        var sFocus = OptimizationObjective.SFocus(m.SigmaFocus, m.LooStdError, m.StepSize, c);
        var sStars = OptimizationObjective.SStars(m.FrameStarCounts, c);
        var sFit = OptimizationObjective.SFit(m.RSquared, m.ReducedChiSquared, c);
        var expected = (c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit) / (c.Wf + c.Ws + c.Wc);
        expected = (1.0 - c.Wtie) * expected + c.Wtie * OptimizationObjective.TieBreakerScore(m, c);
        Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(expected).Within(1e-12));
    }

    // ---- ForAberrationInspection (star-favoring reweight) ----

    [Test]
    public void ForAberrationInspection_FlipsRankingTowardMoreStars() {
        // A = sharp focus but star-poor; B = slightly softer focus (still within the fit-guard margin of A's σ) but
        // star-rich. Under the STANDARD objective the sharp run wins; under the inspection objective (anchored to A's
        // σ as "current settings") the star-rich run wins — the whole point of the mode.
        var sharpStarPoor = new RunEvaluationMetrics {
            SigmaFocus = 0.10, LooStdError = double.NaN, StepSize = 1.0, RSquared = 0.99, ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(12, 10).ToList()
        };
        var softStarRich = new RunEvaluationMetrics {
            SigmaFocus = 0.13, LooStdError = double.NaN, StepSize = 1.0, RSquared = 0.99, ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(60, 10).ToList()
        };

        var standard = C;
        var inspection = ObjectiveConstants.ForAberrationInspection(referenceSigmaFocus: 0.10);

        Assert.Multiple(() => {
            Assert.That(OptimizationObjective.JRun(sharpStarPoor, standard),
                Is.GreaterThan(OptimizationObjective.JRun(softStarRich, standard)),
                "standard objective should prefer the sharper run");
            Assert.That(OptimizationObjective.JRun(softStarRich, inspection),
                Is.GreaterThan(OptimizationObjective.JRun(sharpStarPoor, inspection)),
                "inspection objective should prefer the star-rich run");
        });
    }

    [Test]
    public void ForAberrationInspection_PenalizesRunsThatBlowPastTheFitBound() {
        // A star-rich run whose σ degrades far past the margin is held back by SFitGuard: its J is lower than the same
        // star count would earn within the margin — bounding how far the optimizer can trade fit for stars.
        var inspection = ObjectiveConstants.ForAberrationInspection(referenceSigmaFocus: 0.10);
        var withinBound = new RunEvaluationMetrics {
            SigmaFocus = 0.12, LooStdError = double.NaN, StepSize = 1.0, RSquared = 0.99, ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(60, 10).ToList()
        };
        var wayPastBound = new RunEvaluationMetrics {
            SigmaFocus = 0.60, LooStdError = double.NaN, StepSize = 1.0, RSquared = 0.99, ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(60, 10).ToList()
        };
        Assert.That(OptimizationObjective.JRun(wayPastBound, inspection),
            Is.LessThan(OptimizationObjective.JRun(withinBound, inspection)));
    }

    // ---- SHfrOutlier (extreme-HFR outlier penalty) ----

    // Builds a run whose accepted-star HFRs are supplied per frame. Positions default to all-at-bestFocus (every
    // frame inside the near-focus window); pass explicit positions to place frames inside/outside it. StarCounts are
    // derived from each frame's HFR list length (the SHfrOutlier denominator).
    private static RunEvaluationMetrics HfrRun(
            IReadOnlyList<double>[] frameHfrs, int[] positions = null,
            int stepSize = 100, int bestFocus = 5000, double sigmaFocus = 0.05) {
        var n = frameHfrs.Length;
        var pos = positions ?? Enumerable.Repeat(bestFocus, n).ToArray();
        var counts = frameHfrs.Select(f => f.Count).ToArray();
        return new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = counts,
            FrameFocuserPositions = pos,
            FrameStarHFRs = frameHfrs,
            BestFocusPosition = bestFocus
        };
    }

    // n normal stars sharing one tight HFR (the typical near-focus population).
    private static double[] Normals(int n, double hfr = 2.0) => Enumerable.Repeat(hfr, n).ToArray();

    // A frame of n normals plus a single bloated/saturated star whose HFR is far above the rest.
    private static double[] NormalsPlusBlob(int n, double blob = 6.0, double hfr = 2.0) =>
        Normals(n, hfr).Concat(new[] { blob }).ToArray();

    [Test]
    public void SHfrOutlier_NoData_ReturnsExactlyOne() {
        // GoodRun has null FrameStarHFRs (the legacy caller shape) => no penalty.
        Assert.That(OptimizationObjective.SHfrOutlier(GoodRun(), C), Is.EqualTo(1.0));
    }

    [Test]
    public void SHfrOutlier_NoExtremeOutliers_ReturnsExactlyOne() {
        // Tight near-focus HFRs (median 2.0, MAD 0). The relMargin guard (1.5×median = 3.0) prevents flagging
        // anything when MAD collapses to 0, so a uniform population is never penalized.
        var m = HfrRun(new IReadOnlyList<double>[] { Normals(10) });
        Assert.That(OptimizationObjective.SHfrOutlier(m, C), Is.EqualTo(1.0));
    }

    [Test]
    public void SHfrOutlier_ExtremeStar_DropsBelowOne() {
        // 9 normals at 2.0 + one bloated star at 6.0 (≥ 1.5×median, MAD 0). outliers=1, pooled=10 => frac 0.10;
        // excess over the 0.05 threshold => penalty 1 − 1.0·(0.10 − 0.05) = 0.95.
        var m = HfrRun(new IReadOnlyList<double>[] { NormalsPlusBlob(9) });
        var penalty = OptimizationObjective.SHfrOutlier(m, C);
        Assert.Multiple(() => {
            Assert.That(penalty, Is.LessThan(1.0));
            Assert.That(penalty, Is.EqualTo(0.95).Within(1e-12));
        });
    }

    [Test]
    public void SHfrOutlier_RelaxesAsNormalStarsAdded_Dilution() {
        // THE make-or-break contract: a fixed single bloated star (6.0); as more normal stars (2.0) are admitted,
        // the outlier FRACTION 1/(N+1) shrinks, so the penalty must be non-decreasing and reach exactly 1.0 once
        // 1/(N+1) ≤ threshold (0.05 ⇒ N+1 ≥ 20). This is what couples the penalty to recall.
        var prev = 0.0;
        for (var normals = 4; normals <= 25; normals++) {
            var penalty = OptimizationObjective.SHfrOutlier(
                HfrRun(new IReadOnlyList<double>[] { NormalsPlusBlob(normals) }), C);
            Assert.That(penalty, Is.GreaterThanOrEqualTo(prev),
                $"penalty must not decrease as more normal stars are admitted (normals={normals})");
            prev = penalty;
        }
        // At a low star count the bloated star dominates a large fraction ⇒ a REAL penalty (strictly below 1)...
        var atLow = OptimizationObjective.SHfrOutlier(HfrRun(new IReadOnlyList<double>[] { NormalsPlusBlob(4) }), C);
        Assert.That(atLow, Is.LessThan(1.0), "at low recall the bloated star is a large outlier fraction => penalized");
        // ... and once enough normal stars dilute it (N+1 = 20, normals = 19; frac 0.05 == threshold) ⇒ exactly 1.0.
        var atHigh = OptimizationObjective.SHfrOutlier(HfrRun(new IReadOnlyList<double>[] { NormalsPlusBlob(19) }), C);
        Assert.That(atHigh, Is.EqualTo(1.0).Within(1e-12));
        Assert.That(atHigh, Is.GreaterThan(atLow), "the penalty must relax as recall rises (dilution)");
    }

    [Test]
    public void SHfrOutlier_DefocusedExtremeBlob_NotPenalized() {
        // bestFocus 5000, step 100, window 1.5·100 = 150. The bloated star is on a FAR (defocused) frame at 4600;
        // the near-focus frames (4900/5000/5100) are clean. The far blob is excluded from the near-focus pool.
        var frames = new IReadOnlyList<double>[] { NormalsPlusBlob(9), Normals(10), Normals(10), Normals(10) };
        var positions = new[] { 4600, 4900, 5000, 5100 };
        Assert.That(OptimizationObjective.SHfrOutlier(HfrRun(frames, positions), C), Is.EqualTo(1.0),
            "a bloated star on a defocused extreme is not a near-focus outlier");
    }

    [Test]
    public void SHfrOutlier_PenaltyFlooredAtMinFactor() {
        // frac 0.10 with a very high strength would push the penalty negative; it must clamp to the floor.
        var cHard = new ObjectiveConstants { HfrOutlierStrength = 100.0 };
        var penalty = OptimizationObjective.SHfrOutlier(HfrRun(new IReadOnlyList<double>[] { NormalsPlusBlob(9) }), cHard);
        Assert.That(penalty, Is.EqualTo(cHard.HfrOutlierMinFactor).Within(1e-12));
    }

    [Test]
    public void SHfrOutlier_Fallback_RunLevelPool_WhenNoNearFocusWindow() {
        // No focuser positions / NaN bestFocus => fall back to pooling all frames (guarded by MinFramesForPenalty).
        // 3 frames each {9 normals + 1 blob} => pooled 30, outliers 3 => frac 0.10 => penalty 0.95.
        var frame = NormalsPlusBlob(9);
        var m = new RunEvaluationMetrics {
            SigmaFocus = 0.05,
            LooStdError = double.NaN,
            StepSize = 100,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = new[] { 10, 10, 10 },
            FrameStarHFRs = new IReadOnlyList<double>[] { frame, frame, frame }
            // FrameFocuserPositions null, BestFocusPosition NaN => fallback path
        };
        Assert.That(OptimizationObjective.SHfrOutlier(m, C), Is.EqualTo(0.95).Within(1e-12));
    }

    [Test]
    public void SHfrOutlier_Fallback_TooFewFrames_NoPenalty() {
        var c = C; // MinFramesForPenalty = 3
        var frame = NormalsPlusBlob(9);
        var m = new RunEvaluationMetrics {
            SigmaFocus = 0.05,
            LooStdError = double.NaN,
            StepSize = 100,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = new[] { 10, 10 },
            FrameStarHFRs = new IReadOnlyList<double>[] { frame, frame }
        };
        Assert.That(OptimizationObjective.SHfrOutlier(m, c), Is.EqualTo(1.0),
            "too few frames in the run-level fallback => no penalty");
    }

    [Test]
    public void JRun_ExtremeHfrOutlier_LowersJ() {
        // Identical runs except one star's HFR (clean 2.0 vs bloated 6.0) — same counts and σ, so the only
        // difference in J is the multiplicative SHfrOutlier penalty.
        var clean = HfrRun(new IReadOnlyList<double>[] { Normals(10), Normals(10), Normals(10) });
        var blob = HfrRun(new IReadOnlyList<double>[] { NormalsPlusBlob(9), NormalsPlusBlob(9), NormalsPlusBlob(9) });
        Assert.That(OptimizationObjective.JRun(blob, C), Is.LessThan(OptimizationObjective.JRun(clean, C)));
    }

    // ---- SCoverage (region-coverage reward) ----

    // Builds a run with supplied per-frame region-occupancy fractions, all frames inside the near-focus window.
    private static RunEvaluationMetrics CoverageRun(double[] occupancy, int stepSize = 100, int bestFocus = 5000, double sigmaFocus = 0.05) {
        var n = occupancy.Length;
        return new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(40, n).ToArray(),
            FrameFocuserPositions = Enumerable.Repeat(bestFocus, n).ToArray(),
            FrameRegionOccupancy = occupancy,
            BestFocusPosition = bestFocus
        };
    }

    [Test]
    public void SCoverage_NoData_ReturnsNaN() {
        Assert.That(double.IsNaN(OptimizationObjective.SCoverage(GoodRun(), C)), Is.True);
    }

    [Test]
    public void SCoverage_IsMeanOfNearFocusOccupancy() {
        var s = OptimizationObjective.SCoverage(CoverageRun(new[] { 0.2, 0.4, 0.6 }), C);
        Assert.That(s, Is.EqualTo(0.4).Within(1e-12));
    }

    [Test]
    public void SCoverage_RisesWithSpread() {
        var clustered = OptimizationObjective.SCoverage(CoverageRun(new[] { 1.0 / 9, 1.0 / 9, 1.0 / 9 }), C);
        var spread = OptimizationObjective.SCoverage(CoverageRun(new[] { 1.0, 1.0, 1.0 }), C);
        Assert.That(spread, Is.GreaterThan(clustered));
    }

    [Test]
    public void SCoverage_SkipsNonFiniteOccupancy() {
        // A NaN occupancy (frame had no usable geometry) is skipped, not averaged in.
        var s = OptimizationObjective.SCoverage(CoverageRun(new[] { 0.2, double.NaN, 0.6 }), C);
        Assert.That(s, Is.EqualTo(0.4).Within(1e-12));
    }

    [Test]
    public void JRun_Coverage_RaisesJ() {
        var c = C; // Wcov = 0.05
        var clustered = CoverageRun(new[] { 1.0 / 9, 1.0 / 9, 1.0 / 9 });
        var spread = CoverageRun(new[] { 1.0, 1.0, 1.0 });
        Assert.That(OptimizationObjective.JRun(spread, c), Is.GreaterThan(OptimizationObjective.JRun(clustered, c)));
    }

    [Test]
    public void JRun_Coverage_CanCostFocus() {
        var c = C;
        // A tight-but-clustered run vs a slightly-looser-but-spread run (step 1.0 so σ actually moves SFocus). With
        // the coverage weight the spread run out-scores the tighter clustered run — coverage is allowed to cost a
        // SMALL amount of focus tightness (the user's explicit requirement).
        var tightClustered = CoverageRun(new[] { 1.0 / 9, 1.0 / 9, 1.0 / 9 }, stepSize: 1, sigmaFocus: 0.05);
        var looserSpread = CoverageRun(new[] { 1.0, 1.0, 1.0 }, stepSize: 1, sigmaFocus: 0.08);
        Assert.That(OptimizationObjective.JRun(looserSpread, c), Is.GreaterThan(OptimizationObjective.JRun(tightClustered, c)));
    }

    [Test]
    public void JRun_CoverageExcluded_WhenWcovZero() {
        var c = new ObjectiveConstants { Wtie = 0.0, Wcov = 0.0 };
        var m = CoverageRun(new[] { 1.0, 1.0, 1.0 }); // occupancy present but Wcov = 0 => excluded
        Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(LegacyJRun(m, c, null, null)));
    }

    [Test]
    public void JRun_BitIdentical_WhenNoHfrOrCoverageData() {
        // DEFAULT constants (Wcov = 0.05, HfrOutlier on) but the metrics carry NEITHER occupancy NOR per-star HFR
        // => both new terms inert => J byte-identical to the legacy weighted sum. This pins the "default-on is safe
        // for legacy callers" guarantee for both the labeled and unlabeled cases.
        var c = new ObjectiveConstants { Wtie = 0.0 };
        var m = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18);
        Assert.Multiple(() => {
            Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(LegacyJRun(m, c, null, null)));
            Assert.That(OptimizationObjective.JRun(m, c, recall: 0.7, precision: 0.6), Is.EqualTo(LegacyJRun(m, c, 0.7, 0.6)));
        });
    }

    // ---- SMarginalSnr (F23 marginal-SNR false-positive proxy) ----

    // Builds a run whose accepted-star Sensitivity-gate SNRs are supplied per frame, mirroring HfrRun above.
    // Positions default to all-at-bestFocus (every frame inside the near-focus window); pass explicit positions to
    // place frames outside it. StarCounts are derived from each frame's SNR list length.
    private static RunEvaluationMetrics SnrRun(
            IReadOnlyList<double>[] frameSnrs, int[] positions = null,
            int stepSize = 100, int bestFocus = 5000, double sigmaFocus = 0.05) {
        var n = frameSnrs.Length;
        var pos = positions ?? Enumerable.Repeat(bestFocus, n).ToArray();
        var counts = frameSnrs.Select(f => f.Count).ToArray();
        return new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = counts,
            FrameFocuserPositions = pos,
            FrameStarSnrs = frameSnrs,
            BestFocusPosition = bestFocus
        };
    }

    // n healthy stars sharing one SNR comfortably above the 5.0 floor.
    private static double[] Bright(int n, double snr = 20.0) => Enumerable.Repeat(snr, n).ToArray();

    // n bright stars plus k marginal ones below the floor — the noise-blob signature of a Sensitivity-0 landing.
    private static double[] BrightPlusMarginal(int n, int k, double marginal = 2.0) =>
        Bright(n).Concat(Enumerable.Repeat(marginal, k)).ToArray();

    [Test]
    public void SMarginalSnr_NoData_ReturnsExactlyOne() {
        // GoodRun has null FrameStarSnrs (the legacy caller shape) => no penalty, so J is bit-identical.
        Assert.That(OptimizationObjective.SMarginalSnr(GoodRun(), C), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_AllAboveFloor_ReturnsExactlyOne() {
        // THE structural guarantee: accepted stars are left-censored at the candidate's own Sensitivity, so any
        // candidate with Sensitivity >= MarginalSnrFloor has NO accepted star below the floor and pays exactly
        // nothing. This is why the shipped default (Sensitivity 10, floor 5) is untouched by the term.
        var m = SnrRun(new IReadOnlyList<double>[] { Bright(10), Bright(10) });
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_StrengthZero_ReturnsExactlyOne() {
        // The --legacy-objective escape hatch: strength 0 disables the term even with a floor-violating population.
        var c = new ObjectiveConstants { MarginalSnrStrength = 0.0 };
        var m = SnrRun(new IReadOnlyList<double>[] { BrightPlusMarginal(1, 9) });
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.LessThan(1.0), "precondition: this population does penalize at default strength");
        Assert.That(OptimizationObjective.SMarginalSnr(m, c), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_MarginalFraction_AppliesThePenaltyShape() {
        // 9 bright + 1 marginal => frac 0.10; excess over the 0.05 threshold => 1 − 1.0·(0.10 − 0.05) = 0.95.
        var m = SnrRun(new IReadOnlyList<double>[] { BrightPlusMarginal(9, 1) });
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(0.95).Within(1e-12));
    }

    [Test]
    public void SMarginalSnr_ClampsAtMinFactor() {
        // A wholly-marginal near-focus population (frac 1.0) would give 1 − 0.95 = 0.05; the floor holds it at 0.5,
        // so this term alone can never drive J to zero — the hard floors own the hard-fail path.
        var m = SnrRun(new IReadOnlyList<double>[] { Bright(0).Concat(Enumerable.Repeat(1.5, 12)).ToArray() });
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(0.5).Within(1e-12));
    }

    [Test]
    public void SMarginalSnr_OnlyDefocusedExtremesAreMarginal_ReturnsExactlyOne() {
        // THE window is load-bearing: far from focus a REAL star spreads out and its per-pixel peak SNR legitimately
        // collapses. Marginal SNRs confined to the defocused wings must NOT be penalized.
        var positions = new[] { 4000, 5000, 6000 };  // window = 1.5 × 100 = 150, so only the middle frame qualifies
        var m = SnrRun(
            new IReadOnlyList<double>[] { BrightPlusMarginal(0, 10), Bright(10), BrightPlusMarginal(0, 10) },
            positions);
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_RecoveryFramesExempt() {
        // A recovery frame that lands INSIDE the near-focus window (short/skewed sweep) is exempt on both numerator
        // and denominator, matching every other near-focus term.
        var m = SnrRun(new IReadOnlyList<double>[] { Bright(10), BrightPlusMarginal(0, 10) });
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.LessThan(1.0), "precondition: penalized without the recovery tag");
        m.FrameIsRecovery = new[] { false, true };
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_NonFiniteSnrsAreSkipped_NotCountedAsMarginal() {
        // MeasuredSensitivity is NaN on the legacy cache path. NaN carries no information, so it must inflate
        // neither the numerator nor the denominator — otherwise a stale cache would look like a precision failure.
        var m = SnrRun(new IReadOnlyList<double>[] { Bright(10).Concat(new[] { double.NaN, double.NaN }).ToArray() });
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_FallbackPool_ThinRunNotPenalised() {
        // No fitted minimum => no near-focus window => run-level fallback, which requires MinFramesForPenalty (3)
        // eligible frames before it may penalize. Two frames is too thin to trust.
        var m = SnrRun(new IReadOnlyList<double>[] { BrightPlusMarginal(1, 9), BrightPlusMarginal(1, 9) });
        m.BestFocusPosition = double.NaN;
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(1.0));
    }

    [Test]
    public void SMarginalSnr_FallbackPool_PenalisesOnceThickEnough() {
        var m = SnrRun(new IReadOnlyList<double>[] {
            BrightPlusMarginal(9, 1), BrightPlusMarginal(9, 1), BrightPlusMarginal(9, 1)
        });
        m.BestFocusPosition = double.NaN;
        Assert.That(OptimizationObjective.SMarginalSnr(m, C), Is.EqualTo(0.95).Within(1e-12));
    }

    [Test]
    public void SMarginalSnr_RelaxesAsBrightStarsAdded_Dilution() {
        // Companion to SHfrOutlier's dilution contract: with a FIXED count of marginal detections, admitting more
        // healthy stars shrinks the fraction, so the penalty is non-decreasing and returns to exactly 1.0 once the
        // fraction falls to the threshold. The term charges for the marginal SHARE, not for detecting more.
        double prev = 0.0;
        for (var bright = 4; bright <= 40; bright += 4) {
            var m = SnrRun(new IReadOnlyList<double>[] { BrightPlusMarginal(bright, 1) });
            var penalty = OptimizationObjective.SMarginalSnr(m, C);
            Assert.That(penalty, Is.GreaterThanOrEqualTo(prev), $"penalty must not fall as healthy stars are added (bright={bright})");
            prev = penalty;
        }
        Assert.That(prev, Is.EqualTo(1.0), "1/41 <= 0.05 threshold => exactly no penalty");
    }

    [Test]
    public void JRun_BitIdentical_WhenNoStarSnrData() {
        // The F23 default-on-is-safe guarantee, in the same shape as JRun_BitIdentical_WhenNoHfrOrCoverageData:
        // default constants (MarginalSnrStrength 1.0) with metrics carrying no per-star SNRs => term inert => J
        // byte-identical to the legacy weighted sum, labeled and unlabeled.
        var c = new ObjectiveConstants { Wtie = 0.0 };
        var m = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18);
        Assert.Multiple(() => {
            Assert.That(m.FrameStarSnrs, Is.Null, "baseline: no per-star SNR data");
            Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(LegacyJRun(m, c, null, null)));
            Assert.That(OptimizationObjective.JRun(m, c, recall: 0.7, precision: 0.6), Is.EqualTo(LegacyJRun(m, c, 0.7, 0.6)));
        });
    }

    [Test]
    public void JRun_BitIdentical_WhenEveryAcceptedStarIsAboveTheFloor() {
        // The stronger statement: even WITH per-star SNR data populated (the production optimizer path), a run whose
        // Sensitivity sits at or above the floor pays exactly nothing — so the term cannot perturb any configuration
        // that was already healthy.
        var c = new ObjectiveConstants { Wtie = 0.0 };
        var m = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18);
        m.FrameStarSnrs = Enumerable.Range(0, 9).Select(_ => (IReadOnlyList<double>)Bright(40)).ToArray();
        Assert.That(OptimizationObjective.JRun(m, c), Is.EqualTo(LegacyJRun(m, c, null, null)));
    }

    [Test]
    public void JRun_MarginalPopulation_LowersJ() {
        // The behavioural point of F23: an otherwise-identical run that admits a marginal-SNR tail must score LOWER
        // than one that does not. Without this, star count is free and the search drives Sensitivity to 0.
        var c = new ObjectiveConstants { Wtie = 0.0 };
        var clean = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18);
        clean.FrameStarSnrs = Enumerable.Range(0, 9).Select(_ => (IReadOnlyList<double>)Bright(40)).ToArray();
        var junky = RunWithPositions(frameCount: 9, starsPerFrame: 40, sigmaFocus: 0.18);
        junky.FrameStarSnrs = Enumerable.Range(0, 9).Select(_ => (IReadOnlyList<double>)BrightPlusMarginal(20, 20)).ToArray();
        Assert.That(OptimizationObjective.JRun(junky, c), Is.LessThan(OptimizationObjective.JRun(clean, c)));
    }
}
