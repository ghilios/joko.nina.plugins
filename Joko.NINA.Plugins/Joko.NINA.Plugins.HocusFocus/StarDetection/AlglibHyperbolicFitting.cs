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
        /// Builds the hyperbolic fit selected by <paramref name="model"/>. The legacy uneven blend needs the
        /// focuser step size for its transition width; the other models ignore it.
        /// </summary>
        public static AlglibHyperbolicFitting Create(IAlglibAPI alglibAPI, HyperbolicFitModel model, ICollection<ScatterErrorPoint> points, int stepSize, bool useWeights) {
            switch (model) {
                case HyperbolicFitModel.TiltedHyperbola:
                    return TiltedHyperbolicFittingAlglib.Create(alglibAPI, points, useWeights);
                case HyperbolicFitModel.SmoothBlend:
                    return SmoothBlendHyperbolicFittingAlglib.Create(alglibAPI, points, useWeights);
                case HyperbolicFitModel.Symmetric:
                    return HyperbolicFittingAlglib.Create(alglibAPI, points, useWeights);
                case HyperbolicFitModel.UnevenBlendLegacy:
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
            ComputeMinimumStdError(solution);
            return true;
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
            } catch (Exception) {
                MinimumStdError = double.NaN;
            }
        }

        public override string ToString() {
            return $"{Expression}";
        }
    }
}
