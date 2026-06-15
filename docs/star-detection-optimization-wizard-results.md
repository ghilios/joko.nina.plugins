# Star Detection Optimization Wizard — Empirical Results

Concise record of the verification runs for the optimization wizard. Design: see
`docs/star-detection-optimization-wizard-design.md`. Performance analysis:
`docs/star-detection-optimizer-performance-design.md`.

All runs used the offline harness `TestApp optimize --per-run`, which drives the **same**
`StarDetectionOptimizer` as the in-NINA wizard, against real saved AF sweeps loaded from the user's
NINA profile.

## Cross-setup verification (14-run bank)

`optimize --per-run` over a **14-run bank spanning 11 distinct optical setups**. Three runs are
deliberate copies — Workshop `sensitivity_example1`, `sensitivity_example2`, and `standard_example2`
are copies of the **fmeschia**, **Linwood**, and **CWhite** setups — so they double as a
**bit-identity check** (identical inputs → identical results across builds = detection unchanged by
the perf refactor).

### Outcome (summary; per-run detail in the harness `aggregate_summary.txt`)

| Metric | Result across all 14 runs |
|---|---|
| Hard floor (every frame ≥ 3 stars, incl. defocused extremes) | **14/14 PASS** (lowest observed min = **5**) |
| σ_focus (focus-position uncertainty) | **improved on all 14** (≈ **1.2× to 21×** tighter) |
| Seed Sensitivity 1.6 raised by the optimizer | **12/14** (the seed is too sensitive) |

### Findings

- **The seed Sensitivity (1.6) is too sensitive.** The optimizer raised it on 12 of 14 runs.
- **Very star-rich fields pinned the curated bounds**, so they were widened:
  - `Sensitivity` upper bound `20 → 50`
  - `StarClipping` bounds `[0.5, 5] → [0.25, 10]`
- **Bound widening is roughly a wash.** A re-verify after widening showed little net change — the
  real limiter is the **fixed coarse-grid resolution** over the (now wider) range, not the bound
  endpoints. Logged as a follow-up (coarse-grid-resolution vs bound-range tuning).

## Panos label diagnosis + defocus-aware-gate recovery

The Panos setup (long focal length) misses bloated/donut defocused stars. The interactive label loop
(`review --labels` → `diagnose-labels`) attributed each flagged miss to a specific cause:

| Flagged misses | Cause (from `diagnose-labels`) |
|---|---|
| **9 of 9 donut stars** | **REJECTED: TooDistorted** — killed by the `MaxDistortion` gate (exact match) |
| **4 of 5 dim misses** | **NO CANDIDATE** — structure-detection gaps (no candidate ever formed) |

### Recovery via the opt-in defocus-aware gates (default OFF)

The two size-scaled (size = defocus proxy) gate relaxations recover the distortion-gated donuts:

| `DefocusDistortionSizeReference` | Donuts recovered | Near-focus cost (centering gate) |
|---|---|---|
| **30 px** (safe production default) | **8 / 9** | **zero** |
| 20 px (harness `--defocus-size-ref 20`) | **9 / 9** | zero |

`DefocusAwareCentering` additionally recovers ring-unstable donut centroids whose centers drift. The
4/5 `NO CANDIDATE` dim misses are **not** fixable by gate relaxation — they need
structure-detection-algorithm work (follow-up).

Both gates are **opt-in, default OFF**; off returns `MaxDistortion` / `StarCenterTolerance` verbatim,
so detection stays **bit-identical**.

## Performance (before → after)

Early/late split + per-frame early-context cache + bounded parallel per-frame detection.
**Detection bit-identical** (confirmed, including the cross-setup copy-run check).

| Setup | Before | After | Speedup | Notes |
|---|---|---|---|---|
| CWhite | 19m 51s | 1m 30s | **13.2×** | 61 MP |
| Panos | 7m 02s | 44s | **9.6×** | `--max-evals 24` |

Net **~10–13×**. The earlier logging quick-win is result-neutral but gave **no measurable speedup**
(NINA `Logger` is buffered; detection compute dominates).

## F2 — Precision validation of the defocus-aware gates (follow-up T10)

`TestApp diagnose-labels` on **Panos** (the only setup with ground-truth labels; `attempt01.json` covers two
labeled frames — a near-focus frame @ focuser **32396** and a heavily-defocused donut frame @ **44396**), with
the combined defocus gate via `--defocus-distortion --defocus-centering` at `--defocus-size-ref` 30 (the
production default) and 20. 14 labeled boxes (5 `missed`, 9 `wronglyRejected`).

| Metric | Baseline (gate OFF) | size-ref 30 | size-ref 20 |
|---|---|---|---|
| `wronglyRejected` donuts no longer killed by `TooDistorted` | 0 / 9 | **7 / 9** | **9 / 9** |
| …of those, end-to-end **ACCEPTED** | 0 | **4** | **4** |
| …distortion-passed but then **`LowSensitivity`** (dim) | 0 | 3 | 5 |
| `missed` boxes recovered (ACCEPTED) | 0 / 5 | **1 / 5** | **1 / 5** |
| `missed` still **NO CANDIDATE** (structure gap) | 4 | 4 | 4 |
| Defocused frame @44396 accepted-star count | **6** | **10** (+4 real donuts) | **10** |
| **Near-focus frame @32396 accepted-star count** | **283** | **283** | **283** |

### Findings

- **Zero near-focus cost — proven, not just asserted.** The near-focus frame @32396 detects **283 accepted
  stars identically** with the gate OFF, at sr=30, and at sr=20. The relaxation factor is `clamp(sizeRef/size,
  minFactor, 1)`, which is exactly **1.0 for any candidate ≤ size-ref**, so small sharp near-focus stars are
  never relaxed — near-focus detection stays **bit-identical**.
- **Relaxing distortion does not flood.** On the donut frame the accepted count rises only **6 → 10**, and the
  4 new accepts are exactly labeled **real** donuts. The remaining distortion-passed donuts are dim and are
  caught by the **`LowSensitivity`** gate (3 at sr=30, 5 at sr=20) — the other gates backstop, so the
  distortion relaxation alone admits no junk en masse here.
- **Lowering size-ref below ~30 doesn't add end-to-end recall.** sr=20 lets all 9 wrongly-rejected donuts
  clear the distortion gate (vs 7/9 at sr=30), but the extra two then hit `LowSensitivity`, so the end-to-end
  ACCEPTED set is the **same 4** at sr=20 and sr=30. Below ~30 the limiter shifts from distortion to the
  **sensitivity** gate, not to more accepted stars. ⇒ **Recommended safe default size-ref = 30 (unchanged).**
- **4 / 5 `missed` boxes are `NO CANDIDATE`** (no candidate ever forms) at every size-ref — a structure-detection
  gap that gate relaxation cannot fix (the T13 spike).

### Implication for F3 (the objective precision penalty)

The gate **on its own** is safe: it is size-scaled (no near-focus effect) and the remaining gates backstop dim
junk, so it does not flood. The residual precision risk is only that the **optimizer**, if the gate is added
to its curated set, could *couple* the relaxed distortion with a **lowered sensitivity** threshold to chase the
`LowSensitivity`-rejected donuts — which together could admit junk on defocused frames. A precision /
false-positive penalty term in `OptimizationObjective` therefore remains a prudent **guardrail before** adding
the gate (and/or size-ref) to the optimizer's search — milder than the original "floods near-focus" fear, but
still warranted.

## F3 — Optimizer integration of the defocus gate + precision penalty (follow-up T11)

Added the combined `DefocusAwareGates` flag to the optimizer's curated set, guarded by a **multiplicative**
near-focus precision penalty in `OptimizationObjective` (`j *= SDefocusPrecision`). The penalty is **provably a
no-op when the gate is off** (returns exactly 1.0 when no star was relaxation-admitted), so per-evaluation `J`
and detection star counts stay **bit-identical** to the pre-F3 baseline (unit-tested against an independent
re-implementation of the old formula). The penalty's junk signal is *relaxation-admitted stars near best
focus* — by the gate's size-scaling, sharp near-focus stars are small and never need relaxation, so a large
relaxed candidate there is a junk blob; legitimate donut recovery on the defocused extremes is not penalized.

Verified on the bank (`optimize`, gate now in the search):

| Run | Seed J → Best J | σ_focus / recall | `DefocusAwareGates` | Hard floor |
|---|---|---|---|---|
| Panos (labeled) | 0.868 → **0.941** | σ 67.8 → **12.6 (5.4×)**, recall 0 → **0.417**, precision **1.0** | 0 → 0 (not selected) | PASS (min 24) |
| Panos (unlabeled) | 0.960 → 1.000 | — | 0 → 0 | PASS (min 13) |
| CWhite (no donuts) | 0.989 → 0.998 | — | 0 → 0 | PASS (min 258) |

### Findings

- **No regression, no flooding.** The gate stays **OFF** on the unlabeled and no-donut runs and `J` improved on
  all three — the gate+penalty never misfire. (Bit-identity is per-evaluation; the optimizer's *trajectory* can
  differ because the search space grew by one boolean, but it converges to ≥ the pre-F3 best.)
- **On labeled Panos the optimizer recovers recall via `Sensitivity`↓ + `StructureLayers`↑, not the gate** —
  a cheaper global path to the donuts (defocused-extreme star counts jumped, e.g. 44396: 6 → 24, with σ_focus
  5.4× tighter). The defocus gate remains **available and guarded** for cases where that path isn't viable.
- The unrecovered Panos misses are the **4/5 `NO CANDIDATE` structure gaps** (the T13 spike) — closed by
  neither the gate nor the sensitivity/structure changes.
- Penalty constants (`NearFocusWindowSteps`=1.5, `DefocusPrecisionThreshold`=0.20, `DefocusPrecisionStrength`=
  0.5, `DefocusPrecisionMinFactor`=0.5) are deliberately conservative and were **not triggered** here (the gate
  wasn't selected, so nothing was relaxation-admitted). They will be exercised further once **should-reject
  (precision) labels** are added in a follow-up session.
