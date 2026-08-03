# Synthetic AF bank — followups wave 2

Executes the remaining synthetic-bank followups after [F31](../docs/followups.md) invalidated the
precision metric that wave 1 was scored against. Wave 1's results:
[`docs/f23-objective-precision-term-results.md`](../docs/f23-objective-precision-term-results.md).

**Branch:** `ghilios/synth-bank-followups-wave2` off `develop` (`f066c94`).

## What this wave is for

Wave 1 measured F23 against a metric that charged a false positive for every real star the golden
policy had dropped. F31 repaired the metric (`afbank-verify/4`) and killed F23 as written. That
leaves three separable jobs:

1. **Re-baseline** everything that rested on the broken precision — but only after proving the
   repaired metric measures something.
2. **Re-measure F24**, the one wave-1 followup explicitly left unmeasured, which currently reads as
   confirmed on evidence from the same broken metric.
3. **Ship the findings that never depended on precision** (F20, the recall collapse, F22) and the
   cheap independent fixes (F27, F28, F30, the convergence predicate).

## Task 0 — validate the measuring instrument (GATE, done before anything else)

The wave-1 lesson: *verify the instrument against an independent source before running a multi-hour
experiment against it.* Wave 1's every reproducibility check passed while the reference they all
shared was biased. So this wave starts by re-scoring **saved detection dumps** — `golden eval` writes
`detected_f<focuser>.csv` per frame, so the detector never has to run again and every scoring policy
sees a bit-identical detection set.

30 saved configurations (D09/D13/D17 at nine Sensitivity values each, plus D10/D11/D12 at
Sensitivity 0), scored under four policies plus a null control.

**The offline scorer is validated first:** re-implementing `GoldenMatch.Match` /
`GoldenMatch.ExcludeUnresolved` in Python reproduces the published C# `/3` numbers exactly — D09 s0
0.8037 vs 0.804, D17 s0 0.6974 vs 0.697, D13 s0 0.8504 vs 0.850, D12 s0 0.8222 vs the 0.822 in that
run's own `golden_eval.txt`. Only then are its other columns believable.

### GATE result — the metric is NOT saturated; baselining may proceed

| dataset | sens | dets | P `/3` | P `/4` impl | P `/4` sym | P truth | **null** | scored frac |
|---|---|---|---|---|---|---|---|---|
| D09_c14_3800mm | 0 | 318 | 0.8037 | 1.0000 | 0.9909 | 0.9937 | **0.0000** | 0.689 |
| D09_c14_3800mm | 6 | 214 | 1.0000 | 1.0000 | 1.0000 | 1.0000 | **0.0000** | 0.963 |
| D13_apo200_1800mm | 0 | 1075 | 0.8504 | 1.0000 | 1.0000 | 1.0000 | **0.0028** | 0.735 |
| D17_cdk14_oiii5 | 0 | 337 | 0.6974 | 0.9895 | 0.9793 | 0.9881 | **0.0093** | 0.573 |
| D11_rc10_585_afbin2 | 0 | 415 | 0.9070 | 1.0000 | 1.0000 | 1.0000 | **0.0024** | 0.752 |
| D12_c14_585_afbin2 | 0 | 493 | 0.8222 | 1.0000 | 1.0000 | 1.0000 | **0.0023** | 0.600 |

- **null** = the same detections translated by (+317, +211) px with wraparound. Count and spatial
  clustering are preserved; correspondence with the frame is destroyed. Whatever precision survives
  is chance coincidence, and it is the floor the metric can never read below.
- **P truth** = TP iff the detection is within the match radius of *any* rendered truth star, the
  policy F31's next-step (1) asks for.
- **scored frac** = the fraction of detections that enter the precision ratio at all.

Four findings, each of which changes what this wave does:

**1. All-1.000 here is a measurement, not saturation.** The null control reads **0.000–0.012**. A
detector placing its detections at random would score essentially zero, so the metric's dynamic range
is effectively the full interval and a reading of 1.000 means the detector really did not produce
false positives. The 2·HFR saturation F31 warns about would have shown up here as a *high* null; it
does not. This is the specific check the stop condition asks for, and it passes.

**2. Three independent policies agree to within 0.006.** The as-implemented `/4` metric, a `/4`
variant whose protection predicate is exactly the matching predicate, and direct truth scoring all
land at 0.98–1.00 on every configuration. The real false-positive rate is **0–2%**, confirming F31
from a second direction.

**3. The `/3` metric is confirmed broken, with the violation counted directly.** Detections charged
as a false positive while sitting within the match radius of a real rendered star: **52/318** on D09
s0, **79/337** on D17 s0, **139/1075** on D13 s0, **64/493** on D12 s0. Under `/4` that count is
**0 on all 30 configurations**. This is the assertion Task 5 turns into a permanent guard.

**4. A real residual asymmetry in the `/4` repair, always flattering.** `GoldenMatch.Covers` excludes
on `centre-in-box OR IoU(box, det.bbox) > 0`, so the *detection's own bounding box* dilates every
protection box — a wide donut detection is protected well past the match radius. The class comment
claims protection is "exactly as generous as matching"; measured, it is not: D09 s0 reads 1.0000
under the implemented predicate against 0.9909 under the symmetric one, D17 s0 0.9895 against 0.9793.
Small (≤0.011) but systematic and one-directional. Fixed in Task 1.

**Not a bias, but worth reporting: the denominator shrinks.** At Sensitivity 0 only 57–75% of
detections enter the ratio; the rest land on protected stars and are unjudgeable. That is not
laundering — the excluded fraction is enriched **2.5–53×** over the chance rate implied by the
protected area, so those detections are overwhelmingly on real stars — but precision at low
Sensitivity is measured over a materially smaller sample and the report should say so.

## Task 1 — make the instrument self-reporting (`afbank-verify/5`)

Three changes to `BankVerifyRunner` / `TruthProtection`, none of which touch the real bank (no truth
sidecar ⇒ every path is a no-op and real-bank numbers stay comparable):

1. **Symmetric protection predicate.** Exclude a false positive on a truth-protection box iff its
   centroid is within `matchRadius` of the truth star — the same predicate matching uses. Leave
   `GoldenMatch.ExcludeUnresolved`'s `Covers` semantics alone for the golden's own `unresolved`
   boxes, which is the real-bank path and must not move.
2. **Null-control precision, reported per config.** Re-score the same detections translated with
   wraparound. Costs no extra detection pass. A future reader can tell saturation from a real 1.000
   without re-deriving anything.
3. **`truthViolations`, reported per config.** The count of scored false positives sitting within
   `matchRadius` of any truth star. Must be 0. This is the F31 bug's signature, and it is now a
   number in every report rather than something a person has to think to check.

Also report `scoredFraction` (detections entering the ratio ÷ detections).

## Task 2 — re-baseline

Prepass provenance:

- **Config A** reuses `D:\hf_w2\..\hf_f23\H_A` — the wave-1 control arm, which re-derived config A
  from an unmodified HEAD and reproduced the published V2 landings exactly on all 17 datasets. Not
  re-run; re-running a reproduced deterministic search buys nothing.
- **Config B** is re-derived here (`optimize --per-run --donut`, `D:\hf_w2\B_A`). The V2 config-B
  prepass output no longer exists and wave 1 never re-ran it, which is precisely why F24 could not be
  re-measured.

One `bank-verify --nc-sweep 2 --opt-a <A> --opt-b <B>` pass yields the whole matrix. Then:

- rewrite the V2 matrix in `docs/synthetic-af-bank-baseline-results.md` as a V3 matrix at `/5`;
- write a new measured `docs/synthetic-af-bank-baseline.json` (the old one stays as the audit trail
  it was converted into);
- **re-derive** the `precisionMin` bands in `docs/synthetic-af-bank-expectations.json` from the
  measured distribution. 0.95/0.98 came from the broken metric and are not carried forward.

## Task 3 — F24

Score config B against config A and C0 on the same pass. F24's claim is "donut detection costs
precision, 0.962 → 0.653 on the ε=0 control (D13)". Both of those numbers are `/3`. Close the entry
or restate it on `/5` evidence, whichever the measurement supports.

## Task 4 — the findings that never depended on precision

Documentation only; no behavioural change proposed here.

- **F20** — the objective is identically 0 below `MinHFR`, so D01/D02 land `FinalJ = 0.00000` and the
  search has no gradient at all. Strongest surviving finding. Record the risk that seeding `MinHFR`
  creates a knob the search cannot climb back out of.
- **Recall, not precision, is the interesting axis.** On the real bank the optimizer's landings give
  up 50–90% of recall@≥12 (`mccomiskey` 0.871 → 0.079). Untouched by F31 — the golden's `stars` list
  still defines what must be found.
- **F22** stays confirmed; its "calibrate out the bias" fix stays ruled out; hysteresis stays viable.

## Task 5 — cheap independent fixes

- **F27** — `RejectedCandidates` never reaches the optimizer's evaluation path. Close the gap or
  correct the spec.
- **F28** — the Sensitivity gate provably rejects nothing below `PeakResponse × StarClip`, so
  `LowSensitivity` reads 0 exactly when the gate has collapsed, making `ExposureRecommender`'s
  `StarCountIsTheLimit` branch unreachable for the users it was written for.
- **F30** — a stored `optimized_settings.json` does not record which invocation produced it.
- **Convergence predicate** — `synth-validate` scores `converged=true` ("round applied nothing") at a
  step 4× outside the tolerance band, inflating convergence counts on the most degenerate runs.

## Task 6 — the regression guard

`truthViolations` (Task 1) is the runtime half. The unit half asserts the invariant directly: a
detection within the match radius of a rendered truth star is never scored as a false positive,
whatever tier the golden policy assigned it. Four harness-calibration bugs have now been found, three
in the convergence-predicate family; this class needs to fail loudly rather than be noticed.

## Order of execution

The config-B prepass is the long pole (30–60 min) and depends on nothing, so it starts first and runs
detached while Task 1 and Task 5 proceed. `bank-verify` (~19–25 min for three configs) needs both the
prepass and the `/5` binary. Every run is launched detached with a progress file; the exe is
file-locked while running, so rebuilds go to a separate `-o` directory.

## Conventions

Findings are **flagged in `docs/followups.md`**, not fixed inline. Never push `develop`. Full suite
before the PR.
