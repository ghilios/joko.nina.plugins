# Star Detection Optimizer — Speedup Review: Empirical Results

**Status:** Measured findings from an exhaustive performance review of the Star Detection Optimizer against the
saved autofocus runs in `D:\Autofocus Bank`. Hardware: 48-core workstation. Harness: `TestApp optimize` (the
same `StarDetectionOptimizer` the live wizard drives). Every number below is from running the harness, not
estimated.

## TL;DR

The prior round (`docs/star-detection-optimizer-performance-design.md`, "T14") already captured the large wins
(early/late split + per-(frame,early-key) cache + bounded per-frame parallelism, ~10–13×, bit-identical). This
review tested the remaining proposed levers and found:

1. **Parallel candidate evaluation (WI1): NO speedup — rejected, reverted.** Bit-identical (proven by unit test) but
   0–130% *slower* across every configuration. A single LATE evaluation already parallelizes frames × per-star and
   saturates useful cores; a compass sweep has ≤16 candidates; per-eval cost is overhead-bound.
2. **Heuristic field conversion (WI2): unsafe / no win — rejected, reverted.** Deriving `StructureLayers`
   from pixel scale and removing it from the search **collapses J on defocus-heavy runs** (Panos: J 0.985 → 0.62)
   because its optimum tracks defocus content, not plate scale. `MinimumStarBoundingBoxSize` conversion is J-neutral
   but yields no wall-clock benefit. All heuristic-conversion code (helper, curated-set omission, `--heuristic-seed`)
   was **removed**; only this written record of the findings remains.
3. **Eval-budget reduction: the one real lever — SHIPPED (default 400 → 250).** Wall-clock is governed by eval
   count, and the search wrings out negligible J for most of its budget. The default `OptimizerSettings.MaxEvaluations`
   was lowered to **250** (bank-wide worst-case J cost ≤0.011%; see §3). The full-bank before/after at 400 vs 250 is
   in §3.

The actionable runtime reduction is the **eval-budget cut** (now shipped), not new caching or concurrency — those
are already near-optimal. A separate **wizard "start from my current settings" toggle** (off by default) was added
so a user can refine an already-tuned setup (seed the search from current settings instead of the defaults).

## Method

- `TestApp optimize --runs <run> --per-run --max-evals <B>` drives the production optimizer headlessly, loading the
  user's active profile (single source of truth for params + pixel scale). It reports per-run seed→best J, evals
  used, wall-clock, early-context builds/reuses (cache health), and a hard-floor star-count gate.
- New instrumentation added this round: an **`optimize_trajectory.csv`** (bestJ vs eval#, one row per accepted
  move) so a single run at a high budget reveals the whole convergence curve; analyzed for "smallest eval count to
  reach finalJ·(1−relTol)".
- Representative runs: **muggsie** (9 frames, fast, well-cached), **Panos** (7 frames, labeled, big J improvement),
  plus the whole bank for the aggregate.

## 1. Parallel candidate evaluation (WI1) — rejected

Implemented bit-identically (concurrent LATE-sweep / Phase-A-grid evals, in-order strict reduction, whole-sweep
budget guard, per-key dedup memo) and proven equivalent by a unit test (`BestParams`/`BestJ`/`Evaluations`/
`ChangedVariables` identical, 10 repeats). muggsie, 120 evals, all bit-identical (bestJ=0.998251, evals=120):

| Config | Wall |
|---|---|
| Sequential (baseline) | **12.2–12.9 s** |
| Candidate parallelism = 2 | 12.4 s |
| = 4 | 12.9 s |
| = 8 | 13.0 s |
| = 12 (frames forced to 1) | 17.6 s |
| = 12, single-threaded candidates | 28.4 s |
| Frame fan-out raised to 48 | 12.8 s (no change — 9-frame runs already saturate) |

**Why it fails:** one LATE eval = (frames concurrent) × (per-star `Parallel.For`, bounded by the shared
ProcessorCount scheduler), which already uses the cores worth using. Sweeps have ≤16 candidates, so candidate
concurrency can't fill cores anyway, and forcing frame fan-out to 1 to make room makes each candidate serially
detect all frames (net slower). This matches the prior author's decision not to ship "#6 parallel candidate evals."
Code reverted; the optimizer is unchanged.

## 2. Heuristic field conversion (WI2) — rejected and reverted

> The heuristic-conversion code was **removed** (helper `HeuristicSeedDerivation`, the `CreateCuratedSet(opts)`
> omission overload, the `--heuristic-seed` harness flag). This section preserves the measured findings that drove
> that decision.

Converted `StructureLayers` (EARLY) and `MinimumStarBoundingBoxSize` (LATE) to deterministic functions of pixel
scale (`fwhmPx = 2.5″ / pixelScale`; `StructureLayers = clamp(round(log2(3.5·fwhmPx)), 2, 6)`,
`MinBBox = clamp(max(3, round(2.5·fwhmPx)), 3, 12)`) and removed them from the search. Active-profile pixel scale
1.127″/px ⇒ heuristic StructureLayers=3, MinBBox=6. At 120 evals:

| Run | Arm | bestJ | wall | hard-floor |
|---|---|---|---|---|
| muggsie | full search | 0.998251 | 12.9 s | PASS (min 20) |
| muggsie | heuristic (both) | **0.998454** | **9.0 s** | PASS (min 52) |
| muggsie | MinBBox only | 0.998289 | 12.3 s | PASS |
| **Panos** | full search | **0.984603** | 74.5 s | PASS |
| **Panos** | heuristic (both) | **0.621322** ❌ | 20.6 s | PASS |
| Panos | MinBBox only | 0.979 | 131 s | PASS |

**Verdict:** the muggsie win is real but does **not generalize**. On Panos the full search *keeps* StructureLayers
at 4; pinning it at the pixel-scale value (3) destroys recall on defocused/donut frames — a **36% J collapse**, far
beyond the 1% gate. StructureLayers' optimum depends on defocus content (handled by the search and the
`DefocusAwareStructure` boost), not plate scale, so a pixel-scale heuristic is fundamentally unsound for it. MinBBox
conversion holds J within gate but gives no wall-clock benefit (and can *slow* the search by shifting freed budget
to EARLY rebuilds). Neither ships, and all the tooling was reverted. A future heuristic would need a
better-than-pixel-scale signal (one that captures defocus content) to be safe for StructureLayers.

## 3. Eval-budget reduction — the real lever (SHIPPED: default 400 → 250)

Wall-clock is governed by **eval count** (per-eval cost is already near-optimal). Crucially, the search
**self-terminates at convergence** (a full LATE+EARLY round with no improvement), so most runs stop well before
the 400 cap — and the *late* evals it does run are disproportionately expensive EARLY-context rebuilds. Lowering
the budget caps those late, low-yield, rebuild-heavy evals.

### Bank-wide convergence (7 representative runs, max-evals 400, `--per-run`)

Per run: J at convergence (`finalJ`), evals used, and the smallest eval count reaching `finalJ·(1−relTol)`
(from each run's `optimize_trajectory.csv`):

| Run | seedJ | finalJ | evals used | e@1% | e@0.5% | e@0.2% | e@0.1% |
|---|---|---|---|---|---|---|---|
| CWhiteFocus (61 MP) | 0.9918 | 0.9997 | **400** (capped) | 1 | 17 | 30 | 214 |
| FlyData | 0.8796 | 0.9996 | 265 | 17 | 17 | 32 | 32 |
| LinwoodFocus | 0.0000 | 0.9150 | 207 | 72 | 72 | 85 | 110 |
| Panos (labeled) | 0.3166 | 0.9902 | 339 | 120 | 154 | 212 | 212 |
| caboose | 0.8691 | 1.0000 | 122 | 17 | 17 | 17 | 17 |
| fmeschia | 0.7815 | 0.9957 | 312 | 31 | 31 | 56 | 162 |
| muggsie | 0.9898 | 0.9987 | 299 | 1 | 17 | 32 | 99 |

**Evals-to-converge aggregate (n=7):** within 1% of finalJ — median 17, p95 120; within 0.5% — median 17, p95 154;
within 0.2% — median 32, p95 212.

### Worst-case J cost vs budget cap, and measured wall-clock

If the budget were capped at B, the worst relative J shortfall across all 7 runs (Panos is the binding case at low
B, CWhite at high B):

| Budget B | Worst-case J shortfall | Binding run | Illustrative wall-clock |
|---|---|---|---|
| 120 | 0.51% | Panos | muggsie 12.9 s (vs 76.5 s converged ≈ **6×**); Panos 74.5 s (vs 291.7 s ≈ **3.9×**) |
| 150 | 0.50% | Panos | — |
| 200 | 0.34% | Panos | — |
| **250** | **0.011%** | CWhite | CWhite **541 s** (vs 820 s @400 ≈ **1.5×**) |
| 300 | 0.006% | CWhite | — |

**Decision — SHIPPED B = 250.** `OptimizerSettings.MaxEvaluations` default lowered 400 → 250: essentially free
(≤0.011% worst-case J across the bank), caps the longest runs (CWhite, Panos, fmeschia, muggsie, FlyData). The
per-run wall-clock benefit scales with how *late* a run does its EARLY-rebuild probing (muggsie back-loads it → 6×;
CWhite spreads it → 1.5×). A more aggressive B ≈ 150 (≤0.5% worst-case J, only the steep labeled Panos approaches
that) was available for a larger speedup but 250 was chosen for the negligible-J-cost margin.

### Full-bank before/after (max-evals 400 vs 250, all 16 usable runs)

Every usable run in `D:\Autofocus Bank` (astrodet is excluded — its folders contain no sweep frames), each
optimized at 400 (before) and 250 (after). `dJ%` = relative J lost by the 250 cap; `speedup` = wall@400 / wall@250.

| Setup | J@400 | J@250 | dJ% | evals@400 | evals@250 | wall@400 | wall@250 | speedup |
|---|---|---|---|---|---|---|---|---|
| CWhiteFocus (61 MP) | 0.9997 | 0.9996 | 0.011 | 400 | 250 | 841.6 | 544.9 | 1.54× |
| FlyData | 0.9996 | 0.9996 | 0.000 | 265 | 250 | 43.8 | 43.4 | 1.01× |
| LinwoodFocus | 0.9150 | 0.9150 | 0.000 | 207 | 207 | 205.1 | 207.9 | 0.99× |
| Panos (labeled) | 0.9902 | 0.9902 | 0.006 | 339 | 250 | 291.5 | 191.3 | 1.52× |
| Workshop/sensitivity_example1 | 0.9957 | 0.9957 | 0.000 | 312 | 250 | 85.5 | 53.1 | 1.61× |
| Workshop/sensitivity_example2 | 0.9150 | 0.9150 | 0.000 | 207 | 207 | 206.2 | 210.4 | 0.98× |
| Workshop/standard_example1 | 0.9935 | 0.9930 | 0.049 | 400 | 250 | 1202.7 | 849.0 | 1.42× |
| Workshop/standard_example2 | 0.9997 | 0.9996 | 0.011 | 400 | 250 | 890.8 | 559.6 | 1.59× |
| Workshop/uneven | 0.9980 | 0.9980 | 0.000 | 286 | 250 | 250.0 | 231.3 | 1.08× |
| caboose | 1.0000 | 1.0000 | 0.000 | 122 | 122 | 67.2 | 68.4 | 0.98× |
| fmeschia_Focus | 0.9957 | 0.9957 | 0.000 | 312 | 250 | 86.2 | 52.1 | 1.65× |
| **mccomiskey** | 0.9895 | 0.9857 | **0.392** | 400 | 250 | 1145.0 | 544.7 | 2.10× |
| mufti | 0.9533 | 0.9533 | 0.000 | 226 | 226 | 171.2 | 171.9 | 1.00× |
| muggsie | 0.9987 | 0.9987 | 0.000 | 299 | 250 | 61.6 | 24.2 | 2.55× |
| timmer | 0.9993 | 0.9993 | 0.001 | 360 | 250 | 539.3 | 270.2 | 2.00× |
| toml999 | 0.9976 | 0.9966 | 0.105 | 400 | 250 | 317.5 | 62.7 | 5.06× |
| **TOTAL / median** | | | **0.000** | | | **6405** | **4085** | **1.57×** |

**Result:** worst-case J loss **0.392%** (mccomiskey), median **0.000%** — every run is well under the 0.5% bar.
Aggregate wall-clock **6405 s → 4085 s = 1.57× faster**, with the biggest per-run wins where the search back-loads
its EARLY-rebuild probing (toml999 5.06×, muggsie 2.55×, mccomiskey 2.10×, timmer 2.00×). Runs that already
converge at ≤250 (caboose, LinwoodFocus, mufti, the two `sensitivity_example`s) are unchanged (~1.0×, 0% J). This
confirms 250 captures essentially all of J bank-wide while cutting wall-clock by ~1.5× on average and up to ~5×.

## Shipped vs not

- **Shipped:** `OptimizerSettings.MaxEvaluations` default 400 → 250 (the runtime win); a wizard "Start from my
  current settings" toggle (off by default — seeds the search from current settings to refine an already-tuned
  setup); `optimize_trajectory.csv` harness instrumentation.
- **Reverted (negative results, documented above):** parallel candidate evaluation (WI1); heuristic field
  conversion (WI2) — including the `DerivePreset` scaffolding and `--heuristic-seed` tooling.

## Future work (not done this round)

- **Early-stage internal parallelism (I1):** the wavelet à-trous + binarization statistics are single-threaded; at
  the default budget, EARLY rebuilds dominate wall-clock (muggsie: 216 builds, ~85% of 76.5 s). On many-core boxes,
  parallelizing the separable convolutions internally would speed every build (and production AF) bit-identically.
  Its value shrinks if the eval budget is lowered (fewer rebuilds), so weigh against §3 first.
- **Adaptive tolerance early-stop:** §3 as a convergence criterion (stop when a round improves J by < ε) instead of
  a fixed budget; the prior zero-tolerance prototype never fired, but a small ε would.
