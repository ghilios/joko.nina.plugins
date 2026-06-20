# Donut HFR Normalization — §9 Validation Results

**Date:** 2026-06-19
**Branch:** `ghilios/donut-hfr-normalization`
**Design:** [`donut-hfr-normalization-design.md`](donut-hfr-normalization-design.md)
**Status:** Empirical proof of Approach A (curve-of-growth `R_e`) on real data — **target met on the
mufti frame and replicated across 4 more setups** (§ Multi-setup validation). Key refinement from the
multi-setup run: the metric must be **size-gated per star** (donut regime only), not just toggle-gated.

## Setup

- **Frame:** `D:\Autofocus Bank\mufti\…-3-001\attempt01\01_Frame00_…_Focuser2925.fits` (the most
  heavily-defocused frame; 96 detected donuts), profile `Default`, defocus-aware gates ON.
- **Tool:** `TestApp contamination … --defocus-distortion --defocus-centering`, which now also emits
  `cog_radii.csv` — a per-star curve-of-growth dump (background-subtracted flux in 1-px radial bins
  using each star's fitted local background plane), reporting encircled-flux radii R20/R30/R50/R80
  under two integration caps and a ring-fit center, plus background-perturbation variants.
- **Analysis:** offline Python regressions (this file). No production-plugin code was changed.

## Headline result

| Metric | Brightness slope (px/dex) | t-stat | r² | Verdict |
|---|---|---|---|---|
| **Legacy flux-weighted HFR** | **+3.12 ± 0.57** | **5.50** | 0.244 | brightness-dependent (significant) |
| **`R₅₀` (ring center + adaptive cap)** | **+0.35 ± 0.88** | **0.40** | 0.002 | **indistinguishable from 0** |

**Tilt-fit acid test** — extra variance explained by adding `log(peak brightness)` on top of a spatial
`(x, y)` fit (a pure brightness *confound* with the tilt signal):

| Metric | spatial-only R² | + log(brightness) R² | brightness adds |
|---|---|---|---|
| Legacy HFR | 0.401 | 0.693 | **+29.2 pts** |
| `R₅₀` (ring center + adaptive cap) | 0.562 | 0.573 | **+1.1 pts** |

The brightness confound is **eliminated** (29.2 → 1.1 pts) *and* the spatial tilt signal becomes
**cleaner** (spatial R² 0.40 → 0.56). Partial `log(peak)` coefficient in the joint tilt model drops
from +3.46 to +0.92 px/dex (−73 %).

## What actually mattered (and what didn't)

Two refinements were tested independently against a naive `R₅₀`:

1. **Ring-fit center — load-bearing (the dominant fix).** A hollow donut's intensity centroid is
   unstable: the detector's center differed from the flux-weighted ring center by a **median of
   3.67 px** (large vs a ~14 px radius), and that error was brightness-correlated. Naive `R₅₀` from
   the detector centroid still sloped at **1.5 px/dex**; recomputing from the ring center dropped it
   to **0.35 px/dex**. This confirms reviewer P0 #2.

2. **Adaptive (noise-convergence) cap beat the fixed cap — hypothesis rejected.** I expected a
   brightness-independent *fixed* integration radius (frame-median donut size) to be safest. It was
   **worse**: `R₅₀f` (fixed) sloped at **1.38 px/dex** vs **0.35** for the adaptive convergence cap.
   Reason: integrating every star to the same generous radius gives faint donuts a large
   background-dominated area (background leverage ∝ area), re-biasing their total flux. The
   noise-convergence cap naturally stops where SNR dies, limiting that area for faint stars.

| Fraction | Adaptive cap slope | Fixed cap slope |
|---|---|---|
| R20 | 1.53 | 1.96 |
| R30 | 1.27 | 1.87 |
| **R50** | **0.35** | 1.38 |
| R80 | −1.46 | 0.33 |

For the adaptive cap, **R50 is the sweet spot** (R20/R30 ride the inner ring edge and still slope;
R80 overshoots into the noisy outer wing and goes negative). Note this differs from the reviewer's
*compact-star* simulation (where R20–R30 were best) — for an **annulus** the inner fractions sit on
the rising inner ring edge and are less stable, so R50 wins.

## Background-sensitivity (the residual risk, quantified)

The reviewer's #1 risk — background-plane bias, brightness-dependent via donut area — is real but
**small at realistic bias levels** with the adaptive cap + ring center:

| Background-plane offset | median \|ΔR50f\| | ΔR50f vs log(peak) slope |
|---|---|---|
| ±0.1σ | ~0.04 px | ±0.23–0.27 px/dex |
| ±0.5σ | ~0.20 px | −0.95 / +1.75 px/dex |

A ±0.1σ plane error (a realistic IRLS plane-fit accuracy) perturbs R50 by <0.05 px and adds
<0.3 px/dex of brightness slope. A ±0.5σ error would matter — motivating the design's remaining P0s
(external-annulus background + per-star σ propagation + down-weighting high-σ_bg donuts), which were
**not** needed to hit target here but are the headroom for harder frames. The residual +0.92 px/dex
partial coefficient is consistent with a few tenths of a sigma of brightness-correlated plane error.

## Caveats / scope of this proof

- **One frame, one optical setup.** This proves the mechanism and the fix on the mufti 2925 frame.
  Bank-wide validation across setups (and across the focus sweep, not just the extreme) is still TODO.
- **`R₅₀`'s raw CV is slightly higher** than legacy (21.4 % vs 18.6 %) — but that is because *more of
  its variance is now real spatial tilt signal* (spatial R² 0.40 → 0.56) and *less is brightness
  noise*. CV is the wrong success metric here; the brightness slope and tilt-fit confound are the
  right ones, and both improved decisively.
- **B oracle cross-check still pending.** The §9.5 A+C-vs-B agreement report (forward-model donut fit
  in TestApp) has not been run yet; this validates A against the brightness axis directly, not yet
  against an independent geometric ground truth.

## Conclusion

Approach A is empirically validated on real data: a true encircled-flux `R₅₀`, **measured from a
ring-fit center with a noise-convergence integration cap**, removes the donut HFR brightness artifact
(slope 3.12 → 0.35 px/dex, no longer significant; tilt confound 29.2 → 1.1 R² pts) while sharpening
the spatial tilt signal. The **ring-fit center is essential**; the fixed-radius cap idea was tested
and rejected in favor of the adaptive convergence cap. Approach C (empirical detrend) is **not
needed** for this frame — the residual brightness slope is statistically zero.

## Multi-setup validation (4 additional banks)

Re-ran the same `cog_radii.csv` pipeline on the most-defocused extreme frames of four more setups
(`LinwoodFocus`, `Panos`, `toml999`, `FlyData`), each defocus-aware. Brightness slope vs log(peak) and
the tilt-fit confound (extra R² from log(peak) on top of a spatial fit):

| Frame | N | med HFR | dyn range | Legacy slope (t) | **R₅₀ slope (t)** | Tilt confound legacy→R₅₀ |
|---|---|---|---|---|---|---|
| panos_hi | 26 | 13.2 px | 10× | **+5.71 (6.6, sig)** | −4.02 (−1.2, n.s.) | **63.9 → 5.8** |
| toml999_hi | 533 | 8.7 px | 426× | **+0.89 (18.1, sig)** | +0.47 (1.5, n.s.) | **41.5 → 0.4** |
| toml999_lo | 486 | 8.1 px | 311× | **+0.69 (19.6, sig)** | +0.25 (0.8, n.s.) | **44.7 → 0.1** |
| linwood_hi | 11 | 12.9 px | 65× | +2.31 (2.8, sig) | +1.40 (2.0) | 17.4 → 7.1 |
| flydata_hi | 33 | 6.7 px | 33× | +0.60 (0.3, n.s.) | +0.63 (0.3) | 1.3 → 1.1 |
| flydata_lo | 50 | 4.8 px | 35× | +0.79 (0.8, n.s.) | −1.30 (−0.8) | 1.8 → 0.7 |
| **panos_lo** | 222 | **2.1 px** | 135× | +0.23 (3.2) | **−1.55 (−3.1, sig)** | 6.3 → 4.0 |
| linwood_lo | 5 | — | 11× | (too few stars) | — | — |

### Findings

1. **Donut regime (median HFR ≳ 6 px): R₅₀ works, replicated across 4 setups.** Every frame with a
   *significant* legacy brightness slope (panos_hi t=6.6, toml999_hi/lo t≈18–20, linwood_hi t=2.8) has
   that slope rendered **non-significant** by R₅₀, and the tilt confound collapses (63.9→5.8, 41.5→0.4,
   44.7→0.1, 17.4→7.1). This confirms the mufti 2925 result generalizes.

2. **Near-focus regime (panos_lo, median HFR 2.1 px): R₅₀ HURTS.** At the extreme focuser position but
   essentially in focus, the donut-tuned pipeline (ring-fit center + 1-px-binned curve of growth)
   *introduces* a significant **negative** brightness slope (−1.55, t=−3.1) where legacy had almost
   none (+0.23). Compact stars are quantization-limited at 1-px bins and have no hollow center for the
   ring-fit to lock onto.

3. **No-artifact frames (flydata): R₅₀ is ~neutral** — where legacy already showed no significant
   brightness dependence, R₅₀ neither helps nor clearly hurts (small N).

### Consequence for the design — size-gate, not just toggle-gate

Finding 2 means gating on the `DefocusAwareDonutDetection` *toggle* alone is **insufficient**: a
defocus-aware AF run still contains near-focus frames (and a single frame mixes compact and donut
stars). The normalization must be **size-gated per star** — apply R₅₀ only to candidates above a donut
size threshold (reuse the existing `DefocusDistortionSizeReference` ≈ 22.5 px "large candidate"
notion), and **fall back to legacy HFR for compact stars**. This keeps the win in the donut regime
without the panos_lo-style regression near focus. (Folded into design §8.1.)

## Reproduce

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe contamination \
  --image "D:\Autofocus Bank\mufti\AutoFocus_20260607_030642-20260615T214722Z-3-001\attempt01\01_Frame00_BitDepth16_Bayered0_Focuser2925.fits" \
  --defocus-distortion --defocus-centering --out "C:\temp\hf-donut-2925"
# then regress cog_radii.csv columns R20/R30/R50/R80 (and *f variants) on log10(PeakBrightness)
```

## A-vs-B agreement (§9.5)

`TestApp agreement --image <frame> --defocus-distortion --defocus-centering` runs detection exactly like
the `contamination` runner, then for every accepted **donut** star (bboxMax ≥ `DefocusDistortionSizeReference`
= 22.5 px) computes two independent size estimates from the **same** ring-fit center and local background
plane: **A** = encircled-flux **R₅₀** (the production candidate) and **B** = the annulus⊛Gaussian
forward-fit **Rring = Rout·(1+ε)/2** (geometric, brightness-independent by construction). Per design §9.5 the
metric passes when the R₅₀-vs-Rring OLS slope ∈ ~[0.9, 1.1] **and** both brightness slopes are < 0.3 px/dex.

**Oracle B was hardened** (this round). The previous B fit scored candidates by **correlation r² only** (no
explicit amplitude/background, no noise weighting) and was itself the more brightness-biased of the two
estimates. It is now a proper **noise-weighted least-squares** fit: per `(Rout, ε, σ)` grid point it solves a
closed-form 2×2 normal-equation for the ring **amplitude** and a **residual-background offset** (the local
plane may be slightly off), with per-bin weight `w_b = 1/(count_b·σ²)` from photon/read noise; it rejects
`amp ≤ 0`, scores by **weighted R²**, then does a **sub-pixel local refinement** (0.1 px `Rout`, 0.01 `ε`,
0.1 `σ`) around the coarse optimum. Agreement is now measured over rows with oracle **`FitR2 ≥ 0.8`** (a
genuine fit, not a loose one); rows below the floor are excluded and counted.

| Frame | N donuts (used/excl) | slope (R₅₀ vs Rring) | Pearson r | median \|R₅₀−Rring\| | R₅₀ bright. slope | Rring bright. slope | §9.5 |
|---|---|---|---|---|---|---|---|
| mufti F2925   | 79/3 | 1.134 | 0.934 | 1.17 px (8.4%)  | −0.004 | 1.037 | **FAIL** |
| Panos F44396  | 22/3 | 0.297 | 0.414 | 1.49 px (10.6%) | −0.380 | 2.565 | **FAIL** |
| toml999 F4237 | 512/6 | 0.885 | 0.768 | 2.06 px (28.6%) | 0.913  | 0.738 | **FAIL** |

### Interpretation

**The hardening clearly improved B's *fit quality* but did NOT make Rring brightness-independent — so no frame
passes the §9.5 gate.** What got better: the star-to-star correlation rose sharply (mufti Pearson 0.654→0.934,
slope 0.751→1.134; toml999 0.665→0.768, slope 0.630→0.885), and median |R₅₀−Rring| tightened slightly
(~1.2–2.1 px, still ~8–14 px donut radii). The weighted LSQ also flattened **R₅₀'s** apparent brightness slope
on mufti (−0.513→−0.004) and Panos (−0.926→−0.380), so A looks close to brightness-flat on two of three frames
(toml999 is the exception at +0.913).

**What did NOT get fixed: B (Rring) is still strongly brightness-biased.** Its brightness slope remains large
and positive on every frame — +1.04 / +2.57 / +0.74 px/dex (was +1.69 / +5.91 / +0.42). It came down on mufti
and Panos but is still far above the 0.3 px/dex bar, actually rose on toml999, and is the dominant reason every
frame fails `brightness-flat`. Panos additionally fails on slope (0.30) with only weak correlation (r = 0.41) —
its 22-donut sample is small and the forward fit still drifts to larger `Rout` on the brighter rings. **This
is the opposite of the §9.5 premise that B is the brightness-independent ground truth**, and it means B cannot
yet be used to certify R₅₀: even where A *is* flat (mufti), the gate fails because B is not.

**Bottom line (as measured, not massaged):** weighted amp+bg LSQ + sub-pixel refine + `FitR2 ≥ 0.8` makes B a
*better-correlated* comparator but **does not remove B's own brightness bias**, so A-vs-B still does not
validate R₅₀ as a brightness-independent donut size. The residual bias is intrinsic to fitting a single
ring-amplitude to a brightness-varying donut profile (brighter donuts have more SNR in the outer skirt, pulling
`Rout` outward); resolving it likely needs either an SNR-matched/normalized profile before fitting, or
abandoning B as the ground truth in favor of a direct geometric construct that does not estimate amplitude at
all.

### Reproduce (agreement)

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe agreement \
  --image "D:\Autofocus Bank\mufti\AutoFocus_20260607_030642-20260615T214722Z-3-001\attempt01\01_Frame00_BitDepth16_Bayered0_Focuser2925.fits" \
  --defocus-distortion --defocus-centering --out "C:\temp\hf-agree\mufti2925"
# reads agreement_summary.txt (stats) + agreement.csv (per-donut R50/Rring/Eps/FitR2)
```
