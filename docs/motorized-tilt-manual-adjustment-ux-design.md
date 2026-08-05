# Manual Adjustment UX Spec — Motorized Tilt Adapter (ASG EAT)

**Status:** approved 2026-08-04. Produced with Fable (`claude-fable-5`) against the actual XAML/VMs.
**Scope:** UX only — no XAML, no C#. The panel lives in the Tilt Adapter Wizard dockable, Panel A,
inside the "Motorized Device Connection" GroupBox. Implementation plan (architecture, reuse,
correctness anchors, tests): `plans/motorized-tilt-manual-adjustment-plan.md`.

## 1. Scope and placement

A new collapsed-by-default **Manual adjustment** section for driving a connected ASG EAT tilt
adapter by hand. It lives inside `GroupBox Header="Motorized Device Connection"` in Panel A
(`!IsWizardRunning`), **directly below the live motor-positions grid**
(`HF_TiltDevicePositionsGrid`) and **above the "Safety Limits" header**. Rationale: the positions
grid is the shared state display both modes read against, so the adjustment controls sit
immediately under it; the safety-limit fields it enforces stay below.

Because Panel A is hidden entirely while the wizard runs, this section can never be on screen
during a wizard run — no special coexistence state is needed beyond the device lease (§4.3).

Two modes, switched by a segmented radio (same idiom as the simulator's Corner/Side/Backfocus
radios):

1. **Single move** — pick corner / side / all on a 3×3 pad, a direction, an amount; live inline
   preview; one click sends. No modal.
2. **Target positions** — four absolute per-motor targets in the 2×2 corner arrangement,
   prefilled from live positions; submit opens the existing `TiltDeviceAdjustmentPromptControl`
   modal with the decomposed move queue and residual analysis.

## 2. The expander

Follows the simulator's collapsed-expander pattern (`TiltAdapterDataTemplates.xaml` ~489):
bold header text + dim one-line summary sibling in the header; `IsExpanded` two-way bound to a
session-scoped VM property (default **false**); a getter that force-opens it when there is
something the user must see.

**Header text:** `Manual adjustment` (bold), followed by the dim summary.

**Collapsed summary line** (dim, one line, priority order — first match wins):

| State | Summary text |
|---|---|
| Send from this panel in progress | `Sending move 2 of 3…` (expander is force-open anyway; header still reads this) |
| Last send failed, not yet dismissed | `Last move failed — see details` |
| Device busy with another operation | `Device busy — Tilt Adapter Wizard Calibration` (uses `CurrentOperationName`) |
| Not connected | `Connect to enable` |
| Connected, something sent this session | `Last sent: TR +20 · BL −20 at 21:14` |
| Connected, nothing sent yet | `Nudge a corner or set target positions` |

**Force-open rule:** the expander reports expanded (regardless of the user's toggle) while
(a) a send started from this panel is executing, or (b) the last send ended in failure and the
failure panel has not been dismissed. Otherwise the user's last toggle sticks for the session.

## 3. Panel skeleton (both modes)

Top to bottom inside the expander:

1. **Caution line** (dim, italic, wraps): `Moves are physical and the device remembers every
   move (stored in EEPROM). There is no automatic undo.`
2. **Mode switch** — segmented radio, sibling label `Mode`, options `Single move` ·
   `Target positions`. Selection persists for the session; default `Single move`.
3. **Mode body** (§5 or §6).
4. **Shared status area** (§7): progress line + Stop button during sends; success line or
   failure/stopped result panel after; hidden when idle with nothing to report.

When the device is **not connected**, items 2–4 are replaced by a single dim line:
`Connect the device above to make adjustments.` (An undismissed failure panel from a send that
ended in disconnect stays visible above this line — §7.5.)

## 4. Shared state layer

### 4.1 Live positions
The existing 2×2 positions grid directly above the expander is the single source of truth for
"where the motors are now". The panel never duplicates it as a static copy; the Single-move
preview (§5.5) and Target cells (§6.2) render *predictions/targets* in the same 2×2 spatial
arrangement so the three surfaces read as one map. All position-derived text in the panel
(previews, deltas, hints) recomputes automatically whenever the polled positions change.

### 4.2 Positions unknown
`PositionsKnown == false` (never confirmed since connect, or unparsed `cp`):
- Positions grid already reads `unknown` per corner (existing).
- **Single move:** allowed, with an amber advisory (§5.7) — matches the existing modal's
  treatment of unknown positions as advisory, not blocking. Predicted end positions read
  `unknown → unknown`.
- **Target positions:** blocked (§8) — an absolute target needs a known starting point.

### 4.3 Device busy (exclusive lease)
Any active operation lease (`IsOperationActive`) disables both primary buttons with reason
`Device busy — {CurrentOperationName}. Wait for it to finish.` Sends from this panel take the
lease with operation name **`Manual Adjustment`**, so the existing status line reads
`Connected on COM7 — Manual Adjustment` while it runs, and every other surface (wizard,
inspector) is excluded for the duration.

### 4.4 Disconnected
§3's replacement line. The expander header summary reads `Connect to enable`.

## 5. Mode 1 — Single move

### 5.1 Layout

```
What to move                         (sibling label)
   ┌──────┬──────┬──────┐
   │  TL  │ Top  │  TR  │            3×3 pad of toggle buttons,
   ├──────┼──────┼──────┤            spatial = sensor as imaged in NINA
   │ Left │ All  │ Right│            (same orientation as the positions grid)
   ├──────┼──────┼──────┤
   │  BL  │Bottom│  BR  │
   └──────┴──────┴──────┘
Direction   [ + ] [ − ]              segmented pair, sibling label
Amount      [   10 ] steps  = 18.0 µm of screw travel
┌─ Preview ────────────────────────────────────────────┐
│ [Corner]  TR (Motor 1) +20 · BL (Motor 4) −20 steps   tr,20 │
│   TL · M2  512 → 512        TR · M1  480 → 500 (+20)  │
│   BL · M4  470 → 450 (−20)  BR · M3  505 → 505        │
│ Changes TR-vs-BL spacing by 72 µm of screw travel.    │
│ + = clockwise / tighten (the wizard's positive        │
│ direction); − = the opposite. 1 step = 1.8 µm.        │
│ 1 move · ~10 s                        [ Send 1 move ] │
└──────────────────────────────────────────────────────┘
```

### 5.2 The pad
Nine toggle buttons in a 3×3 grid (~40 px cells; a thin border around the grid so it reads as
the sensor). Cell texts: `TL` `Top` `TR` / `Left` `All` `Right` / `BL` `Bottom` `BR`. Exactly one
cell selected at a time; **no default selection** — until one is picked the preview area shows
only the dim line `Pick a corner, side, or All to move.` and Send is disabled. Selection,
direction and amount all persist for the session (repeat-nudging = click Send again).

Cell semantics (direction `+`; `−` is the same move the other way):

| Cell | Kind | Motors + | Motors − | Wire (dir +) |
|---|---|---|---|---|
| TR | Corner | Motor 1 (TR) | Motor 4 (BL) | `tr,N` |
| TL | Corner | Motor 2 (TL) | Motor 3 (BR) | `tl,N` |
| BL | Corner | Motor 4 (BL) | Motor 1 (TR) | `bl,N` |
| BR | Corner | Motor 3 (BR) | Motor 2 (TL) | `br,N` |
| Top | Side | Motors 2 & 1 (TL, TR) | Motors 4 & 3 (BL, BR) | `tp,N` |
| Bottom | Side | Motors 4 & 3 (BL, BR) | Motors 2 & 1 (TL, TR) | `bt,N` |
| Right | Side | Motors 1 & 3 (TR, BR) | Motors 2 & 4 (TL, BL) | `rt,N` |
| Left | Side | Motors 2 & 4 (TL, BL) | Motors 1 & 3 (TR, BR) | `lt,N` |
| All | Backfocus | all four | — | `bf,N` |

The rule the pad teaches: *the named element gets the signed amount; its coupled opposite gets
the negative* (except All, where all four get the signed amount). The preview makes the
coupling unmissable on every selection — the user who clicks `TR` sees BL move too, every time,
before anything is sent.

Pad tooltips (text equivalents for every cell; also `AutomationProperties.Name`):
- `TR`: `Tilts along the TR–BL diagonal. Motor 1 (TR) gets the signed amount; Motor 4 (BL) gets the opposite. One command.`
- `TL`: `Tilts along the TL–BR diagonal. Motor 2 (TL) gets the signed amount; Motor 3 (BR) gets the opposite. One command.`
- `BL`: `Tilts along the TR–BL diagonal. Motor 4 (BL) gets the signed amount; Motor 1 (TR) gets the opposite. One command.`
- `BR`: `Tilts along the TL–BR diagonal. Motor 3 (BR) gets the signed amount; Motor 2 (TL) gets the opposite. One command.`
- `Top`: `Tilts top vs bottom. Motors 2 & 1 (TL, TR) get the signed amount; Motors 4 & 3 (BL, BR) get the opposite. One command.`
- `Bottom`: `Tilts top vs bottom. Motors 4 & 3 (BL, BR) get the signed amount; Motors 2 & 1 (TL, TR) get the opposite. One command.`
- `Right`: `Tilts right vs left. Motors 1 & 3 (TR, BR) get the signed amount; Motors 2 & 4 (TL, BL) get the opposite. One command.`
- `Left`: `Tilts right vs left. Motors 2 & 4 (TL, BL) get the signed amount; Motors 1 & 3 (TR, BR) get the opposite. One command.`
- `All`: `Moves all four motors together by the signed amount — changes sensor spacing (backfocus), not tilt. One command.`

### 5.3 Direction
Segmented pair of toggle buttons `+` and `−` (exactly one selected; default `+`), sibling label
`Direction`. No up/down words anywhere — whether + physically raises or lowers a corner is
rig-dependent, exactly the reason the approval modal speaks signed steps. The legend line in
the preview carries the meaning. Tooltips / automation names:
- `+`: `The wizard's positive step direction (clockwise / tighten) on the highlighted motors; the coupled motors move the opposite way.`
- `−`: `The opposite of the wizard's positive step direction on the highlighted motors.`

### 5.4 Amount
Numeric box, sibling label `Amount`, unit `steps`, strictly positive integer (1 …
`TiltDeviceMaxExcursionSteps`), default **10**, updates on focus loss / Enter (codebase
convention). Sibling dim hint: `= 18.0 µm of screw travel` (amount × 1.8, one decimal; hidden
if the preset's step size is unknown). One number, one meaning — sign lives in Direction
(same reasoning as the simulator's strictly-positive amount-per-click).
Tooltip: `How far each affected motor moves, in device steps. 1 step = 1.8 µm of screw travel. Amounts above the per-command cap ({TiltDeviceMaxStepsPerCommand}) are sent as several same-direction commands.`

### 5.5 Inline preview (always current, recomputes on any input or position change)
A bordered block, top to bottom:

1. **Semantic line** with kind badge: `[Corner] TR (Motor 1) +20 · BL (Motor 4) −20 steps`,
   `[Side] Top: TL +20 · TR +20 · BL −20 · BR −20 steps`,
   `[Backfocus] All four motors +20 steps together`. Right-aligned dim monospace wire chip
   (`tr,20`), tooltip `The exact command string sent to the device.` — same demotion as the modal.
2. **Predicted end positions**, 2×2 grid mirroring the positions grid: each cell
   `TL · M2  512 → 512` (dim, unchanged) or `TR · M1  480 → 500 (+20)` (emphasized, moving).
   With positions unknown: `unknown → unknown`. Per-cell tooltip:
   `Travel window 0 to {max}. This move ends at {p}, leaving {max−p} to the max and {p} to zero.`
   Per-cell limit tag (text, not color alone): `near max` / `near 0` (amber) when the predicted
   position is within 5 % of the travel window of either end; `below 0` / `over max` (red) when
   outside — red blocks Send (§8).
3. **Consequence line** (rig-safe, no toward/away claims):
   - Corner: `Changes TR-vs-BL spacing by 72 µm of screw travel.`
   - Side: `Changes top-vs-bottom spacing by 72 µm of screw travel.` (or right-vs-left)
   - All: `Changes sensor spacing by 36 µm; tilt unchanged.`
4. **Legend** (dim): `+ = clockwise / tighten (the wizard's positive direction); − = the opposite. 1 step = 1.8 µm.`
5. **Footer**: `{n} move(s) · ~{duration}` + primary button `Send 1 move` / `Send {n} moves`
   (n > 1 only when the amount exceeds the per-command cap and is split into same-axis
   commands). Duration = commands × (move + settle), formatted like the modal (`~10 s`,
   `~1 min`). When Send is disabled, a single reason line (§8) sits directly above the button.

### 5.6 No bias, no decomposition
Single move sends **exactly the one generator move requested** — never an auto-prepended
backfocus bias. If the move would drive a motor below 0, Send is blocked with the reason in §8
(`…send an All + move first to lift all motors.`). The user stays in full control of every step
sent; the "lift first" recovery is one pad click away.

### 5.7 Single-move advisories (amber one-liners above the footer)
- Positions unknown: `Motor positions are unknown — travel limits can't be checked for this
  move. Verify positions in the vendor app before sending.` (Send stays enabled.)
- Near limit: covered per-cell by the `near max` / `near 0` tags; no separate banner.
- No assumed-direction advisory here: a nudge is commanded in signed steps, and no copy in this
  mode asserts a physical toward/away direction, so nothing is assumed that could be wrong. The
  advisory remains where intent-vs-hardware mismatch is possible: the Target-mode modal.

### 5.8 Repeat-nudge feel
After a successful send: selection, direction, and amount are untouched; the positions grid and
preview update from the per-move position report; the button re-enables after settle + refresh.
Repeating the same nudge is a single click. The status area's success line (§7.3) doubles as the
collapsed-summary "Last sent" source.

## 6. Mode 2 — Target positions

### 6.1 Concept
`Type where each motor should end up, in device steps. Current positions are filled in to
start.` (caption, dim, under the mode switch). The user edits absolute targets; the panel shows
the per-corner delta, how many moves it becomes, and whether the four numbers contain twist the
adapter cannot make. `Review moves…` opens the existing approval modal — the same analysis + UI
used during automatic tilt adjustment — seeded with the delta (target − current at open).

### 6.2 Target cells
2×2 grid, same arrangement and headers as the positions grid. Each cell:

```
TL · Motor 2          (bold)
Wizard screw 2        (dim, small)
now 512               (dim, live — updates with the poll)
Target: [  550 ]      (integer TextBox, updates on focus loss / Enter)
Δ +38 (68.4 µm)       (accent when ≠ 0, dim when 0)
will reach 546        (dim amber; only when the request contains twist, §6.4)
```

Prefill: on entering Target mode, and after a completed run (§7.4), targets fill from live
positions. Background position changes (idle poll, another surface's moves) update the `now`
line and recompute Δ/hints but **never overwrite typed targets** — the plan is always
target − current-at-review, so stale-looking targets stay honest.

Validation per cell: whole number in [0, `TiltDeviceMaxExcursionSteps`]; out-of-range or
non-numeric shows the standard red validation and blocks Review (§8).

Buttons row: secondary `Reset to current` (tooltip: `Fill every target from the current motor
positions.`) + primary `Review moves…` (ellipsis = opens the review dialog; nothing is sent
without it).

### 6.3 Pre-submit hint line
Runs the same planner the modal uses (both groups on) on the current delta; one line under the
cells, recomputed with Δ:
- Normal: `Becomes 2 moves · ~20 s.`
- Nothing to do: `Targets match the current positions — nothing to move.` (Review disabled.)
- Planner hard limit (rare — targets are already range-checked, but move ordering can still be
  unreachable): red line `These targets can't be reached without leaving the travel window
  (0 to {max}).` Review **stays enabled** so the user can open the modal and read the full
  blocking explanation; Proceed is disabled there. Review is inspection; Proceed is the guarded
  action.
- If the plan includes a lift: `Becomes 3 moves · ~30 s, including a +{b}-step lift to keep all
  motors above 0.` (the modal's bias warning gives the full story).

### 6.4 Twist — four numbers, three degrees of freedom
The delta decomposes as a = (s1−s3)/2, b = (s2−s4)/2, f = mean, t = (s1−s2+s3−s4)/4. When
|t| ≥ 1 step, an amber panel appears under the hint line:

> **Twist can't be made**
> `These four targets differ from a rigid plane by ±{t} steps (about {t×1.8:0.0} µm across the
> sensor). The adapter can tilt the sensor and change its spacing, but it cannot twist it. Each
> corner above shows the position it will actually reach.`

and each cell gains the `will reach {n}` line (target minus its twist share, t·(+1,−1,+1,−1) on
wizard screws 1..4, rounded). The user learns *before* the modal that they typed an unreachable
shape and exactly what they will get instead; the modal's residual grid then confirms it in µm
(including step rounding).

### 6.5 The review modal (reused as-is)
`Review moves…` opens `TiltDeviceAdjustmentPromptControl` unchanged: title + no-undo caution;
Apply `Tilt correction` / `Backfocus correction` toggles that live-replan (here: tilt = the a/b
components, backfocus = the f component of the user's delta); MOVES TO SEND with badges,
semantic rows, wire chips; the 2×2 residual grid; the severity-ordered warning stack (travel
limit, assumed backfocus direction, twist, bias, pitch mismatch — positions-unknown never fires
here because Target mode requires known positions); footer with count, duration, disabled
reason, Cancel, `Send N moves`.

Cancel returns to the panel with targets preserved. Proceed starts execution (§7).

## 7. During and after a send (both modes)

### 7.1 Progress
The shared status area shows one line per in-flight command, prefixed with the plan position:
`Move 2 of 3 — {controller progress text}` (e.g. `Move 2 of 3 — sending bf,50… settling (3 s)`).
The primary buttons, pad, direction, amount, and target boxes are disabled for the duration; the
expander is force-open. The GroupBox status line reads `Connected on COM7 — Manual Adjustment`.

### 7.2 Stop (multi-move sends only)
A `Stop after this move` button appears beside the progress line whenever more than one command
remains. Tooltip: `The move already sent can't be recalled. Nothing further will be sent.`
There is no device abort — this only skips unsent moves (cancellation is honored before each
send). After stopping, an amber result panel:

> **Stopped after {k} of {n} moves**
> `The moves already sent are applied — there is no automatic undo. Nothing after move {k} was
> sent. Positions above show where the motors are now.`

Single-command sends show no Stop button.

### 7.3 Success
Positions refresh per move from the move response (no extra query); the grid and every
position-derived readout update as the plan runs. On completion the status area shows a dim
success line, which persists until the next action and feeds the collapsed summary:
- Single move: `Sent TR +20 · BL −20 · 1 move · finished at 21:14.`
- Targets: `Adjustment complete — 3 moves sent · finished at 21:22.` Targets re-prefill from
  the reached positions, so the cells now read Δ 0 (± the residual the modal predicted).

### 7.4 Failure and partial failure
A command failure (write timeout / no valid response) leaves device state ambiguous and the
plugin's counters un-advanced — the red result panel says exactly that and stays until
dismissed (`Dismiss` button; expander force-open until then):

- Single command failed:
  > **Move failed**
  > `The command '{wire}' got no valid response. It may or may not have reached the device, so
  > the position counters above were not advanced — they re-sync on the next successful poll.
  > If in doubt, check the vendor app before sending more moves.`
- Multi-move plan, k of n sent (target mode):
  > **Adjustment stopped after {k} of {n} moves**
  > `The first {k} moves were sent and are applied — there is no automatic undo. Move {k+1}
  > ('{wire}') failed and nothing after it was sent. Once the positions above re-sync, press
  > Review moves — the plan recomputes from wherever the motors actually are, so it will
  > finish the remainder.`
  (This recovery is the payoff of absolute targets: re-planning from refreshed positions
  naturally yields "the rest of the journey".)
- Split nudge, k of n commands sent:
  > **Sent {k} of {n} moves ({k×cap} of {amount} steps)**
  > `The steps already sent are applied — there is no automatic undo. The remaining
  > {amount − k×cap} steps were not sent; set Amount to {remaining} and press Send to finish.`

### 7.5 Disconnect mid-send
The in-flight command fails as above; the panel body flips to the disconnected line (§3) but
the failure panel remains above it until dismissed, so the explanation survives the state
change.

## 8. Disabled/blocked states — exact reasons

One reason line at a time, directly above the mode's primary button; highest row wins.
(Red = blocking validation styling; plain = dim.)

| # | Condition | Applies to | Reason line |
|---|---|---|---|
| 1 | Not connected | both | (panel body replaced) `Connect the device above to make adjustments.` |
| 2 | Operation lease held elsewhere | both | `Device busy — {CurrentOperationName}. Wait for it to finish.` |
| 3 | Send from this panel in progress | both | (no reason line — the progress line explains it) |
| 4 | No pad cell selected | Single move | `Pick a corner, side, or All to move.` |
| 5 | Amount empty / < 1 / not a whole number | Single move | `Enter an amount of at least 1 step.` |
| 6 | Predicted position < 0 | Single move | (red) `This move would drive Motor {n} ({corner}) below 0. Reduce the amount, or send an All + move first to lift all motors.` |
| 7 | Predicted position > max excursion | Single move | (red) `This move would drive Motor {n} ({corner}) past the max excursion ({max}). Reduce the amount, or lower all motors with an All − move first.` |
| 8 | Positions unknown | Target positions | `Motor positions are unknown — targets need a known starting point. They update after a successful poll or move.` |
| 9 | Any target invalid / out of range | Target positions | (red, per-cell validation plus) `Targets must be whole numbers between 0 and {max} steps.` |
| 10 | All deltas zero | Target positions | `Targets match the current positions — nothing to move.` |

Positions unknown in Single move is an **advisory**, not a block (§5.7). Planner hard-limit in
Target mode does **not** disable Review (§6.3); it disables Proceed in the modal.

## 9. Copy inventory (everything not already quoted above)

- Expander header: `Manual adjustment`
- Mode label: `Mode` · options `Single move`, `Target positions`
  - `Single move` tooltip: `Send one adjustment now: a corner, a side, or all four motors together.`
  - `Target positions` tooltip: `Type where each motor should end, review the moves it takes to get there, then send them.`
- Pad label: `What to move`
- Amount unit: `steps`; µm hint format: `= {amount×1.8:0.0} µm of screw travel`
- Send button: `Send 1 move` / `Send {n} moves`
- Stop button: `Stop after this move`
- Reset button: `Reset to current`
- Review button: `Review moves…`
- Dismiss button (result panels): `Dismiss`
- Caution line: `Moves are physical and the device remembers every move (stored in EEPROM). There is no automatic undo.`
- Wire chip tooltip: `The exact command string sent to the device.`
- All µm values one decimal with `µm`; durations via the modal's formatter (`~10 s`, `~1 min 35 s`); step values signed (`+20`, `−20`) where they are deltas, unsigned where they are positions.

## 10. Accessibility and theming

- **No CheckBoxes** in this panel (NINA's themed CheckBox often fails to render
  `CheckBox.Content`); the two-state controls here are RadioButtons and ToggleButtons, whose
  content renders. Every caption is still a **sibling TextBlock**, never control Content, for
  the labels (`Mode`, `Direction`, `Amount`, `What to move`).
- Every pad cell, direction toggle, and mode radio carries `AutomationProperties.Name` with the
  full text of its tooltip's first sentence (e.g. `Top-right corner — tilts along the TR–BL diagonal`).
- No color-only signals: `near max` / `below 0` / `will reach {n}` are text tags with the amber/red
  styling on top, and the moving-vs-unchanged distinction in the predicted grid pairs weight
  (bold + signed delta) with the color.
- Panel A renders in the active NINA theme (unlike the modal's self-contained dark palette):
  every locally-styled `TextBlock.Style` must be `BasedOn={StaticResource StandardTextBlock}`
  (documented theme hazard at the top of `DataTemplates.xaml`), and the amber/red panel brushes
  need light-theme-legible counterparts rather than reusing the modal's fixed dark-background
  hex values.
- All interactive controls tab-reachable in reading order: mode → pad (row-major) → direction →
  amount → preview button / target cells (TL, TR, BL, BR) → reset → review.
- Glyph-free: `+`, `−`, `⟳` (existing rescan button) are the only symbols, each with a text
  tooltip; no icon-only actions.

## 11. Edge cases

- **Options changed mid-session** (per-command cap, max excursion, settle): previews, splits,
  durations, and limit checks recompute immediately; an in-flight plan keeps the values it was
  planned with.
- **Amount > per-command cap** in Single move: allowed; preview and button state the split
  (`2 moves · ~20 s`, `Send 2 moves`).
- **Positions become known mid-edit** (first successful poll): Single-move advisory clears and
  predictions populate; Target mode unblocks and prefills (only cells the user hasn't edited).
- **Another surface moves the motors while Target mode has edits**: `now` values and deltas
  update live; typed targets untouched (§6.2).
- **Settle time set to 0**: durations shorten accordingly; no other change.
- **Decomposition halves land on .5 steps** (odd corner deltas): planner rounding applies; the
  panel's `will reach` and the modal's residual grid absorb it — no special copy.
- **Expander collapsed when a background failure would matter**: impossible — sends only start
  from this panel, and it force-opens for the whole send and any undismissed failure.

## 12. Concerns

1. **One-click sends with no undo (accepted decision, flagged residual risk).** The inline
   preview + hard travel blocks are the whole safety net for Single move — there is no
   confirmation step. Mitigations baked in: no default pad selection (first send requires a
   deliberate pick), small default amount (10 steps = 18 µm), the predicted-positions grid, and
   red hard-stops at the travel window. If field feedback shows misfires, the cheapest retrofit
   is a hold-to-confirm on the Send button, not a modal.
2. **Nudging with unknown positions is allowed** (consistent with the existing modal's
   advisory-only treatment), but here there is no modal gate in front of it. A defensible
   stricter rule would block Single move until one successful position poll after connect. The
   spec keeps consistency with the modal; flagging in case the implementer/user prefers strict.
3. **Single move never auto-prepends a backfocus bias** (§5.6), unlike the automatic-adjustment
   path. This is deliberate — "single adjustment" should mean exactly one generator move — but
   it is a behavioral asymmetry between the two paths that the reason-line copy has to teach
   (`…send an All + move first to lift all motors.`).
4. **Vocabulary spans three index spaces.** The panel leads with corner + motor number
   (matching the positions grid); the modal's move rows lead with wizard screw numbers. The
   modal already appends corners (`Screw 1 (TR)`), so the join exists, but a user reading a
   Target-mode plan still crosses vocabularies once. Unifying the modal rows to corner-first is
   out of scope here (it is shared with the wizard/inspector flows) — noted, not redesigned.
5. **Twist by accident is easy in Target mode** — any hand-typed four numbers almost always
   contain some t. The ≥ 1-step threshold keeps rounding noise quiet, and the per-cell
   `will reach` line shows the honest outcome pre-review; still, users expecting four exact
   positions will sometimes get four near-misses, and the copy has to carry that expectation.
