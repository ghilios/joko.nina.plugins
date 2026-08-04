# Tilt Wizard Calibration: Unequal Screw Responses and the Pitch "Non-Linearity" — Root-Cause Analysis

**Run analyzed:** `D:\Tilt Calibration Bank\ghilios_corrected` (2026-08-04 00:15–00:38, 4-screw EAT
steppers, spec 1.8 µm/step, applied 150 steps/move, full-frame IMX455-class sensor 9576×6388 @ 3.76 µm,
screw radius 55 mm, focuser step 0.269 µm, f/6.3, 882 mm).

**Observed symptoms:**
1. The wizard warned *"the two screw turns produced very unequal tilt changes (1.5× apart)"* on a
   run where the motors provably applied identical ±150-step diagonal moves.
2. Empirically across runs, the measured screw pitch ≈ spec (1.864 vs 1.8) when aberrations were
   almost fully corrected, and substantially larger than spec when tilt/backfocus errors were present.

**Conclusion up front:** there is no single non-linearity. Four separable causes stack, and in this run
two of them *cancel* to produce the misleadingly spec-like 1.864 µm/step:

| # | Cause | Effect on this run | Status |
|---|---|---|---|
| 1 | Per-star sensor-model fit underestimates the Screw2-state gradient (×0.80) and the curvature (×0.5) | ratio 1.14→1.52 (fired the warning); pitch avg dragged 2.00→1.86 | **new finding** (survives the robust-weighting fix) |
| 2 | Focuser-frame µm ≠ plate-frame µm on this rig (factor ≈ 1.25–1.30) | every µm readout (pitch, tilt µm, backfocus µm) inflated ~25–30% vs plate µm; honest pitch reads ≈ 2.2–2.3, not 1.8 | **new finding** (piston probe proves it) |
| 3 | State-retention / drift between steps (residuals 43–90 A,B-units vs 600-unit signal) | corner-AF ratio 1.14 ± 0.09 — the true responses are statistically equal | inherent to single-shot ± pattern |
| 4 | F2 anisotropic angle-space bug (known, open) | rawDiff 243.3° displayed vs 265–267° physical (expected 270°) | known |

The user-facing implication (symptom 2) inverts: **the "too large" pitch readings in aberrated runs were
probably the honest ones** (in the focuser frame, the only frame the wizard can measure); the spec-like
1.864 in the corrected run is two errors cancelling. And because the aberration-correction math divides
focuser-frame microns by the configured pitch, **corrections are only self-consistent when the honestly
measured pitch is used — using the spec value on this rig overcorrects tilt by ~25–30%**, and the
sensor-model curvature shrinkage makes backfocus corrections undershoot by ~2× on top of that.

---

## 1. Reconstruction of what the wizard computed

All wizard arithmetic was reproduced exactly from the stored per-step tilt planes
(`metadata.json`, `perStep[]`, A/B in focuser steps per normalized image coordinate):

- Screw1 move (DiagonalA: TR +150, BL −150), model delta vs ReBaseline1: (−586.8, 392.2), |mag| 705.8
- Screw2 move (DiagonalB: TL +150, BR −150), model delta vs ReBaseline2: (+403.8, 229.2), |mag| 464.3
- ratio 705.8/464.3 = **1.520** ✓ (warning threshold 1.5)
- per-screw implied pitch: **2.28 / 1.45 µm/step**, mean 1.864 ✓, `pitchUncertainty` 0.412 ✓
- SNR = 585.1/89.9 = **6.51** ✓, angle uncertainty atan(noise/signal) = **8.74°** ✓
- A,B-space move directions 236.2°/119.6° → rawDiff **243.3°** ✓; constrained fit → screw1 **222.9°** ✓

The reported 1.864 ≈ spec is the average of two individually far-off numbers (2.28 and 1.45).

## 2. Independent estimator: corner-region AF planes

Each step's live AF also produced per-region hyperbolic fits (`autofocus_report_Region2..5.json`,
corner boxes centered at ±1/3 normalized). Fitting tilt planes from the four corner AF vertices
(curvature cancels exactly over symmetric corners; prior e2e work showed region AF reads truth):

| quantity | per-star model | corner AF | corner AF σ |
|---|---|---|---|
| Screw1 delta mag | 705.8 (+6%) | 665.9 | ±34 |
| Screw2 delta mag | 464.3 (**−20%**) | 582.0 | ±29 |
| ratio | 1.52 | **1.14** | ±0.09 |
| implied pitch 1 / 2 | 2.28 / 1.45 | 2.17 / 1.82 | |
| physical move directions | 224.9°/130.4° | 223.5°/130.5° | |
| physical gap | 265.4° | 267.0° | (expected 270°) |

- The model's Screw2-state gradient is a **uniform ×0.80 shrink** (both components ×0.799/0.794,
  direction preserved) — >3σ below the corner estimator. That single error creates most of the 1.52
  warning ratio.
- The corner ratio 1.14 ± 0.09 is statistically compatible with **equal** responses given the run's
  own noise probes (cycle residuals 43.5 and 90.5 units; the two residuals point in unrelated
  directions — 121.9° and 309.8° — so they are settling/hysteresis + drift, not a monotonic thermal
  drift, and not obviously a pull-side spring shortfall either).
- The screw ANGLES came out right despite the F2 bug: the anisotropic distortion (+11°/−11° on the
  two near-diagonal moves) is antisymmetric here and cancels in the constrained equal-spacing fit.
  Casualties are the displayed gap (243° vs 267°), the ratio/SNR metrics computed in A,B space, and
  near-tripping the >30° angle warning (26.7° deviation shown; physically it was 3°).

## 3. The piston probe: focuser microns ≠ plate microns (factor ≈ 1.25–1.30)

The AllInward step commands +150 steps on all four motors = 270 µm plate translation (at spec
1.8 µm/step). The measured mean best-focus moved **1288–1352 focuser steps = 346–364 µm**
(corner-mean vs center; ~333–352 µm after removing the ~−45-step piston drift seen between
re-baselines). Focuser-frame effective pitch:

- piston, center region: **2.34 µm/step**; corner regions: **2.22 µm/step**
- Screw1 tilt response (corner AF): 2.17 µm/step — consistent with the piston number
- Screw2 tilt response (corner AF): 1.82 µm/step — low; within the settling/drift noise floor

So on this rig, 1 µm of plate motion ⇒ ~1.25–1.30 µm of focuser-measured focus shift. Three
degenerate explanations (indistinguishable from this data, same consequences): the focuser moves
optics that re-image (reducer/flattener on the drawtube ⇒ longitudinal magnification ≠ 1); the
configured focuser step size 0.269 µm is ~25% overstated; the EAT spec 1.8 µm/step is understated.
In every case the wizard's implicit assumption *1 focuser-µm = 1 plate-µm* fails by F ≈ 1.25–1.30.

**Why this matters for corrections:** the Aberration Inspector computes
`steps = focuser-frame µm / unitMicrons` (`TiltScrewTargets.ComputePerScrewTargets`). The factor F
**cancels iff `unitMicrons` is the honestly measured (focuser-frame) pitch**, because that pitch was
recovered from the same focuser-frame gradients. Using the spec (plate-frame) pitch leaves the full
F ≈ 1.25–1.30 as a systematic ~25–30% overcorrection on every tilt command. The wizard's
"Measured vs saved (+3.6%)" comparison is therefore apples-to-oranges: it compares a focuser-frame
measurement against a plate-frame spec and small deltas can mean two errors cancelled, not accuracy.

**The genuine non-linearity (small on-sensor, conceptually important):** the field-curvature sag
changes with focuser position (measured: corner−center sag went −147 → −78 steps during the piston,
i.e. the focus surface "breathes" as the focuser moves optics). Solving the vertex equation with
k(z) linearized gives a field-radius-dependent conversion `1/(s + k′r²)` — measured 1.35 at center
vs 1.28 at the corner-region radius (~5–7% across the sensor). It also means a pure piston creates
an apparent curvature change and, when the curvature apex is off-center, an apparent tilt change —
which is what inflates the wizard's AllInward "pure piston" noise probe (residual 69–79 units) and
degrades the reported SNR. These distortions grow with |backfocus error|, which is one mechanism
by which aberrated states read differently than corrected states.

## 4. Sensor-model curvature is ~2× smaller than region AF measures

Baseline state: stored curvature radius 5125 mm ⇒ predicted corner-box-minus-center sag 20.3 µm;
the live region AF measured **39.6 µm** (147.2 steps). Region-AF-implied curvature effect at the
55 mm screw radius: **−576 µm** vs the model's −295 µm. If region AF is right, backfocus
corrections computed from the sensor model undershoot by ~2×. (Caveats to verify: astigmatic
curvature is disabled — `Kx=Ky` forced — and star-distribution weighting differs between the two
estimators; but a factor 2 is far beyond those caveats' plausible size. This looks like the same
gradient/curvature shrinkage family as the Screw2 ×0.80 underestimate.)

## 5. Re-reading the empirical pitch-vs-aberration correlation

- **Aberrated runs:** larger signals → relatively less model shrinkage → pitch reads near the honest
  focuser-frame value ≈ F × 1.8 ≈ **2.2–2.3 µm/step** → looks "substantially larger than spec".
- **Corrected run:** small state gradients → model shrinkage bites (Screw2 ×0.80) → 2.00 → 1.86 →
  looks "close to spec". Two wrongs cancelling, not accuracy.

The correlation is real but its sign was misread: the spec-like value is the corrupted one.

## 6. TestApp replay confirmation

`TestApp tilt --dataset "D:\Tilt Calibration Bank\ghilios_corrected"` (unbayered frames, so the
headless replay applies; judge the paraboloid section — the TestApp region path has a known
separate lever-arm/scale issue, visible again here as an overall ×0.62 magnitude scale on its
4-corner numbers, which cancels in its move ratio).

**The paraboloid path reproduces the wizard deterministically.** Per-step (A, B), replay vs stored:
Baseline (−32.75, −53.27) vs (−32.74, −53.66); ReBaseline1 (71.90, −27.73) vs (72.32, −27.74);
Screw1 (−514.02, 364.28) vs (−514.49, 364.45); ReBaseline2 (−11.63, −49.32) vs (−13.08, −49.35);
Screw2 (391.78, 181.43) vs (390.72, 179.86). Calibration from these: **move ratio 1.5169, SNR 6.48,
gap 243.55°** — the wizard's stored result to within noise. So the 1.52 warning is a systematic
property of the per-star fit on these frames, not run-to-run scatter.

**TestApp's own 4-corner estimator (independent third estimator) again says the moves were equal:
move ratio 1.0833, SNR 9.44** (its per-region hyperbolic fits all R² ≥ 0.98). Its recovered step
size (1.376 µm/step) carries the known region-path scale issue and is not usable, but the ratio is.

**Per-step paraboloid fit diagnostics** (Solved surface model lines):

| step | Gx | K (µm/µm²) | stars | R² | red. χ² |
|---|---|---|---|---|---|
| Baseline | −2.45e-4 | −9.73e-8 | 1435 | 0.466 | 7.41 |
| AllInward | −7.90e-4 | −9.28e-8 | 1421 | 0.444 | 5.17 |
| ReBaseline1 | +5.37e-4 | −10.64e-8 | 1464 | 0.446 | 7.46 |
| Screw1 | −3.84e-3 | −10.63e-8 | 1498 | 0.916 | 8.68 |
| ReBaseline2 | −0.87e-4 | −10.13e-8 | 1382 | 0.382 | 7.28 |
| Screw2 | +2.93e-3 | −8.85e-8 | **1212** | 0.840 | 4.42 |

- The Baseline K (−9.73e-8/µm) predicts a corner-box-minus-center sag of 20.2 µm; region AF
  measured 39.6 µm — the **×2 curvature shrinkage confirmed with the replay's own K**, not just the
  stored curvature radius.
- The Screw2-state fit shed structure across the board: **12–19% fewer stars in the model** (1212 vs
  1382–1498), **K 13% below its neighbors** (−8.85 vs −10.13 RB2), and the gradient ×0.80 vs the
  corner estimator — a coherent "surface amplitude shrink", not an isolated gradient error.
- Small-tilt states fit with R² ≈ 0.38–0.47 (surface signal barely above per-star vertex noise,
  RMS ≈ 15–17 µm/star); reduced χ² 4.4–8.7 everywhere means per-star σ is still understated ~2–3×
  even after the 46778b9 σ-floor — the weighting is still not honestly calibrated on real frames.

## 7. Recommendations

Status as of `plans/tilt-calibration-accuracy-plan.md` Task 8 (items below are annotated inline;
short SHAs refer to commits on `ghilios/tilt-calibration-accuracy`).

1. **Compute calibration magnitudes/directions in physical gradient space** (gx, gy), not (A, B)
   space — closes F2 and makes `MoveMagnitudeRatio`, rawDiff, and SNR honest. (Known open item; the
   pitch math is already isotropic-correct.)
   **Implemented:** `ffabeac` (the (A,B)→physical-gradient conversion in `TiltCalibrationCalculator`),
   `059002e` (routed the wizard's `RunCalibrationMath` through a single `Calibrate()` call so the fix
   reaches the live wizard, not just the calculator), `ee6eca3` (removed a geometry fallback in
   `PhysicalDelta` and re-gated hardware recovery on it, a code-review follow-up).
2. **Cross-check the paraboloid gradient against the corner-region AF planes per step** and flag
   (or blend) when they disagree beyond the region σ — the region data is already computed during
   each wizard AF run; this run's warning would have been avoided and the pitch would have read ~2.0.
   **Implemented (flag, not blend):** `efc208d` (captures the corner-region plane per step and adds
   the disagreement warning), `a98cc77` (code-review follow-up). Prerequisite fix: `60eefda`, which
   found the corner-region estimator itself was regressing the 4-corner plane against the frame
   corners instead of the actual region centers, attenuating it by 2/3 — without that fix the
   cross-check would have been comparing against a biased reference.
3. **Investigate the paraboloid fit's gradient/curvature shrinkage** on real (not simulated) data:
   Screw2-state gradient ×0.80, curvature ×0.5 vs region AF. The winsorized-clip + σ-floor fix
   (46778b9) improved scatter but a state-dependent shrink survives on real frames.
   **Investigated, see §8.** Every candidate mechanism inside the paraboloid solver (clipping,
   pruning, weighting, weight monopoly, the high-σ tail, forced isotropic curvature) was ruled out by
   direct measurement; the compression is already present in the per-star best-focus values the
   solver is handed, and correlates with how well each star's sweep brackets its vertex. The *fix*
   (tightening the per-star vertex estimator's acceptance criteria) is out of scope for this plan and
   deferred to its own follow-up plan — it needs a TDD cycle against the synthetic AF bank, not a
   change to the paraboloid solver's robust loop.
4. **Treat pitch/corrections frame-consistently.** Either (a) always drive corrections with the
   measured (focuser-frame) pitch and reframe the UI comparison ("effective µm/step through your
   optics — expected to differ from the mechanical spec"), or (b) measure F explicitly from the
   AllInward piston (Δmean-focus µm / commanded plate µm — both already recorded) and surface it.
   The piston probe is currently used only for the curvature *sign*; it contains the frame factor
   for free.
   **Measurement half implemented** via item 5's piston probe (`c7fd73c`, `29e451e`) plus this task's
   manual rewrite (`documentation/docs/overview/tilt-adapter-wizard.md`), which reframes the UI
   comparison per option (a). Driving corrections from the measured value is deliberately left as a
   user choice, not automatic: the wizard already exposed a **Use measured value** button before this
   plan; the manual now explains why a rig can legitimately need it.
5. **Use the piston-implied pitch as a third estimate** (346 µm/150 steps, no pull-side mechanics,
   no tilt fit involved) to sanity-check the two tilt-derived per-screw pitches.
   **Implemented:** `c7fd73c` (`PistonImpliedMicronsPerStep`, the 20% disagreement warning, schema
   v3), `29e451e` (code-review follow-up, XAML row placement).
6. **Average out settling/drift**: measure each diagonal both directions (+150 and −150) and average
   the two deltas — cancels hysteresis and linear drift to first order; or raise
   `measurementAverageCount` for calibration runs.
   **Implemented** (the symmetric-delta variant, not the ± remeasurement variant): `a4a7304`
   (drift-cancelling `Screw1Delta` referenced to mid(ReBaseline1, ReBaseline2), unconditional),
   `f33548c` (code-review follow-up), `970bfc8` (optional measured final re-baseline / `ReBaseline3`,
   giving screw 2 the same symmetric reference, default on), `dab006a` (test coverage for the
   device-driven RB3 move and the shipped 5-step default flow).
7. **Backfocus correction undershoot (~2×)**: after (3), re-derive the expected backfocus step
   counts; the chronic "backfocus errors remained" experience during iterative correction is
   consistent with the model's curvature shrinkage.
   **Still pending**, now blocked on the §8 follow-up plan's fix (the per-star vertex estimator), not
   on item 3 in the abstract: §8 found the curvature shrinkage is downstream of the same vertex-level
   compression as the gradient shrinkage, so re-deriving backfocus step counts before that fix lands
   would be re-deriving them against a known-short curvature.

## 8. Shrinkage mechanism: it is not in the surface solver

§7 item 3 asked *what* shrinks the Screw2-state gradient ×0.80 and the curvature ×0.5 while Screw1's
state fits honestly. The replay's per-state solver diagnostics (`diag/<Step>_iterations.csv`,
`_points.csv`, `_stars.csv`) answer the question by elimination: **every candidate mechanism inside
the paraboloid fit is ruled out, so the compression is already present in the per-star best-focus
values the solver is handed.**

### What was ruled out

| Hypothesis | Test | Result |
|---|---|---|
| Robust clipping prunes the far-defocus side | Gx across winsorized iterations | Screw2 Gx 0.0028835 → 0.0029270 (**+1.5%**, it *grows*); Screw1 −0.0038379 → −0.0038403 (−0.06%). Flat. |
| Pruning removes enough stars to matter | `enabledFinal` counts | 3–6.5% pruned in every state (Screw2 79 of 1291). Far too few for a 20% amplitude deficit. |
| Weighting drags the gradient down | Refit the surviving points unweighted | Screw2 \|G\| moves **+3.6%**, Screw1 +2.8%. Not the mechanism. |
| Weight monopoly (a milder repeat of the pre-46778b9 bug) | ESS = (Σw)²/Σw², w = 1/σ² | ESS/N = **0.47–0.56 in every state** (Screw2 0.47). No monopoly. |
| The high-σ tail compresses the fit | Fit best-σ half vs worst-σ half separately | Screw2 0.003572 vs 0.003809 — agree within 4%, and the *worst* half reads larger. |
| Forced isotropic curvature (Kx = Ky) leaks into the linear terms | Refit with free Kx, Ky, and with an xy cross term | \|G\| changes by **≤0.4%** in every state. Dropping curvature entirely moves Screw2 by 2.7%. |

A hand reimplementation of the weighted solve reproduces the reported Gx to six digits for all six
states, so these are tests of the real estimator, not of an approximation to it.

### What the evidence points at instead

The compression is in the per-star vertex (best-focus) estimates, and it scales with the state's tilt:

- **Better-bracketed stars recover a larger gradient, and only on the tilted states.** Splitting each
  state's accepted stars by `matchedFrames` and refitting: Screw2's best-bracketed stars give a
  gradient **+8.8%** above its poorly-bracketed ones (0.003732 vs 0.003431); Screw1 gives +1.6%. The
  near-flat states show no informative signal (their gradients are noise-dominated).
- **Per-star σ correlates with position along the state's own tilt axis — uniquely on Screw2.**
  corr(σ, projection on Ĝ) = **+0.314** for Screw2, vs −0.105 (Screw1), +0.115 (ReBaseline2), and
  ≈0.01 for the flat states. Vertex quality degrades systematically across the tilt direction.
- **Screw2 loses the most stars upstream of the fit**: 2243 detections → 916 rejected for fewer than
  5 matched frames → **1291 accepted**, the fewest of any state (others 1434–1566). The deficit exists
  before the solver's first iteration, not because of it.
- Screw2's residual MAD is the *smallest* of all six states (2.07 vs Screw1's 3.15) at R² 0.84 — the
  surface fit is internally consistent. It is faithfully fitting an input set whose amplitude is short.

Note that +8.8% is a **lower bound** on the compression, not an estimate of it: the best-bracketed
stars are preferentially those whose focus sits near the middle of the sweep, i.e. near the tilt's
neutral axis, which is exactly where the lever arm is smallest.

### Recommended next fix (needs its own plan)

Attack the **per-star vertex estimator and its acceptance criteria**, not the paraboloid solver's
robust loop — the loop is not where the error is:

1. Replace the "≥5 matched frames" gate with a **bracketing** requirement: frames on both sides of the
   estimated vertex, with a minimum span, so a star fit from a partial branch cannot enter the surface.
2. Quantify the vertex estimator's compression against known truth on the synthetic AF bank (where the
   true surface is prescribed), as a function of how the sweep brackets each star. That converts the
   ≥8.8% lower bound into a calibrated number and tells us whether a correction is even needed once (1)
   is in place.
3. Re-run this dataset with more sweep positions to confirm the compression falls as bracketing improves.

Until then, the corner-region AF cross-check added in this plan is the operative safeguard: it fires
exactly on the states where this compression bites, which is what it was built for.

## Appendix: key numbers

- Per-step tilt planes (stored model vs corner AF), µm frame = focuser steps per normalized coord:
  see `metadata.json` `perStep[]`; corner-AF planes derived from `autofocus_report_Region{2..5}.json`
  `CalculatedFocusPoint.Position`, corners at normalized (±1/3, ±1/3).
- Piston: mean focus 11283.9 → 9995.5 (center-region 11360.7 → 10008.6); drift probes −86 to −91
  steps per ~9 min interval (piston), tilt-state cycle residuals (+40.2,+16.7) then (−79.1,−43.9).
- Error propagation: corner LOO stderr 4–29 steps/region ⇒ σ(delta mag) ≈ 29–34 units.
