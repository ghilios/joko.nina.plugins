# Tilt-Adapter Calibration Noise — Diagnosis & Remediation Plan

## Context

The user ran a 6-step Tilt Adapter Wizard calibration on the already-calibrated **astrodet** profile
(`D:\Tilt Calibration Bank\astrodet_6\TiltCalibration_20260628_115023`) and the result is poor: the sensor
models at the three "baseline" steps are very different even though they should be identical. The user was
careful turning the screws and wants to understand **why** — root cause, from all angles — and specifically
whether the per-star focus curves should use **≥6 points instead of 5**.

**Headline finding (already strongly evidenced by the saved data + code):** the calibration is
**noise-limited — the measurement noise floor is as large as the screw-move signal (SNR ≈ 1)**. The screws are
not the problem; the data confirms the two turns were clean. The problem is upstream, in how the per-step tilt
vector is measured. There are **two distinct noise sources** that must be separated: (a) a structurally fragile
tilt-plane *estimator*, and (b) real *between-step physical drift* (thermal / focuser backlash / adapter creep)
that contaminates a measurement built entirely from differences taken ~90–180 s apart.

The 6 steps are: **Baseline → AllInward → ReBaseline1 → Screw1 → ReBaseline2 → Screw2**. The "3 baselines" are
Baseline / ReBaseline1 / ReBaseline2 — all the *same* neutral screw state, so they must measure the same tilt.

---

## Root-cause diagnosis (the "why")

### Evidence the calibration is noise-dominated (from this one run's `metadata.json`)

| Check | Expected | Measured | Verdict |
|---|---|---|---|
| 3 baselines (identical neutral state) | same (A,B) | RMS scatter **±8.0** units; tilt angle 0.089°→0.316° | noise floor ≈ 8 |
| **AllInward** = all screws in equally = pure piston → **zero tilt change** | ~0 | **20.6** units of tilt change | null-move noise ≈ 20 |
| Two screws are physically **120° apart** | 120° apart | **63.7°** apart (`rawAngleDiffDegrees`) | 56° error = 1.7σ of predicted ±33° |
| Screw-move **signal** | — | **13.9** units (both screws) | **≈ the noise → SNR ≈ 1** |
| `moveMagnitudeRatio` (turn evenness) | ~1.0 | **1.0016** | turns were *clean* — user's care confirmed |

Predicted vs observed angle error lines up exactly: transverse σ ≈ 5.7 on a 13.9-unit vector ⇒ per-move
direction σ ≈ 23°, ⇒ ±33° on the screw-to-screw difference. Measured 63.7° (vs 120°) is 1.7σ — fully explained
by noise. **No amount of careful screw-turning fixes an SNR≈1 measurement.**

Corroborating signatures:
- **Curvature term is fitting noise, not optics.** Across the 3 baselines `curvatureRadiusMillimeters` =
  56 / 713 / 1112 mm (20× swing, sign flip); Baseline reports **+17,279 µm** curvature at screw radius — **4.6× the
  entire focuser sweep** (~3,760 µm). Real Petzval curvature is constant; this is the unbounded paraboloid fit
  absorbing outliers.
- **Run-to-run repeatability is poor at the profile level:** applied `astrodet.tilt.json` (4 runs, 2026-06-13)
  used `screwThreadPitchMicrons` = 400; this run *measured* 453 — a 13% drift in a fixed mechanical constant.
- **The wizard's own guards already fire** (advisory only): re-baseline drift **134%** and **74%** of the screw
  move (threshold 50%, `EvaluateRebaselineDrift`, `TiltAdapterWizardVM.cs:1186-1208`); angle-gap deviation 56°
  (threshold 30°, `ValidateCalibrationQuality`, `:1161-1182`). `MeasurementAverageCount` = **1** disables the
  repeatability guard entirely.

### Mechanism — why the per-step tilt vector (A,B) is so noisy

The entire screw calibration is derived purely from how the tilt vector **(A,B)** changes between steps
(`TiltCalibrationCalculator.Calibrate/ComputeScrewAngles`, `TiltCalibrationCalculator.cs:118-219`: each screw =
`Screw_i − ReBaseline_i`, then `atan2(dA,−dB)`). That (A,B) is fragile by construction:

1. **4-corner OLS, 1 degree of freedom, zero robustness** — `TiltPlaneModel.Create`
   (`AutoFocus/TiltModel.cs:132-165`) fits a 3-parameter plane (A, B, intercept) through **only 4 corner-region
   best-focus positions** (center region excluded), unweighted, no outlier rejection, **no curvature term**. Each
   corner is one region's hyperbola minimum aggregating that quadrant's stars. With 4 points for 3 params, any
   per-corner focus error propagates ~1:1 into A/B, and there is nothing to average it down.
2. **Two models that disagree and aren't cross-checked.** The metadata mixes outputs of *two independent fits*:
   `tiltPlaneA/B` from the 4-corner OLS above, but `curvatureRadius…/curvatureEffect…` from a **separate per-star
   tilted-paraboloid** (`Inspection/SensorParaboloidModel.cs` + `SensorModel.cs`). The paraboloid *does* have
   robust winsorized rejection and per-star σ weighting — but its **curvature is totally unbounded/unregularized**
   (`SensorParaboloidModel.cs:398-418`), its quadratic column is ill-conditioned (~1e-6 coeff over ~1e7-scale x²),
   and its **free center (X0,Y0) is confounded with the tilt gradient** (non-identifiable; the solver returns a
   null covariance for exactly this case, `NonLinearLeastSquaresSolver.cs:452-456`). So the rich, robust model is
   *not* what the wizard calibrates from, and the live Inspector guidance (which *does* use the paraboloid Gx/Gy)
   can diverge from the calibration.
3. **Differencing doubles the variance.** Each screw signal subtracts two independent noisy plane fits; with
   `MeasurementAverageCount = 1` nothing averages it down.
4. **Real, time-correlated between-step drift.** Mean focuser position across the three baselines drifts
   **monotonically** 591 → 599.6 → 609.8 (~67 µm over 8 min) — that is *not* random fit noise (random noise isn't
   monotonic); it is thermal / focuser-backlash / tilt-adapter creep / seeing. Because every screw move is a
   *difference taken ~90–180 s apart*, this non-stationarity lands directly in the signal. **This is why
   simply averaging more frames per step will not fix it** (longer steps → more drift between the differenced
   steps).

### Process layer
The wizard detected the failure (drift + angle warnings) but the warnings are advisory; calibration proceeds and
**snaps the noisy 63.7° onto the ideal 120° lattice** (`ComputeScrewAngles`, `:126-130`), emitting
confident-looking screw angles built on SNR≈1 data — which then drive every later screw-turn recommendation.

---

## Answer: should there be ≥6 focus points instead of 5?

**Yes as a floor — but it is not what made *this* run noisy** (this run already used **7–8** focuser positions,
step 175). Precise picture:

- The **calibration-critical tilt plane** uses **region** curves that need only **3 points** and pool all stars
  in a quadrant (`AutoFocusEngine.cs:112`), so raw focuser-point count is *not* its bottleneck.
- The **per-star paraboloid** (curvature) has a hard **min-5-matched-detections-per-star** guard
  (`SensorModel.cs:624,660`). At exactly 5 positions a star must be detected in *every* frame to qualify; one
  defocus dropout drops it from the model. **≥6 positions lets stars survive a dropout** → more stars, more
  stable curvature. So ≥6 (ideally 7–9) is good practice, and step 175 (~23× the critical focus zone) is coarse
  near focus — denser near-focus sampling helps per-curve vertex precision more than raw count does.
- Net: keep ≥6 as a floor, but the dominant levers for *this* failure are the structural fit, averaging where
  drift permits, and killing the between-step drift — not 5→6.

---

## Execution plan

> Plan-mode note: Step 1 needs a Windows build, so it runs first on execution. Deliverables get filed per project
> convention: the diagnosis as a **design spec** `docs/tilt-calibration-noise-design.md`, this plan as
> `plans/tilt-calibration-noise-plan.md`.

### Step 1 — Empirically confirm the diagnosis (replay this saved run)

Build TestApp via Windows `dotnet.exe` (per the WSL build note), then:

> **Done:** fixed a path-portability bug that blocked replay — `TiltCalibrationMetadata.ResolveStepFolder` honored
> stored *absolute* run-folder paths as-is and never checked existence, so this run (captured under `H:\…`, now on
> `D:\…`) was unreplayable. It now rebases a missing absolute path's tail onto the selected run root (fixes both
> offline TestApp and in-app wizard replay). Belongs to Step 5's tooling hardening.

1. **Tilt replay** — confirms per-step (A,B) match metadata, exposes per-corner fit quality:
   ```
   TestApp.exe tilt --dataset "D:\Tilt Calibration Bank\astrodet_6\TiltCalibration_20260628_115023"
   ```
   Inspect `tilt_summary.json`: per-step `regionRSquared` (C/TL/TR/BL/BR) and `regionPositions`, calibrated
   angles, recovered pitch, PASS/FAIL. **Look for:** corners with low R² or sparse stars in specific steps =
   where the 4-corner plane is being corrupted. (`TestApp/TiltCalibrationRunner.cs`.)
2. **Focus-sweep per run** — quantifies dropout/sparsity behind each region curve:
   ```
   TestApp.exe focus-sweep --dataset <each of the 6 AutoFocus_* run folders>
   ```
   Shows star count + HFR spread + rejection reasons vs focuser position. **Look for:** star count collapsing at
   the defocused extremes and any quadrant that is chronically star-poor.
3. Record the measured numbers; separate the **fit-noise** component (per-corner R², star counts) from the
   **drift** component (monotonic mean-focus march, the 134%/74% rebaseline drift). The random fit-noise σ is
   quantified by the bootstrap added in Step 5 (replay alone is deterministic).

### Step 2 — Write the diagnosis design doc

`docs/tilt-calibration-noise-design.md`: everything above + the Step-1 measured numbers. This is the primary
"understand why" deliverable.

### Step 3 — Structural: calibrate from the sensor model's tilt term *(highest accuracy win)*

**Key realization (validated against code): the robust tilt estimate already exists and is computed on every
measurement — the wizard just throws it away.** The per-star `SensorParaboloidModel` carries a tilt gradient
`Gx/Gy` (`SensorParaboloidModel.cs:162-166, 286-288`) fit over *all* matched stars with winsorized outlier
rejection + per-star 1/σ² weighting and an explicit curvature term, and — because `FixedSensorCenter` defaults
**true** (`InspectorOptions.cs:66,98`) — its center is pinned so `Gx/Gy` is a clean, identifiable tilt-at-center
(the tilt↔center confounding only bites when the center floats). It is computed during each wizard step but only
its *curvature* is harvested (`PopulateCurvature`, `TiltAdapterWizardVM.cs:946-951`); calibration instead reads
the crude 4-corner `TiltModel` (`:860-862`).

So the fix is mostly **wiring + a units conversion**, not new math:
- Have the wizard calibrate from the paraboloid tilt gradient (`Gx/Gy`) instead of `TiltPlaneModel` 4-corner A/B
  (`TiltAdapterWizardVM.cs:855-880`). Convert `Gx` (focuser-µm per sensor-µm) → the calibration's `A/B`
  (focuser-steps per normalized [-0.5,0.5] coord) — a linear scale by sensor width/height, pixel size, focuser
  step; or refactor `TiltCalibrationCalculator` to take a physical gradient directly so wizard + live guidance
  share one quantity. Keep the 4-corner value as a cross-check/fallback when the paraboloid is unavailable
  (needs ≥9 pts / ≥10 stars).
- **Bound/regularize curvature** `K` so a non-physical value (we saw 56 mm radius) can't inflate `Gx` variance
  when stars are clustered/few (`SensorParaboloidModel.cs:398-430`); ensure the paraboloid is actually computed
  during the wizard (it was for this run; make it unconditional for calibration, independent of the display
  `SensorCurveModelEnabled` toggle).
- Reuse existing machinery: `SolveWinsorizedResiduals`, per-star σ weighting, `RejectionTest`, `MedianMAD`.
- Fix the minor winsor-band-centered-on-zero bug (`NonLinearLeastSquaresSolver.cs:124-135`).
- Key files: `TiltAdapterWizard/TiltAdapterWizardVM.cs`, `TiltAdapterWizard/TiltCalibrationCalculator.cs`,
  `Inspection/SensorModel.cs`, `Inspection/SensorParaboloidModel.cs`, `Utility/NonLinearLeastSquaresSolver.cs`,
  `AutoFocus/TiltModel.cs` (fallback/cross-check).
- **Honest scope:** this removes the *estimator-noise* component only; the between-step *drift* component
  (Step 6) is orthogonal and unaffected by a better fit.

### Step 4 — Actionable guards + a calibration-confidence number

- The paraboloid **already computes `ThetaStdError`** (1σ tilt-angle SE via the delta method from the fit
  covariance, `SensorParaboloidModel.cs:198,222-248`). Propagate it into a **per-step (A,B) σ** → a **calibration
  SNR** (signal = screw-move magnitude, noise = combined per-step σ) and a **predicted screw-angle uncertainty**;
  surface these in the wizard summary. Largely consume-what-exists.
- Make the existing advisory guards (`ValidateCalibrationQuality`, `EvaluateRebaselineDrift`) **prominent and
  gate "Apply"** when drift ratio > 0.5 or SNR below threshold; recommend re-running. (`TiltAdapterWizardVM.cs:1161-1208`,
  plus the wizard XAML.)

### Step 5 — Validation tooling (so every change is measurable)

Extend `TestApp/TiltCalibrationRunner.cs`:
- **Bootstrap** the per-region/per-star points and refit to report an A/B uncertainty + calibration SNR from a
  single dataset (this is what gives the random fit-noise σ that deterministic replay can't).
- Dump **per-corner residuals**, per-region R², and (new) **per-star plane-fit residuals** (currently no per-star
  dump exists in TestApp).
- A bank-wide repeatability pass across the other runs (`astrodet`, `cwhite`, the four `2026-06-21` runs) to
  characterize the noise floor generally and as a regression baseline.

### Step 6 — Operational / acquisition knobs *(empirically the biggest immediate win)*

Focus-sweep showed the dataset is **star-poor** (peak ~23–38 stars whole-sensor; only ~3 of 7–8 positions carry
>5 stars; wings 0–1) — the soil under all the noise. The user can act on this with **no code change**:
- **Star-rich field + detection sensitivity:** point at a denser field for calibration; the per-corner fit needs
  many stars per quadrant. Re-tune detection for more survivors if the field can't be richer.
- **Finer step near focus:** at step 175 only ~3 positions have stars. Step ~**80–100** puts ~5–6 positions in
  the detectable window → stars reach the per-star 5-detection minimum, regions get real curves. **This is the
  true content of the "≥6 points" question** — points-with-stars, governed by step size, not raw count.
- **Averaging with the drift caveat:** raise `MeasurementAverageCount` (e.g. 3) — beats *fit* noise, not *drift*.
- **Kill drift:** thermal-settle + focuser-backlash move before each step; minimize time between a screw step and
  its re-baseline; optionally drift-correct each screw move using its bracketing rebaselines.
- Any new persisted option needs a control in `Resources/OptionsDataTemplates.xaml` (project invariant).

> **Recommendation surfaced by Step 1:** re-acquire one calibration on a star-rich field with a finer step
> *before* investing in the structural refactor — it may largely resolve the problem, and a good run is the test
> data needed to validate Steps 3–5 against.

---

## Verification

- **Empirical (Step 1):** replay reproduces the metadata per-step (A,B); per-region R² / focus-sweep reveal which
  corners/steps are weak and confirm extreme-defocus dropout.
- **Step 3/5:** the bootstrap SNR on this run should rise materially after the robust fit; re-deriving the screw
  angles should move the 63.7° gap toward 120° (or honestly report that this dataset is too noisy to calibrate).
- **Guards (Step 4):** this run must be flagged (and "Apply" gated) by the new drift/SNR checks.
- **Regression:** `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` must stay green after
  every code change (project invariant). Bank-wide repeatability pass (Step 5) before/after the structural change.
