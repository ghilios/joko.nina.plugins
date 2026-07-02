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

## Terminology

Two different motions get called "in/out"; this spec (and all new UI text) keeps them distinct:

- **Screw motion**: clockwise (CW) always advances/tightens a screw — a fixed hardware fact, never a
  setting. UI text says "clockwise/counter-clockwise (tighten/loosen)", never "in/out", for this.
- **Adapter motion**: *inward* = the adapter's moving plate travels **toward the telescope objective**
  (away from the camera); *outward* = toward the camera. Whether a CW screw turn produces inward or
  outward adapter motion depends on the adapter design (push vs pull screws, spring loading) — **this
  is the rig-specific configuration that needs a setting.**

The existing wizard prompts use "INWARD" in the screw-motion sense; they will be reworded to
"clockwise (tighten)" to remove the ambiguity.

## Decisions confirmed with the user

1. **Shared settings, two surfaces.** Signal Amplification and Center Focuser First remain single
   `InspectorOptions` values; the wizard binds the same instances (no wizard-specific overrides).
2. **Direction semantics.** CW ⇒ screw advances is fixed; the setting captures whether that moves the
   **adapter** inward (toward objective) or outward (toward camera). Guidance states rotation as
   CW/CCW (always valid); adapter in/out wording is derived from the setting.
3. **Manual calibration entry** lives in the wizard's pre-run settings panel (collapsed expander), and
   applying it produces the same persisted calibration state the wizard writes (tagged as manual).
4. **Stepper guidance uses signed steps** (`+35 steps` / `−35 steps`), where "+" is the step direction
   the wizard's calibration prompts establish.

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
`TiltAdapterGuidanceVM.FormatMagnitude` / `FormatTotal`). Internally, a positive `InwardAdjustment`
means "turn the screw in the direction the calibration prompts used" — i.e. CW for screws — so the
rotation word is a fixed mapping; only adapter in/out wording depends on the Feature 3 setting:

- **Screws:** totals become e.g. `1.25 turns CW` / `0.50 turns CCW`.
- **Steppers:** totals become signed steps: `+35 steps` / `−35 steps`, with the same "+" the wizard's
  calibration prompts instruct.
- Per-screw Tilt/Backfocus cells stay magnitude-only (the arrows carry direction). Add a one-line
  legend under the guidance table stating both conventions, with the adapter direction derived from
  the Feature 3 setting: e.g. "⬆ = clockwise (adapter moves toward camera)" — steppers:
  "⬆ = + steps (adapter moves toward camera)".
- The tilt component's direction comes from the measured screw angles and is independent of the
  Feature 3 setting; only the backfocus/total components depend on it. When the setting is assumed
  rather than measured (Feature 3), the guidance shows an "(assumed direction)" annotation.
- `FormatTotal` (and a new small formatter for the legend) extended and kept as **pure static helpers**
  for unit testing.
- Wizard step instructions and baseline-recovery text are reworded from the ambiguous "INWARD/OUT" to
  screw-motion terms: "Turn ALL screws CLOCKWISE (tighten) exactly 1 full turn…", "…back
  COUNTER-CLOCKWISE (loosen)…". For stepper adapters the prompts switch to signed-step phrasing
  ("apply +N steps to every motor…"), replacing the current turns-only wording, with
  N = `CalibrationAppliedAmount`.

## Feature 3 — Curvature calibration becomes opt-in (4-step wizard by default)

### Settings

New/changed persisted options on `TiltAdapterOptions`:

| Option | Type | Default | Meaning |
|---|---|---|---|
| `MeasureCurvatureDuringCalibration` | bool | **false** | Include the Baseline + AllInward steps (6-step wizard) to measure the adapter direction. |
| `ScrewInwardCurvatureSign` | int | **non-zero default** (was 0) | Unchanged storage/semantics: the sign of the focus/curvature response to a CW ("prompt-direction") screw turn, exactly what `ComputeCurvatureSign` measures. Now derived from the mechanical setting below when not measured. |
| `ScrewInwardCurvatureSignIsMeasured` | bool | **false** | Provenance: true only when a 6-step wizard run measured the sign. Cleared when the user edits the direction manually or applies manual calibration entry. |

The **user-facing setting is mechanical**, per the confirmed framing: does a CW screw turn move the
adapter inward (toward objective) or outward (toward camera)? It is presented as a two-entry ComboBox
and stored via `ScrewInwardCurvatureSign` through a **fixed mapping constant**.

**Empirical anchor (user measurement, 2026-07-02):** moving the adapter toward the objective
**decreases** the curvature effect. Combined with the sign's consumer semantics (+1 = a CW turn raises
the curvature effect), the constant is pinned: CW-toward-objective ⇔ sign **−1**
(`TiltScrewGeometry.CurvatureSignWhenCwMovesAdapterTowardObjective = -1`).

**Default:** CW ⇒ adapter moves **outward (toward the camera)** — consistent with
`.claude/docs/tilt-domain.md` ("turning a screw inward pushes that corner of the sensor away from the
telescope"). With the anchor above, the default stored sign is **+1**, whose composed behavior is
exactly the originally requested default: adapter motion inward (toward the objective) decreases the
curvature effect. An optional implementation-time cross-check compares a wizard-measured rig's sign
arrow (↑/↓) against its known mechanical direction; a contradiction would indicate the measured and
manual producers of the sign disagree and must be surfaced rather than papered over. If a rig's
backfocus guidance appears inverted, flipping this setting (or running the 6-step measurement)
corrects it.

### Wizard settings UI (Panel A, in the new "Measurement" section after Feature 1's rows)

1. ComboBox **"Turning screws clockwise moves the adapter"** with two entries:
   - "Toward the camera — outward" (default)
   - "Toward the objective — inward"
   For stepper adapters the label reads **"Applying + steps moves the adapter"** instead. Disabled
   while `MeasureCurvatureDuringCalibration` is checked (measurement will overwrite it); after a
   measured run it displays the value inferred from the measurement (the measured sign back-fills this
   setting through the same fixed mapping).
2. CheckBox **"Measure adapter direction during calibration"** with prose to the right:
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
- The wizard results panel and saved-calibration panel show the adapter direction row with a
  "(measured)" / "(assumed)" annotation from `ScrewInwardCurvatureSignIsMeasured`.

## Feature 4 — Manual calibration entry

A collapsed **"Manual Calibration Entry"** expander at the bottom of wizard Panel A (below the
saved-calibration display):

- **Screw 1 position angle (°)** — TextBox, validated; 0° = straight up (12 o'clock) in the *image*,
  increasing clockwise (the plugin-wide convention). Pre-filled from the current
  `Screw1AngleDegrees` when a calibration exists.
- **Screw numbering direction (in the image)** — ComboBox {Clockwise (default), Counter-clockwise}.
  Remaining screws are placed at equal spacing: 3-screw ±120°; 4-screw ±90° with opposite screws 180°
  apart — the same layout `ComputeScrewAngles` fits to.
- The **adapter direction** setting from Feature 3 sits directly above in the same panel and is
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
- `TiltAdapterOptions`: new option defaults (non-zero `ScrewInwardCurvatureSign`, flags false); the
  mechanical-setting ↔ stored-sign mapping constant round-trips (one test pinned to the empirically
  verified value).

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
- **Leaving the direction unset until chosen** — rejected: a sensible mechanical default plus the
  "(assumed direction)" annotation keeps a wrong assumption visible and recoverable, and the feature
  works out of the box.
- **Phrasing the setting through curvature ("inward decreases curvature")** — rejected after review:
  it conflates the mechanical rig fact (CW ⇒ adapter in/out, which the user knows) with the optical
  response (which the code measures); the mechanical phrasing is the one users can answer.
- **Separate dialog for manual entry** — rejected in favor of an inline expander (fewer clicks, less
  XAML surface).
- **Up/Down stepper wording** — rejected in favor of signed steps (user choice).

## Out of scope

- Driving stepper motors automatically (the user still applies steps in vendor software).
- Per-screw thread handedness or preset-carried direction data.
- Changing the `SignalAmplification` default (stays 2).
- Migrating historical `StepCount` values corrupted by the `TimeoutSeconds` key bug.
