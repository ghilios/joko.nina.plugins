# Simple-Mode Upgrade-Path Hardening — Design

**Status:** design / research spec (no code changes yet)
**Trigger:** user "SorenVance" upgraded HocusFocus `v3.0.0.26 → v4.0.0.3`; AutoFocus began failing with **no settings changed by the user**.
**Evidence:** `D:\Autofocus Bank\SorenVance\AutoFocus_20260711_014141` (WSL: `/mnt/d/Autofocus Bank/SorenVance/AutoFocus_20260711_014141`).

---

## TL;DR

1. **The captured settings exactly match a Simple-mode preset.** Every advanced knob in SorenVance's run is a byte-for-byte match to the **v4.0.0.3 Simple-mode "all-Typical" preset** produced by `DerivePresetSettings()`. Two knobs are *derivation fingerprints* that only that method emits (see §2). He was in Simple/Typical, never customized a knob, then flipped the **Advanced** toggle.

2. **The freeze trap the report suspected did *not* cause the break.** His frozen values are the **v4** preset (`NoiseClip 2.0`, `Sensitivity 2.0`), not v3's (`4.0`, `10.0`). So at first v4 load he was still in Simple mode; v4 silently re-derived and persisted the new preset, and he flipped to Advanced *afterward*. The freeze is a real *latent* hazard but not this incident's cause.

3. **The actual cause is a silent v4 preset + algorithm change applied on upgrade.** The decisive delta is **`LocallyAdaptiveBinarization` defaulting ON** (new in v4): its per-128px-block threshold self-inflates on a defocused donut's own residual ring flux and zeroes it out. Result: candidate **formation** collapses to `StructureCandidates=1 / DetectedStars=0` on the 4 heavily-defocused sweep frames; the hyperbolic fit is left 2 usable points → **AutoFocus fails**.

4. **Repair ≠ prevent.** Because his first v4 load already overwrote his v3 config and he is now in Advanced mode (inert to all Simple-mode logic), most "prevention" measures are **retroactively too late for him**. Repairing already-affected users needs something that works *in Advanced mode / at detection time*; preventing the next regression needs a **release gate**.

---

## 1. The incident, quantified

Per-frame `StarDetectorMetrics` from the run (attempt01 + initial):

| Focuser position | StructureCandidates | DetectedStars | Notes |
|---|---:|---:|---|
| 29295 / 29095 / 28695 / 28495 (4 defocused points) | **1** | **0** | candidate formation collapses entirely |
| ~28895 (2 near-focus points) | ~15,800 / 15,900 | 175 / 330 | ~**87% rejected `TooSmall`** (noise flood) |

`HotpixelCount` ≈ 40k–45k every frame. Rig: **OSC / Bayered** (`debayerImage=true`), **L-eXtreme** narrowband, `PixelScale ≈ 2.03"/px`. With only 2 of 6 sweep points yielding any stars, the weighted hyperbolic fit is unconstrained and the engine reports the bare `"AutoFocus failed"`.

---

## 2. Part 1 — Do the settings exactly match a Simple-mode setting? **Yes.**

Captured snapshot (`metadata.json` / `autofocus_report_Region0.json`): `UseAdvanced=true`, `Simple_NoiseLevel=Typical(0)`, `Simple_PixelScale=Typical(0)`, `Simple_FocusRange=Typical(0)`, `UseOptimizedSettings=false`.

Enum orderings (`Interfaces/IStarDetectionOptions.cs`): `NoiseLevelEnum { Typical=0, None=1, High=2, Low=3 }`, `PixelScaleEnum { Typical=0, WideField=1, LongFocalLength=2 }`, `FocusRangeEnum { Typical=0, WideRange=1 }` → all three `0` = **all Typical** (the defaults).

Every advanced knob equals `DerivePresetSettings()` for all-Typical (`StarDetection/StarDetectionOptions.cs:114-190`). The match is not merely "looks like defaults" — two values are produced **only** by the Simple-mode derivation:

- **`NoiseReductionRadius = 4`** — base `3` (Typical) **+1** from the `HotpixelThresholdingEnabled && HotpixelFiltering` bump inside `DerivePresetSettings` (`:173-176`). Both `ResetDefaults()` and `InitializeOptions()` yield `3`. Only the derivation yields `4`.
- **`BrightnessSensitivity = 2.0`** — exactly `10.0 × sensitivityScale(0.2)` for the Typical noise path (`:151`).

Plus `NoiseClippingMultiplier=2.0`, `StarClippingMultiplier=2.0`, `StructureLayers=4`, `MinHFR=1.2`, `StarPeakResponse=0.75`, `MaxDistortion=0.5`, `StarCenterTolerance=0.3`, `MinStarBoundingBoxSize=5`, `PixelSampleSize=1.0`, `LocallyAdaptiveBinarization=true`, `DefocusAware*=false` — all match.

**Conclusion:** Simple mode, all Typical, untouched, then Advanced toggled on.

### Timeline reconstruction (why it matters)

v3.0.0.26 Simple/Typical preset (`git show release/v3.0.0.26:…/StarDetectionOptions.cs`): `NoiseClippingMultiplier=4`, `BrightnessSensitivity=10.0`, `MinHFR=1.5`, **no** `LocallyAdaptiveBinarization` (feature absent → global threshold), no DefocusAware features.

The snapshot shows **v4** values, not v3's. Therefore at first v4 load `UseAdvanced` was still `false`, so `InitializeOptions → ConfigureSimpleSettings → DerivePresetSettings` recomputed and **persisted** the v4 preset over his v3 values (each setter persists immediately, e.g. `:508`). He flipped to Advanced *after* that. → **His frozen Advanced values are the current v4 preset, and the freeze did not cause the break.**

---

## 3. The actual failure mechanism (candidate-formation regression)

Confirmed against the detector code (v4 vs `release/v3.0.0.26`):

- **Primary driver — `LocallyAdaptiveBinarization=ON` (new default `true`, `StarDetectionOptions.cs:232`).** v3 binarized the structure map with one global scalar threshold `median + NC·σ_global` (v3 `StarDetector.cs:272`). v4 replaces it with a per-128px-block surface `local_median(structureMap) + NC·local_σ(source)`, bilinearly upsampled (`StarDetector.cs:640-644, 882-901`; grid `CvImageUtility.cs:609-667`). `local_σ` is a raw `1.4826·MAD` of the **noise-reduced source** (which still carries the full donut flux), with **no** iterative outlier rejection. So a surviving donut ring inflates **both** threshold terms *in its own block* and the threshold rises above the ring — the detector self-suppresses exactly the extended star it should keep. v3's global threshold was pinned to the whole-frame dark background, so a locally-few-σ ring still crossed it.
- **Pre-existing vulnerability (identical in v3, not the delta) — the wavelet.** `StructureLayers=4` atrous B3-spline residual subtraction + 9px post-blur (`StarDetector.cs:574-591`) erases a large low-spatial-frequency defocused donut down to a thin faint ring. With `DefocusAwareStructure/DonutDetection` OFF there is no `+2` layer boost and no morph-close ring reconnection (`:577-578, 664-670`) to rebuild it. Adaptive binarization then converts "thin ring" into "nothing."
- **`NoiseClippingMultiplier 4→2` (`StarDetectionOptions.cs:146`).** NC is *not* only the binarize-threshold multiplier — it is also the κ in the kappa-sigma **noise estimate** (`StarDetector.cs:525,552`; `CvImageUtility.cs:549`) and a term in the **adaptive** threshold surface (`:896`), so it sits directly in candidate formation. At near focus, lower NC floods the compact frames with ~15,800 candidates (~87% `TooSmall`). See §3a: empirical testing shows NC is also load-bearing for the **defocus** collapse — more central than a threshold-multiplier-only reading suggests.
- **Not implicated in formation — F4 honest-σ.** The honest (≈4× larger, unblurred) σ enters only the downstream sensitivity/pixel-clip gates; the binarize threshold uses the structure-source blurred σ in **both** v3 and v4 (`StarDetector.cs:607-612`). It stiffens rejection on surviving near-focus stars but is not a candidate-formation cause.

**Net:** the upgrade silently swapped a working v3 config (global threshold, NC=4, no adaptive/DefocusAware) for a v4 preset whose defaults are hostile to defocused OSC-narrowband donuts, and it shipped with **no regression gate against defocused-donut data** and **no schema/migration** on the live profile store.

### 3a. Empirical update — a live test supersedes part of §3

**Reported result (from the user):** on SorenVance's rig, detection **started working again** when the Simple preset's `BrightnessSensitivity` and `NoiseClippingMultiplier` were reverted to the v3 values **`10`** and **`4`** respectively.

This is a direct experiment and it overrides the code-only mechanism study in one respect: the study concluded `LocallyAdaptiveBinarization` was the *primary* defocus driver and that "NC `4→2` does not cause the defocus zero." The revert result shows **`NoiseClippingMultiplier` (and/or `BrightnessSensitivity`) is load-bearing for the defocus collapse**, not merely the near-focus flood. Because NC also sets the κ of the noise estimate and feeds the adaptive surface, restoring `NC=4` plausibly changes formation on the defocused frames; and `BrightnessSensitivity=2` (the F4 `×0.2` value) likely over-sensitized the near-focus gate, so `10` cuts the flood. The exact pathway is **not yet pinned** — the metrics we have are from the *broken* (`NC=2`) run only; the *fixed* (`NC=4`) run's per-frame `StarDetectorMetrics` would settle it.

**Implication for the fix:** reverting the two Simple-preset constants is a legitimate, low-risk immediate fix (see R0 below). The one thing to validate first is whether it regresses the recall the `4→2` change was made to buy — see §5 R0 and open question #3.

---

## 4. Two problems, one of which actually broke him

| | Actual cause | Latent hazard |
|---|---|---|
| **What** | Silent v4 preset + algorithm change on upgrade (esp. `LocallyAdaptiveBinarization` default ON) collapses candidate formation on defocused OSC-narrowband donuts. | The `UseAdvanced` freeze: once `UseAdvanced=true` persists, `ConfigureSimpleSettings` early-returns forever (`:62-64`); no "user genuinely customized a knob" vs "user just flipped the toggle" distinction. |
| **Broke SorenVance?** | **Yes.** | **No** — his flip happened after the v4 re-derive. |
| **Why it still matters** | Needs a release gate + safer defaults so it never ships again; needs a runtime net + repair path for those already hit. | It is what would **prevent a corrected preset from ever reaching him** (or anyone who flipped Advanced). Fixing the freeze is how you *deliver* the fix, not how you caused the bug. |

---

## 5. Hardening recommendations (ranked; adversarially reviewed)

Each was proposed under a distinct lens and then adversarially verified against the code. Verifier caveats are folded in — most importantly the **retroactive-too-late** and **inert-in-Advanced-mode** constraints, which several "prevention" ideas fail for *this* user.

### R0 — Revert the Simple preset's `NoiseClippingMultiplier`→`4` and `BrightnessSensitivity`→`10` to v3 (interim) — **effort S, risk low-med, EMPIRICALLY VALIDATED (§14)**

> **IMPLEMENTED (interim, this PR):** `NoiseClippingMultiplier 2→4` **and** `BrightnessSensitivity 2→10` (F4 `sensitivityScale` removed), in lockstep across `DerivePresetSettings` / `ResetDefaults` / `BuildDefaultStarDetectorParams` (drift guard). **Validated by replaying SorenVance's saved AF run (§14):** `BS=10` reproduces the clean v3.0.0.26 curve, while the F4-default `BS=2` corrupts the defocused-frame HFR (median dragged down by admitted small fragments). This **supersedes** the earlier "keep BS at 2.0 (effective-v3)" reasoning — F4's `×0.2 ≈ v3-effective` equivalence does **not** hold on structured/defocused frames (there σ_honest ≈ σ_blurred, so the literal v3 `10` is what matches). **Caveat — not free:** `NC 4→2` came from the golden-set recall audit (`docs/star-detection-golden-audit-cwhite-results.md`), which found ~79% of real faint stars never formed a candidate at 4σ. So `NC=4` may cost faint near-focus recall on the mono data that audit was built from even as it fixes defocused OSC donuts — it is possible **no single global constant wins**. This is decidable, not a matter of taste: run the `running-af-bank-validation` NoiseClip sweep (`NC=4/Sens=10` vs `2/2`) + `golden eval` across `D:\Autofocus Bank`. If reverting shows no golden-recall regression → ship the revert. If it does → prefer **R3** (DefocusAware ON), which recovers the donuts *without* surrendering the `4→2` recall gains. Either way, pair with R1 so the sweep becomes a permanent gate.

### R1 — Gate every preset/algorithm change on the AF-bank + golden eval, with an OSC-narrowband defocused-donut fixture — **effort S, risk low, would-have-prevented ✔**
Make it mandatory that any change to `DerivePresetSettings`, `InitializeOptions` defaults, or the detector pipeline runs `TestApp bank-verify` / `golden eval` across the AF-run bank **before release**, and add SorenVance's run as a permanent regression fixture. Fail the change if any bank run flips AF-success→AF-fail or loses per-frame `DetectedStars` on defocused sweep points. Reuses the existing `running-af-bank-validation` skill; the only new work is the fixture + release-checklist wiring. **This is the only measure that would have *causally stopped v4.0.0.3 from shipping the regression.*** (Open dependency: the bank must actually contain an OSC/Bayered narrowband ~2"/px heavily-defocused rig — see §7.)

### R2 — Per-frame candidate-formation fallback (retry a collapsed frame with relaxed structure) — **effort M, risk med, would-have-prevented ✔ (repairs at runtime, any mode)**
In the detector, when a frame yields candidates/stars far below its siblings (e.g. 0–1 while siblings have thousands), automatically re-run that frame on a relaxed path — boost `StructureLayers`, apply donut morph-close, and/or fall back to the **global** (non-adaptive) binarize threshold — and keep whichever pass finds stars. A runtime safety net **independent of preset/mode**, so it salvages the 4 collapsed frames even for a frozen-Advanced profile. Verifier note: raise the AF retry-loop backstop `maxIterations` from `TotalNumberOfAttempts + 1` to `+2` (`AutoFocus/AutoFocusEngine.cs:1444`) so a detection-relax retry can't be aborted alongside the existing calculated-point retry.

### R3 — Default the DefocusAware structure/gate path ON for the Simple preset (or auto-enable for AF sweeps) — **effort M, risk med, would-have-prevented ✔**
AF sweeps inherently visit extreme defocus, yet v4 ships `DefocusAwareStructure/Gates/DonutDetection` OFF — exactly the features (`+2` layer boost, morph-close ring reconnection, distortion relaxation; `StarDetector.cs:577-578, 664-670`) that offset the adaptive-binarization loss. Turn structure-recovery ON in `DerivePresetSettings` (at least for `WideRange`/`Typical`) or auto-enable it during AF sweeps. Must be validated against the full bank first (see §7) to confirm no near-focus precision regression / no worse `TooSmall` flood.

### R4 — Actionable AF-failure diagnostics + per-frame detection logging — **effort S, risk low, would-have-prevented ✘ (but makes this class self-diagnosing)**
When AF fails for too-few usable sweep points, replace the bare `"AutoFocus failed"` with the cause: log/report per-frame `StructureCandidates`/`DetectedStars` and emit a specific hint (e.g. *"detection collapsed on N defocused frames — enable Defocus-Aware detection or widen Focus Range"*), and write a `FailureReason` into the run metadata. Verifier note: the per-frame result JSON is already read back (`AutoFocusEngine.cs:269, 779`), so this reuses an existing path. Turns a silent dead-end into a self-service recovery + trivially diagnosable future reports.

### R5 — Break the `UseAdvanced` freeze trap with per-knob override tracking — **effort L, risk med, would-have-prevented ✘ (but is how a fix reaches frozen users)**
Track an `AdvancedCustomized` dirty bit so the system distinguishes "user edited this knob" from "user flipped to Advanced with Simple defaults intact"; keep re-deriving non-overridden knobs on upgrade; add an explicit **"Re-sync to Recommended (Simple)"** command. Necessary so a corrected preset can actually reach Advanced-mode users. **Mechanism corrections from review (must-fix, else self-defeating):**
- The bulk-apply suppression flag must also wrap `DerivePresetSettings` (`:114`) and `ApplyOptimizedSnapshotToLiveProperties` (`:79-112`), not just `ApplySnapshotCore`/`ApplyKnobs` — those re-sync paths use public setters and would otherwise self-mark the profile dirty and re-freeze it next load.
- The `UseAdvanced` setter must **clear** `AdvancedCustomized=false` on the `false→true` transition. Because `InitializeOptions` sets the `useAdvanced` **field** directly (`:206`) without firing the setter, "setter fired" is a reliable *live user action* signal and "loaded true with no setter" reliably means *legacy* — this is the clean resolution of the fresh-install vs upgraded-profile ambiguity (there is no live-options schema version to tell them apart today).

### R6 — Add a `schemaVersion` + migration hook to the live `StarDetectionOptions` profile store — **effort L, risk med, would-have-prevented ✘ (foundational)**
The persisted profile options have **no** `schemaVersion` or migration path (only replay/tilt/export/optimized blobs do). Add a version stamp so upgrades can run targeted migrations (re-derive presets, opt existing profiles into corrected DefocusAware defaults, record what changed). Verifier caveats: (a) fresh-vs-upgrade detection *is* sound via load ordering (a sentinel read **before** `:271` returns absent=fresh / present=upgrade), but piggybacking on `NoiseReductionRadius` is fragile — use a dedicated never-removed marker key; (b) **auto-re-derive is retroactively unsafe** for profiles created before the feature ships (their `AdvancedKnobsUserModified` defaults false → genuinely hand-tuned knobs get clobbered). Ship only the **flag + banner, do-not-mutate** variant for the pre-feature cohort.

### R7 — On version bump, notify that detection presets/algorithm changed and offer validate/re-optimize — **effort S, risk low, would-have-prevented ✘**
On detecting a version increase, show a one-time notice that star-detection presets/algorithm changed this release, with a **"Validate detection now"** action that launches the optimizer wizard (reports live star counts) and a plain Simple-vs-Advanced state summary. Cheap transparency; pairs with R6's stamp. Verifier note: an auto-captured pre-upgrade settings backup (via the existing export/import machinery) **cannot** retroactively recover *this* user — his first v4 load already overwrote his v3 values before any backup code existed; the backup only protects *future* upgrades.

---

## 6. The decisive distinction: repair vs prevent

- **Repair the already-affected cohort (works in Advanced mode / at detection time):** R2 (runtime fallback), R4 (diagnostics so they know what to do), R5 (unfreeze so a corrected preset reaches them), plus the manual workaround below. Preset-pinning / value-signature / auto-re-derive ideas are **inert** here: SorenVance is `UseAdvanced=true`, so `ConfigureSimpleSettings` (`:62`) and `StarDetectionOptions_PropertyChanged` (`:193`) early-return and never run — and his v3 value-signatures were already destroyed by the first v4 load.
- **Prevent the next regression (must ship *before* the breaking change):** R1 (release gate) is the only causal preventer; R3 (safer defaults), R6 (deliberate/reversible migrations), and R7 (notice) reduce blast radius going forward.

**Immediate manual workaround for affected users** (no code): in Advanced star-detection options, either turn **Defocus-Aware detection ON**, **or** turn `LocallyAdaptiveBinarization` **OFF** (restores v3's global threshold), and/or widen **Focus Range**; alternatively raise `NoiseClippingMultiplier` back toward 3–4 to cut the near-focus flood.

---

## 7. Phased roadmap

**Immediate**
- R0: validate `NC=4/Sens=10` vs `2/2` on the bank; if no golden-recall regression, revert the two Simple-preset constants (the user-confirmed fix). If it regresses recall, take R3 instead.
- R1: add `AutoFocus_20260711_014141` to the AF-bank as a regression fixture; wire `bank-verify`/`golden eval` into the release checklist so no preset/algorithm change ships without passing it.
- R4: actionable AF-failure diagnostics + per-frame `StructureCandidates`/`DetectedStars` logging + metadata `FailureReason`.
- Ship the manual workaround (§6) as a support note, and — if cheap — R7's one-time version-bump notice.

**Short-term**
- R2: per-frame candidate-formation fallback/retry on collapse (with the `+2` backstop fix).
- R3: re-tune the Simple `Typical`/`WideRange` preset to default the DefocusAware recovery path ON (or auto-enable during AF sweeps), validated against the full bank to confirm no near-focus precision regression.

**Long-term**
- R5: per-knob override tracking + "Re-sync to Recommended (Simple)" (with the must-fix suppression/setter corrections) so corrected presets reach frozen Advanced profiles.
- R6: `schemaVersion` + migration hook on the live profile store (flag+banner, do-not-mutate variant for the pre-feature cohort) so future default/preset changes are deliberate, opt-in, and reversible.

---

## 8. Open questions

1. Does the AF-bank already contain an OSC/Bayered L-eXtreme narrowband, ~2"/px, heavily-defocused-donut rig comparable to SorenVance's? **R1 only catches this class if such a fixture exists** — his run must be added, ideally with a `golden.json`.
2. Would defaulting DefocusAware ON (R3) regress near-focus precision or worsen the ~87% `TooSmall` flood on the broader bank? Needs a full sweep before flipping the default.
3. Is the near-focus `TooSmall` flood driven by `NoiseClippingMultiplier 4→2`? Would restoring NC toward 3–4 *specifically for OSC/Bayered frames* cut the flood without losing the recall the 4→2 change bought (per the golden-set audit)?
4. Should Simple mode branch on rig characteristics (OSC/Bayered + narrowband filter + pixel scale) to pick a defocus-robust preset automatically, rather than one Typical preset for all optical trains?
5. For R5: what are the correct migration semantics for *existing* frozen Advanced profiles — auto-detect "values still equal a known preset" and unfreeze, per-knob dirty flags, or an explicit user-driven "sync to preset"?
6. For R2: what exact per-frame thresholds define "collapse" (absolute candidate count vs. ratio to sibling frames), and what is the compute cost of a second detector pass per suspect frame during a live AF run?

---

## 9. The optimizer does not rescue a weak Simple default (verified)

Strategy context: the intended direction is to push users toward the Optimization Wizard, on the assumption that it "applies the updates for noise clipping and sensitivity" and that the Simple/default detector is its starting point. Verified against the code, with two corrections:

- **The optimizer seeds from a fixed `ResetDefaults` baseline, not the live Simple preset.** `BuildDefaultStarDetectorParams()` (drift-guard-tested `== ResetDefaults()`) is the seed; `StartFromCurrentSettings` defaults **OFF** (`StarDetectionOptimizerWizardVM.cs:1696`, `:500-514`; `docs/optimizer-default-seed-baseline-design.md`). It equals the Simple preset only at Typical/Typical/Typical.
- **The optimizer *does* tune and overwrite both `NoiseClippingMultiplier` and `Sensitivity`** (2 of 12 curated axes, `OptimizerVariable.cs:140-165`; applied via `StarDetectionOptions.cs:85,87`). So for optimizer users the Simple default's NC/sensitivity are only the search origin + never-regress floor — not the final values. ("Sensitivity pinning" in `docs/optimizer-sensitivity-pinning-design.md` is a *pathology* — pinning to the search ceiling 50 — fixed by a tie-breaker, not a seed hold.)

**Why the Simple default must still be extra reliable (the correct justification):** (1) most users never open the wizard — Simple mode is the out-of-box default and `UseOptimizedSettings` defaults false, so their live NC/sensitivity *are* the Simple preset; (2) the untuned knobs (PSF/dilation/contamination/defocus-when-master-off) always come from `DerivePresetSettings()` even after Accept (`StarDetectionOptions.cs:80`). And the optimizer is not a safety net: it needs a good AF run and can converge *worse* than current settings (surfaced + revertable), so it cannot excuse a weak default.

**Implementation flag for R0:** today `Simple/Typical == ResetDefaults == optimizer seed`, all at NC=2/Sens=2, with a drift-guard test tying `BuildDefaultStarDetectorParams` to `ResetDefaults`. Reverting *only* `DerivePresetSettings(Typical)` to NC=4/Sens=10 diverges it from `ResetDefaults`/the seed — decide deliberately whether the fixed default (and thus the optimizer's seed + never-regress floor) moves too, and update the drift-guard test accordingly.

## 10. Bank sweep results (executed) — reprioritization + corrections

Ran `bank-verify --nc-sweep 2,3,4` twice over the 20 golden bank runs: **LAB=ON** (`--adaptive-binarize`, the live Simple-default condition) and **LAB=OFF**. Reports in `D:\Autofocus Bank\_nc_labON` / `_nc_labOFF` (commit ec7fc34).

**Governing principle (from the repo owner, adopted):** for the *default*, **not-breaking is the higher-order bit; recall optimization belongs in the optimizer.** A default that takes a working rig from AF-succeeds to AF-fails is categorically worse than one with marginally lower faint-star recall. Optimizer users get rig-tuned NC/Sensitivity (verified, §9); non-optimizer users judged against their **v3** baseline (what worked) are not worse off. So a modest recall cost on a conservative default is acceptable and not a blocker.

**LAB=ON, median (trustworthy metric = recall@SNR≥12):** NC2 0.877 / NC3 0.845 / NC4 0.819 — raising NC costs recall uniformly (~−0.06, all 15 non-donut runs), precision rises 0.62→0.79 (skewed lower bound; don't over-read). Acceptable cost per the principle above.

**Corrections to earlier mechanism claims (the sweep overrode them):**
- **Do NOT disable LocallyAdaptiveBinarization.** LAB=OFF did *not* un-break the donut runs (`LinwoodFocus` 0.149→0.158, `Panos` 0.208→0.210, `mufti` 0.342→0.291) and *catastrophically regressed* `cwhite_2026` (recall 0.862→0.459 @NC2, 0.797→0.189 @NC4) plus `timmer`/`caboose`. LAB=ON is net-beneficial (varying-background frames). This refutes §3's framing of LAB=ON as the primary defocus-collapse driver.
- **NC is not the formation fix.** Donut runs stay broken at NC 2/3/4 *and* LAB on/off — candidate formation on defocused donuts is only recovered by **DefocusAware** (config B). The wavelet donut-erasure is identical in v3, so the *extreme* golden donut runs were broken in v3 too (not the regression); SorenVance (borderline/mild defocus, `donutAware` frac 1.11) is.
- **The sweep cannot confirm SorenVance's fix.** His run has no goldens (unscored) and the sweep held Sensitivity=2 (varied only NC), so NC=4 was never shown to restore him. His empirical `NC4/Sens10` fix likely hinges on **Sensitivity=10** (cuts the near-focus noise flood) and/or DefocusAware — not NC.

**Reprioritized recommendation:** keep **NC=2 + LAB=ON** (both net-positive bank-wide); make the reliability-first default by **defaulting DefocusAware-on** (R3) — recovers donut/defocus formation with zero recall cost and without removing LAB. R0 (NC/Sens revert) is reclassified: acceptable-cost per the principle, but costs recall and is *not* the formation fix — a fallback, not the lead. **Open validation:** (a) direct per-frame star-count test on SorenVance's 6 frames comparing {v4 default; +DefocusAware-on; +NC4/Sens10} to confirm DefocusAware-on refills the 4 dead frames; (b) DefocusAware-on across the non-donut golden bank to confirm no recall/precision regression.

## 11. MAJOR CORRECTION — the detector settings are not SorenVance's root cause

A faithful per-frame reproduction overturns the premise of §1–§10 for *SorenVance specifically*. Running `focus-sweep --af-run` on his 6 frames at his **exact default settings** (Simple/Typical → NC2/Sens2/LAB-on; params dump confirms NoiseClippingMultiplier=2, StructureLayers=4, MinHFR=1.2), debayered to luminance from the frame's **own RGGB CFA** (ZWO ASI2600MC, `BAYERPAT=RGGB` in the XISF), gives:

| focuser | faithful re-detect (stars / candidates) | his LIVE AF (saved result.json) |
|---|---|---|
| 28495 | 871 / 19027 | **0 / 1** |
| 28695 | 782 / 14880 | **0 / 1** |
| 28895 | 235 / 13901 | 175 / 15807 |
| 29095 | 87 / 13727 | **0 / 1** |
| 29295 | 34 / 10627 | **0 / 1** |
| 29495 | 38 / 11199 | **0 / 1** |

Same detector commit (ec7fc34), same settings, same frames. The detector finds 34–871 stars at **every** position; his live AF collapsed to **1 candidate / 0 stars** at the 4 defocused positions. Near focus **agrees** (both ~175–235 at 28895); only **defocus diverges** → his live pipeline fed the detector a degenerate, near-featureless image at the defocused positions, while those same frames debayered directly are full of stars.

**Therefore:** SorenVance's AF break is **upstream of the star detector — in the OSC/narrowband defocused-frame image preparation (debayer/luminance) of his live AF path — not in NC/Sensitivity/LAB.** His settings already detect his stars. Reverting NC/Sens (R0) and DefocusAware-on (R3) would **not** have fixed him; his empirical "NC4/Sens10 fixed it" is suspect as a fix for this (coincidental/transient, or a debayer/image setting changed at the same time).

**Corollaries:**
- The bank-verify SorenVance rows (696-star flood) were **raw-Bayer-mosaic** detections (dev profile ≠ his camera; optimizer/bank loaders default `debayerToLuminance=false`, `DiagnosticUtil.LoadFloatMat`) — garbage for his case; disregard.
- §1's "candidate-formation collapse under the v4 preset" was inferred from his saved result.json without realizing the detector reproduces fine on the same frames + settings. The formation collapse is a property of the **image his live pipeline produced**, not the preset.
- The §2 finding (his settings exactly equal the v4 Simple/Typical preset) still stands — but is now a side observation, not the cause.

**Real next step:** reproduce his exact live image-prep (his NINA `ImageSettings`/debayer config, or a frame as his AF fed it) and diff the OSC debayer→luminance path feeding AF detection (`StarDetector.PrepareSrcImageFromRenderedImage` and the rendered-image/debayer pipeline) between v3.0.0.26 and v4.0.0.3 to find what degrades defocused narrowband OSC frames. The Simple-mode preset hardening (R0–R7) remains independently valid for the recall/donut trade-off, but is not SorenVance's fix.

## 12. Correction to §11 — it IS a donut-detection gap (DefocusAware), reconciled

§11's "settings are not the cause / it's debayer" conclusion was **wrong**, and the error was a flawed reproduction: `focus-sweep`/`bank-verify` detect on the **raw Bayer mosaic** (`LoadFloatMat` defaults `debayerToLuminance=false`; no runner passes true), and even the added `--debayer-luminance` path lacks the live **CFA hotpixel filter**. Both headless paths therefore **over-detect noise as false-positive "stars" at defocus**. Evidence: `--debayer-luminance` reports 1884 "stars" at his most-defocused position (28495) with **median HFR 1.46** — sharper than his near-focus frame (HFR ~3), and an inverted count profile (most "stars" at most defocus). Real heavily-defocused donuts are HFR ~8–12; HFR 1.46 features are noise.

**Reconciled root cause (consistent with §1's original mechanism and all bank evidence):**
- His heavily-defocused donut stars are **erased by the `StructureLayers=4` wavelet structure-removal** with `DefocusAwareStructure`/`DefocusAwareDonutDetection` **OFF** — candidate formation collapses.
- His live **CFA hotpixel filter + gates correctly rejected the noise**, so `StructureCandidates=1` / 0 real stars at the 4 defocused positions. The diagnostics (no CFA filter) instead keep the noise → thousands of false candidates. His live detection was behaving *correctly*; there were ~0 detectable real stars at those positions.
- Only 2 near-focus points remain → hyperbolic fit unconstrained → AF fails.

**Fix (re-lands on R3):** default **DefocusAwareStructure + DefocusAwareDonutDetection ON** in the Simple preset so heavily-defocused donuts form candidates. NC/Sensitivity do **not** address donut erasure (bank donut runs broken at all NC), so R0/the user's NC4/Sens10 revert is not the fix — it was likely coincidental, or he narrowed the sweep at the same time. A narrower AF sweep (smaller step × offset) is a complementary mitigation so the outer frames stay within detectable defocus.

**Definitive confirmation still owed (headless can't do it cleanly — noise false-positives at defocus):** enable HocusFocus `SaveIntermediateImages` on his rig, reproduce, then (a) inspect the prepared source image at a defocused position (expect a faint donut, not noise), and (b) re-run with DefocusAware structure+donut ON and confirm real candidates form. This supersedes §11.

## 13. FINAL (supersedes §11 and §12) — a live-runtime degeneracy, not reproducible from the saved frames

A faithful headless reproduction of the **exact live image-prep** (added to the diagnostics: `--debayer-luminance` + `--cfa-hotpixel` in `focus-sweep`, mirroring `StarDetector.PrepareSrcImageFromRenderedImage`) settles it. The harness is validated by **near-focus agreement**: at 28895 his live run formed `StructureCandidates=15807`; the reproduction forms ~16,967 — a match. But at the defocused frames:

| focuser | his LIVE `StructureCandidates` | reproduction (debayer+CFA) |
|---|---|---|
| 28895 (near) | 15807 | ~16,967 ✅ |
| 28495 | **1** | 19,616 |
| 28695 | **1** | 15,507 |
| 29095 | **1** | 14,290 |
| 29295 | **1** | 11,241 |

His defocused frames show `cand=1, tooSmall=0, Sigma=NaN` — the structure map / noise estimate **degenerated** (formed essentially nothing), whereas the same saved frames, debayered and CFA-filtered exactly as the live path does, form 12,000–22,000 candidates.

**Ruled out by direct reproduction:** detector settings (NC/Sens/LAB — normal images detect fine); debayer choice (mosaic, luminance, luminance+CFA all form thousands of candidates); and the §12 donut-erasure hypothesis (that yields *many* `TooSmall`-rejected candidates like his near-focus 13,764, not `cand=1, tooSmall=0`; `--defocus-aware` barely changed the reproduction). **§11 (debayer) and §12 (donut/DefocusAware) are both wrong.**

**Conclusion:** SorenVance's AF failure is a **degenerate-image / NaN-noise-estimate condition in his live runtime at the defocused positions**, and it is **not reproducible from the saved bank frames**. That points at his live capture/processing environment (NINA build, camera driver, or an image-prep step producing a degenerate defocused frame), not the detector settings or the plugin's saved-frame detection path.

**Next steps (need his runtime, not the bank):**
1. Get his `SaveIntermediateImages` output for a failing defocused frame — the exact image his detector saw (expect it to be degenerate: black/uniform/NaN), which reveals the runtime cause.
2. Capture his exact NINA + HocusFocus versions and camera/driver.
3. **Robustness guard (independently worthwhile):** the detector should detect a degenerate frame / NaN noise-estimate and fail loud (or fall back) rather than silently returning `cand=1 → 0 stars`, so one bad frame can't sink an AF run without explanation. This would have surfaced SorenVance's real problem immediately.

**Bearing on the Simple-mode work:** R0–R7 (the preset recall/donut trade-off and upgrade-path hazards) remain independently valid, but are **not** SorenVance's fix — his failure is unrelated to the Simple preset.

*Diagnostic flags added to TestApp for this analysis (`--debayer-luminance`, `--cfa-hotpixel`, `--defocus-aware` on `focus-sweep`; CFA option on `DiagnosticUtil.LoadFloatMat`) are OSC-debugging aids, not shipping-plugin changes.*

## 14. Replay evidence — `BrightnessSensitivity` must revert to the literal v3 `10` (supersedes the R0 "effective-v3" reasoning)

Replaying SorenVance's saved AF run through the plugin ("Replay Saved AF", his frames) settles the `BrightnessSensitivity` value empirically:

| Config | 29300 (defocused) HFR | AF curve |
|---|---|---|
| **v3.0.0.26** Typical | ~11.7 (on-curve) | clean, R²=1.00 |
| v4 + `NC=4, BS=2` (F4 default) | **~3.0** (huge outlier) | broken V near 29300 |
| v4 + `NC=4, BS=10` (v3 literal) | ~11.2 (on-curve) | clean, matches v3 |
| v4 + `NC=4, BS=5` | ~10.7, large error bar | fits, but worse |

**Mechanism:** `BrightnessSensitivity` is the SNR floor of the star-acceptance gate (`keep ⟺ flux > Sensitivity × σ`, `StarDetector.cs:1693`). At the low `BS=2` floor the detector admits small noise / partial-donut-arc detections on the defocused frame; their tiny HFRs drag the **median** HFR down (~3 instead of ~11), corrupting that sweep point. `BS=10` rejects them → the median reflects the real donuts.

**Why the F4 "effective-equivalent" (2.0) was wrong here:** F4's `×0.2` assumes the noise-reduction blur understates σ by ~4× — true for *white* noise. A defocused narrowband-OSC frame's variance is dominated by *structured* content (donut flux, sky gradient) that the blur barely reduces, so σ_honest ≈ σ_blurred (factor ~1) on this data. Empirically `BS=10` — not the predicted ~2.5 — matches v3, confirming the compensation is miscalibrated for this rig class. The literal v3 value is the correct interim.

**Bearing on §13:** the original live `cand=1 / 0-star` collapse remains a separate (likely runtime) event that does not reproduce on replay — but the `BS=2` F4 default **does** independently degrade AF on his defocused frames, so the detector settings *are* implicated in the AF-quality failure, contrary to §13's stronger "not settings" wording. Net: the interim revert (NC=4 **and** BS=10) is warranted on its own merits, and the runtime `cand=1` question is now secondary.

## Appendix — provenance

Diagnosis and the mechanism/hardening study were produced by a multi-agent workflow (`simple-mode-upgrade-hardening`, run `wf_4b4844cb-71c`): a code-grounded mechanism investigation (v4 vs `release/v3.0.0.26`) plus five hardening lenses, each adversarially verified against the source before synthesis. Code anchors above were cited by those agents against the current tree; re-verify line numbers before implementing.
