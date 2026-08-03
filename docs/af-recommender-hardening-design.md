# AF recommender hardening — design

Eight followups (F19–F26) came out of building and validating the synthetic AF bank
([`docs/synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md)). This spec says
which of them are actually independent, what to change, in what order, and what each change has to prove
before it ships.

**The headline: three of the eight share one root cause, and it is fixable with one change.** That
reorders everything, so it comes first.

## The root cause chain

The optimizer objective rewards star count and fit quality and carries **no penalty for a false
positive**. It could never have carried one — on the real bank precision is only a lower bound
([F11](followups.md)), so the quantity simply was not measurable. Given exact synthetic truth, the
consequence is visible and stark:

```
objective has no false-positive penalty
  └─> optimizer drives BrightnessSensitivity to 0 (the floor):
      accepting noise blobs raises the star count with no cost
        ├─> precision collapses ................................... F23
        └─> in-focus HFR median is polluted by those noise blobs
            └─> measured HFR drops below the 4.5px binning boundary . F22
                └─> binning-first update ordering defers the step
                    every round, forever ........................... F26
```

### Evidence 1 — Sensitivity 0 separates the precision failures almost perfectly

`optimize --per-run` (config A) landed settings vs that same config's measured precision, 17 datasets:

| config-A landed `BrightnessSensitivity` | n | mean precision |
|---|---|---|
| **0.0** (floor) | 6 | **0.563** |
| > 0 | 11 | **0.931** |

The six at zero are the six worst in the bank — D09 0.451, D17 0.465, D15 0.531, D10 0.547, D12 0.653,
D11 0.732. Every dataset that landed above 2.5 scores ≥ 0.941. Stock defaults (C0) never drop below
0.942 on any dataset, so this is the optimizer actively choosing a worse configuration than the one it
started from, by a metric it cannot see.

### Evidence 2 — the HFR "bias" behind F22 is not a bias

F22 was originally written as a systematic under-read to be calibrated out. Measuring measured-vs-optical
in-focus HFR across all 17 datasets shows that is wrong twice over.

It is **bimodal, not systematic**:

| regime | datasets | measured vs optical |
|---|---|---|
| HFR ≳ 1.8 px | D05–D15, D17 | −4% to −23% |
| HFR ≲ 1.1 px | D01–D04, D16 | **+26% to +234%** |

The over-read at small HFR is the pixelization floor asserting itself — D01's optical 0.23 px measures
as 0.77 px. (Usefully, that independently validates the 0.70 px floor estimate the bank's derivations
use for R2.)

And it is **not reproducible**, tracking Sensitivity rather than optics. Same frames, same seeds, two
runs:

| dataset | run A: HFR / Sensitivity | run B: HFR / Sensitivity | Δ HFR | binning |
|---|---|---|---|---|
| D17_cdk14_oiii5 | 3.27 / **0.0** | 4.94 / 8.0 | **+51%** | 1 → 2 (flips) |
| D15_cdk20_3454mm_e47 | 4.02 / **0.0** | 5.29 / 6.0 | **+32%** | 1 → 2 (flips) |
| all 15 others | — | — | ≤ ±3% | stable |

Only the two runs whose Sensitivity landed at zero moved, and both flipped the binning recommendation.
**F22 is therefore a symptom of F23, not an independent defect**, and "calibrate the bias" would have
been the wrong fix — a real cost of batching that this measurement avoided.

## Change 1 — give the objective a false-positive cost (F23)

The substantive change, and the only one in wave 1.

**What.** The objective must stop rewarding indiscriminate detection. Two candidate mechanisms, not
mutually exclusive; the spec deliberately does not pre-commit, because the bank can now decide between
them empirically:

1. **A false-positive proxy term in `J`.** True precision is unavailable on real data — that is the
   whole problem — so the term has to be built from signals the detector already produces without a
   golden reference. Candidates already collected: the rejected-candidate diagnostics
   (`CollectRejectedCandidateDiagnostics`), the ratio of accepted stars to structure candidates, and the
   distribution of accepted-star SNR (a Sensitivity-0 landing produces a long tail of marginal
   detections that a healthy landing does not).
2. **A floor on the searchable `BrightnessSensitivity` range.** Cruder, and it treats the symptom rather
   than the objective's blindness, but it is a one-line change to `OptimizerVariable.CreateCuratedSet`
   and it is trivially safe to evaluate. Worth measuring as the baseline any cleverer term must beat.

**Acceptance metric.** On the synthetic bank, config A precision must not fall below **0.90** on any
dataset (currently six are below, worst 0.451), *without* losing more than **0.02** recall@high on any
dataset. σ_focus must not worsen by more than 20%. Both banks get scored — the real one to confirm no
regression in the numbers it can measure, the synthetic one because it is the only place the acceptance
metric is exactly measurable.

**Then re-measure before touching anything else.** The prediction on record: F22 and F26 substantially
weaken or disappear; F19, F20, F21 and F25 are untouched because they are recommender-side, downstream
of the landing rather than caused by it. If that prediction is wrong, the wave-2 grouping below needs
revisiting before it is executed.

## Wave 2 — recommender hardening (only what survives re-measurement)

All four are in the recommenders, all validate on the V1 matrix alone (no optimizer prepass), and they
should be batched **together** because they share a validation run and do not interact.

### F21 + F25 — `StepSizeRecommender` robustness

One function, two failure modes, one fix.

- **F21**: two sweeps of D17 at the same step, differing only in noise seed, *both fitting at
  R² = 1.0000*, produced half-widths of 143.6 and 12.1 — recommended steps of 41 and **3** where 60 is
  correct.
- **F25**: from a 4×-too-wide start, D05 fits at **R² = −0.223** (worse than a horizontal line) and the
  recommender answers with a *wider* sweep still — 4× to nearly 7× too wide.

**What.** Gate `Recommend` on fit quality: below an R² floor, hold or shrink the current step, never
widen it, and say why in the returned recommendation. Separately, make `FindHalfWidth`'s outward search
robust to a fitted minimum that lands slightly high — the F21 entry's hypothesis (the coarse walk
terminating on a spurious near-focus crossing) needs confirming on D17's two saved sweeps before the fix
is written.

**Acceptance.** D17 S0 across ≥5 seeds: half-width spread within ±20%. D05 S2: no round widens the sweep
from a negative-R² fit. V1 matrix: no PASS becomes FLAG or FAIL.

### F20 — seed `MinHFR` out of the zero-gradient plateau

Below `MinHFR` the objective is identically 0, so the search has no gradient and never lowers the very
knob that would help — even though `MinHFR` is searchable over 0.1–5.0. D03 (non-zero `J`) does move it
to 0.45; D01 and D02 leave it at the 1.2 default and score recall 0.135 and 0.475.

**What.** Before optimizing, if the median in-focus HFR is at or below `MinHFR`, seed `MinHFR` beneath it
so the search starts somewhere with a gradient. Plus the diagnostic the entry already asks for: when
`MinHFR` is the dominant rejecting gate, say so and name the pixel-scale / focal-length combination.

**Acceptance.** D01/D02/D03 recall@high improves materially from 0.135 / 0.475 / 0.400 with precision
holding ≥ 0.95. No change on the other 14 datasets. **Note this needs a V2 run**, not just the V1 matrix
— it is the one wave-2 item that does, so schedule it with the F23 re-measurement if possible.

### F19 — the exposure recommendation's population

`S_now` is the median across frames of the **20th-brightest** star's SNR, so field richness decides the
answer: 12 of 17 datasets derive the 0.5 s floor, including a 3 nm Hα refractor whose 2.9° field holds
6835 stars.

**What.** This one wants a decision before it wants code. If autofocus genuinely needs only ~20 good
stars, current behaviour is correct and F19 closes as working-as-intended — but that should be a stated
position, not an accident of which statistic was nearest to hand. If it is not, the candidate is a
faint-end statistic (the SNR at the star count the fit actually consumes) rather than a fixed rank.

**Acceptance.** Whatever is chosen, the bank's own derived exposures must be recomputed from it
(`SynthBankDerivations.DeriveExposureBand` deliberately mirrors the product metric), the bank
regenerated — 84 seconds — and S0/S3 must still pass. **Changing this moves the bank's expected-optimal
values, so it must not share a validation run with any other change.**

### F26 — bound the binning deferral

Expected to dissolve with F23. If it survives: if the binning recommendation has not changed the applied
value for N consecutive rounds, stop deferring and let the step update proceed. Worth a guard regardless,
since any future oscillating recommendation reproduces the livelock.

## F24 — donut detection is a real trade, and is not scheduled here

Donut-on costs precision on every dataset where it was measurable, worst on the ε=0 control (D13, 0.962
→ 0.653). But S5 showed 7 of 9 obstructed datasets degrade measurably when donut detection is switched
*off*. So it is genuinely load-bearing for the AF fit **and** expensive for precision.

That is a trade to be characterised, not a bug to be fixed, and the useful next step is diagnostic: score
the nine donut-gated axes individually against the bank to find which of them carries the precision cost.
The synthetic donuts are clean and high-SNR, so anything losing precision there is losing it structurally
rather than to noise. Out of scope for this spec; it wants its own.

## Sequencing, and why not to batch

Batching all eight is tempting because the bank feels expensive. It is not, and the shape of the cost
matters:

| step | cost |
|---|---|
| Bank generation (17 datasets) | **84 s** |
| V1 matrix, S0–S6 | ~1 h 15 m |
| `optimize --per-run` prepass, config A or B | ~1 h 30 m each |
| `bank-verify` (C0 sweep + A + B) | ~1 h 15 m |

Only **F23** and **F20** need the optimizer prepasses. Everything else validates on the V1 matrix, and
targeted single-dataset runs are minutes. So the expensive loop is entered twice, deliberately:

1. **Wave 1** — F23 alone → full V2 → re-measure all eight. (~4 h)
2. **Wave 2** — F21+F25+F26 (+F20, which needs V2) as one batch → V1 matrix, plus one V2 for F20. (~1 h 15 m per iteration, one V2 at the end)
3. **F19** separately, because it moves the bank's own expected values.

Beyond attribution, batching is *actively hazardous* here: F19 changes derived exposures, F22 derived
binning, and F20/F21/F25 change `step_behavioral`, which is assertion A3's reference. Several of these
fixes move the ground truth the harness scores against — the target and the shooter at once.

## Regression protocol

Before any product change, pin the current measurements as the reference the regression rule compares
against. `docs/synthetic-af-bank-expectations.json` holds *aspirational* bands; what is missing is the
*measured* baseline. Add it, then apply the existing rule (recall@high or precision drop > 0.02,
σ_focus worse > 20%, AF R² drop > 0.005) against it.

Two properties make this cheap to trust: the bank regenerates bit-identically from recorded seeds, and
the V1 matrix reproduces every half-width to the tenth. A diff that moves is a real change.

## Risks

| risk | mitigation |
|---|---|
| A false-positive proxy that works on synthetic data does not transfer to real frames | score both banks every time; the synthetic one measures the metric exactly, the real one guards transfer |
| Fixing F23 changes optimizer landings, invalidating the V2 baseline as a comparison | that is expected and is why the baseline is pinned first; compare like-for-like at the same schema |
| The F21 hypothesis (coarse-walk early termination) is wrong | confirm on D17's two saved sweeps *before* writing the fix — the entry says hypothesis, not diagnosis |
| Harness assertions mis-score the result | already bitten three times; the rule learned is that any surprising FAIL gets checked against the raw trajectory before it is believed |
