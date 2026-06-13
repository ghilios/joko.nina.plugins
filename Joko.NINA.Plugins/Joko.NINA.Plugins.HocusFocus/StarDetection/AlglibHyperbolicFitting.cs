#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Math.Optimization.Losses;
using MathNet.Numerics.Distributions;
using MathNet.Numerics.LinearAlgebra;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.WPF.Base.Utility.AutoFocus;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    /// <summary>
    /// Template-method base for the alglib-backed hyperbolic auto-focus curve fits. It owns the shared
    /// Levenberg-Marquardt boilerplate, the optional in-fit Huber IRLS robustness loop, the weighted R²,
    /// and the JᵀWJ covariance used to propagate the standard error of the best-focus position. Concrete
    /// models supply only their value/gradient/seed/minimum/expression via the protected hooks.
    /// </summary>
    public abstract class AlglibHyperbolicFitting : HyperbolicFitting {
        protected IAlglibAPI alglibAPI;

        public double[][] Inputs { get; protected set; }
        public double[] Weights { get; protected set; }
        public double[] Outputs { get; protected set; }
        public int StepSize { get; protected set; }
        public bool OptGuardEnabled { get; set; } = false;

        /// <summary>
        /// When true (default), the fit is wrapped in a Huber IRLS loop that down-weights points whose
        /// residual exceeds δ = <see cref="HuberSigmaMultiplier"/>·σ (σ = MAD of the current residuals),
        /// making the best-focus estimate resistant to a stray bad measurement before any hard rejection.
        /// </summary>
        public bool HuberIrlsEnabled { get; set; } = true;

        public double HuberSigmaMultiplier { get; set; } = 1.5;

        /// <summary>
        /// Standard error of the fitted minimum's focuser position, propagated from the fit covariance.
        /// <see cref="double.NaN"/> when not computed (too few points, or a variant without an analytic
        /// gradient). Used downstream to weight the paraboloid fit by 1/σ².
        /// </summary>
        public double MinimumStdError { get; protected set; } = double.NaN;

        /// <summary>
        /// Weighted χ² of the fit: Σ (Weights[i]·(model(xᵢ) − yᵢ))². When <see cref="WeightedHyperbolicFitEnabled"/>
        /// is on (Weights = 1/σ from each point's ErrorY, regularized at the auto-focus fit entry points) this is a χ² in <b>scatter units</b>:
        /// per-point σ is the star-ensemble scatter (1.483·MAD), which overstates the uncertainty of the
        /// plotted median HFR by roughly √(detected stars) — so values ≪ 1 are normal for star-rich fields.
        /// Unweighted (Weights = 1) it degenerates to the plain residual sum of squares, which is
        /// scale-dependent. Computed for every model.
        /// </summary>
        public double ChiSquared { get; protected set; } = double.NaN;

        /// <summary>Degrees of freedom of the fit, max(1, n − parameter count). Zero until <see cref="Solve"/> runs.</summary>
        public int DegreesOfFreedom { get; protected set; } = 0;

        /// <summary>
        /// Reduced χ² = <see cref="ChiSquared"/> / <see cref="DegreesOfFreedom"/>. Comparable across runs
        /// only as a <b>relative</b> goodness measure: per-point σ is ensemble scatter, so this runs ≪ 1
        /// for star-rich fields (≈ 1/N* scaling) and grows ×FramesPerPoint now that multi-frame σ is
        /// SEM-pooled. Meaningful only for weighted fits; see <see cref="ChiSquared"/>.
        /// <see cref="double.NaN"/> when not computed.
        /// </summary>
        public double ReducedChiSquared { get; protected set; } = double.NaN;

        /// <summary>
        /// Leave-one-out best-focus stability: the sample standard deviation (in focuser steps) of the predicted
        /// best-focus position across the N drop-one refits. A non-parametric robustness companion to
        /// <see cref="MinimumStdError"/>; populated once at run completion via
        /// <see cref="ComputeLeaveOneOutBestFocusStdError"/>. <see cref="double.NaN"/> when not computed.
        /// </summary>
        public double LeaveOneOutStdError { get; set; } = double.NaN;

        /// <summary>
        /// Builds the hyperbolic fit selected by <paramref name="model"/>. The Uneven Blend model needs the
        /// focuser step size for its transition width; the other models ignore it.
        /// </summary>
        public static AlglibHyperbolicFitting Create(IAlglibAPI alglibAPI, HyperbolicFitModel model, ICollection<ScatterErrorPoint> points, int stepSize, bool useWeights) {
            switch (model) {
                case HyperbolicFitModel.TiltedHyperbola:
                // Hybrid is a meta-model: live it renders as the Tilted hyperbola (and any caller passing
                // Hybrid straight into Create gets Tilted as a defensive fallback). The actual per-run /
                // per-star best-fit selection happens in SelectBestModel. Never recurses through Create.
                case HyperbolicFitModel.Hybrid:
                    return TiltedHyperbolicFittingAlglib.Create(alglibAPI, points, useWeights);
                case HyperbolicFitModel.SmoothBlend:
                    return SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, points, useWeights);
                case HyperbolicFitModel.Symmetric:
                    return HyperbolicFittingAlglib.Create(alglibAPI, points, useWeights);
                case HyperbolicFitModel.UnevenBlend:
                default:
                    return HyperbolicUnevenFittingAlglib.Create(alglibAPI, points, stepSize, useWeights);
            }
        }

        /// <summary>
        /// Per-point residual weights (1/σ from each point's ErrorY) for <see cref="MathUtility.RejectionTest"/>,
        /// so outlier rejection uses the same weighting as a weighted fit. Returns null when weighting is off.
        /// </summary>
        public static Func<double, double> BuildResidualWeights(ICollection<ScatterErrorPoint> points, bool useWeights) {
            if (!useWeights) {
                return null;
            }
            var map = new Dictionary<double, double>();
            foreach (var p in points) {
                map[p.X] = 1.0 / Math.Max(Math.Abs(p.ErrorY), 1e-6);
            }
            return x => map.TryGetValue(x, out var w) ? w : 1.0;
        }

        // Per-point weights actually used by the residual/Jacobian callbacks. Equal to the base χ² Weights,
        // optionally multiplied by the Huber IRLS factors. Covariance always uses the base Weights.
        private double[] effectiveWeights;

        #region Model hooks

        protected abstract int ParameterCount { get; }

        /// <summary>Computes the data-driven initial guess, bounds, and per-parameter scale. Returns false
        /// when the data are insufficient to seed a fit.</summary>
        protected abstract bool TryComputeInitialState(out double[] initialGuess, out double[] lowerBounds, out double[] upperBounds, out double[] scale);

        protected abstract double ModelValue(double[] parameters, double x);

        /// <summary>Analytic gradient of the model w.r.t. each parameter at x (unweighted). Only required
        /// when <see cref="UseJacobian"/> or <see cref="SupportsCovariance"/> is true.</summary>
        protected virtual void ModelGradient(double[] parameters, double x, double[] grad) {
            throw new NotSupportedException("Analytic gradient not provided by this model");
        }

        protected abstract DataPoint ComputeMinimum(double[] parameters);

        protected abstract string FormatExpression(double[] parameters);

        protected virtual bool UseJacobian => true;

        /// <summary>Whether the JᵀWJ covariance / MinimumStdError can be computed (needs an analytic
        /// gradient). Defaults to <see cref="UseJacobian"/>.</summary>
        protected virtual bool SupportsCovariance => UseJacobian;

        /// <summary>Gradient of the minimum-position (best focus x) w.r.t. the parameters, for delta-method
        /// propagation of its standard error. Defaults to the minimum sitting at parameter 0 (x0).</summary>
        protected virtual void MinimumPositionGradient(double[] parameters, double[] grad) {
            Array.Clear(grad, 0, grad.Length);
            grad[0] = 1.0;
        }

        #endregion Model hooks

        public bool Solve() {
            if (Inputs == null || Inputs.Length == 0) {
                return false;
            }
            if (!TryComputeInitialState(out var initialGuess, out var lowerBounds, out var upperBounds, out var scale)) {
                return false;
            }

            effectiveWeights = (double[])Weights.Clone();

            double[] solution;
            if (HuberIrlsEnabled) {
                if (!SolveHuberIrls(initialGuess, lowerBounds, upperBounds, scale, out solution)) {
                    return false;
                }
            } else if (!SolveOnce(initialGuess, lowerBounds, upperBounds, scale, out solution)) {
                return false;
            }

            Expression = FormatExpression(solution);
            Fitting = x => ModelValue(solution, x);
            Minimum = ComputeMinimum(solution);
            ComputeRSquared(solution);
            ComputeChiSquared(solution);
            ComputeMinimumStdError(solution);
            return true;
        }

        /// <summary>
        /// Refits the curve dropping each point in turn (leave-one-out) and returns the sample standard deviation
        /// of the predicted best-focus position across those refits — a non-parametric measure of how much the
        /// answer moves when any single measurement is removed. Returns <see cref="double.NaN"/> when there are
        /// fewer than 5 points or fewer than 2 successful refits. Mirrors the offline benchmark's LeaveOneOutStd.
        /// </summary>
        public static double ComputeLeaveOneOutBestFocusStdError(IAlglibAPI alglibAPI, HyperbolicFitModel model, IList<ScatterErrorPoint> points, int stepSize, bool useWeights, int maxDegreeOfParallelism = 0) {
            if (points == null || points.Count < 5) {
                return double.NaN;
            }

            // Drop-one refits are independent: each builds its own point subset and its own AlglibHyperbolicFitting
            // (Create+Solve) through the shared alglibAPI, which serializes only handle alloc/free (SensorModel.cs
            // 626-627). Predictions are written to index-aligned slots (slot = skip index) and reduced in ascending
            // skip-index order AFTER the loop, so the floating-point reduction order is identical to the original
            // sequential loop ⇒ the result is bit-identical regardless of completion order. A skip whose fit fails or
            // produces a non-finite minimum leaves its slot null and is skipped (exactly the sequential `continue`).
            var predictionSlots = new double?[points.Count];
            var parallelOptions = new ParallelOptions {
                // Cap at points.Count: never spin up more parallelism than there are drop-one iterations.
                MaxDegreeOfParallelism = Math.Min(ParallelExecution.ResolveDegreeOfParallelism(maxDegreeOfParallelism), points.Count)
            };
            // Use the default scheduler (not a limited shared one): when this runs inside SensorModel's outer
            // per-star Parallel.For (which already saturates the cores with the default scheduler), callers
            // pass maxDegreeOfParallelism: 1 to keep the inner loop sequential and avoid ~ProcessorCount²
            // active threads (oversubscription).
            Parallel.For(0, points.Count, parallelOptions, skip => {
                var subset = new List<ScatterErrorPoint>(points.Count - 1);
                for (int i = 0; i < points.Count; ++i) {
                    if (i != skip) {
                        subset.Add(points[i]);
                    }
                }
                var fit = Create(alglibAPI, model, subset, stepSize, useWeights);
                if (fit.Solve() && !double.IsNaN(fit.Minimum.X) && !double.IsInfinity(fit.Minimum.X)) {
                    predictionSlots[skip] = fit.Minimum.X;
                }
            });

            // Collect the finite predictions in ascending skip-index order — same order the sequential loop appended
            // them — so Average/variance accumulate in the identical floating-point sequence.
            var predictions = new List<double>(points.Count);
            for (int skip = 0; skip < predictionSlots.Length; ++skip) {
                if (predictionSlots[skip].HasValue) {
                    predictions.Add(predictionSlots[skip].Value);
                }
            }
            if (predictions.Count < 2) {
                return double.NaN;
            }
            var mean = predictions.Average();
            var variance = predictions.Sum(p => (p - mean) * (p - mean)) / (predictions.Count - 1);
            return Math.Sqrt(variance);
        }

        /// <summary>
        /// The concrete models the Hybrid "best fit" meta-model competes. Excludes <see cref="HyperbolicFitModel.Hybrid"/>
        /// itself, so selection never recurses.
        /// </summary>
        private static readonly HyperbolicFitModel[] HybridCandidateModels = {
            HyperbolicFitModel.Symmetric,
            HyperbolicFitModel.UnevenBlend,
            HyperbolicFitModel.TiltedHyperbola,
            HyperbolicFitModel.SmoothBlend,
        };

        /// <summary>
        /// Confidence for the nested F-test that gates the asymmetric (5-parameter) models against the
        /// Symmetric (4-parameter) baseline in <see cref="SelectBestModel"/>. Hardcoded — an internal
        /// statistical threshold, not a user tuning knob (design §6). Higher ⇒ stricter ⇒ more reluctant to
        /// report tilt.
        /// </summary>
        private const double TiltSignificanceConfidence = 0.95;

        /// <summary>
        /// Backwards-compatible overload that performs no outlier rejection (used by callers that prune outliers
        /// themselves, or do not prune at all). Equivalent to passing <c>maxOutlierRejections = 0</c>.
        /// </summary>
        public static HyperbolicFitModel SelectBestModel(IAlglibAPI alglibAPI, IList<ScatterErrorPoint> points, int stepSize, bool useWeights, out AlglibHyperbolicFitting bestFit)
            => SelectBestModel(alglibAPI, points, stepSize, useWeights, maxOutlierRejections: 0, rejectionConfidence: 0.0, out bestFit, out _);

        /// <summary>
        /// Fits all concrete hyperbolic models to <paramref name="points"/> and returns the one with the least
        /// expected error for the best-focus position — the Hybrid "best fit" selection. Candidates that fail to
        /// solve, or whose minimum is non-finite or outside the sampled X range, are dropped.
        ///
        /// Outlier rejection uses <b>consensus</b>: each candidate independently proposes its Grubbs outliers
        /// (pass 1, weighted when <paramref name="useWeights"/> is set), and only the <i>intersection</i> —
        /// points every solved model flags — is removed (up to <paramref name="maxOutlierRejections"/>). All
        /// models then compete on the single common cleaned set (pass 2), so no model can improve its ranking by
        /// aggressively pruning its own data. Fewer than two solved models cannot form a meaningful consensus,
        /// so pass 1 removes nothing in that case. <paramref name="rejectedPoints"/> returns the consensus set
        /// (not any single model's rejects).
        ///
        /// Before ranking, a nested F-test (at <see cref="TiltSignificanceConfidence"/>) gates the asymmetric
        /// (5-parameter) models against the Symmetric (4-parameter) baseline: an asymmetric model may win only when
        /// it significantly improves on Symmetric; otherwise only Symmetric is eligible (parsimony). The gate is
        /// bypassed when no viable Symmetric baseline survives.
        ///
        /// Ranking is tiered: (1) finite parametric σ(focus) = <see cref="MinimumStdError"/> ascending — already
        /// produced by <see cref="Solve"/>, so the common path is cheap. Every concrete model supplies a σ(focus)
        /// (the Uneven Blend model included, via se(x0) from its analytic-gradient covariance), so all four
        /// compete on equal footing; (2) only when no candidate has a finite σ(focus) — i.e. every fit is too
        /// degenerate to localize focus — the leave-one-out best-focus std
        /// (<see cref="ComputeLeaveOneOutBestFocusStdError"/>, computed lazily) breaks the tie; final tiebreak
        /// <see cref="ReducedChiSquared"/> ascending then <see cref="RSquared"/> descending. If nothing survives,
        /// returns <see cref="HyperbolicFitModel.TiltedHyperbola"/> with its solved fit (or null) so the live fit
        /// is preserved. <paramref name="bestFit"/> is the already-solved winning (cleaned) fit. Callers cap how far
        /// a fit may be pruned by lowering <paramref name="maxOutlierRejections"/> (e.g. the sensor model passes
        /// <c>min(configuredCap, count − minPointsPerStar)</c> to keep at least its required points per star).
        /// </summary>
        public static HyperbolicFitModel SelectBestModel(
                IAlglibAPI alglibAPI, IList<ScatterErrorPoint> points, int stepSize, bool useWeights,
                int maxOutlierRejections, double rejectionConfidence,
                out AlglibHyperbolicFitting bestFit, out IReadOnlyList<ScatterErrorPoint> rejectedPoints,
                int maxDegreeOfParallelism = 0) {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            if (points != null) {
                foreach (var p in points) {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                }
            }

            // PASS 1: each candidate proposes its own Grubbs outliers (against its own residuals), but does NOT
            // commit them. modelRejects[k] = the points model k WOULD reject; solved[k] = whether it fit at all.
            var modelRejects = new List<ScatterErrorPoint>[HybridCandidateModels.Length];
            var solved = new bool[HybridCandidateModels.Length];
            var fitOptions = new ParallelOptions {
                MaxDegreeOfParallelism = Math.Min(ParallelExecution.ResolveDegreeOfParallelism(maxDegreeOfParallelism), HybridCandidateModels.Length)
            };
            Parallel.For(0, HybridCandidateModels.Length, fitOptions, k => {
                try {
                    var fit = FitWithOutlierRejection(alglibAPI, HybridCandidateModels[k], points, stepSize, useWeights,
                        maxOutlierRejections, rejectionConfidence, out var rejects, out _);
                    if (fit != null) {
                        solved[k] = true;
                        modelRejects[k] = rejects;
                    }
                } catch (Exception ex) {
                    Logger.Trace($"Hybrid selection (pass 1): model {HybridCandidateModels[k]} failed ({ex.Message})");
                }
            });

            // CONSENSUS: a point is a true outlier only if EVERY solved model flags it. Fewer than 2 solved models
            // cannot distinguish tilt from a bad frame, so reject nothing. Key points by rounded focuser position.
            var solvedCount = solved.Count(s => s);
            var consensus = new List<ScatterErrorPoint>();
            if (solvedCount >= 2 && maxOutlierRejections > 0) {
                IEnumerable<int> common = null;
                for (int k = 0; k < HybridCandidateModels.Length; ++k) {
                    if (!solved[k]) continue;
                    var keys = (modelRejects[k] ?? new List<ScatterErrorPoint>()).Select(p => (int)Math.Round(p.X)).ToHashSet();
                    common = common == null ? keys : common.Intersect(keys);
                }
                var commonKeys = (common ?? Enumerable.Empty<int>()).ToHashSet();
                if (commonKeys.Count > 0) {
                    // Materialize consensus points from the first solved model's proposed set (identical X across
                    // models), preserving that model's rejection order, then cap at maxOutlierRejections.
                    var firstSolved = Array.FindIndex(solved, s => s);
                    consensus = modelRejects[firstSolved]
                        .Where(p => commonKeys.Contains((int)Math.Round(p.X)))
                        .Take(maxOutlierRejections) // cap preserves the first solved model's residual-rejection order (deterministic)
                        .ToList();
                }
            }

            // Build the single common cleaned set every model is judged on (fair comparison; no model cleans its
            // own data differently).
            var consensusKeysFinal = consensus.Select(p => (int)Math.Round(p.X)).ToHashSet();
            var cleaned = points.Where(p => !consensusKeysFinal.Contains((int)Math.Round(p.X))).ToList(); // when consensus is empty (< 2 solved models or no intersection), cleaned == points: pass 2 is a plain multi-model fit on the full set

            // PASS 2: fit every model on the common cleaned set — these fits drive viability, the F-test gate, and ranking.
            var modelFits = new AlglibHyperbolicFitting[HybridCandidateModels.Length];
            Parallel.For(0, HybridCandidateModels.Length, fitOptions, k => {
                try {
                    var fit = Create(alglibAPI, HybridCandidateModels[k], cleaned, stepSize, useWeights);
                    if (fit.Solve()) {
                        modelFits[k] = fit;
                    }
                } catch (Exception ex) {
                    Logger.Trace($"Hybrid selection (pass 2): model {HybridCandidateModels[k]} failed ({ex.Message})");
                }
            });

            // Survivor selection — SEQUENTIAL, fixed HybridCandidateModels index order (preserves deterministic tie-break).
            var survivors = new List<AlglibHyperbolicFitting>();
            var survivorModels = new List<HyperbolicFitModel>();
            var survivorClean = new List<List<ScatterErrorPoint>>();
            AlglibHyperbolicFitting tiltedFit = null;
            for (int k = 0; k < HybridCandidateModels.Length; ++k) {
                var fit = modelFits[k];
                if (fit == null) continue;
                if (HybridCandidateModels[k] == HyperbolicFitModel.TiltedHyperbola) tiltedFit = fit;
                var x = fit.Minimum.X;
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (maxX >= minX && (x < minX || x > maxX)) continue;
                survivors.Add(fit);
                survivorModels.Add(HybridCandidateModels[k]);
                survivorClean.Add(cleaned);
            }

            rejectedPoints = consensus;

            if (survivors.Count == 0) {
                bestFit = tiltedFit;
                return HyperbolicFitModel.TiltedHyperbola;
            }

            // Significance gate: unlock the asymmetric (5-parameter) models only when at least one significantly
            // improves on the Symmetric (4-parameter) baseline (nested F-test at TiltSignificanceConfidence, on the
            // common cleaned set). Otherwise prefer parsimony (Symmetric). Requires a viable Symmetric baseline.
            int symIndex = survivorModels.IndexOf(HyperbolicFitModel.Symmetric);
            var allIndices = Enumerable.Range(0, survivors.Count).ToList();
            List<int> allowed = allIndices;
            if (symIndex >= 0) {
                double chiSym = survivors[symIndex].ChiSquared;
                // n is the common cleaned-set size (design §5). The per-model Y>=0.1 input filter is a no-op for
                // valid focus data (HFR is never < 0.1 px), so this equals each fit's actual fitted-point count and
                // the F-denominator dof (n − 5) is correct.
                int n = cleaned.Count;
                var justified = new List<int>();
                for (int i = 0; i < survivors.Count; ++i) {
                    if (survivorModels[i] == HyperbolicFitModel.Symmetric) continue;
                    if (IsAsymmetryJustified(chiSym, survivors[i].ChiSquared, n, TiltSignificanceConfidence)) {
                        justified.Add(i);
                    }
                }
                allowed = justified.Count > 0 ? justified : new List<int> { symIndex };
            }

            int winner = RankBest(alglibAPI, survivors, survivorModels, survivorClean, allowed, stepSize, useWeights, maxDegreeOfParallelism);
            bestFit = survivors[winner];
            return survivorModels[winner];
        }

        /// <summary>
        /// Ranks the given <paramref name="allowed"/> survivor indices by the standard tiering: Tier 1 = ascending
        /// finite σ(focus) (<see cref="MinimumStdError"/>); Tier 2 = ascending leave-one-out best-focus std (lazy,
        /// only when no allowed candidate has a finite σ(focus)); final tiebreak ascending <see cref="ReducedChiSquared"/>
        /// then descending <see cref="RSquared"/>. Returns the winning index into <paramref name="survivors"/>.
        /// <paramref name="allowed"/> must be non-empty.
        /// </summary>
        private static int RankBest(
                IAlglibAPI alglibAPI, List<AlglibHyperbolicFitting> survivors, List<HyperbolicFitModel> survivorModels,
                List<List<ScatterErrorPoint>> survivorClean, List<int> allowed, int stepSize, bool useWeights, int maxDegreeOfParallelism) {
            var tier1 = allowed.Where(i => {
                var se = survivors[i].MinimumStdError;
                return !double.IsNaN(se) && !double.IsInfinity(se);
            }).ToList();

            var primary = new Dictionary<int, double>();
            List<int> contenders;
            if (tier1.Count > 0) {
                foreach (var i in allowed) primary[i] = survivors[i].MinimumStdError;
                contenders = tier1;
            } else {
                contenders = new List<int>();
                foreach (var i in allowed) {
                    var loo = ComputeLeaveOneOutBestFocusStdError(alglibAPI, survivorModels[i], survivorClean[i], stepSize, useWeights, maxDegreeOfParallelism);
                    primary[i] = loo;
                    if (!double.IsNaN(loo) && !double.IsInfinity(loo)) contenders.Add(i);
                }
                if (contenders.Count == 0) {
                    foreach (var i in allowed) primary[i] = 0.0;
                    contenders = new List<int>(allowed);
                }
            }

            contenders.Sort((i, j) => {
                int c = primary[i].CompareTo(primary[j]);
                if (c != 0) return c;
                c = CompareAscNaNLast(survivors[i].ReducedChiSquared, survivors[j].ReducedChiSquared);
                if (c != 0) return c;
                return CompareAscNaNLast(-survivors[i].RSquared, -survivors[j].RSquared);
            });
            return contenders[0];
        }

        /// <summary>
        /// Fits <paramref name="model"/> to <paramref name="points"/>, then iteratively removes up to
        /// <paramref name="maxOutlierRejections"/> Grubbs-test outliers — judged against <b>this model's own</b>
        /// residuals (weighted when <paramref name="useWeights"/> is set) — refitting after each removal. Returns
        /// the cleaned fit (the last fit that solved) plus the points it rejected and the points it kept, or null
        /// if the model fails to solve even on the full set. A refit that fails to solve ends the loop with the
        /// last good fit, so a rejection is recorded only when its refit succeeded. With
        /// <paramref name="maxOutlierRejections"/> = 0 this is a single solve on all points (no rejection).
        /// </summary>
        private static AlglibHyperbolicFitting FitWithOutlierRejection(
                IAlglibAPI alglibAPI, HyperbolicFitModel model, IList<ScatterErrorPoint> points, int stepSize, bool useWeights,
                int maxOutlierRejections, double rejectionConfidence,
                out List<ScatterErrorPoint> rejectedPoints, out List<ScatterErrorPoint> cleanedPoints) {
            rejectedPoints = new List<ScatterErrorPoint>();
            var working = new List<ScatterErrorPoint>(points);

            var fit = Create(alglibAPI, model, working, stepSize, useWeights);
            if (!fit.Solve()) {
                cleanedPoints = working;
                return null;
            }

            while (rejectedPoints.Count < maxOutlierRejections) {
                var weights = BuildResidualWeights(working, useWeights);
                var rejected = MathUtility.RejectionTest(working, fit.Fitting, rejectionConfidence, weights);
                if (rejected == null) {
                    break;
                }

                var trial = new List<ScatterErrorPoint>(working);
                trial.Remove(rejected);
                var refit = Create(alglibAPI, model, trial, stepSize, useWeights);
                if (!refit.Solve()) {
                    break; // cannot refit without this point; keep the current fit and stop
                }

                working = trial;
                fit = refit;
                rejectedPoints.Add(rejected);
            }
            cleanedPoints = working;
            return fit;
        }

        // Ascending comparison treating NaN as the worst (sorted last).
        private static int CompareAscNaNLast(double a, double b) {
            bool na = double.IsNaN(a), nb = double.IsNaN(b);
            if (na && nb) return 0;
            if (na) return 1;
            if (nb) return -1;
            return a.CompareTo(b);
        }

        /// <summary>
        /// Nested F-test: is the 5-parameter asymmetric fit a statistically significant improvement over the
        /// 4-parameter Symmetric baseline, at <paramref name="confidence"/>? Both χ² are the weighted χ² of the
        /// two fits on the SAME point set (same regularized 1/σ weights), so the ratio is scale-invariant.
        /// Returns false when there are too few points to test (n − 5 &lt; 1), when inputs are non-finite, or when
        /// the asymmetric fit does not actually reduce χ².
        /// </summary>
        internal static bool IsAsymmetryJustified(double chiSquaredSymmetric, double chiSquaredAsymmetric, int n, double confidence) {
            const int pSym = 4, pAsym = 5;
            int dofDenom = n - pAsym;
            if (dofDenom < 1) return false;
            if (double.IsNaN(chiSquaredSymmetric) || double.IsNaN(chiSquaredAsymmetric) || chiSquaredAsymmetric <= 0.0) return false;
            double improvement = chiSquaredSymmetric - chiSquaredAsymmetric;
            if (improvement <= 0.0) return false;
            double f = (improvement / (pAsym - pSym)) / (chiSquaredAsymmetric / dofDenom);
            double fCritical = new FisherSnedecor(pAsym - pSym, dofDenom).InverseCumulativeDistribution(confidence);
            return f > fCritical;
        }

        /// <summary>
        /// Iteratively reweighted least squares with Huber weights. Each iteration re-solves the LM fit, then
        /// recomputes per-point weights from the residual scale: inliers (|r − median(r)| ≤ δ) keep weight 1, outliers are
        /// down-weighted by δ/|r − median(r)|, where δ = HuberSigmaMultiplier·σ and σ is the MAD of the current residuals.
        /// Folds those factors into the base χ² weights and repeats until the residual sum stabilizes.
        /// </summary>
        private bool SolveHuberIrls(double[] initialGuess, double[] lowerBounds, double[] upperBounds, double[] scale, out double[] solution) {
            const int maxIrlsIterations = 10;
            const double tolerance = 1e-6;
            solution = null;
            double[] lastGoodSolution = null;
            var prevSumAbsResiduals = double.PositiveInfinity;
            var guess = initialGuess;
            for (int iter = 0; iter < maxIrlsIterations; ++iter) {
                if (!SolveOnce(guess, lowerBounds, upperBounds, scale, out solution)) {
                    // SolveOnce just overwrote the out param with the failed attempt (or null) —
                    // restore the previous good solution instead of returning the failed parameters.
                    solution = lastGoodSolution;
                    return solution != null;
                }
                lastGoodSolution = solution;
                guess = solution; // warm-start the next reweighted solve

                // Residual scale from the MAD of the unweighted residuals.
                var residuals = new double[Inputs.Length];
                var sumAbs = 0.0;
                for (int i = 0; i < Inputs.Length; ++i) {
                    residuals[i] = ModelValue(solution, Inputs[i][0]) - Outputs[i];
                    sumAbs += Math.Abs(residuals[i]);
                }
                var (residualMedian, mad) = residuals.MedianMAD();
                if (mad <= 0.0 || double.IsNaN(mad)) {
                    break; // residuals already tight; no robust reweighting needed
                }
                var delta = HuberSigmaMultiplier * mad;
                for (int i = 0; i < Inputs.Length; ++i) {
                    // Center on the residual median so the comparison is consistent with the
                    // median-centered MAD that defines δ — an offset residual distribution would
                    // otherwise down-weight the bulk and under-penalize one-sided outliers.
                    var absR = Math.Abs(residuals[i] - residualMedian);
                    var huber = absR <= delta ? 1.0 : delta / absR;
                    effectiveWeights[i] = Weights[i] * huber;
                }

                if (Math.Abs(prevSumAbsResiduals - sumAbs) <= tolerance * Math.Max(1.0, sumAbs)) {
                    break;
                }
                prevSumAbsResiduals = sumAbs;
            }
            return solution != null;
        }

        private bool SolveOnce(double[] initialGuess, double[] lowerBounds, double[] upperBounds, double[] scale, out double[] solution) {
            solution = null;
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                const double deltaForNumericIntegration = 1E-6;
                const double tolerance = 1E-6;
                const int maxIterations = 1000;
                if (UseJacobian) {
                    this.alglibAPI.minlmcreatevj(this.Inputs.Length, initialGuess, out state);
                    this.alglibAPI.minlmsetacctype(state, 1);
                } else {
                    this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                }

                this.alglibAPI.minlmsetbc(state, lowerBounds, upperBounds);
                this.alglibAPI.minlmsetcond(state, tolerance, maxIterations);
                this.alglibAPI.minlmsetscale(state, scale);

                if (OptGuardEnabled) {
                    this.alglibAPI.minlmoptguardgradient(state, deltaForNumericIntegration);
                }

                this.alglibAPI.minlmoptimize(state, this.FitResiduals, this.FitResidualsJacobian, null, null);
                this.alglibAPI.minlmresults(state, out solution, out rep);

                if (rep.terminationtype < 0 && rep.terminationtype != -5) {
                    string reason;
                    if (rep.terminationtype == -8) {
                        reason = "optimizer detected NAN/INF values either in the function itself, or in its Jacobian";
                    } else if (rep.terminationtype == -3) {
                        reason = "constraints are inconsistent";
                    } else {
                        reason = "unknown";
                    }
                    Logger.Error($"Hyperbolic modeling failed with type {rep.terminationtype} and reason: {reason}");
                    return false;
                }

                if (OptGuardEnabled) {
                    alglib.optguardreport ogrep;
                    this.alglibAPI.minlmoptguardresults(state, out ogrep);
                    try {
                        if (ogrep.badgradsuspected) {
                            throw new Exception("OptGuard detected a bad analytic gradient in the hyperbolic fit");
                        }
                    } finally {
                        this.alglibAPI.deallocateimmediately(ref ogrep);
                    }
                }
                return true;
            } finally {
                if (state != null) {
                    this.alglibAPI.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
            }
        }

        public void FitResiduals(double[] parameters, double[] fi, object obj) {
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var x = this.Inputs[i][0];
                fi[i] = effectiveWeights[i] * (ModelValue(parameters, x) - this.Outputs[i]);
            }
        }

        public void FitResidualsJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            var g = new double[ParameterCount];
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var x = this.Inputs[i][0];
                var weight = effectiveWeights[i];
                fi[i] = weight * (ModelValue(parameters, x) - this.Outputs[i]);
                ModelGradient(parameters, x, g);
                for (int j = 0; j < ParameterCount; ++j) {
                    jac[i, j] = weight * g[j];
                }
            }
        }

        protected void ComputeRSquared(double[] solution) {
            var transformed = new double[Inputs.Length];
            var rSquared = new RSquaredLoss(Inputs.Length, Outputs);
            rSquared.Weights = Weights;
            for (var i = 0; i < Inputs.Length; i++) {
                transformed[i] = ModelValue(solution, Inputs[i][0]);
            }
            RSquared = rSquared.Loss(transformed);
        }

        /// <summary>
        /// Weighted χ² and reduced χ² of the fit, using the base χ² <see cref="Weights"/> (not the Huber IRLS
        /// factors). Computed for every model regardless of <see cref="SupportsCovariance"/>, so the Uneven
        /// Blend model gets it too. The same weighted residual sum the covariance path forms in
        /// <see cref="ComputeMinimumStdError"/>.
        /// </summary>
        protected void ComputeChiSquared(double[] solution) {
            int n = Inputs.Length;
            int p = ParameterCount;
            var chiSquared = 0.0;
            for (int i = 0; i < n; i++) {
                var weightedResidual = Weights[i] * (ModelValue(solution, Inputs[i][0]) - Outputs[i]);
                chiSquared += weightedResidual * weightedResidual;
            }
            ChiSquared = chiSquared;
            DegreesOfFreedom = Math.Max(1, n - p);
            ReducedChiSquared = chiSquared / DegreesOfFreedom;
        }

        /// <summary>
        /// Estimates the standard error of the best-focus position from the fit covariance
        /// Cov ≈ s²·(JᵀWJ)⁻¹, where J is the analytic Jacobian at the solution, W = diag(weight²), and
        /// s² = weighted RSS/(n−p). The minimum position's variance is propagated by the delta method using
        /// <see cref="MinimumPositionGradient"/>. Guarded so any failure leaves MinimumStdError as NaN.
        /// </summary>
        protected void ComputeMinimumStdError(double[] solution) {
            try {
                if (!SupportsCovariance) {
                    MinimumStdError = double.NaN;
                    return;
                }
                int p = ParameterCount;
                int n = Inputs.Length;
                if (n <= p) {
                    MinimumStdError = double.NaN;
                    return;
                }

                var jtwj = Matrix<double>.Build.Dense(p, p);
                var g = new double[p];
                var weightedRss = 0.0;
                for (int i = 0; i < n; i++) {
                    var x = Inputs[i][0];
                    var w2 = Weights[i] * Weights[i];
                    ModelGradient(solution, x, g);
                    for (int r = 0; r < p; r++) {
                        for (int c = 0; c < p; c++) {
                            jtwj[r, c] += w2 * g[r] * g[c];
                        }
                    }
                    var residual = ModelValue(solution, x) - Outputs[i];
                    weightedRss += w2 * residual * residual;
                }

                var s2 = weightedRss / (n - p);
                var cov = jtwj.Inverse();
                var mg = new double[p];
                MinimumPositionGradient(solution, mg);
                var variance = 0.0;
                for (int r = 0; r < p; r++) {
                    for (int c = 0; c < p; c++) {
                        variance += mg[r] * cov[r, c] * mg[c];
                    }
                }
                variance *= s2;
                MinimumStdError = variance > 0 && !double.IsNaN(variance) && !double.IsInfinity(variance) ? Math.Sqrt(variance) : double.NaN;

                // Suppress an ill-conditioned covariance: a standard error larger than the entire sampled sweep
                // does not localize focus (near-degenerate / barely-determined fit, e.g. n ≈ parameter count or a
                // monotonic curve). Report it as not-determined rather than a misleadingly precise 1e6+ steps.
                if (!double.IsNaN(MinimumStdError)) {
                    double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                    for (int i = 0; i < n; i++) {
                        var xi = Inputs[i][0];
                        if (xi < minX) minX = xi;
                        if (xi > maxX) maxX = xi;
                    }
                    var xSpan = maxX - minX;
                    if (xSpan > 0 && MinimumStdError > xSpan) {
                        MinimumStdError = double.NaN;
                    }
                }
            } catch (Exception) {
                MinimumStdError = double.NaN;
            }
        }

        public override string ToString() {
            return $"{Expression}";
        }
    }
}
