#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

/// <summary>
/// F79 — per-frame progress INSIDE one optimizer evaluation.
///
/// <para>An early-context rebuild scores one candidate against every frame of every loaded run, and can run for
/// minutes. Until it finishes the evaluation counter does not move, which is indistinguishable from a hang. The
/// evaluator therefore reports frames as they complete, aggregated across runs — a counter that restarted at each
/// run would appear to go backwards partway through a single step.</para>
/// </summary>
[TestFixture]
public class EvaluatorFrameProgressTests {

    private static RunFitConfig FitConfig() => new RunFitConfig {
        StepSize = 100,
        UseWeights = true,
        MaxOutlierRejections = 0,
        RejectionConfidence = 0.0,
        PreferredModel = null
    };

    private static double Hfr(int pos) {
        var dx = (pos - 10000) / 8.0;
        return Math.Sqrt(1.5 * 1.5 + dx * dx);
    }

    private static List<RunFrame> Frames(int count) =>
        Enumerable.Range(0, count)
            .Select(i => {
                var pos = 10000 + (i - count / 2) * 100;
                return new RunFrame { FrameId = $"f{i}", FocuserPosition = pos, Image = new int[] { pos } };
            })
            .ToList();

    private static Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> Detector() =>
        (image, p, token) => {
            var payload = (int[])image;
            return Task.FromResult(new FrameDetectionResult {
                AverageHFR = Hfr(payload[0]),
                HFRStdDev = 0.05,
                StarCount = 40 + (int)p.Sensitivity,
                StarCenters = Array.Empty<(double X, double Y)>()
            });
        };

    private sealed class Recorder : IProgress<RunLoadProgress> {
        private readonly object gate = new object();
        public List<RunLoadProgress> Reports { get; } = new List<RunLoadProgress>();

        public void Report(RunLoadProgress value) {
            lock (gate) {
                Reports.Add(value);
            }
        }
    }

    [Test]
    public async Task FrameProgress_CountsAcrossEveryLoadedRun_NotPerRun() {
        // DISCRIMINATING: forward the per-run reporter unshifted and the counter runs 1..5 twice, so the line
        // visibly rewinds halfway through a single step. It must run 1..12 once against a total of 12.
        using var runA = new RunEvaluationData("a", Frames(5), Detector(), new AlglibAPI(), FitConfig()) { FrameParallelismOverride = 1 };
        using var runB = new RunEvaluationData("b", Frames(7), Detector(), new AlglibAPI(), FitConfig()) { FrameParallelismOverride = 1 };

        var recorder = new Recorder();
        var evaluator = RunEvaluationData.CreateEvaluator(new[] { runA, runB }, recorder);

        await evaluator(new StarDetectorParams { Sensitivity = 5.0 }, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(recorder.Reports, Has.Count.EqualTo(12), "one report per frame across both runs");
            Assert.That(recorder.Reports.Select(r => r.Total).Distinct(), Is.EquivalentTo(new[] { 12 }),
                "the total is the frame count of every loaded run, so the fraction means something");
            Assert.That(recorder.Reports.Select(r => r.Current), Is.EqualTo(Enumerable.Range(1, 12)),
                "the running count must advance monotonically across the run boundary");
        });
    }

    [Test]
    public async Task NoProgressReporter_IsTheHeadlessDefault_AndStillEvaluates() {
        // GUARD: every headless caller (TestApp, the tilt paths) builds the evaluator without a reporter, and the
        // parameter is optional so those call sites are untouched.
        using var run = new RunEvaluationData("a", Frames(5), Detector(), new AlglibAPI(), FitConfig());

        var withProgress = RunEvaluationData.CreateEvaluator(new[] { run }, new Recorder());
        var without = RunEvaluationData.CreateEvaluator(new[] { run });
        var p = new StarDetectorParams { Sensitivity = 5.0 };

        var a = await withProgress(p, CancellationToken.None);
        var b = await without(p, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(b, Has.Count.EqualTo(1));
            // Reporting must not perturb what the objective sees.
            Assert.That(b[0].FrameStarCounts, Is.EqualTo(a[0].FrameStarCounts));
            Assert.That(b[0].SigmaFocus, Is.EqualTo(a[0].SigmaFocus).Within(0.0).Or.NaN);
        });
    }

    [Test]
    public async Task FrameProgress_UnderConcurrentFrameDetection_StaysMonotonicAndReachesTheTotal() {
        // The frames run concurrently by default and complete out of order; the count is incremented atomically,
        // so it must still be a clean 1..N with no duplicates and no gaps.
        using var run = new RunEvaluationData("a", Frames(9), Detector(), new AlglibAPI(), FitConfig()) { FrameParallelismOverride = 8 };

        var recorder = new Recorder();
        await RunEvaluationData.CreateEvaluator(new[] { run }, recorder)(
            new StarDetectorParams { Sensitivity = 5.0 }, CancellationToken.None);

        Assert.That(recorder.Reports.Select(r => r.Current).OrderBy(c => c), Is.EqualTo(Enumerable.Range(1, 9)));
    }
}
