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

        /// <summary>Number of accepted stars on this frame admitted only by a defocus-RELAXED gate (would have
        /// failed the strict gate) — i.e. <c>StarDetectorMetrics.RelaxationAdmittedCount</c> for this frame. Zero
        /// whenever the defocus-aware gates are OFF, so the objective stays bit-identical at the baseline. Feeds the
        /// optimizer's label-free precision/false-positive penalty.</summary>
        public int RelaxationAdmittedCount { get; set; }
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
    /// A ground-truth label region in image space: a box (top-left X,Y + W,H). Recall/precision are scored by
    /// box-containment (an accepted star center lying inside the box), so the box defines the region directly and no
    /// separate radius is needed. Dependency-light (a plain struct) so the objective stays free of any drawing/UI type.
    /// </summary>
    public readonly struct LabelBox {
        public double X { get; }
        public double Y { get; }
        public double W { get; }
        public double H { get; }

        public LabelBox(double x, double y, double w, double h) {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        /// <summary>True when (cx,cy) lies within this box (inclusive AABB containment).</summary>
        public bool Contains(double cx, double cy) =>
            cx >= X && cx <= X + W && cy >= Y && cy <= Y + H;
    }

    /// <summary>
    /// Optional ground-truth labels for one focuser position, used to score recall/precision. Plumbed now (T3),
    /// consumed by the labeling workflow (T7); when no labels are supplied the run scores label-agnostically. Each
    /// label is a BOX; recall/precision use box-containment of accepted-star centers.
    /// </summary>
    public sealed class FrameLabels {
        public int FocuserPosition { get; set; }

        /// <summary>Stars that SHOULD have been detected (recovered when an accepted center lies inside the box).</summary>
        public IReadOnlyList<LabelBox> Missed { get; set; }

        /// <summary>Stars that SHOULD have been rejected (correctly excluded when NO accepted center lies inside the box).</summary>
        public IReadOnlyList<LabelBox> ShouldReject { get; set; }

        /// <summary>Candidates that WERE detected but a gate rejected, which the user judges should have been KEPT.
        /// These fold into recall (treated identically to <see cref="Missed"/> — both are recall targets the optimizer
        /// should recover with an accepted star inside the box).</summary>
        public IReadOnlyList<LabelBox> WronglyRejected { get; set; }

        /// <summary>Legacy default-radius carrier (used only when widening older point-only labels to boxes on load);
        /// no longer consumed by the box-containment scoring.</summary>
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

        // Concurrency cap for the per-frame detect/build+gate loop WITHIN one evaluation. Frames are largely
        // single-threaded in their expensive early stage (wavelet/binarization), so running a few concurrently
        // fills idle cores. The cap is deliberately SMALL for two reasons:
        //   (1) Memory — each in-flight frame holds/builds a ~244 MB early context at 61 MP; the cap bounds peak
        //       footprint to (cap × one source frame-set), not the whole run at once.
        //   (2) Oversubscription — the LATE/gate stage already runs an internal Parallel.For over candidates on the
        //       shared scheduler (≈ ProcessorCount threads). Letting many frames into the gate concurrently would
        //       multiply that against the frame fan-out, so we keep the frame fan-out to a small fraction of cores.
        // Default: ProcessorCount/4, floored at 2 and never more than the frame count. On an 8-core box this is 2;
        // on a 16-core box, 4. A fixed-but-modest value that fills idle cores during the single-threaded early
        // stage without contending hard with the gate phase or blowing the memory budget.
        private static int DefaultFrameParallelism(int frameCount) =>
            Math.Max(1, Math.Min(frameCount, Math.Max(2, Environment.ProcessorCount / 4)));

        // Test/override hook: 1 forces the sequential path (used to prove parallel ≡ sequential); a value > 1 caps
        // the frame fan-out at that value; <= 0 means "use DefaultFrameParallelism". Not persisted; an in-memory knob
        // only (settable so tests can force cap=1 to prove the parallel path ≡ the sequential path).
        public int FrameParallelismOverride { get; set; } = 0;

        /// <summary>The effective frame-detection concurrency cap for this run: the override when set (> 0), else
        /// <see cref="DefaultFrameParallelism"/>. Always in [1, frameCount].</summary>
        private int EffectiveFrameParallelism =>
            FrameParallelismOverride > 0
                ? Math.Max(1, Math.Min(frames.Count, FrameParallelismOverride))
                : DefaultFrameParallelism(frames.Count);

        private readonly IReadOnlyList<RunFrame> frames;
        private readonly Func<object, StarDetectorParams, CancellationToken, Task<FrameDetectionResult>> detect;
        private readonly ISplitFrameDetector splitDetector;
        private readonly IAlglibAPI alglibAPI;
        private readonly RunFitConfig fitConfig;
        private readonly IReadOnlyList<FrameLabels> labels;

        // Early-context cache (only used when splitDetector != null). One slot per frame index; each slot holds the
        // context most recently built for that frame plus the early key it was built under. A candidate whose early
        // key matches the slot reuses the context; a mismatch disposes the old context and rebuilds. Bounded to one
        // context per frame by construction (see class doc). Guarded by cacheLock.
        //
        // Concurrency: WITHIN one evaluation, frames are detected concurrently (capped — see DefaultFrameParallelism),
        // so multiple DetectFrameAsync calls may run at once — but each touches its OWN slot (keyed by frame index),
        // never another frame's. The lock serializes the check-and-claim and the post-build publish so that, even for
        // the same frame, no two callers build the same slot twice and no built context is leaked. Distinct slots are
        // independent: two frames building concurrently into different slots never contend except briefly on the lock
        // (held only around the O(1) slot read/write, not the expensive build). Disposal on eviction/Dispose stays
        // correct: the superseded context captured under the lock is disposed exactly once, by the claiming caller.
        private readonly CachedContext[] contextCache;
        private readonly object cacheLock = new object();
        private bool disposed;

        private sealed class CachedContext {
            public string EarlyKey;
            public IDisposable Context;

            // Set while a context for EarlyKey is being built (between claim and publish). A concurrent caller that
            // wants the SAME (slot, key) awaits this instead of starting a second build, so a slot is never built
            // twice concurrently and no built context is leaked. In normal operation (one task per frame index per
            // evaluation, sequential evaluations) this is never contended; it makes same-slot concurrency safe by
            // construction rather than by assumption.
            public Task<IDisposable> Building;
        }

        public string RunId { get; }

        /// <summary>
        /// A lightweight, Mat-free description of this run's frames for the interactive review step: each frame's
        /// disk path (<see cref="RunFrame.FrameId"/>, set to the saved-image path by the loader) + its focuser
        /// position, tagged with this run's <see cref="RunId"/>. This deliberately exposes ONLY paths/positions
        /// (never the opaque <see cref="RunFrame.Image"/> Mats), so the wizard can snapshot it and rebuild the
        /// review by detecting from disk AFTER the source Mats are disposed. Returned in frame (load) order.
        /// </summary>
        public IReadOnlyList<Review.FrameReviewDescriptor> GetFrameDescriptors() =>
            frames
                .Select(f => new Review.FrameReviewDescriptor(RunId, f.FocuserPosition, f.FrameId))
                .ToList();

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

            var context = await GetOrBuildContextAsync(frameIndex, frameImage, p, token).ConfigureAwait(false);
            return splitDetector.GateAndMeasure(context, p);
        }

        /// <summary>
        /// Returns the early-detection context for one frame's slot, reusing it when the early key matches and
        /// (re)building it otherwise. Safe under concurrency: each frame uses its OWN slot (by index) and the lock
        /// serializes the check/claim/publish so a slot is never built twice concurrently and no built context is
        /// leaked. The expensive <see cref="ISplitFrameDetector.BuildContextAsync"/> runs OUTSIDE the lock; the lock
        /// is held only around the O(1) slot inspection and the publish of the build task / built context.
        /// </summary>
        private async Task<IDisposable> GetOrBuildContextAsync(int frameIndex, object frameImage, StarDetectorParams p, CancellationToken token) {
            var earlyKey = splitDetector.ComputeEarlyKey(p);

            // Spin on (claim a build) | (reuse cached) | (await another caller's in-flight build for this key). The
            // loop only re-iterates when an in-flight build for a DIFFERENT key (or a build we awaited) leaves the
            // slot not matching ours — which cannot happen within one evaluation (one task per slot), but is handled
            // for defensiveness so concurrent same-slot callers can never double-build or leak.
            while (true) {
                token.ThrowIfCancellationRequested();

                Task<IDisposable> awaitExisting = null;                 // an in-flight build by another caller we should await
                TaskCompletionSource<IDisposable> ourClaim = null;      // set if WE claimed the slot to build
                IDisposable evicted = null;                            // a superseded context WE must dispose after building

                lock (cacheLock) {
                    if (disposed) {
                        throw new ObjectDisposedException(nameof(RunEvaluationData));
                    }
                    var slot = contextCache[frameIndex];
                    if (slot != null && slot.Context != null && slot.EarlyKey == earlyKey) {
                        return slot.Context; // reuse the published context
                    }
                    if (slot != null && slot.Building != null && slot.EarlyKey == earlyKey) {
                        awaitExisting = slot.Building; // someone is already building exactly this; await it
                    } else {
                        // Claim the slot for OUR build: record the early key + a build task others can await, and
                        // remember any superseded context so we dispose it after the new one is built.
                        evicted = slot?.Context;
                        ourClaim = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
                        contextCache[frameIndex] = new CachedContext { EarlyKey = earlyKey, Context = null, Building = ourClaim.Task };
                    }
                }

                if (ourClaim != null) {
                    // We own the build: run it OUTSIDE the lock and publish the result.
                    return await RunClaimedBuildAsync(frameIndex, frameImage, p, earlyKey, ourClaim, evicted, token).ConfigureAwait(false);
                }

                // Await another caller's in-flight build for the same key, then re-check the slot (it should now be
                // published with our key; if a later eviction changed it we loop and re-claim). Swallow that build's
                // own exception here — the slot will simply read as un-built and we re-claim on the next iteration.
                try {
                    await awaitExisting.ConfigureAwait(false);
                } catch {
                    // The owning build failed/cancelled; loop and re-evaluate the slot.
                }
            }
        }

        /// <summary>
        /// Runs a build we have claimed for <paramref name="frameIndex"/>: builds outside the lock, disposes the
        /// superseded context, publishes the built context into the slot, and signals waiters via
        /// <paramref name="tcs"/>. On failure the slot's build marker is cleared so a later caller can retry, and the
        /// freshly-built context (if any) is disposed so nothing leaks.
        /// </summary>
        private async Task<IDisposable> RunClaimedBuildAsync(
                int frameIndex, object frameImage, StarDetectorParams p, string earlyKey,
                TaskCompletionSource<IDisposable> tcs, IDisposable evicted, CancellationToken token) {
            IDisposable built = null;
            try {
                built = await splitDetector.BuildContextAsync(frameImage, p, token).ConfigureAwait(false);
                evicted?.Dispose();

                bool disposeBuilt = false;
                lock (cacheLock) {
                    if (disposed) {
                        // Disposed while we built: don't publish; dispose the orphan after the lock.
                        disposeBuilt = true;
                    } else {
                        contextCache[frameIndex] = new CachedContext { EarlyKey = earlyKey, Context = built, Building = null };
                    }
                }
                if (disposeBuilt) {
                    built.Dispose();
                    tcs.TrySetResult(null);
                    throw new ObjectDisposedException(nameof(RunEvaluationData));
                }

                tcs.TrySetResult(built);
                return built;
            } catch (Exception ex) {
                // Failed/cancelled build: clear our build marker so the slot can be re-claimed, dispose any orphaned
                // context, and propagate to both this caller and any waiters.
                lock (cacheLock) {
                    var slot = contextCache[frameIndex];
                    if (slot != null && ReferenceEquals(slot.Building, tcs.Task)) {
                        contextCache[frameIndex] = null;
                    }
                }
                if (built != null) {
                    built.Dispose();
                }
                tcs.TrySetException(ex);
                throw;
            }
        }

        /// <summary>
        /// Detects every frame for one candidate, running up to <see cref="EffectiveFrameParallelism"/> frames
        /// concurrently and returning their results in a per-INDEX array (result[i] is frame i's detection,
        /// regardless of completion order). Determinism: results are assigned by frame index, never appended in
        /// completion order, so the caller sees the identical sequence the old sequential loop produced.
        /// Cancellation: a linked token source cancels all in-flight frames on request (or when any frame faults);
        /// the first fault/cancellation is rethrown after all started tasks settle, so no detection is left running.
        /// </summary>
        private async Task<FrameDetectionResult[]> DetectAllFramesAsync(StarDetectorParams p, CancellationToken token) {
            var count = frames.Count;
            var results = new FrameDetectionResult[count];
            if (count == 0) {
                return results;
            }

            token.ThrowIfCancellationRequested();

            var degree = EffectiveFrameParallelism;
            if (degree <= 1) {
                // Forced/derived sequential path: identical ordering and behavior to the original loop. Used by tests
                // (cap=1) to prove the parallel path produces the same metrics, and on single-frame/single-core runs.
                for (int i = 0; i < count; i++) {
                    token.ThrowIfCancellationRequested();
                    results[i] = await DetectFrameAsync(i, frames[i].Image, p, token).ConfigureAwait(false);
                }
                return results;
            }

            // Bounded fan-out: a SemaphoreSlim caps the number of frames in flight at once; a linked CTS lets us
            // cancel every in-flight frame the moment one faults or the caller cancels. Each task writes ONLY its own
            // index, so there is no shared mutable state across tasks beyond the cache (per-slot safe) and the array
            // (disjoint indices) — no locking needed for the results array.
            using var throttle = new SemaphoreSlim(degree, degree);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var linkedToken = linkedCts.Token;

            var tasks = new Task[count];
            try {
                for (int i = 0; i < count; i++) {
                    linkedToken.ThrowIfCancellationRequested();
                    await throttle.WaitAsync(linkedToken).ConfigureAwait(false);

                    var index = i;
                    tasks[index] = Task.Run(async () => {
                        try {
                            results[index] = await DetectFrameAsync(index, frames[index].Image, p, linkedToken).ConfigureAwait(false);
                        } catch {
                            // Cancel siblings so the whole evaluation tears down promptly; the original exception is
                            // surfaced via Task.WhenAll below.
                            linkedCts.Cancel();
                            throw;
                        } finally {
                            throttle.Release();
                        }
                    }, linkedToken);
                }
            } catch (OperationCanceledException) {
                // The acquire loop was cancelled (caller token or a sibling fault). Make sure every already-started
                // task settles before we surface, so nothing keeps building after we return.
                linkedCts.Cancel();
                await WhenAllSafe(tasks).ConfigureAwait(false);
                throw;
            }

            await Task.WhenAll(tasks.Where(t => t != null)).ConfigureAwait(false);
            return results;
        }

        /// <summary>Awaits all non-null tasks, swallowing their exceptions (used on the cancellation teardown path so
        /// the in-flight frames finish before we rethrow the originating cancellation/fault).</summary>
        private static async Task WhenAllSafe(Task[] tasks) {
            foreach (var t in tasks) {
                if (t == null) {
                    continue;
                }
                try {
                    await t.ConfigureAwait(false);
                } catch {
                    // Intentionally ignored — teardown path.
                }
            }
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
            // 1. Detect every frame, CONCURRENTLY but capped (see EffectiveFrameParallelism), to fill idle cores
            //    during the largely single-threaded early stage. Determinism is preserved by assembling results into
            //    a per-index array (assign by frame index, NOT completion order) and then consuming that array in
            //    strict ascending-index order below — so every downstream value (pooling, fit, J, labels) is byte-
            //    identical to the old sequential loop regardless of how the frame tasks interleave. When a split
            //    detector is in use each frame goes through its own early-context cache slot (reused across late-only
            //    changes); concurrent builds touch distinct slots, so the cache is unaffected by the parallelism.
            var detections = await DetectAllFramesAsync(p, token).ConfigureAwait(false);

            var frameStarCounts = new List<int>(frames.Count);
            var frameRelaxationAdmittedCounts = new List<int>(frames.Count);
            var frameFocuserPositions = new List<int>(frames.Count);
            var perFrame = new List<(int FocuserPosition, FrameDetectionResult Detection)>(frames.Count);
            for (int i = 0; i < frames.Count; i++) {
                var detection = detections[i];
                frameStarCounts.Add(detection.StarCount);
                // Parallel per-frame lists feeding the objective's label-free precision penalty. Relaxed counts are
                // 0 across the board unless a defocus-aware gate is on, so the baseline J is unaffected.
                frameRelaxationAdmittedCounts.Add(detection.RelaxationAdmittedCount);
                frameFocuserPositions.Add(frames[i].FocuserPosition);
                perFrame.Add((frames[i].FocuserPosition, detection));
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
                FrameStarCounts = frameStarCounts,
                FrameRelaxationAdmittedCounts = frameRelaxationAdmittedCounts,
                FrameFocuserPositions = frameFocuserPositions,
                BestFocusPosition = double.NaN
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
                    // Fitted curve-minimum focuser position; defines the near-focus window for SDefocusPrecision.
                    // Left NaN if the fit didn't produce a finite minimum (then SDefocusPrecision falls back to the
                    // run-level relaxed-fraction signal).
                    var minX = bestFit.Minimum.X;
                    metrics.BestFocusPosition = (double.IsNaN(minX) || double.IsInfinity(minX)) ? double.NaN : minX;

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
                // Recall targets = explicitly-missed (false negatives) ∪ wrongly-rejected (detected-but-gated stars the
                // user wants kept). Both want an accepted center INSIDE the box, so they share the recall term. The
                // union is folded here; ComputeLabelScores does box-containment. Precision still depends only on
                // ShouldReject. When neither list has entries the union is empty, so recall stays 1.0 exactly as before
                // — the unlabeled / missed-only paths are bit-identical.
                var recallTargets = UnionTargets(label.Missed, label.WronglyRejected);
                var (recall, precision) = OptimizationObjective.ComputeLabelScores(accepted, recallTargets, label.ShouldReject);
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
        /// Concatenates the two recall-target box lists (missed ∪ wrongly-rejected) into a single list. Returns the
        /// other list as-is when one is null/empty (so the common missed-only / wrongly-only cases allocate nothing
        /// extra and stay bit-identical to the prior single-list behavior). Returns null only when both are empty.
        /// </summary>
        private static IReadOnlyList<LabelBox> UnionTargets(
            IReadOnlyList<LabelBox> a, IReadOnlyList<LabelBox> b) {
            var aEmpty = a == null || a.Count == 0;
            var bEmpty = b == null || b.Count == 0;
            if (aEmpty && bEmpty) {
                return a; // null or empty — ComputeLabelScores treats it as recall = 1.0
            }
            if (bEmpty) {
                return a;
            }
            if (aEmpty) {
                return b;
            }
            var union = new List<LabelBox>(a.Count + b.Count);
            union.AddRange(a);
            union.AddRange(b);
            return union;
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
