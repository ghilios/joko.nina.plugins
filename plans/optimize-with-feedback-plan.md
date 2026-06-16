# Optimize With Feedback — Direct Label→Settings Recommender (+ Warm-Started Optimizer)

> **Repo convention note (CLAUDE.md):** implementation plans live in the repo `plans/` folder. On execution,
> copy this file to `plans/optimize-with-feedback-plan.md` and work from there. (This copy lives in the
> Claude plan-mode dir because that was the only writable path during planning.)
> **`/clear` before executing.** Branch `ghilios/optimize-with-feedback` off `develop`; PR to merge (never push
> `develop`). Commit identity `George Hilios <322725+ghilios@users.noreply.github.com>` (author + committer).

## Context

The Star Detection Optimization Wizard already has a **Review → label → "Optimize with feedback"** loop: the
user labels `missed` (false negatives), `shouldReject` (false positives), and `wronglyRejected` (detected but
gated-out) stars, then re-runs the optimizer. **But the feedback path today just throws those labels into the
black-box `StarDetectionOptimizer` as one weighted objective term (`Wl=0.25`, recall/precision by box
containment).** It never *directly analyzes* which gate is failing which labeled star, so it can't tell the user
"BrightnessSensitivity is rejecting these 21 stars; lower it to 1.1 to recover them," and it has no transparent
notion of "improve recall without hurting precision much."

The user wants the feedback step to **directly analyze the labels** to recommend concrete settings that recover
as many labeled stars as possible, improving recall while bounding the precision cost — and to know whether the
current gates even have the flexibility to do it.

**Empirical anchor (must be solvable):** run `E:\WorkshopData\Data\autofocus\sensitivity_example1\attempt01`
(9 `.xisf`, focuser 3809–5009), labels at `…\attempt01\labels\E__WorkshopData_…_attempt01.json` — **168 boxes
across 7 positions, ALL `wronglyRejected`** (no `missed`, no `shouldReject`). A `wronglyRejected` box means a
candidate *formed* there and a **gate killed it** ⇒ every one is recoverable by gate tuning (not a structure
gap). Folder name + faint/small boxes ⇒ the **LowSensitivity** gate (`BrightnessSensitivity` 2.0) is the prime
suspect, with a secondary **TooLowHFR** (`MinHFR` 1.2) bucket.

## Locked decisions (from the user)

1. **Analyzer primary + warm-start optimizer.** The direct gate analyzer computes recommended settings from the
   labels and shows a per-gate breakdown; that recommendation then **seeds and bounds** the existing
   `StarDetectionOptimizer` so AF focus-curve quality isn't wrecked while recovering stars.
2. **Include NO-CANDIDATE structure-gap recovery.** Add a NEW opt-in, default-OFF *defocus-aware
   structure-detection* setting so stars that never form a candidate (large/donut defocus) can be recovered.
   Bit-identical when OFF.
3. **Precision guard = unlabeled-admit proxy + should-reject when present.** Precision cost of a setting change =
   count of currently-rejected **unlabeled** candidates it would newly admit (near-focus-weighted); AND
   hard-honor `shouldReject` labels when they exist.
4. **Optimize-vs-validate toggle.** The wizard offers a choice: **Optimize** (run the optimization pass, current
   behavior) OR **Use current settings** — skip optimization entirely and jump straight to Review against the
   current/seed params, so the user can validate (and label against) today's settings without an optimization
   pass. Labeling + "Optimize with feedback" stay available from that Review either way.

## Architecture facts that constrain the design (verified)

- **Gates discard the measured value.** `StarDetector.EvaluateStarCandidate` (StarDetector.cs:1242–1379)
  computes each gate's scalar as a local and discards it on `return null`. We must capture it. Per-gate scalars:
  TooSmall `min(W,H)` vs `MinimumStarBoundingBoxSize`; TooDistorted `fillRatio=points/d²` vs
  `effectiveMaxDistortion`; LowSensitivity `NormalizedBrightness/srcImageNoiseSigma` vs `Sensitivity`; TooFlat
  `StarMedian/Peak` vs `PeakResponse`; TooLowHFR `star.HFR` vs `MinHFR`; NotCentered → normalized centroid
  offset vs effective tolerance; OnBorder/Degenerate/HFRAnalysisFailed → **non-scalar** (flag only);
  Contaminated → `ContaminationSensitivity`.
- **Counter-only gates have no bbox list.** `StarDetectorMetrics` carries `*Bounds` lists only for TooDistorted,
  Degenerate, Saturated, LowSensitivity, NotCentered, TooFlat, Contaminated. TooSmall/TooLowHFR/HFRAnalysisFailed
  are counter-only, so `FrameReviewBuilder.ExtractRejected` never emits them and `diagnose-labels` mis-reports a
  TooLowHFR star as **NO CANDIDATE**. The new diagnostics fix this attribution bug.
- **Diagnostics side-channel pattern already exists.** `CollectContaminationDiagnostics` (default false) is in
  `CacheKeyExcludedProperties` (IStarDetector.cs:418/426) and threads a `ConcurrentBag<…>` (StarDetector.cs:623,
  null when off) → **zero overhead / bit-identical when off**. Mirror this exactly.
- **Cache-key machinery:** `EarlyCacheKeyProperties` + `IsEarlyCacheKeyParameter` (StarDetector.cs:109/144) gate
  the optimizer's early-context cache; any new param that changes candidate *formation* must be added there.
  `ToCanonicalCacheString` reflects over public props minus `CacheKeyExcludedProperties`.
- **Optimizer needs no core change.** `StarDetectionOptimizer.OptimizeAsync(seed, variables, evaluator, …)`
  already takes the seed + variable set; only the wizard caller (StarDetectionOptimizerWizardVM.cs:830, which
  hardcodes `runs[0].Seed` + `CreateCuratedSet()`) must inject a warm-start seed + bounded variables.
- **Shared seams:** `FrameReviewBuilder.ExtractRejected` is the single rejected-box source (TestApp delegates to
  it). `ClassifyBox`/`IoU`/`RectD` currently live ONLY in TestApp `DiagnoseLabelsRunner.cs` — factor them into
  shared plugin code so wizard + TestApp + analyzer agree.
- **Defocus *gates* already exist** (DefocusAwareDistortion/Centering etc.) and are LATE. The new
  structure-detection setting (decision 2) is genuinely new and **EARLY**.

---

## Implementation

Ship in slices; each new behavior is **opt-in / default-OFF ⇒ detection star counts AND objective `J`
bit-identical when disabled** (unit-test against an independent re-impl). Run
`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug` after each.

### Phase 0 — Validation pass on the user's example (do FIRST, before building anything new)

The user wants the concrete decided settings for `sensitivity_example1\attempt01` to review directly. Produce
them up front with the **existing** tools (they already match this run's absolute-path runId, so labels load):

1. Build TestApp: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"`.
2. **Attribution:** `TestApp diagnose-labels --runs E:\WorkshopData\Data\autofocus\sensitivity_example1\attempt01
   --params current` → confirms which gates reject the 168 `wronglyRejected` boxes (expected: LowSensitivity
   dominant; a TooLowHFR subset that currently mislabels as NO CANDIDATE — note this as the bug Phase 1 fixes).
3. **Concrete settings:** `TestApp optimize --runs …\attempt01 --labels …\attempt01\labels --out <dir>` → writes
   `optimized_settings.json` + `optimize_summary.txt` with the old→new curated-param deltas. This is the
   black-box recommendation available TODAY; report the changed params (esp. `BrightnessSensitivity`, `MinHFR`)
   and the recall/star-count change to the user.
4. **Report + optionally apply:** tell the user the decided settings and the seed→best `J` / star-count delta.
   On request, set them in the active NINA profile via the Star Detection options (or hand them the JSON) and
   list exactly which option values changed. **Read-only on the profile unless the user says to apply.**

This grounds the design in real numbers and lets the user review before the new analytical recommender lands;
Phases 1–3 then replace the black-box step with the transparent per-gate recommender (more interpretable, same
or better recovery), and Phase 5 adds structure-gap recovery.

**Phase 0 RESULTS (executed 2026-06-15 on the example):**
- **Label loading hit the G3 runId-mismatch bug.** The wizard saved the file as
  `E__WorkshopData_..._attempt01.json` with embedded `runId` = the absolute path, but offline discovery (pointing
  `--runs` at the attempt folder) assigns `RunId="attempt01"`. `diagnose-labels` (uses
  `StarReviewLabelStore.Load`, exact-filename path) matched once the file was copied to `attempt01.json`; but
  `optimize`'s OWN loader (`OptimizationDiagnosticRunner.LoadLabels`) keys by the **embedded** runId, so it also
  needed the embedded `runId` rewritten to `attempt01`. **In-wizard re-optimize is unaffected (in-memory
  labels)**, but this confirms G3 must be fixed for the offline harnesses and any path-relocated run. Workaround
  used: temp `C:\temp\hf-labels-fix\attempt01.json` with `runId":"attempt01"`.
- **Gate attribution (`diagnose-labels --params current`, 132 boxes):** **111 LowSensitivity (84%) + 21
  TooDistorted (16%), 0 NO-CANDIDATE, 0 already-accepted.** Everything is gate-fixable — the current gates DO
  have the flexibility; no structure-detection gap in this example (so Phase 5 isn't exercised here).
- **Current profile is badly mis-tuned:** `BrightnessSensitivity=16.17` (vs default 2.0), `MaxDistortion=0.6125`,
  `StructureLayers=5`, `MinStarBoundingBoxSize=6`, `MinHFR=1.5`, `StarClippingMultiplier=0.3125`. The extreme
  sensitivity is why ~84% of labels are LowSensitivity rejections; detection finds only 11–53 stars/frame.
- **The 21 TooDistorted are SMALL and near-focus (4409/4559)** — NOT large defocused donuts. So the defocus-aware
  *gate* (relaxes only large candidates) would NOT recover them; lowering `MaxDistortion` does. This validates the
  recommender's size-gated branch (don't recommend `DefocusAwareGates` for small TooDistorted).
- **Black-box `optimize --labels`:** **Seed J 0.898 → Best J 0.988, recall 0.943 / precision 1.0**, star counts
  ~2–3× (3809: 11→42, 4259: 53→102, 5009: 16→42), hard-floor PASS. Changed params: `BrightnessSensitivity
  16.17→0`, `MaxDistortion 0.6125→0.5875`, `StructureLayers 5→6`, `StarClippingMultiplier 0.3125→0.375`,
  `MinStarBoundingBoxSize 6→7`, `HotpixelThreshold 0.001375→0.000875`; `DefocusAwareGates` stayed OFF (correct).
- **KEY INSIGHT validating the whole design:** the optimizer drove `BrightnessSensitivity` to **0** (the floor) —
  because with recall-only labels and **no should-reject labels, precision is unconstrained**, so nothing stops
  it admitting noise. This is exactly the failure the transparent recommender's **unlabeled-admit precision
  proxy** (decision 3) prevents: it will lower `BrightnessSensitivity` only to the level that recovers the
  *labeled* stars (≈ the min sensitivity among the 111 labeled boxes, exposed by Phase 1's measured values) and
  report the precision cost, instead of collapsing to 0. Sensitivity=0 should NOT be applied to the profile as-is.

### Phase 0.5 — G3 stable run key (fold-in; fixes offline label matching)

Wizard-saved labels are keyed by the sanitized **absolute** run path (filename + embedded `runId`), but offline
discovery (`OptimizationRunDiscovery`) assigns `RunId` = relative path / folder name (e.g. `attempt01`), so
`optimize`/`diagnose-labels`/`recommend --labels` don't match wizard labels without manual renaming (proven in
Phase 0). Fix: pick a **stable run identity** consistent between the wizard loader (`RunEvaluationLoader` runId
derivation) and `OptimizationRunDiscovery`, and make the label loaders match on it robustly.
- **Approach:** keep the label JSON shape compatible (existing files must still load). Make
  `StarReviewLabelStore.Load` and `OptimizationDiagnosticRunner.LoadLabels` ALSO match when the discovered RunId
  equals the **last path segment(s)** of an embedded absolute-path runId (so `attempt01` matches an embedded
  `…\attempt01`), in addition to exact-match. Prefer a relative-to-runs-root or trailing-segment key.
- **Files:** `Review/StarReviewLabels.cs` (`FileNameFor`/`CleanRunFileStem`/`Load` embedded-scan), `RunEvaluationLoader.cs`
  (runId derivation), `OptimizationRunDiscovery.cs` (offline runId), `OptimizationDiagnosticRunner.LoadLabels` +
  the new `RecommendRunner`. **Tests:** a wizard-written absolute-path file loads against a discovered
  `attempt01` run; legacy exact-match still works.

### Phase 1 — Per-rejected-candidate measured-value exposure (foundation)

- **New type** `RejectedCandidateRecord` in `Interfaces/IStarDetector.cs` (next to `ContaminationDiagnosticRecord`):
  `Rect Bounds; string Gate; double MeasuredValue; double ThresholdValue; double CandidateSize; double CenterX, CenterY;`.
- **New param** `StarDetectorParams.CollectRejectedCandidateDiagnostics` (default false); **add to
  `CacheKeyExcludedProperties`** (output-neutral, like the contamination flag).
- **Populate in `EvaluateStarCandidate`**: thread a `ConcurrentBag<RejectedCandidateRecord> rejectedBag`
  (created in `GateAndMeasureInternal`/`EvaluateStarCandidates` only when the flag is on, exactly like
  `contaminationDiagnosticsBag` at StarDetector.cs:623) → at *every* `return null` gate add a record guarded by
  `if (p.CollectRejectedCandidateDiagnostics && rejectedBag != null)`, **including the counter-only gates**
  (TooSmall/TooLowHFR/HFRAnalysisFailed) and the non-scalar ones (record `NaN`). For NotCentered record the
  normalized offset `max(|cx−bx|/(W/2), |cy−by|/(H/2))`.
- **Surface** `HocusFocusStarDetectorResult.RejectedCandidates` (null unless flag on), materialized in
  `GateAndMeasureInternal` with the same ROI-offset (`+= offset`) + deterministic (Y,X) sort as the existing
  bounds. **Do not** change `StarDetectorMetrics` (records are a parallel side-channel) so all existing readers
  and equivalence/signature tests are untouched.
- **Tests:** `RejectedCandidateDiagnosticsTests` — detect a real frame OFF vs ON → identical `DetectedStars` +
  every metrics counter; ON → every rejected candidate has a record whose `(MeasuredValue, ThresholdValue)`
  inverts the gate correctly; confirm the flag is in `CacheKeyExcludedProperties` (no key change).

### Phase 2 — Shared matcher + `LabelGateAnalyzer`

- **New** `StarDetection/Optimization/Review/BoxMatcher.cs` (plugin): `RectD`, `IoU`, generalized `ClassifyBox`
  that now also consults `RejectedCandidateRecord` (so it reports gate + measured value, and TooLowHFR boxes are
  attributed instead of NO CANDIDATE). Refactor `DiagnoseLabelsRunner` to call it (delete the TestApp copies),
  mirroring how `StarReviewRunner.ExtractRejected` delegates to `FrameReviewBuilder.ExtractRejected`.
- **New** `StarDetection/Optimization/LabelGateAnalyzer.cs` (plugin; detector + float-mat loader seam, like
  `FrameReviewBuilder`). Per labeled position: re-detect once with `CollectRejectedCandidateDiagnostics=true`;
  classify each recall-target box (Missed ∪ WronglyRejected): accepted → **AlreadyRecovered**, rejected-record →
  bucket `(gate, MeasuredValue, CandidateSize)`, neither → **NoCandidate**; match `ShouldReject` boxes to
  accepted stars. Output `LabelGateAnalysis { GateBucket[] Buckets; RecallTarget[] NoCandidate;
  ShouldRejectTarget[] ShouldReject; int AlreadyRecovered, TotalRecallTargets; }`.
- **Tests:** `BoxMatcherTests` + `LabelGateAnalyzerTests` over synthetic accepted/rejected lists; assert the
  TooLowHFR-attributed-not-NO-CANDIDATE case; accepted wins IoU ties.

### Phase 3 — `GateRecommender` (the direct analysis)

- **New** `StarDetection/Optimization/GateRecommender.cs` (pure, deterministic, image-free → fully unit-testable).
- **Per-gate inversion (monotone):** from a bucket's measured-value distribution, the threshold to admit `j`
  targets is an order statistic (e.g. LowSensitivity: `Sensitivity = nextBelow(sort(sensitivities)[j-1])`).
  Directions: Sensitivity↓, MinHFR↓, MaxDistortion↓, MinimumStarBoundingBoxSize↓, PeakResponse↑,
  StarCenterTolerance↑. **Non-invertible:** Degenerate/OnBorder/HFRAnalysisFailed → "not recoverable by a
  threshold; needs detector work" (route to Phase 5). Contaminated → suggest `RejectContaminatedStars=false` /
  raise `ContaminationSensitivity` only if that bucket dominates.
- **Defocus-aware gates as a recovery lever** (TooDistorted / NotCentered buckets): when the rejected targets are
  **large** (`CandidateSize > DefocusDistortionSizeReference`, i.e. defocused), prefer **enabling
  `DefocusAwareGates`** (the existing LATE distortion+centering relaxation, already a synthetic curated optimizer
  variable) over globally lowering `MaxDistortion` / raising `StarCenterTolerance` — the global loosening hurts
  precision on small near-focus stars, whereas the defocus-aware relaxation only fires for large candidates and
  is guarded by the `SDefocusPrecision` near-focus penalty. Emit a row recommending the toggle, and (see Phase 4)
  always leave it in the optimizer's search space so the optimizer can confirm/enable it even when the analytic
  pass didn't pick it.
- **Precision cost** of a proposed threshold = count of currently-rejected **UNLABELED** candidates of that gate
  on the admit side of the new threshold, **near-focus-weighted** (reuse the `NearFocusWindowSteps·stepSize`
  window from `OptimizationObjective.SDefocusPrecision`); **plus** a hard reject of any move that would re-admit a
  `ShouldReject` target.
- **Greedy coordinate selection** across gates by recovered-per-precision-cost, within a precision budget,
  re-attributing remaining targets after each pick (no double-spend). Output `GateRecommendation {
  StarDetectorParams RecommendedParams; RecommendationRow[] Rows; bool RecommendStructureRecovery;
  double EstimatedPrecisionCost; }`; rows read e.g. `"BrightnessSensitivity 2.0 → 1.10  recovers 21/23, admits
  ~7 unlabeled (≈1.2 near-focus)"`.
- **Tests:** `GateRecommenderTests` — synthetic buckets with known measured arrays → exact order-statistic
  threshold, exact recovered counts, exact unlabeled-admit cost, near-focus weighting, and `ShouldReject`
  hard-honor; cover every monotone direction.

### Phase 4 — Warm-start integration (no optimizer-core change)

- **New** `OptimizerVariable.CreateWarmStartSet(baseSet, recommendation)`: for each curated variable the
  recommender *moved*, narrow `[Lower, Upper]` to a tight band around the recommended value (recommended ± a few
  `InitialStep`s, clamped) keeping `InitialStep`; **omit** unimplicated variables from the set entirely (the seed
  already carries their value — pin by omission, NOT by `InitialStep=0`, to dodge the `ContinuousStepsBelowFloor`
  edge case).
- **Always-live defocus-aware toggles** (user requirement): the defocus-aware axes — the existing
  `DefocusAwareGates` (distortion+centering relaxation) and the new `DefocusAwareStructure` (Phase 5) — are
  **never pinned off** by the warm-start set. They stay in the optimizer's search space at full range so the
  optimizer can enable them **if needed** even when the analytic recommender didn't implicate them; the
  `SDefocusPrecision` near-focus penalty keeps them honest (objective bit-identical when no star is
  relaxation-admitted). If the recommender *did* pick a defocus-aware toggle, seed it ON; otherwise seed at the
  current value but keep it tunable. This is an explicit exception to the "omit unimplicated" rule above.
- **Wizard caller** (`StarDetectionOptimizerWizardVM`): make `OptimizeAsync` accept an injected seed + variable
  set; the new feedback flow passes `recommendation.RecommendedParams` as seed and the warm-start variable set.
  The existing label objective term (`Wl`) stays active so the search balances the analytic point against σ.
- **Tests:** `OptimizerVariableTests` additions — bounds narrowed around moved params, unimplicated omitted,
  `Read`/`Write` preserved; seeded optimize never regresses below the recommended-seed `J`.

### Phase 5 — New defocus-aware structure-detection setting (decision 2; EARLY, opt-in)

- **Mechanism (minimal, reuses the proven path):** add EARLY params `bool DefocusAwareStructure` (default false)
  + `int StructureLayerBoost` (default 0). When ON, compute the à-trous B3-spline residual at
  `StructureLayers + StructureLayerBoost` layers (StarDetector.cs ~499–510,
  `ComputeResidualAtrousB3SplineDyadicWaveletLayer` then `SubtractInPlace`) so large/donut structures survive the
  residual subtraction and form candidates; blur kernel stays keyed to the original `StructureLayers` (tune). The
  boost is applied **only inside `if (p.DefocusAwareStructure)`**, else the call is byte-for-byte the current one.
- **Cache key:** add both new params to **`EarlyCacheKeyProperties`** (they change formation → must invalidate
  the early context). When OFF, effective layers == `StructureLayers` ⇒ detected stars identical (the key string
  gains two stable tokens, which is fine).
- **Exposure:** `StarDetectionOptions.DefocusAwareStructure` + `StructureLayerBoost` (+ `IStarDetectionOptions`),
  full options pattern (accessor get/set in setters, `InitializeOptions`, `ResetDefaults`, validation, GUID via
  `typeof(AutoFocusOptions)`); map in `BuildStarDetectorParams`; UI CheckBox + UnitTextBox + `_Tooltip`
  TextBlocks in `Resources/OptionsDataTemplates.xaml`; new EARLY synthetic curated variable
  `DefocusAwareStructure` in `OptimizerVariable.CreateCuratedSet()`.
- **Recommender hook:** when `NoCandidate` is non-trivial and its `CandidateSize`/defocus distribution skews
  large, set `RecommendStructureRecovery = true` and emit a row; warm-start un-pins the `DefocusAwareStructure`
  axis so the optimizer can confirm it helps without hurting σ.
- **Tests:** `DefocusAwareStructureTests` — OFF vs independent re-impl that never reads the flag → identical
  `DetectedStars`/metrics; confirm it IS in `EarlyCacheKeyProperties`. (This is the exploratory part; if a clean
  bit-identical mechanism proves hard, scope it to its own PR but keep the recommender's NoCandidate flagging.)

### Phase 6 — In-wizard UI + offline harness

- **Optimize-vs-validate toggle** (decision 4): on the SelectSource/Optimize step add a mode selector
  (Optimize | Use current settings). "Use current settings" sets `BestParams = seed` (current detector params
  from the active profile), skips the `OptimizeAsync` call, builds the Summary as a no-op delta ("no optimization
  run — current settings"), and enables jumping straight to Review. Flow becomes `SelectSource → Acquire →
  [Optimize?] → Summary → Review`; from that Review the user labels and can still run "Optimize with feedback".
  Implement as a `WizardOptimizeMode` enum + a guarded branch in the start/optimize path; the snapshot/review/
  re-loop machinery is unchanged (it already operates off `BestParams`).
- **Wizard** (`StarDetectionOptimizerWizardVM` + `DataTemplates.xaml`): the "Optimize with feedback" button now
  runs `OptimizeWithFeedbackAsync` = **analyze** (`LabelGateAnalyzer` + `GateRecommender`, off-UI, reusing the
  busy/phase indicators) → **show breakdown** (bind `FeedbackAnalysis`/`Recommendation`: per-gate rows
  recovered/total + threshold change + est. new admits, NoCandidate count + structure suggestion) in the feedback
  `Border` (DataTemplates.xaml:253–276) → **warm-start optimize** (Phase 4) → updated Summary. Keep the existing
  Accept / Review / re-loop flow; reuse the already-snapshotted `reviewDescriptors`/`reviewLabelsDir`/captured
  labels (survive Mat disposal). Add `SDOpt_OptimizeWithFeedback_Tooltip`.
- **TestApp** new subcommand `recommend` (new `TestApp/RecommendRunner.cs`, dispatched in `Program.cs` next to
  `diagnose-labels`): args mirror `diagnose-labels` (`--runs --labels --params --opt-results --profile-id --out`)
  plus `--precision-budget` and `--structure-recovery on|off`; builds `currentParams` via the same
  source-of-truth as `diagnose-labels` with `CollectRejectedCandidateDiagnostics=true`, runs the shared
  analyzer + recommender, prints the per-gate breakdown + recommended delta + total weighted precision cost +
  NoCandidate/structure recommendation, writes `recommend.txt`. (Optional `--then-optimize` chains the
  warm-started optimize for end-to-end validation.)

---

## Files to create / modify

**Create:** `StarDetection/Optimization/Review/BoxMatcher.cs`, `StarDetection/Optimization/LabelGateAnalyzer.cs`,
`StarDetection/Optimization/GateRecommender.cs`, `TestApp/RecommendRunner.cs`; tests
`GateRecommenderTests.cs`, `LabelGateAnalyzerTests.cs`, `BoxMatcherTests.cs`,
`RejectedCandidateDiagnosticsTests.cs`, `DefocusAwareStructureTests.cs`.

**Modify:** `Interfaces/IStarDetector.cs` (`RejectedCandidateRecord`; new params; `CacheKeyExcludedProperties`;
`HocusFocusStarDetectorResult.RejectedCandidates`) · `StarDetection/StarDetector.cs` (`EarlyCacheKeyProperties`;
rejected-bag plumbing + per-gate records; wavelet boost; `GateAndMeasureInternal` materialize) ·
`StarDetection/HocusFocusStarDetection.cs` (`BuildStarDetectorParams` maps new options) ·
`StarDetection/StarDetectionOptions.cs` + `Interfaces/IStarDetectionOptions.cs` (two new options) ·
`StarDetection/Optimization/OptimizerVariable.cs` (`CreateWarmStartSet` + EARLY `DefocusAwareStructure` variable) ·
`StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` (`OptimizeWithFeedbackAsync`, injected seed/vars,
bindings) · `StarDetection/Optimization/DataTemplates.xaml` (breakdown UI) ·
`Resources/OptionsDataTemplates.xaml` (controls + tooltips) · `TestApp/DiagnoseLabelsRunner.cs` (use `BoxMatcher`)
+ `TestApp/Program.cs` (dispatch `recommend`).

## Verification (end-to-end, on the user's example)

Build: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"`. Run folder
`E:\WorkshopData\Data\autofocus\sensitivity_example1\attempt01` (labels in its `labels\`).

1. **Attribution** (proves the TooLowHFR fix): `TestApp diagnose-labels --runs …\attempt01 --params current`
   → `wronglyRejected` dominated by `REJECTED:LowSensitivity` with a secondary `REJECTED:TooLowHFR` bucket
   (previously NO CANDIDATE).
2. **Recommend:** `TestApp recommend --runs …\attempt01 --params current` → dominant gate LowSensitivity;
   `BrightnessSensitivity` 2.0 → ~1.1 recovering the bulk of the 168; a small `MinHFR` drop for the TooLowHFR
   subset; **near-focus-weighted precision cost small** (these stars recur across positions, so few new
   *unlabeled* admits); `NoCandidate ≈ 0` ⇒ structure-recovery NOT recommended — the expected signature.
3. **End-to-end:** `TestApp recommend --then-optimize …` (or `TestApp optimize --runs … --labels …`) → accepted
   counts up at labeled positions while σ(focus) is not materially worse (precision held).
4. **In NINA:** (a) **Use current settings** mode → wizard skips optimization → Review shows current detection to
   validate/label without an optimization pass; (b) run the wizard with **Optimize** → Review → "Optimize with
   feedback" → confirm the breakdown renders, the recommendation applies, Summary updates, Accept persists.

Full `dotnet test … -c Debug` green; every default-OFF path bit-identical (independent-reimpl tests).

## Risks & bit-identicality guards

- **New EARLY structure param is the biggest risk:** must be in `EarlyCacheKeyProperties` AND default-OFF AND
  bit-identical-OFF (independent re-impl). Missing the early-key entry → stale early-context reuse → wrong stars.
  If a clean mechanism is hard, split Phase 5 into its own PR but keep the NoCandidate flagging.
- **Diagnostics flag must not affect detection or the cache key** — keep it in `CacheKeyExcludedProperties`,
  side-channel only, guarded adds; independent-reimpl OFF test.
- **Recommender correctness depends on a complete record set** — every `return null` path must emit a record
  (the per-gate table is the checklist; the diagnostics test asserts it).
- **Non-monotone gates** (Degenerate/OnBorder/HFRAnalysisFailed) → non-recoverable (flag); bound
  NotCentered/`StarCenterTolerance` at curated limits to avoid junk admits.
- **Precision proxy applies even with zero `shouldReject`** (this example) — the unlabeled-admit proxy is the
  primary signal; `shouldReject` is an additional hard constraint only when present.
- **Warm-start pinning** by omission, not `InitialStep=0` — **except** the defocus-aware toggles
  (`DefocusAwareGates`, `DefocusAwareStructure`), which are always kept live in the optimizer's search space so it
  can enable them if needed (guarded by `SDefocusPrecision`).
