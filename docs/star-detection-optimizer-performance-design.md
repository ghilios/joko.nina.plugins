# Star Detection Optimizer — Performance Investigation & Speedup Design

Status: **Implemented.** The §3 early/late split + per-frame early-context cache (#4) AND the §4
bounded parallel per-frame detection (#5) both shipped and were measured: **~10–13× faster,
detection bit-identical** (CWhite 61 MP 19m51s → 1m30s = 13.2×; Panos 7m02s → 44s = 9.6× at
`--max-evals 24`). Both apply to the in-NINA wizard for **live AND replay** sources (both converge
on `RunEvaluationLoader` → `RunEvaluationData` → `HocusFocusSplitFrameDetector`; the offline harness
uses an equivalent `MatSplitFrameDetector`). The §7 logging quick-win shipped too but is
**result-neutral with no measurable speedup** (NINA `Logger` is buffered; detection compute
dominates). The wizard per-eval convert-reuse (#1) remains DEFERRED (see §7). The analysis below is
retained as the rationale; the "ranked recommendations" reflect what shipped vs. what is left.

Scope: the Star Detection Optimization wizard and its offline harness (`TestApp optimize`).
Goal: explain where wall-clock time goes and lay out the highest-leverage speedups, separating
safe "pure performance" wins (must stay bit-identical) from larger refactors.

## 1. Time budget — where the wall-clock goes

A single optimization run does:

```
wall ≈ (distinct evaluator calls) × (frames per run) × (per-frame Detect)   [+ small fixed overhead]
```

- **Distinct evaluator calls** = the optimizer's cache MISSES (`StarDetectionOptimizer.SearchContext.EvalJ`
  memoizes J on `StarDetector.ComputeCacheKey`, so revisited points are free). Bounded by
  `OptimizerSettings.MaxEvaluations`. Harness runs used `--max-evals 24`; the **wizard default is 400**
  (`OptimizerSettings.MaxEvaluations = 400`, StarDetectionOptimizer.cs:25).
- **Frames per run** = number of saved AF exposures (observed bank: 7–10 frames/run).
- **Per-frame Detect** = one `StarDetector.DetectImpl` pass over a full-resolution float Mat.

There is also a fixed tail per optimization: the seed + best are re-evaluated against every run for the
summary (`OptimizeRunSetAsync` perRunSeed/perRunBest; wizard `BuildSummaryAsync` lines 495–496) = +2×frames
detections, plus 1–2 annotated detections. So total detections ≈ `(MaxEvals + 2) × frames + ~2`.

### Per-detection stage split (measured, real bank `run.log`)

These are real TRACE lines (`StarDetector.cs|DetectImpl`), a large sensor (~5 s/frame; the file's frames are
bigger than the 4656×3520 example in the brief):

| Stage | Typical | Share | Depends on (params) |
|---|---|---|---|
| LoadImage | ~0.0 s | ~0% | (ROI submat only) |
| SrcImagePreparation | 0.31–0.52 s | ~6% | HotpixelFiltering, HotpixelThresholdingEnabled, HotpixelThreshold, NoiseReductionRadius, StarMeasurementNoiseReductionEnabled |
| StructureMapPreparation | 0.35–0.51 s | ~7% | NoiseReductionRadius, (hotpixel\*) — copies + 2nd Gaussian + launches K-σ noise tasks |
| **WaveletCalculation** | **2.1–2.5 s** | **~44%** | **StructureLayers** (à-trous B3-spline, `numLayers` separable filter passes; kernel widens per layer) |
| PostWaveletConvolution | 0.15–0.17 s | ~3% | StructureLayers (Gaussian of radius `StructureLayers·2+1`) |
| **BinarizationStatistics** | **0.9–1.2 s** | **~21%** | (histogram median of structure map; param-independent) + awaits the K-σ noise tasks (NoiseClippingMultiplier) |
| Binarization | ~0.02 s | <1% | NoiseClippingMultiplier (threshold), Region.InnerCrop |
| **StarAnalysis** | **0.9–1.3 s** | **~22%** | gate params: Sensitivity, StarClippingMultiplier, PeakResponse, MaxDistortion, MinHFR, StarCenterTolerance, MinimumStarBoundingBoxSize, BackgroundBoxExpansion, AnalysisSamplingSize |
| Elapsed | 4.8–6.0 s | 100% | |

**61 MP vs mid-sensor:** every stage is ≈ linear in pixel count (full-frame OpenCV filters + raster
scans + per-pixel noise estimate). A 24 MP frame ≈ 1.4 s (brief's figure); a 61 MP frame ≈ 5–6 s. So the
24-eval, 7-frame Panos run = (24+2)×7 + 2 ≈ 184 detections × ~5 s ≈ **~15 min** (matches the observed
~20 min at 61 MP with overhead). At the wizard default 400 evals it would be **~16×** that.

Cross-check: the bank `--per-run` batch logged **1659** detections; summing `(24+2)×frames+2` over the 7
completed runs predicts **1626** — the small delta is annotated frames / a partial run. The model holds.

## 2. File I/O — definitive

**Files are loaded ONCE per run and held in memory; they are NOT re-read per evaluation.** Both paths:

- **Harness** (`OptimizationDiagnosticRunner.PrepareRunAsync`, lines 330–339): each frame is loaded once via
  `DiagnosticUtil.LoadFloatMat` into `RunFrame.Image` (a `Mat`) and cached; `DisposeRuns` frees them at the
  end. The detect delegate (lines 354–360) does **not** touch disk.
- **Wizard** (`RunEvaluationLoader.LoadSavedRunAsync`, lines 103–116): each saved exposure is rendered once
  (`PrepareImage`, detectStars:false) into `RunFrame.Image` (an `IRenderedImage`). The detect delegate
  (lines 138–150) re-detects the already-rendered frame.

`RunEvaluationData` originally kept **no per-frame detection cache** (the original class doc reasoned: "the
optimizer already memoizes J at the params level… caching per frame here would be redundant and would pin
many star lists in memory"). That reasoning was sound for *star-list* caching but conflated star-list reuse
with **early-stage image reuse** — see §3, the real opportunity. **As shipped**, `RunEvaluationData` now
caches the **early `DetectionContext`** (not the star list) per (frame, early-key), which is exactly the
reuse the original note missed; star lists are still not cached.

### Redundant per-detection allocations / copies

1. **Mat clone per detection (both paths).**
   - Harness: `using var clone = ((Mat)image).Clone();` before every `Detect` (line 357) because
     `DetectImpl` mutates its input in place (ROI submat, hotpixel filter, Gaussian). At 61 MP a CV_32F clone
     is ~244 MB memcpy — small vs the 5 s detect, but non-trivial and repeated `MaxEvals×frames` times.
   - This clone is **unavoidable as long as `DetectImpl` mutates `srcImage`**; it is the price of the
     in-place pipeline.
2. **Wizard path re-runs the IRenderedImage→Mat conversion every eval.** `starDetector.Detect(image, …)`
   (the `IRenderedImage` overload, StarDetector.cs:151–189) re-does, per candidate: a full `ushort[]` copy,
   CFA hotpixel filter, **debayer**, and `ToOpenCVMat` — *before* `DetectImpl` even starts. The harness avoids
   this by pre-converting to a float Mat once. This is pure wasted work in the wizard (the conversion output is
   identical across candidates **unless** the candidate changes `HotpixelThreshold`/`HotpixelThresholdingEnabled`
   — and only when `HotpixelFiltering && HotpixelThresholdingEnabled` and the image is debayered).
3. **`Console.WriteLine($"PSF time: …")` runs on every detection even when `ModelPSF=false`**
   (StarDetector.cs:378). Harmless per call, but 1659× to a console is measurable; trivially gated.
4. **Verbose TRACE logging.** The optimizer forces `LogLevel = TRACE` (harness) and `DetectImpl` emits
   several `Logger.Trace` lines + K-σ traces per detection. Per detection the logging is small relative to
   ~5 s, but across `MaxEvals×frames` detections it is real I/O; it is *not* the bottleneck (the stage timers
   bound it), but production/wizard runs should not be at TRACE.

## 3. The reuse opportunity (the big one): split Detect into "build candidates" + "gate/measure"

### Param → stage dependency map (for the 12 curated optimizer variables)

| Variable | Type | Affects EARLY (image→structure map→candidate regions) | Affects LATE (StarAnalysis gating/measure) |
|---|---|---|---|
| Sensitivity | Continuous | no | **yes** (EvaluateStarCandidate sensitivity gate) |
| StarClippingMultiplier | Continuous | no | **yes** (clip margin in ComputeStarParameters + MeasureStar) |
| PeakResponse | Continuous | no | **yes** (TooFlat gate + NormalizedBrightness) |
| MaxDistortion | Continuous | no | **yes** (distortion gate) |
| MinHFR | Continuous | no | **yes** (HFR floor) |
| StarCenterTolerance | Continuous | no | **yes** (IsStarCentered) |
| MinimumStarBoundingBoxSize | Integer | no | **yes** (TooSmall gate) |
| NoiseClippingMultiplier | Continuous | **yes** (binarize threshold → which structure pixels survive → candidate regions; also K-σ) | no (only via the σ baked into candidates) |
| StructureLayers | Integer | **yes** (wavelet layers + post-wavelet blur radius) | no |
| NoiseReductionRadius | Integer | **yes** (Gaussian on structure-map source; SrcImagePreparation) | no |
| HotpixelThresholdingEnabled | Boolean | **yes** (hotpixel filter mode) | no |
| HotpixelThreshold | Continuous | **yes** (hotpixel filter threshold) | no |

So of the 12 variables, **7 are LATE-only** (Sensitivity, StarClippingMultiplier, PeakResponse,
MaxDistortion, MinHFR, StarCenterTolerance, MinimumStarBoundingBoxSize) and **5 are EARLY** (NoiseClipping,
StructureLayers, NoiseReductionRadius, Hotpixel×2). The expensive stages — Wavelet (44%) +
BinarizationStatistics (21%) + SrcImagePrep/StructureMapPrep (13%) ≈ **78% of per-detection time** — depend
ONLY on the 5 EARLY variables. StarAnalysis (~22%) depends on the 7 LATE variables.

### Why this matters for the search

Phase B (compass/pattern search) perturbs **one variable at a time** (`ProposeMove`). Every move that
perturbs a LATE-only variable reuses the *identical* hotpixel-filtered source image, structure map,
candidate regions, and noise σ — only the per-candidate gating/measurement changes. With 7 LATE vs 5 EARLY
variables and the search spending most of its budget in Phase B compass moves, **a large majority of
evaluations change only late-stage gates.** Caching the early result keyed on the EARLY-subset would let
those evals skip ~78% of the work.

Two important nuances that make the win even larger or require care:
- **`StarClippingMultiplier` is LATE but reaches into `ComputeStarParameters`/`MeasureStar`** (the clip
  margin), which today live inside the parallel candidate-evaluation stage — so it is correctly LATE: the
  candidate *regions* (flood-fill on the binarized structure map) do not depend on it.
- **`NoiseClippingMultiplier` is the one subtle EARLY var**: it sets the binarize threshold AND the K-σ
  clipping. It changes which pixels enter the structure map → the candidate region set. So a cache keyed only
  on {StructureLayers, NoiseReductionRadius, Hotpixel×2} would be wrong if NoiseClipping changed; it must be
  part of the early-cache key. Note Phase-A's coarse grid sweeps Sensitivity (LATE) × StarClippingMultiplier
  (LATE) — **all 16 Phase-A points share one early result**, a clean 16→1 early-stage win.

### Proposed split — **SHIPPED**

Refactored `DetectImpl` into two phases with an intermediate, reusable artifact (the names below are
the ones that shipped):

- `StarDetector.BuildDetectionContext(srcImage, earlyParams) → DetectionContext` = {hotpixel-filtered
  source Mat, candidate regions, measurementNoiseSigma, structureNoiseSigma, ROI offset, early-only
  metric counters}. Contains everything produced up to and including candidate collection. Pure
  function of the EARLY params + image.
- `StarDetector.GateAndMeasure(context, lateParams) → result` = the per-candidate `EvaluateStarCandidate`
  pass over the cached regions (the cheap 22%).

`RunEvaluationData` caches the `DetectionContext` per (frame, early-key) and reuses it across
candidates that change only late-stage gates; on a late-only move the early 78% is skipped. The early
key is computed by `ComputeEarlyKey` over the EARLY-only param subset. The boundary is the
`RunEvaluationData.ISplitFrameDetector` interface (`ComputeEarlyKey` / `BuildContextAsync` /
`GateAndMeasure`), implemented by `HocusFocusSplitFrameDetector` (wizard) and `MatSplitFrameDetector`
(harness). Cache is bounded to one context per frame; a frame's prior context is disposed when its
early key changes.

**Architecture allows it.** Today the boundary is already clean: `CollectStarCandidates` (sequential
flood-fill, produces independent per-candidate point lists) is followed by a parallel `EvaluateStarCandidate`
pass; the late stage only *reads* the source image + regions. The clone-on-mutate concern (§2.1) means the
cached context's source Mat must be treated as read-only by the late stage — it already is
(`EvaluateStarCandidate`/`ComputeStarParameters`/`MeasureStar` only read `srcImage`).

**Risk / determinism:** the refactor must keep results **bit-identical**. The split is value-preserving only
if the early/late param partition is exactly correct — the table above must be encoded as the cache key, and
any param not provably late-only must stay in the early key (same "safe failure mode = spurious miss"
discipline as `ComputeCacheKey`). Memory: one cached source Mat + region lists per frame per distinct early-key
(at 61 MP, ~244 MB per cached source) — must bound the cache (e.g. keep only the current early-key's contexts,
i.e. one set of frame contexts at a time, since the compass search moves the incumbent).

**Estimated speedup:** if ~⅔ of evals are late-only and the early stage is 78% of a detection, late-only evals
cost ~0.22× → overall detect time ≈ ⅓·1.0 + ⅔·0.22 ≈ **0.48×**, i.e. ~2× faster, before any parallelism.
Combined with Phase-A's 16→1 early reuse the practical win is larger.

## 4. Parallelism opportunities

Current concurrency:
- **Within a detection:** `ScanStars` evaluates candidates with `Parallel.For` governed by
  `MaxStarEvaluationParallelism` (default 0 ⇒ `Environment.ProcessorCount`), StarDetector.cs:774. The two
  K-σ noise estimates run on `Task.Run` concurrently with the structure-map prep. PSF fit is parallel but
  **off** during AF/optimize (`ModelPSF=false`). So the *late* stage already uses all cores; the *early*
  stage (wavelet/binarization-stats) is largely single-threaded OpenCV calls.
- **Across frames within an eval:** SEQUENTIAL — `RunEvaluationData.EvaluateAndFitAsync` lines 160–165 detect
  frames in a `foreach` "deterministic order". 7–10 frames done one at a time.
- **Across candidates:** SEQUENTIAL — `EvalJ` is awaited one candidate at a time in both CoarseGrid and
  PatternSearch.

Options:
- **(a) Parallel frame detection within an eval — SHIPPED.** Frames are independent; they detect
  concurrently in `RunEvaluationData` with a bounded degree (≈ `max(2, ProcessorCount/4)`) chosen so
  the inner per-star `Parallel.For` is not oversubscribed. Results are assembled by frame index and
  pooled by focuser position (`SortedDictionary`), so the fit is order-independent and deterministic.
  Combined with §3 the early (single-threaded) stages parallelize across frames.
- **(b) Parallel candidate evaluation** — Phase-A's 16 grid points are independent; within a Phase-B sweep
  the ±step candidates are independent (the sweep evaluates all, then picks the best). These could be a
  bounded-parallel batch. Caveats: (i) memory at 61 MP (each concurrent detection clones a ~244 MB Mat +
  buffers); (ii) the memo is a plain `Dictionary` (not thread-safe) — concurrent `EvalJ` needs a concurrent
  memo or a gather-then-insert; (iii) determinism — results must be identical regardless of completion order
  (the "take best strictly-improving, ties⇒no move" rule is order-independent on values, so as long as J is
  deterministic per-candidate, parallel evaluation is safe).
- **Determinism guard:** detection itself must remain deterministic under parallelism — it already sorts
  bounds by (Y,X) and assembles stars in index order (`ScanStars`), so per-candidate J is reproducible.

## 5. Other wins

- **Lower the wizard default budget.** 400 is very high for a 12-var compass search seeded by a 16-point
  grid; the harness improved J meaningfully at 24. Estimate the natural stopping point: Phase B halves
  continuous steps from InitialStep down to InitialStep×0.125 = **3 halvings**; with 7 continuous + integer/
  boolean axes a typical search converges in well under 100 evals. A default of ~80–120 (or an
  early-stop on "no improvement across a full sweep after step-floor") would cut wall-clock several-fold with
  little quality loss. (Pure tuning; no result-correctness risk — only changes how long the search runs.)
- **Early-stop the compass search** when a full sweep yields no strictly-improving move AND continuous steps
  are at floor — the loop already terminates there; the lever is the budget cap and the floor fraction.
- **Downsample / region for the search, full pass at the end.** Run the search on a center ROI or a binned
  image (fast, ~linear speedup), then do ONE full-frame confirm at the winning params. Risk: changes results
  during the search (acceptable — it is a search heuristic) but the final applied params are validated full-
  frame. Region plumbing already exists (`StarDetectionRegion`, `p.Region`).
- **Skip the per-detection IRenderedImage→Mat re-conversion in the wizard** (see §2.2): pre-convert each
  frame to a float Mat once (as the harness does) and feed the Mat `Detect` overload, re-doing the CFA
  hotpixel/debayer step only when a candidate changes the hotpixel params. Pure perf; bit-identical when the
  hotpixel params are unchanged.
- **Gate the `Console.WriteLine("PSF time")`** behind `ModelPSF`/a debug flag (§2.3). Trivial.
- **Don't force TRACE during optimize** (§2.4) — or at least not the per-detection traces — in the wizard.

## 6. Ranked recommendations (status)

Safe quick wins (pure performance; keep results bit-identical):
1. **Wizard: pre-convert frames to float Mats once** and use the Mat `Detect` overload (skip per-eval
   debayer/ToOpenCVMat). **DEFERRED** — folded into #4 (the convert path keys on the hotpixel params,
   which the optimizer tunes; only the early/late refactor makes the mono-vs-bayered split safe). See §7.
2. **Lower the default `MaxEvaluations`** (400 → ~80–120) and/or add a no-improvement early stop. Not
   shipped on this branch (search-quality tuning, orthogonal to the per-eval cost win). (StarDetectionOptimizer.cs:25.)
3. **Gate the PSF-time `Console.WriteLine`** and avoid forcing TRACE per-detection in the harness.
   **SHIPPED** — result-neutral, but **no measurable speedup** (Logger is buffered). See §7.

Larger refactors (bigger impact, must preserve bit-identical results):
4. **Split `DetectImpl` into BuildDetectionContext (early) + GateAndMeasure (late), cache the early
   context per (frame, early-key).** **SHIPPED** — `RunEvaluationData` caches the context per
   (frame, early-key) behind the `ISplitFrameDetector` boundary; detection is bit-identical. This is
   the change the T3 "no per-frame cache" note did not consider. (Main driver of the measured ~10–13×.)
5. **Parallelize frame detection within an eval** (bounded). **SHIPPED** — `RunEvaluationData` detects
   frames concurrently at degree ≈ `max(2, ProcessorCount/4)`, assembling by index and pooling by
   position; determinism safe.
6. **Parallelize independent candidate evaluations** (Phase-A grid, Phase-B sweep). Not shipped
   (would need a concurrent memo + memory cap). Optional future win.
7. **Search on a downsampled/ROI image, confirm full-frame at the winner.** Not shipped (search
   heuristic). Optional future win.

Anything touching detection math (the §3 split, parallelism) must be proven result-identical (the existing
`ComputeCacheKey`/equivalence-signature discipline applies). The shipped #4 changes structure but produces
the same stars/values (verified bit-identical, incl. the cross-setup copy-run check); #5's framing is pure
scheduling and does not change detection output.

## 7. Implementation note — what shipped

Implemented (the big wins; detection **bit-identical**, measured **~10–13×**):
- **#4 early/late split + per-frame early-context cache.** `StarDetector.DetectImpl` split into
  `BuildDetectionContext` (early, ~78%) + `GateAndMeasure` (late, ~22%); `RunEvaluationData` caches the
  early `DetectionContext` per (frame, early-key) and reuses it across late-only candidate moves, behind
  the `ISplitFrameDetector` boundary (`HocusFocusSplitFrameDetector` wizard / `MatSplitFrameDetector`
  harness). Applies to live AND replay (both converge on `RunEvaluationLoader` → `RunEvaluationData` →
  the split detector). Cache bounded to one context per frame.
- **#5 bounded parallel per-frame detection within an eval.** `RunEvaluationData` detects frames
  concurrently at degree ≈ `max(2, ProcessorCount/4)`, assembled by index, pooled by focuser position;
  fit is order-independent (deterministic).
- **Measured:** CWhite 61 MP **19m51s → 1m30s = 13.2×**; Panos **7m02s → 44s = 9.6×** (`--max-evals 24`).
  Verified bit-identical, including the cross-setup copy-run (identical-results) check.

Implemented (pure logging/scheduling, bit-identical — **but no measurable speedup**: NINA `Logger` is
buffered and detection compute dominates, so this is a correctness/cleanup change, not a perf win):
- **#3 PSF-time write + per-detection TRACE.** `StarDetector.cs` no longer does a `Console.WriteLine("PSF
  time…")` on every detection: the whole PSF block (incl. the timer) is now inside `if (p.ModelPSF)` and the
  timing line is `Logger.Trace`. During AF/optimize (`ModelPSF=false`, always) that block is skipped entirely.
  The `optimize` harness (`OptimizationDiagnosticRunner.cs`) now defaults to `LogLevelEnum.INFO` instead of
  forcing `TRACE`, with an opt-in `--verbose` flag that restores `TRACE`; this makes the detector's per-
  detection `Logger.Trace` stage-timing lines short-circuit (no disk serialization) on the common path. The
  `contamination` runner is unchanged. The wizard does not call `SetLogLevel`, so it already respects the
  user's configured level — nothing to fix there.

### Related: defocus-aware detector gates (opt-in, default OFF)

Separate from performance, the same branch added two opt-in, default-off, size-scaled gate relaxations
(`DefocusAwareDistortion`, `DefocusAwareCentering`) that recover bloated/donut defocused stars rejected by
the `MaxDistortion` / centering gates. Default-OFF returns the gates verbatim, so they keep detection
**bit-identical** and do not affect any of the perf measurements above. See
`docs/star-detection-optimization-wizard-design.md` (and the results doc) for the diagnosis and recovery
rates.

DEFERRED — #1 wizard per-eval IRenderedImage→Mat convert-reuse (needs the §3 refactor, not a quick win):
The §2.2/§5/§6.1 framing ("bit-identical *when hotpixel params unchanged*") is the catch. The curated
optimizer variable set (`OptimizerVariable.CreateCuratedSet`) **tunes `HotpixelThresholdingEnabled` and
`HotpixelThreshold`** (and `NoiseReductionRadius`). For **bayered (`IDebayeredImage`) frames**, those two are
exactly the params that drive the per-eval conversion branch in `StarDetector.Detect(IRenderedImage)` (the
CFA-hotpixel-filter + debayer + `ToOpenCVMat` path, lines ~158–171): changing them changes the converted
pixels, so a convert-once cache would change results across candidates. A cache keyed on exactly
`(HotpixelFiltering, HotpixelThresholdingEnabled, HotpixelThreshold)` would be *correct* but would essentially
never hit (the optimizer perturbs those keys), yielding no real speedup while adding cache-correctness risk.
The genuinely safe win — convert-once for **mono** AF frames, where the `IDebayeredImage` branch is dead and
the conversion is always the param-independent `ToOpenCVMat(image)` — requires the loader to reliably split
the mono vs. bayered path. That is structural and belongs with the early/late-stage refactor (§3/#4), which
already has to make the cached source Mat read-only and key it on the EARLY params (which include the hotpixel
params). Per the "bit-identical safety beats the speedup" rule, this is left to that refactor. The harness
(`OptimizationDiagnosticRunner.PrepareRunAsync`) already pre-converts each frame to a float Mat once and clones
per eval, so it pays no per-eval conversion cost regardless.
