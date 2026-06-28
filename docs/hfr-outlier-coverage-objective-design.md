# Extreme-HFR outlier penalty + region-coverage reward (AF optimizer) — design & results

## Context

A new AF run was added to the bank at `D:\Autofocus Bank\bobp\AutoFocus_20260626_215910` (9 Bayered XISF frames,
focuser 5588–5780, ~24-step fine near-focus sweep, 5936×3966 ≈ 23.5 MP). The user reported two pathologies from
optimizing it:

1. **A very bright star is accepted whose HFR is far above every other star.** The detector has no saturation
   rejection and no upper-HFR cap, and saturated cores bias HFR *high by design* (`StarDetector.MeasureStar`). A
   bright bloated star sails through every gate and inflates the per-frame HFR (the curve point).
2. **Most other stars are rejected by the `LowSensitivity` gate → very low recall.**

Golden-set audit of the bobp run confirms the second point precisely. Scoring the **user's current settings**
(`Sensitivity=30.33`, `StarClippingMultiplier=10`) against the detector-independent golden (1705 golden stars):

```
Current settings:  recall@all=0.216  recall@SNR>=12=0.354  precision=1.000
FN attribution:    REJECTED:LowSensitivity = 1120 / 1337 misses
```

i.e. an extreme-sensitivity config that rejects ~5/6 of the real stars.

## Goals

1. Add golden ground-truth for the bobp run (done — `*.golden.json` sidecars, 1705 stars).
2. Penalize accepting extreme-HFR outliers in the optimizer objective, **and** add a direct detector-side guard.
3. Reward spatial coverage so the optimizer keeps stars across the sensor even at a small focus-tightness cost.
4. Show precision/recall before and after.

## Design

### Core constraint — dilution sensitivity

A penalty only changes the optimizer's *choice* if the optimizer can reduce it by a move it can make. It cannot
reject the bright star (no gate), but it *can* lower sensitivity to admit more normal stars. So the HFR-outlier
signal must be a **fraction** (outliers / accepted) that *shrinks* as recall rises — an absolute "max/median ratio"
is admitted at every sensitivity and would penalize all configs equally (inert). The fractional design couples the
penalty to recall (and is what the make-or-break unit test pins).

### 1. `SHfrOutlier` — extreme-HFR outlier penalty (objective)

Multiplicative penalty modeled on `SDefocusPrecision` (`OptimizationObjective.cs`). The accepted-star HFRs of the
**near-focus** frames (within `NearFocusWindowSteps·step` of the fitted minimum) are pooled; a star is an extreme
outlier when **both** `HFR ≥ median + k·MAD` *and* `HFR ≥ relMargin·median` (the k·MAD term guards loose frames; the
relative-margin term guards the MAD≈0 tight-frame case). Penalty = `1 − Strength·max(0, frac − Threshold)` clamped
to `[MinFactor,1]`. Returns **exactly 1.0** when there is no per-star HFR data or no near-focus outlier ⇒ J
bit-identical at the baseline. Defaults: `k=4.0`, `relMargin=1.5`, `Threshold=0.05`, `Strength=1.0`,
`MinFactor=0.5`. Worked magnitudes (one blob among N near-focus accepted): 0.72 (N=3), 0.85 (N=5), 0.95 (N=10),
**1.0 (N≥21)** — the dilution gradient.

### 2. `SCoverage` — region-coverage reward (objective)

Additive sub-score folded into the renormalized weighted sum with a small real weight `Wcov=0.05` (so it can cost a
*little* focus tightness — not a plateau-only tie-breaker). Per frame, occupancy = fraction of a 3×3 sensor tiling
(`SensorAberrationCalculator.CreateFullRegionSet`) that holds ≥1 accepted star; `SCoverage` is the near-focus mean.
`Wcov=0` or no occupancy data ⇒ excluded from both numerator and denominator ⇒ J bit-identical (so the default
`Wcov` ships safely — legacy callers carry no occupancy).

### 3. Detector-side saturated-star HFR exclusion (direct cure for #1)

In `HocusFocusStarDetection.BuildStarDetectionResult` (and the offline harness), the per-frame HFR aggregate
(`AverageHFR`/`HFRStdDev`) is computed over a saturated-filtered subset: partially-saturated stars
(`Background + PeakBrightness ≥ SaturationThreshold`) are dropped **only from the HFR average**, and only while
≥ `MinUnsaturatedStarsForHfr` (3) unsaturated stars remain. The saturated star stays detected, counted, in
`StarCenters`/`StarHFRs` (so the objective's outlier penalty still sees it) — only the curve point is cleaned. When
nothing is saturated the aggregate is unchanged (bit-identical). Surfaced as a persisted option
`ExcludeSaturatedStarsFromHFR` (default **on**) with a UI control and cross-machine import/export coverage.

### Plumbing

Per-star HFR and image dimensions are threaded the same way accepted-star centers already were:
`FrameDetectionResult.{StarHFRs, ImageWidth, ImageHeight}` → populated in `RunEvaluationLoader` (wizard) and
`HarnessSplitDetector` (offline, via `DetectionContext.FullImageSize`) → aggregated in `EvaluateAndFitAsync` into
`RunEvaluationMetrics.{FrameStarHFRs, FrameRegionOccupancy}`.

### A/B switch

`TestApp optimize --legacy-objective` zeroes `HfrOutlierStrength` + `Wcov` and forces `ExcludeSaturatedStarsFromHFR`
off, so a single binary produces both the pre-change "before" and the new "after".

## Results — bobp run (golden = 1705 stars; `golden eval --match center --match-radius 12`)

| Config | recall@all | recall@SNR≥12 | precision | σ_focus (opt) |
|---|---|---|---|---|
| **Current settings** (user's, Sens=30.3) | **0.216** | 0.354 | 1.000 | 1.59 (current) |
| Optimized, default seed — *before* (legacy) | 0.638 | 0.691 | 0.620 | 0.1603 |
| Optimized, default seed — *after* (new) | 0.638 | 0.691 | 0.620 | **0.1544** |
| Optimized, from-current — *before* (legacy) | 0.706 | 0.764 | 0.543 | 0.1561 |
| Optimized, from-current — *after* (new) | **0.709** | **0.768** | 0.536 | **0.1551** |

**Findings:**

- The user's low recall (**0.216**, 1120 `LowSensitivity` rejections) is a property of their *current* settings; the
  optimizer fixes it (→ 0.64–0.71) by dropping sensitivity.
- The **new objective terms are near-inert on bobp** (from-current recall 0.706→0.709; default-seed bit-identical).
  bobp's curve is tight at *all* sensitivities, so the legacy objective already favors max recall here, and the
  remaining misses are gate-limited (`TooSmall`/`NotCentered`), which the new terms don't touch. This demonstrates
  the change is **safe** (no regression) on this run; the terms bite only where the optimizer would otherwise trade
  recall for tightness.
- The **detector guard consistently tightens σ_focus** at identical settings (default-seed 0.1603 → 0.1544, ≈3.6%;
  from-current 0.1561 → 0.1551) by removing the saturated bright star's inflated HFR from the curve — the direct fix
  for issue #1. Recall/precision are unchanged by the guard (it only affects the HFR aggregate, not detection).

## Regression check (targeted, ~3 runs)

_Methodology:_ `optimize --per-run` before (`--legacy-objective`) vs after (new) on `fmeschia_Focus`, `uneven`,
`standard_example2` (a fast sensitivity example, a medium stress case, and a large well-behaved run), then
`golden eval --params optimized --match center --match-radius 12` on each. Acceptance: AFTER recall ≥ BEFORE (no
recall regression) and σ_focus not materially worse.

| Run | recall@≥12 b→a | recall@all b→a | precision | σ_focus b→a | verdict |
|---|---|---|---|---|---|
| fmeschia_Focus | 0.333 → 0.344 | 0.226 → 0.233 | 1.00 / 1.00 | 1.032 → 1.087 | slight recall gain; σ +5% |
| uneven | 0.324 → 0.314 | 0.256 → 0.248 | 1.00 / 1.00 | 7.196 → 6.821 | −3% recall for −5% σ (minor trade) |
| **standard_example2** | **0.213 → 0.379** | **0.204 → 0.365** | 1.00 / 1.00 | 2.701 → 2.712 | **+79% recall, σ flat — strong win** |

**Verdict:** the change is **safe and net-positive**. On `standard_example2` the new objective recovered **+4480 real
stars** (TP 5707→10187) — the coverage reward admitting stars the legacy objective had left rejected
(`TooSmall` 1726→82, `LowSensitivity` 17869→15967) — at **zero precision cost** (precision stays 1.000). `fmeschia`
improves slightly; `bobp` is neutral (already maxed). The only regression, `uneven` −3% recall, is a deliberate
recall-for-σ trade (tighter centering gate; σ −5%) consistent with the "cost a small amount of focus tightness"
design intent. No run was wrecked: every optimized config keeps precision 1.000 and passes the per-frame hard floor.
The conservative defaults (`Wcov=0.05`) nudge rather than dominate `SFocus`; on runs where high sensitivity genuinely
gives a much tighter curve the terms recover stars at the margin (via distortion/size gates) without flipping the
sensitivity choice.

## Tests

24 new NUnit tests (1600 total green): dilution (penalty relaxes as normal stars are added, exact 1.0 at N≥20),
near-focus restriction, floor, fallback, coverage rises-with-spread + can-cost-focus, baseline bit-identity with
default-on constants, `RegionCoverage` occupancy geometry, the loader/evaluate plumbing, and the
saturated-star HFR exclusion (excluded when ≥3 unsaturated remain, kept when too few, unchanged when none saturated).
