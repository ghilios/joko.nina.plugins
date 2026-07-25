# Plan: Autofocus robustness when starting far from focus

## Context

HocusFocus's blind autofocus (`AutoFocusEngine.StartBlindFocusPoints`) seeds points on the high side of
the start, then adaptively walks left/right by trendline counts until it brackets a minimum. When a run
**starts far from focus** two things go wrong today:

1. **Lopsided curves.** If detection still works far out, the completed sweep ends up with many more points
   on one side of the minimum than the other, biasing the final fit.
2. **Failure to bootstrap.** The initial outward points can be too far off to establish a curve — and when
   detection degrades (no stars far from focus, or *spurious low-HFR "stars" imagined in background noise*
   creating a false minimum), the walk either throws `TooManyFailedMeasurementsException` or locks onto
   garbage. The only current mitigation is a coarse once-per-run re-center (`ShouldRetryFromCalculatedPoint`)
   after a whole failed sweep.

This change adds two engine behaviors + matching visualization + a simulator test battery, so autofocus
converges to a reliable, balanced curve from a poor start. Behavior parity when disabled/inert is a hard
invariant (mirrors the focus-recovery feature).

**Scope decisions (confirmed with user):**
- Only the **directional cap** is a user-facing option. The **symmetric window** is always-on internal
  behavior (inert for balanced runs), with an internal-only engine flag as a test seam / safety valve — no
  persisted option, no UI.
- Test battery uses **both tiers**: a fast deterministic scripted-degradation battery driving the full
  engine end-to-end, plus 2–3 realistic simulator-camera render capstones that confirm the real detector
  actually produces the degraded regimes.

---

## Behavior A — Symmetric-window exclusion at the final fit (always-on internal)

**Goal:** the final fit only includes points within `minimum ± (offsetSteps+0.5)*stepSize`; points outside
that window are excluded from the fit but still surfaced for display.

**Where:** apply at **finalization only**, never in the hot live `CurveFittingResult.Calculate` (the live
trendline drives the walk's stop condition; clipping it would change sampling). Add a region method invoked
in both finalization loops before `SelectBestHyperbolicModel()`:
- `ValidateCalculatedFocusPosition` finalize site (`AutoFocusEngine.cs` ~:1720)
- `RerunImpl` finalize site (~:2483)

**Method (new, on `AutoFocusRegionState`):** `bool ApplyFinalSymmetricWindow(int offsetSteps, int stepSize)`
— a bounded **fit → window → refit** fixed point:
1. Provisional center = `DetermineFinalFocusPoint()?.X` (the fitted vertex: hyperbolic/quadratic minimum or
   trend/curve average per configured method). **Center on the fitted vertex, NOT the lowest raw point** —
   the vertex is a post-Grubbs, weighted least-squares estimate, so a single spurious low-HFR far point can't
   define the center the way `TrendlineFitting.Minimum` (literally the lowest sample) would. This is the key
   guard against consideration #3's false minimum.
2. Partition points by the window; if none fall outside → return (no-op, byte-identical). If keeping ≥3
   points, drop the outside points, `UpdateCurveFittings(kept)`, repeat.
3. Monotonic point-removal ⇒ terminates in ≤ `offsetSteps+1` passes (usually 1). Never let kept < 3 (the
   `Calculate` `<3 ⇒ null` floor). Order-independent (fit already canonicalizes point order).

**Pure, unit-testable helpers** (mirror the `ComputeSweepPositions` static-helper pattern):
```csharp
internal static bool IsWithinFocusWindow(double position, double minimumX, int offsetSteps, int stepSize)
    => position > minimumX - (offsetSteps + 0.5) * stepSize
    && position < minimumX + (offsetSteps + 0.5) * stepSize;   // strict, per spec

internal static (List<ScatterErrorPoint> included, List<ScatterErrorPoint> excluded)
    PartitionByFocusWindow(IReadOnlyList<ScatterErrorPoint> points, double minimumX, int offsetSteps, int stepSize);
```

**Distinct category — window-excluded ≠ Grubbs-rejected.** Window-exclusion runs *before* the fit; Grubbs
runs *inside* the fit of the windowed subset, so the two sets are disjoint. Surface excluded points on the
same channels `RejectedPoints` uses, as a **sibling** (never reclassified as rejected):
- `AutoFocusRegionState.WindowExcludedPoints : Dictionary<int,MeasureAndError>` (beside `RejectedPoints` ~:204;
  clear in `ResetMeasurements`).
- `CurveFittingResult.WindowExcludedPoints : ImmutableList<ScatterErrorPoint>` (beside `RejectedPoints` ~:99).
- `AutoFocusRegionResult.WindowExcludedPoints`, `AutoFocusRegionHFR.WindowExcludedPoints`,
  `AutoFocusMeasurementPointCompletedEventArgs.WindowExcludedPoints` (`AutoFocusRegionPoint[]`, in
  `IAutoFocusEngine.cs` ~:230/:277/:266) — populated alongside the existing `RejectedPoints` `.Select(...)`
  at ~:1996/:2502/:2554/:2702 (empty on the live per-point events; real set on completed/final).

**Multi-region:** the window pass runs **per region** around each region's own vertex (correct for tilt
runs where regions focus at different positions). Out-of-bounds gate keeps using the full point range, so
windowing can only *reduce* `FinalPointOutOfBounds`, never mask an extrapolating fit.

**Internal enable flag (test seam, not user-facing):** add `bool SymmetricFocusWindowEnabled` to
`AutoFocusEngineOptions` (DTO) **only**, default `true`, wired to `true` in `GetOptions` (not read from any
persisted `IAutoFocusOptions` property, no UI). Tests construct options with it `false` to assert the C0
byte-identical control. `ApplyFinalSymmetricWindow` early-returns when false.

---

## Behavior B — Directional bootstrap cap + reversal (user-facing cap)

**Goal:** cap points attempted in each direction; if one direction can't bootstrap a bracket within the cap,
return toward the start and try the other direction once. Combined with A → a balanced final curve.

**Where:** inside the `StartBlindFocusPoints` walk loop (`AutoFocusEngine.cs` ~:1325).

**Mechanics:**
- Track `leftStepOuts` / `rightStepOuts` (incremented on the step-LEFT ~:1383 and step-RIGHT ~:1397 branches;
  failed detections on an arm count as step-outs too).
- A direction "failed to bootstrap" when its counter reaches the **effective cap** without an interior
  two-sided bracket (`leftTrendCount>0 && rightTrendCount>0`, which the break conditions at :1345/:1363
  already require).
- Add `bool hasReversed = false` + `WalkDirection? forcedDirection = null`. On first cap-out (and
  `!hasReversed`): set `hasReversed`, `MoveFocuser(initialFocusPosition)` (return toward start), force the
  opposite direction, reset that arm's counter for a fresh budget, log the reversal. At most **one** reversal
  per sweep (latch → no oscillation).
- Make the `TooManyFailedMeasurementsException` throw (~:1336) **reversal-aware**: when B is enabled and
  `!hasReversed`, reverse instead of throwing; only throw once both directions are exhausted (or B disabled).

**Effective cap:** `Math.Max(configured, offsetSteps + 1)` — a positive value can never be tighter than a
valid bracket needs.

**Composition (three tiers, cheapest first):** reversal lives *inside* the first sweep attempt (before the
throw and before the coarse retry). If both directions cap out → throw → the existing once-per-run
`ShouldRetryFromCalculatedPoint` re-center (~:470/:1508) remains as the coarser outer tier, unchanged. The
`maxIterations` backstop (~:1469) still bounds everything.

**Pure helper (unit-testable):**
`internal static bool ShouldReverseDirection(int stepOutsThisDir, int effectiveCap, bool bracketFormed, bool hasReversed)`.

**Replay determinism:** counters/decision are read under `SubMeasurementsLock` after
`await Task.WhenAll(AnalysisTasks)` — the same order-independent trend snapshot the walk already relies on.

---

## Options & configuration

| Symbol | Kind | Default | Sites |
|---|---|---|---|
| `MaxBlindStepsPerDirection` | **user option** (int, "steps", 0=disabled, validate ≥0) | 8 | `IAutoFocusOptions.cs` + `AutoFocusOptions.cs` (field + `InitializeOptions` + `ResetDefaults` + property with `SetValueInteger`) + `AutoFocusEngineOptions` (`IAutoFocusEngine.cs`) + `GetOptions` (~:2755) + **UI control** in `Resources/OptionsDataTemplates.xaml` |
| `SymmetricFocusWindowEnabled` | **internal engine flag** (test seam) | true | `AutoFocusEngineOptions` DTO only; hard-wired `true` in `GetOptions`; NO persisted property, NO UI |

- Window half-width reuses existing profile `AutoFocusInitialOffsetSteps` / `AutoFocusStepSize` — **no new
  numeric option**.
- **UI control** for `MaxBlindStepsPerDirection`: append a `RowDefinition` + `ninactrl:UnitTextBox Unit="steps"`
  in `OptionsDataTemplates.xaml` (copy the "Max Outlier Rejections" `UnitTextBox` ~:766-780, swap
  `GreaterZeroRule` for a zero-allowing non-negative rule) + a `*_Tooltip` `TextBlock` resource. Satisfies the
  "every persisted option needs a UI control" invariant.

**Backward-compat invariant:** `MaxBlindStepsPerDirection = 0` ⇒ no counters/reversal, throw reverts to the
original `if (failureCount >= offsetSteps) throw;`. `SymmetricFocusWindowEnabled = false` ⇒ `ApplyFinalSymmetricWindow`
early-returns. Both defaults on + a well-centered run ⇒ still a no-op (window includes all points, no cap hit,
new DTO arrays empty).

---

## Chart visualization — window-excluded points as hollow rings

Reuse the recovery-overlay idiom Fable built for the optimizer (partition into a **separate ItemsSource +
separate overlay series**, no per-point boolean) — deliberately *not* the red-cross "rejected" language.
Reference: `StarDetection/Optimization/DataTemplates.xaml:44-51,90-100`.

Target is **Chart A**, the live/main AF results chart:
- **`AutoFocus/HocusFocusVM.cs`:** add `PlotWindowExcludedFocusPoints : AsyncObservableCollection<ScatterErrorPoint>`
  (mirror `PlotRejectedFocusPoints` decl ~:319 / init ~:133 / clear in `ClearCharts()` ~:365) + gating bool
  `HasWindowExcludedFocusPoints`. Populate in both `MeasurementPointCompleted` (~:775-778) and the final
  re-sync `CompletedNoReport` (~:754-759), from the new `WindowExcludedPoints` DTO members. Use
  `ScatterErrorPoint` (not `ScatterPoint`) so the dimmed error bar renders.
- **`AutoFocus/DataTemplates.xaml`:** add an `oxy:ScatterErrorSeries` overlay in `<oxy:Plot.Series>` (~:162),
  declared *before* the filled `FocusPoints` series so it draws underneath. Style = hollow ring:
  `MarkerFill="Transparent"`, `MarkerStroke={PrimaryBrush via SetAlphaToColorConverter, param 150}`,
  `MarkerType="Circle"`, `MarkerSize="5"`, dimmed error bar (`NotificationErrorBrush` alpha 100). Leave the
  red-cross `PlotRejectedFocusPoints` series (~:177-182) untouched. Add a legend `TextBlock`
  "○ excluded — outside focus window" gated on `HasWindowExcludedFocusPoints` via
  `BooleanToVisibilityCollapsedConverter`. Both converters are NINA-core StaticResources already in scope.

Byte-identical when the window excludes nothing (empty collection ⇒ overlay draws nothing, legend collapses).

---

## Test battery — two tiers (deterministic + realistic)

Closes a known gap: nothing today drives a **successful** full `AutoFocusEngine.Run()` in-process (VM tests
stub `engine.Run` wholesale; `AutoFocusEngineTests` only drive it to failure). The `Run()` default path uses
full-frame `IStarDetection.Detect(...)` and reads only `AverageHFR`/`HFRStdDev`/`DetectedStars` — pixels are
never touched on that path, which makes scripted detection viable.

### Tier A — deterministic scripted battery (the workhorse)
New test doubles (under `Tests/TestDoubles/` and `Tests/AutoFocus/Harness/`):
- **`MovingFocuserMediator`** — state-backed `IFocuserMediator` that actually moves, clamps to `[Min,Max]`,
  and records `MoveHistory` (asserts B's reversal). Add `MediatorBundle.WithMovingFocuser(start,min,max)`.
- **`SweepFrameLedger` + `StampedImagingMediator`** — bind each captured frame to the focuser position **at
  capture time** (via `ConditionalWeakTable<IRenderedImage,int>`), so detection reads the frame's position,
  not a since-moved one (kills the concurrency race). `CaptureImage`/`ToImageData`/`PrepareImage` return
  small substitutes with `Properties` set (64×64, 16-bit, non-bayered) and `OriginalImage=null` (annotation
  early-returns).
- **`ScriptedStarDetection : IHocusFocusStarDetection`** — `Detect(image,...)` looks up `pos =
  ledger.PositionFor(image)`, returns `scenario.Evaluate(pos)` as `AverageHFR`/`HFRStdDev`/`DetectedStars`
  (zero-star ⇒ `AverageHFR=0`, the engine's failure signal). Injected via
  `IPluggableBehaviorSelector<IStarDetection>` (already an engine ctor arg).
- **`DegradationScenario` / `SpuriousSpec`** (seeded records) — deterministic `Evaluate(pos)` returning
  `(hfr, sigma, starCount)` from `|pos−focus|`: baseline HFR from `DefocusModel.HfrAtFocuserPosition`
  (single source of truth), left/right detection cutoffs (zero beyond), a **spurious low-HFR injector**
  (false minimum), star-count drop, σ inflation, asymmetry. Reuse `SyntheticFocusCurveSamples`' seeded-noise
  convention.

Engine constructed via the `AutoFocusEngineTests.Build(...)` shape with `MaxConcurrent=1`, `FramesPerPoint=1`,
`STARHFR`/`HYPERBOLIC`, `Save=false`; `[SetUp]/[TearDown]` resets the process-wide in-progress guard.

**Scenario battery** (Tier A unless marked): `focus=10000`, `stepSize=5`, `offsetSteps=5`, start far unless noted.
| # | Scenario | Asserts | A/B |
|---|---|---|---|
| C0 | Control, knobs off, well-behaved | byte-identical result (window-excluded set empty; same focus) | — |
| C1 | Start far, clean curve | bootstraps, final within tol (smoke test — write first) | — |
| A1 | Lopsided valid far points | far points → **WindowExcluded** not `RejectedPoints`; fit uses window only | A |
| A2 | All points in-window | WindowExcluded empty; identical to no-window | A |
| A3 | Asymmetric spurious low-HFR far side | does NOT lock onto false min; final ≈ true focus; spurious → excluded | A |
| B1 | One-direction cutoff | `MoveHistory` shows cap hit → return to start → other direction; final within tol | B |
| B2 | Bootstrap cap respected | distinct positions ≤ cap bound; no "focuser limit" throw | B |
| B3 | Reversal → success other direction (mirror of B1) | direction symmetry | B |
| F1 | Truly starless everywhere | `Succeeded=false` gracefully, guard released, no crash | safety |
| F2 | Starless only far, stars near | B reversal still finds focus, else graceful fail | B |
| AB1 | Combined worst case (far false-min + cutoff on right, clean left) | B reverses, A excludes false cluster, final ≈ true focus | A+B |
| R1 | **Tier B**: real render, faint field → starless far out | real `StarDetector` ~0 stars far out (confirms F1) | realism |
| R2 | **Tier B**: real render, low SNR → spurious low-HFR far out | real detector yields ≤N spurious low-HFR far out (confirms A3/AB1) | realism |

### Tier B — realism capstones (2–3, `[Category("SlowIntegration")]`)
Reuse `SyntheticCameraTestScene` + `StarFieldCompositor` + `FakeCatalogReader` + the real `StarDetector`
(the `CameraSimulatorCapstoneTests` pattern). Induce degradation via **physical levers** (faint
`LimitingMagnitude`/`Magnitude` → starless; high `SkyBrightnessMagPerArcsec2`/short `ExposureSeconds` → low-SNR
spurious), optionally a thin `DegradingStarDetectionDecorator` wrapping the real detector when a precise
spurious count is needed. Purpose: anchor Tier A's synthetic assumptions to real render+detect physics.

### Fable handoff
Build the **infra** now (mechanical/deterministic): the four doubles, the `DegradationScenario`/`SpuriousSpec`
records, and the C1 smoke test proving `Run()` completes in-process. **Hand to a Fable-model subagent** the
exhaustive scenario enumeration + expected-outcome tuning — exact cutoff steps, the `SpuriousSpec` HFR/onset
that reliably create a false minimum the pre-change engine would lock onto, tolerance bands, and the precise
`MoveHistory` (B) / partition-membership (A) assertions. Clean handoff: infra exposes three deterministic
knobs (cutoff, spurious, asymmetry) and three observables (final position, `MoveHistory`, WindowExcluded-vs-
Rejected partition); Fable sweeps the knob space to earn "high confidence."

---

## Files to modify

**Production:**
- `AutoFocus/AutoFocusEngine.cs` — Behavior A (`ApplyFinalSymmetricWindow` + `IsWithinFocusWindow`/
  `PartitionByFocusWindow`, called at both finalize sites), Behavior B (counters + reversal +
  `ShouldReverseDirection` in `StartBlindFocusPoints`, reversal-aware throw), `WindowExcludedPoints` on
  `AutoFocusRegionState`/`CurveFittingResult`, `GetOptions` wiring.
- `Interfaces/IAutoFocusEngine.cs` — `WindowExcludedPoints` on `AutoFocusRegionResult`,
  `AutoFocusRegionHFR`, `AutoFocusMeasurementPointCompletedEventArgs`; `MaxBlindStepsPerDirection` +
  `SymmetricFocusWindowEnabled` on `AutoFocusEngineOptions`.
- `Interfaces/IAutoFocusOptions.cs` + `AutoFocus/AutoFocusOptions.cs` — `MaxBlindStepsPerDirection` (user
  option, 5-site pattern).
- `Resources/OptionsDataTemplates.xaml` — `MaxBlindStepsPerDirection` UnitTextBox + tooltip (invariant).
- `AutoFocus/HocusFocusVM.cs` — `PlotWindowExcludedFocusPoints` + `HasWindowExcludedFocusPoints` (decl/init/
  clear/populate).
- `AutoFocus/DataTemplates.xaml` — hollow-ring `ScatterErrorSeries` overlay + legend caption.

**Tests:**
- `Tests/TestDoubles/MovingFocuserMediator.cs`, `MediatorBundle.cs` (`WithMovingFocuser`).
- `Tests/AutoFocus/Harness/` — `SweepFrameLedger.cs`, `StampedImagingMediator.cs`, `ScriptedStarDetection.cs`,
  `DegradationScenario.cs`.
- `Tests/AutoFocus/AutoFocusEngineSweepBatteryTests.cs` (Tier A) + new pure-helper tests in
  `AutoFocusEngineTests.cs`.
- `Tests/CameraSimulator/` — 2–3 realism capstones next to `CameraSimulatorCapstoneTests.cs`.

---

## Verification

1. **Unit/battery suite** (Windows `dotnet.exe test` via WSL interop — no dotnet in WSL; `wslpath -w` the sln,
   timeout 600000; note the flaky `SendAsync_WritesOnABackgroundThread` EAT test is unrelated):
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`. Run at milestones + a final gate.
   All Tier-A scenarios green; Tier-B capstones green (may gate behind `SlowIntegration`).
2. **Pure-helper tests**: `IsWithinFocusWindow`, `PartitionByFocusWindow` (lopsided, false-low-star-doesn't-
   shift-center, no-op-when-centered, ≥3 floor), `ShouldReverseDirection`.
3. **Replay regression** (`running-af-bank-validation` skill): replay a saved lopsided/far-from-focus AF-bank
   run with `MaxBlindStepsPerDirection` on vs `0` and `SymmetricFocusWindowEnabled` on vs off — confirm the
   off/inert path is byte-identical and the on path yields a centered curve with balanced arm counts.
4. **In-app NINA validation** (Windows MCP, the "Default" profile's HocusFocus Simulator camera which renders
   focus-dependent frames): set the focuser far from the simulator's `OptimalFocuserPosition`, run autofocus,
   confirm — (a) the sweep reverses when it starts on the wrong/degraded side (log + focuser trajectory),
   (b) the final curve is balanced and converges near optimal, (c) the results chart renders window-excluded
   points as hollow rings with the "○ excluded — outside focus window" caption, distinct from red-cross
   rejects. Full-res capture on this rig needs `PrintWindow(PW_RENDERFULLCONTENT=2)`.

## Sequencing

1. Options plumbing (`MaxBlindStepsPerDirection` user option + UI; internal `SymmetricFocusWindowEnabled`) —
   no behavior change; verify round-trip.
2. `WindowExcludedPoints` DTO/state category (empty for now).
3. Behavior A (`ApplyFinalSymmetricWindow` + helpers) + pure-helper tests.
4. Behavior B (reversal + cap + helper) + pure-helper tests.
5. Chart overlay (VM + XAML).
6. Test harness infra + C1 smoke test.
7. Fable subagent: enumerate + tune the full battery (Tier A) and the realism capstones (Tier B).
8. Replay + in-app validation.
