# Tilt Adapter Screw Labels — Design

## Context

Every tilt UI in HocusFocus refers to the adapter's adjustment points as **"Screw 1".."Screw 4"** — an
index that means nothing on the bench. A user standing at the scope sees motors engraved M1–M4 (ASG
Electronic EAT), or thinks of them as "top left" and "front right". The mapping from the wizard's
number to the thing their hand is on is theirs to hold in their head, and it is not even a stable
mapping: wizard screw 3 is the EAT's **M4**.

This feature lets the user name the screws once and have every UI use those names.

Three findings from exploration shape the design:

1. **The EAT motor mapping is a real permutation, and it is already codified.**
   `TiltAdapterCorner` (`TiltAdapterDevices/Manual/ManualAdjustmentTarget.cs:28-96`) is the single
   table reconciling the three index spaces, and it is pinned by `ManualAdjustmentTargetTests` and by
   `EatTiltMotionController.PermuteWizardToDeviceMotorOrder`:

   | wizard screw | 1 | 2 | 3 | 4 |
   |---|---|---|---|---|
   | corner | TR | TL | BL | BR |
   | device motor | 1 | 2 | **4** | **3** |

   User-confirmed: **M1 = TR, M2 = TL, M3 = BR, M4 = BL**. So EAT defaults are M1, M2, **M4**, **M3**.
   Nothing in this feature may re-derive that permutation by hand — every default resolves through
   `TiltAdapterCorner.ForWizardScrew(n).DeviceMotorNumber`.

2. **A latent correctness bug that this feature fixes.** For a stepper adapter that is *not*
   device-connected, `TiltAdapterWizardVM.StepInstructionsText` (`:1026-1051`) tells the user
   *"Apply +N steps to motor 1 and −N steps to motor 3"*. Per `EatWizardMapping`'s own class doc
   (`EatWizardMapping.cs:22-25`), those are **wizard screw indices, not device motor numbers** — so a
   user who obeys literally and turns the EAT's physical M3 moves the wrong corner. Once labels drive
   this prose it reads *"Apply +150 steps to M1 and −150 steps to M4"*, which is unambiguous and correct.

3. **Labels belong to the device, not just the profile.** Switching device presets must swap which
   label set is active, so an EAT's M-names and a manual adapter's names coexist in one profile and
   each returns when its device is selected.

Out of scope: nothing in the requested surface list. Screw *numbers* remain the canonical identity in
logs, `TiltCalibrationMetadata`, step folder names, and every persisted artifact — labels are display
only.

---

## Design

### Label schemes (the new concept)

A **screw label scheme** is a family of devices that share a naming vocabulary. Add to
`TiltAdapterWizard/TiltAdapterDevicePreset.cs`:

```csharp
public sealed class ScrewLabelScheme {
    public string Id { get; }                       // persistence key
    public string DefaultLabel(int wizardScrewNumber);

    /// "Screw 1".."Screw 4" — today's strings, so an unlabeled manual rig renders byte-identically.
    public static readonly ScrewLabelScheme Generic;

    /// "M{motor}" via TiltAdapterCorner.ForWizardScrew(n).DeviceMotorNumber => M1, M2, M4, M3.
    public static readonly ScrewLabelScheme AsgEat;
}
```

Add a `ScrewLabels` property to `TiltAdapterDevicePreset` (defaulting to `Generic` in the ctor, so the
file's *"To add a device, append one entry to `All` — no other code changes are required"* contract
survives). The two `"ASG Electronic EAT - …"` entries get `ScrewLabelScheme.AsgEat`; every other preset
keeps `Generic`. This is the extension point for the user's stated future case — a new pre-labeled
motorized device adds one scheme and one preset field, nothing else.

### Persistence — one JSON option, keyed by scheme

Add `ScrewLabelsJson` to `ITiltAdapterOptions` / `TiltAdapterOptions`. Follows the established
JSON-blob precedent (`PerFilterStarDetectionStore.cs:270-298`,
`StarDetectionOptions.cs:313-321`) rather than indexed sibling keys, because the store is now
two-dimensional (scheme × screw):

```json
{ "AsgEat": ["", "", "Bob", ""], "Generic": ["Top Left", "", "", ""] }
```

Parse contract, matching both precedents exactly: **tolerant — warn via `Logger.Warning` and discard on
corrupt input, never throw.** Empty string = unset.

**Empty means "use the scheme default"; defaults are never materialized into the field.** Consequences,
all of them desirable: switching EAT → Manual silently moves blanks from "M4" back to "Screw 3" with
zero migration code; `ScrewCount` 4 → 3 parks `Screw4`'s label unused exactly as `Screw4AngleDegrees`
is parked, and it returns on 3 → 4; and a user who never touches the feature on a manual adapter sees
a byte-identical UI to today.

Validation is minimal (hobbyist tool): trim on set, empty-after-trim = unset, `MaxLength=12` enforced
by the TextBox, duplicates allowed, no error states.

Note: tilt options do **not** participate in the star-detection settings export/import path
(`StarDetectionSettingsDiff.ImportableSettings` is typed against `IStarDetectionOptions` and a
reflection guard asserts exact coverage) — adding this option there would *break* that guard. No change
needed.

### The resolver — one site, used by every surface

New `TiltAdapterDevices/Manual/TiltScrewLabels.cs`, beside `TiltAdapterCorner` for the reason that
file's own comment gives (hand-converting between index spaces is "the single most repeated bug in
this feature"):

```csharp
public interface IScrewLabelProvider {
    /// Effective display label for a 1-based wizard screw number.
    string Label(int wizardScrewNumber);
}
```

Resolution: stored override for the current device's scheme if non-empty, else
`scheme.DefaultLabel(n)`. `TiltAdapterOptions` exposes an `IScrewLabelProvider` that reads its own
`DeviceName` → preset → scheme. Static pure formatters (`StepInstructionsText`, `FormatScrews`, …) take
the provider as a parameter so they stay unit-testable.

### Editor UI

A collapsed `Expander` headed **"Screw Labels"** in the Tilt Adapter Wizard settings pane
(`TiltAdapterWizard/DataTemplates.xaml`), directly after the "Screw radius (mm)" row and before
"Measurements to average". One home only — no duplicate in `Resources/OptionsDataTemplates.xaml`
(tilt options deliberately live in the wizard pane; `OptionsDataTemplates.xaml` has zero
`TiltAdapterOptions.` bindings today).

- Caption: *"Optional names for your screws — used everywhere HocusFocus refers to them. Leave blank
  for the default."*
- One `UniformGrid Columns="2"` row per screw, row 4 collapsing on the same
  `TiltAdapterOptions.ScrewCount == 3` trigger the Screw 4 angle row already uses.
  Left: fixed non-editable **"Screw 1".."Screw N"** (the number is the row identity). Right: a
  `TextBox` (`MaxLength=12`) whose **watermark is the effective default** — so a blank box visibly *is*
  the fallback.
- Rows stay enabled when a non-Manual preset locks the hardware fields — renaming EAT motors is the
  primary use case.
- No reset button; clearing the text is the reset.

### Display grammar

`{L}` = effective label. When unset on a manual rig, `{L}` *is* "Screw n", so each template below
degenerates to today's exact string.

| Surface | Template |
|---|---|
| Inspector guidance table headers (both, arrows + numeric) | `{L}`, ToolTip `Screw {n} · {corner} · Motor {m}` + *"Rename in the Tilt Adapter Wizard settings."* |
| Screw diagram | circle keeps the digit `{n}`; `{L}` rendered beside it (see below) |
| Saved-calibration angle rows | `{L}` alone — the diagram sits adjacent and now carries both |
| Wizard step titles | `Move {L}` |
| Wizard prose | `Turn {L} CLOCKWISE and {L₃} COUNTER-CLOCKWISE exactly {amt} each…`; stepper: `Apply +{amt} steps to {L}…` |
| Step-summary chips | `{L} ⟳, {L₃} ⟲` |
| Motion lines (`TiltRunReturnVM`) | motorized `{L} ({corner}): {amount}`; manual `{L}: {amount}` |
| Approval dialog (`FormatScrews`) | `{L1} & {L3} +150 steps`; residual rows `{L} ({corner})` |
| 2×2 device-position grids (both copies) | heading `{corner} · {L}`; caption `Motor {m} · wizard screw {n}` |
| 3×3 nudge pad | **button faces unchanged**; corner-cell tooltips gain `{L}` |

**Width strategy:** a single 12-char-capped label, `TextTrimming="CharacterEllipsis"` plus a
full-mapping tooltip on the width-constrained TextBlocks. No second "short label" field.

**Diagram geometry (the one layout change).** The canvas is a fixed 200×200 with screws on a 75px
radius, so labels placed outside the circles clip. Grow the canvas symmetrically to **260×260**
(offset every existing coordinate by +30 — sensor rect, circles, connection lines, chevrons) and place
each label in a 72px-wide centered `TextBlock` positioned **radially outward** at radius ~97 from
center. At 12 chars / ~9px font this fits every clock position without overlapping the sensor rect or a
neighbour. `HF_TiltScrewDiagram` is shared by the wizard Panels A and C and the simulator panel, so
verify all three render. *If layout regressions prove stubborn, fall back to tooltip-only on the
circles and say so — do not ship a clipped diagram.*

### Do NOT relabel

- **3×3 nudge pad button faces** — a documented sign-correctness anchor whose vocabulary is spatial
  (TL/Top/All), not screw identity. Tooltips only.
- **Motor limit warnings** (*"would drive Motor 4 (BL) below 0"*) — about the device's own EEPROM
  counters; Motor N is the identity the vendor app shows.
- **Collective prose** — "Turn ALL screws…", "every motor", the curvature row's "Screws ⟳".
- **Logs, `TiltCalibrationMetadata`, step folder names, `TiltDeviceShadowPositions`** — replays and
  support logs must never depend on mutable display state.

---
