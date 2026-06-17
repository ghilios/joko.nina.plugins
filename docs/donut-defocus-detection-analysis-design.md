# Donut / defocused-star detection — root-cause analysis (mufti AF run)

**Data:** `D:\Autofocus Bank\mufti\AutoFocus_20260607_030642-20260615T214722Z-3-001\attempt01`
— 7-frame focuser sweep 2325→2925 (step 100), best focus ≈ 2625, central-obstruction scope
(produces donut/annular stars off-focus). Sensor 9576×6388. Existing user labels in `attempt01/labels`.

**Harness:** `TestApp diagnose-labels` + `optimize` (Debug build), active profile **"AA1600MM Copy"**
(Sensitivity 1.6, StructureLayers 5, MaxDistortion 0.5, PixelScale 1.616). All read-only on the profile.

> ⚠️ **Profile caveat:** the harness loads the *developer's* active profile, not mufti's rig. Gate geometry
> (distortion/centering/size) is pixel-based so the diagnosis holds; PixelScale only scales HFR-arcsec / the AF
> curve. A first diagnose-labels run accidentally used a *stale different* profile (Sens 2 / SL 4 / PixScale
> 1.127) and gave a much worse baseline — discard it; the numbers below are the consistent "AA1600MM Copy" run.

---

## Quantified baseline (current settings, per frame)

| Focuser | Defocus | Candidates | **Accepted** | TooSmall | **TooDistorted** | %TooDist |
|---|---|---|---|---|---|---|
| 2625 (focus) | 0 | 1006 | **733** | 161 | 39 | 4% |
| 2525 | −1 | 774 | 485 | 129 | 120 | 16% |
| 2725 | +1 | 747 | 428 | 191 | 90 | 12% |
| 2425 | −2 | 490 | 116 | 163 | 174 | 36% |
| 2825 | +2 | 465 | 97 | 192 | 163 | 35% |
| 2325 | −3 (extreme) | 395 | **35** | 182 | 162 | 41% |
| 2925 | +3 (extreme) | 327 | **26** | 144 | 145 | 44% |

As defocus grows, **TooDistorted** explodes (4% → ~42%) and **TooSmall** stays huge (~40%), while the **candidate
count itself collapses** (1006 → ~330). At the extremes only ~26–35 of ~330–400 candidates survive.

## Label-box attribution (ground truth, current settings)

- **wronglyRejected (missed real donuts), 6 boxes:** 4 already **ACCEPTED**; 1 **Contaminated**; 1 **TooFlat**
  (the 55px object — actually a *saturated bright star with diffraction spikes*, not a clean donut).
- **shouldReject (false positives), 8 boxes:** 6 now **rejected** (5 TooDistorted, 1 TooSmall), 2 still accepted.
- The missed donuts that *do* form a candidate fail TooDistorted by a hair: fill-ratios **0.466–0.491 vs 0.5**.
  They are single candidates (IoU ≈ 0.9), **not** fragmented — so for moderate defocus this is a *gate-threshold*
  problem, not a candidate-formation gap.

## Why "even the optimized settings" miss donuts (the crux)

A labeled `optimize` run (J 0.887 → 0.924) made the extremes **worse**, not better:

| | 2325 | 2425 | 2525 | 2625 | 2725 | 2825 | 2925 |
|---|---|---|---|---|---|---|---|
| seed (current) | 35 | 116 | 485 | 733 | 428 | 97 | 26 |
| **optimized** | **15** | 68 | 214 | 361 | 184 | 62 | **10** |

It got there by making detection **stricter**: **Sensitivity 1.6 → 15.2**, StarClippingMultiplier 2 → 1.5,
PeakResponse 0.75 → 0.8, **MaxDistortion 0.5 → 0.55 (tighter!)**, and **DefocusAwareGates left OFF**. That
**halves** every frame and hammers the extremes (26→10, 35→15) — but improves σ_focus (9.6 → 6.8), R² (0.986 →
0.992), and label **precision = 1.0** (recall only 0.556).

**Root cause:** the optimizer objective rewards a *clean focus curve* + label *precision*; faint extreme donuts
add curve noise, so the optimizer is **incentivized to reject them**. Donut recall and curve-cleanliness are in
direct tension, and the precision-heavy label set (8 shouldReject vs 6 wronglyRejected) pushes toward strictness.
The optimizer is doing its job — its job just doesn't include extreme-donut recall.

## Three real loss mechanisms (off-focus)

1. **Gate rejection (dominant, settings-addressable):** donut forms one candidate, dies at **TooDistorted**
   (fill ≈ 0.47 vs 0.5), **Sensitivity**, or occasionally **TooFlat** (saturated) / **Contaminated**.
2. **Structure fragmentation + faintness (secondary):** at extremes, rims fragment into sub-threshold
   **TooSmall** pieces and faint donuts never exceed the binarize threshold (candidate count collapses).
3. **Bright-star spikes/halo accepted as stars (the "parts of defocused stars" FP):** a saturated star's
   diffraction-spike knots get **green-accepted** as separate stars; its flat core is TooFlat-rejected.

`fill-ratio` is a **poor donut discriminator** — a real donut (~0.47) and a junk blob (~0.47) are
indistinguishable to it, which is why relaxing it admits junk and tightening it kills donuts.

---

## Recommendations

### A. Settings (the bulk of the recovery) — *opposite* to what the optimizer chose
- **Keep Sensitivity low** (~1.5–2.5; the optimizer's 15 is curve-optimal but donut-hostile).
- **Enable DefocusAwareGates** (DefocusAwareDistortion + DefocusAwareCentering).
- **Lower DefocusDistortionSizeReference 30 → ~20** so 20–30px donuts get relaxed (default 30 misses them).
- Consider **MaxDistortion ≈ 0.4** and/or **DefocusAwareStructure** boost ≈ 2 for the extreme fragmentation.
- Consider **RejectContaminatedStars OFF** for AF (recovers the 1 contaminated donut; minor).
- The saturated bright star (TooFlat) is *correctly* down-weighted — saturated cores shouldn't anchor HFR.

*Confirmed lever:* enabling defocus-distortion with size-ref 20 moved 4/6 labeled donuts → ACCEPTED.

### B. Code changes (genuine, but re-prioritized vs. the original rim-bridging plan)
1. **Optimizer objective** — highest leverage. It does not value extreme-donut recall; that is *why* optimized
   settings fail. Options: add an extreme-frame star-count/recall term, weight extreme frames, or capture
   extreme-frame `wronglyRejected` labels. Without this, any detector change is undone by the next optimize run.
2. **Roundness / radial-symmetry admission for large candidates** — replace/augment the fill-ratio relaxation
   so donuts (round, symmetric) are admitted while junk (irregular) is not. More robust than relaxing fill-ratio.
3. **Re-tune DefocusDistortionSizeReference default** (30 → ~20) and ship DefocusAwareGates ON-by-default for AF.
4. **Structure-stage rim bridging** (the original plan) — helps the extreme TooSmall fragmentation, but it is a
   *secondary* win and near-focus-risky; pursue only after 1–3.

**Bottom line:** primarily a **settings + optimizer-objective** problem, not a candidate-formation bug. The
moderate-defocus donuts already detect under sane (low-Sensitivity, defocus-aware) settings; the optimizer
actively tunes those away. A detector-algorithm change is optional and, if pursued, should target the
fill-ratio→roundness discriminator and the objective, not rim-bridging first.

---

## Implementation (approved: gate + objective) — branch `ghilios/donut-defocus-detection`

### 1. Detector: roundness-rescue admission (LATE gate)
- `IStarDetector.cs` `StarDetectorParams`: add `DefocusRoundnessAdmission` (bool, default false) +
  `DefocusMaxElongation` (double, default 2.0). **Not** in `EarlyCacheKeyProperties` (late-only, like
  `DefocusAwareDistortion`).
- `StarDetector.cs` TooDistorted gate (~1353): when `fillRatio < effectiveMaxDistortion`, before rejecting,
  apply a **roundness rescue** — if `DefocusRoundnessAdmission` AND `d > DefocusDistortionSizeReference` AND
  `ComputeCandidateElongation(starPoints) ≤ DefocusMaxElongation`, admit (set `relaxationAdmitted = true`)
  instead of rejecting. Off ⇒ no rescue ⇒ bit-identical. New helper `ComputeCandidateElongation` =
  √(λmax/λmin) of the unweighted pixel-coordinate covariance (≈1 for a round/complete donut ring, large for
  streaks / spike fragments / partial arcs).
- `HocusFocusStarDetection.BuildStarDetectorParams`: `DefocusRoundnessAdmission = options.DefocusAwareGates`,
  `DefocusMaxElongation = options.DefocusMaxElongation`.

### 2. Optimizer objective: extreme-frame donut-recall term
- `OptimizationObjective.ObjectiveConstants`: add `EnableExtremeRecall` (bool, **default false** so existing
  objective tests stay bit-identical), `We` (0.20), `ExtremeTarget` (20).
- `SExtreme(m,c)` = `clamp01(meanExtremeCount / ExtremeTarget)` over the **min & max focuser-position** frames;
  in `JRun`, when enabled AND ≥2 distinct positions, fold `We·SExtreme` into the weighted sum (conditional
  include like `Wl`, renormalized into `wSum`). Reward is capped at target (no incentive to pump junk past it);
  roundness gate + label precision + SDefocusPrecision bound the precision risk.
- `StarDetectionOptimizer` default constants enable it (`EnableExtremeRecall = true`) so the wizard/TestApp use
  it while the objective unit tests (raw `new ObjectiveConstants()`) remain bit-identical.

### 2b. Optimizer coverage of the defocus-aware knobs (curated variable set)
`OptimizerVariable.CreateCuratedSet` now exposes the **entire** defocus-aware family so the optimizer fine-tunes
it, not just flips it on:
- `DefocusAwareGates` — Boolean on/off; its Write drives `DefocusAwareDistortion` + `DefocusAwareCentering` +
  `DefocusRoundnessAdmission` together (one knob enables the whole feature, incl. roundness rescue).
- `DefocusDistortionSizeReference` — Continuous [15, 60] px, step 5.
- `DefocusMaxElongation` — Continuous [1.0, 4.0], step 0.25.
- `DefocusDistortionMinFactor` — Continuous [0.1, 1.0], step 0.05.
- `DefocusCenteringToleranceFactor` — Continuous [1.0, 4.0], step 0.25.
- `DefocusAwareStructure`/`StructureLayerBoost` — Integer boost [0, 4] (already curated).
All four numeric knobs are **LATE** params (gate-only; not in `EarlyCacheKeyProperties`), so exploring them
reuses the cached early detection context — no perf regression. They are inert no-ops while the gates are OFF.
The **objective** constants (`EnableExtremeRecall`, `We`, `ExtremeTarget`) are NOT searched — they shape the
objective itself (set in `StarDetectionOptimizer`), not detector params the search tunes.

### 3. Options / UI / defaults
- `DefocusAwareGates` default **false → true** (ship defocus-aware on for AF; roundness + SDefocusPrecision
  guard precision). Keep `DefocusDistortionSizeReference` default **30** (don't regress the Panos tuning);
  recommend 20 in the per-setup recipe.
- New Advanced option `DefocusMaxElongation` (double, default 2.0, range [1,10]): UnitTextBox + DoubleRangeRule
  + tooltip in `OptionsDataTemplates.xaml`; plus a CheckBox already exists for `DefocusAwareGates`.
- TestApp `diagnose-labels`: add `--defocus-roundness` / `--defocus-max-elongation` switches to validate.

### 4. Tests (NUnit) + validation
- Unit: `ComputeCandidateElongation` (disk≈1, 3:1 streak≈3, ring≈1, degenerate), roundness-rescue OFF path
  bit-identical, `SExtreme` math + `JRun` extreme-term include/renorm, optimizer enables the term.
- Validate on the mufti run: `diagnose-labels`/`optimize` before/after — labeled donuts → ACCEPTED, extreme
  counts recover, near-focus precision not regressed. Deliver the concrete per-setup settings recipe.

### 5. Validation results (mufti run, "AA1600MM Copy" profile)

**Detector fix (default-on gates + roundness) — seed extreme recall roughly doubled:**

| Extreme frame | before fix | after fix (seed) |
|---|---|---|
| 2325 | 35 | **68** |
| 2925 | 26 | **66** |

Visual: recovered donuts are real rings; the bright-star **diffraction-spike fragments are correctly rejected**
(elongated ⇒ fail the roundness cap). Remaining misses at the extreme are **faint, sub-30px** donuts — an SNR
floor below the roundness size gate, not recoverable without admitting noise (honest limitation).

**Objective fix (extreme-recall term, We=0.30 / ExtremeTarget=50) — the optimizer now PRESERVES donuts:**

| Optimized extreme recall | 2325 | 2925 | Sensitivity chosen |
|---|---|---|---|
| old objective | 15 | 10 | 1.6 → **18** (culls donuts) |
| + both fixes, untuned term | 45 | 28 | → 18 |
| + tuned term (final) | **54** | **50** | 1.6 → **0** (keeps donuts) |

The term **flips the optimizer's incentive**: instead of raising Sensitivity to cull faint extreme stars for a
cleaner curve, it lowers Sensitivity to keep them (J 0.915→0.947; σ still improves 8.9→…; label precision 1.0).

**Labels under the new optimized settings** (`diagnose-labels --params optimized`): all **8/8 false-positive
fragments rejected** (was 6/8), **4/6 missed donuts ACCEPTED**; the 2 residual are the saturated diffraction
star (TooFlat — correct) and one Contaminated donut.

### Per-setup settings recipe (delivered)
- **DefocusAwareGates: ON** (now the default) — drives distortion + centering relaxation + roundness rescue.
- **DefocusMaxElongation: 2.0** (default); lower for stricter roundness.
- **DefocusDistortionSizeReference: 30** (default); lower toward **20** to catch 20–30px donuts (costs some
  moderate-frame admissions — validate per setup).
- **Sensitivity: keep low (1.5–2.5)** for donut recall; do NOT adopt the high Sensitivity the *old* optimizer
  recommended (that is what was culling donuts).
- **DefocusAwareStructure: ON + boost 2** only if faint donuts never form candidates (NO-CANDIDATE).
- Optionally **RejectContaminatedStars OFF** for AF to recover contamination-flagged donuts.
- `ExtremeTarget=50 / We=0.30` are first-cut objective constants tuned on this run; revisit across the AF bank.

### 6. Bank regression sweep → opt-in redesign (the donut features trade AF-curve fit)

A full A/B over **16 bank runs** (baseline = HEAD worktree vs. the new fields; identical 400-eval budget; deterministic
optimizer) showed the donut fields, default-on, **regressed AF-curve fit on 11/16 runs** (some 2–3×: Panos σ 3.96→10.2,
fmeschia 2.0→6.2, mccomiskey 0.58→1.46 with R² 0.997→0.985). Attribution (via `--no-extreme-recall` / `--legacy-curated`
isolation runs): the 4 new curated variables (search-space dilution on a fixed budget) ≈4 runs; the detection
default-on + roundness + round-trip seed ≈6 runs; the extreme-recall term ≈1 run. **Donut recall (more, fainter,
larger-HFR stars) is fundamentally in tension with curve tightness — every donut-favoring *default* trades some fit.**

**Resolution — the whole defocus/donut package is OPT-IN, gated on the optimizer screen:**
- `DefocusAwareGates` option default reverted to **OFF** (baseline live detection + optimizer seed); DTO default OFF.
- `OptimizerVariable.CreateCuratedSet(defocusRecovery=false)`: default = the **14-var baseline** set (the gates switch
  drives distortion+centering only, **no** roundness, **no** 4 knobs — `DefocusAwareGates`/`DefocusAwareStructure` were
  curated pre-change so they stay); opt-in (`true`) adds the roundness coupling + the 4 defocus knobs (18 vars).
- Extreme-recall term: `StarDetectionOptimizer` default OFF; the opt-in passes `EnableExtremeRecall=true`.
- Wizard: a **"Recover defocused / donut stars"** checkbox (`DefocusRecovery`, default off) on the optimize screen
  threads through to the curated set + objective; TestApp mirrors it with `--defocus-recovery`.

**Verification (definitive):** with a baseline-state profile (gates OFF), the new default optimizer is **bit-identical
to baseline on all 16 runs** (σ_focus + bestJ match to <5e-4) ⇒ **zero fit regression** unless the user opts in. The
earlier 16/16 "differs" was entirely because the active test profile already had `DefocusAwareGates: true` +
`UseOptimizedSettings` + a donut snapshot (i.e. already in the opt-in state). 1283 tests pass.
