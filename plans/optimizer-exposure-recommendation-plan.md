# Optimizer Exposure Recommendation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** When the Star Detection Optimizer lands `BrightnessSensitivity` at its search floor (`<= 1.0`), the
wizard's Summary page names the problem, shows a **derived** longer exposure, and — for a Live run — re-captures
and re-optimizes at that exposure in one click. Replay runs get the same diagnosis with no action, and Accept
stays enabled.

**Architecture:** The gate statistic is already computed per candidate at `StarDetector.cs:1746-1762` and
discarded. Carry it on `Star.MeasuredSensitivity`, plumb it to `RunEvaluationMetrics.FrameStarSnrs` following the
existing `StarHFRs` precedent verbatim, and invert it in a new pure `ExposureRecommender`:
`t_new = t_old · (10 / S_now)²` where `S_now` is the median across non-recovery frames of each frame's
`NTarget`-th brightest accepted-star SNR. The new data is **inert** — it never enters `JRun`, so the objective
stays bit-identical.

**Tech Stack:** C# / .NET 8.0-windows7.0, WPF, NUnit 4, OpenCvSharp.

**Spec:** `docs/optimizer-exposure-recommendation-design.md` (approved). Branch:
`ghilios/exposure-recommendation` (already created).

**Environment notes (read first):**
- No `dotnet` in WSL — run Windows `dotnet.exe` via WSL interop (`wslpath -w` the sln). Set the Bash `timeout`
  to `600000` for build/test.
- Full suite: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- `SendAsync_WritesOnABackgroundThread` (EAT serial transport) is known-flaky and unrelated — do not chase it on
  full-suite runs. Do not pipe to `tail`; it masks the exit code.
- Building (including a test run) xcopies the plugin into NINA's plugin folder via the csproj PostBuild.
- Every commit must use the noreply identity:
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
  ```
- Never push to `develop`; the PR at the end targets `develop`.

**Run the suite at each task boundary and as a final gate** (not after every edit).

---

## Task 1 — Plumb per-star SNR out of the detector (inert)

Nothing user-visible. The whole task is "carry a number that is already computed", and it must not change a
single detection or objective value.

- [ ] `Interfaces/IStarDetector.cs` (`class Star`, ~:669-700): add
      `public double MeasuredSensitivity { get; set; } = double.NaN;` after `RelaxationAdmitted`; add it to
      `ToString()`. Document: informational only, never affects accept/reject, never enters the objective; it is
      the exact scalar the gate compared (`NormalizedBrightness/σ`, or the donut `max(...)` when
      `DefocusAwareDonutDetection` is on); **dimensionless, so it needs no rescaling** under detection binning or
      an ROI offset; `NaN` at every legacy construction site.
- [ ] `StarDetection/StarDetector.cs` (~:1810, the `new Star()` initializer): add
      `MeasuredSensitivity = sensitivity`. `sensitivity` is already in scope from :1746 with the donut branch
      applied *before* the gate at :1763, so this stores exactly what the gate compared. **No new computation,
      no new branch, no reordering.**
- [ ] `Utility/CvImageUtility.cs` `ScaleToSourcePixels` (~:774): carry through **unscaled**. Comment it beside
      `MeanBrightness`/`PeakBrightness` — it is a ratio of two intensities and mean binning preserves level for
      both numerator and denominator.
- [ ] `Utility/CvImageUtility.cs` `AddOffset` (~:744): carry through (a translation changes nothing). **Note in
      the code that this helper currently drops `RelaxationAdmitted`** — see Task 6; do not fix it here.
- [ ] `StarDetection/HocusFocusStarDetection.cs`: add the field to `HocusFocusDetectedStar` (~:215) and to
      `ToDetectedStar` (~:806). This is the lossy boundary the optimizer's path goes through.
- [ ] `StarDetection/Optimization/RunEvaluationData.cs` (~:41): add `FrameDetectionResult.StarSnrs`
      (`IReadOnlyList<double>`, parallel to `StarCenters`), worded like the adjacent `StarHFRs`.
- [ ] `StarDetection/Optimization/RunEvaluationLoader.cs` (~:273-283, producer #1): populate from
      `result.StarList` with `(s as HocusFocusDetectedStar)?.MeasuredSensitivity ?? double.NaN` — a failed cast
      must surface as `NaN`, not silently as `0`. Parallelism holds because `BuildStarDetectionResult` builds
      `StarList` with one ordered `.Select(ToDetectedStar)` (`HocusFocusStarDetection.cs:770`) and
      centers/HFRs/SNRs all derive from that single list.
- [ ] `TestApp/HarnessSplitDetector.cs` (~:76, **producer #2 — do not miss it**): populate from the post-filter
      `List<Star>` already used for `StarCenters`/`StarHFRs`. `TiltCalibrationRunner.cs:537` picks this up for
      free.
- [ ] `StarDetection/Optimization/OptimizationObjective.cs` (~:203-206): add
      `RunEvaluationMetrics.FrameStarSnrs` (jagged, parallel to `FrameStarCounts`). Comment it as **inert data —
      no sub-score and no term of `JRun` reads it**. **Nothing else in this file changes.**
- [ ] `StarDetection/Optimization/RunEvaluationData.cs` (~:619-640, :731-736): accumulate `frameStarSnrs`
      mirroring `frameStarHfrs` (`?? Array.Empty<double>()` per frame) and assign `FrameStarSnrs`.
- [ ] **Do NOT bump `StarDetector.StarDetectorVersion`** — no detection output changes and no
      `StarDetectorParams` property is added, so `ComputeCacheKey`/`ComputeEarlyCacheKey` are unchanged and every
      user's `_star_detection_result.json` stays valid.

**Tests**
- [ ] `Tests/StarDetection/StarDetectorTests.cs`: accepted stars carry a finite `MeasuredSensitivity` that is
      `> p.Sensitivity`, and (for a non-extended candidate) equals `NormalizedBrightness/σ`.
- [ ] `Tests/StarDetection/DetectionBinningTests.cs`: survives binning **unscaled**.
- [ ] New/extended coverage for the ROI-offset path, so the `AddOffset` field-drop can never recur.
- [ ] `Tests/StarDetection/StarDetectionResultCacheSerializationTests.cs`: round-trips, including the `NaN`
      default. (Newtonsoft's default `FloatFormatHandling.String` writes `"NaN"` — quoted, valid, parseable.)

**Gate — bit-identity must stay green untouched:**
- [ ] `grep -c FrameStarSnrs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/OptimizationObjective.cs`
      returns exactly `1` (the declaration).
- [ ] The `JRun_BitIdentical_*` family in `Tests/.../OptimizationObjectiveTests.cs` (~:392, :402, :412, :665,
      :1000, :1270) passes **unmodified**.
- [ ] `StarDetectorEquivalenceTests` passes unmodified (its signature hashes `Center`/`HFR`/`PeakBrightness`
      and the metrics scalars — none of which change).
- [ ] Full suite green. Commit.

---

## Task 2 — The pure recommender

New file `StarDetection/Optimization/ExposureRecommender.cs`, modeled on `StepSizeRecommender`: static, pure, no
VM and no NINA types, so the math is fully testable in isolation.

- [ ] Constants:
      ```csharp
      public const double TargetSensitivity             = 10.0;
      public const double SensitivityFloorThreshold     = 1.0;
      public const double MaxExposureFactor             = 4.0;
      public const double MaxRecommendedExposureSeconds = 30.0;
      public const int    MinFramesForRecommendation    = 3;
      ```
      Each gets a comment carrying its justification (see the design doc). `SensitivityFloorThreshold` must cite
      `StepFloorFraction = 0.125` — the reason an `== 0` test would miss most real cases.
- [ ] `ExposureRecommendation` result type: `HasRecommendation`, `CurrentSeconds`, `RawSeconds` (uncapped,
      unrounded), `RecommendedSeconds` (capped **then** rounded — this pre-fills the box), `MeasuredSnr`,
      `UsableFrameCount`, `ShortFrameCount`, `WasCapped`, `CappedByAbsoluteLimit`, `ExposureIsNotTheLimit`.
- [ ] `SensitivityIsAtFloor(double)`.
- [ ] Per-frame quantile: drop non-finite and non-positive SNRs, sort **descending**, take index `NTarget-1`.
      A frame with `0 < k < NTarget` accepted stars uses `A[k-1]` (its faintest accepted star) and increments
      `ShortFrameCount` — a bounded *under*-estimate, so it under-recommends rather than inventing a number.
      A frame with zero contributes nothing.
- [ ] `S_now` = **median across non-recovery frames** of those per-frame values. Reuse the `FrameIsRecovery`
      convention from `OptimizationObjective.cs:310-330`; `null` ⇒ every frame is non-recovery.
- [ ] `Recommend(...)` takes `NTarget` from the passed-in `ObjectiveConstants` — **never hard-code 20**, or an
      objective retune silently desyncs the recommendation from the knee it claims to invert.
- [ ] Order of operations, all inside `Recommend`: `S_now` → raw factor `(Target/S_now)²` → `RawSeconds` → cap at
      `min(t_old × 4, 30 s)` → `RoundExposureSeconds` **after** the cap, so the displayed value can never exceed
      it. Set `WasCapped` / `CappedByAbsoluteLimit` to say *which* cap bound.
- [ ] `RoundExposureSeconds` always rounds **up** (rounding down partially undoes the derivation): next 0.5 s
      below 10 s, next 1 s to 30 s, next 5 s above.
- [ ] Edge cases: `< MinFramesForRecommendation` usable frames, non-positive `t_old`, or `FrameStarSnrs == null`
      ⇒ `HasRecommendation = false`. `S_now >= TargetSensitivity` ⇒ `ExposureIsNotTheLimit = true`, and
      **never** a shorter exposure.

**Tests** — new `Tests/StarDetection/Optimization/ExposureRecommenderTests.cs`, following
`StepSizeRecommenderTests` conventions:
- [ ] `SensitivityIsAtFloor` swept: `0 / 0.125 / 0.5 / 1.0` true; `1.0001 / 2.0 / 10.0` false.
- [ ] `TargetSensitivity_MatchesTheShippedDefault` — pinned against
      `HocusFocusStarDetection.BuildDefaultStarDetectorParams().Sensitivity`.
- [ ] Takes the `NTarget`-th brightest per frame (40 descending SNRs ⇒ element 19 exactly).
- [ ] **Medians across frames rather than taking the worst**: per-frame values `{12,11,10,9,2,9,10,11,12}` ⇒ 10,
      not 2; assert a worst-frame rule would have produced a much larger factor.
- [ ] Excludes recovery frames; `FrameIsRecovery == null` treats every frame as non-recovery.
- [ ] Short frame uses its faintest accepted star and increments `ShortFrameCount`.
- [ ] Drops `NaN` / `+∞` / `0` / negative.
- [ ] `< 3` usable frames ⇒ NaN / no recommendation. `FrameStarSnrs == null` ⇒ no recommendation.
- [ ] Reads `NTarget` from the constants (pass `NTarget = 60` and assert the quantile index moves).
- [ ] Scales as the square of the SNR ratio (`S_now = 5`, `t = 5 s` ⇒ raw factor 4.0).
- [ ] `RoundExposureSeconds` always rounds up, swept across the three ladder bands.
- [ ] Never recommends shorter when `S_now >= 10`; sets `ExposureIsNotTheLimit`.
- [ ] Caps at 4× (`WasCapped`, `CappedByAbsoluteLimit == false`) and at 30 s
      (`CappedByAbsoluteLimit == true`); reports `RawSeconds` honestly in both.
- [ ] Non-positive `t_old` ⇒ no recommendation.
- [ ] Full suite green. Commit (no wiring yet — pure math, tested in isolation).

---

## Task 3 — Summary wiring and display

### `t_old`
- [ ] **Live:** `capturedLiveExposureSeconds` (`StarDetectionOptimizerWizardVM.cs:1763`).
- [ ] **Replay:** nothing in a saved attempt records the exposure (`SavedAutoFocusAttempt` /
      `AutoFocusReplayMetadata` carry none). Add `LoadedRun.CapturedExposureSeconds`, read in
      `RunEvaluationLoader.LoadSavedRunAsync` from the first rendered frame's
      `RawImageData.MetaData.Image.ExposureTime`. **Verify at implementation time** that NINA's
      `imageDataFactory.CreateFromFile` actually populates it from the FITS/XISF header.
- [ ] **Fallback:** `profileService.ActiveProfile.FocuserSettings.AutoFocusExposureTime`, stated in the copy.
      If neither yields a positive number ⇒ no derived value. Never scale a factor off an unknown base.

### Model
- [ ] `OptimizationSummary` (`StarDetectionOptimizerWizardVM.cs:99-342`) gains `RunExposureSeconds`,
      `OptimizedSensitivity`, `BaselineSensitivity`, `ExposureAdvice`, `BaselineExposureAdvice`, and
      `HasExposureRecommendation` — mirroring the detection-binning field set (`MeasuredInFocusHfr` /
      `BaselineMeasuredInFocusHfr` / `HasDetectionBinningMeasurement`) exactly.
- [ ] **`HasExposureRecommendation` is `OptimizedSensitivity <= SensitivityFloorThreshold` — sensitivity alone.**
      It is *not* additionally gated on the derived factor being large or on star counts. Comment this
      explicitly: a floored gate is worth saying out loud even when a longer exposure is not the answer; the
      sub-states carry that nuance.
- [ ] `BuildSummaryAsync` (`:2646-2736`): compute both variants' advice inside the existing `if (i == 0)` block
      where `measuredInFocusHfr`/`baselineInFocusHfr` are already captured — both `RunEvaluationResult`s are in
      hand, so **no extra evaluation pass**. Run 0 only, matching the binning precedent (with multiple Replay
      runs the per-run exposures may differ and a single number would be ill-defined).
- [ ] `BuildCurrentSummary` (`:2744`): copy the baseline fields across, exactly as it already does for the HFR
      pair. Variant behaviour: Optimized/Feedback read `res.BestParams.Sensitivity` + `bestEval.Metrics`;
      Current reads `baseline.Sensitivity` + `baselineEval.Metrics`, so a user who hand-set their own gate to 0
      is told why.

### VM
- [ ] Add beside the binning set (`:1670-1753`): `HasExposureBlock`, `LowSignalChartNote`,
      `RecommendedExposureText`, `ExposureBodyText`, `RecaptureExposureSeconds` (pre-filled at summary build,
      not persisted), `ShowCaptureNewSweep` (`lastRunWasLive && !IsUseCurrentMode`), `CanCaptureNewSweep`,
      `CaptureNewSweepCommand`, and `RaiseExposureBlockChanged()`.
- [ ] Call `RaiseExposureBlockChanged()` from `RaiseSelectedVariantDependents()` **and** from
      `SnapshotReviewInputs` — the binning block needed both, and for the same reason.
- [ ] Cleanup while in there: `RaiseSelectedVariantDependents()` calls `RaiseDetectionBinningBlockChanged()`
      twice (`:1614` and `:1631`) — drop one.

### XAML — `StarDetection/Optimization/DataTemplates.xaml`
- [ ] New tooltip resource `SDOpt_StarSignal_Tooltip` (with the others at ~:29): explains the gate, the `[0,50]`
      search, the default of 10, and states the assumption in one clause — *"sky noise is assumed to dominate,
      so quadrupling the exposure doubles S/N."*
- [ ] **(a)** One italic sentence in the slot `OptimizerNoImprovementNote` occupies (~:716-721), bound to
      `LowSignalChartNote` / `HasExposureBlock`, `Foreground="{StaticResource NotificationWarningBrush}"`
      (verified to exist; same treatment as `AutoFocus/DataTemplates.xaml:3197`). This is the page's **only**
      colored element. Never `NotificationErrorBrush`; no icon.
      > Stars in these frames barely cleared the noise, so this result is built from low-confidence detections;
      > see Star signal below.
- [ ] **(b)** A **"Star signal"** block inserted between "Stars per frame" (~:792) and "Auto-focus settings"
      (~:794) — the head of the recommendation region, above the two blocks whose numbers it impugns, and
      adjacent to the existing `SweepExposureChangeText` row. Structure copies the detection-binning block
      (`:835-861`) verbatim: bold header → `Width="200"` label/value row → `MaxWidth="560"` wrapped body →
      left-aligned action.
- [ ] Row: `Recommended exposure` → `3 s → 12 s (measured star S/N 4.1; target 10)`.
- [ ] Action row (visible only when `ShowCaptureNewSweep`): `New sweep exposure` + a `ninactrl:UnitTextBox`
      cloned from `:576-590` (`Unit="s"`, `MinWidth="80"`, `GreaterThanZeroRule`,
      `UpdateSourceTrigger=LostFocus`) bound to `RecaptureExposureSeconds`, then the button
      **`Capture a new sweep and optimize`**.
- [ ] **The button label carries no number** — deliberately breaking from "Optimize again at 2x2". Beside an
      editable `LostFocus`-bound box, a numbered label goes stale mid-edit and disagrees with the box. Comment
      this so it is not "fixed" later. `CanCaptureNewSweep` gates runnability, so an unavailable Live action
      shows as a **disabled button**, never as nothing (the house rule at `:1677-1687`).

### Body copy (house voice: plain, consequence-first, no "Warning:", no exclamation marks)
- [ ] **Live, actionable:** "Stars in these frames sat just above the noise, so the optimizer had to lower
      Brightness Sensitivity to 0.1 to find them, and the focus result rests on low-confidence detections. At
      about 12 s the same stars would reach the detector's normal acceptance level. Capturing a new sweep at
      that exposure re-tunes the settings on trustworthy frames."
- [ ] **Live, capped:** same opening, then report what `RawSeconds` implies — "Reaching the detector's normal
      acceptance level would take about 75 s per frame, roughly 14 minutes per auto-focus run. A longer exposure
      still helps, but at that length this filter is the limit; consider auto-focusing through a broadband
      filter with a filter offset instead."
- [ ] **Replay:** same opening, then "You can still accept these settings; they are the best fit for frames like
      these. For a more reliable tune, raise your auto-focus exposure to about 12 s in NINA's focuser options
      and run this wizard again in Live mode."
- [ ] **`ExposureIsNotTheLimit`:** "The gate landed at its floor, but your stars already clear the default gate —
      this field is star-poor, not under-exposed."
- [ ] **No derivable number:** diagnosis only, no exposure figure, no button.

**Tests**
- [ ] Extend `Tests/.../OptimizationSummaryTests.cs` (the `DetectionBinning_*` cluster is the template): block
      visibility on/off by sensitivity; follows the variant's own sensitivity, not the baseline;
      `BuildCurrentSummary` carries the baseline advice; one case per copy state.
- [ ] Extend `StarDetectionOptimizerWizardVMTests.cs` with a detect stub emitting synthetic `StarSnrs`: the
      block surfaces on a floored run; **Replay shows the block with no button**; **Replay Accept stays
      enabled**; the block is absent on a healthy run.
- [ ] Full suite green. Commit.

---

## Task 4 — The Live capture + re-optimize action

`CaptureNewSweepAsync(CancellationToken)` — a near-clone of `OptimizeAgainAtRecommendedBinningAsync`
(`:3346-3438`), reusing its `running` interlock, progress reset, chain reset, and `finally` shape.

**The differences from the binning clone — each is a real trap:**

| Aspect | Binning version | This one |
|---|---|---|
| Frames | **Reloads from disk** via `reoptimizeRunFolders` | **Captures fresh** via `RunLiveAttemptAsync` |
| Loop | over `reoptimizeRunFolders` | over **`RunCount`** — a single-shot re-run silently drops runs 2..N |
| Pending state | `pendingDetectionBinning`, restored on failure | `LiveExposureSeconds`, restored on failure (same `previous`/`succeeded` pattern) |
| Snapshot | `SnapshotReviewInputs(reloaded, reoptimizeRunFolders, …)` | must pass the **fresh** folders/ids — the old ones are stale |
| Dialog | "no new exposures, no focuser movement" | must say the opposite: new exposures, the focuser moves, estimated duration |

- [ ] Gate `CanCaptureNewSweep` on: `lastRunWasLive`, not `IsUseCurrentMode`, `autoFocusEngine != null`, camera
      and focuser connected, `SaveFolderPath` set, not busy.
- [ ] Set `LiveExposureSeconds = RecaptureExposureSeconds` before capturing (`RunLiveAttemptAsync` reads it);
      restore `previousExposure` in `finally` when `!succeeded`.
- [ ] Loop `RunCount` times: `RunLiveAttemptAsync` → `LoadRunStampedAsync(folder, null, loadProgress, token)`,
      collecting fresh folders and run ids.
- [ ] Then `AnalyzeWithProgressAsync(captured, r => r.Seed, token)` → `ComputeBaselineJAsync` → `OptimizeAsync`
      → `BuildSummaryAsync`, then the binning version's chain-reset block verbatim (clear `optimizedChain` /
      `optimizedRoundJ` / `optimizedRoundCurves`; null the feedback trio; rebuild `currentSummary`; select
      `Optimized`; `RaiseSelectedVariantDependents()`).
- [ ] `SnapshotReviewInputs(captured, freshFolders, freshRunIds)` — **not** `reoptimizeRunFolders`.
- [ ] **Reuse `capturedRecoveryStepsPerSide`** — do NOT re-read `FocusRecoverySteps`, per `LoadRunStampedAsync`'s
      documented contract.
- [ ] **Leave `pendingDetectionBinning` alone** so a prior "Optimize again at 2x2" still applies to the fresh
      capture.
- [ ] `capturedLiveExposureSeconds` is updated by `RunLiveAttemptAsync`, so `SweepExposureChangeText` and the
      `Apply` write-back follow automatically — verify by test, add no extra plumbing.
- [ ] Confirmation dialog: `internal static string DescribeCaptureNewSweep(double from, double to)` beside
      `DescribeReoptimizeAtBinning` (`:564-571`), plus a `confirmCaptureNewSweep` delegate defaulting to
      `() => true` (so tests/headless proceed), wired to `MyMessageBox.Show` in the MEF convenience ctor.
      **Every line must stay under `MaxDialogLineLength = 60`**, hand-broken — NINA's `MyMessageBox` TextBlock
      has no `TextWrapping`.

**Tests**
- [ ] `Tests/StarDetection/DialogTypesettingGuardTests.cs`: a `[TestCase]`-swept case over
      `DescribeCaptureNewSweep` — the numbers are interpolated, which is exactly what the guard exists to catch.
- [ ] `StarDetectionOptimizerWizardVMTests.cs`: declined dialog changes nothing (no
      `CaptureFixedSweepAsync` call, `LiveExposureSeconds` unchanged); capture uses the recommended exposure
      (assert `options.OverrideAutoFocusExposureTime`); honors `RunCount = 2` (two captures); preserves
      `pendingDetectionBinning`; reuses the snapshotted recovery steps when `FocusRecoverySteps` is edited
      mid-run; failed capture restores the exposure and leaves the Summary; resets the round chain and drops
      feedback; updates the Accept write-back; blocked while busy (the interlock).
- [ ] Full suite green. Commit.

---

## Task 5 — Manual

- [ ] Document the rule in `documentation/docs/`, following the `settings/detection-binning.md` precedent (which
      documents its recommendation rule so the UI copy and the manual cannot drift): when it fires, the `√t`
      assumption, the target of 10, and both caps. Cross-link from `settings/acceptance-gates.md`, which already
      ties a starved gate to autofocus at :69.
- [ ] Read `.claude/docs/documentation-style.md` first. Run the `adversarial-doc-review` skill over the changed
      pages before committing.
- [ ] Commit.

---

## Task 6 — Separate: the `AddOffset` `RelaxationAdmitted` fix

Independent of this feature; **do not fold into the commits above.**

- [ ] `Utility/CvImageUtility.cs` `AddOffset` (~:744-766) rebuilds a `Star` and copies 8 of 9 fields, dropping
      `RelaxationAdmitted` — while `ScaleToSourcePixels` (:774-805) carries it. On any ROI run the flag is
      cleared before `BuildStarDetectionResult` re-tallies `RelaxationAdmittedCount`
      (`HocusFocusStarDetection.cs:754`), so `SDefocusPrecision` reads 0 and is silently inert.
- [ ] Restore it, with a test covering the ROI path.
- [ ] **This changes J for ROI + donut-detection runs.** Say so in the commit message. Blast radius today is
      small — the wizard is constructed with `region: StarDetectionRegion.Full`
      (`StarDetectionOptimizerWizardVM.cs:493`) — but the drop is real.
- [ ] Full suite green. Commit.

---

## Verification (final gate before the PR)

- [ ] `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — full suite green.
- [ ] `grep -c FrameStarSnrs .../OptimizationObjective.cs` ⇒ exactly 1. The bit-identity guard.
- [ ] **Offline, on real starved data:** run the optimizer over a bank run and confirm the derived exposure is
      sane. `docs/bobp-m101-recall-investigation-results.md` documents a real run that landed
      `BrightnessSensitivity = 0`; the bank lives at `D:\Autofocus Bank` (see
      `.claude/docs/running-af-bank-validation` / the `running-af-bank-validation` skill).
- [ ] **Confirm it does NOT trigger on a healthy, well-exposed bank run** — no new noise on the happy path.
- [ ] **In the app** (building deploys the plugin to NINA's plugin folder): open the optimizer, run **Replay**
      against a known low-signal saved folder — the warning sentence renders above the chart, the block renders
      with no button, and **Accept is enabled and applies the settings**. Then a **Live** run with a
      deliberately short exposure to exercise the capture button end-to-end.
- [ ] Open the PR against `develop`.
