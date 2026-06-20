# Brightness-Independent Donut Size for HFR Consistency & Sensor Modeling — Design

**Date:** 2026-06-19
**Status:** Design (approved direction; production path A + complement C, gated to defocus-aware runs;
B implemented in `TestApp` as an offline validation oracle for A+C)
**Author:** George Hilios (with Claude)

---

## 1. Problem

Far from focus, stars become **donuts** (annuli, because of the telescope's central
obstruction). In a single heavily-defocused frame the geometric donut size is
**brightness-independent optics** — the defocus blur diameter is `B = Δ·D/f` (Δ = longitudinal
defocus, D = aperture, f = focal length), and with central-obstruction ratio `ε` the annulus
inner/outer radius ratio ≈ `ε`. None of these contain a brightness term.

Yet HocusFocus measures **brighter donuts as larger**. This inflates the within-frame spread of
HFR, which corrupts:

- **(a) single-frame aggregation** — per-frame `AverageHFR`/`HFRStdDev`, the corner/region HFR
  stats (commit `fd375f1`), and the per-step region averages that feed AF curves; and
- **(b) the sensor/tilt model** — per-star hyperbolic focus curves (`SensorModel.FitImages`) and
  the corner best-focus tilt plane (`TiltModel`), because the brightness-driven, defocus-dependent
  inflation asymmetrically biases each star's curve and the region averages.

### 1.1 Empirical measurement (real data)

Measured on `D:\Autofocus Bank\mufti\AutoFocus_20260607_030642-…-3-001\attempt01\01_Frame00_…_Focuser2925.fits`
(the most-defocused frame, 96 detected donuts, via `TestApp contamination --defocus-distortion
--defocus-centering`):

| Quantity | Value |
|---|---|
| HFR (single frame) | min 7.15, **median 14.0**, max 20.7 px; **CV 18.6 %** |
| Peak-brightness dynamic range | **155× (2.19 dex)** |
| HFR vs **log₁₀(peak)** | slope ≈ **3.12 px/dex**, r ≈ 0.49, r² ≈ 0.24 |
| HFR vs **log₁₀(mean)** | slope ≈ **3.77 px/dex**, r ≈ 0.57, r² ≈ 0.32 |
| HFR vs **linear** brightness | r² ≈ 0.06 (log fits far better → not brighter-fatter) |
| Footprint (bbox width) vs log₁₀(peak) | slope ≈ **8.4 px/dex**, r² ≈ 0.28 |
| Brightness vs position | r(logPeak,X) = −0.15, r(logPeak,Y) = −0.01 → **~orthogonal to tilt** |
| HFR spatial gradient (real signal) | r(HFR,X) = +0.37, r(HFR,Y) = +0.59 |
| Tilt fit: `HFR ~ Xn+Yn` → add log(peak) | R² 0.40 → 0.69 (+29 pts); RMSE **1.97 → 1.41 px (−28 %)** |

## 2. Root cause (quantitatively confirmed)

Two compounding measurement artifacts, **not physics**:

1. **The metric.** HocusFocus's HFR is a **flux-weighted *mean* radius**
   `HFR = Σ(wᵢ·fluxᵢ·rᵢ) / Σ(wᵢ·fluxᵢ)`, which **over-weights outer pixels**. For a uniform disk
   it returns `(2/3)R`; the *true* 50 %-enclosed-flux radius is `R/√2 ≈ 0.707R`. The original
   Weber & Brady (2001) HFD was defined as the **true integral-crossing** (50 %-enclosed) radius —
   **the Σ flux-weighted-mean shipped everywhere is a later biased approximation.**

2. **The aperture.** The integration footprint = (flood-filled bbox)/2, and the bbox uses an
   **absolute** detection threshold. A brighter star's seeing-blurred outer ring crosses that
   threshold farther out, so the aperture itself grows with brightness (8.4 px/dex), dragging the
   flux-weighted-mean radius outward.

**Mechanism cross-check (reviewer, validated against the data).** A soft-edge × absolute-threshold
model gives edge radius `r_edge = R₀ + s·√(2 ln(A/T))`, so
`d r_edge / d log₁₀A = s·ln10 / √(2 ln(A/T))`. With a single blurred-edge width `s ≈ 4–5 px` and
`A/T ≈ 30–100`, this predicts **footprint slope ≈ 7–9 px/dex** *and* **HFR slope ≈ 3–4 px/dex**
simultaneously (observed 8.4 and 3.1). The HFR-slope/edge-slope ratio 0.74 ∈ (0,1) is exactly the
"flux-weighted mean dragged by a moving thresholded edge" prediction. This rules out the
brighter-fatter sensor effect (which is ~1–3 % and ~linear, inconsistent with the strong log signal).

## 3. Goal & success criteria

A **brightness-independent per-star size metric** for donuts (continuous through to in-focus) so that
single-frame aggregates and the sensor model are consistent.

Success (measured on the mufti 2925 frame, then bank-wide):

- Residual size–brightness slope **< 0.3 px/dex** and below the per-frame noise floor (was 3.1).
- In the tilt fit, the log-brightness term's contribution (+29 R² pts) **drops to insignificance**
  while the spatial signal (r=0.37/0.59) is **preserved**.
- Single-frame CV of the new metric materially below 18.6 %.
- **Gated to donut scenarios (bit-identical otherwise):** the normalization is active **only when
  defocus-aware donut detection is engaged** (the existing `DefocusAwareDonutDetection` /
  `DefocusAwareGates` master toggle — i.e. runs where donuts are expected). With that toggle off, the
  size metric and every downstream aggregate are **bit-identical to today's legacy HFR**, so in-focus
  AF (`MinHFR`, V-curve scaling, historical HFD comparability) is untouched. See §8.1.

## 4. Approach A (primary) — true encircled radius `R_e` with robust total-flux normalization

Replace/augment the flux-weighted-mean HFR with the radius enclosing a fixed fraction of the star's
**own total flux** (curve-of-growth / SExtractor `FLUX_RADIUS` family). Invariance comes from the
fraction scaling with each star's total flux (invariant under image × constant).

**Algorithm (per star):**

1. **Center.** For deep donuts use a **flux-symmetry / ring-fit center, *not* the intensity-weighted
   centroid** — a hollow annulus has an unstable intensity centroid (the middle is a hole). *(P0,
   reviewer; not in the original sketch.)*
2. **Curve of growth.** Cumulative background-subtracted flux vs radius from that center, using the
   existing fitted **local background plane** (`b0+b1·dx+b2·dy`, robust IRLS) for per-pixel
   subtraction.
3. **Bounded total flux.** Integrate out to a **brightness-independent, tightly capped** outer
   radius: the smaller of (i) CoG convergence (incremental annular flux ≈ noise), (ii) a multiple of
   the geometric defocus radius `Δ·D/f` (Δ from focuser position), and (iii) just inside the nearest
   neighbor. The **cap is the single biggest lever** on residual bias (leverage ∝ area — see §6).
4. **Radius.** `R_e` = radius where cumulative flux = `frac × total`, by interpolation/bisection
   between radial bins. **Use `frac = 0.3–0.5`, *not* 0.5–0.8** — outer fractions sit in the noisy
   wing where convergence/background errors concentrate (R80 slope 0.30 vs R50 0.14 vs R30 0.08
   px/dex in simulation). *(P0, reviewer.)*
5. **Uncertainty.** Propagate background-plane σ into a **per-star `R_e` error bar**; the sensor/tilt
   fit (already weighted by 1/σ²) should **down-weight large-area, high-σ_bg donuts**. *(P0,
   reviewer.)*
6. **Saturation/BFE guard.** Flag/exclude stars with peak above a saturation fraction, verified
   against the **actual full-well in ADU, not normalized display units** (the brightest 2925 donut
   peaks at 0.5 of normalized scale — confirm what that means physically). Mirrors DES magnitude cuts;
   avoids blooming + the worst of brighter-fatter. *(P0, reviewer.)*

**What `R_e` measures for an annulus:** ≈ the **ring-centroid radius** `≈ R_out·(1+ε)/2`, *not* the
outer blur diameter. With fixed optics (constant ε) this is a constant scale factor, so the **tilt
gradient is preserved**; but it is **not** the optics blur diameter — document this so nobody
back-computes `Δ·D/f` from `R_e`, and do not surface `R_e` as "HFR" in the UI without a note (users
comparing to historical HFD will be confused).

**Simulated efficacy (reviewer Monte-Carlo, annulus R_out=14, ε=0.45, s=4):** flux-weighted HFR slope
rising/undefined at the faint end; **`R_e` slope ~0.10 px/dex over ~100× amplitude (≈30× reduction).**

## 5. Approach C (complement) — demoted to per-frame diagnostic/gate

The empirical joint fit `size ~ α + β·log(flux) + spatial(x,y)` cut tilt-fit residual RMSE 28 % on
the 2925 frame. **But if A works, C should not be needed** — a regression patch over a metric that is
no longer biased adds confounding risk for no gain. Therefore:

- **Default: C is OFF.** Use it as a **per-frame diagnostic** to *verify* `R_e`'s residual
  brightness slope is negligible.
- **Activate normalization only if** a real residual slope survives `R_e` **AND** per-frame gates
  pass: `|r(logFlux, x)| < 0.3`, `|r(logFlux, y)| < 0.3`, **VIF < 3**, and a homogeneity-of-slopes
  check (fit `log(flux)·(x,y)` interaction; require it insignificant). *(reviewer)*
- **Functional form:** `log(flux)` is correct as a *local* linearization (mechanism gives
  `β = s·ln10/√(2 ln(A/T))`, itself slowly varying); restrict the brightness range or use `√(log)`
  if a single slope mis-fits across the full 150× range.
- **Always a JOINT model** (size ~ brightness + position), never residualize-then-map — spatial
  confounding can *bias* the tilt estimate, not just inflate its error.

## 6. The dominant residual risk — background-plane bias (P0)

`R_e`'s invariance rests on an **unbiased total flux**. A bias `δ` in the fitted background plane adds
spurious flux `δ × (integration area)`, which is **brightness-dependent and worse for donuts**: for a
~25 px-radius donut (area ≈ 1960 px²), a 0.1σ background bias is **3.3 % of total flux for a faint
donut but only 0.3 % for a bright one** — re-introducing exactly the dependence we removed, and with
*more* leverage than the current (smaller) thresholded aperture. With the 2925 median
Background/NoiseSigma ≈ 21, a 1 % plane-fit error is ~0.2σ — squarely in this regime.

**This, not CoG convergence, is the real risk.** Mitigations are the §4 items: tight integration-
radius cap (leverage ∝ area), background from an annulus outside the donut with propagated
uncertainty, down-weighting high-σ_bg stars, and preferring R30–R50.

## 7. Approach B — parametric donut forward fit, as the offline validation oracle

B is the gold standard (Roddier curvature sensing; LSST/DECam "donut" WFS): a forward model
**annulus(R_out, ε) ⊛ seeing kernel + local background plane + flux amplitude**, where flux enters as
a **separable multiplicative amplitude**, so the geometric size `R_out` is **brightness-independent by
construction**. It is heavy, degenerates near focus, and needs ε — so it does **not** ship in the
production plugin.

Instead, **B is implemented inside `TestApp` as a ground-truth oracle** to validate the production
A+C metric. Because B's `R_out` has *no* brightness term by construction, it is the reference against
which A's empirical brightness-independence is judged on real data.

**B (TestApp) design:**

1. **Model.** Per detected donut, fit `I(x,y) = b(x,y) + amp · [annulus(R_out, ε) ⊛ G(σ_seeing)](x−x₀, y−y₀)`
   where `b` is the existing local background plane (held fixed from the plane fit, or co-fit),
   `annulus` is a top-hat of outer radius `R_out` and inner radius `ε·R_out`, `G` is a Gaussian seeing
   kernel. Free params: `x₀, y₀, R_out, ε, σ_seeing, amp` (+ optional plane). Nonlinear least squares
   (reuse the plugin's Alglib LM infrastructure).
2. **Initialization.** `ε` from the optical train if known (else fit, seeded ~0.3); `R_out` seeded
   from the geometric `Δ·D/f`; center from the ring-fit center (§4.1).
3. **Outputs per star.** `R_out`, `ε`, `σ_seeing`, fit R²/reduced-χ², plus the derived
   **ring-centroid radius `R_ring = R_out·(1+ε)/2`** — the apples-to-apples comparator for A's `R_e`
   (which lands at the ring-centroid radius, §4).
4. **Brightness-independence self-check.** Regress `R_out` and `R_ring` on log(peak); both must be
   flat (slope ≈ 0) by construction — if not, the oracle itself is mis-fit (e.g. saturation), which
   is itself diagnostic.

This is the basis for the §9.5 **A+C-vs-B agreement report**. If A+C tracks B with low scatter and a
near-zero brightness slope, A+C is validated; if it diverges, the divergence localizes the problem
(background leverage, fraction choice, centroid).

## 8. Integration points (codebase)

| Concern | Location |
|---|---|
| HFR computation (add `R_e` path) | `StarDetection/StarDetector.cs` `MeasureStar` (≈1022–1074); footprint/clip in `EffectiveClipMultiplier` |
| Local background plane (reuse) | `StarDetector.cs` `FitLocalBackgroundPlaneAndContamination`; `LocalBackgroundPlane.ValueAt` |
| `Star` field for the new size + its σ | `Interfaces/IStarDetector.cs` `Star` (add parallel field; **do not** overwrite `HFR`) |
| Frame aggregation | `StarDetection/HocusFocusStarDetection.cs` (mean/median reduction); `Optimization/Review/StarReviewHfrStats.cs` (corner stats) |
| Sensor model per-star curves + weighting | `Inspection/SensorModel.cs` `FitImages`, `EstimateHfrStdDev` |
| Gating | New `StarDetectionOptions` flag, tied to defocus-aware donut detection; UI in `Resources/OptionsDataTemplates.xaml` |

**Decision: `R_e` is a *parallel* per-star field, not a replacement for `HFR`** — avoids recalibrating
the entire in-focus AF pipeline (MinHFR, V-curve scale, historical comparability). Donut/inspector
paths consume `R_e`; in-focus AF keeps legacy HFR unless explicitly switched.

### 8.1 Gating — donut scenarios only

The normalization is **strictly gated on the existing defocus-aware donut detection master toggle**
(`DefocusAwareDonutDetection` / `DefocusAwareGates`, mapped in
`HocusFocusStarDetection.BuildStarDetectorParams`). Rationale: that toggle is precisely the signal
that "donuts are expected," and reusing it avoids a second, redundant user option.

- **Toggle OFF (default):** `R_e` is **not computed**, no aggregate consumes it, and the size used
  everywhere is the legacy flux-weighted HFR — **bit-identical to today**. No risk to in-focus AF.
- **Toggle ON:** `R_e` (+ its σ) is computed for detected stars and is the size consumed by the
  donut-aware aggregation and the sensor/tilt model; legacy `HFR` is still carried for display/
  back-compat.
- A `StarDetectorParams` field (e.g. `NormalizeDonutSize`, derived from the master toggle in
  `BuildStarDetectorParams`) carries the gate into the detector, mirroring how
  `DefocusAwareDistortion`/`DefocusAwareCentering` are derived today. TestApp keeps an explicit CLI
  flag to force it for diagnosis.

## 9. Validation plan (must-do, before claiming success)

These need radial profiles the current CSV lacks; extend `TestApp` to dump per-star CoG arrays.

1. **Per-fraction regression.** Dump CoG → compute R20/R30/R50/R80 + total flux + convergence radius;
   regress each on log(peak). Target: chosen-fraction slope < 0.3 px/dex and < noise floor.
2. **Background-perturbation test.** Recompute `R_e` with the bg plane offset by ±0.1σ, ±0.5σ; report
   `dR_e/dδ` vs brightness — directly measures the §6 risk on real data.
3. **Edge-width check.** Fit `s` on ~5 bright donuts; confirm it predicts both the 8.4 and 3.1 px/dex
   slopes (falsifiable mechanism test).
4. **Tilt-fit acid test (the proof that matters).** Refit the sensor tilt/curvature model with
   FW-HFR vs `R_e` on the 2925 frame (and bank-wide): the brightness term's +29 R² should largely
   vanish while r(HFR,X/Y)=0.37/0.59 is preserved.
5. **A+C-vs-B agreement report (deliverable).** Run the §7 TestApp forward-model oracle (B) and the
   production A+C metric on the **same detected donuts**, then emit a report covering:
   - **Scatter & bias:** `R_e` (A+C) vs `R_ring` (B) per star — Pearson r, OLS slope/intercept,
     median |Δ| and robust scatter (MAD); ideally slope ≈ 1, small offset.
   - **Brightness independence head-to-head:** slope of `R_e` vs log(peak) and of B's `R_out`/`R_ring`
     vs log(peak) — both should be ≈ 0; report the gap.
   - **Spatial-signal fidelity:** tilt/curvature gradient recovered from A+C vs from B (do they agree
     on the tilt direction/magnitude?).
   - **Where they disagree:** flag stars with large |R_e − R_ring| and attribute (background-σ,
     proximity to neighbor, partial saturation, poor B fit via reduced-χ²).
   - Run on the mufti 2925 frame first, then across the AF bank for setups with donuts.
   Acceptance: A+C tracks B with slope ∈ ~[0.9, 1.1], residual brightness slope < 0.3 px/dex, and
   agreeing tilt direction. Divergence localizes the failure (§6 background leverage, §4 fraction,
   centroid).

## 10. Risks & mitigations (summary)

| Risk | Mitigation |
|---|---|
| Background-plane bias re-introduces brightness dependence (dominant) | Tight area cap; external-annulus bg + propagated σ; down-weight high-σ_bg; R30–R50 |
| Unstable intensity centroid on hollow donut | Flux-symmetry / ring-fit center for deep donuts |
| Noise-dependent CoG convergence biases total flux (faint → R_e low) | Self-limiting (R_e in steep CoG region); cap radius; ~0.1 px/dex residual, acceptable |
| C biases tilt via spatial confounding | C off by default; gate on r<0.3, VIF<3, homogeneity-of-slopes; joint model only |
| Partial saturation flattens bright ring (deflates R_e, non-monotone) | Saturation guard vs true full-well ADU |
| `R_e` ≠ historical HFD → user confusion | Parallel field; UI note; never relabel as "HFR" silently |

## 11. References

- Weber & Brady 2001, *Fast Auto-Focus Method and Software for CCD-based Telescopes* (HFD = true 50 %-enclosed radius).
- Bertin & Arnouts 1996 (SExtractor `FLUX_RADIUS` / `FLUX_AUTO`).
- Antilogus et al. 2014; Coulton et al. 2018; Broughton et al. 2024 (brighter-fatter, ~1–3 %).
- Roddier 1988 (curvature sensing); Roodman, Reil & Davis 2014; Xin et al. 2015 (LSST/DECam donut WFS).
- Jarvis et al. 2016 (size–magnitude stellar locus); Azevedo et al. 2025 (spatial confounding bias).
- Tokovinin 2021 MNRAS 502, 794 (defocused-ring geometry).

## 12. Tooling note

A small diagnostic-only change was made to extract the §1.1 data: `TestApp`'s
`ContaminationDiagnosticRunner` now emits `PeakBrightness`, `MeanBrightness`, `BBoxW`, `BBoxH` in
`contamination_stars.csv` (no behavioral change to detection). The §9 validation will additionally
require: (a) a per-star **curve-of-growth array dump**, and (b) the **forward-model donut oracle (B,
§7)** as a new `TestApp` path that emits the §9.5 A+C-vs-B agreement report. Neither touches the
production plugin.
