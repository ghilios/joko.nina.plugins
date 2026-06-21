# Plan: Remaining NINA / Hocus Focus documentation screenshots

## Context

Pass 1 of adding real application screenshots to the MkDocs manual is done on branch
`ghilios/docs-screenshots` (24 images across 14 pages, not yet committed). This plan covers the
**remaining shots and refinements** to tackle in a later session.

The full capture/annotate mechanics are documented in **`.claude/docs/nina-mcp-screenshots.md`** —
read it first; it covers driving NINA via the `windows-mcp` server, the second-virtual-desktop
isolation, full-resolution PowerShell capture, the NINA → Hocus Focus navigation map, and the
sidecar-driven Pillow helper. Do not rediscover any of that here.

What already exists and should be reused:
- Helper: `documentation/figures/annotate_screenshots.py` (run with repo `.venv`; `--only`, `--check`).
- Sidecars: `documentation/figures/screenshots/<name>.json` (coords in raw-capture pixel space).
- Raw captures: `documentation/docs/assets/screenshots/raw/`; published PNGs: `…/screenshots/`.
- Capture script on Windows: `$env:TEMP\nina_capture.ps1` (`-Mode window` / `-Mode screen`); recreate
  from the reference doc if missing.
- Data source for data-rich shots: a saved AF run at
  `D:\Tilt Calibration Bank\astrodet\AutoFocus_<date>_<time>` (select the `AutoFocus_…` folder).
- Embed convention: `![alt](PATH){ width=NNN }`, blank line, italic `*caption*`. PATH is
  `assets/screenshots/…` from root pages (`index.md`, `quick-start.md`) and `../assets/screenshots/…`
  from subdir pages. Set `width = min(native_width, 620)` so narrow crops are not upscaled.

Pre-req each session: confirm the installed plugin version on the plugin page matches the develop
docs; if a documented control is absent in the UI, flag it instead of documenting a control the
screenshot cannot show.

## Per-shot work (each: capture raw → write sidecar → `annotate_screenshots.py --only` → embed)

### Tier 1 — straightforward, high value

1. **Quick-start rig-setup fields** (no equipment/data needed)
   - Camera pixel size: Options → Equipment → Camera → the pixel-size field.
     → `quick-start.md` §1 "Tell NINA about your rig" · `equipment-camera-pixelsize.png` · rect highlight.
   - Telescope focal length: Options → Equipment → Telescope → focal-length field.
     → `quick-start.md` §1 · `equipment-telescope-focallength.png` · rect highlight.

2. **Optimizer "Review frames" / labeling tool** (needs a completed optimizer run — have the saved AF)
   - From the optimizer wizard Summary, click **Review frames** to open `StarReviewControl`: image
     viewport with detector boxes + the label modes (Missed / Should-reject / Wrongly-rejected) and
     the Legend / How-to-label / What-this-changes panels.
   - Capture the review viewport (and optionally the "How to label" legend).
     → `optimization/labels-recall-precision.md` "The label-assisted loop" / "Three kinds of label"
     · `optimizer-review-labeling.png` · arrow or rect on the label-mode controls.

3. **Re-shoot `inspector-options-empty` from a widened dock** (refinement)
   - The pass-1 version is from a narrow, clipped dock. Widen the Aberration Inspector dock (splitter
     drag, per reference doc) so both option columns are fully visible, then re-capture and overwrite
     `raw/inspector-options.png` + re-render. No doc edit needed (same filename).

4. **Inspector FWHM contour map + eccentricity vectors** (saved AF already loads these)
   - With a saved AF loaded, scroll the inspector below the tilt-measurement tables to the **FWHM
     Contour Map** and **Eccentricity Vectors** sections; capture each.
     → `overview/tilt-aberration-inspector.md` (Simple Analysis / quick-visualizations area)
     · `inspector-fwhm-contour.png`, `inspector-eccentricity-vectors.png` · no highlight.

### Tier 2 — needs a displayed image with annotations (most valuable, trickiest)

5. **Annotated star-field frames** (needs an image with stars shown in the Imaging viewer + Star
   Annotator overlay on; load a saved AF frame or take a simulator exposure)
   - Accepted (green + HFR labels) vs rejected (pink, by gate) overlay.
     → `index.md` hero (could replace synthetic `annotation-overlay.png`) and
       `overview/star-annotation.md` overlay section · `annotation-overlay-real.png`.
   - Rejection diagnostics lit up (several reject classes in distinct colors).
     → `overview/star-annotation.md` "Rejection diagnostics" · `annotation-rejections-real.png`.
   - Structure-map overlay (debug): enable Show Structure Map.
     → `overview/star-annotation.md` "Structure map overlay (debug)" · `annotation-structure-map.png`.
   - Note: getting clean stars in the simulator frame is the main effort; a saved AF frame loaded
     into the viewer is the likely path. Highlight: none (the frame is the subject), maybe a small
     inset/zoom of a labeled star.

### Tier 3 — multi-step wizard, optional

6. **Tilt Adapter Wizard step screens** (Imaging-tab dock "Tilt Adapter Wizard"; use "Use Saved AF" /
   Replay per step to avoid live hardware)
   - Start/Baseline screen; a measurement step (e.g. AllInward) with the screw-numbering diagram
     (`HF_TiltScrewDiagram`) and calibration info; the per-step measurement summary; device-preset /
     editable-inputs controls.
     → `overview/tilt-aberration-inspector.md` "Tilt Adapter Wizard" · `tilt-wizard-start.png`,
       `tilt-wizard-screw-diagram.png`, `tilt-wizard-step.png` · arrow/rect on the active controls.
   - Highest effort; confirm with the user whether the wizard can be driven via Replay/Use-Saved-AF
     before investing.

### Optional refinements (confirm with user first)

7. **Optimizer summary with non-trivial deltas** — pass 1's summary showed "unchanged" because the
   chosen saved run was already optimal (saturated objective). To show real Current → Optimized
   deltas, either pick a deliberately sub-optimal saved run or perturb a detector setting before
   running, then re-capture `optimizer-wizard-summary.png` (+ the `autofocus-vcurve-real.png` crop).
8. **Highlight-style pass** — if the user wants spotlight emphasis on the densest panels or a
   different accent color, it is a sidecar edit + `annotate_screenshots.py --only …` (no re-capture).

## Surfaces intentionally skipped (note, don't silently omit)

- Star Detection Options / Star Annotation Options docks (Imaging tab) — duplicate the Options-page
  tabs already captured.
- Sequencer item "Run Aberration Inspector" — no manual page documents it.
  (Add either only if a relevant doc section appears.)

## Verification

1. `.venv/bin/python documentation/figures/annotate_screenshots.py` then `--check` — every sidecar
   reproduces its published PNG.
2. `.venv/bin/python -m mkdocs build --strict` — fails on any broken image path.
3. `.venv/bin/python documentation/figures/generate_figures.py --check` — confirm `assets/figures/`
   is untouched.
4. Visually inspect each new PNG (correct control highlighted, tight crop, crisp text); build a
   contact sheet for a fast review.
5. Set each new embed's `width` to `min(native_width, 620)`.

## Wrap-up

- Reset the Aberration Inspector dock width if it was widened (or leave and note it).
- Restore any detector toggles changed during capture; close wizards with Close (not Accept).
- Switch back to the user's virtual desktop; optionally close NINA / the scratch desktop.
- Commit on `ghilios/docs-screenshots` (privacy email per CLAUDE.md), push, open a PR to `develop`.
- Update memory `[[docs-screenshots-branch]]` with what shipped.
