# Future Plan: Higher-Density Focus Sweeps for Sensor-Model / Tilt Runs

## Context

Tilt / sensor-model calibration on the `astrodet_6` run is **noise-limited**. Measured calibration SNR (screw-move
signal vs the AllInward-piston + re-baseline-drift noise probes):

| Estimator | SNR | Screw 1→2 gap |
|---|---|---|
| 4-corner OLS (current wizard) | **0.73** | 247.7° |
| Per-star paraboloid `Gx/Gy` (the structural rewire, Step 3) | **1.61** | 130.2° |
| + donut-aware σ down-weighting (lever 1) | _TBD_ | _TBD_ |

Reliability bar is **SNR ≥ 2**. The paraboloid rewire more than doubled SNR but is still short, and the remaining
limiter is the **per-image donut scatter** (per-star R² ≈ 0.35–0.60). The donut noise exists because the sweep
spans ~±500 focuser steps from focus at **step 175** (≈ 626 µm ≈ ~23× the critical focus zone), so the extreme
frames are huge, partial, overlapping donuts (focuser 54/229/1104) that (a) measure HFR unreliably and (b) fail
RANSAC alignment. The per-star fit is also coarsely sampled near focus (~3 usable points).

**Idea (this plan): run sensor-model / tilt AF sweeps at half the step size and ~twice the step count** to boost
the per-star signal — more measurements per star (variance ↓ ~1/√N) and finer near-focus resolution (better
vertex localization), with proportionally more points landing in the well-behaved (tight-PSF) zone rather than the
donut tails. This must be validated on a **new capture** — it cannot be simulated from the existing 175-step frames.

## Hypothesis

Halving the step (~175 → ~88) and doubling the count (7–8 → ~15) — same total range, 2× sampling density —
roughly doubles the per-star signal and sharpens near-focus sampling, lifting the calibration SNR over the
reliability bar. Secondary variant to test: **finer step with a slightly shorter range** (fewer extreme-donut
frames) vs **same range with 2× density** (more total signal) — measure which wins.

## Plan

1. **Sweep-cadence override for sensor-model/tilt runs.** The inspector AF sweep already supports overrides
   (`InspectorOptions.StepCount` / `StepSize`, default −1 = profile; applied in `InspectorVM` ~1038-1045; engine
   reads them in `AutoFocusEngine.cs:2256-2264`). Expose a sensor-model/tilt "high-density sweep" preset (or let the
   Tilt Adapter Wizard request a finer StepSize + larger StepCount) so calibration can sample finer than the
   profile's normal AF without changing day-to-day autofocus.
2. **Wizard wiring.** Add a wizard option (or default for tilt calibration) to capture each of the six steps at the
   finer cadence. Persist the cadence used into `metadata.json` so replays/comparisons are reproducible.
3. **Capture cost note.** 2× exposures/run × 6 steps ≈ 2× calibration time. Surface the trade-off; consider
   defaulting to finer step only within a bounded near-focus range and sparse sampling beyond it.

## Validation

- Re-capture one tilt calibration at the finer cadence on the same optical train; run
  `TestApp tilt --dataset <run>` and compare **calibration SNR, screw 1→2 gap, move-magnitude ratio, and per-star
  R²/stars-in-model** against the 175-step `astrodet_6` baseline. **Target: paraboloid SNR ≥ 2 (reliable).**
- Bank-validate the sensor-model fit (`running-af-bank-validation` skill / `TestApp bank-verify` sensor-fit
  metrics) at the finer cadence to confirm no regression and to characterise the signal gain across the bank.
- Confirm the finer sweep also improves RANSAC alignment (more near-focus, tight-PSF frames → more frames align,
  fewer wide-radius fallbacks).

## Interactions (stack with the other fixes)

- **Step 3 (paraboloid rewire):** the finer sweep feeds the robust estimator more, better-conditioned points.
- **Lever 1 (donut-aware σ):** down-weights whatever donut tails remain; complementary, not redundant.
- **SNR gate (Step 4):** the same metric scores the new capture and tells the user whether the finer cadence
  cleared the bar.
- **Drift:** more exposures lengthen each step → more thermal/backlash drift *between* differenced steps; pair the
  finer cadence with thermal-settle + backlash compensation (Step 6) and minimise inter-step time.

## Status

Future experiment — **requires a new capture** at the finer cadence. No code is blocked on it; the
override/preset wiring (steps 1–2) can be built ahead of the capture and validated on the bank.
