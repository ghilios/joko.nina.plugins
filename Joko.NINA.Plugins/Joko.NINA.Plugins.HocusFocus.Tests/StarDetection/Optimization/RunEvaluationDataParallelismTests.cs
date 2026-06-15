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
using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// Verifies that detecting a run's frames CONCURRENTLY within one evaluation (the optimizer perf change) is
/// both deterministic (identical metrics every run) and bit-identical to a forced-sequential pass (cap=1).
/// The frames are assembled by index, not completion order, and pooling is order-independent, so parallelism
/// cannot change any value the objective consumes.
/// </summary>
[TestFixture]
public class RunEvaluationDataParallelismTests {

    private static AlglibAPI NewAlglib() => new AlglibAPI();

    private static RunFitConfig DefaultFitConfig() => new RunFitConfig {
        StepSize = 100,
        UseWeights = true,
        MaxOutlierRejections = 0,
        RejectionConfidence = 0.0,
        PreferredModel = null
    };

    // A clean symmetric hyperbola hfr(pos) = sqrt(a^2 + ((pos - p0)/b)^2).
    private const double HyperbolaA = 1.5;
    private const double HyperbolaB = 8.0;
    private const int HyperbolaP0 = 10000;

    private static double Hfr(int pos) {
        var dx = (pos - HyperbolaP0) / HyperbolaB;
        return Math.Sqrt(HyperbolaA * HyperbolaA + dx * dx);
    }

    // A multi-frame run where TWO frames share the middle focuser position (so pooling is exercised) and frames
    // carry distinct HFRs at that shared position to make pooling order-sensitive if it were implemented wrong.
    // The frame payload is (focuserPosition, perFrameSeed): perFrameSeed shifts the HFR a touch so the two frames
    // at the shared position differ, and the star count is a deterministic function of the seed.
    private static List<RunFrame> MixedFrames() {
        var frames = new List<RunFrame>();
        // Nine distinct positions, plus a duplicate of the middle position with a different HFR/seed.
        for (var i = -4; i <= 4; i++) {
            var pos = HyperbolaP0 + i * 100;
            frames.Add(new RunFrame { FrameId = $"pos_{pos}_a", FocuserPosition = pos, Image = new int[] { pos, 0 } });
        }
        // Two extra frames at the SAME (middle) position with different seeds => pooling must average them.
        frames.Add(new RunFrame { FrameId = "pos_mid_b", FocuserPosition = HyperbolaP0, Image = new int[] { HyperbolaP0, 1 } });
        frames.Add(new RunFrame { FrameId = "pos_mid_c", FocuserPosition = HyperbolaP0, Image = new int[] { HyperbolaP0, 2 } });
        return frames;
    }

    // A split detector whose build is artificially "slow and jittered" so frame tasks finish out of order, AND
    // whose per-frame star count / HFR is a pure deterministic function of the frame payload — so the ONLY thing
    // that could differ across runs is completion-order handling, which the index-ordered assembly must defeat.
    private sealed class JitteredSplitDetector : RunEvaluationData.ISplitFrameDetector {
        private sealed class Ctx : IDisposable {
            public int Position;
            public int Seed;
            public void Dispose() { }
        }

        public string ComputeEarlyKey(StarDetectorParams p) =>
            p.NoiseClippingMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public async Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token) {
            var payload = (int[])frameImage;
            // Reverse-ish jitter: later frames sleep less, so completion order scrambles relative to index order.
            var delay = ((payload[1] + 1) * 7 + (payload[0] % 13)) % 11;
            await Task.Delay(delay, token).ConfigureAwait(false);
            return new Ctx { Position = payload[0], Seed = payload[1] };
        }

        public FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p) {
            var ctx = (Ctx)context;
            // HFR is a clean hyperbola plus a small per-frame seed offset; star count is a deterministic function of
            // the seed and a LATE param (Sensitivity), so gating varies per candidate but never per completion order.
            var hfr = Hfr(ctx.Position) + ctx.Seed * 0.01;
            return new FrameDetectionResult {
                AverageHFR = hfr,
                HFRStdDev = 0.05 + ctx.Seed * 0.001,
                StarCount = 40 + ctx.Seed * 3 + (int)p.Sensitivity,
                StarCenters = Array.Empty<(double X, double Y)>()
            };
        }
    }

    private static void AssertMetricsEqual(RunEvaluationMetrics expected, RunEvaluationMetrics actual, string because) {
        Assert.Multiple(() => {
            Assert.That(actual.FrameStarCounts, Is.EqualTo(expected.FrameStarCounts), $"{because}: FrameStarCounts (in order)");
            Assert.That(actual.SigmaFocus, Is.EqualTo(expected.SigmaFocus).Within(0.0).Or.NaN, $"{because}: SigmaFocus");
            Assert.That(actual.LooStdError, Is.EqualTo(expected.LooStdError).Within(0.0).Or.NaN, $"{because}: LooStdError");
            Assert.That(actual.RSquared, Is.EqualTo(expected.RSquared).Within(0.0), $"{because}: RSquared");
            Assert.That(actual.ReducedChiSquared, Is.EqualTo(expected.ReducedChiSquared).Within(0.0).Or.NaN, $"{because}: ReducedChiSquared");
            Assert.That(actual.StepSize, Is.EqualTo(expected.StepSize), $"{because}: StepSize");
        });
    }

    [Test]
    public async Task EvaluateAsync_ParallelFrames_AreDeterministicAcrossRepeatedRuns() {
        // Run the SAME params many times against a jittered (out-of-completion-order) detector; every run must yield
        // bit-identical metrics. If completion order leaked into FrameStarCounts or pooling, the jitter would expose it.
        var p = new StarDetectorParams { NoiseClippingMultiplier = 4.0, Sensitivity = 5.0 };

        RunEvaluationMetrics first = null;
        for (int run = 0; run < 12; run++) {
            using var data = new RunEvaluationData("det", MixedFrames(), new JitteredSplitDetector(), NewAlglib(), DefaultFitConfig());
            var metrics = await data.EvaluateAsync(p, CancellationToken.None);
            if (first == null) {
                first = metrics;
            } else {
                AssertMetricsEqual(first, metrics, $"run {run} must match run 0");
            }
        }

        // Sanity: the duplicated middle position must have pooled (12 frames in, 9 distinct positions out is implied
        // by a finite, well-formed fit). FrameStarCounts is per-frame => 11 entries (9 + 2 extras).
        Assert.That(first.FrameStarCounts.Count, Is.EqualTo(11), "one star count per frame, including the two shared-position extras");
    }

    [Test]
    public async Task EvaluateAsync_ParallelPath_MatchesForcedSequentialPath() {
        // The parallel path (default cap) must produce metrics IDENTICAL to a forced-sequential path (cap=1) for the
        // same synthetic run — proving the concurrency does not change results.
        var p = new StarDetectorParams { NoiseClippingMultiplier = 4.0, Sensitivity = 7.0 };

        using var sequential = new RunEvaluationData("seq", MixedFrames(), new JitteredSplitDetector(), NewAlglib(), DefaultFitConfig()) {
            FrameParallelismOverride = 1 // force the sequential path
        };
        var seqMetrics = await sequential.EvaluateAsync(p, CancellationToken.None);

        using var parallel = new RunEvaluationData("par", MixedFrames(), new JitteredSplitDetector(), NewAlglib(), DefaultFitConfig()) {
            FrameParallelismOverride = 8 // force a wide fan-out
        };
        var parMetrics = await parallel.EvaluateAsync(p, CancellationToken.None);

        AssertMetricsEqual(seqMetrics, parMetrics, "parallel path must equal the forced-sequential path");
    }

    [Test]
    public async Task EvaluateAsync_ParallelPath_MatchesSequential_WithDelegateDetector() {
        // Same equivalence, but for the monolithic delegate path (no early-context cache) so both code paths are
        // covered. The delegate returns a deterministic per-frame result; only the loop's concurrency differs.
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect = async (image, sp, token) => {
            var payload = (int[])image;
            await Task.Delay(((payload[1] + 1) * 5) % 9, token).ConfigureAwait(false); // jitter completion order
            return new FrameDetectionResult {
                AverageHFR = Hfr(payload[0]) + payload[1] * 0.01,
                HFRStdDev = 0.05,
                StarCount = 40 + payload[1] * 3 + (int)sp.Sensitivity,
                StarCenters = Array.Empty<(double X, double Y)>()
            };
        };

        var p = new StarDetectorParams { Sensitivity = 3.0 };

        var sequential = new RunEvaluationData("seq", MixedFrames(), detect, NewAlglib(), DefaultFitConfig()) {
            FrameParallelismOverride = 1
        };
        var seqMetrics = await sequential.EvaluateAsync(p, CancellationToken.None);

        var parallel = new RunEvaluationData("par", MixedFrames(), detect, NewAlglib(), DefaultFitConfig()) {
            FrameParallelismOverride = 8
        };
        var parMetrics = await parallel.EvaluateAsync(p, CancellationToken.None);

        AssertMetricsEqual(seqMetrics, parMetrics, "delegate parallel path must equal the delegate sequential path");
    }

    [Test]
    public void EvaluateAsync_ParallelFrames_HonorCancellation() {
        // Cancelling mid-evaluation must surface an OperationCanceledException and not hang; a detector that blocks
        // on the token lets us cancel while frames are in flight.
        using var cts = new CancellationTokenSource();
        var detector = new BlockingSplitDetector(cts);
        using var data = new RunEvaluationData("cancel", MixedFrames(), detector, NewAlglib(), DefaultFitConfig()) {
            FrameParallelismOverride = 4
        };

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 4.0 }, cts.Token));
    }

    // A split detector that cancels the token as soon as the first build starts, then waits on it — so the parallel
    // loop must propagate the cancellation across all in-flight frames.
    private sealed class BlockingSplitDetector : RunEvaluationData.ISplitFrameDetector {
        private readonly CancellationTokenSource cts;
        public BlockingSplitDetector(CancellationTokenSource cts) { this.cts = cts; }

        private sealed class Ctx : IDisposable { public void Dispose() { } }

        public string ComputeEarlyKey(StarDetectorParams p) => "k";

        public async Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token) {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); // blocks until cancelled
            return new Ctx();
        }

        public FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p) =>
            new FrameDetectionResult { AverageHFR = 2.0, HFRStdDev = 0.05, StarCount = 30, StarCenters = Array.Empty<(double X, double Y)>() };
    }
}
