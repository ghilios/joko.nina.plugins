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
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class ExposureRecommenderTests {

    private static ObjectiveConstants DefaultConstants() => new ObjectiveConstants(); // NTarget = 20

    private static IReadOnlyList<double> Frame(params double[] values) => values;

    /// <summary>
    /// A frame carrying <paramref name="nTarget"/> stars, all at <paramref name="snr"/> — i.e. a frame that MEETS
    /// the star-count target, so the verdict turns on signal alone. Use this, not <see cref="Frame(double[])"/>,
    /// whenever a test means "the S/N already meets the target": a one-star <c>Frame(12.0)</c> is SHORT at
    /// NTarget=20, which is now <see cref="ExposureRecommendation.StarCountIsTheLimit"/> rather than
    /// <see cref="ExposureRecommendation.ExposureIsNotTheLimit"/>. Every star is identical, so the NTarget-th
    /// brightest is exactly <paramref name="snr"/> and the measured value is unchanged from the one-star fixture.
    /// </summary>
    private static IReadOnlyList<double> FullFrame(double snr, int nTarget = 20) =>
        Enumerable.Repeat(snr, nTarget).ToArray();

    private static RunEvaluationMetrics BuildMetrics(
            IReadOnlyList<IReadOnlyList<double>> frameStarSnrs, IReadOnlyList<bool> frameIsRecovery = null,
            int gateRejectionsPerFrame = 0, int flatRejectionsPerFrame = 0) {
        return new RunEvaluationMetrics {
            FrameStarSnrs = frameStarSnrs,
            FrameIsRecovery = frameIsRecovery,
            // Uniform per-frame tallies keep the fixtures readable; only the totals matter to the recommender.
            FrameLowSensitivityCounts = Enumerable.Repeat(gateRejectionsPerFrame, frameStarSnrs?.Count ?? 0).ToArray(),
            FrameTooFlatCounts = Enumerable.Repeat(flatRejectionsPerFrame, frameStarSnrs?.Count ?? 0).ToArray()
        };
    }

    // ── SensitivityIsAtFloor ────────────────────────────────────────────────────────────────────────────────

    [TestCase(0.0, true)]
    [TestCase(0.125, true)] // StarDetectionOptimizer's StepFloorFraction (0.125) landing point -- the case an == 0 test would miss
    [TestCase(0.5, true)]
    [TestCase(1.0, true)]   // the threshold itself is inclusive
    [TestCase(1.0001, false)]
    [TestCase(2.0, false)]  // a legitimately hand-tuned gate of 2 must NOT be flagged
    [TestCase(10.0, false)]
    public void SensitivityIsAtFloor_SweepsTheSearchFloorBand(double sensitivity, bool expected) {
        Assert.That(ExposureRecommender.SensitivityIsAtFloor(sensitivity), Is.EqualTo(expected));
    }

    [Test]
    public void TargetSensitivity_MatchesTheShippedDefault() {
        var defaultParams = HocusFocusStarDetection.BuildDefaultStarDetectorParams();
        Assert.That(ExposureRecommender.TargetSensitivity, Is.EqualTo(defaultParams.Sensitivity),
            "the recommendation targets the SAME gate value the shipped default would admit");
    }

    // ── Per-frame Nth-brightest ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Recommend_TakesTheNTargetThBrightestPerFrame() {
        // 40 distinct SNRs (1..40) on three IDENTICAL frames, so the median-across-frames step is a no-op and the
        // result is exactly the per-frame order statistic. Descending rank (NTarget - 1 = 19, 0-indexed, i.e. the
        // 20th brightest) is ascending index (k - nTarget = 40 - 20 = 20), i.e. value 21 (ascending array is
        // 1..40, so index 20 holds value 21).
        var frame = Frame(Range(1, 40));
        var metrics = BuildMetrics(new[] { frame, frame, frame });
        var c = new ObjectiveConstants { NTarget = 20 };

        var rec = ExposureRecommender.Recommend(metrics, c, currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.HasRecommendation, Is.True);
            Assert.That(rec.MeasuredSnr, Is.EqualTo(21.0));
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3));
            Assert.That(rec.ShortFrameCount, Is.EqualTo(0), "40 survivors >= NTarget=20, not a short frame");
        });
    }

    [Test]
    public void Recommend_ReadsNTargetFromConstants_FallsBackWhenNTargetExceedsSurvivors() {
        // Same 40-value frame as above, but NTarget=60 now exceeds the 40 survivors on every frame, so EVERY
        // frame falls back to its faintest survivor (1.0) instead of the value from the NTarget=20 test (21.0).
        // NOTE: this alone does not prove NTarget is genuinely read (every frame lands in the SHORT branch, whose
        // result -- filtered[0] -- does not depend on nTarget's exact value at all) -- see the sibling test below
        // for a case where the index actually moves within the non-short branch.
        var frame = Frame(Range(1, 40));
        var metrics = BuildMetrics(new[] { frame, frame, frame });
        var c = new ObjectiveConstants { NTarget = 60 };

        var rec = ExposureRecommender.Recommend(metrics, c, currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(1.0));
            Assert.That(rec.ShortFrameCount, Is.EqualTo(3), "all three frames have fewer than NTarget=60 survivors");
        });
    }

    [Test]
    public void Recommend_ReadsNTargetFromConstants_QuantileIndexActuallyMoves() {
        // Same 40-value (1..40) frame, but NTarget=10 (< 40 survivors), so this stays in the NON-short branch and
        // the selected rank genuinely moves with NTarget: ascending index (k - nTarget) = 40 - 10 = 30, value 31
        // -- different from both the NTarget=20 test (21.0) and the NTarget=60 fallback test (1.0). If NTarget
        // were hard-coded to 20 inside the implementation, this would incorrectly report 21.0 instead of 31.0.
        var frame = Frame(Range(1, 40));
        var metrics = BuildMetrics(new[] { frame, frame, frame });
        var c = new ObjectiveConstants { NTarget = 10 };

        var rec = ExposureRecommender.Recommend(metrics, c, currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(31.0));
            Assert.That(rec.ShortFrameCount, Is.EqualTo(0), "40 survivors >= NTarget=10, not a short frame");
        });
    }

    [Test]
    public void Recommend_ShortFrame_UsesItsFaintestAcceptedStarAndIncrementsShortFrameCount() {
        // Only 5 survivors per frame, far below the default NTarget=20, on three identical frames: the per-frame
        // value must be the MINIMUM (10.0), not e.g. the max or a mid-value, and every frame counts as short.
        var frame = Frame(50.0, 40.0, 30.0, 20.0, 10.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(10.0), "the faintest of the frame's survivors");
            Assert.That(rec.ShortFrameCount, Is.EqualTo(3));
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void Recommend_DropsNaN_PositiveInfinity_Zero_AndNegative() {
        // Only 15.0 and 25.0 are real: NaN, +Infinity, 0.0, and -5.0 must all be filtered before the frame is
        // reduced. If any of them leaked through, Sort()/the minimum computation would either throw (NaN/Infinity
        // compare inconsistently) or select a bogus value instead of the true faintest real survivor (15.0).
        var frame = Frame(double.NaN, double.PositiveInfinity, 0.0, -5.0, 15.0, 25.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.HasRecommendation, Is.True);
            Assert.That(rec.MeasuredSnr, Is.EqualTo(15.0));
            Assert.That(rec.ShortFrameCount, Is.EqualTo(3), "only 2 real survivors, well below NTarget=20");
        });
    }

    [Test]
    public void Recommend_EmptyFrameEntry_IsTreatedAsNoDataForThatFrame_NotAsZeroStars() {
        // The RunEvaluationLoader convention (see the FrameStarSnrs doc comment in OptimizationObjective.cs): a
        // producer that never populated a frame's per-star SNRs leaves that frame's entry EMPTY, which is NOT
        // proof the frame had zero accepted stars. An empty entry must contribute nothing -- it must NOT be
        // counted toward UsableFrameCount/ShortFrameCount, and it must not act like a "0" that would drag the
        // median down (if it were misread as a zero-star frame, a naive implementation might either count it as
        // a fourth (very weak) usable frame or otherwise skew MeasuredSnr away from 6.0).
        var metrics = BuildMetrics(new[] { Frame(5.0), Frame(6.0), Frame(7.0), Frame(/* empty: no data for this frame */) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3), "the empty-data frame must not be counted as usable");
            Assert.That(rec.ShortFrameCount, Is.EqualTo(3), "only the 3 real frames are short; the empty one is neither");
            Assert.That(rec.MeasuredSnr, Is.EqualTo(6.0), "median of {5, 6, 7} -- the empty frame contributes nothing");
        });
    }

    [Test]
    public void Recommend_ExactlyNTargetSurvivors_TakesTheNonShortBranch() {
        // k == nTarget EXACTLY: this is the boundary between the ">= nTarget" (non-short) and short branches. Both
        // branches happen to return the SAME VALUE here (filtered[0], the minimum of the 20 values) -- a `>` vs
        // `>=` mutation at the branch condition would be invisible to a value-only assertion. ShortFrameCount is
        // the ONLY observable signal: the correct `>=` takes the non-short branch (wasShort stays false), while a
        // buggy `>` would incorrectly fall through to the short branch.
        var frame = Frame(Range(1, 20)); // exactly 20 survivors
        var metrics = BuildMetrics(new[] { frame, frame, frame });
        var c = new ObjectiveConstants { NTarget = 20 };

        var rec = ExposureRecommender.Recommend(metrics, c, currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(1.0), "the minimum of the 20 values, from either branch's arithmetic");
            Assert.That(rec.ShortFrameCount, Is.EqualTo(0), "k == nTarget must take the non-short (>=) branch");
        });
    }

    [Test]
    public void Recommend_NTargetOfOne_ReturnsTheBrightestSurvivor() {
        // NTarget = 1: the "1st brightest" is simply the maximum of the frame's survivors.
        var frame = Frame(5.0, 3.0, 9.0, 1.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame });
        var c = new ObjectiveConstants { NTarget = 1 };

        var rec = ExposureRecommender.Recommend(metrics, c, currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(9.0), "NTarget=1 must select the brightest survivor, not the faintest");
            Assert.That(rec.ShortFrameCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Recommend_MixedShortAndNonShortFrames_ShortFrameCountIsAPartialCount() {
        // Two NON-short frames (40 survivors each, well above NTarget=20) and one SHORT frame (5 survivors), so
        // ShortFrameCount must land on a genuine PARTIAL count (1 of 3) -- not the all-or-nothing 0 or 3 every
        // other test exercises.
        var richFrame = Frame(Range(1, 40));   // non-short: value = filtered[40-20] = 21
        var thinFrame = Frame(50.0, 40.0, 30.0, 20.0, 10.0); // short: value = min = 10
        var metrics = BuildMetrics(new[] { richFrame, richFrame, thinFrame });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.ShortFrameCount, Is.EqualTo(1), "only the thin frame is short");
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3));
            Assert.That(rec.MeasuredSnr, Is.EqualTo(21.0), "median of {21, 21, 10}");
        });
    }

    [Test]
    public void Recommend_DoesNotMutateTheCallersFrameStarSnrsArrays() {
        // FrameStarSnrs is producer-owned; Recommend must copy before sorting, never sort the caller's array
        // in place. Pass an array already in DESCENDING order and confirm it is untouched after the call -- an
        // in-place Sort() (ascending) on the original would flip it, an easy future regression to reintroduce.
        var descending = new double[] { 40.0, 30.0, 20.0, 10.0, 5.0 };
        var expectedUnchanged = (double[])descending.Clone();
        var metrics = BuildMetrics(new IReadOnlyList<double>[] { descending, Frame(6.0), Frame(7.0) });

        ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.That(descending, Is.EqualTo(expectedUnchanged), "Recommend must not sort/mutate the caller's array in place");
    }

    // ── Median across frames, not the worst frame ──────────────────────────────────────────────────────────

    [Test]
    public void Recommend_MediansAcrossFrames_RatherThanTakingTheWorstFrame() {
        // Nine single-star frames (each frame's lone value IS its per-frame quantile, since 1 < NTarget=20).
        // Sorted: {2, 9, 9, 10, 10, 11, 11, 12, 12} -> median 10. A worst-frame rule would have picked 2.
        double[] values = { 12, 11, 10, 9, 2, 9, 10, 11, 12 };
        var frames = new List<IReadOnlyList<double>>();
        foreach (var v in values) {
            frames.Add(Frame(v));
        }
        var metrics = BuildMetrics(frames);

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 5.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(10.0), "median of the per-frame values");
            Assert.That(rec.MeasuredSnr, Is.Not.EqualTo(2.0), "must not degrade to the single worst frame");

            // Concretely: the worst-frame factor (Target/2)^2 = 25 vs. the actual factor (Target/10)^2 = 1 -- a
            // worst-frame rule would have demanded a far longer exposure than the data actually supports.
            var actualFactor = Math.Pow(ExposureRecommender.TargetSensitivity / rec.MeasuredSnr, 2.0);
            var worstFrameFactor = Math.Pow(ExposureRecommender.TargetSensitivity / 2.0, 2.0);
            Assert.That(actualFactor, Is.LessThan(worstFrameFactor / 10.0));
        });
    }

    // ── Recovery-frame exclusion ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Recommend_ExcludesRecoveryFrames() {
        // Three legitimate frames (5, 6, 7) plus a fourth, heavily-defocused RECOVERY frame (100) that must be
        // excluded from both the median and the frame counts.
        var metrics = BuildMetrics(
            new[] { Frame(5.0), Frame(6.0), Frame(7.0), Frame(100.0) },
            new[] { false, false, false, true });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(6.0), "median of {5, 6, 7} only, excluding the recovery frame");
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void Recommend_NullFrameIsRecovery_TreatsEveryFrameAsNonRecovery() {
        // Identical frame data to the exclusion test above, but with FrameIsRecovery == null: now the 100.0 frame
        // MUST be included (baseline convention), moving the median from 6.0 to 6.5.
        var metrics = BuildMetrics(
            new[] { Frame(5.0), Frame(6.0), Frame(7.0), Frame(100.0) },
            frameIsRecovery: null);

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(6.5), "median of all four frames when FrameIsRecovery is null");
            Assert.That(rec.UsableFrameCount, Is.EqualTo(4));
        });
    }

    // ── Edge cases: not enough data ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void Recommend_FewerThanMinFramesForRecommendation_ReturnsNoRecommendation() {
        var metrics = BuildMetrics(new[] { Frame(5.0), Frame(6.0) }); // only 2 usable frames, floor is 3

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 5.0);

        Assert.Multiple(() => {
            Assert.That(rec.HasRecommendation, Is.False);
            Assert.That(rec.UsableFrameCount, Is.EqualTo(2), "the count is still reported for diagnostics");
            Assert.That(double.IsNaN(rec.RawSeconds), Is.True);
            Assert.That(double.IsNaN(rec.RecommendedSeconds), Is.True);
            Assert.That(double.IsNaN(rec.MeasuredSnr), Is.True);
        });
    }

    [Test]
    public void Recommend_NullFrameStarSnrs_ReturnsNoRecommendation() {
        var metrics = new RunEvaluationMetrics { FrameStarSnrs = null };

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 5.0);

        Assert.That(rec.HasRecommendation, Is.False);
    }

    [TestCase(0.0)]
    [TestCase(-5.0)]
    public void Recommend_NonPositiveCurrentExposure_ReturnsNoRecommendation(double currentExposureSeconds) {
        var metrics = BuildMetrics(new[] { Frame(5.0), Frame(6.0), Frame(7.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds);

        Assert.That(rec.HasRecommendation, Is.False);
    }

    // ── Scaling law, caps, and rounding ─────────────────────────────────────────────────────────────────────

    [Test]
    public void Recommend_ScalesAsTheSquareOfTheSnrRatio() {
        // S_now = 5 (three identical single-star frames), t_old = 5s -> raw factor (10/5)^2 = 4.0 -> RawSeconds = 20.
        var metrics = BuildMetrics(new[] { Frame(5.0), Frame(5.0), Frame(5.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 5.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(5.0));
            Assert.That(rec.RawSeconds, Is.EqualTo(20.0).Within(1e-9));
            Assert.That(rec.ExposureIsNotTheLimit, Is.False);
        });
    }

    [Test]
    public void Recommend_UncappedIncrease_PinsRecommendedSecondsEndToEnd() {
        // The happy path, pinned all the way through: S_now = 5, t_old = 1s -> raw factor (10/5)^2 = 4.0 ->
        // RawSeconds = 4.0. factorCap = 1*4 = 4.0 == RawSeconds exactly (not exceeded) and well under the 30s
        // absolute cap, so this is an ordinary UNCAPPED increase: WasCapped must be false and RecommendedSeconds
        // must be the rounded RawSeconds itself (4.0 is already on the sub-10s 0.5s grid), not merely "some
        // number >= current" -- Recommend_ScalesAsTheSquareOfTheSnrRatio (t_old=5s) only pins RawSeconds and,
        // coincidentally, lands exactly on ITS OWN factor cap, so neither test end-to-end-pins an uncapped result.
        var metrics = BuildMetrics(new[] { Frame(5.0), Frame(5.0), Frame(5.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 1.0);

        Assert.Multiple(() => {
            Assert.That(rec.RawSeconds, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(rec.WasCapped, Is.False);
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(4.0).Within(1e-9));
            Assert.That(rec.IncreasesExposure, Is.True);
            Assert.That(rec.CapLimitsRecommendation, Is.False, "nothing was capped, so the cap cannot be limiting it");
        });
    }

    [TestCase(3.01, 3.5)]   // below 10s: 0.5s granularity, rounds UP past the next half-second
    [TestCase(3.5, 3.5)]    // already exactly on the grid -- ceiling of an exact value is itself, not the next step
    [TestCase(10.0, 10.0)]  // lower band edge: exactly 10.0 uses the 1s (not 0.5s) granularity, already exact
    [TestCase(15.2, 16.0)]  // 10s-30s: 1s granularity
    [TestCase(20.0, 20.0)]  // already exact
    [TestCase(30.0, 30.0)]  // upper band edge: exactly 30.0 uses the 1s (not 5s) granularity, already exact
    [TestCase(32.1, 35.0)]  // above 30s: 5s granularity
    [TestCase(40.0, 40.0)]  // already exact
    public void RoundExposureSeconds_AlwaysRoundsUp_AcrossTheThreeLadderBands(double input, double expected) {
        Assert.That(ExposureRecommender.RoundExposureSeconds(input), Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void Recommend_NeverRecommendsShorterExposure_WhenSnrAlreadyMeetsTarget_AndSetsExposureIsNotTheLimit() {
        // S_now = 12 >= TargetSensitivity(10): the raw factor is < 1, so without the explicit floor this would
        // recommend a SHORTER exposure than the 5s the run actually used. If the never-shorter guard were removed,
        // RecommendedSeconds would round(RawSeconds) ~= 3.5s instead of staying at (or above) 5s.
        // Full frames: this test is about the never-shorter GUARD, so the frames must meet the star-count target.
        // With short frames the run is StarCountIsTheLimit instead and never reaches the guard being tested.
        var metrics = BuildMetrics(new[] { FullFrame(12.0), FullFrame(12.0), FullFrame(12.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 5.0);

        Assert.Multiple(() => {
            Assert.That(rec.ExposureIsNotTheLimit, Is.True);
            Assert.That(rec.RawSeconds, Is.LessThan(rec.CurrentSeconds), "the raw math alone would have shortened the exposure");
            Assert.That(rec.RecommendedSeconds, Is.GreaterThanOrEqualTo(rec.CurrentSeconds), "the guard must override the raw math");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(5.0), "rounds to the current exposure, already on the 0.5s grid");
        });
    }

    [Test]
    public void Recommend_CapsAtFourTimes_WhenTheFactorCapBindsBeforeTheAbsoluteCap() {
        // S_now = 1 -> raw factor (10/1)^2 = 100. t_old = 2s -> factor cap = 8s (well under the 30s absolute cap),
        // so the 4x cap must be the binding one.
        var metrics = BuildMetrics(new[] { Frame(1.0), Frame(1.0), Frame(1.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(rec.RawSeconds, Is.EqualTo(200.0).Within(1e-9), "RawSeconds is reported honestly even though capped");
            Assert.That(rec.WasCapped, Is.True);
            Assert.That(rec.CappedByAbsoluteLimit, Is.False, "the 4x factor cap (8s) bound before the 30s absolute cap");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(8.0).Within(1e-9));
        });
    }

    [Test]
    public void Recommend_CapsAtThirtySeconds_WhenTheAbsoluteCapBindsBeforeTheFactorCap() {
        // Same S_now = 1 (raw factor 100), but t_old = 20s -> factor cap = 80s, so the 30s absolute cap must bind
        // instead of the (looser, here) 4x factor cap.
        var metrics = BuildMetrics(new[] { Frame(1.0), Frame(1.0), Frame(1.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 20.0);

        Assert.Multiple(() => {
            Assert.That(rec.RawSeconds, Is.EqualTo(2000.0).Within(1e-9), "RawSeconds is reported honestly even though capped");
            Assert.That(rec.WasCapped, Is.True);
            Assert.That(rec.CappedByAbsoluteLimit, Is.True, "the 30s absolute cap bound before the (here looser) 4x factor cap");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(30.0).Within(1e-9));
        });
    }

    [Test]
    public void Recommend_CapsBeforeRounding_NotAfter() {
        // A discriminating case needs a cap that is NOT already on the rounding ladder's grid: the two prior cap
        // tests (8.0s and 30.0s) both land exactly on a grid point, so RoundExposureSeconds(min(raw, cap)) (correct)
        // and min(RoundExposureSeconds(raw), cap) (a round-then-cap mutant) happen to agree on those -- they do NOT
        // discriminate cap-then-round from round-then-cap. t_old = 2.2s -> factor cap = 8.8s, which is NOT a
        // multiple of the sub-10s 0.5s ladder step:
        //   correct:  RoundExposureSeconds(min(220, 8.8))      = RoundExposureSeconds(8.8) = 9.0
        //   mutant:   min(RoundExposureSeconds(220), 8.8)      = min(220.0, 8.8)            = 8.8
        // (The absolute 30s cap can NEVER discriminate this, so there is no equivalent case for it: 30 sits exactly
        // on the ladder's own 1s/5s band boundary, and any raw large enough to trigger that cap rounds UP under the
        // >30s 5s-granularity band to something still >= itself > 30, so min(RoundExposureSeconds(raw), 30) always
        // lands on 30 regardless of order -- the two orderings are structurally unable to disagree there.)
        var metrics = BuildMetrics(new[] { Frame(1.0), Frame(1.0), Frame(1.0) }); // S_now = 1.0 -> raw factor 100

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.2);

        Assert.Multiple(() => {
            Assert.That(rec.RawSeconds, Is.EqualTo(220.0).Within(1e-9));
            Assert.That(rec.WasCapped, Is.True);
            Assert.That(rec.CappedByAbsoluteLimit, Is.False, "the factor cap (8.8s) binds well under the 30s absolute cap");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(9.0).Within(1e-9),
                "cap (8.8) THEN round (-> 9.0); a round-then-cap mutant would report 8.8 instead");
        });
    }

    [Test]
    public void Recommend_CurrentExposureAlreadyPastAbsoluteCap_NeverShortensAndReportsNoIncrease() {
        // t_old = 40s already exceeds MaxRecommendedExposureSeconds(30). S_now = 9 (just under the target, so the
        // data genuinely wants more): raw factor (10/9)^2 ~= 1.2346 -> RawSeconds ~= 49.38s, which the absolute
        // cap would pull down to 30 -- BELOW the 40s already in use. The never-shorter floor overrides that back up
        // to CurrentSeconds(40), so HasRecommendation stays true (the situation is worth reporting: the data wants
        // ~49s, past what a sweep can sustain) but IncreasesExposure must be false -- nothing to actually raise.
        var metrics = BuildMetrics(new[] { Frame(9.0), Frame(9.0), Frame(9.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 40.0);

        Assert.Multiple(() => {
            Assert.That(rec.HasRecommendation, Is.True);
            Assert.That(rec.WasCapped, Is.True);
            Assert.That(rec.CappedByAbsoluteLimit, Is.True);
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(rec.CurrentSeconds), "floored back up to current, never shortened");
            Assert.That(rec.IncreasesExposure, Is.False, "a consumer must not render a 'raise it to X' affordance here");
        });
    }

    [Test]
    public void Recommend_CurrentExposureOffGrid_PastAbsoluteCap_FloorsExactlyToCurrent() {
        // The floor-before-round bug (fixed in this file) only shows up when CurrentSeconds is OFF the rounding
        // ladder's grid -- the earlier 40.0s test above sits exactly on the >30s 5s grid, so it passes under
        // EITHER ordering and does not cover this. 31.0s is NOT a multiple of 5, so it discriminates:
        //   S_now = 9, t_old = 31s: raw = 31*(10/9)^2 ~= 38.27s. factorCap = 124s, absoluteCap = 30s -> upperCap = 30.
        //   correct (round THEN floor): max(RoundExposureSeconds(30), 31) = max(30, 31) = 31.0.
        //   buggy   (floor THEN round): RoundExposureSeconds(max(30, 31)) = RoundExposureSeconds(31) = 35.0 --
        //     past the 30s cap AND past CurrentSeconds, exactly the reviewed defect.
        var metrics = BuildMetrics(new[] { Frame(9.0), Frame(9.0), Frame(9.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 31.0);

        Assert.Multiple(() => {
            Assert.That(rec.HasRecommendation, Is.True);
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(31.0).Within(1e-9), "floors exactly to the off-grid current value");
            Assert.That(rec.IncreasesExposure, Is.False);
            Assert.That(rec.RecommendedSeconds,
                Is.LessThanOrEqualTo(Math.Max(ExposureRecommender.MaxRecommendedExposureSeconds, rec.CurrentSeconds)),
                "must never exceed max(absolute cap, current) -- the buggy ordering reported 35.0, violating this");
        });
    }

    [Test]
    public void Recommend_CurrentExposureOffGrid_AlreadyAtTarget_IsConsistentWithExposureIsNotTheLimit() {
        // Another off-grid current value, this time on the OTHER (ExposureIsNotTheLimit) branch:
        //   S_now = 12 >= TargetSensitivity(10), t_old = 3.2s: raw = 3.2*(10/12)^2 ~= 2.22s (uncapped, since well
        //   under both caps).
        //   correct (round THEN floor): max(RoundExposureSeconds(2.22), 3.2) = max(2.5, 3.2) = 3.2 -- unchanged.
        //   buggy   (floor THEN round): RoundExposureSeconds(max(2.22, 3.2)) = RoundExposureSeconds(3.2) = 3.5 --
        //     ExposureIsNotTheLimit=true ("3.2s is already enough") while simultaneously proposing "raise it to
        //     3.5s", the exact no-op-affordance contradiction IncreasesExposure exists to prevent.
        // The pair assertion below (ExposureIsNotTheLimit == true AND IncreasesExposure == false) is what makes
        // the state space non-contradictory; either one alone would not catch the bug.
        // Full frames: the ordering bug under test lives on the ExposureIsNotTheLimit branch, which requires the
        // frames to meet the star-count target as well as the S/N target.
        var metrics = BuildMetrics(new[] { FullFrame(12.0), FullFrame(12.0), FullFrame(12.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 3.2);

        Assert.Multiple(() => {
            Assert.That(rec.ExposureIsNotTheLimit, Is.True);
            Assert.That(rec.IncreasesExposure, Is.False, "must not simultaneously claim the exposure is fine AND propose raising it");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(3.2).Within(1e-9));
        });
    }

    [Test]
    public void Recommend_CappedValueJustBelowOffGridCurrent_RoundOnlyIfExceedsCurrent_DoesNotOvershoot() {
        // The RESIDUAL case the round-then-Math.Max floor missed: S_now just barely above TargetSensitivity, so
        // cappedSeconds lands just BELOW an off-grid current -- close enough that rounding UP would carry it back
        // PAST current, even though Math.Max(rounded, current) looks like a floor.
        //   S_now = 10.16, t_old = 3.2s: raw = cappedSeconds = 3.2*(10/10.16)^2 ~= 3.10s (uncapped).
        //   buggy   (round unconditionally, then Math.Max floor): RoundExposureSeconds(3.10) = 3.5s;
        //     Math.Max(3.5, 3.2) = 3.5s -- past current, contradicting ExposureIsNotTheLimit's "3.2s is enough".
        //   correct (round ONLY IF cappedSeconds > current): 3.10 > 3.2 is false -> return current EXACTLY = 3.2s.
        // As with the sibling test above, the pair assertion (ExposureIsNotTheLimit && !IncreasesExposure) is what
        // makes the state space non-contradictory.
        // Full frames, for the same reason as the sibling test above.
        var metrics = BuildMetrics(new[] { FullFrame(10.16), FullFrame(10.16), FullFrame(10.16) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 3.2);

        Assert.Multiple(() => {
            Assert.That(rec.ExposureIsNotTheLimit, Is.True);
            Assert.That(rec.IncreasesExposure, Is.False, "must not simultaneously claim the exposure is fine AND propose raising it");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(3.2).Within(1e-9), "returns current EXACTLY -- not a rounded-then-maxed approximation");
        });
    }

    // ── StarCountIsTheLimit: bright enough, but too few ─────────────────────────────────────────────────────

    [Test]
    public void Recommend_EveryFrameShort_WithHealthySnr_IsStarCountLimited_NotExposureIsFine() {
        // THE 3800mm DEFECT. Five bright stars per frame against NTarget=20: every frame is short, so S_now is a
        // median of FAINTEST SURVIVORS (20.0), which clears the target and used to assert "exposure is not what is
        // limiting this run". That reasoning is circular -- a star that survived the gate sits at or above it -- and
        // it is contradicted by measurement: on the real rig 2s -> 5s took structure candidates 181 -> 201 and
        // detected stars 76 -> 101 while the stars already found were comfortably bright.
        var frame = Frame(20.0, 21.0, 22.0, 23.0, 24.0);
        // Gate rejections > 0: candidates exist just below the gate, so more signal has something to convert and
        // the probe is honest. With zero the run is StarFieldIsExhausted instead -- see the sibling test.
        var metrics = BuildMetrics(new[] { frame, frame, frame }, gateRejectionsPerFrame: 4);

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(20.0), "faintest survivor, every frame being short");
            Assert.That(rec.ShortFrameCount, Is.EqualTo(3));
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3));
            Assert.That(rec.StarCountIsTheLimit, Is.True);
            Assert.That(rec.ExposureIsNotTheLimit, Is.False,
                "a healthy S/N on too-few stars must NOT claim a longer exposure cannot help");
            Assert.That(rec.RawSeconds, Is.EqualTo(4.0).Within(1e-9), "a fixed doubling probe, not the sky-limited derivation");
            Assert.That(rec.IncreasesExposure, Is.True, "the probe must be offerable, or the user has nothing to try");
        });
    }

    [Test]
    public void Recommend_EveryFrameShort_ButGateRejectedNothing_IsExhausted_AndOffersNoLongerExposure() {
        // THE 14s CASE. Same shape as the probe test above -- bright stars, every frame short -- but the gate
        // rejected NOTHING, so there is no candidate waiting for more signal and none forming. Measured on the
        // reported rig: gate rejections near focus went 5 -> 2 -> 0 across 2/7/14 s while the star count held at
        // 10 per frame and candidate formation FELL 62 -> 55. Recommending another doubling here is what walked
        // that rig from 2 s to 14 s for six extra stars.
        var frame = Frame(20.0, 21.0, 22.0, 23.0, 24.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame }, gateRejectionsPerFrame: 0);

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 14.0);

        Assert.Multiple(() => {
            Assert.That(rec.StarFieldIsExhausted, Is.True);
            Assert.That(rec.StarCountIsTheLimit, Is.False, "the probe has no mechanism to work through");
            Assert.That(rec.GateRejectedCount, Is.EqualTo(0));
            Assert.That(rec.IncreasesExposure, Is.False, "must not offer a longer exposure for a field with nothing left");
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(14.0), "collapses onto the current exposure exactly");
        });
    }

    [Test]
    public void Recommend_UnpopulatedGateRejections_ReadAsExhausted_TheConservativeDirection() {
        // A caller that does not populate FrameLowSensitivityCounts reports 0, which lands on "exhausted" and
        // WITHHOLDS a recommendation. That is the safe direction: silence beats inventing an exposure increase
        // from data the caller never supplied.
        var frame = Frame(20.0, 21.0, 22.0);
        var metrics = new RunEvaluationMetrics { FrameStarSnrs = new[] { frame, frame, frame } };

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 5.0);

        Assert.Multiple(() => {
            Assert.That(rec.StarFieldIsExhausted, Is.True);
            Assert.That(rec.IncreasesExposure, Is.False);
        });
    }

    [Test]
    public void Recommend_ReportsFlatRejections_AsSweepGeometryEvidence() {
        // Flat-topped rejections are surfaced but never drive the exposure verdict -- they point at sweep width.
        var frame = Frame(20.0, 21.0, 22.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame }, gateRejectionsPerFrame: 2, flatRejectionsPerFrame: 5);

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.0);

        Assert.That(rec.FlatRejectedCount, Is.EqualTo(15), "summed across all frames");
    }

    [Test]
    public void Recommend_NoShortFrames_WithHealthySnr_StillReportsExposureIsNotTheLimit() {
        // The star-FLOODING corner (the `bobp` bank run: 52-61 stars/frame at a floored gate). Frames meet the
        // count target, so the NTarget-th-star measurement is real and "exposure is not the limit" is sound. This
        // is the case StarCountIsTheLimit must NOT capture -- the split is all-frames-short, not any-frames-short.
        var metrics = BuildMetrics(new[] { FullFrame(23.3), FullFrame(23.3), FullFrame(23.3) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(rec.ShortFrameCount, Is.EqualTo(0));
            Assert.That(rec.ExposureIsNotTheLimit, Is.True);
            Assert.That(rec.StarCountIsTheLimit, Is.False);
            Assert.That(rec.IncreasesExposure, Is.False, "nothing to offer: the frames are rich AND bright");
        });
    }

    [Test]
    public void Recommend_SomeFramesShort_WithHealthySnr_IsNotStarCountLimited() {
        // The boundary. One full frame leaves a real NTarget-th-star measurement in the run, so the statistic is
        // not degenerate and the old verdict still holds.
        var metrics = BuildMetrics(new[] { FullFrame(12.0), Frame(20.0, 21.0), Frame(20.0, 21.0) });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(rec.ShortFrameCount, Is.EqualTo(2));
            Assert.That(rec.UsableFrameCount, Is.EqualTo(3));
            Assert.That(rec.StarCountIsTheLimit, Is.False, "all-frames-short is the trigger, not any-frames-short");
            Assert.That(rec.ExposureIsNotTheLimit, Is.True);
        });
    }

    [Test]
    public void Recommend_EveryFrameShort_WithWeakSnr_KeepsTheDerivedRecommendation() {
        // Short frames must NOT hijack the case the derivation genuinely answers. S_now = 5 is below the target, so
        // this is ordinary signal starvation: keep the sky-limited number (2s x (10/5)^2 = 8s), not the probe.
        var frame = Frame(5.0, 6.0, 7.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame });

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 2.0);

        Assert.Multiple(() => {
            Assert.That(rec.MeasuredSnr, Is.EqualTo(5.0));
            Assert.That(rec.StarCountIsTheLimit, Is.False, "the S/N target is not met, so the derivation applies");
            Assert.That(rec.ExposureIsNotTheLimit, Is.False);
            Assert.That(rec.RawSeconds, Is.EqualTo(8.0).Within(1e-9), "sky-limited, not the doubling probe");
        });
    }

    [Test]
    public void Recommend_StarCountProbe_IsStillBoundedByTheAbsoluteCap() {
        // The probe is a direction, not a licence to climb: a 20s run doubles to 40s, past the 30s ceiling a sweep
        // can sustain, so the same cap that bounds the derived path bounds this one.
        var frame = Frame(20.0, 21.0, 22.0);
        var metrics = BuildMetrics(new[] { frame, frame, frame }, gateRejectionsPerFrame: 4);

        var rec = ExposureRecommender.Recommend(metrics, DefaultConstants(), currentExposureSeconds: 20.0);

        Assert.Multiple(() => {
            Assert.That(rec.StarCountIsTheLimit, Is.True);
            Assert.That(rec.RawSeconds, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(rec.RecommendedSeconds, Is.EqualTo(ExposureRecommender.MaxRecommendedExposureSeconds));
            Assert.That(rec.CappedByAbsoluteLimit, Is.True);
        });
    }

    /// <summary>Builds an inclusive ascending double range [start, end] -- a small local helper to keep the
    /// 40-value quantile test readable.</summary>
    private static double[] Range(int start, int end) {
        var result = new double[end - start + 1];
        for (var i = 0; i < result.Length; i++) {
            result[i] = start + i;
        }
        return result;
    }
}
