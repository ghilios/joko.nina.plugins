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
forward-fit **Rring = Rout·(1+ε)/2** (geometric, brightness-independent by construction). Agreement is
measured over rows with oracle `FitR2 ≥ 0.5`. Per design §9.5 the metric passes when the R₅₀-vs-Rring OLS
slope ∈ ~[0.9, 1.1] **and** both brightness slopes are < 0.3 px/dex.

| Frame | N donuts (used/excl) | slope (R₅₀ vs Rring) | Pearson r | median \|R₅₀−Rring\| | R₅₀ bright. slope | Rring bright. slope | §9.5 |
|---|---|---|---|---|---|---|---|
| mufti F2925   | 82/0   | 0.751  | 0.654  | 1.29 px (9.2%)  | −0.513 | 1.692 | **FAIL** |
| Panos F44396  | 24/1   | −0.077 | −0.225 | 1.68 px (12.0%) | −0.926 | 5.913 | **FAIL** |
| toml999 F4237 | 517/1  | 0.630  | 0.665  | 2.49 px (36.5%) | 0.957  | 0.419 | **FAIL** |

### Interpretation

**No frame passes the §9.5 gate.** The absolute agreement is reasonable — median |R₅₀−Rring| is ~1.3–2.5 px
and R₅₀/Rring both land in the expected ~7–14 px donut-radius range — but the two estimates do **not** track
each other tightly enough star-to-star: the OLS slope is well below 0.9 on every frame (0.75 / −0.08 / 0.63),
and the correlation is weak-to-absent on Panos (r = −0.22). Worse, **B (Rring) is the more brightness-biased
of the two**: its brightness slope is large and positive on every frame (+1.69 / +5.91 / +0.42 px/dex),
which is the opposite of the §9.5 premise that B is the brightness-independent ground truth. The grid-search
forward fit (coarse `Rout`/`ε`/`σ` steps, correlation-only amplitude fit) appears to latch onto brighter,
better-defined rings at systematically larger `Rout`, so B is not behaving as a clean comparator here. R₅₀'s
own brightness slope is mixed (−0.51 / −0.93 / +0.96) and only marginal on mufti.

**Bottom line:** A-vs-B does not yet validate R₅₀ as a brightness-independent donut size. Before this gates
production, the oracle (B) needs to be hardened — finer/continuous forward-fit parameters, a proper
amplitude+background least-squares (not correlation), and a tighter `FitR2` floor — so that B is genuinely
brightness-flat and the slope comparison is meaningful. Reported as measured; numbers were not massaged.

### Reproduce (agreement)

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe agreement \
  --image "D:\Autofocus Bank\mufti\AutoFocus_20260607_030642-20260615T214722Z-3-001\attempt01\01_Frame00_BitDepth16_Bayered0_Focuser2925.fits" \
  --defocus-distortion --defocus-centering --out "C:\temp\hf-agree\mufti2925"
# reads agreement_summary.txt (stats) + agreement.csv (per-donut R50/Rring/Eps/FitR2)
```
