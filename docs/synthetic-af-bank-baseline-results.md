# Synthetic AF bank — initial baseline results

The first run of the synthetic autofocus bank at `D:\SyntheticAutofocusBank`: 17 datasets spanning
40 mm–3800 mm, rendered by the in-repo camera simulator with exact per-star truth, plus the
convergence harness that measures whether the optimizer's bootstrap recommendations walk toward a
known-correct answer. Design: [`docs/synthetic-af-bank-design.md`](synthetic-af-bank-design.md).
Plan: [`plans/synthetic-af-bank-plan.md`](../plans/synthetic-af-bank-plan.md). Expected bands:
[`docs/synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json).

*The question this bank was built to answer: **precision on the real bank is a lower bound, and the
optimizer's recommendations have never been checked for convergence — what do they look like when the
right answer is known by construction?***

Everything below is **flagged, not fixed**. Eight product findings became `docs/followups.md` entries
(F19–F26); none of them changed product behaviour.

## Headline

| what | result |
|---|---|
| Bank generated | 17/17 datasets, 6.4 GB, 84 s |
| Header round-trip (`--verify`, risk R1) | **PASS** — XPIXSZ/FOCALLEN/XBINNING/EXPTIME and the composed arcsec/px all land, on every dataset |
| Determinism | **PASS** — D07 + D12 regenerated from seeds: 36/36 frames and golden sidecars bit-identical |
| Golden precision (D06, `golden eval`) | **1.000** — 205 TP, **0 FP**. Exact, not a lower bound |
| V1 convergence matrix, S0–S6 | **62 PASS · 15 FLAG · 8 FAIL** over 85 scored runs (34 correctly skipped as not-applicable) |
| V2 precision/recall baseline | 17 runs, 0 failed, `afbank-verify/3` — see the V2 section; C0 precision never below **0.942**, config A as low as **0.451** |
| Pixel-scale default change (V-P1) | **no measurable effect** — see "The pixel-scale change did nothing" below |
| Full unit suite | **3293 passed, 0 failed** at both gates (the flaky `SendAsync_WritesOnABackgroundThread` EAT test fails only under concurrent optimizer load; clean on an idle machine) |

**Reading it:** the instrument works. Precision of exactly 1.000 with zero false positives is the
thing the real bank can never give, and it is what makes the golden close-pair policy (merge tight
pairs, mark blends unresolved) worth its complexity — without it a detector that correctly reports one
blob for a tight double is charged both a miss and a false positive.

## The exposure derivation had to be corrected twice, and that is the point of `--dry-run`

The design tabulated exposures from an imager's intuition (0.5–25 s). Deriving them from
`ExposureRecommender`'s own arithmetic replaced most of that with the 0.5 s floor, and generating the
bank then showed the derivation itself was wrong:

| stage | what it produced | why it was wrong |
|---|---|---|
| design intuition | 0.5–25 s spread | not derived from anything the product measures |
| derived, in-focus frame only | 12/17 at the 0.5 s floor | the product takes the **median across sweep frames**, not the sharpest one |
| derived, median across sweep | 0.5–30 s, with D10 and D17 clamped at the ceiling | matches `ExposureRecommender.PerFrameNthBrightest`'s actual definition |

The middle row's symptom was visible in the goldens before any validation ran: at the in-focus-only
exposure, the extreme frames of D08/D09 carried **3–7 golden stars out of ~470**. That is not a sweep
an autofocus fit can use, and it would have quietly poisoned every downstream number.

It also moved the `CappedByAbsoluteLimit` role from D16 to D10 — see **F19**.

## S0 — the control

S0 bootstraps each dataset at its own expected optimum, so the recommendations should be ~no-ops.
Final scoring (`--max-rounds 4`): **13 PASS · 3 FLAG · 1 FAIL**, every dataset converging in 1–2 rounds.

| dataset | verdict | final step | target (`step_behavioral`) |
|---|---|---|---|
| D01_ultrawide_40mm | **FAIL** | 10 | 8 — R² 0.648, sub-`MinHFR` (F20) |
| D02_rich_135mm | FLAG | 7 | 6 — sub-`MinHFR` (F20) |
| D03_redcat_250mm | PASS | 21 | 16 |
| D04_esprit_550mm | PASS | 20 | 15 |
| D05_tec140_1000mm | PASS | 36 | 35 |
| D06_sparse_1000mm | PASS | 37 | 35 |
| D07_rc10_2000mm | PASS | 54 | 55 |
| D08_c11_2800mm | PASS | 82 | 82 |
| D09_c14_3800mm | PASS | 115 | 118 |
| D10_rc16_3250mm_sparse | PASS | 94 | 89 |
| D11_rc10_585_afbin2 | PASS | 53 | 55 |
| D12_c14_585_afbin2 | FLAG | 141 | 141 — binning misread (F22) |
| D13_apo200_1800mm | PASS | 127 | 127 |
| D14_cdk14_2563mm_e47 | PASS | 59 | 60 |
| D15_cdk20_3454mm_e47 | PASS | 61 | 64 |
| D16_esprit550_ha3 | FLAG | 22 | 15 |
| D17_cdk14_oiii5 | PASS | 58 | 60 |

Fit quality is **R² = 1.0000 on 15 of 17**; only D01 (0.648) and D02 (0.916) are degraded, and both are the
sub-`MinHFR` datasets. D08 and D13 land exactly on target; most others sit within a few percent.

The run also doubles as a determinism check on the *validation* path: rerunning the whole pass reproduces every
half-width to the tenth — including D17's 143.6 and 12.1 (F21) — because the round seed is
`SeedMixer.Combine(datasetSeed, scenarioId, round)` with the scenario id hashed by FNV-1a rather than
`string.GetHashCode()`, which .NET randomizes per process.

## The pixel-scale change did nothing, which is worth knowing

V-P1 made `bank-verify` and `golden eval` read pixel scale from each run's own frame header instead of
one bank-wide value from the active NINA profile. The design expected this to shift real-bank numbers.
Measured, it does not:

| check | result |
|---|---|
| `--pixel-scale profile` vs the pre-change commit, `cwhite_2026` | **bit-identical** — P=0.8405923344947736, recall@≥12=0.31827176781002636, σ_focus=3.4197942376007235, sensor R²=0.9799173346975301, 9/9 aligned |
| `header` vs `profile` on `cwhite_2026` (scales differ **4.3×**: 0.880 vs 0.204 ″/px) | every metric identical to the last digit |
| `header` vs `profile` on synthetic D06 (scales differ 3.8×) | identical: P=1.000, recall@high=0.964, recall@all=0.817 |

**Reason:** `StarDetectorParams.PixelScale` reaches only `PSFModeler` and the reported analysis scale.
It never feeds a detection gate — not `Sensitivity`, not `NoiseClip`, not the structure layers, not the
HFR the autofocus fit consumes. A wrong bank-wide pixel scale was corrupting *reported units*, not
detections. The change is still correct and still worth having; it just is not a numbers-moving change.

A note on the older anchor: `docs/af-bank-noiseclip-sweep-results.md` records `cwhite_2026` at P=0.848,
recall@≥12=0.181, sensor R²=0.9933, 7/9 aligned. That is a **config-B** number (optimizer-enriched,
donut forced on) from the June prepass and sits behind every merge into `develop` since, so it is not a
valid guard for this change. The single-commit before/after above is the guard that isolates it.

## V3 — the precision/recall baseline, re-measured after the metric was repaired

> **The V2 table that stood here is retired.** It was scored by a metric that charged a false positive for
> every real star the golden policy had dropped ([F31](followups.md)), so **96% of the false positives it
> reported were real rendered stars** and every precision figure in it was an artifact. It survives in
> [`synthetic-af-bank-baseline.json`](synthetic-af-bank-baseline.json) — marked `SUPERSEDED`, with the
> precision keys renamed `precision_VOID` so a tool reading them for a gate fails loudly — as the audit trail
> of how a reproducible measurement can be reproducibly wrong.

`bank-verify --runs D:\SyntheticAutofocusBank --nc-sweep 2 --opt-a … --opt-b …`, schema **`afbank-verify/5`**,
header pixel scale, 17 runs, 0 failed. C0 = stock defaults; A = `optimize --per-run`; B = the same with donut
detection forced on. Cells are **recall@high / precision**.

| dataset | C0@nc2 | A | B | null (C0) | afR² (C0) |
|---|---|---|---|---|---|
| D01_ultrawide_40mm | 0.135 / 1.000 | 0.129 / 1.000 | 0.115 / 1.000 | 0.169 | 0.9098 |
| D02_rich_135mm | 0.475 / 1.000 | 0.451 / 1.000 | 0.442 / 1.000 | 0.067 | 0.9172 |
| D03_redcat_250mm | 0.400 / 1.000 | 0.396 / 1.000 | **0.537** / 1.000 | 0.036 | 0.9512 |
| D04_esprit_550mm | 0.810 / 1.000 | 0.855 / 1.000 | **0.697** / 1.000 | 0.113 | 0.9993 |
| D05_tec140_1000mm | 0.982 / 1.000 | 0.989 / 1.000 | 0.978 / 1.000 | 0.013 | 0.9999 |
| D06_sparse_1000mm | 0.982 / 1.000 | 0.964 / 1.000 | 0.988 / 1.000 | 0.000 | 0.9999 |
| D07_rc10_2000mm | 0.965 / 1.000 | 0.973 / 1.000 | 0.944 / 0.999 | 0.029 | 1.0000 |
| D08_c11_2800mm | 1.000 / 1.000 | 0.990 / 1.000 | 1.000 / 1.000 | 0.000 | 0.9996 |
| D09_c14_3800mm | 0.922 / 1.000 | 0.956 / **0.958** | 1.000 / 1.000 | 0.000 | 0.9996 |
| D10_rc16_3250mm_sparse | 0.983 / 1.000 | 0.931 / **0.910** | 0.966 / 0.991 | 0.000 | 0.9996 |
| D11_rc10_585_afbin2 | 0.887 / 1.000 | 0.850 / 1.000 | 0.917 / 1.000 | 0.000 | 1.0000 |
| D12_c14_585_afbin2 | 0.897 / 1.000 | 0.897 / 1.000 | 0.879 / 0.997 | 0.000 | 0.9994 |
| D13_apo200_1800mm (ε=0 control) | 1.000 / 1.000 | 1.000 / 1.000 | 0.986 / **1.000** | 0.003 | 0.9992 |
| D14_cdk14_2563mm_e47 | 0.979 / 1.000 | 0.990 / 1.000 | 0.984 / 0.997 | 0.002 | 1.0000 |
| D15_cdk20_3454mm_e47 | 0.954 / 1.000 | 0.943 / **0.968** | 0.931 / 1.000 | 0.000 | 1.0000 |
| D16_esprit550_ha3 | 0.886 / 1.000 | 0.908 / 1.000 | **0.739** / 1.000 | 0.000 | 0.9954 |
| D17_cdk14_oiii5 | 1.000 / 1.000 | 1.000 / **0.959** | 1.000 / 1.000 | 0.006 | 0.9995 |

**null** is the precision the same detections earn after being translated with wraparound — chance alone, and
therefore the floor this metric can read. It is reported because a precision column of 1.000 is exactly what a
saturated metric produces, and this baseline exists to replace one that failed that way. `truthViolations` — a
scored false positive sitting on a real rendered star, the F31 signature itself — is **0 on all 51 config
rows**.

### Reading it

**1. The detector produces essentially no false positives on this bank, and that is now a measurement.**
C0@nc2 is 1.000 on all 17 datasets against a null floor of 0.000–0.169, so the metric had ample room to read
lower and did not. The detector's real false-positive rate here is **0–9%**, concentrated entirely in config A
on four long-focal-length datasets.

**2. F23's direction survives; its magnitude does not.** Config A does cost precision, and on exactly the
datasets F23 named — D10 0.910, D09 0.958, D17 0.959, D15 0.968, all long focal length, all landing at
Sensitivity 0. But V2 reported that as 0.451–0.547. The effect is real and roughly **one-fifth** the size of
the artifact that hid it, and C0's "never below 0.942" was really "never below 1.000". No objective term is
justified by numbers this small; see [F32](followups.md) for why one could not have helped anyway.

**3. F24 is refuted, and the real cost of donut detection is recall.** F24's headline was "donut detection
costs precision even where donuts exist, and badly where they do not — D13 0.962 → 0.653". Re-measured,
**D13 scores 1.000 under config B**, and B's precision never falls below 0.991 on any dataset. What B actually
costs is *recall*, on the datasets that do not need it: D16 0.886 → **0.739**, D04 0.810 → **0.697**,
D13 1.000 → 0.986. And it genuinely helps where the PSF is annular or undersampled: D03 0.400 → **0.537**,
D11 0.887 → 0.917. That is a different finding with a different fix.

**4. Recall still tracks focal length exactly as physics predicts, then cliffs.** 0.135 → 0.475 → 0.400 →
0.810 → 0.982 as the PSF grows past `MinHFR` and becomes well sampled. Untouched by the metric repair — the
golden's `stars` list defines what must be found and F31 does not change it. The W-class band is still missed
badly, still for the `MinHFR` cliff ([F20](followups.md)), and D01 still detects *zero* stars at focus while
its goldens are fully populated.

**5. Watch the scored fraction on config A.** At a floored Sensitivity only 45–75% of detections enter the
precision ratio at all (D09 0.471, D17 0.485, D12 0.508); the rest land on real stars the golden dropped and
are unjudgeable. That is not a bias — protection removes a detection from both numerator and denominator, and
the null shows chance protection is ~0 — but precision on those rows rests on a materially smaller sample than
C0's, and a future comparison should say so rather than treating the two as equally well determined.

### Expectation-band scorecard

| band | result |
|---|---|
| W class C0@nc2 recall@high ≥ 0.90 | **MISS** (0.135 / 0.475 / 0.400) — F20, band left unwidened |
| W class C0@nc2 precision ≥ 0.98 | PASS (1.000 / 1.000 / 1.000) |
| M class C0@nc2 recall@high ≥ 0.97 | PARTIAL — D05/D06/D13 pass (0.982–1.000); D04 0.810 and D16 0.886 miss |
| M class C0@nc2 precision ≥ 0.98 | PASS (1.000 across the class) |
| L classes on config B, recall ≥ 0.90 | mostly PASS (0.879–1.000; D12 0.879 marginal) |
| L classes on config B, precision ≥ 0.98 | PASS (0.991–1.000) — was a MISS across the board under the V2 metric |
| Config A precision ≥ 0.95, non-donut | PASS (1.000 on every non-donut dataset) |
| afR² floor 0.95 | PASS on 15/17; D01 0.910 and D02 0.917 miss (F20) |

The bands themselves were re-derived for this baseline — the old 0.95/0.98 were fitted to the broken metric.
See [`synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json) for the reasoning behind each.

## V1 — the full S0–S6 convergence matrix

`synth-validate --scenarios S0,S1,S2,S3,S4,S5,S6 --max-rounds 4 --max-evals 120`, all 17 datasets.
**62 PASS · 15 FLAG · 8 FAIL** over 85 scored runs; 34 skipped as not-applicable, each with a recorded reason.

| scenario | what it perturbs | PASS | FLAG | FAIL | skipped |
|---|---|---|---|---|---|
| S0 | nothing (control) | 13 | 3 | 1 | — |
| S1 | step ×0.25 | 13 | 0 | 4 | — |
| S2 | step ×4 | 11 | 4 | 2 | — |
| S3 | exposure ×0.25 | 7 | 2 | 0 | 8 |
| S4 | binning 1 where 2 expected | 5 | 2 | 0 | 10 |
| S5 | donut off (obstructed only) | 7 | 2 | 0 | 8 |
| S6 | step + exposure both wrong | 6 | 2 | 1 | 8 |

**The convergence itself is sound.** From 4× too fine and 4× too coarse alike, the step recommender lands
within a few percent of target, usually in **one round**:

| dataset | S1 (from ×0.25) | S2 (from ×4) | target |
|---|---|---|---|
| D09_c14_3800mm | 30 → **117** | 472 → **115** | 118 |
| D15_cdk20_3454mm_e47 | 16 → **64** | 256 → **63** | 64 |
| D13_apo200_1800mm | 32 → **128** | 508 → **131** | 127 |
| D11_rc10_585_afbin2 | 14 → **55** | 220 → **47** | 55 |

That is the question the plan set out to answer, and the answer is yes. It also narrows F21: the half-width
collapses (D17 60→3, D02 6→7→1) are a specific defect, not general instability.

**S5 confirms donut detection earns its place on obstructed optics.** PASS here means the degradation signature
*appeared* when donut detection was wrongly switched off — 7 of 9 obstructed datasets degraded measurably
(D09 and D17 did not). Read against **F24**, which shows donut-on costing precision everywhere, the picture is
that donut detection is genuinely load-bearing for the AF fit on obstructed optics while being expensive for
precision — a real trade, not a free win.

### The 8 surviving failures, all accounted for

| cause | runs | entry |
|---|---|---|
| in-focus HFR below `MinHFR` — fit R² 0.65–0.93, recommendation unreliable | D01 S0/S1/S2, D02 S1, D03 S1 | F20 |
| a degenerate fit makes the recommender widen an already-too-wide sweep | D05 S2 | **F25 (new)** |
| a stuck binning recommendation starves the step update for all 4 rounds | D08 S1, D12 S6 | **F26 (new)** |

**F25**: D05 S2 starts 4× too wide (140 vs 35), fits at **R² = −0.223** — worse than a horizontal line — and the
recommender answers with **240**, pushing from 4× to nearly 7× too wide. It recovered only because the next
round happened to fit cleanly.

**F26**: D08 S1 holds step **21** for four consecutive rounds against a target of 82, at R² = 0.999–1.000,
because the binning-first update rule defers the step every round while F22's misread keeps recommending
binning 1. Two individually-defensible behaviours composing into a livelock.

### Three harness calibration bugs had to be fixed first

The matrix was scored three times. The first two scorings were wrong, in the same way each time: **the driver
defined convergence as "the recommender emits the same value again", and it never does** — every round is a
fresh noise realization, so the recommendation keeps nudging by a percent or two indefinitely.

| symptom | mis-scored |
|---|---|
| absolute 0.5-step band ⇒ any movement reads "moved away" | S0: 14 of 17 FAIL, including a literal 64 → 64 no-op |
| the same relative eps reused as *minimum per-round progress* ⇒ a round had to close 40% of the target | S1/S2/S6: 42 FLAGs, e.g. D15's 16 → 27 → 46 → 63 → 64 scored "no meaningful progress" |
| loop stops only on an exact no-op ⇒ `RoundsUsed` always = max | S2 "expected convergence within ≤2 rounds, used 4" when D13 converged in **one** |
| sub-tolerance jitter counted as oscillation | D15 S0's ±5% wander scored FAIL for "2 sign changes" |

Fixed consistently: **converged means "inside the tolerance band"**, everywhere. Verdicts went 40/26/19 →
**62/15/8** with byte-identical trajectories. Recording this because the first two scorings would have been
published as damning product findings, and they were the instrument's fault.

## Still not run

- The **CLI-parity check** (in-process round 0 == real `optimize --per-run` on the same folder) and the
  `--max-evals` 120-vs-250 stability check from the plan's self-verification order.

## Followups raised

| id | finding |
|---|---|
| **F25** | From a far-too-wide sweep the step recommender widens it further — D05 fits at R² = −0.223 and answers with a wider sweep still |
| **F26** | A stuck binning recommendation starves the step update indefinitely — D08 holds step 21 against a target of 82 for four rounds at R² ≈ 1.000 |
| **F23** | The optimizer objective has no precision term, so it trades precision away for marginal recall — C0 never drops below 0.942, config A reaches 0.451 |
| **F24** | Donut detection costs precision even where donuts exist, and worst on the ε=0 control (D13: 0.962 → 0.653) |
| **F19** | The exposure recommendation is decided by the 20 brightest stars, so a rich field can never earn one — a 3 nm Hα refractor still derives the 0.5 s floor because its 2.9° field holds 6835 stars |
| **F20** | Below `MinHFR` the autofocus objective collapses to exactly 0 with no diagnostic — and the fix is inside the existing search space: `MinHFR` is a curated axis (0.1–5.0), but D01/D02 leave it at the 1.2 default because `J = 0` gives the search no gradient, while D03 (non-zero `J`) does move it to 0.45 |
| **F21** | `StepSizeRecommender`'s half-width is not stable against noise: D17 gave 143.6 and 12.1 on two seeds, both fitting at R²=1.0000 — steps of 41 and 3 where 60 is correct |
| **F22** | Detection binning is a hard threshold at 4.5 px on a measurement that under-reads by 2–38%; D14 and D17 have identical optics and pixel size and get opposite recommendations |

## Reproduce

```
# generate the bank (84 s, 6.4 GB)
TestApp synth-bank --spec SynthBank\synthetic-bank-spec.json --out "D:\SyntheticAutofocusBank" --verify

# see every derived parameter without rendering a bank
TestApp synth-bank --spec SynthBank\synthetic-bank-spec.json --out "D:\SyntheticAutofocusBank" --dry-run

# the S0 control
TestApp synth-validate --spec SynthBank\synthetic-bank-spec.json \
    --out "D:\SyntheticAutofocusBank-validation" --scenarios S0 --max-rounds 2 --max-evals 120

# exact precision/recall on one dataset
TestApp golden eval --runs "D:\SyntheticAutofocusBank" --match centroid --out <dir>

# real-bank behaviour preserved behind the escape hatch
TestApp bank-verify --runs "D:\Autofocus Bank\cwhite_2026" --pixel-scale profile --nc-sweep 2 --out <dir>
```

Seeds are deterministic (`SeedMixer.Combine(bankSeed, datasetIndex, frameIndex)`, all recorded in each
dataset's `synthetic_meta.json`), so every number above regenerates exactly.
