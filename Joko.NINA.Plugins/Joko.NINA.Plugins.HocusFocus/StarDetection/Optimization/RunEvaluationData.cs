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
    /// <para>Optionally, an <see cref="ISplitFrameDetector"/> may be supplied instead of a monolithic detect
    /// delegate. When it is, the evaluator caches the EXPENSIVE early-stage detection result per (frame, early
    /// cache key) and reuses it across candidate evaluations that change only late-stage gate params (the bulk of
    /// a compass search), re-running only the cheap gate+measure step. This is the ~2× optimizer speedup; it is a
    /// pure performance optimization and produces byte-identical per-frame results to the non-cached path. The
    /// cache is bounded to ONE context per frame (the current early key), since the search moves the incumbent —
    /// when a frame's early key changes the prior context is disposed before the new one is built; at 61 MP each
    /// cached context pins ~244 MB, so keeping only one per frame caps the footprint at one frame-set of source
    /// Mats. The cache lives for this <see cref="RunEvaluationData"/> instance's lifetime (which in the wizard
    /// spans seed-guard → optimize → summary, all evaluated against the same instance); <see cref="Dispose"/>
    /// releases all cached contexts and MUST be called by the owner once the instance is no longer needed.</para>
    ///
    /// <para>When only a monolithic delegate is supplied (the legacy path), no per-frame detection cache is kept:
    /// the optimizer already memoizes J at the params level (one evaluator call per distinct params via
    /// <see cref="StarDetector.ComputeCacheKey"/>), so star-list caching there would be redundant.</para>
    /// </summary>
    public sealed class RunEvaluationData : IDisposable {
        /// <summary>
        /// A split-capable detection façade the evaluator can drive in two phases so it can cache the expensive
        /// early phase. Implemented by the wizard loader (over <c>HocusFocusStarDetection</c>/<c>StarDetector</c>)
        /// and the offline harness; the frame payload and the context are opaque to the evaluator.
        /// </summary>
        public interface ISplitFrameDetector {
            /// <summary>A deterministic key over ONLY the early-affecting params (see
            /// <c>StarDetector.ComputeEarlyCacheKey</c>): two params with the same key yield an identical early
            /// context, so a cached context may be reused.</summary>
            string ComputeEarlyKey(StarDetectorParams p);

            /// <summary>Runs the expensive early detection stage for one frame and returns an opaque, disposable
            /// context. The evaluator owns disposing it.</summary>
            Task<IDisposable> BuildContextAsync(object frameImage, StarDetectorParams p, CancellationToken token);

            /// <summary>Runs the cheap late (gate + measure) stage against a previously-built context. Pure over
            /// the context (read-only), so it is safe to call repeatedly with different late params.</summary>
            FrameDetectionResult GateAndMeasure(IDisposable context, StarDetectorParams p);
        }

        // A hyperbola has 4-5 parameters; fewer than this many distinct positions can never determine a fit.
        private const int MinPositionsForFit = 3;

        private readonly IReadOnlyList<RunFrame> frames;
        private readonly Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect;
        private readonly ISplitFrameDetector splitDetector;
        private readonly IAlglibAPI alglibAPI;
        private readonly RunFitConfig fitConfig;
        private readonly IReadOnlyList<FrameLabels> labels;

        // Early-context cache (only used when splitDetector != null). One slot per frame index; each slot holds the
        // context most recently built for that frame plus the early key it was built under. A candidate whose early
        // key matches the slot reuses the context; a mismatch disposes the old context and rebuilds. Bounded to one
        // context per frame by construction (see class doc). Guarded by cacheLock so a (future) concurrent caller
        // cannot corrupt it — today the evaluator runs frames sequentially, so contention is nil.
        private readonly CachedContext[] contextCache;
        private readonly object cacheLock = new object();
        private bool disposed;

        private sealed class CachedContext {
            public string EarlyKey;
            public IDisposable Context;
        }

        public string RunId { get; }

        public RunEvaluationData(
            string runId,
            Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect,
            ISplitFrameDetector splitDetector,
            IReadOnlyList<RunFrame> frames,
            IAlglibAPI alglibAPI,
            RunFitConfig fitConfig,
            IReadOnlyList<FrameLabels> labels) {
            RunId = runId;
            this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
            this.alglibAPI = alglibAPI ?? throw new ArgumentNullException(nameof(alglibAPI));
            this.fitConfig = fitConfig;
            this.labels = labels;

            // Exactly one of (detect, splitDetector) must be supplied.
            if (detect == null && splitDetector == null) {
                throw new ArgumentNullException(nameof(detect), "either a detect delegate or a split detector is required");
            }
            this.detect = detect;
            this.splitDetector = splitDetector;
            this.contextCache = splitDetector != null ? new CachedContext[frames.Count] : null;
        }

        /// <summary>Legacy constructor: a monolithic detect delegate, no early-context cache.</summary>
        public RunEvaluationData(
            string runId,
            IReadOnlyList<RunFrame> frames,
            Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect,
            IAlglibAPI alglibAPI,
            RunFitConfig fitConfig,
            IReadOnlyList<FrameLabels> labels = null)
            : this(runId, detect ?? throw new ArgumentNullException(nameof(detect)), null, frames, alglibAPI, fitConfig, labels) {
        }

        /// <summary>Split constructor: caches the expensive early detection per (frame, early key) and reuses it
        /// across late-only candidate changes (the optimizer speedup). Results are identical to the delegate path.</summary>
        public RunEvaluationData(
            string runId,
            IReadOnlyList<RunFrame> frames,
            ISplitFrameDetector splitDetector,
            IAlglibAPI alglibAPI,
            RunFitConfig fitConfig,
            IReadOnlyList<FrameLabels> labels = null)
            : this(runId, null, splitDetector ?? throw new ArgumentNullException(nameof(splitDetector)), frames, alglibAPI, fitConfig, labels) {
        }

        /// <summary>
        /// Detects one frame, going through the early-context cache when a split detector was supplied. On a
        /// late-only param change the cached early context is reused; on an early-key change (or first use) the
        /// prior context is disposed and a new one is built. Falls back to the monolithic delegate otherwise.
        /// </summary>
        private async Task<FrameDetectionResult> DetectFrameAsync(int frameIndex, object frameImage, StarDetectorParams p, CancellationToken token) {
            if (splitDetector == null) {
                return await detect(frameImage, p, token).ConfigureAwait(false);
            }

            var earlyKey = splitDetector.ComputeEarlyKey(p);
            IDisposable context;
            IDisposable evicted = null;
            lock (cacheLock) {
                var slot = contextCache[frameIndex];
                if (slot != null && slot.Context != null && slot.EarlyKey == earlyKey) {
                    context = slot.Context; // reuse
                } else {
                    context = null; // must build (below, outside the lock)
                    evicted = slot?.Context; // dispose the superseded context after building the new one
                }
            }

            if (context == null) {
                // Build outside the lock (BuildContextAsync is the expensive stage). The evaluator runs frames
                // sequentially today, so there is no risk of two builders racing for the same slot.
                var built = await splitDetector.BuildContextAsync(frameImage, p, token).ConfigureAwait(false);
                evicted?.Dispose();
                lock (cacheLock) {
                    contextCache[frameIndex] = new CachedContext { EarlyKey = earlyKey, Context = built };
                }
                context = built;
            }

            return splitDetector.GateAndMeasure(context, p);
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
            // 1. Detect every frame in a fixed, deterministic order; collect per-frame results. When a split
            //    detector is in use this goes through the early-context cache (reused across late-only changes),
            //    which is a pure-perf optimization — the per-frame results are identical to the delegate path.
            var frameStarCounts = new List<int>(frames.Count);
            var perFrame = new List<(int FocuserPosition, FrameDetectionResult Detection)>(frames.Count);
            for (int i = 0; i < frames.Count; i++) {
                token.ThrowIfCancellationRequested();
                var frame = frames[i];
                var detection = await DetectFrameAsync(i, frame.Image, p, token).ConfigureAwait(false);
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

        /// <summary>
        /// Releases every cached early-detection context (frees the pinned source Mats) held for this instance's
        /// lifetime. Idempotent. Only the split path holds contexts; the monolithic path keeps none, so Dispose is a
        /// no-op there.
        /// </summary>
        public void Dispose() {
            lock (cacheLock) {
                if (disposed) {
                    return;
                }
                disposed = true;
                if (contextCache != null) {
                    for (int i = 0; i < contextCache.Length; i++) {
                        contextCache[i]?.Context?.Dispose();
                        contextCache[i] = null;
                    }
                }
            }
        }
    }
}
