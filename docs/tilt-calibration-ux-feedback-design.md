# Tilt Adapter Calibration — UX Feedback Design

Three user-reported problems with the Tilt Adapter Calibration wizard, addressed together because two of them
(the illegible alert text and the missing explanation for a blocked automation gate) are the same failure:
the plugin knows something the user needs and fails to put it on screen legibly.

| # | Reported as | Actual defect |
|---|---|---|
| 1 | "Red alert text during tilt calibration is very difficult to see against dark themes" | NINA's `NotificationError/WarningBrush` are **background** colors used as `Foreground` at 27 sites. 1.06–1.9:1 contrast on every dark schema. |
| 2 | "Wizard says it moves screws/steppers inwards, but moves EAT motors positive, which would be outwards" | **No motion defect.** The step is titled `"All Screws Inward"` while it applies `+N` to every motor — and `+N`'s physical direction is precisely what the step *measures*. Stale label, contradicting the plugin's own naming rule. |
| 3 | "I forced the rotation angle to the one I know is true… then it said it can't do automation because my calibration didn't fit this setup" | `ApplyManualCalibration()` silently clears both automation gates. Correct safety behavior, zero communication, and no way back short of a full re-run. |

---

## Part 1 — Accessible alert badges

### The defect

NINA's color schemas define `NotificationErrorColor` / `NotificationWarningColor` as **fills** to be paired with
`NotificationErrorTextColor` / `NotificationWarningTextColor`. NINA itself only ever uses them as `Background`
(`NINA.Sequencer/Trigger/Datatemplates.xaml`, `MiniSequenceItem.xaml`, `ProgressStyle.xaml`).

HocusFocus uses them as `Foreground` in 27 places. In 14 of the 16 built-in schemas that color is `#FF700000`
(error) and `#FF5E330B` (warning) — near-black reds — so against a dark page background the text is invisible:

| Schema | page bg | red text on page (today) |
|---|---|---|
| Persian / Persian Faint | `#263238` | **1.06:1** |
| Vivid Malachite | `#34403A` | **1.15:1** |
| Arsenic | `#394648` | **1.27:1** |
| Dark | `#02010A` | **1.67:1** |
| High Contrast | `#000000` | **1.69:1** |

WCAG AA body text wants 4.5:1.

### Why the plain NINA badge is not enough

Switching to NINA's own convention (fill = alert brush, text = `NotificationErrorTextBrush`) fixes 11 schemas but
**not the ones that matter most**, because several schemas pair a dark fill with near-black text:

| Schema | `NotificationErrorTextColor` on `NotificationErrorColor` |
|---|---|
| **Dark** | `#02010A` on `#700000` → **1.67:1** |
| **Classic** | `#000000` on `#700000` → **1.69:1** |
| **Navy** | `#6FC3DF` on `#DB0606` → **2.61:1** |

"Dark" is the likeliest schema for a user reporting a dark-theme problem, so a plain badge would not fix the
report. The badge therefore keeps NINA's fill (native look, follows custom schemes) but computes its **text**
color instead of reading it.

### Design

**`Converters/ContrastMath.cs`** — pure static, no WPF plumbing, directly unit-testable:

- `RelativeLuminance(Color)` — WCAG 2.x sRGB linearization.
- `ContrastRatio(Color a, Color b)` — `(L_hi + 0.05) / (L_lo + 0.05)`.
- `BestContrastText(Color fill)` → `Colors.Black` or `Colors.White`, whichever contrasts more with `fill`.
- `AccessibleAccent(Color alert, Color pageBackground)` → the alert color adjusted to clear the ratio target
  **in HSL, preserving hue and saturation, moving only lightness**, by binary search (24 iterations, evaluated on
  the byte-rounded color so the returned value is the one actually rendered).
  - Moving in HSL rather than lerping toward white matters: `#700000` lerped toward white only reaches 4.5:1 at
    `#A96666` (a washed-out rose), while raising HSL lightness reaches it at `#EF0000` — still obviously red.
  - Direction: whichever pole (lighter/darker) can reach the target; if both can, away from the background's
    luminance. If neither can, fall back to `BestContrastText(pageBackground)`.

**Ratio target is 4.6 internally, asserted at ≥ 4.5 in tests.** The 0.1 margin absorbs byte rounding and
float differences; targeting 4.5 exactly lands results at 4.50–4.53 with no headroom.

**`Converters/BadgeTextColorConverter.cs`** (`IValueConverter`, `Color → Color`) and
**`Converters/AccessibleAccentColorConverter.cs`** (`IMultiValueConverter`, `[alertColor, backgroundColor] → Color`).
Thin wrappers; all logic stays in `ContrastMath`.

**`Resources/AlertBrushes.xaml`** — a new `ResourceDictionary` defining four brushes, using the same
bound-`SolidColorBrush.Color` shape NINA uses in `NINA.WPF.Base/Resources/StaticResources/Brushes.xaml`, so they
re-evaluate when the user changes color schema at runtime:

| Key | Bound to | Role |
|---|---|---|
| `HF_AlertErrorTextBrush` | `NotificationErrorBrush.Color` → `BadgeTextColorConverter` | text **on** an error badge |
| `HF_AlertWarningTextBrush` | `NotificationWarningBrush.Color` → `BadgeTextColorConverter` | text **on** a warning badge |
| `HF_AlertErrorAccentBrush` | `[NotificationErrorBrush.Color, BackgroundBrush.Color]` → `AccessibleAccentColorConverter` | inline error text **on the page** |
| `HF_AlertWarningAccentBrush` | `[NotificationWarningBrush.Color, BackgroundBrush.Color]` → `AccessibleAccentColorConverter` | inline warning text **on the page** |

Plus two `Border` styles, `HF_AlertErrorBadge` / `HF_AlertWarningBadge`: `Background` = the NINA alert brush,
`BorderBrush` = same, `BorderThickness=1`, `CornerRadius=3`, `Padding=6,4`.

Dictionary-level `{StaticResource NotificationErrorBrush}` lookup into `Application.Resources` is already proven
in this plugin (e.g. `TiltAdapterWizard/DataTemplates.xaml:228` resolves `ButtonForegroundBrush` inside a
dictionary-level `Style`), so no `ProfileService` binding proxy is needed.

`AlertBrushes.xaml` is merged into each of the five dictionaries that need it.

### Verified output

Computed for every built-in NINA schema (`ColorSchemas.ReadColorSchemas()`):

| Schema | page bg | error accent | ratio | warning accent | ratio | badge text ratio (err / warn) |
|---|---|---|---|---|---|---|
| Light / Classic / Seance | `#FFFFFF` | `#700000` (unchanged) | 12.43 | `#5E330B` (unchanged) | 10.75 | 12.43 / 10.75 |
| Dark | `#02010A` | `#EF0000` | 4.62 | `#B46115` | 4.61 | 12.43 / 10.75 |
| High Contrast | `#000000` | `#ED0000` | 4.60 | `#B26115` | 4.61 | 12.43 / 10.75 |
| Persian / Persian Faint | `#263238` | `#FF6666` | 4.60 | `#E57F1F` | 4.63 | 12.43 / 10.75 |
| Black Coral | `#545E75` | `#FFCECE` | 4.63 | `#F6D3B3` | 4.60 | 12.43 / 10.75 |
| Arsenic | `#394648` | `#FF9494` | 4.63 | `#ECA25C` | 4.61 | 12.43 / 10.75 |
| Vivid Malachite | `#34403A` | `#FF8585` | 4.61 | `#EA9647` | 4.61 | 12.43 / 10.75 |
| Shark | `#36393E` | `#FF7B7B` | 4.62 | `#E88E39` | 4.61 | 12.43 / 10.75 |
| Slate | `#1E2129` | `#FF3E3E` | 4.61 | `#CF7118` | 4.62 | 12.43 / 10.75 |
| Wisteria | `#2D0D25` | `#FF2222` | 4.60 | `#C66B17` | 4.61 | 12.43 / 10.75 |
| Navy | `#0C141F` | `#F91E1E` | 4.61 | `#EA3F1E` | 4.60 | 5.20 / 5.10 |
| Dark Nebula / Dichromacy | `#191A1C` | `#FF2626` | 4.61 | `#CD6808` | 4.63 | 12.43 / 4.69 |

Worst case across 16 schemas × 4 roles: **4.60:1**. Light-background schemas are left untouched, because the
existing color already passes there.

### Call-site changes

27 `Foreground=` sites across five dictionaries, plus the 5 existing (already-badge-shaped) sites:

| File | error fg | warning fg | notes |
|---|---|---|---|
| `TiltAdapterWizard/DataTemplates.xaml` | 10 | 5 | the reported surface |
| `AutoFocus/DataTemplates.xaml` | 1 | 3 | + 3 existing badge sites to re-point |
| `StarDetection/Optimization/DataTemplates.xaml` | 1 | 4 | |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | 0 | 1 | + 3 existing badge sites to re-point |
| `Resources/OptionsDataTemplates.xaml` | 0 | 2 | |

Assignment rule:

- **Block-level alerts become filled badges.** Multi-line warning/error paragraphs and boxed banners. Most
  already sit inside a `<Border BorderBrush="{StaticResource NotificationErrorBrush}">`, so the change is adding
  `Background` (via the badge style) and swapping the inner `Foreground` to `HF_AlertErrorTextBrush`.
- **Inline in-row tags keep being text**, switched to `HF_AlertErrorAccentBrush` / `HF_AlertWarningAccentBrush`:
  the plan-preview `LimitTag` (wizard `DataTemplates.xaml:372,378`), the twist `WillReachText` (`:510`), and the
  short optimizer notes. Filling these would turn dense grids into a wall of pills.
- **The 5 existing badge sites** switch `NotificationWarningTextBrush` → `HF_AlertWarningTextBrush`; they read at
  1.93:1 on the Dark schema today.

**Explicitly out of scope:** the 11 chart `Fill` / `Stroke` / `ErrorBarColor` sites that bind
`NotificationErrorBrush.Color`. NINA's own `NINA/View/AutoFocusChart.xaml` uses the identical brush for error
bars; matching NINA's chart is worth more there than a contrast bump, and these are plot marks, not alert text.

### Tests — `Tests/Converters/ContrastMathTests.cs`

- `RelativeLuminance` / `ContrastRatio` against published WCAG reference pairs (black/white = 21:1, etc.).
- **Schema sweep:** iterate `ColorSchemas.ReadColorSchemas()` (public, pure in-memory) and assert, for every
  schema, that `BestContrastText(errorFill)` on the fill and `AccessibleAccent(errorFill, pageBg)` on the page
  both clear 4.5:1 — and the same for warning. This is the regression that would have caught the original bug,
  and it will also catch NINA adding a new schema with unusable colors.
- `AccessibleAccent` returns the input unchanged when it already passes (keeps light themes byte-identical).
- `AccessibleAccent` preserves hue: assert the returned color's HSL hue is within a degree of the input's.
- Converter wrappers: null/wrong-type inputs return `Binding.DoNothing`-safe values rather than throwing.

---

## Part 2 — "Inward" wording

### Finding: the motion is correct

`EatWizardMapping.MoveForStep(WizardStep.AllInward, N)` returns `TiltAdapterMove(TiltMoveAxis.Backfocus, +N, …)`
— `+N` on all four motors. That matches the instruction paragraph the user is reading
(`"Apply +N steps to EVERY motor"`), and it matches `EatWizardMappingTests`' full-sequence trace, which pins that
the six moves sum to `(0,0,0,0)` per corner.

Nothing in the plugin claims `+N` is physically inward. It cannot: `docs/asg-eat-serial-protocol-design.md` §162
records that per-motor directions were never confirmed on hardware, and determining that direction is the entire
purpose of this step — it is what sets `ScrewInwardCurvatureSign`, reported afterwards as "measured" rather than
"assumed".

The wizard's own source states the rule being broken, at `TiltAdapterWizardVM.cs:1089`:

> Screw motion is worded as CLOCKWISE/COUNTER-CLOCKWISE (tighten/loosen) — never "inward/outward", which this
> plugin reserves for adapter-plate motion.

`StepTitleText` returns `"All Screws Inward"` and `MoveForStep` labels the move `"Wizard All Inward"`. Both
violate that rule, and they are the only reason the run looks wrong.

### Design

Wording only; no behavior change, no math change.

1. `TiltAdapterWizardVM.StepTitleText` gains a `bool isStepper = false` parameter:
   - screws → `"All Screws Clockwise"`
   - steppers → `"All Motors + Steps"`

   Callers: `StepTitle` (passes `IsStepperAdjustment`) and `ReplayStepInstructionsText` (threads it through).
2. `EatWizardMapping.MoveForStep`: `"Wizard All Inward: +150 backfocus"` → `"Wizard All Motors: +150 backfocus"`.
3. New tooltip on the step title, shown for this step only:
   > This step measures which way the adapter plate actually moves. `+` steps are not assumed to be inward —
   > determining that direction is the point of the step.
4. Reword the stale `WizardStep.AllInward` enum comment (`"all screws inward once"`) and the reference in
   `docs/tilt-adapter-ui-review-design.md:83`.

**`WizardStep.AllInward` keeps its name.** It is persisted in saved replay runs and referenced across
`TiltCalibrationCalculator`, `EatWizardMapping` and four test files; renaming it would need a serialization shim
for no user-visible gain.

### Tests

- `TiltAdapterWizardVMTests:689` currently asserts `[TestCase(WizardStep.AllInward, "All Screws Inward")]` —
  update to the two new titles, one case per `isStepper` value.
- Add a guard test asserting no user-facing wizard string for a *screw/stepper move* contains "inward" or
  "outward" (`StepTitleText`, `StepInstructionsText`, `BaselineRecoveryText`, `StepDescription` and every
  `MoveForStep` description, across both screw counts and both adjustment types). This pins the naming rule the
  code comment states but nothing enforced.

---

## Part 3 — Explicit opt-in re-link for hand-entered calibration

### The defect

`TiltAdapterWizardVM.ApplyManualCalibration()` ends with two `[CRITICAL GATE]` writes:

```csharp
tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = string.Empty;  // :1375
tiltAdapterOptions.CalibrationIsReliable = false;                     // :1379
```

Both are required by `InspectorVM.CanExecuteAutomaticAdjustment`, so hand-editing the calibration disables
Automatic Adjustment. The safety reason is real — a hand-entered screw numbering is not guaranteed to match how
the device's motors are wired, and a 90°/180° rotated correction applied unattended makes tilt worse.

What is wrong is everything around it:

- **Nothing says it happened.** No notification, no wizard-side indication.
- **The one explanation lives elsewhere.** `InspectorVM.AutomaticAdjustmentRemediationText` shows
  *"This calibration is not linked to the connected device"* — in the Inspector, only while a device is
  connected, and it never mentions manual entry as the cause.
- **The only remedy offered is a full re-run**, which is an entire imaging session's worth of work to undo a
  deliberate one-number correction.

### Design

Keep the gate closed by default; add a deliberate, warned, reversible way to re-arm it.

**Wizard — a new alert block** in the saved-calibration sub-panel of Panel A (`TiltAdapterWizard/DataTemplates.xaml`,
the `IsCalibrationValid` `StackPanel` that currently holds the "Manually entered calibration" note and the
`Clear Calibration` button), rendered with Part 1's `HF_AlertWarningBadge`. Shown when all of:
`IsCalibrationValid` ∧ ¬(device-linked ∧ reliable) ∧ a motorized device preset is selected.

> **Automatic Adjustment is disabled.** This calibration was entered or edited by hand, so HocusFocus cannot
> verify that your screw numbering matches the device's motor wiring. Re-run the wizard with the device
> connected, or trust this calibration explicitly.

with a **`Trust This Calibration for Automation`** button.

**Confirmation before trusting — the in-pane two-step pattern**, not a modal. This mirrors
`SimulatedTiltAdapterVM`'s `CopyToAdapterCommand` / `IsCopyToAdapterPending` /
`ConfirmCopyToAdapterCommand` / `CancelCopyToAdapterCommand` trio, which already guards the structurally
identical decision ("overwrite a calibration the user cannot cheaply repeat"). A modal `MyMessageBox` — the
wizard's other confirmation style, at `TiltAdapterWizardVM.cs:1936` — would put the whole decision out of reach
of unit tests, and this one has enough branches to be worth testing.

`TrustCalibrationCommand` sets `IsTrustCalibrationPending = true`, which reveals the risk copy and a
Confirm / Cancel pair inside the same badge:

> If your screw numbering does not match the device's motor wiring, Automatic Adjustment will drive the adapter
> unattended in the wrong direction and make tilt worse. Verify with a single manual adjustment before leaving
> it unattended.

`ConfirmTrustCalibrationCommand` then performs the writes and clears the pending flag:

```csharp
tiltAdapterOptions.DeviceLinkedCalibrationDeviceName = tiltAdapterOptions.DeviceName;
tiltAdapterOptions.CalibrationIsReliable = true;
```

`CancelTrustCalibrationCommand` clears the pending flag and writes nothing.

**No new persisted option.** `CalibrationIsManual == true` together with a device link already means exactly
"manually trusted" — every surface that needs to distinguish it can read those two. This keeps the change out of
`Resources/OptionsDataTemplates.xaml`, consistent with both markers being deliberately absent from the options UI.

**Trust revokes itself.** Everything that already invalidates the correspondence continues to:

- `ApplyManualCalibration()` clears both markers (unchanged) — editing the angle again requires re-trusting.
- Changing the device preset invalidates it for free: `IsCalibrationDeviceLinked` compares the stored name
  against the *current* `DeviceName`.
- `ClearCalibration()` **now also clears both markers.** It does not today — a latent bug, harmless only because
  clearing the angles also removes the numeric guidance the gate requires, but wrong the moment Trust can set
  them deliberately.
- `SimulatedTiltAdapterVM.ConfirmCopyToAdapter()` gets the same two lines. It currently relies on setting
  `DeviceName = ManualName` to break the link implicitly; making it explicit matches the other two paths.

**Tell the user at the moment it happens.** `ApplyManualCalibration()` checks whether it actually revoked
anything (were the markers set before?) and, if so, raises `Notification.ShowWarning` naming the consequence and
pointing at the Trust button.

**Name the real cause in the Inspector.** `AutomaticAdjustmentRemediationText` gains a manual-entry branch:

> This calibration was entered by hand, so Automatic Adjustment is disabled — HocusFocus can't confirm your screw
> numbering matches the device's motor wiring. Re-run calibration with the device connected, or trust it
> explicitly in the Tilt Adapter Wizard.

The existing not-linked and low-confidence branches are unchanged.

### Tests

- `ApplyManualCalibration` still clears both markers (existing behavior, re-pinned).
- `ApplyManualCalibration` warns only when it actually revoked something — not on a first-ever manual entry.
- `TrustCalibrationCommand` alone writes nothing — it only sets `IsTrustCalibrationPending`.
- `ConfirmTrustCalibrationCommand` sets both markers, using the *current* `DeviceName`, and clears the pending flag.
- `CancelTrustCalibrationCommand` clears the pending flag and leaves both markers untouched.
- The Trust commands are unavailable when no motorized preset is selected or no calibration is saved.
- After trusting, `InspectorVM.CanExecuteAutomaticAdjustment` returns true with everything else held equal.
- `ClearCalibration` clears both markers.
- Changing `DeviceName` after trusting makes `IsCalibrationDeviceLinked` false again.
- The banner's visibility predicate: hidden for a wizard-measured device-linked calibration, hidden with no
  calibration, hidden on a non-motorized preset, shown for a manual calibration on a motorized preset.
- `AutomaticAdjustmentRemediationText` returns the manual-entry wording when `CalibrationIsManual`, and the
  existing wording otherwise.

---

## Out of scope

- Renaming `WizardStep.AllInward` (persisted; see Part 2).
- Chart mark colors (`Fill` / `Stroke` / `ErrorBarColor`) — see Part 1.
- Any change to the calibration math, the move sequence, or the automation gate's *conditions*. Part 3 adds a way
  for the user to satisfy the existing gate deliberately; it does not weaken it.
- `TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptControl.xaml` — already carries a self-contained,
  theme-independent palette (`WarnFg`, `DangerFg`) and is unaffected.

## Verification

- `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` green.
- Live check in NINA on the **Dark** schema: wizard alert text legible, step titled "All Motors + Steps" on an
  EAT preset, Trust button appears after a manual entry and re-enables Automatic Adjustment.
