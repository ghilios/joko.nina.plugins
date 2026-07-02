# Tilt Guidance: Motion-Anchored Arrows & Rotation Glyphs — Design Spec

**Status:** approved 2026-07-02 (user UI feedback on PR #117's guidance table)
**Scope:** Aberration Inspector "Tilt Adapter Guidance" section (+ the wizard's step-summary rotation glyphs for vocabulary consistency). Lands as additional commits on PR #117 (`ghilios/tilt-calibration-ux`), since it revises presentation semantics that PR introduced.

## Problem

Three pieces of user feedback on the guidance table shipped in PR #117:

1. The direction legend sits *below* the guidance grids; it should be at the top, prominent.
2. The legend wording ("⬆ = clockwise (adapter moves toward the camera/objective)") is too complex; it should read simply "up = adapter moves toward the objective".
3. "CW"/"CCW" text should be replaced by circular-arrow glyphs, and the rotation direction should appear on **all three** numeric rows (Tilt, Backfocus, Total), not just Total.

Feedback 2+3 together imply a semantic re-anchoring, confirmed with the user: the ⬆/⬇ arrows stop meaning *rotation* and start meaning *adapter motion*; the new circular glyphs carry the rotation.

## The contract

Two glyph vocabularies, each answering a different question:

| Glyph | Question it answers | Meaning |
|---|---|---|
| ⬆ / ↑ / — / ↓ / ⬇ (arrows grid) | *What must the adapter do at this screw?* | ⬆ = this corner of the adapter plate moves **toward the objective**; ⬇ = toward the camera. Magnitude tiers (large/small/dash) keep today's threshold logic. |
| ⟳ / ⟲ (numeric grid, screws) | *Which way do I turn this screw?* | ⟳ = clockwise (tighten); ⟲ = counter-clockwise (loosen). U+27F3 / U+27F2 text glyphs. |

Because "adapter moves toward the objective" ⇔ "the local best-focus position decreases" (this is what `ScrewInwardCurvatureSign` σ together with the pinned constant `CurvatureSignWhenCwMovesAdapterTowardObjective = −1` encode — no new empirical input is needed), both vocabularies are computable from existing state:

- **Tilt row** — rotation is σ-free (the calibrated screw angles already encode the rig's response direction): glyph ⟳ iff the CW-positive tilt turns value is positive. Motion needs σ: arrow ⬆ iff `σ · turns[i] < 0`.
- **Backfocus row** — motion is σ-free (pure curvature physics): arrow ⬆ iff the backfocus correction calls for the local best-focus position to decrease (`BackfocusMicrons < 0` in the correction convention — verify the equivalent expression the VM already computes when implementing). Rotation needs σ: glyph ⟳ iff `σ · BackfocusMicrons > 0`.
- **Total row** — rotation per the corrected PR #117 formula (`SignedTotalAdjustment`): glyph ⟳ iff `TiltMicrons + σ·BackfocusMicrons > 0`. (No motion arrow — the arrows grid has no Total row; unchanged.)

Robustness property (state it in code comments and pin it in tests): with a **wrong** adapter-direction setting, the tilt *arrows* flip but the tilt *glyphs* stay correct, and the backfocus *arrows* stay correct while the backfocus *glyphs* flip. The old failure mode — two rotation indicators contradicting each other — cannot recur because arrows and glyphs no longer claim to answer the same question. The "(assumed …)" legend suffix remains the flag that σ is unverified.

### Cell formats

- Screws — Tilt `0.75 ⟳`, Backfocus `0.30 ⟲`, Total `1.05 ⟳` (bold, per today's Total styling). **No units and no "CW"/"CCW" text in any cell** — today's cells say "0.75 turns"; the unit moves into the legend ("amounts in turns") and the glyph alone carries rotation.
- Steppers — no rotation glyphs (the motor abstracts rotation). All three rows show **signed steps** in the wizard-prompt convention: Tilt `+35 steps`, Backfocus `−12 steps`, Total `+23 steps`. (Tilt/Backfocus cells were magnitude-only before; they gain the sign so steppers carry the same per-row direction information screws get from glyphs.) Signs: Tilt σ-free, Backfocus σ-signed, Total per `SignedTotalAdjustment` — identical logic to the glyph selection.
- Below-threshold values keep today's "—" dash behavior (no glyph/sign on a dash).

### Direction unknown (σ = 0)

Unreachable from persisted options since PR #117 (default +1, wizard writes ±1, manual entry preserves the setting). Defensively, resolve `σ = 0 → TiltScrewGeometry.DefaultScrewInwardCurvatureSign` (the same rule `CwMovesAdapterTowardObjective` already uses) and drop the old magnitude-only "hasDirection = false" display path; the formatter parameters simplify accordingly.

## Legend

- **Position:** first element under the "Tilt Adapter Guidance" header, above the arrows grid (moved from below the numeric grid).
- **Style:** regular (non-italic), full opacity — prominence by position *and* weight; wraps.
- **Text (fixed, no longer varies with σ):**
  - Screws: `⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns`
  - Steppers: `⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts`
  - Suffix when σ is not wizard-measured (unchanged rule): ` (assumed — set or measure in the Tilt Adapter Wizard)`.
- Since the wording no longer depends on σ, `BuildDirectionLegend` drops its `curvatureSign` parameter: `BuildDirectionLegend(bool steps, bool signIsMeasured)`.
- Visibility gating unchanged: legend renders iff guidance rows render.

## Vocabulary consistency (wizard step summary)

PR #117's Fix C set the wizard step-summary Rotation column to ↑ = CW. With ⬆ re-anchored to adapter motion, ↑-as-rotation must not survive anywhere: switch the step-summary rotation glyphs from ↑/↓ to ⟳/⟲ (same `StepDescription` static, same tests updated). The saved-calibration row's "Screws CW → Curvature ↑ (measured)" is out of scope: its arrow denotes the curvature-effect direction, is explicitly labeled, and predates this feedback.

## Out of scope / non-goals

- Merging the arrows grid and numeric grid into one table (kept separate: conceptual vs actionable).
- Vector/Path icons for the rotation glyphs (Unicode ⟳/⟲ chosen; revisit only if live rendering is poor — ↻/↺ are the lighter-weight fallback).
- Any change to wizard prompts, TestApp, stored conventions, or the calculator math (presentation-layer only; `SignedTotalAdjustment` remains the single rotation source of truth).

## Files

- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs` — formatter statics reworked (glyph/sign selection replaces FormatTotal's CW/CCW text; magnitude formatter gains the rotation/sign variant); legend text fixed.
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` — `RebuildTiltGuidance` motion-arrow mapping (tilt ×−σ into the existing threshold picker; backfocus σ dropped); `FillNumericGuidance` per-row rotation signs.
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` — legend relocation and styling.
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` — step-summary ⟳/⟲ glyphs.
- Tests: `TiltAdapterGuidanceVMTests.cs`, `TiltAdapterWizardVMTests.cs`, plus an InspectorVM behavioral test for the σ-flip matrix.
- Docs: `documentation/docs/overview/tilt-aberration-inspector.md` guidance/legend description; grep all docs for `turns CW`, `⬆ = clockwise`, and stale examples.

## Testing requirements

- Formatter statics: glyph/sign selection for all three rows × screws/steppers; dash thresholds; Total loses "turns"/"CW" text.
- Legend: fixed wording both adapter types; assumed suffix; 0→default resolution.
- σ-flip matrix (behavioral, σ = ±1 with identical model inputs): tilt arrows flip / tilt glyphs don't; backfocus arrows don't flip / backfocus glyphs do; Total glyph matches `SignedTotalAdjustment` sign.
- Wizard step-summary glyph update pinned.
- Full suite green; `mkdocs build --strict` clean.
