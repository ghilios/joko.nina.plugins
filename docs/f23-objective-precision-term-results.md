# F23 — giving the optimizer objective a false-positive cost

Wave 1 of [`docs/af-recommender-hardening-design.md`](af-recommender-hardening-design.md).
Plan: [`plans/af-recommender-hardening-plan.md`](../plans/af-recommender-hardening-plan.md).
Baseline: [`docs/synthetic-af-bank-baseline.json`](synthetic-af-bank-baseline.json), transcribed from
[`docs/synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md).

*The question: the objective rewards star count and fit quality with no penalty for a false positive, so
the optimizer drives `BrightnessSensitivity` to its 0.0 floor and config-A precision collapses to 0.451.
Two candidate costs were implemented — a proxy term in `J`, and a hard floor on the searchable range.
Which one works, and does fixing this dissolve F22 and F26 as predicted?*

## Headline

| what | result |
|---|---|
| Mechanism (a) — false-positive proxy term in `J` | **REJECTED** — clears no acceptance gate |
| Mechanism (b) — floor on the searchable Sensitivity range | **REJECTED** — worse than doing nothing |
| Baseline reproducibility | **PASS** — C0 *and* config A reproduce the published V2 table exactly, all 17 datasets |
| F22 (binning misread) | **confirmed a symptom of F23**, measured with a toggle |
| F26 (binning livelock) | **does not reproduce** as a livelock — one deferral round, then converges |
| F21 (half-width instability) | reproduces **weaker** than recorded — 2.2× spread over 6 seeds, not 12× |
| Ships | **nothing behavioural.** `MarginalSnrStrength` defaults to 0; J is bit-identical to before |

**The short version.** The objective needs a false-positive cost — that much the bank confirmed again, in a
sharper form than before. But neither of the two mechanisms the spec proposed survives measurement, and they
fail for *opposite* reasons that together say something useful about where the fix has to live.

## The baseline reproduces exactly — but the artifact on disk is not what it looks like

The V2 measurement's optimizer prepass output directories no longer exist, and `bank-verify --opt-a` only
ever *reads* settings from disk (`BankVerifyRunner.cs:468-479`) — it never runs the optimizer. So config A
had to be re-derived before anything could be compared against it. Arm **H** (unmodified HEAD, same session,
same `harness_settings.json`) does that.

**It reproduces the published table exactly.** All 17 datasets, both the C0@nc2 column and the A column, to
every published digit — D09 0.451, D10 0.547, D12 0.653, D15 0.531, D17 0.465 — with the same six datasets
landing at `BrightnessSensitivity` 0.0 (D09, D10, D11, D12, D15, D17), precisely the design spec's Evidence-1
list. The optimizer, the bank regeneration, the detector and the scoring chain are all reproducible.

**The trap, recorded because it cost real time.** `optimize --per-run` writes `optimized_settings.json` into
**both** its `--out` tree and the run's own source folder (`OptimizationDiagnosticRunner.cs:698-703`), so the
copies sitting in each dataset's `attempt01/` are whichever prepass wrote last. Read as config A they show 5
datasets at 0.0 instead of 6, with D15 at 10.0 — which looks exactly like a reproducibility failure, and was
filed as one. They are **config B's** landings: every one carries `DefocusAwareDonutDetection: true`, the tell
that was in the file the whole time. Nothing was irreproducible; the artifact was misattributed. The surviving
finding is narrower and real — a stored landing does not record which invocation produced it ([F30](followups.md)).

The control arm was worth running regardless: it is what proved reproducibility rather than assuming it.

## The floor was calibrated, and the textbook value was wrong

`golden eval --params default --sensitivity <S>` scores exact precision against synthetic truth while
varying *only* the gate, in minutes per dataset. That turns the term's one free constant from a guess
into a measurement. Precision, with recall@high in brackets:

| gate `S` | D09_c14_3800mm | D17_cdk14_oiii5 | D13_apo200_1800mm |
|---|---|---|---|
| 0 | 0.804 [0.911] | 0.697 [1.000] | 0.850 [1.000] |
| 1.5 | 0.804 [0.911] | 0.697 [1.000] | 0.850 [1.000] |
| 2.5 | 0.804 [0.911] | 0.697 [1.000] | 0.850 [1.000] |
| 3 | 0.822 [0.911] | 0.697 [1.000] | 0.851 [1.000] |
| 4 | 0.939 [0.911] | 0.744 [1.000] | 0.857 [1.000] |
| 5 | 0.995 [0.911] | **0.892** [1.000] | **0.878** [1.000] |
| **6** | **1.000** [0.911] | **0.950** [1.000] | **0.945** [1.000] |
| 8 | 1.000 [0.911] | 0.994 [1.000] | 0.996 [1.000] |
| 10 | 1.000 [0.911] | 1.000 [1.000] | 1.000 [1.000] |

Three results, each of which changed the work:

**1. A floor at 2.5 would have been a no-op.** `S` = 0, 1.5 and 2.5 are bit-identical on all three
datasets. The algebra guarantees the gate is inert at or below `PeakResponse × StarClippingMultiplier`
= 0.75 × 2.0 = **1.5** (`StarDetector.cs:1746`, `:1763`), and measured, the inert region reaches 2.5–3.0
because no candidate happens to land in (1.5, 3]. The plan originally specified 2.5 for mechanism (b) —
that would have "implemented" the mechanism and measured nothing. Worth stating plainly because 1.0 is a
tempting value: it is `ExposureRecommender.SensitivityFloorThreshold`, but that constant is a *"the
optimizer hit its floor"* predicate, not a detection floor, and reusing it here cannot change detection
at all.

**2. The textbook 5σ threshold is not enough.** `MarginalSnrFloor` was first set to 5.0 from first
principles. Measured, it leaves D17 at 0.892 and D13 at 0.878 — both under the 0.90 acceptance bar. 6.0
clears all three.

**3. recall@high does not move anywhere in the sweep.** Not at any gate value, on any of the three
datasets. The bright tier (synthetic golden `high` = peak SNR ≥ 20) is simply not at risk from this gate,
which is why raising the floor costs nothing against the stated acceptance metric. Past `S` = 8 the trade
inverts: real stars start being lost for precision that is already exhausted.

## What was built

Two mechanisms, both shipped in one binary so the arm is a flag rather than a build — a mis-built arm
cannot silently corrupt a result.

**(a) `OptimizationObjective.SMarginalSnr`** — a multiplicative penalty on the near-focus fraction of
accepted stars whose measured Sensitivity-gate SNR falls below `MarginalSnrFloor`, shaped exactly like
the existing `SHfrOutlier`: same near-focus window, same recovery-frame exemption, same
`PenaltyFromFraction` shape, same run-level fallback guards.

The signal needed no plumbing. `Star.MeasuredSensitivity` is the very quantity the detector's gate
compares — `NormalizedBrightness / noiseSigma`, a peak-SNR in σ — and it already reaches the objective as
`RunEvaluationMetrics.FrameStarSnrs`, where it had been carried as inert data.

The near-focus window is load-bearing rather than decorative: far from focus a *real* star spreads out
and its per-pixel peak SNR legitimately collapses, so an absolute floor applied to every frame would
penalise correct defocused detections.

**(b) `OptimizerVariable.CreateCuratedSet(seed, sensitivityLower)`** — a hard floor on the searchable
Sensitivity range. `DefaultSensitivityLower` stays 0.0, so shipping behaviour is unchanged and the floor
is exercised only through the harness.

**Both use floor 6.0**, so the head-to-head isolates exactly one variable: *soft and data-adaptive*
versus *hard and unconditional*.

### Why (a) is not just (b) with extra steps

Every accepted star satisfies `sensitivity > p.Sensitivity`, so the accepted-SNR sample is
**left-censored at exactly the knob being tuned**. The marginal fraction is therefore identically 0
whenever `p.Sensitivity ≥ MarginalSnrFloor`, and can only become non-zero below it. The term is a *soft*
floor — which is the whole point:

- it is continuous, so a dataset can still buy its way below the floor when the recall gain is worth it;
- it charges for **admitted marginal detections**, not for the knob's value, so a field whose structure
  map yields few sub-floor candidates pays nothing for landing low;
- below the floor it charges regardless of *which* knob opened the door (NoiseClip, StructureLayers), not
  only the Sensitivity axis.

D13 is the discriminating dataset: it landed at Sensitivity 2.5 under the published config A. (b) must
force it to 6; (a) should stop wherever its sub-6 tail thins out.

That is a claim the bank can settle, and settling it is what the arms below are for.

## Arms

Three code states, one binary, arm selected by flag (`--marginal-snr-strength`, `--sensitivity-floor`) so a
mis-built arm cannot silently corrupt a result. Scored by `bank-verify --nc-sweep 2 --opt-a <arm>`,
schema `afbank-verify/3`, header pixel scale. Cells are **config-A precision**; bold = below the 0.90 gate.

| dataset | C0@nc2 | H (control) | b (floor) | a (term) | H rec@high | a rec@high |
|---|---|---|---|---|---|---|
| D01_ultrawide_40mm | 0.963 | 0.970 | 0.970 | 0.970 | 0.129 | 0.129 |
| D02_rich_135mm | 0.991 | 0.991 | 0.991 | 0.991 | 0.451 | 0.451 |
| D03_redcat_250mm | 0.988 | 0.992 | 0.992 | 0.992 | 0.396 | 0.396 |
| D04_esprit_550mm | 0.979 | 0.977 | **0.859** | 0.977 | 0.855 | 0.855 |
| D05_tec140_1000mm | 0.986 | 0.966 | 0.958 | 0.966 | 0.989 | 0.989 |
| D06_sparse_1000mm | 0.978 | 0.984 | 0.981 | 0.984 | 0.964 | 0.964 |
| D07_rc10_2000mm | 0.953 | 0.941 | 0.963 | 0.941 | 0.973 | 0.973 |
| D08_c11_2800mm | 0.959 | **0.748** | 0.919 | **0.748** | 0.990 | 0.990 |
| D09_c14_3800mm | 0.993 | **0.451** | **0.850** | 0.944 | 0.956 | 0.933 |
| D10_rc16_3250mm_sparse | 1.000 | **0.547** | **0.757** | **0.814** | 0.931 | 0.983 |
| D11_rc10_585_afbin2 | 0.986 | **0.732** | 0.969 | **0.833** | 0.850 | 0.842 |
| D12_c14_585_afbin2 | 0.952 | **0.653** | **0.547** | **0.659** | 0.897 | 0.841 |
| D13_apo200_1800mm | 0.962 | 0.985 | **0.860** | 0.985 | 1.000 | 1.000 |
| D14_cdk14_2563mm_e47 | 0.965 | 0.948 | **0.739** | 0.948 | 0.990 | 0.990 |
| D15_cdk20_3454mm_e47 | 0.979 | **0.531** | **0.527** | **0.587** | 0.943 | 0.885 |
| D16_esprit550_ha3 | 0.985 | **0.744** | **0.740** | **0.744** | 0.908 | 0.908 |
| D17_cdk14_oiii5 | 0.942 | **0.465** | **0.650** | **0.735** | 1.000 | 1.000 |

**Scorecard against the acceptance gates** (precision ≥ 0.90 everywhere; recall@high loss ≤ 0.02; σ_focus no
worse than 20%):

| arm | precision < 0.90 | recall@high drop > 0.02 | σ_focus worse > 20% | verdict |
|---|---|---|---|---|
| H (control) | 8/17 | — | — | — |
| b | **9/17** | | | REJECTED |
| a | **7/17** | 3 (D09, D12, D15) | 3 (D09, D10, D11) | REJECTED |

### (b) is worse than doing nothing, and that is the informative part

The hard floor helps three datasets and **breaks four that were healthy**: D13 0.985 → 0.860, D14 0.948 →
0.739, D04 0.977 → 0.859. Net 9 failures against the control's 8.

The mechanism is worth stating because it generalises: **restricting the domain of one axis does not remove
the incentive, it redirects it.** The objective still pays for star count and charges nothing for a false
positive, so when the floor takes stars away the search wins them back by loosening whatever gate is still
free. Junk enters through a different door. Any future "just clamp the knob" proposal inherits this result.

### (a) works where it can fire, and is structurally escapable

Real gains — D09 **0.451 → 0.944**, D17 0.465 → 0.735, D10 0.547 → 0.814 — but no gate cleared, and three
σ_focus regressions bought with them.

The failure is not a mis-set constant, which is why no floor value fixes it. The gate guarantees

```
sensitivity  >=  PeakResponse × StarClippingMultiplier
```

and **both of those are searchable curated axes**. So the optimizer can lift the statistic's own *lower bound*
above `MarginalSnrFloor`, making the penalty structurally unable to fire while the false positives remain:

| dataset | landed Sensitivity | StarClip | PeakResponse | PR × StarClip | floor | precision |
|---|---|---|---|---|---|---|
| D12 | 0 | 6.25 | **1.00** | **6.25** | 6.0 | 0.659 |
| D15 | 0 | 6.75 | **1.00** | **6.75** | 6.0 | 0.587 |

Both land *just past* the floor, with `PeakResponse` pinned at the top of its searchable range. A floor of 8
would be escaped at 8. The plan budgeted one retune round; it was deliberately not spent, because the
diagnosis rules the retune out rather than leaving it uncertain.

**And the floor is not the whole story anyway.** D08 lands at Sensitivity 8 — above any floor, term
legitimately inert — with precision 0.748. So there is a second false-positive source that operates at
perfectly healthy Sensitivity values, which the spec's root-cause chain does not describe.

### What ships

`MarginalSnrStrength` defaults to **0**. J is bit-identical to before, and the shipping optimizer is
unchanged. The implementation, its tests, and both harness flags stay so the next attempt starts from a
measured position rather than from scratch.

## Re-measuring the eight followups

The design spec's prediction on record: F22 and F26 substantially weaken or disappear; F19, F20, F21 and F25
are untouched. Measured:

| id | verdict | evidence |
|---|---|---|
| **F23** | **open** | neither mechanism passes; a second FP source exists at healthy Sensitivity (D08 lands at 8, precision 0.748) |
| **F22** | reproduces — and **confirmed caused by F23** | toggle evidence below |
| **F26** | **does not reproduce as a livelock** | D08 S1: 21 → 21 → 36 → **62**, converged in 3 rounds, against a recorded 4-round stall. One round is still lost to the binning-first deferral. D12 S6 still fails to converge (final 80 vs 141) — but **does** converge with the term on (103) |
| **F21** | reproduces **weaker than recorded** | six seeds on D17: half-widths 143.6 / 90.0 / 156.0 / 201.5 / 90.0 / 156.0 → **2.2× spread**, recommended step never below 26. The entry's 12× collapse (143.6 vs 12.1 → steps 41 and 3) does not appear. The 143.58 reproduces to the hundredth, so this is rarity, not nondeterminism |
| **F25** | manifestation seed-dependent; entry stands | D05 S2 fits at R² = **−0.106** and **holds** at 140 rather than widening to 240. The code gap (no fit-quality gate) is untouched, so whether a degenerate fit holds or widens is left to the noise realization |
| **F20** | reproduces exactly | D01/D02 land `FinalJ = 0.00000`; recall@high 0.129 / 0.451 / 0.396 on D01/D02/D03 |
| **F19** | unchanged | no wave-1 code touches the exposure statistic; still the median 20th-brightest SNR |
| **F24** | **not re-measured** | needs a config-B (`--donut`) prepass, which wave 1 did not run. Stated as unmeasured rather than cleared |

### F22 is a symptom of F23 — measured, not inferred

Because the objective term is a *switch* (`--marginal-snr-strength`), the same frames and the same seed can be
scored with the optimizer landing at the Sensitivity floor or above it. Everything else is held constant.
`D17_cdk14_oiii5` scenario S0:

| objective | landed Sensitivity | measured in-focus HFR | binning recommended |
|---|---|---|---|
| term OFF (shipping) | 0.0 | **3.27 px** | 1 |
| term ON | 7.0 | **4.66 px** | **2** |

The 4.5 px binning threshold sits between the two readings, so the Sensitivity landing *alone* flips the
factor — a 30% shift in measured HFR from nothing but admitted noise. The same signature appears on
`D12_c14_585_afbin2` S6 round 2 (Sensitivity 0 → 6 moves the in-focus HFR 2.99 → 4.45, **+49%**) and on
`D08_c11_2800mm` S0.

This retires one of F22's two candidate fixes. "Calibrate out the systematic bias" is wrong: the bias is not a
property of the measurement, it tracks what the optimizer chose to detect. Hysteresis around the threshold
remains viable.

That the term is too weak to *ship* and still strong enough to *prove the mechanism* is the useful shape of
this result — a rejected fix that settles an attribution is worth more than an unmeasured one that doesn't.

### A fourth harness-calibration bug

Found while re-measuring F25. `D05_tec140_1000mm` S2 is scored:

```
converged: true
stoppedReason: "converged (round applied nothing)"
finalStepSize: 140     stepBehavioral: 35     deltaStepVsExpected: +105
assertions: A3 verdict=FAIL  "final step 140 outside [21,56] = [0.6,1.6]x step_behavioral (35)"
```

A degenerate fit produces a no-op recommendation, and the loop reads "nothing changed" as convergence — at a
step four times too wide, while the assertion correctly fails it. This inflates convergence counts on exactly
the runs that are most broken, and joins the three calibration bugs already recorded in
[`synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md). Convergence should require
being inside the tolerance band, not merely unchanged.

## The real bank — transfer guard

`bank-verify --runs "D:\Autofocus Bank" --nc-sweep 2 --pixel-scale header`, 19 runs, 0 failed.

**The documented anchor reproduces exactly.** `cwhite_2026` C0@nc2: precision **0.8405923344947736**,
recall@≥12 **0.31827176781002636**, σ_focus **3.4197942376007235**, sensor R² **0.9799173346975301** — every
digit as recorded. Third independent confirmation this session that the measurement chain is sound.

**The F23 pathology transfers, in the form F11 predicted.** Real-bank precision is a lower bound, so it
*understates* the problem; what is visible instead is the recall side of the same trade:

| run | C0@nc2 recall@≥12 | config A | C0 precision → A |
|---|---|---|---|
| CWhiteFocus | 0.810 | **0.327** | 0.954 → 1.000 |
| mccomiskey | 0.871 | **0.079** | 0.838 → 0.992 |
| uneven | 0.932 | **0.319** | 0.990 → 1.000 |
| standard_example1 | 0.893 | **0.268** | 0.976 → 0.998 |
| vsn07 | 0.751 | **0.207** | 0.968 → 1.000 |

The landings give up 50–90% of recall while precision reads *higher*. On synthetic data the same landings are
visibly buying junk; on real data they look like precision wins, because the false positives admitted are not
in the golden and cannot be counted. That is F11 in action, and it is the clearest possible statement of why
the synthetic bank was worth building.

**Arm a on the real bank is a wash — no transfer benefit demonstrated.** recall@≥12 against arm H, 17 scorable
runs (`SorenVance` and `lumos` score NaN — pre-existing bank issues, F13):

| better by > 0.02 | worse by > 0.02 |
|---|---|
| mccomiskey **+0.320** (0.079 → 0.399) | caboose −0.142 |
| toml999 **+0.210** | CWhiteFocus −0.137 |
| timmer +0.090 | LinwoodFocus −0.067 |
| FlyData +0.071 | Panos −0.036, bobp_m101 −0.027, vsn07 −0.026 |

Four better, six worse; precision flat to marginally higher everywhere (and a lower bound, so it cannot
adjudicate). The single largest movement is a real improvement — `mccomiskey` is the run
[F4](followups.md) cites for catastrophic star-shedding (recall 0.871 → 0.079 under the optimizer), and the
term recovers a third of it. But there is no bank-wide gain to claim, which is what the synthetic result
already predicted.

## What wave 2 should do differently

1. **The proxy must be computed on a statistic the search cannot lift.** `peak/σ` with `PeakResponse` out of
   the expression. `Star` exposes only the gated `MeasuredSensitivity` today, so this needs plumbing — but the
   plumbing is one line at the seam that already reads `LowSensitivity`/`TooFlat`
   (`RunEvaluationLoader.cs:331-335`).
2. **Find the second false-positive source.** D08 lands at Sensitivity 8 with precision 0.748 and D16 at 2.5
   with 0.744, both unmoved by the term. A floored Sensitivity is not the whole mechanism, and the spec's
   root-cause chain does not describe this part.
3. **Do not restrict the search domain.** Measured, it is worse than doing nothing.
4. **Re-check the coverage reward.** `SCoverage` (`Wcov = 0.05`) rewards a 3×3 tiling holding ≥1 accepted star,
   and spatially uniform noise *raises* occupancy — so on sparse fields the objective may be paying for the
   very detections the FP term is trying to charge for. Unmeasured; an ablation arm with `Wcov = 0` on the
   moved datasets would settle it.
5. **Fix the convergence predicate** before the next V1 matrix, or its PASS counts will keep flattering the
   most degenerate runs.

## Reproduce

```
# calibrate the floor on one dataset (minutes)
TestApp golden eval --runs "D:\SyntheticAutofocusBank\D09_c14_3800mm" \
    --params default --sensitivity 6 --match centroid --match-radius 12 --out <dir>

# arm H (HEAD behaviour, from any build)
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out <H> --max-evals 250 \
    --marginal-snr-strength 0

# arm b — hard floor only
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out <b> --max-evals 250 \
    --sensitivity-floor 6 --marginal-snr-strength 0

# arm a — the objective term only (shipping defaults)
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out <a> --max-evals 250

# score any arm
TestApp bank-verify --runs "D:\SyntheticAutofocusBank" --out <dir> --nc-sweep 2 \
    --match-radius 12 --pixel-scale header --opt-a <arm>
```

Each `optimize` run echoes its arm into the log (`objective: marginalSnr strength=… floor=…; searchable
Sensitivity lower bound=…`), so a log identifies its own configuration.
