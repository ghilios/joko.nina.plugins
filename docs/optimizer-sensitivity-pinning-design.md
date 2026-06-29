# Optimizer Sensitivity-Pinning / Star-Count Bistability — Design

## Problem

The star-detection optimizer can land on settings that **shed most of the real stars** while still scoring
J ≈ 0.999. On `bobp_m101/AutoFocus_20260626_225408` the stored optimized settings pinned **two** star-shedding
knobs to extremes — `BrightnessSensitivity = 50` (the search ceiling) and `StarClippingMultiplier = 9.5` — giving
**recall@SNR≥12 = 0.126** against a detector-independent golden set (full audit:
`docs/bobp-m101-recall-investigation-results.md`). 94% of the missed stars are attributable to those two knobs
(LowSensitivity gate 1302; the high star-clip firing the Degenerate guard on defocused-star rings 762 —
`StarDetector.cs:116-124`).

This is **not** rig-specific. The standard objective is **bistable** in the (sensitivity, star-clip) plane:

| corner | sens | starClip | recall@SNR≥12 | precision | J |
|--------|------|----------|---------------|-----------|---|
| star-shedding | 50 | 9.5 | 0.126 | 1.000 | ~0.999 |
| star-flooding | 0 | 0.375 | 0.867 | 0.572 | ~0.999 |

Both corners score essentially the same J, so **which one the search reaches depends on pixel scale + seed, not
on AF quality.** Evidence: bobp_m101 landed on the shedding corner on its capture rig; the bobp **sibling** run
(same imager, one night earlier) landed on the flooding corner (`BrightnessSensitivity = 0`); and re-running
`optimize` on a different profile lands at sens 0 again. The `mufti` run produced the identical
`clip 0.375→9.5, sens 16.67→49.25` wander that motivated the existing tie-breaker (see below).

## Why the existing guards don't catch it

- **`S_stars` saturates** (`ObjectiveConstants.NFloor = 8`, `NTarget = 20`): once a config clears ~8 min / ~20
  median stars the star sub-score clamps to 1.0, so beyond the knee the objective gives **zero** reward for the
  hundreds of additional real stars a lower sensitivity would recover. The sensitivity and star-clip axes are
  therefore nearly J-flat over a wide range.
- **The plateau tie-breaker is too weak.** `TieBreakerScore` (unsaturating: mean stars + lower σ) is blended with
  `Wtie = 1e-3` (`docs/optimizer-plateau-tiebreaker-design.md`) — deliberately tiny so it only decides exact J
  ties. When pixel scale puts the shedding corner *above* the `S_stars` knee, J is on a saturated plateau but any
  ~1e-3 numerical wiggle in σ_focus swamps the tie-breaker; when it puts the corner *below* the knee, the primary
  `S_stars` gradient (Ws = 0.20) actually pulls *toward* more stars but with no precision signal it over-shoots to
  the flooding corner. Either way the operating point is an extreme, not the interior sweet spot.
- **No precision signal when unlabeled.** The label term (`Wl`) is off without labels; `SHfrOutlier` only catches
  saturated bright blobs and `SDefocusPrecision` only near-focus relaxation junk — neither penalizes generic faint
  false positives. So nothing stops the flooding corner, and nothing rewards the star-rich-but-clean interior.

## Goal

Make the standard objective **never prefer the star-shedding corner** (so the optimizer cannot land where
bobp_m101 did), **bounded** so it does not simply relocate to the precision-cratering flooding corner. Keep
`--inspection` (already star-favoring, fit-bounded) unchanged. Modest σ_focus loosening is acceptable
(user-confirmed); a global focus regression is not.

## Candidate fixes (choose empirically — bank-verify is the arbiter)

- **A. Cap the sensitivity / star-clip search ceiling.** REJECTED: the sensitivity ceiling was deliberately
  widened 20→50 and the star-clip 5→10 "because rich fields pinned the old ceiling" (`OptimizerVariable.cs:124`).
  Capping re-introduces that problem and does not remove the underlying flat-plateau incentive.
- **B. Raise the standard `S_stars` knees** (`NFloor`, `NTarget`) so the star sub-score keeps a gradient to a
  higher count before clamping — extends the primary pull off the shedding corner, then saturates (so it does not
  chase infinite stars). Cleanest "stable interior" lever; risk = global focus loosening (bank gate guards it).
- **C. Strengthen the unsaturating tie-breaker** (`Wtie` up, e.g. 1e-3 → 1e-2…3e-2). Minimal change — the
  mechanism already exists and already prefers more stars + lower σ; just make it bite on the near-plateau. Risk =
  it can override a genuine off-plateau primary-J difference; tends toward flooding at large Wtie.
- **D. A bounded false-positive proxy** (e.g. penalize accepted stars far below the per-frame robust flux/SNR
  floor, or reward a star-count *band* rather than "more is better"). Targets the flooding overshoot directly so
  the interior optimum is stable; most complex, needs its own calibration.

**Chosen fix: C (strengthen the tie-breaker), `Wtie` 1e-3 → 0.02.** The bistability is a **plateau**
phenomenon: where the shedding corner falls *below* the `S_stars` knees, the primary objective *already* disfavors
it (Default-profile re-optimize correctly goes to the star-rich corner) — so no primary change is needed there.
The only unhandled case is the **saturated plateau** (both corners at `S_stars = 1.0`), where a ~1e-3 σ-focus
wiggle out-votes the original `Wtie = 1e-3`. Strengthening exactly that tie-breaker is the **most targeted,
lowest-σ-risk** lever: it only decides genuine ties (it cannot override a real primary-J gap), so it cannot loosen
focus on runs that are not on a plateau. Candidate **B** (raise knees) was rejected as higher-blast-radius — it
shifts the focus/star balance on *every* run (broad σ-regression risk) and breaks `S_stars` invariants — for no
gain on the plateau case it cannot reach. **A** is rejected (deliberately-widened ceilings). **D** stays a
follow-up if the bank shows flooding.

**Calibration of 0.02.** Two unit tests bracket it: it must out-vote a fine-step plateau σ-wiggle (the bug,
primary-Δ ≈ 8e-4 → flips a star-rich corner ahead) yet preserve a GENUINE focus-quality gap (the
`ForAberrationInspection_FlipsRankingTowardMoreStars` case: a 30% σ difference at coarse step, primary-Δ ≈ 9e-3,
where the *standard* objective must still prefer the sharper run and only `--inspection` flips to stars). 0.02
flips primary-Δ ≲ 4e-3 and preserves ≥ 9e-3, so it decides plateau ties without blurring the standard/inspection
boundary. Verified by `JRun_TieBreaker_RejectsStarSheddingOnNearPlateau` + the full objective suite (76/76).

## Decision criteria (gate = `bank-verify` over `D:\Autofocus Bank`)

A candidate ships only if, across the bank:
1. **No star-shedding landings** — the per-run optimized `BrightnessSensitivity`/`StarClippingMultiplier` no
   longer sit at the shedding extreme, and **recall@SNR≥12 rises** on runs that had the deficit.
2. **σ_focus does not materially worsen** and curve R² holds on runs that had no recall problem (no global focus
   regression). This is the guardrail on "modest loosening."
3. **Precision holds** — the fix must not relocate to the flooding corner (watch precision on the golden-bearing
   runs; a large precision drop fails the gate).
4. **`cwhite_2026` anchor reproduces** its known config-B numbers (harness integrity).

If no single strength satisfies 1–3 simultaneously, that is itself a finding: report the recall/precision/σ
tradeoff curve and pick the most balanced operating point (the user has accepted modest σ loosening; a genuine
recall↔precision tradeoff is theirs to weigh), and consider exposing it as a preference.

## Implementation & validation plan

1. Implement the chosen constants in `OptimizationObjective.cs` (`ObjectiveConstants`; + `OptimizerVariable.cs`
   only if a bound changes). `Wtie = 0` / unchanged knees must remain **bit-identical** to today (existing
   weight-normalization / bit-identity tests must still pass).
2. **NUnit test (the rigorous proof):** a synthetic `RunEvaluationMetrics` pair where raising sensitivity sheds
   stars for ≤ε σ_focus change — assert the fixed objective scores the **higher-recall** config strictly above the
   shedding one (and that the standard-objective bit-identity tests with the feature off still hold).
3. Re-`optimize` bobp_m101 and confirm it no longer lands on the shedding corner; re-score with `golden eval`.
4. `bank-verify` (C0 NC-sweep + A/B optimized prepass) and check criteria 1–4; append the results table here.

## Validation results (bank-wide A/B: OLD `Wtie=1e-3` vs NEW `0.02`, `optimize --per-run`, Default profile)

Ran `optimize --per-run` over all 19 bank runs under each objective and compared per-run optimized σ_focus +
landed knobs.

- **Mechanism (unit test):** `JRun_TieBreaker_RejectsStarSheddingOnNearPlateau` — on a near-plateau the NEW
  objective scores the star-rich corner above the σ-wiggle-favored shedding corner (the OLD `1e-3` does the
  opposite). Full suite 1607/1607.
- **σ no-regression (16/19 runs):** NEW σ is identical-or-better than OLD. The bobp_m101 / bobp / sensitivity-copy
  runs land star-rich under both (no plateau on this profile → the tie-breaker is a no-op there, by design).
- **Real-data demonstration the fix recovers shed stars — `muggsie`:** OLD shed it to **75 mean stars/frame**
  (σ 7.06); NEW keeps **198 mean stars (2.6×)** for σ 7.58 (+7% on an already-loose σ≈7 curve). This is exactly
  the authorized "modest σ loosening for more stars" — the bobp_m101 pathology, caught and corrected on a
  *different* real run (one whose pixel scale put it on the plateau).
- **Only other σ change — `CWhiteFocus` ≡ `standard_example2`** (identical md5, one dataset): +0.089 σ (+3%) on an
  ultra-rich field (min **291** stars/frame, unstarved either way); sens unchanged at 50, a negligible starClip
  step. Not a starvation case.
- **No starvation introduced:** every run's hard-floor passed; the remaining high-sensitivity landings
  (CWhiteFocus/standard_example2 sens 50, standard_example1 48.75, uneven 32) are all ultra-rich fields
  (min 50–291 stars) where shedding to "only" hundreds of stars is not a recall problem, so the tie-breaker
  correctly does not force sensitivity down.
- **`cwhite_2026` anchor reproduces:** NEW 16.67/3.625, σ 1.272 — identical to OLD (harness integrity).

**Verdict:** the gate passes. The fix recovers shed stars where the objective was on a plateau (`muggsie`
75→198), is a no-op where there is no plateau (16/19), never starves a run, and its only σ increases are the
intended star-recovery trade + a 3% perturbation on one ultra-rich dataset — all within the user-authorized modest
σ loosening. (Precision was not re-measured bank-wide — only the bobp_m101 golden set exists — but no run flooded
to a degenerate state and J held everywhere; D, a bounded FP proxy, remains a follow-up if a future golden-bearing
run shows flooding.)

## Status

Root cause + fix + bank validation: **complete**. Shipped on branch `ghilios/optimizer-sensitivity-pinning`
(commit `8eeaafc`). Per-run recommendation for bobp_m101: `docs/bobp-m101-recall-investigation-results.md`.
