# Focuser step size from the driver — Implementation Plan

Executes `docs/focuser-step-size-driver-design.md`. Steps are ordered; each ends green
(`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`).

**Persisted options touched: none.** `DriverMicronsPerFocuserStep` is in-memory device state and must never
reach the options accessor, so the "every new option needs a control in `Resources/OptionsDataTemplates.xaml`"
invariant does not apply to it. The two *display* changes (hint text, mismatch flag) land in the existing
Focuser Step Size controls.

**The one-line trap, repeated because it is the whole correctness of the resolver:** the acceptance test is
`value > 0`, never `!(value <= 0)`. `NaN` fails both comparisons; only the positive form rejects it. A `NaN`
step size that slips through produces an all-NaN sensor model with no error anywhere.

---

## Step 1 — The resolver on `IInspectorOptions` / `InspectorOptions`

**Files:** `Interfaces/IInspectorOptions.cs`, `AutoFocus/InspectorOptions.cs`

Add beside `MicronsPerFocuserStep`:

- `double DriverMicronsPerFocuserStep { get; set; }` — doc comment states: in-memory only, never persisted,
  written solely by `InspectorVM.UpdateDeviceInfo`, sticky because only valid values are accepted, cleared on
  profile change. The setter must accept only finite `> 0` values *and* silently ignore anything else, so
  stickiness is a property of the setter rather than of every caller.
- `double EffectiveMicronsPerFocuserStep { get; }` — `MicronsPerFocuserStep > 0 ? MicronsPerFocuserStep :
  (DriverMicronsPerFocuserStep > 0 ? DriverMicronsPerFocuserStep : -1)`.
- `bool HasFocuserStepSizeMismatch { get; }` — override `> 0` **and** driver `> 0` **and**
  `Math.Abs(override - driver) / driver > 0.01`.
- `const double FocuserStepSizeMismatchFraction = 0.01` on `InspectorOptions`, so the test and the tooltip
  quote one number.

Both new derived properties must be re-raised whenever either input changes: `MicronsPerFocuserStep`'s setter
and `DriverMicronsPerFocuserStep`'s setter each raise `EffectiveMicronsPerFocuserStep` and
`HasFocuserStepSizeMismatch` as well as themselves. `InitializeOptions` clears
`DriverMicronsPerFocuserStep` (it runs on construction *and* `ProfileChanged` — that is what implements the
profile-change clear).

`ResetDefaults` leaves the driver value alone: it is not a default, it is device state.

**Tests** (`Tests/AutoFocus/InspectorOptionsTests.cs`):
- precedence: override wins over driver; driver used when override unset; `-1` when neither.
- `0`, `-5`, `NaN`, `PositiveInfinity` are never adopted as the driver value.
- the driver value never appears in `store.Snapshot`.
- stickiness: set a valid driver value, then push `0` → the valid value stands.
- profile change clears it (raise `ProfileChanged` on the substitute).
- mismatch matrix: (1.00, 1.005) false; (1.00, 2.00) true; (unset, 1.00) false; (1.00, 0) false;
  (1.00, NaN) false.
- `PropertyChanged` for `EffectiveMicronsPerFocuserStep` / `HasFocuserStepSizeMismatch` fires from both setters.

## Step 2 — `InspectorVM` writes the driver value

**File:** `AutoFocus/InspectorVM.cs`

`UpdateDeviceInfo(FocuserInfo)` (:2949) additionally assigns
`InspectorOptions.DriverMicronsPerFocuserStep = deviceInfo.StepSize`. The setter's guard does the filtering, so
this is unconditional here — do **not** add a second guard at the call site, or the two will drift.

`InspectorVM` is a Shared MEF singleton already registered as a focuser consumer (:210), so this is the single
writer. Nothing else may write it.

**Tests** (`Tests/AutoFocus/InspectorVMBehavioralTests.cs` or a new focuser-step-size fixture): pushing a
`FocuserInfo { StepSize = 3.5 }` sets the driver value; a subsequent `StepSize = 0` (disconnect) leaves 3.5.

## Step 3 — Consumers read `Effective`

- `AutoFocus/InspectorVM.cs:662` — `SensorModelFocuserSizeOverrideMicrons ?? InspectorOptions.EffectiveMicronsPerFocuserStep`
  (the per-run override stays layer 1).
- `AutoFocus/InspectorVM.cs:1681–1682` — `BackfocusMicronDelta`.
- `AutoFocus/TiltModel.cs:203`.
- `CameraSimulator/CameraSimulatorOptions.cs:347–354` — `EffectiveFocuserStepSizeMicrons` resolves
  `inspectorOptions.EffectiveMicronsPerFocuserStep` first, falling back to `DefaultFocuserStepSizeMicrons`
  only when that is non-positive. Its `InspectorOptions_PropertyChanged` (:96) must also re-raise on
  `EffectiveMicronsPerFocuserStep` (a driver change moves the render scale without touching
  `MicronsPerFocuserStep`).
- `TiltAdapterWizard/TiltAdapterWizardVM.cs:2145` and `:2179` — captured metadata and drift comparison.
- `TestApp/InspectAlignRunner.cs:148`, `TestApp/BankVerifyRunner.cs:514`.

**Do NOT touch** (these are the override itself): `InspectorOptions`' load/store/reset,
`CameraSimulatorOptions.FocuserStepSizeMicrons` (:341–344), the legacy migration (:212–216),
`TiltAdapterWizardVM.MicronsPerFocuserStepValue` (:2020/:2025), and the two-way XAML bindings.

**Tests:** the simulator's inject⇄recover loop closes with `k` supplied by the driver and no override
(extend an existing `SimulatedTiltAdapterVMTests` convergence case, or add one); `CameraSimulatorOptions`
resolves driver → default in the right order.

## Step 4 — UI: hint text and the mismatch flag

**Files:** `AutoFocus/DataTemplates.xaml` (Inspector dock, ~:1787–1814),
`Resources/OptionsDataTemplates.xaml` (Camera Simulator tab, ~:3216–3240)

- Hint text: bind to the effective value (`InspectorOptions.EffectiveMicronsPerFocuserStep` /
  `CameraSimulatorOptions.EffectiveFocuserStepSizeMicrons`, both `Mode=OneWay`) instead of `(disabled)` / the
  hardcoded default. Keep the existing `HF_DoubleNegativeToEmptyStringConverter` on the two-way `Text`
  binding — the box still edits the raw override.
- Mismatch flag: a `TextBlock` beside each box, `Visibility` bound to `HasFocuserStepSizeMismatch` via
  `BooleanToVisibilityCollapsedConverter`, text naming both numbers. Style it as a warning consistent with the
  existing warning text in each file (do not invent a new visual treatment).
- Update `MicronsPerFocuserStep_Tooltip` (`AutoFocus/DataTemplates.xaml:34`) and
  `CamSim_FocuserStepSizeMicrons_Tooltip` (`Resources/OptionsDataTemplates.xaml`) to state that the driver's
  value is used when the box is empty, and that a value here overrides it.

## Step 5 — Documentation

- `documentation/docs/overview/tilt-aberration-inspector.md` — the **Focuser Step Size** options-table row
  becomes "-1 (auto): uses the focuser driver's reported step size; set a value to override it", plus a
  sentence on the mismatch flag and why a driver value can be wrong (steps-not-microns).
- Check `documentation/docs/` for any other page that tells the user to set this by hand.
- `mkdocs build --strict`.

## Step 6 — Verification

1. Full suite green.
2. `mkdocs build --strict`.
3. Manual: connect a focuser whose driver reports a step size, confirm the empty box hints that value and
   micron readouts appear; type a 2× different override and confirm the flag appears and the numbers follow
   the override; clear the box and confirm it returns to the driver's value.
