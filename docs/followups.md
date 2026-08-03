# Follow-ups register

Findings that surfaced while doing something else, were deliberately **not** acted on at the time, and would
otherwise be lost. Each entry says what was observed, the evidence, why it matters, and the suggested next step.

**Conventions.** Keep entries short and falsifiable — quote the measurement, not an impression. When one is picked
up, write its spec in `docs/<topic>-design.md` and its plan in `plans/<topic>-plan.md`, then mark the entry
**Done** with the commit or PR that closed it rather than deleting it, so the reasoning stays traceable.

Status: **Open** · **In progress** · **Done** · **Won't fix**

---

## Detector / optimizer behaviour

### F1 — The donut heuristic misses small donuts
**Status:** Open · found 2026-07-30 during the bank donut audit

`BankVerification.Decide` flags a run donut-aware only when
`donutPeakFracMax >= 1.0 AND (extremeFrameMedianHFR >= 9.0 OR extremeDonutBBoxMedianPx >= 24)`. The second clause
is calibrated for **large** donuts and vetoes small ones.

**Evidence.** `FlyData` (frac 10.36, extremeHFR 5.0, bbox 13 → vetoed) shows unmistakable annuli — a bright ring
with a dark centre in nearly every one of the 36 largest stars at its most-defocused frame. Meanwhile `toml999`
(frac **47.17**) and `bobp_m101` (frac **39.00**) are pure filled disks, so `frac` alone predicts nothing: on
saturated runs a flat-topped clipped core mimics the donut signature, which is exactly what the veto was written
to suppress, and there it works correctly.

**Why it matters.** Annularity is a geometric ratio (the central-obstruction shadow), not an absolute size, so a
fast short-focal-length rig produces a genuine ring only ~10–15 px across. A vetoed run is scored with donut
detection off *and* its golden is built without `snr_ref --donut`, and per-pixel SNR under-counts donuts — so the
reference itself is incomplete, not merely the detector config.

**Next step.** Re-calibrate the heavy-defocus clause on a size-independent annularity measure (e.g. central-dip
depth relative to ring flux) rather than HFR/bbox thresholds. It changes the verdict for every run, so it needs
its own spec and a re-audit of the bank. Do **not** simply lower the thresholds — that would re-admit the
saturated-core false positives the veto exists to block.

### F2 — `cwhite_2026`'s donut status is ambiguous
**Status:** Open · needs a human eye

The validation anchor is donut-off because its `frac` is 0.67, below the 1.0 threshold. Its most-defocused stars
show **irregular, partial** central darkening at low SNR — not the clean symmetric rings of `mufti` / `Panos` /
`FlyData`, but not clean filled disks either. It is also the run the veto is named after
("cwhite-style over-flag avoided"), so its appearance informed the original calibration.

**Why it matters.** It is the harness anchor; if its donut status is wrong, the anchor is measured on the wrong
config. Bundle this with F1's re-audit rather than deciding it in isolation.

### F3 — `Wtie` does not prevent star-shedding in general
**Status:** Open · documentation/characterisation

`docs/optimizer-sensitivity-pinning-design.md` frames `Wtie = 0.02` as stopping the optimizer landing on the
star-shedding corner. Measured bank-wide with `Wtie` present (`ec6a1b4`), A/B configs still shed heavily:
`mccomiskey` A → recall@≥12 **0.079** vs C0's 0.871; `timmer` B → **0.055**; `CWhiteFocus` A at sens 50 → 0.327.

This is *correct by construction* — `Wtie` only arbitrates genuine plateaus, and these were real σ improvements —
but the doc should say it prevents shedding chosen **on a σ-wiggle**, not shedding generally.

### F4 — The objective has no sensor-model term
**Status:** Open · potentially the most consequential entry here

J scores curve tightness and star count, with no term for the downstream sensor/tilt fit. So it can pick an
operating point that tightens focus while destroying tilt measurement.

**Evidence.** `mccomiskey` config A: σ_focus 0.460 and J 0.9940, but sensor stars collapse 3,618 → **43**, sensor
R² 0.944 → **0.526**, alignment 9/9 → 7/9. Config B reaches the same σ_focus (0.441) at J 0.9950 while keeping
937 sensor stars and R² 0.954 — **A is dominated by B on every axis**, and J could not tell them apart (Δ 0.001).

**Next step.** Consider a sensor-fit guard or term in the objective, at least for the inspection variant.

### F5 — Donut detection halves σ_focus on a run with **no** donuts
**Status:** Open · unexplained

On `mccomiskey` (max HFR 2.01", no donuts — see the F1 audit), enabling the donut master takes σ_focus
**3.263 → 1.648**, a ~49% improvement, measured by one-at-a-time ablation holding sensitivity and star-clip fixed.

**Why it matters.** If the donut path's morphological processing improves HFR consistency generally rather than
only on rings, that is a free win available to every run — or a sign that something in the non-donut path is
worse than it needs to be. Worth understanding before anyone "optimises" the donut path away.

### F6 — Sensitivity and star-clip act only in combination
**Status:** Open · explains the documented plateau

One-at-a-time ablation on `mccomiskey` σ_focus (adaptive on, donut on, NC 4):

| | `clip 2` | `clip 10` |
|---|---|---|
| **`sens 10`** | 1.648 | 2.960 |
| **`sens 33.3`** | 1.503 | **0.441** |

At clip 2 sensitivity barely matters; at clip 10 it is decisive. Raising clip *hurts* at default sensitivity and
*helps* strongly at high sensitivity. Neither knob does anything useful alone — the objective surface is a
**diagonal valley**, not two independent axes, which is a concrete mechanism for the plateau
`docs/optimizer-sensitivity-pinning-design.md` describes and a hint that a coordinate-wise search is the wrong
shape for this space.

### F7 — Adaptive binarization is not uniformly good for the AF fit
**Status:** Open · first observation under a correct C0

Now that C0 runs as-shipped (`2c91e1b`), enabling `LocallyAdaptiveBinarization` improves σ_focus on some runs and
degrades it on others: `CWhiteFocus` 3.000 → 2.502 and `mufti` 6.917 → 5.894, but `caboose` 1.398 → 4.196 and
`uneven` 14.107 → 20.123. Recall is essentially unchanged (median Δ −0.0003).

The feature was AF-bank-gated on other criteria; this is the first look at it with C0 measuring the shipped
config. Not necessarily wrong — but it should not be assumed uniformly beneficial.

### F8 — Optimizer landings are not reproducible across invocations
**Status:** Open

`bobp_m101` landed at three different points on the same frames and the same corrected pipeline:
`sens 0 / clip 0.25`, `sens 0 / clip 6.875` (prepass A), `sens 30.1 / clip 3.44` (prepass B). Consistent with the
F6 diagonal valley, but it means **a single landing is not evidence** about which corner the optimizer prefers,
and any claim resting on one `optimize` run should be treated as anecdote.

### F18 — Step size is sized by curve geometry alone, so the sweep outruns what the detector can see
**Status:** Open · found 2026-08-02 reproducing a 3800 mm sweep that yielded four dead frames

`StepSizeRecommender` sets the half-width from the fitted HFR curve: the distance at which HFR reaches
`HfrThresholdMultiple = 3.0` × its minimum, then `step = W / 3.5` at `DefaultOffsetSteps = 4`. That is a pure
**curve-geometry** criterion — nothing in it asks whether stars are still *detectable* out there.

Measured on `AutoFocus_20260802_122354` (3800 mm, 14 s, in-focus HFR 5.96 px, one frame per position):

| distance | 1770 | 3540 | 5310 | 7080 | 8850 |
|---|---|---|---|---|---|
| HFR (× min) | 1.2–1.3× | 1.8–2.0× | 2.6–2.8× | 3.4–3.6× | — |
| stars | 10 | 10 | 6, 3 | 1, 1 | **0** |

Two separate over-reaches:

1. **The 3× band is wider than detectability.** The band puts the half-width at 6221 steps (→ step 1778; the
   wizard recommended 1725), but the outermost position still yielding the `NHard = 3` stars the objective
   requires is at **5310**. The constant is ~15% past what this rig can measure.
2. **The executed sweep exceeds the band the step was sized for.** The step is sized for 3.5 points per side
   *within* the band, but the run takes 4 offset + 1 focus-recovery = 5 per side: 5 × 1770 = 8850 = **4.4× min
   HFR**, 43% beyond the band. The recovery step lands where nothing is detectable, which is where all the
   flat-topped rejections and starless frames came from.

**Why it matters beyond one rig.** The band is relative to min HFR, so it adapts correctly when a filter changes
the focus spread — but it does **not** adapt to flux. A narrowband filter spreads fewer photons over the same
defocused area, so stars vanish at a *lower* HFR multiple while the curve looks similar. This is the likely cause
of step-size recommendations that vary confusingly between filters on the same rig: each is right about geometry
and blind to signal.

**Suggested next step.** Bound the half-width by both criteria:

```
half_width = min(W_3x,        # current: fitted 3x min-HFR band
                 W_detect)    # NEW: outermost position still yielding >= NHard stars
step = half_width / points_per_side   # sized for the sweep ACTUALLY run, incl. recovery
```

`W_detect` is *measured*, not extrapolated, so it fits the recommender's converge-over-runs philosophy and its
`MaxHalfWidthSampledHalfSpanMultiple = 1.5` cap, and it adapts per filter for free. The per-frame star counts it
needs are already plumbed (`RunEvaluationMetrics.FrameStarCounts`). On the run above it gives
`min(6221, 5310) = 5310` → step ≈ 1060–1330 instead of 1725, putting every frame inside the detectable range.

Two open decisions: whether focus-recovery steps *should* extend the sweep past the band (they are deliberately
far-from-focus, but today they silently widen it by 43%), and what floor `W_detect` needs so a starless run cannot
collapse the sweep — it should only ever tighten `W_3x`, never drive it below a sane minimum.

This changes the shipped recommender for every user, so it wants a design spec plus bank validation with σ_focus
as the acceptance metric, not an inline patch. Related: the flat-topped rejections that motivated this are
surfaced as sweep-geometry evidence by `ExposureRecommendation.FlatRejectedCount` (PR #159).

### F25 — From a far-too-wide sweep the step recommender widens it further, instead of recovering
**Status:** Open · found 2026-08-03 running scenario S2 (step ×4) on the synthetic AF bank

When the sweep is so wide that the hyperbola fit degenerates, `StepSizeRecommender` responds by asking for a
**wider** sweep still. There is nothing that recognises "this fit is garbage, retreat".

**Evidence.** `D05_tec140_1000mm`, scenario S2 (bootstrap step = 140, i.e. 4× the correct 35):

| round | step | fit R² | recommended |
|---|---|---|---|
| 0 | 140 | **−0.223** | **240** |
| 1 | 240 | 1.000 | 36 |

A **negative** R² means the fit is worse than a horizontal line — there is no usable curve at all — and from
that the recommender produced 240, moving 4× too wide to nearly 7× too wide. It only recovered because round 1
happened to fit cleanly at the wider spacing. `A4` also fired here (`WasCapped=True but truth predicts False`),
which is the cap logic responding to the same degenerate fit.

**Why it matters.** This is the mirror image of [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see):
F18 is the sweep over-reaching what the detector can see, and this is the recommender *amplifying* that
over-reach once it has. A user who starts too wide — the likely case on an unfamiliar rig — can be walked
further out rather than back in. The recovery on D05 was luck, not design.

**Next step.** Gate the recommendation on fit quality. The R² is already in hand at the call site; a fit below
some floor should either hold the current step or shrink it, never widen it. Reproduce with
`synth-validate --datasets D05_tec140_1000mm --scenarios S2 --max-rounds 4`.

### F26 — A stuck binning recommendation starves the step update indefinitely
**Status:** Open · found 2026-08-03 running scenarios S1/S6 on the synthetic AF bank

The wizard's update ordering applies **binning first** and defers the step by a round, on the sound reasoning
that SNRs are per-binned-pixel so changing binning invalidates the exposure and step measurements. But when the
binning recommendation is *persistently wrong* ([F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)),
"defer the step" becomes "never update the step".

**Evidence.** `D08_c11_2800mm`, scenario S1 (bootstrap step 21, correct 82), four rounds, fits at R² = 0.999–1.000:

| round | step | binning recommendation | step applied? |
|---|---|---|---|
| 0 | 21 | 1 (expected 2) | no — binning first |
| 1 | 21 | 1 | no |
| 2 | 21 | 1 | no |
| 3 | 21 | 1 | no |

The step never moves off 21 — a quarter of the correct value — despite a near-perfect fit every round.
`D12_c14_585_afbin2` S6 shows the same pattern (35 → 60 → 60 → 74 against a target of 141). Both are datasets
whose true HFR sits near the 4.5 px binning boundary, i.e. exactly F22's population.

**Why it matters.** Two individually-defensible behaviours compose into a livelock: a measurement bias that
flips a threshold, plus an ordering rule that waits for that threshold to settle. The user sees the wizard
"recommending" the same wrong binning every round and never getting to the knob that actually matters. Neither
F22 nor the ordering rule looks broken on its own, which is why this needs its own entry.

**Next step.** Bound the deferral: if the binning recommendation has not changed the applied value for N
consecutive rounds, stop deferring and let the step update proceed. Fixing F22 would also dissolve this, but the
livelock is worth guarding against independently — any future oscillating recommendation would reproduce it.

### F23 — The optimizer objective has no precision term, so it trades precision away for marginal recall
**Status:** Open · found 2026-08-02, the first measurement of **exact** detector precision (synthetic AF bank)

The objective `J` rewards star count and fit quality. Nothing in it penalises a false positive — and
nothing could have, because until this bank existed precision was only ever a *lower bound* on real data
(`F11`). Given exact precision, the optimizer's landings are revealed to be a bad trade: it gains a few
points of recall and gives up **half** the precision.

**Evidence.** `bank-verify --nc-sweep 2,3,4 --opt-a --opt-b` over all 17 synthetic datasets
(`afbank-verify/3`, header pixel scale, 0 failed). C0 = stock defaults; A = `optimize --per-run`;
B = the same with donut detection forced on. recall@high / precision:

| dataset | C0@nc2 | A | B |
|---|---|---|---|
| D08_c11_2800mm | 1.000 / **0.959** | 0.990 / **0.748** | 1.000 / 0.781 |
| D09_c14_3800mm | 0.922 / **0.993** | 0.956 / **0.451** | 1.000 / 0.448 |
| D10_rc16_3250mm_sparse | 0.983 / **1.000** | 0.931 / **0.547** | 0.966 / 0.661 |
| D11_rc10_585_afbin2 | 0.887 / **0.986** | 0.850 / **0.732** | 0.917 / 0.627 |
| D12_c14_585_afbin2 | 0.897 / **0.952** | 0.897 / **0.653** | 0.879 / 0.506 |
| D15_cdk20_3454mm_e47 | 0.954 / **0.979** | 0.943 / **0.531** | 0.931 / 0.708 |
| D16_esprit550_ha3 | 0.886 / **0.985** | 0.908 / **0.744** | 0.739 / 0.983 |
| D17_cdk14_oiii5 | 1.000 / **0.942** | 1.000 / **0.465** | 1.000 / 0.567 |

C0's precision never drops below **0.942** on any of the 17 datasets. Config A drops as low as 0.451.
On D09 the optimizer bought +0.034 recall for −0.542 precision.

**Why it matters.** This is the wizard's headline output — the settings a user is invited to Accept. On
a long-focal-length rig it is currently recommending a configuration that roughly doubles the false-
positive rate. Those false positives then feed the autofocus fit and the sensor-model fit, so the cost
is not confined to a reported number. It also reframes the real-bank optimizer results: every prior "A
beat C0" conclusion was scored against a precision figure that could not see this.

**Next step.** Add a precision-like term to the objective. It cannot be true precision on real data
(that is the whole problem), but two proxies are already available: the golden-independent
false-positive *proxies* the detector already collects, and — for tuning and regression — this bank,
where precision is exact. Any change wants scoring against **both** banks, since the synthetic one can
now measure exactly the quantity the real one cannot. Related: [F4](#f4--the-objective-has-no-sensor-model-term)
is the same shape of gap (a term the objective omits), and [F11](#f11--precision-is-a-lower-bound-on-runs-whose-faint-tier-was-budget-truncated--re-measured) is why this went unseen.

### F24 — Donut detection costs precision even where donuts exist, and badly where they do not
**Status:** Open · found 2026-08-02 on the synthetic AF bank

Config B (donut-aware detection forced on) reduced precision on **every** dataset where it was
measurable, including the datasets that genuinely have donuts, and most sharply on the ε=0 control that
has none.

**Evidence.** `D13_apo200_1800mm` is a 1800 mm **unobstructed** refractor — the design's donut control,
present precisely so "donut" and "long focal length" cannot be confounded. Its extreme-defocus PSFs are
filled discs, not annuli:

| dataset | ε | C0@nc2 precision | B precision |
|---|---|---|---|
| **D13_apo200_1800mm** | **0** | 0.962 | **0.653** |
| D14_cdk14_2563mm_e47 | 0.47 | 0.965 | 0.671 |
| D12_c14_585_afbin2 | 0.34 | 0.952 | 0.506 |
| D09_c14_3800mm | 0.34 | 0.993 | 0.448 |

Two further expectations the run overturned: the design assumed **C0 would be broken on donut datasets**
(as it is on the real bank's `Panos`/`mufti`/`LinwoodFocus`) — it is not, reaching recall 1.000 on D08
and D17 with precision 0.942–0.959. And `donutEffect` over the 17 A/B pairs is `donutHelpedAF: 5`,
`donutHurtSensor: 6` — no clear win.

**Why it matters.** Donut detection is a bootstrap input that gates nine optimizer axes, and there is
still no product signal that recommends it ([F1](#f1--the-donut-heuristic-misses-small-donuts) is about
the heuristic missing *small* donuts). This says the cost of enabling it wrongly is high and concrete,
which raises the stakes on getting that signal right.

**Next step.** Score the nine donut-gated axes individually against this bank to find which of them
carries the precision cost — the synthetic donuts are clean and high-SNR, so anything that loses
precision here is losing it structurally rather than to noise.

### F19 — The exposure recommendation is decided by the 20 brightest stars, so a rich field can never earn one
**Status:** Open · found 2026-08-02 deriving expected-optimal exposures for the synthetic AF bank

`ExposureRecommender`'s `S_now` is the median, across non-recovery frames, of each frame's
**`NTarget`-th-brightest** accepted-star SNR, with `NTarget = 20`. Any reasonably wide field contains 20 stars
bright enough to sail past the gate no matter what filter is in front of them, so `SensitivityIsAtFloor` never
trips and no recommendation is ever offered.

**Evidence.** Deriving the exposure band for the synthetic bank from the recommender's own arithmetic (see
`docs/synthetic-af-bank-design.md`) produced the 0.5 s floor for **12 of 17** datasets. The clearest case is
`D16_esprit550_ha3` — a 550 mm refractor behind a **3 nm Hα** filter, passing roughly 64× less flux than
luminance. It still derives 0.5 s, because its 2.9° field carries 6835 on-frame stars and the 20th brightest is
magnitude ~9. The datasets that do demand a long exposure are the *narrow, sparse* ones — `D10` (0.28° field,
26 on-frame stars) derives 30 s — and they get there by having few bright stars, not by being photon-starved.

**Why it matters.** The knob is sized by field richness rather than by whether the stars the autofocus fit
actually depends on are above the noise. A long-focal-length rig on a bright field will report "exposure is not
the limit" while its faint-end stars — the ones that carry the wings of the V-curve — are still noise-dominated.
That is the same shape of gap as [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see):
a recommendation computed from one convenient statistic rather than from what the fit needs.

**Next step.** Decide deliberately whether `NTarget = 20` is the right population for this question. If autofocus
genuinely only needs 20 good stars, then the current behaviour is correct and this entry closes as "working as
intended" — but that should be a stated position, not an accident of which statistic was nearest to hand. If it
is not, the candidate is a faint-end statistic (e.g. the SNR at the star count the fit actually consumes) rather
than a fixed rank.

### F20 — Below `MinHFR` the autofocus objective collapses to exactly zero, with no diagnostic
**Status:** Open · found 2026-08-02 running `optimize --per-run` over the synthetic AF bank

When a sweep's in-focus HFR falls below the detector's `MinHFR` gate (default 1.2 px), the whole core of the
V-curve is rejected, the run objective is `0`, and the optimizer terminates having explored the space for
nothing. Nothing in the output says "your stars are smaller than the minimum HFR".

**Evidence.** `D01_ultrawide_40mm` (40 mm f/2.8, 19.4″/px) has truth HFR per frame
`[2.27, 1.71, 1.15, 0.61, 0.24, 0.61, 1.15, 1.71, 2.27]` px — the middle **five of nine** frames sit at or below
`MinHFR = 1.2`. `optimize --per-run` reports `Current settings J: 0`, then
`Optimization complete: currentJ=0 -> bestJ=0 (no improvement over current), evals=88`. The goldens for those
frames are populated (the stars are there and bright — tiering is by SNR, not size), so this is a gate rejecting
real, well-detected signal, not an empty field.

**Why it matters.** `J = 0` is indistinguishable from "starless frames", "wrong folder", and "detector
misconfigured". A user pointing the wizard at a short-focal-length rig gets a silent null result.

**And the capability is reachable — the optimizer simply cannot get to it.** `MinHFR` *is* a curated optimizer
axis, searchable over **0.1–5.0** (`OptimizerVariable.CreateCuratedSet`), so lowering it is exactly the move that
would unlock these rigs. What the landings show is that the optimizer only makes that move when it already has a
gradient:

| dataset | in-focus HFR (px) | `MinHFR` landed by config A | outcome |
|---|---|---|---|
| `D01_ultrawide_40mm` | 0.24 | **1.2 — the default, unmoved** | J = 0, recall 0.129 |
| `D02_rich_135mm` | ~0.7 | **1.2 — the default, unmoved** | recall 0.451 |
| `D03_redcat_250mm` | ~0.97 | **0.45** (moved down) | recall 0.396 → 0.537 under B |
| `D05_tec140_1000mm` | 1.77 | 1.45 | recall 0.989 |

D03 proves the search *can* find a sub-default `MinHFR` when the objective is non-zero. D01 and D02 never move it,
because `J` is identically 0 across the neighbourhood the search explores — the objective only becomes non-zero
once *enough* of the curve is simultaneously measurable, so no single-axis step off the seed improves anything.
This is a **cold-start plateau**, not a missing knob, and it is why the earlier framing of this entry ("the gate
is defensible, only the silence is a problem") was too generous: the gate costs a whole class of rigs their
autofocus, and the fix is within the existing search space.

**Next step.** Two parts, and the second is the substantive one.
1. *Report it.* When a large fraction of accepted candidates are rejected by `MinHFR` specifically, say so and
   name the pixel-scale / focal-length combination. The counts are already collected
   (`CollectRejectedCandidateDiagnostics`), so this is reporting, not new measurement.
2. *Seed out of the plateau.* Before optimizing, if the median in-focus HFR is at or below `MinHFR`, seed
   `MinHFR` beneath it (the measured HFR is available from the same in-focus record
   `DetectionBinningResolver` already consumes) so the search starts somewhere with a gradient. Score any change
   on **D01–D03** of the synthetic bank, where the correct answer is known and current recall is 0.135 / 0.475 /
   0.400.

### F21 — `StepSizeRecommender`'s half-width is not stable against noise, even on a perfect fit
**Status:** Open · found 2026-08-02 running the synthetic bank's S0 control

Two sweeps of the **same dataset at the same step**, differing only in noise seed and both fitting at
**R² = 1.0000**, produced half-widths an order of magnitude apart — and therefore recommended steps an order of
magnitude apart.

| dataset | round | fit R² | `HalfWidth` | recommended step |
|---|---|---|---|---|
| `D17_cdk14_oiii5` | 0 | 1.0000 | 143.6 | 41 |
| `D17_cdk14_oiii5` | 1 | 1.0000 | **12.1** | **3** |
| `D02_rich_135mm` | 0 | 0.9156 | 23.2 | 7 |
| `D02_rich_135mm` | 1 | 0.9324 | **2.5** | **1** |

**Evidence.** `TestApp synth-validate --scenarios S0`, bootstrap = the dataset's own expected optimum
(D17: step 60). A recommended step of 3 where 60 is correct turns a ±240-step sweep into a ±12-step one — every
frame lands inside the focus zone and the V-curve has no wings at all. This is *not* [F8](#f8--optimizer-landings-are-not-reproducible-across-invocations):
F8 is about the optimizer's search landing in different corners of a flat valley, whereas here the fit is
essentially exact both times and it is `FindHalfWidth`'s own outward search that returns a wildly different
answer.

**Why it matters.** The step size is the one recommendation a user is most likely to accept unread, and a
collapse of this magnitude silently destroys the next autofocus run. It also makes any single measurement of
"what step does the recommender want" untrustworthy — the same caveat F8 imposes on optimizer landings now
applies to the recommender itself.

**Next step.** Instrument `FindHalfWidth`: log the fitted `minimum`, the `3 × minimum` target, and the bracket it
converged on, for both rounds of D17. The suspicion is that when the fitted minimum sits slightly high, the
`3 × min` crossing is found very close to focus and the coarse walk terminates before it reaches the real one —
but that is a hypothesis, not a diagnosis, and it should be confirmed on the two saved sweeps before any change.
Reproduce: `synth-validate --datasets D17_cdk14_oiii5 --scenarios S0 --max-rounds 2` (seeds are deterministic).

### F22 — Detection binning is a hard threshold on a measurement that under-reads, so boundary rigs get the wrong factor
**Status:** Open · found 2026-08-02 running the synthetic bank's S0 control

`DetectionBinningResolver.RecommendFromHfr` is `clamp(round(hfr / 3), 1, 4)` — a hard threshold with its 1→2
boundary at **4.5 px**. The HFR it is given is the detector's *measured* in-focus HFR, which systematically
under-reads the optical HFR, by 2–10% usually but by up to **38%** in the cases that matter. Rigs whose true HFR
sits near 4.5 px therefore land on the wrong side.

**Evidence.** On S0, where the bootstrap already *is* each dataset's expected optimum, three datasets that need
binning 2 were told to use 1:

| dataset | optical HFR_min (captured px) | measured vertex HFR | under-read | recommended | correct |
|---|---|---|---|---|---|
| `D14_cdk14_2563mm_e47` | 5.3 | 4.92 | −7% | **2** | 2 |
| `D17_cdk14_oiii5` | 5.3 | 3.27 | **−38%** | **1** | 2 |
| `D15_cdk20_3454mm_e47` | 5.9 | 4.02 | −32% | **1** | 2 |
| `D12_c14_585_afbin2` | 5.07 | 3.94 | −22% | **1** | 2 |
| `D10_rc16_3250mm_sparse` | 5.6 | 5.66 | +1% | 2 | 2 |

The cleanest pair is **D14 vs D17**: *identical* optics (2563 mm f/7.2, ε=0.47), *identical* pixel size (3.76 µm),
so identical true HFR — and opposite recommendations. The only differences are the filter and exposure (L at 0.5 s
vs OIII 5 nm at 30 s), i.e. the star population and SNR. The measurement, not the optics, decided the answer.

**Why it matters.** Binning is the highest-leverage knob for a long-focal-length rig — it is why
`DetectionBinningResolver` exists — and the wizard's gate on it is fit quality (R² ≥ 0.9), which is satisfied
here (R² = 1.0000). So the recommendation is delivered with full confidence and is wrong. Worse, it is
*bistable*: the same rig can be told 1 on a narrowband night and 2 on a luminance night.

**Next step.** Two candidates, not mutually exclusive. (a) Calibrate out the bias — the measured-vs-optical
under-read is systematic and could be characterised against this bank rather than guessed. (b) Add hysteresis or
a dead band around the 4.5/7.5 px boundaries so a marginal rig does not flip between sessions, and say
"borderline" in the UI rather than presenting a coin flip as a recommendation. Note this compounds with
[F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic): the same
under-reading pushes small-HFR rigs toward the `MinHFR` cliff.

### F27 — The optimizer cannot reach the rejected-candidate diagnostics an approved spec says it can
**Status:** Open · found 2026-08-03 implementing [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)

[`docs/af-recommender-hardening-design.md`](af-recommender-hardening-design.md) lists "the rejected-candidate
diagnostics (`CollectRejectedCandidateDiagnostics`)" among the signals "already collected" and available to
build a false-positive term from. They are not reachable from the optimizer's evaluation path.

**Evidence.** `RejectedCandidateRecord`s are produced onto `HocusFocusStarDetectorResult.RejectedCandidates`
(`IStarDetector.cs:981`, set in `StarDetector.cs:878`). But `HocusFocusStarDetection.BuildStarDetectionResult`
copies only `Metrics` onto the `HocusFocusStarDetectionResult` the optimizer consumes
(`HocusFocusStarDetection.cs:818`), and that type has **no `RejectedCandidates` member at all**
(`HocusFocusStarDetection.cs:180-213`; the identifier does not appear anywhere in that file). The optimizer's
per-frame contract carries exactly three metric scalars — `RelaxationAdmittedCount`, `LowSensitivityCount`,
`TooFlatCount` (`RunEvaluationData.cs:68,76,86`). Only the review/feedback path re-detects with the flag on,
and it does so outside the optimizer loop (`Review/FrameReviewBuilder.cs:163-184`).

**Why it matters.** It is a load-bearing claim in an approved spec — F23's wave 1 was written assuming a
choice of three proxy signals and in fact had one. The gap is cheap to close: the flag is on the detection
cache-key **denylist** (`IStarDetector.cs:523-542`), so enabling it inside the optimizer would invalidate no
memo and no early-context key; the only cost is per-detection allocation in the hot loop.

**Next step.** Either add `RejectedCandidates` to `HocusFocusStarDetectionResult` and summarise it into
`FrameDetectionResult`, or correct the spec. Note the nine per-gate `*Bounds` rect lists **are** already on
`Metrics` and reachable at the same one-line seam that reads `LowSensitivity`/`TooFlat`
(`RunEvaluationLoader.cs:331-335`), so they are the cheaper signal if per-rejection geometry is wanted.

### F28 — `LowSensitivity` reads exactly zero precisely when the Sensitivity gate has collapsed
**Status:** Open · found 2026-08-03 implementing [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)

The gate rejects on `sensitivity <= p.Sensitivity` (`StarDetector.cs:1763`), and every candidate's
`sensitivity` is bounded below by `PeakResponse × EffectiveClipMultiplier` — 0.75 × 2.0 = **1.5** at shipped
defaults. So a gate anywhere below 1.5 rejects **nothing**, and `StarDetectorMetrics.LowSensitivity` is
identically 0.

**Derivation.** Clip survivors satisfy `raw > background + clipMargin` with
`clipMargin = EffectiveClipMultiplier · σ` (`StarDetector.cs:2179`), so `meanFlux > EffectiveClipMultiplier · σ`;
`peak ≥ meanFlux`; and `NormalizedBrightness = peak − (1 − PeakResponse)·meanFlux ≥ PeakResponse · meanFlux`
(`StarDetector.cs:2274`). Hence `sensitivity = NormalizedBrightness / σ > PeakResponse × EffectiveClipMultiplier`.

**Why it matters.** `FrameLowSensitivityCounts` exists specifically to separate "the gate is holding stars
back" from "there is nothing left to find" (`OptimizationObjective.cs:194-199`), and
`ExposureRecommender.Recommend` turns it into `gateIsHoldingStarsBack = gateRejectedCount > 0`, which is the
sole discriminator between `StarCountIsTheLimit` and `StarFieldIsExhausted` (`ExposureRecommender.cs:515-517`).
The exposure advice is surfaced only when `HasLowStarSignal` — i.e. `Sensitivity ≤ 1.0`
(`StarDetectionOptimizerWizardVM.cs:248`, `ExposureRecommender.cs:284,336`) — which sits strictly inside the
provably-inert region. So on exactly the population the affordance was written for, the evidence test is
structurally dead: `StarCountIsTheLimit` can never fire, `StarFieldIsExhausted` is always taken, and the
recommender always concludes the field has nothing more to give.

**Why it did not show up before.** The zero-rejections test was added from a real rig where it was correct
and valuable (2 s → 5 rejections, 14 s → zero; the comment at `ExposureRecommender.cs:511-514` records it).
That rig was not at the search floor. The defect is the *interaction* with the floor, not the test.

**Next step.** Make the discriminator conditional on the gate being able to reject at all — compare
`p.Sensitivity` against `PeakResponse × EffectiveClipMultiplier` and report "the gate is inert, so its
rejection count carries no information" rather than silently reading it as exhaustion. Never use this counter
as a false-positive signal: the pathological landing produces its cleanest possible value.

### F30 — The published V2 config-A landings do not reproduce, and their inputs were not recorded
**Status:** Open · found 2026-08-03 pinning the [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall) baseline

Re-running `optimize --per-run` at HEAD on the bit-identical synthetic bank produces different landings from
the V2 table in [`docs/synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md): D03
32.33 vs 17.67, D04 19.67 vs 17.67, D05 8 vs 10, D07 8 vs 7.

**Evidence.** Separately, the `optimized_settings.json` copies left in each dataset's `attempt01/` match
neither published config — 5 datasets at Sensitivity 0.0 where config A had 6 (D15 is 10.0 there, 0.0 in the
table), and D17 is 0.0 where config B's published landing was 8.0. That is [F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings)
biting: the prepass overwrites those files in place, so the last writer wins and neither arm survives.

**Why it matters.** The V2 table is what the expectations file's `regressionRule` is meant to compare
against, and it cannot be regenerated. `--max-evals` and the `harness_settings.json` state in force were not
recorded alongside the results, and the prepass `--out` trees are gone, so the inputs are unrecoverable. Any
before/after therefore needs a same-session control arm rather than the published numbers — which is how F23
wave 1 was run.

**Next step.** Record the provenance in `optimized_settings.json`, which already carries `CreatedAtUtc`,
`RunCount`, `BaselineJ` and `FinalJ`: add the full `optimize` argv and a hash of the effective
`harness_settings.json`. Cheap, and it makes a landing self-describing.

---

## Harness / tooling

### F9 — `bank-verify` cannot pin C0's detector knobs
**Status:** Open

C0 consumes `BuildDefaultStarDetectorParams()` verbatim with no `--sensitivity` / `--star-clip` override. When a
shipped default changes (as in `59d5e59`, sensitivity 2.0 → 10.0), old and new reports are no longer comparable
and there is **no way to reproduce the old configuration** without a scratch build. That is what made the
2026-06-24 anchor comparison untestable as written.

**Next step.** Add explicit overrides mirroring `golden eval`'s, so a historical config can be re-measured.

### F10 — Golden bbox is not a valid donut proxy
**Status:** Done (recorded so it is not re-attempted)

Using the golden star bbox to detect donuts fails: `Panos` is a genuine donut run but its most-defocused golden
frame measures a median bbox of only 12 px, because the SNR reference detects **fragments** of a thin ring rather
than the ring. This is the documented under-counting of defocused donuts in `.claude/docs/golden-star-set.md`.
Use the heuristic's own donut statistics, or render the pixels and classify them.

---

## Bank data quality

### F11 — Precision is a lower bound on runs whose faint tier was budget-truncated → **re-run these with more montages**
**Status:** Open · the largest known data-quality gap in the bank

`build_goldens` auto-confirms the SNR≥12 tier and LLM-QAs only a bounded slice of the uncertain tier, capped by
`golden_prep --budget-montages`. On deep fields that slice is a few percent, so real faint detections HocusFocus
finds are scored as **false positives**. Precision on those runs is a weak lower bound, not a measurement.

**recall@SNR≥12 is unaffected everywhere** — the high tier is auto-confirmed in full — and remains the trustworthy
headline. Only precision (and recall@all) is compromised.

**Measured coverage** (rendered ÷ actual uncertain candidates):

| run | frames | rendered | uncertain | coverage | montages | budget used |
|---|---|---|---|---|---|---|
| `bobp_m101` | 10 | 2,303 | 2,303 | **100%** | 67 | 20/frame |
| `bobp` | 9 | 1,870 | 1,870 | **100%** | 56 | 20/frame |
| `vsn07` | 7 | 1,362 | 1,362 | **100%** | 42 | 20/frame |
| `FlyData` | 9 | 6,480 | 17,215 | **37.6%** | 180 | 20/frame |
| `timmer` | 9 | 6,480 | 117,107 | **5.5%** | 180 | 20/frame |
| `SorenVance` | 6 | 4,320 | 130,932 | **3.3%** | 120 | 20/frame (golden invalid — still unscored, see F17) |
| `lumos` | 23 | 16,560 | 564,813 | **2.9%** | 460 | 20/frame (golden invalid — still unscored, see F17) |

**Every pre-existing run in the bank is worse still** — all were built at the skill-default 4 montages/frame,
which hard-caps the faint tier at 144 cells/frame. Their goldens show exactly that fingerprint:

| run | faint stars/frame | cap |
|---|---|---|
| `mccomiskey` | 139 | 144 |
| `uneven` | 133 | 144 |
| `muggsie` | 129 | 144 |
| `toml999` | 124 | 144 |
| `FlyData` | 102 | 144 |

On `mccomiskey` that is ~2% of its ~6,900 uncertain candidates per frame and **nothing at all below SNR 8**, which
is why its C0@nc2 "6,871 false positives" at precision 0.8325 is an artifact rather than a finding.

**Next step — re-run the deep runs with a much larger `--budget-montages`.** Cost scales linearly with coverage:
the 236-montage QA pass took 27 min at chunk=4, so ~7 min per 60 montages.

| target | montages (`timmer`) | QA time | coverage |
|---|---|---|---|
| current (20/frame) | 180 | ~20 min | 5.5% |
| 60/frame | 540 | ~65 min | 17% |
| 200/frame | 1,800 | ~3.5 hr | 55% |
| full | 3,253 | ~6 hr | 100% |

Full coverage on every deep run is ~6 hr each and probably not worth it. Suggested: **60–200 montages/frame for
any run whose precision is quoted in a report**, prioritised as `mccomiskey` (its precision has already been cited
and is the most misleading), then `timmer`, `SorenVance`, `lumos`, then the remaining default-budget runs.

Independently of any re-run, **record the coverage fraction in the golden sidecar** so a consumer can tell whether
a precision figure is a measurement or a bound. Today nothing in `<frame>.golden.json` says how much of the
uncertain tier was examined, which is why this went unnoticed for so long.

**The coverage-recording half of this is done** (schema v2, `docs/golden-tier-plausibility-design.md` §4.5):
goldens now carry per-tier `examined`/`total`, and candidates the budget never reached are `unresolved` and
excluded from **both** denominators instead of being counted as false positives. That removes the artifact at
its source for any regenerated run — the "6,871 false positives" shape cannot recur. The remaining work here is
purely the larger `--budget-montages` re-runs on the runs still carrying v1 sidecars.

### F16 — The SNR≥12 auto-confirm gate inverts on heavily-defocused runs
**Status:** **Tooling fixed and merged** (PR #163) · the bank rebuild it enables is incomplete — see **F17**

**Two root causes were found beyond the original diagnosis, both by rendering real output rather than trusting
the metrics:**

1. **`donut_k` = 6.0 sat inside the noise.** The matched-filter response distribution's median was 6.41 against
   a 6.0 threshold, so ~98% of the candidate pool was junk and the bounded QA budget was spent rejecting it.
   Confirmed real donuts had min 7.48 / median 14.46. Now 8.0: candidates/frame fell 5,000–7,300 → 649–919 on
   `LinwoodFocus` and QA coverage rose ~14% → 78–100% at unchanged cost.
2. **The tier assignment still mixed two quantities.** `tier()` buckets on `snr`, which was peak/σ for connected
   components but the disk-integrated response for the matched filter — the same confusion F16 is about,
   surviving in a place the first fix didn't reach. It only shows at low coverage. Matched-filter candidates now
   carry a measured peak.

The donut matched filter shreds each ring into many tiny high-SNR fragments, which then pass `build_goldens`'
SNR≥12 **auto-confirm** gate without QA. The gate assumes a high-SNR candidate is a real star — sound for the
per-pixel-SNR path it was validated on, **not** for the matched-filter path.

**Evidence.** Golden star widths, donut runs old vs newly generated:

| run | mode | stars | median W | p90 W | **≤4 px** |
|---|---|---|---|---|---|
| `lumos` | donut, new | 60,348 | 3 px | 4 px | **91%** |
| `SorenVance` | donut, new | 28,935 | 3 px | 36 px | **67%** |
| `FlyData` | donut, new | 5,462 | 11 px | 22 px | 26% ✅ |
| `mufti` | donut, existing | 5,612 | 18 px | 36 px | 1% |
| `Panos` | donut, existing | 7,178 | 12 px | 36 px | 11% |
| `LinwoodFocus` | donut, existing | 1,640 | 20 px | 36 px | 19% |

`lumos`'s donuts measure ~21 px and `SorenVance`'s ~26 px per the donut heuristic, so a golden whose stars are
median **3 px** is fragments, not stars. The scored consequence: HocusFocus finds 8–813 stars/frame (plausible for
these fields) against a reference claiming ~4,600/frame, giving recall@≥12 of **0.036** (`SorenVance`) and
**0.013** (`lumos`) — and **config B (donut-on) is no better than C0**, which is the tell. A real donut-detection
gap would show B recovering; it doesn't, because the reference is wrong rather than the detector blind.

**ROOT CAUSE (investigated 2026-07-30) — the tiering, not the matched filter, and NOT bad data.**
`snr_ref`'s SNR is a **per-pixel/peak** measure, so it scales inversely with how far a star's flux is spread. A
2 px noise spike concentrates its signal and scores high; a 36 px defocused donut spreads the same flux over
~1,000 px and scores low. `build_goldens` then auto-confirms everything at SNR≥12 **without QA**, assuming high
SNR implies real. On a heavily-defocused run that assumption **inverts** — the gate keeps the noise and discards
the stars:

| run | extreme HFR | auto-confirmed (SNR≥12) | discarded (SNR<12) | corr(bbox, SNR) |
|---|---|---|---|---|
| `lumos` | 10.7 | n=2,268, median **2 px** | n=24,499, median **36 px** | **−0.495** |
| `SorenVance` | 7.5 | n=4,506, median 3 px | n=21,601, median 12 px | −0.082 |
| `FlyData` | 5.0 | n=544, median 6 px | n=1,991, median 28 px | −0.054 |

Severity tracks defocus exactly, which is the confirming signature. `FlyData` is mild enough that the ordering
survives, which is why it produced a good golden.

**The `--donut` matched filter is not at fault** — it *found* the donuts; they sit in the candidate list at 36 px
(13,170 of them ≥30 px on one `lumos` frame). What fails is the SNR tiering applied afterwards.

**The data is good.** Cropping `lumos`'s large low-SNR candidates shows genuine faint diffuse defocused discs at
SNR 15–44. These are legitimate low-SNR, heavily-defocused runs — exactly what a validation bank should contain.
**Do not delete them.**

**Designed 2026-07-30 → `docs/golden-tier-plausibility-design.md`** (plan: `plans/golden-tier-plausibility-plan.md`).

**The integrated-SNR next step proposed here was measured and does not work** — on `lumos@209735` it keeps 2,445
candidates at 88.0% ≤4 px versus peak SNR's 2,390 at 88.1%, i.e. marginally worse, and on `FlyData@889` it takes
≤4 px from 16.7% to 29.0%. A 3 px spike at 12σ peak has integrated significance ≈20; a 36 px donut at 0.25σ/px
has ≈8. Integrated SNR is the *correct* significance ordering and still ranks the spike higher. No
significance-based statistic can fix this — the gate's error is using significance as a proxy for "is a star".

The design instead keeps the `snr >= 12` tier definition (so `recall@SNR≥12` keeps its meaning), adds a
frame-relative **star-plausibility** measure (candidate size ÷ the frame's own star scale) that reorders the QA
worklist, drops auto-confirm entirely on `--donut` runs, and adds an **unresolved** state excluded from both
recall and precision denominators.

Two findings from that work belong here regardless of when it lands:

- **`LinwoodFocus` is also contaminated** (per-frame high/QA tier width ratio **0.71**: high tier median 13 px vs
  QA tier 28 px). Its historical `recall@SNR≥12` measures the wrong star population and cannot be rescued by
  rescoring. `Panos` is inconclusive; `mufti` and `FlyData` are clean.
- **Not hot pixels, and σ is not mis-estimated.** Zero auto-confirmed `lumos` sites recur in ≥11 of 23 frames
  (68.3% appear in exactly one), and block-MAD σ agrees with an adjacent-difference estimate (99.33 vs 103.79) at
  unit z-width. Do not re-investigate either.

`verification_20260730T141918Z`'s rows for `lumos` and `SorenVance` must still be disregarded.

### F17 — The bank rebuild F16 enables is only 2 runs deep
**Status:** Open · **`lumos` and `SorenVance` are still unscored — this is the remaining gap in the bank**

F16's tooling is merged and tested, but the rebuild it exists to enable ran out of LLM budget at **396 of 1,040
montages**. Two of six donut-aware runs are rebuilt; the rest are untouched or restored. Nothing is corrupted —
`build_goldens` never ran for the incomplete runs — but the bank is now of **mixed provenance**, and any report
that mixes these rows must say so.

| run | state | schema | action needed |
|---|---|---|---|
| `LinwoodFocus` | rebuilt, validator clean (widthRatio 0.71 → **3.00**, 99% high-tier coverage) | v2 | regenerate — predates the peak-SNR fix |
| `FlyData` | rebuilt, validator clean (ratio 1.50, coverage recorded) | v2 | regenerate — predates the peak-SNR fix |
| `Panos` | rebuild came out `TIER-INVERSION` (0.75) at 10–25% coverage → **restored from backup**; still `WIDTH-FLAT` | v1 | full rebuild |
| `mufti` | untouched, QA never ran | v1 | full rebuild |
| `lumos` | **quarantined, unscored** | — | full rebuild |
| `SorenVance` | **quarantined, unscored** | — | full rebuild |

**Do not compare a v2 row against a v1 row.** The v2 goldens exclude `unresolved` candidates from both
denominators and record per-tier coverage; the v1 goldens auto-confirmed their high tier without QA. The 11
non-donut runs are all v1 and remain internally comparable, exactly as before.

**To resume.** Salvaged QA for `FlyData` (9/9 frames), `Panos` (7/7) and `SorenVance` (3/6) is preserved at
`_prior_reports/salvaged_qa_20260730/` — **reuse it rather than re-paying for those montages**. Per-run backups of
every overwritten golden are at `_prior_reports/<run>_pre_plausibility_20260730/`. The pipeline is
`golden_prep.py --donut --budget-montages N` → `build_qa_worklist.py` → `qa_workflow.js` → `persist_qa.py` →
`build_goldens.py` → `golden_health.py`, per `.claude/docs/golden-star-set.md`.

**Cost, measured rather than estimated:** montages = `budget-montages × frames`, at ~8.74 montages/min. The five
outstanding runs at 20/frame are ~1,040 montages ≈ **2 hr at one vote**. Coverage at that budget was 78–100% on
`LinwoodFocus` but only ~10% on `lumos` and ~4% on `SorenVance`, which have 7k and 18k candidates per frame.

**Two things to fix before the next attempt, since they change the yield:**

- **`donut_radii` is hardcoded `(6,10,14,18)`**, capping the matched filter at a 36 px box. `mufti` measures star
  scales of **42–52 px**, so its largest donuts exceed what the reference can represent, and on near-focus frames
  r=18 kernels can only return noise. The per-frame star scale that `plausibility.frame_star_scale` already
  computes is the natural input for sizing these.
- **Single-vote QA is not reproducible.** Two independent passes over the same `LinwoodFocus` frames agreed on
  only 62% of confirmed stars (Jaccard 0.6202) while auto-confirm reproduced bit-for-bit. Everything rebuilt so
  far records `qaVotes: 1`. If `recall@SNR≥12` on donut runs is going to be quoted, the high tier wants ≥3 votes.

### F12 — `mccomiskey` is a low-SNR run
**Status:** Open · data-quality note, no action needed

7.0 s subs, Green filter, 0.2885 "/px. Median candidate SNR **11.3** at best focus and **8.1** at the wings; only
47% and 24% of candidates clear SNR 12. Best-focus HFR 3.43 px is inside the detector's calibrated 2–4 px band, so
this is **not** an oversampling problem and binning is not the remedy (2×2 would push HFR to ~1.7 px, below the
band, breaking every pixel-unit knob). Longer exposure is the only lever. Worth knowing when its numbers look
noisy.

### F13 — `SorenVance` was the one bayered run never scored
**Status:** Blocked by **F17** (F16's tooling fix is merged; the rebuild is not done)

Bayered, 6 frames, and had no goldens or linear exports at all, so it produced NaN in every report. Linear exports
now exist and the donut heuristic flags it (frac 12.00, bbox 26 px), but its generated golden proved invalid — see
F16 — so it remains unscored. `lumos` (23 frames, mono, donut-aware) is in the same position. `vsn07` (7 frames,
mono, no donut) succeeded and is now scored, at 100% uncertain coverage.

Post-F16 prep is encouraging for both: `lumos`'s star scale now traces a proper focus curve
(36…36,35,27,17,28,**8**,16,27,36,36) where it was previously flat at 3 px on all 23 frames — the flatness that
first proved its reference was measuring noise. `SorenVance` is the harder case: its candidate pool barely shrank
under `donut_k=8` (19,752/frame vs `lumos`'s 7,000), so it lands at ~4% coverage and may need a larger budget or
the `donut_radii` work in F17 before it yields a usable denominator.

### F14 — `astrodet` is frameless
**Status:** Won't fix

Holds artifacts but zero `Focuser*` frames, so run discovery skips it and no golden can be built. Documented so it
is not repeatedly investigated.

---

## Process

### F15 — `optimize --per-run` overwrites each run's stored settings
**Status:** Open

The prepass writes `optimized_settings.json` back into the run folder as well as `--out`. That destroyed
`bobp_m101`'s historical "row (a)" settings (`sens 50 / clip 9.5`) mid-investigation, leaving only the two knobs
quoted in the write-up, so the baseline had to be reconstructed rather than reproduced.

**Next step.** Consider writing only to `--out` unless a flag opts into updating the run folder, or snapshot the
previous file alongside it.
