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
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class ExposureRecommenderTests {

    private static ObjectiveConstants DefaultConstants() => new ObjectiveConstants(); // NTarget = 20

    private static IReadOnlyList<double> Frame(params double[] values) => values;

    private static RunEvaluationMetrics BuildMetrics(IReadOnlyList<IReadOnlyList<double>> frameStarSnrs, IReadOnlyList<bool> frameIsRecovery = null) {
        return new RunEvaluationMetrics {
            FrameStarSnrs = frameStarSnrs,
            FrameIsRecovery = frameIsRecovery
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

    // ── Per-frame quantile ──────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Recommend_TakesTheNTargetThBrightestPerFrame() {
        // 40 distinct SNRs (1..40) on three IDENTICAL frames, so the median-across-frames step is a no-op and
        // the result is exactly the per-frame quantile. Descending rank (NTarget - 1) == ascending index 19,
        // i.e. value 21 (ascending array is 1..40, so index 20 holds 21) -- element 19 of the descending order.
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
    public void Recommend_ReadsNTargetFromConstants_QuantileIndexMoves() {
        // Same 40-value frame as above, but NTarget=60 now exceeds the 40 survivors on every frame, so EVERY
        // frame falls back to its faintest survivor (1.0) instead of the value from the NTarget=20 test (21.0).
        // If NTarget were hard-coded to 20 inside the implementation this assertion would fail.
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

    [TestCase(3.01, 3.5)]   // below 10s: 0.5s granularity, rounds UP past the next half-second
    [TestCase(3.5, 3.5)]    // already exactly on the grid -- ceiling of an exact value is itself, not the next step
    [TestCase(15.2, 16.0)]  // 10s-30s: 1s granularity
    [TestCase(20.0, 20.0)]  // already exact
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
        var metrics = BuildMetrics(new[] { Frame(12.0), Frame(12.0), Frame(12.0) });

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
