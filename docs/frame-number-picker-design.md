# Frame-Number Picker in the Review Toolbars — Design

**Status:** Approved (design); implementation plan to follow in `plans/frame-number-picker-plan.md`.
**Date:** 2026-06-23

## Purpose

Add a drop-down frame picker between the **Prev** and **Next** buttons in each review UI. Its value tracks
the currently displayed frame, and selecting an entry jumps to that frame. Each entry shows the **1-based
frame number plus the frame's focuser position** (e.g. `3 — 2745`).

## Where it applies

The three image-with-overlays review controls, each with a Fit / Prev / Next toolbar:

| # | Control | View-model | Toolbar |
|---|---|---|---|
| 1 | `AutoFocus/Review/AutoFocusFrameReviewControl.xaml` | `AutoFocusFrameReviewVM : FrameReviewVMBase<…>` | `DockPanel` |
| 2 | `StarDetection/Optimization/Review/FrameReviewControl.xaml` (Inspector) | `StarDetection/Optimization/Review/FrameReviewVM.cs : FrameReviewVMBase<…>` | `DockPanel` |
| 3 | `StarDetection/Optimization/Review/StarReviewControl.xaml` | `StarReviewVM` (standalone `BaseINPC`) | `WrapPanel` |

Controls #1 and #2 share the base **`FrameReviewVMBase<TMarker>`** (it owns `CurrentIndex`, `FrameCount`,
`Prev()/Next()`, `LoadCurrent(bool)`, the 1-based `PositionLabel`). Control #3 is a separate hierarchy but
has the identical shape (`CurrentIndex`, a private `queue`, `Prev/Next` → `LoadCurrent`, the same
`PositionLabel`).

## Existing frame model (verified)

- The current frame is a **0-based `int CurrentIndex`** (public getter, non-public setter that raises
  `PropertyChanged` for itself and `PositionLabel`). It is not two-way bindable as-is.
- Navigation jumps go through **`LoadCurrent(fit:false)`** (clears markers, calls `LoadFrame(CurrentIndex)`,
  refreshes Prev/Next `CanExecute`). `fit:false` preserves the user's zoom/pan — Prev/Next and the ◀/▶ keys
  all use it.
- The frames themselves are **private** (`snapshot.Frames` for #1/#2, `queue` for #3); each frame exposes a
  `FocuserPosition`. There is **no** public bindable frames/numbers collection and **no** absolute "jump to N"
  entry point today — both must be added.
- `PositionLabel` is already 1-based (`"{CurrentIndex+1} / {FrameCount}"`), so a 1-based picker number is
  consistent with what the user already sees.

## Approach

### New type: `FramePickerItem`

A small immutable item (in the Review folder/namespace, shared by all three VMs):

```
public sealed class FramePickerItem {
    public FramePickerItem(int number, string label) { Number = number; Label = label; }
    public int Number { get; }    // 1-based frame number; the ComboBox selection value
    public string Label { get; }  // e.g. "3 — 2745"  (number — focuser position)
}
```

### `FrameReviewVMBase` (covers #1 and #2)

- `public IReadOnlyList<FramePickerItem> FramePickerItems { get; protected set; }` — the ComboBox
  `ItemsSource`. The **subclass** populates it in its ctor (the base has no frame data, only `FrameCount`).
- `public int SelectedFrameNumber` — the ComboBox `SelectedValue` (1-based):
  - `get => CurrentIndex + 1;`
  - `set`: `var idx = value - 1; if (idx in [0, FrameCount) && idx != CurrentIndex) { CurrentIndex = idx;
    LoadCurrent(fit:false); }` — same sequence as Prev/Next, so zoom/pan is preserved and Prev/Next
    `CanExecute` refresh.
- In the existing `CurrentIndex` setter, also `RaisePropertyChanged(nameof(SelectedFrameNumber))` so the
  dropdown selection follows the Prev/Next buttons and the ◀/▶ keys.

### Subclass ctors (#1 `AutoFocusFrameReviewVM`, #2 `FrameReviewVM`)

After the base ctor runs, build the items from the snapshot, e.g.:

```
FramePickerItems = snapshot.Frames
    .Select((f, i) => new FramePickerItem(i + 1, $"{i + 1} — {f.FocuserPosition:0}"))
    .ToList();
```

(Frames are already in display order — focuser-sweep order for #1 — so item `N` is the same frame the
`n / N` label and Prev/Next refer to.)

### `StarReviewVM` (#3, standalone)

The same three additions directly on the VM:
- `public IReadOnlyList<FramePickerItem> FramePickerItems { get; }` built in the ctor from `queue`
  (`$"{i + 1} — {f.FocuserPosition}"`; `FocuserPosition` is an `int` here).
- `public int SelectedFrameNumber` with the same get/jump/guard, using `queue.Count`, the private
  `CurrentIndex` setter, `LoadCurrent(fitView:false)`, and `Next/PrevCommand.NotifyCanExecuteChanged()`.
- Add `RaisePropertyChanged(nameof(SelectedFrameNumber))` to the existing `CurrentIndex` setter (so Prev /
  Next / arrow keys / undo-redo `NavigateTo` all keep the dropdown in sync).

~8 lines duplicated between the base and `StarReviewVM`; acceptable since the two VM hierarchies don't share
a base and a shared interface would be heavier than the duplication it removes.

### XAML — all three toolbars

Insert a `ComboBox` between Prev and Next (so it renders in that position: `DockPanel.Dock="Left"` after the
Prev button for #1/#2; plain child after Prev for #3's `WrapPanel`):

```xml
<ComboBox DockPanel.Dock="Left" Margin="4,0" VerticalAlignment="Center" MinWidth="96"
          ItemsSource="{Binding FramePickerItems}" DisplayMemberPath="Label"
          SelectedValuePath="Number" SelectedValue="{Binding SelectedFrameNumber, Mode=TwoWay}" />
```

Reuse the existing `HF_LightCombo` style where the control already defines it (the dark-panel light-box look
the other combos use); otherwise a plain `ComboBox` with an explicit foreground/vertical-centering. Drop
`DockPanel.Dock="Left"` for #3 (`WrapPanel` lays out left-to-right by document order).

## Testing

- Unit-test the `SelectedFrameNumber` jump/guard logic on `FrameReviewVMBase` via a minimal test subclass
  (generic marker + a trivial `LoadFrame` that records the loaded index): setting `SelectedFrameNumber`
  moves `CurrentIndex` and triggers a load; out-of-range and equal-value sets are no-ops; `get` returns
  `CurrentIndex + 1`. (If the base's other members make a test subclass impractical, fall back to verifying
  the same logic and rely on build + manual for the binding.)
- The XAML wiring and visual placement are verified by build + manual smoke (the dropdown shows
  `number — focuser`, tracks Prev/Next and the arrow keys, and jumps on selection).

## Out of scope

No change to Prev/Next behavior, the `n / N` label, frame ordering, zoom/pan on navigation, or the frame
data model. No new persisted option or `StarDetectorMetrics` field, so those UI-template invariants do not
apply.
