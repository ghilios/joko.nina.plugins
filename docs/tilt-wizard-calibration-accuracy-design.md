# Tilt Wizard Calibration Accuracy — Simulated End-to-End Run Investigation

**Run analyzed:** `D:\TiltAdapterTesting\TiltCalibration_20260718_141421` (2026-07-18 14:14–14:51, NINA log
`20260718-141219-3.3.0.1048.29076-202607.log`, TRACE). Simulated camera (61 MP, 9576×6388 @ 3.76 µm, focuser
0.26 µm/step) + simulated tilt adapter (4 screws @ 34/124/214/304°, steppers 1.8 µm/step, R = 55 mm, injected
tilt 0.064° @ 82, backfocus −23.7 µm, excursion 150 steps).

**Symptom:** wizard stored screw angles 41.32/131.32/221.32/311.32° (true 34/124/214/304 → **+7.32° error**),
pitch 2.0394 µm/step (true 1.8 → **+13.3%**), raw angle gap 54.33° (expected 90), SNR 4.12, and it fired the
"angle gap … expected ~90°, consider recalibrating" warning — yet reported `confidenceIsReliable = true`.

## Method

Five independent evidence streams, cross-checked:

1. **Solver code** (`TiltCalibrationCalculator.cs` + callers) — every stored number reproduced from the six
   sensor-model fits to 4+ decimals.
2. **Simulator code** (`CameraSimulator/TiltAdapter/*`, `Rendering/*`) — injection math, conventions, determinism.
3. **NINA log timeline** — every move command, motor shadow position, AF frame, RANSAC and fit line.
4. **Independent numeric reconstruction** (Python; `verify_tilt_calibration.py`) — formula identification and
   error decomposition.
5. **Physical frame ground truth** — detector-independent per-ROI V-curves over all 109 saved FITS frames
   (flux²-weighted centroid lever arms, saturation-masked), fitting the focus plane each run actually encoded.

## Findings (ranked by contribution)

### F1 — The per-star sensor-model (paraboloid) fit stage is the dominant error source. The simulator and frames are essentially perfect.

Physical ground truth from the saved frames:

| Quantity | True | Physically in frames | Wizard's per-star fit read |
|---|---|---|---|
| Diagonal-A response | 0.0049091 @ 34° | 0.004994 @ 34.2° (101.7%) | 0.006539 @ 48.17° (**133.2%, +14.2°**) |
| Diagonal-B response | 0.0049091 @ 124° | 0.004944 @ 124.7° (100.7%) | 0.004585 @ 123.08° (93.4%, −0.9°) |
| Gap between moves | 90° | 90.5° | 74.9° raw (54.3° reported, see F2) |
| Magnitude ratio | 1.0 | 1.010 | 1.43 raw (1.355 reported) |
| Baseline repeatability | 0 | ≤ 0.012° | 0.065–0.098° scatter |

- Motor moves were commanded and applied exactly (log shadow strings decode to the intended (1,3)/(2,4)
  diagonals; the fold-state writes show per-move gradient deltas of exactly 4.909091e-3 @ 34.000°/124.000°,
  90.000° apart; restores round-trip to 1e-14; the final restore **was** applied at 14:50:49).
- The three baseline-state AF runs are physically identical to ≤ 3.8 µm per corner, yet the per-star paraboloid
  fits of those same states scattered by 0.065–0.098° — 5–8× the physical repeatability, and the same order as
  the injected tilt itself. This scatter is the wizard's own noise estimate (SNR 4.12 = 683/165.6, reproduced
  exactly).
- The **Screw1 (diagonal-A) fit is the single dominant anomaly**: +33% magnitude, +14.2° rotation, star count
  2901 vs 3801–3886 elsewhere, curvature K sign flipped (+1.37e-7 vs physical ≈ −5e-8), RMS 16.75,
  reduced χ² 146. Its error vector is 0.123° of tilt — ~1.9× the injected tilt magnitude.
- Decisive isolation: the wizard's **own per-region hyperbolic AF fits on the same frames recover the truth**
  (Screw1 delta az 34.15° @ 100.8%, Screw2 az 121.8° @ 98.1%, R² > 0.999). The frames are fine; star detection
  per frame is fine; the per-star paraboloid fit stage is where the numbers go wrong.

Error decomposition (exact, sums to the stored values):

- Screw-angle error +7.323° = **+7.085° Screw1 fit anomaly** + 0.698° anisotropic-angle bug (F2) − 0.460° Screw2.
- Pitch error +13.30% = **+16.60% Screw1** − 3.30% Screw2 + 0.00% anisotropy (pitch math is isotropic-correct).

Mechanism inside the Screw1 fit: **proven by instrumented replay** — see "Root cause of the per-star fit
failure" below. (The initially-suspected corner-star-dropout mechanism was refuted: detection totals are
identical across runs; the 25% star loss happens entirely inside the paraboloid solve's outlier trimming.)

### F1a — Root cause of the per-star fit failure (instrumented replay of the saved dataset)

`TestApp tilt --dataset` reproduces every live fit to 8–9 significant digits (fully deterministic), so the
paraboloid stage was instrumented (opt-in per-iteration snapshots in `NonLinearLeastSquaresSolver` + per-star
CSV dumps via a static seam in `SensorModel`; observation-only, fits verified bit-identical) and the dumped
data refit offline against the known truth surfaces. Proven causal chain, one number per link:

1. **The per-star best-focus estimates are NOT biased.** Truth residuals of the 4010 Screw1 data points are
   flat to <0.6 µm across x, y, radius, defocus, and model family; plain unweighted OLS on them recovers the
   true gradient to 0.0024° (and 0.0005–0.0032° on every other step). The per-star estimator is fine.
2. **The per-star σ estimates are wildly miscalibrated.** `MinimumStdError` goes as low as 0.008 µm against a
   ~2.5 µm true error floor (up to ~300× understated). With weights 1/σ², the effective sample size of the
   Screw1 solve is **1.8 of 4010** — one star holds 72.6% of the total weight, the top three hold 90.8%.
3. **The seed solve is hoisted by a lever star** (a 970σ truth-outlier holding 4.8% of total weight at
   (+6.9, +6.6) mm) → seed Gx +8.8e-4 / K +3.8e-8 above truth, and a skewed *unweighted* residual field
   (median +6.86 µm) because the fit centers itself in weighted space.
4. **`SolveWinsorizedResiduals` clips the wrong quantity around the wrong center**: ±2.5·MAD on *unweighted*
   residuals centered on *zero* (the median is computed but unused). With median +6.86 vs bounds ±29.7 the
   prune is 18:1 one-sided; 97.1% of the 1109 disabled points sit at x≥0, 98.2% on one residual sign.
5. **Runaway feedback**: each prune moves the fit toward the surviving side (Gx +3.9e-4, K +3.3e-8 per pass),
   manufacturing new outliers — measured one-step gain **1.18** (the only step > 1; Screw2's is 0.13, which is
   why Screw2 survived: its lever stars happened to sit near the field center — sampling luck, not physics).
   The Gx↔K covariance (+0.61, pinned-center design) converts the x-sided prune into the K sign flip, and the
   GoF gate never trips because R² is recomputed on the shrinking point set (0.890 → 0.957 while diverging).
   Ten iterations (cap) prune 27.7% of stars: Gx ends 2× truth, K flipped — the wizard's +7.3°/+13.3% errors.
6. **The identical-state scatter (0.065–0.098°, wizard SNR 4.1) is the same disease**: with ESS of a few stars,
   every fit is hostage to which lever stars land where. It is not shot noise and not the simulator.

**Validated fix** (offline refits of all six steps' full point sets):

| rule | mean grad err (deg) | Screw1 err | baseline scatter | screw1 angle | pitch |
|---|---|---|---|---|---|
| current production | 0.0680 | 0.1666 | 0.0934 | 40.62 | 2.039 |
| median-centered clip only | 0.0655 | 0.1480 | 0.0954 | 39.81 | 2.000 |
| Huber IRLS (no hard removal) | 0.0061 | 0.0020 | 0.0173 | 32.72 | 1.788 |
| weighted-residual median clip | 0.0047 | 0.0036 | 0.0176 | 32.62 | 1.788 |
| **σ floor (2 µm, quadrature) + plain WLS** | **0.0011** | 0.0015 | **0.0026** | **33.77** | **1.807** |

Production change (**implemented and verified**): (i) **variance floor** `σ_used² = σ_raw² + (2 µm)²`
(`SensorParaboloidDataPoint.RegularizeStdDev`; insensitive over 1–3 µm; restores ESS to ~2600/4000) — this is
the root cause; (ii) `SolveWinsorizedResiduals` now clips **weighted residuals (r/σ), median-centered**,
worst-first under a 10% total-removal budget, converges on "no points removed", and the cross-point-set GoF
gate is gone. End-to-end replay of this dataset through the fixed pipeline: stored screw angles
34.50/124.50/214.50/304.50 (was 41.32; residual +0.50° is F2's anisotropic-angle bias, unfixed here — the
raw-space responses read 100.75% @ 33.74° and 99.67% @ 123.68°), pitch 1.8038 (was 2.0394), SNR 205.6 (was
4.12), predicted angle uncertainty ±0.28° (was ±13.6°), identical-state scatter 0.0009–0.0030° (was
0.065–0.098°), Screw1 stars kept 3747 (was 2901), and `rawAngleDiffDegrees` 68.93° = exactly the perfect-data
anisotropic-space value.

### F2 — Genuine solver bug: screw directions are computed in anisotropic (A,B) space

`TiltCalibrationCalculator` computes response directions as `atan2(dA, −dB)` where `A = Gx·W/fstep`,
`B = Gy·H/fstep` (`TiltScrewGeometry.PhysicalGradientToPlane`) — i.e. x is scaled by sensor *width* and y by
sensor *height* (36.0 vs 24.0 mm, ratio 1.499). Angles in that space are sheared: on **perfect** data with true
screws at 34/124°, the gap reads 68.91° (not 90°) and `moveMagnitudeRatio` reads 0.865 (not 1.0).

- Downstream consumers (`TiltScrewTargets` → `ScrewCorrectionMicrons` → guidance + motorized planner, and the
  simulator's own inverse) all interpret the stored angles as **physical isotropic image-space** angles — so
  this is a unit mismatch, not a consistent internal convention. No code comment claims it is intentional; the
  calculator's doc comment asserts the convention "matches the rest of the plugin", which is false for W ≠ H.
- Impact here was small on the stored angles (+0.70°) because the 4-screw rigid 90°-fit averages the shear's
  ±sin(2θ) term away; worst case is ±1.14° for 4-screw but **±5.96° for 3-screw adapters** (120° spacing does
  not cancel).
- It structurally breaks the diagnostics: the "expected ~90°" gap check and "expected 1.0" magnitude ratio are
  evaluated against anisotropic values (this run: perfect data would read 68.9°, eating 21° of the 30° warning
  margin — the warning would fire on modest noise even with a perfect measurement stage).
- Related inconsistency: `InspectorVM` (~2019–2037) dots the stored angles against anisotropic `tiltPlane.A/B`
  for the qualitative arrow row while the numeric rows use physical space.

### F3 — The reliability gate accepted a run its own uncertainty estimate had flagged

SNR = mean move magnitude / RMS of three should-be-zero probes (AllInward tilt-residual + two re-baseline
drifts) = 4.12, and `predictedAngleUncertaintyDeg = atan(noise/signal) = 13.6°` — which honestly anticipated
the +7.3° error (0.54 σ). But `confidenceIsReliable` only requires SNR ≥ 2.0, so the run passed. The estimate
was right; the gate was too permissive.

### F4 — Regression at HEAD: the wizard cannot run from zero motor counters anymore

This run executed on the pre-`a8a72d2` build (run 14:14, commit 15:13). At HEAD,
`EatTiltMotionController.ExecuteMoveAsync` throws `TiltDeviceLimitException` for any predicted motor position
< 0 (`EatTiltMotionController.cs:274-276`), and the wizard's diagonal moves drive one motor to −150 from
all-zero counters. The auto-bias planner exists only in the InspectorVM correction path, not the wizard's
direct `ExecuteMoveAsync` path — so the next wizard run on HEAD will fail at the Screw1 step unless the motors
are pre-raised.

### F5 — Simulator convention wrinkle (not causal): injected azimuth is 90° rotated from the screw convention

The injected tilt renders faithfully in magnitude (physical 0.054–0.068° vs 0.064 injected) but the "Tilt
Azimuth 82°" input is treated as the math-convention angle `atan2(Gy,Gx)`; the rendered gradient sits at
compass azimuth ≈ 172° (= 82 + 90), while screw angles use the compass convention directly (physical responses
landed at 34/124 exactly). Harmless for calibration accuracy, but the two conventions in one panel invite
misinterpretation when comparing wizard output against injected values.

Minor UI note: after Auto Run All completes, the banner still reads "−150 diagonal-B (restore). Click Run
Measurement… to apply" although the restore was already applied — stale text.

## Remediation approach

1. **Fix the angle space (small, high-value).** Convert deltas back to raw (Gx,Gy) via
   `PlaneGradientToPhysical` before `atan2` in `ComputeScrewAngles`, and compute `rawAngleDiffDegrees` /
   `moveMagnitudeRatio` there too, so 90°/1.0 expectations hold on any sensor aspect. Fix the InspectorVM arrow
   row to the same space. (On this run that alone moves 41.3 → 40.6; the point is correctness of the
   diagnostics and the 3-screw case.)
2. **Fix the paraboloid solve (root cause — proven, fixed, and replay-verified; see F1a).**
   - Variance floor on the per-star best-focus σ (`σ² = σ_raw² + (≈2 µm)²`) before weighting — ends the
     lever-star weight monopoly that causes both the Screw1 runaway and the identical-state scatter.
   - Rework `SolveWinsorizedResiduals`: clip weighted residuals (r/σ) about their median (or switch to Huber
     IRLS), converge on "no points removed", keep the iteration cap, add a total-removal backstop, drop the
     cross-point-set GoF gate.
   - Still worth doing independently: symmetric ±N excursions (half-difference cancels residual baseline error),
     a region-AF cross-check on the paraboloid response, and gating on `predictedAngleUncertaintyDeg` (≤ ~5°)
     rather than SNR ≥ 2 alone.
3. **Un-break the wizard at HEAD:** pre-bias the wizard sequence (e.g. raise all motors by the excursion before
   the diagonal steps and fold the bias into the backfocus bookkeeping) or route wizard moves through the
   auto-bias planner. Add a wizard-path test from all-zero counters.
4. **Simulator polish:** unify the injected-azimuth convention with the screw-angle compass convention (or label
   the field); optionally expose `DonutRadiusQuantumPixels` — lowering it is also the direct experiment for the
   baseline-scatter mechanism (prediction: identical-state fit scatter drops from ~0.07–0.10° toward ~0.001°).

## Verification data

- Numeric reconstruction script: `verify_tilt_calibration.py` (session scratchpad); reproduces every stored
  calibration number from the six logged fits to 6+ decimals.
- Frame ground-truth: per-ROI V-curve analysis over all 6 runs (saturation-masked flux² metric, centroid lever
  arms; plane residuals ≤ 1.4 µm; two independent ROI rings agree).
- Log anchors: sensor-model fits at log lines 17760/31351/42535/55112/67751/80756; motor shadow writes at
  1691/17962/31553/42798/55315/67975/80965.
