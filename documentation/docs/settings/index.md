# Star Detection Settings

Hocus Focus's star detector turns a raw frame into a set of accepted stars with measured Half-Flux Radius (HFR), and optionally a fitted PSF for FWHM and eccentricity. Almost every tunable knob feeds a single parameter bundle (`StarDetectorParams`) built by one function, `BuildStarDetectorParams`, so what you set in NINA's plugin options is exactly what the detector runs, and exactly what the headless tooling reproduces.

This page is the entry point to the settings reference. It explains the two ways to drive the detector (**Simple mode** vs **Advanced mode**), where to find the options in NINA, the high-level shape of the detection pipeline, and the practical EARLY-vs-LATE parameter distinction. Each pipeline stage links to its own sub-page where every setting is documented with its tooltip, default, range, and when to adjust it.

## Where to find these settings

Open NINA's options, go to the **Plugins** tab, and select **Hocus Focus**. The star-detection settings live under the star-detection options area. Two top-level switches decide which controls you see:

- **Advanced Mode** — exposes the full set of fine-grained parameters. Tooltip:

    > Enables advanced mode with fine-grained control over star detection parameters. Not recommended unless you're an expert

    Backed by the `UseAdvanced` option (default **off**).

- **Use Optimized Settings** — drives detection from a snapshot produced by the Optimization Wizard instead of the Simple-mode presets. Tooltip:

    > When enabled, the optimized star-detection settings produced by the optimization wizard drive detection instead of the Noise Level / Pixel Scale / Focus Range presets. Only available in Simple Mode after the wizard has produced and applied a result

    Backed by the `UseOptimizedSettings` option (default **off**). It only does anything once the wizard has produced and applied a result.

## Simple mode vs Advanced mode

Most users should stay in **Simple mode** (Advanced Mode off). In Simple mode you do not edit the low-level parameters directly. Instead, three plain-language presets are translated into a full parameter set every time you change one of them, the active profile changes, or you toggle the relevant switches. The translation lives in `DerivePresetSettings`, and it sets *all* the advanced knobs for you, so the advanced controls become outputs of the presets rather than independent inputs.

The three Simple-mode presets are:

| Preset | Default | What it controls |
|---|---|---|
| **Noise Level** | Typical | How much the source image is blurred/noise-handled before detection |
| **Pixel Scale** | Typical | Adjusts for apparent star size at your focal length |
| **Focus Range** | Typical | Whether heavily out-of-focus (donut) stars should still be found |

**Noise Level** — tooltip:

> Controls the amount of blurring done on the source image before beginning the star detection process. Increase this if you have a particularly noisy sensor or shoot at a very high focal ratio

Internally, this preset chooses the noise-reduction radius, whether measurement-time noise reduction is on, whether hotpixel filtering runs, and the σ-based sensitivity scale. `None` disables noise reduction entirely; `Low`/`Typical` apply a small blur; `High` enables star-measurement noise reduction with a larger radius. See [Preprocessing & Noise](preprocessing.md).

**Pixel Scale** — tooltip:

> At high focal lengths, pixel scale decreases and apparent star size (in pixels) increases. Setting this to a higher focal length increases the number of layers considered for star detection, which could increase false positives due from nebulosity

`WideField` removes a structure layer and shrinks the minimum bounding box; `LongFocalLength` adds a layer, grows the minimum box, and increases sensitivity. See [Structure & Detection](structure-detection.md) and [Star Acceptance Gates](acceptance-gates.md).

**Focus Range** — tooltip:

> As you get further from critical focus, stars become large donuts. Star detection will typically consider them too large to be stars when you're far enough from focus. Enabling a wide range increases the number of layers considered for star detection

`WideRange` adds a structure layer and increases sensitivity so larger, more defocused stars survive. See [Structure & Detection](structure-detection.md).

!!! tip "When to leave Simple mode"

    Stay in Simple mode unless you have a specific reason not to. If the presets don't give you enough stars on your rig, the recommended next step is **not** to start hand-tuning advanced knobs. Instead, run the [Optimization Wizard](../optimization/index.md), which tunes detection against your own saved auto-focus runs and can apply the result as your Simple-mode settings. Advanced mode is for experts who want to set each parameter by hand.

### How the optimized snapshot interacts with the presets

When **Use Optimized Settings** is on and a wizard result exists, Simple mode first derives the preset baseline, then overlays the curated subset of parameters from the saved snapshot (sensitivity, clipping multipliers, peak response, distortion, min HFR, center tolerance, structure layers, noise-reduction radius, minimum bounding box, and the hotpixel knobs). Non-curated advanced knobs keep their preset defaults; the curated ones win. See [Labels, Recall & Precision](../optimization/labels-recall-precision.md) for how those values are chosen.

## Reading the results: the Star Detection Results panel

Throughout these pages you are told to "watch the metrics panel." That panel is the **Star Detection
Results** readout that Hocus Focus shows after a detection (and in the autofocus report). It is the feedback
signal for every adjustment below. It reports:

- **Structure candidates** — bright structures evaluated as potential stars before any gate.
- **Total detected** — stars accepted after all gates.
- a **per-reason rejection count** for each gate: **Too Small**, **On Border**, **Too Distorted**, **Not
  Centered**, **Too Flat**, **Low Sensitivity**, **Saturated** (kept, not rejected: pixels masked during
  PSF fitting), **Degenerate**, and **Contaminated**.

When you change a setting, re-run detection and watch the count for the gate you are tuning. That number,
not a subjective look at the image, tells you whether the change helped.

## Tuning workflow (Advanced mode)

Advanced tuning is a short, repeatable loop, not a one-shot. Change **one** knob at a time and confirm the
effect in the [Star Detection Results panel](#reading-the-results-the-star-detection-results-panel) before
moving on:

1. Run star detection on a representative frame and open the Star Detection Results panel.
2. Identify the dominant problem: either real stars **missing entirely** (no marker at all) or stars
   **rejected with a reason** (a high per-reason count).
3. Route to the right page:
    - **Missing entirely** — the candidate was never formed: tune [Structure & Detection](structure-detection.md)
      (and [Preprocessing](preprocessing.md) / [Hot Pixels](hotpixel-saturation.md)).
    - **Rejected with a reason** — the candidate formed but a gate dropped it: go to
      [Acceptance Gates](acceptance-gates.md) (or [Contamination](contamination.md)) for that reason.
4. Change one setting in the direction that page recommends.
5. Re-detect and re-read the same count. Keep the change if the target count improved without hurting the
   others; otherwise revert and try the next candidate.

| Symptom in Star Detection Results | Page | Knob |
|---|---|---|
| Real stars with no marker at all | Structure & Detection | Structure Layers, Noise Clipping Multiplier, Dilation |
| High **Too Small** | Acceptance Gates | Min Star Bounding Box Size |
| High **Too Distorted** / **Not Centered** | Acceptance Gates | Max Distortion / Star Center Tolerance (or Defocus-Aware Gates if defocused) |
| High **Too Flat** | Acceptance Gates | Star Peak Response |
| High **Low Sensitivity** | Acceptance Gates | Brightness Sensitivity |
| High **Degenerate** | Preprocessing | Star Clipping Multiplier |
| High **Contaminated** | Contamination | Contamination Sensitivity / Reject Contaminated Stars |
| Hot pixels detected as stars | Hot Pixels & Saturation | Hotpixel Filtering / Hotpixel Threshold |

## The detection pipeline at a glance

A frame flows through the detector in roughly this order. The figure below shows the heart of it: a raw frame (with nebulosity) is reduced to a wavelet residual that suppresses large-scale structure, then binarized into a structure map of star candidates.

![Raw frame with nebula reduced to a wavelet residual, then binarized into a structure map of star candidates](../assets/figures/structure-map.png){ width=620 }

*The structure-detection core: large-scale structure (nebula) is removed by the à-trous wavelet residual, and the result is thresholded into a binary candidate map.*

| Stage | What happens | Settings page |
|---|---|---|
| **Preprocessing & noise** | Hotpixel filtering, optional Gaussian blur, two noise-σ estimates | [Preprocessing & Noise](preprocessing.md) |
| **Structure & detection** | Wavelet layers → noise-clipped binarization → dilation → candidate flood-fill | [Structure & Detection](structure-detection.md) |
| **Star acceptance gates** | Size, distortion, centering, sensitivity, flatness, min-HFR checks per candidate | [Star Acceptance Gates](acceptance-gates.md) |
| **Hot pixels & saturation** | Hotpixel threshold, saturation rejection threshold | [Hot Pixels & Saturation](hotpixel-saturation.md) |
| **PSF modeling** | Optional Gaussian/Moffat fit for FWHM, eccentricity, goodness-of-fit gate | [PSF Modeling](psf-modeling.md) |
| **Contamination rejection** | Gradient-robust test that flags/rejects stars with a one-sided neighbor | [Contamination Rejection](contamination.md) |
| **Advanced & debug** | Measurement averaging, defocus-aware gates, intermediate file output, debug mode | [Advanced & Debug](advanced-debug.md) |

Each accepted star contributes its HFR (and, with PSF modeling on, FWHM and eccentricity) to the frame's aggregate measurement; rejected candidates are tallied by reason and can be visualized in the annotation overlay.

## EARLY vs LATE parameters

Internally the detector splits into an **EARLY** phase (`BuildDetectionContext`) and a **LATE** phase (`GateAndMeasure`):

- **EARLY** parameters affect the prepared image, the candidate region set, and the noise estimates. Changing one forces a **full re-detect**. These are the hotpixel knobs (`HotpixelFiltering`, `HotpixelThresholdingEnabled`, `HotpixelThreshold`), noise reduction (`StarMeasurementNoiseReductionEnabled`, `NoiseReductionRadius`, `NoiseClippingMultiplier`), the structure-map knobs (`StructureLayers`, `DefocusAwareStructure`, `StructureLayerBoost`, `StructureDilationSize`, `StructureDilationCount`), `SaturationThreshold`, and the detection `Region`.
- **LATE** parameters only re-gate or re-measure the candidates that already exist (sensitivity, distortion, centering, min-HFR, PSF settings, the defocus-aware *gate* relaxations, contamination). They are cheap to change.

You don't normally need to think about this when using NINA, since it changes whichever parameters you change. The distinction matters because the [Optimization Wizard](../optimization/index.md) and the headless tooling exploit it: an expensive early context is cached and reused across many late-only candidate moves, which is what makes the optimizer fast. The safe failure mode of the cache is always a redundant recompute, never stale reuse.

!!! note "Profile-scoped and auto-saved"

    All star-detection settings are stored per NINA profile. Switching profiles re-derives Simple-mode settings from that profile's presets. There is also a **Reset to defaults** action that restores every parameter to its shipped value (Simple mode, Typical presets, PSF modeling on with Moffat 4.0, and so on) and clears any optimized snapshot.

## Reference sub-pages

- [Preprocessing & Noise](preprocessing.md) — hotpixel filtering, noise reduction radius, clipping multipliers, measurement noise reduction.
- [Structure & Detection](structure-detection.md) — structure layers, dilation, brightness sensitivity, defocus-aware structure.
- [Star Acceptance Gates](acceptance-gates.md) — distortion, centering, peak response, min bounding box, min HFR, defocus-aware gates.
- [Hot Pixels & Saturation](hotpixel-saturation.md) — hotpixel threshold and saturation rejection threshold.
- [PSF Modeling](psf-modeling.md) — fit type, resolution, goodness-of-fit threshold, pixel integration.
- [Contamination Rejection](contamination.md) — contamination sensitivity and reject-vs-flag behavior.
- [Advanced & Debug](advanced-debug.md) — measurement averaging, pixel sample size, intermediate files, debug mode.
