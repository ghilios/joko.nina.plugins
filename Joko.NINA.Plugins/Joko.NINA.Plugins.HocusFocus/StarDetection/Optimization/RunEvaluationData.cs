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

        /// <summary>Accepted-star centers (image pixel coords), used for label recall/precision and region coverage.</summary>
        public IReadOnlyList<(double X, double Y)> StarCenters { get; set; }

        /// <summary>Accepted-star HFRs (PARALLEL to <see cref="StarCenters"/>; same surviving set, same order). Feeds
        /// the optimizer's extreme-HFR outlier penalty. Null/empty whenever the caller doesn't populate it, in which
        /// case the penalty is inert (objective bit-identical).</summary>
        public IReadOnlyList<double> StarHFRs { get; set; }

        /// <summary>Accepted-star measured Sensitivity-gate SNRs (PARALLEL to <see cref="StarCenters"/>; same
        /// surviving set, same order) — i.e. <see cref="Star.MeasuredSensitivity"/> carried through (see that
        /// property's doc comment for the full explanation of what it is and its caveats). INERT DATA: no sub-score
        /// or objective term reads this yet; it exists so a later exposure-recommendation feature can read the
        /// measured per-star SNR without re-plumbing. NaN entries where the caller can't determine it (e.g. a
        /// failed cast to the concrete star type).
        /// <para>
        /// TWO CAVEATS a consumer must respect (both inherited from <see cref="Star.MeasuredSensitivity"/>):
        /// (1) values are in BINNED-PIXEL space when <c>StarDetectorParams.DetectionBinning &gt; 1</c> — NOT
        /// comparable across frames/runs detected at different binning factors; (2) each entry is EITHER the
        /// per-pixel <c>NormalizedBrightness / σ</c> OR the donut matched-filter <c>TotalFlux / (σ√N)</c>, depending
        /// on <c>StarDetectorParams.DefocusAwareDonutDetection</c> and candidate size — two statistics with
        /// different scalings that must not be pooled or thresholded as a single physical quantity.
        /// </para></summary>
        public IReadOnlyList<double> StarSnrs { get; set; }

        /// <summary>Full-frame sensor dimensions (pixels) used to convert <see cref="StarCenters"/> to ratio coords
        /// for the region-coverage metric. 0 when unknown, in which case coverage is skipped for the frame.</summary>
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }

        /// <summary>Number of accepted stars on this frame admitted only by a defocus-RELAXED gate (would have
        /// failed the strict gate) — i.e. <c>StarDetectorMetrics.RelaxationAdmittedCount</c> for this frame. Zero
        /// whenever the defocus-aware gates are OFF, so the objective stays bit-identical at the baseline. Feeds the
        /// optimizer's label-free precision/false-positive penalty.</summary>
        public int RelaxationAdmittedCount { get; set; }

        /// <summary>
        /// Candidates on this frame rejected by the Sensitivity gate (<c>StarDetectorMetrics.LowSensitivity</c>).
        /// The exposure recommendation's key discriminator: a candidate rejected here EXISTS and merely fell below
        /// the gate, so it is evidence that more signal could convert it. ZERO means the gate is not what is
        /// holding the star count down, and a longer exposure has no mechanism to help through it.
        /// </summary>
        public int LowSensitivityCount { get; set; }

        /// <summary>
        /// Candidates on this frame rejected as flat-topped (<c>StarDetectorMetrics.TooFlat</c>): the gate is
        /// <c>StarMedian &gt;= PeakResponse × Peak</c>. Heavily defocused stars go flat-topped — a filled disk with
        /// no central obstruction, an annulus with one — so a frame far enough from focus rejects its stars here no
        /// matter how much signal they carry. Note this gate has NO defocus-aware relaxation (unlike distortion and
        /// centering), so donut recovery does not affect it; only <c>PeakResponse</c> does. Concentrated on the
        /// sweep's outer frames, it means the sweep reaches further from focus than the detector can follow.
        /// </summary>
        public int TooFlatCount { get; set; }

        /// <summary>
        /// Candidates on this frame rejected for measuring SMALLER than the minimum HFR
        /// (<c>StarDetectorMetrics.TooLowHFR</c>); the gate is <c>star.HFR &lt;= p.MinHFR</c>, inclusive, applied in
        /// BINNED detection pixels (StarDetector.cs:1894). Unlike its two siblings this one is not about signal:
        /// the stars are there and bright, they are simply undersampled for the gate the rig is running. A
        /// concentration on the sweep's INNER frames is the F20 collapse — the gate emptying the V-curve's core,
        /// which zeroes the objective outright via the NHard floor rather than merely lowering it.
        /// </summary>
        public int TooLowHFRCount { get; set; }
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

        /// <summary>Number of NON-recovery distinct positions actually added to the fit's <see cref="Points"/>. In the
        /// weighted fit recovery positions ARE added (down-weighted), so this is less than <see cref="PooledPointCount"/>;
        /// in the un-weighted fit recovery positions are excluded, so it equals <see cref="PooledPointCount"/>. When the
        /// focus-recovery feature is off (no recovery positions) this equals <see cref="PooledPointCount"/> exactly.</summary>
        public int NonRecoveryPooledPointCount { get; set; }

        /// <summary>The pooled (mean) HFR at each distinct focuser position; for assertions/diagnostics.</summary>
        public IReadOnlyDictionary<int, double> PooledHfrByPosition { get; set; }

        /// <summary>The pooled per-position scatter points the fit was computed from (X = focuser position,
        /// Y = pooled mean HFR, ErrorY = pooled stdev via <see cref="SafeDisplayError"/>). Mat-free and cheap;
        /// exposed so the wizard can plot the auto-focus curve without re-detecting. Empty when there were too
        /// few positions to pool.</summary>
        public IReadOnlyList<ScatterErrorPoint> Points { get; set; }

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

        // Focus-recovery down-weighting: the multiplier applied to a recovery sweep position's pooled scatter (its fit
        // ErrorY) so its fit weight (1/ErrorY — see HyperbolicFittingAlglib) lands at ≈ 1/RecoveryErrorInflation of a
        // typical near-focus point's. A far-from-focus "recovery" frame is noisy (few/no stars) and should only PIN the
        // curve's wings, never STEER the minimum, so it is bracketed in at ~1/10 the weight. Single tuning knob.
        // Internal (not private) so the test suite can assert the down-weighted ErrorY against the production symbol.
        internal const double RecoveryErrorInflation = 10.0;

        // Region-coverage grid (structural, not a scoring weight): per-frame occupancy is computed over a
        // CoverageGridRows × CoverageGridCols equal tiling of the sensor, matching the inspector's region set. 3×3
        // is coarse enough that a thin-but-spread frame still registers full coverage, yet penalizes corner clusters.
        private const int CoverageGridRows = 3;
        private const int CoverageGridCols = 3;

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

        // Focus-recovery knob (in-memory, not persisted; mirrors FrameParallelismOverride's style). Number of extra
        // sweep steps per side captured for focus recovery: the outermost RecoveryStepsPerSide DISTINCT sweep positions
        // per side are DOWN-WEIGHTED in the AF curve fit (their ErrorY inflated by RecoveryErrorInflation, or excluded
        // outright when the fit is un-weighted) and TAGGED (FrameIsRecovery) so a later task's objective can exempt them
        // from the star-count gates. Default 0 ⇒ feature off ⇒ byte-identical: no recovery set is computed, no ErrorY
        // changes, no fit point is added/removed, and FrameIsRecovery on the metrics is null.
        public int RecoveryStepsPerSide { get; set; } = 0;

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

        // Cache instrumentation (split path only). ContextBuilds = expensive early-stage rebuilds actually run;
        // ContextReuses = late-only moves served from a cached context (the cheap path). The build:reuse ratio is the
        // direct measure of early-context cache health — it quantifies how much of the per-frame early cost the
        // staged search (T14) / context cache avoids. Updated with Interlocked so the concurrent per-frame loop is safe.
        private long contextBuilds;
        private long contextReuses;

        /// <summary>Number of expensive early-stage <see cref="ISplitFrameDetector.BuildContextAsync"/> calls that
        /// actually ran (cache misses). Zero on the legacy monolithic path.</summary>
        public long ContextBuilds => System.Threading.Interlocked.Read(ref contextBuilds);

        /// <summary>Number of frame detections served by reusing an already-built early context (cache hits) — the
        /// cheap late-only path. Zero on the legacy monolithic path.</summary>
        public long ContextReuses => System.Threading.Interlocked.Read(ref contextReuses);

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

        /// <summary>The number of frames in this run (used by the wizard to set determinate "Analyzing frames"
        /// progress totals before the first evaluation builds the per-frame early contexts).</summary>
        public int FrameCount => frames.Count;

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
                        System.Threading.Interlocked.Increment(ref contextReuses);
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
                System.Threading.Interlocked.Increment(ref contextBuilds);
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
        private async Task<FrameDetectionResult[]> DetectAllFramesAsync(StarDetectorParams p, IProgress<RunLoadProgress> frameProgress, CancellationToken token) {
            var count = frames.Count;
            var results = new FrameDetectionResult[count];
            if (count == 0) {
                return results;
            }

            token.ThrowIfCancellationRequested();

            // Determinate progress for the (optional) reporter: count frames as their detection completes. Interlocked
            // so the parallel path (frames complete out of order) reports a monotonic running count safely.
            var completed = 0;
            void ReportFrameDone() {
                if (frameProgress != null) {
                    frameProgress.Report(new RunLoadProgress(System.Threading.Interlocked.Increment(ref completed), count));
                }
            }

            var degree = EffectiveFrameParallelism;
            if (degree <= 1) {
                // Forced/derived sequential path: identical ordering and behavior to the original loop. Used by tests
                // (cap=1) to prove the parallel path produces the same metrics, and on single-frame/single-core runs.
                for (int i = 0; i < count; i++) {
                    token.ThrowIfCancellationRequested();
                    results[i] = await DetectFrameAsync(i, frames[i].Image, p, token).ConfigureAwait(false);
                    ReportFrameDone();
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
                            ReportFrameDone();
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

        /// <summary>Overload reporting determinate per-frame progress as each frame's detection completes; see
        /// the <see cref="EvaluateAndFitAsync(StarDetectorParams, IProgress{RunLoadProgress}, CancellationToken)"/>
        /// remarks. <paramref name="frameProgress"/> may be null.</summary>
        public async Task<RunEvaluationMetrics> EvaluateAsync(StarDetectorParams p, IProgress<RunLoadProgress> frameProgress, CancellationToken token) {
            var result = await EvaluateAndFitAsync(p, frameProgress, token).ConfigureAwait(false);
            return result.Metrics;
        }

        /// <summary>
        /// The full evaluation: detect every frame (deterministic order), pool frames at the same focuser
        /// position into one scatter point, fit the AF curve, and assemble the metrics (+ keep the winning fit
        /// for the step-size recommender). Never throws on a degenerate run — too few points produce NaN σ so the
        /// objective hard-fails gracefully.
        /// </summary>
        public Task<RunEvaluationResult> EvaluateAndFitAsync(StarDetectorParams p, CancellationToken token) =>
            EvaluateAndFitAsync(p, frameProgress: null, token);

        /// <summary>
        /// Overload that reports determinate per-frame progress as each frame's detection completes (used by the
        /// wizard's seed-guard, where the first evaluation builds every frame's expensive early context — the long
        /// initial wait). <paramref name="frameProgress"/> is OPTIONAL: the optimizer hot path
        /// (<see cref="EvaluateAsync"/> → the 2-arg overload) passes none, so its evaluation behavior and metrics
        /// are byte-identical to before.
        /// </summary>
        public async Task<RunEvaluationResult> EvaluateAndFitAsync(StarDetectorParams p, IProgress<RunLoadProgress> frameProgress, CancellationToken token) {
            // 1. Detect every frame, CONCURRENTLY but capped (see EffectiveFrameParallelism), to fill idle cores
            //    during the largely single-threaded early stage. Determinism is preserved by assembling results into
            //    a per-index array (assign by frame index, NOT completion order) and then consuming that array in
            //    strict ascending-index order below — so every downstream value (pooling, fit, J, labels) is byte-
            //    identical to the old sequential loop regardless of how the frame tasks interleave. When a split
            //    detector is in use each frame goes through its own early-context cache slot (reused across late-only
            //    changes); concurrent builds touch distinct slots, so the cache is unaffected by the parallelism.
            var detections = await DetectAllFramesAsync(p, frameProgress, token).ConfigureAwait(false);

            var frameStarCounts = new List<int>(frames.Count);
            var frameRelaxationAdmittedCounts = new List<int>(frames.Count);
            var frameLowSensitivityCounts = new List<int>(frames.Count);
            var frameTooFlatCounts = new List<int>(frames.Count);
            var frameTooLowHfrCounts = new List<int>(frames.Count);
            var frameFocuserPositions = new List<int>(frames.Count);
            var frameStarHfrs = new List<IReadOnlyList<double>>(frames.Count);
            var frameStarSnrs = new List<IReadOnlyList<double>>(frames.Count);
            var frameRegionOccupancy = new List<double>(frames.Count);
            var perFrame = new List<(int FocuserPosition, FrameDetectionResult Detection)>(frames.Count);
            for (int i = 0; i < frames.Count; i++) {
                var detection = detections[i];
                frameStarCounts.Add(detection.StarCount);
                // Parallel per-frame lists feeding the objective's label-free precision penalty. Relaxed counts are
                // 0 across the board unless a defocus-aware gate is on, so the baseline J is unaffected.
                frameRelaxationAdmittedCounts.Add(detection.RelaxationAdmittedCount);
                // Inert in the objective; read by the exposure recommendation (see RunEvaluationMetrics).
                frameLowSensitivityCounts.Add(detection.LowSensitivityCount);
                frameTooFlatCounts.Add(detection.TooFlatCount);
                frameTooLowHfrCounts.Add(detection.TooLowHFRCount);
                frameFocuserPositions.Add(frames[i].FocuserPosition);
                // Per-frame accepted-star HFRs (extreme-HFR outlier penalty) and region occupancy (coverage reward).
                // Both stay inert in the objective when null/NaN, so the baseline J is unaffected.
                frameStarHfrs.Add(detection.StarHFRs ?? (IReadOnlyList<double>)Array.Empty<double>());
                // Per-frame accepted-star SNRs — inert data, mirrors frameStarHfrs (see FrameStarSnrs).
                frameStarSnrs.Add(detection.StarSnrs ?? (IReadOnlyList<double>)Array.Empty<double>());
                frameRegionOccupancy.Add(RegionCoverage.Occupancy(
                    detection.StarCenters, detection.ImageWidth, detection.ImageHeight, CoverageGridRows, CoverageGridCols));
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

            // A position with NO detected stars still lands in byPosition, carrying a NON-FINITE pooled HFR. Such a
            // point cannot be a fit input: least squares propagates the NaN and the fit returns NaN for EVERY
            // output, so one empty frame discards an otherwise perfect curve. Filtering is not merely an
            // optimization — it is the difference between a usable σ(focus) and none.
            //
            // This matters most on the WEIGHTED path (WeightedHyperbolicFitEnabled, the shipped default). There,
            // recovery positions are ADDED with an inflated error rather than excluded, so tagging an empty wing as
            // "recovery" does NOT keep its NaN out of the fit — no weight rescues a NaN. Excluding by finiteness
            // here is what makes the recovery exemption actually work under the default settings.
            //
            // Focus-recovery: identify the outermost RecoveryStepsPerSide DISTINCT sweep positions per side (the
            // far-from-focus extremes). Null when the feature is off / too few positions / the cap collapses perSide to
            // 0, in which case every downstream value is byte-identical to the baseline (see ComputeRecoveryPositions).
            var recoveryPositions = ComputeRecoveryPositions(byPosition, RecoveryStepsPerSide);

            // Per-frame recovery flags (PARALLEL to frameStarCounts / frameFocuserPositions). A frame is a recovery
            // frame iff its DISTINCT focuser position is in the recovery set, so ALL frames sharing a recovery position
            // are flagged. Left null (never an all-false list) when there is no recovery ⇒ baseline / FrameIsRecovery null.
            List<bool> frameIsRecovery = null;
            if (recoveryPositions != null) {
                frameIsRecovery = new List<bool>(frameFocuserPositions.Count);
                foreach (var pos in frameFocuserPositions) {
                    frameIsRecovery.Add(recoveryPositions.Contains(pos));
                }
            }

            var points = new List<ScatterErrorPoint>(byPosition.Count);
            var pooledHfr = new Dictionary<int, double>(byPosition.Count);
            int nonRecoveryPooledPointCount;
            if (recoveryPositions == null) {
                // Baseline path: pool every position and add exactly one fit point each. Positions whose pooled
                // measure is NON-FINITE are recorded in pooledHfr but kept OUT of the fit — see the note below.
                foreach (var kvp in byPosition) {
                    var pooled = kvp.Value.AverageMeasurement();
                    pooledHfr[kvp.Key] = pooled.Measure;
                    if (!double.IsFinite(pooled.Measure)) {
                        continue;
                    }
                    points.Add(new ScatterErrorPoint(kvp.Key, pooled.Measure, 0, SafeDisplayError(pooled.Stdev)));
                }
                nonRecoveryPooledPointCount = points.Count;
            } else {
                // Recovery path: pool each position ONCE (avoid re-pooling), collecting the non-recovery display errors
                // so we can derive a positive reference scatter for the down-weighting.
                var pooledByPosition = new List<(int Pos, double Measure, double DisplayError, bool IsRecovery)>(byPosition.Count);
                var nonRecoveryErrors = new List<double>();
                foreach (var kvp in byPosition) {
                    var pooled = kvp.Value.AverageMeasurement();
                    pooledHfr[kvp.Key] = pooled.Measure;
                    if (!double.IsFinite(pooled.Measure)) {
                        continue; // never a fit input at any weight — see the note below
                    }
                    var displayError = SafeDisplayError(pooled.Stdev);
                    var isRecovery = recoveryPositions.Contains(kvp.Key);
                    pooledByPosition.Add((kvp.Key, pooled.Measure, displayError, isRecovery));
                    if (!isRecovery) {
                        nonRecoveryErrors.Add(displayError);
                    }
                }

                // Reference scatter: the median of the NON-recovery positions' display errors, floored strictly
                // positive. This keeps the inflated recovery ErrorY positive even when a recovery position's own scatter
                // is 0 (a single frame there ⇒ SafeDisplayError == 0, and 0 × inflation would be inert / weightless).
                var medianError = 0.0;
                if (nonRecoveryErrors.Count > 0) {
                    (medianError, _) = nonRecoveryErrors.MedianMAD();
                }
                var refErr = Math.Max(medianError, 1e-6);

                var addedNonRecovery = 0;
                foreach (var pp in pooledByPosition) {
                    if (pp.IsRecovery) {
                        if (fitConfig.UseWeights) {
                            // Down-weight: inflate ErrorY so the fit weight (1/ErrorY) is ≈ 1/RecoveryErrorInflation of a
                            // typical point's. max(ownError, refErr) keeps it strictly positive and never below the ref.
                            var inflatedError = RecoveryErrorInflation * Math.Max(pp.DisplayError, refErr);
                            points.Add(new ScatterErrorPoint(pp.Pos, pp.Measure, 0, inflatedError));
                        }
                        // Un-weighted fit: ErrorY is ignored by the fitter, so inflation cannot down-weight — EXCLUDE the
                        // recovery position from the fit input entirely (it still populates the per-frame metrics below).
                    } else {
                        points.Add(new ScatterErrorPoint(pp.Pos, pp.Measure, 0, pp.DisplayError));
                        addedNonRecovery++;
                    }
                }
                nonRecoveryPooledPointCount = addedNonRecovery;
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
                FrameLowSensitivityCounts = frameLowSensitivityCounts,
                FrameTooFlatCounts = frameTooFlatCounts,
                FrameTooLowHFRCounts = frameTooLowHfrCounts,
                FrameFocuserPositions = frameFocuserPositions,
                FrameStarHFRs = frameStarHfrs,
                FrameStarSnrs = frameStarSnrs,
                FrameRegionOccupancy = frameRegionOccupancy,
                // null (never an all-false list) when the focus-recovery feature is off ⇒ baseline.
                FrameIsRecovery = frameIsRecovery,
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
                NonRecoveryPooledPointCount = nonRecoveryPooledPointCount,
                PooledHfrByPosition = pooledHfr,
                Points = points
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
        ///
        /// <para>F79 — <paramref name="frameProgress"/> is OPTIONAL and reports frames done across ALL runs
        /// (<c>Total</c> = Σ FrameCount), because one evaluation is one candidate scored on every loaded run and a
        /// counter that restarted per run would move backwards mid-step. It exists for the ONE case the user
        /// cannot otherwise distinguish from a hang: an early-context rebuild, where a single evaluation can run
        /// for minutes. Passing null (every headless caller) leaves the evaluation byte-identical.</para>
        /// </summary>
        public static Func<StarDetectorParams, CancellationToken, Task<IReadOnlyList<RunEvaluationMetrics>>> CreateEvaluator(
            IReadOnlyList<RunEvaluationData> runs,
            IProgress<RunLoadProgress> frameProgress = null) {
            if (runs == null) {
                throw new ArgumentNullException(nameof(runs));
            }
            var totalFrames = runs.Sum(r => r.FrameCount);
            return async (p, token) => {
                var results = new List<RunEvaluationMetrics>(runs.Count);
                var framesDoneBefore = 0;
                foreach (var run in runs) {
                    token.ThrowIfCancellationRequested();
                    if (frameProgress == null) {
                        results.Add(await run.EvaluateAsync(p, token).ConfigureAwait(false));
                    } else {
                        var offset = framesDoneBefore;
                        var shifted = new DelegateProgress<RunLoadProgress>(
                            rp => frameProgress.Report(new RunLoadProgress(offset + rp.Current, totalFrames)));
                        results.Add(await run.EvaluateAsync(p, shifted, token).ConfigureAwait(false));
                        framesDoneBefore += run.FrameCount;
                    }
                }
                return results;
            };
        }

        /// <summary>
        /// A synchronous <see cref="IProgress{T}"/> adapter. Deliberately NOT <see cref="Progress{T}"/>, which
        /// captures the SynchronizationContext of whatever thread happens to construct it — here a thread-pool
        /// thread inside the evaluator, i.e. none — and would then post callbacks asynchronously for no benefit.
        /// The reporter this forwards to is the wizard's own <c>Progress&lt;T&gt;</c>, which does the marshalling.
        /// </summary>
        private sealed class DelegateProgress<T> : IProgress<T> {
            private readonly Action<T> onReport;

            public DelegateProgress(Action<T> onReport) {
                this.onReport = onReport;
            }

            public void Report(T value) => onReport(value);
        }

        /// <summary>
        /// The outermost <paramref name="recoverySteps"/> DISTINCT sweep positions per side (the far-from-focus
        /// extremes), taken from the SORTED keys of <paramref name="byPosition"/> — the authoritative distinct
        /// positions the fit pools on. <c>perSide</c> is capped at <c>(D − MinPositionsForFit) / 2</c> so the fit always
        /// keeps ≥ <see cref="MinPositionsForFit"/> non-recovery anchor positions (2·perSide ≤ D − MinPositionsForFit).
        /// Returns <b>null</b> (no recovery) when <paramref name="recoverySteps"/> ≤ 0, there are no positions, or the
        /// cap collapses perSide to 0 — so the caller's downstream values stay byte-identical to the baseline.
        /// </summary>
        /// <summary>
        /// The smallest per-side recovery exemption that removes every STARLESS sweep position from the fit, or
        /// <c>-1</c> when no exemption can (a starless position in the interior, or the wings so wide that fewer
        /// than <see cref="MinPositionsForFit"/> positions would survive). <c>0</c> when nothing is starless.
        ///
        /// <para><b>Why this exists.</b> A starless position still enters <c>byPosition</c>, contributing a NaN
        /// HFR — so a single one poisons the hyperbolic fit and σ(focus) comes back NaN no matter how good the
        /// remaining curve is. On a deliberately wide sweep (large step × many points) the far wings are so far
        /// out of focus that NO detection settings will find stars there, so the run gets refused before the
        /// search can start, even when the interior positions form a clean, perfectly fittable V. Measured case:
        /// an 11-point sweep at step 1770 whose interior read HFR 15.2 / 9.9 / 6.3 / 5.3 / 7.0 / 11.6 / 17.1 with
        /// the minimum dead centre, refused because its four outermost frames were empty.</para>
        ///
        /// <para><b>Wings only, deliberately.</b> A starless position BETWEEN two populated ones is not a
        /// too-defocused wing — it is a gap in the data (cloud, a passing satellite, a tracking glitch), and
        /// exempting inward from the ends would silently discard good positions on either side of it to reach it.
        /// That case returns -1 so the caller reports it rather than papering over it.</para>
        ///
        /// <para>Uses star COUNT, not HFR finiteness, because the caller has counts before it has a fit; the two
        /// agree on the case that matters (no stars ⇒ no HFR).</para>
        /// </summary>
        public static int RecoveryStepsToExcludeStarlessWings(
                IReadOnlyList<int> frameFocuserPositions, IReadOnlyList<int> frameStarCounts) {
            if (frameFocuserPositions == null || frameStarCounts == null
                    || frameFocuserPositions.Count != frameStarCounts.Count || frameFocuserPositions.Count == 0) {
                return -1;
            }

            // Pool to DISTINCT positions the way the fit does: a position is starless only when every frame at it
            // is starless (one populated frame is enough to give the position a finite pooled HFR).
            var starsAt = new SortedDictionary<int, int>();
            for (var i = 0; i < frameFocuserPositions.Count; i++) {
                var pos = frameFocuserPositions[i];
                starsAt[pos] = starsAt.TryGetValue(pos, out var running) ? running + frameStarCounts[i] : frameStarCounts[i];
            }

            var counts = starsAt.Values.ToList(); // ascending by position
            var d = counts.Count;
            var leading = 0;
            while (leading < d && counts[leading] <= 0) {
                leading++;
            }
            if (leading == d) {
                return -1; // every position starless: nothing to fit at any exemption
            }
            var trailing = 0;
            while (trailing < d && counts[d - 1 - trailing] <= 0) {
                trailing++;
            }

            // Any starless position left strictly inside the surviving span is an interior gap, not a wing.
            for (var i = leading; i < d - trailing; i++) {
                if (counts[i] <= 0) {
                    return -1;
                }
            }

            var needed = Math.Max(leading, trailing);
            if (needed == 0) {
                return 0;
            }
            // Same cap ComputeRecoveryPositions enforces, so a value returned here is one it will honour in full.
            var maxPerSide = Math.Max(0, (d - MinPositionsForFit) / 2);
            return needed <= maxPerSide ? needed : -1;
        }

        private static HashSet<int> ComputeRecoveryPositions(SortedDictionary<int, List<MeasureAndError>> byPosition, int recoverySteps) {
            var d = byPosition.Count;
            if (recoverySteps <= 0 || d <= 0) {
                return null;
            }
            var perSide = Math.Min(recoverySteps, Math.Max(0, (d - MinPositionsForFit) / 2));
            if (perSide <= 0) {
                return null;
            }
            var sortedKeys = byPosition.Keys.ToList(); // ascending distinct sweep positions
            var recoveryPositions = new HashSet<int>();
            for (var k = 0; k < perSide; k++) {
                recoveryPositions.Add(sortedKeys[k]);            // one of the perSide smallest positions
                recoveryPositions.Add(sortedKeys[d - 1 - k]);    // one of the perSide largest positions
            }
            return recoveryPositions;
        }

        /// <summary>
        /// σ for the fit's display/weight layer: keep a finite, non-negative value; non-finite σ (NaN/±Inf) and
        /// negatives map to 0 = "no error bar". Mirrors <c>AutoFocusEngine.SafeDisplayError</c> so the fit inputs
        /// here are identical to the ones the AF engine produces.
        /// </summary>
        private static double SafeDisplayError(double stdev) {
            return double.IsFinite(stdev) ? Math.Max(0.0, stdev) : 0.0;
        }

        /// <summary>Optional prepared-source cache (see <see cref="PreparedSourceCache"/>) whose lifetime is
        /// tied to this run's frames; disposed with the run. Set by the loaders that stamp the cache onto the
        /// seed/baseline params; null when the optimization does not use one.</summary>
        public PreparedSourceCache OwnedSourceCache { get; set; }

        /// <summary>
        /// Releases every cached early-detection context (frees the pinned source Mats) and the owned
        /// prepared-source cache held for this instance's lifetime. Idempotent. Only the split path holds
        /// contexts; the monolithic path keeps none.
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
                OwnedSourceCache?.Dispose();
                OwnedSourceCache = null;
            }
        }
    }
}
