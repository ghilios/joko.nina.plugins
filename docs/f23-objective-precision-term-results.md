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

<!-- filled in after the arms complete -->

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

<!-- filled in after the arms complete -->

## Re-measuring the eight followups

<!-- filled in after the arms complete -->

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
