#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Math.Optimization.Losses;
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
        /// is on (Weights = 1/σ from each point's ErrorY) this is a true χ²; unweighted (Weights = 1) it degenerates
        /// to the plain residual sum of squares, which is scale-dependent. Computed for every model.
        /// </summary>
        public double ChiSquared { get; protected set; } = double.NaN;

        /// <summary>Degrees of freedom of the fit, max(1, n − parameter count). Zero until <see cref="Solve"/> runs.</summary>
        public int DegreesOfFreedom { get; protected set; } = 0;

        /// <summary>
        /// Reduced χ² = <see cref="ChiSquared"/> / <see cref="DegreesOfFreedom"/>. Meaningful as a goodness-of-fit
        /// measure (≈1 for a correct model with well-estimated per-point σ) only for weighted fits; see
        /// <see cref="ChiSquared"/>. <see cref="double.NaN"/> when not computed.
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
        public static double ComputeLeaveOneOutBestFocusStdError(IAlglibAPI alglibAPI, HyperbolicFitModel model, IList<ScatterErrorPoint> points, int stepSize, bool useWeights) {
            if (points == null || points.Count < 5) {
                return double.NaN;
            }
            var predictions = new List<double>(points.Count);
            for (int skip = 0; skip < points.Count; ++skip) {
                var subset = new List<ScatterErrorPoint>(points.Count - 1);
                for (int i = 0; i < points.Count; ++i) {
                    if (i != skip) {
                        subset.Add(points[i]);
                    }
                }
                var fit = Create(alglibAPI, model, subset, stepSize, useWeights);
                if (fit.Solve() && !double.IsNaN(fit.Minimum.X) && !double.IsInfinity(fit.Minimum.X)) {
                    predictions.Add(fit.Minimum.X);
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
        /// Each candidate rejects its <b>own</b> outliers: up to <paramref name="maxOutlierRejections"/> points
        /// are removed by the Grubbs test against that model's residuals (weighted when <paramref name="useWeights"/>
        /// is set), refitting after each removal, before the model competes. Outlier-ness is model-specific — a
        /// point far from a Symmetric hyperbola may sit on a Tilted one — so sharing a single pruned set across all
        /// candidates would bias the choice; instead every model is judged on the fit it produces after cleaning
        /// the points that are outliers <i>for it</i>. <paramref name="rejectedPoints"/> returns the winning
        /// model's rejected points (so the caller can report exactly what the chosen fit excluded).
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
                out AlglibHyperbolicFitting bestFit, out IReadOnlyList<ScatterErrorPoint> rejectedPoints) {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            if (points != null) {
                foreach (var p in points) {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                }
            }

            var survivors = new List<AlglibHyperbolicFitting>();
            var survivorModels = new List<HyperbolicFitModel>();
            var survivorRejects = new List<List<ScatterErrorPoint>>();
            var survivorClean = new List<List<ScatterErrorPoint>>();
            AlglibHyperbolicFitting tiltedFit = null;
            List<ScatterErrorPoint> tiltedRejects = null;
            foreach (var model in HybridCandidateModels) {
                AlglibHyperbolicFitting fit;
                List<ScatterErrorPoint> modelRejects, modelClean;
                try {
                    fit = FitWithOutlierRejection(alglibAPI, model, points, stepSize, useWeights, maxOutlierRejections, rejectionConfidence, out modelRejects, out modelClean);
                    if (fit == null) {
                        continue;
                    }
                } catch (Exception ex) {
                    Logger.Trace($"Hybrid selection: model {model} failed to solve ({ex.Message})");
                    continue;
                }
                if (model == HyperbolicFitModel.TiltedHyperbola) {
                    tiltedFit = fit;
                    tiltedRejects = modelRejects;
                }
                var x = fit.Minimum.X;
                if (double.IsNaN(x) || double.IsInfinity(x)) {
                    continue;
                }
                if (maxX >= minX && (x < minX || x > maxX)) {
                    continue;
                }
                survivors.Add(fit);
                survivorModels.Add(model);
                survivorRejects.Add(modelRejects);
                survivorClean.Add(modelClean);
            }

            if (survivors.Count == 0) {
                // All candidates degenerate/out-of-range — keep the live (Tilted) fit so the rest of the pipeline
                // stays consistent with what was rendered during the sweep.
                bestFit = tiltedFit;
                rejectedPoints = tiltedRejects ?? (IReadOnlyList<ScatterErrorPoint>)Array.Empty<ScatterErrorPoint>();
                return HyperbolicFitModel.TiltedHyperbola;
            }

            // Tier 1: candidates whose parametric σ(focus) is finite.
            var tier1 = new List<int>();
            for (int i = 0; i < survivors.Count; ++i) {
                var se = survivors[i].MinimumStdError;
                if (!double.IsNaN(se) && !double.IsInfinity(se)) {
                    tier1.Add(i);
                }
            }

            var primary = new double[survivors.Count];
            List<int> contenders;
            if (tier1.Count > 0) {
                for (int i = 0; i < survivors.Count; ++i) {
                    primary[i] = survivors[i].MinimumStdError;
                }
                contenders = tier1;
            } else {
                // Tier 2: leave-one-out fallback (lazy — only when no candidate has a finite σ(focus)). Each model's
                // LOO uses its own cleaned point set, consistent with how it was ranked.
                contenders = new List<int>();
                for (int i = 0; i < survivors.Count; ++i) {
                    var loo = ComputeLeaveOneOutBestFocusStdError(alglibAPI, survivorModels[i], survivorClean[i], stepSize, useWeights);
                    primary[i] = loo;
                    if (!double.IsNaN(loo) && !double.IsInfinity(loo)) {
                        contenders.Add(i);
                    }
                }
                if (contenders.Count == 0) {
                    // Neither σ(focus) nor LOO available (e.g. < 5 points and no covariance) — rank every survivor
                    // by the χ²/R² tiebreak alone.
                    for (int i = 0; i < survivors.Count; ++i) {
                        primary[i] = 0.0;
                        contenders.Add(i);
                    }
                }
            }

            contenders.Sort((i, j) => {
                int c = primary[i].CompareTo(primary[j]);
                if (c != 0) return c;
                c = CompareAscNaNLast(survivors[i].ReducedChiSquared, survivors[j].ReducedChiSquared);
                if (c != 0) return c;
                return CompareAscNaNLast(-survivors[i].RSquared, -survivors[j].RSquared); // R² descending
            });

            var best = contenders[0];
            bestFit = survivors[best];
            rejectedPoints = survivorRejects[best];
            return survivorModels[best];
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
        /// Iteratively reweighted least squares with Huber weights. Each iteration re-solves the LM fit, then
        /// recomputes per-point weights from the residual scale: inliers (|r| ≤ δ) keep weight 1, outliers are
        /// down-weighted by δ/|r|, where δ = HuberSigmaMultiplier·σ and σ is the MAD of the current residuals.
        /// Folds those factors into the base χ² weights and repeats until the residual sum stabilizes.
        /// </summary>
        private bool SolveHuberIrls(double[] initialGuess, double[] lowerBounds, double[] upperBounds, double[] scale, out double[] solution) {
            const int maxIrlsIterations = 10;
            const double tolerance = 1e-6;
            solution = null;
            var prevSumAbsResiduals = double.PositiveInfinity;
            var guess = initialGuess;
            for (int iter = 0; iter < maxIrlsIterations; ++iter) {
                if (!SolveOnce(guess, lowerBounds, upperBounds, scale, out solution)) {
                    return iter > 0; // keep the previous good solution if a later reweighting fails
                }
                guess = solution; // warm-start the next reweighted solve

                // Residual scale from the MAD of the unweighted residuals.
                var residuals = new double[Inputs.Length];
                var sumAbs = 0.0;
                for (int i = 0; i < Inputs.Length; ++i) {
                    residuals[i] = ModelValue(solution, Inputs[i][0]) - Outputs[i];
                    sumAbs += Math.Abs(residuals[i]);
                }
                var (_, mad) = residuals.MedianMAD();
                if (mad <= 0.0 || double.IsNaN(mad)) {
                    break; // residuals already tight; no robust reweighting needed
                }
                var delta = HuberSigmaMultiplier * mad;
                for (int i = 0; i < Inputs.Length; ++i) {
                    var absR = Math.Abs(residuals[i]);
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
