# Manual Adjustment Panel for a Connected Motorized Tilt Adapter

## Context

The motorized tilt-adapter (ASG EAT) feature can only drive the hardware in two fully-automatic
flows: the wizard's **Auto Run All** calibration sequence, and the Inspector's **Automatic
Adjustment**, which computes a correction from a fitted sensor model. There is **no way to move the
adapter by hand** — the only manual jog UI in the product drives the *simulated* adapter
(`CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs`), not real hardware.

That leaves real gaps: recovering a motor that sits too near 0 or the excursion cap, applying a
correction you worked out yourself, dialing in backfocus, or re-running the tail of an adjustment
that failed partway. Today all of those require the vendor app.

This adds a **collapsed-by-default "Manual adjustment" section** to the Tilt Adapter Wizard's
*Motorized Device Connection* group box, with two modes:

1. **Single move** — pick a corner / side / all on a spatial pad, a direction, an amount; see the
   consequence inline; one click sends it.
2. **Target positions** — type absolute per-motor end positions; the delta is decomposed by the
   existing planner and reviewed in the existing approval modal, exactly as Automatic Adjustment
   does.

Both are raw hardware control: **neither requires a fitted sensor model, a device-linked
calibration, or `CalibrationIsReliable`** — the gates that gate Automatic Adjustment do not apply
here.

## UX spec

Fable produced the full UX spec (layout, all copy, every state, edge cases, concerns). It is at:

`docs/motorized-tilt-manual-adjustment-ux-design.md`

Build the UI from that document — it is the source of truth for wording and layout; this plan
covers architecture, reuse, correctness anchors, and tests.

Shape, in brief:

```
GroupBox "Motorized Device Connection"
  Serial port / Connect / Disconnect / status          (existing)
  HF_TiltDevicePositionsGrid  — live 2x2 motor positions (existing)
  ▸ Manual adjustment   <dim summary line>             (NEW, collapsed by default)
       caution: moves are physical, stored in EEPROM, no automatic undo
       Mode: (•) Single move   ( ) Target positions
       ── Single move ──────────────────────────────────────
       What to move   [TL][Top][TR]     Direction [+][−]
                      [Left][All][Right]  Amount [ 10 ] steps = 18.0 µm
                      [BL][Bottom][BR]
       Preview: [Corner] TR (Motor 1) +20 · BL (Motor 4) −20 steps      tr,20
                TL·M2 512→512    TR·M1 480→500 (+20)
                BL·M4 470→450 (−20)  BR·M3 505→505
                1 move · ~10 s                        [ Send 1 move ]
       ── Target positions ─────────────────────────────────
       2x2 target boxes prefilled from live positions, Δ per corner,
       "Becomes 2 moves · ~20 s", twist panel + per-cell "will reach"
                          [ Reset to current ]  [ Review moves… ]
  Safety Limits …                                      (existing)
```

## Architecture — reuse, don't rebuild

Everything needed already exists. The new VM is a thin composition layer.

| Need | Reuse |
|---|---|
| Move type + per-corner effect | `TiltAdapterDevices/TiltAdapterMove.cs` — `TiltMoveAxis`, `TiltAdapterMove.UnitEffect(axis)` |
| Delta → ordered move list | `TiltAdapterDevices/TiltMovePlanner.cs` — `Plan(...)`, and `Decompose` for the twist term |
| Ordering + non-negative bias | `ITiltMotionController.OrderForMinimalPeakExcursion` |
| Plan + residual + bias preview | `InspectorVM.BuildPlanPreview` — **extract to shared** (below) |
| Approval modal | `TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPrompt.ShowAsync(...)` — unchanged |
| Wire command text | `AsgEat/EatCommands.Format(move)` — **never hand-build the chip** |
| Corner labels | `AsgEat/EatWizardMapping.CornerLabelForWizardScrew(int)` |
| Connection, lease, positions | `TiltDeviceConnectionService`: `Connected`, `Controller`, `CurrentPositions`, `PositionsKnown`, `IsOperationActive`, `CurrentOperationName`, `TryBeginOperation(name)`, `PublishControllerPositions()` |
| Execution | `ITiltMotionController.ExecuteMoveAsync(move, progress, ct)` |
| Positions grid template | `HF_TiltDevicePositionsGrid` in `TiltAdapterWizard/DataTemplates.xaml` |
| Collapsed-expander idiom | `SimulatedTiltAdapterVM.IsInjectionExpanded` + header summary (`CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` ~489) |

### One small extraction

`InspectorVM.BuildPlanPreview` (`AutoFocus/InspectorVM.cs:2492`) is exactly what target mode needs
(plan → order → recompute residual from what will actually be sent → derive `BiasSteps` → rescale
duration → catch `TiltDeviceLimitException` into a blocking preview). Move the body verbatim to a
new `TiltAdapterDevices/TiltDevicePlanPreviewBuilder.cs` (`internal static TiltDevicePlanPreview
Build(...)`) and leave `InspectorVM.BuildPlanPreview` as a one-line forwarder so
`InspectorVMAutomaticAdjustmentTests` keeps compiling untouched. Do not otherwise modify the
Automatic Adjustment path.

### New files

- `TiltAdapterDevices/Manual/TiltAdapterManualAdjustmentVM.cs` — the panel VM (`BaseINPC`, plain
  class, constructed by `TiltAdapterWizardVM`; **not** a MEF export).
- `TiltAdapterDevices/Manual/ManualAdjustmentTarget.cs` — the pad's 9 selectable targets and the
  correctness-anchor mapping table (below).
- `TiltAdapterDevices/TiltDevicePlanPreviewBuilder.cs` — the extraction above.

### Modified files

- `TiltAdapterWizard/TiltAdapterWizardVM.cs` — construct + expose
  `public TiltAdapterManualAdjustmentVM ManualAdjustment { get; }`; dispose/unsubscribe with the VM.
- `TiltAdapterWizard/DataTemplates.xaml` — new keyed `DataTemplate` (e.g.
  `HF_TiltManualAdjustmentPanel`) plus a `<ContentControl Content="{Binding ManualAdjustment}"
  ContentTemplate="{StaticResource HF_TiltManualAdjustmentPanel}" />` inserted in the *Motorized
  Device Connection* group box **between the `HF_TiltDevicePositionsGrid` ContentControl and the
  "Safety Limits" header** (~line 430).
- `AutoFocus/InspectorVM.cs` — forwarder only.

### No new persisted options

Expander state, mode, pad selection, direction, amount and typed targets are **session-scoped VM
state**. This is deliberate: it keeps the project invariant "every persisted option gets a control
in `Resources/OptionsDataTemplates.xaml`" from pulling jog defaults into the global options screen.

## Correctness anchors

**The pad mapping table.** Nine targets, each one axis + sign. This is the file's equivalent of
`TiltAdapterMove`'s unit-effect table — a sign error here is an EEPROM-persisted wrong-way move.
Encode it as a static table in `ManualAdjustmentTarget.cs` and pin it with a table-driven test.

| Pad cell | Axis | Sign for `+` | Wizard screws that get `+amount` |
|---|---|---|---|
| TR | DiagonalA | +1 | s1 (TR); s3 (BL) gets −amount |
| BL | DiagonalA | −1 | s3 (BL); s1 (TR) gets −amount |
| TL | DiagonalB | +1 | s2 (TL); s4 (BR) gets −amount |
| BR | DiagonalB | −1 | s4 (BR); s2 (TL) gets −amount |
| Top | EdgeVertical | +1 | s1, s2 (TR, TL); s3, s4 get −amount |
| Bottom | EdgeVertical | −1 | s3, s4 (BL, BR); s1, s2 get −amount |
| Right | EdgeHorizontal | +1 | s1, s4 (TR, BR); s2, s3 get −amount |
| Left | EdgeHorizontal | −1 | s2, s3 (TL, BL); s1, s4 get −amount |
| All | Backfocus | +1 | all four |

**Three index spaces** (`.claude/docs/tilt-domain.md`): corners TR/TL/BR/BL; device motors
TR=1, TL=2, BR=3, BL=4; wizard screws TR=1, TL=2, **BL=3, BR=4**. `TiltAdapterMove.PerCornerSteps`
and `TiltMovePlanner`'s `sPerScrew` are in **wizard screw** order;
`TiltDeviceConnectionService.CurrentPositions` is in **device motor** order. Convert with
`EatTiltMotionController.PermuteWizardToDeviceMotorOrder` (`[w0, w1, w3, w2]`, *not* identity) —
never by hand. Prefer rendering by corner label via `EatWizardMapping.CornerLabelForWizardScrew`.

**Wire chips come from `EatCommands.Format(move)`.** The default sign encoding is
`SignedArgument`, so a "BL +20" pad click sends `tr,-20`, not `bl,20`. Formatting the chip by hand
from the pad label would print a command that is never sent.

**"Will reach" values come from the plan, not from re-deriving twist.**
`TiltAdapterMovePlan.ResidualMicronsPerCorner[i] = (applied[i] − target[i]) × unitMicrons`, so
`willReach[i] = target[i] + residual[i] / unitMicrons`. That is exact and already folds in twist,
step rounding, *and* any prepended bias. Use `TiltMovePlanner.Decompose(...).t` only for the
"is there twist worth warning about" test (|t| ≥ 1 step).

**Single move sends exactly one generator move** — it goes straight to `ExecuteMoveAsync` (split
into same-axis chunks by `TiltMovePlanner.SplitStepsForCap` when the amount exceeds
`TiltDeviceMaxStepsPerCommand`), never through `OrderForMinimalPeakExcursion`, so no backfocus bias
is ever silently prepended. A move that would carry a motor below 0 is blocked with a reason line
telling the user to send an `All +` move first.

**Travel-window prediction.** Mirror `EatTiltMotionController.ExecuteMoveAsync`'s check
(`AsgEat/EatTiltMotionController.cs:267–293`): predicted = current + permuted delta, window
`[0, TiltDeviceMaxExcursionSteps]`, asymmetric. Note the controller enforces this **even when
positions are unknown** (against the persisted estimate, with a "degraded" note in the message) —
which is why unknown positions are an *advisory* for Single move rather than a block, while Target
mode blocks (an absolute target is meaningless without a known start).

**Modal parameters for target mode.** Call
`TiltDeviceAdjustmentPrompt.ShowAsync(windowServiceFactory, replanner, screwInwardCurvatureSignIsMeasured: true, pitchMismatchWarning: string.Empty, positionsUnknown: false, unitMicrons)`.
The assumed-direction and pitch-mismatch warnings exist because Automatic Adjustment *infers* screw
motion from a measured curvature; in target mode the user commanded steps directly, so nothing is
assumed and firing those warnings would be false. `positionsUnknown` is always false because the
mode is gated on `PositionsKnown`.

**UI-thread rules** (`.claude/docs/mvvm-patterns.md`, memory): `NotifyCanExecuteChanged` must be
raised on the UI thread; use `applicationDispatcher.PostSynchronizationContext` (Post, never a
blocking Dispatch) when republishing from the poll/move path.

**Publish positions after every move** (`.claude/docs/tilt-domain.md`): the 5 s `cp` poll is
suspended for the whole lease, so the panel must call
`TiltDeviceConnectionService.PublishControllerPositions()` after each `ExecuteMoveAsync` — one call
updates every bound surface. Never issue a follow-up `cp`.

## Tasks

1. **Move the UX spec** to `docs/motorized-tilt-manual-adjustment-ux-design.md`. *(done)*
2. **Extract `TiltDevicePlanPreviewBuilder`**; leave `InspectorVM.BuildPlanPreview` as a forwarder.
   Run the suite — `InspectorVMAutomaticAdjustmentTests` must be untouched and green.
3. **`ManualAdjustmentTarget.cs`** — the 9-target table (label, kind, axis, sign, tooltip text) plus
   `IReadOnlyList<double> PerScrewEffect(target, direction, amount)`. Pure, no dependencies.
4. **`TiltAdapterManualAdjustmentVM` — shared + Single move.** Connection/lease/positions state,
   expander + summary line, mode switch, pad/direction/amount, live preview (semantic line, wire
   chip, predicted 2×2 end positions with `near max`/`near 0`/`below 0`/`over max` tags,
   consequence line, legend, count + duration), the disabled-reason table, `SendCommand`.
5. **Execution + status.** Take `TryBeginOperation("Manual Adjustment")`; run split commands
   sequentially with a `CancellationTokenSource` for *Stop after this move* (the token is only
   honored at `ExecuteMoveAsync` entry — there is no device abort); `PublishControllerPositions()`
   after each; progress line `Move k of n — {controller text}`; success / stopped / failed /
   partial-failure result panels per the spec, with force-open-until-dismissed.
6. **Target positions mode.** 2×2 absolute target cells prefilled from live positions (background
   position updates refresh `now`/Δ but never overwrite typed targets), per-corner Δ in steps + µm,
   validation, hint line from the planner (`Becomes N moves · ~T`, nothing-to-do, hard-limit,
   bias-included), twist panel + per-cell "will reach", `Reset to current`, `Review moves…` →
   modal → same executor as task 5.
7. **XAML.** Keyed `DataTemplate` + `ContentControl` insertion. Theme-correct: Panel A renders in
   the active NINA theme (unlike the modal's self-contained palette) — every local
   `TextBlock.Style` must be `BasedOn="{StaticResource StandardTextBlock}"`, and warning colors
   need light-theme-legible brushes. No `CheckBox` (NINA's themed template frequently drops
   `CheckBox.Content`); use `RadioButton`/`ToggleButton` with **sibling** `TextBlock` captions and
   `AutomationProperties.Name` on every control.
8. **Manual documentation.** Add a section to `documentation/docs/overview/motorized-tilt-adapter.md`
   per `.claude/docs/documentation-style.md`.

## Testing

New `Tests/TiltAdapterDevices/Manual/TiltAdapterManualAdjustmentVMTests.cs` (NUnit 4, existing
seams: fake `ITiltMotionController`, `ITiltDeviceTimeSource`, an injectable prompt delegate mirroring
`InspectorVM`'s `showAdjustmentPromptAsync`):

- **Pad mapping table** — all 9 targets × both directions → expected `PerScrewEffect` vector and
  expected `EatCommands.Format` wire string. Table-driven; this is the anchor test.
- Preview predicted end positions in **device motor order** for each target (catches a permutation
  slip); `near max` / `near 0` tags at the 5 % thresholds; `below 0` / `over max` block `Send`.
- Amount > `TiltDeviceMaxStepsPerCommand` splits into same-axis chunks summing to the amount, and
  the button/duration text reflects the count.
- Every row of the disabled-reason table produces its exact reason string; precedence order holds.
- Target mode: delta → expected move list; zero-delta disables Review; out-of-range target blocks;
  `PositionsKnown == false` blocks with the right reason.
- Twist: a target vector with |t| ≥ 1 step raises the twist panel, and `willReach` equals
  `target + residual/unitMicrons` from the plan.
- Execution: lease acquired/released (including on throw); `PublishControllerPositions` called once
  per move; Stop skips remaining moves; a `TiltDeviceCommandFailedException` on move k of n yields
  the partial-failure panel with the right k/n and does **not** auto-revert.
- Regression: existing `InspectorVMAutomaticAdjustmentTests`, `TiltMovePlannerTests`,
  `TiltAdapterMoveTests`, `Tests/TiltAdapterDevices/Prompt/` unchanged and green.

Full suite (memory: no `dotnet` in WSL — use Windows `dotnet.exe` via interop, `wslpath -w` the
solution, timeout 600000). Run at milestones (after tasks 2, 5, 7) and as a final gate, not after
every edit. Note the flaky `SendAsync_WritesOnABackgroundThread` in the EAT serial transport tests
is pre-existing and unrelated.

## Verification (end-to-end)

1. `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — green.
2. NINA → **Tilt Adapter Wizard**, select an ASG EAT preset, Connect to **`Simulator`** (the
   in-process `SimulatedEatTransport` runs the entire real EAT stack unchanged, so this exercises
   the true command path with no hardware):
   - The *Manual adjustment* expander is **collapsed** on first open, with the summary line.
   - Single move: click `TR`, `+`, 20 → preview names Motor 1 and Motor 4, shows both predicted
     positions, and the chip reads `tr,20`; Send moves the live positions grid by exactly ±20.
   - Click `BL`, `+`, 20 → chip reads `tr,-20` (sign encoding), and the move reverses the previous.
   - `All`, `+`, 50 → all four counters +50, consequence line says spacing not tilt.
   - Set Amount above the per-command cap → preview shows the split and `Send N moves`.
   - Drive a motor near 0 → `below 0` tag appears and Send is blocked with the "lift first" reason.
   - Target mode: prefilled from current; edit two corners asymmetrically so the request contains
     twist → twist panel + per-cell "will reach"; `Review moves…` opens the existing modal with the
     decomposed queue and 2×2 residual grid; Proceed executes and the counters land on the "will
     reach" values.
   - Start a wizard run → Panel A (and the panel) disappears; with a run's lease held, both primary
     buttons read `Device busy — …`.
3. Hardware pass on COM7 with the real EAT: one small `All +10`, confirm the counters and the
   physical adapter agree, then `All −10` back.
4. Confirm the Inspector's **Automatic Adjustment** still works unchanged (the extraction in task 2
   is the only thing this plan touches in that path).
