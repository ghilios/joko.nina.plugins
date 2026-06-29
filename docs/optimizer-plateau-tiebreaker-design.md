# Optimizer Plateau Tie-Breaker — Design

## Problem

On an "easy" auto-focus run (star-rich, well-sampled, a clean V-curve) the optimization objective **J
saturates at its maximum 1.0 over a large region of parameter space**. Every sub-score clamps:

- `S_focus = 1/(1+(ρ/RhoRef)²)` ≈ 1 when ρ = σ/step ≪ RhoRef (e.g. σ≈1e-4, step=100 ⇒ ρ≈1e-6).
- `S_stars = 0.6·clamp01(nMin/NFloor) + 0.4·clamp01(nMed/NTarget)` = **1.0 exactly** once nMin≥NFloor(8)
  and nMed≥NTarget(20) — a hard clamp, no gradient.
- `S_fit = clamp01(R²)·penalty` = 1.0 when R²≥1 and reducedχ² ≤ ChiTau.

So `J = Wf+Ws+Wc = 1.0` for a whole plateau of settings. Empirically (run
`mufti/AutoFocus_20260607_030642…`, profile b10b1d6d): the user's hand-tuned settings score **J=1.0** AND a
wildly different setting set (StarClippingMultiplier 0.375→9.5, Sensitivity 16.67→49.25) also scores **J=1.0**
("no improvement over current"). Because the search **seeds from the fully-default params** and only accepts
strictly-improving moves, with no gradient on the plateau it **wanders to an arbitrary tie-point** far from
the user's settings — one that roughly **halves the star count** (best-focus frame 334→147) and worsens
σ_focus ~20× (both still scoring 1.0). Fewer, less-distributed stars ⇒ a noisier tilt/sensor model, which is
the "worse results" the user observes. See `optimizer-saturated-objective-finding` (memory) for the full
diagnosis.

## Approach

Add a small, **unsaturating secondary "quality" score `T`** that breaks ties on the plateau in favour of the
two things that actually degraded — **more stars** and **lower σ_focus** — and fold it into J as a convex
blend so J stays in [0,1]:

```
J = (1 − Wtie)·J_primary + Wtie·T          (only on the feasible path; hard fails still return 0.0)
```

- `J_primary` = the existing weighted-sum × SDefocusPrecision (unchanged).
- `Wtie` (ObjectiveConstants, **default 1e-3**) — small enough that any genuine primary-J difference
  (≥~1e-3, which a real star-count/σ change produces) dominates, so **off-plateau ranking is unchanged**; on
  the plateau (J_primary tied) `T` decides.
- `Wtie = 0` ⇒ J is **bit-identical** to today (used by the core weight-normalization / F3 bit-identity unit
  tests).

### The tie-breaker score T (unsaturating, ∈ [0,1))

Reuses the existing knees as half-saturation points so it needs no new tuning constants, and uses the
Michaelis–Menten form `n/(n+k)` which is strictly monotone everywhere (never clamps):

```
Tstars = nMean / (nMean + NTarget)        // TOTAL richness across the sweep — more stars ⇒ higher, forever
Tfocus = RhoRef / (RhoRef + ρ)            // lower σ ⇒ higher, forever
T      = 0.6·Tstars + 0.4·Tfocus          // star-count dominant
```

Star count dominates because it is the robust, large-signal indicator of sensor-model quality (334 vs 147 is
a clear difference) and because at plateau-tiny σ the focus term is near-equal for all candidates. The
star/focus split (0.6/0.4) mirrors the primary objective's focus emphasis without letting σ alone decide.

**Why MEAN, not a minimum-dominated blend (empirical correction).** A first cut used
`0.6·(nMin/(nMin+NFloor)) + 0.4·(nMed/(nMed+NTarget))`, mirroring the primary `S_stars`. Re-validating on the
mufti run showed it **did not work**: the optimizer still drifted to the corner (clip 10, Sensitivity 48.75),
because that corner *raised only the worst frame* (min 82→91) while shedding 27% of the total stars
(1247→904) — and the min-dominant tie-breaker scored the gamed set HIGHER (0.9411 vs 0.9396). On the plateau
every frame is already well past the `S_stars` knees, so minimum-frame protection is redundant (the hard
floor + primary `S_stars` own starvation). The mean rewards keeping stars *everywhere*, which is what a tilt
model needs and is not gameable by lifting one frame.

### Why this fixes it (and its limits)

On the plateau `J_primary` is flat, so the compass search now climbs `T` — toward **more stars / lower σ**,
i.e. away from the star-halving extreme and toward the star-rich basin the user's settings live in. It works
**even when seeded from the default params** (the wizard's default), and composes with "Start from my current
settings" (the never-regress floor then already includes T).

Crucially, `Wtie` is tiny: if loosening detection to gain stars starts admitting junk that **hurts the fit**
(σ_focus up, χ² up, a starved frame), `J_primary` drops by ≫1e-3 and the tie-breaker cannot rescue it — the
primary objective still guards quality. The tie-breaker only operates among **AF-equivalent** configs. It is
therefore **not** a substitute for precision labels on a genuinely noisy field; it is a sensible default
preference ("when AF quality is tied, keep more stars") for the unlabeled plateau case.

Hard fails (NaN focus σ, too many starved frames) return 0.0 **before** the blend, so the tie-breaker can
never rescue an infeasible run.

## Scope

- `ObjectiveConstants`: add `Wtie` (default 1e-3) + the (reused) knees already present.
- `OptimizationObjective`: add `TieBreakerScore(m, c)`; blend it into `JRun` on the feasible path.
- Production picks it up automatically (`new ObjectiveConstants()` in the wizard + TestApp).
- Tests: 5 existing tests that pin the pure weighted sum set `Wtie = 0`; new tests pin `TieBreakerScore`
  monotonicity and the plateau-tie-break behaviour of `JRun`.

## Validation

Headless `TestApp optimize` on the mufti run (donut on, 400 evals, profile b10b1d6d):

| Objective | seed | Best J vs Current J | result |
|---|---|---|---|
| pre-tie-breaker | default | 1.0 vs 1.0 (tie) | drifts to corner (clip 9.5, halved stars) |
| nMin tie-breaker | default | 0.999941 **> 0.99994** | STILL drifts (gamed the min frame) |
| mean tie-breaker | default | 0.999919 **< 0.999939** | objective now ranks current ABOVE the corner ✓ |
| mean tie-breaker | **current** | ≥ current (guaranteed) | refines current; never the corner ✓ |

**Key finding — the tie-breaker is necessary but not sufficient.** With the (corrected) mean tie-breaker the
objective correctly ranks the user's current settings ABOVE the drifted corner (0.999939 > 0.999919). But the
default-seeded search **still returns the corner**: the corner is a genuine local optimum reachable from the
default params (the trajectory converged at eval 333, flat to 400 — NOT budget-limited), and the user's
settings live in a different basin (low clip/sensitivity) that a local compass search starting from default
cannot climb across to. The optimizer only guarantees "≥ the **seed**" (default), not "≥ current".

**Complete fix = tie-breaker + seed-from-current + a wizard guard.**

1. **Seed-from-current** (`StartFromCurrentSettings`, the wizard's "Start from my current settings" toggle;
   `--start-from-current` in TestApp) makes the never-regress floor the current J, so the result can never be
   worse than current — and the tie-breaker gives the search a gradient to refine current toward MORE stars /
   lower σ rather than returning it unchanged. Validated: returns the mufti settings exactly unchanged.

2. **Wizard guard (implemented).** Seed-independent protection for users who DON'T enable the toggle: the
   results page now defaults to the **Current** variant (and shows a "could not improve on your current
   settings — keeping Current" note) whenever `BestJ ≤ currentBaselineJ + ε`. The Optimized variant stays
   available to inspect, but the default Accept keeps current. Exposed as `OptimizerImprovedOverCurrent` /
   `ShowNoImprovementNote` / `OptimizerNoImprovementNote` on the wizard VM; red-green verified.

## Follow-up: Wtie strengthened 1e-3 → 0.02 (sensitivity-pinning bistability)

The default-seeded `bobp_m101` audit (`docs/optimizer-sensitivity-pinning-design.md`,
`docs/bobp-m101-recall-investigation-results.md`) showed the tie-breaker at `Wtie = 1e-3` was **too weak** in
practice. The (sensitivity, star-clip) plane is bistable — a star-SHEDDING corner and a star-RICH corner score
essentially the same J — and on the plateau the shedding corner holds a ~1e-3 σ-focus wiggle that `1e-3` could not
out-vote, so the run pinned `Sensitivity 50 / StarClip 9.5` (recall@SNR≥12 0.13) where a star-rich config scored
~0.87 at the same J. `Wtie` is raised to **0.02**, calibrated to bracket the two regimes — it out-votes a
fine-step plateau σ-wiggle (primary-Δ ≲ 4e-3) yet stays below a GENUINE focus-quality gap (primary-Δ ~9e-3 for a
30% σ difference at a coarse step, so the standard objective still prefers the sharper run and only `--inspection`
flips toward stars). Guarded by `JRun_TieBreaker_RejectsStarSheddingOnNearPlateau`, and unchanged-pass on
`JRun_TieBreaker_DoesNotOverrideRealPrimaryDifference` / `ForAberrationInspection_FlipsRankingTowardMoreStars`.
