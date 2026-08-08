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
using System.Globalization;
using System.Linq;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OxyPlot.Series;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class StepSizeRecommenderTests {

    // Symmetric hyperbola hfr(pos) = sqrt(a^2 + ((pos - p0)/b)^2). The offset W where hfr == 3*minHfr (== 3a)
    // satisfies 9a^2 = a^2 + (W/b)^2 => W = b*sqrt(8)*a. With a=1.5, b=8 => W = 8*sqrt(8)*1.5 ~= 33.94.
    private const double A = 1.5;
    private const double B = 8.0;
    private const int P0 = 10000;
    private static double ExpectedHalfWidth => B * Math.Sqrt(8.0) * A; // ~33.94

    // Mirrors StepSizeRecommender's private PointsPerSide (3.5): the fixed-point tests below need the same
    // closed-form step* = W / PointsPerSide the production code uses, to derive plausible starting step sizes.
    private const double PointsPerSide = 3.5;

    // Sweep shape shared by the fixed-point convergence tests: offsetSteps=4 per side (9 points total), matching
    // StepSizeRecommender.DefaultOffsetSteps -- the recommendation the loop is chasing is itself always 4.
    private const int FixedPointOffsetSteps = 4;
    private const int FixedPointMaxRounds = 10;

    private static AlglibHyperbolicFitting FitCleanHyperbola() {
        var alglib = new AlglibAPI();
        var points = new List<ScatterErrorPoint>();
        for (var i = -6; i <= 6; i++) {
            var pos = P0 + i * 100;
            var dx = (pos - P0) / B;
            var hfr = Math.Sqrt(A * A + dx * dx);
            points.Add(new ScatterErrorPoint(pos, hfr, 0, 0.02));
        }
        AlglibHyperbolicFitting.SelectBestModel(alglib, points, stepSize: 100, useWeights: true,
            maxOutlierRejections: 0, rejectionConfidence: 0.0, out var bestFit, out _);
        return bestFit;
    }

    /// <summary>Fits a SHALLOW sweep: one whose ends only reach <paramref name="edgeHfrRatio"/> times the minimum
    /// HFR, so the 3x band the recommender measures lies outside the sampled range entirely.</summary>
    private static AlglibHyperbolicFitting FitShallowHyperbola(double minHfr, double edgeHfrRatio, int stepSize, int offsetSteps) {
        var halfSpan = stepSize * offsetSteps;
        var b = halfSpan / (Math.Sqrt(edgeHfrRatio * edgeHfrRatio - 1.0) * minHfr);
        var points = new List<ScatterErrorPoint>();
        for (var i = -offsetSteps; i <= offsetSteps; i++) {
            var dx = (i * stepSize) / b;
            points.Add(new ScatterErrorPoint(P0 + i * stepSize, Math.Sqrt(minHfr * minHfr + dx * dx), 0, 0.02));
        }
        AlglibHyperbolicFitting.SelectBestModel(new AlglibAPI(), points, stepSize, useWeights: true,
            maxOutlierRejections: 0, rejectionConfidence: 0.0, out var bestFit, out _);
        return bestFit;
    }

    /// <summary>
    /// Fits the 9-point sweep (offsetSteps=4 per side) a real auto-focus run would capture around
    /// <paramref name="centre"/> at <paramref name="stepSize"/>, from the exact AF physics truth curve
    /// <c>HFR(dx) = sqrt(hfrMin^2 + (kappa*dx)^2)</c> -- the same closed form
    /// <c>InFocusHfrDiagnosticTests.Diagnostic_StepSizeRecommendation_ExtrapolationBeyondTheSampledSweep</c>
    /// builds, so a fixed-point iteration over this is exactly "re-sweep at the recommended step and see what
    /// it recommends next".
    /// </summary>
    private static AlglibHyperbolicFitting FitTruthSweep(double hfrMin, double kappa, int centre, int stepSize, int offsetSteps) {
        var points = new List<ScatterErrorPoint>();
        for (var i = -offsetSteps; i <= offsetSteps; i++) {
            var dx = i * stepSize;
            var hfr = Math.Sqrt(hfrMin * hfrMin + kappa * dx * kappa * dx);
            points.Add(new ScatterErrorPoint(centre + dx, hfr, 0, 0.02));
        }
        AlglibHyperbolicFitting.SelectBestModel(new AlglibAPI(), points, stepSize, useWeights: true,
            maxOutlierRejections: 0, rejectionConfidence: 0.0, out var bestFit, out _);
        return bestFit;
    }

    /// <summary>
    /// The convergence driver under test (design doc "V1. The convergence driver", A3/A4): sweep at
    /// (centre, step, offsetSteps=4) on the truth curve, fit, <see cref="StepSizeRecommender.Recommend"/>, take
    /// the new step, repeat -- exactly the update loop <c>synth-validate</c> runs against a real optimizer,
    /// except here the "detector" is the exact closed-form curve so only the recommender's own arithmetic can
    /// make it fail to settle. Returns once the step stops changing (a fixed point) along with how many rounds
    /// that took; fails the test outright if it has not settled within <paramref name="maxRounds"/>, since a
    /// step size that never stabilizes would mean the recommender cannot be trusted to converge at all.
    /// </summary>
    private static (int FixedPoint, int Rounds) ConvergeStepSize(double hfrMin, double kappa, int startStep, int maxRounds = FixedPointMaxRounds) {
        var step = startStep;
        for (var round = 1; round <= maxRounds; round++) {
            var fit = FitTruthSweep(hfrMin, kappa, P0, step, FixedPointOffsetSteps);
            var rec = StepSizeRecommender.Recommend(fit, step);
            if (rec.StepSize == step) {
                return (step, round - 1);
            }
            step = rec.StepSize;
        }
        Assert.Fail($"step size did not converge within {maxRounds} rounds from start {startStep} " +
            $"(hfrMin={hfrMin}, kappa={kappa}); still changing at step {step}");
        return (step, maxRounds); // unreachable; Assert.Fail throws
    }

    [Test]
    public void Recommend_ShallowSweep_CapsTheExtrapolationAndSaysSo() {
        // The reported case: HFR ~5.1 px at the vertex and only ~6.9 px at the ends of a +/-2096-step sweep. The
        // 3x band sits ~6400 steps out — 3.1x further than anything measured — and uncapped that recommended a
        // step of ~1838, i.e. a +/-7352 sweep derived almost entirely from extrapolating the model.
        const int stepSize = 524;
        const int offsetSteps = 4;
        var fit = FitShallowHyperbola(minHfr: 5.1, edgeHfrRatio: 1.36, stepSize: stepSize, offsetSteps: offsetSteps);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: stepSize);

        var sampledHalfSpan = stepSize * offsetSteps;
        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.True, "the half-width came from outside the sampled sweep");
            Assert.That(rec.HalfWidth, Is.EqualTo(StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple * sampledHalfSpan).Within(1.0));
            Assert.That(rec.StepSize, Is.EqualTo(898).Within(2), "a bounded step toward the answer, not a 3x jump");
            Assert.That(rec.StepSize * rec.OffsetSteps, Is.LessThanOrEqualTo(2 * sampledHalfSpan),
                "the implied sweep must stay near what this run actually measured");
        });
    }

    [Test]
    public void Recommend_SweepThatAlreadyReachesTheBand_IsNotCapped() {
        // A well-shaped sweep whose ends reach 3x the minimum already contains the half-width, so nothing is
        // extrapolated and the recommendation stands on its own data.
        var fit = FitShallowHyperbola(minHfr: 5.1, edgeHfrRatio: 3.0, stepSize: 524, offsetSteps: 4);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 524);

        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.False);
            Assert.That(rec.StepSize, Is.EqualTo(599).Within(3));
        });
    }

    /// <summary>
    /// Extends the two cap tests above (a single shallow sweep, a single well-shaped one) across a spread of
    /// curve widths relative to the sampled span, including a pair straddling the 1.5x boundary from either
    /// side. <see cref="FitShallowHyperbola"/> places the sweep's outer edge at
    /// <paramref name="edgeHfrRatio"/>-times the minimum HFR; from that same closed form the true 3x-band
    /// half-width is <c>sampledHalfSpan * sqrt(8 / (edgeHfrRatio^2 - 1))</c> (algebra: with
    /// <c>b = halfSpan / (sqrt(edgeHfrRatio^2-1)*minHfr)</c> and <c>W = b*sqrt(8)*minHfr</c>, the minHfr and b
    /// terms cancel). <see cref="StepSizeRecommender.WasCapped"/> must agree with that ratio exceeding
    /// <see cref="StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple"/> exactly -- not approximately --
    /// since it is a boolean gate, not a continuous quantity.
    /// </summary>
    [TestCase(1.20)]  // deeply shallow: true half-width many multiples of the sampled span
    [TestCase(1.50)]
    [TestCase(1.80)]
    [TestCase(2.00)]
    [TestCase(2.08)]  // ratio ~1.55x -- close to the 1.5x boundary from above (capped)
    [TestCase(2.19)]  // ratio ~1.45x -- close to the 1.5x boundary from below (not capped)
    [TestCase(2.30)]
    [TestCase(2.50)]
    [TestCase(3.00)]  // the sweep's own ends already reach the 3x band: nothing extrapolated
    [TestCase(5.00)]  // very deep sweep relative to the band: half-width sits well inside the data
    public void Recommend_CapSemantics_MatchTheHalfWidthToSampledSpanRatio(double edgeHfrRatio) {
        const int stepSize = 200;
        const int offsetSteps = 4;
        const double minHfr = 3.3;
        var sampledHalfSpan = stepSize * offsetSteps;
        var fit = FitShallowHyperbola(minHfr, edgeHfrRatio, stepSize, offsetSteps);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: stepSize);

        var trueHalfWidth = sampledHalfSpan * Math.Sqrt(8.0 / (edgeHfrRatio * edgeHfrRatio - 1.0));
        var expectCapped = trueHalfWidth > StepSizeRecommender.MaxHalfWidthSampledHalfSpanMultiple * sampledHalfSpan;

        TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "edge {0:F2}x min | sampledHalfSpan {1} | trueHalfWidth {2:F1} ({3:F3}x sampledHalfSpan) | capped={4} (expected {5}) | step={6}",
            edgeHfrRatio, sampledHalfSpan, trueHalfWidth, trueHalfWidth / sampledHalfSpan, rec.WasCapped, expectCapped, rec.StepSize));

        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.EqualTo(expectCapped),
                "WasCapped must say exactly when the true half-width fell outside 1.5x the sampled half-span");
            // The default offset steps is a fixed constant of the recommender, never derived from the curve.
            Assert.That(rec.OffsetSteps, Is.EqualTo(4));
            Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo(1));
            if (rec.WasCapped) {
                // Capping clamps HalfWidth to the same 1.5x*sampledHalfSpan regardless of how far out the true
                // band actually sits, so the sweep the recommendation implies cannot run away even for an
                // extremely shallow curve (edgeHfrRatio near 1) -- same bound the original diagnostic test pins.
                var impliedHalfSpan = (double)rec.StepSize * rec.OffsetSteps;
                Assert.That(impliedHalfSpan, Is.LessThanOrEqualTo(2.0 * sampledHalfSpan),
                    "a capped recommendation must not ask for a sweep several times wider than anything measured");
            }
        });
    }

    /// <summary>
    /// A curve with negligible curvature over the whole sampled span never reaches the 3x-min band within
    /// <c>StepSizeRecommender</c>'s bounded outward search, so <c>FindHalfWidth</c> returns NaN on both sides
    /// (or the fit itself fails to solve a meaningful curvature term) and <c>Recommend</c> falls back to the
    /// degenerate path. Either way the result must still be a legal step (&gt;= 1, not NaN, not zero) -- a
    /// "successful but uninformative" fit must not be able to produce an illegal recommendation just because it
    /// solved.
    /// </summary>
    [Test]
    public void Recommend_NearFlatCurve_StillReturnsALegalStepSize() {
        const int stepSize = 300;
        const int offsetSteps = 4;
        var fit = FitTruthSweep(hfrMin: 4.0, kappa: 1e-9, centre: P0, stepSize, offsetSteps);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: stepSize);

        Assert.Multiple(() => {
            Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo(1));
            Assert.That(rec.OffsetSteps, Is.EqualTo(4));
        });
    }

    /// <summary>
    /// The fixed-point claim (design doc "V1. The convergence driver", assertions A3/A4): sweeping AT the step
    /// size <see cref="StepSizeRecommender.Recommend"/> just recommended must recommend that SAME step again.
    /// Proven here by iterating <see cref="ConvergeStepSize"/> from two very different starting points -- 0.25x
    /// and 4x the theoretical step* = sqrt(8)*hfrMin/(kappa*PointsPerSide) -- and requiring both to land on the
    /// identical step. Landing on the same answer from opposite directions is what makes it a genuine attractor
    /// rather than a coincidence of wherever the iteration happened to stop; a 0.25x start additionally exercises
    /// <see cref="StepSizeRecommender.WasCapped"/> firing on the early (too-narrow) rounds, since the design
    /// doc's S1 scenario is exactly this start.
    ///
    /// <para>Rows span the bank's actual regimes by <c>hfrMin</c> (a short-focal-length oversampled rig, a
    /// mid-range rig, and a long obstructed rig), with <c>kappa</c> chosen so step* lands at a plausible focuser
    /// step count for each. The ratio step_behavioral/step_theory is recorded (not asserted at 1): F18
    /// (docs/followups.md) already records that <c>StepSizeRecommender</c> sizes purely from curve geometry with
    /// no detectability term, so its fixed point legitimately differs from the theoretical step* on real data;
    /// asserting equality here would just re-report that known gap as a new failure. On this synthetic
    /// noise-free curve there is no detectability effect to create such a gap, so the ratio is expected to land
    /// close to 1 -- but the assertion below only pins that it is finite and positive, matching the
    /// synth-validate harness's A3, which is exactly this tolerant.</para>
    /// </summary>
    [TestCase(0.7, 0.0100, "short-FL oversampled")]
    [TestCase(2.1, 0.0060, "mid-FL")]
    [TestCase(5.6, 0.0036, "long obstructed")]
    public void Recommend_IteratedFromEitherDirection_ConvergesToTheSameFixedPoint(double hfrMin, double kappa, string regime) {
        var stepTheory = Math.Sqrt(8.0) * hfrMin / (kappa * PointsPerSide);
        var startLow = Math.Max(1, (int)Math.Round(0.25 * stepTheory, MidpointRounding.AwayFromZero));
        var startHigh = (int)Math.Round(4.0 * stepTheory, MidpointRounding.AwayFromZero);

        var (fixedFromLow, roundsFromLow) = ConvergeStepSize(hfrMin, kappa, startLow);
        var (fixedFromHigh, roundsFromHigh) = ConvergeStepSize(hfrMin, kappa, startHigh);

        // Stability: feeding the fixed point back in as its OWN starting step must return itself in round 1 --
        // the defining property of a fixed point, not merely "the loop happened to stop".
        var (fixedFromSelf, roundsFromSelf) = ConvergeStepSize(hfrMin, kappa, fixedFromLow);

        var ratio = fixedFromLow / stepTheory;
        TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "[{0}] hfrMin={1:F2}px kappa={2:F4}px/step step_theory={3:F1} | 0.25x start {4}->{5} in {6} rounds | " +
            "4x start {7}->{8} in {9} rounds | self-check {10} in {11} round(s) | step_behavioral/step_theory={12:F3}",
            regime, hfrMin, kappa, stepTheory, startLow, fixedFromLow, roundsFromLow, startHigh, fixedFromHigh,
            roundsFromHigh, fixedFromSelf, roundsFromSelf, ratio));

        Assert.Multiple(() => {
            Assert.That(fixedFromHigh, Is.EqualTo(fixedFromLow),
                "a genuine attractor: the 0.25x and 4x starts must converge to the SAME step, not merely both stop changing");
            Assert.That(fixedFromSelf, Is.EqualTo(fixedFromLow),
                "the fixed point must be stable under its own feedback: sweeping AT the recommended step must recommend itself again");
            Assert.That(roundsFromSelf, Is.EqualTo(0), "starting exactly at the fixed point should need zero further rounds");
            // F18: not asserted against 1 -- see the method doc. Only pin that it is a sane, finite, positive number.
            Assert.That(double.IsFinite(ratio) && ratio > 0.0, Is.True,
                $"step_behavioral/step_theory must be a finite positive ratio; got {ratio}");
        });
    }

    [Test]
    public void Recommend_KnownHalfWidth_GivesAboutWidthOver3Point5() {
        var fit = FitCleanHyperbola();
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100);

        var expectedStep = (int)Math.Round(ExpectedHalfWidth / 3.5); // ~10
        Assert.Multiple(() => {
            Assert.That(rec.HalfWidth, Is.EqualTo(ExpectedHalfWidth).Within(2.0), "modeled 3x-min-HFR half-width");
            Assert.That(rec.StepSize, Is.EqualTo(expectedStep).Within(1), "step size ~= round(W / 3.5) so the sweep has ~3-4 points/side");
            Assert.That(rec.OffsetSteps, Is.EqualTo(4));
        });
    }

    [Test]
    public void Recommend_ClampsToFocuserMaxStep() {
        var fit = FitCleanHyperbola();
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100, focuserMaxStep: 2);
        Assert.That(rec.StepSize, Is.LessThanOrEqualTo(2), "must clamp to the focuser max step");
        Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo(1), "step size never below 1");
    }

    [Test]
    public void Recommend_StepSizeNeverBelowOne() {
        var fit = FitCleanHyperbola();
        // Even with a focuserMaxStep of 0/negative the recommender must keep a legal >= 1 step.
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100, focuserMaxStep: 0);
        Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo(1));
    }

    /// <summary>
    /// Pins the degenerate-fit contract documented on <see cref="StepSizeRecommender.Recommend"/>: a null fit
    /// (one of its three degenerate triggers, alongside a non-finite minimum position or a non-positive minimum
    /// HFR) returns <paramref name="currentStepSize"/> unchanged, <c>HalfWidth</c> NaN, and -- pinned here so a
    /// future refactor cannot silently start reporting a cap that never happened -- <c>WasCapped</c> false.
    /// <c>WasCapped</c> means "the model was trusted past the sampled data"; a degenerate fit was not trusted at
    /// all, so it must never read as capped.
    /// </summary>
    [Test]
    public void Recommend_NullFit_ReturnsCurrentStepUnchanged() {
        var rec = StepSizeRecommender.Recommend(null, currentStepSize: 42);
        Assert.Multiple(() => {
            Assert.That(rec.StepSize, Is.EqualTo(42), "degenerate (null) fit => keep current step");
            Assert.That(double.IsNaN(rec.HalfWidth), Is.True);
            Assert.That(rec.WasCapped, Is.False, "a degenerate fit trusted no model at all, so it cannot have been 'capped'");
            Assert.That(rec.OffsetSteps, Is.EqualTo(4), "even the degenerate fallback reports the fixed default offset steps");
        });
    }

    /// <summary>
    /// The degenerate path clamps <paramref name="currentStepSize"/> through the exact same
    /// <c>ClampStep</c>/<c>focuserMaxStep</c> logic as the main path -- there is no separate "degenerate steps
    /// don't get clamped" carve-out. Pinned separately from <see cref="Recommend_ClampsToFocuserMaxStep"/>
    /// (which clamps a real fit) because the degenerate branch is a distinct code path in
    /// <c>StepSizeRecommender</c> and nothing else exercises its clamp.
    /// </summary>
    [Test]
    public void Recommend_DegenerateFit_AlsoClampsToFocuserMaxStep() {
        var rec = StepSizeRecommender.Recommend(null, currentStepSize: 5000, focuserMaxStep: 200);
        Assert.Multiple(() => {
            Assert.That(rec.StepSize, Is.EqualTo(200), "the degenerate fallback clamps currentStepSize just like the main path");
            Assert.That(double.IsNaN(rec.HalfWidth), Is.True);
            Assert.That(rec.WasCapped, Is.False);
        });
    }

    // ---- F18: the detectability bound -----------------------------------------------------------------------
    //
    // The reproduced rig is F18's own: AutoFocus_20260802_122354, 3800 mm, in-focus HFR 5.96 px, step 1770, 4
    // offset steps + 1 focus-recovery step per side. Its measured star counts by distance from focus were
    // 1770 -> 10, 3540 -> 10, 5310 -> 6 and 3, 7080 -> 1 and 1, 8850 -> 0. The fitted 3x band put the half-width
    // at 6221 steps while the outermost position still yielding NHard = 3 stars was 5310: the band is ~15% past
    // what the rig can measure, and the recovery step reaches 8850, where nothing is detectable at all.

    private const int F18Step = 1770;
    private const double F18HfrMin = 5.96;

    // kappa chosen so the fitted 3x band lands at F18's measured 6221 steps: W_3x = sqrt(8)*hfrMin/kappa.
    private const double F18Kappa = 2.828427124746190 * F18HfrMin / 6221.0;

    private static AlglibHyperbolicFitting FitF18Sweep()
        => FitTruthSweep(F18HfrMin, F18Kappa, P0, F18Step, offsetSteps: 4);

    /// <summary>The 9 sweep positions of <see cref="FitF18Sweep"/>, ascending — parallel to any counts array below.</summary>
    private static int[] F18Positions()
        => Enumerable.Range(-4, 9).Select(i => P0 + i * F18Step).ToArray();

    private static SweepDetectability Detectability(int[] counts, bool[] isRecovery = null, int recoveryStepsPerSide = 0)
        => new SweepDetectability {
            FrameStarCounts = counts,
            FrameFocuserPositions = F18Positions(),
            FrameIsRecovery = isRecovery,
            HardFloorStarCount = 3,
            RecoveryStepsPerSide = recoveryStepsPerSide
        };

    [Test]
    public void Recommend_DetectabilityBound_TightensToTheOutermostFrameThatStillMeasuredStars() {
        var fit = FitF18Sweep();
        var unbounded = StepSizeRecommender.Recommend(fit, F18Step);
        // 1, 3, 10, 10, 20, 10, 10, 6, 1 — F18's own counts. NHard = 3 is last cleared at +/-5310.
        var rec = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(new[] { 1, 3, 10, 10, 20, 10, 10, 6, 1 }));

        Assert.Multiple(() => {
            Assert.That(unbounded.HalfWidth, Is.EqualTo(6221).Within(60), "the fitted 3x band, as F18 measured it");
            Assert.That(rec.WasDetectBounded, Is.True, "detectability, not geometry, must set the half-width here");
            Assert.That(rec.MaxUsefulHalfSpan, Is.EqualTo(3.0 * F18Step).Within(1e-6), "5310 = the outermost frame clearing NHard");
            Assert.That(rec.HalfWidth, Is.EqualTo(3.0 * F18Step).Within(1e-6));
            Assert.That(rec.StepSize, Is.LessThan(unbounded.StepSize), "the sweep must narrow to what the rig can see");
            Assert.That(rec.StepSize * rec.OffsetSteps, Is.LessThanOrEqualTo(6221),
                "the OFFSET sweep now stays inside the fitted band; bounding the recovery step too is the separate "
                + "sizeForExecutedSweep arm, deliberately");
        });
    }

    [Test]
    public void Recommend_EveryFrameCleared_TheLimitWasNeverOBSERVED_SoThereIsNoBound() {
        // The bound is bounded ABOVE by the sampled half-span by construction — it is the outermost SAMPLED
        // position. If no frame failed the floor, the sweep never reached the detectability limit, and reporting
        // its own edge would turn min(W_3x, W_detect) into "never recommend a sweep wider than the one you just
        // took" on every healthy run: a cap on WIDENING, which is a different rule the recommender already has.
        // An unobserved limit is ABSENT. (Caught by the pre-registered instrument check: W_detect came back equal
        // to the sampled half-span on all 20 bank datasets, which is the signature of exactly this mistake.)
        var fit = FitF18Sweep();
        var unbounded = StepSizeRecommender.Recommend(fit, F18Step);
        var rec = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(new[] { 40, 40, 40, 40, 40, 40, 40, 40, 40 }));

        Assert.Multiple(() => {
            Assert.That(rec.WasDetectBounded, Is.False);
            Assert.That(double.IsNaN(rec.MaxUsefulHalfSpan), Is.True,
                "no frame was starved, so no detectability limit was observed — the bound must be absent, not the sweep edge");
            Assert.That(rec.StepSize, Is.EqualTo(unbounded.StepSize));
            Assert.That(rec.HalfWidth, Is.EqualTo(unbounded.HalfWidth).Within(1e-9));
        });
    }

    [Test]
    public void Recommend_OneStarvedFrame_IsWhatTurnsTheBoundOn() {
        // The discriminator for the rule above: the ONLY difference here is that the outermost pair fell below
        // NHard. That single observation is what makes the limit real, and the bound then reports the outermost
        // frame that still cleared it.
        var fit = FitF18Sweep();
        var rec = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(new[] { 2, 40, 40, 40, 40, 40, 40, 40, 2 }));

        Assert.Multiple(() => {
            Assert.That(rec.MaxUsefulHalfSpan, Is.EqualTo(3.0 * F18Step).Within(1e-6));
            Assert.That(rec.WasDetectBounded, Is.True);
        });
    }

    [Test]
    public void Recommend_TooFewQualifyingFrames_LeavesTheRecommendationUnbounded() {
        // A starless run has not shown that nothing is detectable — only that this sweep detected nothing. Below
        // the frame floor the answer is "unmeasurable" (no bound), never "zero" (collapse the sweep).
        var fit = FitF18Sweep();
        var unbounded = StepSizeRecommender.Recommend(fit, F18Step);
        var rec = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(new[] { 0, 0, 0, 2, 20, 2, 0, 0, 0 })); // only the vertex clears NHard

        Assert.Multiple(() => {
            Assert.That(rec.WasDetectBounded, Is.False);
            Assert.That(double.IsNaN(rec.MaxUsefulHalfSpan), Is.True, "unmeasurable, and it must say so rather than report 0");
            Assert.That(rec.StepSize, Is.EqualTo(unbounded.StepSize));
        });
    }

    [Test]
    public void Recommend_OnlyTheInnermostFramesDetect_TheFloorBoundsHowFarOneRunMayShrink() {
        // Exactly MinFramesForDetectHalfWidth frames qualify and they are all within one step of focus, so the raw
        // detectability half-width is 1770. The floor (0.5 x the sampled half-span = 3540) keeps one run from
        // collapsing the sweep; the next, narrower sweep then measures a better-grounded bound.
        var fit = FitF18Sweep();
        var rec = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(new[] { 0, 0, 0, 10, 20, 10, 0, 0, 0 }));

        var sampledHalfSpan = 4.0 * F18Step;
        Assert.Multiple(() => {
            Assert.That(rec.MaxUsefulHalfSpan, Is.EqualTo(1.0 * F18Step).Within(1e-6), "the raw measurement is reported unfloored");
            Assert.That(rec.HalfWidth, Is.EqualTo(StepSizeRecommender.MinHalfWidthSampledHalfSpanMultiple * sampledHalfSpan).Within(1e-6));
            Assert.That(rec.WasDetectBounded, Is.True);
            Assert.That(rec.StepSize, Is.GreaterThanOrEqualTo((int)(0.55 * F18Step)),
                "one run may at most roughly halve the step — the mirror of the 1.5x widening cap");
        });
    }

    [Test]
    public void Recommend_RecoveryFramesDoNotVoteOnTheDetectableRange() {
        // Recovery frames are DELIBERATELY far from focus and are exempt from the objective's own hard floor
        // (JRun's FrameIsRecovery exemption), so letting them widen the ordinary sweep would defeat the bound.
        // The |k|=3 pair is starved (so a limit IS observed either way), while the |k|=4 pair — the recovery
        // wing — still carries stars. Counting the wing puts the limit at 7080; excluding it puts the limit at
        // 3540, the outermost ORDINARY frame that cleared the floor.
        var fit = FitF18Sweep();
        var counts = new[] { 10, 2, 10, 10, 20, 10, 10, 2, 10 };
        var outerAreRecovery = new[] { true, false, false, false, false, false, false, false, true };

        var withRecoveryCounted = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(counts));
        var withRecoveryExcluded = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: Detectability(counts, isRecovery: outerAreRecovery));

        Assert.Multiple(() => {
            Assert.That(withRecoveryCounted.MaxUsefulHalfSpan, Is.EqualTo(4.0 * F18Step).Within(1e-6));
            Assert.That(withRecoveryExcluded.MaxUsefulHalfSpan, Is.EqualTo(2.0 * F18Step).Within(1e-6));
        });
    }

    [Test]
    public void Recommend_WithoutDetectability_IsIdenticalToThePreF18Result() {
        // The absent-input contract: one binary, both arms (F41). Null detectability, and a detectability whose
        // lists are null, must both leave the recommendation exactly where it was.
        var fit = FitF18Sweep();
        var baseline = StepSizeRecommender.Recommend(fit, F18Step);
        var emptyDetectability = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: new SweepDetectability { RecoveryStepsPerSide = 1 });

        Assert.Multiple(() => {
            Assert.That(emptyDetectability.StepSize, Is.EqualTo(baseline.StepSize));
            Assert.That(emptyDetectability.HalfWidth, Is.EqualTo(baseline.HalfWidth).Within(1e-9));
            Assert.That(emptyDetectability.WasDetectBounded, Is.False);
            Assert.That(double.IsNaN(emptyDetectability.MaxUsefulHalfSpan), Is.True);
        });
    }

    [Test]
    public void Recommend_SizeForExecutedSweep_IsIndependentOfWhetherDetectabilityWasMeasurable() {
        // The two behaviours are separately switchable on purpose. The executed-sweep divisor is about how many
        // points the run VISITS, which is known even when nothing about detectability is: a run whose counts were
        // never populated still takes its recovery step.
        var fit = FitF18Sweep();
        var baseline = StepSizeRecommender.Recommend(fit, F18Step);
        var executedOnly = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null,
            detectability: new SweepDetectability { RecoveryStepsPerSide = 1 }, sizeForExecutedSweep: true);

        Assert.Multiple(() => {
            Assert.That(executedOnly.WasDetectBounded, Is.False, "no counts were supplied, so nothing bounded the half-width");
            Assert.That(executedOnly.HalfWidth, Is.EqualTo(baseline.HalfWidth).Within(1e-9));
            Assert.That(executedOnly.StepSize, Is.LessThan(baseline.StepSize), "but the divisor still accounts for the recovery step");
        });
    }

    [Test]
    public void Recommend_SizeForExecutedSweep_AccountsForTheRecoveryStepsAndNothingElse() {
        // Today the step is sized for 4 offset points per side, so the outermost lands at 4/3.5 = 1.14x the
        // half-width — but the run also takes a recovery step, reaching 5/3.5 = 1.43x: F18's 43% over-reach.
        // Sizing for the executed sweep holds the OUTERMOST EXECUTED point at that same 1.14x instead.
        var fit = FitF18Sweep();
        var detect = Detectability(new[] { 1, 3, 10, 10, 20, 10, 10, 6, 1 }, recoveryStepsPerSide: 1);

        var offsetOnly = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null, detectability: detect);
        var executed = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null, detectability: detect,
            sizeForExecutedSweep: true);

        Assert.Multiple(() => {
            Assert.That(offsetOnly.HalfWidth, Is.EqualTo(executed.HalfWidth).Within(1e-9),
                "the half-width is the same measurement either way; only the divisor moves");
            Assert.That(executed.StepSize, Is.EqualTo((int)Math.Round(executed.HalfWidth / (5 * 3.5 / 4.0), MidpointRounding.AwayFromZero)));
            // The invariant that matters: the OUTERMOST EXECUTED point (4 offset + 1 recovery) lands at the same
            // ~1.14x of the half-width today's outermost OFFSET point does, instead of 1.43x. Stated with one
            // step-unit of slack because StepSize is an integer.
            Assert.That(executed.StepSize * 5, Is.LessThanOrEqualTo(1.15 * executed.HalfWidth),
                "the executed sweep now reaches no further than the offset-only sweep used to");
            Assert.That(offsetOnly.StepSize * 5, Is.GreaterThan(1.35 * offsetOnly.HalfWidth),
                "...which is exactly what it does NOT do without this flag — F18's 43% over-reach");
        });
    }

    [Test]
    public void Recommend_SizeForExecutedSweep_WithNoRecoverySteps_IsIdenticalToToday() {
        var fit = FitF18Sweep();
        var detect = Detectability(new[] { 1, 3, 10, 10, 20, 10, 10, 6, 1 }, recoveryStepsPerSide: 0);
        var a = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null, detectability: detect);
        var b = StepSizeRecommender.Recommend(fit, F18Step, focuserMaxStep: null, detectability: detect,
            sizeForExecutedSweep: true);
        Assert.That(b.StepSize, Is.EqualTo(a.StepSize));
    }

    // ---- F49(c) / F51: what a CAPPED recommendation is able to say about itself -----------------------------

    /// <summary>
    /// The growth ratio is <c>MaxHalfWidthSampledHalfSpanMultiple · P / PointsPerSide</c> and NOTHING else — in
    /// particular it does not depend on the extrapolated half-width, which is the whole reason it is quotable to
    /// a user while a projected run count is not.
    ///
    /// <para>Both rows are load-bearing and neither is hypothetical. P = 4 is the shipped offset-only sweep and
    /// gives 1.714; wave 7's F18 control arm has 22 capped rounds across six datasets on disk and every one lands
    /// at 1.667–1.750, the scatter being integer rounding of the step. P = 5 is 4 offset + 1 focus-recovery step
    /// and gives 2.143 — the geometry of the field session behind F49/F51, whose 11-point sweeps went
    /// 100 → 214 → 459. A single hard-coded constant would have been wrong on one of the two.</para>
    /// </summary>
    [TestCase(4, 1.5 * 4 / 3.5)]
    [TestCase(5, 1.5 * 5 / 3.5)]
    public void Recommend_Capped_ReportsTheExactRatioTheNextRunWillApply(int pointsPerSide, double expectedRatio) {
        const int stepSize = 214;
        var fit = FitShallowHyperbola(minHfr: 1.9, edgeHfrRatio: 1.8, stepSize: stepSize, offsetSteps: pointsPerSide);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: stepSize);

        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.True, "the premise: a sweep reaching only 1.8x the minimum HFR is capped");
            Assert.That(rec.CappedGrowthRatio, Is.EqualTo(expectedRatio).Within(1e-9),
                "the ratio is algorithm arithmetic, not a property of the fit");
            // And it PREDICTS: the step the recommender returned is the ratio applied to the current step.
            Assert.That(rec.StepSize, Is.EqualTo((int)Math.Round(stepSize * expectedRatio, MidpointRounding.AwayFromZero)).Within(1),
                "the ratio must actually describe the move this recommendation just made");
        });
    }

    /// <summary>
    /// The field session behind F49/F51, replayed: 100 → 214 → 459 at 4 offset + 1 recovery step per side. The
    /// entry derives 459 independently from the screenshot's focuser axis (11 points at spacing 214 ⇒ sampled
    /// half-span 1070; ×1.5 ⇒ 1605; /3.5 ⇒ 458.6), so this is a check against a number the recommender did not
    /// produce here.
    ///
    /// <para>It also pins the half of the user's complaint that is NOT a defect: the sequence TERMINATES. The
    /// user saw four runs each asking to widen and read it as a runaway; it is a geometric approach to the point
    /// where the sweep contains the 3x band, and their runs 3 and 4 asked +3 % and +5 %.</para>
    /// </summary>
    [Test]
    public void Recommend_TheFieldSessionSequence_ReproducesAndTerminates() {
        const double hfrMin = 1.9;
        const double kappa = 0.0026;   // ~3.5 px at the +/-1070 edge of the session's second sweep
        const int pointsPerSide = 5;   // 4 offset + 1 focus recovery, i.e. the session's 11-point sweeps

        var step = 100;
        var seen = new List<int> { step };
        var cappedRounds = 0;
        var roundTheCapReleased = -1;
        for (var round = 0; round < 12; round++) {
            var fit = FitTruthSweep(hfrMin, kappa, P0, step, pointsPerSide);
            var rec = StepSizeRecommender.Recommend(fit, step);
            if (rec.WasCapped) {
                cappedRounds++;
                Assert.That(rec.CappedGrowthRatio, Is.EqualTo(1.5 * pointsPerSide / 3.5).Within(1e-9),
                    "every capped round reports the same exact ratio");
            } else if (roundTheCapReleased < 0) {
                roundTheCapReleased = round;
            }
            if (rec.StepSize == step) {
                break;
            }
            step = rec.StepSize;
            seen.Add(step);
        }
        TestContext.WriteLine($"sequence: {string.Join(" -> ", seen)}  (cap released at round {roundTheCapReleased})");

        Assert.Multiple(() => {
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(3), "the session took at least three widening rounds");
            Assert.That(seen[1], Is.EqualTo(214).Within(2), "100 x 2.143 = 214.3 -- the session's first recommendation");
            Assert.That(seen[2], Is.EqualTo(459).Within(3), "214 x 2.143 = 458.6 -- the session's second");
            Assert.That(cappedRounds, Is.GreaterThan(0), "the widening rounds are the CAPPED ones");
            // TERMINATION is "the cap stops binding", not "two consecutive integers are equal". Asserting the
            // latter would make the test hostage to a +/-1 step oscillation from integer rounding near the fixed
            // point -- which is not the behaviour the user complained about and not what the copy claims.
            Assert.That(roundTheCapReleased, Is.InRange(1, 5),
                "the geometric widening ENDS, and within a handful of runs -- that is the half of the user's " +
                "'runaway' that is not a defect, and the half the copy is now allowed to promise");
        });
    }

    /// <summary>
    /// <see cref="StepSizeRecommendation.SampledHfrRange"/> is what the sweep MEASURED, so unlike the half-width
    /// it involves no extrapolation — which is why the copy leads with it. Paired with the uncapped case so the
    /// test says which side of <see cref="StepSizeRecommender.HfrThresholdMultiple"/> each lands on.
    /// </summary>
    [TestCase(1.8, false)]
    [TestCase(3.6, true)]
    public void Recommend_ReportsTheHfrRangeTheSweepActuallyMeasured(double edgeHfrRatio, bool expectReached) {
        var fit = FitShallowHyperbola(minHfr: 1.9, edgeHfrRatio: edgeHfrRatio, stepSize: 214, offsetSteps: 4);

        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 214);

        Assert.Multiple(() => {
            Assert.That(rec.SampledHfrRange, Is.EqualTo(edgeHfrRatio).Within(0.05),
                "max(HFR)/min(HFR) over the fitted points");
            Assert.That(rec.ReachedHfrBand, Is.EqualTo(expectReached));
            // The two agree: a sweep that did not reach the band is exactly one whose half-width was capped.
            Assert.That(rec.WasCapped, Is.EqualTo(!expectReached),
                "SampledHfrRange >= 3 and WasCapped must not disagree about whether the band was sampled");
        });
    }

    /// <summary>
    /// An UNCAPPED recommendation has no growth ratio, because there is no next capped run for one to describe.
    /// NaN rather than 1.0: "this is not widening geometrically" and "it widens by a factor of one" would be the
    /// same number, and the caller turns one of them into a sentence.
    /// </summary>
    [Test]
    public void Recommend_NotCapped_HasNoGrowthRatioAtAll() {
        var fit = FitCleanHyperbola();
        var rec = StepSizeRecommender.Recommend(fit, currentStepSize: 100);
        Assert.Multiple(() => {
            Assert.That(rec.WasCapped, Is.False, "the premise");
            Assert.That(double.IsNaN(rec.CappedGrowthRatio), Is.True, "NaN, never 1.0");
        });
    }

    /// <summary>
    /// A degenerate recommendation reports NEITHER quantity. It never fitted anything, so both "what did this
    /// sweep measure" and "what will the next run multiply by" are unanswerable rather than zero — the same
    /// NaN-never-0 rule <see cref="StepSizeRecommendation.MaxUsefulHalfSpan"/> already follows.
    /// </summary>
    [Test]
    public void Recommend_DegenerateFit_ReportsNeitherMeasurement() {
        var rec = StepSizeRecommender.Recommend(null, currentStepSize: 77);
        Assert.Multiple(() => {
            Assert.That(rec.StepSize, Is.EqualTo(77), "unchanged, as before");
            Assert.That(double.IsNaN(rec.SampledHfrRange), Is.True);
            Assert.That(double.IsNaN(rec.CappedGrowthRatio), Is.True);
            Assert.That(rec.ReachedHfrBand, Is.False, "an unmeasurable range is not a reached band");
        });
    }
}
