# Tilt-Adapter Calibration — Error Bounds & Reproducibility (RESOLVED)

**Subject calibration:** `D:\Tilt Calibration Bank\astrodet_6_2\TiltCalibration_20260705_120026`
(3-screw, screws mode, ASI2600MC Duo 6248×4176 RGGB, nominal pitch 400 µm/turn, screw 1 physically 60°)
**Wizard result (Default profile, "use captured/stored settings"):** screw 1 = 67.1°, pitch = 418.7 µm/turn.
**Repro:** `scratchpad/wizard_bounds.py` reconstructs the wizard exactly from the NINA replay log
(`20260705-073652-3.3.0.1048…`), which records the per-step sensor-model fits.

## TL;DR

- The wizard's calibration is **reconstructed exactly** (67.10° / 418.7 µm) from the per-step paraboloid
  Gx/Gy in the NINA log — so the wizard's numbers are fully explained and **correct**.
- **Error bounds on the wizard's actual calibration:** screw angle **67.1° ± ~3.6°** (internal spacing) up to
  **±25°** (the wizard's own all-inward noise model, SNR 2.16); **pitch 419 ± ~43 µm/turn (±10%)**; curvature
  sign −1, robust. It is a **reliable** calibration (move ratio 1.14, near-ideal 112.8° spacing) — unlike the
  stale saved `metadata.json` (ratio 5.48), which my first analysis wrongly used.
- The headless `TestApp tilt` validator **cannot reproduce the wizard** because its detection differs
  (raw-Bayer / imperfect-debayer + independent RANSAC → different Gx/Gy). **Debayering was a red herring.**
  The right move is to consume the wizard's logged tilt planes, not re-detect headlessly.

## 1. How the wizard's numbers arise (from the NINA log)

The replay logs, per step, `Building Sensor Model … Image size (6248×4176)`, RANSAC per-frame star counts,
and `Solved surface model: {Gx, Gy, …}. Stars: N`. The six steps in order give:

| step | Gx | Gy | stars | GoD |
|---|---|---|---|---|
| Baseline | −1.429e-4 | 4.078e-4 | 707 | 0.95 |
| AllInward | −2.931e-4 | 2.959e-4 | 696 | 0.93 |
| ReBaseline1 | (= Baseline, reused frames) | | 707 | |
| Screw1 | 2.213e-4 | 2.169e-4 | 685 | 0.87 |
| ReBaseline2 | (= Baseline) | | 707 | |
| Screw2 | −1.636e-4 | 9.125e-4 | 616 | 0.97 |

Feeding these through the calibrator reproduces **screw1 = 67.10°, pitch = 418.7 µm/turn** — the UI values.

### The `MicronsPerFocuserStep = 0.26` subtlety (not a calibration bug, but a profile misconfig)

The log shows the sensor model ran with `Focuser Size (0.26)` — your Default profile's inspector
`MicronsPerFocuserStep` — not the 3.6 µm the metadata/UI show. This **cancels in the tilt plane**
(A = Gx·W/f; Gx already scales with f), so the plane and the fStep-independent **screw angles are correct**,
and hardware recovery (which uses the real 3.6 µm) yields the correct pitch. Confirmation: a *consistent*
0.26 would give an absurd ~30 µm/turn; 3.6 gives 418.7 ≈ the 400 nominal, so 3.6 is the true focuser step and
**0.26 is a stale inspector value**. It does **not** corrupt the tilt calibration, but it *would* corrupt any
inspector display that uses focuserSizeMicrons non-cancelling (tilt-effect µm, backfocus µm, curvature radius)
— worth fixing separately.

## 2. Error bounds on the wizard's actual calculated values

| value | estimate | 1σ bound | basis |
|---|---|---|---|
| Screw 1 / 2 / 3 | 67.1 / 187.1 / 307.1° | **± 3.6°** … **± 25°** | equal-spacing residual (−7.2° off 120°) … all-inward noise model |
| Pitch | 419 µm/turn | **± 43 (±10%)** | two screws imply 376 vs 462 µm/turn (ratio 1.23) |
| Curvature sign | −1 | robust | Z0 margin −25 µm |

- **Reliability:** signal 32.7 vs all-inward residual 15.2 → **SNR 2.16** (just above the 2.0 "reliable" gate),
  so the wizard shows no low-confidence warning, but its own predicted screw-direction σ is **±25°**. The
  tight spacing bound (±3.6°) and the noise bound (±25°) bracket the real error: the physical **60°** is
  **+7.1°** from 67.1° — 2.0σ on the tight bound, 0.3σ on the noise bound. So the angle is good to **~±7°**
  realistically, not ±3.6°.
- **Pitch** 418.7 is **+4.7% vs the 400 nominal** — comfortably inside the ±10% bound. Trustworthy to ~±10–15%.

These supersede every earlier bound in this doc's history: the first (±16.8°/±65%) used stale metadata; the
"4-corner 301 µm" and "paraboloid 77°/380" were headless estimators that don't match the wizard.

## 3. Why headless reproduction failed (and the real validator fix)

`TestApp tilt` re-detects the frames headlessly and runs the sensor model with `image:null`. Its Gx/Gy differ
from the app's (raw-Bayer vs debayered detection, plus independent RANSAC), and the per-star fit is unstable
headless (paraboloid swung 77°→27° between raw and my debayer). Adding `--debayer` (implemented, opt-in) made
detection use luminance but did **not** converge — debayering was not the difference. **The faithful path is
to consume the wizard's logged tilt planes** (as done here), or to drive the full app image/sensor-model
pipeline. The `--debayer` flag remains a partial, correct improvement (matches the app's detection
representation) but is not sufficient alone and is incomplete (skips the CFA hot-pixel step).

## 4. Recommendation for the UI

- **Surface the wizard's own uncertainty** (`Confidence.PredictedAngleUncertaintyDeg`, here ±25°, and SNR
  2.16) next to the result — always, not only on the SNR<2 warning. It is honest and already computed.
- **Show pitch as ±~10%** and gate its reliability on the **physical** delta ratio (1.23 here), not the (A,B)
  ratio.
- **Persist the confidence block + the focuser-step actually used** into `metadata.json`, so a replay under a
  profile with a stale `MicronsPerFocuserStep` (the 0.26 here) is detectable.
- **Fix the validator** to consume logged/stored tilt planes (or the full pipeline) rather than re-detect — it
  currently cannot reproduce the wizard on bayered runs.

## 5. Saved `metadata.json` calibration ≠ replay — different inputs, not a math bug

The saved `calibration` block (screw1 **65.99°**, pitch **352**, move ratio **5.48**, SNR **1.05 = unreliable**)
differs sharply from the replay (**67.1° / 418.7**, ratio **1.14**, SNR **2.16 = reliable**). This is **not a
calculation bug** — the calibrator reproduces the replay exactly (§1). It is **different persisted metadata /
replay conditions**:

- **The move-magnitude ratio is focuser-step-independent** (f cancels in `m1/m2`), so 5.48 vs 1.14 is a genuine
  **detection/estimator difference** between capture and replay — the two runs fit the saved frames
  *differently*. The saved `perStep` was therefore produced under a different measurement context (detection
  state / sensor-model behavior / possibly an older build) than a replay applies.
- metadata.json persists geometry + the optimized detection settings, but the saved `perStep`/`calibration` is
  only a **historical snapshot** ("stored for reference; re-derived from the per-step readings"). A replay does
  **not** reuse it — it re-measures the sensor-model tilt from the frames using **current** profile/inspector
  state, so it legitimately diverges.
- Net: **do not trust the saved `calibration` block** — here it captured an *unreliable* run (ratio 5.48). The
  saved run is not reproducible from the stored artifacts, which is the gap §6 closes.

## 6. Replay reproducibility — measurement context to persist (design extension)

**Problem.** `SensorModel.UpdateModel` reads `InspectorOptions.MicronsPerFocuserStep` (`InspectorVM.cs:578`, the
stale **0.26** here) and `profile.TelescopeSettings.FocalRatio` (`:592`) from **live state**, and the AF
curve-fit + sensor-model fit read more live options. metadata.json persists none of these, so a replay under a
different profile reproduces neither the saved calibration nor a prior replay.

**Fix.** Add a `MeasurementContext` block to `TiltCalibrationMetadata`, captured at save time from the values
actually used, and have replay **apply it transiently** (like the existing capture-time detection-settings
override — never mutating the profile). Then in-app replay and `TestApp tilt` both become deterministic and
faithful.

**Fields to persist** (verify the complete set against the measurement path at implementation):

| field | source today | why it matters |
|---|---|---|
| `MicronsPerFocuserStep` | `InspectorOptions` (`:578`) | sensor-model Z scale; must equal the real focuser step |
| `FocalRatio` | profile (`:592`) | sensor-model + tilt-angle |
| `FocalLengthMm` | profile | detection pixel scale |
| `UseRANSAC`, `FixedSensorCenter`, `AstigmaticCurvatureEnabled`, `AcceptableRSquaredMin` | `InspectorOptions` | paraboloid registration/fit |
| `WeightedHyperbolicFitEnabled`, `MaxOutlierRejections`, `OutlierRejectionConfidence`, `HyperbolicFitModel` | `AutoFocusOptions` | per-region focus minima |
| `SensorROI`, `CornersROI` | `InspectorOptions` | region geometry (4-corner path) |
| *(already persisted)* screws/type, pitch/step, radius, pixel, focuserStep, appliedAmount, measurementAverage, `OptimizedStarDetectionSettings`, `runStepMapping` | metadata | geometry + detection |

**Also:** persist the computed `Confidence` block and a `SchemaVersion` bump; on replay, if the current
profile's `MicronsPerFocuserStep`/`FocalRatio` differ from the persisted context, **warn** that the replay used
captured settings (so the 0.26-style drift is visible). This is what lets a saved run be re-scored honestly.

## 7. UI confidence surfacing — what a spec needs

Expands §4. To write the spec we need these **decisions** (yours) and **prerequisites** (code):

**Decisions**
1. *Which metrics* to show: SNR + reliable/unreliable; `PredictedAngleUncertaintyDeg` (±25° here); per-screw
   angle ±; pitch ±. All, or a subset?
2. *When*: always, or only the existing SNR<2 warning? (Recommend: always show a compact confidence line;
   escalate to the red warning at SNR<2.)
3. *Where* in the "Calibration Complete" panel: a summary confidence row, a ± next to each screw angle, a ±
   next to the pitch, or a combination?
4. *Visual treatment + thresholds*: green/amber/red keyed to SNR (and/or predicted σ)? Threshold values +
   wording.
5. *Apply gating*: keep the hard "do not apply" at SNR<2; add a softer caution band above it?
6. *Pitch reliability metric*: switch the gate to the **physical** delta ratio (delta1/delta2 = 1.23 here)
   instead of the (A,B) `MoveMagnitudeRatio` (which under-reports it)?

**Prerequisites (code the spec depends on)**
- Compute a **pitch ±** from delta1/delta2 (new — today only the (A,B) ratio exists).
- Decide per-screw angle ± vs a single global `PredictedAngleUncertaintyDeg`.
- **Persist the `Confidence` block** in metadata.json (§6) so saved/replayed runs carry it.
- Bind new fields in `Resources`/`TiltAdapterWizard/DataTemplates.xaml`.
- Optional: the focuser-step-drift warning from §6.

Once (1)–(6) are decided, this is a self-contained spec + plan.
