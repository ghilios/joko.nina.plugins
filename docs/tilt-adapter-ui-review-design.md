# Motorized Tilt-Adapter UI Review — Update Guidance

Design review of two UI surfaces of the motorized tilt-adapter (ASG EAT) feature, produced with Fable
(`claude-fable-5`) reviewing the actual XAML/VM, then consolidated here. Scope: the **"Review motor
commands" adjustment dialog** and the **Tilt Adapter Calibration Wizard UX** (especially when a motorized
adapter is connected).

Priority key: **[H]** high (includes the four explicitly-requested changes), **[M]** medium, **[L]** low.

---

## A. "Review motor commands" adjustment dialog
Files: `TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptControl.xaml`,
`TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptVM.cs`.

**Overall:** The safety skeleton is solid (the displayed plan always equals what executes; hard limits block
Proceed; warnings are tiered). But the information hierarchy is inverted for a non-expert: each row leads with
the raw wire command (`tr,142`) in large bold monospace, while the human-meaningful fact — *which numbered
screws move, and which way* — is absent (the `Description` is axis jargon, "Diagonal A: +142 steps"). The
per-screw signed steps the UI needs are already on `TiltAdapterMove.PerCornerSteps`.

1. **[H] Move-row semantics (user request #1).** Replace the prominent wire command with a semantic move line
   built from `PerCornerSteps`:
   - Corner axes (DiagonalA/B): `Corner move — Screw 1 up 142 steps, Screw 3 down 142 steps`.
   - Edge axes (EdgeVertical/Horizontal): `Side move — Screws 1 & 2 up 20 steps, Screws 3 & 4 down 20 steps`.
   - Backfocus: `Backfocus — all four screws move together (+N steps; changes sensor spacing, not tilt)`.
   - Demote the wire command to a small dim monospace chip on the right (`sent as tr,142`) for auditability —
     keep it, don't lead with it. Add a `Corner` / `Side` / `Backfocus` kind tag where the wide command box was.
   - **Direction wording caveat:** use "up/down" only after confirming the positive-step physical convention
     (see §C); if the convention is not pinned, say "driven +N / −N steps" with a one-line legend.

2. **[H] Apply toggle labels (user request #2).** The captions are supplied as `CheckBox.Content`, which
   NINA's themed CheckBox template frequently does **not** render — so the user sees two bare toggles beside
   "Apply:". Fix: make each caption a **sibling `TextBlock`**, not `CheckBox.Content`, so it renders regardless
   of theme; move the `⚠ (assumed direction)` badge out to sit after the Backfocus caption; add
   `AutomationProperties.Name` on each CheckBox for screen readers.

3. **[M] Corner context on screw references.** Append the corner to every "Screw N": `Screw 1 (TR)`,
   `Screw 3 (BL)`. Optionally lay the residual cells out as a 2×2 spatial grid (TL|TR / BL|BR) matching the
   sensor. **Verify the 1..4 ↔ corner mapping** against `.claude/docs/tilt-domain.md` first — it may be
   rig-dependent (response-convention angles); if so, keep plain "Screw N" plus a legend.

4. **[M] Intro copy.** Drop leftover jargon ("verify each signed command"). Reword to plain physical language:
   "…turn the tilt adapter's motor screws, and the device remembers every move (stored in EEPROM). There is no
   automatic undo — check which screws move, and in which direction, before proceeding."

5. **[M] Warning-panel titles.** Give every warning the danger panel's title+body structure: "Approaching
   travel limit", "Motor positions unknown", "Backfocus direction is assumed", "Twist cannot be corrected",
   "Screw pitch mismatch". In the twist body, lowercase "no" and translate steps→µm (same scale as the residual
   table). Order the stack by severity.

6. **[M] Proceed disabled-state / label.** When both toggles are off, `CanProceed` is false but nothing near the
   button says why. Add a dim reason (or a `ToolTipService.ShowOnDisabled` tooltip): "Select at least one
   correction to apply." Optionally make the label dynamic ("Send 3 moves").

7. **[M] Residual interpretation.** Add one line to the residual caption explaining sign/scale ("Positive = that
   corner remains slightly high after the moves. Values under ~1 µm are within one motor step."). Verify sign
   against the planner (residual = applied − target).

8. **[M] Assumed-direction row anchor.** When the assumed-direction warning is active it applies to exactly the
   Backfocus row — add a small inline `⚠ direction assumed` tag on that row so the warning has an anchor.

9. **[L] Group badge / footer wording.** Once rows are semantic the Tilt/Backfocus pill is largely redundant
   (drop it for Backfocus rows or replace with a subheader); rename "commands" → "moves" in the footer to match.

---

## B. Tilt Adapter Calibration Wizard UX
Files: `TiltAdapterWizard/DataTemplates.xaml`, `TiltAdapterWizard/TiltAdapterWizardVM.cs`.

**Overall:** The flow model is solid (explicit step enum, 4/6-step variants, device-driven vs manual, careful
failure/recovery). But the **running-wizard surface (Panel B)** under-communicates: a single instruction
paragraph with no step number, no title, and — during a run — no visibility of the motor positions the VM
carefully refreshes per move (they live only in Panel A's connection GroupBox, which is collapsed while running).
In Auto Run All mode the copy still tells the user to click buttons that are hidden. All four requested gaps are
confirmed.

1. **[H] Step number + total (request a).** Add `StepProgressDisplay` = `"Step {i+1} of {N}"` over the active
   step set (N is 4 or 6 depending on `MeasureCurvatureDuringCalibration`); render dim at the top of Panel B.
   Optionally append the variant: "Step 3 of 6 (direction measurement included)".

2. **[H] Per-step header (request b).** Add a short `StepTitle` above the instruction paragraph: Baseline →
   "Baseline Measurement", AllInward → "All Screws Clockwise" / "All Motors + Steps", ReBaseline → "Return to Baseline", Screw1/2 →
   "Move Screw 1/2", Complete → "Calibration Complete". Render bold, mirroring Panel C's existing bold header.

3. **[H] Auto-Run-All copy (request c).** `DeviceStepInstructionsText` always ends "Click Run Measurement (or
   Auto Run All)…", but during `AutoRunAllAsync` those buttons are hidden. Expose `IsAutoRunningAll` (INPC) and,
   when true, return automated-status wording with no imperative ("Running automatically — taking the baseline
   measurement." / "…the wizard will apply this move, measure, and advance without further input."). Keep the
   "Click…" wording only for connected-but-manually-stepped mode. Consider a persistent "Auto Run All in
   progress — Cancel returns the device to its starting position." banner.

4. **[H] Motor positions + deltas visible during the run (request d).** The 2×2 position/Δ grid exists but only
   in Panel A, which is collapsed while running — i.e. hidden exactly when `RefreshRunDevicePositionsAsync` is
   updating it and the deltas matter. Extract it into a shared keyed `DataTemplate` and render it in **Panel B**
   (gated on `IsTiltDeviceConnected`) and **Panel C** (so the user can verify the restore-to-baseline brought
   every Δ back to 0 — the single most valuable moment for this display). Style Δ separately (accent when ≠0,
   dim when 0) rather than appending it into the same text run.

5. **[M] Auto Run All prominence.** For a connected motorized adapter, Auto Run All is the intended primary
   path but is the third, identically-styled button. When `IsMotorizedDevice && IsTiltDeviceConnected`, make it
   visually primary (accent) and reorder/retitle the others ("Run This Step" / "Use Saved AF").

6. **[M] Cancel reassurance during Auto Run All.** Retitle Cancel → "Cancel Run" with a tooltip explaining it
   finishes the in-flight move/measurement (no device abort command), then auto-returns every motor to its start.

7. **[M] Surface the auto-enabled direction measurement.** `ConnectTiltDeviceAsync` silently turns on
   `MeasureCurvatureDuringCalibration` (4→6 steps). Notify or note it so the extra "all screws" steps aren't
   confusing (the step counter also mitigates this).

8. **[M] Screw↔corner mapping in visible text.** The mapping is tooltip-only; repeat it once as visible small
   italic, and/or highlight the moving corner cards during a device move.

9. **[L] Verb consistency.** Align copy verbs with on-screen labels ("Calibrate"/"Abort Wizard"/"Done") — some
   strings say "Start".

10. **[L] Progress inside Auto Run All / Panel B device status.** Prefix `StatusText` with step context ("Step
    3 of 6 — Run 1/2…") and carry the small `TiltDeviceStatusText` line into Panel B alongside the positions.

---

## C. Positive-step direction convention (blocker for "up/down" wording)
Both the move-row semantics (§A.1) and any "up/down" copy depend on what a **positive step** does physically
(raise/lower a corner, or drive it in/out). This must be confirmed against the wizard's existing terminology
before hardcoding "up"/"down" — otherwise fall back to "+N / −N steps" with a legend. (Being fetched from the
wizard XAML/VM + `TiltScrewTargets.cs`.)

---

## Implementation scope — all implemented

Both the four explicit requests and every additional Fable suggestion have been implemented (branch
`ghilios/motorized-tilt-adapter`, PR #140):

- **Explicit requests:** A.1 (semantic move rows), A.2 (toggle labels), B.1 (step number), B.2 (step header),
  B.3 (Auto-Run-All copy), B.4 (positions+deltas in the running/complete panels). — commit `31ea5c4`.
- **Additional suggestions:** A.3 (corner labels + 2×2 residual grid), A.5 (titled/ordered warning panels), A.6
  (dynamic Proceed label + disabled reason), A.7 (residual interpretation), A.8 (inline assumed-direction tag),
  A.9 ("moves" wording); B.5 (Auto-Run-All primary/first + "Run This Step"), B.6 (Cancel Run + tooltip), B.7
  (direction-measurement default notification), B.8 (visible screw↔corner mapping), B.9 (verb alignment →
  "Calibrate"), B.10 (connection status in the running panel). — commit `da9d65a`.

**Twist warning steps→µm (A.5):** now implemented — `unitMicrons` is plumbed through
`TiltDeviceAdjustmentPrompt.ShowAsync` → the prompt VM ctor → the InspectorVM adjustment seam, so the twist
warning reads "…N steps (about X µm across the sensor)…".

## Follow-up feedback (beyond the Fable review)

A later round of direct feedback, also implemented on this branch:
- Current stepper positions now shown in the inspector's **Tilt Adapter Guidance** section when a motorized
  adapter is connected (2×2 corner grid, no Δ — the inspector has no calibration-run baseline).
- "Review motor commands": "Apply:" → "Apply"; move rows drop the redundant "Corner move"/"Side move" prefix
  (the badge already says it); the backfocus row is trimmed to "All four screws +N steps together".
- The Focus Chart now shows the **5-curve (Center + 4 corners) view live during a sweep** even when the sensor
  model is enabled — via a chart-only `ShowSingleAfCurve` gate (`ShowSensorModel.Tag AND NOT IsAnalysisRunning`)
  that leaves every other sensor-model gate untouched.
