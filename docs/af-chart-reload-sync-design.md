# AF Results Chart: Reload Sync Design

## Problem

Field report (2026-07-29, NINA 3.2.0.9001 + HocusFocus 4.0.0.11): the imaging-tab AF results chart
showed the fitted curve, trendlines, fit-quality stats, timestamp, duration, and final position of one
AF run (00:33:29, 540910 → 544024) while the data-point markers, initial position, and Start/HFR-change
rows came from a *different, hours-older* run (22:54:32, 539586 → 540059). Visually the curve "did not
match up with the points" — the marker minimum sat ~4.4k steps left of the curve minimum. Pixel-level
forensics of the screenshot against the NINA log confirmed every element's provenance run by run.

## Root cause

NINA core's saved-chart loader (`AutoFocusToolVM.LoadChart`) runs both when the user picks a chart from
the imaging-tab history dropdown and when the report `FileSystemWatcher` auto-selects a newly written
report (e.g. one written by a *sequence-triggered* run, which executes on its own transient
`HocusFocusVM`). `LoadChart` can only rebuild what `IAutoFocusVM` exposes:

- `FocusPoints`, `PlotFocusPoints` (replaced with the report's `MeasurePoints`)
- `FinalFocusPoint`, `LastAutoFocusPoint`, `AutoFocusDuration`, chart method/fitting
- fittings, indirectly, via `SetCurveFittings(method, fitting)`

Since v4.0.0.11 the chart's *markers* no longer bind `FocusPoints`; they bind two plugin-only
collections introduced for the symmetric focus window (Behavior A): `PlotCoreFocusPoints` (filled) and
`PlotWindowExcludedFocusPoints` (hollow rings). `LoadChart` cannot see them, and nothing on the plugin
side refreshed them on that path — so the markers (and the plugin-only `InitialFocuserPosition` /
`InitialHFR` / `FinalHFR` info rows) kept showing the VM's last *live* run while everything else
switched to the loaded report.

## Fix

`SetCurveFittings` is the one plugin method core's `LoadChart` reliably calls *after* it has replaced
both collections, so it becomes the reload reconciliation choke-point:

1. **Refit with Behavior A parity.** For the hyperbolic fittings, after the initial solve, run the same
   bounded fit → window → refit loop as the engine's `ApplyFinalSymmetricWindow`: partition the fit
   input around the fitted vertex with `PartitionByFocusWindow` (window = minimum ± (offsetSteps+0.5) ×
   stepSize, from the active profile's focuser settings), refit on the kept subset (≥3 valid points
   floor, ≤ offsetSteps+1 passes), recomputing the trendline alongside and the LOO stability on the
   final kept set. Non-hyperbolic fittings keep their existing all-points refit (the engine centers its
   window on `DetermineFinalFocusPoint`, which for the hyperbolic paths is the fitted vertex — the same
   center used here; reproducing the other methods' centers is not worth duplicating engine logic for
   charts this plugin does not fit hyperbolically).
2. **Rebuild the display split.** Rebuild `PlotCoreFocusPoints` from the (just-loaded) `FocusPoints`
   and route the loop's excluded set through the existing `ApplyWindowExclusionToDisplay`, which fills
   the hollow overlay, prunes the filled and line series, and refreshes the legend gate — exactly what
   the live completion handler does.
3. **Reset live-only info rows on foreign charts.** The VM records the `Timestamp` of every report it
   generates itself. When the loaded chart's `LastAutoFocusPoint.Timestamp` (set by core from the
   report file) differs, the chart belongs to some other run/VM, so `InitialFocuserPosition`,
   `InitialHFR`, and `FinalHFR` reset to their sentinels and those rows collapse instead of showing a
   previous run's values. When the timestamps match (the watcher re-loading the pane's own
   just-completed run — the common case after every pane run), the fields are preserved.

## Non-goals

- `LoadChart` aborting before `SetCurveFittings` (unparseable `Fitting` in a foreign/corrupt report)
  still leaves stale markers; no plugin code runs on that path.
- The replay path (`LoadSavedAutoFocusRun`) re-runs the engine and fires the normal live events, so it
  needs no changes.
- The optimizer wizard's chart has its own core/recovery split and is not reachable by `LoadChart`.
