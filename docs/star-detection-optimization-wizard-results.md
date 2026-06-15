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
