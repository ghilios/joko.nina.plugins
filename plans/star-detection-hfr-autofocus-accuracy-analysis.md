# Star Detection, HFR & AutoFocus Curve Fitting — Accuracy Analysis

## Context

Star detection is the bedrock of HocusFocus: auto-focus, aberration inspection, and sensor/tilt
modeling all consume its outputs. This analysis assesses (1) the star detection algorithm —
including faint-star sensitivity and robustness to defocused (donut) stars, which AF depends on at
sweep extremes; (2) how HFR is calculated; (3) how per-star values flow into the AF routine and
curve fitting. Every claim below was verified against the code (file:line cited), and the ten
highest-risk quantitative claims were independently re-verified by an adversarial review pass.

**Deliverable**: this report. No code changes were made — recommendations are listed for follow-up
work to pick from.

---

## Executive summary

The architecture is genuinely strong — robust statistics nearly everywhere (median/MAD aggregation,
MAD-based Grubbs outlier rejection, Huber IRLS in both PSF and curve fits), a gradient-robust local
background plane feeding every measurement, four hyperbolic models with principled Hybrid selection,
and layered validation (R²/χ² gates, bracket check, HFR-improvement check). The weaknesses cluster
in exactly the two areas of concern:

1. **Out-of-focus stars are systematically disadvantaged** by four independent mechanisms: the
   wavelet structure cap, the peak-based sensitivity gate, the TooFlat gate, and the WideRange
   preset that raises (not lowers) the detection bar — its comment says the opposite of what the
   code does.
2. **HFR carries two opposing, defocus-dependent biases** (soft-threshold subtraction pulls it
   down; noise inclusion through a 4–5× understated σ pushes it up), which individually distort
   the V-curve's wings even though run-to-run consistency is preserved.
3. **The curve-fitting weight chain has degenerate paths** (0.001 σ floor → 1000× weight; RMS
   instead of SEM pooling; scatter-vs-precision semantics) that can let one bad point steer a
   weighted fit, and TRENDHYPERBOLIC dilutes the sophisticated hyperbolic models 50/50 with a
   plain trendline intersection.

None of these break typical near-focus operation — which is why AF works well day to day — but
they bound accuracy at sweep extremes and in sparse/faint fields.

---

## 1. How detection works (verified pipeline)

`StarDetector.DetectImpl` (StarDetector.cs:150):

1. Optional CFA-aware hotpixel filter (3×3 median, threshold-gated; default ON, threshold 0.001).
2. Source image for **measurement** stays sharp by default (`StarMeasurementNoiseReductionEnabled`
   default false); a **copy** is Gaussian-smoothed (effective default radius 4 → kernel 9, because
   `ConfigureSimpleSettings` adds +1 when hotpixel thresholding is on, StarDetectionOptions.cs:107-110).
3. Noise σ via `KappaSigmaNoiseEstimate` **on the smoothed copy** (CvImageUtility.cs:503-543;
   κ=3, ≤5 iterations, first iteration unmasked).
4. Structure map: à-trous B3-spline wavelet residual (4 layers default; largest kernel
   2^(layers+1)+1 = 33 px) subtracted to suppress large structures (nebulae) → Gaussian blur
   (kernel 2·layers+1) → binarize at `median + 4.0·σ_smoothed` → optional dilation (default 0).
5. Row-scan connected components → per-candidate gates, in order: TooSmall (<5 px box) → OnBorder
   → TooDistorted (fill factor < 0.5) → degenerate → Sensitivity (`NB/σ > 10`, where
   `NB = peak − (1−PeakResponse)·meanFlux`) → off-center (0.3 tolerance) → **TooFlat**
   (`median ≥ 0.75·peak`) → HFR failure → MinHFR (1.5 px) → contamination (octant residual test
   on a robust IRLS background plane — flag + reject by default).
6. Per-star: robust local background **plane** (Huber IRLS) used for clipping, flux, iterative
   3-pass aperture centroid, HFR, and PSF — excellent design; gradients don't bias measurements.
7. PSF fitting (Moffat β=4 default, alglib LM, optional Huber IRLS, R² ≥ 0.9 gate, saturated
   pixels masked, ≥10 unsaturated pixels required) — **disabled during AF**
   (HocusFocusStarDetection.cs:324-327), so AF always runs on HFR, never FWHM.

**Strengths worth keeping**: background-plane subtraction everywhere; contamination test with 50%-
breakdown MAD scaling; saturated stars no longer hard-rejected (masked in PSF); iterative aperture
centroid (pinned by tests); deterministic ordering; rich rejection metrics; TestApp headless
diagnostics.

---

## 2. HFR calculation (`MeasureStar`, StarDetector.cs:609-653)

```
HFR = Σ w·v·d / Σ w·v,   v = bilinear(x,y) − plane(x,y) − τ,   τ = 2.0·σ_smoothed
```
over a circular aperture R = min(bboxW,bboxH)/2 with a 0.5-px linear edge ramp `w`, sampled on an
`AnalysisSamplingSize` grid (default 1.0) aligned through the centroid, bilinear-interpolated.

Findings:

- **It is the flux-weighted mean radius, not a true half-flux radius.** Same convention as NINA
  core, so cross-tool comparability holds; monotonic in star width, so fine for AF. Worth
  documenting, not changing.
- **F3 — soft-threshold bias (confirmed).** τ is *subtracted from* each pixel's flux, not used as
  a mere inclusion gate. For surviving pixels (f₁−τ)/(f₂−τ) > f₁/f₂ when f₁>f₂, so wing pixels
  lose relative weight → HFR biased low, increasingly as peak/τ shrinks — i.e. for faint stars
  and at defocus. Inconsistency: `ComputeIterativeCentroid` (StarDetector.cs:968-971) uses the
  same margin as a **gate only** and weights by `pixel − background`. The two conventions should
  be reconciled deliberately.
- **F4 — σ mismatch (confirmed).** τ and the sensitivity gate use σ measured on the smoothed copy
  while sampling the sharp image. A 9-px Gaussian kernel cuts white-noise σ ~4-5×, so effective
  clip τ ≈ 0.4-0.5·σ_sharp and "10σ" sensitivity ≈ 2.5σ_sharp. Consequences: positive-only noise
  pixels enter the flux sum at large radii (HFR inflated for faint stars — opposing F3), and the
  nominal meaning of both knobs silently depends on the noise-reduction settings. When
  `StarMeasurementNoiseReductionEnabled` is on, the mismatch disappears but HFR is inflated by the
  blur itself (consistent within a run; absolute values shift).
- **F14 — saturated stars are not masked in HFR** (only in PSF, which is off during AF). A
  saturated flat top under-weights the core → HFR overestimated near focus, where saturation is
  most likely. Median aggregation limits the damage unless many stars saturate.
- Net curve-shape effect: F3 flattens the V-curve wings, F4 lifts faint-star HFR; both are
  defocus-dependent with opposite signs. Run-to-run consistency is preserved (same biases each
  run), so AF repeatability is unharmed — but the fitted minimum can shift when the star
  population changes across the sweep (faint stars dropping out at defocus). Empirical
  quantification needs a defocus dataset (see Recommendations).

---

## 3. Faint-star sensitivity

- Detection is driven by the structure map (binarize at median + 4σ_smoothed on the
  wavelet-filtered, blurred map) — coherent and effective for faint sharp stars; the smoothing
  legitimately raises faint-star SNR before thresholding.
- The per-candidate Sensitivity gate `NB/σ_smoothed > 10` is **peak-based**, and due to F4 is
  effectively ~2.5σ_sharp — permissive; false positives are controlled by the shape gates and
  MinHFR instead. Reasonable, but the knob's meaning is not what the UI number implies.
- **F11 — `meanFlux = totalFlux / starPoints.Count`** (StarDetector.cs:1172): numerator sums only
  clip-survivors, denominator counts all structure pixels → NB slightly inflated for faint stars
  (loosens the gate inconsistently).
- MinHFR=1.5 px and the hotpixel filter give good hot-pixel immunity; contamination rejection
  (default ON) keeps blended/contaminated stars out of the HFR statistics — good for AF integrity.

## 4. Out-of-focus (donut) robustness — the weakest area

Four mechanisms compound against heavily defocused stars:

- **F2 — wavelet cap (confirmed).** Structures at/above the residual scale (~16-32 px for 4
  layers; largest kernel 33 px, cumulative support 61 px) are strongly attenuated before
  binarization. Big donuts fade out of the structure map. The post-wavelet blur and dilation
  smooth what survives but cannot restore subtracted amplitude.
- **WideRange preset contradiction (confirmed).** `Simple_FocusRange=WideRange` raises
  StructureLayers 4→5 (good) **and raises** BrightnessSensitivity 10→12 — the comment says "we
  want to be more sensitive" but a higher threshold is *less* sensitive
  (StarDetectionOptions.cs:89-93). For the one preset aimed at defocus, the second change works
  against the first.
- **F1 — TooFlat gate (confirmed).** Reject when `median ≥ 0.75·peak` over clip-surviving pixels
  (StarDetector.cs:831). A defocused annulus/plateau has median/peak → 0.8-1.0 once amplitude
  ≳ 7-10σ — i.e. precisely bright defocused stars; noise on the peak statistic is what lets
  marginal ones through. No AF-specific override exists (GetStarDetectorParams only flips
  ModelPSF/save-path).
- **Peak-based sensitivity** (§3) penalizes defocused stars whose flux is spread thin.

Also relevant: `MaxDistortion=0.5` rejects surviving *un-filled* rings with hole radius >~60% of
the outer radius (fill factor < 0.5), though wavelet+blur usually fills small holes. The AF engine
inherits all of this unchanged — there is no defocus-aware parameter adaptation along the sweep,
even though the engine knows exactly how far from focus each exposure is.

## 5. Aggregation: stars → one AF measurement

(HocusFocusStarDetection.cs:334-433)

- Default `MeasurementAverage=Median`: **AverageHFR = median** of per-star HFRs, **HFRStdDev =
  1.483·MAD** (σ-consistent). `MeanOutliers` alternative: median±3/4·MAD pre-filter then mean +
  sample stddev. Robust, good defaults.
- PSF stats (FWHM/eccentricity medians+MADs) are computed only when PSF is on — i.e. not during AF.
- **F8 — "brightest N AF stars" score units mismatch (confirmed).**
  `OrderByDescending(s => s.HFR*0.3 + s.MeanBrightness*0.7)` (line 404): HFR is in pixels (≥1.5 by
  the MinHFR gate), MeanBrightness is normalized [0,1] flux/pixel — the HFR term dominates, so the
  selection prefers the *largest-HFR* objects (extended objects, blends), and it runs on the first
  AF exposure, which is taken at maximum defocus. Subsequent frames then position-match to those
  picks with **no distance cap and possible duplicates** (line 409, Aggregate over min distance).
  Only affects users with NINA's "use brightest N stars" > 0.
- **F9 — ROI offset drops fields (confirmed).** `Star.AddOffset` (CvImageUtility.cs:561-570)
  copies only Center/BBox/Background/MeanBrightness/HFR/PSF — `PeakBrightness`,
  `BackgroundPlane`, `StarContaminationSuspected` are silently zeroed for every ROI detection
  (the common AF inner-crop path): MaxBrightness=0 in results, contamination flags wiped.

## 6. AF routine & curve fitting

(AutoFocusEngine.cs; AlglibHyperbolicFitting.cs)

Flow (verified): initial HFR at start position (FramesPerPoint frames, mean of sub-measurements) →
overshoot out by (offsetSteps+1)·stepSize, walk back in measuring offsetSteps points → extend left
or right until the **NINA trendline fit** has ≥offsetSteps points on one side and ≥1 on the other →
fit curves on every point completion → validate → move → re-measure → require
`finalHFR ≤ initialHFR·(1+0.15)`. Failure paths: ≥offsetSteps zero-measure points →
`TooManyFailedMeasurementsException`; up to TotalNumberOfAttempts retries. Backlash is delegated to
NINA's focuser mediator.

Per-point: `MeasureAndError{ Measure=AverageHFR, Stdev=HFRStdDev }` → multi-frame
`AverageMeasurement` → `ScatterErrorPoint(pos, hfr, 0, max(0.001, σ))`; weighted fits use
`w = 1/max(|ErrorY|, 1e-6)` (default `WeightedHyperbolicFitEnabled=true`).

Fitting machinery (strong): four models — Symmetric (4p), UnevenBlend (5p, C⁰ ramp, deprecated),
**TiltedHyperbola** (5p, smooth skew, live default), SmoothBlend (5p, logistic blend) — all alglib
LM with bounds, data-driven seeds, optional Huber IRLS (δ=1.5·MAD, ≤10 iters), weighted R²,
weighted χ², JᵀWJ delta-method `MinimumStdError` (with span-based degeneracy suppression), LOO
stability diagnostic. **Hybrid** fits all four with *per-model* Grubbs rejection, requires a finite
in-range minimum, ranks by σ(focus) → LOO → χ²_red → R². Grubbs uses median/MAD residuals with
weight-matched scaling — well done.

Findings:

- **F5 — weight floor (confirmed).** A point with MAD=0 (2 stars with near-identical HFR, or the
  F5b invalid-σ path) gets ErrorY=0.001 → weight 1000 vs typical 2-20 → it dominates the weighted
  fit ~(50-500)× per unit residual; Huber IRLS and Grubbs won't catch it because LM pins the curve
  to it (its residual is minimized by construction). No relative-weight cap exists.
  **F5b**: `AverageMeasurement` (CvImageUtility.cs:607-637) — once one frame has σ≤0/NaN,
  accumulation stops for all later frames but the divisor still counts them → underestimated σ;
  the invalid flag is never surfaced.
- **F6 — σ semantics (confirmed).** ErrorY is the star-ensemble scatter (field tilt/curvature/
  seeing spread), not the median's precision (≈1.2533·σ/√N*); multi-frame pooling is RMS, not SEM,
  so FramesPerPoint does not reduce reported σ. Relative weighting across the sweep is still
  sensible (scatter grows with defocus), and `MinimumStdError` is **immune** to uniform σ scaling
  (s²-rescaled covariance — verified). Casualty: `ReducedChiSquared` runs ≪1 when many stars are
  detected, so the χ²>5 rejection gate (when selected) almost never trips; default criterion is
  R², so most users are unaffected.
- **F7 — TRENDHYPERBOLIC averaging (confirmed).** Final position = round((trendline intersection +
  hyperbolic minimum)/2) (AutoFocusEngine.cs:351-357). The Hybrid/asymmetric machinery determines
  only half the answer; a biased trendline intersection (asymmetric curve, asymmetric sampling)
  shifts the result by half its bias regardless of hyperbolic quality. Only the bracket check
  bounds it. (Whether TRENDHYPERBOLIC is the active default is NINA-profile data, outside this
  repo.)
- **F12 — inconsistent outlier weighting**: parabolic Grubbs passes no weights
  (AutoFocusEngine.cs:123) while hyperbolic does (line 132).
- Sensor-model (inspection) per-star curves: per-star σ_HFR = HFR/max(SNR,1) proxy, per-star
  R²≥0.90 gate (named constant), paraboloid surface fit — reasonable; inspection-only, not in the
  AF position path.

## 7. Test coverage gaps (Tests project)

PSF parameter recovery, eccentricity/FWHM formulas, iterative centroid, and the fitting models are
well pinned. **Not tested**: the HFR formula itself (no synthetic-star MeasureStar test), donut
/defocused-star detection (no annular synthetic generator), contamination octant math, saturation
masking, pixel-integration options.

---

## 8. Ranked findings

| # | Severity | Finding | Where |
|---|----------|---------|-------|
| F1 | High | TooFlat gate (median ≥ 0.75·peak) rejects bright defocused/flat-top stars; no AF override | StarDetector.cs:831 |
| F2 | High | Wavelet residual subtraction attenuates large donuts; WideRange raises the sensitivity bar too (comment contradicts code) | StarDetector.cs:241; StarDetectionOptions.cs:89-93 |
| F3 | High | HFR soft-threshold subtraction biases HFR low, defocus/faintness-dependent; inconsistent with centroid's gate-only convention | StarDetector.cs:638 |
| F4 | High | σ-based thresholds on the sharp image use smoothed-image σ (~4-5× small) by default | StarDetector.cs:225-233,301 |
| F5 | Medium | 0.001 ErrorY floor → 1000× fit weight; invalid-σ accumulation bug underestimates pooled σ | AutoFocusEngine.cs:695; CvImageUtility.cs:607-637 |
| F6 | Medium | Per-point σ = ensemble scatter, RMS-pooled (not SEM); χ²_red ≪ 1 → χ² gate toothless when enabled | AutoFocusEngine.cs:592,712; AlglibHyperbolicFitting.cs:561-572 |
| F7 | Medium | TRENDHYPERBOLIC: 50/50 average with plain trendline intersection halves the asymmetric models' influence | AutoFocusEngine.cs:351-357 |
| F8 | Medium | AF star selection score mixes pixels with [0,1] flux → largest-HFR wins; unbounded/duplicate position matching | HocusFocusStarDetection.cs:404,409 |
| F9 | Medium | ROI AddOffset drops PeakBrightness/BackgroundPlane/contamination flag on AF-crop runs | CvImageUtility.cs:561-570 |
| F10 | Low | ResetDefaults: StarPeakResponse 0.6 vs 0.75; writes simple_FocusRange backing field (no persist/notify) | StarDetectionOptions.cs:191,203 |
| F11 | Low | NormalizedBrightness meanFlux numerator/denominator mismatch | StarDetector.cs:1172,1217 |
| F12 | Low | Parabolic Grubbs unweighted vs hyperbolic weighted | AutoFocusEngine.cs:123,132 |
| F13 | Low | KappaSigma first iteration unmasked; 5-iteration cap | CvImageUtility.cs:516-521 |
| F14 | Low | Saturated pixels unmasked in HFR (and PSF is off during AF) | StarDetector.cs:609-653 |

## 9. Recommended follow-ups (prioritized)

1. **Build the evidence base first**: extend TestApp with a `focus-sweep` diagnostic (run detection
   over a saved AF image sequence; report star count, HFR, rejection-reason histogram vs focuser
   position) + add a synthetic annular/defocused star generator and unit tests pinning `MeasureStar`
   (none exist today). This converts F1-F4 from "mechanism confirmed" to measured magnitudes before
   touching production behavior.
2. **Defocus robustness** (F1/F2): relax or context-gate TooFlat for AF (e.g. via the existing
   isAutoFocus override point in GetStarDetectorParams), fix the WideRange sensitivity direction,
   consider deriving StructureLayers from expected max HFR (the engine knows sweep geometry).
3. **σ consistency** (F4): scale σ for the measurement image (analytic factor for a Gaussian
   kernel) or estimate σ on the image actually measured; then revisit τ semantics (F3) — gate-only
   like the centroid, or document the subtraction as intentional smoothing of the V-curve.
4. **Weight-chain hygiene** (F5/F6): floor ErrorY at a fraction of the median ErrorY (or cap
   relative weights), fix the invalidStdDev accumulation, divide pooled σ by √FramesPerPoint, and
   recalibrate or document the χ² gate.
5. **F7-F14** as small independent fixes (one-liners to small patches each).

---

**Verification of this analysis**: all file:line citations were read directly; the ten highest-risk
quantitative claims were independently re-verified by an adversarial reviewer against the code (all
confirmed; two refinements incorporated: effective default noise kernel is 9 px, and NINA-profile
defaults are outside this repo's purview).

---

## 10. Implementation progress

Tracks the Section 9 follow-ups. Each step is its own branch + PR.

| Step | Status | Notes |
|---|---|---|
| 1. Evidence base | ✅ Done (PR #46, merged) | Synthetic disk/annulus generator + ground truth, `MeasureStar` bias tests (F3/F4 magnitudes measured), `TestApp focus-sweep` diagnostic. Plans: `focus-sweep-evidence-base-{design,plan}.md`. |
| 2. Defocus robustness (F1/F2) | ✅ Done (PR #47, merged) | WideRange sensitivity direction fixed (F2 — 10→8); TooFlat intentionally kept active during AF, documented (F1); LongFocalLength preset also made more sensitive (tuning). StructureLayers-from-HFR deferred. Plan: `defocus-robustness-plan.md`. |
| 3. σ consistency (F4, then F3) | ✅ Done (PR #48, merged) | Honest measured-image σ for all measurement-side thresholds; per-preset knob recalibration preserves effective behavior; τ semantics decided empirically (gate-only @2.0σ) and MinHFR floor recalibrated 1.5→1.2 for honest faint-star HFRs (see `sigma-consistency-f3-results.md`). Plans: `sigma-consistency-{design,plan}.md`. |
| 4. Weight-chain hygiene (F5/F6) | ✅ Done (PR #49, merged) | ErrorY regularization at all five fit entry points (0.2·median floor, protecting NINA-core 1/σ² fitters and the saved-chart reload); fabricated 0.001 floors removed at all three construction sites; invalid-σ accumulation fix + SEM pooling, with the pooled measurement now forwarded to charts/broadcast/reports; χ² gate kept at 5.0 per calibration (see `weight-chain-hygiene-chi2-results.md`). New findings for step 5 recorded in the design doc (IRLS self-masking, SolveHuberIrls failed-solve bug, SensorModel sibling hazard). Plans: `weight-chain-hygiene-{design,plan}.md`. |
| 5. Small fixes (F7–F14) | ⬜ Not started | Independent one-liner-to-small patches. |

**Measured F3/F4 magnitudes (from step 1's tests, for steps 2–4 to target):** F3 soft-threshold biases HFR
*down* on a Gaussian by −0.47px (τ=0.02) to −1.90px (τ=0.20, ≈30% of true), with relative bias rising
13%→45% as τ/peak goes 0.05→0.40; the uniform-disk control shifts only −0.12px (confirming the bias needs a
radial gradient). F4 understated-σ inflated HFR by up to +16.9px in a worst-case stress test (tiny star, large
aperture, σ understated 5×). The estimator is exact on clean shapes (<0.01px error).
