# Synthetic AF bank — followups wave 2

Plan: [`plans/synthetic-af-bank-followups-wave2-plan.md`](../plans/synthetic-af-bank-followups-wave2-plan.md).
Wave 1: [`docs/f23-objective-precision-term-results.md`](f23-objective-precision-term-results.md).
Baseline: [`docs/synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md) §V3.

*The question: [F31](followups.md) proved the metric wave 1 was scored against was broken. Re-measure everything
that rested on it, close F24 (the one followup wave 1 never re-measured), and ship the findings that never
depended on precision.*

## Headline

| what | result |
|---|---|
| Does the repaired metric measure anything? | **Yes** — null control 0.000–0.169, `truthViolations` 0 on all 51 rows |
| F23 — "trades precision for marginal recall" | **Won't fix as written.** Real effect ~1/5 the reported size; C0's floor was 1.000, not 0.942 |
| F24 — "donut detection costs precision" | **REFUTED.** D13 (ε=0 control) scores **1.000** under B, not 0.653. Restated as a *recall* finding |
| F27 — rejected-candidate diagnostics | **Done** — the spec making the claim was never merged; record corrected, seam documented in code |
| F28 — `LowSensitivity` reads 0 when the gate is inert | **Done** — user-visible; the exposure probe now reaches the population it was written for |
| F30 — a landing does not say what produced it | **Done** — provenance block, schema 3 |
| F34 — `synth-validate` scored a stall as convergence | **Done** — convergence now requires the tolerance band |
| New: F32 | **`J` is saturated near 1.0.** `toml999` gives up 0.462 of recall@≥12 to gain **+0.0002** of `J` |
| New: F33 | **The synthetic bank does not reproduce the real bank's failure mode** — opposite signs |

## The gate: validate the instrument before spending hours against it

Wave 1's most expensive lesson was that reproducibility validated nothing — every check confirmed determinism,
and a deterministic pipeline reproduces a systematic error perfectly, because the bias lived in the reference
all the checks shared. So wave 2 opened by checking the instrument against an independent source, and did it
**without running the detector at all**.

`golden eval` writes `detected_f<focuser>.csv` per frame. Thirty saved configurations were still on disk from
wave 1 — D09/D13/D17 at nine Sensitivity values each, plus D10/D11/D12 — so every scoring policy could be
applied to a bit-identical detection set offline. Minutes of work.

**The offline scorer was validated first.** Re-implementing `GoldenMatch.Match` / `ExcludeUnresolved` in Python
reproduces the published C# `/3` numbers exactly: D09 s0 **0.8037** vs 0.804, D17 s0 **0.6974** vs 0.697,
D13 s0 **0.8504** vs 0.850, D12 s0 **0.8222** vs the 0.822 in that run's own `golden_eval.txt`. Only then were
its other columns worth reading.

| dataset | sens | dets | `/3` | `/4` impl | `/4` sym | truth | **null** | scored |
|---|---|---|---|---|---|---|---|---|
| D09_c14_3800mm | 0 | 318 | 0.8037 | 1.0000 | 0.9909 | 0.9937 | **0.0000** | 0.689 |
| D13_apo200_1800mm | 0 | 1075 | 0.8504 | 1.0000 | 1.0000 | 1.0000 | **0.0028** | 0.735 |
| D17_cdk14_oiii5 | 0 | 337 | 0.6974 | 0.9895 | 0.9793 | 0.9881 | **0.0093** | 0.573 |
| D11_rc10_585_afbin2 | 0 | 415 | 0.9070 | 1.0000 | 1.0000 | 1.0000 | **0.0024** | 0.752 |
| D12_c14_585_afbin2 | 0 | 493 | 0.8222 | 1.0000 | 1.0000 | 1.0000 | **0.0023** | 0.600 |

**null** translates every detection by (+317, +211) with wraparound: count and clustering survive,
correspondence with the frame does not, so whatever precision remains is chance. **truth** scores TP as "within
the match radius of any rendered star". **scored** is the fraction of detections entering the ratio at all.

Four findings, each of which changed what wave 2 did:

**1. All-1.000 here is a measurement, not saturation — the stop condition passes.** The null reads 0.000–0.012.
A detector placing detections at random scores essentially zero, so the metric's dynamic range is effectively
the full interval. The 2·HFR saturation F31 warns about would have appeared here as a *high* null.

**2. Three independent policies agree to within 0.006.** The real false-positive rate is **0–2%** on these
configurations, confirming F31 from a second direction.

**3. The `/3` violation, counted rather than argued.** Detections charged as a false positive while sitting on a
real rendered star: **52/318** on D09 s0, **79/337** on D17 s0, **139/1075** on D13 s0, **64/493** on D12 s0.
Under `/4`: **0 on all 30 configurations.** That count is now a shipped field.

**4. A residual asymmetry in the F31 repair, always flattering.** `GoldenMatch.Covers` excludes on
`centre-in-box OR IoU(box, det.bbox) > 0`, so the *detection's own bounding box* dilates every protection box —
a wide donut detection is protected far past the match radius, despite the class comment claiming protection is
"exactly as generous as matching". Measured at ≤0.011 and one-directional. Fixed in `/5`.

## What `afbank-verify/5` adds

Three things, so the next reader can check the instrument instead of trusting it:

- **A symmetric protection predicate.** A detection is excluded iff its centroid is within the match radius of a
  protected truth star — the same predicate matching uses. The golden's own `unresolved` boxes keep `Covers`,
  because that is the real bank's scoring path and moving it would break comparability with every published
  real-bank number.
- **`precisionNull`** — what chance alone scores, per config. The floor the metric can read.
- **`truthViolations`** — scored false positives sitting on a real rendered star. The F31 signature itself; it
  must be 0, and a non-zero value prints loudly rather than producing a quietly wrong number.

Plus `scoredFraction`, because at a floored Sensitivity only 45–75% of detections are judgeable and a precision
figure there rests on a much smaller sample than C0's.

Real-bank runs have no truth sidecar, so every new path is a no-op and real-bank numbers stay comparable.

## The re-baseline

Full V3 matrix in [`synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md). The two
results that move:

**F23's direction survives, its magnitude does not.**

| | as recorded | at `/5` |
|---|---|---|
| C0@nc2 precision, worst of 17 | 0.942 | **1.000** |
| Config A precision, worst of 17 | 0.451 (D09) | **0.910** (D10) |
| Config A datasets below 0.90 | 6 | **0** |

Config A does cost precision, on exactly the datasets F23 named — D10 0.910, D09 0.958, D17 0.959, D15 0.968,
every one a long-focal-length rig landing at Sensitivity 0. That is a real effect roughly **one fifth** the size
of the artifact that hid it.

**F24 is refuted.** Its headline was "donut detection costs precision even where donuts exist, and badly where
they do not — D13 0.962 → 0.653" on the ε=0 control. Re-measured, **D13 scores 1.000 under config B**, and B's
precision never falls below 0.991 on any dataset. What B actually costs is *recall*, and only where it is not
needed:

| dataset | ε | Δrecall@high | σ_focus C0 → B |
|---|---|---|---|
| D16_esprit550_ha3 | 0 | **−0.147** | 0.615 → 0.315 |
| D04_esprit_550mm | 0 | **−0.113** | 0.232 → 0.059 |
| D13_apo200_1800mm | 0 | −0.014 | **2.352 → 0.291** |
| D03_redcat_250mm | 0 | **+0.138** | 1.183 → 0.341 |

D13 is the sharpest reversal: the dataset cited as proof that donut detection does damage keeps 1.000 precision,
gives up 0.014 recall, and improves its AF fit **eightfold**.

**The bands were re-derived, not carried forward.** 0.95/0.98 were fitted to the broken metric, and a band
calibrated to a broken instrument encodes the breakage. They now sit where a regression would show, plus two
instrument gates: `precisionNullMax` 0.20 and `truthViolationsMax` 0 — the latter a *void* condition, not a
tolerance.

## The findings that never depended on precision

These are the real remaining value, and F31 touches none of them.

### F32 — `J` is saturated near 1.0

Across the 17 scorable real-bank runs the optimizer gives up a **median 0.243 of recall@≥12** to gain a **median
ΔJ of +0.0125**:

| run | recall@≥12 C0 → A | Δrecall | ΔJ |
|---|---|---|---|
| `toml999` | 0.819 → 0.357 | **−0.462** | **+0.0002** |
| `CWhiteFocus` | 0.810 → 0.327 | −0.483 | +0.0042 |
| `uneven` | 0.931 → 0.319 | −0.613 | +0.0046 |
| `muggsie` | 0.879 → 0.512 | −0.368 | +0.0039 |

`toml999` states it best: **two ten-thousandths of `J` bought with 46 points of recall.** At `BaselineJ` values
of 0.98–0.999 there is no headroom left, so every remaining move is a rounding error in the objective and a
catastrophe in the star list.

This is upstream of F23 and F4. **A precision term, a sensor term, or any other new term added to a `J` already
sitting at 0.998 competes for an exhausted fourth decimal place** — which is the better explanation for why F23
wave 1's mechanism (a) produced real precision movement and still cleared no gate.

### F33 — the synthetic bank does not reproduce the real bank's failure mode

| bank | landings | detections C0 → A |
|---|---|---|
| synthetic (6 of 17) | Sensitivity **0.0** | up |
| real: `CWhiteFocus` | **50.0** | 1807 → **534** |
| real: `standard_example1` | **34.3** | 602 → **177** |
| real: `mccomiskey` | 0.0, but **StarClip 10.0** | 3606 → **43** |

Opposite signs. F23's framing — "the optimizer drives `BrightnessSensitivity` to its 0.0 floor" — is a
**synthetic-bank-only** description; on real rigs it drives Sensitivity *up* and sheds stars. `mccomiskey` is
the instructive case: Sensitivity reads 0.0, which looks like the synthetic pathology, but the effective gate is
`PeakResponse × StarClip = 7.5` — the same escape route F23 wave 1 measured on D12 and D15, operating here as
the shedding mechanism.

**Consequence:** no objective change may be accepted on the synthetic bank alone.

### F20 reproduces on the real bank

`BaselineJ` is exactly **0.0000** on `Panos` and `LinwoodFocus` — the only two runs of nineteen where it is, and
the only two whose recall *improves* under the optimizer (+0.031, +0.089), for the reason F32 gives: they are
the only ones with headroom to climb. So the cold-start plateau is not a synthetic artifact of the render, and
the affected population is not hypothetical. The prepass reproduced it again under config B: D01 and D02 both
report `currentJ=0 bestJ=0 hard-floor FAIL`.

The risk on the proposed fix stands and is worth restating: seeding `MinHFR` beneath the measured in-focus HFR
creates a knob the search has no gradient to climb back out of, so it should be seeded to a *measured* value,
not a permissive one.

### F22 is unaffected

A binning/HFR result, not a precision one. Still confirmed, its "calibrate out the bias" fix still ruled out
(the under-read tracks the Sensitivity landing rather than being a property of the measurement), hysteresis
still viable.

## What shipped

| change | user-visible? |
|---|---|
| `afbank-verify/5` — symmetric protection, `precisionNull`, `truthViolations`, `scoredFraction` | no (harness) |
| F28 — the exposure discriminator is conditional on the gate being able to reject | **yes** |
| F30 — `Provenance` on `optimized_settings.json`, schema 3 | no (metadata) |
| F34 — convergence requires the tolerance band | no (harness) |
| F27 — stale gate counts corrected, the optimizer seam documented | no (docs) |

**F28 is the only behavioural change.** Signal-sufficient runs where every frame is short of the star-count
target AND the gate sat below its own inert bound move from "no exposure offered" to a 2× probe. The rig that
motivated the zero-rejections test is untouched — it rejected 5 candidates at 2 s, so its gate was demonstrably
live, and the two populations are disjoint by construction.

## Reproduce

```
# config-B prepass (~55 min; config A reuses the wave-1 control arm)
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out <B> --max-evals 250 --donut

# the V3 matrix, all three configs in one pass (~24 min)
TestApp bank-verify --runs "D:\SyntheticAutofocusBank" --out <dir> --nc-sweep 2 \
    --match-radius 12 --pixel-scale header --opt-a <A> --opt-b <B>
```

Check `truthViolations == 0` and `precisionNull` before reading any precision figure. Both are in the per-run
JSON and in the markdown table.
