# Tilt-Adapter Calibration Noise — Root-Cause Diagnosis

**Status:** diagnosis (design spec). Implementation plan: `plans/tilt-calibration-noise-plan.md`.
**Dataset:** `D:\Tilt Calibration Bank\astrodet_6\TiltCalibration_20260628_115023` (astrodet profile, 3-screw).
**Date:** 2026-06-29.

## Context

A 6-step Tilt Adapter Wizard calibration on the already-calibrated **astrodet** profile produced a poor result:
the sensor models at the three "baseline" steps (which represent the *same* neutral screw state) came out very
different, and the recovered screw geometry was unreliable. The user reports turning the screws carefully. This
document explains **why**, from the saved data, the source code, and an empirical replay of the run.

**Headline:** the calibration is **noise-limited — the measurement noise floor is as large as the screw-move
signal (SNR ≈ 1)**, and the noise originates in a chain that starts with a **star-poor, too-coarsely-sampled
focus sweep** and is then **amplified by a structurally fragile 4-corner tilt estimator** and **contaminated by
real between-step drift**. The screws are not the problem — the data confirms the two turns were clean
(`moveMagnitudeRatio` = 1.0016). No amount of careful turning fixes a measurement whose noise ≈ its signal.

## How a tilt measurement is computed (the chain)

1. An autofocus run sweeps the focuser over 7–8 positions and detects stars in each frame.
2. The sensor is split into **5 regions** (center + 4 corners). For each region, all its stars' HFRs are pooled
   into one HFR-vs-position curve and a hyperbola minimum gives that region's best-focus position.
3. **`TiltPlaneModel.Create`** (`AutoFocus/TiltModel.cs:132-165`) fits a plane `z = A·x + B·y + c` through the **4
   corner** best-focus positions (center excluded) — **4 points, 3 parameters, 1 residual DOF, no weighting, no
   outlier rejection, no curvature term.** `(A,B)` is the tilt vector the wizard calibrates from.
4. The wizard derives screw geometry purely from how `(A,B)` *changes* between steps: each screw =
   `Screw_i − ReBaseline_i`, screw direction = `atan2(dA, −dB)` (`TiltCalibrationCalculator.cs:118-219`).

Separately and on the same frames, a **per-star tilted paraboloid** (`Inspection/SensorParaboloidModel.cs`) is
fit over *all* matched stars with winsorized outlier rejection and per-star σ weighting. It has its own tilt
gradient `Gx/Gy` **and** a curvature term — but the wizard harvests **only its curvature**
(`PopulateCurvature`, `TiltAdapterWizardVM.cs:946-951`) and **discards its tilt** (`:860-862` reads the 4-corner
`A/B` instead). So the rich, robust tilt estimate is computed every step and thrown away.

## Root cause, in layers

### Layer 0 — the soil: a star-poor field sampled too coarsely near focus
`TestApp focus-sweep` on all six runs (per-position star counts, whole sensor):

| Run (best focus) | star count vs focuser position | positions with ≥5 stars |
|---|---|---|
| Baseline (~579) | 54:1 229:2 404:11 **579:23** 754:10 929:1 1104:1 | 3 |
| AllInward (~609) | 0:1 84:1 259:9 **434:32** 609:26 784:5 959:1 1134:**0** | 3 |
| ReBaseline1 (~600) | 144:1 319:5 494:26 **669:28** 844:7 1019:1 1194:**0** | 3 |
| Screw1 (~619) | 94:1 269:6 444:19 **619:34** 794:8 969:1 1144:1 | 3 |
| ReBaseline2 (~584) | 59:1 234:1 409:9 **584:26** 759:13 934:4 1109:1 | 3 |
| Screw2 (~635) | 110:1 285:7 460:25 **635:38** 810:6 985:1 1160:1 | 3 |

Peak star count is only **~23–38 across the whole sensor**, and only **~3 of 7–8 positions** carry >5 stars; the
defocused wings collapse to **0–1**. With the step size of **175** (≈ 626 µm/step ≈ ~23× the critical focus
zone), stars are only detectable within ~±1–2 steps of focus, so the sweep effectively has **~3 usable points**.
Consequences:
- Each of the 4 corner quadrants gets only **~5–6 stars over ~3 positions** → its pooled best-focus is poorly
  constrained and moves with the exact star set detected.
- The per-star paraboloid needs **≥5 matched detections per star** (`SensorModel.cs:660`); almost no star is
  detectable in ≥5 of these positions, so the curvature fit runs on a marginal sample → it blows up (curvature
  radius came out 56 mm on Baseline, +17,279 µm sag at screw radius = **4.6× the entire focuser sweep** —
  physically impossible).

### Layer 1 — the amplifier: the 4-corner estimator (1 DOF, no robustness)
A 3-parameter plane through 4 noisy corner positions has **1 degree of freedom and no outlier rejection**, so a
single off corner maps almost 1:1 into `(A,B)`. Combined with Layer 0, tiny differences in which ~5 stars land in
a quadrant swing the tilt vector wildly. The robust per-star tilt (`Gx/Gy`, with the center pinned by default via
`FixedSensorCenter=true`, and an already-computed uncertainty `ThetaStdError`) would average over *all* stars and
model curvature — but it is discarded.

### Layer 2 — the differencing: variance adds, nothing averages it down
Each screw signal subtracts two independent noisy 4-corner fits, so their variances add; and
`MeasurementAverageCount = 1` means no per-step averaging. The strongest repeatability guard is therefore inert.

### Layer 3 — real between-step drift
Mean focuser position across the three baselines marches **monotonically** 591 → 599.6 → 609.8 (~67 µm over the
8-minute session) — not random fit noise, but thermal / focuser-backlash / adapter-creep / seeing. Because every
screw move is a *difference taken ~90–180 s apart*, this non-stationarity lands directly in the signal. **A better
estimator does not fix this** — a longer per-step average would actually make it worse.

## Empirical evidence

### From the saved metadata (SNR ≈ 1)
| Check | Expected | Measured | Verdict |
|---|---|---|---|
| 3 baselines (identical neutral state) | same (A,B) | RMS scatter **±8.0** units | noise ≈ 8 |
| AllInward = pure piston → zero tilt change | ~0 | **20.6** units | null-move noise ≈ 20 |
| Two screws physically 120° apart | 120° | **63.7°** (`rawAngleDiffDegrees`) | 56° error = 1.7σ of ±33° |
| Screw-move signal | — | **13.9** units | **≈ the noise → SNR ≈ 1** |
| `moveMagnitudeRatio` (turn evenness) | ~1 | **1.0016** | turns were clean — user's care confirmed |

Re-baseline drift guard (`EvaluateRebaselineDrift`) fires at **134%** and **74%** of the screw move (threshold
50%); angle-gap guard at **56°** deviation (threshold 30°). Both are advisory only; calibration proceeds and snaps
the 63.7° gap onto the ideal 120° lattice, emitting confident screw angles built on SNR≈1 data.

### From replaying the run (`TestApp tilt`, 3× identical)
- **Deterministic but fragile:** three replays were byte-for-byte identical, yet the replay's per-step `(A,B)` are
  **completely different from the live wizard's** on the *same frames* (e.g. Baseline live (−2.1, 24.1) vs replay
  (23.5, 12.9); replay calibration gap **247.7°**, `moveMagnitudeRatio` **4.42×**). Two faithful, deterministic
  analyses of identical frames disagree wildly — direct proof of Layer-1 sensitivity, amplified by Layer 0.
- **R² ≈ 1.00 everywhere is false confidence:** every region in every step fit at R² 0.96–1.00. The curves are
  smooth; the *minima* are unstable because they rest on ~5 stars. Curve-fit quality is **not** the problem.
- **One corner drives each vector:** replay Baseline corners were TL 566, TR 575, BL 565, **BR 602** → that lone
  off corner produces A≈23, B≈13.
- **Side finding:** the `TestApp tilt` validator does not reproduce the live wizard's numbers (different
  detection settings/fit path). The tooling is not faithful enough to validate wizard changes (Layer-0 star
  poverty makes the two diverge). The Step 3 fix — one shared robust estimator — resolves this too.

## Answer: should there be ≥6 focus points instead of 5?

**Raw point count is not the binding constraint here — points-with-stars-near-focus is.** This run already used
**7–8** positions, but only ~3 carried usable stars. The per-star paraboloid's **5-detection minimum** can't be
met when stars are detectable in only ~3 positions, so a 6th *coarse* point (at a 0–1-star wing) barely helps.
The real lever is a **finer step near focus** so more positions land in the detectable window:
- At step 175, ~3 of 7 positions have stars. A step of ~**80–100** would put ~5–6 positions in the usable range,
  letting stars reach the 5-detection threshold and giving each corner region a real curve.
- Keep ≥6 positions as a floor, but **prioritize step size and a star-rich field over raw count.**

## Remediation (prioritized; detail in the plan)

1. **Operational, do tonight (biggest immediate win):** calibrate on a **star-rich field**; use a **finer step**
   (~80–100) so ≥5 positions carry stars; raise `MeasurementAverageCount` (e.g. 3) understanding it fights
   *fit* noise not *drift*; add thermal-settle + focuser-backlash before each step and minimize time between a
   screw step and its re-baseline to suppress Layer-3 drift.
2. **Structural (highest accuracy):** calibrate from the per-star paraboloid tilt `Gx/Gy` (robust, curvature-aware,
   identifiable, already computed and discarded) instead of the 4-corner OLS — mostly wiring + a units
   conversion; bound/regularize curvature so a junk value can't inflate the tilt. One shared estimator for wizard
   + live guidance + the validator.
3. **Guards + confidence:** propagate the existing `ThetaStdError` into a per-step σ → a **calibration SNR** and
   predicted screw-angle uncertainty; make the drift/angle guards prominent and **gate "Apply"** on low SNR /
   high drift.
4. **Validation tooling:** bootstrap the per-region/per-star points to report an A/B uncertainty + SNR from a
   single run; dump per-corner residuals and per-star plane-fit residuals; make `TestApp tilt` faithful to the
   wizard; run a bank-wide repeatability pass.

## Verification status

- Built TestApp (Windows dotnet); fixed a path-portability bug in `TiltCalibrationMetadata.ResolveStepFolder`
  that blocked replay of runs captured under a different drive.
- Ran `TestApp tilt` (3× — deterministic) and `TestApp focus-sweep` on all 6 runs; all numbers above are measured.

## Implemented so far (verified)

- **Calibration confidence / SNR gate (Step 4):** `TiltCalibrationCalculator.ComputeConfidence` derives a
  signal-to-noise (screw-move signal vs the AllInward-piston + re-baseline-drift noise probes), surfaced in the
  `TestApp tilt` report + verdict and as a wizard warning (`HasConfidenceWarning`, DataTemplates.xaml). On this
  run it correctly reports **SNR 0.73–0.81 (< 2) → confidence FAIL**, ±~51–54° predicted screw-direction error,
  with a "re-capture on a star-rich field with a finer step; do not apply" note. Unit-tested incl. a regression
  lock on this run's values.
- **Structural foundation (Step 3):** `TiltScrewGeometry.PhysicalGradientToPlane` (exact inverse of
  `PlaneGradientToPhysical`) converts the robust per-star paraboloid tilt `Gx/Gy` into the `(A,B)` units the
  calibration math consumes — so the wizard can be switched to the better estimator without changing downstream
  geometry. Round-trip unit-tested. (Live rewire deferred until a star-rich run exists to validate it end-to-end.)
- Full suite green: **1623 passed, 0 failed**.

## Recommended next acquisition (no code needed)

Re-run one calibration on a **star-rich field** with a **finer step (~80–100)**, thermally settled, screws turned
the same amount. This is the highest-leverage action and produces the star-rich dataset needed to validate the
structural estimator swap (Step 3 live wiring) and the bootstrap tooling (Step 5).
