# Design: Tilt Guidance Turn-Units Dropdown + Camera Sim Rotator Angle

Two independent features requested together. They touch different subsystems and share no
code, so they can be implemented, tested, and committed separately. This doc captures both.

- **Feature 1** — Add a Turns/Degrees unit dropdown to the Tilt Adapter Guidance panel on the
  Aberration Inspector (screw adapters only).
- **Feature 2** — When a rotator is connected, drive the Camera Simulator's field rotation from
  the rotator's mechanical angle.

---

## Feature 1 — Turn-units dropdown (Turns / Degrees) in Tilt Adapter Guidance

### Goal

The Tilt Adapter Guidance table currently shows each screw's adjustment as **turns**
(e.g. `1.25 ⟳`). Add a dropdown in that panel to switch the numeric display between **Turns**
(default, current behavior) and **Degrees**, rounding degree values to the nearest whole degree
(e.g. `45° ⟳`). The dropdown is shown only for **screw** adapters; stepper adapters continue to
display whole steps and have no dropdown. The chosen unit **persists** across NINA restarts.

### Background (current code)

- Turns are computed in `AutoFocus/InspectorVM.cs → FillNumericGuidance(...)`: axial best-focus
  microns ÷ thread-pitch µm-per-turn = turns, then formatted to a display string by
  `TiltAdapterGuidanceVM.FormatAmount(double signedAmount, bool steps)`.
- `TiltAdapterGuidanceVM` (`AutoFocus/TiltScrewGuidanceRow.cs`) is a plain POCO holding
  already-formatted strings (`Screw1TiltAmount` … `Screw4TotalAmount`). The raw numeric turn
  value is **discarded** after formatting. The whole object is swapped and
  `RaisePropertyChanged(nameof(TiltGuidance))` fires; it raises no per-property notifications.
- `FormatAmount` current behavior:
  - steppers: signed whole steps `"+35 steps"` / `"−35 steps"`, `0` → `—`.
  - screws: `"{|turns|:0.00} {⟳ / ⟲}"`, and `|turns| < 0.005` → `—` (noise floor).
- `BuildDirectionLegend(bool steps, bool signIsMeasured)` produces the legend line; the screw
  variant ends with `amounts in turns`.
- `1 turn = 360°`, so degrees is a pure presentation transform (`degrees = turns × 360`). No
  geometry/math changes.
- Adapter type is `ITiltAdapterOptions.AdjustmentType` (`TiltAdjustmentType.Screws` /
  `StepperMotors`). `InspectorVM` derives `bool steps` from it; `TiltAdapterGuidanceVM.UnitsAreSteps`
  mirrors it.
- The guidance panel XAML lives in `AutoFocus/DataTemplates.xaml` under the
  `Expander Header="Tilt Adapter Guidance"`. Cells bind to `TiltGuidance.*` via the `IconizedText`
  attached property, which swaps the `⟳`/`⟲` sentinels for vector icons; any other char (including
  `°`) passes through as plain text, so **no new converter is needed**.
- `InspectorVM` already subscribes `tiltAdapterOptions.PropertyChanged += (s,e) => RebuildTiltGuidance()`
  (`InspectorVM.cs:216`), so writing a new option on `ITiltAdapterOptions` auto-rebuilds the guidance.
- Tilt-adapter options are edited in the wizard / inspector UI, **not** the main Options tab (e.g.
  `AdjustmentType` appears only in `TiltAdapterWizard/DataTemplates.xaml`), so the guidance-panel
  dropdown *is* this option's UI control — satisfying the options-UI invariant.

### Design

**New enum** `TiltGuidanceAngleUnit { Turns = 0, Degrees = 1 }`, placed next to `TiltAdjustmentType`
in `Interfaces/ITiltAdapterOptions.cs`, each value carrying a `[Description]` for the ComboBox
(`"Turns"`, `"Degrees"`).

**New persisted option** `TiltGuidanceAngleUnit AngleDisplayUnit { get; set; }` on
`ITiltAdapterOptions` and its impl `TiltAdapterWizard/TiltAdapterOptions.cs`:
- Backed by `optionsAccessor.GetValueEnum(nameof(AngleDisplayUnit), TiltGuidanceAngleUnit.Turns)`
  in `InitializeOptions`, and `SetValueEnum` in the setter (mirror `AdjustmentType`).
- Default `Turns`.

**Rendering** (`TiltAdapterGuidanceVM`, `TiltScrewGuidanceRow.cs`):
- `FormatAmount` gains a `TiltGuidanceAngleUnit angleUnit` parameter (new required arg; update all
  call sites). Steppers ignore it (always whole steps). Screws:
  - `Turns`: unchanged — `"{|turns|:0.00} {⟳/⟲}"`.
  - `Degrees`: keep the **same physical noise floor** `|turns| < 0.005` → `—`; otherwise
    `degrees = |turns| × 360`, rounded to the nearest whole degree
    (`Math.Round(degrees, MidpointRounding.AwayFromZero)`), rendered `"{n}° {⟳/⟲}"`.
    - Consequence: the smallest non-dash value shown in Degrees is ~2° (0.005 turns = 1.8°). This
      is intentional — the noise floor is a physical quantity and stays consistent between modes.
- `BuildDirectionLegend` gains a `TiltGuidanceAngleUnit angleUnit` parameter (steppers unchanged);
  the screw legend ends with `amounts in degrees` when `Degrees`, else `amounts in turns`.

**InspectorVM** (`FillNumericGuidance` and the `BuildDirectionLegend` call site in
`RebuildTiltGuidance`): read `tiltAdapterOptions.AngleDisplayUnit` and thread it into `FormatAmount`
and `BuildDirectionLegend`. No geometry changes.

**Passthrough property for binding**: add
`public TiltGuidanceAngleUnit TiltGuidanceAngleUnit { get => tiltAdapterOptions.AngleDisplayUnit;
set { tiltAdapterOptions.AngleDisplayUnit = value; } }` on `InspectorVM` (guard the null
`tiltAdapterOptions` case as the existing guidance code does). Writing it flows through
`tiltAdapterOptions` → `PropertyChanged` → `RebuildTiltGuidance()`, which regenerates the strings;
the setter itself needs no explicit `RaisePropertyChanged` for the rebuild, but should raise its own
so the ComboBox reflects the value.

**UI** (`AutoFocus/DataTemplates.xaml`, Tilt Adapter Guidance expander): a small labeled `ComboBox`
near the direction legend, using the existing `util:EnumBindingSource` +
`HF_EnumStaticDescriptionValueConverter` pattern already used in this file, two-way bound to
`TiltGuidance`'s host VM property `TiltGuidanceAngleUnit`. Visibility bound so it appears only for
screw adapters with numeric guidance: visible when `HasNumericGuidance && !UnitsAreSteps` (reuse the
existing `BooleanToVisibilityCollapsedConverter`; combine the two bools via the established approach
in this file — a single exposed bool on the guidance VM if a MultiBinding proves awkward, matching
the `HasFourScrewBackfocus` precedent).

### Tests

Extend `Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`:
- `FormatAmount` in Degrees: rounds to nearest degree, applies the `⟳/⟲` glyph by sign, uses the
  `|turns| < 0.005` noise floor (→ `—`), and steppers ignore the unit (still whole steps).
- `BuildDirectionLegend` in Degrees: screw legend says `amounts in degrees`; steppers unchanged.

### Out of scope

- No change to the motion-arrow grid (⬆/⬇) — it is not a numeric amount.
- No degrees mode for steppers.
- No entry added to the main Options tab (`Resources/OptionsDataTemplates.xaml`); the dropdown is
  the option's UI, consistent with the other tilt-adapter options.

---

## Feature 2 — Camera Sim uses rotator mechanical angle (offset)

### Goal

When a rotator is connected in NINA, the Camera Simulator should render the star field at the
rotator's **mechanical angle**. When no rotator is connected, behavior is unchanged (the manual
"Field Rotation" option drives rendering). When a rotator is connected, the manual "Field Rotation"
value acts as an additive **offset** (a zero-point/calibration nudge):
`effective rotation = rotator.MechanicalPosition + options.RotationDegrees`.

### Background (current code)

- The sim already has a complete field-rotation path: `CameraSimulatorOptions.RotationDegrees` →
  `RenderRequest.RotationDegrees` (set in `HocusFocusSimulatorCamera.BuildRenderRequest`,
  `HocusFocusSimulatorCamera.cs:678`) → `TanProjection` rotation about the image center. **No pixel
  math changes are needed.**
- `BuildRenderRequest` already resolves live device state the same way this feature needs: it calls
  `focuserMediator.GetInfo()` / `telescopeMediator.GetInfo()` and reads `.Connected` + values.
- The plugin has **no rotator usage today** — `IRotatorMediator` is a new dependency. NINA exports
  it as a standard mediator (`NINA.Equipment.Interfaces.Mediator`), and `RotatorInfo` derives from
  `DeviceInfo` (so `.Connected` is present) and exposes `MechanicalPosition` (degrees).
- Sim construction: `HocusFocusSimulatorCameraProvider` (`[ImportingConstructor]`) imports
  `ITelescopeMediator` + `IFocuserMediator` and forwards them into `HocusFocusSimulatorCamera`
  (which has a public production ctor and an `internal` test ctor taking an `IStarFieldCompositor`).
- `TiltAngleDegrees` (sensor-tilt azimuth) is separate from `RotationDegrees` and is **not** changed
  by this feature.

### Design

**Injection**: add `IRotatorMediator rotatorMediator` to:
- `HocusFocusSimulatorCameraProvider`'s `[ImportingConstructor]` (beside the telescope/focuser
  imports), stored in a field, and forwarded in `GetEquipment()`.
- Both `HocusFocusSimulatorCamera` constructors (public production + `internal` test), stored in a
  field, mirroring `focuserMediator`/`telescopeMediator` (including the `ArgumentNullException`
  guard).

**Render request** (`BuildRenderRequest`): alongside the existing focuser/telescope `GetInfo()`
calls, add:
```csharp
var rotatorInfo = rotatorMediator.GetInfo();
var rotatorConnected = rotatorInfo?.Connected ?? false;
double rotationDegrees = rotatorConnected
    ? rotatorInfo.MechanicalPosition + options.RotationDegrees   // rotator drives; manual = offset
    : options.RotationDegrees;                                   // unchanged fallback
```
and set `RotationDegrees = rotationDegrees` in the returned `RenderRequest` (replacing the current
`RotationDegrees = options.RotationDegrees`).

- **Sign/convention check (implementation-time):** verify the rendered field turns the correct way
  as `MechanicalPosition` increases, against `TanProjection`'s convention ("positive rotationDegrees
  rotates the (East, North) axes CCW into (x, up)"). The manual offset absorbs any fixed zero-point,
  but the *rotation sense* must match — negate `MechanicalPosition` if the field turns the wrong way.
- Only `RotationDegrees` changes. `TiltAngleDegrees` stays fixed, which correctly models the
  camera+sensor assembly rotating together: the star field rotates relative to a sensor whose tilt
  is fixed to it.

**UI** (`Resources/OptionsDataTemplates.xaml`): update the existing "Field Rotation" tooltip to note
that when a rotator is connected, this value is **added to** the rotator's mechanical angle (offset).
No new controls.

### Tests

Add a sim test (extend the existing Camera Simulator test that exercises the exposure path with a
fake `IStarFieldCompositor` capturing the `RenderRequest`; follow the existing setup for
connected focuser/mount):
- Mock rotator reports `Connected = true`, `MechanicalPosition = R`; assert captured
  `RenderRequest.RotationDegrees == R + options.RotationDegrees`.
- Mock rotator reports `Connected = false`; assert `RenderRequest.RotationDegrees == options.RotationDegrees`.

### Out of scope

- No use of the rotator's **sky** `Position` (the request is for the mechanical angle).
- No change to `TiltAngleDegrees` or any PSF/aberration/projection math.
- No new persisted option (the manual `RotationDegrees` already exists and becomes the offset).

---

## Verification (both features)

Run the full unit suite after each feature:
```
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```
(In this environment, invoke Windows `dotnet.exe test` via WSL interop.) Fix any failure at its
cause; never skip/ignore tests.
