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

Everything below is **flagged, not fixed**. Six product findings became `docs/followups.md` entries
(F19–F24); none of them changed product behaviour in this branch.

## Headline

| what | result |
|---|---|
| Bank generated | 17/17 datasets, 6.4 GB, 84 s |
| Header round-trip (`--verify`, risk R1) | **PASS** — XPIXSZ/FOCALLEN/XBINNING/EXPTIME and the composed arcsec/px all land, on every dataset |
| Determinism | **PASS** — D07 + D12 regenerated from seeds: 36/36 frames and golden sidecars bit-identical |
| Golden precision (D06, `golden eval`) | **1.000** — 205 TP, **0 FP**. Exact, not a lower bound |
| S0 control (harness self-test) | **13 PASS · 1 FLAG · 3 FAIL**; R² = 1.0000 on 15 of 17 |
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

## S0 — the control, and what it found

S0 bootstraps each dataset at its own expected optimum, so the recommendations should be ~no-ops.
Two rounds, `--max-evals 120`.

**Result: 13 PASS · 1 FLAG · 3 FAIL.**

| dataset | verdict | R² (r0/r1) | half-width (r0/r1) | step recommended (r0/r1) | note |
|---|---|---|---|---|---|
| D01_ultrawide_40mm | **FAIL** | 0.648 / 0.949 | 35.6 / 43.1 | 10 / 12 | sub-`MinHFR` (F20) |
| D02_rich_135mm | **FAIL** | 0.916 / 0.932 | 23.2 / **2.5** | 7 / **1** | sub-`MinHFR` (F20) + half-width collapse (F21) |
| D03_redcat_250mm | PASS | 0.999 / 1.000 | 75.7 / 82.3 | 22 / 24 | |
| D04_esprit_550mm | PASS | 1.000 / 1.000 | 60.2 / 66.3 | 17 / 19 | |
| D05_tec140_1000mm | PASS | 1.000 / 1.000 | 130.1 / 147.9 | 37 / 42 | |
| D06_sparse_1000mm | PASS | 1.000 / 1.000 | 129.3 / 145.1 | 37 / 41 | |
| D07_rc10_2000mm | PASS | 1.000 / 1.000 | 183.8 / 176.3 | 53 / 50 | |
| D08_c11_2800mm | PASS | 1.000 / 1.000 | 303.1 / 315.2 | 87 / 90 | |
| D09_c14_3800mm | PASS | 1.000 / 1.000 | 399.1 / 324.9 | 114 / 93 | |
| D10_rc16_3250mm_sparse | PASS | 1.000 / 1.000 | 330.0 / 327.8 | 94 / 94 | |
| D11_rc10_585_afbin2 | PASS | 1.000 / 1.000 | 183.5 / 158.6 | 52 / 45 | |
| D12_c14_585_afbin2 | **FLAG** | 0.999 / 1.000 | 396.9 / 316.9 | 113 / 91 | binning misread (F22) deferred the step update both rounds |
| D13_apo200_1800mm | PASS | 1.000 / 1.000 | 436.8 / 446.8 | 125 / 128 | the ε=0 donut control |
| D14_cdk14_2563mm_e47 | PASS | 1.000 / 1.000 | 201.7 / 201.8 | 58 / 58 | |
| D15_cdk20_3454mm_e47 | PASS | 1.000 / 1.000 | 168.8 / 221.1 | 48 / 63 | |
| D16_esprit550_ha3 | PASS | 1.000 / 1.000 | 73.4 / 76.1 | 21 / 22 | |
| D17_cdk14_oiii5 | **FAIL** | 1.000 / 1.000 | 143.6 / **12.1** | 41 / **3** | half-width collapse (F21) |

**Reading it:** the fits are essentially perfect and most step trajectories are drifts of a few
percent — D14 recommends 58 twice running, D10 recommends 94 twice. Every non-PASS maps to a filed
followup: D01/D02 to F20 (in-focus HFR below `MinHFR`), D12 to F22 (binning misread), D02/D17 to F21
(half-width collapse). Nothing failed for a reason that is not written down.

The run also doubles as a determinism check on the *validation* path, not just the generator: rerunning
the whole S0 pass reproduced every half-width to the tenth — 143.6 and 12.1 on D17 both times — because
the round seed is `SeedMixer.Combine(datasetSeed, scenarioId, round)` with the scenario id hashed by
FNV-1a rather than `string.GetHashCode()` (which .NET randomizes per process).

### The harness had to be corrected before this table could be believed

The first S0 pass reported **FAIL on 14 of 17** datasets. That was the instrument's fault, not the
product's: A1 compared distance-to-target against an *absolute* 0.5-step tolerance, while the design
declares a *relative* one (`StepSizeTolerance = 0.4`). On a control that starts at the target, any
movement whatsoever then reads as "moved away" — including D15's literal no-op at 64 → 64. A baseline
harness that manufactures failures is worse than no baseline, so the tolerance was made relative. The
three genuine instabilities above survive that correction; they are outside a 40% band on their own
merits.

Three further harness defects were found and fixed the same way — the exposure derivation above,
`--verify` asserting the wrong side of NINA's `XPIXSZ`/`BinX` convention (it caught the two AF-bin-2
datasets: wrote 5.8, read back 2.9), and A3 computing its reference fixed point on the *optical* curve
rather than the pixelization-floored one the detector can actually observe (which alone would have
failed D01–D03).

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

## V2 — the precision/recall baseline, and the biggest result in this document

`bank-verify --runs D:\SyntheticAutofocusBank --nc-sweep 2,3,4 --opt-a … --opt-b …`, schema
`afbank-verify/3`, header pixel scale, 17 runs, 0 failed. C0 = stock defaults; A = `optimize --per-run`;
B = the same with donut detection forced on. Cells are **recall@high / precision**.

| dataset | C0@nc2 | A | B | afR² (C0) |
|---|---|---|---|---|
| D01_ultrawide_40mm | 0.135 / 0.963 | 0.129 / 0.970 | 0.115 / 0.967 | 0.9098 |
| D02_rich_135mm | 0.475 / 0.991 | 0.451 / 0.991 | 0.442 / 0.990 | 0.9172 |
| D03_redcat_250mm | 0.400 / 0.988 | 0.396 / 0.992 | 0.537 / 0.991 | 0.9512 |
| D04_esprit_550mm | 0.810 / 0.979 | 0.855 / 0.977 | 0.697 / 0.977 | 0.9993 |
| D05_tec140_1000mm | **0.982** / 0.986 | 0.989 / 0.966 | 0.978 / 0.992 | 0.9999 |
| D06_sparse_1000mm | **0.982** / 0.978 | 0.964 / 0.984 | 0.988 / 0.977 | 0.9997 |
| D07_rc10_2000mm | 0.965 / 0.953 | 0.973 / 0.941 | 0.944 / 0.859 | 1.0000 |
| D08_c11_2800mm | **1.000** / 0.959 | 0.990 / **0.748** | 1.000 / 0.781 | 0.9996 |
| D09_c14_3800mm | 0.922 / 0.993 | 0.956 / **0.451** | 1.000 / **0.448** | 0.9992 |
| D10_rc16_3250mm_sparse | 0.983 / **1.000** | 0.931 / **0.547** | 0.966 / 0.661 | 0.9996 |
| D11_rc10_585_afbin2 | 0.887 / 0.986 | 0.850 / **0.732** | 0.917 / 0.627 | 1.0000 |
| D12_c14_585_afbin2 | 0.897 / 0.952 | 0.897 / **0.653** | 0.879 / **0.506** | 0.9994 |
| D13_apo200_1800mm (ε=0 control) | **1.000** / 0.962 | 1.000 / 0.985 | 0.986 / **0.653** | 0.9992 |
| D14_cdk14_2563mm_e47 | 0.979 / 0.965 | 0.990 / 0.948 | 0.984 / 0.671 | 0.9997 |
| D15_cdk20_3454mm_e47 | 0.954 / 0.979 | 0.943 / **0.531** | 0.931 / 0.708 | 0.9992 |
| D16_esprit550_ha3 | 0.886 / 0.985 | 0.908 / **0.744** | 0.739 / 0.983 | 0.9954 |
| D17_cdk14_oiii5 | **1.000** / 0.942 | 1.000 / **0.465** | 1.000 / 0.567 | 0.9988 |

**Reading it — three things, in order of importance.**

**1. The optimizer trades precision away for marginal recall (F23).** C0's precision never falls below
**0.942** on any dataset. Config A falls to 0.451. On D09 the optimizer bought +0.034 recall for −0.542
precision; on D10, +0.000 recall (it lost 0.052) for −0.453 precision. The objective rewards star count
and fit quality and has no false-positive term — and could not have had one, because on the real bank
precision is only a lower bound (F11). This is the single result that most justifies the bank existing:
it is invisible without exact truth, and it reframes every prior "A beat C0" conclusion.

**2. Recall tracks focal length exactly as physics predicts, then cliffs.** 0.135 → 0.475 → 0.400 →
0.810 → **0.982** as the PSF grows past `MinHFR` and becomes well sampled. The design's M-class anchor
("well-sampled ⇒ essentially everything", ≥0.97) is **validated** at 0.982/0.982/1.000. The W-class band
(≥0.90) is **missed badly**, and for a reason worth stating: it was set assuming a gentle pixelization
loss, but the real mechanism is the hard `MinHFR` cliff (F20) — D01 detects *zero* stars at focus while
its goldens are fully populated. The band is left as written, with a note; it should be revisited when
F20 is addressed, not widened to fit.

**3. Donut detection costs precision everywhere, including where donuts are absent (F24).** D13 is the
1800 mm **unobstructed** control, present exactly so "donut" and "long focal length" cannot be
confounded — config B takes its precision from 0.962 to **0.653**. Two design assumptions also fell:
C0 is *not* broken on synthetic donut datasets (D08 and D17 reach recall 1.000 at precision 0.942–0.959),
unlike the real bank's donut runs; and `donutEffect` across the 17 A/B pairs is `donutHelpedAF: 5`,
`donutHurtSensor: 6` — no clear win either way.

The NC sweep independently recommends **NC = 2** (recall@high 0.95, precision 0.98, still rising at the
bottom of the swept range), which agrees with the real bank's conclusion in
`af-bank-noiseclip-sweep-results.md` — a useful cross-check that the synthetic bank is not living in its
own universe.

### Expectation-band scorecard

| band | result |
|---|---|
| W class C0@nc2 recall@high ≥ 0.90 | **MISS** (0.135 / 0.475 / 0.400) — F20, band left unwidened |
| W class C0@nc2 precision ≥ 0.95 | PASS (0.963 / 0.991 / 0.988) |
| M class C0@nc2 recall@high ≥ 0.97 | PARTIAL — D05/D06/D13 pass (0.982–1.000); D04 0.810 and D16 0.886 miss |
| M class C0@nc2 precision ≥ 0.95 | PASS (0.962–0.986) |
| L classes on config B, recall ≥ 0.90 | mostly PASS (0.879–1.000; D12 0.879 marginal) |
| L classes on config B, precision ≥ 0.90 | **MISS across the board** (0.448–0.859) — F23/F24 |
| Config A precision ≥ 0.98, non-donut | PARTIAL — D02/D03 pass; D16 0.744 misses badly |
| afR² floor 0.95 | PASS on 15/17; D01 0.910 and D02 0.917 miss (F20) |

## What was not run

Stated plainly so the baseline is not read as more complete than it is:

- **V1 scenarios S1–S6 were not run.** Only S0, the control, completed (all 17 datasets, twice). The
  perturbation scenarios — step ×0.25 and ×4, exposure ×0.25, binning, donut-off, combined — are
  implemented and smoke-tested but the matrix is several hours of compute that this session did not
  reach. S0 was the gating self-test and it is done; S1–S6 remain.
- **The CLI-parity check and the `--max-evals` 120-vs-250 stability check** listed in the plan's
  self-verification order were not run.

## Followups raised

| id | finding |
|---|---|
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
