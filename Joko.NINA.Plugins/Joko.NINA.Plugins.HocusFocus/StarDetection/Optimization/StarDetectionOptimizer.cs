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
        /// <summary>Hard cap on evaluator invocations (cache misses). The search never exceeds this.</summary>
        public int MaxEvaluations { get; set; } = 400;

        /// <summary>
        /// MINIMUM number of grid levels per axis in the Phase-A coarse seed (over the 2 highest-impact axes).
        /// Each axis gets at least this many levels; wide axes get more (up to
        /// <see cref="CoarseGridMaxLevelsPerAxis"/>) so the grid SPACING stays roughly consistent regardless of
        /// how wide the curated bounds are — see <see cref="CoarseGridSpacingFactor"/>.
        /// </summary>
        public int CoarseGridLevels { get; set; } = 4;

        /// <summary>
        /// MAXIMUM number of grid levels for any single Phase-A axis. Caps the coarse grid so its total size
        /// (levelsA × levelsB) stays bounded and Phase B retains budget within <see cref="MaxEvaluations"/>.
        /// With the defaults (Sensitivity [0,50]/step1 → 9, StarClipping [0.25,10]/step0.5 → 4) the grid is
        /// 9×4 = 36 evals, well within the 400-eval budget.
        /// </summary>
        public int CoarseGridMaxLevelsPerAxis { get; set; } = 9;

        /// <summary>
        /// Per-axis grid resolution control. The Phase-A level count for an axis is chosen so that adjacent
        /// grid samples are roughly <c>InitialStep × CoarseGridSpacingFactor</c> apart:
        /// <c>levels = Clamp(1 + round((Upper − Lower) / (InitialStep × CoarseGridSpacingFactor)),
        /// CoarseGridLevels, CoarseGridMaxLevelsPerAxis)</c>. A larger factor ⇒ coarser grid (fewer levels);
        /// a smaller factor ⇒ finer grid. Pure function of the bounds/step, so the search stays deterministic.
        /// </summary>
        public double CoarseGridSpacingFactor { get; set; } = 6.0;

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
    ///   Phase B — a compass/pattern search: from the incumbent, try ±step on every variable, accept the single
    ///             best strictly-improving move; when a sweep finds none, halve every Continuous step; stop when
    ///             all Continuous steps fall below their floor (or the eval budget is exhausted).
    /// Because the seed is the initial incumbent and only strictly-improving moves are accepted, the result can
    /// never be worse than the seed.
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
            /// Phase A. Grid over the two highest-impact axes (Sensitivity × StarClippingMultiplier). The number
            /// of evenly-spaced levels is computed PER AXIS (see <see cref="AxisLevels"/>) so the grid SPACING
            /// stays roughly consistent regardless of how wide each axis's curated bounds are: a wide axis (e.g.
            /// Sensitivity [0,50]) gets more levels than a narrow one (e.g. StarClipping [0.25,10]), each capped
            /// at <see cref="OptimizerSettings.CoarseGridMaxLevelsPerAxis"/>. Each axis spans [Lower, Upper]
            /// inclusively (level 0 → Lower, level n-1 → Upper), so optima AT a bound are still sampled; the win
            /// is finer interior sampling on wide axes. All other variables are held at the incumbent. The
            /// iteration order is fixed/deterministic. Keeps the best strictly-improving point.
            /// </summary>
            public async Task<(double[] theta, double j)> CoarseGrid(double[] bestTheta, double bestJ, double seedJ) {
                var axisA = IndexOf(nameof(StarDetectorParams.Sensitivity));
                var axisB = IndexOf(nameof(StarDetectorParams.StarClippingMultiplier));
                // A missing axis contributes a single "level" (the incumbent value, as before); a present axis
                // gets a per-axis count scaled by its range so spacing is consistent across axes.
                var levelsA = axisA >= 0 ? AxisLevels(variables[axisA]) : 1;
                var levelsB = axisB >= 0 ? AxisLevels(variables[axisB]) : 1;

                for (var ia = 0; ia < levelsA; ia++) {
                    for (var ib = 0; ib < levelsB; ib++) {
                        if (BudgetExhausted) {
                            return (bestTheta, bestJ);
                        }
                        var candidate = (double[])bestTheta.Clone();
                        if (axisA >= 0) {
                            candidate[axisA] = variables[axisA].Quantize(GridValue(variables[axisA], ia, levelsA));
                        }
                        if (axisB >= 0) {
                            candidate[axisB] = variables[axisB].Quantize(GridValue(variables[axisB], ib, levelsB));
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

            /// <summary>
            /// Per-axis Phase-A level count: scales with the axis's range so adjacent grid samples are roughly
            /// <c>InitialStep × CoarseGridSpacingFactor</c> apart, clamped to
            /// [<see cref="OptimizerSettings.CoarseGridLevels"/>, <see cref="OptimizerSettings.CoarseGridMaxLevelsPerAxis"/>].
            /// A non-positive InitialStep (no meaningful spacing) falls back to the minimum. Pure function of the
            /// variable's bounds/step and the settings — no RNG — so the search remains deterministic.
            /// </summary>
            private int AxisLevels(OptimizerVariable v) {
                var min = Math.Max(2, settings.CoarseGridLevels);
                var max = Math.Max(min, settings.CoarseGridMaxLevelsPerAxis);
                if (v.InitialStep <= 0.0 || settings.CoarseGridSpacingFactor <= 0.0) {
                    return min;
                }
                var raw = 1 + (int)Math.Round((v.Upper - v.Lower) / (v.InitialStep * settings.CoarseGridSpacingFactor),
                    MidpointRounding.AwayFromZero);
                if (raw < min) {
                    return min;
                }
                if (raw > max) {
                    return max;
                }
                return raw;
            }

            private static double GridValue(OptimizerVariable v, int level, int levels) {
                // Evenly spaced inclusive of both bounds: level 0 => Lower, level (levels-1) => Upper.
                // With a single level the axis collapses to its Lower bound (degenerate; not hit in practice
                // because a present axis always yields >= CoarseGridLevels >= 2 levels).
                if (levels <= 1) {
                    return v.Lower;
                }
                var t = (double)level / (levels - 1);
                return v.Lower + t * (v.Upper - v.Lower);
            }

            /// <summary>
            /// Phase B. Compass/pattern search. Each sweep proposes, for every variable, +step and −step
            /// (Integer: ±max(1, round(step)); Boolean: flip; Continuous: ±step) in a fixed order, evaluates
            /// all of them, and takes the single best STRICTLY-improving move (ties => no move). A sweep with no
            /// improving move halves every Continuous step. Stops when all Continuous steps fall below their
            /// floor (InitialStep × StepFloorFraction) or the budget is exhausted.
            /// </summary>
            public async Task<(double[] theta, double j)> PatternSearch(double[] bestTheta, double bestJ, double seedJ) {
                // Current per-variable step (only Continuous steps shrink).
                var steps = new double[variables.Count];
                for (var i = 0; i < variables.Count; i++) {
                    steps[i] = variables[i].InitialStep;
                }

                while (!BudgetExhausted && !ContinuousStepsBelowFloor(steps)) {
                    // Collect all candidate moves in a fixed deterministic order.
                    double improvedJ = bestJ;
                    double[] improvedTheta = null;

                    for (var i = 0; i < variables.Count; i++) {
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
                        // No improving move: refine the Continuous steps and sweep again.
                        HalveContinuousSteps(steps);
                    }
                }
                Report("PatternSearch", bestJ, seedJ);
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

            private bool ContinuousStepsBelowFloor(double[] steps) {
                var anyContinuous = false;
                for (var i = 0; i < variables.Count; i++) {
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
                    // No continuous variables at all: there is nothing to refine, so treat as "below floor" and
                    // let the no-improving-move logic end the search. This is the critical guard — without it an
                    // all-Integer/Boolean variable set loops forever once the discrete axes are exhausted, since
                    // every proposed candidate is then a memo hit, Evaluations never advances, and
                    // BudgetExhausted never trips.
                    return true;
                }
                // At least one continuous variable exists and we reached here only because every continuous step
                // has fallen below its floor — also nothing left to refine.
                return true;
            }

            private void HalveContinuousSteps(double[] steps) {
                for (var i = 0; i < variables.Count; i++) {
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
