# Pre-Release UI Feedback — Design

**Status:** Approved (design); implementation plan to follow in `plans/ui-feedback-prerelease-plan.md`.
**Date:** 2026-06-23

## Purpose

Three pieces of UI feedback to incorporate before the next release:

1. **Review UI layout** — in the review UIs, the header and footer should span the full window
   width, the right pane (legend) should size to its content height, and the image canvas should take
   the remaining space.
2. **Tilt adapter wizard replay** — the wizard currently exposes two replay buttons; with the recently
   added replay-settings modal ("ReplayVM"), consolidate to a single button driven by that modal.
3. **AutoFocus button rename** — rename the "Reprocess Saved Run" button to **"Replay Saved AF"**.

This is pre-release polish. The guiding constraint throughout is **lowest-risk change that satisfies
the feedback**: per-file edits over refactors, no changes to window default sizes, follow existing
XAML/VM idioms.

---

## Item 1 — Review UI layout

### What exists today

Four "review" surfaces display an image with overlays plus a legend:

| # | Control | Root | Footer? |
|---|---|---|---|
| 1 | `AutoFocus/Review/AutoFocusFrameReviewControl.xaml` | `ReviewViewportHostBase` | no |
| 2 | `StarDetection/Optimization/Review/FrameReviewControl.xaml` (Inspector) | `ReviewViewportHostBase` | no |
| 3 | `StarDetection/Optimization/Review/StarReviewControl.xaml` | plain `UserControl` | yes (help/counts) |
| 4 | Optimizer wizard `DataTemplate` (`StarDetection/Optimization/DataTemplates.xaml`) | container; embeds #3 | own header/footer |

All three canvas controls (#1–#3) share one copy-structured idiom: an outer `Grid` (Margin=10) with
two columns — `Col0 Width="*"` (image area) and `Col1 Width="Auto"` (fixed-width right legend pane) —
and rows for a header (`Auto`), a toolbar (`Auto`), the image+scrollbars (`*`), and (only in #3) a
footer (`Auto`). The header, toolbar, and footer are all placed in **`Grid.Column=0` only**, so they
stretch to the image-column width rather than the full window width. The legend pane is a fixed-width
element (`230`/`240`) that **`RowSpan`s every row**, so it is allotted the full control height; in #1
the legend is additionally wrapped in a `ScrollViewer` (`VerticalScrollBarVisibility=Auto`) that forces
full height + scrolling. The image canvas already occupies a `Height="*"` cell and takes the remaining
space.

There is **no shared base style / `ResourceDictionary`** governing this row/column layout — the grid is
duplicated per file.

### Approach (chosen layout)

> Header & footer span the full window width; the **toolbar stays at image-column width**; the legend
> sizes to its content height (top-aligned); the canvas fills the rest.

Apply a uniform rule **per-file** to #1–#3 (no shared-template refactor pre-release):

- **Header** → top row, `Grid.ColumnSpan="2"` → spans image + legend columns (full width).
- **Footer** (only #3) → bottom row, `Grid.ColumnSpan="2"` → full width. #1 and #2 have no footer, so
  nothing is added there.
- **Toolbar** (Fit / Prev / Next / Close / Undo / Redo) → remains in `Col0` at image-column width.
- **Legend pane** → drop the all-rows `RowSpan`; place it in the toolbar+image band (`Col1`),
  `VerticalAlignment="Top"`, so it sizes to its content height. For #1, this also relaxes the
  `ScrollViewer`'s forced full height: with `VerticalAlignment="Top"` it fits content and only scrolls
  as an overflow fallback when the legend is taller than the image area.
- **Canvas** → unchanged; it already fills the remaining `*` cell.

The legend's fixed width (`230`/`240`) is **left as-is** — the feedback is "autosize **vertically**",
which is a height-only concern.

### Out of scope

- Window default sizes: `AutoFocusFrameReviewControl.xaml.cs` (`1100×720`, mins `640×440`), the
  `FrameReviewVM` template's `MinWidth=1280`/`MinHeight=860`, and the `ReviewDialogHost` window chrome.
  These set the initial/min size; resizing is already allowed and the layout fix is what the feedback
  asks for.
- The optimizer **wizard's** own fixed per-step width (`Grid.Style` 640/820/1120). Making the wizard
  stretch to the window is a broader behavior change; out of scope. The wizard's review step still
  benefits because the embedded `StarReviewControl` (#3) is fixed by this item.
- No shared layout template / `UserControl` is introduced. (Noted as a possible future cleanup, since
  the grid is currently duplicated across the three files.)

### Risks

Low. Pure XAML grid attribute changes following the existing idiom. The main thing to watch is that the
header's right-docked content (e.g. `PositionLabel` in a `LastChildFill=False` `DockPanel`) now docks
to the full window's right edge — which is the intended "full width" behavior.

---

## Item 2 — Tilt wizard: one replay button + ReplayVM modal (all three choices)

### What exists today

The Tilt Adapter Wizard (`TiltAdapterWizard/DataTemplates.xaml`, VM `TiltAdapterWizardVM.cs`) has two
buttons, both calling the same `ReplayAsync(useMetadataSettings)`:

- **"Replay"** → `ReplayCommand` → `ReplayAsync(useMetadataSettings: true)`: re-analyzes each
  calibration step's saved AF frames using the run's **capture-time** star-detection settings (built
  per step via `BuildTiltReplayDetectionOverride`) and recomputes calibration math from the
  **metadata** geometry (screw count/radius, pixel size, focuser step, applied amount).
- **"Replay Current Settings"** → `ReplayCurrentSettingsCommand` → `ReplayAsync(useMetadataSettings:
  false)`: re-analyzes with the **current** profile star-detection settings (null override) and
  recomputes from **current** geometry. (Added 2026-06-21.)

The "ReplayVM" referenced in the feedback is **`ReplaySettingsPromptVM`**
(`AutoFocus/Replay/ReplaySettingsPromptVM.cs`), the view-model for a 3-choice modal introduced
2026-06-22. It resolves a `ReplaySettingsChoice` ∈ `{ UseCurrentSettings,
UseCaptureTimeSettingsInMemory, UpdateProfileToCaptureTime, Cancel }`. The AutoFocus pane and the
Inspector rerun already use it (via `AutoFocusReplayCoordinator.ResolveAsync`) to collapse exactly this
current-vs-capture-time decision into a single button. The same commit migrated the tilt wizard onto
the new no-mutation override seam (`BuildTiltReplayDetectionOverride` +
`AnalyzeAutoFocusFromSavedPath`'s `starDetectionOptionsOverride`) but kept its two pre-existing
buttons — so the two tilt buttons are now redundant with the modal pattern used everywhere else.

### Approach

Collapse to a **single "Replay" button** whose command drives the **`ReplaySettingsPromptVM`** modal,
offering all three choices.

- **XAML:** remove the "Replay Current Settings" button; keep the single "Replay" button bound to
  `ReplayCommand`.
- **VM:** delete `ReplayCurrentSettingsCommand` (declaration, construction, and its
  `NotifyCanExecuteChanged` site). `ReplayCommand` no longer hard-codes `useMetadataSettings`. After
  the folder pick + tilt-metadata load, it shows the modal (reusing `ReplaySettingsPrompt.ShowAsync`)
  and maps the resolved choice:

  | Choice | Star-detection override | Calibration geometry | Profile mutation |
  |---|---|---|---|
  | `UseCurrentSettings` | null (current profile) | current | none |
  | `UseCaptureTimeSettingsInMemory` | capture-time (per-step `BuildTiltReplayDetectionOverride`) | metadata | none |
  | `UpdateProfileToCaptureTime` | capture-time | metadata | persist capture-time **star-detection settings** to the live profile |
  | `Cancel` | — | — | abort |

  `UseCurrentSettings` reproduces the old "Replay Current Settings"; `UseCaptureTimeSettingsInMemory`
  reproduces the old "Replay". `UpdateProfileToCaptureTime` adds: persist the run's capture-time
  star-detection settings to the profile (reuse `AutoFocusReplayOptionsMapper` / the snapshot-apply
  mechanism), then run the capture-time + metadata-geometry path.

- **Reuse the prompt, not the whole coordinator.** `AutoFocusReplayCoordinator` maps to
  `AutoFocusEngineOptions`, which the tilt path does not use (it calls
  `inspector.AnalyzeAutoFocusFromSavedPath` + its own `RunCalibrationMath`). So the tilt wizard reuses
  the **prompt VM/dialog** (and the profile-apply mapper for the update-profile case) while keeping its
  per-step override lookup and calibration math internal and unchanged. The per-step override remains an
  internal detail — the modal resolves a single current-vs-capture-time intent for the whole run, which
  is unchanged from how the two buttons behaved.

### Seams to verify during plan-writing

This is the highest-risk item. The plan must confirm (by reading code) before settling final steps:

1. **DI:** that `TiltAdapterWizardVM` can obtain `IWindowServiceFactory` (and any other deps
   `ReplaySettingsPrompt.ShowAsync` needs) — it currently uses `profileService` and `HocusFocusPlugin`
   singletons; `windowServiceFactory` may need to be added to the constructor.
2. **Metadata compatibility:** that the tilt run's `metadata.json` carries a capture-time
   star-detection snapshot compatible with the prompt's `CaptureSummary` text and with the
   profile-apply mapper used by `UpdateProfileToCaptureTime`. A **tilt-specific `CaptureSummary`**
   variant may be needed (the AF summary is AF-centric).
3. **Update-profile semantics for tilt:** confirm the profile-apply path writes only star-detection
   settings (matching the AF behavior) and does **not** touch tilt-adapter geometry options (which are
   not part of the capture-time snapshot).
4. **Tests:** `TiltAdapterWizardVMTests` / `TiltAdapterWizardBehavioralTests` reference both commands
   and must be updated to the single-command + prompt-choice flow.

### Risks

Medium-high relative to the other two items: DI wiring, reusing an AF-centric prompt for tilt, and
adding profile-mutation semantics to the tilt path. Mitigated by reusing the existing modal + mapper
rather than inventing new infrastructure, and by the test updates above.

---

## Item 3 — Rename "Reprocess Saved Run" → "Replay Saved AF"

### What exists today

Exactly one button: a `ninactrl:CancellableButton` in `AutoFocus/DataTemplates.xaml` with
`ButtonText="Reprocess Saved Run"` (line ~620), bound to `LoadSavedAutoFocusRunCommand`. The label is a
**plain literal** (the HocusFocus project has no `.resx`; localized strings elsewhere use `{ns:Loc …}`).
Three nearby tooltips reference the old name in prose: the button's own tooltip (~625), the "Review
Frames" tooltip (~637), and the "Keep frames for review" tooltip (~644).

### Approach

- Change `ButtonText` to **"Replay Saved AF"**.
- Reword the button's own tooltip ("Reprocesses…" → "Replays saved AutoFocus images…").
- Update the two cross-reference tooltips (Review Frames, Keep-frames) that name the old button to use
  "Replay Saved AF".
- **Leave unchanged:** internal code-behind strings in `HocusFocusVM.cs` (a comment ~961 and
  log/notification strings ~972/973) — not user-facing. The command name
  `LoadSavedAutoFocusRunCommand` is not shown to users and does not change.

### Risks

Trivial. Literal text edits in one XAML file.

---

## Close-out

- Run the full unit test suite after implementation (`dotnet test …`) — project invariant. Item 2's VM
  test updates are part of this item, not an afterthought.
- No new persisted options or `StarDetectorMetrics` fields are introduced, so the associated UI-template
  invariants do not apply.
