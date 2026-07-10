# Optimizer control of the three detection-quality flags — design

## Context

Two prior changes to the star detector interact badly for users upgrading from `release/v3.0.0.26`:

1. **F4 σ-consistency** (commit `9b29d1b`) split the noise estimate into a blurred *structure* σ (binarize
   threshold only) and an honest *measurement* σ (~3.96× larger for white noise) that now feeds the sensitivity
   gate, clip margins, `MeasureStar` τ, and PSF floor. The Simple presets were rescaled (`BrightnessSensitivity`
   base `10.0 → 2.0`, i.e. `×0.2`) so the *effective* gate is ~unchanged.
2. A working-tree edit reverted only the `BrightnessSensitivity` half of F4 while leaving the σ split in place,
   which made the default sensitivity gate ~4× **stricter** than baseline. That edit has been reverted
   (`git restore`), restoring F4.

Separately, three new detector features default ON in 4.x and did not exist in `v3.0.0.26`:
`LocallyAdaptiveBinarization`, `RejectContaminatedStars`, `ExcludeSaturatedStarsFromHFR`. They are validated
improvements (see `docs/af-bank-noiseclip-sweep-results.md`: adaptive binarization lifts bank-median AF σ_focus
`10.26 → 8.84`).

## Goal

Make the Star Detection Optimizer able to **enable and tune** the three flags, seeded ON when a run starts, and
persist the winning choice so it survives Accept and tilt-calibration replay. The optimizer is the sanctioned
place these features are dialed in per optical train.

## Decisions

- **Default state is unchanged — all three stay ON** on the non-optimized (Simple) path. (Maintainer decision:
  the F4 revert already removed the real upgrade regression; these three are net-positive and stay on.)
- **Seed the three flags ON at the start of every optimizer run**, mirroring the existing
  `seed.DefocusAwareDonutDetection = starDetectionOptions.DefocusAwareDonutDetection` stamp in
  `StarDetectionOptimizerWizardVM.OptimizeAsync`. Because they are also search axes, the optimizer can turn any
  of them back OFF for a rig where it hurts the objective. The stamp runs **after** `ComputeBaselineJAsync`
  (line ~1403) computes the user-facing "before" J, so it does not contaminate the baseline comparison.
- **`ContaminationSensitivity` is left fixed** (not a search axis, not carried in the optimizer snapshot). The
  request was to enable the boolean gate; the companion threshold keeps its live/default value (5.0). It is
  already present in the replay `StarDetectionSettingsSnapshot`. Making it optimizer-tunable is a clean future
  extension if wanted.

## Why the optimizer can actually act on these

- **Objective responds.** `LocallyAdaptiveBinarization` and `RejectContaminatedStars` change the accepted star
  set (recall/precision + per-frame star-count/σ terms); `ExcludeSaturatedStarsFromHFR` changes the HFR
  aggregation → `SigmaFocus`. None are inert to `OptimizationObjective.JRun`.
- **Cache key distinguishes them.** `StarDetector.ComputeCacheKey` is a denylist over `ToCanonicalCacheString`
  (every param included by default), so flipping any flag yields a distinct memo key — the compass search sees
  the real ΔJ instead of a stale cache hit.
- **Staging is correct for free.** `LocallyAdaptiveBinarization` is in `EarlyCacheKeyProperties` (candidate
  formation) → the optimizer stages it EARLY (bounded, expensive rebuilds). The other two are absent → LATE
  (per-frame cache hits, cheap). `IsEarlyCacheKeyParameter` already classifies them; no optimizer-staging code
  changes are needed.

## Change set

1. **`OptimizerVariable.CreateCuratedSet`** — add three `BooleanVar` base axes: `LocallyAdaptiveBinarization`,
   `RejectContaminatedStars`, `ExcludeSaturatedStarsFromHFR` (base 12 → 15; full no-arg set 21 → 24).
2. **`OptimizedStarDetectionSettings`** — add `RejectContaminatedStars` and `ExcludeSaturatedStarsFromHFR` DTO
   fields, defaulted `true` (backward-compat: an old snapshot missing the keys deserializes to the same values
   the pre-change apply path left them at — live default ON — so no silent flip for existing snapshots). Populate
   both in `FromParams`. Bump `SchemaVersion` 2 → 3. (`LocallyAdaptiveBinarization`/`AdaptiveNoiseBlockSize` were
   already carried.)
3. **`StarDetectionOptions.ApplyOptimizedSnapshotToLiveProperties`** — apply the two new snapshot fields to the
   live options.
4. **`TiltAdapterWizardVM.OverlayOptimizedSettings`** — copy the two new fields onto the replay
   `StarDetectionSettingsSnapshot` (which already exposes both properties). Enforced by the reproducibility guard
   `OverlayOptimizedSettings_AppliesEveryCuratedKnob` — a DTO knob missing from the overlay fails that test.
5. **`StarDetectionOptimizerWizardVM.OptimizeAsync`** — stamp the three flags ON onto the seed.

## Test & doc impact

- `OptimizerVariableTests`: variable count 21 → 24 and master-off 12 → 15; add the three names to the expected
  name list; rename the count test.
- `OptimizedStarDetectionSettingsTests`: default `SchemaVersion` 2 → 3; add the two fields to the round-trip and
  `FromParams` coverage.
- `TiltAdapterWizardVMTests.OverlayOptimizedSettings_AppliesEveryCuratedKnob`: self-enforcing (passes once the
  overlay copies the two fields).
- Docs: `documentation/docs/optimization/search-variables.md` gains the three new search variables.
