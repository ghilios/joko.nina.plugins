# Tilt Calibration & Sensor Model UX — Design

## Problem

Users complain that Tilt Adapter Calibration and Sensor Model runs take too long. Two duration-extending
features are on by default and buried in the Aberration Inspector's Options expander, invisible from the
wizard entirely:

- **Center Focuser First** (`InspectorOptions.CenterFocuserBeforeRun`, default **on**) — a full extra
  standard autofocus before every live sensor-model sweep. The wizard pays this on every one of its 6
  measurement steps.
- **Signal Amplification** (`InspectorOptions.SignalAmplification`, default **2**) — multiplies the sweep
  point count (offset steps × amp, step size ÷ amp), roughly doubling images per sweep at the default.

In addition, the wizard always runs 6 steps even though the first 2 (Baseline → AllInward) exist only to
measure the curvature/backfocus direction sign; guidance never states a turn direction (only IN/OUT); and
there is no way to enter a known calibration manually.

## Decisions confirmed with the user

1. **Shared settings, two surfaces.** Signal Amplification and Center Focuser First remain single
   `InspectorOptions` values; the wizard binds the same instances (no wizard-specific overrides).
2. **Direction semantics.** Clockwise always drives a screw inward (hardware fact — right-hand thread).
   The rig-specific unknown is whether that inward screw motion moves the sensor plate **toward the
   telescope objective** or **away from it (toward the camera)**. No rotation-direction enum is needed;
   IN ⇔ CW and OUT ⇔ CCW are fixed wording. The plate-direction unknown is exactly what
   `ScrewInwardCurvatureSign` already encodes.
3. **Manual calibration entry** lives in the wizard's pre-run settings panel (collapsed expander), and
   applying it produces the same persisted calibration state the wizard writes (tagged as manual).
4. **Stepper guidance uses signed steps** (`+35 steps` / `−35 steps`), positive = inward, matching the
   convention the wizard prompts establish.

## Feature 1 — Surface the sweep-cost settings

### Behavior changes

- `InspectorOptions.CenterFocuserBeforeRun` default flips **true → false** (both `InitializeOptions()`
  and `ResetDefaults()`). Existing users who never persisted a value will see the centering AF stop
  running — intended.
- `SignalAmplification` default stays 2. No engine behavior changes.

### Aberration Inspector UI (`AutoFocus/DataTemplates.xaml`)

- Remove the "Signal Amplification" and "Center Focuser First" rows from the Options expander (current
  row 7, ~L1767–1799).
- Add a new always-visible block **above the Options expander** (between the buttons row and the
  expander). Two rows, Signal Amplification **first**:
  - Row layout: label + control on the left (same styles as existing option rows: `ninactrl:UnitTextBox`
    for amplification, `CheckBox` for centering), and wrapping explanatory prose to the right
    (`FontStyle="Italic"`, `Opacity="0.7"`, `TextWrapping="Wrap"` — the established prose style).
- **Signal Amplification prose** (dynamic, new computed VM property, e.g.
  `InspectorVM.SignalAmplificationSummary`):
  > "Each auto focus run will capture ~**N** images (**P** focus positions × **F** exposures each). More,
  > finer-spaced points give a steadier fit for weak signal; set to 1 to run a regular autofocus
  > (fastest)."
  Estimate: `P ≈ 2 × offsetSteps × amp + 1`, where `offsetSteps` = `InspectorOptions.StepCount` if > 0
  else the profile's AF initial offset steps, and `F` = `FramesPerPoint` if > 0 else the profile's
  frames-per-point. Labeled approximate ("~") because the AF engine can extend a sweep dynamically.
  Recomputed when `SignalAmplification` / `StepCount` / `FramesPerPoint` / profile change.
- **Center Focuser First prose** (static):
  > "Runs one standard autofocus to center the focuser before each measurement sweep. Turn on if your
  > focuser drifts between runs or starts far from best focus; leave off to save time."

### Tilt Adapter Wizard UI (`TiltAdapterWizard/DataTemplates.xaml`, Panel A)

- New **"Measurement"** bold sub-header after the "Focuser step size" row and before the
  Calibrate/Replay buttons. Rows in order:
  1. **Signal Amplification** (bound to the shared `InspectorOptions.SignalAmplification` via a wizard VM
     passthrough property) with prose to the right, wizard-flavored and dynamic:
     > "Every calibration step runs a full autofocus sweep of ~**N** images. At the current settings this
     > calibration will take **S** sweeps ≈ **S×N** images total. Increase for more signal on faint
     > stars; decrease to run faster (1 = a regular autofocus)."
     `S` = (4 or 6, per Feature 3) × `MeasurementAverageCount`.
  2. **Center Focuser First** (shared `InspectorOptions.CenterFocuserBeforeRun`) with prose:
     > "Adds one standard autofocus before every measurement sweep to re-center focus. Turn on if focus
     > drifts between steps (e.g., temperature) or your focuser starts far from focus."
- The wizard VM exposes `IInspectorOptions InspectorOptions => inspector.InspectorOptions` (or
  equivalent) for binding.

## Feature 2 — Directional screw/stepper guidance (CW/CCW, signed steps)

The numeric guidance pipeline already exists (`InspectorVM.FillNumericGuidance`,
`TiltScrewGeometry.ScrewCorrectionMicrons` / `InwardAdjustment`,
`TiltAdapterGuidanceVM.FormatMagnitude` / `FormatTotal`). Changes are wording plus the assumed-direction
default (Feature 3):

- **Screws:** totals become e.g. `1.25 turns CW (in)` / `0.50 turns CCW (out)`. IN maps to CW
  unconditionally, per the confirmed hardware fact.
- **Steppers:** totals become signed steps: `+35 steps (in)` / `−35 steps (out)`, positive = inward —
  the same "+" the wizard's calibration prompts instruct.
- Per-screw Tilt/Backfocus cells stay magnitude-only (the arrows carry direction). Add a one-line legend
  under the guidance table: "⬆ = inward (clockwise)" (steppers: "⬆ = inward (+ steps)").
- `FormatTotal` (and a new small formatter for the legend) extended and kept as **pure static helpers**
  for unit testing.
- Wizard step instructions and baseline-recovery text gain the fixed rotation words:
  "Turn ALL screws INWARD (clockwise) exactly 1 full turn…", "…back OUT (counter-clockwise)…". For
  stepper adapters the prompts switch to signed-step phrasing ("apply +N steps to every motor…"),
  replacing the current turns-only wording, with N = `CalibrationAppliedAmount`.
- The direction is available whenever calibration is available; because Feature 3 gives
  `ScrewInwardCurvatureSign` an assumed default, totals always carry a direction word, with an
  "(assumed)" caveat where the sign is not measured (see below).

## Feature 3 — Curvature calibration becomes opt-in (4-step wizard by default)

### Settings

New/changed persisted options on `TiltAdapterOptions`:

| Option | Type | Default | Meaning |
|---|---|---|---|
| `MeasureCurvatureDuringCalibration` | bool | **false** | Include the Baseline + AllInward steps (6-step wizard) to measure the direction sign. |
| `ScrewInwardCurvatureSign` | int | **−1** (was 0) | Unchanged storage/semantics: +1 = inward turns raise the curvature effect; −1 = lower it. New default = the user-requested assumption "inward decreases curvature". |
| `ScrewInwardCurvatureSignIsMeasured` | bool | **false** | Provenance: true only when a 6-step wizard run measured the sign. Cleared when the user edits the direction manually or applies manual calibration entry. |

Physical interpretation shown to the user (consistent with `.claude/docs/tilt-domain.md` and the
standard NINA focuser convention of position increasing = drawtube out): sign **−1** ⇔ "turning screws
clockwise (inward) moves the sensor plate **away from the objective** — curvature effect decreases";
sign **+1** ⇔ "…**toward the objective** — curvature effect increases". If a rig's guidance appears
inverted, flipping this setting (or running the 6-step measurement) corrects it.

### Wizard settings UI (Panel A, in the new "Measurement" section after Feature 1's rows)

1. ComboBox **"Turning screws inward (CW)"** with two entries bound to the sign:
   - "Moves sensor away from objective — curvature decreases" (−1, default)
   - "Moves sensor toward objective — curvature increases" (+1)
   For stepper adapters the label reads **"Applying + steps"** instead. Disabled while
   `MeasureCurvatureDuringCalibration` is checked (measurement will overwrite it); after a measured run
   it displays the measured value.
2. CheckBox **"Measure curvature direction during calibration"** with prose to the right:
   > "Adds 2 extra measurement steps (6 instead of 4 — about 50% longer) to determine the direction
   > automatically. Turn on if you don't know how your adapter behaves, or to verify the setting above."

### Wizard sequencing

- When **off** (default): the run performs 4 measurement steps — Baseline → Screw1 → ReBaseline → Screw2
  — by starting the existing linear sequence at `ReBaseline1` (its instruction text shows baseline
  wording in this mode). `TiltCalibrationInputs.Baseline`/`AllInward` become optional; `Calibrate()`
  passes the configured sign through instead of computing it, and `RunCalibrationMath` leaves
  `ScrewInwardCurvatureSign`/`...IsMeasured` untouched.
- When **on**: current 6-step behavior; the measured sign is written and `...IsMeasured = true`.
- Confidence model (`ComputeConfidence`): without the AllInward residual and first re-baseline drift,
  noise is estimated from the remaining re-baseline drift probe alone (1 probe instead of 3); the SNR
  threshold and warnings are unchanged. Angle math, hardware recovery (`RecoverHardwareMicrons`),
  magnitude-ratio and drift checks all use only the c→d / e→f deltas and are unaffected.
- **Metadata/replay/validator:** `TiltCalibrationMetadata` records which steps ran (`RunStepMapping`
  already lists them per step). Replaying an old 6-step run still measures the sign; a 4-step run
  replays as 4 steps. The TestApp validator accepts both.
- The wizard results panel and saved-calibration panel annotate the curvature row with
  "(measured)" / "(assumed)" from `ScrewInwardCurvatureSignIsMeasured`.

## Feature 4 — Manual calibration entry

A collapsed **"Manual Calibration Entry"** expander at the bottom of wizard Panel A (below the
saved-calibration display):

- **Screw 1 position angle (°)** — TextBox, validated; 0° = straight up (12 o'clock) in the *image*,
  increasing clockwise (the plugin-wide convention). Pre-filled from the current
  `Screw1AngleDegrees` when a calibration exists.
- **Screw numbering direction (in the image)** — ComboBox {Clockwise (default), Counter-clockwise}.
  Remaining screws are placed at equal spacing: 3-screw ±120°; 4-screw ±90° with opposite screws 180°
  apart — the same layout `ComputeScrewAngles` fits to.
- The **curvature direction** setting from Feature 3 sits directly above in the same panel and is
  called out in the expander's prose as the companion setting ("set the direction above if you know
  it").
- **Apply** button: writes `Screw1..4AngleDegrees`, `CalibratedScrewCount = ScrewCount`,
  `IsCalibrated = true`, sets new persisted `CalibrationIsManual = true` (wizard runs set it false),
  and clears `ScrewInwardCurvatureSignIsMeasured`. Inspector guidance activates immediately.
- Prose warning (italic style):
  > "Angles and numbering direction are in image space — mirrors or diagonals in the optical train can
  > flip them relative to the physical adapter. If guidance moves tilt the wrong way, flip the numbering
  > direction."
- The saved-calibration panel shows a "Manually entered" tag when `CalibrationIsManual` is true.
- Angle placement implemented as a **pure static helper** (e.g.
  `TiltCalibrationCalculator.ComputeManualScrewAngles(screw1Deg, clockwise, screwCount)`) for unit
  testing.

## Incidental fix

`InspectorOptions.TimeoutSeconds` setter persists under `nameof(StepCount)` (`InspectorOptions.cs:194`),
silently corrupting the persisted step count whenever the timeout is edited. Fix the key to
`nameof(TimeoutSeconds)` (forward-looking only; no migration).

## Options-UI invariant

All new persisted options get visible controls. They live in `TiltAdapterWizard/DataTemplates.xaml`,
following the existing precedent that `TiltAdapterOptions` controls live in the wizard's own template
(not `Resources/OptionsDataTemplates.xaml`). The two relocated inspector settings remain in
`AutoFocus/DataTemplates.xaml`, just outside the Options expander.

## Testing

New/updated NUnit tests (test project links shared sources directly):

- `TiltCalibrationCalculator`: 4-step inputs — sign passthrough, confidence noise fallback (single drift
  probe), unchanged angle/hardware math; 6-step behavior regression.
- `ComputeManualScrewAngles`: 3- and 4-screw, CW and CCW winding, wrap-around normalization.
- `TiltAdapterGuidanceVM.FormatTotal` (+ legend formatter): CW/CCW wording, signed steps, "(assumed)"
  handling, em-dash small-value behavior.
- `InspectorOptions`: `CenterFocuserBeforeRun` default false; `TimeoutSeconds` persists under its own
  key (and no longer clobbers `StepCount`).
- `TiltAdapterOptions`: new option defaults (`ScrewInwardCurvatureSign` = −1, flags false).

Full suite (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`) must pass.

## Documentation deliverables

Update the MkDocs manual (follow `.claude/docs/documentation-style.md`):

- `overview/tilt-adapter-wizard.md` — 4-step default flow, optional 2-step curvature measurement,
  manual entry, new Measurement section, CW/CCW prompt wording.
- `overview/tilt-aberration-inspector.md` — relocated Signal Amplification / Center Focuser First
  (new default), directional guidance wording and legend.
- `overview/sensor-model.md`, `quick-start.md` — cross-references and defaults.

## Alternatives considered

- **Wizard-specific overrides** for amplification/centering — rejected by user (shared settings).
- **A rotation-direction enum (CW/CCW = inward)** — rejected: CW⇒screw-inward is fixed by hardware; the
  only rig-specific sign is the plate direction, already covered by `ScrewInwardCurvatureSign`.
- **Leaving the direction unset until chosen** — rejected: user wants the inward-decreases default;
  the "(assumed)" tag plus the IN/OUT word retained next to CW/CCW keeps a wrong assumption visible and
  recoverable.
- **Separate dialog for manual entry** — rejected in favor of an inline expander (fewer clicks, less
  XAML surface).
- **Up/Down stepper wording** — rejected in favor of signed steps (user choice).

## Out of scope

- Driving stepper motors automatically (the user still applies steps in vendor software).
- Per-screw thread handedness or preset-carried direction data.
- Changing the `SignalAmplification` default (stays 2).
- Migrating historical `StepCount` values corrupted by the `TimeoutSeconds` key bug.
