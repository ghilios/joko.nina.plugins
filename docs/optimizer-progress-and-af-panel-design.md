# Optimizer progress honesty + AF panel real-estate — design

Three independent changes, grouped because they were reported together:

1. The Star Detection Optimizer wizard silently stalls on expensive search steps, and its "at most N more"
   time bound is computed from a blended rate that makes it a gross underestimate rather than a bound.
2. The saved aberration-inspection run folder still accumulates annotated `Registered_*.tiff` /
   `StarAlignment_*.tiff` overlays that the Review UI now re-renders live.
3. The AutoFocus panel spends two rows on numbers with low decision value (hyperbolic reduced χ² and
   best-focus LOO stability) and shows nothing about how many stars the curve was actually built from.

---

## 1. Expensive search steps: say they are happening, and stop promising a time

### The mechanism

`StarDetectionOptimizer` splits the curated axes into two classes (`StarDetector.IsEarlyCacheKeyParameter`):

- **LATE** axes (Sensitivity, StarClippingMultiplier, the gates …) are gate-only. Moving one re-scores the
  cached per-frame `DetectionContext`, so an evaluation is a cache hit and costs milliseconds-to-a-second.
- **EARLY** axes (StructureLayers, NoiseClippingMultiplier, HotpixelFiltering, DetectionBinning, …) feed
  `BuildDetectionContext`. Moving one both **rebuilds and evicts** every frame's context: ~1.65 s per frame,
  × frames, × loaded runs. On an 11-frame two-run search that is ~36 s for a single step.

Phase B alternates a LATE compass stage with an EARLY compass stage. So the search spends long uninterrupted
stretches being cheap, then suddenly runs a block of steps that are one to two orders of magnitude slower.

### Why it reads as a hang

- The UI is fed by `MaybeReportProgress`, which reports only every `ProgressEvaluationInterval` (10) completed
  evaluations. Ten expensive steps in a row is several minutes with **no** counter movement.
- Nothing is reported *before* an evaluation starts, so the freeze begins with no explanation.
- `BuildSummaryAsync` — which runs **two full evaluations per loaded run** after the search ends, both of them
  early-context rebuilds — reports no phase and no progress at all. The panel keeps showing "Refining settings"
  with a frozen counter for the whole of it.

### Why the time bound lies

`SecondsPerEvaluation` is `evaluatorSeconds / Evaluations` — one blended mean over both classes. Early in a
run the mix is almost entirely cheap steps, so the mean is small; `ProgressTimingText` then multiplies it by
the remaining budget and renders it as **"at most X more"**. The moment the search enters an EARLY stage the
true remaining cost jumps ~10×, and the figure that was labelled an upper bound is exceeded.

### Decision

**Do not project a duration while expensive steps are still possible.** Show the observed rate and how many
steps remain, and say plainly that the remaining time is not predictable. A search whose variable set contains
no early axes at all (reachable via the narrowed feedback path) *is* uniform, and there the existing bound is
honest, so it is kept for that case only.

### Design

**Classification (optimizer).** An evaluation is EXPENSIVE iff its early cache key differs from the early key
of the previous evaluation that actually invoked the evaluator:

```
expensive = StarDetector.ComputeEarlyCacheKey(candidate) != lastEvaluatedEarlyKey
```

This is precisely the per-frame rebuild condition, so it is correct for every phase without duplicating the
staging logic — Phase A is never expensive, `RevertNeutralAxes` is expensive exactly when it pulls an early
axis back. Memo hits do not invoke the evaluator and therefore do not move `lastEvaluatedEarlyKey`.

The **seed** evaluation contributes to neither rate. Its cost depends on whether the caller pre-warmed the
contexts (the wizard does, via `AnalyzeWithProgressAsync`; TestApp does not), so it is not a sample of either
class. It does establish `lastEvaluatedEarlyKey`.

**New `OptimizationProgress` fields**

| Field | Meaning |
|---|---|
| `StepIsExpensive` | the step in flight (pre-report) or just completed rebuilds every frame's context |
| `ExpensiveStepsPossible` | the variable set contains at least one early axis — constant for a search |
| `SecondsPerCheapEvaluation` | mean seconds per cache-hit evaluation; NaN until one completes |
| `SecondsPerExpensiveEvaluation` | mean seconds per rebuild evaluation; NaN until one completes |

`SecondsPerEvaluation` (blended) stays for the log line.

**Pre-announcement.** `EvalJ` reports **before** invoking the evaluator whenever the step is expensive, so the
phase heading changes at the start of the wait rather than after it.

**Report cadence.** `MaybeReportProgress` is split: the UI report fires on **every** completed evaluation
(a `Progress<T>` post, negligible), the INFO log line stays at one per 10.

**Per-frame sub-progress.** `RunEvaluationData.CreateEvaluator` gains an optional
`IProgress<RunLoadProgress>` that it forwards to the existing
`EvaluateAndFitAsync(p, frameProgress, token)` overload, aggregated across runs
(`total = Σ run.FrameCount`, cumulative current). The wizard shows "analyzing frame 6 / 11" under the phase
heading **only while `StepIsExpensive`** — cheap steps are sub-second and would just churn.

**Post-search summary stage.** `BuildSummaryAsync` gets its own phase ("Measuring before / after") and
determinate frame progress over its `2 × runs.Count` evaluations, so the currently-silent tail of the run is
visible.

**Rendered text**

```
Refining settings — slow step (re-analyzing every frame)
  analyzing frame 6 / 11
312 / 500  (14:22)
fast steps 3.1 s · slow steps 38 s · 188 steps left — remaining time not predictable
```

With only one class observed so far the rate collapses to `3.1 s per step`. With
`ExpensiveStepsPossible == false` the line keeps its existing bounded form
(`{rate} · at most {X} more, usually much less`).

---

## 2. Remove the annotated inspection TIFFs

`InspectorVM.SaveRegisteredImages` writes two families into the run folder:

- `Registered_IndexNN_FocuserNNNNN[_ref].tiff` — unconditional
- `StarAlignment_IndexNN_FocuserNNNNN[_ref].tiff` — behind the `SaveAlignmentImages` option

Both are baked-in overlays of information the **Review Frames** UI now re-renders live from the raw frames and
the capture-time settings. This is the same reasoning already applied to the per-region annotated TIFFs in
`AutoFocusEngine` ("the 'Review Frames' feature re-renders the annotator overlays live … a baked-in annotated
image is redundant — and rendering/encoding it per frame was a needless cost").

**Removed:** `SaveRegisteredImages`, `SaveRegisteredImage`, `SaveAlignmentImage`, the two call sites, the
`suppressRegisteredImages` parameter on `AnalyzeAutoFocusResult`, the `SaveImagesOnReruns` and
`SaveAlignmentImages` options (`InspectorOptions`, `IInspectorOptions`, the two Options rows in
`AutoFocus/DataTemplates.xaml`), their tests, and the two manual rows in
`documentation/docs/overview/tilt-aberration-inspector.md`.

Persisted values for the two removed keys are simply never read again; no migration is needed.

---

## 3. AutoFocus panel: two rows out, one row in

**Out** (view only — both stay in `HocusFocusReport`, which is what tooling reads):

- `Hyperbolic Reduced χ²` — its own tooltip already concedes it is "a true reduced χ² only when the weighted
  hyperbolic fit is enabled" and "routinely runs above 1 even for good fits", i.e. it is not directly
  actionable at a glance.
- `Best-focus stability (LOO)` — duplicated in spirit by the `Hyperbolic σ(focus)` row directly above it.

The two now-unused tooltip resources go with them.

**In: `Stars detected (curve)`** — the min–max star count over the points the curve was actually fitted on.

- **Source.** `AutoFocusSubMeasurementPointCompletedEventArgs` already carries the per-frame
  `StarDetectionResult`, whose `DetectedStars` is the accepted-star count. `HocusFocusVM` accumulates
  `focuserPosition → counts[]` for region 0. No engine or event change is needed.
  Only `FocusPointMeasurementAction` raises this event, so initial-HFR and final-validation frames are
  naturally excluded.
- **Per point.** Mean of that position's frames, rounded — matching how `TryCompleteFocuserPoint` pools the
  HFR measurements when `FramesPerPoint > 1`.
- **Accepted points.** Measured positions minus `RejectedPoints` (Grubbs consensus outliers) minus
  `WindowExcludedPoints` (symmetric-window exclusions). Both sets are re-synced to their final values in
  `AutoFocusEngine_CompletedNoReport`, so the row is recomputed there as well as live.
- **Persistence.** `AcceptedStarCountMin` / `AcceptedStarCountMax` are added to `HocusFocusReport` and restored
  through the existing `ApplyInfoRowsFromLoadedReport` path, which is how the other plugin-only rows survive a
  chart reload. Absent (older reports, core reports) collapses the row rather than showing 0.
- **Display.** `412 – 1,067`; a single accepted point renders as one number.

---

## Testing

- `OptimizerProgressCostTests` — expensive/cheap classification off the early cache key, seed excluded from
  both rates, split rates populated, a report emitted before an expensive evaluation and after every
  evaluation.
- `StarDetectionOptimizerWizardVMTests` — `ProgressTimingText` in each of its three shapes (one class known,
  both known, no early axes), and the frame sub-progress gating.
- `InspectorOptionsTests` / `FakeSensorModelOptions` — the two removed options.
- `HocusFocusVMTests` / `HocusFocusReportTests` — star-count range over accepted points only, pooling across
  `FramesPerPoint`, report round-trip, and the collapsed row when nothing is known.
