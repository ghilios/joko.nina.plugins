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
/// Verifies the optimizer-side early-context cache in <see cref="RunEvaluationData"/>: it must reuse a built
/// context across candidate evaluations whose EARLY key is unchanged (rebuilding only when the early key
/// changes), while producing exactly the same per-frame results as the non-cached delegate path. The cache is
/// a pure performance optimization — results must be identical.
/// </summary>
[TestFixture]
public class RunEvaluationDataCacheTests {

    private static AlglibAPI NewAlglib() => new AlglibAPI();

    private static RunFitConfig DefaultFitConfig() => new RunFitConfig {
        StepSize = 100,
        UseWeights = true,
        MaxOutlierRejections = 0,
        RejectionConfidence = 0.0,
        PreferredModel = null
    };

    private static List<RunFrame> ThreeFrames() => new List<RunFrame> {
        new RunFrame { FrameId = "a", FocuserPosition = 9900, Image = 9900 },
        new RunFrame { FrameId = "b", FocuserPosition = 10000, Image = 10000 },
        new RunFrame { FrameId = "c", FocuserPosition = 10100, Image = 10100 },
    };

    // A spy split-detector that counts BuildContext vs GateAndMeasure calls and keys the early context on
    // NoiseClippingMultiplier only (a stand-in early param), so a change to a "late" param reuses the context.
    private sealed class SpySplitDetector : RunEvaluationData.ISplitFrameDetector {
        public int BuildCount;
        public int GateCount;

        // The "context" carries the frame's focuser position + the early-key value it was built with.
        private sealed class Ctx : IDisposable {
            public int Position;
            public double EarlyValue;
            public bool Disposed;
            public void Dispose() { Disposed = true; }
        }

        public string ComputeEarlyKey(StarDetectorParams p) =>
            p.NoiseClippingMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token) {
            BuildCount++;
            return Task.FromResult<IDisposable>(new Ctx { Position = (int)frameImage, EarlyValue = p.NoiseClippingMultiplier });
        }

        public FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p) {
            GateCount++;
            var ctx = (Ctx)context;
            // HFR is a clean function of the frame position; star count varies with a LATE param (Sensitivity) so
            // gating actually changes per candidate, while the early context value must be the one it was built with.
            var dx = (ctx.Position - 10000) / 8.0;
            return new FrameDetectionResult {
                AverageHFR = Math.Sqrt(1.5 * 1.5 + dx * dx),
                HFRStdDev = 0.05,
                StarCount = (int)(50 + p.Sensitivity + 100 * ctx.EarlyValue),
                StarCenters = Array.Empty<(double X, double Y)>()
            };
        }
    }

    [Test]
    public async Task SplitDetector_LateOnlyChanges_ReuseContextAcrossEvaluations() {
        var spy = new SpySplitDetector();
        var data = new RunEvaluationData("cache", ThreeFrames(), spy, NewAlglib(), DefaultFitConfig());

        // First eval: 3 frames => 3 builds + 3 gates.
        await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 4.0, Sensitivity = 2.0 }, CancellationToken.None);
        // Second eval changes ONLY a late param (same early key): must reuse the 3 contexts => 0 new builds, 3 gates.
        await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 4.0, Sensitivity = 9.0 }, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(spy.BuildCount, Is.EqualTo(3), "early context must be built once per frame and reused across late-only evals");
            Assert.That(spy.GateCount, Is.EqualTo(6), "gate must run for every (frame, eval): 3 frames × 2 evals");
        });
    }

    [Test]
    public async Task SplitDetector_EarlyParamChange_RebuildsContext() {
        var spy = new SpySplitDetector();
        var data = new RunEvaluationData("cache", ThreeFrames(), spy, NewAlglib(), DefaultFitConfig());

        await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 4.0 }, CancellationToken.None);
        // Changing the EARLY param changes the early key => contexts must be rebuilt.
        await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 5.0 }, CancellationToken.None);

        Assert.That(spy.BuildCount, Is.EqualTo(6), "an early-param change must rebuild every frame's context");
    }

    [Test]
    public async Task SplitDetector_ProducesIdenticalResults_ToNonCachedDelegate() {
        // The cached split path must give byte-identical per-frame results to a plain monolithic delegate that
        // calls BuildContext+GateAndMeasure inline with no reuse.
        var pLate = new StarDetectorParams { NoiseClippingMultiplier = 4.0, Sensitivity = 7.0 };

        var spy = new SpySplitDetector();
        var cached = new RunEvaluationData("cached", ThreeFrames(), spy, NewAlglib(), DefaultFitConfig());
        var cachedMetrics = await cached.EvaluateAsync(pLate, CancellationToken.None);

        // Reference: a non-split delegate built from the same detector, no caching.
        Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> reference =
            async (image, p, token) => {
                var refSpy = new SpySplitDetector();
                var ctx = await refSpy.BuildContextAsync(image, p, token);
                using (ctx) {
                    return refSpy.GateAndMeasure(ctx, p);
                }
            };
        var refData = new RunEvaluationData("ref", ThreeFrames(), reference, NewAlglib(), DefaultFitConfig());
        var refMetrics = await refData.EvaluateAsync(pLate, CancellationToken.None);

        Assert.That(cachedMetrics.FrameStarCounts, Is.EqualTo(refMetrics.FrameStarCounts),
            "cached split path must produce identical per-frame star counts to the non-cached delegate");
    }

    [Test]
    public async Task SplitDetector_DisposesEvictedContextsAtRunEnd() {
        // Track that contexts get disposed both when superseded (early-key change) AND when the run finishes
        // (RunEvaluationData.Dispose). This covers the Dispose() cleanup loop the wizard/harness fixes rely on to
        // release the per-frame source Mats.
        var disposed = new List<bool>();
        var spy = new DisposalTrackingSplitDetector(disposed);
        var data = new RunEvaluationData("dispose", ThreeFrames(), spy, NewAlglib(), DefaultFitConfig());

        await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 4.0 }, CancellationToken.None);
        await data.EvaluateAsync(new StarDetectorParams { NoiseClippingMultiplier = 5.0 }, CancellationToken.None);

        // After two distinct early keys over 3 frames: 6 contexts were built. The first key's 3 were disposed when
        // the second key superseded them; the second key's 3 are still cached (alive).
        Assert.Multiple(() => {
            Assert.That(disposed.Count, Is.EqualTo(6), "two early keys × 3 frames must have built 6 contexts");
            Assert.That(disposed.Count(d => d), Is.EqualTo(3),
                "only the evicted first-key contexts are disposed before run end; the surviving ones are still cached");
        });

        // Run end: Dispose() must release every surviving cached context too, so ALL created contexts end disposed.
        data.Dispose();
        Assert.That(disposed.Count(d => d), Is.EqualTo(6),
            "Dispose() must release every surviving cached context (the source Mats), so all created contexts are freed");

        // Dispose is idempotent: a second call must not throw or double-dispose anything.
        Assert.DoesNotThrow(() => data.Dispose());
        Assert.That(disposed.Count(d => d), Is.EqualTo(6), "idempotent Dispose must not change the disposed count");
    }

    private sealed class DisposalTrackingSplitDetector : RunEvaluationData.ISplitFrameDetector {
        private readonly List<bool> disposed;
        public DisposalTrackingSplitDetector(List<bool> disposed) { this.disposed = disposed; }

        private sealed class Ctx : IDisposable {
            private readonly List<bool> sink;
            public Ctx(List<bool> sink) { this.sink = sink; sink.Add(false); Index = sink.Count - 1; }
            public int Index;
            public void Dispose() { sink[Index] = true; }
        }

        public string ComputeEarlyKey(StarDetectorParams p) =>
            p.NoiseClippingMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token) =>
            Task.FromResult<IDisposable>(new Ctx(disposed));

        public FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p) =>
            new FrameDetectionResult { AverageHFR = 2.0, HFRStdDev = 0.05, StarCount = 30, StarCenters = Array.Empty<(double X, double Y)>() };
    }
}
