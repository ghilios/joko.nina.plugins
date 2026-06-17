# Star Detection Heuristic Defaults — Analysis

**Status:** Analysis & recommendations only. No production behavior changes are proposed for this PR; this
document records which star-detection options *could* be derived heuristically (from measurable rig
properties) rather than tuned empirically, with concrete candidate formulas for follow-up work.

## Context / Problem

The star detector exposes ~30 tunable options (see `StarDetection/StarDetectionOptions.cs`). The best values
depend on the optical setup, and most are set today by one of three empirical mechanisms:

1. **Simple-mode presets** — `DerivePresetSettings()` in `StarDetectionOptions.cs` already maps three
   coarse user choices (`Simple_NoiseLevel`, `Simple_PixelScale`, `Simple_FocusRange`) onto a subset of the
   advanced settings. This is itself a heuristic layer.
2. **Manual Advanced-mode tuning.**
3. **The Optimization Wizard** — `StarDetection/Optimization/*` searches a curated subset against an
   autofocus-curve objective (see `docs/star-detection-optimization-wizard-design.md`).

Several of these options are, in principle, *computable* from quantities the plugin already has or can
measure: the **pixel scale** (arcsec/px, from the profile), the **expected star size** (FWHM, in arcsec →
pixels), the **background noise** σ (the detector estimates this via Kappa-Sigma / MAD), the sensor **full
well** / saturation point, and the local **star density** (from the detections themselves). This note
classifies every option and proposes formulas where a heuristic is defensible.

Define the working quantities:

- `pixelScale` — arcsec per pixel (profile: pixel size and focal length, × binning).
- `fwhmPx = fwhmArcsec / pixelScale` — expected stellar FWHM in pixels (seeing + optics).
- `sigmaBkg` — background noise standard deviation, MAD-based, as already computed in detection.
- `fullWell` — `2^bitDepth − 1` (or the sensor's linear full well).
- `density` — accepted stars per unit area, available after a first detection pass.

## Classification

### A. Strong heuristic candidates (derivable from rig properties)

| Option | Current default | Proposed derivation | Rationale |
|---|---|---|---|
| `StructureLayers` | 4 | `L = clamp(round(log2(c · fwhmPx)), 1, 8)`, c ≈ 3–4 | The à-trous wavelet removes structures larger than ~`2^L` px. Sizing `2^L` to a small multiple of the star's pixel size keeps stars while removing nebulosity/gradients. `fwhmPx` follows from pixel scale. The `Simple_PixelScale` preset already nudges this ±1. |
| `MinStarBoundingBoxSize` | 5 | `max(3, round(k · fwhmPx))`, k ≈ 2–3 | A real star's bounding box spans a few × FWHM. A pixel-scale-relative floor rejects sub-PSF noise specks without dropping genuine small stars at long focal length. Preset already shifts ±1. |
| `PixelSampleSize` | 1.0 | `1.0` if `fwhmPx ≥ ~3`, ramp toward `0.5` as `fwhmPx → ~1.5`, `< 0.5` if `fwhmPx < 1` | Sub-pixel (bilinear) sampling only helps undersampled rigs. The benefit is a direct function of FWHM in pixels — exactly the undersampling measure. Preset sets 0.5 for wide-field. |
| `NoiseReductionRadius` | 3 | scale with `sigmaBkg / peakSignal` (more blur when noisier); map to a small integer radius | Blur trades noise suppression for resolution; the right amount tracks the noise-to-signal ratio, which is measured. Preset already ties this to `Simple_NoiseLevel`. |
| `HotpixelThreshold` | 0.001 (frac. full well) | noise-relative: flag when `|median3x3 − pixel| > k · sigmaLocal` (k ≈ 5–8), or a high quantile gated by an absolute noise floor | See worked example below. The current full-well fraction mis-scales across gain/exposure; a noise-relative form adapts and flags nothing on clean frames. |
| `StarBackgroundBoxExpansion` | 3 | scale inversely with `density` (wider annulus when sparse, tighter when crowded), clamped to ≥1 | The annulus should sample background, not neighbors. Optimal width depends on crowding, which is measurable after a first pass. |

### B. Already in σ-units / near-universal (little per-rig tuning needed)

These are σ-multiples or dimensionless ratios. Because the sensitivity recalibration made the noise an
**honest** multiple of the measurement-image noise (see `docs/f11-meanflux-sensitivity-recalibration-design.md`
and `docs/sigma-consistency-design.md`), their defaults travel well:

- `NoiseClippingMultiplier` (4.0) — binarization floor in σ above background.
- `StarClippingMultiplier` (2.0) — measurement-pixel clip in σ.
- `BrightnessSensitivity` (2.0) — acceptance on `(s−b)/n`.
- `ContaminationSensitivity` (5.0) — annulus asymmetry in σ.
- `SaturationThreshold` (0.99) — a natural fraction of full well; could be set from the sensor's known
  linearity knee but 0.99 is near-universal.
- `MaxDistortion` (0.5), `StarCenterTolerance` (0.3), `StarPeakResponse` (0.75) — geometric/flatness ratios,
  scale-free.

Recommendation: keep these as fixed defaults; expose for tuning but do not auto-derive.

### C. Empirical / preference / performance (leave to the optimizer or the user)

- `DefocusAwareGates` + `DefocusDistortionSizeReference` / `DefocusDistortionMinFactor` /
  `DefocusCenteringToleranceFactor` — relaxations whose value is a trade-off the objective's
  `SDefocusPrecision` penalty is designed to police; the optimizer explores them.
- `MeasurementAverage` (Median / Mean / MeanOutliers) — a robustness preference.
- PSF options (`PSFFitType`, `PSFResolution`, `PSFFitThreshold`, `UsePSFAbsoluteDeviation`,
  `PSFPixelIntegration`, `PSFParallelPartitionSize`) — quality/performance knobs, only weakly rig-dependent
  (PSFPixelIntegration does track undersampling and could be auto-enabled for `fwhmPx ≲ 1.5`).
- `MinHFR` (1.2) — a viability floor; mildly rig-dependent, safe as a constant.

## Worked example: HotpixelThreshold

`HotpixelThreshold` (default `0.001`) is consumed in `BuildStarDetectorParams` and applied as a fraction of
full well: a pixel is replaced when `|median3x3 − pixel| > HotpixelThreshold · (2^bitDepth − 1)`. The user's
proposal — *"set it as a percentage of total pixels"* — is worth evaluating precisely, because it is
appealing but, taken literally, unsafe:

- **Percentage-of-pixels (quantile) alone over-flags clean frames.** A calibrated frame (dark/bad-pixel-map
  applied) has ~0 hot pixels. A rule that flags the top X% of `|median3x3 − pixel|` values will always flag
  X% of pixels — i.e. it manufactures corrections on images that need none, biasing HFR/PSF.
- **A fixed full-well fraction is not adaptive.** The same ADU gap is many σ at low gain and sub-σ at high
  gain; it ignores the actual noise.

**Recommended heuristic — noise-relative threshold.** Flag a pixel as hot when its deviation from the local
median exceeds a multiple of the local noise:

```
isHot(p) = |median3x3(p) − p| > k · sigmaLocal      # k ≈ 5–8
```

Equivalently, choose the threshold as a high quantile of `|median3x3 − pixel|` **but gated by an absolute
floor of `k · sigmaBkg`**, so that on a clean frame (where the quantile sits at the noise level) nothing trips.
`sigmaBkg` is already computed during detection, so no new measurement is required. This:

1. adapts per sensor, gain, and exposure automatically;
2. flags essentially nothing on clean/calibrated frames;
3. expresses the threshold in the same σ-currency as the rest of the gates (category B), which the codebase has
   deliberately standardized on.

A migration could keep the existing full-well-fraction control for back-compat and add a noise-relative mode
(default), mirroring how `HotpixelThresholdingEnabled` already gates replacement to large deviations.

## Recommendations

1. **No behavior change in this PR.** This is analysis only.
2. For follow-up: prototype noise-relative `HotpixelThreshold` (highest value-per-effort; removes a
   full-well-coupled magic number) behind a mode flag, validated with the `TestApp contamination` /
   `optimize` harnesses.
3. Consider extending the Simple-mode derivation (`DerivePresetSettings`) to compute `StructureLayers`,
   `MinStarBoundingBoxSize`, and `PixelSampleSize` *continuously* from the profile's pixel scale rather than
   from a 3-way enum, seeding the optimizer with a rig-aware starting point.
4. Leave category-B σ-unit gates as fixed defaults; leave category-C options to the Optimization Wizard and
   user preference.

## Cross-references

- `docs/star-detection-optimization-wizard-design.md` — the empirical search the heuristics would seed.
- `docs/star-detection-hfr-psf-analysis.md`, `docs/f11-meanflux-sensitivity-recalibration-design.md`,
  `docs/sigma-consistency-design.md` — why the gates are already σ-normalized (category B).
- `StarDetection/StarDetectionOptions.cs` (`DerivePresetSettings`) — the existing heuristic layer.
- `StarDetection/HocusFocusStarDetection.cs` (`BuildStarDetectorParams`) — where each option becomes a
  detector parameter.
