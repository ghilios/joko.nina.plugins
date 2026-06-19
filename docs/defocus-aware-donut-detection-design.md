# Defocus-Aware Donut Detection — design & results

## Problem

On wide-range autofocus runs with a central-obstruction scope, heavily-defocused stars become hollow
**donuts** (rings). The star detector dropped most of them, and once detection was made aggressive enough to
catch some, a bright **saturated star's diffraction spikes** were mis-detected as multiple stars. Root cause,
confirmed on the `mufti` run (`AutoFocus_20260607_030642…`) via the detector's own metrics on the most
defocused frame: of 279 structure candidates only 43 were accepted — dominated by **TooSmall = 122** (rings
fragment into arcs) and **TooDistorted = 99** (whole hollow rings fail the fill-ratio test).

## Approach (opt-in, default OFF, bit-identical when off)

A single profile-saved **`DefocusAwareDonutDetection`** master toggle (optimizer-wizard start page + Advanced
options) gates everything. When OFF, detection is byte-for-byte the legacy path.

When ON:
- **Structure survival (EARLY).** Large donuts are erased by the structure-removal wavelet *before* later
  stages can act, so the master applies a default **+2 wavelet-layer boost** (`DonutDefaultStructureLayerBoost`)
  so big rings survive; the optimizer can raise it further (`DefocusAwareStructure`/`StructureLayerBoost`).
- **Fragmentation fix (EARLY).** A morphological **close** of the binarized structure map reconnects ring arcs
  into one candidate (`DonutMorphCloseSize`, default 5). Kernel ≪ spike gaps so it never bridges diffraction
  spikes.
- **Hollow-ring fix (LATE, detection-only).** An **annularity hole-count** (`CountEnclosedHole`) adds the
  enclosed dark-center pixel count to the fill-ratio used by the TooDistorted gate, so a ring is judged like a
  filled disk (`DonutMinAnnularityHoleFraction`, default 0.15). It does **not** mutate the measured point set,
  so HFR / flux / centroid are byte-identical (unit-tested).
- **Gate relaxation (default ON with the master).** The distortion + centering relaxations
  (`ComputeEffectiveMaxDistortion` / `ComputeEffectiveStarCenterTolerance`) are enabled intrinsically when the
  master is on (they only relax for LARGE candidates, so near-focus point sources are unaffected). This handles
  *sparse* faint donuts whose ring isn't a clean enclosed hole (so hole-fill can't help them) — the candidates
  the live run reported as "TooDistorted".
- **Donut-aware sensitivity (default ON with the master).** A faint defocused donut spreads its flux thinly, so
  its per-pixel peak (and `NormalizedBrightness`) is low even when the *integrated* ring flux is a strong
  detection. For an EXTENDED candidate (bbox ≥ `DefocusDistortionSizeReference`) the `LowSensitivity` gate also
  accepts on the **integrated-flux SNR** `TotalFlux / (σ·√N)` (the matched-filter statistic, √N× more sensitive
  to extended sources). The same `Sensitivity` threshold now means "σ of an integrated detection" on this path,
  so the bar stays high — small fragments / point noise keep the strict per-pixel floor. Recovers the faint
  donuts the live run reported as "LowSensitivity".
- **Spike/bloom suppression (LATE, default OFF, optimizer-enabled via labels).** A second-moment **eccentricity
  streak gate** (`DonutMaxStreakEccentricity`, 1.0 = off → `TooElongated`) and a **saturation bloom-zone**
  (`DonutSaturationBloomRadius`, 0 = off → `BloomSuppressed`). Diffraction spikes only arise from a
  spider/central obstruction — the same optics that produce donuts — so they're correctly bundled under the
  donut master.
- **Optimizer.** Every defocus-aware axis (the combined gate switch, structure boost, the three gate tuning
  knobs, and the donut/spike knobs) is included in the curated search set **only when the master is on**; the
  `SDefocusPrecision` near-focus penalty + the label term keep precision.

Optimized defocus values persist via `OptimizedStarDetectionSettings` schema **v2** (back-compatible: a v1
snapshot loads as master-OFF/legacy).

## Validation (headless, vs an independent by-eye golden set)

Golden set: every real star in all 7 sweep frames cataloged by eye (tiled, proposer-assisted but eye-verified),
4666 stars + saturated-star spike boxes as should-reject. Rendered via the new `TestApp annotate` tool on the
real plugin stretch. Headless `optimize --labels` (100 evals) on the mufti run:

| | recall | precision | σ_focus |
|---|---|---|---|
| Baseline (master OFF, optimized) | 0.098 | 0.992 | 28.9 → 7.5 |
| **Donut mode (master ON, optimized)** | **0.226** | **1.000** | 10.3 → 5.6 |

Recall ~2.3× with perfect precision; per-frame accepted counts on the worst frames rose ~4× (e.g. Focuser2925
8 → 31, Focuser2325 12 → 50). The saturated star's core and spikes produced **no** spurious detections. Recall
is 22.6% because the by-eye golden set is very dense (~666 stars/frame, incl. faint near-noise rings);
precision 1.0 confirms the recovered detections are real.

### Live-NINA follow-up (donut-aware sensitivity + master-default gate relaxation)

A live NINA run with the master on still rejected many donuts. `diagnose-labels` on the golden set gave the
definitive breakdown (master on, before the fixes below): of 4666 golden boxes — 1317 ACCEPTED, 1437
NO-CANDIDATE (below the binarization floor), 943 LowSensitivity, 721 TooDistorted. Root cause: the faint
*diffuse* extreme-defocus donuts have no clean enclosed hole (hole-fill can't fire) and a low per-pixel peak,
and the master did not default the distortion relaxation on. Adding the **integrated-flux sensitivity** and
**defaulting the distortion/centering relaxation on with the master** raised ACCEPTED 1317 → **1490** (+173,
recall 28% → 32% by box-containment) with **shouldReject still 0 admitted** (the saturated star is never
falsely detected). The remaining rejects (NO-CANDIDATE + small-fragment LowSensitivity + sparse TooDistorted)
are genuinely at the noise floor — recovering them would require lowering the global binarization/sensitivity
floor (a precision trade), which the dense golden set inflates.

### Follow-up 2: Off-Center & Degenerate recovery (donut-aware clip cap)

After the sensitivity fix, a live run still rejected many donuts as **Off-Center (NotCentered)** and **Degenerate
Shape (Degenerate)**. `diagnose-labels` on the golden set (live profile, master on) attributed, of 4666 golden
boxes: 932 ACCEPTED, 184 NotCentered, 198 Degenerate (plus 1521 TooDistorted / 1437 NO-CANDIDATE upstream).
Cropping the flagged boxes confirmed they are **real donut rings** (clear hollow rings at the opposite defocus
extreme), with strong signal (ring peak 7–13σ, 200–480 px above 3σ).

**Single root cause — an aggressive per-pixel clip strips the thin ring.** The clip margin used for flux /
centroid / HFR is `StarClippingMultiplier × noiseSigma`. This run's profile had `StarClippingMultiplier = 9.5`
(default 2.0; pushed high by a prior optimization — the optimizer's own recommendation here is 9.5 → 2.0). A
defocused donut spreads its flux thinly, so each ring pixel sits only a few σ above background; a 9.5σ clip
exceeds the ring's per-pixel amplitude and removes the **entire** ring, which:
- leaves ≤1 surviving pixel → the `ComputeStarParameters` degenerate guard fires → **Degenerate**; and
- when a few of the brightest pixels do survive, they are the brightest **arc** of the ring → the flux-weighted
  centroid is pulled off the geometric (hole) center → **NotCentered**.

This was proven by instrumenting the degenerate path (ring rawRange `[0.0140, 0.0184]`, bgMed `0.0141`,
**clip `0.0053` > ring amplitude `0.0043`** → flatSurv = 0) and by a centroid replication: at a 9.5σ clip 0–15
pixels survive and the centroid offset is 0.35–0.55 (rejected); at a 2σ clip 276–581 pixels survive and the
offset drops to 0.11–0.20 (accepted).

**Fix — donut-aware clip cap (`EffectiveClipMultiplier` / `DonutClipMultiplierCap = 2.0`).** For an EXTENDED
candidate (bbox max-dim ≥ `DefocusDistortionSizeReference`, the existing defocused-star proxy) when the master
is on, the effective clip multiplier is `Math.Min(StarClippingMultiplier, 2.0)` — the honest default τ from the
sigma-consistency work — applied consistently in `ComputeStarParameters` (flux/centroid/degenerate) **and**
`MeasureStar` (HFR). It only ever LOWERS the clip, so profiles with `StarClippingMultiplier ≤ 2.0` are
unchanged even with the master on, and master-OFF is bit-identical. Lowering the clip lets the full ring back
into the flux sum, which fixes the survivor count (Degenerate) and re-centers the centroid (NotCentered) in one
move.

**Result** (golden set, master on, vs the live profile): ACCEPTED **932 → 1174** (+242; recall 20% → 25%),
NotCentered **184 → 46**, Degenerate **198 → 113**, with the saturated-star/spike `shouldReject` boxes still at
**0 ACCEPTED** (precision held). **AF-fit impact** (identical current settings, fix off → on): the per-position
star count at the defocus extremes roughly **doubles** (e.g. focuser 2925 35 → 70, 2325 56 → 89), giving more
robustness against the ≥3-star hard floor; the V-curve fit stays excellent (R² 0.9995 → 0.9967) with a small
rise in focus-position scatter (σ_focus 1.8 → 4.4 steps, still ≪ the recommended 36-step interval) from the
extra extreme-defocus wing points. The deeper remedy is to re-optimize the profile (the optimizer drops
`StarClippingMultiplier` to ~2.0 on its own); the cap is the safety net that keeps an aggressive clip from
destroying donuts.

## Key files

- `StarDetection/StarDetector.cs` — EARLY structure boost + morph-close; LATE hole-count, streak gate, two-pass
  bloom suppression; donut-aware clip cap (`EffectiveClipMultiplier` / `DonutClipMultiplierCap`, applied in
  `ComputeStarParameters` + `MeasureStar`); `CountEnclosedHole` / `ComputePointCloudEccentricity` helpers.
- `Interfaces/IStarDetector.cs` — `StarDetectorParams` donut fields; `EarlyCacheKeyProperties` (master +
  morph-close); `TooElongated`/`BloomSuppressed` metrics + `RejectionGate` consts.
- `StarDetection/HocusFocusStarDetection.cs` — `BuildStarDetectorParams`/`BuildDefaultStarDetectorParams`
  master-gating + lockstep.
- `StarDetection/StarDetectionOptions.cs` + `Interfaces/IStarDetectionOptions.cs` — master + 4 knobs.
- `StarDetection/Optimization/OptimizerVariable.cs` — master-gated curated set.
- `StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` + `.../DataTemplates.xaml` — start-page toggle.
- `Resources/OptionsDataTemplates.xaml`, `AutoFocus/DataTemplates.xaml` — Advanced UI + metrics panel.
- `TestApp/AnnotateRunner.cs` — `annotate` subcommand (CSV → real-stretch overlay). `optimize --donut` forces
  the master on for headless A/B.

Tests: `StarDetection/DonutDetectionTests.cs` (bit-identical-when-off, EARLY/LATE classification, hole-count &
eccentricity geometry, donut recovery, HFR invariance). Full suite 1286 passing.
