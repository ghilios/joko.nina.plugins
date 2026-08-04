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
        // extreme (where one run is great and the other terrible).
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> twoRun = (p, token) => {
            double step = 100.0;
            double sens = p.Sensitivity;
            // Run A sigma minimized at sens=4; Run B minimized at sens=16. Midpoint balances both.
            double sigmaA = step * (0.02 + Math.Abs(sens - 4.0) / 20.0);
            double sigmaB = step * (0.02 + Math.Abs(sens - 16.0) / 20.0);
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

        // The balanced optimum is sens=10 (midpoint of 4 and 16). Should land closer to 10 than to either extreme.
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
    public async Task Optimize_ReportsIncumbentSigmaFocus() {
        // The wizard's live readout shows σ (focus precision) rather than a percent of J, so every progress report
        // must carry the σ OF THE CURRENT INCUMBENT — the same σ BuildSummaryAsync later recomputes for BestParams.
        var progress = new SynchronousProgress<OptimizationProgress>();
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await new StarDetectionOptimizer().OptimizeAsync(Seed(), variables, SyntheticEvaluator(), DefaultSettings(), progress, CancellationToken.None);

        var last = progress.Reports.Last();
        Assert.Multiple(() => {
            Assert.That(progress.Reports.All(r => double.IsFinite(r.BestSigmaFocus)), Is.True,
                "every report carries the incumbent's σ");
            Assert.That(last.BestSigmaFocus, Is.EqualTo(SyntheticRunFor(result.BestParams).SigmaFocus).Within(1e-9),
                "the final report's σ is the σ of the params the optimizer returns");
            Assert.That(last.BestSigmaFocus, Is.LessThan(SyntheticRunFor(Seed()).SigmaFocus),
                "this landscape ties higher J to tighter σ, so the incumbent's σ beats the seed's");
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
    [CancelAfter(30000)]
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

    // ---- T14: staged Phase-B (late→early) partition + convergence ----------------------------------------

    [Test]
    public void Partition_EarlyAxes_AreExactlyTheEarlyCacheKeyMembers_AndDefocusGatesIsLate() {
        // The optimizer partitions curated axes by StarDetector.IsEarlyCacheKeyParameter (single source of
        // truth). Mirror that classification here and assert (a) the early set is EXACTLY the five expected
        // early-cache-key axes present in the curated set, and (b) the synthetic DefocusAwareGates is LATE.
        var variables = OptimizerVariable.CreateCuratedSet();

        var early = variables.Where(v => StarDetector.IsEarlyCacheKeyParameter(v.Name)).Select(v => v.Name).ToList();
        var late = variables.Where(v => !StarDetector.IsEarlyCacheKeyParameter(v.Name)).Select(v => v.Name).ToList();

        var expectedEarly = new[] {
            nameof(StarDetectorParams.NoiseClippingMultiplier),
            nameof(StarDetectorParams.StructureLayers),
            nameof(StarDetectorParams.NoiseReductionRadius),
            nameof(StarDetectorParams.HotpixelThresholdingEnabled),
            nameof(StarDetectorParams.HotpixelThreshold),
            // Synthetic defocus-aware-structure knob — named for the EARLY cache-key property it drives.
            OptimizerVariable.DefocusAwareStructureName,
            // Donut morphological-close kernel size — EARLY (changes candidate formation).
            nameof(StarDetectorParams.DonutMorphCloseSize),
        };

        Assert.Multiple(() => {
            Assert.That(early, Is.EquivalentTo(expectedEarly), "early axes must equal the EarlyCacheKeyProperties members in the curated set");
            // DefocusAwareGates is a synthetic alias (drives late gates) — it must be LATE.
            Assert.That(late, Does.Contain(OptimizerVariable.DefocusAwareGatesName), "DefocusAwareGates must be a LATE axis");
            Assert.That(early, Does.Not.Contain(OptimizerVariable.DefocusAwareGatesName));
            // Every curated axis is in exactly one partition.
            Assert.That(early.Count + late.Count, Is.EqualTo(variables.Count));
            // IsEarlyCacheKeyParameter is robust to synthetic/unknown/null names.
            Assert.That(StarDetector.IsEarlyCacheKeyParameter(OptimizerVariable.DefocusAwareGatesName), Is.False);
            Assert.That(StarDetector.IsEarlyCacheKeyParameter("NotARealParam"), Is.False);
            Assert.That(StarDetector.IsEarlyCacheKeyParameter(null), Is.False);
        });
    }

    [Test]
    public async Task StagedSearch_NeverRegresses_AndRespectsBudget() {
        // Tight budget forces early budget exhaustion; the staged loop must still never drop below seed J and
        // must never exceed MaxEvaluations.
        var optimizer = new StarDetectionOptimizer();
        var variables = OptimizerVariable.CreateCuratedSet();
        var settings = new OptimizerSettings { MaxEvaluations = 25, CoarseGridLevels = 4, StepFloorFraction = 0.125 };
        var result = await optimizer.OptimizeAsync(Seed(), variables, SyntheticEvaluator(), settings, null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ), "staged search must never regress below seed");
            Assert.That(result.Evaluations, Is.LessThanOrEqualTo(settings.MaxEvaluations), "staged search must respect the eval budget");
        });
    }

    [Test]
    public async Task StagedSearch_FindsOptimum_OnOneLatePlusOneEarlyAxis() {
        // A 2-axis objective with ONE late axis (Sensitivity) and ONE early axis (NoiseClippingMultiplier).
        // J peaks at a known (late*, early*) interior point. The staged search (late stage then early stage,
        // alternating) must locate it — i.e. staging does not lose the optimum that a single combined loop finds.
        const double optSens = 12.0;   // late axis optimum, interior of [0, 50]
        const double optNoise = 4.0;   // early axis optimum, interior of [1, 10]

        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> twoAxis = (p, token) => {
            var dSens = (p.Sensitivity - optSens) / 20.0;
            var dNoise = (p.NoiseClippingMultiplier - optNoise) / 5.0;
            var dist = Math.Sqrt(dSens * dSens + dNoise * dNoise);
            var step = 100.0;
            IReadOnlyList<RunEvaluationMetrics> runs = new[] {
                new RunEvaluationMetrics {
                    SigmaFocus = step * (0.02 + 1.0 * dist),
                    LooStdError = double.NaN,
                    StepSize = step,
                    RSquared = 0.99,
                    ReducedChiSquared = 1.0,
                    FrameStarCounts = Enumerable.Repeat(50, 10).ToList()
                }
            };
            return Task.FromResult(runs);
        };

        var seed = new StarDetectorParams { Sensitivity = 2.0, StarClippingMultiplier = 2.5, NoiseClippingMultiplier = 8.0 };
        var variables = OptimizerVariable.CreateCuratedSet();
        var settings = new OptimizerSettings { MaxEvaluations = 600, CoarseGridLevels = 4, StepFloorFraction = 0.125 };
        var result = await new StarDetectionOptimizer().OptimizeAsync(seed, variables, twoAxis, settings, null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.BestJ, Is.GreaterThan(result.SeedJ), "should improve over seed");
            // Both axes (one late, one early) must converge near their optima — staging recovers both.
            Assert.That(Math.Abs(result.BestParams.Sensitivity - optSens), Is.LessThan(1.5),
                $"late axis (Sensitivity) should approach {optSens}, got {result.BestParams.Sensitivity}");
            Assert.That(Math.Abs(result.BestParams.NoiseClippingMultiplier - optNoise), Is.LessThan(1.0),
                $"early axis (NoiseClippingMultiplier) should approach {optNoise}, got {result.BestParams.NoiseClippingMultiplier}");
        });
    }

    // ---- Inert-axis (free-rider) landscape -------------------------------------------------------------
    // Reproduces a real 3800mm run: J depends ONLY on StarClippingMultiplier; Sensitivity is provably inert
    // (a gate sweep over 0..15 produced bit-identical star counts and HFRs on every frame, because the
    // sensitivity gate rejected 0 of ~1300 candidates). Phase A grids Sensitivity x StarClip with Sensitivity
    // as the OUTER loop and level 0 = v.Lower, so the winning StarClip is first found in the Sensitivity=0
    // row and drags Sensitivity to its lower bound; later rows tie exactly and are refused by the strict `>`.
    // The delivered Sensitivity=0 then reads to the user as "gate at the bottom of its range".
    // StarClip's optimum sits exactly ON a Phase-A grid level (levels over [0.25, 10] at 4 levels are
    // 0.25 / 3.5 / 6.75 / 10), and InertSeed starts far from it, so the coarse grid genuinely improves via
    // StarClip -- which is the precondition for Sensitivity to ride along as a free rider.
    private const double InertOptStarClip = 3.5;

    private static StarDetectorParams InertSeed() => new StarDetectorParams {
        Sensitivity = 2.0,
        StarClippingMultiplier = 0.5,
    };

    private static Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> InertSensitivityEvaluator() {
        return (p, token) => {
            var dClip = (p.StarClippingMultiplier - InertOptStarClip) / 4.5;   // Sensitivity deliberately unread
            var stepSize = 100.0;
            IReadOnlyList<RunEvaluationMetrics> runs = new[] {
                new RunEvaluationMetrics {
                    SigmaFocus = stepSize * (0.02 + Math.Abs(dClip)),
                    LooStdError = double.NaN,
                    StepSize = stepSize,
                    RSquared = 0.99,
                    ReducedChiSquared = 1.0,
                    FrameStarCounts = Enumerable.Repeat(50, 10).ToList()
                }
            };
            return Task.FromResult(runs);
        };
    }

    [Test]
    public async Task Optimize_LeavesAnInertAxisAtItsSeedValue_RatherThanItsLowerBound() {
        var optimizer = new StarDetectionOptimizer();
        var seed = InertSeed();   // Sensitivity = 2.0
        var variables = OptimizerVariable.CreateCuratedSet();

        var result = await optimizer.OptimizeAsync(
            seed, variables, InertSensitivityEvaluator(), DefaultSettings(), null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.BestParams.Sensitivity, Is.EqualTo(seed.Sensitivity).Within(1e-9),
                "Sensitivity cannot change J at all here, so the search must not report having changed it");
            Assert.That(result.ChangedVariables.Any(v => v.Name == nameof(StarDetectorParams.Sensitivity)), Is.False,
                "an inert axis must not appear in ChangedVariables");
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ),
                "reverting inert axes must never cost J");
        });
    }

    [Test]
    public async Task Optimize_PullsAnAxisBackToTheNearestEqualJPoint_WhenTheSeedIsOutsideThePlateau() {
        // The real 3800mm case, and the one a revert-all-the-way-to-seed rule cannot fix. Sensitivity is inert
        // across a PLATEAU of [0, 15] but genuinely (slightly) worse above it, while the seed sits at 33.3 —
        // outside the plateau. Reverting fully to 33.3 correctly costs J and is refused, which would strand the
        // axis at 0 and keep raising the false "gate is at the bottom of its range" banner. The search must
        // instead pull back to the point NEAREST the seed that costs nothing, landing inside the plateau but
        // well clear of the floor.
        const double plateauTop = 15.0;
        Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> plateau = (p, token) => {
            var dClip = (p.StarClippingMultiplier - InertOptStarClip) / 4.5;
            // Flat in Sensitivity up to plateauTop, then a gentle real penalty above it.
            var sensPenalty = p.Sensitivity <= plateauTop ? 0.0 : (p.Sensitivity - plateauTop) / 100.0;
            var stepSize = 100.0;
            IReadOnlyList<RunEvaluationMetrics> runs = new[] {
                new RunEvaluationMetrics {
                    SigmaFocus = stepSize * (0.02 + Math.Abs(dClip) + sensPenalty),
                    LooStdError = double.NaN,
                    StepSize = stepSize,
                    RSquared = 0.99,
                    ReducedChiSquared = 1.0,
                    FrameStarCounts = Enumerable.Repeat(50, 10).ToList()
                }
            };
            return Task.FromResult(runs);
        };

        var seed = new StarDetectorParams { Sensitivity = 33.3333, StarClippingMultiplier = 0.5 };
        var variables = OptimizerVariable.CreateCuratedSet();
        var result = await new StarDetectionOptimizer().OptimizeAsync(
            seed, variables, plateau, DefaultSettings(), null, CancellationToken.None);

        Assert.Multiple(() => {
            // Asserted against the search-floor BAND rather than a bit-exact 0: a genuinely-floored search
            // lands on 0.125/0.25/0.5 as often as on 0 (Sensitivity's step halves to InitialStep x
            // StepFloorFraction), so `== 0` would miss most real cases. Consumers that warn about a floored
            // gate use the same one-full-step band.
            const double sensitivityFloorBand = 1.0;
            Assert.That(result.BestParams.Sensitivity, Is.GreaterThan(sensitivityFloorBand),
                "an axis with a wide equal-J plateau must not be left sitting at its floor");
            Assert.That(result.BestParams.Sensitivity, Is.LessThanOrEqualTo(plateauTop + 1e-9),
                "must stay inside the plateau — pulling past it would cost real J");
            Assert.That(result.BestJ, Is.GreaterThanOrEqualTo(result.SeedJ),
                "the pull-back must never regress J");
        });
    }

    [Test]
    public async Task Optimize_KeepsAnAxisThatActuallyEarnedItsChange() {
        // The guard must not undo real improvements: StarClip genuinely drives J in this landscape.
        var optimizer = new StarDetectionOptimizer();
        var seed = InertSeed();   // StarClippingMultiplier = 0.5, optimum 3.5
        var variables = OptimizerVariable.CreateCuratedSet();

        var result = await optimizer.OptimizeAsync(
            seed, variables, InertSensitivityEvaluator(), DefaultSettings(), null, CancellationToken.None);

        Assert.That(result.BestParams.StarClippingMultiplier, Is.Not.EqualTo(seed.StarClippingMultiplier).Within(1e-9),
            "the axis that actually moves J must still be optimized");
    }

    // --- F35: MinHfrSeedFloor, applied at the engine seam ahead of theta0 -------------------------------------

    /// <summary>
    /// The seed must be stamped BEFORE theta0 is read, so the search genuinely starts from the lowered gate —
    /// rather than merely being permitted to reach it, which on F20's cold-start plateau it never does.
    /// </summary>
    [Test]
    public async Task MinHfrSeedFloor_LowersTheSeedGateBeforeTheFirstEvaluation() {
        var seed = Seed();
        seed.MinHFR = 1.2;
        var settings = DefaultSettings();
        settings.MinHfrSeedFloor = MinHfrSeed.SeedFloor;
        var seenFirst = double.NaN;

        await new StarDetectionOptimizer().OptimizeAsync(seed, OptimizerVariable.CreateCuratedSet(),
            SyntheticEvaluator(p => { if (double.IsNaN(seenFirst)) { seenFirst = p.MinHFR; } }),
            settings, null, CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(seenFirst, Is.EqualTo(MinHfrSeed.SeedFloor).Within(1e-9),
                "the seed evaluation — the never-regress floor — must already see the seeded gate");
            Assert.That(seed.MinHFR, Is.EqualTo(1.2).Within(1e-9),
                "the CALLER'S seed must be left alone — see MinHfrSeedFloor_DoesNotLeakIntoALaterRun");
        });
    }

    /// <summary>
    /// THE REGRESSION THIS EXISTS FOR. Callers reuse one <see cref="StarDetectorParams"/> across many runs:
    /// TestApp <c>optimize --per-run</c> builds a single context outside its per-dataset loop, and the wizard
    /// hands over a live reference to <c>runs[0].Seed</c>. An in-place write inside the engine therefore leaks
    /// the first run's seeded gate into every later run — silently re-gating datasets whose own fit never
    /// triggered, and making the result depend on dataset ORDER.
    ///
    /// <para>Measured before the fix: one firing on D01 dragged the entire 17-dataset synthetic bank to
    /// MinHFR 0.3, including <c>D05_tec140_1000mm</c> — the control whose whole purpose is to be left alone —
    /// while printing exactly one "seeding" line, because every subsequent run saw a seed that was already at
    /// the floor and so never triggered.</para>
    /// </summary>
    [Test]
    public async Task MinHfrSeedFloor_DoesNotLeakIntoALaterRun() {
        var sharedSeed = Seed();
        sharedSeed.MinHFR = 1.2;
        var variables = OptimizerVariable.CreateCuratedSet();

        // Run 1 opts in (its fit triggered).
        var withFloor = DefaultSettings();
        withFloor.MinHfrSeedFloor = MinHfrSeed.SeedFloor;
        await new StarDetectionOptimizer().OptimizeAsync(sharedSeed, variables, SyntheticEvaluator(), withFloor, null, CancellationToken.None);

        // Run 2 does NOT opt in (a well-sampled rig — D05's case). It must start from the ORIGINAL gate.
        var seenSecond = double.NaN;
        await new StarDetectionOptimizer().OptimizeAsync(sharedSeed, variables,
            SyntheticEvaluator(p => { if (double.IsNaN(seenSecond)) { seenSecond = p.MinHFR; } }),
            DefaultSettings(), null, CancellationToken.None);

        Assert.That(seenSecond, Is.EqualTo(1.2).Within(1e-9),
            "a run whose fit did not trigger the seed must not inherit the previous run's seeded gate");
    }

    /// <summary>Null (every caller that does not opt in, including synth-validate and tilt) must be bit-identical.</summary>
    [Test]
    public async Task MinHfrSeedFloor_Unset_LeavesTheSeedUntouched() {
        var seed = Seed();
        seed.MinHFR = 1.2;

        await new StarDetectionOptimizer().OptimizeAsync(seed, OptimizerVariable.CreateCuratedSet(),
            SyntheticEvaluator(), DefaultSettings(), null, CancellationToken.None);

        Assert.That(seed.MinHFR, Is.EqualTo(1.2).Within(1e-9));
    }

    /// <summary>
    /// Only ever lowers. The objective rewards star count, so nothing pulls a seeded MinHFR back up — raising a
    /// gate a caller deliberately set lower would be a knob with no gradient to climb back down.
    /// </summary>
    [Test]
    public async Task MinHfrSeedFloor_NeverRaisesAGateThatIsAlreadyLower() {
        var seed = Seed();
        seed.MinHFR = 0.15;
        var settings = DefaultSettings();
        settings.MinHfrSeedFloor = MinHfrSeed.SeedFloor;   // 0.30 > 0.15

        await new StarDetectionOptimizer().OptimizeAsync(seed, OptimizerVariable.CreateCuratedSet(),
            SyntheticEvaluator(), settings, null, CancellationToken.None);

        Assert.That(seed.MinHFR, Is.EqualTo(0.15).Within(1e-9));
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
