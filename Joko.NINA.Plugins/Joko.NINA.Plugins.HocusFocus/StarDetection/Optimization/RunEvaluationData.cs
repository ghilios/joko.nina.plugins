#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.WPF.Base.ViewModel.AutoFocus;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// A normalized, lightweight per-frame detection result the evaluator's detect delegate returns. It is the
    /// only thing the evaluator needs from detection (HFR, σ, accepted-star count, and — for labels — the
    /// accepted-star centers), so the evaluator stays decoupled from the concrete detection result type.
    /// </summary>
    public sealed class FrameDetectionResult {
        public double AverageHFR { get; set; }
        public double HFRStdDev { get; set; }
        public int StarCount { get; set; }

        /// <summary>Accepted-star centers (image pixel coords), used only for label recall/precision.</summary>
        public IReadOnlyList<(double X, double Y)> StarCenters { get; set; }
    }

    /// <summary>
    /// One auto-focus frame: a stable id for logging, its focuser position, and an opaque image payload that the
    /// detect delegate knows how to consume (an <c>IRenderedImage</c> in the wizard, a float <c>Mat</c> in the
    /// offline harness). Keeping the payload as <see cref="object"/> is what lets the same evaluator serve both.
    /// </summary>
    public sealed class RunFrame {
        public string FrameId { get; set; }
        public int FocuserPosition { get; set; }
        public object Image { get; set; }
    }

    /// <summary>
    /// Optional ground-truth labels for one focuser position, used to score recall/precision. Plumbed now (T3),
    /// consumed by the labeling workflow (T7); when no labels are supplied the run scores label-agnostically.
    /// </summary>
    public sealed class FrameLabels {
        public int FocuserPosition { get; set; }

        /// <summary>Stars that SHOULD have been detected (recovered when an accepted center lands within radius).</summary>
        public IReadOnlyList<(double X, double Y)> Missed { get; set; }

        /// <summary>Stars that SHOULD have been rejected (correctly excluded when NO accepted center is within radius).</summary>
        public IReadOnlyList<(double X, double Y)> ShouldReject { get; set; }

        public double RadiusPx { get; set; }
    }

    /// <summary>
    /// The auto-focus curve-fit configuration for a run, taken from the run's <c>AutoFocusEngineOptions</c> so the
    /// optimizer's fit matches the AF engine's fit exactly.
    /// </summary>
    public struct RunFitConfig {
        public int StepSize { get; set; }
        public bool UseWeights { get; set; }
        public int MaxOutlierRejections { get; set; }
        public double RejectionConfidence { get; set; }

        /// <summary>The user's preferred model. Currently informational: the evaluator always runs the Hybrid
        /// "best fit" selection (mirroring the AF engine) regardless, so the winning model competes on merit.</summary>
        // Reserved: not yet honored — SelectBestModel currently chooses the best model per fit.
        public HyperbolicFitModel? PreferredModel { get; set; }
    }

    /// <summary>
    /// The fitted outcome of evaluating one candidate params bundle against one run: the <see cref="Metrics"/>
    /// the objective consumes, plus the winning <see cref="BestFit"/> (so the step-size recommender can use it)
    /// and a couple of pooling diagnostics for tests/diagnostics.
    /// </summary>
    public sealed class RunEvaluationResult {
        public RunEvaluationMetrics Metrics { get; set; }
        public AlglibHyperbolicFitting BestFit { get; set; }

        /// <summary>Number of DISTINCT focuser positions that fed the fit (frames at the same position are pooled).</summary>
        public int PooledPointCount { get; set; }

        /// <summary>The pooled (mean) HFR at each distinct focuser position; for assertions/diagnostics.</summary>
        public IReadOnlyDictionary<int, double> PooledHfrByPosition { get; set; }

        public double PooledHfrAt(int focuserPosition) => PooledHfrByPosition[focuserPosition];
    }

    /// <summary>
    /// Holds one auto-focus run as loaded-once frames + an injected detection delegate + the AF fit config, and
    /// evaluates a candidate <see cref="StarDetectorParams"/> into a <see cref="RunEvaluationMetrics"/> (run
    /// detection on every frame, pool frames sharing a focuser position, fit the AF curve, read σ(focus)/R²/χ²,
    /// and — when labels exist — recall/precision).
    ///
    /// <para>This type is image-source-agnostic: the frame payload is opaque (<see cref="RunFrame.Image"/> is an
    /// <see cref="object"/>) and detection is an injected delegate, so the same evaluator drives the live wizard
    /// (IRenderedImage) and the offline harness (Mat) unchanged.</para>
    ///
    /// <para>No per-frame detection cache is kept on purpose: the optimizer already memoizes J at the params
    /// level (one evaluator call per distinct params via <see cref="StarDetector.ComputeCacheKey"/>), so caching
    /// per frame here would be redundant and would pin many star lists in memory.</para>
    /// </summary>
    public sealed class RunEvaluationData {
        // A hyperbola has 4-5 parameters; fewer than this many distinct positions can never determine a fit.
        private const int MinPositionsForFit = 3;

        private readonly IReadOnlyList<RunFrame> frames;
        private readonly Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect;
        private readonly IAlglibAPI alglibAPI;
        private readonly RunFitConfig fitConfig;
        private readonly IReadOnlyList<FrameLabels> labels;

        public string RunId { get; }

        public RunEvaluationData(
            string runId,
            IReadOnlyList<RunFrame> frames,
            Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect,
            IAlglibAPI alglibAPI,
            RunFitConfig fitConfig,
            IReadOnlyList<FrameLabels> labels = null) {
            RunId = runId;
            this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
            this.detect = detect ?? throw new ArgumentNullException(nameof(detect));
            this.alglibAPI = alglibAPI ?? throw new ArgumentNullException(nameof(alglibAPI));
            this.fitConfig = fitConfig;
            this.labels = labels;
        }

        /// <summary>
        /// Evaluates a candidate params bundle into the metrics the objective consumes. Delegates to
        /// <see cref="EvaluateAndFitAsync"/> and returns only its <see cref="RunEvaluationResult.Metrics"/>.
        /// </summary>
        public async Task<RunEvaluationMetrics> EvaluateAsync(StarDetectorParams p, CancellationToken token) {
            var result = await EvaluateAndFitAsync(p, token).ConfigureAwait(false);
            return result.Metrics;
        }

        /// <summary>
        /// The full evaluation: detect every frame (deterministic order), pool frames at the same focuser
        /// position into one scatter point, fit the AF curve, and assemble the metrics (+ keep the winning fit
        /// for the step-size recommender). Never throws on a degenerate run — too few points produce NaN σ so the
        /// objective hard-fails gracefully.
        /// </summary>
        public async Task<RunEvaluationResult> EvaluateAndFitAsync(StarDetectorParams p, CancellationToken token) {
            // 1. Detect every frame in a fixed, deterministic order; collect per-frame results.
            var frameStarCounts = new List<int>(frames.Count);
            var perFrame = new List<(int FocuserPosition, FrameDetectionResult Detection)>(frames.Count);
            foreach (var frame in frames) {
                token.ThrowIfCancellationRequested();
                var detection = await detect(frame.Image, p, token).ConfigureAwait(false);
                frameStarCounts.Add(detection.StarCount);
                perFrame.Add((frame.FocuserPosition, detection));
            }

            // 2. Pool frames sharing a focuser position using the SAME semantics as the AF engine's
            //    AverageMeasurement(): mean of the per-frame measures and SEM-pooled σ (RMS of valid per-frame σs
            //    divided by sqrt(count of contributing frames)). Iterating in ascending focuser position keeps the
            //    fit input order deterministic.
            var byPosition = new SortedDictionary<int, List<MeasureAndError>>();
            foreach (var (pos, detection) in perFrame) {
                if (!byPosition.TryGetValue(pos, out var measures)) {
                    measures = new List<MeasureAndError>();
                    byPosition.Add(pos, measures);
                }
                measures.Add(new MeasureAndError { Measure = detection.AverageHFR, Stdev = detection.HFRStdDev });
            }

            var points = new List<ScatterErrorPoint>(byPosition.Count);
            var pooledHfr = new Dictionary<int, double>(byPosition.Count);
            foreach (var kvp in byPosition) {
                var pooled = kvp.Value.AverageMeasurement();
                pooledHfr[kvp.Key] = pooled.Measure;
                points.Add(new ScatterErrorPoint(kvp.Key, pooled.Measure, 0, SafeDisplayError(pooled.Stdev)));
            }

            // 3. Fit (or degrade gracefully when there are too few distinct positions).
            var metrics = new RunEvaluationMetrics {
                SigmaFocus = double.NaN,
                LooStdError = double.NaN,
                StepSize = fitConfig.StepSize,
                RSquared = 0.0,
                ReducedChiSquared = double.NaN,
                FrameStarCounts = frameStarCounts
            };
            AlglibHyperbolicFitting bestFit = null;

            if (points.Count >= MinPositionsForFit) {
                var winningModel = AlglibHyperbolicFitting.SelectBestModel(
                    alglibAPI, points, fitConfig.StepSize, fitConfig.UseWeights,
                    fitConfig.MaxOutlierRejections, fitConfig.RejectionConfidence,
                    out bestFit, out _);

                if (bestFit != null) {
                    metrics.SigmaFocus = bestFit.MinimumStdError;
                    metrics.RSquared = bestFit.RSquared;
                    metrics.ReducedChiSquared = bestFit.ReducedChiSquared;

                    // Only pay for the leave-one-out fallback when the parametric σ is unusable (matches the
                    // objective, which prefers SigmaFocus and only falls back to LooStdError).
                    if (double.IsNaN(metrics.SigmaFocus) || double.IsInfinity(metrics.SigmaFocus)) {
                        metrics.LooStdError = AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(
                            alglibAPI, winningModel, points, fitConfig.StepSize, fitConfig.UseWeights);
                    }
                }
            }

            // 4. Labels (only when supplied): recall/precision per labeled position, averaged across them.
            ApplyLabelScores(metrics, perFrame);

            return new RunEvaluationResult {
                Metrics = metrics,
                BestFit = bestFit,
                PooledPointCount = points.Count,
                PooledHfrByPosition = pooledHfr
            };
        }

        private void ApplyLabelScores(RunEvaluationMetrics metrics, List<(int FocuserPosition, FrameDetectionResult Detection)> perFrame) {
            if (labels == null || labels.Count == 0) {
                return;
            }

            double recallSum = 0.0, precisionSum = 0.0;
            var scored = 0;
            foreach (var label in labels) {
                // Accepted centers at this labeled position: union across every frame sampled there.
                var accepted = perFrame
                    .Where(f => f.FocuserPosition == label.FocuserPosition)
                    .SelectMany(f => f.Detection.StarCenters ?? Array.Empty<(double X, double Y)>())
                    .ToList();
                var (recall, precision) = OptimizationObjective.ComputeLabelScores(accepted, label.Missed, label.ShouldReject, label.RadiusPx);
                recallSum += recall;
                precisionSum += precision;
                scored++;
            }

            if (scored > 0) {
                metrics.Recall = recallSum / scored;
                metrics.Precision = precisionSum / scored;
            }
        }

        /// <summary>
        /// Builds the optimizer's evaluator delegate from N runs: evaluates each run in the given (deterministic)
        /// order and returns one <see cref="RunEvaluationMetrics"/> per run, ready to feed
        /// <see cref="StarDetectionOptimizer.OptimizeAsync"/>.
        /// </summary>
        public static Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> CreateEvaluator(
            IReadOnlyList<RunEvaluationData> runs) {
            if (runs == null) {
                throw new ArgumentNullException(nameof(runs));
            }
            return async (p, token) => {
                var results = new List<RunEvaluationMetrics>(runs.Count);
                foreach (var run in runs) {
                    token.ThrowIfCancellationRequested();
                    results.Add(await run.EvaluateAsync(p, token).ConfigureAwait(false));
                }
                return results;
            };
        }

        /// <summary>
        /// σ for the fit's display/weight layer: keep a finite, non-negative value; non-finite σ (NaN/±Inf) and
        /// negatives map to 0 = "no error bar". Mirrors <c>AutoFocusEngine.SafeDisplayError</c> so the fit inputs
        /// here are identical to the ones the AF engine produces.
        /// </summary>
        private static double SafeDisplayError(double stdev) {
            return double.IsFinite(stdev) ? Math.Max(0.0, stdev) : 0.0;
        }
    }
}
