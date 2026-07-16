# Camera Simulator — Rig/Observing Split & Virtual Tilt Adapter — Design

Extends the synthetic camera (`docs/synthetic-camera-design.md`). The panel's interaction detail lives in the
companion **`docs/virtual-tilt-adapter-panel-ux-design.md`**; this doc is the what/why/architecture.

## Problem & goal

The synthetic camera can *inject* a known tilt/backfocus, and the Aberration Inspector can *recover* it — the
automated capstone already proves that loop headlessly. What's missing is the **interactive** loop, the one a
human actually does at a real telescope:

> inject a tilt → run the Aberration Inspector → it says *"turn screw 2 by −0.5 turns"* → **turn that screw** →
> re-run → confirm the tilt went to zero.

Today that requires a telescope, a tilted sensor, and a screwdriver. This adds a **virtual tilt adapter** so the
whole calibration/adjustment workflow — including the Tilt Adapter Wizard and the inspector's screw guidance —
can be exercised from a desk. Two supporting changes make that usable: splitting the simulator's options into
*rig* (set once, on the camera) vs *observing* (changed constantly), and surfacing the adapter panel next to the
inspector while calibrating.

**Success criterion:** a user can close the inspector→screw→inspector loop entirely in software, and the loop
converges to ≈0 tilt — because the simulator's screw math is the *exact inverse* of the guidance math, not a
private reimplementation.

## Key decisions

| Decision | Choice | Why |
|---|---|---|
| Camera-specific options UI | `IDevice.HasSetupDialog` + `SetupDialog()` | The only mechanism NINA supports; NINA's own `SimulatorCamera` is the precedent (see Feasibility). |
| Which options move to it | The whole **physical-rig** set | Conceptually clean: rig on the camera, observing in plugin Options. |
| Tilt source of truth | Existing `TiltAngle`/`TiltAmount`/`Backfocus` knobs; screws apply **deltas** | One parameterization; direct injection still works (type a tilt). |
| Screw math | Reuse `TiltScrewGeometry` | Guarantees the sim is the guidance math's exact inverse — a private copy would only prove the sim agrees with itself. |
| Sim vs real adapter config | Separate `Sim*` fields + coherence badge | The inspector guides from the user's **real** `TiltAdapterOptions`; silent divergence would make the loop never converge. |
| "Apply inspector's suggestion" button | **Rejected** | It bypasses the human-readable presentation + execution that this rig exists to validate. Belongs in a capstone test, not the UI. |
| Imaging dockable | Export unconditionally, gate behavior | NINA cannot remove a dockable's sidebar button (see Feasibility). |

## Feasibility (verified against NINA's assemblies, not assumed)

**Camera setup dialog — supported.** `IDevice` declares `bool HasSetupDialog { get; }` and `void SetupDialog()`.
`DeviceChooserVM<T>.SetupDialog` invokes it on a **dedicated STA thread** and blocks on `Join()`. NINA's own
`NINA.WPF.Base.Model.Equipment.MyCamera.Simulator.SimulatorCamera` does exactly what we want —
`WindowService.Show(this, "Simulator Setup", …)` plus an implicit `DataTemplate` keyed on the camera type — and
it even uses `PluginOptionsAccessor`. Plugins reach the same place because `PluginLoader.Compose` merges exported
`ResourceDictionary` parts into `Application.Current.Resources`; this plugin already exports five.

- **Constraint:** the gear button is gated only by `IsEnabled="{Binding HasSetupDialog}"` — **not** by `Connected`
  (unlike the device ComboBox). It is clickable while connected, so *we* must enforce any "before connect" rule.
- **Constraint:** never build WPF on the STA thread NINA spawns. `WindowService` captures the main dispatcher, so
  `Show` (non-modal) marshals correctly and returns promptly.
- There is **no** per-device settings-UI extension point. `PartsImport` (the authoritative list) exposes only:
  sequencer entities, `ResourceDictionary`, `IDockableVM`, `IPluggableBehavior`, `IEquipmentProvider`.

**Conditional dockable — only half supported.** `IsVisible=false` genuinely closes the panel (two-way bound in
`overview.baml`'s `LayoutAnchorableItem` style). But the sidebar toggle button has **no `Visibility` binding** —
hiding merely unchecks it. And the dockable set is immutable for the process: `DockManagerVM` builds it once at
startup into a plain `List<IDockableVM>` (not observable), with private setters, an `internal` class, and
`IDockManagerVM` is not composed into the plugin container. The MEF export is static, so a restart doesn't help
either. **Accepted limitation: the button remains.** Also, `InitializeAvalonDockLayout` sets every anchorable
`IsVisible=false` then re-enables from the saved `<profileId>.dock.config`, so any value set in a constructor is
overwritten — gating must happen *after* initialization.

---

## 1. Rig / observing options split

`HocusFocusSimulatorCamera.HasSetupDialog => true`; `SetupDialog()` → `WindowService.Show(this, "Hocus Focus
Simulator Setup", ResizeMode.NoResize, WindowStyle.ToolWindow)`. A new exported `ResourceDictionary` supplies
`<DataTemplate DataType="{x:Type camsim:HocusFocusSimulatorCamera}">` hosting the setup view.

| Group | Options | Where editable |
|---|---|---|
| **Rig** | `SensorModel`, `ApertureMillimeters`, `FocalLengthMillimeters`, `CentralObstructionEnabled`, `CentralObstructionFraction`, `OpticalThroughput` | Camera setup dialog only |
| **Observing** | `Filter`, `SkyBrightnessMagPerArcsec2`, `SeeingArcsec`, `Gain`, `BiasPedestalAdu`, `SensorTemperatureCelsius`, `AstapCatalogPath`, `LimitingMagnitude`, `RotationDegrees`, `NoiseSeed`, `OptimalFocuserPosition`, `FocuserStepSizeMicrons`, aberration knobs | Plugin Options (unchanged) |

- In **plugin Options**, the rig group renders **read-only** (values visible) with a tooltip: *"Set in the camera's
  setup dialog (gear icon in the camera equipment pane)."* This satisfies "keep options viewable".
- In the **setup dialog**, only `SensorModel` is disabled while connected —
  `IsEnabled="{Binding Connected, Converter={StaticResource InverseBooleanConverter}}"` with a hint *"disconnect to
  change the sensor — geometry is latched at connect."* The rest of the rig set is read per-exposure from the
  render snapshot and is safe to change live, so it stays editable. (Over-disabling the whole group would be a
  restriction the code does not actually require.)

## 2. Aberration group visibility

The plugin-Options aberration group currently uses `IsEnabled="{Binding CameraSimulatorOptions.EnableAberrations}"`.
Change to `Visibility` (via the existing boolean→visibility converter) so the group **collapses** when disabled.
The `EnableAberrations` toggle itself stays visible. Same treatment for the panel's "Injected aberration" section.

## 3. Virtual tilt adapter

### 3.1 Configuration (`Sim*` fields, mirroring `ITiltAdapterOptions`)

New persisted options on `ICameraSimulatorOptions`, **manually entered** (no calibration wizard run):
`SimScrewCount` (3|4), `SimScrew1..4AngleDegrees` (CW from up, image space; `Screw4 = NaN` when 3-screw),
`SimScrewInwardCurvatureSign` (+1/−1), `SimAdjustmentType` (`Screws`|`StepperMotors`), `SimThreadPitchMicrons`,
`SimStepperStepSizeMicrons`, `SimScrewRadiusMillimeters`.

**They are deliberately separate from the user's real `TiltAdapterOptions`** and are never implicitly written.
The panel shows an always-visible coherence badge — `matches adapter ✓` / `⚠ differs` — plus explicit
**Copy from adapter** / **Copy to adapter** (the latter confirmed, since it overwrites real calibration).
Deliberate mismatch stays possible: flipping the sim's sign to test the inspector's glyph robustness is itself a
valid scenario. Without this badge, a divergence would make the loop silently never converge and the user would
blame the math.

### 3.2 The screw model — the exact inverse of the guidance

All conventions come from `TiltScrewGeometry` (the single source used by the wizard and inspector). No new
geometry constants.

- Screw *i* sits at image-space point `p_i = (R·sinθ_i, −R·cosθ_i)` µm from sensor center, `R = SimScrewRadiusMillimeters·1000`
  (matching `docs/precise-screw-adjustments-design.md`).
- A click of `N` units on screw *i* → axial displacement `Δz_i = N · unit · σ`, where `unit` is
  `SimThreadPitchMicrons` (turns) or `SimStepperStepSizeMicrons` (steps), and `σ` is the direction sign from the
  button (⟳/⟲, or +/−) combined with `SimScrewInwardCurvatureSign`.
- The displacement set `{Δz_i}` defines a plane change `Δz(x,y) = ΔGx·x + ΔGy·y + ΔZ0` — exactly determined for
  3 screws, least-squares for 4.

Applied to the simulator's existing state:

1. **Tilt.** Current `(Gx,Gy)` ← the existing inversion (`|G| = TiltAmount/(|cosφ|·halfW + |sinφ|·halfH)`,
   `Gx=|G|cosφ`, `Gy=|G|sinφ`). Add `(ΔGx,ΔGy)`, then invert back:
   `TiltAmountMicrons = |Gx|·halfW + |Gy|·halfH`, `TiltAngleDegrees = atan2(Gy,Gx)`.
2. **Piston.** `ΔZ0` moves the sensor axially ⇒ best focus shifts:
   `OptimalFocuserPosition += ΔZ0 / FocuserStepSizeMicrons`. (A single screw on a 3-screw adapter unavoidably
   pistons by `Δz/3`; a corner move pistons by 0 by symmetry. Modeling this keeps the sim honest — the AF
   re-finds focus exactly as it would on a real rig.)
3. **Backfocus/curvature.** Curvature responds to the **piston** `ΔZ0` (the axial spacing change), not to any
   individual `Δz_i` — so a corner move (`ΔZ0 = 0` by symmetry) correctly leaves curvature untouched, while a
   backfocus move (`ΔZ0 = Δz`) changes it fully. Derived as the exact inverse of the inspector's
   `backTurns = CurvatureAt(p)/pitch`: removing `ΔZ0` µm of curvature at radius `R` means `Δ(K·R²) = −ΔZ0`, so
   `ΔBackfocusErrorMicrons = ΔK·(halfW²+halfH²) = −ΔZ0·(halfW²+halfH²)/R²`.
   Note this makes steps 2 and 3 two consequences of the same piston: the sensor moving axially both shifts best
   focus *and* violates the optics' backfocus spacing.

### 3.3 Movement semantics

- **3 screws** — one row per screw, each turned independently.
- **4 screws** — opposite screws are mechanically coupled, so the user first picks a movement type:
  - **Corner** — a diagonal pair turned opposite ways (2 screws) ⇒ tilt toward a corner.
  - **Side** — all 4, two pairs turned opposite ways ⇒ tilt about an edge axis.
  - **Backfocus** — all 4 the same way ⇒ pure piston + curvature change.
  Rows are labeled by **screw number**, not "corner A/B", so the inspector's pair-antisymmetric 4-screw output
  (`S1: 0.60 ⟳ / S3: 0.60 ⟲`) maps to a single click with the partner counter-turned automatically.
  4-screw angle entry derives screws 3/4 as `+180°` (dimmed) to eliminate config typos.

### 3.4 UX

Full detail in **`docs/virtual-tilt-adapter-panel-ux-design.md`**. Load-bearing points:

- **Three layers by cadence**: *Operate* always visible; *Injected aberration* and *Adapter configuration* as
  collapsed expanders with live summary headers. Config is edited rarely, operation constantly.
- **One shared strictly-positive "amount per click" box + per-row ⟲/⟳ buttons.** Direction comes only from the
  button — no signed text entry, no double negatives. "Screw 2, −0.5 turns" = one click once the amount is set.
- **Glyph contract (inherited, non-negotiable):** buttons speak **rotation** (⟳/⟲ for screws, +/− for steppers,
  the same glyphs the inspector prints — transcription, not translation); tooltips and the feedback line speak
  **motion** (⬆/⬇ = that corner moves toward the objective). Never present ⬆/⬇ as a rotation.
- **Feedback:** a state strip (`Tilt 12.4 µm @ 214° · BF −5.0 µm`, `✓ ≈ flat` under 1 µm), a last-action line
  showing the before→after tilt, single-level **Undo**, and a per-screw net-position strip stored in **µm** (so
  changing pitch/units re-scales rather than corrupts). `Re-zero` re-bases the display only — never the plane.
- **No auto-apply of inspector guidance** (see Key decisions).

## 4. Imaging-tab dockable

A new `DockableVM` (5 existing precedents in this plugin) hosting **the same control** as the setup dialog, so the
adapter can be operated beside the Aberration Inspector during a calibration run. Gated by a new plugin option
`ShowSimulatorTiltAdapterPanel`:

- Export **unconditionally** (MEF metadata is static; a conditional export isn't possible, and throwing in the
  ctor fails the whole plugin load).
- When the option is off: set `IsVisible = false` **after** `Initialized` (the saved dock layout overwrites
  ctor-time values), and override `Hide(object)` to no-op so the sidebar button cannot reopen it.
- **Documented limitation:** the 30×30 sidebar button remains. NINA exposes no supported way to remove it.

## Architecture summary

One control, two hosts. A single `UserControl`/`DataTemplate` (`CameraSimulator/TiltAdapter/`) is hosted by both
the camera setup dialog and the dockable, backed by one VM, so there is one implementation of the operate loop.

New/changed:

- `Interfaces/ICameraSimulatorOptions.cs`, `CameraSimulator/CameraSimulatorOptions.cs` — the `Sim*` adapter fields
  + `ShowSimulatorTiltAdapterPanel`.
- `CameraSimulator/TiltAdapter/SimulatedTiltAdapter.cs` — the screw model (§3.2), pure and unit-testable, built on
  `TiltScrewGeometry`.
- `CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs` + view — the panel (operate/config/feedback, Undo).
- `CameraSimulator/TiltAdapter/SimulatorTiltAdapterDockableVM.cs` — `[Export(typeof(IDockableVM))]`, gated.
- `CameraSimulator/HocusFocusSimulatorCamera.cs` — `HasSetupDialog`/`SetupDialog`.
- New exported `ResourceDictionary` with the camera-keyed `DataTemplate` + the setup view.
- `Resources/OptionsDataTemplates.xaml` — rig group → read-only + tooltip; aberration group → `Visibility`.

## Testing

- **Screw-model unit tests** — pure `SimulatedTiltAdapter`: single-screw 3-screw move produces the expected
  `(ΔGx,ΔGy)` and `Δz/3` piston; corner move pistons zero; backfocus move changes only curvature + piston;
  stepper vs turn units; σ sign flips direction.
- **Capstone round-trip (the automated form of the interactive loop)** — inject a known tilt/backfocus → compute
  the inspector's per-screw guidance from the **real** guidance math → apply the negated guidance through the
  simulator's screw model → assert the residual tilt/backfocus ≈ 0. Parameterized across **screw count (3,4) ×
  adjustment type (screws, steppers) × σ (±1)**. This is what pins "exact inverse"; a private reimplementation
  would fail it.
- **Options tests** — `Sim*` persistence/defaults; `ShowSimulatorTiltAdapterPanel` gating.
- **Manual (Windows NINA)** — extend `docs/synthetic-camera-manual-smoke-test.md`: gear button opens the setup
  dialog; rig options read-only in plugin Options; sensor locked while connected; aberration group collapses;
  the full inspector→screw→inspector loop converges; dockable appears/stays closed per the option.

## Risks

- **Sim/real adapter divergence** — the loop silently won't converge. Mitigated by the coherence badge + explicit
  copy commands (§3.1).
- **Sign conventions & image mirroring** — the historical foot-gun in this domain. Mitigated by reusing
  `TiltScrewGeometry` for every convention and by the parameterized round-trip capstone.
- **STA/threading in `SetupDialog()`** — mitigated by `WindowService` (captures the main dispatcher); never
  construct WPF on NINA's spawned STA thread.
- **Gear button live while connected** — mitigated by binding `SensorModel` to `!Connected` ourselves.

## Out of scope / accepted limitations

- The dockable's sidebar button cannot be hidden (NINA API limitation, evidenced above).
- No auto-apply of inspector guidance (deliberate — it would bypass what the rig validates).
- The sim's adapter is manually configured; running the real Tilt Adapter Wizard *against* the simulator is a
  natural follow-on but is not required here.
- Per-screw backlash/hysteresis modeling is not attempted.
