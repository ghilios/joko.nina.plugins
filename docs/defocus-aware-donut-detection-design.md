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

## Key files

- `StarDetection/StarDetector.cs` — EARLY structure boost + morph-close; LATE hole-count, streak gate, two-pass
  bloom suppression; `CountEnclosedHole` / `ComputePointCloudEccentricity` helpers.
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
