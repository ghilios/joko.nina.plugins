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
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization;

[TestFixture]
public class StarDetectionOptimizerTests {

    // Two-axis synthetic landscape. J is maximized when (Sensitivity, StarClippingMultiplier) are at
    // the chosen optimum. We map distance-from-optimum to SigmaFocus so SFocus (and thus J) peaks there.
    // All other metrics are kept star-rich / well-fit so they never hard-fail.
    private const double OptSensitivity = 10.0;   // interior of [0, 50]
    private const double OptStarClip = 2.5;        // interior of [0.25, 10]

    private static RunEvaluationMetrics SyntheticRunFor(StarDetectorParams p) {
        // Normalized distance (0 at optimum) over the two optimized axes, each scaled by its span.
        var dSens = (p.Sensitivity - OptSensitivity) / 20.0;
        var dClip = (p.StarClippingMultiplier - OptStarClip) / 4.5;
        var dist = Math.Sqrt(dSens * dSens + dClip * dClip);
        // SigmaFocus grows with distance: small (sharp) at the optimum, larger away.
        var stepSize = 100.0;
        var sigmaFocus = stepSize * (0.02 + 1.0 * dist);
        return new RunEvaluationMetrics {
            SigmaFocus = sigmaFocus,
            LooStdError = double.NaN,
            StepSize = stepSize,
            RSquared = 0.99,
            ReducedChiSquared = 1.0,
            FrameStarCounts = Enumerable.Repeat(50, 10).ToList()
        };
    }

    private static Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> SyntheticEvaluator(
        Action<StarDetectorParams> onCall = null) {
        return (p, token) => {
            onCall?.Invoke(p);
            IReadOnlyList<RunEvaluationMetrics> runs = new[] { SyntheticRunFor(p) };
            return Task.FromResult(runs);
        };
    }

    private static StarDetectorParams Seed() => new StarDetectorParams {
        Sensitivity = 2.0,
        StarClippingMultiplier = 2.0,
    };

    private static OptimizerSettings DefaultSettings() => new OptimizerSettings {
        MaxEvaluations = 400,
        CoarseGridLevels = 4,
        StepFloorFraction = 0.125
    };

    [Test]
    public async Task Optimize_MovesTowardOptimum_AndNeverWorseThanSeed() {
        var optimizer = new StarDetectionOptimizer();
        var seed = Seed();
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await optimizer.OptimizeAsync(seed, variables, SyntheticEvaluator(), DefaultSettings(), null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ), "best J must never regress below seed");
            Assert.That(result.Evaluations, Is.LessThanOrEqualTo(DefaultSettings().MaxEvaluations), "must respect budget");
            // Moved measurably toward the optimum on the two optimized axes.
            var seedDistSens = Math.Abs(seed.Sensitivity - OptSensitivity);
            var bestDistSens = Math.Abs(result.BestParams.Sensitivity - OptSensitivity);
            var seedDistClip = Math.Abs(seed.StarClippingMultiplier - OptStarClip);
            var bestDistClip = Math.Abs(result.BestParams.StarClippingMultiplier - OptStarClip);
            Assert.That(bestDistSens + bestDistClip, Is.LessThan(seedDistSens + seedDistClip), "should approach the optimum");
            Assert.That(result.ImprovedOverSeed, Is.True);
        });
    }

    [Test]
    public async Task Optimize_IsDeterministic() {
        var variables = OptimizerVariable.CreateCuratedSet();
        var r1 = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(), DefaultSettings(), null, CancellationToken.None);
        var r2 = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(), DefaultSettings(), null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(r1.BestJ, Is.EqualTo(r2.BestJ).Within(0.0));
            Assert.That(r1.SeedJ, Is.EqualTo(r2.SeedJ).Within(0.0));
            Assert.That(r1.Evaluations, Is.EqualTo(r2.Evaluations));

            // Every curated variable must read back identically across both runs (not just the two grid axes).
            foreach (var v in variables) {
                Assert.That(v.Read(r1.BestParams), Is.EqualTo(v.Read(r2.BestParams)).Within(0.0), $"{v.Name} differs between runs");
            }

            // ChangedVariables must match in count AND contents (name + seed/best values), in order.
            Assert.That(r1.ChangedVariables.Count, Is.EqualTo(r2.ChangedVariables.Count), "ChangedVariables count differs");
            for (var i = 0; i < r1.ChangedVariables.Count; i++) {
                Assert.That(r1.ChangedVariables[i].Name, Is.EqualTo(r2.ChangedVariables[i].Name), $"ChangedVariables[{i}].Name");
                Assert.That(r1.ChangedVariables[i].SeedValue, Is.EqualTo(r2.ChangedVariables[i].SeedValue).Within(0.0), $"ChangedVariables[{i}].SeedValue");
                Assert.That(r1.ChangedVariables[i].BestValue, Is.EqualTo(r2.ChangedVariables[i].BestValue).Within(0.0), $"ChangedVariables[{i}].BestValue");
            }
        });
    }

    [Test]
    public async Task Optimize_FlatLandscape_KeepsSeedAndReportsNoImprovement() {
        // Constant J everywhere => no strictly-improving move => result equals seed.
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> flat = (p, token) => {
            IReadOnlyList<RunEvaluationMetrics> runs = new[] {
                new RunEvaluationMetrics {
                    SigmaFocus = 10.0,
                    LooStdError = double.NaN,
                    StepSize = 100.0,
                    RSquared = 0.95,
                    ReducedChiSquared = 1.0,
                    FrameStarCounts = Enumerable.Repeat(40, 10).ToList()
                }
            };
            return Task.FromResult(runs);
        };

        var seed = Seed();
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await new StarDetectionOptimizer().OptimizeAsync(seed, variables, flat, DefaultSettings(), null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.ImprovedOverSeed, Is.False);
            Assert.That(result.BestJ, Is.EqualTo(result.SeedJ).Within(0.0));
            Assert.That(result.BestParams.Sensitivity, Is.EqualTo(seed.Sensitivity).Within(0.0));
            Assert.That(result.BestParams.StarClippingMultiplier, Is.EqualTo(seed.StarClippingMultiplier).Within(0.0));
            Assert.That(result.ChangedVariables, Is.Empty);
        });
    }

    [Test]
    public async Task Optimize_NeverEvaluatesSameCacheKeyTwice() {
        var keysSeen = new List<string>();
        var locker = new object();
        Action<StarDetectorParams> onCall = p => {
            var key = StarDetector.ComputeCacheKey(p);
            lock (locker) {
                keysSeen.Add(key);
            }
        };

        var variables = OptimizerVariable.CreateCuratedSet();
        await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(onCall), DefaultSettings(), null, CancellationToken.None);

        Assert.That(keysSeen.Count, Is.EqualTo(keysSeen.Distinct().Count()),
            "the evaluator must never be invoked twice for the same cache key (revisits hit the memo)");
    }

    [Test]
    public async Task Optimize_EvaluationCountMatchesUniqueEvaluatorInvocations() {
        var invocations = 0;
        Action<StarDetectorParams> onCall = _ => Interlocked.Increment(ref invocations);
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(onCall), DefaultSettings(), null, CancellationToken.None);
        // Evaluations counts only cache misses; every cache miss is exactly one evaluator invocation.
        Assert.That(result.Evaluations, Is.EqualTo(invocations));
    }

    [Test]
    public async Task Optimize_MultiRun_PrefersBalancedOverLopsided() {
        // Two runs: run A peaks at low Sensitivity, run B peaks at high Sensitivity. With beta=0.5 the min
        // term dominates, so the optimizer should settle near the midpoint (balanced) rather than at either
        // extreme (where one run is great and the other terrible). The two per-run optima are kept CLOSE
        // (6 and 14) on purpose: with that separation the composite J_total = 0.5·mean + 0.5·min is strictly
        // UNIMODAL with its single peak exactly at the midpoint sens=10, so the test pins the objective's
        // balanced-blend property rather than which local basin a particular Phase-A grid seed happens to fall
        // into. (Wider separations like 4/16 make J_total multimodal — two near-equal shoulder maxima flanking
        // a slightly-lower midpoint — so the result then depends on grid-seed luck, not the β blend the test
        // is about.)
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> twoRun = (p, token) => {
            double step = 100.0;
            double sens = p.Sensitivity;
            // Run A sigma minimized at sens=6; Run B minimized at sens=14. Midpoint (sens=10) balances both.
            double sigmaA = step * (0.02 + Math.Abs(sens - 6.0) / 20.0);
            double sigmaB = step * (0.02 + Math.Abs(sens - 14.0) / 20.0);
            RunEvaluationMetrics Make(double sigma) => new RunEvaluationMetrics {
                SigmaFocus = sigma,
                LooStdError = double.NaN,
                StepSize = step,
                RSquared = 0.99,
                ReducedChiSquared = 1.0,
                FrameStarCounts = Enumerable.Repeat(50, 10).ToList()
            };
            IReadOnlyList<RunEvaluationMetrics> runs = new[] { Make(sigmaA), Make(sigmaB) };
            return Task.FromResult(runs);
        };

        // Seed at an extreme.
        var seed = new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.5 };
        var variables = OptimizerVariable.CreateCuratedSet();
        var settings = new OptimizerSettings { MaxEvaluations = 600, CoarseGridLevels = 5, StepFloorFraction = 0.125 };
        var result = await new StarDetectionOptimizer().OptimizeAsync(seed, variables, twoRun, settings, null, CancellationToken.None);

        // The balanced optimum is sens=10 (midpoint of 6 and 14). Should land closer to 10 than to either extreme.
        Assert.That(Math.Abs(result.BestParams.Sensitivity - 10.0), Is.LessThan(3.0),
            $"expected balanced ~10, got {result.BestParams.Sensitivity}");
    }

    [Test]
    public void Optimize_CancellationThrows() {
        var cts = new CancellationTokenSource();
        var calls = 0;
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evalWithCancel = (p, token) => {
            // Cancel after a couple of evaluations.
            if (Interlocked.Increment(ref calls) >= 2) {
                cts.Cancel();
            }
            IReadOnlyList<RunEvaluationMetrics> runs = new[] { SyntheticRunFor(p) };
            return Task.FromResult(runs);
        };

        var variables = OptimizerVariable.CreateCuratedSet();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, evalWithCancel, DefaultSettings(), null, cts.Token));
    }

    [Test]
    public async Task Optimize_RespectsBoundsAndQuantization() {
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(), DefaultSettings(), null, CancellationToken.None);
        var p = result.BestParams;

        Assert.Multiple(() => {
            // Continuous bounds.
            Assert.That(p.Sensitivity, Is.InRange(0.0, 50.0));
            Assert.That(p.StarClippingMultiplier, Is.InRange(0.25, 10.0));
            Assert.That(p.NoiseClippingMultiplier, Is.InRange(1.0, 10.0));
            Assert.That(p.PeakResponse, Is.InRange(0.1, 1.0));
            Assert.That(p.MaxDistortion, Is.InRange(0.1, 1.0));
            Assert.That(p.MinHFR, Is.InRange(0.1, 5.0));
            Assert.That(p.StarCenterTolerance, Is.InRange(0.05, 1.0));
            Assert.That(p.HotpixelThreshold, Is.InRange(0.0001, 0.05));
            // Integers are whole and bounded.
            Assert.That(p.StructureLayers, Is.InRange(1, 8));
            Assert.That(p.NoiseReductionRadius, Is.InRange(0, 10));
            Assert.That(p.MinimumStarBoundingBoxSize, Is.InRange(2, 20));
        });
    }

    [Test]
    public async Task Optimize_ReportsProgress() {
        // Use a SYNCHRONOUS IProgress double instead of Progress<T>, which posts to the captured
        // SynchronizationContext and may not drain deterministically under NUnit.
        var progress = new SynchronousProgress<OptimizationProgress>();
        var settings = DefaultSettings();
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(), settings, progress, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Evaluations, Is.GreaterThan(0));
            Assert.That(progress.Reports, Is.Not.Empty, "at least one progress report must be received");
            Assert.That(progress.Reports.All(r => r.MaxEvaluations == settings.MaxEvaluations), Is.True,
                "every report carries the configured MaxEvaluations");
            Assert.That(progress.Reports.Any(r => !string.IsNullOrEmpty(r.Phase)), Is.True,
                "at least one report carries a non-empty Phase");
        });
    }

    [Test]
    public async Task Optimize_ChangedVariables_OnlyListsDifferences() {
        var variables = OptimizerVariable.CreateCuratedSet();
        var seed = Seed();
        var result = await new StarDetectionOptimizer().OptimizeAsync(seed, variables, SyntheticEvaluator(), DefaultSettings(), null, CancellationToken.None);
        foreach (var (name, seedValue, bestValue) in result.ChangedVariables) {
            Assert.That(seedValue, Is.Not.EqualTo(bestValue), $"{name} listed as changed but values equal");
        }
        // Sensitivity should have changed (it moves from 2 toward 10).
        Assert.That(result.ChangedVariables.Any(cv => cv.Name == nameof(StarDetectorParams.Sensitivity)), Is.True);
    }

    [Test]
    [Timeout(30000)]
    public void Optimize_AllDiscreteVariables_Terminates() {
        // REGRESSION (BLOCKING): an all-Integer/Boolean variable set used to hang forever. With no continuous
        // variables, ContinuousStepsBelowFloor returned false, so Phase-B's loop guard
        // (while !BudgetExhausted && !ContinuousStepsBelowFloor) never exited: once the discrete axes were
        // exhausted every proposed candidate was a memo hit, Evaluations never advanced, and BudgetExhausted
        // never tripped. The fix returns true when there are no continuous variables, letting the
        // no-improving-move logic end the search. The [Timeout] guards against a hang before the fix.
        var discreteVariables = OptimizerVariable.CreateCuratedSet()
            .Where(v => v.Type != OptimizerVariableType.Continuous)
            .ToList();
        // Sanity: the curated set has at least StructureLayers + HotpixelThresholdingEnabled (no continuous).
        Assert.That(discreteVariables, Is.Not.Empty);
        Assert.That(discreteVariables.All(v => v.Type != OptimizerVariableType.Continuous), Is.True);

        var settings = DefaultSettings();
        var task = new StarDetectionOptimizer().OptimizeAsync(
            Seed(), discreteVariables, SyntheticEvaluator(), settings, null, CancellationToken.None);

        // Before the fix this never completes; the [Timeout] catches the hang. After the fix it returns.
        Assert.That(task.Wait(TimeSpan.FromSeconds(30)), Is.True, "OptimizeAsync must terminate for an all-discrete variable set");
        var result = task.Result;
        Assert.That(result.Evaluations, Is.LessThanOrEqualTo(settings.MaxEvaluations), "must respect the eval budget");
    }

    // ----- T12 (F6): per-axis adaptive Phase-A coarse-grid resolution -----

    // Builds a minimal two-axis curated subset (Sensitivity + StarClippingMultiplier, both Continuous) so the
    // Phase-A grid is the ONLY thing exercising those axes; lets a test read the grid sampling directly off the
    // recorded candidates without curated-set Phase-B noise from other variables mixing in.
    private static OptimizerVariable ContinuousVar(string name, double lower, double upper, double step,
        Func<StarDetectorParams, double> read, Action<StarDetectorParams, double> store) {
        OptimizerVariable v = null;
        v = new OptimizerVariable {
            Name = name, Type = OptimizerVariableType.Continuous,
            Lower = lower, Upper = upper, InitialStep = step,
            Read = read,
            Write = (p, raw) => store(p, v.Quantize(raw))
        };
        return v;
    }

    // Expected per-axis level count, mirroring StarDetectionOptimizer.SearchContext.AxisLevels exactly. Kept as
    // an independent reimplementation so the tests pin the published formula, not the production code's call.
    private static int ExpectedAxisLevels(double lower, double upper, double step, OptimizerSettings s) {
        var min = Math.Max(2, s.CoarseGridLevels);
        var max = Math.Max(min, s.CoarseGridMaxLevelsPerAxis);
        if (step <= 0.0 || s.CoarseGridSpacingFactor <= 0.0) {
            return min;
        }
        var raw = 1 + (int)Math.Round((upper - lower) / (step * s.CoarseGridSpacingFactor), MidpointRounding.AwayFromZero);
        return Math.Clamp(raw, min, max);
    }

    [Test]
    public void AxisLevels_WideAxis_HitsMax_NarrowAxis_HitsMin_AndIsClamped() {
        var s = new OptimizerSettings { CoarseGridLevels = 4, CoarseGridMaxLevelsPerAxis = 9, CoarseGridSpacingFactor = 6.0 };

        // Wide Sensitivity axis: range 50, step 1 => 1 + round(50/6) = 9 => caps at CoarseGridMaxLevelsPerAxis.
        Assert.That(ExpectedAxisLevels(0.0, 50.0, 1.0, s), Is.EqualTo(9));
        // Narrow StarClipping axis: range 9.75, step 0.5 => 1 + round(9.75/3) = 4 => the minimum.
        Assert.That(ExpectedAxisLevels(0.25, 10.0, 0.5, s), Is.EqualTo(4));
        // A genuinely tiny axis floors at the minimum.
        Assert.That(ExpectedAxisLevels(0.0, 1.0, 1.0, s), Is.EqualTo(4));
        // An extremely wide axis is clamped to the maximum.
        Assert.That(ExpectedAxisLevels(0.0, 10000.0, 1.0, s), Is.EqualTo(9));
        // Non-positive step falls back to the minimum (no meaningful spacing).
        Assert.That(ExpectedAxisLevels(0.0, 50.0, 0.0, s), Is.EqualTo(4));
        Assert.That(ExpectedAxisLevels(0.0, 50.0, -1.0, s), Is.EqualTo(4));
        // Always within [min, max].
        Assert.That(ExpectedAxisLevels(0.0, 50.0, 1.0, s), Is.InRange(4, 9));
    }

    [Test]
    public async Task CoarseGrid_PerAxisLevels_SampleBothBounds_AndExpectedSpacing() {
        // Two-axis grid only: Sensitivity (wide, 9 levels) × StarClipping (narrow, 4 levels). The Phase-A grid
        // must sample each axis at every evenly-spaced level inclusive of both bounds. We record every evaluated
        // candidate and inspect the distinct values the grid sampled on each axis.
        var sensSamples = new HashSet<double>();
        var clipSamples = new HashSet<double>();
        Action<StarDetectorParams> onCall = p => {
            sensSamples.Add(p.Sensitivity);
            clipSamples.Add(p.StarClippingMultiplier);
        };

        var variables = new List<OptimizerVariable> {
            ContinuousVar(nameof(StarDetectorParams.Sensitivity), 0.0, 50.0, 1.0,
                p => p.Sensitivity, (p, v) => p.Sensitivity = v),
            ContinuousVar(nameof(StarDetectorParams.StarClippingMultiplier), 0.25, 10.0, 0.5,
                p => p.StarClippingMultiplier, (p, v) => p.StarClippingMultiplier = v),
        };
        var settings = DefaultSettings();
        var levelsA = ExpectedAxisLevels(0.0, 50.0, 1.0, settings);   // 9
        var levelsB = ExpectedAxisLevels(0.25, 10.0, 0.5, settings);  // 4
        Assume.That(levelsA, Is.EqualTo(9));
        Assume.That(levelsB, Is.EqualTo(4));

        await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(onCall), settings, null, CancellationToken.None);

        // Both bounds sampled on each axis (level 0 -> Lower, level n-1 -> Upper).
        Assert.Multiple(() => {
            Assert.That(sensSamples, Does.Contain(0.0), "Sensitivity Lower bound must be sampled");
            Assert.That(sensSamples, Does.Contain(50.0), "Sensitivity Upper bound must be sampled");
            Assert.That(clipSamples, Does.Contain(0.25), "StarClipping Lower bound must be sampled");
            Assert.That(clipSamples, Does.Contain(10.0), "StarClipping Upper bound must be sampled");

            // Every expected evenly-spaced grid level appears among the sampled values.
            for (var ia = 0; ia < levelsA; ia++) {
                var expected = 0.0 + ((double)ia / (levelsA - 1)) * 50.0;
                Assert.That(sensSamples.Any(x => Math.Abs(x - expected) < 1e-9), Is.True,
                    $"Sensitivity grid level {ia} (={expected}) must be sampled");
            }
            for (var ib = 0; ib < levelsB; ib++) {
                var expected = 0.25 + ((double)ib / (levelsB - 1)) * (10.0 - 0.25);
                Assert.That(clipSamples.Any(x => Math.Abs(x - expected) < 1e-9), Is.True,
                    $"StarClipping grid level {ib} (={expected}) must be sampled");
            }
        });
    }

    [Test]
    public async Task CoarseGrid_PerAxisLevels_AreDeterministic_SameSequenceOfGridPoints() {
        // Same inputs => identical ORDERED sequence of evaluated candidates (pure function of bounds/step; no RNG).
        var variables = new List<OptimizerVariable> {
            ContinuousVar(nameof(StarDetectorParams.Sensitivity), 0.0, 50.0, 1.0,
                p => p.Sensitivity, (p, v) => p.Sensitivity = v),
            ContinuousVar(nameof(StarDetectorParams.StarClippingMultiplier), 0.25, 10.0, 0.5,
                p => p.StarClippingMultiplier, (p, v) => p.StarClippingMultiplier = v),
        };

        List<(double, double)> Record() {
            var seq = new List<(double, double)>();
            Action<StarDetectorParams> onCall = p => seq.Add((p.Sensitivity, p.StarClippingMultiplier));
            // Synchronous evaluator + single-threaded await => deterministic recording order.
            new StarDetectionOptimizer()
                .OptimizeAsync(Seed(), variables, SyntheticEvaluator(onCall), DefaultSettings(), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            return seq;
        }

        var s1 = Record();
        var s2 = Record();
        Assert.That(s1, Is.EqualTo(s2), "the ordered sequence of evaluated grid candidates must be deterministic");
    }

    [Test]
    public async Task CoarseGrid_MissingGridAxis_StillRunsAndNeverRegresses() {
        // Only ONE of the two grid axes present (Sensitivity). The missing StarClipping axis contributes a
        // single level (just the incumbent), so the grid is levelsA × 1. Optimizer must still run, respect the
        // budget, and never return below the seed J.
        var variables = new List<OptimizerVariable> {
            ContinuousVar(nameof(StarDetectorParams.Sensitivity), 0.0, 50.0, 1.0,
                p => p.Sensitivity, (p, v) => p.Sensitivity = v),
        };
        var settings = DefaultSettings();
        var result = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(), settings, null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ), "must never regress below seed");
            Assert.That(result.Evaluations, Is.LessThanOrEqualTo(settings.MaxEvaluations), "must respect budget");
            // Sensitivity bounds must both have been reachable as grid samples; the best lands within bounds.
            Assert.That(result.BestParams.Sensitivity, Is.InRange(0.0, 50.0));
        });
    }

    /// <summary>
    /// A synchronous <see cref="IProgress{T}"/> test double: <see cref="Report"/> appends directly to a list
    /// on the calling thread, so reports are captured deterministically (unlike <see cref="Progress{T}"/>,
    /// which posts to the captured SynchronizationContext).
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T> {
        public List<T> Reports { get; } = new List<T>();

        public void Report(T value) => Reports.Add(value);
    }
}
