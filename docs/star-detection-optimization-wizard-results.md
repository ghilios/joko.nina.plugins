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

## F6 — Coarse-grid resolution vs bound range (follow-up T12): investigated → wash → reverted

Prototyped a per-axis adaptive coarse grid (Phase-A levels scale with each axis's range so the grid spacing is
consistent regardless of bound width). Verified before/after (fixed 4-level grid vs per-axis adaptive) on the
bound-pinned setups at 150 evals:

| Setup | frames × size | Sensitivity opt (before → after) | Outcome |
|---|---|---|---|
| CWhiteFocus | 9 × 122 MB | 50 → 50 (upper bound) | unchanged (both grids sample the bound) |
| timmer | 18 × 52 MB | 50 → 50 (upper bound) | unchanged |
| muggsie | 9 × 23 MB | 16.667 (coarse node) → 18.75 (fine node) | J 0.99685 → 0.99655, σ 10.96 → 11.45 (marginally **worse**) |
| mccomiskey | 9 × 113 MB | 16.667 (coarse node) → not tested (~24 min/run) | — |

**Finding:** the finer grid shifts *which* node the interior optimum lands on but does **not** improve J/σ — on
muggsie it was marginally worse (σ +4.5%). This **confirms the original "bound-widening was ≈ a wash"** result:
**coarse-grid resolution is not the limiter.** Reverted (keeps the optimizer simple and the documented baselines
intact).

**The real limiter (and the cause of the long 24–38 min optimize runtimes on big sensors):** the compass search
re-probes the **5 early (wavelet/structure) params** (`NoiseClippingMultiplier`, `StructureLayers`,
`NoiseReductionRadius`, hotpixel ×2) on **every sweep**; each early probe is a full per-frame early-context
rebuild that the per-frame cache (one context per frame) cannot amortize. The ~10–13× perf win applies only to
the **late-only** moves. Quantified below.

## Cache-health investigation (follow-up T12b)

Triggered by the 24–38 min runtimes above being ~25× slower than the documented "CWhite 1m30s". **Verdict:
inherent, NOT a regression** — the early/late cache and per-frame parallelism both work exactly as designed;
nothing in the F1–F3 work broke them. Evidence (instrumented muggsie run, 9 frames, 150 evals, 143 s):

- **Parallelism healthy.** `Environment.ProcessorCount = 48` as seen by the TestApp process; per-frame degree =
  `min(frameCount, max(2, ProcessorCount/4))` = 9 (frame-count-capped). Frames detect concurrently (1350 gate
  calls = 150 evals × 9 frames), not serialized.
- **Cache healthy, no regression.** The early key is an allow-list (`StarDetector.cs` `EarlyCacheKeyProperties`,
  11 props); late moves HIT, early moves MISS. The recently-added `DefocusAware*` / `RelaxationAdmittedCount` /
  `DefocusAwareGates` are all LATE/informational and correctly **absent** from the early key (`StarDetectorVersion`
  still 1). The 5 early axes + the 1-context-per-frame cache bound predate this branch.
- **The cache thrashes.** **46% of detections (621/1350) forced a full early rebuild** at **~1650 ms/frame vs
  ~53 ms for a late gate (~31×)**; builds were **93.5%** of CPU. Mechanism: the cache holds **one** context per
  frame, so each early-axis probe both rebuilds for the probe **and evicts the incumbent**, forcing the next
  late probe to rebuild the incumbent too (~50% miss/sweep). If it amortized as designed (~50 builds vs 621),
  that's the ~12× = documented headline — which only holds when few early moves are explored.
- On muggsie, J barely moved (0.9944 → 0.9969) for 1025 s of build CPU — most early-axis re-probing is wasted
  work on an already-good setup.

**Scoped fix options (ranked; not implemented here):** **(A)** stage Phase B — refine the 8 late axes first with
the early context pinned (near-100% cache hits), then a bounded early-axis pass → **~5–10×** on big sensors,
low-moderate risk (trajectory changes; verify quality-neutral). **(B)** bounded multi-context LRU per frame (2–3)
so an early probe doesn't evict the incumbent → **~2×**, bit-identical results, but ~6.6 GB peak at 61 MP (needs
a memory guard). **(C)** early-stop early-axis probing after K non-improving sweeps → **~2–4×**, low-moderate risk.
A captures most of the win with the least memory risk; A+B is best.

> **Update (T14 — implemented):** option **(A)** shipped in commit `a765c9a` as the staged Phase-B compass
> search (LATE axes refined first with the early context pinned, then a bounded EARLY stage). The headline ~6.7×
> matches the projected ~5–10×. See `docs/star-detection-optimizer-performance-design.md` for the design write-up.

## G2 — Structure-boost validation (`DefocusAwareStructure` / `StructureLayerBoost`, follow-up T13)

The defocus-aware **structure** relaxation (the EARLY-stage candidate-formation knob, distinct from the two LATE
`DefocusAwareGates`) shipped in PR #61 but had **never been A/B-tested on data** — F2/F3 had recorded the 4/5
missed Panos boxes as an undifferentiated "`NO CANDIDATE` structure gap." This closes that out. The
`DefocusAwareStructure` flag computes the large-structure wavelet residual at `StructureLayers + StructureLayerBoost`
**extra** layers (coarser removal), so a heavily-defocused donut survives the subtraction and forms a candidate
instead of being erased. Tested via two new **`diagnose-labels`** switches (`--defocus-structure` / `--structure-boost`,
plus a diagnostic `--structure-layers` override) on **Panos** `attempt01` (labeled near-focus frame @ **32396** and
donut frame @ **44396**). The profile (`AA1600MM`) has since drifted to *post-optimization* settings
(`Sensitivity`=15.67, `StructureLayers`=5 — the F3 path), so the baseline `StructureLayers` is pinned to the factory
**4** (where the gap is open) to isolate the mechanism.

| Config (SL=4 baseline + flag) | near-focus @32396 acc/rej | donut @44396 acc/rej | missed `NO CANDIDATE` |
|---|---|---|---|
| baseline (flag OFF) | 182 / 778 | 0 / 240 | **4** / 5 |
| `DefocusAwareStructure` ON, **boost 0** (control) | 182 / 778 | 0 / 240 | 4 / 5 |
| `DefocusAwareStructure` ON, **boost 1** (eff. 5) | 202 / 803 | 0 / 333 | **2** / 5 |
| `DefocusAwareStructure` ON, **boost 2** (eff. 6) | 197 / 821 | 1 / 435 | 2 / 5 |
| `DefocusAwareStructure` ON, **boost 3** (eff. 7) | 204 / 811 | 1 / 450 | 2 / 5 |
| (cross-check) `StructureLayers`=5 global, flag OFF | 204 / 679 | 0 / 259 | 2 / 5 |

### Findings

- **Bit-identical OFF — proven, not just asserted.** `DefocusAwareStructure=ON` with **boost 0** is *byte-identical*
  to the flag OFF (182/778, 0/240, same 4 `NO CANDIDATE`): `Math.Max(1, StructureLayers + 0) == StructureLayers`, so
  the wavelet depth is unchanged. Default-OFF detection is unaffected.
- **The mechanism works — it recovers the structure-gap *donuts*.** boost 1 drops `NO CANDIDATE` from **4 → 2**: the
  two recovered boxes are exactly the **large donuts** at @44396 (label boxes **47×50** and **47×43** px) — they go
  from forming no candidate to forming one. The two *unrecoverable* boxes are the faint **near-focus** stars at @32396
  (12×12 px): a **sensitivity floor**, not a structure erasure — coarser large-structure removal can't and shouldn't
  resurrect them. (So the F2/F3 "4/5 structure gap" was really **2 structure-recoverable donuts + 2 faint-star floor**.)
- **Recovery is necessary but not sufficient for end-to-end recall.** The recovered donut forms only a **thin ring
  fragment** (measured 3–4 px vs the `TooSmall` threshold 6), so it is admitted as a candidate but then re-rejected
  (`TooSmall`), and the donut-frame ACCEPTED count stays 0–1. Structure boost is **complementary** to the gates: it
  moves a star from "no candidate at all" into the gate pipeline, where `DefocusAwareGates` (+ a lower `TooSmall`/
  `Sensitivity`) would have to finish the job. On its own it adds no accepted stars on Panos.
- **boost 1 is the sweet spot; higher adds churn, not recall.** boost 2/3 recover **no additional** donuts (still 2
  `NO CANDIDATE`) while inflating the donut-frame candidate pool (333 → 435 → 450) and perturbing the near-focus
  frame more — i.e. more work, more junk to backstop, no extra real stars.
- **Near-focus cost is modest, not a flood.** Turning the flag on is a *global* depth change (not per-star), so
  near-focus is **not** bit-identical when ON: @32396 accepted moves 182 → 202 (+11%) and rejected 778 → 803. That
  is a real but contained change (the other gates backstop the extra candidates), nowhere near a flood. This is why
  the flag is correctly **opt-in / default-OFF**.
- **Relation to the optimizer's global path.** Raising `StructureLayers` to 5 *globally* (the F3 optimizer path, now
  baked into the profile) reaches the same `2/5 NO CANDIDATE` for the donut frame, because the residual is computed at
  5 layers either way; the only difference is the post-wavelet blur radius (boost keeps it at the nominal 4 ⇒ a few
  more small candidates: 803 vs 679 near-focus rejected). So `StructureLayerBoost` is **not** a more-targeted relaxation
  than `StructureLayers↑` — both are global — it just decouples the residual depth from the blur radius.

**Recommendation.** Keep **default-OFF** (correct as shipped). For users whose heavily-defocused frames *lose donut
stars entirely* (not merely reject them), `DefocusAwareStructure` + `StructureLayerBoost`=**1** is the effective
setting — it reopens the structure gap for large donuts — but it should be paired with `DefocusAwareGates` and a
modestly lower `TooSmall`/`Sensitivity` to carry the recovered fragments through to ACCEPTED; boost > 1 is not worth
it. The remaining faint near-focus `NO CANDIDATE` misses are a sensitivity floor, outside this feature's reach. The
new `diagnose-labels --defocus-structure/--structure-boost/--structure-layers` switches make this re-runnable on any
labeled setup.

### Still open (G1 — precision penalty, deferred)

The `SDefocusPrecision` near-focus precision penalty (F3) and its constants remain **exercised only by unit tests** —
no `should-reject` (false-positive) labels exist yet, on Panos or any setup, so the penalty has never *bitten* on real
data and its constants (`NearFocusWindowSteps`=1.5, `DefocusPrecisionThreshold`=0.20, `DefocusPrecisionStrength`=0.5,
`DefocusPrecisionMinFactor`=0.5) are untuned-against-data. Closing this needs a near-focus-heavy setup labeled with
`should-reject` boxes (click an **accepted** star in `TestApp review` or the in-wizard review); then
`optimize --labels` should show the labeled loop improves recall *without* tanking precision and the penalty bites
when relaxation admits near-focus junk. Deferred to a future session pending those labels.
