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

        /// <summary>
        /// F35 — when set, <see cref="StarDetectorParams.MinHFR"/> is lowered to this value on the seed before the
        /// search reads θ0, so a rig whose stars are smaller than the shipped gate starts somewhere with a
        /// gradient instead of on the plateau where <c>J</c> is identically zero (F20).
        ///
        /// <para><b>The caller decides, the engine applies.</b> The trigger is
        /// <c>BestFit.Minimum.Y &lt;= MinHFR</c> from a fit taken BEFORE the search, and
        /// <see cref="RunEvaluationMetrics"/> — all this engine sees of an evaluation — carries the vertex X only
        /// (<c>BestFocusPosition</c>), never its Y. So the two callers that already hold a pre-search
        /// <c>RunEvaluationResult</c> compute the rule via <see cref="MinHfrSeed.Resolve"/> and pass the answer
        /// here; the clamp lives in one place, ahead of θ0, so the seeded value flows into the seed evaluation,
        /// into the never-regress floor, and into <c>RevertNeutralAxes</c>' notion of the seed.</para>
        ///
        /// <para>Null (the default) leaves every caller bit-identical — which is deliberately the case for
        /// <c>synth-validate</c> and <c>tilt</c>, neither of which fits a curve before optimizing.</para>
        /// </summary>
        public double? MinHfrSeedFloor { get; set; }
    }

    /// <summary>Progress payload emitted during <see cref="StarDetectionOptimizer.OptimizeAsync"/>.</summary>
    public sealed class OptimizationProgress {
        public int Evaluations { get; set; }
        public int MaxEvaluations { get; set; }
        public double BestJ { get; set; }
        public double SeedJ { get; set; }

        /// <summary>Mean focus σ of the current incumbent, aggregated over the runs that produced a finite σ (the
        /// same aggregation the wizard's summary applies to the winning params). NaN when no run yielded one. The
        /// wizard's live readout shows this, not a percent of <see cref="BestJ"/>, so it agrees with the summary.</summary>
        public double BestSigmaFocus { get; set; } = double.NaN;

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

            // F35 — seed MinHFR beneath the gate BEFORE θ0 is read, so the search starts from the lowered value
            // rather than merely being allowed to reach it. Only ever lowers: the objective rewards star count, so
            // nothing pulls a seeded MinHFR back up, and raising a gate a caller deliberately set lower would be a
            // knob with no gradient to climb back down. See MinHfrSeed for why the value is a sampling constant
            // and not derived from any measured HFR.
            if (settings.MinHfrSeedFloor is double minHfrFloor && minHfrFloor < seed.MinHFR) {
                seed.MinHFR = minHfrFloor;
            }

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

            ctx.Report("Seed", bestTheta, bestJ, seedJ);

            // Phase A — coarse grid over the two highest-impact axes.
            (bestTheta, bestJ) = await ctx.CoarseGrid(bestTheta, bestJ, seedJ).ConfigureAwait(false);

            // Phase B — compass/pattern search.
            (bestTheta, bestJ) = await ctx.PatternSearch(bestTheta, bestJ, seedJ).ConfigureAwait(false);

            // Phase C — revert every axis that did not earn its change (see RevertNeutralAxes).
            (bestTheta, bestJ) = await ctx.RevertNeutralAxes(theta0, bestTheta, bestJ, seedJ).ConfigureAwait(false);

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

            /// <summary>Phase-C pull-back fractions along incumbent → seed, MOST SEED-WARD FIRST so the first
            /// accepted one is the nearest-to-seed neutral point. Four coarse steps rather than a bisection: the
            /// question is "is this axis inert over a wide region", which does not need sub-step resolution, and a
            /// fixed ladder keeps the phase deterministic and its cost bounded at ≤4 evals per changed axis.</summary>
            private static readonly double[] PullBackFractions = { 1.0, 0.75, 0.5, 0.25 };

            private readonly StarDetectionOptimizer owner;
            private readonly StarDetectorParams seed;
            private readonly IReadOnlyList<OptimizerVariable> variables;
            private readonly Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> evaluator;
            private readonly OptimizerSettings settings;
            private readonly IProgress<OptimizationProgress> progress;
            private readonly CancellationToken token;
            private readonly Dictionary<string, double> memo = new Dictionary<string, double>(StringComparer.Ordinal);

            // σ of every evaluated candidate, keyed exactly like <see cref="memo"/>. Filled alongside J on a cache
            // miss (free — the metrics are already in hand) so a Report can look up the incumbent's σ without
            // re-evaluating, and without threading a second value through every search stage.
            private readonly Dictionary<string, double> memoSigma = new Dictionary<string, double>(StringComparer.Ordinal);

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
                memoSigma[key] = MeanFiniteSigmaFocus(runMetrics);
                return j;
            }

            /// <summary>Mean σ over the runs that produced a finite focus σ; NaN when none did. Mirrors the wizard's
            /// baseline/best σ aggregation so the reported σ is directly comparable to the summary's.</summary>
            private static double MeanFiniteSigmaFocus(IReadOnlyList<RunEvaluationMetrics> runMetrics) {
                if (runMetrics == null) {
                    return double.NaN;
                }
                var sum = 0.0;
                var count = 0;
                foreach (var m in runMetrics) {
                    if (double.IsFinite(m.SigmaFocus)) {
                        sum += m.SigmaFocus;
                        count++;
                    }
                }
                return count > 0 ? sum / count : double.NaN;
            }

            /// <summary>The memoized σ of an already-evaluated point. NaN if it was never evaluated.</summary>
            private double SigmaFor(double[] theta) =>
                memoSigma.TryGetValue(StarDetector.ComputeCacheKey(Materialize(theta)), out var s) ? s : double.NaN;

            /// <summary>True once the eval budget is spent. We never start an evaluation past the cap.</summary>
            public bool BudgetExhausted => Evaluations >= settings.MaxEvaluations;

            public void Report(string phase, double[] bestTheta, double bestJ, double seedJ) {
                progress?.Report(new OptimizationProgress {
                    Evaluations = Evaluations,
                    MaxEvaluations = settings.MaxEvaluations,
                    BestJ = bestJ,
                    SeedJ = seedJ,
                    BestSigmaFocus = SigmaFor(bestTheta),
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
                Report("CoarseGrid", bestTheta, bestJ, seedJ);
                return (bestTheta, bestJ);
            }

            /// <summary>
            /// Phase C. For every axis the search moved, try putting it BACK to its seed value and keep the
            /// revert when doing so costs no J. Deterministic order (variable order), one pass, at most one
            /// evaluation per changed axis — and most are memo hits, since the seed value on that axis was
            /// usually visited during the search.
            ///
            /// <para><b>Why this is needed.</b> An axis that cannot move J at all is a FREE RIDER: Phase A grids
            /// Sensitivity × StarClippingMultiplier with Sensitivity as the OUTER loop and level 0 = <c>Lower</c>,
            /// so the winning StarClip is first discovered in the <c>Sensitivity = Lower</c> row and drags
            /// Sensitivity to its lower bound. Later rows re-test the same StarClip at higher Sensitivity, score
            /// EXACTLY the same, and are refused by the strict <c>j &gt; bestJ</c> — so nothing can ever undo it.
            /// Phase B cannot undo it either: <see cref="CompassStage"/> moves one axis at a time and also demands
            /// strict improvement, so a flat axis is frozen wherever Phase A left it.</para>
            ///
            /// <para>Measured on a real 3800 mm run: sweeping the Sensitivity gate over 0–15 produced BIT-IDENTICAL
            /// star counts and median HFRs on all 11 frames (the gate rejected 0 of ~1300 candidates), yet the
            /// optimizer reported <c>Sensitivity: 33.3→0</c>. That spurious floor is user-visible — it raises the
            /// "star acceptance gate is at the bottom of its range" banner and drives
            /// <see cref="ExposureRecommender"/> to answer a question about exposure that the run never posed.</para>
            ///
            /// <para><b>Exact ties only.</b> The comparison is <c>j &gt;= bestJ</c> with no tolerance: a genuinely
            /// inert axis produces a bit-identical J (identical detector inputs ⇒ identical metrics), so no epsilon
            /// is required to catch it, and introducing one would let this phase trade away real, if small,
            /// improvements. This can never regress the result — a revert is kept only when J does not drop, and
            /// <see cref="OptimizationResult.BestJ"/> is updated to the (equal-or-better) reverted value.</para>
            ///
            /// <para>Axes are pulled back INDEPENDENTLY and greedily, each against the current incumbent, so a
            /// pull-back that is only neutral BECAUSE an earlier axis already moved is still caught; conversely two
            /// axes that are individually neutral but jointly matter cannot both move, because the second is
            /// evaluated against the first's already-updated incumbent.</para>
            ///
            /// <para><b>Graded, not all-or-nothing.</b> Reverting only to the seed exactly is not enough: the flat
            /// region is a PLATEAU, and the seed can sit outside it. On the measured run the plateau was
            /// Sensitivity ∈ [0, 15] while the seed was 33.3 — a full revert genuinely costs J and is correctly
            /// refused, which would strand the axis at 0 and keep raising the false floor banner. So each axis is
            /// tried at a few fixed fractions along the path from the incumbent BACK toward the seed, most
            /// seed-ward first, and the first that costs no J wins: the axis ends at the point nearest its seed
            /// that the data cannot distinguish from the search's answer.</para>
            /// </summary>
            public async Task<(double[] theta, double j)> RevertNeutralAxes(double[] theta0, double[] bestTheta, double bestJ, double seedJ) {
                var moved = false;
                for (var i = 0; i < variables.Count; i++) {
                    if (bestTheta[i] == theta0[i]) {
                        continue; // never moved
                    }
                    foreach (var t in PullBackFractions) {
                        if (BudgetExhausted) {
                            break;
                        }
                        // t = 1 is the seed itself; smaller t stays nearer the search's answer.
                        var value = variables[i].Quantize(bestTheta[i] + t * (theta0[i] - bestTheta[i]));
                        if (value == bestTheta[i]) {
                            continue; // quantized back onto the incumbent: nothing to test
                        }
                        var candidate = (double[])bestTheta.Clone();
                        candidate[i] = value;
                        var j = await EvalJ(candidate).ConfigureAwait(false);
                        if (j >= bestJ) {
                            bestTheta = candidate;
                            bestJ = j;
                            moved = true;
                            break; // most seed-ward neutral point for this axis; go to the next axis
                        }
                    }
                }
                if (moved) {
                    Report("RevertNeutral", bestTheta, bestJ, seedJ);
                }
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

                Report("PatternSearch", bestTheta, bestJ, seedJ);
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
                        Report("PatternSearch", bestTheta, bestJ, seedJ);
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
