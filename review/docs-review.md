# Hocus Focus Documentation Review

**Scope:** `documentation/docs/` (MkDocs Material) verified against the C# source on branch
`ghilios/docs-site` (the open PR #65 — the docs are **not yet on `develop`**).
**Method:** 13 parallel source-vs-doc verifier agents (one per settings page + optimization, overview,
heuristic, completeness, terminology, audience-fit), every accuracy finding then independently re-checked
against the cited code by a separate adversarial pass, plus the reviewer's own direct verification of the
load-bearing defaults, ranges, formulas, and the two confirmed accuracy bugs.
**Meta-corrections to the brief:** the MkDocs config is at repo-root `mkdocs.yml` (not
`documentation/mkdocs.yml`); `docs_dir: documentation/docs`. Build is run from the repo root.

Findings tally: **0 Critical · 12 Major · 41 Minor · 18 Nit** across 71 raw findings (deduped below).

---

## 1. Executive summary

**Audience-fit verdict — can a motivated astronomer tune past defaults from these docs today? *Yes, with
friction.*** The technical substance is excellent and, unusually, *trustworthy*: **all 44 star-detection
settings are documented, and 44/44 stated defaults and 44/44 stated ranges match the code** (initializers,
setter clamps, and XAML validators), the option→`StarDetectorParams` mappings in `BuildStarDetectorParams`
are correct, the optimization objective is reproduced constant-for-constant, the gradient-robust
contamination algorithm is described faithfully, and every quoted in-app tooltip matches the resource
strings verbatim. A reader who finds the right knob will get correct direction-of-effect and cost guidance.

The friction is in **getting to and acting on** that guidance, and it clusters into four recurring themes:

1. **UI-label drift (dominant, ~17 settings).** Pages name controls by their C# property
   (e.g. "Use Advanced", "Model PSF", "Measurement Average") instead of NINA's on-screen label
   ("Advanced Mode", "Fit PSF", "Measurement Averaging"). A reader scanning the NINA options panel for the
   documented name will not find it. This is the single highest-leverage fix.
2. **No assembled tuning loop + an unlocatable feedback panel.** Per-knob advice and the routing logic
   ("missing star → structure detection; rejected-with-a-reason → acceptance gates") are strong, but never
   collected into one ordered observe→change→re-check procedure, and the **star-detection metrics panel**
   the docs repeatedly send readers to (for rejection counts) is never located or described.
3. **Two real accuracy slips + one inverted causal claim** (all on the Simple-mode / debug surface),
   detailed below — none change a primary default/range/direction, but each could mislead.
4. **Actionable rig-derived starting points and per-knob feedback signals are one click away.** The
   `2^L ≈ a few × FWHM_px` style seeds live only on the analysis page, and ~16 "When to adjust" boxes name
   the trigger but not the specific number to watch to confirm success.

**Task 5 (build hygiene):** `mkdocs build --strict` passes clean (exit 0) — no broken links, missing
anchors, pages missing from nav, or orphaned files. Every figure has descriptive alt text and an italic
caption and is referenced from the text.

---

## 2. Prioritized findings

### Accuracy (Task 1 / 3) — verified against code

#### A1 — `[Major]` Simple-mode noise-reduction-radius table is wrong by one for 3 of 4 presets
- **Location:** `settings/advanced-debug.md`, "### Noise Level" mapping table (radius column) + the framing
  sentence "The mapping below is exactly what the plugin applies."
- **Finding:** The table lists Noise-reduction radius **0 / 3 / 3 / 5** for None/Low/Typical/High, but the
  values actually applied under defaults are **0 / 4 / 4 / 6**.
- **Evidence:** `DerivePresetSettings` sets base radius 0/3/3/5 (`StarDetectionOptions.cs:111,116,122,128`),
  then `if (HotpixelThresholdingEnabled && HotpixelFiltering) NoiseReductionRadius += 1;`
  (`StarDetectionOptions.cs:156-158`). `HotpixelThresholdingEnabled` defaults **true** and `HotpixelFiltering`
  is on for Low/Typical/High, so +1 applies to all three. Adversarially confirmed.
- **Recommendation:** Change the column to 0/4/4/6, or footnote it: "base 3/5; +1 is added whenever Hotpixel
  Thresholding is enabled (the default), so the applied radius is 4/6."

#### A2 — `[Minor]` Hot-pixel "+1 compensation" condition stated backwards
- **Location:** `settings/hotpixel-saturation.md:54`.
- **Finding:** "Simple mode accounts for this by adding 1 to the noise-reduction radius whenever filtering is
  on **without thresholding**." The code adds the +1 **with thresholding enabled**, not without — the
  rationale is that *with* thresholding the hot-pixel filter does not blur, so the extra radius compensates.
- **Evidence:** `if (HotpixelThresholdingEnabled && HotpixelFiltering) NoiseReductionRadius += 1;`
  (`StarDetectionOptions.cs:156-158`). (Reviewer-found; the area agent rated the page "accurate" and missed
  this — same root cause as A1.)
- **Recommendation:** Invert the clause: "…adding 1 whenever hot-pixel **thresholding is on** (the default),
  because thresholded filtering does not blur and so needs the extra noise reduction."

#### A3 — `[Major]` "Use Auto Focus Crop" claim is wrong for the HocusFocus AF engine
- **Location:** `settings/advanced-debug.md`, "## Use Auto Focus Crop" (lines ~208-210).
- **Finding:** The summary sentence "during actual autofocus the crop still applies regardless" is only true
  for NINA's **stock** AF engine. With the HocusFocus AF engine (what most plugin users run), the ROI crop
  **is** dropped during AF when the toggle is off. The first sentence's "NINA stock auto-focus run"
  qualifier is correct; the summary drops it.
- **Evidence:** crop suppression is gated on `isNinaAutoFocus`, computed from whether the selected pluggable
  AF behavior is the stock one (`HocusFocusStarDetection.cs:251-256`); a HocusFocus AF run sets it false.
  Adversarially confirmed.
- **Recommendation:** Mirror the precise earlier wording: "During a NINA *stock* auto-focus run the crop
  still applies regardless of this setting; for a HocusFocus AF run the toggle takes effect the same as for
  normal exposures."

#### A4 — `[Major→Completeness]` Undocumented inspector control: Detailed-Analysis exposure
- **Location:** `overview/tilt-aberration-inspector.md`, "Inspector options" table (lines 142-168).
- **Finding:** `InspectorOptions.DetailedAnalysisExposureSeconds` — a UI-exposed, TwoWay-bound, tooltipped
  field (the per-frame exposure for a Detailed Analysis sweep) — has no row, while the *distinct* "Simple
  Exposure (s)" and its panel-mates (Step Count/Size, Frames Per Point, Timeout) are documented. A reader
  running a Detailed Analysis cannot find the control for that run's exposure.
- **Evidence:** `InspectorOptions.DetailedAnalysisExposureSeconds` (property + tooltip in the inspector
  panel). Adversarially confirmed.
- **Recommendation:** Add a row: "**Detailed Analysis Exposure (s)** | -1 (auto) | -1 or >0 | per-frame
  exposure for a Detailed Analysis sweep; defaults to the AutoFocus exposure when blank," and one line
  distinguishing it from Simple Exposure.

#### A5 — `[Nit]` In-app StructureLayers tooltip is stale (source-side bug, doc already compensates)
- **Location:** `settings/structure-detection.md` (the known seed) + `OptionsDataTemplates.xaml:435`.
- **Finding:** The at-a-glance table's Default **4** is correct and the prose already flags the tooltip; but
  the underlying in-app tooltip still says "default of 5 (2⁵=32)" while the code ships 4
  (`StarDetectionOptions.cs:133,203,266`; structures > 2⁴=16 px). The doc's "describes the scaling
  relationship" framing is a charitable spin on what is simply a stale tooltip value.
- **Recommendation:** Keep the table at 4. Add a one-liner that the in-app tooltip is out of date, and file a
  source-side fix so the hover text reads "4 (2⁴=16)". (A `~2^L` precision nit: the coarsest B3-spline layer
  spans ~2^(L+1)+1 px — the existing "roughly" hedge covers it.)

### Findability — UI-label drift (Task 4) — the dominant theme

These all read: *documented name ≠ NINA's on-screen control label*. Verified against the control
`Content`/labels in `Resources/OptionsDataTemplates.xaml` (and `AutoFocus/DataTemplates.xaml` for the
inspector). Keep the C# property name parenthetically for discoverability, but lead with the UI label.

| # | Sev | Page | Documented as | NINA UI label |
|---|---|---|---|---|
| L1 | Major | advanced-debug, settings/index, overview/index | Use Advanced | **Advanced Mode** |
| L2 | Major | psf-modeling, settings/index | Model PSF / PSF Fit Type | **Fit PSF** / **PSF Type** |
| L3 | Major | overview/autofocus | Max Concurrent / AutoFocus Timeout (s) / HFR Improvement **Threshold** | **Max Concurrency** / **AutoFocus Timeout** / **HFR Improvement Tolerance** |
| L4 | Major | overview/star-annotation | Show Annotation Type / Annotation Color / Annotation Font | **Show Property** / **Property Color** / **Property Font** |
| L5 | Major | overview/star-annotation | Show Too Distorted / "…Color" | **Show Distorted** / **"&lt;Reason&gt; Box Color"** |
| L6 | Major | advanced-debug | Measurement Average / Use Auto Focus Crop / Save Intermediate Files / Save Intermediate Path | **Measurement Averaging** / **Use AutoFocus Crop** / **Save Intermediate** / **Intermediate Path** |
| L7 | Minor | psf-modeling | metric `PsfFitFailed` (×3) | code property **`PSFFitFailed`** (shown in the metrics panel) |

> The HFR one (L3) is the most misleading: prose, table, and the formula all call it "Threshold" while the
> control says "Tolerance" (backing property is `HFRImprovementThreshold` — note both).

### Audience-fit (Task 2)

#### F1 — `[Major]` No coherent end-to-end Advanced-tuning workflow
- **Location:** `settings/index.md` + per-knob pages; no dedicated tuning-procedure section exists.
- **Finding:** Routing logic and per-knob advice are excellent but never assembled into an ordered loop.
- **Recommendation:** Add a "Tuning workflow (Advanced mode)" section to `settings/index.md`: one
  symptom→action table + the loop *run detection → open metrics panel → find dominant rejection reason →
  go to the matching page → change one knob → re-detect → re-read the same number*.

#### F2 — `[Major]` The metrics panel is cited everywhere but never located
- **Location:** `overview/star-detection.md:135`; `settings/acceptance-gates.md:3`;
  `settings/hotpixel-saturation.md:92,101`.
- **Finding:** Nearly every feedback-signal instruction points at "the star-detection metrics panel," but no
  page says where it is in NINA, how to open it, or what it shows — undercutting the guidance it anchors.
- **Recommendation:** One short paragraph (ideally in `settings/index.md`, linked from each reference)
  locating the panel and listing the per-reason rejection counts it displays.

#### F3 — `[Major]` Rig-derived starting points stranded on the analysis page
- **Location:** `analysis/heuristic-defaults.md` vs the settings pages a reader actually opens.
- **Finding:** The actionable seeds — `StructureLayers L = clamp(round(log2(c·fwhmPx)),1,8), c≈3–4`;
  `MinStarBoundingBoxSize ≈ max(3, round(k·fwhmPx)), k≈2–3`; `PixelSampleSize` ramp by `fwhmPx`;
  `FWHM_px = FWHM_arcsec / pixelScale` — exist only on the analysis page. The settings pages' "When to
  adjust" give direction ("raise it at long focal lengths") but no rig-derived number. Adversarially
  confirmed (downgraded to Minor by the confirmer because the analysis page *does* carry the formula; kept
  here as Major for audience impact because it is absent *where the reader tunes*).
- **Recommendation:** Surface the one-line seed on each relevant settings page's "When to adjust" /
  at-a-glance, e.g. Structure Detection: "starting point — pick L so 2^L is a few × your FWHM in pixels
  (FWHM_px = FWHM_arcsec / pixel scale)."

#### F4 — `[Minor ×~16]` "When to adjust" names the trigger but not the feedback number
- Settings whose "When to adjust" is rated *partial* for a missing concrete feedback signal and/or
  magnitude/stopping-condition: `NoiseReductionRadius`, `StarClippingMultiplier`, `StructureLayers`,
  `StructureDilationSize`, `ModelPSF`, `UsePSFAbsoluteDeviation`, `PSFPixelIntegration`,
  `RejectContaminatedStars`, `Simple_NoiseLevel`, `UseAutoFocusCrop` (and a few more).
- **Recommendation:** For each, append the specific signal: e.g. Star Clipping Multiplier → "watch the
  **Degenerate** rejection count in the metrics panel" (`StarDetectorMetrics.Degenerate`,
  `IStarDetector.cs:619`); Noise Reduction Radius → "step +1 from the default of 3; watch detected-star
  count rise and the junk-candidate count fall, stop before faint stars drop."

### Terminology & jargon-on-first-use (Task 4, Minor)

- **`backfocus`** — headline feature on `index.md`/`overview/index.md` before any definition; also spelled
  inconsistently (`backfocus` vs `back-focus`, sometimes in the same paragraph). Gloss + pick one spelling.
- **`MAD`** — bare acronym at `overview/star-detection.md:24`; not expanded until `advanced-debug.md:185`.
- **`recall`/`precision`** — used as an objective-term name at `optimization/index.md:41,88` before defined
  (on `labels-recall-precision.md`). Add a one-clause parenthetical or inline link on first use.
- **`kappa-sigma`** — inconsistent casing (`kappa-sigma` vs `Kappa-Sigma`) across pages; add a half-sentence
  gloss on first use.
- **The à-trous wavelet is named four ways** — "B-spline wavelet", "à-trous B-spline wavelet", "à-trous
  wavelet", "à-trous B3-spline wavelet". Pick one canonical phrase + a one-line plain-language gloss.

### Semantic nits (Task 4, Nit)

- `overview/star-annotation.md` groups the **Show Saturated** toggle under "Rejection diagnostics," but
  saturated stars are *kept* (measured with masked pixels), not rejected. Add a one-line caveat.
- `acceptance-gates.md:146` MinHFR range reads "> 0 (must be non-negative…)" — self-contradictory phrasing
  (setter only throws on `< 0`; UI requires `> 0`). Reword.
- `tilt-aberration-inspector.md` calls the tilt-plane intercept *C* "the mean focus plane"; in code *C* is
  the OLS intercept and Adjustment Required subtracts `MeanFocuserPosition` — equal only on a symmetric grid.
  Harmless; noted for precision.

---

## 3. Coverage matrix

All 44 audited star-detection settings: **documented = yes; default-correct = yes; range-correct = yes**
(verified against `StarDetectionOptions.cs` initializers/setters, `IStarDetector.cs`, and the
`OptionsDataTemplates.xaml` validators). Only the *When-to-adjust* dimension and a few range-presentation
caveats vary; those are tabulated here (settings not listed are `full`).

| Setting | Doc | Default | Range | When-to-adjust |
|---|---|---|---|---|
| NoiseReductionRadius | ✅ | ✅ 3 | ✅ ≥0 (UI >0) | **partial** — no magnitude/feedback (F4) |
| StarClippingMultiplier | ✅ | ✅ 2.0 | ✅ ≥0 | **partial** — name the Degenerate metric (F4) |
| PixelSampleSize | ✅ | ✅ 1.0 | ✅ (0,1] — *table omits UI 25% floor* | full |
| StructureLayers | ✅ | ✅ 4 | ✅ >0 | **partial** — add 2^L≈FWHM_px seed + feedback (F3/F4) |
| StructureDilationSize | ✅ | ✅ 3 | ✅ ≥3 | **partial** — magnitude/feedback (F4) |
| ModelPSF | ✅ | ✅ On | ✅ bool | **partial** — explicit cost (F4) |
| UsePSFAbsoluteDeviation | ✅ | ✅ Off | ✅ bool | **partial** — feedback signal (F4) |
| PSFPixelIntegration | ✅ | ✅ Off | ✅ bool | **partial** — observable feedback (F4) |
| RejectContaminatedStars | ✅ | ✅ On | ✅ bool | **partial** — feedback signal (F4) |
| Simple_NoiseLevel | ✅ | ✅ Typical | ✅ enum | **partial** — how-to-tell + table bug A1 |
| UseAutoFocusCrop | ✅ | ✅ On | ✅ bool | **partial** — + accuracy A3 |
| **Other 33 settings** | ✅ | ✅ | ✅ | **full** |

(Full 44-row data with file:line evidence per setting is in the workflow result; the only ranges with a
presentation caveat are PixelSampleSize — UI 25% floor not in the table — and MinHFR — "> 0 / non-negative"
wording.)

Completeness diff: **no undocumented star-detection / annotation / autofocus settings** (the completeness
agent's grep "misses" were all UI-label-vs-symbol mismatches, i.e. theme L). The one genuine omission is the
inspector's Detailed-Analysis exposure (A4).

---

## 4. Highest-leverage improvements (ranked)

1. **Relabel controls to NINA's on-screen text (L1–L7).** Biggest return: it's the difference between a
   reader finding a setting in the options panel or not. Lead with the UI label, keep the property name in
   parentheses. ~17 spots, mechanical.
2. **Add a one-paragraph "where is the metrics panel" + a "Tuning workflow (Advanced mode)" section (F1, F2).**
   Turns excellent-but-scattered per-knob advice into an actionable loop and makes every "watch the metrics
   panel" instruction usable.
3. **Fix the two accuracy slips (A1, A3) and the inverted clause (A2).** Small edits; they're the only places
   the docs would actively mislead a tuning reader.
4. **Surface rig-derived starting points on the settings pages (F3)** — one seed line per relevant page so the
   reader gets a *number*, not just a direction, without leaving the page they're tuning on.
5. **Add the missing per-knob feedback signals (F4)** — name the specific metric/number for the ~16 "partial"
   knobs, and add the missing inspector exposure row (A4).
6. **Terminology hygiene** — define backfocus/MAD/recall/precision/kappa-sigma on first use; pick one
   wavelet phrase and one "backfocus" spelling.

**Bottom line:** the documentation is accurate and technically trustworthy — rare for a first version. The
work needed is not correction but *findability and workflow*: match NINA's labels, locate the feedback
panel, assemble the tuning loop, and bring the rig-derived seeds and feedback signals onto the pages where
the reader actually turns the knobs.
