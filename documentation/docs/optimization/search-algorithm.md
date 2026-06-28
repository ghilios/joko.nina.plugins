# Search Algorithm

The optimizer that powers the Star Detection Optimization Wizard does not use a smooth solver like
gradient descent or Nelder–Mead. The star-detection objective is **piecewise-constant**: detection
*counts* stars crossing thresholds, so the score jumps as stars enter or leave the accepted set, and
gradients are undefined. A smooth solver would chase that quantization noise. Instead the optimizer
runs a **deterministic, derivative-free, memoized staged compass (pattern) search**: it probes each
axis by fixed steps, keeps only strictly-improving moves, and shrinks the steps as it homes in.

This page describes *how* the search moves. The score it maximizes is on
[Objective function](objective-function.md); the knobs it is allowed to move and their bounds are on
[Search variables](search-variables.md).

!!! note "Where this lives"
    The engine is `StarDetectionOptimizer.OptimizeAsync`. It is a pure function over an injected
    *evaluator* delegate (candidate parameters → per-run metrics): it never loads images, runs
    detection, or touches the UI. The same engine drives the live wizard and the offline `TestApp
    optimize` harness.

## Three guarantees

The whole search is built around three properties that the design relies on.

- **Deterministic.** There is no random number generator. The seed is read from a fixed starting
  vector: by default the **default** detection parameters, or your **current** settings when you
  choose *"Start from my current settings"*. Axes are visited in a fixed curated order, and the two
  probe directions are always tried `+` then `−`. Detection and the curve fit are themselves
  deterministic, so the same inputs always produce the same optimized result.
- **Never-regress.** The seed is the initial *incumbent* and the floor: only **strictly-improving**
  moves are ever accepted (ties move nothing), so the optimized result can never score worse than the
  seed. The wizard layers one more guarantee on top. It compares the result against your **current**
  settings and will not hand back anything worse than them, so the worst case is "no change." (When you
  start from current settings, the two floors coincide.)
- **Memoized.** Every candidate is keyed on `StarDetector.ComputeCacheKey`. A point the search
  revisits is served from a cache and never re-invokes the expensive evaluator. The evaluation
  budget counts only cache *misses*.

![Staged compass / pattern search trajectory on a 2D objective surface](../assets/figures/compass-search.png){ width=620 }

*The compass search starts at the seed, probes each axis by \(\pm\)step, steps to the best improving
neighbor, and halves the step when a sweep finds nothing — converging on a local optimum.*

## The search, end to end

The run proceeds in two phases after the seed is scored.

### Seed

Each variable is read from the seed parameters (the default settings, or your current settings if you
chose to start from them) and quantized to a legal value (integers rounded, booleans thresholded at
0.5). That vector is evaluated once. Its score, \(J_{\text{seed}}\), is both the starting incumbent and
the never-regress floor.

### Phase A — coarse grid

Detection has two basins that a local search alone can get stuck between: a "few bright stars" basin
and a "many faint stars" basin. Phase A escapes a bad starting basin with a coarse grid over the
**two highest-impact axes**, `Sensitivity` × `StarClippingMultiplier`. It evaluates a
`CoarseGridLevels`-by-`CoarseGridLevels` grid (default **4×4 = 16 points**), with every other variable
held at the incumbent. The levels are evenly spaced and inclusive of both bounds: level 0 is the
lower bound, the last level is the upper bound. The best strictly-improving grid point becomes the
new incumbent; if none beats the seed, the incumbent is unchanged.

!!! tip "Why these two axes"
    `Sensitivity` and `StarClippingMultiplier` are the two knobs that most strongly control how many
    candidates survive into the accepted set, so they define the basin. Gridding them first means the
    finer Phase-B search starts from a sensible region instead of refining a poor one.

### Phase B — staged compass search

Phase B refines the incumbent with a compass search, but it **stages** that search so it stays fast.
The curated axes are partitioned into two groups by their effect on the detection pipeline:

| Group | What a move does | Members |
|---|---|---|
| **EARLY** | Forces a full re-detect: rebuilds *and* evicts the cached per-frame detection context (the expensive wavelet / binarization / candidate-collection stage) | `HotpixelThresholdingEnabled`, `HotpixelThreshold`, `NoiseReductionRadius`, `NoiseClippingMultiplier`, `StructureLayers`, `DefocusAwareStructure` (plus internal context keys such as the saturation threshold and region) |
| **LATE** | Re-runs only the cheap gate-and-measure step against an already-built context (a cache hit) | everything else — the gate/measure knobs and the synthetic `DefocusAwareGates` switch |

Each stage is itself a full compass over its subset:

1. From the incumbent, propose `±step` on every axis in the subset (integers: `±max(1, round(step))`;
   booleans: a single flip; continuous: `±step`), in the subset's fixed order.
2. Evaluate them all and accept the **single best strictly-improving** move, then re-sweep from there.
3. A sweep that finds no improving move **halves every continuous step in the subset** and sweeps
   again.
4. The stage ends when all continuous steps fall below their floor (`StepFloorFraction × InitialStep`,
   with `StepFloorFraction = 0.125`, one-eighth of the starting step), or, for an all-discrete
   subset, when a sweep finds no improving move, or when the budget is exhausted.

The two stages alternate in an outer loop:

- **LATE stage** runs first and refines cheaply with the early parameters pinned, so every late probe
  is a per-frame cache hit.
- **EARLY stage** then runs one bounded compass over the early axes. The next late stage refines from
  the rebuilt-once early context, again via cache hits.
- The **outer loop** repeats LATE → EARLY while a full round improves and budget remains. The early
  stage is **permanently skipped** once an early stage yields no improvement, so the expensive axes
  are not re-probed indefinitely; the loop then stops as soon as a late-only round can no longer
  improve.

!!! note "Staging changes the order, not the result"
    Splitting the axes into LATE and EARLY subsets only changes *which order* candidates are visited,
    not which points are reachable. With no early axes the early stage is a no-op (pure late search);
    with no late axes the late stage is a no-op (pure early search). The never-regress and determinism
    guarantees hold regardless.

## Convergence and the budget

A continuous axis halves its step each fruitless sweep (`InitialStep`, then half, then a quarter)
until it passes the `0.125 × InitialStep` floor. With three halvings reaching that floor, each axis
gets a coarse-to-fine refinement and then stops; the search terminates rather than oscillating on
quantization noise.

Two hard stops bound the work:

- **The step floor** ends each compass stage once every continuous step is below its floor (and an
  all-discrete subset ends as soon as a sweep finds nothing).
- **The evaluation budget**, `MaxEvaluations = 250` by default (raised to **400** when *Recover
  out-of-focus donut stars* is enabled, since that unlocks extra defocus axes to explore; overridable
  in the harness via `--max-evals`), caps the number of *distinct* candidate evaluations. The search
  never starts an evaluation past the cap. Because of memoization, revisited candidates do not count
  against it.

## What a single evaluation costs

One evaluation scores a candidate against the run the wizard is optimizing. The evaluator
(`RunEvaluationData`):

1. **Detects every frame** in the run, concurrently but capped (default \(\approx \max(2,
   \text{ProcessorCount}/4)\) frames in flight) to fill idle cores during the largely single-threaded
   early stage without exhausting memory. Results are assembled by frame index, so the outcome is
   identical to a sequential loop regardless of how the frames interleave.
2. **Pools frames** that share a focuser position into one scatter point (mean HFR, SEM-pooled error),
   matching the auto-focus engine's averaging.
3. **Fits the focus curve** with your run's actual AF fit settings, requiring at least
   `MinPositionsForFit = 3` distinct focuser positions; too few yields a NaN focus σ and the objective
   hard-fails that run gracefully.
4. Reads \(\sigma_{\text{focus}}\), \(R^2\), and reduced \(\chi^2\) off the winning fit, plus recall
   and precision when labels exist.

These metrics feed the composite objective \(J\) for the run (see
[Objective function](objective-function.md)).

### The early-context cache — why staging matters

Detection compute dominates wall-clock: a full-frame wavelet plus binarization-statistics plus
gate-and-measure pass. The detector is split into a cacheable **EARLY** phase (depends only on the
early parameters, ~78% of a detection) and a cheap **LATE** phase (the gate/measure step, ~22%).
`RunEvaluationData` caches the early context per (frame, early-key) and reuses it across candidates
that change only late-stage gates, which is the bulk of a compass search. This is exactly why the
LATE stage runs first and pins the early parameters: those probes are all cache hits.

The cache is bounded to **one context per frame** (the current early key); when a frame's early key
changes the prior context is disposed before a new one is built (at 61 MP each context pins roughly
244 MB). The split is value-preserving (detection output is **bit-identical** to the uncached path),
so the speedup is free. Measured end to end, the early/late split plus bounded parallelism delivers
**~10–13×** faster optimization on real AF banks.

!!! example "Reading the result"
    `OptimizeAsync` returns the best parameters, \(J_{\text{seed}}\) and \(J_{\text{best}}\), the
    number of evaluations spent, an `ImprovedOverSeed` flag (true only when \(J_{\text{best}} >
    J_{\text{seed}}\)), and a `ChangedVariables` list: every axis whose optimized value differs from
    the seed, with both values. The wizard summary surfaces exactly these changes, so every move the
    search made is reversible and auditable.

## Step-size recommendation

The recommended auto-focus step size is **derived from the winning fit, not searched** — it is not one
of the optimizer's variables. From the fitted curve, the recommender finds the focus-sensitive
half-width \(W\): the offset from best focus at which the modeled HFR reaches three times the minimum HFR
(averaged over the two sides to handle an asymmetric model). It then sets the step to
\(W / 3.5\), so a sweep lands roughly 3–4 measurement points per side inside the band where the curve
carries the most slope, with a default of 4 offset steps per side. The result is clamped to at least
1 and, when supplied, to the focuser's maximum step. A degenerate fit returns your current step size
unchanged.

See [Step-size recommendation](step-size.md) for the full derivation and figure.

!!! warning "It applies to future runs"
    A replayed sweep cannot be re-sampled at a new spacing, so the recommended step is advice for
    your *next* run. The wizard writes it to your profile only when you confirm.
