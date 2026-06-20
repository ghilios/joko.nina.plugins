# Precise Screw-Turn Adjustments for Tilt & Backfocus — Design

## Context

The Aberration Inspector already detects sensor tilt and field curvature and shows the user
**qualitative** screw guidance (arrow glyphs ⬆ ↑ — ↓ ⬇) telling them which way to turn each
tilt-adapter screw. The magnitude is computed but thrown away, because the plugin has no physical
model of the adapter hardware (thread pitch, stepper step size, screw radius). This feature
surfaces **precise, absolute** adjustment amounts — how much to turn each screw, in turns (or
stepper steps) — for both tilt and backfocus, plus a per-screw total.

It is an **extension of an existing subsystem**, not greenfield. Relevant existing pieces:

- `TiltAdapterWizard/TiltAdapterOptions.cs` (+ `Interfaces/ITiltAdapterOptions.cs`) — persisted
  per-profile calibration (screw count, screw angles, curvature sign).
- `TiltAdapterWizard/TiltAdapterWizardVM.cs` — multi-step wizard that turns screws by a known
  amount and measures the tilt-plane change (`CalculateAndSaveAngles`,
  `CalculateAndSaveCurvatureSign`, `ComputeTiltAngleDeg`).
- `AutoFocus/InspectorVM.cs` `RebuildTiltGuidance()` — already computes a per-screw turn
  projection `(2/n)·(−a·sinθ + b·cosθ)` and a backfocus direction from `CurvatureEffectMicrons`,
  but emits only arrows.
- `AutoFocus/TiltScrewGuidanceRow.cs` `TiltAdapterGuidanceVM` — the per-screw guidance output VM.
- `Inspection/SensorParaboloidModel.cs` — the fitted surface, with `TiltAt(x,y)` and
  `CurvatureAt(x,y)` returning **microns** of best-focus deviation (coords in microns from sensor
  center). Available in the inspector via `SensorModel.DisplayedSensorModel`.

## Assessment of the "square root of the effect" question

**No square root is needed.** `SensorParaboloidModel.CurvatureAt(x,y)` returns `Kx·x² + Ky·y²`,
units (1/µm)·µm² = **microns** of axial best-focus deviation — the same unit as physical screw
axial travel (`pitch × turns`). The backfocus turn count is therefore a direct linear conversion:

```
backfocusTurns = CurvatureAt(screwPointMicrons) / pitchMicronsPerTurn
```

The square root only appears in the model's internal display parameter `C = sign(K)·√|K|`, which
re-expresses the curvature **coefficient** `K` (units 1/µm) — not the physical defocus. Because we
evaluate the curvature **term** (already in microns) directly at the screw point, the sqrt drops
out. Sign/direction comes from the sign of the curvature term combined with the calibrated
`ScrewInwardCurvatureSign`, exactly as the existing arrow logic already does.

With isotropic curvature (`Kx == Ky`) every screw is at the same radius, so the backfocus
contribution is identical across screws (a pure uniform translation = true backfocus). With
astigmatic curvature (`Kx ≠ Ky`) it varies slightly per screw angle — that residual can't be
fully removed by a planar move, but the per-screw number is still the correct local target.

## Design

### 1. Configuration options — `ITiltAdapterOptions` / `TiltAdapterOptions`

New persisted, per-profile fields (existing get/set + `optionsAccessor` pattern):

| Field | Type | Meaning / default |
|---|---|---|
| `AdjustmentType` | enum `TiltAdjustmentType { Screws, StepperMotors }` | default `Screws` |
| `ThreadPitchMicrons` | double | µm of axial travel per **full turn** (screws). Default −1 (unset). UI shows mm. |
| `StepperStepSizeMicrons` | double | µm of axial travel per **step** (steppers). Default −1 (unset). |
| `ScrewRadiusMillimeters` | double | screw distance from sensor center. Default −1 (unset). |

UI in `TiltAdapterWizard/DataTemplates.xaml` settings panel: enum `ComboBox` for `AdjustmentType`,
`UnitTextBox` + `DoubleRangeRule` for pitch (mm), step size (µm), screw radius (mm), with tooltips;
pitch-vs-step fields shown conditionally on `AdjustmentType` via a `DataTrigger`.

### 2. Wizard — measure pitch / step size + summary page

- New input: `CalibrationTurnsApplied` (double, default 1.0) / `CalibrationStepsApplied` (int),
  shown on the per-screw measurement steps — the known amount the user actually moved each screw.
- After calibration, compute the measured pitch/step size from each per-screw tilt-plane delta
  `(ΔA, ΔB)`: convert to a physical axial displacement at the screw location via the shared helper
  (focuser step size µm + pixel size µm + image size + screw radius lever arm), then
  `measuredPitchMicrons = axialDisplacementMicrons / appliedAmount`, averaged across screws.
- **Summary page** (extend the Complete panel; modeled on `OptimizationSummary` in the
  star-detection wizard): measured pitch/step vs saved value with a delta/percent, a
  **"Use measured value"** button that writes the measured value into the saved option, and the
  input variables that fed the calculation: pixel size (µm), focuser step size (µm), screw radius
  (mm), applied turns/steps.

### 3. Aberration Inspector — precise numeric guidance

Extend `TiltAdapterGuidanceVM` with per-screw numeric fields (keep the arrow fields — **Numbers +
keep arrows**): `Screw{1..4}TiltAmountText`, `Screw{1..4}BackfocusAmountText`,
`Screw{1..4}TotalAmountText`, a `UnitsAreSteps` flag, and a `PitchMismatchWarning` string.

In `RebuildTiltGuidance()`, when calibrated **and** the relevant hardware option is set, evaluate
the paraboloid model (`SensorModel.DisplayedSensorModel`) in microns at each screw point, then
convert to turns/steps using the **saved** pitch/step size:

- Screw point in sensor microns from angle θ (clockwise from up) + radius: `R_µm = radius_mm·1000`;
  image coords +x right / +y down, up = −y ⇒ `point = (R_µm·sinθ, −R_µm·cosθ)` (verify sign
  against the wizard's existing convention).
- `tiltµm = model.TiltAt(point)` → `tiltTurns = tiltµm / pitch`
- `backµm = model.CurvatureAt(point − (X0,Y0))` → `backTurns = backµm / pitch`
- `totalTurns = tiltTurns + backTurns`
- Tilt direction reuses the existing validated projection sign; backfocus sign reuses
  `ScrewInwardCurvatureSign`.
- Format: screws → `"+0.75 turns CW"` (turns, 2 dp, with direction); steppers → **whole steps
  rounded** `"120 steps IN"`. Arrow computed from the same signed magnitude so arrow and number agree.
- **Pitch mismatch warning:** inspector always uses the saved pitch; if a measured value exists and
  the saved value differs by more than ~15%, set `PitchMismatchWarning`.

Display: extend the guidance table in `AutoFocus/DataTemplates.xaml` (the screw-guidance grid) with
a numeric line under each screw's arrow, a Backfocus numeric row, a Total row, and the warning
banner; gate visibility on the hardware option being set (hint to run/configure the wizard if unset).

### Orientation (must be unit-tested)

Image mirroring means screw winding in image-space ≠ physical-space; the existing code derives
winding from measured data. The new per-screw evaluation reuses the existing angle/sign convention
(`atan2(dA, −dB)`, `(−a·sinθ + b·cosθ)`); a round-trip unit test confirms a synthetic tilt plane of
known gradient produces per-screw turn numbers whose direction matches the existing arrow output.

## Files to modify

- `Interfaces/ITiltAdapterOptions.cs`, `TiltAdapterWizard/TiltAdapterOptions.cs` — new options + enum.
- `TiltAdapterWizard/TiltAdapterWizardVM.cs` — applied amount, measured pitch/step, summary props +
  "Use measured value" command.
- `TiltAdapterWizard/DataTemplates.xaml` — config UI + applied-amount input + summary panel.
- `AutoFocus/TiltScrewGuidanceRow.cs` — numeric + total + warning + units fields.
- `AutoFocus/InspectorVM.cs` — numeric computation in `RebuildTiltGuidance()` + mismatch warning.
- `AutoFocus/DataTemplates.xaml` — numeric/total rows + warning banner.
- Shared helper (new, e.g. `Utility` or `TiltAdapterWizard`) — tilt-plane gradient ↔ physical axial
  displacement at screw, used by both wizard calibration and inspector application (exact inverses).

## Testing

NUnit (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug`):

- `TiltAdapterOptionsTests` — persistence/defaults of the new options.
- Tilt-guidance tests: pure tilt → expected per-screw turns, direction agrees with arrows; pure
  isotropic curvature → equal backfocus turns, linear in K (no sqrt); astigmatic → per-screw varies;
  total = tilt + backfocus; stepper rounding vs fractional turns; mismatch warning threshold.
- Wizard pitch-calibration test: synthetic known screw move → recovered pitch matches input (inverse
  of the inspector conversion).

## Verification (end-to-end)

1. `dotnet build` then `dotnet test … -c Debug --nologo` — all green.
2. NINA: Tilt Adapter Wizard — set adjustment type / pitch / radius, run/replay a calibration,
   confirm summary shows measured-vs-saved pitch + variables and "Use measured value" updates it.
3. NINA: Aberration Inspector on a run with known tilt/curvature — confirm per-screw tilt turns,
   backfocus turns, totals (steps in stepper mode), arrows still present and agreeing, and that an
   artificially mismatched saved pitch raises the warning.

## Risks

- The exact lever-arm/geometry constant (3-screw pivot vs 4-screw coupled pairs) is pinned by the
  round-trip test; wizard calibration and inspector application share one helper so they are exact
  inverses.
- Sign conventions (image mirroring) are the main risk — covered by the orientation round-trip test
  against the already-validated arrow logic.
