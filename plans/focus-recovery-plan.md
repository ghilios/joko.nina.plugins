# Plan: "Focus recovery" for the Star Detection Optimizer wizard (Live mode)

## Context

The Star Detection Optimizer wizard's **Live** source mode captures a fixed, non-convergent
focuser sweep centered on the user's current (rough-focus) position, then optimizes star-detection
parameters against those frames. The sweep is strictly symmetric: `2·offsetSteps + 1` positions at
`center ± i·stepSize` (`ComputeSweepPositions`, `AutoFocusEngine.cs:1893`), where `offsetSteps` comes
from the profile's `AutoFocusInitialOffsetSteps`. This assumes the starting position is genuinely near
focus. When it is **not** (the user is beyond the sweep's reach), the whole sweep can sit on one flank
of the focus curve, never bracketing the minimum — so the AF curve fit fails and the optimizer has
nothing usable to score.

This change adds a **"Focus recovery"** knob to the wizard that widens the Live sweep by *N* extra
steps per side, giving the fit measurements far enough out to bracket focus even from a poor start. The
extra far-from-focus frames are noisy (few/no stars), so they are fed to the curve fit at **reduced
weight** and are **exempt from the optimizer's star-count gates** — otherwise they would break the
objective (see "Why the exemption is mandatory" below).

**Confirmed scope decisions (from the user):**
1. **Scope:** the detection-optimizer wizard's **Live mode only**. Production autofocus
   (`StartBlindFocusPoints`) and Replay mode are untouched and stay byte-identical.
2. **Extension:** `Focus recovery = N` adds **N steps per side** (symmetric): effective offset =
   `profile offsetSteps + N`, sweep = `2·(offset+N)+1` positions.
3. **Weighting:** **fit-only, down-weighted.** Recovery frames feed only the curve fit at reduced
   weight and are exempt from the star-count hard floor & star-count sub-scores.
4. **Persistence:** wizard **session-only** VM property, default 1 each launch (like
   `StartFromCurrentSettings`). No persisted profile option → no `Resources/OptionsDataTemplates.xaml`
   control needed.

### Why the exemption is mandatory (not just a refinement)
`OptimizationObjective.JRun` (`OptimizationObjective.cs:300`) has a **hard floor**:
`MaxFramesBelowHardFloor = 0`, so **any** single frame with `< NHard (3)` accepted stars forces
`J = 0`. Heavily-defocused recovery frames routinely detect `< 3` stars, so adding them without an
exemption would zero out **every** candidate and make the wizard unusable. Consideration #2's
down-weighting is therefore a correctness requirement of consideration #1, not an optional extra.

---

## Implementation

Preserve the file's pervasive **bit-identical-baseline invariant** throughout: when `N == 0`
(Replay / recovery off), every new list field must be **null** (not an all-false list) and every new
branch must return the exact current behavior.

### 1. Wizard VM option + readouts — `StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs`
- Add a session-only VM property mirroring `StartFromCurrentSettings` (~line 607):
  - `private int focusRecoverySteps = 1;` (field default = 1; the VM is constructed fresh each launch,
    so no explicit reset is needed).
  - `public int FocusRecoverySteps { get; set; }` — clamp to `Math.Max(0, value)`, raise
    `RaisePropertyChanged()` **and** `RaiseSweepReadoutsChanged()` (the point count changes).
- Effective-offset readouts (the Live readouts at ~758–819 read the profile):
  - Add `public int SweepEffectiveOffsetSteps => SweepOffsetSteps + (IsLive ? Math.Max(0, FocusRecoverySteps) : 0);`
  - Change `SweepPointCount` to `2 * SweepEffectiveOffsetSteps + 1` (so `SweepEstimatedFrames` /
    `SweepEstimatedDurationText` widen automatically).
  - Add `SweepEffectiveOffsetSteps` to `RaiseSweepReadoutsChanged()` (~821).
- Recovery is inert for Replay (`SweepEffectiveOffsetSteps` adds 0 when `!IsLive`, and step 3 leaves
  Replay's `RecoveryStepsPerSide == 0`).

### 2. Widen the Live sweep — `RunLiveAttemptAsync` (~1977), same file
- Add an **`internal static` helper** so the bump is unit-testable (the file already exposes
  `HandleCaptureProgress` as `internal`, and the test assembly has `InternalsVisibleTo`):
  `internal static void ApplyFocusRecovery(AutoFocusEngineOptions options, int recoverySteps)`.
  - No-op when `recoverySteps <= 0` (keeps N=0 byte-identical).
  - `options.AutoFocusInitialOffsetSteps += recoverySteps;` (do **not** touch `AutoFocusStepSize` —
    recovery widens range, it does not refine spacing).
  - **Scale the timeout** by the point-count ratio, because `CaptureFixedSweepImpl`
    (`AutoFocusEngine.cs`) enforces `new CancellationTokenSource(options.AutoFocusTimeout)`:
    `options.AutoFocusTimeout = TimeSpan.FromTicks((long)(oldTicks * (double)newPoints / oldPoints));`
    Scale only the **per-run override**, never the persisted `AutoFocusTimeoutSeconds` (same discipline
    as `InspectorVM.ApplySignalAmplification`, `InspectorVM.cs:1228`).
- Call `ApplyFocusRecovery(options, FocusRecoverySteps);` right after the existing
  `Save`/`SavePath`/`OverrideAutoFocusExposureTime` mutations, before `CaptureFixedSweepAsync` (~2014).

### 3. Thread the recovery tag into the evaluator → metrics → objective
- **`RunEvaluationData.cs`:** add `public int RecoveryStepsPerSide { get; set; } = 0;` (mirrors the
  existing in-memory `FrameParallelismOverride` knob, ~226). Default 0 ⇒ Replay/harness byte-identical.
- **Wizard, `StartAsync`:** snapshot the applied value alongside `LastRunWasLive` into a new field:
  `capturedRecoveryStepsPerSide = (SourceMode == SourceMode.Live) ? Math.Max(0, FocusRecoverySteps) : 0;`
  (snapshot, so a later edit of the box can't desync the two optimize passes).
- **`AcquireAsync` (~1940)** and the **re-optimize/feedback reload loop (~2665)** — both create fresh
  `LoadedRun`s via `loader.LoadSavedRunAsync(...)`. After each, set
  `loaded.Data.RecoveryStepsPerSide = capturedRecoveryStepsPerSide;`.
  **The reload site is critical** — miss it and the feedback pass loses the tag, so the run suddenly
  hard-floors and its J disagrees with the first pass.
- **Recovery-position identification** in `EvaluateAndFitAsync` (~623, over the existing ascending
  `SortedDictionary<int,…> byPosition`): let `D = byPosition.Count`, `N = RecoveryStepsPerSide`.
  - `N <= 0` or `D == 0` → no recovery; leave `FrameIsRecovery = null`; build `points` exactly as today.
  - Else `perSide = Math.Min(N, Math.Max(0, (D - MinPositionsForFit) / 2));` (always leaves ≥3
    non-recovery anchor positions). Mark the `perSide` smallest and `perSide` largest distinct
    positions → `HashSet<int> recoveryPositions`. `perSide == 0` → treat as no recovery (null).
  - This handles failed/missing frames (perSide shrinks), asymmetric loaded sets (extremes of the
    sorted list), and `FramesPerPoint > 1` (recovery is per distinct position; all frames at that
    position are flagged).
  - Build `FrameIsRecovery` parallel to `FrameStarCounts` in the same 604–617 loop as
    `recoveryPositions.Contains(frames[i].FocuserPosition)`; assign to metrics **only when**
    `recoveryPositions.Count > 0` (else null).
- **`OptimizationObjective.cs` `RunEvaluationMetrics` (~179):** add
  `public IReadOnlyList<bool> FrameIsRecovery { get; set; }` (parallel to `FrameStarCounts`; documented
  `null ⇒ baseline`, like the other nullable parallel lists).

### 4. Down-weight recovery points in the fit — `RunEvaluationData.cs` pooled-point build (~632)
Fit weight is `1 / max(|ErrorY|, 1e-6)` (`HyperbolicFittingAlglib.cs:33`), so inflating a recovery
position's `ErrorY` down-weights it. Two traps the implementation **must** handle:
- **`ErrorY == 0` far frames** (single-frame or degenerate scatter → `SafeDisplayError` yields 0): a
  bare `× factor` is inert. Compute a positive reference scatter and floor against it:
  `refErr = Math.Max(median(SafeDisplayError(stdev) over non-recovery positions), 1e-6);`
  recovery `ErrorY = RecoveryErrorInflation * Math.Max(SafeDisplayError(pooled.Stdev), refErr);`
  Ship a documented constant `RecoveryErrorInflation = 10.0` (recovery point ≈ 1/10 the weight of a
  typical near-focus point — a bracketing point that pins the wings without steering the minimum), as a
  single tuning knob. (Optional later refinement: distance-grade the factor by ring; ship the flat
  factor first.)
- **`UseWeights == false`** (`fitConfig.UseWeights`, from `WeightedHyperbolicFitEnabled`, default
  **true**): the fit ignores `ErrorY` entirely, so inflation can't down-weight. In this mode, when
  `recoveryPositions.Count > 0`, **exclude** recovery positions from the fit input `points` (they still
  populate all per-frame metrics, so the objective exemption and capture accounting are unaffected;
  only the fit omits them). `PooledPointCount` then counts fitted (non-recovery) positions.
- Byte-identity: `RecoveryStepsPerSide == 0` ⇒ `recoveryPositions` empty ⇒ no inflation, no exclusion,
  identical `points`/`ErrorY`/`FrameIsRecovery`.

### 5. Objective exemptions — `OptimizationObjective.cs` `JRun` (`null ⇒ bit-identical` everywhere)
- **Hard floor (~307):** count starved frames only among non-recovery frames —
  `below = count of i where FrameStarCounts[i] < c.NHard AND !(FrameIsRecovery?[i] ?? false)`.
  This is the core fix.
- **`SStars` (~263):** compute `nMin`/`nMedian` over **non-recovery** frames only (filter the counts by
  `FrameIsRecovery` before the call, or pass it in). `null ⇒` identical.
- **`TieBreakerScore` (~389):** it uses `FrameStarCounts.Average()` over **all** frames and is active
  by default (`Wtie = 0.02`) and is **not** near-focus-windowed — filter recovery frames out of that
  mean, or recovery wings bias the plateau search. (Surfaced by the plan audit; easy to miss.)
- **Near-focus penalties (`SDefocusPrecision`/`SHfrOutlier`/`SCoverage`):** no change on the primary
  path — they already window to `1.5·step` of `BestFocusPosition`, which excludes the outer recovery
  frames. *Optional low-risk hardening:* add `if (FrameIsRecovery?[i] ?? false) continue;` in their
  fallback loops (taken only when `BestFocusPosition` is NaN) so even the fallback excludes recovery
  frames.
- Thread `FrameIsRecovery` from `EvaluateAndFitAsync`'s metrics assembly (~641) into the new field.

### 6. Wizard UI — `StarDetection/Optimization/DataTemplates.xaml` (wizard XAML, **not** `OptionsDataTemplates.xaml`)
- Inside the existing `IsLive`-gated `StackPanel` (~line 418), add a **"Focus recovery (extra steps per
  side)"** integer input near the capture summary (just above the "Number of points" row). Use the
  file's numeric idiom (`ninactrl:UnitTextBox`/`TextBox`, `UpdateSourceTrigger=LostFocus`) bound to
  `FocusRecoverySteps` with a **≥ 0** validation rule (there's a `rules:GreaterThanZeroRule` precedent
  at ~line 458; add/reuse a non-negative-int rule). Add a tooltip string alongside the other
  `SDOpt_*_Tooltip` resources.
- The "Number of points" row already binds `SweepPointCount` (now effective), so the widened count shows
  automatically. Optionally add a small "(includes N recovery steps/side)" note bound to
  `FocusRecoverySteps`/`SweepEffectiveOffsetSteps`.

### 7. Seed-guard hardening (recommended) — `SeedFitIsUsableAsync` (~2058)
The guard requires finite σ **and** `PooledPointCount >= 3`. With recovery frames the count is trivially
≥ 3, so a starless-near-focus run could pass on recovery positions alone. Add
`NonRecoveryPooledPointCount` to `RunEvaluationResult` (fitted non-recovery positions) and gate on that
instead. `RecoveryStepsPerSide == 0` ⇒ `NonRecoveryPooledPointCount == PooledPointCount` (unchanged).

### 8. Summary curve (optional, cosmetic) — `BuildSummaryAsync` (~2245)
Recovery points appear on the plotted Current/Optimized curves (they're in `Points`). Functionally fine.
Optionally add `IReadOnlyList<bool> FrameIsRecovery` to `OptimizationCurve` and render recovery scatter
points hollow/greyed so the user reads them as down-weighted bracketing points. Not required for
correctness. (`StepSizeRecommender.Recommend` reads the half-width off the **fitted model**, so
down-weighted recovery points barely move the recommendation — no change needed there.)

---

## Files to modify
- `StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` — VM property + readouts,
  `ApplyFocusRecovery` helper, `RunLiveAttemptAsync`, snapshot in `StartAsync`, tag in `AcquireAsync`
  **and** the re-optimize reload loop, seed-guard gate.
- `StarDetection/Optimization/RunEvaluationData.cs` — `RecoveryStepsPerSide`, recovery-position
  identification, `ErrorY` inflation, unweighted-mode exclusion, `FrameIsRecovery` +
  `NonRecoveryPooledPointCount` plumbing.
- `StarDetection/Optimization/OptimizationObjective.cs` — `RunEvaluationMetrics.FrameIsRecovery`;
  `JRun` hard-floor + `SStars` + `TieBreakerScore` exemptions.
- `StarDetection/Optimization/DataTemplates.xaml` — Live-panel recovery control + effective-offset readout.

**Reuse (don't reinvent):** `InspectorVM.ApplySignalAmplification` (option-mutation + timeout-scaling
precedent), `RunEvaluationData.FrameParallelismOverride` (in-memory knob precedent), `SafeDisplayError`
(`RunEvaluationData.cs:773`), and the existing `null ⇒ bit-identical` nullable-parallel-list pattern
throughout `OptimizationObjective.cs`.

---

## Risks & edge cases
- **`UseWeights == false`** makes `ErrorY` inflation inert → recovery positions must be **excluded** from
  the fit in that mode (uncommon path; default is weighted).
- **`ErrorY == 0` recovery points** → the reference-scatter floor (`refErr`) is what actually
  down-weights them; a bare multiply does nothing. Subtlest correctness point.
- **`TieBreakerScore` (Wtie = 0.02)** is an un-obvious `FrameStarCounts` consumer, not near-focus
  windowed — must be filtered.
- **Re-optimize reload** drops the tag unless `capturedRecoveryStepsPerSide` is re-applied — first vs
  second pass J divergence / hard-floor.
- **Timeout scaling** is required (fixed sweep enforces `AutoFocusTimeout`); scale only the per-run
  override.
- **True focus near a sweep extreme:** recovery is geometric (outermost N), so a genuinely near-focus
  outer frame could be down-weighted/exempt. Conservative (never zeroes a good run) and consistent with
  the confirmed geometric definition; the near-focus penalties still key off `BestFocusPosition`.
- **`RecoveryErrorInflation` magnitude:** too low ⇒ wings bend the fit; too high ⇒ recovery points add
  nothing. Ship ~10, cover with a fit-shape test, expose as one constant.

---

## Test plan (NUnit 4.4.0)
**Wizard VM / helper** (`StarDetectionOptimizerWizardVMTests`, extends existing `CaptureFixedSweepAsync`
stub + `HandleCaptureProgress` tests):
- `FocusRecoverySteps` defaults to 1; negative clamps to 0.
- Widens `SweepEffectiveOffsetSteps`/`SweepPointCount`/`SweepEstimatedFrames` and raises their
  `PropertyChanged`; only affects readouts in Live mode.
- `Start_LiveSweep_PassesWidenedOffsetToEngine` — capture the options passed to
  `CaptureFixedSweepAsync`; assert `AutoFocusInitialOffsetSteps == profileOffset + N` and timeout grew
  by the point ratio.
- `ApplyFocusRecovery` direct tests: bumps offset by N; scales timeout by point ratio; zero/negative is
  a no-op; does not change `AutoFocusStepSize`.

**Evaluator** (`RunEvaluationDataTests`, synthetic frames + fake detector):
- Marks outermost N-per-side as recovery; recovery positions get inflated `ErrorY` (incl. a zero-scatter
  recovery point still down-weighted); too-few-positions leaves ≥3 anchors (null when perSide collapses
  to 0); duplicate positions flag all frames there; unweighted fit excludes recovery from
  `Points`/`PooledPointCount` but keeps `FrameIsRecovery`; `RecoveryStepsPerSide == 0` is byte-identical
  (`FrameIsRecovery == null`).

**Objective** (`OptimizationObjectiveTests`):
- Recovery frames exempt from the hard floor (`J > 0` where it would be 0 without the tag); `SStars`
  ignores recovery frames; `TieBreakerScore` ignores recovery frames; **`FrameIsRecovery == null` is
  bit-identical** to the pre-feature `JRun` (the regression guard, parameterized).

---

## Verification (end-to-end)
1. **Unit suite** — run at **milestones, not after every edit** (per the user's reduced-cadence
   preference): once after the evaluator + objective changes (steps 3–5, the correctness core), and
   once as a final gate before completion. Between those, rely on a compile/build to catch breakage.
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
   (In this WSL environment use Windows `dotnet.exe` via interop with a `wslpath -w`'d solution path;
   timeout ~600s. The csproj PostBuild also xcopies the plugin into NINA's plugin folder.)
   All existing tests must stay green — the `FrameIsRecovery == null` / `RecoveryStepsPerSide == 0`
   regression guards prove the baseline is untouched.
2. **In-app (NINA + Windows MCP):** open the Star Detection Optimizer wizard → Live mode; confirm the
   "Focus recovery" input shows and that "Number of points" / "Total frames" / "Estimated time" widen as
   it changes. Run a Live sweep starting deliberately **off** focus with recovery = 1–2 and confirm the
   sweep captures the wider range, the optimizer produces a usable curve+result (does **not** hard-fail
   on the defocused wings), and the recommended step size is sane.
3. **Regression:** run a **Replay** optimization and confirm identical behavior to before (recovery
   inert), and a recovery=0 Live run to confirm the no-op path.

---

## Note on plan location
Per the project's `CLAUDE.md`, implementation plans live in the repo's `plans/` folder with a
`-plan.md` suffix. This file is the plan-mode working copy; on execution, save it to
`plans/focus-recovery-plan.md` in the repo, and `/clear` context before implementing.
