# Analysis: Star Detection, HFR, and PSF Fitting in HocusFocus

## Context

The HocusFocus plugin's auto-focus quality depends on three coupled measurements made on every analysis frame: (1) which pixels are stars, (2) the HFR of each star, and (3) optional PSF fits that produce FWHM / eccentricity / R². Any inaccuracy here propagates directly into focus position, V-curve fit, and aberration inspector tilt vectors.

This document is the result of a thorough static analysis of those three subsystems. It identifies concrete bugs (two confirmed by direct code reading), accuracy / robustness gaps, and a prioritized list of improvement opportunities. No code has been changed.

---

## 1. Star Detection Pipeline

**Entry point:** `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` (orchestration)
**Engine:** `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (`DetectImpl` at line 138)
**Options:** `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`

### 1.1 Pipeline summary

1. Hot-pixel filter (mono path: median-blur + threshold; CFA path: Bayer-aware in `HotpixelFiltering.cs`).
2. Optional source-image Gaussian blur (`NoiseReductionRadius`, default 3 → 7×7 kernel).
3. B-spline atrous wavelet decomposition, residual layer subtracted to suppress nebulae / gradients (`StructureLayers`, default 4).
4. Post-wavelet Gaussian blur, K-sigma binarization with threshold `median + NoiseClippingMultiplier × σ` (default 4σ).
5. Optional morphological dilation (`StructureDilationCount`, default 0).
6. Raster-scan connected-component growth (`ScanStars`, line 436) → bounding boxes.
7. `EvaluateStarCandidate` (line 541) filters by size, edge proximity, distortion, saturation, sensitivity, centeredness, peak/median ratio, HFR.
8. `MeasureStar` (line 389) computes HFR.
9. Optional PSF modelling (`StarDetector.cs` line 330).

### 1.2 Confirmed bugs

**Bug A — Even-length median is wrong (StarDetector.cs:747)**

```csharp
starMedian = (starPixels[starPixels.Length >> 1 + 1] + starPixels[starPixels.Length >> 1]) / 2.0;
```
In C#, additive `+` binds tighter than shift `>>`, so `Length >> 1 + 1` evaluates as `Length >> (1+1) = Length / 4`. For a 10-element array the code averages indices 2 and 5 instead of the two middle indices (4 and 5). The `StarMedian` then feeds the "too flat" rejection at line 597 (`starMedian >= PeakResponse * peak`), so over- or under-rejection of stars with non-uniform internal pixel distributions is silently happening today.

**Fix:**
```csharp
starMedian = (starPixels[(starPixels.Length >> 1) - 1] + starPixels[starPixels.Length >> 1]) / 2.0;
```

**Bug B — Histogram median tie-break loops on the wrong variable (CvImageUtility.cs:181)**

```csharp
} else if (currentCount == targetMedianCount) {
    for (uint j = i + 1; i <= ushort.MaxValue; ++i) {  // condition & increment use i, not j
        if (histogram[j] > 0) {
            median = (i + j) / 2.0d;
            break;
        }
    }
    break;
}
```
The inner loop never advances `j`, so `histogram[j]` is the same value every iteration. If that bin happens to be zero, the loop instead increments the *outer* `i` past `ushort.MaxValue` and exits, leaving `median = -1`. This branch only fires on exact ties (cumulative count equal to half the pixel count), which is rare on float images binned to 65536 levels — but when it does fire, the histogram median (used by background noise estimation) becomes `-1` and all downstream sigma estimates are corrupted for that frame.

**Fix:**
```csharp
for (uint j = i + 1; j <= ushort.MaxValue; ++j) {
    if (histogram[j] > 0) {
        median = (i + j) / 2.0d;
        break;
    }
}
```

### 1.3 Algorithmic concerns (no bug, but accuracy / robustness)

- **Border-touching rejection is binary** (line 559). Stars whose bounding box touches `x=0`, `y=0`, the right edge, or the bottom edge are dropped even if the star body is fully inside. For small FOVs or panel detectors this can disproportionately reject edge stars exactly where the inspector wants them for tilt assessment.
- **Saturation check is on `Background + Peak`** (line 578). A bright sky on a noisy night can push `Background + Peak` past 0.99 even when only a couple of central pixels are clipped, rejecting otherwise usable stars. A pixel-level saturated-count check (e.g., reject only if ≥ N central pixels are at full well) is more discriminating.
- **CFA hot-pixel filter neighborhood (HotpixelFiltering.cs:74-122)** falls back to `Median_3` / `Median_5` at image edges; the fallback samples a different Bayer phase than the interior path. For images with strong color casts this can leave residual chroma "hot pixels" along edges.
- **`ScanStars` loop bounds** (line 450): `for (var yTop = 0; yTop < yBottom; ...)` and `xLeft < xRight` mean the last row and last column are never used as a seed pixel. In practice the wavelet-blurred binary mask is rarely exactly 1 px wide at the edge, but a tiny star whose only above-threshold pixel sits in the last row will be missed.
- **`MinimumStarBoundingBoxSize = 5`** is a hard floor. On undersampled rigs (small focal length / large pixels) where FWHM ≈ 1.5 px, stars routinely have 3×3 or 4×4 bounding boxes after wavelet thresholding and are dropped. Worth exposing the default differently for "fast" rigs.

### 1.4 Test coverage gaps

The `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/` folder has tests for parameter conversion, PSF math, and options validation, but **no end-to-end test that feeds a synthetic image through `DetectImpl` and asserts star count / positions / HFR**. The repo already ships `SyntheticGaussianStarImage` — it is used by PSF tests but not by full-pipeline tests. Adding even a single noiseless-grid synthetic test would catch both bugs above today.

---

## 2. HFR Calculation

**File:** `StarDetector.cs`, `MeasureStar` (line 389-419).

### 2.1 What's implemented

```csharp
star.HFR = totalWeightedDistance / totalBrightness;  // line 415
```
with
```
totalWeightedDistance = Σ (pixel - background - k·σ) · r
totalBrightness       = Σ (pixel - background - k·σ)
```
This is the **flux-weighted mean radius**, identical to PHD2 / CCDInspector. It is *not* the strict half-flux radius (the radius enclosing 50% of total flux). The two diverge for non-Gaussian profiles (Moffat with low β has heavy wings, inflating the weighted mean) and for asymmetric stars.

### 2.2 Concrete issues

- **Aperture is the rectangular bounding box**, not a circular aperture (line 401-411). Corner pixels at `r ≈ √2 × box/2` are included; this biases HFR upward, more so for square boxes that just barely enclose round stars.
- **Sampling grid is `AnalysisSamplingSize`-spaced (default 1 px)** but pixel values are bilinearly interpolated (line 403). The interpolation gives sub-pixel value precision but the *grid* is still on integer steps — no actual super-sampling. For undersampled stars (FWHM ≲ 2 px), the integer grid under-resolves the core.
- **Hard threshold at `background + k·σ`** discards low-SNR wing pixels (default `k = 2`). For a Gaussian, ~5% of the flux sits beyond 2σ; clipping it deflates HFR for faint stars but not for bright ones, introducing a **brightness-dependent HFR bias**. This shows up as a systematic V-curve asymmetry near focus.
- **No iterative centroid refinement.** The centroid used at line 405-406 is the one computed in `ComputeStarParameters` (line 751), a single-pass `Σx·flux / Σflux` over threshold-clipped pixels. For asymmetric / tilted stars the centroid can be 0.3-0.5 px off, which directly enlarges HFR (the radius is measured from the wrong point).
- **No safeguard for zero / negative `totalBrightness`** other than the final guard at line 414. If background is over-estimated, all pixels can fall below threshold and the star reports `HFRAnalysisFailed`, but there is no diagnostic message — the star silently disappears from the analysis.
- **Saturation handling is "reject the star entirely"** (line 578). For tilt analysis on bright targets, this can leave the four corners of a frame with no measured stars. A profile-aware HFR (skip pixels above saturation, fit the wings only) would preserve information.

### 2.3 Test coverage gaps

No HFR test asserts HFR on a synthetic Gaussian / Moffat. The only HFR-touching tests are parameter-conversion tests in `HocusFocusStarDetectionTests.cs`. There is no regression guard against the bounding-box-vs-circular-aperture bias.

---

## 3. PSF Fitting

**Files:**
- `StarDetection/PSFModeler.cs` — fit driver (lines 145-335)
- `StarDetection/GaussianPSFType.cs` — Gaussian model
- `StarDetection/MoffatPSFType.cs` — Moffat (β fixed = 4.0)
- `StarDetection/PSFModel.cs` — derived quantities (FWHM, eccentricity)
- `Utility/AlglibAPI.cs` — Alglib wrapper

### 3.1 What's implemented

- Two models, both elliptical with rotation: Gaussian and Moffat(β=4). Moffat β is **hard-coded** (StarDetectionOptions.cs:119, PSFModeler.cs:376) — the option enum is `Moffat_40`, not a free β.
- Alglib Levenberg-Marquardt via `minlmcreatevj` (analytical Jacobian) or `minlmcreatev` (numerical, δ=1e-4). Max LM iterations capped at 20 (PSFModeler.cs:164).
- Optional IRLS outer loop (`UsePSFAbsoluteDeviation`) up to 10 outer iterations, with `weight = 1 / max(noiseSigma, |residual|)`.
- 7 fitted parameters: `A, B, x0, y0, σx, σy, θ`.
- R² gate at `PSFGoodnessOfFitThreshold` (default 0.9, StarDetectionOptions.cs:122). Below threshold → `metrics.PSFFitFailed++`, no PSF stored.
- FWHM formulas verified correct:
  - Gaussian: `FWHM = σ · 2√(2 ln 2) ≈ 2.355 σ`
  - Moffat(β): `FWHM = 2σ · √(2^(1/β) − 1)`, for β=4 ≈ 1.307 σ
- Eccentricity uses FWHM (PSFModel.cs:45-47): `ecc = √(1 − b²/a²)` ✓.

### 3.2 Concerns

**a. Initial guess is weak (PSFModeler.cs:155, 260).**
```csharp
initialGuess = { Max(0, centroidBrightness - background), background, 0.0, 0.0, width/3, height/3, 0.0 };
```
Sigma is seeded from the bounding box, not from second moments of the star pixels. For elongated stars the right answer is `σx, σy` proportional to the moment ellipse axes; seeding both at `box/3` puts LM far from the minimum and risks converging to the wrong local minimum on noisy stars. A proper raw-moment seed (`σ² = Σ pixel·(x−x̄)² / Σ pixel`) costs ~50 multiplications and would dramatically improve fit robustness.

**b. Offset bounds are too tight (PSFModeler.cs:156-157).**
```csharp
dxLimit = box.Width / 8.0;
dyLimit = box.Height / 8.0;
```
For a 10 px bounding box the optimizer can only shift the centroid ±1.25 px. If `ComputeStarParameters` produced a centroid that's 1.5 px off (asymmetric star, hot pixel pulling it), LM hits the bound and never relaxes. Better to let the optimizer move ±box/2 and let the prior on amplitude / sigma keep it in shape.

**c. Amplitude upper bound 2.0 (PSFModeler.cs:159).**
After normalization, pixel values are nominally [0, 1]; an amplitude up to 2.0 only matters if the input image has values > 1 (e.g., user-supplied float images). It is not catastrophic but it is arbitrary — `Max(1.0, observedMax)` would be a better safety value.

**d. Background bound is [0, 1].**
This silently fails on calibration frames with negative background after bias subtraction, or on offset-shifted floats. Allowing slightly negative background (e.g., −0.05) avoids LM clamping at a wall.

**e. Residual weighting is statistically wrong for Poisson data (PSFModeler.cs:81-97, 208-212).**
The default `FitResiduals` is **unweighted**. The IRLS variant uses `w = 1 / max(σ, |r|)` (an M-estimator-style robust weight), which suppresses outliers but is not a noise model — it does not match the shot-noise variance `var(pixel) = pixel + readNoise²`. For dim stars this means the wings dominate the fit (lots of low-signal pixels with zero weight contribution but heavy noise), biasing σ upward. Proper inverse-variance weighting (`w = 1 / (pixel + readNoise²)`) would be more accurate; even a constant `w = 1 / pixel` is an improvement.

**f. R² is not noise-normalized.**
`R² = 1 − rss/tss` (PSFModeler.cs:142). For a faint star, `tss` is small (low contrast), so even a structurally-correct fit can score R² < 0.9 and be discarded. Conversely, a bright saturated star with a flat top scores R² > 0.99 even though the model is obviously wrong. A reduced χ² (with a noise model) or a residual-peak-to-amplitude ratio would be a more robust gate.

**g. θ ambiguity is handled by post-hoc swap (PSFModeler.cs:411-431).**
After solving, if `σy > σx` the code swaps them and adjusts θ by ±π/2. For near-circular stars `σx ≈ σy` and the swap toggles between solutions across frames — this introduces noise in the reported position angle of the tilt vector. A better fix: re-parameterize as `(σmaj, σmin, θ)` with `σmaj ≥ σmin` enforced as a constraint, or with `σmin = σmaj · (1 − e)` for `e ∈ [0, 1)`.

**h. Saturated / clipped pixels poison the fit.**
The star is rejected entirely if `Background + Peak ≥ 0.99`, but partially-saturated stars (a couple of clipped pixels in the core) pass through with no special handling. LM fits the flat top, sigma inflates, FWHM is wrong. A robust loss (Huber/Tukey) or per-pixel masking of saturated pixels would fix this without losing the star.

**i. Pixel sampling is point sampling, not pixel integration.**
`Value(parameters, input)` (GaussianPSFType.cs:55, MoffatPSFType.cs:58) evaluates the model at the pixel center, not as `∫∫ model(x,y) dx dy` over the pixel area. For well-sampled stars (FWHM ≥ 3 px) the error is < 1%; for FWHM ≈ 1.5 px it can be 5-10% in σ. Critical-sampling rigs are affected.

### 3.3 Test coverage gaps

`PSFModelerTests.cs` and `MoffatPSFTypeTests.cs` exercise **noiseless** synthetic stars with `θ = 0` and `σx = σy`. Missing scenarios:
- Elongated stars (`σx ≠ σy`)
- Rotated stars (`θ ≠ 0`)
- Stars with realistic noise (Poisson + Gaussian read noise)
- IRLS mode
- Partially-saturated stars
- Stars where `ComputeStarParameters` produced a slightly off centroid

---

## 4. Cross-Cutting Observations

- **Two centroids in play.** HFR uses the centroid from `ComputeStarParameters` (single-pass, threshold-clipped). PSF fitting also uses that centroid as its seed and only allows ±box/8 refinement. If the seed is wrong, both metrics inherit the error and no diagnostic flags this. Adding a single iterative centroid refinement (e.g., 2-3 passes of `Σ x·flux / Σ flux` with a circular mask centered on the previous estimate) would improve both HFR and PSF fits.
- **Background is estimated three times.** (1) Median of expansion-box annulus around the star (StarDetector.cs:696), (2) Per-pixel noise threshold inside MeasureStar (line 400), (3) Fitted again by PSF model as parameter B. These are not cross-checked. When they disagree by > 2σ that is a strong signal the star is contaminated (neighbor, gradient, hot column) and should be flagged.
- **Bayer / mono path divergence.** Star detection is documented for mono in CLAUDE.md but the CFA hot-pixel path uses Bayer-aware filtering. There is no test that exercises a CFA image, so any regression in the CFA branch would be silent.
- **No outlier diagnostics returned to the user.** Counts of rejected stars per reason (saturated / too-flat / centeredness / HFR-failed / PSF-failed) are tracked in `StarDetectorMetrics` but not surfaced in the auto-focus / inspector UI in a debuggable form. Adding a "rejection breakdown" panel would dramatically shorten user-side parameter tuning.

---

## 5. Prioritized Improvement List

**Tier 1 — confirmed bugs, fix immediately**
1. Fix even-length median operator precedence in `StarDetector.cs:747`. Add a unit test that constructs a star with an even pixel count and asserts the right `StarMedian`.
2. Fix the inner-loop variable in `CvImageUtility.cs:181`. Add a unit test with a synthetic histogram exhibiting the exact tie-break condition.

**Tier 2 — accuracy wins, low risk**
3. Add an iterative centroid refinement (2-3 passes, circular mask) in `ComputeStarParameters`. Reuse the result for both `MeasureStar` and as the PSF fit seed.
4. Switch `MeasureStar`'s rectangular aperture to a circular one, integrating partial-pixel contributions for the boundary. This removes a known HFR positive bias.
5. Seed PSF sigmas from raw second moments instead of `box/3`. Even noiseless, this halves LM iteration count.
6. Loosen PSF offset bounds (`dxLimit`, `dyLimit`) to `box/2` so the optimizer can recover from a bad seed.

**Tier 3 — robustness, moderate risk**
7. Replace IRLS weight `1/max(σ, |r|)` with a Huber-loss IRLS (Huber threshold = `1.5 σ`). Robust to hot pixels and partial saturation without the ad-hoc behavior of the current scheme.
8. Mask (don't reject) partially-saturated pixels and refit. Lets the inspector use bright corner stars on tilt analysis.
9. Replace R² gate with reduced χ² gate using `noiseSigma`. R² alone is brightness-biased.
10. Re-parameterize PSF as `(σmaj, σmin, θ)` with `σmaj ≥ σmin` to eliminate the post-hoc swap discontinuity.

**Tier 4 — diagnostics / observability**
11. Surface rejection counts by reason in a debug overlay / log line per detection run.
12. Cross-check the three background estimates and flag disagreement > 2σ as `StarContaminationSuspected`.

**Tier 5 — feature, opt-in**
13. Optional pixel-area integration (vs. point sampling) for PSF fits on undersampled rigs. Behind a `PSFPixelIntegration` option.
14. Make Moffat β fittable (or at least selectable from a small set: 1.5, 2.5, 4.0).

---

## 6. Critical Files

| Path | Reason |
| --- | --- |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` | Median bug, HFR algorithm, aperture shape, centroid |
| `Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs` | Histogram median loop variable bug |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/PSFModeler.cs` | LM driver, bounds, IRLS weighting, θ swap |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/GaussianPSFType.cs` | Gaussian model + Jacobian |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/MoffatPSFType.cs` | Moffat model + Jacobian, β hardcode |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` | Option defaults and ranges |
| `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/*.cs` | Tests to extend; existing `SyntheticGaussianStarImage` is reusable |

## 7. Verification Plan (for future fix work)

- For each Tier 1/2 fix: add a focused unit test in `Joko.NINA.Plugins.HocusFocus.Tests` that reproduces the bug or quantifies the bias *before* the fix, then asserts the corrected value after.
- Add one full-pipeline integration test using `SyntheticGaussianStarImage` with a known star grid (positions, FWHMs, eccentricities). Assert star count, centroid error < 0.1 px, HFR within 2% of analytic value, PSF FWHM within 3% of injected.
- Run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` and require all existing tests still pass.
- After Tier 2 changes, run a known-good real-data auto-focus log through the analysis (the user has historical AF runs in NINA's storage) and compare HFR distributions and V-curve fits before/after.
