# Virtual Tilt Adapter Panel — UX Design

**Status:** draft for review, 2026-07-16
**Scope:** UX only — a design brief for the on-screen "virtual tilt adapter" that turns simulated screws on the
HocusFocus synthetic camera (`ghilios/synthetic-camera` branch). No code or XAML here; a developer implements from
this. The panel exists to close this loop fast:

> inject a tilt → run the Aberration Inspector → it says "screw 2: 0.75 ⟳" → click that on the virtual adapter →
> re-run → confirm tilt → 0.

Every decision below is optimized for that loop: **zero mental arithmetic, transcription not translation, one
action per click, instant visible consequence.**

## Vocabulary and machinery reused (do not reinvent)

- **Glyph contract** (authoritative: `docs/tilt-guidance-motion-arrows-design.md`): ⟳/⟲ = screw rotation
  (CW/CCW), +/− = signed stepper steps, ⬆/⬇ = adapter-plate **motion** toward objective/camera. The panel's
  *inputs* are rotations (⟳/⟲ or +/−); its *feedback* describes motion (⬆/⬇). Never mix.
- **Screw diagram**: reuse `HF_TiltScrewDiagram` (`TiltAdapterWizard/DataTemplates.xaml`) — sensor rectangle,
  numbered screw circles at their image-space angles, 0° chevron, "top of image" caption. It already solves the
  mirroring-education problem; the panel adopts it unchanged inside its config expander.
- **Layout idiom**: labeled two-column grid rows, `ninactrl:UnitTextBox` with unit suffix and validation rules,
  ComboBox for enums, tooltip on every control, bold section headers (`Resources/OptionsDataTemplates.xaml`
  Camera Simulator section is the template to extend).
- **Math**: forward model `G = (2/(n·R²))·Σ δᵢ·pᵢ` and the σ conventions in
  `TiltAdapterWizard/TiltScrewGeometry.cs`. The panel converts each click → per-screw axial µm → delta on the
  stored sim state (`TiltAngleDegrees`, `TiltAmountMicrons`, `BackfocusErrorMicrons` in
  `CameraSimulator/CameraSimulatorOptions.cs`).

## Mental model: three layers, three cadences

| Layer | Cadence | Contents |
|---|---|---|
| **Operate** | constantly | amount-per-click box, per-row ⟲/⟳ (or −/+) buttons, movement-type selector (4-screw), state strip, last-action line, net counters |
| **Inject** | per scenario | direct edit of tilt azimuth / tilt amount / backfocus error; "Zero all aberrations" |
| **Configure** | rarely | screw count, angles, direction σ, adjustment type, pitch/step size, radius; screw diagram; copy from/to real adapter settings |

Operate is always visible and on top. Inject and Configure are collapsed `Expander`s below it, each with a
one-line live summary in its header so you can verify without expanding. Reasoning: the loop lives in Operate;
everything else is glanceable state, not workspace.

## 1. Layout

One control, both hosts: (a) a section in the simulator camera's setup dialog (gear button;
`HocusFocusSimulatorCamera.HasSetupDialog` flips to true), (b) an Imaging-tab dockable ("Virtual Tilt Adapter")
docked beside the Aberration Inspector. Identical content; vertical stack; min width ~320 px; no horizontal
scrolling — the state strip and config summaries wrap.

### 3-screw variant

```
┌─ Virtual Tilt Adapter ────────────────────────────────┐
│ Plane:  Tilt 12.4 µm @ 214°   ·   Backfocus −5.0 µm   │   ← live state strip, values bold
│ Last: Screw 2 ⟳ 0.75 (S2 ⬇) — tilt 12.4→3.1 µm [Undo] │   ← last-action line (empty until first click)
│                                                       │
│ Amount per click  [ 0.75 |turns]   ≈ 262 µm axial     │
│                                                       │
│   Screw 1 · 0°      [ ⟲ ]  [ ⟳ ]                      │
│   Screw 2 · 120°    [ ⟲ ]  [ ⟳ ]                      │
│   Screw 3 · 240°    [ ⟲ ]  [ ⟳ ]                      │
│                                                       │
│ Net (turns):  1: +1.25   2: −0.50   3: 0.00  [Re-zero]│   ← accumulated position strip
│                                                       │
│ ▸ Injected aberration   (tilt 12.4 µm @ 214°, BF −5.0)│   ← collapsed expander
│ ▸ Adapter configuration (3 screws · 350 µm/turn ·     │   ← collapsed expander
│      R 27 mm · ⟳ → camera · matches adapter ✓)        │
└───────────────────────────────────────────────────────┘
```

### 4-screw variant — what changes

Only two things change: a **movement-type selector** appears above the rows, and the **rows change identity with
the selected mode**. Everything else (strips, expanders, amount box) is identical.

```
│ Amount per click  [ 0.50 |turns]   ≈ 175 µm axial     │
│                                                       │
│ Move:  (•) Corner    ( ) Side    ( ) Backfocus        │   ← segmented radio, one click to switch
│                                                       │
│   Screw 1 · 45°     [ ⟲ ]  [ ⟳ ]      3 opposes       │   ← Corner mode: rows are screws
│   Screw 2 · 135°    [ ⟲ ]  [ ⟳ ]      4 opposes       │
│   Screw 3 · 225°    [ ⟲ ]  [ ⟳ ]      1 opposes       │
│   Screw 4 · 315°    [ ⟲ ]  [ ⟳ ]      2 opposes       │
│                                                       │
│ Net (turns): 1: +0.50  2: 0.00  3: −0.50  4: 0.00     │
```

Side mode swaps the rows:

```
│ Move:  ( ) Corner    (•) Side    ( ) Backfocus        │
│   Side 1+2 · top      [ ⟲ ]  [ ⟳ ]    3+4 oppose      │
│   Side 2+3 · right    [ ⟲ ]  [ ⟳ ]    4+1 oppose      │
│   Side 3+4 · bottom   [ ⟲ ]  [ ⟳ ]    1+2 oppose      │
│   Side 4+1 · left     [ ⟲ ]  [ ⟳ ]    2+3 oppose      │
```

Backfocus mode is a single row:

```
│ Move:  ( ) Corner    ( ) Side    (•) Backfocus        │
│   All screws 1–4      [ ⟲ ]  [ ⟳ ]                    │
```

Stepper variant (either screw count): the buttons become `[ − ]  [ + ]`, the amount unit becomes `steps`, the
net strip shows steps, and no rotation glyphs appear anywhere — motors abstract rotation, per the glyph contract.

The `top/right/bottom/left` side hints are derived from the mean angle of the pair (image space) so they stay
correct under mirrored configurations. Reasoning: side names give a spatial anchor the pure screw numbers lack.

## 2. Interaction model

**One shared "Amount per click" box + two direction buttons per row.** A click applies
`amount × (direction from the clicked glyph)` to the row's screws, immediately updates the persisted sim state
(next exposure reflects it), and writes the last-action line.

- The amount box is a `UnitTextBox` whose unit follows `AdjustmentType` (`turns` / `steps`). **Validation:
  strictly positive.** Direction comes *only* from the buttons — banning negative amounts eliminates
  double-negative confusion (−0.5 with ⟲ meaning... what?). One number, one meaning.
- A dim computed hint beside the box shows the axial equivalent ("≈ 262 µm axial" = amount × pitch), so the user
  develops feel for magnitudes without doing arithmetic.
- Buttons: `⟳` = clockwise (tighten), `⟲` = counter-clockwise — same glyphs, same U+27F3/U+27F2, as the
  inspector's numeric guidance rows. Steppers: `+`/`−` matching the wizard-prompt signed-step convention. The
  panel's buttons speak exactly the language the inspector's cells speak; executing guidance is glyph-to-glyph
  pattern matching, not translation.
- Button tooltips state the **motion** consequence, computed live from σ: "⟳ 0.75 turns — screw-2 corner moves
  toward the camera (⬇); tilt tips toward screw 2's side." Buttons carry rotation; tooltips/feedback carry
  motion. This is the two-vocabulary contract applied to an input device.
- Default amount: 0.25 turns / 10 steps (a sane mechanical granularity). The box remembers its last value, so
  repeated equal-size moves are one click each.

**Click accounting for the canonical task** — inspector says "Screw 2: 0.50 ⟲":

1. Type `0.5` in the amount box (skip if already there).
2. Click `⟲` on the Screw 2 row.

That is **one text entry + one click**, or **one click** when the amount carries over. Stepper "−12 steps": type
`12`, click `−` on row 2 — same shape (the sign in the inspector cell *is* the button choice).

**Rejected alternative — per-row signed amount boxes with an Apply button:** more controls, requires the user to
internalize "+ = CW", enables batch mistakes with muddy attribution, and drifts toward auto-apply. One action per
click keeps cause→effect legible and makes Undo trivial.

**Undo:** a single-level `[Undo]` on the last-action line reverts the previous click (state and counters).
Reasoning: misclicks in a rapid loop must be free; clicking the opposite glyph only works if the amount box
wasn't touched in between.

## 3. The 4-screw movement-type selector

A three-way **segmented radio** (`Corner | Side | Backfocus`) directly above the rows — one click to switch, all
options permanently visible. This is a deliberate, flagged deviation from the ComboBox-for-enums idiom: a hot-path
mode switch shouldn't cost open-then-pick, and seeing all three modes teaches that they exist.

**One rule covers all three modes, stated once as a legend tooltip on the selector and in each row tooltip:**

> The screws named in the row turn in the clicked direction; their coupled opposites automatically counter-turn.
> (Backfocus: all four turn the same way — no counter-turn.)

- **Corner mode (default — it is the loop's workhorse):** rows are the four *screws*, not abstract "corners."
  Clicking ⟳ on "Screw 2" turns screw 2 ⟳ and screw 4 ⟲ by the same amount (pure diagonal tilt, backfocus
  untouched). The dim right-aligned "4 opposes" plus the net strip visibly counting screw 4 backwards teach the
  coupling on first use. Reasoning for screw-labeled rows: the inspector's guidance is per **screw number**; on a
  coupled adapter its tilt values are automatically pair-antisymmetric (δ₃ = −δ₁), so "Screw 1: 0.60 ⟳ / Screw 3:
  0.60 ⟲" maps to *one click on the Screw 1 row* — the user matches the leading screw and ignores the mirrored
  partner line. Labeling rows "Corner A/B/C/D" would force a screw→corner translation, which is exactly the
  arithmetic this panel exists to remove.
- **Side mode:** rows are adjacent pairs ("Side 1+2 · top"); clicking ⟳ turns both named screws ⟳ and the
  opposite side ⟲ (tilt about the edge axis, backfocus untouched). Side is kept per the product requirement and
  is genuinely useful for *injecting* clean horizontal/vertical tilts; it is not needed to execute inspector
  guidance (a side move = two equal corner moves).
- **Backfocus mode:** one row, "All screws 1–4"; ⟳ turns all four ⟳ (pure piston — backfocus/curvature changes,
  tilt untouched). The σ setting determines whether that raises or lowers `BackfocusErrorMicrons`, using the same
  `TiltScrewGeometry` sign anchors the wizard and inspector use.

Mode is remembered per session; switching modes never applies anything.

3-screw adapters show **no selector** — three independent screw rows, full stop. The inspector's Total row
already folds backfocus into per-screw totals for 3-screw rigs, so per-screw buttons execute everything.

## 4. Feedback — knowing you're converging

Four elements, top of panel, always visible:

1. **State strip** — `Plane:  Tilt 12.4 µm @ 214°  ·  Backfocus −5.0 µm`, values bold, same units and angle
   convention (0° = up, CW, image space) as the inspector. When tilt < 1 µm **and** |backfocus| < 1 µm it
   appends `✓ ≈ flat` — the loop's finish line. Thresholds are display-only constants; no configuration.
2. **Last-action line** — `Last: Screw 2 ⟳ 0.75 (S2 ⬇, S4 ⬆) — tilt 12.4 → 3.1 µm  [Undo]`. It names the
   rotation applied (input vocabulary), the resulting plate **motion** with honest ⬆/⬇ arrows (output
   vocabulary — continuous, low-stakes education about what σ means on this rig), and the before→after of
   whichever state values changed. Backfocus moves report `BF −5.0 → +0.2 µm` instead.
3. **Net-position strip** — per-screw accumulated turns/steps since last re-zero:
   `Net (turns): 1: +1.25 · 2: −0.50 · 3: 0.00 [Re-zero]`. This is the "pencil mark on the screw head." It shows
   coupling live (corner clicks count the partner in reverse), lets the user manually walk anything back, and
   answers "how far from where I started?" `Re-zero` **only re-bases this display** — tooltip says explicitly
   "does not move screws or change the simulated plane" (naming it *Re-zero*, not *Reset*, for the same reason).
   Internally counters store axial µm and render in the active unit, so a mid-session pitch edit re-scales the
   display rather than corrupting it (tooltip notes this).
4. **Convergence is read from the state strip trend**, deliberately *not* from a history graph: the loop is
   click → number shrinks → click. If the number grows, the last-action line + Undo make the wrong-direction
   hypothesis testable in two seconds. The Aberration Inspector next door remains the rich visualization; this
   panel never competes with it.

## 5. The closed loop — mapping inspector guidance onto the panel

Inspector numeric guidance → panel actions, with zero arithmetic:

| Inspector says | User does |
|---|---|
| 3-screw, Total row `Screw 2: 1.05 ⟳` | type `1.05`, click ⟳ on Screw 2 (repeat per screw) |
| 4-screw, Tilt row `S1: 0.60 ⟳ / S3: 0.60 ⟲` | Corner mode: type `0.60`, click ⟳ on Screw 1 (partner handled) |
| 4-screw, Backfocus row `0.30 ⟲` (per screw) | Backfocus mode: type `0.30`, click ⟲ once |
| Steppers, `Screw 2: −12 steps` | type `12`, click `−` on Screw 2 |

The only judgment call left to the human is the real one: on 4-screw rigs the inspector's per-screw backfocus
cells can differ slightly (curvature isn't perfectly common-mode); the sim's backfocus is a scalar, so the user
applies a representative value — exactly the approximation they'd make with a screwdriver.

**No "Apply inspector's suggestion" button. Opinionated call:** auto-apply would read the correction from the
same calculator that produced it and push it straight into the model, bypassing the two things this rig exists
to validate — the human-readable presentation (glyphs, signs, row mapping) and the human's execution of it. A
presentation sign bug would sail through auto-apply and be caught by the manual loop. End-to-end
guidance→physics consistency absolutely should be verified automatically — as a unit/capstone test against
`TiltScrewGeometry`, not as a UI affordance. If a future need arises (bulk regression demos), it belongs behind
a debug flag, not on this panel.

What the panel *does* do for loop speed: guidance values paste cleanly (unsigned magnitudes for screws — the
box takes them verbatim), rows are ordered and named identically to guidance columns, and the two surfaces are
designed to sit side by side in the Imaging tab.

## 6. Config vs operate

**Operate** (described above) is the permanent top of the panel. **Two collapsed expanders** sit below, each
with a live summary in its header:

```
▸ Injected aberration   (tilt 12.4 µm @ 214° · BF −5.0 µm)
▸ Adapter configuration (3 screws · Screws · 350 µm/turn · R 27 mm · ⟳ → camera · matches adapter ✓)
```

**Injected aberration** (expanded on demand): the three existing `UnitTextBox`es — Tilt Azimuth (°), Tilt Amount
(µm), Backfocus Error (µm) — bound to the same persisted options as the camera options page, plus a
`Zero all aberrations` button (instant flat plane for starting a fresh scenario). Direct edit *is* injection;
screw clicks and these boxes mutate the same three values, and the state strip is the single source of truth
above both.

**Adapter configuration** (expanded on demand; auto-expanded while invalid):

- Screw count (ComboBox 3/4), Adjustment type (Screws/Stepper motors), Thread pitch (µm/turn) *or* Stepper step
  size (µm/step) — only the active one shown, Screw radius (mm), per-screw angle boxes (°, image space), and
  Direction as a mechanical-words ComboBox: `⟳ tighten moves adapter toward: [camera | objective]` (writes
  `ScrewInwardCurvatureSign` via `TiltScrewGeometry.CurvatureSignForCwDirection`). Never expose "+1/−1" to the
  user — the words are the meaning.
- **The reused `HF_TiltScrewDiagram`** renders live from the entered angles — the user sees screw 2 land where
  they typed it, in image space, with the existing "top of image" caption doing the mirroring education.
  Reasoning for putting the diagram here and not in Operate: executing "screw 2 ⟳" requires no spatial
  reasoning; verifying angles does. Operate rows carry the angle inline (`Screw 2 · 135°`) for quick reference.
- **4-screw angle entry: screws 1 and 2 are editable; 3 and 4 render dimmed as "+180°" derived values.**
  Opposite screws on a coupled adapter are 180° apart by construction; deriving them kills an entire class of
  config typos.
- `Auto-fill evenly` link: one click writes 0/120/240° (3-screw) or 45/135/225/315° (4-screw). For a simulated
  rig the exact angles rarely matter; this gets a valid config in one click.
- **Coherence with the real adapter settings** — the critical, non-obvious requirement: the inspector computes
  its guidance from `TiltAdapterOptions` (the user's *real* adapter calibration), while the simulator obeys this
  panel's own persisted fields (stored in `CameraSimulatorOptions`, **never** written into `TiltAdapterOptions`
  implicitly — clobbering a user's real rig calibration from a simulator panel would be unforgivable). The loop
  converges only when the two agree. So the config header carries a live badge:
  - `matches adapter ✓` when count/angles/pitch/σ/radius agree within tolerance (angles ±2°, pitch ±5%);
  - `⚠ differs from adapter settings` (tooltip lists the differing fields) otherwise;
  and the expander offers two explicit one-way copies: `Copy from adapter settings` and
  `Copy to adapter settings…` (the latter with a confirmation naming the fields it will overwrite, and marking
  the result as manual calibration the same way the wizard's manual-entry path does). Deliberate mismatch stays
  *possible* — it is itself a test scenario (e.g., flip σ to verify the inspector's arrow/glyph robustness
  property) — but never *silent*.

## 7. Error and edge states

Non-blocking banners at the top of Operate; controls disable rather than hide; nothing modal — a rapid-click
loop must never be interrupted by dialogs.

| State | Behavior |
|---|---|
| Pitch/step size unset (−1) or radius unset | Operate rows + amount box disabled; banner "Set thread pitch and screw radius to enable the adapter"; config expander auto-expands with the offending boxes highlighted (standard validation-error styling) |
| Screw angle NaN/invalid | Same pattern; `Auto-fill evenly` offered in the banner itself (one-click fix) |
| 3-screw with stale Screw4 angle | Screw 4 row never shown nor read when count = 3; switching 4→3 writes Screw4 = NaN (mirrors the existing convention); switching 3→4 derives/suggests angles rather than resurrecting stale ones |
| Screws ↔ Steppers switch | Amount unit, button glyphs (⟳⟲ ↔ +−), and net-strip unit all swap atomically; counters persist (stored in µm) and re-render in the new unit |
| Pitch edited mid-session | Net strip re-scales (µm canonical); tooltip on the strip explains |
| Aberrations disabled (`EnableAberrations` false) | Banner "Aberrations are disabled — the plane below has no effect on rendered frames" + inline `Enable` button; operate stays enabled (state edits are legal, just inert) |
| Connected camera isn't the HF simulator (dockable host) | Operate disabled; banner "Connect the Hocus Focus simulated camera to use the virtual adapter" |
| Absurd move (click would push tilt or BF past the persisted bounds, ±10 000 µm) | Apply clamped, last-action line appends "(clamped)"; additionally a passive warning badge on the state strip when tilt amount > 500 µm: "extreme tilt — expect heavy donuts". Never confirm-dialog: extreme states are legitimate detector-stress scenarios, and Undo is free |
| σ never set | Impossible by construction (defaults to +1 = ⟳ → camera); the direction ComboBox always shows a concrete mechanical meaning. No "(assumed)" suffix here — manual entry is authoritative for a simulated rig |

## 8. Top 3 UX risks and mitigations

1. **Direction/sign confusion** — the user clicks ⟳ expecting the tilt to shrink and it grows (the classic
   tilt-adapter failure, now with a σ setting in the mix). Mitigations: one shared rotation vocabulary with the
   inspector (glyph-to-glyph transcription, no sign conversion anywhere); direction configured in mechanical
   words, never ±1; button tooltips and the last-action line express consequences in honest motion arrows
   (⬆/⬇), so every click teaches the σ mapping; wrong guesses cost two seconds (state strip trend + free Undo).
2. **4-screw coupling surprise** — the user doesn't expect screw 4 to move when they click screw 2, or hunts for
   "which corner is corner 2." Mitigations: rows are labeled by screw number (the inspector's vocabulary), the
   coupled partner is named on the row ("4 opposes"), the net strip visibly counts the partner in reverse on the
   very first click, and one sentence — "named screws follow the glyph; opposites counter-turn" — governs all
   three modes. Corner is the default mode because it is what inspector guidance maps onto.
3. **Silent config incoherence** — the sim adapter's geometry drifts from the real `TiltAdapterOptions` the
   inspector uses, and the loop mysteriously refuses to converge (worst version: user "fixes" it by breaking
   their real calibration). Mitigations: separate persisted sim fields with an always-visible ✓/⚠ coherence
   badge in the config summary, explicit one-way copy buttons (the write-to-real direction confirmed and
   labeled), and deliberate mismatch preserved as a supported test scenario rather than prevented.

## Implementation pointers (for the executing developer, not part of the UX)

- New persisted fields live in `CameraSimulatorOptions` (e.g. `SimScrewCount`, `SimScrew1..4AngleDegrees`,
  `SimScrewInwardCurvatureSign`, `SimAdjustmentType`, `SimThreadPitchMicrons`, `SimStepperStepSizeMicrons`,
  `SimScrewRadiusMillimeters`) — every one gets a control per the options-system invariant (they all appear in
  this panel, which is referenced from `Resources/OptionsDataTemplates.xaml` for the setup-dialog host).
- Click → state delta goes through `TiltScrewGeometry` (screw position, forward gradient, σ anchors) so the
  virtual adapter and the inspector can never disagree about conventions; pin the round-trip (inject → guidance
  → apply guidance → ≈ flat) in a capstone test for both screw counts, both adjustment types, and both σ values.
- Dockable host follows the existing DockableVM MEF pattern (`.claude/docs/mef-and-bootstrap.md`);
  `HF_TiltScrewDiagram` is consumed via `ContentTemplate` exactly as the wizard does.
