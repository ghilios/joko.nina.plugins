#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>Tuning knobs for the search engine (not the objective — those live in <see cref="ObjectiveConstants"/>).</summary>
    public sealed class OptimizerSettings {
        /// <summary>
        /// Hard cap on evaluator invocations (cache misses). The search never exceeds this. Default 250: a
        /// bank-wide convergence study (docs/star-detection-optimizer-speedup-results.md) showed the search
        /// self-terminates well before 400 on most runs and that capping at 250 costs ≤0.011% J worst-case while
        /// cutting wall-clock on the long runs — the late evals are disproportionately expensive EARLY rebuilds.
        /// </summary>
        public int MaxEvaluations { get; set; } = 250;

        /// <summary>Number of grid levels per axis in the Phase-A coarse seed (over the 2 highest-impact axes).</summary>
        public int CoarseGridLevels { get; set; } = 4;

        /// <summary>
        /// Phase-B stops halving a Continuous variable's step once it drops below InitialStep × this fraction.
        /// </summary>
        public double StepFloorFraction { get; set; } = 0.125;
    }

    /// <summary>Progress payload emitted during <see cref="StarDetectionOptimizer.OptimizeAsync"/>.</summary>
    public sealed class OptimizationProgress {
        public int Evaluations { get; set; }
        public int MaxEvaluations { get; set; }
        public double BestJ { get; set; }
        public double SeedJ { get; set; }
        public string Phase { get; set; }
    }

    /// <summary>Outcome of an optimization run.</summary>
    public sealed class OptimizationResult {
        public StarDetectorParams BestParams { get; set; }
        public double BestJ { get; set; }
        public double SeedJ { get; set; }
        public int Evaluations { get; set; }
        public bool ImprovedOverSeed { get; set; }
        public IReadOnlyList<(string Name, double SeedValue, double BestValue)> ChangedVariables { get; set; }
    }

    /// <summary>
    /// A pure, derivative-free optimizer over star-detection parameters. It depends only on an INJECTED
    /// evaluator delegate (params → per-run metrics); it never loads images, runs detection, or touches WPF.
    /// The search is fully deterministic (no RNG, fixed evaluation order) and memoized on
    /// <see cref="StarDetector.ComputeCacheKey"/> so revisited points never re-invoke the evaluator.
    ///
    /// Strategy:
    ///   Phase A — a coarse grid over the two highest-impact axes (Sensitivity × StarClippingMultiplier).
    ///   Phase B — a STAGED compass/pattern search. The curated axes are partitioned into LATE (cheap — a move
    ///             is a per-frame early-context cache hit) and EARLY (expensive — a move rebuilds AND evicts the
    ///             cached early DetectionContext). It alternates a LATE stage (compass over the late axes only,
    ///             early params pinned) with a bounded EARLY stage (compass over the 5 early axes), repeating
    ///             while a round improves; the early stage is permanently skipped once it stops improving. Each
    ///             stage is itself a compass: from the incumbent, try ±step on every axis in the subset, accept
    ///             the single best strictly-improving move; when a sweep finds none, halve the subset's Continuous
    ///             steps; stop when all Continuous steps fall below their floor (or the eval budget is exhausted).
    /// Because the seed is the initial incumbent and only strictly-improving moves are accepted, the result can
    /// never be worse than the seed; staging changes the visit order, not the reachable set.
    /// </summary>
    public sealed class StarDetectionOptimizer {
        private readonly ObjectiveConstants constants;

        public StarDetectionOptimizer(ObjectiveConstants constants = null) {
            this.constants = constants ?? new ObjectiveConstants();
        }

        public async Task<OptimizationResult> OptimizeAsync(
            StarDetectorParams seed,
            IReadOnlyList<OptimizerVariable> variables,
            Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator,
            OptimizerSettings settings,
            IProgress<OptimizationProgress> progress,
            CancellationToken token) {
            if (seed == null) {
                throw new ArgumentNullException(nameof(seed));
            }
            if (variables == null || variables.Count == 0) {
                throw new ArgumentException("At least one variable is required", nameof(variables));
            }
            if (evaluator == null) {
                throw new ArgumentNullException(nameof(evaluator));
            }
            settings = settings ?? new OptimizerSettings();

            var ctx = new SearchContext(this, seed, variables, evaluator, settings, progress, token);

            // θ0 = read each variable from the seed.
            var theta0 = new double[variables.Count];
            for (var i = 0; i < variables.Count; i++) {
                theta0[i] = variables[i].Quantize(variables[i].Read(seed));
            }

            // Seed evaluation; this is the initial incumbent and the never-regress floor.
            var seedJ = await ctx.EvalJ(theta0).ConfigureAwait(false);
            var bestTheta = (double[])theta0.Clone();
            var bestJ = seedJ;

            ctx.Report("Seed", bestJ, seedJ);

            // Phase A — coarse grid over the two highest-impact axes.
            (bestTheta, bestJ) = await ctx.CoarseGrid(bestTheta, bestJ, seedJ).ConfigureAwait(false);

            // Phase B — compass/pattern search.
            (bestTheta, bestJ) = await ctx.PatternSearch(bestTheta, bestJ, seedJ).ConfigureAwait(false);

            // Materialize the winning params.
            var bestParams = ctx.Materialize(bestTheta);

            // ChangedVariables: seed vs best, only where they differ.
            var changed = new List<(string Name, double SeedValue, double BestValue)>();
            for (var i = 0; i < variables.Count; i++) {
                if (theta0[i] != bestTheta[i]) {
                    changed.Add((variables[i].Name, theta0[i], bestTheta[i]));
                }
            }

            return new OptimizationResult {
                BestParams = bestParams,
                BestJ = bestJ,
                SeedJ = seedJ,
                Evaluations = ctx.Evaluations,
                ImprovedOverSeed = bestJ > seedJ,
                ChangedVariables = changed
            };
        }

        /// <summary>
        /// Holds all mutable search state (memo, eval counter) plus the immutable inputs, so the search phases
        /// read as small deterministic methods.
        /// </summary>
        private sealed class SearchContext {
            // Fixed compass directions tried for every variable each sweep: + then −, deterministic order.
            private static readonly double[] Directions = { +1.0, -1.0 };

            private readonly StarDetectionOptimizer owner;
            private readonly StarDetectorParams seed;
            private readonly IReadOnlyList<OptimizerVariable> variables;
            private readonly Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator;
            private readonly OptimizerSettings settings;
            private readonly IProgress<OptimizationProgress> progress;
            private readonly CancellationToken token;
            private readonly Dictionary<string, double> memo = new Dictionary<string, double>(StringComparer.Ordinal);

            // Phase-B staging partition (T14): the curated axes split into EARLY (members of
            // StarDetector.EarlyCacheKeyProperties — each move both rebuilds AND evicts the per-frame early
            // DetectionContext, a ~1.65s full detection) and LATE (everything else, including the synthetic
            // DefocusAwareGates axis — these refine via cache hits when the early params are held fixed).
            // Membership is sourced ONCE from StarDetector.IsEarlyCacheKeyParameter (no second copy of the list).
            // Index order within each subset preserves the curated order, so the staged sweep stays deterministic.
            private readonly int[] earlyIndices;
            private readonly int[] lateIndices;

            public int Evaluations { get; private set; }

            public SearchContext(
                StarDetectionOptimizer owner,
                StarDetectorParams seed,
                IReadOnlyList<OptimizerVariable> variables,
                Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator,
                OptimizerSettings settings,
                IProgress<OptimizationProgress> progress,
                CancellationToken token) {
                this.owner = owner;
                this.seed = seed;
                this.variables = variables;
                this.evaluator = evaluator;
                this.settings = settings;
                this.progress = progress;
                this.token = token;

                var early = new List<int>();
                var late = new List<int>();
                for (var i = 0; i < variables.Count; i++) {
                    if (StarDetector.IsEarlyCacheKeyParameter(variables[i].Name)) {
                        early.Add(i);
                    } else {
                        late.Add(i);
                    }
                }
                earlyIndices = early.ToArray();
                lateIndices = late.ToArray();
            }

            /// <summary>Clones the seed and writes the candidate vector through each variable's Write.</summary>
            public StarDetectorParams Materialize(double[] theta) {
                var p = seed.Clone();
                for (var i = 0; i < variables.Count; i++) {
                    variables[i].Write(p, theta[i]);
                }
                return p;
            }

            /// <summary>
            /// Memoized objective. Materializes params, keys on <see cref="StarDetector.ComputeCacheKey"/>, and
            /// only invokes the evaluator on a cache MISS (incrementing the eval counter then). J is the
            /// multi-run aggregate <see cref="OptimizationObjective.JTotal"/> over per-run J. Honors cancellation.
            /// </summary>
            public async Task<double> EvalJ(double[] theta) {
                token.ThrowIfCancellationRequested();
                var p = Materialize(theta);
                var key = StarDetector.ComputeCacheKey(p);
                if (memo.TryGetValue(key, out var cached)) {
                    return cached;
                }

                var runMetrics = await evaluator(p, token).ConfigureAwait(false);
                Evaluations++;

                double j;
                if (runMetrics == null || runMetrics.Count == 0) {
                    j = 0.0;
                } else {
                    var perRunJ = runMetrics
                        .Select(m => OptimizationObjective.JRun(m, owner.constants))
                        .ToList();
                    j = OptimizationObjective.JTotal(perRunJ, owner.constants);
                }

                memo[key] = j;
                return j;
            }

            /// <summary>True once the eval budget is spent. We never start an evaluation past the cap.</summary>
            public bool BudgetExhausted => Evaluations >= settings.MaxEvaluations;

            public void Report(string phase, double bestJ, double seedJ) {
                progress?.Report(new OptimizationProgress {
                    Evaluations = Evaluations,
                    MaxEvaluations = settings.MaxEvaluations,
                    BestJ = bestJ,
                    SeedJ = seedJ,
                    Phase = phase
                });
            }

            /// <summary>
            /// Phase A. Grid over the two highest-impact axes (Sensitivity × StarClippingMultiplier) with
            /// CoarseGridLevels evenly-spaced levels each spanning [Lower, Upper]; all other variables held at
            /// the incumbent. Deterministic iteration order. Keeps the best strictly-improving point.
            /// </summary>
            public async Task<(double[] theta, double j)> CoarseGrid(double[] bestTheta, double bestJ, double seedJ) {
                var axisA = IndexOf(nameof(StarDetectorParams.Sensitivity));
                var axisB = IndexOf(nameof(StarDetectorParams.StarClippingMultiplier));
                var levels = Math.Max(2, settings.CoarseGridLevels);

                for (var ia = 0; ia < levels; ia++) {
                    for (var ib = 0; ib < levels; ib++) {
                        if (BudgetExhausted) {
                            return (bestTheta, bestJ);
                        }
                        var candidate = (double[])bestTheta.Clone();
                        if (axisA >= 0) {
                            candidate[axisA] = variables[axisA].Quantize(GridValue(variables[axisA], ia, levels));
                        }
                        if (axisB >= 0) {
                            candidate[axisB] = variables[axisB].Quantize(GridValue(variables[axisB], ib, levels));
                        }
                        var j = await EvalJ(candidate).ConfigureAwait(false);
                        if (j > bestJ) {
                            bestJ = j;
                            bestTheta = candidate;
                        }
                    }
                }
                Report("CoarseGrid", bestJ, seedJ);
                return (bestTheta, bestJ);
            }

            private static double GridValue(OptimizerVariable v, int level, int levels) {
                // Evenly spaced inclusive of both bounds: level 0 => Lower, level (levels-1) => Upper.
                var t = (double)level / (levels - 1);
                return v.Lower + t * (v.Upper - v.Lower);
            }

            /// <summary>
            /// Phase B (T14 staged). The curated axes are partitioned into LATE (cache-hit-cheap) and EARLY
            /// (each move forces a ~1.65s early-context rebuild AND evicts the incumbent's cached context). The
            /// old single loop re-probed all 5 EARLY axes on every sweep — 46% of detections were full rebuilds.
            /// We instead stage the compass:
            ///   LATE stage  — run the compass over the LATE axes only, holding the early params fixed (so every
            ///                 late probe is a per-frame cache hit), halving late Continuous steps to their floor.
            ///   EARLY stage — one bounded compass over the EARLY axes (also halving to floor), then the next late
            ///                 stage refines from the rebuilt-once early context via cache hits.
            ///   OUTER loop  — alternate LATE→EARLY; repeat while a full round improved AND budget remains. The
            ///                 early stage is permanently skipped (earlyExhausted) once an early stage yields no
            ///                 improvement, so the EARLY axes are not re-probed indefinitely.
            /// The search remains deterministic and never-regress: it starts from the incumbent and accepts only
            /// strictly-improving moves; the staged subsets do not change which points are reachable, only the
            /// order they are visited. With no early axes the early stage is a no-op (pure late search); with no
            /// late axes the late stage is a no-op (pure early search) — both degrade to the old behavior.
            /// </summary>
            public async Task<(double[] theta, double j)> PatternSearch(double[] bestTheta, double bestJ, double seedJ) {
                var earlyExhausted = earlyIndices.Length == 0; // no early axes => never run the early stage

                while (!BudgetExhausted) {
                    var roundStartJ = bestJ;

                    // LATE stage: refine cheaply with the early params pinned (all per-frame cache hits).
                    (bestTheta, bestJ) = await CompassStage(lateIndices, bestTheta, bestJ, seedJ).ConfigureAwait(false);

                    if (BudgetExhausted) {
                        break;
                    }

                    // EARLY stage: one bounded refinement over the 5 early axes (skipped once exhausted).
                    if (!earlyExhausted) {
                        var beforeEarlyJ = bestJ;
                        (bestTheta, bestJ) = await CompassStage(earlyIndices, bestTheta, bestJ, seedJ).ConfigureAwait(false);
                        if (bestJ <= beforeEarlyJ) {
                            // The early axes produced nothing new — don't probe them again in later rounds.
                            earlyExhausted = true;
                        }
                    }

                    // Outer loop terminates when a full round (late + early) made no progress, or the budget is
                    // spent. Once early is exhausted, a round is just the late stage; if late also can't improve
                    // (roundStartJ unchanged) we stop.
                    if (bestJ <= roundStartJ) {
                        break;
                    }
                }

                Report("PatternSearch", bestJ, seedJ);
                return (bestTheta, bestJ);
            }

            /// <summary>
            /// One compass/pattern search restricted to the axes in <paramref name="axisIndices"/> (a fixed,
            /// deterministic subset of the curated variables). Each sweep proposes, for every axis in the subset,
            /// +step and −step (Integer: ±max(1, round(step)); Boolean: flip; Continuous: ±step) in the subset's
            /// fixed order, evaluates them, and takes the single best STRICTLY-improving move (ties => no move). A
            /// sweep with no improving move halves every Continuous step IN THE SUBSET. The stage ends when all
            /// Continuous steps in the subset fall below their floor (InitialStep × StepFloorFraction) — or, if the
            /// subset has no Continuous axes, once a sweep finds no improving move — or the budget is exhausted.
            /// Returns the (possibly improved) incumbent. Steps are local to the stage (a fresh start each call).
            /// </summary>
            private async Task<(double[] theta, double j)> CompassStage(int[] axisIndices, double[] bestTheta, double bestJ, double seedJ) {
                if (axisIndices.Length == 0) {
                    return (bestTheta, bestJ);
                }

                // Per-axis step for the subset (only Continuous steps shrink). Reset at the start of each stage.
                var steps = new double[variables.Count];
                for (var k = 0; k < axisIndices.Length; k++) {
                    var i = axisIndices[k];
                    steps[i] = variables[i].InitialStep;
                }

                while (!BudgetExhausted && !ContinuousStepsBelowFloor(steps, axisIndices)) {
                    double improvedJ = bestJ;
                    double[] improvedTheta = null;

                    for (var k = 0; k < axisIndices.Length; k++) {
                        var i = axisIndices[k];
                        var v = variables[i];
                        foreach (var direction in Directions) {
                            if (BudgetExhausted) {
                                break;
                            }
                            var candidate = ProposeMove(bestTheta, i, v, steps[i], direction);
                            if (candidate == null) {
                                continue; // move produced no change (e.g. already at bound, or boolean no-op)
                            }
                            var j = await EvalJ(candidate).ConfigureAwait(false);
                            if (j > improvedJ) {
                                improvedJ = j;
                                improvedTheta = candidate;
                            }
                        }
                        if (BudgetExhausted) {
                            break;
                        }
                    }

                    if (improvedTheta != null) {
                        // Accept the single best strictly-improving move and re-sweep from there.
                        bestTheta = improvedTheta;
                        bestJ = improvedJ;
                        Report("PatternSearch", bestJ, seedJ);
                    } else {
                        // No improving move: refine the subset's Continuous steps and sweep again.
                        HalveContinuousSteps(steps, axisIndices);
                    }
                }
                return (bestTheta, bestJ);
            }

            /// <summary>
            /// Builds a candidate vector for moving variable <paramref name="i"/> by ±step. Returns null when
            /// the move yields no change after quantization/clamping (so it is not evaluated needlessly).
            /// </summary>
            private double[] ProposeMove(double[] theta, int i, OptimizerVariable v, double step, double direction) {
                double proposed;
                switch (v.Type) {
                    case OptimizerVariableType.Boolean:
                        // Flip; direction is irrelevant for a binary axis, so only +1 produces a move (the −1
                        // branch will yield the same flipped value and be deduped by the memo, but we still
                        // suppress it here to keep candidate sets minimal and deterministic).
                        if (direction < 0) {
                            return null;
                        }
                        proposed = theta[i] >= 0.5 ? 0.0 : 1.0;
                        break;

                    case OptimizerVariableType.Integer:
                        var intStep = Math.Max(1.0, Math.Round(step, MidpointRounding.AwayFromZero));
                        proposed = theta[i] + direction * intStep;
                        break;

                    default: // Continuous
                        proposed = theta[i] + direction * step;
                        break;
                }

                var quantized = v.Quantize(proposed);
                if (quantized == theta[i]) {
                    return null; // no movement
                }
                var candidate = (double[])theta.Clone();
                candidate[i] = quantized;
                return candidate;
            }

            private bool ContinuousStepsBelowFloor(double[] steps, int[] axisIndices) {
                var anyContinuous = false;
                for (var k = 0; k < axisIndices.Length; k++) {
                    var i = axisIndices[k];
                    if (variables[i].Type != OptimizerVariableType.Continuous) {
                        continue;
                    }
                    anyContinuous = true;
                    var floor = variables[i].InitialStep * settings.StepFloorFraction;
                    if (steps[i] >= floor) {
                        return false; // at least one continuous step still above its floor
                    }
                }
                if (!anyContinuous) {
                    // No continuous variables in this subset: there is nothing to refine, so treat as "below
                    // floor" and let the no-improving-move logic end the stage. This is the critical guard —
                    // without it an all-Integer/Boolean subset loops forever once the discrete axes are
                    // exhausted, since every proposed candidate is then a memo hit, Evaluations never advances,
                    // and BudgetExhausted never trips.
                    return true;
                }
                // At least one continuous variable exists and we reached here only because every continuous step
                // has fallen below its floor — also nothing left to refine.
                return true;
            }

            private void HalveContinuousSteps(double[] steps, int[] axisIndices) {
                for (var k = 0; k < axisIndices.Length; k++) {
                    var i = axisIndices[k];
                    if (variables[i].Type == OptimizerVariableType.Continuous) {
                        steps[i] *= 0.5;
                    }
                }
            }

            private int IndexOf(string name) {
                for (var i = 0; i < variables.Count; i++) {
                    if (string.Equals(variables[i].Name, name, StringComparison.Ordinal)) {
                        return i;
                    }
                }
                return -1;
            }
        }
    }
}
