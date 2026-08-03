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
}
