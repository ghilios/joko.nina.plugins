# Initial-HFR validation failure — diagnosis and logging expansion

## Context

During a Tilt Adapter calibration **baseline** autofocus run (`TiltCalibration_20260703_225028/01_Baseline`),
the run failed with two log entries a user reported:

1. `WARNING AutoFocusEngine.cs ValidateCalculatedFocusPosition:1575 Failed assessing HFR at the initial position`
2. `ERROR AutoFocusToolVM.cs LoadChart:327 Failed to load autofocus chart — System.IO.IOException: The process
   cannot access the file 'C:\Users\Detle\AppData\Local\NINA\AutoFocus\2026-07-03--22-53-42.json' because it is
   being used by another process.`

The two failures are **independent**. Neither is a star-detection or curve-fit problem — the autofocus sweep and
fit were excellent (all 7 regions chose Symmetric hyperbolic models; calculated point 7236 → 7241 after the +5
offset, well inside the swept 6998–7498 range; final-validation HFR ≈ 2.67).

## Failure #2 — "Failed assessing HFR at the initial position" (the real rejection)

### What the check actually does

`ValidateHfrImprovement` compares the HFR at the **final** (calculated) focus position against the HFR at the
**initial** focuser position and rejects the run if focus did not improve. The per-region validation loop
(`AutoFocusEngine.cs:1565–1592`) requires **every** region to have *both* a valid final HFR **and** a valid
initial HFR. The failing branch is:

```csharp
if (!autoFocusRegionState.InitialHFR.HasValue || autoFocusRegionState.InitialHFR.Value.Measure == 0.0) {
    Logger.Warning("Failed assessing HFR at the initial position");   // line 1575
    ...
    autoFocusState.LastFailureMode = AutoFocusFailureMode.InitialHfrFailed;
    return false;
}
```

The message names neither the region nor the reason, so the log cannot tell you *why*.

### Root cause (from the full log)

The **initial** frame at focuser 7248 (`\initial\01_..._Focuser7248.fits`, 22:51:16) was anomalously star-poor.
Its per-region detection lines were:

| Region | 0 | 1 | 2 | 3 | 4 | 5 | 6 |
|--------|---|---|---|---|---|---|---|
| Stars  | 19 | 30 | 51 | **— (≤1 usable)** | 33 | 12 | 19 |

**Region 3 logged no HFR line for the initial frame** because it had ≤1 usable star. Both the `AverageHFR`
assignment *and* the summary log line in `BuildStarDetectionResult` are gated on `hfrStars.Count > 1`
(`HocusFocusStarDetection.cs:689`); with ≤1 usable star, `AverageHFR` stays at its default **0.0** and nothing is
logged. (A `StarDetectionResult` object was still created and stored — this is "no usable-star HFR", not a
missing result.) For comparison, the *same* focuser position 7248 during the sweep 70 s later
(`\attempt01\06_..._Focuser7248.fits`) produced 88–104 stars per sub-region and **837** in the full frame — a
~44× jump at identical focus. HFR of the stars that *were* detected in the initial frame was normal (≈2.6), so
this is a transient acquisition/transparency event (thin cloud, dew, gust), not defocus, trailing, or a detector
regression.

The chain that turned "region 3 had ≤1 usable star in one frame" into a whole-run failure:

1. `EvaluateExposure` returned `Measure = analysisResult.AverageHFR = 0.0` **directly** (`:681`) for region 3 —
   it did *not* throw, so the `AnalyzeExposure` catch (`:973–978`, which would also yield `Measure = 0.0`) was
   not the path here; the `hfrStars.Count > 1` guard left `AverageHFR` at 0.0.
2. `StartAutoFocusPoint` (`:1014`) dispatches an analysis task for **every** region, so
   `InitialHFRMeasurementAction` (`:906`) ran for region 3, appended the 0.0 sub-measurement, reached
   `FramesPerPoint` (1), and set `region3.InitialHFR = { Measure: 0.0 }` (a value, **not** null — but line 1574
   rejects on either `null` *or* `Measure == 0.0`, so the distinction does not change the outcome).
3. The early guard in `StartBlindFocusPoints` (`:1069`) only inspects **region 0** (`InitialHFR == 0.0`), which
   was a healthy 2.60, so it did not fire.
4. The full sweep ran (~2 min), all fits succeeded, the final-validation image was captured — and only then did
   the per-region loop (`:1574`) reach region 3, see `Measure == 0.0`, and reject the run as `InitialHfrFailed`.
5. `InitialHfrFailed` is **not** eligible for the re-centered retry (`ShouldRetryFromCalculatedPoint`,
   `:464–475`), so that path did not fire. More importantly, even the *standard* attempt-count reattempt
   (`:1293`) could not have rescued this run: `StartInitialFocusPoints` (`:1056`) only re-measures the initial
   HFR when **region 0's** `InitialHFR` is null — region 0 was a valid 2.60, so region 3's cached `0.0` would
   never be re-measured and every reattempt would fail identically at line 1574.

**Net:** one transient zero-star region in the single initial frame discarded an otherwise-perfect autofocus,
and the generic log line gave no way to see that region 3 (and only region 3, and only at the initial position)
was responsible.

## Failure #1 — the IOException (cosmetic, plugin-caused, separate)

`%LOCALAPPDATA%\NINA\AutoFocus\2026-07-03--22-53-42.json` is written by the **plugin** on the AF-failed path:
`InspectorVM.AutoFocusEngine_Failed` (`InspectorVM.cs:1519`) does `File.WriteAllText(ReportDirectory\<ts>.json)`
where `ReportDirectory = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus")` (`HocusFocusVM.cs:96`). The
filename has **no** profile-GUID suffix, which distinguishes it from `HocusFocusVM.GenerateReport`
(`…--{profileId}.json`, `:453`) and matches the log exactly.

NINA core's `AutoFocusToolVM` watches that directory and, on a new report, calls `LoadChart()` →
`File.OpenText(newest)`. `File.OpenText` opens with `FileAccess.Read, FileShare.Read`; while `File.WriteAllText`
still holds its `FileAccess.Write` handle open, the reader's share mode does not admit the existing write access,
so Windows raises `ERROR_SHARING_VIOLATION` → the `IOException`. It is intermittent (depends on whether the
watcher's read lands during the write) and affects only the **chart display** — calibration data is unaffected.

**Mitigation (plugin side) — *implemented*:** `PathUtility.WriteAllTextAtomic` serializes to a sibling temp file
(non-`.json` extension so the watcher ignores it), then `File.Move(tmp, path, overwrite: true)` renames it into
place, so the final report never has an open write handle. Applied to the three writes that target the watched
`ReportDirectory` (`InspectorVM.cs` success + failed paths, `HocusFocusVM.GenerateReport`); the per-attempt
writes under the user's AF save folder are unwatched and left as-is. Note the reader is NINA core — changing the
*writer's* `FileShare` cannot help, because the failing check is the reader's fixed `FileShare.Read` refusing the
writer's `Write` access; only removing the concurrent write handle resolves it.

## Changes

Status: **all implemented** (`AutoFocusEngine.cs`, `HocusFocusStarDetection.cs`, `PathUtility.cs`,
`InspectorVM.cs`, `HocusFocusVM.cs` + tests; full suite green — 1698 passed).

### 1. Robustness: gate the HFR-improvement check on region 0 only — *implemented*

The whole-run HFR-improvement validation now checks **region 0** (the primary/full-frame region that drives the
focus decision) only, instead of looping over every region. A transient dropout in a corner/sub-region — exactly
the incident, where region 3 momentarily detected ≤1 usable star — is now **non-fatal** and cannot discard an
otherwise-good autofocus. This mirrors the region-0-only early guard already in `StartBlindFocusPoints` (`:1069`),
so region 0's own bad initial HFR still fails fast there.

The three-way decision is extracted into a pure, unit-tested `EvaluateHfrImprovement(initialHfr, finalHfr,
threshold)` returning the specific `AutoFocusFailureMode` (`FinalHfrMissing` / `InitialHfrFailed` /
`HfrRegression`) or `None`; the call site branches on it for the per-mode log/notification/state.

### 2. Log every region at the detector — *implemented*

`BuildStarDetectionResult` (`HocusFocusStarDetection.cs`) previously wrote its `Average HFR … Region: N` line
**inside** the `hfrStars.Count > 1` guard, so a region with 0–1 usable stars logged nothing — which is why region
3 was silently absent and its failure had to be inferred. The `AverageHFR`/`HFRStdDev` computation stays guarded
(so those values, and the fit inputs, are bit-identical), but the log line now always emits (respecting
`SuppressInfoLogging`). Region 3 with 0 stars now logs `Average HFR: 0, HFR MAD: 0, Detected Stars 0, Region: 3`.

### 3. Log the reason for the HFR-validation failure — *implemented*

The two generic warnings are replaced by a per-region diagnostic from a pure, unit-tested helper
`DescribeHfrValidationFailure(phase, regionIndex, hfr, subMeasurements, framesPerPoint)`, reporting the region
index (cross-references the detector's `Region: N` lines), the phase, `null` vs `Measure == 0`, and the per-sub-
frame `Measure`+σ. The `σ` is the tell for the zero case: finite `σ` = detector found no usable stars
(`{Measure:0, Stdev:0}`, `:681`); `σ = NaN` = analysis threw (`{Measure:0, Stdev:NaN}`, `AnalyzeExposure` catch
`:976`). The `Notification` toast is a short form (`… (Region N). See log for details.`). Example:

```
Failed assessing HFR at the initial position for Region 0: HFR measured 0 - the detector found no usable stars
in this region across 1 sub-frame(s). Measured HFR=0.00; sub-frames 1/1 [0.00 (σ=0.00)]. Cross-reference the
"Region: 0" star-detection lines.
```

### 4. Carry the region into `metadata.json`'s `FailureReason` — *implemented*

`AutoFocusState` gained `LastFailureRegionIndex` (set at each HFR-validation failure return);
`FailureReasonText(mode, int? regionIndex)` appends `"; Region N"` when present (append-only, so the existing
round-trip test still holds), making the replay record self-describing.

### Not kept

An earlier capture-time warning in `InitialHFRMeasurementAction` was removed: with region-0-only gating, region 0
already fails fast in `StartBlindFocusPoints`, and change #2 makes every region visible at capture time — so the
extra warning was redundant and its "Failed…" wording would have been misleading for the now-non-fatal corner
regions.

## Tests

- `EvaluateHfrImprovement` — the region-0 gate decision across all branches (final null/zero → `FinalHfrMissing`;
  initial null/zero → `InitialHfrFailed`; final worse-beyond-threshold → `HfrRegression`; within-threshold and
  improved → `None`). Written test-first (watched the stub fail before implementing).
- `DescribeHfrValidationFailure` — null-vs-zero, finite-σ (no stars) vs NaN-σ (analysis error), multi-frame list,
  and region/phase echo, in the existing pure-helper style in `AutoFocusEngineTests.cs`.
- `PathUtility.WriteAllTextAtomic` — content round-trips, no leftover temp file, overwrites existing, null guard.
- Full suite (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`): **1698 passed, 0 failed**.
  The detector-equivalence tests still pass, confirming change #2 left `AverageHFR`/`HFRStdDev` bit-identical.
